package com.mobiloby;

import com.sun.jna.Library;
import com.sun.jna.Native;
import com.sun.jna.Pointer;
import com.sun.jna.Structure;
import com.sun.jna.ptr.IntByReference;

import java.io.File;
import java.util.Arrays;
import java.util.List;

/**
 * IDSIF.dll (Creator CRT-603-7005) JNA binding — crtIDS.h'ye göre.
 * 64-bit DLL, cdecl convention.
 *
 * SDK sürümü: crt700x_PCSC_x64_SDK_20260520
 */
public interface IDSIF extends Library {

    String NATIVE_DIR = new File("native_x64").getAbsolutePath();

    /** DLL'leri doğru sırada yükle, sonra Native.load ile IDSIF'i bağla. */
    static IDSIF load() {
        // Önce bağımlılıkları sıralı yükle (msvcr, libusb, opencv, vb.)
        String[] depsOrder = {
                "vcruntime140", "msvcp140", "concrt140",
                "libusb0_x64", "opencv_world455",
                "zsocr_7200", "crtPace_PCSC_Drv", "CardProcessor"
        };
        for (String dep : depsOrder) {
            File dll = new File(NATIVE_DIR, dep + ".dll");
            if (dll.exists()) {
                try {
                    System.load(dll.getAbsolutePath());
                    System.out.println("  yüklendi: " + dep);
                } catch (UnsatisfiedLinkError e) {
                    System.err.println("  yüklenemedi (devam): " + dep + " → " + e.getMessage());
                }
            }
        }
        System.setProperty("jna.library.path", NATIVE_DIR);
        return Native.load("IDSIF", IDSIF.class);
    }

    // === Enum sabitleri (header'dan) ===

    interface Pos {
        int INSERT = 0;       // Kart yerleştirme
        int SCAN = 1;         // Tarama pozisyonu
        int CALI = 2;         // Kalibrasyon
        int RECLAIM = 3;      // Tutma
        int EJECT_HALF = 4;   // RF/NFC pozisyonu ← BAC için bu
        int PRELOAD = 5;
        int SWALLOW = 6;
        int OUT = 7;          // Dışarı çıkar
        int CPU = 8;          // Temaslı CPU pozisyonu
    }

    interface ScanSide {
        int FRONT = 0x01;
        int BACK = 0x02;
        int DUPLEX = 0x03;
    }

    interface ScanDpi {
        int D150 = 150, D200 = 200, D300 = 300, D600 = 600;
    }

    interface ScanMode {
        int COLOR = 0, GRAY_R = 1, GRAY_G = 2, GRAY_B = 3, GRAY_IR = 4;
    }

    interface CardType {
        int IMAGE = 1, ID_CN = 2, PASS_CN = 3, ONLY_ID = 4, ONLY_ALIEN = 5;
    }

    interface Ret {
        int OK = 0, OK_BACK = 1;
        int ERR_NOOPEN = -1, ERR_ALREADYOPEN = -2, ERR_NODEVICE = -3, ERR_OPEN = -4;
        int ERR_MRZOCR = -18, ERR_MRZ = -22, ERR_NOCARD = -20;
        int ERR_RF = -116, ERR_UNAUTHORIZED = -99;
    }

    // === Struct'lar (header'dan) ===

    /** _IDS_IMAGE_PARAM: parlaklık, kontrast, gamma. */
    @Structure.FieldOrder({"lBrightness", "lContrast", "lGamma"})
    class ImageParam extends Structure {
        public int lBrightness;
        public int lContrast;
        public int lGamma;
    }

    /** _IDS_SCAN_CONF: tarama yapılandırması — by value Scan'e geçer. */
    @Structure.FieldOrder({"type", "mode", "side", "dpi", "front", "back"})
    class ScanConf extends Structure implements Structure.ByValue {
        public int type;            // _IDS_CARD_TYPE (e.g. 2 = ID_CN)
        public int mode;            // _IDS_SCAN_MODE
        public int side;            // _IDS_SCAN_SIDE
        public int dpi;             // _IDS_SCAN_DPI
        public ImageParam front = new ImageParam();
        public ImageParam back = new ImageParam();
    }

    /** _IDS_SCAN_RESULT: Scan dönüşünde dolar. */
    @Structure.FieldOrder({"cFileNameFront", "cFileNameBack", "ucCardDir", "ucCardType", "aucRsv"})
    class ScanResult extends Structure {
        public byte[] cFileNameFront = new byte[260];
        public byte[] cFileNameBack = new byte[260];
        public byte ucCardDir;     // Kart yönü — rotate ipucu
        public byte ucCardType;
        public byte[] aucRsv = new byte[2];

        public String getFrontPath() { return cstr(cFileNameFront); }
        public String getBackPath()  { return cstr(cFileNameBack); }
        private static String cstr(byte[] b) {
            int n = 0;
            while (n < b.length && b[n] != 0) n++;
            return new String(b, 0, n, java.nio.charset.StandardCharsets.UTF_8);
        }

        public static class ByReference extends ScanResult implements Structure.ByReference {}
    }

    /** PACE okuma sonucu — uint8_t*+size_t. JNA tarafında pointer+size. */
    @Structure.FieldOrder({"dgData", "dgDataSize"})
    class PcscReadResult extends Structure {
        public Pointer dgData;
        public com.sun.jna.NativeLong dgDataSize;

        public static class ByReference extends PcscReadResult implements Structure.ByReference {}
    }

    // === Fonksiyonlar (header'dan) ===

    /** Cihazı aç. pcFileSavePath: BMP'lerin kaydedileceği yol. */
    int OpenDev(String pcFileSavePath);

    /** Cihazı kapat. */
    int Uninit();

    /** Versiyon: 1=seri no, 2=firmware, 3=SDK, 4=üretici seri. */
    int GetVersionInfo(int iVerType, byte[] szVersionInfo);

    /** Kart durumu: 0=yok, 1=hareket, 2=ön, 3=içeride, 4=arka. */
    int CardStatus(IntByReference iStatus);

    /** Kartı belirtilen pozisyona taşı (Pos.* sabitleri). */
    int Move(int _Pos);

    /** Tarama yap, sonuç struct'ı dolar. */
    int Scan(ScanConf _ScanConf, ScanResult.ByReference pResult);

    /** Tek seferde tarama + MRZ tanıma. */
    int ScanMRZ(ScanConf _ScanConf, ScanResult.ByReference pResult,
                byte[] mrzData, IntByReference mrzLen);

    /** Var olan resimden MRZ tanı. */
    int RecognizeMRZ(ScanResult pResult, byte[] mrzData, IntByReference mrzLen,
                     IntByReference CardType, byte[] CardTypeName);

    /** BMP→JPG çevir. */
    int Bmp2Jpeg(int lQuality, String pcBmpFile, String pcJpegFile, boolean bDelSrc);

    /** BMP→PNG çevir. */
    int Bmp2Png(int lQuality, String pcBmpFile, String pcPngFile, boolean bDelSrc);

    /** DPI değiştir. */
    int CrtchangeImgDpi(String imgPath, int dpi);

    /** Resimleri birleştir (icomType: 0=yatay 1=dikey). */
    int CRT_CombineImg(String comPath, String img1, String img2, int icomType);

    /** Kart üzerindeki yüz fotoğrafını cropla. */
    int GetScanHead(String frontPath, String HeadPath);

    /** Buzzer kontrolü. */
    int SetBuzzer(int onOff);

    /**
     * PACE protokolü ile çipten DG oku — Burak gibi BAC reddeden kartlar için.
     * @param can CAN (Card Access Number, 6 hane) - kartın ön yüzünde
     * @param birth doğum tarihi YYMMDD
     * @param validity geçerlilik YYMMDD
     * @param bDG hangi DG (1, 2, 11, vb.)
     * @param out_result çıkış: DG verisi
     * @return 0=OK
     */
    int pcsc_pace_read_card(String can, String birth, String validity,
                            byte bDG, PcscReadResult.ByReference out_result);

    /** PACE result struct'ı serbest bırak. */
    void pcsc_pace_result_free(PcscReadResult.ByReference result);
}
