using System.Diagnostics;

namespace IdScanner.Core.Diagnostics;

/// <summary>
/// Bir işlemin başlangıcını, süresini ve sonucunu loglayan kapsam.
///
/// <code>
/// using var op = log.BeginOperation("DG2 okuma");
/// var bytes = ReadDg2();
/// op.Success($"{bytes.Length} bayt");
/// </code>
///
/// Sonuç bildirilmeden blok biterse "sonuç bildirilmeden kapandı" olarak
/// kaydedilir — yarıda kalan bir işlem log'da sessizce kaybolmaz. Nerede
/// takıldığımızı Cuma günü log'dan okuyabilmemizin yolu bu.
///
/// Not: .NET'te <c>Dispose</c> içinden "istisnayla mı çıkıldı" sorusunun
/// desteklenen bir cevabı yok. İstisnayı da kaydetmek için ya
/// <see cref="Failed"/> çağrılır ya da <see cref="AppLoggerExtensions.Track{T}"/>
/// kullanılır — o sarmalayıcı try/catch'i kendi yapar.
/// </summary>
public sealed class OperationScope : IDisposable
{
    private readonly IAppLogger _log;
    private readonly string _operation;
    private readonly Stopwatch _clock;
    private bool _completed;

    internal OperationScope(IAppLogger log, string operation)
    {
        _log = log;
        _operation = operation;
        _clock = Stopwatch.StartNew();
        _log.Write(LogLevel.Debug, $"▶ {operation}");
    }

    /// <summary>Başlangıçtan bu yana geçen süre.</summary>
    public TimeSpan Elapsed => _clock.Elapsed;

    /// <summary>İşlem başarıyla bitti.</summary>
    public void Success(string? detail = null)
    {
        if (_completed) return;
        _completed = true;
        _clock.Stop();
        var suffix = string.IsNullOrEmpty(detail) ? "" : " — " + detail;
        _log.Write(LogLevel.Debug, $"✔ {_operation} ({Ms()}){suffix}");
    }

    /// <summary>İşlem beklenen bir sebeple başarısız oldu (istisna yok).</summary>
    public void Failure(string reason)
    {
        if (_completed) return;
        _completed = true;
        _clock.Stop();
        _log.Write(LogLevel.Warn, $"✘ {_operation} ({Ms()}) — {reason}");
    }

    /// <summary>İşlem istisna ile başarısız oldu — tam zincir kaydedilir.</summary>
    public void Failed(Exception error)
    {
        if (_completed) return;
        _completed = true;
        _clock.Stop();
        _log.Write(LogLevel.Error, $"✘ {_operation} ({Ms()})", error);
    }

    public void Dispose()
    {
        if (_completed) return;
        _completed = true;
        _clock.Stop();
        _log.Write(LogLevel.Warn, $"? {_operation} ({Ms()}) — sonuç bildirilmeden kapandı");
    }

    private string Ms() => $"{_clock.Elapsed.TotalMilliseconds:F0} ms";
}
