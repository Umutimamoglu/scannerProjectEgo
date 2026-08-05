namespace IdScanner.Core.Diagnostics;

/// <summary>
/// Bir okuma oturumunda çipten gelen ham veriyi diske döker.
///
/// <b>Neden var:</b> Çip okuma hatalarında log satırı çoğu zaman yetmez —
/// "DG11 çözümlenemedi" yazar ama çipin ne gönderdiğini söylemez. Ham baytlar
/// diskte durursa, sorun sahada değil masa başında çözülebilir: aynı baytlarla
/// çözümleyiciyi tekrar tekrar çalıştırmak mümkün olur.
///
/// Her oturum kendi klasörüne yazar:
/// <code>
///   diag/20260807-143210/
///     ├─ sod.bin
///     ├─ dg1.bin, dg2.bin, dg11.bin, dg12.bin, dg15.bin
///     ├─ apdu.log
///     └─ ozet.txt
/// </code>
///
/// <b>Gizlilik:</b> Bu dosyalar gerçek bir kişinin adını, fotoğrafını ve
/// T.C. kimlik numarasını içerir. <c>diag/</c> klasörü depoya girmez
/// (.gitignore) ve paylaşılmadan önce temizlenmelidir.
/// </summary>
public sealed class DiagnosticsDump
{
    private readonly string? _sessionDirectory;
    private readonly IAppLogger _log;
    private readonly Lock _gate = new();

    /// <summary>Döküm açık mı.</summary>
    public bool IsEnabled => _sessionDirectory is not null;

    /// <summary>Bu oturumun klasörü; kapalıysa <c>null</c>.</summary>
    public string? Directory => _sessionDirectory;

    /// <summary>Hiçbir şey yazmayan döküm.</summary>
    public static DiagnosticsDump Disabled { get; } = new();

    private DiagnosticsDump()
    {
        _sessionDirectory = null;
        _log = NullLogger.Instance;
    }

    /// <param name="rootDirectory">Tüm oturumların altında toplanacağı klasör.</param>
    /// <param name="logger">Döküm işlemlerinin kendisini loglamak için.</param>
    public DiagnosticsDump(string rootDirectory, IAppLogger? logger = null)
    {
        _log = (logger ?? NullLogger.Instance).ForComponent("Dump");
        try
        {
            var name = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var dir = Path.Combine(rootDirectory, name);
            System.IO.Directory.CreateDirectory(dir);
            _sessionDirectory = dir;
            _log.Info("Oturum dökümü açık: " + dir);
        }
        catch (Exception e)
        {
            _sessionDirectory = null;
            _log.Warn("Döküm klasörü oluşturulamadı, döküm kapalı", e);
        }
    }

    /// <summary>Ham baytları adlandırılmış bir dosyaya yaz.</summary>
    public void WriteBytes(string fileName, ReadOnlySpan<byte> data)
    {
        if (_sessionDirectory is null) return;

        var bytes = data.ToArray();
        lock (_gate)
        {
            try
            {
                var path = Path.Combine(_sessionDirectory, fileName);
                File.WriteAllBytes(path, bytes);
                _log.Debug($"{fileName} yazıldı ({bytes.Length} bayt)");
            }
            catch (Exception e)
            {
                _log.Warn($"{fileName} yazılamadı", e);
            }
        }
    }

    /// <summary>Metin dosyasına ekle (APDU izi, özet gibi).</summary>
    public void AppendText(string fileName, string text)
    {
        if (_sessionDirectory is null) return;

        lock (_gate)
        {
            try
            {
                var path = Path.Combine(_sessionDirectory, fileName);
                File.AppendAllText(path, text + Environment.NewLine);
            }
            catch (Exception e)
            {
                _log.Warn($"{fileName} yazılamadı", e);
            }
        }
    }

    /// <summary>Bir veri grubunun ham hâlini dök: <c>dg11.bin</c>.</summary>
    public void WriteDataGroup(int dataGroupNumber, ReadOnlySpan<byte> raw)
        => WriteBytes($"dg{dataGroupNumber}.bin", raw);

    /// <summary>EF.SOD'un ham hâlini dök.</summary>
    public void WriteSod(ReadOnlySpan<byte> raw) => WriteBytes("sod.bin", raw);

    /// <summary>Gönderilen ve alınan APDU'yu ize ekle.</summary>
    public void WriteApdu(string direction, ReadOnlySpan<byte> data)
        => AppendText("apdu.log", $"{DateTime.Now:HH:mm:ss.fff}  {direction,-4}  {Hex.ToHex(data)}");
}
