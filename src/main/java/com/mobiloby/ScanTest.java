package com.mobiloby;

import com.sun.jna.ptr.IntByReference;
import java.nio.file.Files;
import java.nio.file.Paths;
import java.util.Arrays;

/**
 * IDSIF tarama akışı: Init → OpenDev → WaitCardIn → Scan → ReadPic(front/back) → Uninit.
 * Cihaz kartı içeri yutuyor, iki yüzü taradıktan sonra dışarı verir.
 *
 * Kullanım:
 *   1) Komutu çalıştır
 *   2) "Kart bekleniyor..." mesajını görünce kartı slot ağzına koy
 *   3) Cihaz kartı yutup tarayacak, sonra geri verecek
 */
public class ScanTest {
    public static void main(String[] args) {
        IDSIF lib = null;
        try {
            System.out.println("DLL yükleniyor...");
            lib = IDSIF.load();
            System.out.println("IDSIF bağlandı.\n");

            // CDECL ile parametreli Init dene
            com.sun.jna.Pointer entryTab = lib.Dll_IDS_Entry();
            System.out.println("Dll_IDS_Entry() → ST_IDS_FUN_TAB* = " + entryTab);

            IDSIF.DevDesc.ByReference desc = new IDSIF.DevDesc.ByReference();
            desc.ulPid = 0x5710;
            desc.ulVid = 0x0483;
            desc.iFileDesc = 0;
            String savePath = "C:\\Users\\Mobiloby\\Desktop\\Yeni klasör\\CRT-603-7005-001 DEMO\\CRT-603-7005-001 DEMO";

            int r = lib.Init(savePath, desc);
            System.out.println("Init(\"" + savePath + "\", Vid=0x0483/Pid=0x5710) → " + r);
            if (r != 0) { System.err.println("Init başarısız"); return; }

            try {
                int gd = lib.GetDevInfo();
                System.out.println("GetDevInfo()  → " + gd);
            } catch (Throwable t) {
                System.out.println("GetDevInfo() HATA: " + t);
            }

            // Buzzer'ı aç ki kart yutarken ses duyalım
            try { lib.SetBuzzer(1); } catch (Throwable ignore) {}

            // Polling ile kart algılamasını dinle (WaitCardIn -48 = timeout, kullanılmıyor)
            // CardStatus / Test fonksiyonlarından dönen değer kart varlığında değişebilir
            System.out.println("\n=== Kartı cihaza yerleştirip ENTER'a basın (çıkmak için Q+ENTER) ===");
            System.out.print("> ");
            String input = new java.util.Scanner(System.in).nextLine();
            if (input != null && input.trim().equalsIgnoreCase("q")) {
                System.out.println("İptal.");
                return;
            }

            // CardStatus ve diğerlerini yeniden oku — kart yerinde mi?
            try { System.out.println("CardStatus (yerleştirme sonrası) → " + lib.CardStatus()); } catch (Throwable t) {}
            try { System.out.println("Test (yerleştirme sonrası)       → " + lib.Test()); } catch (Throwable t) {}

            // Img klasöründeki snapshot al (Scan öncesi)
            java.io.File imgDir = new java.io.File("Img");
            imgDir.mkdirs();
            java.util.Set<String> before = new java.util.HashSet<>();
            for (java.io.File f : imgDir.listFiles()) before.add(f.getName());
            System.out.println("Img/ içinde Scan ÖNCESİ " + before.size() + " dosya var.");

            System.out.println("\n=== Scan() tetikleniyor ===");
            long t0 = System.currentTimeMillis();
            int sc = lib.Scan();
            long dt = System.currentTimeMillis() - t0;
            System.out.println("Scan() → " + sc + " (süre: " + dt + " ms)");

            // 5 saniye boyunca Img/ klasörüne düşen yeni dosyaları izle
            System.out.println("\nImg/ klasörü 5 saniye izleniyor (otomatik kayıt var mı?)...");
            long deadline = System.currentTimeMillis() + 5000;
            java.util.List<java.io.File> news = new java.util.ArrayList<>();
            while (System.currentTimeMillis() < deadline) {
                for (java.io.File f : imgDir.listFiles()) {
                    if (!before.contains(f.getName()) && !news.contains(f)) {
                        news.add(f);
                        System.out.println("  YENİ: " + f.getName() + " (" + f.length() + " byte)");
                    }
                }
                try { Thread.sleep(200); } catch (InterruptedException e) { break; }
            }
            if (news.isEmpty()) {
                System.out.println("  (hiç yeni dosya düşmedi)");
            } else {
                System.out.println("Toplam " + news.size() + " yeni dosya oluştu.");
            }

        } catch (Throwable t) {
            System.err.println("BOMBA: " + t);
            t.printStackTrace();
        } finally {
            // BİLEREK Uninit YAPMIYORUZ — DLL state'i bozulmasın diye.
            // Programdan çıkışta OS DLL'i unload edecek; resource leak normal.
            System.out.println("[Uninit atlandı — kararlılık testi]");
        }
    }

    private static String sniffExt(byte[] d) {
        if (d.length < 4) return "bin";
        // BMP: 'BM'
        if (d[0] == 0x42 && d[1] == 0x4D) return "bmp";
        // JPEG: FF D8 FF
        if ((d[0] & 0xFF) == 0xFF && (d[1] & 0xFF) == 0xD8 && (d[2] & 0xFF) == 0xFF) return "jpg";
        // PNG: 89 50 4E 47
        if ((d[0] & 0xFF) == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47) return "png";
        return "bin";
    }
}
