using IdScanner.Chip.Bac;
using IdScanner.Chip.Pcsc;
using IdScanner.Core.Diagnostics;

namespace IdScanner.Chip;

/// <summary>
/// eMRTD uygulamasındaki dosya tanımlayıcıları (ICAO 9303 Part 10).
/// </summary>
internal static class ElementaryFile
{
    /// <summary>eMRTD uygulamasının AID'si — SELECT APPLICATION için.</summary>
    internal static readonly byte[] ApplicationId = [0xA0, 0x00, 0x00, 0x02, 0x47, 0x10, 0x01];

    internal const ushort Dg1 = 0x0101;   // MRZ
    internal const ushort Dg2 = 0x0102;   // Yüz görüntüsü
    internal const ushort Dg11 = 0x010B;  // Ek kişisel bilgi (Türkçe isimler)
    internal const ushort Dg12 = 0x010C;  // Ek belge bilgisi
    internal const ushort Dg15 = 0x010F;  // Active Authentication açık anahtarı
    internal const ushort Sod = 0x011D;   // Document Security Object

    /// <summary>Dosya tanımlayıcısından DG numarası — log ve hash eşlemesi için.</summary>
    internal static int ToDataGroupNumber(ushort fileId) => fileId switch
    {
        Dg1 => 1,
        Dg2 => 2,
        Dg11 => 11,
        Dg12 => 12,
        Dg15 => 15,
        _ => 0,
    };
}

/// <summary>
/// Çipteki dosyaları Secure Messaging altında seçer ve okur.
///
/// Java'da JMRTD'nin <c>getInputStream(fid)</c> çağrısıydı — SELECT ve
/// parça parça READ BINARY işlerini o hallediyordu. Burada elle yazıldı.
///
/// <b>Neden parça parça:</b> DG2 (yüz görüntüsü) 15-20 KB olabiliyor ama tek
/// APDU'da en fazla ~250 bayt okunabiliyor. Dosya, uzunluğu TLV başlığından
/// öğrenilip bloklar hâlinde çekiliyor.
/// </summary>
internal sealed class ChipFileReader(PcscConnection connection, SecureMessaging messaging, IAppLogger logger)
{
    private const byte ClaPlain = 0x00;
    private const byte InsSelect = 0xA4;
    private const byte InsReadBinary = 0xB0;

    /// <summary>
    /// Tek okumada istenecek bayt sayısı.
    ///
    /// Secure Messaging sarmalı cevabı büyüttüğü için 0xFF istenirse tampon
    /// taşabiliyor. 0xDF (223) güvenli ve yaygın kullanılan bir değer.
    /// </summary>
    private const byte ReadBlockSize = 0xDF;

    /// <summary>TLV başlığını görmek için yeterli ilk okuma.</summary>
    private const byte HeaderProbeSize = 0x04;

    private readonly IAppLogger _log = logger.ForComponent("File");

    /// <summary>
    /// eMRTD uygulamasını seç — <b>Secure Messaging olmadan, düz komutla</b>.
    ///
    /// <b>Sıra kritik (ICAO 9303 Part 11):</b> Bu çağrı BAC'tan ÖNCE yapılmak
    /// zorunda. Çip, hangi uygulamanın seçili olduğunu bilmeden BAC anahtarlarını
    /// çözemez; uygulama seçilmeden gönderilen EXTERNAL AUTHENTICATE komutunu
    /// reddeder (kartına göre 6A00 / 6982 / 6D00 döner).
    ///
    /// Java'da bu adım JMRTD'nin <c>PassportService.open()</c> çağrısının
    /// içindeydi ve gözden kaçması kolaydı.
    /// </summary>
    internal static void SelectApplication(PcscConnection connection, IAppLogger logger)
    {
        var log = logger.ForComponent("File");
        var apdu = CommandApdu.Send(ClaPlain, InsSelect, 0x04, 0x0C, ElementaryFile.ApplicationId);

        var response = connection.Transmit(apdu.ToBytes());
        var (_, sw) = StatusWord.Split(response);

        if (!StatusWord.IsSuccess(sw))
        {
            throw new ChipProtocolException(
                $"eMRTD uygulaması seçilemedi: {StatusWord.Describe(sw)} — " +
                "kart bir kimlik/pasaport çipi değil olabilir");
        }

        log.Debug("eMRTD uygulaması seçildi (AID A0000002471001)");
    }

    /// <summary>
    /// Bir dosyayı tümüyle oku.
    /// </summary>
    /// <returns>Dosyanın ham baytları; dosya yoksa <c>null</c>.</returns>
    internal byte[]? ReadFile(ushort fileId, string label)
    {
        using var op = _log.BeginOperation($"{label} okuma");
        try
        {
            SelectFile(fileId);

            var totalLength = ProbeLength(fileId);
            if (totalLength <= 0)
            {
                op.Failure("uzunluk belirlenemedi");
                return null;
            }

            var content = ReadAllBlocks(totalLength);
            op.Success($"{content.Length} bayt");
            return content;
        }
        catch (ChipProtocolException e)
        {
            // Dosya yoksa (6A82) bu beklenen bir durum — kartta o DG olmayabilir.
            op.Failure(e.Message);
            return null;
        }
    }

    /// <summary>Dosyayı tanımlayıcısıyla seç.</summary>
    private void SelectFile(ushort fileId)
    {
        var fid = new[] { (byte)(fileId >> 8), (byte)fileId };
        var apdu = CommandApdu.Send(ClaPlain, InsSelect, 0x02, 0x0C, fid);
        Exchange(apdu, $"dosya seçme (FID {fileId:X4})");
    }

    /// <summary>
    /// Dosyanın toplam uzunluğunu TLV başlığından öğren.
    ///
    /// İlk 4 bayt okunur; etiket ve uzunluk alanı çözülüp gövde uzunluğu
    /// eklenerek toplam bulunur.
    /// </summary>
    private int ProbeLength(ushort fileId)
    {
        var header = ReadBlock(0, HeaderProbeSize);
        if (header.Length < 2)
        {
            _log.Warn($"FID {fileId:X4}: başlık okunamadı ({header.Length} bayt)");
            return 0;
        }

        // Etiket 1 veya 2 bayt olabilir
        var tagSize = (header[0] & 0x1F) == 0x1F ? 2 : 1;
        if (header.Length <= tagSize)
        {
            _log.Warn($"FID {fileId:X4}: etiket sonrası uzunluk alanı yok");
            return 0;
        }

        var lengthByte = header[tagSize];
        int bodyLength;
        int lengthSize;

        if (lengthByte < 0x80)
        {
            bodyLength = lengthByte;
            lengthSize = 1;
        }
        else
        {
            var count = lengthByte & 0x7F;
            if (count == 0 || tagSize + count >= header.Length)
            {
                _log.Warn($"FID {fileId:X4}: uzunluk alanı başlık örneğine sığmadı");
                return 0;
            }
            bodyLength = 0;
            for (var i = 1; i <= count; i++) bodyLength = (bodyLength << 8) | header[tagSize + i];
            lengthSize = 1 + count;
        }

        var total = tagSize + lengthSize + bodyLength;
        _log.Trace($"FID {fileId:X4}: etiket {tagSize} bayt, uzunluk {lengthSize} bayt, gövde {bodyLength} → toplam {total}");
        return total;
    }

    /// <summary>Dosyayı bloklar hâlinde sonuna kadar oku.</summary>
    private byte[] ReadAllBlocks(int totalLength)
    {
        var buffer = new byte[totalLength];
        var offset = 0;

        while (offset < totalLength)
        {
            var remaining = totalLength - offset;
            var request = (byte)Math.Min(ReadBlockSize, remaining);

            var block = ReadBlock(offset, request);
            if (block.Length == 0)
            {
                _log.Warn($"Okuma {offset}. baytta durdu — çip boş blok döndürdü");
                break;
            }

            block.CopyTo(buffer, offset);
            offset += block.Length;
        }

        return offset == totalLength ? buffer : buffer[..offset];
    }

    /// <summary>Belirli bir ofsetten blok oku (READ BINARY).</summary>
    private byte[] ReadBlock(int offset, byte length)
    {
        // Ofset P1-P2'ye yerleşir; 32 KB'a kadar dosyalar için yeterli
        var apdu = CommandApdu.Get(
            ClaPlain, InsReadBinary, (byte)(offset >> 8), (byte)offset, length);

        return Exchange(apdu, $"okuma (ofset {offset}, {length} bayt)");
    }

    /// <summary>Komutu Secure Messaging ile sarıp gönder, cevabı çöz.</summary>
    private byte[] Exchange(CommandApdu apdu, string what)
    {
        var protectedApdu = messaging.Protect(apdu);
        var response = connection.Transmit(protectedApdu);
        var (body, sw) = StatusWord.Split(response);

        if (!StatusWord.IsSuccess(sw))
        {
            throw new ChipProtocolException($"{what} başarısız: {StatusWord.Describe(sw)}");
        }

        return messaging.Unprotect(body);
    }
}
