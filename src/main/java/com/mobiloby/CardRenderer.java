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

    /** EGO kurumsal kırmızısı, Ankara arması lacivertine yakın ton ve ulaşım mavisi. */
    static final Color EGO_RED = new Color(227, 6, 19);
    static final Color ANKARA_BLUE = new Color(20, 60, 140);
    static final Color TRANSIT_BLUE = new Color(0, 95, 175);

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

        // Dekoratif üst zemin — açık mavi low-poly mesh + soluk kırmızı EGO motifi.
        // Yalnızca üst ~42 mm'ye çizilir; 180° döndürülünce renkli banda oturur.
        drawHeaderPattern(g, w, 0.0, 42.0);

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

        // Ulaşım şeridi — kırmızı/mavi otobüs ve tren
        drawTransitStrip(g, lx, mmToPx(32.5), mmToPx(4.6));

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

    /**
     * Dekoratif üst zemin: açık mavi low-poly üçgen mesh (sola doğru yoğun,
     * sağa doğru saydam) + sağ tarafta soluk kırmızı ışınsal EGO motifi.
     * Sadece [topMm, botMm] aralığına çizilir (döndürülünce renkli banda gelir).
     */
    private static void drawHeaderPattern(Graphics2D g, int w, double topMm, double botMm) {
        int y0 = mmToPx(topMm), y1 = mmToPx(botMm);
        java.awt.Shape oldClip = g.getClip();
        g.setClip(0, y0, w, y1 - y0);
        java.util.Random rnd = new java.util.Random(7);

        // --- açık mavi low-poly mesh ---
        int cols = 8, rows = 4;
        double cw = (double) w / cols, ch = (double) (y1 - y0) / rows;
        java.awt.geom.Point2D.Double[][] p = new java.awt.geom.Point2D.Double[rows + 1][cols + 1];
        for (int r = 0; r <= rows; r++)
            for (int c = 0; c <= cols; c++) {
                double jx = (c == 0 || c == cols) ? 0 : (rnd.nextDouble() - 0.5) * cw * 0.55;
                double jy = (r == 0 || r == rows) ? 0 : (rnd.nextDouble() - 0.5) * ch * 0.55;
                p[r][c] = new java.awt.geom.Point2D.Double(c * cw + jx, y0 + r * ch + jy);
            }
        for (int r = 0; r < rows; r++)
            for (int c = 0; c < cols; c++) {
                int a1 = meshAlpha(c, cols, rnd), a2 = meshAlpha(c, cols, rnd);
                fillTri(g, p[r][c], p[r][c + 1], p[r + 1][c], new Color(200, 219, 244, a1));
                fillTri(g, p[r + 1][c], p[r][c + 1], p[r + 1][c + 1], new Color(200, 219, 244, a2));
            }

        // --- soluk kırmızı ışınsal motif (sağ) ---
        g.setColor(new Color(228, 110, 120, 42));
        g.setStroke(new BasicStroke(Math.max(1f, mmToPx(0.12))));
        double mx = w - mmToPx(3), my = y0 + (y1 - y0) * 0.42, rr = mmToPx(19);
        for (int i = 0; i <= 18; i++) {
            double ang = Math.PI * (0.15 + 0.9 * i / 18.0);
            g.draw(new java.awt.geom.Line2D.Double(mx, my,
                    mx - Math.cos(ang) * rr, my - Math.sin(ang) * rr));
        }
        for (int k = 1; k <= 3; k++) {
            double kr = rr * k / 3.0;
            g.draw(new java.awt.geom.Ellipse2D.Double(mx - kr, my - kr, kr * 2, kr * 2));
        }

        g.setClip(oldClip);
    }

    /** Mesh üçgeni için sola doğru yoğun, sağa doğru saydam mavi alfa. */
    private static int meshAlpha(int c, int cols, java.util.Random rnd) {
        double t = 1.0 - (double) c / cols;          // sol=1, sağ=0
        return (int) Math.max(0, Math.min(150, 150 * t * t + rnd.nextInt(18) - 9));
    }

    /** Üç noktalı üçgeni doldurur + saydam beyaz kenar (faset görünümü). */
    private static void fillTri(Graphics2D g, java.awt.geom.Point2D a,
                                java.awt.geom.Point2D b, java.awt.geom.Point2D c, Color col) {
        java.awt.geom.Path2D.Double t = new java.awt.geom.Path2D.Double();
        t.moveTo(a.getX(), a.getY());
        t.lineTo(b.getX(), b.getY());
        t.lineTo(c.getX(), c.getY());
        t.closePath();
        g.setColor(col);
        g.fill(t);
        g.setColor(new Color(255, 255, 255, 90));
        g.setStroke(new BasicStroke(1f));
        g.draw(t);
    }

    /** Ulaşım şeridi — 4 ikon: kırmızı otobüs, mavi otobüs, kırmızı metro, mavi metro. */
    private static void drawTransitStrip(Graphics2D g, int x, int yTop, int d) {
        Color[] col = {EGO_RED, TRANSIT_BLUE, EGO_RED, TRANSIT_BLUE};
        boolean[] metro = {false, false, true, true};
        int gap = mmToPx(2.4);
        java.awt.Stroke old = g.getStroke();
        g.setStroke(new BasicStroke(Math.max(1.5f, d * 0.05f),
                BasicStroke.CAP_ROUND, BasicStroke.JOIN_ROUND));
        for (int i = 0; i < 4; i++) {
            int cx = x + i * (d + gap);
            g.setColor(col[i]);
            g.fillOval(cx, yTop, d, d);
            drawVehicleSide(g, cx, yTop, d, metro[i], col[i]);
        }
        g.setStroke(old);
    }

    /**
     * Daire içine beyaz araç. metro=true → ÖNDEN görünüm metro (yuvarlak tavan + cam + far);
     * false → yandan görünüm otobüs (yuvarlak tekerlekli). cut = daire rengi (oyuk).
     */
    private static void drawVehicleSide(Graphics2D g, int ox, int oy, int d,
                                        boolean metro, Color cut) {
        if (metro) {
            // Metro — önden görünüm: yuvarlak tavanlı gövde + geniş cam + 2 far
            double bw = d * 0.44, bh = d * 0.58;
            double bx = ox + (d - bw) / 2.0, by = oy + d * 0.21;
            g.setColor(Color.WHITE);
            g.fill(new java.awt.geom.RoundRectangle2D.Double(bx, by, bw, bh, bw * 0.6, bw * 0.6));
            // alt köşeleri düz olsun (tavan yuvarlak, taban düz)
            g.fill(new java.awt.geom.Rectangle2D.Double(bx, by + bh * 0.5, bw, bh * 0.5));
            g.setColor(cut);
            // ön cam (geniş üst pencere)
            g.fill(new java.awt.geom.RoundRectangle2D.Double(bx + bw * 0.15, by + bh * 0.15,
                    bw * 0.70, bh * 0.32, bw * 0.22, bw * 0.22));
            // 2 far
            double lr = bw * 0.22;
            g.fill(new java.awt.geom.Ellipse2D.Double(bx + bw * 0.14, by + bh * 0.62, lr, lr));
            g.fill(new java.awt.geom.Ellipse2D.Double(bx + bw * 0.64, by + bh * 0.62, lr, lr));
            // peron/ray çizgisi
            g.setColor(Color.WHITE);
            g.draw(new java.awt.geom.Line2D.Double(ox + d * 0.16, oy + d * 0.86, ox + d * 0.84, oy + d * 0.86));
        } else {
            // Otobüs: dörtgen gövde + pencere şeridi + 2 yuvarlak tekerlek
            double bx = ox + d * 0.17, by = oy + d * 0.33, bw = d * 0.66, bh = d * 0.30;
            g.setColor(Color.WHITE);
            g.fill(new java.awt.geom.RoundRectangle2D.Double(bx, by, bw, bh, bh * 0.4, bh * 0.4));
            g.setColor(cut);
            g.fill(new java.awt.geom.RoundRectangle2D.Double(bx + bw * 0.12, by + bh * 0.15,
                    bw * 0.76, bh * 0.34, bh * 0.2, bh * 0.2));
            double wr = d * 0.12, wy = by + bh - wr * 0.35;
            g.setColor(Color.WHITE);
            g.fill(new java.awt.geom.Ellipse2D.Double(bx + bw * 0.14, wy, wr, wr));
            g.fill(new java.awt.geom.Ellipse2D.Double(bx + bw * 0.66, wy, wr, wr));
            g.setColor(cut);
            g.fill(new java.awt.geom.Ellipse2D.Double(bx + bw * 0.14 + wr * 0.33, wy + wr * 0.33, wr * 0.34, wr * 0.34));
            g.fill(new java.awt.geom.Ellipse2D.Double(bx + bw * 0.66 + wr * 0.33, wy + wr * 0.33, wr * 0.34, wr * 0.34));
        }
    }

    /** 5 koşul maddesi için temiz siyah çizgi ikonlar. */
    private static void drawTermIcon(Graphics2D g, int idx, int x, int y, int s) {
        g.setColor(Color.BLACK);
        g.setStroke(new BasicStroke(Math.max(2, mmToPx(0.35)),
                BasicStroke.CAP_ROUND, BasicStroke.JOIN_ROUND));
        switch (idx) {
            case 0: { // mülkiyet: köşesi kıvrık belge + satırlar
                int f = s / 4;
                java.awt.Polygon doc = new java.awt.Polygon();
                doc.addPoint(x, y); doc.addPoint(x + s - f, y);
                doc.addPoint(x + s, y + f); doc.addPoint(x + s, y + s);
                doc.addPoint(x, y + s);
                g.drawPolygon(doc);
                g.drawLine(x + s - f, y, x + s - f, y + f);
                g.drawLine(x + s - f, y + f, x + s, y + f);
                for (int i = 2; i <= 4; i++)
                    g.drawLine(x + s / 5, y + i * s / 6, x + s - s / 5, y + i * s / 6);
                break;
            }
            case 1: { // yasal: terazi
                int cx = x + s / 2;
                g.drawLine(cx, y, cx, y + s);                 // direk
                g.drawLine(x, y + s / 5, x + s, y + s / 5);   // kiriş
                g.drawLine(x, y + s, x + s, y + s);           // taban
                // iki kefe ( V)
                g.drawLine(x, y + s / 5, x + s / 6, y + s / 2);
                g.drawLine(x + s / 3, y + s / 2, x + s / 6, y + s / 2);
                g.drawLine(x + s, y + s / 5, x + s - s / 6, y + s / 2);
                g.drawLine(x + s - s / 3, y + s / 2, x + s - s / 6, y + s / 2);
                break;
            }
            case 2: { // teslim: kutuya inen ok
                g.drawLine(x + s / 2, y, x + s / 2, y + s * 3 / 5);
                g.drawLine(x + s / 2 - s / 4, y + s * 7 / 20, x + s / 2, y + s * 3 / 5);
                g.drawLine(x + s / 2 + s / 4, y + s * 7 / 20, x + s / 2, y + s * 3 / 5);
                g.drawLine(x, y + s * 3 / 5, x, y + s);       // kutu (üstü açık)
                g.drawLine(x + s, y + s * 3 / 5, x + s, y + s);
                g.drawLine(x, y + s, x + s, y + s);
                break;
            }
            case 3: { // bükme yok: kart + yasak dairesi
                g.drawRoundRect(x + s / 8, y + s * 3 / 8, s * 3 / 4, s / 4, s / 8, s / 8);
                g.drawOval(x, y, s, s);
                g.drawLine(x + (int) (s * 0.15), y + (int) (s * 0.85),
                           x + (int) (s * 0.85), y + (int) (s * 0.15));
                break;
            }
            default: { // web: dünya (boylam + enlem)
                g.drawOval(x, y, s, s);
                g.drawOval(x + s / 3, y, s / 3, s);
                g.drawLine(x, y + s / 2, x + s, y + s / 2);
                g.drawLine(x + (int) (s * 0.07), y + s / 4, x + (int) (s * 0.93), y + s / 4);
                g.drawLine(x + (int) (s * 0.07), y + s * 3 / 4, x + (int) (s * 0.93), y + s * 3 / 4);
                break;
            }
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
