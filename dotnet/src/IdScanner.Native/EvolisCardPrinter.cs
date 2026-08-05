using System.Runtime.InteropServices;
using IdScanner.Core;
using IdScanner.Core.Diagnostics;
using IdScanner.Core.Model;
using IdScanner.Native.Interop;
using IdScanner.Workflow.Abstractions;

namespace IdScanner.Native;

/// <summary>
/// Evolis KC Prime kart yazıcısı.
///
/// Java karşılığı: EvolisPrinter.java.
///
/// Java'daki davranış korundu: bağlantı yokken hiçbir metot istisna
/// fırlatmaz, anlamlı bir sonuç döndürür — arayüzde her çağrıyı try/catch'e
/// sarmamak için.
/// </summary>
public sealed class EvolisCardPrinter : ICardPrinter
{
    /// <summary>Baskı zaman aşımı — kart mekanik olarak yavaş ilerleyebiliyor.</summary>
    private const int PrintTimeoutMs = 5 * 60_000;

    /// <summary>
    /// Yarım panel renkli bandın konumlanma yöntemi: AUTO | CUSTOM | OFF.
    ///
    /// AUTO'da bandı yazıcı kendi yerleştirir; dikey (PORTRAIT) tasarımda bandı
    /// fotoğrafın çok altına koyduğu gözlendi — fotoğraf yalnızca K paneliyle
    /// basılıp açık tonlar kayboldu. Bu yüzden CUSTOM ile elle konumlandırılıyor.
    /// </summary>
    public static string ShortPanelMode { get; set; } = "CUSTOM";

    /// <summary>
    /// CUSTOM modda bandın kaydırma miktarı. PRN'de "Psp;değer" olarak gider.
    ///
    /// <b>ETKİSİ YOK</b> — kalibrasyon kartıyla ölçüldü, 0 ve 36 değerleri birebir
    /// aynı kartı bastı; bant her durumda ~47,5 mm'de başlıyor. Negatif değerler
    /// yazıcıda sessizce Psp;3'e dönüşüyor.
    ///
    /// Bu yüzden bandın yeri sabit kabul edildi ve tasarım ona uyduruldu.
    /// 0'da bırakıldı: AUTO görüntü içeriğine göre değişebildiği için CUSTOM
    /// ile sabitlemek baskıyı öngörülebilir kılıyor.
    /// </summary>
    public static int ShortPanelShift { get; set; }

    /// <summary>
    /// Çift yüz çevirme ekseni — GDuplexType. NONE=tek yüz, HORIZONTAL/VERTICAL
    /// çevirme yönünü belirler. Hangisinin doğru olduğu kalibrasyonla saptanır.
    /// </summary>
    public static string DuplexType { get; set; } = "HORIZONTAL";

    private readonly IAppLogger _log;
    private IntPtr _context = IntPtr.Zero;
    private string? _printerName;
    private bool _disposed;

    public EvolisCardPrinter(IAppLogger? logger = null)
    {
        _log = (logger ?? NullLogger.Instance).ForComponent("Printer");

        NativeLibraryLoader.Register(
            typeof(EvolisNative).Assembly,
            AppPaths.Resolve("native_evolis"),
            dependencies: [],
            _log);
    }

    /// <inheritdoc />
    public bool IsConnected => _context != IntPtr.Zero;

    /// <inheritdoc />
    public string? PrinterName => _printerName;

    // === Bağlantı ===

    /// <inheritdoc />
    public IReadOnlyList<string> ListPrinters()
    {
        var names = new List<string>();
        try
        {
            var count = EvolisNative.evolis_get_devices(out var buffer, 0);
            _log.Debug($"evolis_get_devices → {count}");

            if (count <= 0 || buffer == IntPtr.Zero) return names;

            try
            {
                var size = Marshal.SizeOf<EvolisNative.Device>();
                for (var i = 0; i < count; i++)
                {
                    var device = Marshal.PtrToStructure<EvolisNative.Device>(buffer + i * size);
                    names.Add(device.Name);
                    _log.Debug($"  cihaz {i}: {device.Name} " +
                               $"(model={device.Model}, çevrimiçi={device.IsOnline}, " +
                               $"denetimli={device.IsSupervised}, sürücü={device.DriverVersion})");
                }
            }
            finally
            {
                EvolisNative.evolis_free_devices(buffer);
            }
        }
        catch (DllNotFoundException e)
        {
            _log.Error($"evolis.dll bulunamadı. Beklenen klasör: {AppPaths.Resolve("native_evolis")}", e);
        }
        catch (Exception e)
        {
            _log.Error("Yazıcı listesi alınamadı", e);
        }
        return names;
    }

    /// <inheritdoc />
    public bool Connect()
    {
        var printers = ListPrinters();
        if (printers.Count == 0)
        {
            _log.Warn("Hiç Evolis yazıcı bulunamadı — USB bağlantısı ve sürücüyü kontrol edin");
            return false;
        }
        return Connect(printers[0]);
    }

    /// <inheritdoc />
    public bool Connect(string name)
    {
        Disconnect();
        using var op = _log.BeginOperation($"Yazıcıya bağlanma: {name}");

        try
        {
            // Önce DIRECT — Premium Suite kurulu olmasa da çalışır.
            _context = EvolisNative.evolis_open_with_mode(name, EvolisNative.OpenMode.Direct);
            if (_context != IntPtr.Zero)
            {
                _printerName = name;
                op.Success("DIRECT mod");
                return true;
            }

            _log.Debug("DIRECT mod başarısız, SUPERVISED deneniyor");
            _context = EvolisNative.evolis_open_with_mode(name, EvolisNative.OpenMode.Supervised);
            if (_context != IntPtr.Zero)
            {
                _printerName = name;
                op.Success("SUPERVISED mod");
                return true;
            }

            op.Failure("her iki mod da başarısız — yazıcı açık mı, başka uygulama kullanıyor olabilir mi?");
            return false;
        }
        catch (Exception e)
        {
            op.Failed(e);
            return false;
        }
    }

    /// <inheritdoc />
    public void Disconnect()
    {
        if (_context == IntPtr.Zero) return;
        try
        {
            EvolisNative.evolis_close(_context);
            _log.Debug($"Yazıcı bağlantısı kapatıldı: {_printerName}");
        }
        catch (Exception e)
        {
            _log.Warn("Yazıcı kapatılırken hata", e);
        }
        _context = IntPtr.Zero;
        _printerName = null;
    }

    // === Durum ===

    /// <inheritdoc />
    public PrinterState ReadState()
    {
        var state = new PrinterState();
        if (_context == IntPtr.Zero) return state;

        try
        {
            if (EvolisNative.evolis_get_state(_context, out var major, out var minor) == EvolisNative.Ret.Ok)
            {
                state.Kind = Enum.IsDefined(typeof(PrinterStateKind), major)
                    ? (PrinterStateKind)major
                    : PrinterStateKind.Off;
                state.Minor = minor;
            }

            var status = new EvolisNative.Status { Extensions = new int[4] };
            if (EvolisNative.evolis_status(_context, ref status) == EvolisNative.Ret.Ok)
            {
                state.HasError = status.Error != 0;
                state.HasWarning = status.Warning != 0;

                for (var id = 0; id < EvolisFlags.Names.Length; id++)
                {
                    var name = EvolisFlags.Name(id);
                    if (!EvolisFlags.IsReportable(name)) continue;
                    if (EvolisNative.evolis_status_is_on(ref status, id)) state.Flags.Add(name);
                }

                if (state.Flags.Count > 0) _log.Debug("Açık bayraklar: " + string.Join(", ", state.Flags));
            }
        }
        catch (Exception e)
        {
            _log.Error("Yazıcı durumu okunamadı", e);
        }

        return state;
    }

    /// <inheritdoc />
    public bool ClearMechanicalErrors()
    {
        if (_context == IntPtr.Zero) return false;
        try
        {
            var rc = EvolisNative.evolis_clear_mechanical_errors(_context);
            _log.Info($"Mekanik hata temizleme → {EvolisNative.Describe(rc)}");
            return rc == EvolisNative.Ret.Ok;
        }
        catch (Exception e)
        {
            _log.Error("Mekanik hata temizlenemedi", e);
            return false;
        }
    }

    // === Bilgiler ===

    /// <inheritdoc />
    public Core.Model.PrinterInfo? ReadInfo()
    {
        if (_context == IntPtr.Zero) return null;
        try
        {
            var native = default(EvolisNative.PrinterInfoNative);
            var rc = EvolisNative.evolis_get_info(_context, ref native);
            if (rc != EvolisNative.Ret.Ok)
            {
                _log.Warn($"Yazıcı künyesi okunamadı → {EvolisNative.Describe(rc)}");
                return null;
            }

            _log.Info($"Yazıcı: {native.ModelName} (seri {native.SerialNumber}, firmware {native.FwVersion})");

            return new Core.Model.PrinterInfo
            {
                Name = native.Name,
                MarkName = native.MarkName,
                ModelName = native.ModelName,
                FirmwareVersion = native.FwVersion,
                SerialNumber = native.SerialNumber,
                PrintHeadKitNumber = native.PrintHeadKitNumber,
                Zone = native.Zone,
                HasFlip = native.HasFlip,
                HasEthernet = native.HasEthernet,
                HasWifi = native.HasWifi,
                HasLaminator = native.HasLaminator,
                HasMagneticEncoder = native.HasMagEnc,
                HasSmartEncoder = native.HasSmartEnc,
                HasContactlessEncoder = native.HasContactLessEnc,
                HasLcd = native.HasLcd,
                HasLock = native.HasLock,
                HasScanner = native.HasScanner,
            };
        }
        catch (Exception e)
        {
            _log.Error("Yazıcı künyesi okunurken hata", e);
            return null;
        }
    }

    /// <inheritdoc />
    public Core.Model.RibbonInfo? ReadRibbon()
    {
        if (_context == IntPtr.Zero) return null;
        try
        {
            var native = default(EvolisNative.RibbonInfoNative);
            var rc = EvolisNative.evolis_get_ribbon(_context, ref native);
            if (rc != EvolisNative.Ret.Ok)
            {
                _log.Warn($"Ribon bilgisi okunamadı → {EvolisNative.Describe(rc)}");
                return null;
            }

            _log.Info($"Ribon: {native.Description} ({native.ProductCode}), " +
                      $"kalan {native.Remaining}/{native.Capacity}");

            return new Core.Model.RibbonInfo
            {
                Description = native.Description,
                ProductCode = native.ProductCode,
                Zone = native.Zone,
                Type = native.Type,
                Capacity = native.Capacity,
                Remaining = native.Remaining,
                Progress = native.Progress,
                SerialNumber = native.SerialNumber,
            };
        }
        catch (Exception e)
        {
            _log.Error("Ribon bilgisi okunurken hata", e);
            return null;
        }
    }

    /// <inheritdoc />
    public Core.Model.CleaningInfo? ReadCleaning()
    {
        if (_context == IntPtr.Zero) return null;
        try
        {
            var native = default(EvolisNative.CleaningInfoNative);
            var rc = EvolisNative.evolis_get_cleaning(_context, ref native);
            if (rc != EvolisNative.Ret.Ok)
            {
                _log.Warn($"Temizlik bilgisi okunamadı → {EvolisNative.Describe(rc)}");
                return null;
            }

            _log.Debug($"Baskı sayacı: toplam {native.TotalCardCount}, " +
                       $"son temizlikten beri {native.CardCount}, " +
                       $"uyarıya kalan {native.CardCountBeforeWarning}");

            return new Core.Model.CleaningInfo
            {
                TotalCardCount = native.TotalCardCount,
                CardCount = native.CardCount,
                CardCountBeforeWarning = native.CardCountBeforeWarning,
                CardCountBeforeWarrantyLost = native.CardCountBeforeWarrantyLost,
                CardCountAtLastCleaning = native.CardCountAtLastCleaning,
                RegularCleaningCount = native.RegularCleaningCount,
                AdvancedCleaningCount = native.AdvancedCleaningCount,
                PrintHeadUnderWarranty = native.PrintHeadUnderWarranty,
                WarningThreshold = native.WarningThreshold,
                WarrantyLostThreshold = native.WarrantyLostThreshold,
            };
        }
        catch (Exception e)
        {
            _log.Error("Temizlik bilgisi okunurken hata", e);
            return null;
        }
    }

    /// <inheritdoc />
    public int ReadFeeder()
    {
        if (_context == IntPtr.Zero) return -1;
        try
        {
            return EvolisNative.evolis_get_feeder(_context, out var feeder) == EvolisNative.Ret.Ok
                ? feeder
                : -1;
        }
        catch (Exception e)
        {
            _log.Warn("Besleyici okunamadı", e);
            return -1;
        }
    }

    // === Baskı ===

    /// <inheritdoc />
    public Core.Model.PrintResult Print(string imagePath, bool dryRun)
        => RunPrintJob(
            jobName: dryRun ? "Tek yüz prova" : "Tek yüz baskı",
            images: [(EvolisNative.CardFace.Front, imagePath, "Ön")],
            duplex: false,
            dryRun: dryRun,
            prnFileName: "card_dryrun.prn");

    /// <inheritdoc />
    public Core.Model.PrintResult PrintDuplex(string frontImagePath, string backImagePath, bool dryRun)
        => RunPrintJob(
            jobName: dryRun ? "Çift yüz prova" : "Çift yüz baskı",
            images:
            [
                (EvolisNative.CardFace.Front, frontImagePath, "Ön"),
                (EvolisNative.CardFace.Back, backImagePath, "Arka"),
            ],
            duplex: true,
            dryRun: dryRun,
            prnFileName: "card_duplex_dryrun.prn");

    /// <summary>
    /// Tek ve çift yüz baskının ortak akışı.
    ///
    /// Java'da bu mantık <c>print</c> ve <c>printDuplex</c> içinde iki kez
    /// yazılmıştı; ayarlar ve hata kontrolleri birebir tekrar ediyordu.
    /// Burada tek yerde toplandı — bir ayar değişikliği iki yeri birden etkiler.
    /// </summary>
    private Core.Model.PrintResult RunPrintJob(
        string jobName,
        IReadOnlyList<(int Face, string Path, string Label)> images,
        bool duplex,
        bool dryRun,
        string prnFileName)
    {
        if (_context == IntPtr.Zero) return Core.Model.PrintResult.Fail("Yazıcı bağlı değil");

        foreach (var (_, path, label) in images)
        {
            if (!File.Exists(path)) return Core.Model.PrintResult.Fail($"{label} görsel bulunamadı: {path}");
        }

        // Baskı öncesi hata kontrolü — hatalıysa yazıcı işi zaten reddeder
        var state = ReadState();
        if (state.HasError)
        {
            var flags = string.Join(", ", state.Flags);
            _log.Error($"Baskı reddedildi, yazıcıda hata var: {flags}");
            return Core.Model.PrintResult.Fail("Yazıcıda hata var, önce temizleyin: " + flags);
        }

        using var op = _log.BeginOperation(jobName);
        try
        {
            var rc = EvolisNative.evolis_print_init(_context);
            if (rc != EvolisNative.Ret.Ok)
            {
                op.Failure(EvolisNative.Describe(rc));
                return new Core.Model.PrintResult(false, rc, "print_init başarısız: " + EvolisNative.Describe(rc));
            }

            ApplyPrintSettings(duplex);

            foreach (var (face, path, label) in images)
            {
                var absolute = Path.GetFullPath(path);
                rc = EvolisNative.evolis_print_set_imagep(_context, face, absolute);
                if (rc != EvolisNative.Ret.Ok)
                {
                    op.Failure($"{label} görsel yüklenemedi");
                    return new Core.Model.PrintResult(false, rc,
                        $"{label} görsel yüklenemedi: {EvolisNative.Describe(rc)}");
                }
                _log.Debug($"{label} görsel yüklendi: {absolute}");
            }

            if (dryRun)
            {
                var prn = Path.Combine(AppPaths.EnsureDir("output"), prnFileName);
                rc = EvolisNative.evolis_print_to_file(_context, prn);
                if (rc == EvolisNative.Ret.Ok)
                {
                    op.Success(prn);
                    return new Core.Model.PrintResult(true, rc,
                        $"{jobName} başarılı — kart harcanmadı ({Path.GetFileName(prn)})");
                }
                op.Failure(EvolisNative.Describe(rc));
                return new Core.Model.PrintResult(false, rc, $"{jobName} başarısız: {EvolisNative.Describe(rc)}");
            }

            rc = EvolisNative.evolis_print_exect(_context, PrintTimeoutMs);
            if (rc == EvolisNative.Ret.Ok)
            {
                op.Success();
                return new Core.Model.PrintResult(true, rc, $"{jobName} tamamlandı");
            }

            op.Failure(EvolisNative.Describe(rc));
            return new Core.Model.PrintResult(false, rc, $"{jobName} başarısız: {EvolisNative.Describe(rc)}");
        }
        catch (Exception e)
        {
            op.Failed(e);
            return Core.Model.PrintResult.Fail($"{jobName} sırasında hata: {e.Message}");
        }
    }

    /// <summary>Dikey tasarım + yarım panel bant konumlama ayarlarını uygula.</summary>
    private void ApplyPrintSettings(bool duplex)
    {
        SetSetting(EvolisNative.SettingKey.Orientation, "PORTRAIT");
        SetSetting(EvolisNative.SettingKey.ShortPanelManagement, ShortPanelMode);

        if (ShortPanelMode == "CUSTOM")
        {
            SetSetting(EvolisNative.SettingKey.ShortPanelShift, ShortPanelShift.ToString());
        }

        if (duplex) SetSetting(EvolisNative.SettingKey.DuplexType, DuplexType);
    }

    /// <summary>Tek bir baskı ayarı ver ve sonucunu kaydet.</summary>
    private void SetSetting(int key, string value)
    {
        var ok = EvolisNative.evolis_print_set_setting(_context, key, value);
        if (ok)
        {
            _log.Trace($"ayar {key} = {value}");
        }
        else
        {
            // SDK burada sessizce başarısız olabiliyor; log olmadan fark edilmez.
            _log.Warn($"Baskı ayarı REDDEDİLDİ: {key} = {value}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Disconnect();
    }
}
