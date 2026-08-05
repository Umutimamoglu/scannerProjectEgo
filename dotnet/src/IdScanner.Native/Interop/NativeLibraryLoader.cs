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
/// <c>System.load</c> döngüsü + <c>jna.library.path</c> ayarı. .NET'te
/// <see cref="NativeLibrary.SetDllImportResolver"/> ile çözülüyor.
///
/// <b>Hata ayıklama:</b> Her yükleme denemesi ayrı ayrı loglanır. Cuma günü
/// "DLL yüklenemedi" hatası gelirse, hangi dosyanın hangi yolda aranıp
/// bulunamadığı log'da yazacak.
/// </summary>
internal static class NativeLibraryLoader
{
    private static readonly Lock Gate = new();
    private static readonly HashSet<string> Registered = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Bir derlemenin <c>DllImport</c> çağrılarını verilen klasöre yönlendir.
    /// Aynı klasör için tekrar çağrılması zararsız.
    /// </summary>
    /// <param name="assembly">Çözümleyicinin bağlanacağı derleme.</param>
    /// <param name="directory">DLL'lerin bulunduğu klasör.</param>
    /// <param name="dependencies">
    /// IDSIF gibi kütüphanelerin önceden yüklenmesi gereken bağımlılıkları,
    /// <b>yükleme sırasına göre</b>. Uzantısız yazılır.
    /// </param>
    /// <param name="log">Tanılama.</param>
    public static void Register(
        System.Reflection.Assembly assembly,
        string directory,
        IReadOnlyList<string> dependencies,
        IAppLogger log)
    {
        var scoped = log.ForComponent("DllLoader");

        lock (Gate)
        {
            if (!Registered.Add(directory))
            {
                scoped.Trace($"{directory} zaten kayıtlı, atlanıyor");
                return;
            }

            scoped.Info($"Native klasör: {directory}");
            if (!Directory.Exists(directory))
            {
                scoped.Error($"Native klasör YOK: {directory} — DLL çağrıları başarısız olacak");
            }

            PreloadDependencies(directory, dependencies, scoped);

            NativeLibrary.SetDllImportResolver(assembly, (name, asm, path) =>
            {
                var candidate = Path.Combine(directory, name + ".dll");
                if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle))
                {
                    scoped.Debug($"{name} → {candidate}");
                    return handle;
                }

                scoped.Trace($"{name} bu klasörde bulunamadı, varsayılan aramaya bırakılıyor");
                return IntPtr.Zero; // Varsayılan arama devreye girsin
            });
        }
    }

    /// <summary>
    /// Bağımlılıkları sırayla yükle. Biri eksikse akış durmaz — asıl DLL
    /// yine de yüklenebiliyor olabilir; ama log'a uyarı düşer.
    /// </summary>
    private static void PreloadDependencies(string directory, IReadOnlyList<string> dependencies, IAppLogger log)
    {
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
