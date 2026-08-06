using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace IdScanner.Render;

/// <summary>
/// Java <c>Graphics2D</c> ile .NET <c>Graphics</c> arasındaki davranış
/// farklarını kapatan yardımcılar.
///
/// <b>En önemli fark — metin konumu:</b> Java'da <c>drawString(s, x, y)</c>
/// çağrısındaki <c>y</c>, yazının <b>taban çizgisidir</b> (baseline).
/// .NET'te <c>DrawString</c> ise <c>y</c>'yi metin kutusunun <b>üst kenarı</b>
/// sayar. Bu düzeltilmezse tüm yazılar bir satır yüksekliği kadar aşağı kayar
/// ve kart tasarımı bozulur. Buradaki
/// <see cref="DrawStringAtBaseline"/> farkı kapatıyor.
///
/// <b>İkinci fark — ölçüm:</b> <c>MeasureString</c> varsayılan olarak metnin
/// iki yanına boşluk ekler; Java'nın <c>stringWidth</c>'i eklemez. Ölçümler
/// bu yüzden tipografik biçimle yapılıyor.
/// </summary>
internal static class GraphicsHelpers
{
    /// <summary>Java'nın <c>stringWidth</c>'i gibi davranan ölçüm biçimi.</summary>
    private static readonly StringFormat TypographicFormat = new(StringFormat.GenericTypographic)
    {
        FormatFlags = StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap,
    };

    /// <summary>Kart çizimi için ortak kalite ayarları (Java'daki RenderingHints karşılığı).</summary>
    internal static void ApplyQualitySettings(this Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.CompositingQuality = CompositingQuality.HighQuality;
    }

    /// <summary>
    /// Metni <b>taban çizgisi</b> verilen konuma çizer — Java'daki
    /// <c>drawString</c> ile aynı davranış.
    /// </summary>
    internal static void DrawStringAtBaseline(
        this Graphics g, string text, Font font, Brush brush, float x, float baselineY)
    {
        if (string.IsNullOrEmpty(text)) return;
        g.DrawString(text, font, brush, x, baselineY - Ascent(font), TypographicFormat);
    }

    /// <summary>Metni yatayda ortalayıp taban çizgisine oturtur.</summary>
    internal static void DrawCentered(
        this Graphics g, string text, Font font, Brush brush, int width, float baselineY)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        var textWidth = g.MeasureWidth(text, font);
        g.DrawStringAtBaseline(text, font, brush, (width - textWidth) / 2f, baselineY);
    }

    /// <summary>Java'nın <c>FontMetrics.stringWidth</c> karşılığı.</summary>
    internal static float MeasureWidth(this Graphics g, string text, Font font)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        return g.MeasureString(text, font, int.MaxValue, TypographicFormat).Width;
    }

    /// <summary>Yazı tipinin taban çizgisi üstündeki yüksekliği (piksel).</summary>
    private static float Ascent(Font font)
    {
        var family = font.FontFamily;
        var style = font.Style;
        var emHeight = family.GetEmHeight(style);
        var ascent = family.GetCellAscent(style);
        return font.Size * ascent / emHeight;
    }

    /// <summary>
    /// Yuvarlatılmış dikdörtgen yolu — Java'daki <c>RoundRectangle2D</c> karşılığı.
    /// Java yay <b>çaplarını</b> alır, .NET ise yay kutusu; dönüşüm burada yapılır.
    /// </summary>
    internal static GraphicsPath RoundedRectangle(float x, float y, float w, float h, float arcW, float arcH)
    {
        var path = new GraphicsPath();

        // Yay çapları kenarı aşmasın
        arcW = Math.Min(arcW, w);
        arcH = Math.Min(arcH, h);

        if (arcW <= 0 || arcH <= 0)
        {
            path.AddRectangle(new RectangleF(x, y, w, h));
            return path;
        }

        path.AddArc(x, y, arcW, arcH, 180, 90);
        path.AddArc(x + w - arcW, y, arcW, arcH, 270, 90);
        path.AddArc(x + w - arcW, y + h - arcH, arcW, arcH, 0, 90);
        path.AddArc(x, y + h - arcH, arcW, arcH, 90, 90);
        path.CloseFigure();
        return path;
    }

    /// <summary>Görüntüyü 180° döndür — çift yüz baskıda arka yön düzeltmesi.</summary>
    internal static Bitmap Rotate180(Bitmap source)
    {
        var result = (Bitmap)source.Clone();
        result.RotateFlip(RotateFlipType.Rotate180FlipNone);
        return result;
    }

    /// <summary>Bitmap'i PNG baytlarına çevir.</summary>
    internal static byte[] ToPng(this Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
        return stream.ToArray();
    }
}
