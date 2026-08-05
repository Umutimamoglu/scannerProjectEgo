using IdScanner.Core.Diagnostics;
using IdScanner.Core.Verification;
using IdScanner.Workflow.Abstractions;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;

namespace IdScanner.Crypto;

/// <summary>
/// Active Authentication doğrulaması — klon çip tespiti.
///
/// Java karşılığı: ChipVerifier.verifyActiveAuth, verifyAaRsa, verifyAaEc.
///
/// Çipe gönderilen challenge, DG15'teki açık anahtara karşılık gelen özel
/// anahtarla imzalanmış olmalı. Özel anahtar çipten çıkarılamadığı için
/// kopyalanmış bir kart geçerli cevap üretemez.
///
/// <b>Önkoşul:</b> DG15'in kendi gerçekliği Passive Authentication ile
/// kanıtlanmış olmalı — aksi halde saldırgan kendi anahtarını koyar ve bu
/// kontrol anlamsızlaşır.
///
/// <b>Hata ayıklama notu:</b> Kartın hangi imza kombinasyonunu kullandığı
/// önceden bilinemediği için birkaç aday sırayla denenir. Java'da bu deneme
/// döngüsü istisnaları sessizce yutuyordu — başarısızlıkta elde hiçbir ipucu
/// kalmıyordu. Burada her deneme ayrı ayrı <see cref="LogLevel.Trace"/>
/// seviyesinde kaydediliyor: hangi özet denendi, neden tutmadı.
/// </summary>
public sealed class ActiveAuthVerifier : IActiveAuthVerifier
{
    private readonly IAppLogger _log;

    public ActiveAuthVerifier(IAppLogger? logger = null)
    {
        _log = (logger ?? NullLogger.Instance).ForComponent("AA");
    }

    /// <summary>
    /// RSA denemeleri: (özet adı, özet üretici, örtük sonek mi).
    ///
    /// ISO/IEC 9796-2 scheme 1 hem açık hem örtük sonek (trailer) ile
    /// kullanılabiliyor. Sıra, sahada en sık görülenden nadire doğru.
    /// </summary>
    private static readonly (string Name, Func<IDigest> Digest, bool ImplicitTrailer)[] RsaCombinations =
    [
        ("SHA-1/örtük", () => new Sha1Digest(), true),
        ("SHA-256", () => new Sha256Digest(), false),
        ("SHA-1", () => new Sha1Digest(), false),
        ("SHA-224", () => new Sha224Digest(), false),
        ("SHA-384", () => new Sha384Digest(), false),
        ("SHA-512", () => new Sha512Digest(), false),
    ];

    /// <summary>EC denemeleri — aynı belirsizlik, özet tarafında.</summary>
    private static readonly (string Name, Func<IDigest> Digest)[] EcDigests =
    [
        ("SHA-256", () => new Sha256Digest()),
        ("SHA-1", () => new Sha1Digest()),
        ("SHA-224", () => new Sha224Digest()),
        ("SHA-384", () => new Sha384Digest()),
        ("SHA-512", () => new Sha512Digest()),
    ];

    /// <inheritdoc />
    public string Verify(byte[] publicKeyDer, byte[] challenge, byte[] response, VerificationResult result)
    {
        result.AaAttempted = true;

        _log.Debug($"challenge={Hex.Preview(challenge)}, cevap={Hex.Preview(response)}");

        using var op = _log.BeginOperation("Active Authentication doğrulama");
        var (ok, detail) = Run(publicKeyDer, challenge, response);
        if (ok) op.Success(detail); else op.Failure(detail);

        result.AaValid = ok;
        result.Checks.Add(new VerificationCheck("Active Authentication", ok, detail));
        return (ok ? "  [AA ✓] " : "  [AA ✗] ") + "Active Authentication — " + detail;
    }

    private (bool Ok, string Detail) Run(byte[] publicKeyDer, byte[] challenge, byte[] response)
    {
        AsymmetricKeyParameter key;
        try
        {
            key = PublicKeyFactory.CreateKey(publicKeyDer);
        }
        catch (Exception e)
        {
            _log.Error($"DG15 anahtarı çözümlenemedi. Ham anahtar: {Hex.Preview(publicKeyDer, 32)}", e);
            return (false, "DG15 anahtarı çözümlenemedi: " + e.Message);
        }

        _log.Info("DG15 anahtarı: " + Describe(key));

        try
        {
            return key switch
            {
                RsaKeyParameters rsa => VerifyRsa(rsa, challenge, response)
                    ? (true, "RSA / ISO9796-2 challenge-response doğrulandı")
                    : (false, "RSA imza tutmadı"),

                ECPublicKeyParameters ec => VerifyEc(ec, challenge, response)
                    ? (true, "ECDSA challenge-response doğrulandı")
                    : (false, "ECDSA imza tutmadı"),

                _ => (false, "bilinmeyen AA anahtar tipi: " + key.GetType().Name),
            };
        }
        catch (Exception e)
        {
            _log.Error("AA doğrulaması beklenmeyen hatayla bitti", e);
            return (false, "hata: " + e.Message);
        }
    }

    /// <summary>Anahtarı log için tarif et — tip ve boyut.</summary>
    private static string Describe(AsymmetricKeyParameter key) => key switch
    {
        RsaKeyParameters rsa => $"RSA {rsa.Modulus.BitLength} bit",
        ECPublicKeyParameters ec => $"EC {ec.Parameters.Curve.FieldSize} bit ({ec.AlgorithmName})",
        _ => key.GetType().Name,
    };

    /// <summary>
    /// RSA AA: ISO/IEC 9796-2 scheme 1, mesaj kurtarmalı.
    /// Yaygın özet/sonek kombinasyonlarını sırayla dener.
    /// </summary>
    private bool VerifyRsa(RsaKeyParameters key, byte[] challenge, byte[] response)
    {
        foreach (var (name, digestFactory, implicitTrailer) in RsaCombinations)
        {
            try
            {
                var signer = new Iso9796d2Signer(new RsaEngine(), digestFactory(), implicitTrailer);
                signer.Init(forSigning: false, key);
                signer.BlockUpdate(challenge, 0, challenge.Length);

                if (signer.VerifySignature(response))
                {
                    _log.Info($"RSA kombinasyonu tuttu: {name}");
                    return true;
                }

                _log.Trace($"RSA {name}: imza eşleşmedi");
            }
            catch (Exception e)
            {
                _log.Trace($"RSA {name}: {e.GetType().Name} — {e.Message}");
            }
        }

        _log.Warn($"Hiçbir RSA kombinasyonu tutmadı ({RsaCombinations.Length} deneme yapıldı)");
        return false;
    }

    /// <summary>
    /// EC AA: ECDSA-Plain — imza, r ve s'in bitişik yazılmış hâli (ASN.1 sarmalı yok).
    ///
    /// Java'da bu iş sağlayıcıya kayıtlı "SHA256withPLAIN-ECDSA" adıyla
    /// yapılıyordu. Burada elle yapılıyor: ad kaydına bağlı kalmamak, hangi
    /// eğri/özet gelirse gelsin çalışmasını sağlıyor.
    /// </summary>
    private bool VerifyEc(ECPublicKeyParameters key, byte[] challenge, byte[] response)
    {
        // r ve s eşit uzunlukta — imza tek sayıda bayt taşıyorsa biçim bozuk
        if (response.Length < 2 || response.Length % 2 != 0)
        {
            _log.Warn($"ECDSA-Plain imzası beklenen biçimde değil: {response.Length} bayt (çift olmalı)");
            return false;
        }

        var half = response.Length / 2;
        var r = new BigInteger(1, response, 0, half);
        var s = new BigInteger(1, response, half, half);
        _log.Trace($"ECDSA-Plain: r ve s {half} bayt olarak ayrıldı");

        foreach (var (name, digestFactory) in EcDigests)
        {
            try
            {
                var digest = digestFactory();
                var hash = new byte[digest.GetDigestSize()];
                digest.BlockUpdate(challenge, 0, challenge.Length);
                digest.DoFinal(hash, 0);

                var signer = new ECDsaSigner();
                signer.Init(forSigning: false, key);

                if (signer.VerifySignature(hash, r, s))
                {
                    _log.Info($"ECDSA özeti tuttu: {name}");
                    return true;
                }

                _log.Trace($"ECDSA {name}: imza eşleşmedi");
            }
            catch (Exception e)
            {
                _log.Trace($"ECDSA {name}: {e.GetType().Name} — {e.Message}");
            }
        }

        _log.Warn($"Hiçbir ECDSA özeti tutmadı ({EcDigests.Length} deneme yapıldı)");
        return false;
    }
}
