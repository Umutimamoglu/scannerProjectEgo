package com.mobiloby;

import com.sun.jna.ptr.IntByReference;

import java.io.File;
import java.nio.charset.StandardCharsets;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.util.Scanner;

/**
 * Yeni SDK (20260520) ile JNA üzerinden IDSIF.dll kullanımı.
 * Pipeline: OpenDev → ScanMRZ (tek seferde tara+OCR) → Move(EJECT_HALF) → DG oku.
 */
public class ScannerBridge {

    private static IDSIF lib;

    public static class ScanOutput {
        public final Path backBmp;
        public final Path frontBmp;
        public final String mrzText;     // DLL'in built-in OCR'ından
        public final byte cardDir;       // Kart yönü ipucu (rotate gerekiyor mu)

        public ScanOutput(Path back, Path front, String mrz, byte dir) {
            this.backBmp = back;
            this.frontBmp = front;
            this.mrzText = mrz;
            this.cardDir = dir;
        }
    }

    /**
     * Tara, MRZ'yi DLL'in built-in OCR'ı ile çıkar, sonra kartı NFC pozisyonuna taşı.
     */
    public static ScanOutput scan() throws Exception {
        if (lib == null) {
            System.out.println("Tarayıcı DLL'i yükleniyor (64-bit)...");
            lib = IDSIF.load();
        }

        String savePath = new File("scan_output").getAbsolutePath();
        new File(savePath).mkdirs();

        int initRc = lib.OpenDev(savePath);
        if (initRc != IDSIF.Ret.OK) {
            throw new IllegalStateException("OpenDev failed: " + initRc);
        }
        System.out.println("Cihaz açıldı (savePath=" + savePath + ").");

        System.out.println();
        System.out.println("=== KARTI CİHAZA YERLEŞTİRİN, sonra ENTER (Q=iptal) ===");
        System.out.print("> ");
        String input = new Scanner(System.in).nextLine();
        if (input != null && input.trim().equalsIgnoreCase("q")) {
            throw new InterruptedException("Kullanıcı iptal etti.");
        }

        // Tarama yapılandırması: T.C. Kimlik Kartı için duplex renkli 300 DPI
        IDSIF.ScanConf conf = new IDSIF.ScanConf();
        conf.type = IDSIF.CardType.ID_CN;       // 2 = ID kart (T.C. dahil)
        conf.mode = IDSIF.ScanMode.COLOR;       // 0
        conf.side = IDSIF.ScanSide.DUPLEX;      // 0x03 (ön+arka)
        conf.dpi  = IDSIF.ScanDpi.D300;         // 300
        conf.front.lBrightness = 50;
        conf.front.lContrast = 50;
        conf.front.lGamma = 50;
        conf.back.lBrightness = 50;
        conf.back.lContrast = 50;
        conf.back.lGamma = 50;

        IDSIF.ScanResult.ByReference result = new IDSIF.ScanResult.ByReference();
        byte[] mrzBuf = new byte[256];
        IntByReference mrzLen = new IntByReference(mrzBuf.length);

        System.out.println("Tarama + MRZ tanıma başladı...");
        long t0 = System.currentTimeMillis();
        int rc = lib.ScanMRZ(conf, result, mrzBuf, mrzLen);
        long dt = System.currentTimeMillis() - t0;
        System.out.println("ScanMRZ → " + rc + " (" + dt + " ms)");

        if (rc != IDSIF.Ret.OK && rc != IDSIF.Ret.OK_BACK) {
            throw new IllegalStateException("ScanMRZ failed: " + rc);
        }

        String mrz = new String(mrzBuf, 0, Math.max(0, mrzLen.getValue()),
                StandardCharsets.US_ASCII).trim();
        System.out.println("MRZ ham metin:");
        for (String line : mrz.split("\\r?\\n")) System.out.println("  " + line);

        Path backBmp = Paths.get(result.getBackPath());
        Path frontBmp = Paths.get(result.getFrontPath());
        System.out.println("BMP'ler: " + backBmp + " | " + frontBmp);
        System.out.println("Kart yönü (ucCardDir): " + (result.ucCardDir & 0xFF));

        // Kartı NFC pozisyonuna taşı
        System.out.println("Kart NFC pozisyonuna taşınıyor (Move=EJECT_HALF=4)...");
        int moveRc = lib.Move(IDSIF.Pos.EJECT_HALF);
        System.out.println("Move → " + moveRc);
        Thread.sleep(1500);

        return new ScanOutput(backBmp, frontBmp, mrz, result.ucCardDir);
    }

    /** Kartı dışarı çıkar (Move=OUT). */
    public static void eject() {
        if (lib != null) {
            try {
                int r = lib.Move(IDSIF.Pos.OUT);
                System.out.println("Kart eject (Move=OUT) → " + r);
            } catch (Throwable ignore) {}
        }
    }

    /** Cihaz uzantısını kapat. */
    public static void close() {
        if (lib != null) {
            try { lib.Uninit(); } catch (Throwable ignore) {}
            lib = null;
        }
    }
}
