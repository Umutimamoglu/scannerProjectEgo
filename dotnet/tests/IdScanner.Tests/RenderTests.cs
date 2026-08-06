using System.Drawing;
using System.Text;
using CSJ2K;
using IdScanner.Core.Model;
using IdScanner.Render;

namespace IdScanner.Tests;

/// <summary>
/// Çizim ve görüntü çözme testleri.
///
/// Bu katman donanım gerektirmediği için taşımanın doğruluğu burada gerçekten
/// sınanabiliyor.
/// </summary>
public class RenderTests
{
    // === JPEG2000 — planın en büyük riskiydi ===

    /// <summary>
    /// JPEG2000 gidiş-dönüş: küçük bir görüntü kodlanıp geri çözülüyor.
    ///
    /// <b>Bu test neden var:</b> DG2'deki yüz fotoğrafı JPEG2000 kodlu geliyor
    /// ve .NET bunu kendi çözemiyor. Çözücü çalışmazsa karta basılacak fotoğraf
    /// olmaz. Test, CSJ2K köprüsünün (System.Drawing'e bağlanan
    /// IImageCreator uygulaması) gerçekten iş gördüğünü kanıtlıyor.
    /// </summary>
    [Fact]
    public void Jpeg2000_cozulebiliyor()
    {
        const int width = 32;
        const int height = 24;

        var j2kBytes = EncodeTestImageAsJpeg2000(width, height);
        Assert.NotEmpty(j2kBytes);

        var image = new FaceImage(j2kBytes, FaceImageFormat.Jpeg2000);
        using var decoded = FaceImageDecoder.Decode(image);

        Assert.NotNull(decoded);
        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);
    }

    [Fact]
    public void Jpeg_cozulebiliyor()
    {
        using var source = new Bitmap(20, 16);
        using (var g = Graphics.FromImage(source)) g.Clear(Color.CornflowerBlue);

        using var stream = new MemoryStream();
        source.Save(stream, System.Drawing.Imaging.ImageFormat.Jpeg);

        var image = new FaceImage(stream.ToArray(), FaceImageFormat.Jpeg);
        using var decoded = FaceImageDecoder.Decode(image);

        Assert.NotNull(decoded);
        Assert.Equal(20, decoded.Width);
        Assert.Equal(16, decoded.Height);
    }

    [Fact]
    public void Bozuk_goruntu_null_doner_ve_cokmez()
    {
        var image = new FaceImage([1, 2, 3, 4, 5], FaceImageFormat.Jpeg2000);
        Assert.Null(FaceImageDecoder.Decode(image));
    }

    /// <summary>
    /// CSJ2K'nın kodlayıcısıyla test görüntüsü üret.
    ///
    /// Kodlayıcı girdiyi PPM (P6) biçiminde bekliyor — çözücüyü sınamak için
    /// elde JPEG2000 örneği olmadığından burada üretiliyor.
    /// </summary>
    private static byte[] EncodeTestImageAsJpeg2000(int width, int height)
    {
        using var ppm = new MemoryStream();

        var header = Encoding.ASCII.GetBytes($"P6\n{width} {height}\n255\n");
        ppm.Write(header, 0, header.Length);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                ppm.WriteByte((byte)(x * 255 / width));
                ppm.WriteByte((byte)(y * 255 / height));
                ppm.WriteByte(128);
            }
        }

        ppm.Position = 0;
        var source = J2kImage.CreateEncodableSource(ppm);
        return J2kImage.ToBytes(source, J2kImage.GetDefaultEncoderParameterList(null));
    }

    // === Kart çizimi ===

    private static CardData SampleCard() => new()
    {
        Name = "AHMET MEHMET",
        Surname = "YILMAZ",
        IdNumber = "12345678901",
        BirthDate = "01.01.1980",
        ExpiryDate = "01.01.2030",
    };

    /// <summary>ISO 7810 ID-1, 300 DPI, dikey: 54 × 85,6 mm → 638 × 1011 piksel.</summary>
    private const int ExpectedWidth = 638;
    private const int ExpectedHeight = 1011;

    [Fact]
    public void On_yuz_dogru_olcude_ciziliyor()
    {
        var renderer = new CardRenderer();
        using var bitmap = renderer.RenderFront(SampleCard());

        Assert.Equal(ExpectedWidth, bitmap.Width);
        Assert.Equal(ExpectedHeight, bitmap.Height);
    }

    [Fact]
    public void Arka_yuz_dogru_olcude_ciziliyor()
    {
        var renderer = new CardRenderer();
        using var bitmap = renderer.RenderBack();

        Assert.Equal(ExpectedWidth, bitmap.Width);
        Assert.Equal(ExpectedHeight, bitmap.Height);
    }

    [Fact]
    public void On_yuz_bos_alanlarla_da_cizilebiliyor()
    {
        // Çip okunamayıp alanlar boş kalırsa çizim yine de çalışmalı
        var renderer = new CardRenderer();
        using var bitmap = renderer.RenderFront(new CardData());
        Assert.Equal(ExpectedWidth, bitmap.Width);
    }

    [Fact]
    public void Baski_hali_okuma_halinin_dondurulmusu()
    {
        var renderer = new CardRenderer();
        using var reading = renderer.RenderBack();
        using var printing = renderer.RenderBackForPrint();

        // Aynı ölçü, ama karşılıklı köşeler yer değiştirmiş olmalı
        Assert.Equal(reading.Width, printing.Width);
        Assert.Equal(reading.Height, printing.Height);

        var topLeft = reading.GetPixel(0, 0);
        var bottomRight = printing.GetPixel(printing.Width - 1, printing.Height - 1);
        Assert.Equal(topLeft, bottomRight);
    }

    [Fact]
    public void Png_ciktisi_uretiliyor()
    {
        var renderer = new CardRenderer();
        var png = renderer.RenderFrontPng(SampleCard());

        Assert.NotEmpty(png);
        // PNG dosya imzası
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png[..4]);
    }

    // === JavaRandom ===

    /// <summary>
    /// Arka yüzdeki desen sabit tohumlu rastgeleliğe dayanıyor; .NET'in kendi
    /// üreteci farklı sayılar verdiği için Java'nınki birebir kopyalandı.
    ///
    /// Aşağıdaki değerler <b>gerçek JDK 17 çalıştırılarak</b> alındı
    /// (<c>new java.util.Random(7)</c>), ezberden yazılmadı.
    /// </summary>
    [Fact]
    public void JavaRandom_java_ile_ayni_diziyi_uretiyor()
    {
        var random = new JavaRandom(7);

        Assert.Equal(0.7306990420600421, random.NextDouble(), 15);
        Assert.Equal(0.7491696031336331, random.NextDouble(), 15);
        Assert.Equal(0.34830970303125697, random.NextDouble(), 15);
    }

    /// <summary>
    /// <c>nextInt(bound)</c> mesh alfa değerlerinde kullanılıyor; sapması
    /// desenin saydamlığını değiştirir.
    /// </summary>
    [Fact]
    public void JavaRandom_nextInt_java_ile_ayni()
    {
        var random = new JavaRandom(7);
        int[] expected = [16, 2, 15, 4, 10];

        foreach (var value in expected)
        {
            Assert.Equal(value, random.NextInt(18));
        }
    }

    [Fact]
    public void JavaRandom_ayni_tohumla_ayni_diziyi_tekrarliyor()
    {
        var first = new JavaRandom(7);
        var second = new JavaRandom(7);

        for (var i = 0; i < 20; i++)
        {
            Assert.Equal(first.NextDouble(), second.NextDouble());
        }
    }
}
