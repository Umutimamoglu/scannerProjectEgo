using System.Drawing;
using System.Drawing.Drawing2D;
using IdScanner.Core;
using IdScanner.Core.Diagnostics;
using IdScanner.Core.Model;
using IdScanner.Workflow.Abstractions;

namespace IdScanner.Render;

/// <summary>
/// Kart görselini üretir — çipten okunan veriyi basılabilir bir bitmap'e çevirir.
///
/// Java karşılığı: CardRenderer.java.
///
/// <b>ÖNEMLİ — yarım panel ribon kısıtı:</b>
/// Takılı ribon "Color Half YMCKO" (R5H004NAA). Yarım panel ribonlarda renkli
/// (YMC) paneller kartın yalnızca ~1/3'lük bir bandını kaplar; K (siyah) ve
/// O (koruyucu) tam boy. Bu yüzden:
///   - Fotoğraf TEK renkli öğe olmalı ve bandın içinde durmalı
///   - Diğer her şey saf siyah olmalı ki K paneliyle bassın
/// Bandın yeri kalibrasyon kartıyla ölçüldü ve yazılımla oynatılamıyor
/// (bkz. <see cref="BandStartMm"/>), o yüzden tasarım banda uyduruldu.
///
/// Baskı YAPMAZ — yalnızca görüntü üretir.
/// </summary>
public sealed class CardRenderer(IAppLogger? logger = null) : ICardRenderer
{
    // === Ölçüler (mm) — ISO 7810 ID-1, dikey yerleşim ===

    /// <summary>Kart genişliği (dikey tasarımda kısa kenar).</summary>
    private const double CardWidthMm = 54.0;

    /// <summary>Kart yüksekliği (dikey tasarımda uzun kenar).</summary>
    private const double CardHeightMm = 85.6;

    /// <summary>Baskı çözünürlüğü.</summary>
    private const int Dpi = 300;

    /// <summary>
    /// Renkli bandın kart üzerindeki GERÇEK konumu — kalibrasyon kartıyla ölçüldü.
    ///
    /// IShortPanelShift ayarı denendi; 0/36/1000 değerlerinin hiçbiri bandı
    /// oynatmadı. Bant sabit kabul edildi, tasarım ona uyduruldu.
    /// </summary>
    private const double BandStartMm = 47.5;
    private const double BandEndMm = 85.6;

    private const double MarginMm = 4.0;

    // Başlık
    private const double TitleYMm = 9.0;
    private const double TitleFontMm = 4.2;
    private const double SubtitleYMm = 14.0;
    private const double SubtitleFontMm = 2.8;

    /// <summary>
    /// Fotoğraf kutusu — renkli bandın İÇİNDE olmalı, yoksa yalnızca K paneliyle
    /// basılır ve açık tonlar (yüz) kaybolup geriye sadece saç gibi koyu yerler kalır.
    /// 54,0–80,0 aralığı bandın (47,5–85,6) ortasına oturur.
    /// </summary>
    private const double PhotoWidthMm = 20.0;
    private const double PhotoHeightMm = 26.0;
    private const double PhotoYMm = 54.0;

    // Bilgi alanları — fotoğrafın üstünde, bandın dışında (saf siyah)
    private const double FieldStartYMm = 20.0;
    private const double FieldStepMm = 6.5;
    private const double LabelFontMm = 2.3;
    private const double ValueFontMm = 3.0;

    // === Renkler ===

    /// <summary>EGO kurumsal kırmızısı.</summary>
    private static readonly Color EgoRed = Color.FromArgb(227, 6, 19);

    /// <summary>Ulaşım mavisi.</summary>
    private static readonly Color TransitBlue = Color.FromArgb(0, 95, 175);

    /// <summary>Arka yüzdeki kullanım koşulları.</summary>
    private static readonly string[] BackTerms =
    [
        "Bu kart EGO Genel Müdürlüğü mülkiyetindedir.",
        "İzinsiz kullananlar hakkında yasal işlem yapılır.",
        "Bulduğunuz kartları en yakın EGO otobüsüne veya gişesine teslim ediniz.",
        "Kartınızı bükmeyiniz, delmeyiniz ve manyetik alanlardan uzak tutunuz.",
        "Kart kullanım koşulları www.ego.gov.tr adresinde yer almaktadır.",
    ];

    /// <summary>Arka yüz kart numarası — şu an sabit (kullanıcı onayı).</summary>
    public static string BackCardNumber { get; set; } = "0000 0000 0000 0000";

    /// <summary>
    /// Arka görseli 180° döndür. Çift yüz baskıda yazıcı kartı fiziksel çevirir;
    /// arka ters çıkıyorsa bu bayrakla yazılımda düzeltilir.
    /// </summary>
    public static bool BackRotate180 { get; set; }

    private readonly IAppLogger _log = (logger ?? NullLogger.Instance).ForComponent("Render");

    /// <summary>Milimetreyi piksele çevir.</summary>
    private static int Px(double mm) => (int)Math.Round(mm / 25.4 * Dpi);

    private static int CardWidthPx => Px(CardWidthMm);
    private static int CardHeightPx => Px(CardHeightMm);

    // === ICardRenderer ===

    /// <inheritdoc />
    public byte[] RenderFrontPng(CardData data)
    {
        using var bitmap = RenderFront(data);
        return bitmap.ToPng();
    }

    /// <inheritdoc />
    public byte[] RenderBackPng()
    {
        using var bitmap = RenderBack();
        return bitmap.ToPng();
    }

    /// <inheritdoc />
    public byte[] RenderCalibrationPng()
    {
        using var bitmap = RenderCalibration();
        return bitmap.ToPng();
    }

    /// <inheritdoc />
    public byte[] RenderBothPng(CardData data)
    {
        using var front = RenderFront(data);
        using var back = RenderBack();
        using var combined = SideBySide(front, back, Px(4.0));
        return combined.ToPng();
    }

    /// <inheritdoc />
    public (string Front, string Back) RenderFacesToBmp(CardData data)
    {
        var outputDir = AppPaths.EnsureDir("output");

        var frontPath = Path.Combine(outputDir, "card_print.bmp");
        var backPath = Path.Combine(outputDir, "card_back_print.bmp");

        using (var front = RenderFront(data)) SaveBmp(front, frontPath);
        using (var back = RenderBackForPrint()) SaveBmp(back, backPath);

        _log.Info($"Baskı görselleri yazıldı: {frontPath}, {backPath}");
        return (frontPath, backPath);
    }

    /// <inheritdoc />
    public (string Front, string Back) RenderCalibrationToBmp()
    {
        var outputDir = AppPaths.EnsureDir("output");

        var frontPath = Path.Combine(outputDir, "card_calibration.bmp");
        var backPath = Path.Combine(outputDir, "card_calibration_back.bmp");

        using (var front = RenderCalibration()) SaveBmp(front, frontPath);
        using (var back = RenderCalibrationBack()) SaveBmp(back, backPath);

        _log.Info($"Kalibrasyon görselleri yazıldı: {frontPath}, {backPath}");
        return (frontPath, backPath);
    }

    // === Hazır basılı kart üzerine baskı ===

    /// <inheritdoc />
    public byte[] RenderOverlayPng(CardData data, OverlayLayout layout)
    {
        using var bitmap = RenderOverlay(data, layout);
        return bitmap.ToPng();
    }

    /// <inheritdoc />
    public string RenderOverlayToBmp(CardData data, OverlayLayout layout)
    {
        var path = Path.Combine(AppPaths.EnsureDir("output"), "card_overlay.bmp");
        using var bitmap = RenderOverlay(data, layout);
        SaveBmp(bitmap, path);
        _log.Info($"Hazır kart baskı görseli yazıldı: {path}");
        return path;
    }

    /// <summary>
    /// Matbaada basılmış kartın üzerine eklenecek katmanı çizer.
    ///
    /// Yalnızca <b>fotoğraf ve ad/soyad</b> çizilir; kalan her yer beyaz kalır.
    /// Yazıcı beyaz alanlara mürekkep basmadığı için matbaa baskısı olduğu gibi
    /// korunur.
    ///
    /// Tam kart tasarımından (<see cref="RenderFront"/>) ayrı tutuldu: ikisi
    /// farklı ürünler ve biri değişince diğeri etkilenmemeli.
    /// </summary>
    public Bitmap RenderOverlay(CardData data, OverlayLayout layout)
    {
        using var op = _log.BeginOperation("Hazır kart katmanı çizimi");

        var bitmap = new Bitmap(CardWidthPx, CardHeightPx, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

        using (var g = Graphics.FromImage(bitmap))
        {
            g.ApplyQualitySettings();

            // Beyaz = mürekkep yok. Hazır baskının korunmasını sağlayan şey bu.
            g.Clear(Color.White);

            // Taklit kart yalnızca önizlemede; baskıda ShowGuides kapalı olduğu
            // için karta yalnızca fotoğraf ve ad/soyad gider.
            if (layout.ShowGuides)
            {
                PreprintedCardMockup.Draw(g, CardWidthMm, CardHeightMm, Px);
            }

            DrawOverlayPhoto(g, data, layout);
            DrawOverlayText(g, data, layout);

            if (layout.ShowGuides) DrawOverlayGuides(g, layout);
        }

        // Kart yazıcıya ters besleneceği için görsel de döndürülür;
        // ikisi birlikte sonucu düz hale getirir.
        if (layout.Rotate180)
        {
            var rotated = GraphicsHelpers.Rotate180(bitmap);
            bitmap.Dispose();
            op.Success("180° döndürüldü (ters besleme)");
            return rotated;
        }

        op.Success();
        return bitmap;
    }

    /// <summary>Fotoğrafı hazır kartın boş kutusuna oturt.</summary>
    private void DrawOverlayPhoto(Graphics g, CardData data, OverlayLayout layout)
    {
        if (string.IsNullOrWhiteSpace(data.PhotoPath) || !File.Exists(data.PhotoPath))
        {
            _log.Warn("Hazır kart baskısı için fotoğraf yok — kutu boş kalacak");
            return;
        }

        var x = Px(layout.PhotoXMm);
        var y = Px(layout.PhotoYMm);
        var width = Px(layout.PhotoWidthMm);
        var height = Px(layout.PhotoHeightMm);

        try
        {
            using var photo = new Bitmap(data.PhotoPath);

            // Kutuyu tamamen doldur, taşanı kırp — matbaa kutusunda beyaz
            // kenar kalmasın.
            var scale = Math.Max((double)width / photo.Width, (double)height / photo.Height);
            var drawWidth = (int)Math.Round(photo.Width * scale);
            var drawHeight = (int)Math.Round(photo.Height * scale);

            var previousClip = g.Clip;
            g.SetClip(new Rectangle(x, y, width, height));
            g.DrawImage(photo,
                x + (width - drawWidth) / 2,
                y + (height - drawHeight) / 2,
                drawWidth, drawHeight);
            g.Clip = previousClip;

            _log.Debug($"Fotoğraf yerleştirildi: {layout.PhotoXMm}×{layout.PhotoYMm} mm, " +
                       $"{layout.PhotoWidthMm}×{layout.PhotoHeightMm} mm");
        }
        catch (Exception e)
        {
            _log.Error($"Fotoğraf çizilemedi: {data.PhotoPath}", e);
        }
    }

    /// <summary>Ad ve soyadı hazır etiketlerin yanına yaz.</summary>
    private void DrawOverlayText(Graphics g, CardData data, OverlayLayout layout)
    {
        // Saf siyah — K paneliyle basılır, konum kısıtı yok
        using var black = new SolidBrush(Color.Black);
        using var font = CreateFont(layout.TextSizeMm, FontStyle.Bold);

        if (!string.IsNullOrWhiteSpace(data.Name))
        {
            g.DrawStringAtBaseline(data.Name, font, black, Px(layout.NameXMm), Px(layout.NameYMm));
        }

        if (!string.IsNullOrWhiteSpace(data.Surname))
        {
            g.DrawStringAtBaseline(data.Surname, font, black, Px(layout.SurnameXMm), Px(layout.SurnameYMm));
        }

        _log.Debug($"Ad '{data.Name}' ve soyad '{data.Surname}' yazıldı");
    }

    /// <summary>
    /// Yerleşim denetimi için kılavuz çizgiler — yalnızca önizlemede.
    ///
    /// Milimetre ızgarası da çizilir: önizlemeye bakıp "fotoğraf 6 mm sağa
    /// kaysın" gibi bir karar verilebilsin ve <c>overlay-layout.txt</c> tek
    /// seferde doğru doldurulabilsin.
    /// </summary>
    private static void DrawOverlayGuides(Graphics g, OverlayLayout layout)
    {
        DrawMillimetreGrid(g);

        using var pen = new Pen(Color.FromArgb(160, 200, 60, 60), 2f)
        {
            DashStyle = DashStyle.Dash,
        };

        g.DrawRectangle(pen,
            Px(layout.PhotoXMm), Px(layout.PhotoYMm),
            Px(layout.PhotoWidthMm), Px(layout.PhotoHeightMm));

        g.DrawLine(pen, Px(layout.NameXMm), Px(layout.NameYMm),
            Px(layout.NameXMm + 30), Px(layout.NameYMm));
        g.DrawLine(pen, Px(layout.SurnameXMm), Px(layout.SurnameYMm),
            Px(layout.SurnameXMm + 30), Px(layout.SurnameYMm));
    }

    /// <summary>
    /// 5 mm aralıklı ızgara ve 10 mm'de bir sayı etiketi.
    ///
    /// Ölçüler kartın sol üst köşesinden (0,0) sayılır — dosyadaki değerlerle
    /// aynı eksen takımı.
    /// </summary>
    private static void DrawMillimetreGrid(Graphics g)
    {
        const double Step = 5.0;
        const double LabelStep = 10.0;

        // Taklit kartın kırmızı zemini üzerinde okunabilsin diye beyaz
        using var minor = new Pen(Color.FromArgb(45, 255, 255, 255), 1f);
        using var major = new Pen(Color.FromArgb(110, 255, 255, 255), 1f);
        using var border = new Pen(Color.FromArgb(160, 255, 255, 255), 2f);
        using var labelBrush = new SolidBrush(Color.FromArgb(200, 255, 255, 255));
        using var labelFont = CreateFont(2.0, FontStyle.Regular);

        for (var mm = Step; mm < CardWidthMm; mm += Step)
        {
            var isLabelled = Math.Abs(mm % LabelStep) < 0.001;
            g.DrawLine(isLabelled ? major : minor, Px(mm), 0, Px(mm), CardHeightPx);
            if (isLabelled) g.DrawString($"{mm:0}", labelFont, labelBrush, Px(mm) + 2, 2);
        }

        for (var mm = Step; mm < CardHeightMm; mm += Step)
        {
            var isLabelled = Math.Abs(mm % LabelStep) < 0.001;
            g.DrawLine(isLabelled ? major : minor, 0, Px(mm), CardWidthPx, Px(mm));
            if (isLabelled) g.DrawString($"{mm:0}", labelFont, labelBrush, 2, Px(mm) + 2);
        }

        // Kart kenarı — önizlemede kâğıt sınırı belli olsun
        g.DrawRectangle(border, 0, 0, CardWidthPx - 1, CardHeightPx - 1);
    }

    // === Ön yüz ===

    /// <summary>Kartın ön yüzünü üretir.</summary>
    public Bitmap RenderFront(CardData data)
    {
        using var op = _log.BeginOperation("Ön yüz çizimi");

        var width = CardWidthPx;
        var height = CardHeightPx;
        var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

        using (var g = Graphics.FromImage(bitmap))
        {
            g.ApplyQualitySettings();

            // Beyaz zemin — yazıcı beyaz alanlara mürekkep basmaz
            g.Clear(Color.White);

            using var black = new SolidBrush(Color.Black);

            // Başlık
            using (var titleFont = CreateFont(TitleFontMm, FontStyle.Bold))
            {
                g.DrawCentered(data.Title, titleFont, black, width, Px(TitleYMm));
            }
            using (var subtitleFont = CreateFont(SubtitleFontMm, FontStyle.Regular))
            {
                g.DrawCentered(data.Subtitle, subtitleFont, black, width, Px(SubtitleYMm));
            }

            DrawPhoto(g, data, width);
            DrawFields(g, data, width);
        }

        op.Success($"{width}×{height} piksel");
        return bitmap;
    }

    /// <summary>Fotoğrafı çerçevesiyle çiz — kartın tek renkli öğesi.</summary>
    private void DrawPhoto(Graphics g, CardData data, int width)
    {
        var photoWidth = Px(PhotoWidthMm);
        var photoHeight = Px(PhotoHeightMm);
        var photoX = (width - photoWidth) / 2;
        var photoY = Px(PhotoYMm);

        if (!string.IsNullOrWhiteSpace(data.PhotoPath) && File.Exists(data.PhotoPath))
        {
            try
            {
                using var photo = new Bitmap(data.PhotoPath);

                // Kutuyu dolduracak şekilde ölçekle, taşan kısmı kırp
                var scale = Math.Max((double)photoWidth / photo.Width, (double)photoHeight / photo.Height);
                var drawWidth = (int)Math.Round(photo.Width * scale);
                var drawHeight = (int)Math.Round(photo.Height * scale);

                var previousClip = g.Clip;
                g.SetClip(new Rectangle(photoX, photoY, photoWidth, photoHeight));
                g.DrawImage(photo,
                    photoX + (photoWidth - drawWidth) / 2,
                    photoY + (photoHeight - drawHeight) / 2,
                    drawWidth, drawHeight);
                g.Clip = previousClip;
            }
            catch (Exception e)
            {
                _log.Warn($"Fotoğraf çizilemedi: {data.PhotoPath}", e);
            }
        }
        else if (!string.IsNullOrWhiteSpace(data.PhotoPath))
        {
            _log.Warn($"Fotoğraf dosyası yok: {data.PhotoPath} — çerçeve boş çizilecek");
        }

        // Fotoğraf çerçevesi (siyah)
        using var pen = new Pen(Color.Black, Math.Max(1, Px(0.25)));
        g.DrawRectangle(pen, photoX, photoY, photoWidth, photoHeight);
    }

    /// <summary>Bilgi alanlarını sırayla çiz; boş olanlar atlanır.</summary>
    private void DrawFields(Graphics g, CardData data, int width)
    {
        var y = FieldStartYMm;
        y = DrawField(g, "Adı", data.Name, y, width);
        y = DrawField(g, "Soyadı", data.Surname, y, width);
        y = DrawField(g, "T.C. Kimlik No", data.IdNumber, y, width);
        y = DrawField(g, "Doğum Tarihi", data.BirthDate, y, width);
        DrawField(g, "Geçerlilik", data.ExpiryDate, y, width);
    }

    /// <summary>Etiket + değer çifti çiz; sonraki alanın Y'sini döndür.</summary>
    private static double DrawField(Graphics g, string label, string value, double yMm, int width)
    {
        if (string.IsNullOrWhiteSpace(value)) return yMm;

        var x = Px(MarginMm);
        using var black = new SolidBrush(Color.Black);

        using (var labelFont = CreateFont(LabelFontMm, FontStyle.Regular))
        {
            g.DrawStringAtBaseline(label + ":", labelFont, black, x, Px(yMm));
        }
        using (var valueFont = CreateFont(ValueFontMm, FontStyle.Bold))
        {
            g.DrawStringAtBaseline(value, valueFont, black, x, Px(yMm + 3.4));
        }

        return yMm + FieldStepMm;
    }

    // === Arka yüz ===

    /// <summary>
    /// Kartın arka yüzünü üretir (okuma görünümü).
    ///
    /// Üst ~33 mm renkli (logolar + slogan + ulaşım ikonları) — renkli banda
    /// denk gelmeli. Alt kısım saf siyah (koşullar + kart no) — K paneli basar.
    /// </summary>
    public Bitmap RenderBack()
    {
        using var op = _log.BeginOperation("Arka yüz çizimi");

        var width = CardWidthPx;
        var height = CardHeightPx;
        var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

        using (var g = Graphics.FromImage(bitmap))
        {
            g.ApplyQualitySettings();
            g.Clear(Color.White);

            // Dekoratif üst zemin — döndürülünce renkli banda oturur
            DrawHeaderPattern(g, width, 0.0, 42.0);

            var leftX = Px(4.0);

            // --- RENKLİ ÜST BÖLGE ---
            DrawLogoFit(g, AppPaths.Resolve("assets", "logo_ego.png"),
                Px(4.0), Px(7.0), Px(26.0), Px(13.0), center: false);
            DrawLogoFit(g, AppPaths.Resolve("assets", "logo_ankara.png"),
                Px(38.0), Px(6.5), Px(12.0), Px(13.5), center: true);

            // Slogan
            using (var font = CreateFont(2.7, FontStyle.Regular))
            using (var gray = new SolidBrush(Color.FromArgb(90, 90, 90)))
            {
                g.DrawStringAtBaseline("Güvenli Yolculuk,", font, gray, leftX, Px(26.0));
            }
            using (var font = CreateFont(3.0, FontStyle.Bold))
            using (var red = new SolidBrush(EgoRed))
            {
                g.DrawStringAtBaseline("Güzel Ankara.", font, red, leftX, Px(30.0));
            }

            DrawTransitStrip(g, leftX, Px(32.5), Px(4.6));

            // Ayraç çizgisi
            using (var pen = new Pen(Color.FromArgb(200, 200, 200), Math.Max(1, Px(0.2))))
            {
                g.DrawLine(pen, Px(4.0), Px(40.0), width - Px(4.0), Px(40.0));
            }

            DrawTerms(g);

            // Kart numarası — siyah, ortalı, en altta
            using (var font = CreateFont(3.0, FontStyle.Bold, "Consolas"))
            using (var black = new SolidBrush(Color.Black))
            {
                g.DrawCentered(BackCardNumber, font, black, width, Px(83.5));
            }
        }

        op.Success();
        return bitmap;
    }

    /// <summary>
    /// Arka yüzün BASKI hâli — okuma görünümünün 180° döndürülmüşü.
    ///
    /// Renkli üst blok, döndürülünce fiziksel alt banda oturur; kullanıcı kartı
    /// çevirince tasarım düz ve logolar renkli okunur.
    /// </summary>
    public Bitmap RenderBackForPrint()
    {
        using var back = RenderBack();
        return GraphicsHelpers.Rotate180(back);
    }

    /// <summary>Kullanım koşullarını ikonlarıyla birlikte çiz.</summary>
    private void DrawTerms(Graphics g)
    {
        // BOLD: resin K panelinin daha çok temas etmesi için (silik baskıya karşı)
        var y = 44.0;
        const double lineMm = 2.6;

        using var black = new SolidBrush(Color.Black);
        using var font = CreateFont(2.2, FontStyle.Bold);

        for (var i = 0; i < BackTerms.Length; i++)
        {
            DrawTermIcon(g, i, Px(4.0), Px(y - 3.0), Px(4.4));

            var lines = DrawWrapped(g, BackTerms[i], font, black,
                Px(11.0), Px(y), Px(CardWidthMm - 11.0 - 3.0), Px(lineMm));

            y += lines * lineMm + 1.6;
        }
    }

    /// <summary>Metni verilen genişliğe sarar; kullanılan satır sayısını döndürür.</summary>
    private static int DrawWrapped(
        Graphics g, string text, Font font, Brush brush,
        int x, int baselineY, int maxWidth, int lineHeight)
    {
        var words = text.Split(' ');
        var line = "";
        var y = baselineY;
        var lines = 0;

        foreach (var word in words)
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (g.MeasureWidth(candidate, font) > maxWidth && line.Length > 0)
            {
                g.DrawStringAtBaseline(line, font, brush, x, y);
                line = word;
                y += lineHeight;
                lines++;
            }
            else
            {
                line = candidate;
            }
        }

        if (line.Length > 0)
        {
            g.DrawStringAtBaseline(line, font, brush, x, y);
            lines++;
        }

        return lines;
    }

    /// <summary>
    /// Dekoratif üst zemin: açık mavi low-poly üçgen mesh (sola doğru yoğun,
    /// sağa doğru saydam) + sağda soluk kırmızı ışınsal motif.
    /// </summary>
    private static void DrawHeaderPattern(Graphics g, int width, double topMm, double bottomMm)
    {
        var y0 = Px(topMm);
        var y1 = Px(bottomMm);

        var previousClip = g.Clip;
        g.SetClip(new Rectangle(0, y0, width, y1 - y0));

        // Sabit tohum — desen her çalıştırmada aynı olsun (Java ile birebir)
        var random = new JavaRandom(7);

        // --- açık mavi low-poly mesh ---
        const int cols = 8;
        const int rows = 4;
        var cellWidth = (double)width / cols;
        var cellHeight = (double)(y1 - y0) / rows;

        var points = new PointF[rows + 1, cols + 1];
        for (var r = 0; r <= rows; r++)
        {
            for (var c = 0; c <= cols; c++)
            {
                var jitterX = c == 0 || c == cols ? 0 : (random.NextDouble() - 0.5) * cellWidth * 0.55;
                var jitterY = r == 0 || r == rows ? 0 : (random.NextDouble() - 0.5) * cellHeight * 0.55;
                points[r, c] = new PointF(
                    (float)(c * cellWidth + jitterX),
                    (float)(y0 + r * cellHeight + jitterY));
            }
        }

        for (var r = 0; r < rows; r++)
        {
            for (var c = 0; c < cols; c++)
            {
                var alpha1 = MeshAlpha(c, cols, random);
                var alpha2 = MeshAlpha(c, cols, random);
                FillTriangle(g, points[r, c], points[r, c + 1], points[r + 1, c],
                    Color.FromArgb(alpha1, 200, 219, 244));
                FillTriangle(g, points[r + 1, c], points[r, c + 1], points[r + 1, c + 1],
                    Color.FromArgb(alpha2, 200, 219, 244));
            }
        }

        // --- soluk kırmızı ışınsal motif (sağ) ---
        using (var pen = new Pen(Color.FromArgb(42, 228, 110, 120), Math.Max(1f, Px(0.12))))
        {
            double centerX = width - Px(3);
            var centerY = y0 + (y1 - y0) * 0.42;
            double radius = Px(19);

            for (var i = 0; i <= 18; i++)
            {
                var angle = Math.PI * (0.15 + 0.9 * i / 18.0);
                g.DrawLine(pen,
                    (float)centerX, (float)centerY,
                    (float)(centerX - Math.Cos(angle) * radius),
                    (float)(centerY - Math.Sin(angle) * radius));
            }

            for (var k = 1; k <= 3; k++)
            {
                var r = radius * k / 3.0;
                g.DrawEllipse(pen, (float)(centerX - r), (float)(centerY - r), (float)(r * 2), (float)(r * 2));
            }
        }

        g.Clip = previousClip;
    }

    /// <summary>Mesh üçgeni için sola doğru yoğun, sağa doğru saydam mavi alfa.</summary>
    private static int MeshAlpha(int col, int cols, JavaRandom random)
    {
        var t = 1.0 - (double)col / cols; // sol=1, sağ=0
        return (int)Math.Max(0, Math.Min(150, 150 * t * t + random.NextInt(18) - 9));
    }

    /// <summary>Üçgeni doldur + saydam beyaz kenar (faset görünümü).</summary>
    private static void FillTriangle(Graphics g, PointF a, PointF b, PointF c, Color color)
    {
        PointF[] triangle = [a, b, c];

        using (var brush = new SolidBrush(color))
        {
            g.FillPolygon(brush, triangle);
        }
        using (var pen = new Pen(Color.FromArgb(90, 255, 255, 255), 1f))
        {
            g.DrawPolygon(pen, triangle);
        }
    }

    /// <summary>Ulaşım şeridi — kırmızı/mavi otobüs ve metro.</summary>
    private static void DrawTransitStrip(Graphics g, int x, int yTop, int diameter)
    {
        Color[] colors = [EgoRed, TransitBlue, EgoRed, TransitBlue];
        bool[] isMetro = [false, false, true, true];
        var gap = Px(2.4);

        for (var i = 0; i < colors.Length; i++)
        {
            var cx = x + i * (diameter + gap);
            using (var brush = new SolidBrush(colors[i]))
            {
                g.FillEllipse(brush, cx, yTop, diameter, diameter);
            }
            DrawVehicle(g, cx, yTop, diameter, isMetro[i], colors[i]);
        }
    }

    /// <summary>
    /// Daire içine beyaz araç. <paramref name="metro"/> ise önden görünüm metro,
    /// değilse yandan görünüm otobüs. <paramref name="cut"/> daire rengidir (oyuk).
    /// </summary>
    private static void DrawVehicle(Graphics g, int ox, int oy, int d, bool metro, Color cut)
    {
        using var white = new SolidBrush(Color.White);
        using var cutBrush = new SolidBrush(cut);

        if (metro)
        {
            // Metro — önden: yuvarlak tavanlı gövde + geniş cam + 2 far
            var bw = (float)(d * 0.44);
            var bh = (float)(d * 0.58);
            var bx = (float)(ox + (d - bw) / 2.0);
            var by = (float)(oy + d * 0.21);

            using (var body = GraphicsHelpers.RoundedRectangle(bx, by, bw, bh, bw * 0.6f, bw * 0.6f))
            {
                g.FillPath(white, body);
            }
            // Taban düz olsun (tavan yuvarlak kalır)
            g.FillRectangle(white, bx, by + bh * 0.5f, bw, bh * 0.5f);

            using (var window = GraphicsHelpers.RoundedRectangle(
                bx + bw * 0.15f, by + bh * 0.15f, bw * 0.70f, bh * 0.32f, bw * 0.22f, bw * 0.22f))
            {
                g.FillPath(cutBrush, window);
            }

            var lampSize = bw * 0.22f;
            g.FillEllipse(cutBrush, bx + bw * 0.14f, by + bh * 0.62f, lampSize, lampSize);
            g.FillEllipse(cutBrush, bx + bw * 0.64f, by + bh * 0.62f, lampSize, lampSize);

            using var railPen = new Pen(Color.White, Math.Max(1.5f, d * 0.05f))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
            };
            g.DrawLine(railPen,
                (float)(ox + d * 0.16), (float)(oy + d * 0.86),
                (float)(ox + d * 0.84), (float)(oy + d * 0.86));
        }
        else
        {
            // Otobüs: dörtgen gövde + pencere şeridi + 2 yuvarlak tekerlek
            var bx = (float)(ox + d * 0.17);
            var by = (float)(oy + d * 0.33);
            var bw = (float)(d * 0.66);
            var bh = (float)(d * 0.30);

            using (var body = GraphicsHelpers.RoundedRectangle(bx, by, bw, bh, bh * 0.4f, bh * 0.4f))
            {
                g.FillPath(white, body);
            }
            using (var windows = GraphicsHelpers.RoundedRectangle(
                bx + bw * 0.12f, by + bh * 0.15f, bw * 0.76f, bh * 0.34f, bh * 0.2f, bh * 0.2f))
            {
                g.FillPath(cutBrush, windows);
            }

            var wheelRadius = (float)(d * 0.12);
            var wheelY = by + bh - wheelRadius * 0.35f;

            g.FillEllipse(white, bx + bw * 0.14f, wheelY, wheelRadius, wheelRadius);
            g.FillEllipse(white, bx + bw * 0.66f, wheelY, wheelRadius, wheelRadius);
            g.FillEllipse(cutBrush, bx + bw * 0.14f + wheelRadius * 0.33f, wheelY + wheelRadius * 0.33f,
                wheelRadius * 0.34f, wheelRadius * 0.34f);
            g.FillEllipse(cutBrush, bx + bw * 0.66f + wheelRadius * 0.33f, wheelY + wheelRadius * 0.33f,
                wheelRadius * 0.34f, wheelRadius * 0.34f);
        }
    }

    /// <summary>Koşul maddeleri için siyah çizgi ikonlar.</summary>
    private static void DrawTermIcon(Graphics g, int index, int x, int y, int s)
    {
        using var pen = new Pen(Color.Black, Math.Max(2, Px(0.35)))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };

        switch (index)
        {
            case 0: // mülkiyet: köşesi kıvrık belge + satırlar
            {
                var f = s / 4;
                Point[] doc =
                [
                    new(x, y), new(x + s - f, y), new(x + s, y + f),
                    new(x + s, y + s), new(x, y + s),
                ];
                g.DrawPolygon(pen, doc);
                g.DrawLine(pen, x + s - f, y, x + s - f, y + f);
                g.DrawLine(pen, x + s - f, y + f, x + s, y + f);
                for (var i = 2; i <= 4; i++)
                {
                    g.DrawLine(pen, x + s / 5, y + i * s / 6, x + s - s / 5, y + i * s / 6);
                }
                break;
            }

            case 1: // yasal: terazi
            {
                var cx = x + s / 2;
                g.DrawLine(pen, cx, y, cx, y + s);               // direk
                g.DrawLine(pen, x, y + s / 5, x + s, y + s / 5); // kiriş
                g.DrawLine(pen, x, y + s, x + s, y + s);         // taban
                g.DrawLine(pen, x, y + s / 5, x + s / 6, y + s / 2);
                g.DrawLine(pen, x + s / 3, y + s / 2, x + s / 6, y + s / 2);
                g.DrawLine(pen, x + s, y + s / 5, x + s - s / 6, y + s / 2);
                g.DrawLine(pen, x + s - s / 3, y + s / 2, x + s - s / 6, y + s / 2);
                break;
            }

            case 2: // teslim: kutuya inen ok
            {
                g.DrawLine(pen, x + s / 2, y, x + s / 2, y + s * 3 / 5);
                g.DrawLine(pen, x + s / 2 - s / 4, y + s * 7 / 20, x + s / 2, y + s * 3 / 5);
                g.DrawLine(pen, x + s / 2 + s / 4, y + s * 7 / 20, x + s / 2, y + s * 3 / 5);
                g.DrawLine(pen, x, y + s * 3 / 5, x, y + s);
                g.DrawLine(pen, x + s, y + s * 3 / 5, x + s, y + s);
                g.DrawLine(pen, x, y + s, x + s, y + s);
                break;
            }

            case 3: // bükme yok: kart + yasak dairesi
            {
                using (var card = GraphicsHelpers.RoundedRectangle(
                    x + s / 8f, y + s * 3 / 8f, s * 3 / 4f, s / 4f, s / 8f, s / 8f))
                {
                    g.DrawPath(pen, card);
                }
                g.DrawEllipse(pen, x, y, s, s);
                g.DrawLine(pen,
                    x + (int)(s * 0.15), y + (int)(s * 0.85),
                    x + (int)(s * 0.85), y + (int)(s * 0.15));
                break;
            }

            default: // web: dünya (boylam + enlem)
            {
                g.DrawEllipse(pen, x, y, s, s);
                g.DrawEllipse(pen, x + s / 3, y, s / 3, s);
                g.DrawLine(pen, x, y + s / 2, x + s, y + s / 2);
                g.DrawLine(pen, x + (int)(s * 0.07), y + s / 4, x + (int)(s * 0.93), y + s / 4);
                g.DrawLine(pen, x + (int)(s * 0.07), y + s * 3 / 4, x + (int)(s * 0.93), y + s * 3 / 4);
                break;
            }
        }
    }

    /// <summary>Saydam PNG'yi kutuya oranını koruyarak sığdırır.</summary>
    private void DrawLogoFit(Graphics g, string logoPath, int bx, int by, int bw, int bh, bool center)
    {
        try
        {
            if (!File.Exists(logoPath))
            {
                _log.Warn($"Logo bulunamadı: {logoPath}");
                return;
            }

            using var logo = new Bitmap(logoPath);
            var scale = Math.Min((double)bw / logo.Width, (double)bh / logo.Height);
            var drawWidth = (int)Math.Round(logo.Width * scale);
            var drawHeight = (int)Math.Round(logo.Height * scale);
            var dx = center ? bx + (bw - drawWidth) / 2 : bx;
            var dy = by + (bh - drawHeight) / 2;

            // Alfa korunur, beyaz kutu çıkmaz
            g.DrawImage(logo, dx, dy, drawWidth, drawHeight);
        }
        catch (Exception e)
        {
            _log.Warn($"Logo çizilemedi: {logoPath}", e);
        }
    }

    // === Kalibrasyon ===

    /// <summary>
    /// Kalibrasyon hedefi — yarım panel renkli bandın kartın neresine düştüğünü ölçer.
    ///
    /// Her 5 mm'de bir renkli çubuk ve yanında SİYAH mm etiketi var. Siyah
    /// etiketler K paneliyle her yere basılır, renkli çubuklar yalnızca YMC
    /// bandının denk geldiği yerde görünür. Basılan kartta hangi numaraların
    /// hizasında renk çıktıysa bant oradadır.
    /// </summary>
    public Bitmap RenderCalibration()
    {
        var width = CardWidthPx;
        var height = CardHeightPx;
        var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);

        using (var g = Graphics.FromImage(bitmap))
        {
            g.ApplyQualitySettings();
            g.Clear(Color.White);

            var labelWidth = Px(9.0);
            var barX = labelWidth + Px(1.0);
            var barWidth = width - barX - Px(2.0);
            var barHeight = Px(3.6);

            using var black = new SolidBrush(Color.Black);
            using var scaleFont = CreateFont(2.8, FontStyle.Bold);

            for (var mm = 5; mm <= 80; mm += 5)
            {
                var y = Px(mm);

                // Ardışık çubuklar farklı renkte: bandın kenarları da ayırt edilebilsin
                var barColor = mm / 5 % 2 == 0 ? Color.FromArgb(220, 0, 0) : Color.FromArgb(0, 110, 220);
                using (var barBrush = new SolidBrush(barColor))
                {
                    g.FillRectangle(barBrush, barX, y - barHeight / 2, barWidth, barHeight);
                }

                g.DrawStringAtBaseline(mm.ToString(), scaleFont, black, Px(2.0), y + Px(1.0));
                g.FillRectangle(black, labelWidth, y - Px(0.15), Px(1.0), Px(0.3));
            }

            using var titleFont = CreateFont(2.4, FontStyle.Bold);
            g.DrawCentered("KALIBRASYON", titleFont, black, width, Px(3.0));
            g.DrawCentered("renkli cikan mm araligi = bant", titleFont, black, width, Px(84.5));
        }

        return bitmap;
    }

    /// <summary>
    /// Arka yüz kalibrasyon hedefi: renkli mm cetveli + büyük yön işareti.
    /// Çift yüz baskıda arka yüzün bandının nereye düştüğünü ve ters mi düz mü
    /// çıktığını tek kartta gösterir.
    /// </summary>
    public Bitmap RenderCalibrationBack()
    {
        var bitmap = RenderCalibration();

        using (var g = Graphics.FromImage(bitmap))
        {
            g.ApplyQualitySettings();
            using var black = new SolidBrush(Color.Black);

            // Yön belirsizliği kalmasın
            using (var bigFont = CreateFont(4.0, FontStyle.Bold))
            {
                g.DrawCentered("ARKA UST ^", bigFont, black, CardWidthPx, Px(7.5));
            }
            using (var font = CreateFont(3.0, FontStyle.Bold))
            {
                g.DrawCentered("ALT", font, black, CardWidthPx, Px(80.0));
            }
        }

        return BackRotate180 ? GraphicsHelpers.Rotate180(bitmap) : bitmap;
    }

    // === Yardımcılar ===

    /// <summary>
    /// Yazı tipi oluştur.
    ///
    /// <b>GraphicsUnit.Pixel şart:</b> .NET'te yazı tipi boyutu varsayılan
    /// olarak <i>punto</i> cinsindendir; Java'da ise burada piksel olarak
    /// kullanılıyordu. Birim belirtilmezse tüm yazılar ~1,33 kat büyük çıkar.
    /// </summary>
    private static Font CreateFont(double sizeMm, FontStyle style, string family = "Arial")
        => new(family, Px(sizeMm), style, GraphicsUnit.Pixel);

    /// <summary>İki görüntüyü yan yana koy (önizleme için).</summary>
    private static Bitmap SideBySide(Bitmap left, Bitmap right, int gap)
    {
        var width = left.Width + gap + right.Width;
        var height = Math.Max(left.Height, right.Height);

        var result = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
        using var g = Graphics.FromImage(result);
        g.Clear(Color.White);
        g.DrawImage(left, 0, 0);
        g.DrawImage(right, left.Width + gap, 0);
        return result;
    }

    /// <summary>
    /// BMP olarak kaydet.
    ///
    /// Evolis SDK görseli dosya yolundan alıyor ve BMP bekliyor; format
    /// değiştirilirse baskı sessizce boş çıkabiliyor.
    /// </summary>
    private static void SaveBmp(Bitmap bitmap, string path)
    {
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Bmp);
    }
}
