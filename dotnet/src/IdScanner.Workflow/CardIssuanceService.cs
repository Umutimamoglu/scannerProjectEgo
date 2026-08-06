using IdScanner.Core.Diagnostics;
using IdScanner.Core.Model;
using IdScanner.Workflow.Abstractions;

namespace IdScanner.Workflow;

/// <summary>Bir akış adımının sonucu.</summary>
/// <typeparam name="T">Başarı durumunda dönen değer.</typeparam>
public readonly record struct StepResult<T>(bool Ok, T? Value, string Message)
{
    public static StepResult<T> Success(T value, string message = "") => new(true, value, message);
    public static StepResult<T> Failure(string message) => new(false, default, message);
}

/// <summary>
/// Kart verme akışını yürütür: tara → çipi oku → doğrula → çiz → bas.
///
/// <b>Java'da bu mantık MainUI'ın içindeydi</b> — 837 satırlık formun bir
/// bölümüydü. Buraya taşınmasının sebebi, arayüzün değiştirilebilir olması:
/// ileride kiosk arayüzü yazılırsa akış aynen kalır, yalnızca üstündeki ekran
/// değişir. Ayrıca akış, gerçek donanım olmadan (sahte uygulamalarla) test
/// edilebilir hale geliyor.
///
/// Bu sınıf hiçbir somut donanım sınıfı tanımaz; yalnızca arayüzleri bilir.
/// </summary>
public sealed class CardIssuanceService(
    IDocumentScanner scanner,
    IChipReader chipReader,
    ICardRenderer renderer,
    ICardPrinter printer,
    IAppLogger? logger = null)
{
    private readonly IAppLogger _log = (logger ?? NullLogger.Instance).ForComponent("Akış");

    /// <summary>Son okunan kimlik verisi — baskı bunu kullanır.</summary>
    public IdData? LastRead { get; private set; }

    /// <summary>
    /// Kartı tara, çipi oku ve doğrula.
    ///
    /// Java karşılığı: MainUI.doScanAndRead içindeki arka plan işi.
    /// </summary>
    public StepResult<IdData> ScanAndRead()
    {
        using var op = _log.BeginOperation("Tara ve oku");

        try
        {
            var scan = scanner.Scan();

            if (scan.Mrz is null)
            {
                op.Failure("MRZ okunamadı");
                return StepResult<IdData>.Failure(
                    "MRZ okunamadı — çip anahtarları üretilemiyor. Kartı ters koymuş olabilir misiniz?");
            }

            scanner.MoveToNfc();

            var credentials = new BacCredentials(
                scan.Mrz.DocumentNumber, scan.Mrz.DateOfBirth, scan.Mrz.DateOfExpiry);

            var data = chipReader.Read(credentials);

            // MRZ satırlarını taramadan al — çipten gelenle aynı olmalı ama
            // taranan hâli kullanıcıya gösterilecek olan.
            data.MrzLine1 = scan.Mrz.RawLine1;
            data.MrzLine2 = scan.Mrz.RawLine2;
            data.MrzLine3 = scan.Mrz.RawLine3;

            LastRead = data;

            var summary = $"{data.Name} {data.Surname}";
            op.Success(summary);
            LogVerificationSummary(data);

            return StepResult<IdData>.Success(data, summary);
        }
        catch (Exception e)
        {
            op.Failed(e);
            return StepResult<IdData>.Failure(e.Message);
        }
    }

    /// <summary>
    /// Doğrulama sonucunu tek satırda özetle.
    ///
    /// <b>Bilgilendirici mod:</b> başarısızlık akışı durdurmaz. Bu bilinçli bir
    /// karar — sistem sahte/klon kartı tespit ediyor ama reddetmiyor.
    /// </summary>
    private void LogVerificationSummary(IdData data)
    {
        if (data.Verification is not { } v)
        {
            _log.Warn("Çip doğrulaması yapılmadı");
            return;
        }

        var parts = new List<string>
        {
            v.AllHashesMatched ? "veri bütün" : "VERİ DEĞİŞMİŞ",
            v.SodSignatureValid ? "imza geçerli" : "İMZA GEÇERSİZ",
            v.ChainValid ? $"kök: {v.MatchedRootCn}" : "ZİNCİR YOK",
        };

        if (v.AaAttempted) parts.Add(v.AaValid ? "klon değil" : "KLON ŞÜPHESİ");

        var line = "Doğrulama: " + string.Join(" · ", parts);
        var healthy = v.AllHashesMatched && v.SodSignatureValid && v.ChainValid && (!v.AaAttempted || v.AaValid);

        if (healthy) _log.Info(line); else _log.Warn(line + "  (bilgilendirici mod: akış durdurulmuyor)");
    }

    /// <summary>Kartı cihazdan çıkar.</summary>
    public void Eject()
    {
        _log.Info("Kart çıkarılıyor");
        scanner.Eject();
    }

    /// <summary>Okunan veriden karta basılacak veriyi hazırla.</summary>
    public CardData BuildCardData(IdData data, string? photoPath = null) => new()
    {
        Name = data.Name,
        Surname = data.Surname,
        IdNumber = data.TcNo,
        BirthDate = data.BirthDate,
        ExpiryDate = data.ExpiryDate,
        PhotoPath = photoPath,
    };

    /// <summary>Ön yüz önizlemesi (PNG).</summary>
    public byte[] PreviewFront(CardData data) => renderer.RenderFrontPng(data);

    /// <summary>Ön + arka yan yana önizleme (PNG).</summary>
    public byte[] PreviewBoth(CardData data) => renderer.RenderBothPng(data);

    /// <summary>
    /// Kartı bas.
    /// </summary>
    /// <param name="data">Basılacak veri.</param>
    /// <param name="dryRun"><c>true</c> ise PRN üretir, kart harcamaz.</param>
    public StepResult<PrintResult> Print(CardData data, bool dryRun)
    {
        using var op = _log.BeginOperation(dryRun ? "Prova baskı" : "Kart baskı");

        try
        {
            if (!printer.IsConnected)
            {
                op.Failure("yazıcı bağlı değil");
                return StepResult<PrintResult>.Failure("Yazıcı bağlı değil");
            }

            var (front, back) = renderer.RenderFacesToBmp(data);
            var result = printer.PrintDuplex(front, back, dryRun);

            if (result.Ok) op.Success(result.Message); else op.Failure(result.Message);
            return result.Ok
                ? StepResult<PrintResult>.Success(result, result.Message)
                : StepResult<PrintResult>.Failure(result.Message);
        }
        catch (Exception e)
        {
            op.Failed(e);
            return StepResult<PrintResult>.Failure(e.Message);
        }
    }

    /// <summary>
    /// Kalibrasyon kartı bas — yarım panel renkli bandın kartın neresine
    /// düştüğünü ölçmek için.
    /// </summary>
    public StepResult<PrintResult> PrintCalibration(bool dryRun)
    {
        using var op = _log.BeginOperation("Kalibrasyon kartı");

        try
        {
            if (!printer.IsConnected)
            {
                op.Failure("yazıcı bağlı değil");
                return StepResult<PrintResult>.Failure("Yazıcı bağlı değil");
            }

            var (front, back) = renderer.RenderCalibrationToBmp();
            var result = printer.PrintDuplex(front, back, dryRun);

            if (result.Ok) op.Success(result.Message); else op.Failure(result.Message);
            return result.Ok
                ? StepResult<PrintResult>.Success(result, result.Message)
                : StepResult<PrintResult>.Failure(result.Message);
        }
        catch (Exception e)
        {
            op.Failed(e);
            return StepResult<PrintResult>.Failure(e.Message);
        }
    }
}
