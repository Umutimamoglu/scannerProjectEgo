using System.Text;

namespace IdScanner.Core.Diagnostics;

/// <summary>
/// Ham baytları log'da okunur hâle getirir.
///
/// Çip iletişiminde hata ayıklamanın tek yolu gönderilen/alınan baytları
/// görmek. Java tarafında bu yoktu; "DG2 okunamadı" yazıp geçiyordu ve
/// nedenini anlamak imkânsız oluyordu.
/// </summary>
public static class Hex
{
    /// <summary>Baytları bitişik onaltılık metne çevir: <c>a1b2c3</c>.</summary>
    public static string ToHex(ReadOnlySpan<byte> bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    /// <summary>
    /// İlk <paramref name="max"/> baytı onaltılık ver, gerisini "…" ile kısalt.
    /// Log satırını şişirmeden içeriğe bakabilmek için.
    /// </summary>
    public static string Preview(ReadOnlySpan<byte> bytes, int max = 16)
    {
        var n = Math.Min(max, bytes.Length);
        var head = ToHex(bytes[..n]);
        return bytes.Length > n
            ? $"{head}… ({bytes.Length} bayt)"
            : $"{head} ({bytes.Length} bayt)";
    }

    /// <summary>
    /// Klasik 16 sütunlu döküm — ofset, onaltılık, yazdırılabilir karakterler.
    /// APDU ve DG içeriğini gözle incelemek için.
    /// </summary>
    public static string Dump(ReadOnlySpan<byte> bytes, int maxBytes = 512)
    {
        const int PerLine = 16;
        var limit = Math.Min(maxBytes, bytes.Length);
        var sb = new StringBuilder();

        for (var offset = 0; offset < limit; offset += PerLine)
        {
            var count = Math.Min(PerLine, limit - offset);
            var chunk = bytes.Slice(offset, count);

            sb.Append(offset.ToString("x4")).Append("  ");

            for (var i = 0; i < PerLine; i++)
            {
                sb.Append(i < count ? chunk[i].ToString("x2") : "  ").Append(' ');
                if (i == 7) sb.Append(' ');
            }

            sb.Append(' ');
            foreach (var b in chunk)
            {
                sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
            }

            sb.AppendLine();
        }

        if (bytes.Length > limit)
        {
            sb.Append("... (toplam ").Append(bytes.Length).AppendLine(" bayt)");
        }

        return sb.ToString().TrimEnd();
    }
}
