using IdScanner.Core;
using IdScanner.Core.Diagnostics;
using IdScanner.Core.Model;
using IdScanner.Core.Mrz;
using IdScanner.Native.Interop;
using IdScanner.Workflow.Abstractions;

namespace IdScanner.Native;

/// <summary>Tarayıcı işlemi başarısız olduğunda fırlatılır.</summary>
public sealed class ScannerException(string message, int returnCode = 0)
    : Exception(message)
{
    /// <summary>IDSIF dönüş kodu; yoksa 0.</summary>
    public int ReturnCode { get; } = returnCode;
}

/// <summary>
/// IDSIF.dll üzerinden kart tarayıcı.
///
/// Java karşılığı: IdCardReader'ın tarayıcı kısmı ve ScannerBridge.
/// Java'da bu iş çip okumayla aynı sınıftaydı; burada ayrıldı çünkü ikisi
/// farklı donanım (tarayıcı USB, çip PC/SC) ve ayrı ayrı test edilebilmeleri
/// gerekiyor.
/// </summary>
public sealed class DocumentScanner : IDocumentScanner
{
    /// <summary>T.C. Kimlik Kartı için tarama ayarları — Java ile aynı değerler.</summary>
    private const int DefaultBrightness = 50;
    private const int DefaultContrast = 50;
    private const int DefaultGamma = 50;

    /// <summary>MRZ tamponu; TD1 3×30 = 90 karakter, 256 fazlasıyla yeter.</summary>
    private const int MrzBufferSize = 256;

    /// <summary>Kart NFC pozisyonuna geçtikten sonra alanın oturması için beklenen süre.</summary>
    private static readonly TimeSpan NfcSettleDelay = TimeSpan.FromMilliseconds(1500);

    private readonly IAppLogger _log;
    private readonly DiagnosticsDump _dump;
    private bool _open;
    private bool _disposed;

    public DocumentScanner(IAppLogger? logger = null, DiagnosticsDump? dump = null)
    {
        _log = (logger ?? NullLogger.Instance).ForComponent("Scanner");
        _dump = dump ?? DiagnosticsDump.Disabled;

        NativeLibraryLoader.Register(
            typeof(IdsifNative).Assembly,
            AppPaths.Resolve("native_x64"),
            IdsifNative.Dependencies,
            _log);
    }

    /// <inheritdoc />
    public bool IsOpen => _open;

    /// <inheritdoc />
    public bool Open()
    {
        if (_open)
        {
            _log.Trace("Cihaz zaten açık");
            return true;
        }

        using var op = _log.BeginOperation("Tarayıcı açma");
        try
        {
            var savePath = AppPaths.EnsureDir("scan_output");
            _log.Debug($"Tarama çıktı klasörü: {savePath}");

            var rc = IdsifNative.OpenDev(savePath);
            if (!IdsifReturnCode.IsSuccess(rc))
            {
                op.Failure(IdsifReturnCode.Describe(rc));
                return false;
            }

            _open = true;
            op.Success();
            LogDeviceInfo();
            return true;
        }
        catch (DllNotFoundException e)
        {
            op.Failed(e);
            _log.Error($"IDSIF.dll bulunamadı. Beklenen klasör: {AppPaths.Resolve("native_x64")}");
            return false;
        }
        catch (Exception e)
        {
            op.Failed(e);
            return false;
        }
    }

    /// <summary>
    /// Cihazın seri no / firmware / SDK sürümünü log'a yaz.
    ///
    /// Java'da bu bilgi yalnızca istendiğinde okunuyordu. Burada açılışta
    /// otomatik kaydediliyor: saha sorunlarında "hangi firmware" sorusunun
    /// cevabı log'un başında hazır dursun.
    /// </summary>
    private void LogDeviceInfo()
    {
        (int Type, string Label)[] items =
        [
            (1, "seri no"), (2, "firmware"), (3, "SDK"),
        ];

        foreach (var (type, label) in items)
        {
            var value = GetVersionInfo(type);
            if (!string.IsNullOrWhiteSpace(value)) _log.Info($"Tarayıcı {label}: {value}");
        }
    }

    /// <summary>Sürüm bilgisi (1=seri no, 2=firmware, 3=SDK); okunamazsa boş.</summary>
    public string GetVersionInfo(int type)
    {
        if (!_open) return "";
        try
        {
            var buffer = new byte[256];
            var rc = IdsifNative.GetVersionInfo(type, buffer);
            if (!IdsifReturnCode.IsSuccess(rc))
            {
                _log.Trace($"GetVersionInfo({type}) → {IdsifReturnCode.Describe(rc)}");
                return "";
            }
            return ReadCString(buffer);
        }
        catch (Exception e)
        {
            _log.Warn($"Sürüm bilgisi okunamadı (tip {type})", e);
            return "";
        }
    }

    /// <inheritdoc />
    public CardStatus GetCardStatus()
    {
        if (!_open) return CardStatus.Unknown;
        try
        {
            var rc = IdsifNative.CardStatus(out var status);
            if (!IdsifReturnCode.IsSuccess(rc))
            {
                _log.Trace($"CardStatus → {IdsifReturnCode.Describe(rc)}");
                return CardStatus.Unknown;
            }
            return Enum.IsDefined(typeof(CardStatus), status) ? (CardStatus)status : CardStatus.Unknown;
        }
        catch (Exception e)
        {
            _log.Warn("Kart durumu okunamadı", e);
            return CardStatus.Unknown;
        }
    }

    /// <inheritdoc />
    public ScanResult Scan()
    {
        if (!_open) throw new ScannerException("Tarayıcı açık değil");

        var conf = new IdsifNative.ScanConf
        {
            Type = IdsifCardType.IdCn,
            Mode = IdsifScanMode.Color,
            Side = IdsifScanSide.Duplex,
            Dpi = IdsifScanDpi.Dpi300,
            Front = new IdsifNative.ImageParam
            {
                Brightness = DefaultBrightness, Contrast = DefaultContrast, Gamma = DefaultGamma,
            },
            Back = new IdsifNative.ImageParam
            {
                Brightness = DefaultBrightness, Contrast = DefaultContrast, Gamma = DefaultGamma,
            },
        };

        _log.Debug($"Tarama ayarı: tip={conf.Type}, mod={conf.Mode}, yüz={conf.Side}, dpi={conf.Dpi}");

        var native = new IdsifNative.ScanResultNative { Reserved = new byte[2] };
        var mrzBuffer = new byte[MrzBufferSize];
        var mrzLength = mrzBuffer.Length;

        using var op = _log.BeginOperation("Tarama + MRZ tanıma");
        var rc = IdsifNative.ScanMRZ(conf, ref native, mrzBuffer, ref mrzLength);

        if (!IdsifReturnCode.IsSuccess(rc))
        {
            var reason = IdsifReturnCode.Describe(rc);
            op.Failure(reason);
            throw new ScannerException("Tarama başarısız — " + reason, rc);
        }

        var mrzText = ReadAscii(mrzBuffer, mrzLength);
        op.Success($"{IdsifReturnCode.Describe(rc)}, MRZ {mrzText.Length} karakter");

        var result = new ScanResult
        {
            MrzText = mrzText,
            FrontBmp = NullIfEmpty(native.FrontFileName),
            BackBmp = NullIfEmpty(native.BackFileName),
            CardDirection = native.CardDirection,
        };

        _log.Info($"Tarama görüntüleri: ön={result.FrontBmp ?? "(yok)"}, arka={result.BackBmp ?? "(yok)"}");
        _log.Debug($"Kart yönü ipucu (ucCardDir): {native.CardDirection}");
        _dump.AppendText("mrz.txt", mrzText);

        // MRZ ayrıştırılamazsa tarama yine de başarılı — görüntüler elimizde.
        // Çağıran taraf MRZ'siz devam edemeyeceğine kendi karar verir.
        if (MrzParser.TryParse(mrzText, out var mrz, _log))
        {
            result.Mrz = mrz;
        }
        else
        {
            _log.Warn("MRZ ayrıştırılamadı — çip okuma için BAC anahtarları üretilemeyecek");
        }

        return result;
    }

    /// <summary>Kartı NFC (çip okuma) pozisyonuna taşı ve alanın oturmasını bekle.</summary>
    public void MoveToNfc()
    {
        if (!_open) return;
        _log.Info("Kart NFC pozisyonuna taşınıyor");
        MoveTo(IdsifPosition.EjectHalf);
        Thread.Sleep(NfcSettleDelay);
    }

    /// <inheritdoc />
    public void Eject()
    {
        if (!_open) return;
        _log.Info("Kart çıkarılıyor");
        MoveTo(IdsifPosition.Out);
    }

    /// <summary>Kartı belirtilen konuma taşı.</summary>
    private void MoveTo(int position)
    {
        try
        {
            var rc = IdsifNative.Move(position);
            if (!IdsifReturnCode.IsSuccess(rc))
            {
                _log.Warn($"Move({position}) → {IdsifReturnCode.Describe(rc)}");
            }
            else
            {
                _log.Trace($"Move({position}) tamam");
            }
        }
        catch (Exception e)
        {
            _log.Error($"Move({position}) çağrısı hata verdi", e);
        }
    }

    /// <summary>Sesli uyarıyı aç/kapat.</summary>
    public void SetBuzzer(bool on)
    {
        if (!_open) return;
        try
        {
            IdsifNative.SetBuzzer(on ? 1 : 0);
        }
        catch (Exception e)
        {
            _log.Trace($"Buzzer ayarlanamadı: {e.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (!_open) return;
        try
        {
            var rc = IdsifNative.Uninit();
            _log.Debug($"Tarayıcı kapatıldı → {IdsifReturnCode.Describe(rc)}");
        }
        catch (Exception e)
        {
            _log.Warn("Tarayıcı kapatılırken hata", e);
        }
        _open = false;
    }

    // === Yardımcılar ===

    /// <summary>Sıfır sonlandırmalı tampondan metin oku.</summary>
    private static string ReadCString(byte[] buffer)
    {
        var length = Array.IndexOf(buffer, (byte)0);
        if (length < 0) length = buffer.Length;
        return System.Text.Encoding.UTF8.GetString(buffer, 0, length).Trim();
    }

    /// <summary>MRZ tamponundan ASCII metin oku (uzunluk DLL tarafından verilir).</summary>
    private static string ReadAscii(byte[] buffer, int length)
    {
        var safe = Math.Clamp(length, 0, buffer.Length);
        return System.Text.Encoding.ASCII.GetString(buffer, 0, safe).Trim();
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
