package com.mobiloby;

import net.sf.scuba.smartcards.CardService;
import org.jmrtd.BACKey;
import org.jmrtd.PassportService;
import org.jmrtd.lds.CardSecurityFile;
import org.jmrtd.lds.DisplayedImageInfo;
import org.jmrtd.lds.SODFile;
import org.jmrtd.lds.icao.COMFile;
import org.jmrtd.lds.icao.DG11File;
import org.jmrtd.lds.icao.DG12File;
import org.jmrtd.lds.icao.DG14File;
import org.jmrtd.lds.icao.DG15File;
import org.jmrtd.lds.icao.DG1File;
import org.jmrtd.lds.icao.DG2File;
import org.jmrtd.lds.icao.DG5File;
import org.jmrtd.lds.icao.DG7File;
import org.jmrtd.lds.icao.MRZInfo;
import org.jmrtd.lds.iso19794.FaceImageInfo;
import org.jmrtd.lds.iso19794.FaceInfo;

import javax.imageio.ImageIO;
import javax.smartcardio.CardTerminal;
import javax.smartcardio.TerminalFactory;
import java.awt.image.BufferedImage;
import java.io.ByteArrayOutputStream;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.util.Comparator;
import java.util.List;
import java.util.stream.Stream;

public class App {

    private static final Path SCAN_DIR = Paths.get("scan_output");
    private static final Path OUT_DIR = AppPaths.resolve("output");

    public static void main(String[] args) {
        try {
            Files.createDirectories(OUT_DIR);

            // 1. BAC değerlerini al — 4 yol var (öncelik sırasıyla):
            //    (a) --mrz docNo,dob,exp  → manuel override (taramayı atla)
            //    (b) --bmp <path>          → o BMP'yi OCR'la (taramayı atla)
            //    (c) --latest              → Img/ klasöründeki en yeni back BMP'sini OCR'la
            //    (d) argsız (default)      → demoyu başlat, yeni tarama yap, OCR
            String overrideMrz = parseArg(args, "--mrz");
            String overrideBmp = parseArg(args, "--bmp");
            boolean useLatest = hasFlag(args, "--latest");
            String docNo, dob, exp;

            if (overrideMrz != null) {
                String[] parts = overrideMrz.split(",");
                if (parts.length != 3) {
                    throw new IllegalArgumentException("--mrz formatı: docNo,dob,exp (örn: A12345678,030505,310209)");
                }
                docNo = parts[0].trim();
                dob = parts[1].trim();
                exp = parts[2].trim();
                System.out.println("Manuel BAC değerleri: docNo=" + docNo + " dob=" + dob + " exp=" + exp + "\n");
            } else {
                System.out.println("=== Tarama (IDSIF.dll built-in OCR) ===");
                ScannerBridge.ScanOutput out = ScannerBridge.scan();
                System.out.println("Üretilen BMP: " + out.backBmp);
                MrzReader.Mrz mrz = MrzReader.parse(out.mrzText);
                System.out.println("MRZ parse: " + mrz);
                System.out.println("  satır 1: " + mrz.rawLine1);
                System.out.println("  satır 2: " + mrz.rawLine2);
                System.out.println("  satır 3: " + mrz.rawLine3);
                System.out.println();
                docNo = mrz.documentNumber;
                dob = mrz.dateOfBirth;
                exp = mrz.dateOfExpiry;
            }

            // 2. Okuyucu bul
            TerminalFactory factory = TerminalFactory.getDefault();
            List<CardTerminal> terminals = factory.terminals().list();
            if (terminals.isEmpty()) {
                System.out.println("Okuyucu bulunamadı!");
                return;
            }
            CardTerminal terminal = terminals.get(0);
            System.out.println("Aktif Okuyucu: " + terminal.getName());

            // NFC pad'de kart bekle (Move sonrası gecikmeli algılanabilir)
            long cardDeadline = System.currentTimeMillis() + 8_000;
            while (System.currentTimeMillis() < cardDeadline) {
                if (terminal.isCardPresent()) break;
                Thread.sleep(400);
            }

            // İlk denemede algılanmadıysa: manuel fallback
            if (!terminal.isCardPresent()) {
                System.out.println();
                System.out.println(">>> Kart NFC pad'inde otomatik algılanmadı.");
                System.out.println(">>> Cihazdan çıkarıp ELLE NFC bölgesine yerleştirin,");
                System.out.println(">>> sonra ENTER (Q=iptal).");
                System.out.print("> ");
                String again = new java.util.Scanner(System.in).nextLine();
                if (again != null && again.trim().equalsIgnoreCase("q")) return;

                // Tekrar 15 sn boyunca polling
                long retryDeadline = System.currentTimeMillis() + 15_000;
                while (System.currentTimeMillis() < retryDeadline) {
                    if (terminal.isCardPresent()) break;
                    Thread.sleep(400);
                }
                if (!terminal.isCardPresent()) {
                    System.out.println("Kart hâlâ algılanmadı. İptal.");
                    return;
                }
            }

            CardService cardService = CardService.getInstance(terminal);
            cardService.open();
            System.out.println("Çipe bağlandık, BAC başlatılıyor...");

            PassportService service = new PassportService(
                    cardService,
                    PassportService.NORMAL_MAX_TRANCEIVE_LENGTH,
                    PassportService.DEFAULT_MAX_BLOCKSIZE,
                    false,
                    false
            );
            service.open();

            // 3. BAC
            BACKey bacKey = new BACKey(docNo, dob, exp);
            service.sendSelectApplet(false);
            service.doBAC(bacKey);
            System.out.println("KAPI AÇILDI! Çipin içine girdik.\n");

            // 4. Tüm DG'ları oku
            readAllDataGroups(service);

            service.close();
            cardService.close();
            System.out.println("\nHer şey tamam — çıktılar: " + OUT_DIR.toAbsolutePath());

        } catch (Exception e) {
            System.err.println("KRİTİK HATA: " + e.getMessage());
            e.printStackTrace();
        }
    }

    private static String parseArg(String[] args, String flag) {
        for (int i = 0; i < args.length - 1; i++) {
            if (args[i].equals(flag)) return args[i + 1];
        }
        return null;
    }

    private static boolean hasFlag(String[] args, String flag) {
        for (String a : args) if (a.equals(flag)) return true;
        return false;
    }

    private static Path findLatestBackBmp(Path dir) throws Exception {
        try (Stream<Path> stream = Files.list(dir)) {
            return stream
                    .filter(p -> p.getFileName().toString().toLowerCase().endsWith("_back.bmp"))
                    .max(Comparator.comparing(p -> p.toFile().lastModified()))
                    .orElseThrow(() -> new IllegalStateException(
                            "Hiç '_back.bmp' bulunamadı: " + dir));
        }
    }

    private static byte[] readAll(InputStream in) throws java.io.IOException {
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        byte[] buf = new byte[8192];
        int n;
        while ((n = in.read(buf)) > 0) out.write(buf, 0, n);
        return out.toByteArray();
    }

    private static void readAllDataGroups(PassportService service) {
        tryRead("COM", () -> {
            try (InputStream in = service.getInputStream(PassportService.EF_COM)) {
                COMFile com = new COMFile(in);
                System.out.println("LDS sürümü: " + com.getLDSVersion());
                System.out.println("Çipteki DG listesi: " + com.getTagList());
            }
        });

        tryRead("DG1 (MRZ)", () -> {
            try (InputStream in = service.getInputStream(PassportService.EF_DG1)) {
                DG1File dg1 = new DG1File(in);
                MRZInfo m = dg1.getMRZInfo();
                System.out.println("  Ad         : " + clean(m.getSecondaryIdentifier()));
                System.out.println("  Soyad      : " + clean(m.getPrimaryIdentifier()));
                System.out.println("  Belge No   : " + m.getDocumentNumber());
                System.out.println("  Belge Tipi : " + m.getDocumentCode());
                System.out.println("  Veren Devlet: " + m.getIssuingState());
                System.out.println("  Uyruk      : " + m.getNationality());
                System.out.println("  Cinsiyet   : " + m.getGender());
                System.out.println("  Doğum      : " + m.getDateOfBirth());
                System.out.println("  Geçerlilik : " + m.getDateOfExpiry());
                System.out.println("  Kişisel No : " + m.getPersonalNumber());
                Files.write(OUT_DIR.resolve("dg1_mrz.txt"),
                        m.toString().replace("\n", System.lineSeparator())
                                .getBytes(StandardCharsets.UTF_8));
            }
        });

        tryRead("DG2 (Biyometrik Fotoğraf)", () -> {
            try (InputStream in = service.getInputStream(PassportService.EF_DG2)) {
                DG2File dg2 = new DG2File(in);
                int idx = 0;
                for (FaceInfo fi : dg2.getFaceInfos()) {
                    for (FaceImageInfo img : fi.getFaceImageInfos()) {
                        idx++;
                        String mime = img.getMimeType();
                        String ext = mime != null && mime.contains("jp2") ? "jp2" : "img";
                        byte[] bytes;
                        try (InputStream is = img.getImageInputStream()) {
                            bytes = readAll(is);
                        }
                        Path jp2 = OUT_DIR.resolve("dg2_face_" + idx + "." + ext);
                        Files.write(jp2, bytes);
                        System.out.println("  Kayıt: " + jp2 + " (" + bytes.length + " byte, "
                                + img.getWidth() + "x" + img.getHeight() + ", " + mime + ")");
                        try {
                            BufferedImage bi = ImageIO.read(new java.io.ByteArrayInputStream(bytes));
                            if (bi != null) {
                                Path png = OUT_DIR.resolve("dg2_face_" + idx + ".png");
                                ImageIO.write(bi, "png", png.toFile());
                                System.out.println("  PNG : " + png);
                            } else {
                                System.out.println("  (PNG'ye çevrilemedi — JP2 plugin yüklü değil olabilir)");
                            }
                        } catch (Exception ex) {
                            System.out.println("  PNG dönüşüm hatası: " + ex.getMessage());
                        }
                    }
                }
            }
        });

        tryRead("DG5 (Portre)", () -> {
            try (InputStream in = service.getInputStream(PassportService.EF_DG5)) {
                DG5File dg5 = new DG5File(in);
                saveDisplayedImages(dg5.getImages(), "dg5_portre");
            }
        });

        tryRead("DG7 (İmza)", () -> {
            try (InputStream in = service.getInputStream(PassportService.EF_DG7)) {
                DG7File dg7 = new DG7File(in);
                saveDisplayedImages(dg7.getImages(), "dg7_imza");
            }
        });

        tryRead("DG11 (Ek Kişisel Bilgi)", () -> {
            try (InputStream in = service.getInputStream(PassportService.EF_DG11)) {
                DG11File dg11 = new DG11File(in);
                System.out.println("  Tam Ad         : " + dg11.getNameOfHolder());
                System.out.println("  Diğer İsimler  : " + dg11.getOtherNames());
                System.out.println("  Kişisel No     : " + dg11.getPersonalNumber());
                System.out.println("  Doğum Tarihi   : " + dg11.getFullDateOfBirth());
                System.out.println("  Doğum Yeri     : " + dg11.getPlaceOfBirth());
                System.out.println("  Kalıcı Adres   : " + dg11.getPermanentAddress());
                System.out.println("  Telefon        : " + dg11.getTelephone());
                System.out.println("  Meslek         : " + dg11.getProfession());
                System.out.println("  Ünvan          : " + dg11.getTitle());
                System.out.println("  Kişisel Özet   : " + dg11.getPersonalSummary());
            }
        });

        tryRead("DG12 (Belge Detayı)", () -> {
            try (InputStream in = service.getInputStream(PassportService.EF_DG12)) {
                DG12File dg12 = new DG12File(in);
                System.out.println("  Veren Kurum    : " + dg12.getIssuingAuthority());
                System.out.println("  Veriliş Tarihi : " + dg12.getDateOfIssue());
                System.out.println("  Veren Kişi(ler): " + dg12.getNamesOfOtherPersons());
            }
        });

        tryRead("DG14 (Güvenlik)", () -> {
            try (InputStream in = service.getInputStream(PassportService.EF_DG14)) {
                DG14File dg14 = new DG14File(in);
                System.out.println("  Security Info sayısı: " + dg14.getSecurityInfos().size());
            }
        });

        tryRead("DG15 (AA Public Key)", () -> {
            try (InputStream in = service.getInputStream(PassportService.EF_DG15)) {
                DG15File dg15 = new DG15File(in);
                System.out.println("  Public Key Algo: " + dg15.getPublicKey().getAlgorithm());
            }
        });

        tryRead("SOD", () -> {
            try (InputStream in = service.getInputStream(PassportService.EF_SOD)) {
                SODFile sod = new SODFile(in);
                System.out.println("  Hash algoritması     : " + sod.getDigestAlgorithm());
                System.out.println("  DG hash sayısı       : " + sod.getDataGroupHashes().size());
            }
        });

        tryRead("EF.CardSecurity", () -> {
            try (InputStream in = service.getInputStream(PassportService.EF_CARD_SECURITY)) {
                CardSecurityFile cs = new CardSecurityFile(in);
                System.out.println("  Security Info sayısı: " + cs.getSecurityInfos().size());
            }
        });
    }

    private static void tryRead(String label, IoTask task) {
        System.out.println("--- " + label + " ---");
        try {
            task.run();
        } catch (Exception e) {
            System.out.println("  [okunamadı] " + e.getClass().getSimpleName() + ": " + e.getMessage());
        }
        System.out.println();
    }

    private static void saveDisplayedImages(List<DisplayedImageInfo> imgs, String prefix) throws Exception {
        int idx = 0;
        for (DisplayedImageInfo img : imgs) {
            idx++;
            byte[] bytes;
            try (InputStream is = img.getImageInputStream()) {
                bytes = readAll(is);
            }
            String mime = img.getMimeType();
            String ext = mime != null && mime.contains("jp2") ? "jp2"
                    : mime != null && mime.contains("png") ? "png"
                    : mime != null && mime.contains("jpeg") ? "jpg"
                    : "img";
            Path p = OUT_DIR.resolve(prefix + "_" + idx + "." + ext);
            Files.write(p, bytes);
            System.out.println("  Kayıt: " + p + " (" + bytes.length + " byte, " + mime + ")");
        }
    }

    private static String clean(String s) {
        return s == null ? "" : s.replace("<", " ").trim();
    }

    @FunctionalInterface
    private interface IoTask {
        void run() throws Exception;
    }
}
