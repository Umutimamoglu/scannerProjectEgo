using System.Text;
using IdScanner.Core.Diagnostics;
using IdScanner.Core.Model;

namespace IdScanner.Core.Mrz;

/// <summary>MRZ metni beklenen biçimde değilse fırlatılır.</summary>
public sealed class MrzParseException(string message) : Exception(message);

/// <summary>
/// OCR metninden MRZ satırlarını yakalar, pozisyon-bazlı düzeltme yapar ve
/// BAC için gereken üç değeri (belge no, doğum, son kullanma) çıkarır.
///
/// Java karşılığı: MrzReader.parse ve yardımcıları. Saf mantık — dosya, süreç
/// veya donanım dokunuşu yok; OCR'ı çalıştıran kısım Chip/Native katmanında.
/// </summary>
public static class MrzParser
{
    /// <summary>MRZ satırı sayılabilmesi için gereken en az karakter.</summary>
    private const int MinMrzLineLength = 25;

    /// <summary>ICAO 9303 TD1 satır uzunluğu.</summary>
    private const int Td1LineLength = 30;

    /// <summary>ICAO 9303 check digit ağırlıkları — 7-3-1 dönüşümlü, mod 10.</summary>
    private static readonly int[] CheckWeights = [7, 3, 1];

    /// <summary>OCR'ın sık karıştırdığı glyph çiftleri.</summary>
    private static readonly (char Digit, char Letter)[] Confusions =
        [('0', 'O'), ('1', 'I'), ('5', 'S'), ('8', 'B'), ('2', 'Z')];

    /// <summary>
    /// OCR metnini ayrıştırır.
    /// </summary>
    /// <param name="ocrText">Tesseract veya DLL OCR çıktısı.</param>
    /// <param name="logger">Ayrıştırma adımlarını raporlamak için.</param>
    /// <exception cref="MrzParseException">MRZ satırları bulunamazsa.</exception>
    public static MrzData Parse(string ocrText, IAppLogger? logger = null)
    {
        var log = (logger ?? NullLogger.Instance).ForComponent("Mrz");

        var candidates = CollectCandidateLines(ocrText);
        log.Trace($"{candidates.Count} aday satır bulundu (>= {MinMrzLineLength} karakter)");

        var idx = FindFirstMrzLine(candidates);
        if (idx < 0 || idx + 2 >= candidates.Count)
        {
            // OCR çıktısının tamamı hata mesajında: MRZ neden bulunamadı
            // sorusunun cevabı çoğu zaman burada yazıyor.
            log.Error("MRZ 3 satırı bulunamadı. Ham OCR çıktısı:" + Environment.NewLine + ocrText);
            throw new MrzParseException("MRZ 3 satırı bulunamadı. OCR çıktısı:\n" + ocrText);
        }

        var l1 = candidates[idx];
        var l2 = candidates[idx + 1];
        var l3 = candidates[idx + 2];

        log.Debug($"MRZ satır 1: {l1}");
        log.Debug($"MRZ satır 2: {l2}");
        log.Debug($"MRZ satır 3: {l3}");

        var (docNo, _) = ParseLine1(l1, log);
        var line2 = ParseLine2(l2, log);
        var (surname, given) = ParseLine3(l3);

        var mrz = new MrzData(
            docNo, line2.DateOfBirth, line2.DateOfExpiry, line2.Sex, line2.Nationality,
            surname, given, l1, l2, l3);

        log.Info($"MRZ ayrıştırıldı: {mrz}");
        return mrz;
    }

    /// <summary>Fırlatmayan sürüm — ayrıştırılamazsa <c>false</c> döner.</summary>
    public static bool TryParse(string ocrText, out MrzData? mrz, IAppLogger? logger = null)
    {
        try
        {
            mrz = Parse(ocrText, logger);
            return true;
        }
        catch (MrzParseException)
        {
            mrz = null;
            return false;
        }
    }

    // === Satır yakalama ===

    /// <summary>Boşlukları atıp yeterince uzun satırları aday olarak topla.</summary>
    private static List<string> CollectCandidateLines(string ocrText)
    {
        var result = new List<string>();
        foreach (var raw in ocrText.Split('\n'))
        {
            var line = new string(raw.Where(c => !char.IsWhiteSpace(c)).ToArray());
            if (line.Length >= MinMrzLineLength) result.Add(line);
        }
        return result;
    }

    /// <summary>TD1'in ilk satırını bul: "I&lt;" veya "ID" ile başlar ve "TUR" içerir.</summary>
    private static int FindFirstMrzLine(List<string> candidates)
    {
        for (var i = 0; i < candidates.Count; i++)
        {
            // ToUpperInvariant bilinçli: Java'daki toUpperCase() varsayılan yerel
            // ayarı kullanıyor ve Türkçe yerelde 'i' → 'İ' olup eşleşmeyi bozabilir.
            var l = candidates[i].ToUpperInvariant();
            if ((l.StartsWith("I<", StringComparison.Ordinal) ||
                 l.StartsWith("ID", StringComparison.Ordinal)) &&
                l.Contains("TUR", StringComparison.Ordinal))
            {
                return i;
            }
        }
        return -1;
    }

    // === Satır ayrıştırma ===

    /// <summary>
    /// Satır 1: "TUR" çapasından sonra 9 karakter belge no + 1 check digit.
    /// </summary>
    private static (string DocNo, char Check) ParseLine1(string l1, IAppLogger log)
    {
        const int DocNoLength = 9;

        var turIdx = l1.ToUpperInvariant().IndexOf("TUR", StringComparison.Ordinal);
        if (turIdx < 0 || turIdx + 3 + DocNoLength + 1 > l1.Length)
        {
            throw new MrzParseException("Satır 1'de TUR bulunamadı: " + l1);
        }

        var rawDocNo = l1.Substring(turIdx + 3, DocNoLength);
        var check = l1[turIdx + 3 + DocNoLength];
        var docNo = CorrectWithCheckDigit(FixToAlphanumeric(rawDocNo), check, log);
        return (docNo, check);
    }

    private readonly record struct Line2(string DateOfBirth, string DateOfExpiry, string Sex, string Nationality);

    /// <summary>
    /// Satır 2: uyruk (3 harf) çapasından GERİYE doğru sayılır —
    /// ...&lt;dogum6&gt;&lt;chk1&gt;&lt;cinsiyet1&gt;&lt;sonKullanma6&gt;&lt;chk1&gt;TUR...
    /// </summary>
    private static Line2 ParseLine2(string l2, IAppLogger log)
    {
        // Uyruktan önce en az 15 karakter olmalı (6+1+1+6+1)
        const int MinPrefix = 15;

        var natIdx = -1;
        for (var i = l2.Length - 3; i >= MinPrefix - 1; i--)
        {
            var sub = l2.Substring(i, 3).ToUpperInvariant();
            if (sub.All(c => c is >= 'A' and <= 'Z')) { natIdx = i; break; }
        }
        if (natIdx < 0 || natIdx - MinPrefix < 0)
        {
            throw new MrzParseException("Satır 2'de uyruk bulunamadı: " + l2);
        }

        var nationality = l2.Substring(natIdx, 3);

        var expiry = CorrectWithCheckDigit(
            FixToDigits(l2.Substring(natIdx - 7, 6)), l2[natIdx - 1], log);

        var sex = NormalizeSex(l2[natIdx - 8]);

        var birth = CorrectWithCheckDigit(
            FixToDigits(l2.Substring(natIdx - 15, 6)), l2[natIdx - 9], log);

        return new Line2(birth, expiry, sex, nationality);
    }

    /// <summary>Satır 3: "SOYAD&lt;&lt;ADLAR".</summary>
    private static (string Surname, string Given) ParseLine3(string l3)
    {
        var sep = l3.IndexOf("<<", StringComparison.Ordinal);
        if (sep <= 0)
        {
            return (l3.Replace('<', ' ').Trim(), string.Empty);
        }
        var surname = l3[..sep].Replace('<', ' ').Trim();
        var given = l3[(sep + 2)..].Replace('<', ' ').Trim();
        return (surname, given);
    }

    /// <summary>OCR cinsiyet karakterini M/F/&lt; değerlerine indirger.</summary>
    private static string NormalizeSex(char c)
    {
        var sex = char.ToUpperInvariant(c).ToString();
        if (sex is "6" or "H") sex = "M";
        if (sex is not ("F" or "M" or "<")) sex = "<";
        return sex;
    }

    // === OCR düzeltme ===

    /// <summary>Rakam olması gereken pozisyonda harfleri rakama çevir.</summary>
    private static string FixToDigits(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s)
        {
            sb.Append(c switch
            {
                'O' or 'o' or 'D' => '0',
                'I' or 'l' or 'L' => '1',
                'Z' => '2',
                'S' => '5',
                'B' => '8',
                'G' or 'Q' => '9',
                '?' => '7',
                'M' or 'H' or 'N' => '2',
                _ => c,
            });
        }
        return sb.ToString();
    }

    /// <summary>Alfanumerik pozisyonda geçersiz glyph'leri dolgu karakterine çevir.</summary>
    private static string FixToAlphanumeric(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var c in s.ToUpperInvariant())
        {
            sb.Append(c is (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '<' ? c : '<');
        }
        return sb.ToString();
    }

    /// <summary>ICAO 9303 check digit: ağırlıklar 7-3-1, mod 10.</summary>
    public static int CheckDigit(string s)
    {
        var sum = 0;
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            var v = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'A' and <= 'Z' => c - 'A' + 10,
                _ => 0, // '<' veya bilinmeyen
            };
            sum += v * CheckWeights[i % CheckWeights.Length];
        }
        return sum % 10;
    }

    /// <summary>
    /// OCR bazen 0↔O, 1↔I, 5↔S karıştırır. Check digit tutmuyorsa olası tüm
    /// glyph kombinasyonlarını dener; tutan ilkini döndürür.
    ///
    /// Arama uzayı küçük: her karakterin en fazla 2 alternatifi var, belge no
    /// 9 karakter → en kötü 2^9 = 512 deneme.
    /// </summary>
    private static string CorrectWithCheckDigit(string raw, char expectedCheck, IAppLogger log)
    {
        if (expectedCheck is < '0' or > '9')
        {
            log.Trace($"'{raw}' için check digit okunamadı ('{expectedCheck}'), düzeltme atlandı");
            return raw;
        }

        var target = expectedCheck - '0';
        if (CheckDigit(raw) == target)
        {
            log.Trace($"'{raw}' check digit'i tuttu ({target})");
            return raw;
        }

        var alternatives = BuildAlternatives(raw);

        // Sayaç mantığıyla tüm kombinasyonları gez
        var n = raw.Length;
        var idx = new int[n];
        var buf = new char[n];
        while (true)
        {
            for (var i = 0; i < n; i++) buf[i] = alternatives[i][idx[i]];
            var candidate = new string(buf);
            if (CheckDigit(candidate) == target)
            {
                if (candidate != raw)
                {
                    log.Info($"OCR düzeltildi: '{raw}' → '{candidate}' (check digit '{expectedCheck}' uyumlu)");
                }
                return candidate;
            }

            var k = n - 1;
            while (k >= 0 && ++idx[k] >= alternatives[k].Length) { idx[k] = 0; k--; }
            if (k < 0) break;
        }

        // Hiçbir kombinasyon tutmadı — OCR'ın okuduğu korunuyor ama bu, BAC'ın
        // başarısız olacağının güçlü işareti. Uyarı seviyesinde bırakılıyor ki
        // "kart okunamadı" hatasının kökeni log'da görünsün.
        log.Warn($"Check digit hiçbir düzeltmeyle tutmadı, OCR korunuyor: '{raw}' " +
                 $"(beklenen check '{expectedCheck}', hesaplanan '{CheckDigit(raw)}')");
        return raw;
    }

    /// <summary>Her karakter için {kendisi, varsa karıştırıldığı eş} listesi.</summary>
    private static char[][] BuildAlternatives(string raw)
    {
        var alts = new char[raw.Length][];
        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            var list = new List<char>(2) { c };
            foreach (var (digit, letter) in Confusions)
            {
                if (c == digit) list.Add(letter);
                else if (c == letter) list.Add(digit);
            }
            alts[i] = [.. list];
        }
        return alts;
    }

    /// <summary>Satırı TD1 uzunluğuna (30) dolgu karakteriyle tamamlar veya kırpar.</summary>
    public static string PadToLineLength(string s) =>
        s.Length >= Td1LineLength ? s[..Td1LineLength] : s.PadRight(Td1LineLength, '<');
}
