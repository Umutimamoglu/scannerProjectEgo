using IdScanner.Chip.Bac;
using IdScanner.Core.Diagnostics;
using PCSC;

namespace IdScanner.Chip.Pcsc;

/// <summary>PC/SC katmanında bir sorun olduğunda fırlatılır.</summary>
public sealed class PcscException(string message, Exception? inner = null)
    : Exception(message, inner);

/// <summary>
/// Temassız okuyucuya PC/SC üzerinden bağlantı ve ham APDU alışverişi.
///
/// Java karşılığı: <c>javax.smartcardio</c> (TerminalFactory, CardTerminal,
/// CardService). .NET'te PCSC (pcsc-sharp) paketi kullanılıyor.
///
/// <b>Hata ayıklama:</b> Gönderilen ve alınan her APDU, döküm açıksa
/// <c>apdu.log</c> dosyasına yazılır. Çip iletişimi bozulduğunda sorunun
/// hangi komutta başladığını görmenin başka yolu yok.
/// </summary>
internal sealed class PcscConnection : IDisposable
{
    /// <summary>Cevap tamponu — DG2 gibi büyük dosyalar parça parça okunur.</summary>
    private const int ReceiveBufferSize = 512;

    private readonly IAppLogger _log;
    private readonly DiagnosticsDump _dump;
    private ISCardContext? _context;
    private ICardReader? _reader;
    private bool _disposed;

    /// <summary>Bağlanılan okuyucunun adı.</summary>
    internal string ReaderName { get; private set; } = "";

    internal PcscConnection(IAppLogger log, DiagnosticsDump dump)
    {
        _log = log.ForComponent("Pcsc");
        _dump = dump;
    }

    /// <summary>Sistemdeki PC/SC okuyucularının adları.</summary>
    internal static IReadOnlyList<string> ListReaders(IAppLogger log)
    {
        try
        {
            using var context = ContextFactory.Instance.Establish(SCardScope.System);
            var readers = context.GetReaders();
            return readers is { Length: > 0 } ? readers : [];
        }
        catch (Exception e)
        {
            log.Warn("PC/SC okuyucu listesi alınamadı — Smart Card servisi çalışıyor mu?", e);
            return [];
        }
    }

    /// <summary>
    /// İlk okuyucuya bağlan ve kart gelene kadar bekle.
    /// </summary>
    /// <param name="waitTime">Kart için beklenecek en uzun süre.</param>
    internal void Connect(TimeSpan waitTime)
    {
        _context = ContextFactory.Instance.Establish(SCardScope.System);

        var readers = _context.GetReaders();
        if (readers is not { Length: > 0 })
        {
            throw new PcscException("PC/SC okuyucu bulunamadı — okuyucu takılı mı, sürücüsü yüklü mü?");
        }

        _log.Debug($"Bulunan okuyucular: {string.Join(", ", readers)}");
        ReaderName = readers[0];
        _log.Info($"Okuyucu: {ReaderName}");

        WaitForCard(waitTime);
    }

    /// <summary>Kart alana girene kadar bekle; giremezse istisna fırlat.</summary>
    private void WaitForCard(TimeSpan waitTime)
    {
        using var op = _log.BeginOperation($"Kart bekleme (en fazla {waitTime.TotalSeconds:F0} sn)");
        var deadline = DateTime.UtcNow + waitTime;
        var attempt = 0;

        while (DateTime.UtcNow < deadline)
        {
            attempt++;
            try
            {
                _reader = _context!.ConnectReader(ReaderName, SCardShareMode.Shared, SCardProtocol.Any);
                op.Success($"{attempt}. denemede bağlanıldı, protokol {_reader.Protocol}");
                return;
            }
            catch (Exception e)
            {
                // Kart henüz alanda değil — normal durum, sessizce yeniden dene.
                if (attempt == 1) _log.Trace($"Kart henüz yok ({e.GetType().Name}), bekleniyor");
                Thread.Sleep(300);
            }
        }

        op.Failure("kart algılanmadı");
        throw new PcscException(
            $"NFC alanında kart algılanmadı ({waitTime.TotalSeconds:F0} sn beklendi). " +
            "Kart okuma pozisyonunda mı?");
    }

    /// <summary>
    /// Ham APDU gönder ve cevabı al.
    ///
    /// Kısaltılmış cevap (61xx) ve yanlış uzunluk (6Cxx) durumları burada
    /// <b>ele alınmaz</b> — Secure Messaging altında bunlar sarmalanmış
    /// cevabın içinde gelir ve çözüldükten sonra anlam kazanır.
    /// </summary>
    internal byte[] Transmit(byte[] command)
    {
        if (_reader is null) throw new PcscException("Okuyucuya bağlı değil");

        _dump.WriteApdu(">>", command);
        _log.Trace($">> {Hex.ToHex(command)}");

        var buffer = new byte[ReceiveBufferSize];
        int received;
        try
        {
            received = _reader.Transmit(command, buffer);
        }
        catch (Exception e)
        {
            _log.Error($"APDU gönderilemedi: {Hex.ToHex(command)}", e);
            throw new PcscException("Karta komut gönderilemedi — kart alandan çıkmış olabilir", e);
        }

        var response = buffer[..received];
        _dump.WriteApdu("<<", response);
        _log.Trace($"<< {Hex.ToHex(response)}");

        return response;
    }

    /// <summary>Kartın ATR'si (Answer To Reset) — kart tipini tanımaya yarar.</summary>
    internal byte[] GetAtr()
    {
        try
        {
            return _reader?.GetAttrib(SCardAttribute.AtrString) ?? [];
        }
        catch (Exception e)
        {
            _log.Trace($"ATR okunamadı: {e.Message}");
            return [];
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _reader?.Dispose();
            _context?.Dispose();
        }
        catch (Exception e)
        {
            _log.Trace($"PC/SC kaynakları kapatılırken hata: {e.Message}");
        }

        _reader = null;
        _context = null;
    }
}
