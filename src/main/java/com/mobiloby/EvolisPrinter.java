package com.mobiloby;

import com.sun.jna.Pointer;
import com.sun.jna.ptr.IntByReference;
import com.sun.jna.ptr.PointerByReference;

import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;

/**
 * Evolis yazıcı servis katmanı — UI'ın kullandığı temiz arayüz.
 *
 * evolis.dll çağrılarını sarmalar, bağlantı yaşam döngüsünü yönetir.
 * Tüm metotlar bağlantı yoksa güvenli şekilde davranır (exception fırlatmaz,
 * anlamlı bir sonuç döndürür) — UI'da her çağrıyı try/catch'e sarmamak için.
 */
public class EvolisPrinter implements AutoCloseable {

    private static final int PRINT_TIMEOUT_MS = 5 * 60_000;

    /**
     * Yarım panel renkli bandın konumlanma yöntemi: AUTO | CUSTOM | OFF.
     *
     * AUTO'da bandı yazıcı kendi yerleştirir; dikey (PORTRAIT) tasarımda bandı
     * fotoğrafın çok altına koyduğu gözlendi — fotoğraf yalnızca K paneliyle
     * basılıp açık tonlar kayboldu. Bu yüzden CUSTOM ile elle konumlandırıyoruz.
     */
    public static String shortPanelMode = "CUSTOM";

    /**
     * CUSTOM modda bandın kaydırma miktarı. PRN'de "Psp;<değer>" olarak gider.
     *
     * ETKİSİ YOK — kalibrasyon kartıyla ölçüldü, 0 ve 36 değerleri birebir aynı
     * kartı bastı; bant her durumda ~47,5 mm'de başlıyor. Negatif değerler ise
     * yazıcıda sessizce Psp;3'e dönüşüyor (yani reddedilmeleri bile belirtilmiyor).
     *
     * Bu yüzden bandın yeri sabit kabul edildi ve tasarım ona göre kuruldu:
     * fotoğraf CardRenderer.BAND_START_MM..BAND_END_MM arasına yerleştirildi.
     * Değer 0'da bırakıldı — AUTO görüntü içeriğine göre değişebildiği için
     * CUSTOM ile sabitlemek baskıyı öngörülebilir kılıyor.
     */
    public static int shortPanelShift = 0;

    private final EvolisSDK lib;
    private Pointer ctx;
    private String printerName;

    public EvolisPrinter() {
        this.lib = EvolisSDK.load();
    }

    // === Bağlantı ===

    /** Sistemdeki Evolis yazıcılarının adlarını listele. */
    public List<String> listPrinters() {
        List<String> names = new ArrayList<>();
        PointerByReference ref = new PointerByReference();
        int count = lib.evolis_get_devices(ref, 0);
        if (count <= 0) return names;
        Pointer p = ref.getValue();
        EvolisSDK.Device[] devices = (EvolisSDK.Device[]) new EvolisSDK.Device(p).toArray(count);
        for (EvolisSDK.Device d : devices) names.add(d.getName());
        lib.evolis_free_devices(p);
        return names;
    }

    /** İlk bulunan yazıcıya DIRECT modda bağlan. */
    public boolean connect() {
        List<String> printers = listPrinters();
        if (printers.isEmpty()) return false;
        return connect(printers.get(0));
    }

    /** Belirtilen yazıcıya bağlan — önce DIRECT, olmazsa SUPERVISED. */
    public boolean connect(String name) {
        disconnect();
        ctx = lib.evolis_open_with_mode(name, EvolisSDK.OpenMode.DIRECT);
        if (ctx == null) {
            ctx = lib.evolis_open_with_mode(name, EvolisSDK.OpenMode.SUPERVISED);
        }
        if (ctx != null) printerName = name;
        return ctx != null;
    }

    public boolean isConnected() { return ctx != null; }

    public String getPrinterName() { return printerName; }

    public void disconnect() {
        if (ctx != null) {
            try { lib.evolis_close(ctx); } catch (Throwable ignore) {}
            ctx = null;
            printerName = null;
        }
    }

    @Override
    public void close() { disconnect(); }

    // === Durum ===

    /** Yazıcının özet durumu — UI durum satırı için. */
    public static class State {
        public int major = EvolisSDK.State.OFF;
        public int minor;
        public boolean hasError;
        public boolean hasWarning;
        public List<String> flags = new ArrayList<>();

        public String label() {
            switch (major) {
                case EvolisSDK.State.READY:   return "Hazır";
                case EvolisSDK.State.WARNING: return "Uyarı";
                case EvolisSDK.State.ERROR:   return "Hata";
                default:                      return "Kapalı / bağlı değil";
            }
        }
    }

    /** Durumu ve açık bayrakları oku. */
    public State readState() {
        State s = new State();
        if (ctx == null) return s;

        IntByReference major = new IntByReference();
        IntByReference minor = new IntByReference();
        if (lib.evolis_get_state(ctx, major, minor) == EvolisSDK.Ret.OK) {
            s.major = major.getValue();
            s.minor = minor.getValue();
        }

        EvolisSDK.Status.ByReference st = new EvolisSDK.Status.ByReference();
        if (lib.evolis_status(ctx, st) == EvolisSDK.Ret.OK) {
            s.hasError = st.error != 0;
            s.hasWarning = st.warning != 0;
            for (int id = 0; id < EvolisFlags.NAMES.length; id++) {
                String name = EvolisFlags.name(id);
                if (name.startsWith("RSV_") || name.startsWith("CFG_")) continue;
                if (lib.evolis_status_is_on(st, id)) s.flags.add(name);
            }
        }
        return s;
    }

    /**
     * Mekanik hatayı temizle. Baskı sırasında mekanik hata olursa yazıcı
     * başka iş kabul etmez; bu çağrı onu hazır duruma döndürür.
     */
    public boolean clearMechanicalErrors() {
        if (ctx == null) return false;
        return lib.evolis_clear_mechanical_errors(ctx) == EvolisSDK.Ret.OK;
    }

    // === Bilgiler ===

    // Son okuma denemelerinin dönüş kodları — null döndüğünde sebebi görmek için.
    public int lastInfoRc, lastRibbonRc, lastCleaningRc;

    /** Yazıcı künyesi — model, seri no, firmware, donanım özellikleri. */
    public EvolisSDK.PrinterInfo.ByReference readInfo() {
        if (ctx == null) { lastInfoRc = Integer.MIN_VALUE; return null; }
        EvolisSDK.PrinterInfo.ByReference info = new EvolisSDK.PrinterInfo.ByReference();
        lastInfoRc = lib.evolis_get_info(ctx, info);
        return lastInfoRc == EvolisSDK.Ret.OK ? info : null;
    }

    /** Takılı ribon — tip, kapasite, kalan baskı. */
    public EvolisSDK.RibbonInfo.ByReference readRibbon() {
        if (ctx == null) { lastRibbonRc = Integer.MIN_VALUE; return null; }
        EvolisSDK.RibbonInfo.ByReference rb = new EvolisSDK.RibbonInfo.ByReference();
        lastRibbonRc = lib.evolis_get_ribbon(ctx, rb);
        return lastRibbonRc == EvolisSDK.Ret.OK ? rb : null;
    }

    /** Baskı sayaçları ve temizlik durumu. */
    public EvolisSDK.CleaningInfo.ByReference readCleaning() {
        if (ctx == null) { lastCleaningRc = Integer.MIN_VALUE; return null; }
        EvolisSDK.CleaningInfo.ByReference c = new EvolisSDK.CleaningInfo.ByReference();
        lastCleaningRc = lib.evolis_get_cleaning(ctx, c);
        return lastCleaningRc == EvolisSDK.Ret.OK ? c : null;
    }

    /** Aktif besleyici (A/B/C/D), okunamazsa -1. */
    public int readFeeder() {
        if (ctx == null) return -1;
        IntByReference f = new IntByReference();
        return lib.evolis_get_feeder(ctx, f) == EvolisSDK.Ret.OK ? f.getValue() : -1;
    }

    // === Baskı ===

    /** Baskı sonucu. */
    public static class PrintResult {
        public final boolean ok;
        public final int code;
        public final String message;

        PrintResult(boolean ok, int code, String message) {
            this.ok = ok; this.code = code; this.message = message;
        }
    }

    /**
     * Kart bas.
     *
     * @param imagePath basılacak bitmap
     * @param dryRun true ise PRN üretir ama KART HARCAMAZ
     */
    public PrintResult print(Path imagePath, boolean dryRun) {
        if (ctx == null) return new PrintResult(false, 0, "Yazıcı bağlı değil");
        if (!Files.exists(imagePath)) {
            return new PrintResult(false, 0, "Görsel bulunamadı: " + imagePath);
        }

        // Baskı öncesi hata kontrolü — hatalıysa yazıcı işi reddeder
        State s = readState();
        if (s.hasError) {
            return new PrintResult(false, 0,
                    "Yazıcıda hata var, önce temizleyin: " + String.join(", ", s.flags));
        }

        int rc = lib.evolis_print_init(ctx);
        if (rc != EvolisSDK.Ret.OK) {
            return new PrintResult(false, rc, "print_init başarısız");
        }

        // Dikey tasarım + yarım panel ribon renkli bant konumlama
        lib.evolis_print_set_setting(ctx, EvolisSDK.SettingKey.ORIENTATION, "PORTRAIT");
        lib.evolis_print_set_setting(ctx, EvolisSDK.SettingKey.G_SHORT_PANEL_MANAGEMENT, shortPanelMode);
        if ("CUSTOM".equals(shortPanelMode)) {
            lib.evolis_print_set_setting(ctx, EvolisSDK.SettingKey.I_SHORT_PANEL_SHIFT,
                    String.valueOf(shortPanelShift));
        }

        rc = lib.evolis_print_set_imagep(ctx, EvolisSDK.CardFace.FRONT,
                imagePath.toAbsolutePath().toString());
        if (rc != EvolisSDK.Ret.OK) {
            return new PrintResult(false, rc, "Görsel yüklenemedi");
        }

        if (dryRun) {
            Path prn = AppPaths.resolve("output", "card_dryrun.prn").toAbsolutePath();
            rc = lib.evolis_print_to_file(ctx, prn.toString());
            return rc == EvolisSDK.Ret.OK
                    ? new PrintResult(true, rc, "Prova başarılı — kart harcanmadı (" + prn.getFileName() + ")")
                    : new PrintResult(false, rc, "Prova başarısız");
        }

        rc = lib.evolis_print_exect(ctx, PRINT_TIMEOUT_MS);
        if (rc == EvolisSDK.Ret.OK) {
            return new PrintResult(true, rc, "Baskı tamamlandı");
        }
        return new PrintResult(false, rc, "Baskı başarısız: " + returnCodeName(rc));
    }

    /**
     * Çift yüz çevirme ekseni — GDuplexType. NONE=tek yüz, HORIZONTAL/VERTICAL
     * çevirme yönünü belirler. Hangisinin doğru olduğu kalibrasyonla saptanır.
     */
    public static String duplexType = "HORIZONTAL";

    /**
     * Kartın ÖN ve ARKA yüzünü tek işte bas (çift yüz).
     *
     * Yazıcı kartı fiziksel olarak çevirir; biz iki görsel veririz. Arka görselin
     * yönü (ters/ayna) duplexType eksenine bağlı — gerçek baskıdan önce
     * kalibrasyon kartıyla doğrulanmalı.
     */
    public PrintResult printDuplex(Path front, Path back, boolean dryRun) {
        if (ctx == null) return new PrintResult(false, 0, "Yazıcı bağlı değil");
        if (!Files.exists(front)) return new PrintResult(false, 0, "Ön görsel yok: " + front);
        if (!Files.exists(back)) return new PrintResult(false, 0, "Arka görsel yok: " + back);

        State s = readState();
        if (s.hasError) {
            return new PrintResult(false, 0,
                    "Yazıcıda hata var, önce temizleyin: " + String.join(", ", s.flags));
        }

        int rc = lib.evolis_print_init(ctx);
        if (rc != EvolisSDK.Ret.OK) return new PrintResult(false, rc, "print_init başarısız");

        lib.evolis_print_set_setting(ctx, EvolisSDK.SettingKey.ORIENTATION, "PORTRAIT");
        lib.evolis_print_set_setting(ctx, EvolisSDK.SettingKey.G_SHORT_PANEL_MANAGEMENT, shortPanelMode);
        if ("CUSTOM".equals(shortPanelMode)) {
            lib.evolis_print_set_setting(ctx, EvolisSDK.SettingKey.I_SHORT_PANEL_SHIFT,
                    String.valueOf(shortPanelShift));
        }
        lib.evolis_print_set_setting(ctx, EvolisSDK.SettingKey.G_DUPLEX_TYPE, duplexType);

        rc = lib.evolis_print_set_imagep(ctx, EvolisSDK.CardFace.FRONT, front.toAbsolutePath().toString());
        if (rc != EvolisSDK.Ret.OK) return new PrintResult(false, rc, "Ön görsel yüklenemedi");
        rc = lib.evolis_print_set_imagep(ctx, EvolisSDK.CardFace.BACK, back.toAbsolutePath().toString());
        if (rc != EvolisSDK.Ret.OK) return new PrintResult(false, rc, "Arka görsel yüklenemedi");

        if (dryRun) {
            Path prn = AppPaths.resolve("output", "card_duplex_dryrun.prn").toAbsolutePath();
            rc = lib.evolis_print_to_file(ctx, prn.toString());
            return rc == EvolisSDK.Ret.OK
                    ? new PrintResult(true, rc, "Çift yüz prova başarılı — kart harcanmadı (" + prn.getFileName() + ")")
                    : new PrintResult(false, rc, "Çift yüz prova başarısız: " + returnCodeName(rc));
        }

        rc = lib.evolis_print_exect(ctx, PRINT_TIMEOUT_MS);
        return rc == EvolisSDK.Ret.OK
                ? new PrintResult(true, rc, "Çift yüz baskı tamamlandı")
                : new PrintResult(false, rc, "Çift yüz baskı başarısız: " + returnCodeName(rc));
    }

    /** Sık karşılaşılan dönüş kodlarının okunabilir karşılığı. */
    public static String returnCodeName(int rc) {
        switch (rc) {
            case 0:   return "OK";
            case -1:  return "EUNDEFINED";
            case -2:  return "EINTERNAL";
            case -3:  return "ECANCELLED";
            case -6:  return "EPARAMS";
            case -7:  return "ETIMEOUT";
            case -8:  return "ENEEDACTION";
            case -21: return "PRINT_NEEDACTION (yazıcı hazır değil — ribon/kapak/hazne)";
            case -22: return "PRINT_EMECHANICAL (kart veya ribon sıkışması)";
            case -23: return "PRINT_WAITCARDINSERT";
            default:  return "kod " + rc;
        }
    }
}
