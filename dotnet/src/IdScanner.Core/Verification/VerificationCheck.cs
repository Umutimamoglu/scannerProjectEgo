namespace IdScanner.Core.Verification;

/// <summary>
/// Tek bir doğrulama kontrolünün sonucu (bir DG hash'i, imza, zincir gibi).
///
/// Java karşılığı: ChipVerifier.Check.
/// </summary>
/// <param name="Name">Kontrolün adı — "DG1 hash", "SOD imzası" gibi.</param>
/// <param name="Passed">Geçti mi.</param>
/// <param name="Detail">İnsan-okur açıklama; boş olabilir.</param>
public sealed record VerificationCheck(string Name, bool Passed, string Detail = "");
