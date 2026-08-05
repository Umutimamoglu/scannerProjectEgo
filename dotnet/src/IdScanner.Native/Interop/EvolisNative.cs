using System.Runtime.InteropServices;

namespace IdScanner.Native.Interop;

/// <summary>
/// evolis.dll (Evolis KC Prime kart yazıcısı) P/Invoke bağlantıları.
/// 64-bit DLL, cdecl çağrı düzeni. SDK sürümü: Evolis SDK v3.
///
/// Java karşılığı: EvolisSDK.java (JNA arayüzü).
///
/// <b>KRİTİK — bool hizalaması:</b> C tarafındaki alanlar <c>bool</c> (1 bayt,
/// C99 <c>_Bool</c>). .NET'te <c>bool</c> struct içinde varsayılan olarak
/// <b>4 baytlık</b> Win32 BOOL gibi hizalanır. <see cref="MarshalAs"/> ile
/// <see cref="UnmanagedType.I1"/> verilmezse alanlar kayar ve struct sessizce
/// yanlış okunur — hata vermez, sadece saçma değerler döner. Bu yüzden her
/// bool alanda açıkça belirtildi.
/// </summary>
internal static class EvolisNative
{
    private const string Dll = "evolis";

    // === Numaralandırmalar ===

    /// <summary>Yazıcıya nasıl bağlanılacağı.</summary>
    internal static class OpenMode
    {
        internal const int Auto = 0;
        internal const int Direct = 1;      // Doğrudan iletişim — Premium Suite gerekmez
        internal const int Supervised = 2;  // Evolis Supervision Service üzerinden
    }

    /// <summary>Kartın hangi yüzü.</summary>
    internal static class CardFace
    {
        internal const int Front = 0;
        internal const int Back = 1;
    }

    /// <summary>Genel dönüş/durum kodları.</summary>
    internal static class Ret
    {
        internal const int Ok = 0;
        internal const int EUndefined = -1;
        internal const int EInternal = -2;
        internal const int ECancelled = -3;
        internal const int EDisabled = -4;
        internal const int EUnsupported = -5;
        internal const int EParams = -6;
        internal const int ETimeout = -7;
        internal const int ENeedAction = -8;
        internal const int SessionETimeout = -10;
        internal const int SessionEBusy = -11;
        internal const int SessionDisabled = -12;
    }

    /// <summary>Yazıcının özet durumu (evolis_get_state ana durumu).</summary>
    internal static class State
    {
        internal const int Off = 0;
        internal const int Ready = 1;
        internal const int Warning = 2;
        internal const int Error = 3;
    }

    /// <summary>Baskı ayarı anahtarları (SettingKey numaralandırması).</summary>
    internal static class SettingKey
    {
        internal const int DuplexType = 40;
        internal const int RibbonType = 52;
        internal const int ShortPanelManagement = 55;   // AUTO | CUSTOM | OFF
        internal const int ShortPanelShift = 127;
        internal const int Orientation = 128;           // LANDSCAPE_CC90 | PORTRAIT
    }

    // === Struct'lar ===

    /// <summary>evolis_device_t: evolis_get_devices ile dolar.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct Device
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Id;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string DisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 512)] public string Uri;
        public int Mark;
        public int Model;
        [MarshalAs(UnmanagedType.I1)] public bool IsSupervised;
        [MarshalAs(UnmanagedType.I1)] public bool IsOnline;
        public int Link;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DriverVersion;
    }

    /// <summary>evolis_ribbon_t: takılı ribon bilgisi.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct RibbonInfoNative
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string Description;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 8)] public string Zone;
        public int Type;
        public int Capacity;
        public int Remaining;
        public int Progress;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)] public string ProductCode;
        public int BatchNumber;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 24)] public string BuildAt;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 24)] public string SerialNumber;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 24)] public string InternalCode;
    }

    /// <summary>
    /// evolis_printer_info_t: model, seri no, firmware, donanım özellikleri.
    ///
    /// 13 adet bool alan içeriyor — <c>MarshalAs(I1)</c> olmadan buradan
    /// sonraki her alan kayar. Struct boyutu testi bu yüzden var.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct PrinterInfoNative
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Name;
        public int Type;
        public int Mark;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string MarkName;
        public int Model;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string ModelName;
        public int ModelId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)] public string FwVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)] public string SerialNumber;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)] public string PrintHeadKitNumber;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)] public string Zone;

        [MarshalAs(UnmanagedType.I1)] public bool HasFlip;
        [MarshalAs(UnmanagedType.I1)] public bool HasEthernet;
        [MarshalAs(UnmanagedType.I1)] public bool HasWifi;
        [MarshalAs(UnmanagedType.I1)] public bool HasLaminator;
        [MarshalAs(UnmanagedType.I1)] public bool HasLaminator2;
        [MarshalAs(UnmanagedType.I1)] public bool HasMagEnc;
        [MarshalAs(UnmanagedType.I1)] public bool HasJisMagEnc;
        [MarshalAs(UnmanagedType.I1)] public bool HasSmartEnc;
        [MarshalAs(UnmanagedType.I1)] public bool HasContactLessEnc;
        [MarshalAs(UnmanagedType.I1)] public bool HasLcd;
        [MarshalAs(UnmanagedType.I1)] public bool HasKineclipse;
        [MarshalAs(UnmanagedType.I1)] public bool HasLock;
        [MarshalAs(UnmanagedType.I1)] public bool HasScanner;

        public int InsertionCaps;
        public int EjectionCaps;
        public int RejectionCaps;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)] public string LcdFwVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)] public string LcdGraphVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string ScannerFwVersion;
    }

    /// <summary>evolis_cleaning_t: baskı sayaçları ve temizlik durumu.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct CleaningInfoNative
    {
        public int TotalCardCount;                 // Ömür boyu basılan kart
        public int CardCount;                      // Son temizlikten beri
        public int CardCountBeforeWarning;         // Temizlik uyarısına kalan
        public int CardCountBeforeWarrantyLost;
        public int CardCountAtLastCleaning;
        public int RegularCleaningCount;
        public int AdvancedCleaningCount;
        [MarshalAs(UnmanagedType.I1)] public bool PrintHeadUnderWarranty;
        public int WarningThreshold;
        public int WarrantyLostThreshold;
    }

    /// <summary>evolis_status_t: yazıcı durum bayrakları.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Status
    {
        public int Config;
        public int Information;
        public int Warning;
        public int Error;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)] public int[] Extensions;
        public short Session;
    }

    // === Fonksiyonlar ===

    /// <summary>Sistemdeki Evolis cihazlarını listele; dönüş = cihaz sayısı.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int evolis_get_devices(out IntPtr devices, int flags);

    /// <summary>evolis_get_devices'in ayırdığı belleği serbest bırak.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void evolis_free_devices(IntPtr devices);

    /// <summary>Yazıcıya belirli bir modla bağlan; <c>IntPtr.Zero</c> = başarısız.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern IntPtr evolis_open_with_mode(
        [MarshalAs(UnmanagedType.LPStr)] string name, int mode);

    /// <summary>Bağlantıyı kapat.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void evolis_close(IntPtr context);

    /// <summary>Yazıcı durumunu oku.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int evolis_status(IntPtr context, ref Status status);

    /// <summary>Belirli bir durum bayrağı açık mı (0-255 arası kimlik).</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool evolis_status_is_on(ref Status status, int flagId);

    /// <summary>Takılı ribon bilgisini oku.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int evolis_get_ribbon(IntPtr context, ref RibbonInfoNative ribbon);

    /// <summary>Yazıcı künyesi.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int evolis_get_info(IntPtr context, ref PrinterInfoNative info);

    /// <summary>Baskı sayaçları ve temizlik durumu.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int evolis_get_cleaning(IntPtr context, ref CleaningInfoNative cleaning);

    /// <summary>Aktif besleyici (Feeder A/B/C/D).</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int evolis_get_feeder(IntPtr context, out int feeder);

    /// <summary>Yazıcının özet durumu.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int evolis_get_state(IntPtr context, out int major, out int minor);

    /// <summary>
    /// Mekanik hatayı temizle ve yazıcıyı hazır duruma döndür.
    ///
    /// Baskı sırasında kart/ribon sıkışırsa yazıcı ERR_MECHANICAL bayrağını
    /// set eder ve başka hiçbir işi kabul etmez; bu çağrı onu sıfırlar.
    /// </summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int evolis_clear_mechanical_errors(IntPtr context);

    /// <summary>Baskı oturumunu başlat.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int evolis_print_init(IntPtr context);

    /// <summary>Metin tipi baskı ayarı ver.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    [return: MarshalAs(UnmanagedType.I1)]
    internal static extern bool evolis_print_set_setting(
        IntPtr context, int key, [MarshalAs(UnmanagedType.LPStr)] string value);

    /// <summary>Basılacak görseli dosya yolundan ver.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int evolis_print_set_imagep(
        IntPtr context, int face, [MarshalAs(UnmanagedType.LPStr)] string path);

    /// <summary>Baskıyı çalıştır; <paramref name="timeoutMs"/> milisaniye.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int evolis_print_exect(IntPtr context, int timeoutMs);

    /// <summary>PRN dosyası üret ama BASMA — kart harcamadan baskı hattını sınamak için.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int evolis_print_to_file(
        IntPtr context, [MarshalAs(UnmanagedType.LPStr)] string path);

    /// <summary>Sık karşılaşılan dönüş kodlarının okunabilir karşılığı.</summary>
    internal static string Describe(int code) => code switch
    {
        0 => "OK",
        -1 => "EUNDEFINED",
        -2 => "EINTERNAL",
        -3 => "ECANCELLED",
        -4 => "EDISABLED",
        -5 => "EUNSUPPORTED",
        -6 => "EPARAMS",
        -7 => "ETIMEOUT",
        -8 => "ENEEDACTION",
        -10 => "SESSION_ETIMEOUT",
        -11 => "SESSION_EBUSY",
        -12 => "SESSION_DISABLED",
        -21 => "PRINT_NEEDACTION (yazıcı hazır değil — ribon/kapak/hazne)",
        -22 => "PRINT_EMECHANICAL (kart veya ribon sıkışması)",
        -23 => "PRINT_WAITCARDINSERT",
        _ => $"kod {code}",
    };
}
