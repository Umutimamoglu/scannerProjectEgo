using System.Drawing;
using System.Drawing.Drawing2D;

namespace IdScanner.Render;

/// <summary>
/// Matbaada basılmış Başkent Kart'ın <b>önizleme taklidi</b>.
///
/// <b>Ne işe yarar:</b> Hazır karta basarken ekranda beyaz bir zemin görmek
/// hizayı denetlemeye yetmiyor. Buradaki çizim, gerçek kartın kırmızı zeminini,
/// fotoğraf kutusunu ve "Adı:" / "Soyadı:" etiketlerini yaklaşık olarak
/// canlandırır; böylece basılacak içeriğin kutuya oturup oturmadığı kart
/// harcamadan görülür.
///
/// <b>ASLA basılmaz.</b> Yalnızca <see cref="Core.Model.OverlayLayout.ShowGuides"/>
/// açıkken çizilir, baskıda bu bayrak kapalıdır.
///
/// <b>Buradaki ölçüler kartın gerçeğidir</b>, yerleşim ayarı değildir:
/// <see cref="Core.Model.OverlayLayout"/> nereye BASACAĞIMIZI, bu sınıf kartta
/// NE OLDUĞUNU söyler. İkisi önizlemede üst üste gelmiyorsa düzeltilecek olan
/// yerleşim dosyasıdır, bu sınıf değildir.
/// </summary>
internal static class PreprintedCardMockup
{
    // === Kart üzerindeki gerçek öğelerin konumu (mm, sol üst köşe 0,0) ===
    // Telefonla çekilen kart fotoğrafından ölçüldü; perspektif payı vardır.

    /// <summary>Üstteki otobüs görsellerinin kapladığı bant.</summary>
    private const double ArtworkTopBandMm = 30.0;

    /// <summary>"Başkent Kart" logosunun bulunduğu satır.</summary>
    private const double LogoBaselineMm = 44.0;

    /// <summary>Boş fotoğraf kutusu — basılacak fotoğrafın hedefi.</summary>
    private const double BoxXMm = 4.0;
    private const double BoxYMm = 50.0;
    private const double BoxWidthMm = 14.5;
    private const double BoxHeightMm = 15.5;

    /// <summary>"Adı:" ve "Soyadı:" etiketlerinin sol kenarı.</summary>
    private const double LabelXMm = 25.0;
    private const double NameLabelBaselineMm = 61.0;
    private const double SurnameLabelBaselineMm = 65.5;
    private const double LabelSizeMm = 2.6;

    /// <summary>Alttaki kurum logolarının bandı.</summary>
    private const double FooterTopMm = 74.0;

    private static readonly Color CardRed = Color.FromArgb(196, 30, 42);
    private static readonly Color CardRedDark = Color.FromArgb(158, 22, 32);
    private static readonly Color BoxFill = Color.FromArgb(214, 229, 233);

    /// <summary>
    /// Taklit kartı çizer.
    /// </summary>
    /// <param name="g">Hedef yüzey.</param>
    /// <param name="cardWidthMm">Kart genişliği.</param>
    /// <param name="cardHeightMm">Kart yüksekliği.</param>
    /// <param name="px">Milimetreyi piksele çeviren dönüşüm (çağıran DPI'ı bilir).</param>
    internal static void Draw(Graphics g, double cardWidthMm, double cardHeightMm, Func<double, int> px)
    {
        DrawBackground(g, cardWidthMm, cardHeightMm, px);
        DrawTopBand(g, cardWidthMm, px);
        DrawLogo(g, cardWidthMm, px);
        DrawPhotoBox(g, px);
        DrawLabels(g, px);
        DrawFooter(g, cardWidthMm, cardHeightMm, px);
    }

    private static void DrawBackground(Graphics g, double widthMm, double heightMm, Func<double, int> px)
    {
        using var brush = new LinearGradientBrush(
            new Rectangle(0, 0, px(widthMm), px(heightMm)),
            CardRed, CardRedDark, LinearGradientMode.Vertical);

        g.FillRectangle(brush, 0, 0, px(widthMm), px(heightMm));
    }

    /// <summary>Üstteki otobüs fotoğrafları — soyut lekelerle temsil edilir.</summary>
    private static void DrawTopBand(Graphics g, double widthMm, Func<double, int> px)
    {
        using var band = new SolidBrush(Color.FromArgb(60, 255, 255, 255));
        g.FillRectangle(band, 0, 0, px(widthMm), px(ArtworkTopBandMm));

        using var shape = new SolidBrush(Color.FromArgb(90, 255, 255, 255));
        for (var i = 0; i < 3; i++)
        {
            var x = 3.0 + i * 16.0;
            g.FillRectangle(shape, px(x), px(9.0), px(14.0), px(14.0));
        }

        using var caption = new Font("Arial", px(1.9), FontStyle.Italic, GraphicsUnit.Pixel);
        using var white = new SolidBrush(Color.FromArgb(200, 255, 255, 255));
        g.DrawString("(otobüs görselleri)", caption, white, px(3.0), px(24.5));
    }

    private static void DrawLogo(Graphics g, double widthMm, Func<double, int> px)
    {
        using var font = new Font("Arial", px(4.6), FontStyle.Bold, GraphicsUnit.Pixel);
        using var white = new SolidBrush(Color.White);

        var text = "Başkent Kart";
        var width = g.MeasureString(text, font).Width;
        g.DrawString(text, font, white, (px(widthMm) - width) / 2f, px(LogoBaselineMm - 4.6));
    }

    private static void DrawPhotoBox(Graphics g, Func<double, int> px)
    {
        using var fill = new SolidBrush(BoxFill);
        using var edge = new Pen(Color.FromArgb(120, 255, 255, 255), 1f);

        var rect = new Rectangle(px(BoxXMm), px(BoxYMm), px(BoxWidthMm), px(BoxHeightMm));
        g.FillRectangle(fill, rect);
        g.DrawRectangle(edge, rect);
    }

    private static void DrawLabels(Graphics g, Func<double, int> px)
    {
        using var font = new Font("Arial", px(LabelSizeMm), FontStyle.Regular, GraphicsUnit.Pixel);
        using var white = new SolidBrush(Color.White);

        // DrawString üstten hizalar; etiket taban çizgisinden yukarı kaydırılır.
        g.DrawString("Adı:", font, white, px(LabelXMm), px(NameLabelBaselineMm - LabelSizeMm));
        g.DrawString("Soyadı:", font, white, px(LabelXMm), px(SurnameLabelBaselineMm - LabelSizeMm));
    }

    private static void DrawFooter(Graphics g, double widthMm, double heightMm, Func<double, int> px)
    {
        using var brush = new SolidBrush(Color.FromArgb(70, 255, 255, 255));
        g.FillRectangle(brush, px(6.0), px(FooterTopMm), px(widthMm - 12.0), px(heightMm - FooterTopMm - 4.0));

        using var font = new Font("Arial", px(1.9), FontStyle.Italic, GraphicsUnit.Pixel);
        using var white = new SolidBrush(Color.FromArgb(210, 255, 255, 255));
        g.DrawString("(kurum logoları)", font, white, px(8.0), px(FooterTopMm + 2.0));
    }
}
