package com.mobiloby;

import java.io.File;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;

/**
 * Uygulama dosyalarının kök dizinini çözer.
 *
 * Neden gerekli: kod `native_x64`, `native_evolis`, `output` gibi yolları
 * göreli kullanıyor. Bunlar çalışma dizinine göre çözülür — `run.bat` ile
 * proje klasöründen çalıştırıldığında sorun yok, ama exe'ye çift
 * tıklandığında çalışma dizini bambaşka bir yer olabilir ve DLL'ler
 * bulunamaz.
 *
 * Çözüm: jpackage ile paketlendiğinde başlatıcı, `jpackage.app-path`
 * sistem özelliğine exe'nin tam yolunu koyar. Kök dizini oradan alırız.
 * Paketlenmemişse (geliştirme) çalışma dizinine düşeriz.
 */
public final class AppPaths {

    private AppPaths() {}

    private static final Path BASE = resolveBase();

    private static Path resolveBase() {
        // jpackage başlatıcısı bunu exe'nin tam yoluna ayarlar
        String appPath = System.getProperty("jpackage.app-path");
        if (appPath != null && !appPath.isBlank()) {
            Path exe = Paths.get(appPath);
            Path parent = exe.getParent();
            if (parent != null) return parent.toAbsolutePath();
        }
        // Geliştirme: proje klasöründen çalıştırılıyor
        return Paths.get("").toAbsolutePath();
    }

    /** Uygulamanın kök dizini — native klasörleri ve çıktılar buna göre. */
    public static Path base() {
        return BASE;
    }

    /** Kök dizine göre bir alt yol. */
    public static Path resolve(String first, String... more) {
        return BASE.resolve(Paths.get(first, more));
    }

    /** Kök dizine göre bir alt yol, mutlak String olarak (JNA için). */
    public static String absolute(String name) {
        return BASE.resolve(name).toAbsolutePath().toString();
    }

    /** Yoksa oluşturur ve döndürür. */
    public static Path ensureDir(String name) {
        Path p = BASE.resolve(name);
        try {
            Files.createDirectories(p);
        } catch (Exception ignore) {
            // Yazma izni yoksa çağıran taraf zaten hata alacak
        }
        return p;
    }

    /** Paketlenmiş exe olarak mı çalışıyoruz? */
    public static boolean isPackaged() {
        String p = System.getProperty("jpackage.app-path");
        return p != null && !p.isBlank();
    }

    /** Tanılama için — hangi kök kullanılıyor. */
    public static String describe() {
        return (isPackaged() ? "paketlenmiş" : "geliştirme") + " kök: " + BASE
                + (new File(BASE.toFile(), "native_x64").isDirectory() ? "" : "  [UYARI: native_x64 yok]");
    }
}
