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

        // Dikey tasarım + yarım panel ribon için otomatik renkli bant konumlama
        lib.evolis_print_set_setting(ctx, EvolisSDK.SettingKey.ORIENTATION, "PORTRAIT");
        lib.evolis_print_set_setting(ctx, EvolisSDK.SettingKey.G_SHORT_PANEL_MANAGEMENT, "AUTO");

        rc = lib.evolis_print_set_imagep(ctx, EvolisSDK.CardFace.FRONT,
                imagePath.toAbsolutePath().toString());
        if (rc != EvolisSDK.Ret.OK) {
            return new PrintResult(false, rc, "Görsel yüklenemedi");
        }

        if (dryRun) {
            Path prn = Path.of("output", "card_dryrun.prn").toAbsolutePath();
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
