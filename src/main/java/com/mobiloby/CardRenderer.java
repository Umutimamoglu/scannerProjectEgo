package com.mobiloby;

import javax.imageio.ImageIO;
import java.awt.BasicStroke;
import java.awt.Color;
import java.awt.Font;
import java.awt.Graphics2D;
import java.awt.RenderingHints;
import java.awt.image.BufferedImage;
import java.io.File;
import java.nio.file.Files;
import java.nio.file.Path;

/**
 * Kart görselini üretir — çipten okunan veriyi basılabilir bir bitmap'e çevirir.
 *
 * ÖNEMLİ — yarım panel ribon kısıtı:
 * Takılı ribon "Color Half YMCKO" (R5H004NAA). Yarım panel ribonlarda renkli
 * (YMC) paneller kartın yalnızca ~1/3'lük bir bandını kaplar; K (siyah) ve
 * O (koruyucu) tam boy. Bu yüzden:
 *   - Fotoğraf TEK renkli öğe olmalı ve kartın uzun ekseninde ~28 mm'yi aşmamalı
 *   - Diğer her şey saf siyah (0,0,0) olmalı ki K paneliyle bassın
 * GShortPanelManagement=AUTO ayarıyla yazıcı renkli bölgeyi kendi bulup
 * paneli oraya konumlandırır.
 *
 * Baskı YAPMAZ — sadece PNG önizleme üretir. Baskı için PrintCard kullanılır.
 */
public class CardRenderer {

    /** ISO CR80 kart ölçüsü (mm). Dikey tasarımda genişlik 54, yükseklik 85.6. */
    public static final double CARD_W_MM = 54.0;
    public static final double CARD_H_MM = 85.6;

    public static final int DPI = 300;

    /** Renkli bandın güvenli üst sınırı (mm) — yarım panel kısıtı. */
    static final double COLOR_BAND_MAX_MM = 28.0;

    // === Yerleşim (mm) ===

    static double MARGIN_MM = 4.0;

    /** Başlık. */
    static double TITLE_Y_MM = 9.0;
    static double TITLE_FONT_MM = 4.2;
    static double SUBTITLE_Y_MM = 14.0;
    static double SUBTITLE_FONT_MM = 2.8;

    /** Fotoğraf kutusu — renkli banda sığmalı. */
    static double PHOTO_W_MM = 20.0;
    static double PHOTO_H_MM = 26.0;
    static double PHOTO_Y_MM = 19.0;

    /** Bilgi alanları. */
    static double FIELD_START_Y_MM = 50.0;
    static double FIELD_STEP_MM = 6.5;
    static double LABEL_FONT_MM = 2.3;
    static double VALUE_FONT_MM = 3.0;

    /** Karta basılacak veriler. */
    public static class CardData {
        public String title = "BAŞKENT KART";
        public String subtitle = "ULAŞIM";
        public String name = "";
        public String surname = "";
        public String idNumber = "";
        public String birthDate = "";
        public String expiryDate = "";
        public Path photo;
    }

    static int mmToPx(double mm) {
        return (int) Math.round(mm / 25.4 * DPI);
    }

    /** YYMMDD → GG.AA.YYYY */
    static String formatDate(String yymmdd) {
        if (yymmdd == null || yymmdd.length() != 6) return "";
        int yy = Integer.parseInt(yymmdd.substring(0, 2));
        String year = (yy <= 40 ? "20" : "19") + yymmdd.substring(0, 2);
        return yymmdd.substring(4, 6) + "." + yymmdd.substring(2, 4) + "." + year;
    }

    public static BufferedImage render(CardData d) throws Exception {
        int w = mmToPx(CARD_W_MM);
        int h = mmToPx(CARD_H_MM);

        BufferedImage img = new BufferedImage(w, h, BufferedImage.TYPE_INT_RGB);
        Graphics2D g = img.createGraphics();
        g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
        g.setRenderingHint(RenderingHints.KEY_RENDERING, RenderingHints.VALUE_RENDER_QUALITY);
        g.setRenderingHint(RenderingHints.KEY_INTERPOLATION, RenderingHints.VALUE_INTERPOLATION_BICUBIC);
        g.setRenderingHint(RenderingHints.KEY_TEXT_ANTIALIASING, RenderingHints.VALUE_TEXT_ANTIALIAS_ON);

        // Beyaz zemin — yazıcı beyaz alanlara mürekkep basmaz
        g.setColor(Color.WHITE);
        g.fillRect(0, 0, w, h);

        g.setColor(Color.BLACK);

        // Başlık
        g.setFont(new Font("Arial", Font.BOLD, mmToPx(TITLE_FONT_MM)));
        drawCentered(g, d.title, w, mmToPx(TITLE_Y_MM));
        g.setFont(new Font("Arial", Font.PLAIN, mmToPx(SUBTITLE_FONT_MM)));
        drawCentered(g, d.subtitle, w, mmToPx(SUBTITLE_Y_MM));

        // Fotoğraf — kartın TEK renkli öğesi
        int pw = mmToPx(PHOTO_W_MM);
        int ph = mmToPx(PHOTO_H_MM);
        int px = (w - pw) / 2;
        int py = mmToPx(PHOTO_Y_MM);

        if (d.photo != null && Files.exists(d.photo)) {
            BufferedImage photo = ImageIO.read(d.photo.toFile());
            if (photo != null) {
                double scale = Math.max((double) pw / photo.getWidth(),
                                        (double) ph / photo.getHeight());
                int dw = (int) Math.round(photo.getWidth() * scale);
                int dh = (int) Math.round(photo.getHeight() * scale);
                java.awt.Shape clip = g.getClip();
                g.setClip(px, py, pw, ph);
                g.drawImage(photo, px + (pw - dw) / 2, py + (ph - dh) / 2, dw, dh, null);
                g.setClip(clip);
            }
        }
        // Fotoğraf çerçevesi (siyah)
        g.setStroke(new BasicStroke(Math.max(1, mmToPx(0.25))));
        g.setColor(Color.BLACK);
        g.drawRect(px, py, pw, ph);

        // Bilgi alanları
        double y = FIELD_START_Y_MM;
        y = drawField(g, "Adı", d.name, y, w);
        y = drawField(g, "Soyadı", d.surname, y, w);
        if (!d.idNumber.isBlank())   y = drawField(g, "T.C. Kimlik No", d.idNumber, y, w);
        if (!d.birthDate.isBlank())  y = drawField(g, "Doğum Tarihi", d.birthDate, y, w);
        if (!d.expiryDate.isBlank()) drawField(g, "Geçerlilik", d.expiryDate, y, w);

        g.dispose();
        return img;
    }

    private static void drawCentered(Graphics2D g, String text, int w, int baselineY) {
        if (text == null || text.isBlank()) return;
        int tw = g.getFontMetrics().stringWidth(text);
        g.drawString(text, (w - tw) / 2, baselineY);
    }

    private static double drawField(Graphics2D g, String label, String value, double yMm, int w) {
        if (value == null || value.isBlank()) return yMm;
        int x = mmToPx(MARGIN_MM);
        g.setColor(Color.BLACK);
        g.setFont(new Font("Arial", Font.PLAIN, mmToPx(LABEL_FONT_MM)));
        g.drawString(label + ":", x, mmToPx(yMm));
        g.setFont(new Font("Arial", Font.BOLD, mmToPx(VALUE_FONT_MM)));
        g.drawString(value, x, mmToPx(yMm + 3.4));
        return yMm + FIELD_STEP_MM;
    }

    /**
     * Önizleme üret — baskı YAPMAZ.
     *
     * Kullanım:
     *   run.bat card                          → output/ verisinden üret
     *   run.bat card --ad UMUT --soyad İMAMOĞLU  → Türkçe isimleri elle ver
     *
     * MRZ sadece ASCII taşır (İ, Ğ, Ş yok). Doğru Türkçe yazım için
     * --ad / --soyad ile elle verin veya çipten DG11 okuyun.
     */
    public static void main(String[] args) throws Exception {
        Path outDir = Path.of("output");
        Files.createDirectories(outDir);

        CardData d = new CardData();

        // MRZ'den doldur
        Path mrzFile = outDir.resolve("dg1_mrz.txt");
        if (Files.exists(mrzFile)) {
            try {
                MrzReader.Mrz m = MrzReader.parse(Files.readString(mrzFile));
                d.name = m.secondaryName;
                d.surname = m.primaryName;
                d.birthDate = formatDate(m.dateOfBirth);
                d.expiryDate = formatDate(m.dateOfExpiry);
                // TD1 satır 1'in opsiyonel alanında T.C. kimlik no bulunur
                String opt = m.rawLine1.replace("<", "");
                int i = opt.length() - 11;
                if (i > 0 && opt.substring(i).matches("\\d{11}")) d.idNumber = opt.substring(i);
            } catch (Exception e) {
                System.out.println("MRZ ayrıştırılamadı: " + e.getMessage());
            }
        }

        // Elle override — Türkçe karakterler için
        String ad = arg(args, "--ad");
        String soyad = arg(args, "--soyad");
        String baslik = arg(args, "--baslik");
        String altbaslik = arg(args, "--altbaslik");
        if (ad != null) d.name = ad;
        if (soyad != null) d.surname = soyad;
        if (baslik != null) d.title = baslik;
        if (altbaslik != null) d.subtitle = altbaslik;

        if (d.name.isBlank()) d.name = "AD";
        if (d.surname.isBlank()) d.surname = "SOYAD";

        Path photo = outDir.resolve("dg2_face_1.png");
        if (Files.exists(photo)) d.photo = photo;

        System.out.println("Başlık     : " + d.title + " / " + d.subtitle);
        System.out.println("Ad         : " + d.name);
        System.out.println("Soyad      : " + d.surname);
        System.out.println("T.C. No    : " + (d.idNumber.isBlank() ? "(yok)" : d.idNumber));
        System.out.println("Doğum      : " + d.birthDate);
        System.out.println("Geçerlilik : " + d.expiryDate);
        System.out.println("Fotoğraf   : " + (d.photo != null ? d.photo : "(yok)"));

        if (PHOTO_H_MM > COLOR_BAND_MAX_MM) {
            System.out.println();
            System.out.println("UYARI: fotoğraf yüksekliği (" + PHOTO_H_MM + " mm) yarım panel"
                    + " renkli bant sınırını (" + COLOR_BAND_MAX_MM + " mm) aşıyor.");
        }

        BufferedImage img = render(d);
        File png = outDir.resolve("card_preview.png").toFile();
        ImageIO.write(img, "png", png);

        // Baskı için BMP — SDK bitmap bekliyor
        File bmp = outDir.resolve("card_print.bmp").toFile();
        ImageIO.write(img, "bmp", bmp);

        System.out.println();
        System.out.println("Önizleme : " + png + "  "
                + img.getWidth() + "x" + img.getHeight() + " px @" + DPI + "dpi");
        System.out.println("Baskı BMP: " + bmp);
        System.out.println();
        System.out.println("Baskı YAPILMADI — önce önizlemeyi kontrol edin.");
    }

    private static String arg(String[] args, String flag) {
        for (int i = 0; i < args.length - 1; i++) {
            if (args[i].equals(flag)) return args[i + 1];
        }
        return null;
    }
}
