using IdScanner.Core.Mrz;

namespace IdScanner.Core.Model;

/// <summary>
/// Tarama sonucu — DLL'in ürettiği görüntüler ve OCR metni.
///
/// Java karşılığı: IdCardReader.ScanResult ve ScannerBridge.ScanOutput.
/// İkisi neredeyse aynı şeydi; tek tipte birleştirildi.
/// </summary>
public sealed class ScanResult
{
    /// <summary>DLL'in built-in OCR'ından gelen ham MRZ metni.</summary>
    public string MrzText { get; set; } = "";

    /// <summary>Ön yüz taraması (BMP dosya yolu).</summary>
    public string? FrontBmp { get; set; }

    /// <summary>Arka yüz taraması (BMP dosya yolu).</summary>
    public string? BackBmp { get; set; }

    /// <summary>Ayrıştırılmış MRZ — ayrıştırılamadıysa <c>null</c>.</summary>
    public MrzData? Mrz { get; set; }

    /// <summary>Kart yönü ipucu (döndürme gerekiyor mu) — IDSIF <c>ucCardDir</c>.</summary>
    public byte CardDirection { get; set; }
}
