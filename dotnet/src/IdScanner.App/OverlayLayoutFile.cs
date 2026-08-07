using System.Globalization;
using IdScanner.Core;
using IdScanner.Core.Diagnostics;
using IdScanner.Core.Model;

namespace IdScanner.App;

/// <summary>
/// Hazır kart yerleşim ölçülerini metin dosyasından okur.
///
/// <b>Neden dosya:</b> Ölçüler matbaa baskısına birebir oturmak zorunda ve
/// ilk denemelerde kaydırma yapılacak. Her ayar için yeniden derlemek yerine
/// <c>overlay-layout.txt</c> düzenlenip uygulama yeniden başlatılıyor.
///
/// Biçim — satır başına <c>anahtar = değer</c>, <c>#</c> ile yorum:
/// <code>
///   photo_x  = 4.0
///   photo_y  = 6.0
///   photo_w  = 20.0
///   photo_h  = 26.0
///   name_x   = 14.0
///   name_y   = 36.0
///   surname_x = 14.0
///   surname_y = 42.0
///   text_size = 3.0
/// </code>
/// </summary>
internal static class OverlayLayoutFile
{
    private const string FileName = "overlay-layout.txt";

    /// <summary>Ölçüleri oku; dosya yoksa oluştur ve varsayılanı döndür.</summary>
    internal static OverlayLayout Load(IAppLogger logger)
    {
        var log = logger.ForComponent("Yerlesim");
        var path = Path.Combine(AppPaths.Base, FileName);

        if (!File.Exists(path))
        {
            WriteTemplate(path, log);
            return OverlayLayout.Default;
        }

        try
        {
            var values = ReadValues(path);
            var layout = Apply(values);
            log.Info($"Hazır kart ölçüleri okundu: {path}");
            log.Debug($"  foto {layout.PhotoXMm}×{layout.PhotoYMm} mm, " +
                      $"{layout.PhotoWidthMm}×{layout.PhotoHeightMm} mm · " +
                      $"ad {layout.NameXMm},{layout.NameYMm} · " +
                      $"soyad {layout.SurnameXMm},{layout.SurnameYMm}");
            return layout;
        }
        catch (Exception e)
        {
            log.Error($"{FileName} okunamadı, varsayılan ölçüler kullanılıyor", e);
            return OverlayLayout.Default;
        }
    }

    private static Dictionary<string, double> ReadValues(string path)
    {
        var values = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            var separator = line.IndexOf('=');
            if (separator <= 0) continue;

            var key = line[..separator].Trim();
            var text = line[(separator + 1)..].Trim();

            // Ondalık ayırıcı hem nokta hem virgül kabul edilir — kullanıcı
            // Türkçe klavyeyle virgül yazarsa dosya bozulmasın.
            text = text.Replace(',', '.');

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                values[key] = value;
            }
        }

        return values;
    }

    private static OverlayLayout Apply(Dictionary<string, double> v)
    {
        var d = OverlayLayout.Default;
        return d with
        {
            PhotoXMm = Get(v, "photo_x", d.PhotoXMm),
            PhotoYMm = Get(v, "photo_y", d.PhotoYMm),
            PhotoWidthMm = Get(v, "photo_w", d.PhotoWidthMm),
            PhotoHeightMm = Get(v, "photo_h", d.PhotoHeightMm),
            NameXMm = Get(v, "name_x", d.NameXMm),
            NameYMm = Get(v, "name_y", d.NameYMm),
            SurnameXMm = Get(v, "surname_x", d.SurnameXMm),
            SurnameYMm = Get(v, "surname_y", d.SurnameYMm),
            TextSizeMm = Get(v, "text_size", d.TextSizeMm),
        };
    }

    private static double Get(Dictionary<string, double> values, string key, double fallback)
        => values.TryGetValue(key, out var value) ? value : fallback;

    /// <summary>Dosya yoksa açıklamalı bir şablon bırak.</summary>
    private static void WriteTemplate(string path, IAppLogger log)
    {
        var d = OverlayLayout.Default;
        var template = $"""
            # Hazır basılı kart üzerine baskı ölçüleri (mm)
            #
            # Kartın sol üst köşesi (0,0). X sağa, Y aşağı artar.
            # Kart ölçüsü: 54 x 85,6 mm (ISO 7810 ID-1, dikey)
            #
            # Değiştirdikten sonra uygulamayı yeniden başlatın.
            # Ondalık ayırıcı olarak nokta veya virgül kullanabilirsiniz.

            # Fotoğraf kutusu — matbaa baskısındaki boş beyaz alan
            photo_x   = {d.PhotoXMm}
            photo_y   = {d.PhotoYMm}
            photo_w   = {d.PhotoWidthMm}
            photo_h   = {d.PhotoHeightMm}

            # "Adı:" etiketinin yanına yazılacak değerin konumu
            # (y = yazının TABAN çizgisi)
            name_x    = {d.NameXMm}
            name_y    = {d.NameYMm}

            # "Soyadı:" etiketinin yanına yazılacak değerin konumu
            surname_x = {d.SurnameXMm}
            surname_y = {d.SurnameYMm}

            # Ad/soyad yazı boyutu
            text_size = {d.TextSizeMm}
            """;

        try
        {
            File.WriteAllText(path, template);
            log.Info($"Ölçü dosyası oluşturuldu: {path} — ilk baskıdan sonra buradan ayarlayın");
        }
        catch (Exception e)
        {
            log.Warn($"Ölçü dosyası oluşturulamadı: {path}", e);
        }
    }
}
