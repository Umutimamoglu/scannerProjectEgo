namespace IdScanner.Core.Verification;

/// <summary>
/// Çip doğrulamasının toplam sonucu — Passive + Active Authentication.
///
/// Java karşılığı: ChipVerifier.Result.
///
/// <b>Bilgilendirici mod:</b> Bu sonuç hiçbir şeyi reddetmez, yalnızca rapor
/// eder. Akışı kesme kararı çağıran tarafın işidir ve şu an alınmıyor —
/// yani sistem sahte/klon kartı <i>tespit ediyor</i>, <i>reddetmiyor</i>.
/// </summary>
public sealed class VerificationResult
{
    public List<VerificationCheck> Checks { get; } = [];

    /// <summary>Hash karşılaştırma adımı çalıştı mı.</summary>
    public bool HashCheckRun { get; set; }

    /// <summary>Çalıştıysa tüm DG hash'leri tuttu mu.</summary>
    public bool AllHashesMatched { get; set; } = true;

    /// <summary>Yüklü kök sertifika sayısı.</summary>
    public int TrustAnchorCount { get; set; }

    /// <summary>SOD imzası Document Signer sertifikasıyla doğrulandı mı.</summary>
    public bool SodSignatureValid { get; set; }

    /// <summary>DS sertifikası köke kadar zincirlendi mi.</summary>
    public bool ChainValid { get; set; }

    /// <summary>Hangi kök sertifikaya bağlandı (= kart sürümü). Bağlanmadıysa <c>null</c>.</summary>
    public string? MatchedRootCn { get; set; }

    /// <summary>Active Authentication denendi mi (DG15 var mı).</summary>
    public bool AaAttempted { get; set; }

    /// <summary>AA challenge-response doğrulandı mı.</summary>
    public bool AaValid { get; set; }

    /// <summary>İnsan-okur özet satırları — log ve arayüz için.</summary>
    public IReadOnlyList<string> Report()
    {
        var output = new List<string>();

        foreach (var c in Checks)
        {
            var prefix = c.Passed ? "  [PA ✓] " : "  [PA ✗] ";
            var detail = string.IsNullOrWhiteSpace(c.Detail) ? "" : " — " + c.Detail;
            output.Add(prefix + c.Name + detail);
        }

        if (HashCheckRun)
        {
            output.Add(AllHashesMatched
                ? "  [PA] Tüm DG hash'leri SOD ile eşleşti (veri tahrif edilmemiş)."
                : "  [PA] DİKKAT: en az bir DG hash'i tutmadı — veri değişmiş olabilir!");
        }

        if (MatchedRootCn is not null)
        {
            output.Add("  [PA] Kart şu kök sertifikaya bağlı: " + MatchedRootCn);
        }

        if (AaAttempted)
        {
            output.Add(AaValid
                ? "  [AA] Active Authentication GEÇTİ — çip gerçek (klon değil)."
                : "  [AA] Active Authentication BAŞARISIZ — çip klonlanmış olabilir!");
        }

        return output;
    }
}
