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
    /// Kartı tara ve MRZ'yi çıkar.
    ///
    /// MRZ ayrıştırılamazsa yine de başarılı döner — görüntüler elde olur ve
    /// <see cref="ScanResult.Mrz"/> <c>null</c> kalır. Çip okumaya devam
    /// edilip edilmeyeceğine çağıran karar verir.
    /// </summary>
    ScanResult Scan();

    /// <summary>Kartı NFC (çip okuma) pozisyonuna taşı.</summary>
    void MoveToNfc();

    /// <summary>Kartı dışarı çıkar.</summary>
    void Eject();
}
