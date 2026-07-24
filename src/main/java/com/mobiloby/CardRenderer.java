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
 *   - Fotoğraf TEK renkli öğe olmalı ve BAND_START_MM..BAND_END_MM arasında durmalı
 *   - Diğer her şey saf siyah (0,0,0) olmalı ki K paneliyle bassın
 * Bandın yeri kalibrasyon kartıyla ölçüldü ve yazılımla oynatılamıyor
 * (bkz. BAND_START_MM). Bu yüzden fotoğraf kartın alt yarısında.
 *
 * Baskı YAPMAZ — sadece PNG önizleme üretir. Baskı için PrintCard kullanılır.
 */
public class CardRenderer {

    /** ISO CR80 kart ölçüsü (mm). Dikey tasarımda genişlik 54, yükseklik 85.6. */
    public static final double CARD_W_MM = 54.0;
    public static final double CARD_H_MM = 85.6;

    public static final int DPI = 300;

    /**
     * Renkli bandın kart üzerindeki GERÇEK konumu (mm) — kalibrasyon kartıyla ölçüldü.
     *
     * Yarım panel ribonda YMC panelleri kartın yalnızca bir bandını kaplar ve bu
     * bant SABİTTİR: IShortPanelShift ayarı (PRN'de "Psp;N") denendi, 0/36/1000
     * değerlerinin hiçbiri bandı oynatmadı. Bu yüzden tasarım banda uydurulur,
     * bant tasarıma değil — renkli olması gereken her şey bu aralıkta durmalı.
     */
    static final double BAND_START_MM = 47.5;
    static final double BAND_END_MM = 85.6;

    // === Yerleşim (mm) ===

    static double MARGIN_MM = 4.0;

    /** Başlık. */
    static double TITLE_Y_MM = 9.0;
    static double TITLE_FONT_MM = 4.2;
    static double SUBTITLE_Y_MM = 14.0;
    static double SUBTITLE_FONT_MM = 2.8;

    /**
     * Fotoğraf kutusu — renkli bandın İÇİNDE olmalı, yoksa yalnızca K paneliyle
     * basılır ve açık tonlar (yüz) kaybolup geriye sadece saç gibi koyu yerler kalır.
     * 54,0-80,0 aralığı bandın (47,5-85,6) ortasına oturur, iki yanda ~6 mm pay bırakır.
     */
    static double PHOTO_W_MM = 20.0;
    static double PHOTO_H_MM = 26.0;
    static double PHOTO_Y_MM = 54.0;

    /** Bilgi alanları — fotoğrafın üstünde, bandın dışında (saf siyah, K paneli basar). */
    static double FIELD_START_Y_MM = 20.0;
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

    // === Arka yüz ===

    /** EGO kurumsal kırmızısı ve Ankara arması lacivertine yakın tonlar. */
    static final Color EGO_RED = new Color(227, 6, 19);
    static final Color ANKARA_BLUE = new Color(20, 60, 140);

    static final String[] BACK_TERMS = {
        "Bu kart EGO Genel Müdürlüğü mülkiyetindedir.",
        "İzinsiz kullananlar hakkında yasal işlem yapılır.",
        "Bulduğunuz kartları en yakın EGO otobüsüne veya gişesine teslim ediniz.",
        "Kartınızı bükmeyiniz, delmeyiniz ve manyetik alanlardan uzak tutunuz.",
        "Kart kullanım koşulları www.ego.gov.tr adresinde yer almaktadır.",
    };

    /** Arka yüz kart numarası — statik (kullanıcı onayı: sabit 4x4 sıfır). */
    static String BACK_CARD_NUMBER = "0000 0000 0000 0000";

    /**
     * Arka görseli 180° döndür. Çift yüz baskıda yazıcı kartı fiziksel çevirir;
     * arka ters çıkıyorsa bu bayrakla yazılımda düzeltiriz. Kalibrasyonla belirlenir.
     */
    static boolean backRotate180 = false;

    /**
     * Kartın ARKA yüzünü üretir.
     *
     * Üst ~33 mm renkli (logolar + slogan + ulaşım ikonları) — renkli banda
     * denk gelmeli. Alt kısım saf siyah (koşullar + kart no) — K paneli basar,
     * konum kısıtı yok. Bandın arkada nereye düştüğü kalibrasyonla doğrulanacak.
     */
    public static BufferedImage renderBack() throws Exception {
        int w = mmToPx(CARD_W_MM);
        int h = mmToPx(CARD_H_MM);

        BufferedImage img = new BufferedImage(w, h, BufferedImage.TYPE_INT_RGB);
        Graphics2D g = img.createGraphics();
        g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
        g.setRenderingHint(RenderingHints.KEY_RENDERING, RenderingHints.VALUE_RENDER_QUALITY);
        g.setRenderingHint(RenderingHints.KEY_INTERPOLATION, RenderingHints.VALUE_INTERPOLATION_BICUBIC);
        g.setRenderingHint(RenderingHints.KEY_TEXT_ANTIALIASING, RenderingHints.VALUE_TEXT_ANTIALIAS_ON);
        g.setColor(Color.WHITE);
        g.fillRect(0, 0, w, h);

        // OKUMA GÖRÜNÜMÜ (şablon düzeni): logolar ÜSTTE, maddeler altta.
        // Baskıya giderken renderBackForPrint() bunu 180° döndürür; böylece renkli
        // üst blok fiziksel alt banda oturur, kullanıcı kartı çevirince düz+renkli okur.
        int lx = mmToPx(4.0);

        // --- RENKLİ ÜST BÖLGE (döndürülünce alt banda oturar) ---
        // EGO logosu (sol) — yatay, oran korunur
        drawLogoFit(g, AppPaths.resolve("assets", "logo_ego.png"),
                mmToPx(4.0), mmToPx(7.0), mmToPx(26.0), mmToPx(13.0), false);
        // Ankara arması (sağ) — dikey, kutuya sığdır
        drawLogoFit(g, AppPaths.resolve("assets", "logo_ankara.png"),
                mmToPx(38.0), mmToPx(6.5), mmToPx(12.0), mmToPx(13.5), true);

        // Slogan
        g.setFont(new Font("Arial", Font.PLAIN, mmToPx(2.7)));
        g.setColor(new Color(90, 90, 90));
        g.drawString("Güvenli Yolculuk,", lx, mmToPx(26.0));
        g.setFont(new Font("Arial", Font.BOLD, mmToPx(3.0)));
        g.setColor(EGO_RED);
        g.drawString("Güzel Ankara.", lx, mmToPx(30.0));

        // Ulaşım ikon şeridi (kırmızı) — YER TUTUCU
        drawTransitStrip(g, lx, mmToPx(32.5), mmToPx(4.2), 5);

        // Ayraç çizgisi
        g.setColor(new Color(200, 200, 200));
        g.setStroke(new BasicStroke(Math.max(1, mmToPx(0.2))));
        g.drawLine(mmToPx(4.0), mmToPx(40.0), w - mmToPx(4.0), mmToPx(40.0));

        // --- SİYAH ALT BÖLGE (koşullar) — K paneli, konum serbest ---
        // BOLD: resin K panelinin daha çok temas etmesi için (silik baskıya karşı).
        double ty = 44.0;
        double lineMm = 2.6;
        for (int i = 0; i < BACK_TERMS.length; i++) {
            drawTermIcon(g, i, mmToPx(4.0), mmToPx(ty - 3.0), mmToPx(4.4));
            g.setFont(new Font("Arial", Font.BOLD, mmToPx(2.2)));
            g.setColor(Color.BLACK);
            int lines = drawWrapped(g, BACK_TERMS[i],
                    mmToPx(11.0), mmToPx(ty), mmToPx(CARD_W_MM - 11.0 - 3.0), mmToPx(lineMm));
            ty += lines * lineMm + 1.6;
        }

        // Kart numarası (siyah, ortalı, en altta)
        g.setColor(Color.BLACK);
        g.setFont(new Font("Consolas", Font.BOLD, mmToPx(3.0)));
        drawCentered(g, BACK_CARD_NUMBER, w, mmToPx(83.5));

        g.dispose();
        return img;
    }

    /**
     * Arka yüzün BASKI hâli — okuma görünümünün 180° döndürülmüşü.
     *
     * Renkli üst blok, döndürülünce fiziksel alt banda (BAND_START..END) oturur;
     * yazıcı arka görseli olduğu gibi bastığı için, kullanıcı kartı çevirince
     * tasarım düz ve logolar renkli okunur. (Çevirme yönü kalibrasyonla doğrulandı.)
     */
    public static BufferedImage renderBackForPrint() throws Exception {
        return rotate180(renderBack());
    }

    /** Görseli 180° döndürür (çift yüz arka yön düzeltmesi). */
    static BufferedImage rotate180(BufferedImage src) {
        BufferedImage out = new BufferedImage(src.getWidth(), src.getHeight(), src.getType());
        Graphics2D g = out.createGraphics();
        g.rotate(Math.PI, src.getWidth() / 2.0, src.getHeight() / 2.0);
        g.drawImage(src, 0, 0, null);
        g.dispose();
        return out;
    }

    /**
     * Arka yüz kalibrasyon hedefi: renkli mm cetveli + büyük yön işareti.
     * Çift yüz baskıda arka yüzün (a) renkli bandı nereye düştüğünü,
     * (b) ters/düz mü çıktığını tek kartta gösterir.
     */
    public static BufferedImage renderCalibrationBack() {
        BufferedImage img = renderCalibration();
        Graphics2D g = img.createGraphics();
        g.setRenderingHint(RenderingHints.KEY_TEXT_ANTIALIASING, RenderingHints.VALUE_TEXT_ANTIALIAS_ON);
        g.setColor(Color.BLACK);
        // Üstte büyük "ARKA ÜST ^", altta "ALT" — yön belirsizliği kalmasın
        g.setFont(new Font("Arial", Font.BOLD, mmToPx(4.0)));
        drawCentered(g, "ARKA UST ^", mmToPx(CARD_W_MM), mmToPx(7.5));
        g.setFont(new Font("Arial", Font.BOLD, mmToPx(3.0)));
        drawCentered(g, "ALT", mmToPx(CARD_W_MM), mmToPx(80.0));
        g.dispose();
        return backRotate180 ? rotate180(img) : img;
    }

    /** Saydam PNG'yi kutuya oranını koruyarak sığdırır; kutuda ortalar. */
    private static void drawLogoFit(Graphics2D g, Path logo, int bx, int by, int bw, int bh,
                                    boolean center) {
        try {
            if (!Files.exists(logo)) return;
            BufferedImage im = ImageIO.read(logo.toFile());
            if (im == null) return;
            double scale = Math.min((double) bw / im.getWidth(), (double) bh / im.getHeight());
            int dw = (int) Math.round(im.getWidth() * scale);
            int dh = (int) Math.round(im.getHeight() * scale);
            int dx = center ? bx + (bw - dw) / 2 : bx;
            int dy = by + (bh - dh) / 2;
            g.drawImage(im, dx, dy, dw, dh, null); // alfa korunur, beyaz kutu çıkmaz
        } catch (Exception ignore) {
        }
    }

    /** Metni verilen genişliğe sarar; kullanılan satır sayısını döndürür. */
    private static int drawWrapped(Graphics2D g, String text, int x, int yBaseline,
                                   int maxW, int lineH) {
        String[] words = text.split(" ");
        StringBuilder line = new StringBuilder();
        int y = yBaseline, lines = 0;
        for (String word : words) {
            String test = line.length() == 0 ? word : line + " " + word;
            if (g.getFontMetrics().stringWidth(test) > maxW && line.length() > 0) {
                g.drawString(line.toString(), x, y);
                line = new StringBuilder(word);
                y += lineH;
                lines++;
            } else {
                line = new StringBuilder(test);
            }
        }
        if (line.length() > 0) { g.drawString(line.toString(), x, y); lines++; }
        return lines;
    }

    /** Kırmızı ulaşım ikon şeridi — basit yer tutucu otobüs simgeleri. */
    private static void drawTransitStrip(Graphics2D g, int x, int yTop, int d, int n) {
        int gap = mmToPx(1.4);
        for (int i = 0; i < n; i++) {
            int cx = x + i * (d + gap);
            g.setColor(EGO_RED);
            g.fillOval(cx, yTop, d, d);
            // beyaz otobüs silueti
            g.setColor(Color.WHITE);
            int bw = (int) (d * 0.55), bh = (int) (d * 0.38);
            int bx = cx + (d - bw) / 2, by = yTop + (d - bh) / 2;
            g.fillRoundRect(bx, by, bw, bh, bh / 2, bh / 2);
            g.setColor(EGO_RED);
            g.fillOval(bx + bw / 6, by + bh - bh / 5, bh / 3, bh / 3);
            g.fillOval(bx + bw - bw / 6 - bh / 3, by + bh - bh / 5, bh / 3, bh / 3);
        }
    }

    /** 5 koşul maddesi için basit siyah çizgi ikonlar (yer tutucu). */
    private static void drawTermIcon(Graphics2D g, int idx, int x, int y, int s) {
        g.setColor(Color.BLACK);
        g.setStroke(new BasicStroke(Math.max(2, mmToPx(0.4))));  // kalın: silik baskıya karşı
        switch (idx) {
            case 0: // belge/kart
                g.drawRoundRect(x, y, s, s, s / 5, s / 5);
                for (int i = 1; i <= 3; i++)
                    g.drawLine(x + s / 5, y + i * s / 4, x + s - s / 5, y + i * s / 4);
                break;
            case 1: // yasal (§)
                g.setFont(new Font("Serif", Font.BOLD, s));
                g.drawString("§", x + s / 4, y + s);
                break;
            case 2: // teslim (kutuya ok)
                g.drawRect(x, y + s / 3, s, s - s / 3);
                g.drawLine(x + s / 2, y, x + s / 2, y + s / 2);
                g.drawLine(x + s / 2 - s / 4, y + s / 4, x + s / 2, y + s / 2);
                g.drawLine(x + s / 2 + s / 4, y + s / 4, x + s / 2, y + s / 2);
                break;
            case 3: // bükme yok (kart + yasak)
                g.drawRoundRect(x, y + s / 4, s, s / 2, s / 6, s / 6);
                g.drawOval(x + s / 4, y, s, s);
                g.drawLine(x + s / 4, y + s, x + s / 4 + s, y);
                break;
            default: // web (dünya)
                g.drawOval(x, y, s, s);
                g.drawOval(x + s / 3, y, s / 3, s);
                g.drawLine(x, y + s / 2, x + s, y + s / 2);
                break;
        }
    }

    /** Rakamların arasını hafifçe açar (kart no görünümü). */
    private static String spaced(String s) {
        StringBuilder sb = new StringBuilder();
        for (char c : s.toCharArray()) { sb.append(c); sb.append(c == ' ' ? "  " : " "); }
        return sb.toString();
    }

    /**
     * Kalibrasyon hedefi — yarım panel renkli bandın kartın neresine düştüğünü ölçer.
     *
     * Kart boyunca her 5 mm'de bir renkli çubuk ve yanında SİYAH mm etiketi var.
     * Siyah etiketler K paneliyle her yere basılır, renkli çubuklar ise yalnızca
     * YMC bandının denk geldiği yerde görünür. Yani basılan kartta hangi
     * numaraların hizasında renk çıktıysa bant oradadır.
     */
    public static BufferedImage renderCalibration() {
        int w = mmToPx(CARD_W_MM);
        int h = mmToPx(CARD_H_MM);

        BufferedImage img = new BufferedImage(w, h, BufferedImage.TYPE_INT_RGB);
        Graphics2D g = img.createGraphics();
        g.setRenderingHint(RenderingHints.KEY_ANTIALIASING, RenderingHints.VALUE_ANTIALIAS_ON);
        g.setRenderingHint(RenderingHints.KEY_TEXT_ANTIALIASING, RenderingHints.VALUE_TEXT_ANTIALIAS_ON);
        g.setColor(Color.WHITE);
        g.fillRect(0, 0, w, h);

        int labelW = mmToPx(9.0);
        int barX = labelW + mmToPx(1.0);
        int barW = w - barX - mmToPx(2.0);
        int barH = mmToPx(3.6);

        g.setFont(new Font("Arial", Font.BOLD, mmToPx(2.8)));
        for (int mm = 5; mm <= 80; mm += 5) {
            int y = mmToPx(mm);

            // Renkli çubuk — sadece YMC bandının içinde görünür.
            // Ardışık çubuklar farklı renkte: bandın kenarları da ayırt edilebilsin.
            g.setColor((mm / 5) % 2 == 0 ? new Color(220, 0, 0) : new Color(0, 110, 220));
            g.fillRect(barX, y - barH / 2, barW, barH);

            // Siyah etiket ve çizgi — K paneliyle kartın her yerine basılır.
            g.setColor(Color.BLACK);
            g.drawString(String.valueOf(mm), mmToPx(2.0), y + mmToPx(1.0));
            g.fillRect(labelW, y - mmToPx(0.15), mmToPx(1.0), mmToPx(0.3));
        }

        g.setFont(new Font("Arial", Font.BOLD, mmToPx(2.4)));
        g.setColor(Color.BLACK);
        drawCentered(g, "KALIBRASYON", w, mmToPx(3.0));
        drawCentered(g, "renkli cikan mm araligi = bant", w, mmToPx(84.5));

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
        Path outDir = AppPaths.resolve("output");
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

        if (PHOTO_Y_MM < BAND_START_MM || PHOTO_Y_MM + PHOTO_H_MM > BAND_END_MM) {
            System.out.println();
            System.out.println("UYARI: fotoğraf (" + PHOTO_Y_MM + "-" + (PHOTO_Y_MM + PHOTO_H_MM)
                    + " mm) renkli bandın (" + BAND_START_MM + "-" + BAND_END_MM
                    + " mm) dışına taşıyor — taşan kısım renksiz basılır.");
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
