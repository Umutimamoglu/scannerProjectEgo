using IdScanner.Core.Diagnostics;
using IdScanner.Core.Model;

namespace IdScanner.Chip.DataGroups;

/// <summary>
/// DG2'den yüz görüntüsünü çıkarır.
///
/// Java karşılığı: JMRTD'nin DG2File → FaceInfo → FaceImageInfo zinciri.
///
/// DG2'nin yapısı iç içe: CBEFF sarmalları (7F61 → 7F60 → 5F2E/7F2E) içinde
/// ISO/IEC 19794-5 kaydı, onun içinde de asıl JPEG veya JPEG2000 verisi var.
///
/// <b>Neden iki yöntem:</b> Önce başlık alanları düzgün çözülmeye çalışılır.
/// Kartlar bu başlıklarda sürüm farklılıkları gösterebildiği için, çözüm
/// tutmazsa görüntü imzasını (magic number) tarayan yedek yönteme düşülür.
/// Hangi yolun kullanıldığı loglanır — fotoğraf bozuk çıkarsa buradan bakılır.
/// </summary>
internal static class FaceImageExtractor
{
    /// <summary>JPEG dosya imzası.</summary>
    private static readonly byte[] JpegMagic = [0xFF, 0xD8, 0xFF];

    /// <summary>JPEG2000 kapsayıcı (JP2) imzası.</summary>
    private static readonly byte[] Jp2Magic = [0x00, 0x00, 0x00, 0x0C, 0x6A, 0x50, 0x20, 0x20];

    /// <summary>JPEG2000 ham kod akışı (J2K) imzası.</summary>
    private static readonly byte[] J2kMagic = [0xFF, 0x4F, 0xFF, 0x51];

    /// <summary>ISO/IEC 19794-5 kayıt başlığı: "FAC" + 0x00.</summary>
    private static readonly byte[] FacMagic = [0x46, 0x41, 0x43, 0x00];

    /// <summary>
    /// DG2'nin ham içeriğinden yüz görüntüsünü çıkar.
    /// </summary>
    /// <returns>Görüntü ve kodlaması; bulunamazsa <c>null</c>.</returns>
    internal static FaceImage? Extract(byte[] dg2, IAppLogger logger)
    {
        var log = logger.ForComponent("Dg2");

        // ISO 19794-5 kaydını bul — TLV sarmallarını tek tek soymak yerine
        // "FAC\0" imzasını aramak, sürüm farklarına karşı dayanıklı.
        var facOffset = IndexOf(dg2, FacMagic);
        if (facOffset >= 0)
        {
            log.Trace($"ISO 19794-5 kaydı {facOffset}. baytta bulundu");
        }
        else
        {
            log.Warn("ISO 19794-5 ('FAC') başlığı bulunamadı — doğrudan görüntü imzası aranacak");
        }

        var searchFrom = facOffset >= 0 ? facOffset : 0;
        var image = FindImage(dg2, searchFrom, log);

        if (image is null)
        {
            log.Error($"DG2 içinde tanınan görüntü verisi yok ({dg2.Length} bayt). " +
                      $"İlk baytlar:{Environment.NewLine}{Hex.Dump(dg2, 64)}");
            return null;
        }

        log.Info($"Yüz görüntüsü çıkarıldı: {image.Format}, {image.Bytes.Length} bayt");
        return image;
    }

    /// <summary>Bilinen görüntü imzalarından ilkini bul ve sonuna kadar al.</summary>
    private static FaceImage? FindImage(byte[] data, int from, IAppLogger log)
    {
        (byte[] Magic, FaceImageFormat Format, string Label)[] candidates =
        [
            (Jp2Magic, FaceImageFormat.Jpeg2000, "JPEG2000 (JP2 kapsayıcı)"),
            (J2kMagic, FaceImageFormat.Jpeg2000, "JPEG2000 (ham kod akışı)"),
            (JpegMagic, FaceImageFormat.Jpeg, "JPEG"),
        ];

        var best = -1;
        FaceImageFormat bestFormat = FaceImageFormat.Unknown;
        var bestLabel = "";

        foreach (var (magic, format, label) in candidates)
        {
            var index = IndexOf(data, magic, from);
            if (index < 0) continue;

            log.Trace($"{label} imzası {index}. baytta");

            // En erken başlayan aday doğru olandır — sonrakiler görüntünün
            // içinde tesadüfen oluşan bayt dizileri olabilir.
            if (best < 0 || index < best)
            {
                best = index;
                bestFormat = format;
                bestLabel = label;
            }
        }

        if (best < 0) return null;

        log.Debug($"Seçilen kodlama: {bestLabel}, {best}. bayttan itibaren");
        return new FaceImage(data[best..], bestFormat);
    }

    /// <summary>Bayt dizisi içinde alt dizi ara.</summary>
    private static int IndexOf(byte[] haystack, byte[] needle, int from = 0)
    {
        var span = haystack.AsSpan(Math.Min(from, haystack.Length));
        var index = span.IndexOf(needle);
        return index < 0 ? -1 : index + Math.Min(from, haystack.Length);
    }
}
