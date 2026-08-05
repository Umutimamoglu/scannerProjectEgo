using IdScanner.Core.Model;

namespace IdScanner.Workflow.Abstractions;

/// <summary>
/// Kart tarayıcı — kartı içeri alır, iki yüzünü tarar, MRZ'yi OCR'lar ve
/// kartı çip okuma (NFC) pozisyonuna taşır.
///
/// Java karşılığı: IDSIF.dll'i saran IdCardReader'ın tarayıcı kısmı ve
/// ScannerBridge. Uygulaması Native katmanında (IDSIF.dll, P/Invoke).
/// </summary>
public interface IDocumentScanner : IDisposable
{
    /// <summary>Cihaz açık mı.</summary>
    bool IsOpen { get; }

    /// <summary>DLL'i yükle ve cihazı aç. Başarılıysa <c>true</c>.</summary>
    bool Open();

    /// <summary>Cihazdaki kartın konumu.</summary>
    CardStatus GetCardStatus();

    /// <summary>
    /// Kartı tara ve MRZ'yi çıkar, ardından çip okuma pozisyonuna taşı.
    /// </summary>
    ScanResult Scan();

    /// <summary>Kartı dışarı çıkar.</summary>
    void Eject();
}
