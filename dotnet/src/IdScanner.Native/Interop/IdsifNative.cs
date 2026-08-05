using System.Runtime.InteropServices;

namespace IdScanner.Native.Interop;

/// <summary>
/// IDSIF.dll (Creator CRT-603-7005 kart tarayıcı) P/Invoke bağlantıları.
/// 64-bit DLL, cdecl çağrı düzeni. SDK sürümü: crt700x_PCSC_x64_SDK_20260520.
///
/// Java karşılığı: IDSIF.java (JNA arayüzü).
///
/// KURAL: Bu tip <c>internal</c>'dır ve dışarı sızmaz. Üst katmanlar
/// <see cref="IdScanner.Native.DocumentScanner"/> üzerinden konuşur.
///
/// Dizeler <c>LPStr</c> (ANSI) olarak geçiyor çünkü başlık dosyası
/// <c>const char*</c> tanımlıyor.
/// </summary>
internal static class IdsifNative
{
    private const string Dll = "IDSIF";

    /// <summary>
    /// Yükleme sırası önemli — IDSIF.dll bunları adıyla arıyor ve önceden
    /// yüklenmezlerse "modül bulunamadı" ile başarısız oluyor.
    /// </summary>
    internal static readonly string[] Dependencies =
    [
        "vcruntime140", "msvcp140", "concrt140",
        "libusb0_x64", "opencv_world455",
        "zsocr_7200", "crtPace_PCSC_Drv", "CardProcessor",
    ];

    // === Struct'lar (crtIDS.h) ===

    /// <summary>_IDS_IMAGE_PARAM: parlaklık, kontrast, gamma.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ImageParam
    {
        public int Brightness;
        public int Contrast;
        public int Gamma;
    }

    /// <summary>
    /// _IDS_SCAN_CONF: tarama yapılandırması.
    /// Native tarafa <b>değer olarak</b> geçer (JNA'daki Structure.ByValue).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ScanConf
    {
        public int Type;   // _IDS_CARD_TYPE
        public int Mode;   // _IDS_SCAN_MODE
        public int Side;   // _IDS_SCAN_SIDE
        public int Dpi;    // _IDS_SCAN_DPI
        public ImageParam Front;
        public ImageParam Back;
    }

    /// <summary>_IDS_SCAN_RESULT: Scan/ScanMRZ dönüşünde dolar.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    internal struct ScanResultNative
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string FrontFileName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string BackFileName;

        public byte CardDirection;
        public byte CardType;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 2)]
        public byte[] Reserved;
    }

    // === Fonksiyonlar ===

    /// <summary>Cihazı aç. <paramref name="fileSavePath"/>: BMP'lerin kaydedileceği klasör.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int OpenDev([MarshalAs(UnmanagedType.LPStr)] string fileSavePath);

    /// <summary>Cihazı kapat.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Uninit();

    /// <summary>Versiyon: 1=seri no, 2=firmware, 3=SDK, 4=üretici seri.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int GetVersionInfo(int versionType, byte[] versionInfo);

    /// <summary>Kart durumu: 0=yok, 1=hareket, 2=ön, 3=içeride, 4=arka.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int CardStatus(out int status);

    /// <summary>Kartı belirtilen pozisyona taşı.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Move(int position);

    /// <summary>Tarama yap; sonuç struct'ı dolar.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Scan(ScanConf conf, ref ScanResultNative result);

    /// <summary>Tek çağrıda tarama + MRZ tanıma.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int ScanMRZ(ScanConf conf, ref ScanResultNative result,
        byte[] mrzData, ref int mrzLength);

    /// <summary>Var olan görüntüden MRZ tanı.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int RecognizeMRZ(ScanResultNative result, byte[] mrzData,
        ref int mrzLength, out int cardType, byte[] cardTypeName);

    /// <summary>BMP → JPG dönüştür.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int Bmp2Jpeg(int quality,
        [MarshalAs(UnmanagedType.LPStr)] string bmpFile,
        [MarshalAs(UnmanagedType.LPStr)] string jpegFile,
        [MarshalAs(UnmanagedType.Bool)] bool deleteSource);

    /// <summary>BMP → PNG dönüştür.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int Bmp2Png(int quality,
        [MarshalAs(UnmanagedType.LPStr)] string bmpFile,
        [MarshalAs(UnmanagedType.LPStr)] string pngFile,
        [MarshalAs(UnmanagedType.Bool)] bool deleteSource);

    /// <summary>Kart üzerindeki yüz fotoğrafını kırp.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Ansi)]
    internal static extern int GetScanHead(
        [MarshalAs(UnmanagedType.LPStr)] string frontPath,
        [MarshalAs(UnmanagedType.LPStr)] string headPath);

    /// <summary>Sesli uyarıyı aç/kapat.</summary>
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int SetBuzzer(int onOff);
}

/// <summary>Kartın tarayıcı içindeki hedef konumu (_IDS_POS).</summary>
internal static class IdsifPosition
{
    internal const int Insert = 0;      // Kart yerleştirme
    internal const int Scan = 1;        // Tarama pozisyonu
    internal const int Calibration = 2;
    internal const int Reclaim = 3;     // Tutma
    internal const int EjectHalf = 4;   // RF/NFC pozisyonu — BAC için bu
    internal const int Preload = 5;
    internal const int Swallow = 6;
    internal const int Out = 7;         // Dışarı çıkar
    internal const int Cpu = 8;         // Temaslı CPU pozisyonu
}

/// <summary>Taranacak yüz (_IDS_SCAN_SIDE).</summary>
internal static class IdsifScanSide
{
    internal const int Front = 0x01;
    internal const int Back = 0x02;
    internal const int Duplex = 0x03;
}

/// <summary>Tarama çözünürlüğü (_IDS_SCAN_DPI).</summary>
internal static class IdsifScanDpi
{
    internal const int Dpi150 = 150;
    internal const int Dpi200 = 200;
    internal const int Dpi300 = 300;
    internal const int Dpi600 = 600;
}

/// <summary>Renk modu (_IDS_SCAN_MODE).</summary>
internal static class IdsifScanMode
{
    internal const int Color = 0;
    internal const int GrayRed = 1;
    internal const int GrayGreen = 2;
    internal const int GrayBlue = 3;
    internal const int GrayInfrared = 4;
}

/// <summary>Kart tipi (_IDS_CARD_TYPE).</summary>
internal static class IdsifCardType
{
    internal const int Image = 1;
    internal const int IdCn = 2;       // T.C. Kimlik Kartı dahil kimlik kartları
    internal const int PassCn = 3;
    internal const int OnlyId = 4;
    internal const int OnlyAlien = 5;
}
