namespace IdScanner.Core.Model;

/// <summary>
/// Yazıcının özet durumu.
///
/// Java karşılığı: EvolisSDK.State sabitleri.
/// </summary>
public enum PrinterStateKind
{
    Off = 0,
    Ready = 1,
    Warning = 2,
    Error = 3,
}

/// <summary>
/// Yazıcı durumu ve açık bayraklar.
///
/// Java karşılığı: EvolisPrinter.State.
/// </summary>
public sealed class PrinterState
{
    public PrinterStateKind Kind { get; set; } = PrinterStateKind.Off;

    /// <summary>SDK'nın verdiği detaylı sebep kodu.</summary>
    public int Minor { get; set; }

    public bool HasError { get; set; }
    public bool HasWarning { get; set; }

    /// <summary>Açık olan durum bayraklarının adları (RSV_/CFG_ hariç).</summary>
    public List<string> Flags { get; } = [];

    /// <summary>Arayüzde gösterilecek Türkçe etiket.</summary>
    public string Label => Kind switch
    {
        PrinterStateKind.Ready => "Hazır",
        PrinterStateKind.Warning => "Uyarı",
        PrinterStateKind.Error => "Hata",
        _ => "Kapalı / bağlı değil",
    };
}

/// <summary>
/// Yazıcı künyesi — model, seri no, firmware, donanım özellikleri.
///
/// Java karşılığı: EvolisSDK.PrinterInfo (JNA struct). Burada native struct
/// değil, temiz bir kayıt: üst katmanlar Marshal/IntPtr görmez.
/// </summary>
public sealed record PrinterInfo
{
    public string Name { get; init; } = "";
    public string MarkName { get; init; } = "";
    public string ModelName { get; init; } = "";
    public string FirmwareVersion { get; init; } = "";
    public string SerialNumber { get; init; } = "";
    public string PrintHeadKitNumber { get; init; } = "";
    public string Zone { get; init; } = "";

    public bool HasFlip { get; init; }
    public bool HasEthernet { get; init; }
    public bool HasWifi { get; init; }
    public bool HasLaminator { get; init; }
    public bool HasMagneticEncoder { get; init; }
    public bool HasSmartEncoder { get; init; }
    public bool HasContactlessEncoder { get; init; }
    public bool HasLcd { get; init; }
    public bool HasLock { get; init; }
    public bool HasScanner { get; init; }
}

/// <summary>
/// Takılı ribon bilgisi.
///
/// Java karşılığı: EvolisSDK.RibbonInfo.
/// </summary>
public sealed record RibbonInfo
{
    public string Description { get; init; } = "";
    public string ProductCode { get; init; } = "";
    public string Zone { get; init; } = "";
    public int Type { get; init; }

    /// <summary>Ribonun toplam baskı kapasitesi.</summary>
    public int Capacity { get; init; }

    /// <summary>Kalan baskı sayısı.</summary>
    public int Remaining { get; init; }

    public int Progress { get; init; }
    public string SerialNumber { get; init; } = "";
}

/// <summary>
/// Baskı sayaçları ve temizlik durumu.
///
/// Java karşılığı: EvolisSDK.CleaningInfo.
/// </summary>
public sealed record CleaningInfo
{
    /// <summary>Ömür boyu basılan kart sayısı.</summary>
    public int TotalCardCount { get; init; }

    /// <summary>Son temizlikten beri basılan kart.</summary>
    public int CardCount { get; init; }

    /// <summary>Temizlik uyarısına kalan kart.</summary>
    public int CardCountBeforeWarning { get; init; }

    public int CardCountBeforeWarrantyLost { get; init; }
    public int CardCountAtLastCleaning { get; init; }
    public int RegularCleaningCount { get; init; }
    public int AdvancedCleaningCount { get; init; }
    public bool PrintHeadUnderWarranty { get; init; }
    public int WarningThreshold { get; init; }
    public int WarrantyLostThreshold { get; init; }
}

/// <summary>
/// Baskı sonucu.
///
/// Java karşılığı: EvolisPrinter.PrintResult.
/// </summary>
/// <param name="Ok">Baskı başarılı mı.</param>
/// <param name="Code">SDK dönüş kodu (0 = OK).</param>
/// <param name="Message">İnsan-okur açıklama.</param>
public sealed record PrintResult(bool Ok, int Code, string Message)
{
    public static PrintResult Fail(string message) => new(false, 0, message);
    public static PrintResult Success(string message) => new(true, 0, message);
}
