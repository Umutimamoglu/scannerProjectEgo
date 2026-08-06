using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Engines;
using Org.BouncyCastle.Crypto.Macs;
using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Paddings;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace IdScanner.Chip.Bac;

/// <summary>
/// BAC ve Secure Messaging'in ihtiyaç duyduğu kripto ilkelleri.
///
/// Java'da bunlar JMRTD ve JCE sağlayıcısı tarafından sağlanıyordu
/// ("DESede/CBC/NoPadding", "ISO9797Alg3Mac"). Burada BouncyCastle .NET
/// üzerinden elle kuruldu.
///
/// <b>Neden ayrı bir sınıf:</b> Bu dört işlem BAC el sıkışmasında ve her
/// APDU'da tekrar tekrar kullanılıyor. Tek yerde toplanmazsa mod/dolgu
/// ayarları kopyalanır ve bir yerde kaçan ayar sessizce yanlış şifreleme
/// üretir — çip de sebebini söylemeden reddeder.
/// </summary>
internal static class ChipCrypto
{
    /// <summary>DES blok boyutu.</summary>
    internal const int BlockSize = 8;

    /// <summary>MAC uzunluğu — Retail MAC 8 bayt üretir.</summary>
    internal const int MacLength = 8;

    /// <summary>
    /// 3DES-CBC şifreleme, dolgusuz, sıfır IV.
    ///
    /// Girdi blok boyutunun katı olmalı — dolgu çağıranın işi
    /// (<see cref="Pad"/>), çünkü Secure Messaging dolguyu MAC'ten önce
    /// uygulamak zorunda.
    /// </summary>
    internal static byte[] TripleDesEncrypt(byte[] key, byte[] data) => TripleDes(key, data, forEncryption: true);

    /// <summary>3DES-CBC çözme, dolgusuz, sıfır IV.</summary>
    internal static byte[] TripleDesDecrypt(byte[] key, byte[] data) => TripleDes(key, data, forEncryption: false);

    private static byte[] TripleDes(byte[] key, byte[] data, bool forEncryption)
    {
        if (data.Length % BlockSize != 0)
        {
            throw new ArgumentException(
                $"3DES girdisi {BlockSize} baytın katı olmalı, {data.Length} bayt verildi", nameof(data));
        }

        var cipher = new BufferedBlockCipher(new CbcBlockCipher(new DesEdeEngine()));
        cipher.Init(forEncryption,
            new ParametersWithIV(new KeyParameter(ExpandTo24(key)), new byte[BlockSize]));

        var output = new byte[cipher.GetOutputSize(data.Length)];
        var written = cipher.ProcessBytes(data, 0, data.Length, output, 0);
        written += cipher.DoFinal(output, written);
        return output[..written];
    }

    /// <summary>
    /// ISO/IEC 9797-1 Algoritma 3 (Retail MAC), dolgu yöntemi 2.
    ///
    /// Kısaca: tüm bloklar K_a ile DES-CBC-MAC'lenir, son blok K_b ile
    /// çözülüp K_a ile yeniden şifrelenir. Secure Messaging'in bütünlük
    /// kontrolü bunun üzerine kurulu.
    /// </summary>
    internal static byte[] RetailMac(byte[] key, byte[] data)
    {
        // ISO9797Alg3Mac, 16 baytlık anahtarı Ka|Kb olarak kendisi ayırır.
        // Dolgu MAC'in içinde uygulanır — çağıran ayrıca Pad() çağırmamalı.
        var mac = new ISO9797Alg3Mac(new DesEngine(), MacLength * 8, new ISO7816d4Padding());
        mac.Init(new KeyParameter(key));
        mac.BlockUpdate(data, 0, data.Length);

        var result = new byte[mac.GetMacSize()];
        mac.DoFinal(result, 0);
        return result;
    }

    /// <summary>
    /// ISO/IEC 9797-1 dolgu yöntemi 2: 0x80, ardından blok dolana kadar 0x00.
    ///
    /// Girdi zaten tam blok olsa bile <b>her zaman</b> eklenir — aksi halde
    /// alıcı dolgunun nerede bittiğini bilemez.
    /// </summary>
    internal static byte[] Pad(byte[] data)
    {
        var paddedLength = ((data.Length / BlockSize) + 1) * BlockSize;
        var result = new byte[paddedLength];
        data.CopyTo(result, 0);
        result[data.Length] = 0x80;
        return result;
    }

    /// <summary>Dolguyu kaldır — sondaki 0x00'ları atlayıp 0x80'i düşür.</summary>
    internal static byte[] Unpad(byte[] data)
    {
        var i = data.Length - 1;
        while (i >= 0 && data[i] == 0x00) i--;

        if (i < 0 || data[i] != 0x80)
        {
            throw new ChipProtocolException(
                "Dolgu bozuk: 0x80 işaretçisi bulunamadı — Secure Messaging anahtarları uyuşmuyor olabilir");
        }

        return data[..i];
    }

    /// <summary>
    /// Çift uzunluktaki (16 bayt) anahtarı 3DES'in beklediği 24 bayta genişlet:
    /// K1 || K2 || K1. ICAO 9303 çift uzunluk kullanır.
    /// </summary>
    private static byte[] ExpandTo24(byte[] key)
    {
        if (key.Length == 24) return key;
        if (key.Length != 16)
        {
            throw new ArgumentException($"Anahtar 16 veya 24 bayt olmalı, {key.Length} verildi", nameof(key));
        }

        var expanded = new byte[24];
        key.CopyTo(expanded, 0);
        Array.Copy(key, 0, expanded, 16, 8);
        return expanded;
    }

    /// <summary>Kriptografik kalitede rastgele bayt üret.</summary>
    internal static byte[] RandomBytes(int count)
    {
        var buffer = new byte[count];
        new SecureRandom().NextBytes(buffer);
        return buffer;
    }

    /// <summary>İki diziyi zaman-sabit karşılaştır (MAC doğrulaması için).</summary>
    internal static bool ConstantTimeEquals(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
        => System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(a, b);
}

/// <summary>Çip protokolü beklenen şekilde ilerlemediğinde fırlatılır.</summary>
public sealed class ChipProtocolException(string message, Exception? inner = null)
    : Exception(message, inner);
