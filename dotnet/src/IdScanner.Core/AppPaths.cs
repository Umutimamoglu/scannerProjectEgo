namespace IdScanner.Core;

/// <summary>
/// Uygulama dosyalarının kök dizinini çözer.
///
/// Neden gerekli: kod <c>native_x64</c>, <c>native_evolis</c>, <c>output</c> gibi
/// yolları göreli kullanıyor. Bunlar çalışma dizinine göre çözülseydi, exe'ye
/// çift tıklandığında çalışma dizini bambaşka bir yer olabilir ve DLL'ler
/// bulunamazdı.
///
/// Java karşılığı: AppPaths.java. Orada jpackage'ın <c>jpackage.app-path</c>
/// sistem özelliğine bakmak gerekiyordu; .NET'te <see cref="AppContext.BaseDirectory"/>
/// bunu hazır veriyor — hem geliştirmede hem yayınlanmış exe'de doğru sonucu
/// döndürüyor, ayrı bir "paketlenmiş mi" kontrolüne gerek kalmıyor.
/// </summary>
public static class AppPaths
{
    /// <summary>Uygulamanın kök dizini — native klasörleri ve çıktılar buna göre.</summary>
    public static string Base { get; } = ResolveBase();

    private static string ResolveBase()
    {
        // Yayınlanmış tek dosya (single-file) yayınında da exe'nin bulunduğu
        // dizini verir; geliştirmede bin/Debug/... altını verir.
        var dir = AppContext.BaseDirectory;
        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(dir));
    }

    /// <summary>Kök dizine göre bir alt yol.</summary>
    public static string Resolve(params string[] parts)
    {
        var all = new string[parts.Length + 1];
        all[0] = Base;
        Array.Copy(parts, 0, all, 1, parts.Length);
        return Path.GetFullPath(Path.Combine(all));
    }

    /// <summary>Yoksa oluşturur ve döndürür.</summary>
    public static string EnsureDir(params string[] parts)
    {
        var p = Resolve(parts);
        try
        {
            Directory.CreateDirectory(p);
        }
        catch (Exception)
        {
            // Yazma izni yoksa çağıran taraf zaten hata alacak — burada
            // yutmak, klasörü kullanmayan akışların çalışmaya devam etmesi için.
        }
        return p;
    }

    /// <summary>Tanılama için — hangi kök kullanılıyor ve native klasör yerinde mi.</summary>
    public static string Describe()
    {
        var nativeOk = Directory.Exists(Resolve("native_x64"));
        return $"kök: {Base}" + (nativeOk ? "" : "  [UYARI: native_x64 yok]");
    }
}
