using IdScanner.Core.Model;

namespace IdScanner.Workflow.Abstractions;

/// <summary>
/// Kart yazıcısı (Evolis KC Prime).
///
/// Java karşılığı: EvolisPrinter.java (JNA ile evolis.dll).
/// Uygulaması Native katmanında.
/// </summary>
public interface ICardPrinter : IDisposable
{
    bool IsConnected { get; }

    /// <summary>Bağlı yazıcının adı; bağlı değilse <c>null</c>.</summary>
    string? PrinterName { get; }

    /// <summary>Sistemdeki Evolis yazıcılarını listele.</summary>
    IReadOnlyList<string> ListPrinters();

    /// <summary>İlk bulunan yazıcıya bağlan.</summary>
    bool Connect();

    /// <summary>Adı verilen yazıcıya bağlan.</summary>
    bool Connect(string name);

    void Disconnect();

    /// <summary>Durum ve açık bayraklar.</summary>
    PrinterState ReadState();

    /// <summary>Künye — model, seri no, firmware. Okunamazsa <c>null</c>.</summary>
    PrinterInfo? ReadInfo();

    /// <summary>Takılı ribon. Okunamazsa <c>null</c>.</summary>
    RibbonInfo? ReadRibbon();

    /// <summary>Baskı sayaçları ve temizlik durumu. Okunamazsa <c>null</c>.</summary>
    CleaningInfo? ReadCleaning();

    /// <summary>Aktif besleyici (A/B/C/D); okunamazsa -1.</summary>
    int ReadFeeder();

    /// <summary>
    /// Mekanik hatayı temizle.
    ///
    /// Baskı sırasında kart/ribon sıkışırsa yazıcı ERR_MECHANICAL bayrağını
    /// set eder ve <b>başka hiçbir işi kabul etmez</b>; bu çağrı onu sıfırlar.
    /// </summary>
    bool ClearMechanicalErrors();

    /// <summary>Tek yüz bas.</summary>
    /// <param name="dryRun"><c>true</c> ise PRN dosyası üretir, kart harcamaz.</param>
    PrintResult Print(string imagePath, bool dryRun);

    /// <summary>Çift yüz bas.</summary>
    /// <param name="dryRun"><c>true</c> ise PRN dosyası üretir, kart harcamaz.</param>
    PrintResult PrintDuplex(string frontImagePath, string backImagePath, bool dryRun);
}
