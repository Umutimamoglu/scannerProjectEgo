namespace IdScanner.Core.Model;

/// <summary>
/// MRZ'den ayrıştırılmış alanlar.
///
/// Java karşılığı: MrzReader.Mrz (iç sınıf).
///
/// Türk kimlik kartı MRZ formatı ICAO 9303 TD1 — 3 satır × 30 karakter:
///   Satır 1: I&lt;TUR + belgeNo(9) + check(1) + opsiyonel(15)
///   Satır 2: dogum(6) + check(1) + cinsiyet(1) + sonKullanma(6) + check(1)
///            + uyruk(3) + opsiyonel(11) + bileşik check(1)
///   Satır 3: SOYAD&lt;&lt;ADLAR
/// </summary>
/// <param name="DocumentNumber">Belge numarası (check digit ile düzeltilmiş).</param>
/// <param name="DateOfBirth">Doğum tarihi, YYMMDD.</param>
/// <param name="DateOfExpiry">Son kullanma tarihi, YYMMDD.</param>
/// <param name="Sex">Cinsiyet: M, F veya &lt;.</param>
/// <param name="Nationality">Uyruk, 3 harf (TUR).</param>
/// <param name="PrimaryName">Soyad.</param>
/// <param name="SecondaryName">Ad(lar).</param>
/// <param name="RawLine1">Ham MRZ satır 1.</param>
/// <param name="RawLine2">Ham MRZ satır 2.</param>
/// <param name="RawLine3">Ham MRZ satır 3.</param>
public sealed record MrzData(
    string DocumentNumber,
    string DateOfBirth,
    string DateOfExpiry,
    string Sex,
    string Nationality,
    string PrimaryName,
    string SecondaryName,
    string RawLine1,
    string RawLine2,
    string RawLine3)
{
    public override string ToString() =>
        $"MRZ{{ docNo={DocumentNumber}, dob={DateOfBirth}, exp={DateOfExpiry}, " +
        $"sex={Sex}, nat={Nationality}, surname={PrimaryName}, given={SecondaryName} }}";
}
