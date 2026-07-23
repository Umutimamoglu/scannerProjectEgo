package com.mobiloby;

import com.sun.jna.ptr.IntByReference;
import net.sf.scuba.smartcards.CardService;
import org.jmrtd.BACKey;
import org.jmrtd.PassportService;
import org.jmrtd.lds.icao.DG11File;
import org.jmrtd.lds.icao.DG12File;
import org.jmrtd.lds.icao.DG1File;
import org.jmrtd.lds.icao.DG2File;
import org.jmrtd.lds.icao.MRZInfo;
import org.jmrtd.lds.iso19794.FaceImageInfo;
import org.jmrtd.lds.iso19794.FaceInfo;

import javax.imageio.ImageIO;
import javax.smartcardio.CardTerminal;
import javax.smartcardio.TerminalFactory;
import java.awt.image.BufferedImage;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.nio.file.Path;
import java.nio.file.Paths;
import java.util.List;
import java.util.function.Consumer;

/**
 * Kimlik okuma servisi — tarayıcı kontrolü + çip okuma.
 *
 * App.java'daki mantığın konsoldan bağımsız hali: hiçbir yerde stdin
 * beklemez, ilerlemeyi callback ile bildirir. Böylece UI'dan
 * (SwingWorker içinden) çağrılabilir.
 *
 * Türkçe isimler DG11'den alınır — MRZ yalnızca ASCII taşır, İ/Ğ/Ş yoktur.
 */
public class IdCardReader implements AutoCloseable {

    /** Kart durumu (IDSIF CardStatus dönüşü). */
    public interface CardStatus {
        int NONE = 0;      // Cihazda kart yok
        int MOVING = 1;    // Hareket halinde
        int FRONT = 2;     // Ön pozisyonda
        int INSIDE = 3;    // İçeride
        int BACK = 4;      // Arka pozisyonda
    }

    /** Çipten ve MRZ'den okunan kimlik verisi. */
    public static class IdData {
        public String name = "";
        public String surname = "";
        public String tcNo = "";
        public String documentNumber = "";
        public String birthDate = "";        // GG.AA.YYYY
        public String birthPlace = "";
        public String gender = "";
        public String nationality = "";
        public String issueDate = "";
        public String expiryDate = "";       // GG.AA.YYYY
        public String issuingAuthority = "";
        public BufferedImage photo;
        public String mrzLine1 = "", mrzLine2 = "", mrzLine3 = "";

        /** BAC için ham MRZ değerleri (YYMMDD). */
        public String bacDocNo = "", bacBirth = "", bacExpiry = "";
    }

    /** Tarama sonucu — MRZ metni ve üretilen görüntüler. */
    public static class ScanResult {
        public String mrzText = "";
        public Path frontBmp;
        public Path backBmp;
        public MrzReader.Mrz mrz;
    }

    private static final Path OUT_DIR = Paths.get("output");

    private IDSIF lib;
    private boolean deviceOpen;
    private Consumer<String> logger = s -> {};

    /** İlerleme/log mesajlarını nereye yazacağını belirle. */
    public void setLogger(Consumer<String> logger) {
        if (logger != null) this.logger = logger;
    }

    private void log(String msg) { logger.accept(msg); }

    // === Tarayıcı kontrolü ===

    /** Tarayıcı DLL'ini yükle ve cihazı aç. */
    public synchronized boolean openDevice() {
        if (deviceOpen) return true;
        try {
            if (lib == null) {
                log("Tarayıcı DLL'i yükleniyor...");
                lib = IDSIF.load();
            }
            String savePath = new File("scan_output").getAbsolutePath();
            new File(savePath).mkdirs();
            int rc = lib.OpenDev(savePath);
            if (rc != IDSIF.Ret.OK) {
                log("Cihaz açılamadı (OpenDev=" + rc + ")"
                        + (rc == IDSIF.Ret.ERR_NODEVICE ? " — cihaz bulunamadı, USB/sürücü kontrol edin" : ""));
                return false;
            }
            deviceOpen = true;
            log("Tarayıcı hazır.");
            return true;
        } catch (Throwable e) {
            log("Tarayıcı açma hatası: " + e.getMessage());
            return false;
        }
    }

    public boolean isDeviceOpen() { return deviceOpen; }

    /** Cihazdaki kartın durumu (CardStatus.*), okunamazsa -1. */
    public int getCardStatus() {
        if (!deviceOpen) return -1;
        try {
            IntByReference st = new IntByReference();
            int rc = lib.CardStatus(st);
            return rc == IDSIF.Ret.OK ? st.getValue() : -1;
        } catch (Throwable e) {
            return -1;
        }
    }

    /** Tarayıcının firmware/seri bilgisi (1=seri, 2=firmware, 3=SDK). */
    public String getVersionInfo(int type) {
        if (!deviceOpen) return "";
        try {
            byte[] buf = new byte[256];
            if (lib.GetVersionInfo(type, buf) != IDSIF.Ret.OK) return "";
            int n = 0;
            while (n < buf.length && buf[n] != 0) n++;
            return new String(buf, 0, n, StandardCharsets.UTF_8).trim();
        } catch (Throwable e) {
            return "";
        }
    }

    /**
     * Kartı tara ve MRZ'yi DLL'in dahili OCR'ı ile çıkar.
     * Kart cihazda olmalı — çağırmadan önce getCardStatus() kontrol edin.
     */
    public ScanResult scan() throws Exception {
        if (!deviceOpen) throw new IllegalStateException("Tarayıcı açık değil");

        IDSIF.ScanConf conf = new IDSIF.ScanConf();
        conf.type = IDSIF.CardType.ID_CN;
        conf.mode = IDSIF.ScanMode.COLOR;
        conf.side = IDSIF.ScanSide.DUPLEX;
        conf.dpi = IDSIF.ScanDpi.D300;
        conf.front.lBrightness = 50; conf.front.lContrast = 50; conf.front.lGamma = 50;
        conf.back.lBrightness = 50;  conf.back.lContrast = 50;  conf.back.lGamma = 50;

        IDSIF.ScanResult.ByReference res = new IDSIF.ScanResult.ByReference();
        byte[] mrzBuf = new byte[256];
        IntByReference mrzLen = new IntByReference(mrzBuf.length);

        log("Taranıyor ve MRZ okunuyor...");
        long t0 = System.currentTimeMillis();
        int rc = lib.ScanMRZ(conf, res, mrzBuf, mrzLen);
        long dt = System.currentTimeMillis() - t0;

        if (rc != IDSIF.Ret.OK && rc != IDSIF.Ret.OK_BACK) {
            throw new IllegalStateException("Tarama başarısız (ScanMRZ=" + rc + ")");
        }

        ScanResult out = new ScanResult();
        out.mrzText = new String(mrzBuf, 0, Math.max(0, mrzLen.getValue()),
                StandardCharsets.US_ASCII).trim();
        out.frontBmp = Paths.get(res.getFrontPath());
        out.backBmp = Paths.get(res.getBackPath());
        out.mrz = MrzReader.parse(out.mrzText);

        log("Tarama tamam (" + dt + " ms). Belge no: " + out.mrz.documentNumber);
        return out;
    }

    /** Kartı NFC okuma pozisyonuna taşı. */
    public void moveToNfc() {
        if (!deviceOpen) return;
        log("Kart NFC pozisyonuna taşınıyor...");
        lib.Move(IDSIF.Pos.EJECT_HALF);
        sleep(1500);
    }

    /** Kartı dışarı çıkar. */
    public void ejectCard() {
        if (!deviceOpen) return;
        log("Kart çıkarılıyor...");
        lib.Move(IDSIF.Pos.OUT);
    }

    /** Kartı belirtilen pozisyona taşı (IDSIF.Pos.*). */
    public void move(int pos) {
        if (deviceOpen) lib.Move(pos);
    }

    public void setBuzzer(boolean on) {
        if (deviceOpen) {
            try { lib.SetBuzzer(on ? 1 : 0); } catch (Throwable ignore) {}
        }
    }

    @Override
    public synchronized void close() {
        if (lib != null && deviceOpen) {
            try { lib.Uninit(); } catch (Throwable ignore) {}
        }
        deviceOpen = false;
        lib = null;
    }

    // === Çip okuma ===

    /**
     * BAC ile çipe bağlan ve kimlik verilerini oku.
     *
     * @param docNo belge no, dob doğum YYMMDD, exp geçerlilik YYMMDD
     * @param waitMs NFC alanında kart için beklenecek süre
     */
    public IdData readChip(String docNo, String dob, String exp, int waitMs) throws Exception {
        TerminalFactory factory = TerminalFactory.getDefault();
        List<CardTerminal> terminals = factory.terminals().list();
        if (terminals.isEmpty()) {
            throw new IllegalStateException("PC/SC okuyucu bulunamadı");
        }
        CardTerminal terminal = terminals.get(0);
        log("Okuyucu: " + terminal.getName());

        long deadline = System.currentTimeMillis() + waitMs;
        while (System.currentTimeMillis() < deadline && !terminal.isCardPresent()) {
            sleep(300);
        }
        if (!terminal.isCardPresent()) {
            throw new IllegalStateException("NFC alanında kart algılanmadı");
        }

        CardService cardService = CardService.getInstance(terminal);
        cardService.open();
        PassportService service = new PassportService(
                cardService,
                PassportService.NORMAL_MAX_TRANCEIVE_LENGTH,
                PassportService.DEFAULT_MAX_BLOCKSIZE,
                false, false);
        service.open();

        try {
            log("BAC başlatılıyor...");
            service.sendSelectApplet(false);
            service.doBAC(new BACKey(docNo, dob, exp));
            log("Çipe erişim açıldı.");

            IdData d = new IdData();
            d.bacDocNo = docNo; d.bacBirth = dob; d.bacExpiry = exp;

            readDg1(service, d);
            readDg2(service, d);
            readDg11(service, d);   // DG1'in üzerine yazar — Türkçe karakterler burada
            readDg12(service, d);

            return d;
        } finally {
            try { service.close(); } catch (Throwable ignore) {}
            try { cardService.close(); } catch (Throwable ignore) {}
        }
    }

    private void readDg1(PassportService service, IdData d) {
        try (InputStream in = service.getInputStream(PassportService.EF_DG1)) {
            MRZInfo m = new DG1File(in).getMRZInfo();
            d.name = clean(m.getSecondaryIdentifier());
            d.surname = clean(m.getPrimaryIdentifier());
            d.documentNumber = m.getDocumentNumber();
            d.nationality = m.getNationality();
            d.gender = "MALE".equalsIgnoreCase(String.valueOf(m.getGender())) ? "E"
                     : "FEMALE".equalsIgnoreCase(String.valueOf(m.getGender())) ? "K"
                     : String.valueOf(m.getGender());
            d.birthDate = formatDate(m.getDateOfBirth());
            d.expiryDate = formatDate(m.getDateOfExpiry());
            String pn = m.getPersonalNumber();
            if (pn != null) d.tcNo = pn.replace("<", "").trim();
            log("DG1 okundu.");
        } catch (Exception e) {
            log("DG1 okunamadı: " + e.getMessage());
        }
    }

    private void readDg2(PassportService service, IdData d) {
        try (InputStream in = service.getInputStream(PassportService.EF_DG2)) {
            DG2File dg2 = new DG2File(in);
            for (FaceInfo fi : dg2.getFaceInfos()) {
                for (FaceImageInfo img : fi.getFaceImageInfos()) {
                    byte[] bytes;
                    try (InputStream is = img.getImageInputStream()) {
                        bytes = readAll(is);
                    }
                    BufferedImage bi = ImageIO.read(new java.io.ByteArrayInputStream(bytes));
                    if (bi != null) {
                        d.photo = bi;
                        java.nio.file.Files.createDirectories(OUT_DIR);
                        ImageIO.write(bi, "png", OUT_DIR.resolve("dg2_face_1.png").toFile());
                        log("DG2 fotoğraf okundu (" + bi.getWidth() + "x" + bi.getHeight() + ").");
                        return;
                    }
                }
            }
            log("DG2 okundu ama görüntü çözülemedi (JP2 plugin?).");
        } catch (Exception e) {
            log("DG2 okunamadı: " + e.getMessage());
        }
    }

    private void readDg11(PassportService service, IdData d) {
        try (InputStream in = service.getInputStream(PassportService.EF_DG11)) {
            DG11File dg11 = new DG11File(in);

            // Tam ad "SOYAD<<AD" biçiminde — Türkçe karakterlerle
            String full = dg11.getNameOfHolder();
            if (full != null && !full.isBlank()) {
                int sep = full.indexOf("<<");
                if (sep > 0) {
                    d.surname = clean(full.substring(0, sep));
                    d.name = clean(full.substring(sep + 2));
                } else {
                    d.surname = clean(full);
                }
            }
            if (dg11.getPersonalNumber() != null && !dg11.getPersonalNumber().isBlank()) {
                d.tcNo = dg11.getPersonalNumber().trim();
            }
            List<String> places = dg11.getPlaceOfBirth();
            if (places != null && !places.isEmpty()) {
                d.birthPlace = String.join(" ", places).replace("<", " ").trim();
            }
            String fullDob = dg11.getFullDateOfBirth();   // YYYYMMDD
            if (fullDob != null && fullDob.length() == 8) {
                d.birthDate = fullDob.substring(6, 8) + "." + fullDob.substring(4, 6)
                        + "." + fullDob.substring(0, 4);
            }
            log("DG11 okundu (Türkçe isimler).");
        } catch (Exception e) {
            log("DG11 okunamadı: " + e.getMessage());
        }
    }

    private void readDg12(PassportService service, IdData d) {
        try (InputStream in = service.getInputStream(PassportService.EF_DG12)) {
            DG12File dg12 = new DG12File(in);
            if (dg12.getIssuingAuthority() != null) {
                d.issuingAuthority = dg12.getIssuingAuthority().trim();
            }
            String issue = dg12.getDateOfIssue();          // YYYYMMDD
            if (issue != null && issue.length() == 8) {
                d.issueDate = issue.substring(6, 8) + "." + issue.substring(4, 6)
                        + "." + issue.substring(0, 4);
            }
            log("DG12 okundu.");
        } catch (Exception e) {
            log("DG12 okunamadı: " + e.getMessage());
        }
    }

    // === Yardımcılar ===

    /** YYMMDD → GG.AA.YYYY */
    static String formatDate(String yymmdd) {
        if (yymmdd == null || yymmdd.length() != 6) return "";
        int yy = Integer.parseInt(yymmdd.substring(0, 2));
        String year = (yy <= 40 ? "20" : "19") + yymmdd.substring(0, 2);
        return yymmdd.substring(4, 6) + "." + yymmdd.substring(2, 4) + "." + year;
    }

    private static String clean(String s) {
        return s == null ? "" : s.replace("<", " ").trim();
    }

    private static byte[] readAll(InputStream in) throws java.io.IOException {
        ByteArrayOutputStream out = new ByteArrayOutputStream();
        byte[] buf = new byte[8192];
        int n;
        while ((n = in.read(buf)) > 0) out.write(buf, 0, n);
        return out.toByteArray();
    }

    private static void sleep(long ms) {
        try { Thread.sleep(ms); } catch (InterruptedException e) { Thread.currentThread().interrupt(); }
    }
}
