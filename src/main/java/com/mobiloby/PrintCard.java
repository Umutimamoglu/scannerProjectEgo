package com.mobiloby;

import com.sun.jna.Pointer;
import com.sun.jna.ptr.PointerByReference;

import java.nio.file.Files;
import java.nio.file.Path;

/**
 * Kart baskısı — CardRenderer'ın ürettiği BMP'yi Evolis KC Prime'a gönderir.
 *
 * GÜVENLİK: Varsayılan olarak PROVA yapar (evolis_print_to_file) — tüm baskı
 * hattını çalıştırır, PRN dosyası üretir ama KART HARCAMAZ. Gerçek baskı için
 * açıkça --onayla vermek gerekir.
 *
 * Kullanım:
 *   run.bat print              → prova (kart harcamaz)
 *   run.bat print --onayla     → GERÇEK BASKI (kart harcar)
 */
public class PrintCard {

    private static final int PRINT_TIMEOUT_MS = 5 * 60_000;

    public static void main(String[] args) {
        boolean confirmed = hasFlag(args, "--onayla");
        Pointer ctx = null;
        EvolisSDK lib = null;

        try {
            Path bmp = AppPaths.resolve("output", "card_print.bmp").toAbsolutePath();
            if (!Files.exists(bmp)) {
                System.out.println("Basılacak görsel yok: " + bmp);
                System.out.println("Önce 'run.bat card' ile üretin.");
                return;
            }
            System.out.println("Görsel: " + bmp);
            System.out.println("Mod   : " + (confirmed ? "GERÇEK BASKI" : "PROVA (kart harcanmaz)"));
            System.out.println();

            lib = EvolisSDK.load();

            // Cihazı bul
            PointerByReference ref = new PointerByReference();
            int count = lib.evolis_get_devices(ref, 0);
            if (count <= 0) {
                System.out.println("Yazıcı bulunamadı.");
                return;
            }
            Pointer devPtr = ref.getValue();
            EvolisSDK.Device dev = new EvolisSDK.Device(devPtr);
            String printerName = dev.getName();
            lib.evolis_free_devices(devPtr);
            System.out.println("Yazıcı: " + printerName);

            ctx = lib.evolis_open_with_mode(printerName, EvolisSDK.OpenMode.DIRECT);
            if (ctx == null) {
                System.out.println("DIRECT modda bağlanılamadı.");
                return;
            }

            // Baskı öncesi durum kontrolü — hata varsa hiç başlama
            EvolisSDK.Status.ByReference st = new EvolisSDK.Status.ByReference();
            if (lib.evolis_status(ctx, st) == EvolisSDK.Ret.OK) {
                if (st.error != 0) {
                    System.out.println("Yazıcıda HATA var, baskı iptal edildi:");
                    printFlags(lib, st, "ERR_");
                    return;
                }
                printFlags(lib, st, "WAR_");
            }

            // Ribon bilgisi
            EvolisSDK.RibbonInfo.ByReference rb = new EvolisSDK.RibbonInfo.ByReference();
            if (lib.evolis_get_ribbon(ctx, rb) == EvolisSDK.Ret.OK) {
                System.out.println("Ribon : " + rb.getDescription() + " (" + rb.getProductCode() + ")");
            }

            // Baskı oturumu
            int rc = lib.evolis_print_init(ctx);
            System.out.println("evolis_print_init → " + rc);
            if (rc != EvolisSDK.Ret.OK) return;

            // Dikey tasarım — bitmap'i biz döndürmüyoruz, ayarla belirtiyoruz
            setSetting(lib, ctx, EvolisSDK.SettingKey.ORIENTATION, "PORTRAIT");
            // Yarım panel ribon: renkli bölgeyi yazıcı kendi bulsun
            setSetting(lib, ctx, EvolisSDK.SettingKey.G_SHORT_PANEL_MANAGEMENT, "AUTO");

            rc = lib.evolis_print_set_imagep(ctx, EvolisSDK.CardFace.FRONT, bmp.toString());
            System.out.println("evolis_print_set_imagep → " + rc);
            if (rc != EvolisSDK.Ret.OK) return;

            if (!confirmed) {
                // PROVA: tüm hattı çalıştır, PRN üret, kart harcama
                Path prn = AppPaths.resolve("output", "card_dryrun.prn").toAbsolutePath();
                rc = lib.evolis_print_to_file(ctx, prn.toString());
                System.out.println("evolis_print_to_file → " + rc);
                if (rc == EvolisSDK.Ret.OK && Files.exists(prn)) {
                    System.out.println("PRN üretildi: " + prn + " (" + Files.size(prn) + " byte)");
                    System.out.println();
                    System.out.println("PROVA BAŞARILI — baskı hattı sorunsuz çalışıyor.");
                    System.out.println("Gerçek baskı için: run.bat print --onayla");
                } else {
                    System.out.println("Prova başarısız (rc=" + rc + ").");
                }
                return;
            }

            // GERÇEK BASKI
            System.out.println();
            System.out.println(">>> BASKI BAŞLIYOR — kart harcanacak...");
            rc = lib.evolis_print_exect(ctx, PRINT_TIMEOUT_MS);
            System.out.println("evolis_print_exect → " + rc);
            System.out.println(rc == EvolisSDK.Ret.OK ? "BASKI TAMAM." : "Baskı başarısız (rc=" + rc + ").");

            if (lib.evolis_status(ctx, st) == EvolisSDK.Ret.OK && st.error != 0) {
                System.out.println("Baskı sonrası hata bayrakları:");
                printFlags(lib, st, "ERR_");
            }

        } catch (Throwable e) {
            System.err.println("KRİTİK HATA: " + e.getMessage());
            e.printStackTrace();
        } finally {
            if (ctx != null && lib != null) {
                try { lib.evolis_close(ctx); } catch (Throwable ignore) {}
            }
        }
    }

    private static void setSetting(EvolisSDK lib, Pointer ctx, int key, String value) {
        boolean ok = lib.evolis_print_set_setting(ctx, key, value);
        System.out.println("  ayar [" + key + "] = " + value + (ok ? "" : "  (REDDEDİLDİ)"));
    }

    private static void printFlags(EvolisSDK lib, EvolisSDK.Status.ByReference st, String prefix) {
        for (int id = 0; id < EvolisFlags.NAMES.length; id++) {
            String name = EvolisFlags.name(id);
            if (!name.startsWith(prefix)) continue;
            if (lib.evolis_status_is_on(st, id)) System.out.println("    [" + id + "] " + name);
        }
    }

    private static boolean hasFlag(String[] args, String flag) {
        for (String a : args) if (a.equals(flag)) return true;
        return false;
    }
}
