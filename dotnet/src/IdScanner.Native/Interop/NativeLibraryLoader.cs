using System.Reflection;
using System.Runtime.InteropServices;
using IdScanner.Core.Diagnostics;

namespace IdScanner.Native.Interop;

/// <summary>
/// Native DLL'leri doğru sıradan ve doğru klasörden yükler.
///
/// <b>Neden gerekli:</b> DLL'ler uygulamanın yanında değil, <c>native_x64</c> ve
/// <c>native_evolis</c> klasörlerinde duruyor. Ayrıca IDSIF.dll kendi
/// bağımlılıklarını (opencv, libusb, CardProcessor...) adıyla arıyor; onlar
/// önceden yüklenmezse IDSIF.dll yüklenirken "modül bulunamadı" ile patlıyor
/// ve hata mesajı hangi bağımlılığın eksik olduğunu söylemiyor.
///
/// Java karşılığı: IDSIF.load() ve EvolisSDK.load() içindeki
/// <c>System.load</c> döngüsü + <c>jna.library.path</c> ayarı.
///
/// <b>Önemli kısıt:</b> <see cref="NativeLibrary.SetDllImportResolver"/> bir
/// derleme için <b>yalnızca bir kez</b> çağrılabilir; ikincisi
/// <see cref="InvalidOperationException"/> fırlatır. Tarayıcı ve yazıcı aynı
/// derlemede olduğu için tek bir çözümleyici kaydediliyor ve o çözümleyici
/// kayıtlı tüm klasörleri sırayla tarıyor.
/// </summary>
internal static class NativeLibraryLoader
{
    private static readonly Lock Gate = new();

    /// <summary>Aranacak klasörler — kayıt sırasına göre.</summary>
    private static readonly List<string> SearchDirectories = [];

    /// <summary>Çözümleyicisi kurulmuş derlemeler.</summary>
    private static readonly HashSet<Assembly> ResolverInstalled = [];

    /// <summary>Bağımlılıkları bir kez yüklenmiş klasörler.</summary>
    private static readonly HashSet<string> PreloadedDirectories = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Bir derlemenin <c>DllImport</c> çağrılarını verilen klasöre de yönlendir.
    /// Aynı klasör için tekrar çağrılması zararsız.
    /// </summary>
    /// <param name="assembly">Çözümleyicinin bağlanacağı derleme.</param>
    /// <param name="directory">DLL'lerin bulunduğu klasör.</param>
    /// <param name="dependencies">
    /// IDSIF gibi kütüphanelerin önceden yüklenmesi gereken bağımlılıkları,
    /// <b>yükleme sırasına göre</b>. Uzantısız yazılır.
    /// </param>
    /// <param name="log">Tanılama.</param>
    internal static void Register(
        Assembly assembly,
        string directory,
        IReadOnlyList<string> dependencies,
        IAppLogger log)
    {
        var scoped = log.ForComponent("DllLoader");

        lock (Gate)
        {
            AddSearchDirectory(directory, scoped);
            PreloadDependencies(directory, dependencies, scoped);
            InstallResolver(assembly, scoped);
        }
    }

    /// <summary>Klasörü arama listesine ekle (varsa tekrar ekleme).</summary>
    private static void AddSearchDirectory(string directory, IAppLogger log)
    {
        if (SearchDirectories.Contains(directory, StringComparer.OrdinalIgnoreCase))
        {
            log.Trace($"{directory} zaten arama listesinde");
            return;
        }

        SearchDirectories.Add(directory);
        log.Info($"Native klasör: {directory}");

        if (!Directory.Exists(directory))
        {
            log.Error($"Native klasör YOK: {directory} — bu klasördeki DLL çağrıları başarısız olacak");
        }
    }

    /// <summary>
    /// Çözümleyiciyi derleme başına bir kez kur.
    ///
    /// Çözümleyici, kayıtlı tüm klasörleri sırayla dener; bulamazsa çalışma
    /// zamanının varsayılan aramasına bırakır.
    /// </summary>
    private static void InstallResolver(Assembly assembly, IAppLogger log)
    {
        if (!ResolverInstalled.Add(assembly))
        {
            log.Trace($"{assembly.GetName().Name} için çözümleyici zaten kurulu");
            return;
        }

        NativeLibrary.SetDllImportResolver(assembly, (name, _, _) =>
        {
            // Kilit alınmadan okunuyor: kayıtlar uygulama açılışında yapılır,
            // çözümleme ise ilk DllImport çağrısında — araya girme olmaz.
            foreach (var directory in SearchDirectories)
            {
                var candidate = Path.Combine(directory, name + ".dll");
                if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
                {
                    log.Debug($"{name} → {candidate}");
                    return handle;
                }
            }

            log.Trace($"{name} kayıtlı klasörlerde bulunamadı, varsayılan aramaya bırakılıyor");
            return IntPtr.Zero;
        });

        log.Debug($"{assembly.GetName().Name} için DLL çözümleyicisi kuruldu");
    }

    /// <summary>
    /// Bağımlılıkları sırayla yükle. Biri eksikse akış durmaz — asıl DLL
    /// yine de yüklenebiliyor olabilir; ama log'a uyarı düşer.
    /// </summary>
    private static void PreloadDependencies(string directory, IReadOnlyList<string> dependencies, IAppLogger log)
    {
        if (dependencies.Count == 0) return;
        if (!PreloadedDirectories.Add(directory)) return;

        foreach (var dep in dependencies)
        {
            var path = Path.Combine(directory, dep + ".dll");

            if (!File.Exists(path))
            {
                log.Debug($"bağımlılık yok, atlandı: {dep}.dll");
                continue;
            }

            if (NativeLibrary.TryLoad(path, out _))
            {
                log.Debug($"bağımlılık yüklendi: {dep}");
            }
            else
            {
                // Genellikle 32/64 bit uyuşmazlığı ya da onun da bir
                // bağımlılığının eksik olması. Devam ediyoruz çünkü bazı
                // bağımlılıklar isteğe bağlı.
                log.Warn($"bağımlılık YÜKLENEMEDİ (devam ediliyor): {dep}.dll — " +
                         "64-bit mi, kendi bağımlılıkları yerinde mi?");
            }
        }
    }
}
