using IdScanner.Core.Diagnostics;

namespace IdScanner.Chip.Tlv;

/// <summary>Tek bir BER-TLV öğesi.</summary>
/// <param name="Tag">Etiket (çok baytlı etiketler tek sayıda birleştirilir).</param>
/// <param name="Value">Değer baytları.</param>
public readonly record struct TlvItem(int Tag, byte[] Value)
{
    /// <summary>Etiketin okunabilir gösterimi.</summary>
    public string TagHex => Tag <= 0xFF ? $"{Tag:X2}" : $"{Tag:X4}";
}

/// <summary>TLV yapısı beklenen biçimde değilse fırlatılır.</summary>
public sealed class TlvParseException(string message) : Exception(message);

/// <summary>
/// BER-TLV çözümleyici — veri gruplarının iç yapısını okumak için.
///
/// Java'da bu iş JMRTD'nin DG sınıflarının içindeydi (<c>DG11File</c> vb.);
/// .NET karşılığı olmadığı için elle yazıldı.
///
/// Sadece ihtiyaç duyulan kadarı uygulandı: etiket, uzunluk, değer okuma ve
/// iç içe yapılarda etiket arama. Tam bir ASN.1 kütüphanesi değil.
/// </summary>
public static class BerTlv
{
    /// <summary>Çok baytlı etiket göstergesi — ilk baytın alt 5 biti hep 1 ise.</summary>
    private const byte MultiByteTagMask = 0x1F;

    /// <summary>Sonraki baytın da etikete ait olduğunu gösteren bit.</summary>
    private const byte TagContinuesMask = 0x80;

    /// <summary>Öğenin yapısal (iç içe TLV taşıyan) olduğunu gösteren bit.</summary>
    private const byte ConstructedMask = 0x20;

    /// <summary>
    /// Bir baytlık dizideki üst seviye TLV öğelerini sırayla çöz.
    /// </summary>
    public static IEnumerable<TlvItem> Parse(byte[] data)
    {
        var offset = 0;
        while (offset < data.Length)
        {
            // Dolgu baytlarını atla — bazı kartlar öğeler arasına 0x00 koyuyor
            if (data[offset] == 0x00 || data[offset] == 0xFF)
            {
                offset++;
                continue;
            }

            var (tag, tagSize) = ReadTag(data, offset);
            var (length, lengthSize) = ReadLength(data, offset + tagSize);
            var valueStart = offset + tagSize + lengthSize;

            if (valueStart + length > data.Length)
            {
                throw new TlvParseException(
                    $"TLV bozuk: 0x{tag:X} etiketi {length} bayt istiyor ama " +
                    $"{data.Length - valueStart} bayt kaldı");
            }

            yield return new TlvItem(tag, data[valueStart..(valueStart + length)]);
            offset = valueStart + length;
        }
    }

    /// <summary>
    /// Belirtilen etiketi, iç içe yapıların içinde de arayarak bul.
    /// </summary>
    /// <returns>Bulunan değer; yoksa <c>null</c>.</returns>
    public static byte[]? FindValue(byte[] data, int tag, int maxDepth = 6)
    {
        if (maxDepth <= 0) return null;

        foreach (var item in ParseSafely(data))
        {
            if (item.Tag == tag) return item.Value;

            // Yapısal öğelerin içine in
            if (IsConstructed(item.Tag))
            {
                var nested = FindValue(item.Value, tag, maxDepth - 1);
                if (nested is not null) return nested;
            }
        }

        return null;
    }

    /// <summary>
    /// Bozuk veriyi istisna yerine kısmi sonuçla geçiştiren çözümleme.
    ///
    /// Arama sırasında kullanılır: kartın bir yerinde beklenmedik bayt varsa
    /// bu, aradığımız alanın bulunamaması demek olabilir ama tüm okumayı
    /// düşürmemeli.
    /// </summary>
    private static List<TlvItem> ParseSafely(byte[] data)
    {
        var items = new List<TlvItem>();
        try
        {
            items.AddRange(Parse(data));
        }
        catch (TlvParseException)
        {
            // Kısmi sonuç yeterli — çağıran zaten null kontrolü yapıyor
        }
        return items;
    }

    /// <summary>Öğe iç içe TLV taşıyor mu.</summary>
    public static bool IsConstructed(int tag)
    {
        // Çok baytlı etiketlerde bayrak ilk bayttadır
        var firstByte = tag > 0xFF ? (byte)(tag >> 8) : (byte)tag;
        return (firstByte & ConstructedMask) != 0;
    }

    /// <summary>Etiketi oku; (etiket, kapladığı bayt) döner.</summary>
    private static (int Tag, int Size) ReadTag(byte[] data, int offset)
    {
        var first = data[offset];
        if ((first & MultiByteTagMask) != MultiByteTagMask) return (first, 1);

        // Çok baytlı etiket — devam biti temizlenene kadar oku
        var tag = (int)first;
        var size = 1;
        while (offset + size < data.Length)
        {
            var next = data[offset + size];
            tag = (tag << 8) | next;
            size++;
            if ((next & TagContinuesMask) == 0) break;
        }
        return (tag, size);
    }

    /// <summary>Uzunluğu oku; (uzunluk, kapladığı bayt) döner.</summary>
    private static (int Length, int Size) ReadLength(byte[] data, int offset)
    {
        if (offset >= data.Length) throw new TlvParseException("TLV uzunluk alanı eksik");

        var first = data[offset];
        if (first < 0x80) return (first, 1);

        var byteCount = first & 0x7F;
        if (byteCount == 0) throw new TlvParseException("Belirsiz uzunluk (0x80) desteklenmiyor");
        if (offset + byteCount >= data.Length) throw new TlvParseException("TLV uzunluk alanı taşıyor");

        var length = 0;
        for (var i = 1; i <= byteCount; i++) length = (length << 8) | data[offset + i];
        return (length, 1 + byteCount);
    }

    /// <summary>Bir TLV yapısının içeriğini log için özetle.</summary>
    public static string Describe(byte[] data, int maxItems = 12)
    {
        var lines = new List<string>();
        foreach (var item in ParseSafely(data).Take(maxItems))
        {
            lines.Add($"    {item.TagHex}: {Hex.Preview(item.Value, 12)}");
        }
        return lines.Count > 0 ? string.Join(Environment.NewLine, lines) : "    (öğe bulunamadı)";
    }
}
