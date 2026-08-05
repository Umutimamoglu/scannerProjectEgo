using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using IdScanner.Core.Diagnostics;
using IdScanner.Core.Verification;
using IdScanner.Workflow.Abstractions;
using Org.BouncyCastle.Cms;
using Org.BouncyCastle.Security;

namespace IdScanner.Crypto;

/// <summary>
/// Passive Authentication — çip verisinin gerçekliğini ICAO 9303 Part 11'e
/// göre doğrular.
///
/// Java karşılığı: ChipVerifier.verifyPassiveAuth ve verifyDocumentSigner.
///
/// Üç kontrol:
///   1. Her DG'nin hash'i SOD'da yazanla aynı mı  → veri tahrif edilmiş mi
///   2. SOD imzası Document Signer sertifikasıyla geçerli mi → imza gerçek mi
///   3. DS sertifikası CSCA köküne kadar zincirleniyor mu → hangi sürüm
///
/// <b>Bilgilendirici mod:</b> hiçbir şeyi reddetmez, sonucu raporlar.
/// </summary>
public sealed partial class PassiveAuthVerifier : IChipVerifier
{
    private readonly X509Certificate2Collection _trustAnchors;
    private readonly IAppLogger _log;

    /// <inheritdoc />
    public int TrustAnchorCount => _trustAnchors.Count;

    /// <param name="certDirectory">Kök (CSCA) sertifikalarının bulunduğu klasör.</param>
    /// <param name="logger">İlerleme mesajları için.</param>
    public PassiveAuthVerifier(string certDirectory, IAppLogger? logger = null)
    {
        _log = (logger ?? NullLogger.Instance).ForComponent("PA");
        _trustAnchors = LoadTrustAnchors(certDirectory);
    }

    // === Kök sertifikalar ===

    /// <summary>certs/ klasöründeki tüm X.509 sertifikalarını kök olarak yükle.</summary>
    private X509Certificate2Collection LoadTrustAnchors(string certDirectory)
    {
        var anchors = new X509Certificate2Collection();

        if (string.IsNullOrWhiteSpace(certDirectory) || !Directory.Exists(certDirectory))
        {
            _log.Warn($"Sertifika klasörü bulunamadı: {certDirectory} — zincir doğrulaması yapılamayacak");
            return anchors;
        }

        string[] extensions = [".cer", ".crt", ".der", ".pem"];
        var files = Directory.EnumerateFiles(certDirectory)
            .Where(f => extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .ToList();

        _log.Debug($"{certDirectory} içinde {files.Count} aday sertifika dosyası bulundu");

        foreach (var file in files)
        {
            try
            {
                var cert = X509CertificateLoader.LoadCertificateFromFile(file);
                anchors.Add(cert);
                _log.Info($"Kök sertifika yüklendi: {CommonName(cert.Subject)} " +
                          $"(geçerlilik {cert.NotBefore:yyyy-MM-dd} → {cert.NotAfter:yyyy-MM-dd})");
            }
            catch (Exception e)
            {
                _log.Warn($"Sertifika yüklenemedi: {Path.GetFileName(file)}", e);
            }
        }

        if (anchors.Count == 0) _log.Warn("Hiç kök sertifika yüklenemedi — zincir kontrolü başarısız olacak");
        return anchors;
    }

    // === Passive Authentication ===

    /// <inheritdoc />
    public VerificationResult VerifyPassiveAuth(byte[] rawSod, IReadOnlyDictionary<int, byte[]> rawDataGroups)
    {
        var result = new VerificationResult { TrustAnchorCount = _trustAnchors.Count };
        using var op = _log.BeginOperation("Passive Authentication");

        if (rawSod is null || rawSod.Length == 0)
        {
            op.Failure("SOD yok");
            result.Checks.Add(new VerificationCheck("SOD okunamadı", false, "doğrulama yapılamıyor"));
            return result;
        }

        _log.Debug($"SOD {rawSod.Length} bayt, baş: {Hex.Preview(rawSod, 12)}");
        _log.Debug($"Doğrulanacak DG'ler: {string.Join(", ", rawDataGroups.Keys.Order().Select(k => "DG" + k))}");

        SodFile sod;
        try
        {
            sod = new SodFile(rawSod, _log);
        }
        catch (SodParseException e)
        {
            _log.Error("SOD çözümlenemedi — ham içeriğin başı aşağıda", e);
            _log.Debug(Environment.NewLine + Hex.Dump(rawSod, 128));
            op.Failed(e);
            result.Checks.Add(new VerificationCheck("SOD çözümlenemedi", false, e.Message));
            return result;
        }

        result.Checks.Add(new VerificationCheck("SOD okundu", true,
            $"özet algoritması: {sod.DigestAlgorithm}, {sod.DataGroupHashes.Count} DG hash'i içeriyor"));

        CompareDataGroupHashes(sod, rawDataGroups, result);
        VerifyDocumentSigner(sod, result);

        op.Success($"hash={(result.AllHashesMatched ? "tamam" : "TUTMADI")}, " +
                   $"imza={(result.SodSignatureValid ? "geçerli" : "geçersiz")}, " +
                   $"zincir={(result.ChainValid ? result.MatchedRootCn : "bağlanamadı")}");
        return result;
    }

    /// <summary>Kontrol 1 — her DG'nin hash'i SOD'da yazanla aynı mı.</summary>
    private void CompareDataGroupHashes(
        SodFile sod, IReadOnlyDictionary<int, byte[]> rawDataGroups, VerificationResult result)
    {
        result.HashCheckRun = true;

        _log.Debug($"SOD şu DG'ler için hash taşıyor: " +
                   string.Join(", ", sod.DataGroupHashes.Keys.Order().Select(k => "DG" + k)));

        foreach (var (dgNumber, rawBytes) in rawDataGroups.OrderBy(p => p.Key))
        {
            if (!sod.DataGroupHashes.TryGetValue(dgNumber, out var expected))
            {
                _log.Warn($"DG{dgNumber} okundu ama SOD'da hash'i yok — doğrulanamıyor");
                result.Checks.Add(new VerificationCheck($"DG{dgNumber}", false, "SOD'da bu DG için hash yok"));
                result.AllHashesMatched = false;
                continue;
            }

            byte[] actual;
            try
            {
                actual = DigestUtilities.CalculateDigest(sod.DigestAlgorithm, rawBytes);
            }
            catch (Exception e)
            {
                _log.Error($"DG{dgNumber} özeti hesaplanamadı ({sod.DigestAlgorithm})", e);
                result.Checks.Add(new VerificationCheck(
                    $"DG{dgNumber} hash", false, $"özet hesaplanamadı ({sod.DigestAlgorithm}): {e.Message}"));
                result.AllHashesMatched = false;
                continue;
            }

            var matched = actual.AsSpan().SequenceEqual(expected);
            if (matched)
            {
                _log.Trace($"DG{dgNumber} hash eşleşti ({rawBytes.Length} bayt → {Hex.Preview(actual, 8)})");
            }
            else
            {
                result.AllHashesMatched = false;
                // Kısaltılmamış hâli bilinçli: hash uyuşmazlığında asıl bilgi tam değerdir.
                _log.Error($"DG{dgNumber} hash TUTMADI ({rawBytes.Length} bayt okundu)" +
                           $"{Environment.NewLine}        beklenen: {Hex.ToHex(expected)}" +
                           $"{Environment.NewLine}        bulunan : {Hex.ToHex(actual)}");
            }

            result.Checks.Add(new VerificationCheck(
                $"DG{dgNumber} hash", matched,
                matched ? "eşleşti" : $"TUTMADI (beklenen {Hex.Preview(expected, 8)}, bulunan {Hex.Preview(actual, 8)})"));
        }
    }

    /// <summary>
    /// Kontrol 2 ve 3 — SOD imzasını doğrula, sonra DS sertifikasını köke zincirle.
    /// </summary>
    private void VerifyDocumentSigner(SodFile sod, VerificationResult result)
    {
        var ds = sod.DocumentSignerCertificate;
        if (ds is null)
        {
            _log.Warn("SOD içinde Document Signer sertifikası yok — imza ve zincir doğrulanamıyor");
            result.Checks.Add(new VerificationCheck("Document Signer sertifikası", false, "SOD'da yok"));
            return;
        }

        var dsNet = X509CertificateLoader.LoadCertificate(ds.GetEncoded());
        _log.Info($"Document Signer: {CommonName(dsNet.Subject)}");
        _log.Debug($"  veren    : {CommonName(dsNet.Issuer)}");
        _log.Debug($"  geçerlilik: {dsNet.NotBefore:yyyy-MM-dd} → {dsNet.NotAfter:yyyy-MM-dd}");
        _log.Debug($"  seri no  : {dsNet.SerialNumber}");

        result.Checks.Add(new VerificationCheck(
            "Document Signer sertifikası bulundu", true, CommonName(dsNet.Subject)));

        VerifySodSignature(sod, result);
        VerifyCertificateChain(dsNet, result);
    }

    /// <summary>Kontrol 2 — CMS imzası (signed attributes dahil) geçerli mi.</summary>
    private void VerifySodSignature(SodFile sod, VerificationResult result)
    {
        var ds = sod.DocumentSignerCertificate!;
        try
        {
            var verified = false;
            var attempts = 0;

            foreach (SignerInformation signer in sod.SignedData.GetSignerInfos().GetSigners())
            {
                attempts++;
                _log.Trace($"İmzalayan {attempts}: özet={signer.DigestAlgorithmID.Algorithm.Id}, " +
                           $"imza={signer.SignatureAlgorithm.Algorithm.Id}");
                verified = signer.Verify(ds.GetPublicKey());
                if (verified) break;
                _log.Warn($"İmzalayan {attempts} doğrulanamadı");
            }

            if (attempts == 0) _log.Warn("CMS içinde hiç imzalayan (SignerInfo) yok");

            result.SodSignatureValid = verified;
            result.Checks.Add(new VerificationCheck("SOD imzası", verified,
                verified ? "geçerli (CMS: signed attributes doğrulandı)" : "GEÇERSİZ"));
        }
        catch (Exception e)
        {
            _log.Error("SOD imzası doğrulanırken hata", e);
            result.Checks.Add(new VerificationCheck("SOD imzası", false, "doğrulanamadı: " + e.Message));
        }
    }

    /// <summary>Kontrol 3 — DS sertifikası yüklü CSCA köklerinden birine bağlanıyor mu.</summary>
    private void VerifyCertificateChain(X509Certificate2 ds, VerificationResult result)
    {
        if (_trustAnchors.Count == 0)
        {
            result.Checks.Add(new VerificationCheck("Sertifika zinciri", false, "yüklü kök sertifika yok"));
            return;
        }

        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.AddRange(_trustAnchors);

        // CRL/OCSP yok — çevrimdışı doğrulama (Java: setRevocationEnabled(false))
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;

        if (chain.Build(ds))
        {
            var root = chain.ChainElements[^1].Certificate;
            result.ChainValid = true;
            result.MatchedRootCn = CommonName(root.Subject);
            _log.Info($"Zincir köke ulaştı: {result.MatchedRootCn} ({chain.ChainElements.Count} halka)");
            result.Checks.Add(new VerificationCheck(
                "Sertifika zinciri", true, "köke ulaştı: " + result.MatchedRootCn));
            return;
        }

        // Başarısızlıkta hangi kökün neden reddettiği kritik bilgi
        _log.Error("Sertifika zinciri hiçbir köke bağlanamadı");
        foreach (var status in chain.ChainStatus)
        {
            _log.Error($"  zincir durumu: {status.Status} — {status.StatusInformation.Trim()}");
        }
        foreach (var anchor in _trustAnchors)
        {
            _log.Debug($"  yüklü kök: {CommonName(anchor.Subject)}");
        }

        var reasons = string.Join(", ", chain.ChainStatus.Select(s => s.StatusInformation.Trim()));
        result.Checks.Add(new VerificationCheck(
            "Sertifika zinciri", false,
            "hiçbir köke bağlanamadı" + (reasons.Length > 0 ? ": " + reasons : "")));
    }

    // === Yardımcılar ===

    /// <summary>Ayırt edici addan (DN) CN kısmını çıkar.</summary>
    private static string CommonName(string distinguishedName)
    {
        var m = CommonNamePattern().Match(distinguishedName);
        return m.Success ? m.Groups[1].Value.Trim() : distinguishedName;
    }

    [GeneratedRegex(@"CN=([^,]*)", RegexOptions.IgnoreCase)]
    private static partial Regex CommonNamePattern();
}
