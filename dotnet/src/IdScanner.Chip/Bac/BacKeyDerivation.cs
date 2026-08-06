using System.Text;
using IdScanner.Core.Diagnostics;
using Org.BouncyCastle.Crypto.Digests;

namespace IdScanner.Chip.Bac;

/// <summary>
/// BAC (Basic Access Control) anahtar türetme — ICAO 9303 Part 11, §9.7.2.
///
/// <b>Java'da bu iş JMRTD'nin içindeydi</b> (<c>BACKey</c> + <c>Util</c>);
/// .NET karşılığı olmadığı için elle yazıldı.
///
/// Mantık: çip, MRZ'yi <i>okuyabilen</i> birinin veriye erişmesine izin verir.
/// Yani anahtar, kartın üzerinde yazan üç bilgiden türetilir — belge numarası,
/// doğum tarihi ve son kullanma tarihi. Kartı fiziksel olarak görmeyen biri
/// bunları bilemeyeceği için çip uzaktan okunamaz.
/// </summary>
internal static class BacKeyDerivation
{
    /// <summary>Şifreleme anahtarı için KDF sayacı.</summary>
    private const int CounterEncryption = 1;

    /// <summary>MAC anahtarı için KDF sayacı.</summary>
    private const int CounterMac = 2;

    /// <summary>Türetilen anahtar uzunluğu — çift uzunlukta 3DES (2 × 8 bayt).</summary>
    private const int KeyLength = 16;

    /// <summary>BAC oturumunda kullanılan anahtar çifti.</summary>
    /// <param name="Encryption">K_enc — veri şifreleme.</param>
    /// <param name="Mac">K_mac — bütünlük kontrolü.</param>
    internal readonly record struct KeyPair(byte[] Encryption, byte[] Mac);

    /// <summary>
    /// MRZ bilgilerinden K_enc ve K_mac türet.
    /// </summary>
    /// <param name="documentNumber">Belge numarası (9 karaktere '&lt;' ile tamamlanır).</param>
    /// <param name="dateOfBirth">Doğum tarihi, YYMMDD.</param>
    /// <param name="dateOfExpiry">Son kullanma tarihi, YYMMDD.</param>
    /// <param name="log">Tanılama.</param>
    internal static KeyPair Derive(string documentNumber, string dateOfBirth, string dateOfExpiry, IAppLogger log)
    {
        var mrzInfo = BuildMrzInformation(documentNumber, dateOfBirth, dateOfExpiry);
        log.Trace($"BAC girdisi (MRZ_information): {mrzInfo}");

        // K_seed = SHA-1(MRZ_information)'ın ilk 16 baytı
        var seed = Sha1(Encoding.ASCII.GetBytes(mrzInfo))[..KeyLength];
        log.Trace($"K_seed: {Hex.ToHex(seed)}");

        var kEnc = DeriveKey(seed, CounterEncryption);
        var kMac = DeriveKey(seed, CounterMac);

        log.Debug($"BAC anahtarları türetildi — K_enc: {Hex.Preview(kEnc, 4)}, K_mac: {Hex.Preview(kMac, 4)}");
        return new KeyPair(kEnc, kMac);
    }

    /// <summary>
    /// MRZ_information = belgeNo + check + doğum + check + sonKullanma + check.
    ///
    /// Check digit'ler burada <b>yeniden hesaplanır</b>, MRZ'den okunan değer
    /// kullanılmaz: OCR check digit'i yanlış okuduysa MRZ ayrıştırıcı zaten
    /// alanı düzeltmiş olur, ama anahtar düzeltilmiş alandan türetilmelidir.
    /// </summary>
    internal static string BuildMrzInformation(string documentNumber, string dateOfBirth, string dateOfExpiry)
    {
        var doc = PadDocumentNumber(documentNumber);
        return doc + CheckDigit(doc)
             + dateOfBirth + CheckDigit(dateOfBirth)
             + dateOfExpiry + CheckDigit(dateOfExpiry);
    }

    /// <summary>
    /// Belge numarasını 9 karaktere tamamla.
    ///
    /// MRZ'de belge no alanı sabit 9 karakterdir ve kısa numaralar '&lt;' ile
    /// doldurulur. Anahtar türetmede bu dolgu <b>dahil edilir</b> — kırpılırsa
    /// anahtar yanlış çıkar ve çip erişimi reddeder.
    /// </summary>
    private static string PadDocumentNumber(string documentNumber)
    {
        var trimmed = documentNumber.Trim().ToUpperInvariant();
        return trimmed.Length >= 9 ? trimmed[..9] : trimmed.PadRight(9, '<');
    }

    /// <summary>
    /// ICAO 9303 check digit — ağırlıklar 7-3-1, mod 10.
    ///
    /// Core'daki MrzParser'da da var; burada tekrarlanmasının sebebi Chip
    /// katmanının MRZ ayrıştırıcısına bağımlı olmaması. Aynı ICAO tablosu.
    /// </summary>
    internal static int CheckDigit(string value)
    {
        int[] weights = [7, 3, 1];
        var sum = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            var v = c switch
            {
                >= '0' and <= '9' => c - '0',
                >= 'A' and <= 'Z' => c - 'A' + 10,
                _ => 0, // '<' veya bilinmeyen
            };
            sum += v * weights[i % weights.Length];
        }
        return sum % 10;
    }

    /// <summary>
    /// ICAO 9303 anahtar türetme fonksiyonu.
    ///
    /// D = K_seed || sayaç(4 bayt, big-endian); H = SHA-1(D);
    /// K_a = H[0..8], K_b = H[8..16]; her ikisinin eşlik bitleri düzeltilir.
    /// </summary>
    internal static byte[] DeriveKey(byte[] seed, int counter)
    {
        var input = new byte[seed.Length + 4];
        seed.CopyTo(input, 0);
        input[^4] = (byte)(counter >> 24);
        input[^3] = (byte)(counter >> 16);
        input[^2] = (byte)(counter >> 8);
        input[^1] = (byte)counter;

        var hash = Sha1(input);
        var key = hash[..KeyLength];
        AdjustParity(key);
        return key;
    }

    /// <summary>
    /// DES eşlik (parity) bitlerini düzelt — her baytın en düşük biti, üst
    /// 7 bitteki 1'lerin sayısını tek yapacak şekilde ayarlanır.
    ///
    /// DES anahtarlarının klasik gereği. Çoğu uygulama yanlış eşliği tolere
    /// eder ama bazı kartlar reddeder; ICAO açıkça şart koşuyor.
    /// </summary>
    internal static void AdjustParity(byte[] key)
    {
        for (var i = 0; i < key.Length; i++)
        {
            var b = key[i];
            var onesInTop7 = System.Numerics.BitOperations.PopCount((uint)(b & 0xFE));
            key[i] = (byte)(onesInTop7 % 2 == 0 ? b | 0x01 : b & 0xFE);
        }
    }

    private static byte[] Sha1(byte[] data)
    {
        var digest = new Sha1Digest();
        var result = new byte[digest.GetDigestSize()];
        digest.BlockUpdate(data, 0, data.Length);
        digest.DoFinal(result, 0);
        return result;
    }
}
