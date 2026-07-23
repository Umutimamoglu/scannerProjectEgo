package com.mobiloby;

import com.sun.jna.Library;
import com.sun.jna.Native;
import com.sun.jna.Pointer;
import com.sun.jna.Structure;
import com.sun.jna.ptr.IntByReference;
import com.sun.jna.ptr.PointerByReference;

import java.io.File;

/**
 * evolis.dll (Evolis KC Prime kart yazıcısı) JNA binding.
 * 64-bit DLL, cdecl convention.
 *
 * SDK sürümü: Evolis SDK v3 (PyPI Evolis-SDK 9.4.1 içinden çıkarıldı).
 * İmzalar SDK'nın Python ctypes bağlayıcılarından birebir alındı.
 */
public interface EvolisSDK extends Library {

    String NATIVE_DIR = new File("native_evolis").getAbsolutePath();

    static EvolisSDK load() {
        System.setProperty("jna.library.path", NATIVE_DIR);
        return Native.load("evolis", EvolisSDK.class);
    }

    // === Enum sabitleri (SDK'dan) ===

    /** Yazıcıya nasıl bağlanılacağı. */
    interface OpenMode {
        int AUTO = 0;        // Otomatik seç
        int DIRECT = 1;      // Doğrudan iletişim — Premium Suite gerekmez
        int SUPERVISED = 2;  // Evolis Supervision Service üzerinden
    }

    /** Kartın hangi yüzü. */
    interface CardFace {
        int FRONT = 0;
        int BACK = 1;
    }

    /** Baskı sonrası kart nereye gitsin. */
    interface Ret {
        int OK = 0;
        int EUNDEFINED = -1, EINTERNAL = -2, ECANCELLED = -3, EDISABLED = -4;
        int EUNSUPPORTED = -5, EPARAMS = -6, ETIMEOUT = -7, ENEEDACTION = -8;
        int SESSION_ETIMEOUT = -10, SESSION_EBUSY = -11, SESSION_DISABLED = -12;
    }

    /** Yazıcı markası (Device.mark). */
    interface Mark {
        int INVALID = 0, EVOLIS = 1, EDIKIO = 3, BADGEPASS = 4;
        int ID_MAKER = 5, DURABLE = 6, PLASCO = 7, IDENTISYS = 8, BODNO = 9, BRAVO = 10;
    }

    /** Yazıcı modeli (Device.model) — bizimki KC_PRIME. */
    interface Model {
        int INVALID = 0;
        int KC100 = 1, KC100B = 2, KC200 = 3, KC200B = 4;
        int KC_ESSENTIAL = 31, KC_PRIME = 32, KC_MAX = 34;
    }

    // === Struct'lar (SDK'dan) ===

    /** evolis_device_t: evolis_get_devices ile dolar. */
    @Structure.FieldOrder({"id", "name", "displayName", "uri", "mark", "model",
                           "isSupervised", "isOnline", "link", "driverVersion"})
    class Device extends Structure {
        public byte[] id = new byte[128];
        public byte[] name = new byte[256];
        public byte[] displayName = new byte[256];
        public byte[] uri = new byte[512];
        public int mark;
        public int model;
        public boolean isSupervised;
        public boolean isOnline;
        public int link;
        public byte[] driverVersion = new byte[128];

        public Device() {}

        /** evolis_get_devices'in döndürdüğü diziyi okumak için. */
        public Device(Pointer p) {
            super(p);
            read();
        }

        public String getId()            { return cstr(id); }
        public String getName()          { return cstr(name); }
        public String getDisplayName()   { return cstr(displayName); }
        public String getUri()           { return cstr(uri); }
        public String getDriverVersion() { return cstr(driverVersion); }

        private static String cstr(byte[] b) {
            int n = 0;
            while (n < b.length && b[n] != 0) n++;
            return new String(b, 0, n, java.nio.charset.StandardCharsets.UTF_8);
        }

        public static class ByReference extends Device implements Structure.ByReference {}
    }

    /** Baskı ayarı anahtarları (SettingKey enum'undan). */
    interface SettingKey {
        int G_DUPLEX_TYPE = 40;
        int G_RIBBON_TYPE = 52;
        int G_SHORT_PANEL_MANAGEMENT = 55;   // AUTO | CUSTOM | OFF
        int I_SHORT_PANEL_SHIFT = 127;
        int ORIENTATION = 128;               // LANDSCAPE_CC90 | PORTRAIT
    }

    /** evolis_ribbon_t: takılı ribon bilgisi. */
    @Structure.FieldOrder({"description", "zone", "type", "capacity", "remaining",
                           "progress", "productCode", "batchNumber", "buildAt",
                           "serialNumber", "internalCode"})
    class RibbonInfo extends Structure {
        public byte[] description = new byte[64];
        public byte[] zone = new byte[8];
        public int type;
        public int capacity;
        public int remaining;
        public int progress;
        public byte[] productCode = new byte[16];
        public int batchNumber;
        public byte[] buildAt = new byte[24];
        public byte[] serialNumber = new byte[24];
        public byte[] internalCode = new byte[24];

        public String getDescription() { return cstr(description); }
        public String getProductCode() { return cstr(productCode); }
        public String getZone()        { return cstr(zone); }

        private static String cstr(byte[] b) {
            int n = 0;
            while (n < b.length && b[n] != 0) n++;
            return new String(b, 0, n, java.nio.charset.StandardCharsets.UTF_8);
        }

        public static class ByReference extends RibbonInfo implements Structure.ByReference {}
    }

    /** evolis_get_state ana durumu. */
    interface State {
        int OFF = 0, READY = 1, WARNING = 2, ERROR = 3;
    }

    /** Kartın alınacağı yer. */
    interface InputTray {
        int FEEDER = 1, MANUAL = 2, MANUALANDCMD = 4, BEZEL = 8,
            BOTH = 16, NOFEEDER = 32, REAR = 64;
    }

    /** Kartın gönderileceği yer. */
    interface OutputTray {
        int STANDARD = 1, STANDARDSTANDBY = 2, MANUAL = 4, ERROR = 8,
            ERRORSTANDBY = 16, EJECT = 32, BEZEL = 64, ERRORSLOT = 128, LOCKED = 256;
    }

    /** Bezel (kart yuvası) davranışı. */
    interface BezelBehavior {
        int UNKNOWN = 0, REJECT = 1, INSERT = 2, DONOTHING = 3;
    }

    /** evolis_printer_info_t: model, seri no, firmware, donanım özellikleri. */
    @Structure.FieldOrder({"name", "type", "mark", "markName", "model", "modelName", "modelId",
                           "fwVersion", "serialNumber", "printHeadKitNumber", "zone",
                           "hasFlip", "hasEthernet", "hasWifi", "hasLaminator", "hasLaminator2",
                           "hasMagEnc", "hasJisMagEnc", "hasSmartEnc", "hasContactLessEnc",
                           "hasLcd", "hasKineclipse", "hasLock", "hasScanner",
                           "insertionCaps", "ejectionCaps", "rejectionCaps",
                           "lcdFwVersion", "lcdGraphVersion", "scannerFwVersion"})
    class PrinterInfo extends Structure {
        public byte[] name = new byte[128];
        public int type;
        public int mark;
        public byte[] markName = new byte[32];
        public int model;
        public byte[] modelName = new byte[32];
        public int modelId;
        public byte[] fwVersion = new byte[16];
        public byte[] serialNumber = new byte[16];
        public byte[] printHeadKitNumber = new byte[16];
        public byte[] zone = new byte[16];
        public boolean hasFlip;
        public boolean hasEthernet;
        public boolean hasWifi;
        public boolean hasLaminator;
        public boolean hasLaminator2;
        public boolean hasMagEnc;
        public boolean hasJisMagEnc;
        public boolean hasSmartEnc;
        public boolean hasContactLessEnc;
        public boolean hasLcd;
        public boolean hasKineclipse;
        public boolean hasLock;
        public boolean hasScanner;
        public int insertionCaps;
        public int ejectionCaps;
        public int rejectionCaps;
        public byte[] lcdFwVersion = new byte[16];
        public byte[] lcdGraphVersion = new byte[16];
        public byte[] scannerFwVersion = new byte[64];

        public String getName()         { return str(name); }
        public String getModelName()    { return str(modelName); }
        public String getMarkName()     { return str(markName); }
        public String getFwVersion()    { return str(fwVersion); }
        public String getSerialNumber() { return str(serialNumber); }
        public String getPrintHeadKit() { return str(printHeadKitNumber); }
        public String getZone()         { return str(zone); }

        private static String str(byte[] b) {
            int n = 0;
            while (n < b.length && b[n] != 0) n++;
            return new String(b, 0, n, java.nio.charset.StandardCharsets.UTF_8);
        }

        public static class ByReference extends PrinterInfo implements Structure.ByReference {}
    }

    /** evolis_cleaning_t: baskı sayaçları ve temizlik durumu. */
    @Structure.FieldOrder({"totalCardCount", "cardCount", "cardCountBeforeWarning",
                           "cardCountBeforeWarrantyLost", "cardCountAtLastCleaning",
                           "regularCleaningCount", "advancedCleaningCount",
                           "printHeadUnderWarranty", "warningThreshold", "warrantyLostThreshold"})
    class CleaningInfo extends Structure {
        public int totalCardCount;                // Ömür boyu basılan kart
        public int cardCount;                     // Son temizlikten beri
        public int cardCountBeforeWarning;        // Temizlik uyarısına kalan
        public int cardCountBeforeWarrantyLost;
        public int cardCountAtLastCleaning;
        public int regularCleaningCount;
        public int advancedCleaningCount;
        public boolean printHeadUnderWarranty;
        public int warningThreshold;
        public int warrantyLostThreshold;

        public static class ByReference extends CleaningInfo implements Structure.ByReference {}
    }

    /** evolis_status_t: yazıcı durum bayrakları. */
    @Structure.FieldOrder({"config", "information", "warning", "error", "exts", "session"})
    class Status extends Structure {
        public int config;
        public int information;
        public int warning;
        public int error;
        public int[] exts = new int[4];
        public short session;

        public static class ByReference extends Status implements Structure.ByReference {}
    }

    // === Fonksiyonlar ===

    /**
     * Sistemdeki Evolis cihazlarını listele.
     * @param devices çıkış: Device dizisine pointer (evolis_free_devices ile bırakılmalı)
     * @param flags 0 = varsayılan
     * @return bulunan cihaz sayısı (negatifse hata)
     */
    int evolis_get_devices(PointerByReference devices, int flags);

    /** evolis_get_devices'in ayırdığı belleği serbest bırak. */
    void evolis_free_devices(Pointer devices);

    /** Yazıcıya bağlan (varsayılan mod). Dönen context NULL ise başarısız. */
    Pointer evolis_open(String name);

    /** Yazıcıya belirli bir modla bağlan (OpenMode.*). */
    Pointer evolis_open_with_mode(String name, int mode);

    /** Bağlantıyı kapat. */
    void evolis_close(Pointer context);

    /** Yazıcı durumunu oku. */
    int evolis_status(Pointer context, Status.ByReference status);

    /** Durumu okunabilir metne çevir. */
    long evolis_status_to_string(Status.ByReference status, byte[] buffer, long bufferSize);

    /**
     * Belirli bir durum bayrağı açık mı?
     * @param flagId 0-255 arası bayrak ID'si — bkz. {@link EvolisFlags}
     */
    boolean evolis_status_is_on(Status.ByReference status, int flagId);

    /** Takılı ribon bilgisini oku. */
    int evolis_get_ribbon(Pointer context, RibbonInfo.ByReference ribbon);

    /** Yazıcı künyesi: model, seri no, firmware, donanım özellikleri. */
    int evolis_get_info(Pointer context, PrinterInfo.ByReference info);

    /** Baskı sayaçları ve temizlik durumu. */
    int evolis_get_cleaning(Pointer context, CleaningInfo.ByReference cleaning);

    /** Aktif besleyici (Feeder A/B/C/D). */
    int evolis_get_feeder(Pointer context, IntByReference feeder);

    /** Kartın nereden alınacağı (InputTray.*). */
    int evolis_get_input_tray(Pointer context, IntByReference tray);

    /** Başarılı baskıdan sonra kartın nereye gideceği (OutputTray.*). */
    int evolis_get_output_tray(Pointer context, IntByReference tray);

    /** Hatalı kartın nereye gideceği (OutputTray.*). */
    int evolis_get_error_tray(Pointer context, IntByReference tray);

    /** Bezel davranışı (BezelBehavior.*). */
    int evolis_bezel_get_behavior(Pointer context, IntByReference behavior);

    /**
     * Yazıcının özet durumu.
     * @param major State.* (OFF/READY/WARNING/ERROR)
     * @param minor detaylı sebep kodu
     */
    int evolis_get_state(Pointer context, IntByReference major, IntByReference minor);

    /** Model ID'sinden okunabilir model adı. */
    String evolis_get_model_name(int model);

    /**
     * Mekanik hatayı temizle ve yazıcıyı hazır duruma döndür.
     *
     * Baskı sırasında mekanik hata olursa (kart/ribon sıkışması) yazıcı
     * ERR_MECHANICAL bayrağını set eder ve BAŞKA HİÇBİR İŞİ KABUL ETMEZ.
     * Bu çağrı onu sıfırlar.
     */
    int evolis_clear_mechanical_errors(Pointer context);

    /** Baskı oturumunu başlat. */
    int evolis_print_init(Pointer context);

    /** Metin tipi baskı ayarı ver (SettingKey.* / değer string). */
    boolean evolis_print_set_setting(Pointer context, int key, String value);

    /** Sayısal baskı ayarı ver. */
    boolean evolis_print_set_int_setting(Pointer context, int key, int value);

    /** Baskı ayarını oku. */
    boolean evolis_print_get_setting(Pointer context, int key, PointerByReference value);

    /** Baskı ayarlarını yüklü sürücüden içe aktar (opsiyonel). */
    int evolis_print_init_from_driver_settings(Pointer context);

    /** Basılacak görseli dosya yolundan ver (CardFace.FRONT / BACK). */
    int evolis_print_set_imagep(Pointer context, int face, String path);

    /** Baskıyı çalıştır. timeout ms cinsinden. */
    int evolis_print_exect(Pointer context, int timeout);

    /**
     * PRN dosyası üret ama BASMA — kart harcamadan baskı hattını sınamak için.
     */
    int evolis_print_to_file(Pointer context, String path);

    /** Test kartı bas. */
    int evolis_print_test_card(Pointer context, int type);
}
