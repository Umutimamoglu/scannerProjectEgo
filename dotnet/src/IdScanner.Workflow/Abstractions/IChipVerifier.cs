using IdScanner.Core.Verification;

namespace IdScanner.Workflow.Abstractions;

/// <summary>
/// Passive Authentication — çipten okunan verinin tahrif edilmediğini ve
/// devletin imzasını taşıdığını kanıtlar.
///
/// <b>Çevrimdışıdır:</b> ham byte'lar ve kök sertifikalar yeter, kart takılı
/// olmasa da çalışır. Bu yüzden Active Authentication ile aynı arayüzün
/// arkasına konmaz — AA canlı kart gerektirir, bkz. <see cref="IActiveAuthVerifier"/>.
///
/// Java karşılığı: ChipVerifier.verifyPassiveAuth.
/// </summary>
public interface IChipVerifier
{
    /// <summary>Yüklü kök sertifika sayısı.</summary>
    int TrustAnchorCount { get; }

    /// <param name="rawSod">Çipten okunan EF.SOD'un ham byte'ları (0x77 sarmalı dahil).</param>
    /// <param name="rawDataGroups">DG numarası → çipten okunan ham byte (parse edilmemiş).</param>
    VerificationResult VerifyPassiveAuth(byte[] rawSod, IReadOnlyDictionary<int, byte[]> rawDataGroups);
}

/// <summary>
/// Active Authentication — çipin klon olmadığını kanıtlar.
///
/// <b>Canlı kart gerektirir:</b> çipe rastgele challenge gönderilir, çip onu
/// yalnızca kendisinde bulunan özel anahtarla imzalar. Bu yüzden çağrı
/// zinciri okuyucudan geçer; burada yalnızca imza doğrulaması yapılır.
///
/// Java karşılığı: ChipVerifier.verifyActiveAuth.
/// </summary>
public interface IActiveAuthVerifier
{
    /// <summary>
    /// Challenge-response imzasını DG15'teki açık anahtara karşı doğrula.
    /// </summary>
    /// <param name="publicKeyDer">DG15'ten çıkan SubjectPublicKeyInfo (DER).</param>
    /// <param name="challenge">Çipe gönderilen rastgele veri.</param>
    /// <param name="response">Çipin döndürdüğü imza.</param>
    /// <param name="result">Sonucun ekleneceği doğrulama kaydı.</param>
    /// <returns>Loglanacak tek satırlık özet.</returns>
    string Verify(byte[] publicKeyDer, byte[] challenge, byte[] response, VerificationResult result);
}
