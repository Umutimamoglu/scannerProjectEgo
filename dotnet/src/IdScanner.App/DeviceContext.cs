using IdScanner.Chip;
using IdScanner.Core;
using IdScanner.Core.Diagnostics;
using IdScanner.Crypto;
using IdScanner.Native;
using IdScanner.Render;
using IdScanner.Workflow;
using IdScanner.Workflow.Abstractions;

namespace IdScanner.App;

/// <summary>
/// Composition root — somut uygulamaların arayüzlere bağlandığı <b>tek</b> yer.
///
/// Java'da böyle bir ayrım yoktu; MainUI doğrudan <c>new IdCardReader()</c> ve
/// <c>new EvolisPrinter()</c> yapıyordu. Buradaki ayrımın faydası, formun
/// somut donanım sınıflarını hiç tanımaması: ileride sahte (fake) uygulamalar
/// takılarak arayüz donanımsız çalıştırılabilir.
/// </summary>
public sealed class DeviceContext : IDisposable
{
    private readonly FileLogger _fileLogger;
    private bool _disposed;

    /// <summary>Tarayıcı (IDSIF.dll).</summary>
    public IDocumentScanner Scanner { get; }

    /// <summary>Yazıcı (evolis.dll).</summary>
    public ICardPrinter Printer { get; }

    /// <summary>Akış orkestrasyonu.</summary>
    public CardIssuanceService Service { get; }

    /// <summary>Yazılan log dosyasının yolu — açılışta kullanıcıya gösterilir.</summary>
    public string LogFilePath => _fileLogger.Path;

    /// <summary>Bu oturumun ham veri döküm klasörü; kapalıysa <c>null</c>.</summary>
    public string? DumpDirectory { get; }

    /// <param name="uiSink">Log satırlarının arayüze yollanacağı geri çağrı.</param>
    public DeviceContext(Action<LogLevel, string> uiSink)
    {
        // Dosyaya her şey, arayüze yalnızca Info ve üstü.
        // Sahada "ekranda bir şey yoktu" denince dosyada tam iz olsun diye.
        _fileLogger = new FileLogger(AppPaths.EnsureDir("logs"));
        var logger = new CompositeLogger(_fileLogger, new DelegateLogger(uiSink, LogLevel.Info));

        var dump = new DiagnosticsDump(AppPaths.EnsureDir("diag"), logger);
        DumpDirectory = dump.Directory;

        logger.Info($"Uygulama başlıyor — {AppPaths.Describe()}");

        var verifier = new PassiveAuthVerifier(AppPaths.Resolve("certs"), logger);
        var activeAuth = new ActiveAuthVerifier(logger);

        Scanner = new DocumentScanner(logger, dump);
        Printer = new EvolisCardPrinter(logger);

        var chipReader = new PassportChipReader(verifier, activeAuth, logger, dump);
        var renderer = new CardRenderer(logger);

        Service = new CardIssuanceService(Scanner, chipReader, renderer, Printer, logger);
        Logger = logger;
    }

    /// <summary>Uygulama genelinde kullanılan logger.</summary>
    public IAppLogger Logger { get; }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Scanner.Dispose();
        Printer.Dispose();
        _fileLogger.Dispose();
    }
}
