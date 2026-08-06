using IdScanner.Core.Diagnostics;

namespace IdScanner.Chip.Bac;

/// <summary>
/// ISO 7816-4 komut APDU'su.
///
/// Java'da <c>net.sf.scuba.smartcards.CommandAPDU</c> vardı; .NET'te
/// PCSC kütüphanesi daha alt seviye çalıştığı için burada tanımlandı.
/// </summary>
/// <param name="Cla">Sınıf baytı.</param>
/// <param name="Ins">Komut baytı.</param>
/// <param name="P1">Birinci parametre.</param>
/// <param name="P2">İkinci parametre.</param>
/// <param name="Data">Komut verisi; yoksa <c>null</c>.</param>
/// <param name="Le">Beklenen cevap uzunluğu; <see cref="HasLe"/> ile birlikte anlamlı.</param>
/// <param name="HasLe">Le alanı var mı.</param>
internal readonly record struct CommandApdu(
    byte Cla, byte Ins, byte P1, byte P2,
    byte[]? Data = null, byte Le = 0, bool HasLe = false)
{
    /// <summary>Veri taşımayan, cevap bekleyen komut.</summary>
    internal static CommandApdu Get(byte cla, byte ins, byte p1, byte p2, byte le)
        => new(cla, ins, p1, p2, null, le, true);

    /// <summary>Veri gönderen komut.</summary>
    internal static CommandApdu Send(byte cla, byte ins, byte p1, byte p2, byte[] data, byte le = 0, bool hasLe = false)
        => new(cla, ins, p1, p2, data, le, hasLe);

    /// <summary>Düz (korumasız) bayt dizisine çevir.</summary>
    internal byte[] ToBytes()
    {
        var bytes = new List<byte> { Cla, Ins, P1, P2 };

        if (Data is { Length: > 0 })
        {
            bytes.Add((byte)Data.Length);
            bytes.AddRange(Data);
        }

        if (HasLe) bytes.Add(Le);
        return [.. bytes];
    }

    /// <summary>Log için kısa tanım.</summary>
    internal string Describe()
    {
        var name = Ins switch
        {
            0xA4 => "SELECT",
            0xB0 => "READ BINARY",
            0x84 => "GET CHALLENGE",
            0x82 => "EXTERNAL AUTHENTICATE",
            0x88 => "INTERNAL AUTHENTICATE",
            _ => $"INS={Ins:X2}",
        };
        var dataInfo = Data is { Length: > 0 } ? $", {Data.Length} bayt veri" : "";
        return $"{name} (P1={P1:X2} P2={P2:X2}{dataInfo})";
    }
}

/// <summary>
/// ISO 7816-4 durum kelimeleri (SW1-SW2).
///
/// <b>Neden bu kadar ayrıntılı:</b> Çip hatalarında elimizde sadece iki bayt
/// oluyor. "6982" ile "güvenlik durumu sağlanmadı — BAC yapılmamış" arasındaki
/// fark, sahada saatler kazandırıyor.
/// </summary>
internal static class StatusWord
{
    internal const ushort Success = 0x9000;

    /// <summary>Cevap kısaltıldı — kalan bayt sayısı SW2'de.</summary>
    internal const ushort BytesRemainingPrefix = 0x6100;

    /// <summary>Beklenen uzunluk yanlış — doğrusu SW2'de.</summary>
    internal const ushort WrongLengthPrefix = 0x6C00;

    internal static bool IsSuccess(ushort sw) => sw == Success;

    /// <summary>Durum kelimesinin okunabilir açıklaması.</summary>
    internal static string Describe(ushort sw)
    {
        var hex = $"{sw:X4}";

        if ((sw & 0xFF00) == BytesRemainingPrefix)
        {
            return $"{hex} (cevap devam ediyor, {sw & 0xFF} bayt daha var)";
        }
        if ((sw & 0xFF00) == WrongLengthPrefix)
        {
            return $"{hex} (yanlış uzunluk, doğrusu {sw & 0xFF} bayt)";
        }

        return sw switch
        {
            0x9000 => $"{hex} (başarılı)",
            0x6200 => $"{hex} (uyarı: durum değişmedi)",
            0x6281 => $"{hex} (dönen veri bozuk olabilir)",
            0x6282 => $"{hex} (dosya sonuna erken ulaşıldı)",
            0x6300 => $"{hex} (doğrulama başarısız)",
            0x6400 => $"{hex} (çalıştırma hatası, durum değişmedi)",
            0x6581 => $"{hex} (belleğe yazma hatası)",
            0x6700 => $"{hex} (yanlış uzunluk)",
            0x6800 => $"{hex} (CLA fonksiyonu desteklenmiyor)",
            0x6882 => $"{hex} (Secure Messaging desteklenmiyor)",
            0x6900 => $"{hex} (komut izin verilmiyor)",
            0x6982 => $"{hex} (güvenlik durumu sağlanmadı — BAC yapılmamış veya oturum düştü)",
            0x6983 => $"{hex} (doğrulama yöntemi bloke)",
            0x6984 => $"{hex} (referans veri kullanılamaz)",
            0x6985 => $"{hex} (kullanım koşulları sağlanmadı)",
            0x6986 => $"{hex} (komut izinsiz — dosya seçilmemiş olabilir)",
            0x6987 => $"{hex} (beklenen Secure Messaging nesnesi eksik)",
            0x6988 => $"{hex} (Secure Messaging nesnesi hatalı — SSC kaymış olabilir)",
            0x6A80 => $"{hex} (veri alanı parametreleri hatalı)",
            0x6A81 => $"{hex} (fonksiyon desteklenmiyor)",
            0x6A82 => $"{hex} (dosya bulunamadı — bu DG kartta yok)",
            0x6A83 => $"{hex} (kayıt bulunamadı)",
            0x6A84 => $"{hex} (dosyada yer yok)",
            0x6A86 => $"{hex} (P1-P2 parametreleri hatalı)",
            0x6A88 => $"{hex} (referans veri bulunamadı)",
            0x6B00 => $"{hex} (yanlış P1-P2)",
            0x6D00 => $"{hex} (INS desteklenmiyor)",
            0x6E00 => $"{hex} (CLA desteklenmiyor)",
            0x6F00 => $"{hex} (tanısız hata)",
            _ => $"{hex} (bilinmeyen durum kelimesi)",
        };
    }

    /// <summary>Cevabın son iki baytını durum kelimesi olarak ayır.</summary>
    internal static (byte[] Data, ushort Sw) Split(byte[] response)
    {
        if (response.Length < 2)
        {
            throw new ChipProtocolException(
                $"Cevap çok kısa ({response.Length} bayt) — durum kelimesi okunamıyor: {Hex.ToHex(response)}");
        }

        var sw = (ushort)((response[^2] << 8) | response[^1]);
        return (response[..^2], sw);
    }
}
