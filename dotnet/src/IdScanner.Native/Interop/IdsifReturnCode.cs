namespace IdScanner.Native.Interop;

/// <summary>
/// IDSIF.dll dönüş kodları (crtIDS.h içindeki _IDS_RET numaralandırması).
///
/// Java karşılığı: IDSIF.Ret arayüzü ve <c>name(int)</c> metodu.
///
/// <b>Neden bu kadar ayrıntılı:</b> Cihaz hatalarında elimizde yalnızca bir
/// sayı oluyor. "-13" ile "kart yok" arasındaki farkı log'da görebilmek,
/// sahada geçen süreyi doğrudan etkiliyor.
/// </summary>
internal static class IdsifReturnCode
{
    internal const int OkBack = 1;   // Başarılı — MRZ arka yüzden okundu
    internal const int Ok = 0;

    internal const int ErrNoOpen = -1;
    internal const int ErrAlreadyOpen = -2;
    internal const int ErrNoDevice = -3;
    internal const int ErrOpen = -4;
    internal const int ErrScanCardInGate = -12;
    internal const int ErrMrzOcr = -18;
    internal const int ErrNoCard = -20;
    internal const int ErrMrzDgKey = -23;
    internal const int ErrJam = -106;
    internal const int ErrRf = -116;

    /// <summary>Başarı sayılan kodlar — 0 ve "arka yüzden okundu".</summary>
    internal static bool IsSuccess(int code) => code is Ok or OkBack;

    /// <summary>Kodun okunabilir açıklaması.</summary>
    internal static string Describe(int code) => code switch
    {
        OkBack => "OK (MRZ arka yüzden okundu)",
        Ok => "OK",

        -1 => "ERR_NOOPEN (cihaz açılmamış)",
        -2 => "ERR_ALREADYOPEN (cihaz zaten açık)",
        -3 => "ERR_NODEVICE (cihaz bulunamadı — USB bağlantısı ve libusb-win32 sürücüsünü kontrol edin)",
        -4 => "ERR_OPEN (cihaz açılamadı)",
        -5 => "ERR_COMMAND (komut hatası)",
        -6 => "ERR_RECVTIMEOUT (veri alma zaman aşımı)",
        -7 => "ERR_RECVFAILD (veri alınamadı)",
        -8 => "ERR_SENDFAILD (komut gönderilemedi)",
        -9 => "COMM_ERR (iletişim hatası)",
        -10 => "ERR_RECVSCAN_ERROR (tarama verisi alınamadı)",
        -11 => "ERR_SCAN_GETCARD (kart durumu alınamadı)",
        -12 => "ERR_SCAN_CARD_IN_GATE (kart girişte — taramayı tekrarlayın)",
        -13 => "ERR_SCAN_NOCARD (tarama sırasında kart yok)",
        -14 => "ERR_IMG_INVALID (görüntü dosyası geçersiz)",
        -15 => "ERR_SAVEJPG (JPG kaydedilemedi)",
        -16 => "ERR_SAVEPNG (PNG kaydedilemedi)",
        -17 => "ERR_IMGNAME (görüntü adı boş)",
        -18 => "ERR_MRZOCR (MRZ OCR başarısız)",
        -19 => "PARAM (geçersiz parametre)",
        -20 => "ERR_NOCARD (kart yok)",
        -21 => "ERR_4E (cihaz 4E yanıtı döndü)",
        -22 => "ERR_MRZ (MRZ hatası)",
        -23 => "ERR_MRZ_DGKEY (MRZ'den BAC anahtarı türetilemedi)",
        -24 => "ERR_SAVEBMP (BMP kaydedilemedi)",
        -25 => "ERR_READ (okuma başarısız)",
        -26 => "ERR_WLT (fotoğraf çözme başarısız)",
        -27 => "ERR_MEDIA (medya hatası)",
        -28 => "ERR_FINGER (parmak izi okunamadı)",
        -96 => "HEADER_ERR (paket başlığı hatalı)",
        -97 => "PACKAGE_ERR (paket uzunluğu hatalı)",
        -98 => "ERR (genel hata)",
        -99 => "ERR_UNAUTHORIZED (yetkisiz)",
        -100 => "ERR_MUTEX (kilit alınamadı)",
        -101 => "ERR_GET_PIC_DATA (görüntü verisi alınamadı)",
        -102 => "ERR_FILE_NOT_EXIST (dosya yok)",
        -103 => "ERR_RECOGNIZE_QR_FAILED (QR okunamadı)",
        -104 => "ERR_NO_QR (QR bulunamadı)",
        -105 => "ERR_SAVEPIC (görüntü kaydedilemedi)",
        -106 => "ERR_JAM (kart sıkıştı)",
        -110 => "ERR_UNSUPPORTED (desteklenmeyen işlev)",
        -111 => "ERR_SIDE (desteklenmeyen tarama yüzü)",
        -112 => "ERR_MODE (desteklenmeyen renk modu)",
        -113 => "ERR_TYPE (desteklenmeyen kart tipi)",
        -114 => "ERR_DPI (desteklenmeyen çözünürlük)",
        -115 => "ERR_DELAY (gecikme aralık dışında)",
        -116 => "ERR_RF (RF/NFC okuma başarısız)",
        -150 => "ERR_CPUEMV (çip ATR bilgisi EMV formatında değil)",
        -160 => "ERR_CISDATA (tarayıcı sensör verisi okunamadı)",
        -161 => "ERR_SET_CIS_PARAM (tarayıcı parametresi ayarlanamadı)",
        -162 => "ERR_LOAD_DLL (DLL yüklenemedi)",

        _ => $"bilinmeyen kod {code}",
    };
}
