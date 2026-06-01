package com.mobiloby;

import javax.imageio.ImageIO;
import java.awt.Graphics2D;
import java.awt.geom.AffineTransform;
import java.awt.image.AffineTransformOp;
import java.awt.image.BufferedImage;
import java.io.File;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.util.Scanner;

/**
 * IDSIF.dll'i JNA ile çağırıp tarama yapar. Demo'ya hiç ihtiyaç yok.
 * Sırayla: Init(cdecl, savePath+DevDesc) → ENTER → Scan() → back_cp.bmp dosyasını döndür.
 *
 * Önemli: Uninit BİLEREK çağrılmıyor — Init/Uninit cycle USB endpoint'i bozuyor.
 * Program çıkışında OS DLL'i unload eder, kart hardware'i temizler.
 */
public class ScannerBridge {

    private static IDSIF lib;

    /**
     * Tarama yap, üretilen back_cp.bmp'nin yolunu döndür.
     * Kullanıcıdan ENTER bekler (kartı yerleştirsin).
     */
    public static Path scan() throws Exception {
        if (lib == null) {
            System.out.println("Tarayıcı DLL'i yükleniyor...");
            lib = IDSIF.load();
        }

        // SavePath = proje altında scan_output/ — DLL sondaki "." karakteriyle
        // boş BMP üretiyordu; clean absolute path veriyoruz.
        File savedir = new File("scan_output").getCanonicalFile();
        savedir.mkdirs();
        String savePath = savedir.getAbsolutePath();

        IDSIF.DevDesc.ByReference desc = new IDSIF.DevDesc.ByReference();
        desc.ulPid = 0x5710;
        desc.ulVid = 0x0483;
        desc.iFileDesc = 0;

        int initRc = lib.Init(savePath, desc);
        if (initRc != 0) {
            throw new IllegalStateException("IDSIF.Init failed: " + initRc);
        }
        System.out.println("Tarayıcı hazır (savePath=" + savePath + ").");

        // Kullanıcı kartı yerleştirsin
        System.out.println();
        System.out.println("=== KARTI CİHAZA YERLEŞTİRİN, sonra ENTER (Q=iptal) ===");
        System.out.print("> ");
        String input = new Scanner(System.in).nextLine();
        if (input != null && input.trim().equalsIgnoreCase("q")) {
            throw new InterruptedException("Kullanıcı iptal etti.");
        }

        // Scan başlat
        System.out.println("Tarama yapılıyor...");
        long t0 = System.currentTimeMillis();
        int scanRc = lib.Scan();
        long dt = System.currentTimeMillis() - t0;
        if (scanRc != 0) {
            throw new IllegalStateException("IDSIF.Scan failed: " + scanRc + " (" + dt + " ms)");
        }
        System.out.println("Tarama tamam (" + dt + " ms).");

        // back_cp.bmp ham taranan görüntü (2 MB civarı, 648x1050 dikey, ters)
        Path rawBack = Paths.get(savePath, "back_cp.bmp");
        if (!Files.exists(rawBack)) {
            rawBack = Paths.get(savePath, "back.bmp");
            if (!Files.exists(rawBack)) {
                throw new IllegalStateException("Tarama BMP'si bulunamadı: " + savePath);
            }
        }
        System.out.println("Ham BMP: " + rawBack + " (" + Files.size(rawBack) + " byte)");

        // DLL'in ham çıktısı 90° ters + flip → düzeltip OCR-ready hale getir
        Path processed = Paths.get(savePath, "back_processed.bmp");
        rotateAndFlip(rawBack, processed);
        System.out.println("İşlenmiş BMP: " + processed + " (" + Files.size(processed) + " byte)");

        // Kartı RF/NFC pozisyonuna getir ki JMRTD okuyabilsin
        System.out.println("Kart NFC pozisyonuna hareket ediyor (Move=2)...");
        int moveRc = lib.Move(2);
        System.out.println("Move(2) → " + moveRc);
        Thread.sleep(3000); // hareket için süre + Windows PCSC algılaması

        return processed;
    }

    /** Çıkışta kartı dışarı vermek için (Java app sonunda çağrılabilir). */
    public static void eject() {
        if (lib != null) {
            try {
                int r = lib.Move(4);
                System.out.println("Kart eject (Move=4) → " + r);
            } catch (Throwable ignore) {}
        }
    }

    /** rotate(90 CCW) + flip horizontal → MRZ alt kenarda, düz okunabilir hale gelir. */
    private static void rotateAndFlip(Path src, Path dst) throws Exception {
        BufferedImage in = ImageIO.read(src.toFile());
        int w = in.getWidth(), h = in.getHeight();
        BufferedImage out = new BufferedImage(h, w, BufferedImage.TYPE_INT_RGB);
        // Birleşik: orijinal (x,y) → rotate(90 CCW): (y, w-1-x) → flipH: (h-1-y, w-1-x)
        for (int y = 0; y < h; y++) {
            for (int x = 0; x < w; x++) {
                out.setRGB(h - 1 - y, w - 1 - x, in.getRGB(x, y));
            }
        }
        ImageIO.write(out, "bmp", dst.toFile());
    }
}
