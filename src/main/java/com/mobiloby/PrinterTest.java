package com.mobiloby;

import com.sun.jna.Pointer;
import com.sun.jna.ptr.PointerByReference;

import java.nio.charset.StandardCharsets;

/**
 * Evolis KC Prime bağlantı testi — sürücüsüz (DIRECT) modu doğrular.
 *
 * Amaç: Premium Suite / yazıcı sürücüsü kurulu DEĞİLKEN evolis.dll'in
 * cihazı USB üzerinden görüp göremediğini anlamak. Cihaz bulunursa
 * sürücüsüz entegrasyon mümkün demektir.
 *
 * Çalıştırma: run.bat printer
 */
public class PrinterTest {

    public static void main(String[] args) {
        try {
            System.out.println("Evolis SDK yükleniyor (evolis.dll, 64-bit)...");
            EvolisSDK lib = EvolisSDK.load();
            System.out.println("DLL yüklendi: " + EvolisSDK.NATIVE_DIR);
            System.out.println();

            // 1. Cihazları listele — asıl test bu
            System.out.println("=== evolis_get_devices ===");
            PointerByReference ref = new PointerByReference();
            int count = lib.evolis_get_devices(ref, 0);
            System.out.println("Bulunan cihaz sayısı: " + count);

            if (count <= 0) {
                System.out.println();
                System.out.println(">>> Cihaz bulunamadı.");
                System.out.println(">>> Olası nedenler:");
                System.out.println("    - Yazıcı USB'ye takılı değil veya açık değil");
                System.out.println("    - DIRECT mod bir sürücü gerektiriyor (Premium Suite kurulmalı)");
                return;
            }

            Pointer p = ref.getValue();
            EvolisSDK.Device[] devices = (EvolisSDK.Device[]) new EvolisSDK.Device(p).toArray(count);

            EvolisSDK.Device target = null;
            for (int i = 0; i < devices.length; i++) {
                EvolisSDK.Device d = devices[i];
                System.out.println();
                System.out.println("--- Cihaz " + (i + 1) + " ---");
                System.out.println("  Ad            : " + d.getName());
                System.out.println("  Görünen ad    : " + d.getDisplayName());
                System.out.println("  URI           : " + d.getUri());
                System.out.println("  Model         : " + modelName(d.model) + " (" + d.model + ")");
                System.out.println("  Marka         : " + d.mark);
                System.out.println("  Çevrimiçi     : " + d.isOnline);
                System.out.println("  Supervised    : " + d.isSupervised);
                System.out.println("  Sürücü sürümü : " +
                        (d.getDriverVersion().isEmpty() ? "(yok — sürücüsüz)" : d.getDriverVersion()));
                if (target == null) target = d;
            }
            lib.evolis_free_devices(p);

            // 2. DIRECT modda bağlan
            System.out.println();
            System.out.println("=== evolis_open_with_mode (DIRECT) ===");
            Pointer ctx = lib.evolis_open_with_mode(target.getName(), EvolisSDK.OpenMode.DIRECT);
            if (ctx == null) {
                System.out.println(">>> DIRECT modda bağlanılamadı.");
                System.out.println(">>> SUPERVISED mod deneniyor (Premium Suite gerekir)...");
                ctx = lib.evolis_open_with_mode(target.getName(), EvolisSDK.OpenMode.SUPERVISED);
                if (ctx == null) {
                    System.out.println(">>> O da olmadı. Sürücü kurulumu gerekiyor.");
                    return;
                }
                System.out.println("SUPERVISED modda bağlandık.");
            } else {
                System.out.println("DIRECT modda bağlandık — sürücüsüz çalışıyor!");
            }

            // 3. Durum oku
            System.out.println();
            System.out.println("=== evolis_status ===");
            EvolisSDK.Status.ByReference st = new EvolisSDK.Status.ByReference();
            int rc = lib.evolis_status(ctx, st);
            System.out.println("evolis_status → " + rc);
            if (rc == EvolisSDK.Ret.OK) {
                System.out.printf("  config=0x%08X information=0x%08X%n", st.config, st.information);
                System.out.printf("  warning=0x%08X error=0x%08X%n", st.warning, st.error);

                // Açık olan bayrakları çöz — hex yerine okunabilir isimler
                System.out.println();
                System.out.println("  Açık bayraklar:");
                int shown = 0;
                for (int id = 0; id < EvolisFlags.NAMES.length; id++) {
                    if (!lib.evolis_status_is_on(st, id)) continue;
                    String name = EvolisFlags.name(id);
                    if (name.startsWith("RSV_")) continue;   // rezerve bitler ilgisiz
                    System.out.println("    [" + id + "] " + name);
                    shown++;
                }
                if (shown == 0) System.out.println("    (yok)");
            }

            // 4. İstenirse mekanik hatayı temizle (--temizle)
            if (hasFlag(args, "--temizle")) {
                System.out.println();
                System.out.println("=== evolis_clear_mechanical_errors ===");
                int crc = lib.evolis_clear_mechanical_errors(ctx);
                System.out.println("Temizleme → " + crc +
                        (crc == EvolisSDK.Ret.OK ? " (yazıcı hazır duruma döndü)" : " (başarısız)"));
            }

            // 5. Ribon bilgisi — tek kartımız olduğu için baskı öncesi kritik
            System.out.println();
            System.out.println("=== evolis_get_ribbon ===");
            EvolisSDK.RibbonInfo.ByReference rb = new EvolisSDK.RibbonInfo.ByReference();
            int rrc = lib.evolis_get_ribbon(ctx, rb);
            System.out.println("evolis_get_ribbon → " + rrc);
            if (rrc == EvolisSDK.Ret.OK) {
                System.out.println("  Açıklama   : " + rb.getDescription());
                System.out.println("  Ürün kodu  : " + rb.getProductCode());
                System.out.println("  Tip (enum) : " + rb.type);
                System.out.println("  Kapasite   : " + rb.capacity);
                System.out.println("  Kalan      : " + rb.remaining);
            }

            // 6. Kart yolu yapılandırması — mekanik hata teşhisi için
            System.out.println();
            System.out.println("=== Kart yolu yapılandırması ===");
            com.sun.jna.ptr.IntByReference v = new com.sun.jna.ptr.IntByReference();
            if (lib.evolis_get_input_tray(ctx, v) == EvolisSDK.Ret.OK) {
                System.out.println("  Giriş (input tray) : " + inputTrayName(v.getValue()));
            }
            if (lib.evolis_get_output_tray(ctx, v) == EvolisSDK.Ret.OK) {
                System.out.println("  Çıkış (output tray): " + outputTrayName(v.getValue()));
            }
            if (lib.evolis_get_error_tray(ctx, v) == EvolisSDK.Ret.OK) {
                System.out.println("  Hata (error tray)  : " + outputTrayName(v.getValue()));
            }
            if (lib.evolis_bezel_get_behavior(ctx, v) == EvolisSDK.Ret.OK) {
                System.out.println("  Bezel davranışı    : " + bezelName(v.getValue()));
            }

            lib.evolis_close(ctx);
            System.out.println();
            System.out.println("Bağlantı kapatıldı. Test tamam.");

        } catch (Throwable e) {
            System.err.println("KRİTİK HATA: " + e.getMessage());
            e.printStackTrace();
        }
    }

    private static String inputTrayName(int v) {
        switch (v) {
            case EvolisSDK.InputTray.FEEDER:       return "FEEDER (hazne)";
            case EvolisSDK.InputTray.MANUAL:       return "MANUAL (elle besleme)";
            case EvolisSDK.InputTray.MANUALANDCMD: return "MANUALANDCMD";
            case EvolisSDK.InputTray.BEZEL:        return "BEZEL (yuvadan)";
            case EvolisSDK.InputTray.BOTH:         return "BOTH";
            case EvolisSDK.InputTray.NOFEEDER:     return "NOFEEDER";
            case EvolisSDK.InputTray.REAR:         return "REAR (arkadan)";
            default:                               return "bilinmeyen (" + v + ")";
        }
    }

    private static String outputTrayName(int v) {
        switch (v) {
            case EvolisSDK.OutputTray.STANDARD:        return "STANDARD (normal çıkış)";
            case EvolisSDK.OutputTray.STANDARDSTANDBY: return "STANDARDSTANDBY";
            case EvolisSDK.OutputTray.MANUAL:          return "MANUAL";
            case EvolisSDK.OutputTray.ERROR:           return "ERROR (hata kutusu)";
            case EvolisSDK.OutputTray.ERRORSTANDBY:    return "ERRORSTANDBY";
            case EvolisSDK.OutputTray.EJECT:           return "EJECT (dışarı at)";
            case EvolisSDK.OutputTray.BEZEL:           return "BEZEL (yuvaya ver)";
            case EvolisSDK.OutputTray.ERRORSLOT:       return "ERRORSLOT (ret yuvası)";
            case EvolisSDK.OutputTray.LOCKED:          return "LOCKED (kilitli)";
            default:                                   return "bilinmeyen (" + v + ")";
        }
    }

    private static String bezelName(int v) {
        switch (v) {
            case EvolisSDK.BezelBehavior.REJECT:    return "REJECT (geri al/ret)";
            case EvolisSDK.BezelBehavior.INSERT:    return "INSERT (içeri al)";
            case EvolisSDK.BezelBehavior.DONOTHING: return "DONOTHING (bekle)";
            default:                                return "UNKNOWN (" + v + ")";
        }
    }

    private static boolean hasFlag(String[] args, String flag) {
        for (String a : args) if (a.equals(flag)) return true;
        return false;
    }

    private static String modelName(int model) {
        switch (model) {
            case EvolisSDK.Model.KC_PRIME:     return "KC Prime";
            case EvolisSDK.Model.KC_ESSENTIAL: return "KC Essential";
            case EvolisSDK.Model.KC_MAX:       return "KC Max";
            case EvolisSDK.Model.KC100:        return "KC100";
            case EvolisSDK.Model.KC100B:       return "KC100B";
            case EvolisSDK.Model.KC200:        return "KC200";
            case EvolisSDK.Model.KC200B:       return "KC200B";
            default:                           return "bilinmeyen";
        }
    }
}
