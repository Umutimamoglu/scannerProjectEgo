using IdScanner.Chip.Bac;
using IdScanner.Chip.DataGroups;
using IdScanner.Chip.Pcsc;
using IdScanner.Core.Diagnostics;
using IdScanner.Core.Model;
using IdScanner.Core.Verification;
using IdScanner.Workflow.Abstractions;

namespace IdScanner.Chip;

/// <summary>
/// Temassız çipten kimlik verisi okur ve gerçekliğini doğrular.
///
/// Java karşılığı: IdCardReader.readChip (JMRTD ile).
///
/// Akış:
///   1. PC/SC okuyucuya bağlan, kartı bekle
///   2. MRZ'den BAC anahtarlarını türet, karşılıklı doğrulama yap
///   3. Veri gruplarını <b>ham</b> oku (hash kontrolü aynı baytlardan yapılsın)
///   4. Passive Authentication — veri tahrif edilmiş mi, imza gerçek mi
///   5. Active Authentication — çip klon mu (DG15 varsa)
///   6. Ham baytları çözümleyip alanları doldur
///
/// <b>Sıralama önemli:</b> Ham okuma ile çözümleme ayrı adımlar. Java'da da
/// böyle yapılmıştı, sebebi hash'in ve ayrıştırmanın <i>aynı</i> baytlardan
/// çalışması — yeniden okunan baytlar farklı olabilir ve doğrulama boşuna
/// başarısız olur.
/// </summary>
public sealed class PassportChipReader(
    IChipVerifier verifier,
    IActiveAuthVerifier activeAuthVerifier,
    IAppLogger? logger = null,
    DiagnosticsDump? dump = null,
    TimeSpan? cardWaitTime = null) : IChipReader
{
    private const byte ClaPlain = 0x00;
    private const byte InsInternalAuthenticate = 0x88;

    /// <summary>Active Authentication challenge uzunluğu — ICAO 9303 sabiti.</summary>
    private const int AaChallengeLength = 8;

    private readonly IAppLogger _log = (logger ?? NullLogger.Instance).ForComponent("Chip");
    private readonly DiagnosticsDump _dump = dump ?? DiagnosticsDump.Disabled;
    private readonly TimeSpan _cardWait = cardWaitTime ?? TimeSpan.FromSeconds(10);

    /// <summary>Okunacak veri grupları — sıra, log'daki sırayı belirler.</summary>
    private static readonly (ushort FileId, int Number, string Label)[] DataGroups =
    [
        (ElementaryFile.Dg1, 1, "DG1 (MRZ)"),
        (ElementaryFile.Dg2, 2, "DG2 (fotoğraf)"),
        (ElementaryFile.Dg11, 11, "DG11 (Türkçe isimler)"),
        (ElementaryFile.Dg12, 12, "DG12 (belge bilgisi)"),
        (ElementaryFile.Dg15, 15, "DG15 (AA anahtarı)"),
    ];

    /// <inheritdoc />
    public bool IsReaderAvailable() => PcscConnection.ListReaders(_log).Count > 0;

    /// <inheritdoc />
    public IdData Read(BacCredentials credentials)
    {
        using var op = _log.BeginOperation("Çip okuma");
        var data = new IdData
        {
            BacDocNo = credentials.DocumentNumber,
            BacBirth = credentials.DateOfBirth,
            BacExpiry = credentials.DateOfExpiry,
        };

        using var connection = new PcscConnection(_log, _dump);
        connection.Connect(_cardWait);

        var atr = connection.GetAtr();
        if (atr.Length > 0) _log.Debug($"Kart ATR: {Hex.ToHex(atr)}");

        var keys = BacKeyDerivation.Derive(
            credentials.DocumentNumber, credentials.DateOfBirth, credentials.DateOfExpiry, _log);

        var session = BacHandshake.Perform(connection, keys, _log);
        var files = new ChipFileReader(connection, session.Messaging, _log);
        files.SelectApplication();

        var rawDataGroups = ReadRawDataGroups(files);
        var rawSod = ReadSod(files);

        // Doğrulama, çözümlemeden ÖNCE — ham baytlar hâlâ elimizdeyken
        data.Verification = VerifyPassive(rawSod, rawDataGroups);
        TryActiveAuth(connection, session, rawDataGroups, data);

        ParseDataGroups(rawDataGroups, data);

        data.Kaynak = KartKaynagi.CipTckk;
        op.Success($"{rawDataGroups.Count} veri grubu okundu");
        return data;
    }

    /// <summary>Veri gruplarını ham olarak oku ve dök.</summary>
    private Dictionary<int, byte[]> ReadRawDataGroups(ChipFileReader files)
    {
        var result = new Dictionary<int, byte[]>();

        foreach (var (fileId, number, label) in DataGroups)
        {
            var raw = files.ReadFile(fileId, label);
            if (raw is null)
            {
                // Kartta o DG yoksa bu normaldir — DG15 opsiyonel, DG11/12 de öyle
                _log.Warn($"{label} okunamadı, atlanıyor");
                continue;
            }

            result[number] = raw;
            _dump.WriteDataGroup(number, raw);
        }

        return result;
    }

    /// <summary>EF.SOD'u oku ve dök.</summary>
    private byte[]? ReadSod(ChipFileReader files)
    {
        var raw = files.ReadFile(ElementaryFile.Sod, "EF.SOD");
        if (raw is null)
        {
            _log.Error("EF.SOD okunamadı — Passive Authentication yapılamayacak");
            return null;
        }
        _dump.WriteSod(raw);
        return raw;
    }

    /// <summary>Passive Authentication — veri tahrif edilmiş mi, imza gerçek mi.</summary>
    private VerificationResult VerifyPassive(byte[]? rawSod, Dictionary<int, byte[]> rawDataGroups)
    {
        if (rawSod is null)
        {
            var empty = new VerificationResult();
            empty.Checks.Add(new VerificationCheck("SOD okunamadı", false, "doğrulama yapılamıyor"));
            return empty;
        }

        var result = verifier.VerifyPassiveAuth(rawSod, rawDataGroups);
        foreach (var line in result.Report()) _log.Info(line);
        return result;
    }

    /// <summary>
    /// Active Authentication — DG15 varsa çipe challenge gönderip cevabı doğrula.
    ///
    /// Başarısızlık akışı durdurmaz (bilgilendirici mod); sonuç yalnızca
    /// raporlanır.
    /// </summary>
    private void TryActiveAuth(
        PcscConnection connection, BacHandshake.Session session,
        Dictionary<int, byte[]> rawDataGroups, IdData data)
    {
        if (!rawDataGroups.TryGetValue(15, out var rawDg15))
        {
            _log.Debug("DG15 yok — Active Authentication atlanıyor (kart bu özelliği desteklemiyor olabilir)");
            return;
        }

        var publicKeyDer = DataGroupParser.ParseDg15PublicKey(rawDg15, _log);
        if (publicKeyDer is null) return;

        try
        {
            var challenge = ChipCrypto.RandomBytes(AaChallengeLength);
            var response = InternalAuthenticate(connection, session, challenge);

            data.Verification ??= new VerificationResult();
            var line = activeAuthVerifier.Verify(publicKeyDer, challenge, response, data.Verification);
            _log.Info(line);
        }
        catch (Exception e)
        {
            // AA başarısızlığı okumanın tamamını düşürmemeli — Java'da da böyleydi
            _log.Warn("Active Authentication yapılamadı", e);
        }
    }

    /// <summary>INTERNAL AUTHENTICATE — çipe challenge gönder, imzalı cevabı al.</summary>
    private byte[] InternalAuthenticate(
        PcscConnection connection, BacHandshake.Session session, byte[] challenge)
    {
        var apdu = CommandApdu.Send(
            ClaPlain, InsInternalAuthenticate, 0x00, 0x00, challenge, le: 0x00, hasLe: true);

        var protectedApdu = session.Messaging.Protect(apdu);
        var response = connection.Transmit(protectedApdu);
        var (body, sw) = StatusWord.Split(response);

        if (!StatusWord.IsSuccess(sw))
        {
            throw new ChipProtocolException($"INTERNAL AUTHENTICATE reddedildi: {StatusWord.Describe(sw)}");
        }

        var signature = session.Messaging.Unprotect(body);
        _log.Debug($"AA imzası alındı: {signature.Length} bayt");
        return signature;
    }

    /// <summary>Ham baytları çözümleyip alanları doldur.</summary>
    private void ParseDataGroups(Dictionary<int, byte[]> rawDataGroups, IdData data)
    {
        TryParse(rawDataGroups, 1, "DG1", raw => DataGroupParser.ParseDg1(raw, data, _log));

        TryParse(rawDataGroups, 2, "DG2", raw =>
        {
            data.Photo = FaceImageExtractor.Extract(raw, _log);
        });

        // DG11 DG1'in üzerine yazar — Türkçe karakterler orada
        TryParse(rawDataGroups, 11, "DG11", raw => DataGroupParser.ParseDg11(raw, data, _log));
        TryParse(rawDataGroups, 12, "DG12", raw => DataGroupParser.ParseDg12(raw, data, _log));
    }

    /// <summary>
    /// Bir veri grubunu çözümle; hata olursa kaydet ama akışı durdurma.
    ///
    /// Java'daki davranış: bir DG çözümlenemezse diğerleri yine okunur.
    /// Kısmi veri, hiç veri olmamasından iyidir.
    /// </summary>
    private void TryParse(Dictionary<int, byte[]> rawDataGroups, int number, string label, Action<byte[]> parse)
    {
        if (!rawDataGroups.TryGetValue(number, out var raw)) return;

        try
        {
            parse(raw);
        }
        catch (Exception e)
        {
            _log.Error($"{label} çözümlenemedi ({raw.Length} bayt). " +
                       $"Ham içerik döküm klasöründe: dg{number}.bin", e);
        }
    }
}
