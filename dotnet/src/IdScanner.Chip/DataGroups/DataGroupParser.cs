using System.Text;
using IdScanner.Chip.Tlv;
using IdScanner.Core.Diagnostics;
using IdScanner.Core.Model;

namespace IdScanner.Chip.DataGroups;

/// <summary>
/// Veri gruplarının içeriğini çözer.
///
/// Java karşılığı: JMRTD'nin DG1File / DG11File / DG12File / DG15File
/// sınıfları ve IdCardReader'daki parseDgN metotları.
///
/// <b>Kodlama notu:</b> DG11 ve DG12'deki metinler UTF-8 olabilir; MRZ ise
/// yalnızca ASCII taşır. Türkçe isimler (İ, Ğ, Ş) bu yüzden DG11'den alınır
/// ve DG1'den gelen ASCII karşılıklarının üzerine yazılır.
/// </summary>
internal static class DataGroupParser
{
    // ICAO 9303 Part 10 veri nesnesi etiketleri
    private const int TagMrz = 0x5F1F;             // DG1 içindeki MRZ metni
    private const int TagFullName = 0x5F0E;        // DG11 — "SOYAD<<AD"
    private const int TagPersonalNumber = 0x5F10;  // DG11 — T.C. kimlik no
    private const int TagPlaceOfBirth = 0x5F11;    // DG11 — doğum yeri
    private const int TagFullDateOfBirth = 0x5F2B; // DG11 — YYYYMMDD
    private const int TagIssuingAuthority = 0x5F19; // DG12 — veren makam
    private const int TagDateOfIssue = 0x5F26;     // DG12 — veriliş tarihi

    /// <summary>DG1 — MRZ metnini çıkar ve alanları doldur.</summary>
    internal static void ParseDg1(byte[] raw, IdData data, IAppLogger log)
    {
        var mrzBytes = BerTlv.FindValue(raw, TagMrz);
        if (mrzBytes is null)
        {
            log.Warn($"DG1 içinde MRZ etiketi (5F1F) bulunamadı. İçerik:{Environment.NewLine}{BerTlv.Describe(raw)}");
            return;
        }

        var mrz = Encoding.ASCII.GetString(mrzBytes).Trim();
        log.Debug($"DG1 MRZ ({mrz.Length} karakter): {mrz}");

        // TD1: 3 satır × 30 karakter, bitişik gelir
        if (mrz.Length >= 90)
        {
            data.MrzLine1 = mrz[..30];
            data.MrzLine2 = mrz[30..60];
            data.MrzLine3 = mrz[60..90];
        }

        var parsed = Core.Mrz.MrzParser.TryParse(
            string.Join('\n', data.MrzLine1, data.MrzLine2, data.MrzLine3), out var mrzData, log);

        if (!parsed || mrzData is null)
        {
            log.Warn("DG1'deki MRZ ayrıştırılamadı");
            return;
        }

        data.Name = Clean(mrzData.SecondaryName);
        data.Surname = Clean(mrzData.PrimaryName);
        data.DocumentNumber = mrzData.DocumentNumber;
        data.Nationality = mrzData.Nationality;
        data.Gender = mrzData.Sex;
        data.BirthDate = FormatDate(mrzData.DateOfBirth);
        data.ExpiryDate = FormatDate(mrzData.DateOfExpiry);

        // TD1'de T.C. kimlik no, satır 1'in opsiyonel alanında taşınır
        var optional = data.MrzLine1.Length >= 30 ? data.MrzLine1[15..30] : "";
        var personalNumber = optional.Replace("<", "").Trim();
        if (personalNumber.Length > 0) data.TcNo = personalNumber;

        log.Info("DG1 okundu");
    }

    /// <summary>DG11 — Türkçe karakterli isimler ve ek kişisel bilgi.</summary>
    internal static void ParseDg11(byte[] raw, IdData data, IAppLogger log)
    {
        var fullName = ReadText(raw, TagFullName);
        if (fullName is not null)
        {
            // "SOYAD<<AD" biçiminde — MRZ'nin aksine Türkçe karakterlerle
            var separator = fullName.IndexOf("<<", StringComparison.Ordinal);
            if (separator > 0)
            {
                data.Surname = Clean(fullName[..separator]);
                data.Name = Clean(fullName[(separator + 2)..]);
            }
            else
            {
                data.Surname = Clean(fullName);
            }
            log.Debug($"DG11 ad/soyad: {data.Name} {data.Surname}");
        }

        var personalNumber = ReadText(raw, TagPersonalNumber);
        if (!string.IsNullOrWhiteSpace(personalNumber)) data.TcNo = personalNumber.Trim();

        var placeOfBirth = ReadText(raw, TagPlaceOfBirth);
        if (!string.IsNullOrWhiteSpace(placeOfBirth)) data.BirthPlace = Clean(placeOfBirth);

        var fullDob = ReadText(raw, TagFullDateOfBirth);
        if (fullDob is { Length: 8 })
        {
            // YYYYMMDD → GG.AA.YYYY (DG11 tam yılı taşır, MRZ yalnızca son iki hane)
            data.BirthDate = $"{fullDob[6..8]}.{fullDob[4..6]}.{fullDob[..4]}";
        }

        log.Info("DG11 okundu (Türkçe isimler)");
    }

    /// <summary>DG12 — veren makam ve veriliş tarihi.</summary>
    internal static void ParseDg12(byte[] raw, IdData data, IAppLogger log)
    {
        var authority = ReadText(raw, TagIssuingAuthority);
        if (!string.IsNullOrWhiteSpace(authority)) data.IssuingAuthority = Clean(authority);

        var issueDate = ReadText(raw, TagDateOfIssue);
        if (issueDate is { Length: 8 })
        {
            data.IssueDate = $"{issueDate[6..8]}.{issueDate[4..6]}.{issueDate[..4]}";
        }
        else if (!string.IsNullOrWhiteSpace(issueDate))
        {
            data.IssueDate = issueDate.Trim();
        }

        log.Info("DG12 okundu");
    }

    /// <summary>
    /// DG15 — Active Authentication açık anahtarı (SubjectPublicKeyInfo, DER).
    /// </summary>
    /// <returns>DER kodlu anahtar; çıkarılamazsa <c>null</c>.</returns>
    internal static byte[]? ParseDg15PublicKey(byte[] raw, IAppLogger log)
    {
        // DG15 = [APPLICATION 15] (0x6F) sarmalı içinde SubjectPublicKeyInfo
        foreach (var item in SafeParse(raw))
        {
            if (item.Tag != 0x6F) continue;
            log.Debug($"DG15 açık anahtarı bulundu ({item.Value.Length} bayt)");
            return item.Value;
        }

        // Sarmal yoksa içerik doğrudan SubjectPublicKeyInfo olabilir
        if (raw.Length > 0 && raw[0] == 0x30)
        {
            log.Debug("DG15 sarmalsız — içerik doğrudan SubjectPublicKeyInfo sayıldı");
            return raw;
        }

        log.Warn($"DG15 çözümlenemedi. İçerik:{Environment.NewLine}{BerTlv.Describe(raw)}");
        return null;
    }

    // === Yardımcılar ===

    private static IEnumerable<TlvItem> SafeParse(byte[] data)
    {
        try
        {
            return BerTlv.Parse(data).ToList();
        }
        catch (TlvParseException)
        {
            return [];
        }
    }

    /// <summary>Etiketi bul ve metne çevir (UTF-8, olmazsa Latin-1).</summary>
    private static string? ReadText(byte[] raw, int tag)
    {
        var value = BerTlv.FindValue(raw, tag);
        if (value is null) return null;

        try
        {
            return new UTF8Encoding(false, true).GetString(value);
        }
        catch (DecoderFallbackException)
        {
            // Bazı kartlar Latin-1 yazıyor; UTF-8 çözülemezse ona düş
            return Encoding.Latin1.GetString(value);
        }
    }

    /// <summary>MRZ dolgu karakterlerini temizle ve boşlukları düzelt.</summary>
    private static string Clean(string value) =>
        string.Join(' ', value.Replace('<', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries));

    /// <summary>YYMMDD → GG.AA.YYYY. 40 ve altı 2000'li, üstü 1900'lü yıl sayılır.</summary>
    private static string FormatDate(string yymmdd)
    {
        if (yymmdd.Length != 6 || !yymmdd.All(char.IsDigit)) return "";
        var year = int.Parse(yymmdd[..2]) <= 40 ? "20" + yymmdd[..2] : "19" + yymmdd[..2];
        return $"{yymmdd[4..6]}.{yymmdd[2..4]}.{year}";
    }
}
