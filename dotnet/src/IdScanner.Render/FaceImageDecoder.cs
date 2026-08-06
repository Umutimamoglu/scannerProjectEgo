using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using CSJ2K;
using CSJ2K.Util;
using IdScanner.Core.Diagnostics;
using IdScanner.Core.Model;

namespace IdScanner.Render;

/// <summary>
/// Çipten gelen yüz görüntüsünü çözer.
///
/// Java'da bu iş <c>ImageIO.read</c> ve <c>jai-imageio-jpeg2000</c> eklentisi
/// tarafından yapılıyordu. .NET JPEG'i kendi çözer ama JPEG2000 için ayrı bir
/// çözücü gerekiyor — CSJ2K kullanılıyor.
///
/// <b>Bu sınıf, taşımanın en riskli parçasıydı:</b> fotoğraf çözülemezse karta
/// basılacak görüntü olmaz. Bu yüzden başarısızlıkta hangi aşamada, hangi
/// biçimde ve kaç baytta takıldığı ayrıntılı loglanıyor.
/// </summary>
public static class FaceImageDecoder
{
    private static readonly Lock RegistrationGate = new();
    private static bool _imageCreatorRegistered;

    /// <summary>
    /// Yüz görüntüsünü <see cref="Bitmap"/>'e çevir.
    /// </summary>
    /// <returns>Çözülen görüntü; çözülemezse <c>null</c>.</returns>
    public static Bitmap? Decode(FaceImage image, IAppLogger? logger = null)
    {
        var log = (logger ?? NullLogger.Instance).ForComponent("Photo");

        using var op = log.BeginOperation($"Yüz görüntüsü çözme ({image.Format})");
        try
        {
            var bitmap = image.Format switch
            {
                FaceImageFormat.Jpeg => DecodeJpeg(image.Bytes, log),
                FaceImageFormat.Jpeg2000 => DecodeJpeg2000(image.Bytes, log),
                _ => DecodeUnknown(image.Bytes, log),
            };

            if (bitmap is null)
            {
                op.Failure("çözülemedi");
                return null;
            }

            op.Success($"{bitmap.Width}×{bitmap.Height}");
            return bitmap;
        }
        catch (Exception e)
        {
            op.Failed(e);
            log.Error($"Görüntü çözülemedi. İlk baytlar:{Environment.NewLine}{Hex.Dump(image.Bytes, 48)}");
            return null;
        }
    }

    /// <summary>
    /// Görüntüyü çöz ve PNG olarak dosyaya yaz.
    /// </summary>
    /// <returns>Yazılan dosyanın yolu; çözülemezse <c>null</c>.</returns>
    public static string? DecodeToFile(FaceImage image, string path, IAppLogger? logger = null)
    {
        using var bitmap = Decode(image, logger);
        if (bitmap is null) return null;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    /// <summary>JPEG — .NET kendi çözer.</summary>
    private static Bitmap DecodeJpeg(byte[] bytes, IAppLogger log)
    {
        log.Trace($"JPEG çözülüyor ({bytes.Length} bayt)");
        using var stream = new MemoryStream(bytes);

        // Bitmap akışa bağlı kalır; kopyalayıp akışı serbest bırakıyoruz
        using var decoded = new Bitmap(stream);
        return new Bitmap(decoded);
    }

    /// <summary>
    /// JPEG2000 — CSJ2K ile çözülür.
    ///
    /// CSJ2K çıktısını <c>IImageCreator</c> üzerinden verir; netstandard
    /// sürümünde System.Drawing'e bağlı bir uygulama gelmediği için
    /// <see cref="BitmapImageCreator"/> burada yazıldı ve bir kez kaydediliyor.
    /// </summary>
    private static Bitmap? DecodeJpeg2000(byte[] bytes, IAppLogger log)
    {
        EnsureImageCreatorRegistered(log);

        log.Trace($"JPEG2000 çözülüyor ({bytes.Length} bayt)");
        var portable = J2kImage.FromBytes(bytes);

        var bitmap = portable.As<Bitmap>();
        if (bitmap is null) log.Error("CSJ2K görüntüyü çözdü ama Bitmap'e çevrilemedi");
        return bitmap;
    }

    /// <summary>
    /// Biçim tanınamadıysa ikisini de dene.
    ///
    /// Kart beklenmedik bir sarmalama kullanıyor olabilir; tahmin etmek
    /// yerine denemek, fotoğrafsız kalmaktan iyidir.
    /// </summary>
    private static Bitmap? DecodeUnknown(byte[] bytes, IAppLogger log)
    {
        log.Warn("Görüntü biçimi belirlenemedi — JPEG ve JPEG2000 sırayla denenecek");

        try
        {
            return DecodeJpeg(bytes, log);
        }
        catch (Exception e)
        {
            log.Trace($"JPEG denemesi başarısız: {e.Message}");
        }

        try
        {
            return DecodeJpeg2000(bytes, log);
        }
        catch (Exception e)
        {
            log.Trace($"JPEG2000 denemesi başarısız: {e.Message}");
        }

        return null;
    }

    /// <summary>CSJ2K'ya System.Drawing köprüsünü bir kez tanıt.</summary>
    private static void EnsureImageCreatorRegistered(IAppLogger log)
    {
        lock (RegistrationGate)
        {
            if (_imageCreatorRegistered) return;
            ImageFactory.Register(new BitmapImageCreator());
            _imageCreatorRegistered = true;
            log.Debug("CSJ2K görüntü üreticisi kaydedildi (System.Drawing köprüsü)");
        }
    }

    /// <summary>
    /// CSJ2K'nın çözdüğü ham pikselleri <see cref="Bitmap"/>'e çevirir.
    ///
    /// CSJ2K bize genişlik, yükseklik ve 32 bit ARGB bayt dizisi verir;
    /// tek yapılması gereken bunu bir bitmap'e kopyalamak.
    /// </summary>
    private sealed class BitmapImageCreator : IImageCreator
    {
        /// <summary>Bu üretici System.Drawing'e bağlı, yani yalnızca Windows'ta.</summary>
        public bool IsDefault => false;

        public IImage Create(int width, int height, byte[] bytes)
            => new BitmapImage(width, height, bytes);

        /// <summary>Kodlama yönü kullanılmıyor — yalnızca çözme yapıyoruz.</summary>
        public CSJ2K.j2k.image.BlkImgDataSrc ToPortableImageSource(object imageObject)
            => throw new NotSupportedException("Bu uygulama JPEG2000 kodlaması yapmaz, yalnızca çözer");
    }

    /// <summary>CSJ2K'nın beklediği görüntü sarmalı.</summary>
    private sealed class BitmapImage(int width, int height, byte[] bytes) : IImage
    {
        public T As<T>()
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);

            var rect = new Rectangle(0, 0, width, height);
            var data = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                // Satır atlaması (stride) genişlikten büyük olabilir; satır satır kopyala
                var bytesPerRow = width * 4;
                for (var y = 0; y < height; y++)
                {
                    Marshal.Copy(bytes, y * bytesPerRow, data.Scan0 + y * data.Stride, bytesPerRow);
                }
            }
            finally
            {
                bitmap.UnlockBits(data);
            }

            return (T)(object)bitmap;
        }
    }
}
