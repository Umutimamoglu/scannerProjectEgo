using IdScanner.Core.Diagnostics;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Cms;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace IdScanner.Crypto;

/// <summary>EF.SOD çözümlenemediğinde fırlatılır.</summary>
public sealed class SodParseException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// EF.SOD (Document Security Object) çözümleyici.
///
/// Java'da bu iş JMRTD'nin <c>SODFile</c> sınıfı tarafından yapılıyordu;
/// .NET'te karşılığı olmadığı için elle yazıldı.
///
/// Yapı (ICAO 9303 Part 11):
/// <code>
///   EF.SOD = [APPLICATION 23] (0x77) sarmalı
///              └─ ContentInfo (RFC 5652 CMS SignedData)
///                   ├─ eContent = LDSSecurityObject
///                   │    ├─ version           INTEGER
///                   │    ├─ hashAlgorithm     AlgorithmIdentifier
///                   │    └─ dataGroupHashes   SEQUENCE OF { dgNumber, hashValue }
///                   ├─ certificates = Document Signer sertifikası
///                   └─ signerInfos  = imza
/// </code>
/// </summary>
public sealed class SodFile
{
    /// <summary>EF.SOD dış sarmalının etiketi — ASN.1 APPLICATION 23.</summary>
    private const byte ApplicationTag23 = 0x77;

    private readonly CmsSignedData _signedData;

    /// <summary>Özet algoritmasının adı — örn. "SHA-256".</summary>
    public string DigestAlgorithm { get; }

    /// <summary>DG numarası → SOD'da yazan beklenen hash.</summary>
    public IReadOnlyDictionary<int, byte[]> DataGroupHashes { get; }

    /// <summary>Document Signer sertifikası; SOD'da yoksa <c>null</c>.</summary>
    public X509Certificate? DocumentSignerCertificate { get; }

    /// <summary>CMS katmanı — imza doğrulaması için.</summary>
    public CmsSignedData SignedData => _signedData;

    /// <param name="rawSod">Çipten okunan ham EF.SOD (0x77 sarmalı dahil olabilir).</param>
    /// <param name="logger">Her çözümleme adımını raporlamak için.</param>
    /// <exception cref="SodParseException">Yapı beklenen biçimde değilse.</exception>
    public SodFile(byte[] rawSod, IAppLogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(rawSod);
        var log = (logger ?? NullLogger.Instance).ForComponent("Sod");

        var cmsBytes = StripApplicationTag(rawSod);
        log.Trace(cmsBytes.Length == rawSod.Length
            ? "0x77 sarmalı yok, girdi doğrudan ContentInfo sayıldı"
            : $"0x77 sarmalı soyuldu: {rawSod.Length} → {cmsBytes.Length} bayt");

        try
        {
            _signedData = new CmsSignedData(cmsBytes);
        }
        catch (Exception e)
        {
            throw new SodParseException("CMS SignedData çözümlenemedi: " + e.Message, e);
        }

        var eContent = ExtractSignedContent(_signedData);
        log.Trace($"İmzalanmış içerik (LDSSecurityObject): {eContent.Length} bayt");

        var (digestAlg, hashes) = ParseLdsSecurityObject(eContent);
        log.Debug($"LDSSecurityObject çözüldü: özet={digestAlg}, {hashes.Count} DG hash'i");

        DigestAlgorithm = digestAlg;
        DataGroupHashes = hashes;
        DocumentSignerCertificate = FindDocumentSigner(_signedData, log);
    }

    // === Çözümleme adımları ===

    /// <summary>
    /// Dış APPLICATION 23 sarmalını soyup içindeki ContentInfo DER'ini döndür.
    /// Sarmal yoksa girdiyi aynen verir.
    ///
    /// Java karşılığı: ChipVerifier.stripApplicationTag.
    /// </summary>
    internal static byte[] StripApplicationTag(byte[] ef)
    {
        if (ef.Length < 2 || ef[0] != ApplicationTag23) return ef;

        var idx = 1;
        int first = ef[idx++];
        int length;

        if (first < 0x80)
        {
            // Kısa biçim — uzunluk tek byte
            length = first;
        }
        else
        {
            // Uzun biçim — düşük 7 bit, uzunluğun kaç byte olduğunu söyler
            var byteCount = first & 0x7F;
            if (byteCount == 0 || idx + byteCount > ef.Length) return ef; // tanımsız uzunluk vb.
            length = 0;
            for (var i = 0; i < byteCount; i++) length = (length << 8) | ef[idx++];
        }

        return idx + length > ef.Length ? ef : ef[idx..(idx + length)];
    }

    /// <summary>CMS'in imzalanmış içeriğini (LDSSecurityObject DER'i) çıkar.</summary>
    private static byte[] ExtractSignedContent(CmsSignedData signedData)
    {
        var content = signedData.SignedContent
            ?? throw new SodParseException("CMS içeriği boş (detached imza?)");

        using var ms = new MemoryStream();
        content.Write(ms);
        return ms.ToArray();
    }

    /// <summary>
    /// LDSSecurityObject'i ayrıştır: özet algoritması ve DG hash tablosu.
    /// </summary>
    private static (string DigestAlgorithm, Dictionary<int, byte[]> Hashes) ParseLdsSecurityObject(byte[] der)
    {
        Asn1Sequence root;
        try
        {
            root = Asn1Sequence.GetInstance(der);
        }
        catch (Exception e)
        {
            throw new SodParseException("LDSSecurityObject bir SEQUENCE değil: " + e.Message, e);
        }

        // version(0), hashAlgorithm(1), dataGroupHashValues(2), [ldsVersionInfo(3) opsiyonel]
        if (root.Count < 3)
        {
            throw new SodParseException($"LDSSecurityObject beklenenden kısa ({root.Count} eleman)");
        }

        var algId = AlgorithmIdentifier.GetInstance(root[1]);
        var digestName = ResolveDigestName(algId.Algorithm.Id);

        var hashes = new Dictionary<int, byte[]>();
        var hashSeq = Asn1Sequence.GetInstance(root[2]);
        foreach (var entry in hashSeq)
        {
            var pair = Asn1Sequence.GetInstance(entry);
            if (pair.Count < 2) continue;
            var dgNumber = DerInteger.GetInstance(pair[0]).IntValueExact;
            var hashValue = Asn1OctetString.GetInstance(pair[1]).GetOctets();
            hashes[dgNumber] = hashValue;
        }

        return (digestName, hashes);
    }

    /// <summary>OID'den okunabilir özet adı — çözülemezse OID'in kendisi.</summary>
    private static string ResolveDigestName(string oid)
    {
        try
        {
            return DigestUtilities.GetAlgorithmName(new DerObjectIdentifier(oid));
        }
        catch (Exception)
        {
            return oid;
        }
    }

    /// <summary>
    /// İmzalayanın sertifikasını bul — SignerInfo'daki tanımlayıcıya (SID) göre
    /// eşleştirir; eşleşme yoksa CMS'teki ilk sertifikaya düşer.
    /// </summary>
    private static X509Certificate? FindDocumentSigner(CmsSignedData signedData, IAppLogger log)
    {
        var store = signedData.GetCertificates();

        foreach (SignerInformation signer in signedData.GetSignerInfos().GetSigners())
        {
            foreach (var cert in store.EnumerateMatches(signer.SignerID))
            {
                log.Trace("Document Signer, imzalayan tanımlayıcısıyla (SID) eşleşti");
                return cert;
            }
        }

        // Eşleşme yoksa ilk sertifikaya düşülür — SID'in eksik ya da farklı
        // biçimde yazıldığı kartlarda olur. Hangi yolun kullanıldığı log'da
        // görünsün ki imza doğrulaması patlarsa sebebi anlaşılsın.
        foreach (var cert in store.EnumerateMatches(null))
        {
            log.Warn("İmzalayan tanımlayıcısı eşleşmedi, CMS'teki ilk sertifika kullanılıyor");
            return cert;
        }

        log.Warn("CMS içinde hiç sertifika yok");
        return null;
    }
}
