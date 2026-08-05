using System.Globalization;
using System.Text;

namespace IdScanner.Core.Diagnostics;

/// <summary>Hiçbir yere yazmaz — test ve varsayılan için.</summary>
public sealed class NullLogger : IAppLogger
{
    public static readonly NullLogger Instance = new();
    private NullLogger() { }

    public void Write(LogLevel level, string message, Exception? error = null) { }
    public bool IsEnabled(LogLevel level) => false;
    public IAppLogger ForComponent(string component) => this;
}

/// <summary>
/// Bileşen adını mesajın önüne ekleyen sarmalayıcı.
///
/// Log satırında hangi katmanın konuştuğu görünsün diye:
/// <c>[ChipReader] DG2 okundu</c>. Saha log'unda bu olmadan hangi çağrının
/// hangi bileşenden geldiğini kestirmek mümkün olmuyor.
/// </summary>
internal sealed class ComponentLogger(IAppLogger inner, string component) : IAppLogger
{
    public void Write(LogLevel level, string message, Exception? error = null)
        => inner.Write(level, $"[{component}] {message}", error);

    public bool IsEnabled(LogLevel level) => inner.IsEnabled(level);

    // Bileşen adını zincirleme: [Chip] + [Bac] -> [Chip.Bac]
    public IAppLogger ForComponent(string child) => new ComponentLogger(inner, $"{component}.{child}");
}

/// <summary>Birden fazla hedefe aynı anda yazar (dosya + arayüz gibi).</summary>
public sealed class CompositeLogger(params IAppLogger[] targets) : IAppLogger
{
    public void Write(LogLevel level, string message, Exception? error = null)
    {
        foreach (var t in targets)
        {
            try
            {
                t.Write(level, message, error);
            }
            catch (Exception)
            {
                // Bir hedefin çökmesi diğerlerini ve asıl akışı durdurmamalı.
                // Loglama, uygulamayı düşürebilecek bir işlem olmamalı.
            }
        }
    }

    public bool IsEnabled(LogLevel level) => targets.Any(t => t.IsEnabled(level));

    public IAppLogger ForComponent(string component) => new ComponentLogger(this, component);
}

/// <summary>Satırları bir temsilciye yollar — arayüzdeki log paneli için.</summary>
public sealed class DelegateLogger(Action<LogLevel, string> sink, LogLevel minimum = LogLevel.Info) : IAppLogger
{
    public void Write(LogLevel level, string message, Exception? error = null)
    {
        if (!IsEnabled(level)) return;
        var text = error is null ? message : $"{message} — {error.GetType().Name}: {error.Message}";
        sink(level, text);
    }

    public bool IsEnabled(LogLevel level) => level >= minimum;

    public IAppLogger ForComponent(string component) => new ComponentLogger(this, component);
}

/// <summary>
/// Günlük dosyaya yazar: <c>logs/idscanner-yyyyMMdd.log</c>.
///
/// Neden dosya: Cuma günkü testte ekrandaki log paneli kaydırılıp kaybolur,
/// uygulama kapanınca da gider. Sorunları sonradan inceleyebilmek için
/// kalıcı bir iz gerekiyor. Dosyaya <see cref="LogLevel.Trace"/> dahil her şey
/// yazılır; arayüz daha sakin bir seviye gösterir.
/// </summary>
public sealed class FileLogger : IAppLogger, IDisposable
{
    private readonly string _path;
    private readonly LogLevel _minimum;
    private readonly Lock _gate = new();
    private StreamWriter? _writer;
    private bool _disposed;

    /// <summary>Yazılan dosyanın tam yolu — açılışta kullanıcıya bildirilir.</summary>
    public string Path => _path;

    /// <param name="directory">Log klasörü; yoksa oluşturulur.</param>
    /// <param name="minimum">Bu seviyenin altındakiler yazılmaz.</param>
    public FileLogger(string directory, LogLevel minimum = LogLevel.Trace)
    {
        _minimum = minimum;
        Directory.CreateDirectory(directory);
        var name = $"idscanner-{DateTime.Now:yyyyMMdd}.log";
        _path = System.IO.Path.Combine(directory, name);

        _writer = new StreamWriter(_path, append: true, Encoding.UTF8) { AutoFlush = true };
        WriteRaw(new string('=', 78));
        WriteRaw($"Oturum başladı: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
    }

    public bool IsEnabled(LogLevel level) => !_disposed && level >= _minimum;

    public void Write(LogLevel level, string message, Exception? error = null)
    {
        if (!IsEnabled(level)) return;

        var line = new StringBuilder()
            .Append(DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture))
            .Append("  ")
            .Append(Abbrev(level))
            .Append("  ")
            .Append(message);

        if (error is not null)
        {
            line.AppendLine().Append(Describe(error));
        }

        WriteRaw(line.ToString());
    }

    public IAppLogger ForComponent(string component) => new ComponentLogger(this, component);

    private void WriteRaw(string line)
    {
        lock (_gate)
        {
            try
            {
                _writer?.WriteLine(line);
            }
            catch (Exception)
            {
                // Disk dolu, dosya kilitli vb. — loglama asıl işi durdurmamalı.
            }
        }
    }

    private static string Abbrev(LogLevel level) => level switch
    {
        LogLevel.Trace => "TRC",
        LogLevel.Debug => "DBG",
        LogLevel.Info => "INF",
        LogLevel.Warn => "WRN",
        LogLevel.Error => "ERR",
        _ => "???",
    };

    /// <summary>
    /// İstisnayı iç zinciriyle birlikte yaz.
    ///
    /// Java'da <c>e.getMessage()</c> yazılıyordu; sebep zinciri kayboluyordu.
    /// P/Invoke ve kripto hatalarında asıl bilgi çoğu zaman en içteki
    /// istisnadadır, o yüzden tamamı kaydediliyor.
    /// </summary>
    internal static string Describe(Exception error)
    {
        var sb = new StringBuilder();
        var current = error;
        var depth = 0;

        while (current is not null && depth < 8)
        {
            var indent = new string(' ', 8 + depth * 2);
            sb.Append(indent)
              .Append(depth == 0 ? "! " : "└ sebep: ")
              .Append(current.GetType().FullName)
              .Append(": ")
              .AppendLine(current.Message);

            if (current.StackTrace is { Length: > 0 } stack)
            {
                foreach (var frame in stack.Split('\n').Take(6))
                {
                    sb.Append(indent).Append("    ").AppendLine(frame.TrimEnd());
                }
            }

            current = current.InnerException;
            depth++;
        }

        return sb.ToString().TrimEnd();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        lock (_gate)
        {
            try
            {
                _writer?.WriteLine($"Oturum bitti: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
                _writer?.Dispose();
            }
            catch (Exception)
            {
                // Kapanış sırasında hata — yapacak bir şey yok.
            }
            _writer = null;
        }
    }
}
