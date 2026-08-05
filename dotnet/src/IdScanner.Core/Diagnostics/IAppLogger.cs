namespace IdScanner.Core.Diagnostics;

/// <summary>
/// İlerleme ve tanılama mesajlarının gideceği yer.
///
/// Java karşılığı: her sınıfa ayrı geçirilen <c>Consumer&lt;String&gt; logger</c>.
/// Burada üç şey eklendi, üçü de saha hatasını okunur kılmak için:
///   1. <see cref="LogLevel"/> — arayüz ile dosya farklı ayrıntı görsün
///   2. Bileşen adı — satırın hangi katmandan geldiği belli olsun
///   3. <see cref="Exception"/> taşıma — mesaj değil, tam zincir kaydedilsin
/// </summary>
public interface IAppLogger
{
    /// <summary>Bir satır yaz.</summary>
    void Write(LogLevel level, string message, Exception? error = null);

    /// <summary>Bu seviye kayda alınıyor mu — pahalı mesaj kurmadan önce sorulur.</summary>
    bool IsEnabled(LogLevel level);

    /// <summary>Bileşen adı eklenmiş bir alt logger döndürür (örn. "ChipReader").</summary>
    IAppLogger ForComponent(string component);
}

/// <summary>Seviye kısayolları ve işlem kapsamı.</summary>
public static class AppLoggerExtensions
{
    public static void Trace(this IAppLogger log, string message) => log.Write(LogLevel.Trace, message);
    public static void Debug(this IAppLogger log, string message) => log.Write(LogLevel.Debug, message);
    public static void Info(this IAppLogger log, string message) => log.Write(LogLevel.Info, message);
    public static void Warn(this IAppLogger log, string message, Exception? error = null)
        => log.Write(LogLevel.Warn, message, error);
    public static void Error(this IAppLogger log, string message, Exception? error = null)
        => log.Write(LogLevel.Error, message, error);

    /// <summary>
    /// Bir işlemi başlat — süre ve sonuç loglanır.
    /// Amaç: "nerede takıldı" sorusunun cevabı log'da kendiliğinden dursun.
    /// </summary>
    public static OperationScope BeginOperation(this IAppLogger log, string operation)
        => new(log, operation);

    /// <summary>
    /// İşlemi çalıştır, süresini ve sonucunu logla; istisna çıkarsa tam
    /// zinciriyle kaydedip yeniden fırlat.
    ///
    /// <see cref="BeginOperation"/>'dan farkı: try/catch'i kendi yapar, yani
    /// istisna ile sonlanan işlem de log'a doğru şekilde düşer.
    /// </summary>
    public static T Track<T>(this IAppLogger log, string operation, Func<T> work,
        Func<T, string>? describeResult = null)
    {
        using var op = log.BeginOperation(operation);
        try
        {
            var result = work();
            op.Success(describeResult?.Invoke(result));
            return result;
        }
        catch (Exception e)
        {
            op.Failed(e);
            throw;
        }
    }

    /// <inheritdoc cref="Track{T}"/>
    public static void Track(this IAppLogger log, string operation, Action work)
    {
        using var op = log.BeginOperation(operation);
        try
        {
            work();
            op.Success();
        }
        catch (Exception e)
        {
            op.Failed(e);
            throw;
        }
    }
}
