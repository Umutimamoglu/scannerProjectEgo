using IdScanner.Core.Diagnostics;

namespace IdScanner.Chip.Bac;

/// <summary>
/// ICAO 9303 Part 11 Secure Messaging — BAC oturumu açıldıktan sonra
/// gönderilen her komutu şifreler ve imzalar, gelen her cevabı doğrular.
///
/// Java'da bu iş JMRTD'nin <c>SecureMessagingWrapper</c> sınıfındaydı.
///
/// <b>En kritik ayrıntı — SSC (Send Sequence Counter):</b> Her komutta ve her
/// cevapta birer artar. Bir kez kayarsa çip sonraki tüm komutları reddeder ve
/// hata mesajı bunu söylemez ("6988" gibi bir kod döner). Bu yüzden sayaç
/// hareketleri <see cref="LogLevel.Trace"/> seviyesinde kaydediliyor.
/// </summary>
internal sealed class SecureMessaging(byte[] sessionEnc, byte[] sessionMac, ulong initialSsc, IAppLogger log)
{
    // Secure Messaging veri nesnesi etiketleri (ISO 7816-4)
    private const byte TagEncryptedData = 0x87;   // Şifreli veri (dolgu göstergeli)
    private const byte TagLe = 0x97;              // Beklenen cevap uzunluğu
    private const byte TagStatus = 0x99;          // Cevap durum kelimesi
    private const byte TagMac = 0x8E;             // Bütünlük kontrolü

    /// <summary>Secure Messaging'i işaretleyen CLA biti.</summary>
    private const byte ClaSecureMessaging = 0x0C;

    private readonly IAppLogger _log = log.ForComponent("SM");
    private ulong _ssc = initialSsc;

    /// <summary>Şu anki sayaç değeri — tanılama için.</summary>
    internal ulong SendSequenceCounter => _ssc;

    /// <summary>
    /// Düz komutu Secure Messaging ile sar.
    /// </summary>
    /// <param name="apdu">CLA INS P1 P2 [Lc data] [Le] biçiminde düz komut.</param>
    internal byte[] Protect(CommandApdu apdu)
    {
        _ssc++;
        _log.Trace($"SSC → {_ssc} (komut {apdu.Describe()})");

        // Başlık: CLA'ya Secure Messaging biti eklenir, sonra bloğa doldurulur
        var header = new byte[] { (byte)(apdu.Cla | ClaSecureMessaging), apdu.Ins, apdu.P1, apdu.P2 };
        var paddedHeader = ChipCrypto.Pad(header);

        var body = new List<byte>();

        // DO'87' — veri varsa şifrele
        if (apdu.Data is { Length: > 0 })
        {
            var cryptogram = ChipCrypto.TripleDesEncrypt(sessionEnc, ChipCrypto.Pad(apdu.Data));

            // İçerik: 0x01 (dolgu göstergesi) + şifreli veri
            var content = new byte[cryptogram.Length + 1];
            content[0] = 0x01;
            cryptogram.CopyTo(content, 1);

            body.Add(TagEncryptedData);
            body.AddRange(EncodeLength(content.Length));
            body.AddRange(content);
        }

        // DO'97' — beklenen cevap uzunluğu
        if (apdu.HasLe)
        {
            body.Add(TagLe);
            body.Add(0x01);
            body.Add(apdu.Le);
        }

        // MAC, doldurulmuş başlık + gövde üzerinden hesaplanır
        var macInput = new List<byte>();
        macInput.AddRange(SscBytes());
        macInput.AddRange(paddedHeader);
        macInput.AddRange(body);

        var mac = ChipCrypto.RetailMac(sessionMac, [.. macInput]);

        body.Add(TagMac);
        body.Add(ChipCrypto.MacLength);
        body.AddRange(mac);

        // Korumalı APDU: başlık + Lc + gövde + Le(0x00)
        var result = new List<byte>(header) { (byte)body.Count };
        result[0] = (byte)(apdu.Cla | ClaSecureMessaging);
        result.AddRange(body);
        result.Add(0x00);

        return [.. result];
    }

    /// <summary>
    /// Korumalı cevabı çöz: MAC'i doğrula, veriyi çöz, durum kelimesini ayır.
    /// </summary>
    /// <param name="response">Çipten gelen ham cevap (durum kelimesi hariç).</param>
    /// <returns>Çözülmüş veri; cevapta veri yoksa boş dizi.</returns>
    internal byte[] Unprotect(byte[] response)
    {
        _ssc++;
        _log.Trace($"SSC → {_ssc} (cevap {response.Length} bayt)");

        byte[]? encryptedData = null;
        byte[]? statusBytes = null;
        byte[]? receivedMac = null;

        // MAC, kendisinden ÖNCEKİ tüm veri nesneleri üzerinden hesaplanır
        var macInput = new List<byte>(SscBytes());

        var offset = 0;
        while (offset < response.Length)
        {
            var tag = response[offset];
            var (length, lengthSize) = DecodeLength(response, offset + 1);
            var valueStart = offset + 1 + lengthSize;

            if (valueStart + length > response.Length)
            {
                throw new ChipProtocolException(
                    $"Cevap bozuk: 0x{tag:X2} etiketinin uzunluğu ({length}) cevaba sığmıyor");
            }

            var value = response[valueStart..(valueStart + length)];

            switch (tag)
            {
                case TagEncryptedData:
                    // İlk bayt dolgu göstergesi (0x01), gerisi şifreli veri
                    encryptedData = value.Length > 1 ? value[1..] : [];
                    macInput.AddRange(response[offset..(valueStart + length)]);
                    break;

                case TagStatus:
                    statusBytes = value;
                    macInput.AddRange(response[offset..(valueStart + length)]);
                    break;

                case TagMac:
                    receivedMac = value;
                    break;

                default:
                    _log.Trace($"Bilinmeyen cevap etiketi 0x{tag:X2} ({length} bayt) — MAC'e dahil ediliyor");
                    macInput.AddRange(response[offset..(valueStart + length)]);
                    break;
            }

            offset = valueStart + length;
        }

        VerifyMac(macInput, receivedMac);
        CheckStatus(statusBytes);

        if (encryptedData is null or { Length: 0 }) return [];

        var plain = ChipCrypto.TripleDesDecrypt(sessionEnc, encryptedData);
        return ChipCrypto.Unpad(plain);
    }

    private void VerifyMac(List<byte> macInput, byte[]? receivedMac)
    {
        if (receivedMac is null)
        {
            throw new ChipProtocolException("Cevapta MAC (DO'8E') yok — Secure Messaging bozulmuş");
        }

        var expected = ChipCrypto.RetailMac(sessionMac, [.. macInput]);
        if (!ChipCrypto.ConstantTimeEquals(expected, receivedMac))
        {
            // Bu neredeyse her zaman SSC kayması demektir.
            _log.Error($"MAC uyuşmadı. Beklenen {Hex.ToHex(expected)}, gelen {Hex.ToHex(receivedMac)}, " +
                       $"SSC={_ssc}. En olası sebep: bir komut/cevap çiftinde sayaç kaymış.");
            throw new ChipProtocolException("Cevap MAC'i doğrulanamadı — oturum bozuldu");
        }
    }

    private void CheckStatus(byte[]? statusBytes)
    {
        if (statusBytes is not { Length: 2 }) return;

        var sw = (ushort)((statusBytes[0] << 8) | statusBytes[1]);
        if (sw != StatusWord.Success)
        {
            throw new ChipProtocolException($"Çip hata döndürdü: {StatusWord.Describe(sw)}");
        }
    }

    /// <summary>Sayaç, MAC girdisinde 8 baytlık big-endian olarak yer alır.</summary>
    private byte[] SscBytes()
    {
        var bytes = BitConverter.GetBytes(_ssc);
        if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
        return bytes;
    }

    /// <summary>BER uzunluk kodlaması (kısa biçim veya 0x81/0x82).</summary>
    private static byte[] EncodeLength(int length) => length switch
    {
        < 0x80 => [(byte)length],
        <= 0xFF => [0x81, (byte)length],
        _ => [0x82, (byte)(length >> 8), (byte)length],
    };

    /// <summary>BER uzunluk çözme; (uzunluk, uzunluk alanının bayt sayısı) döner.</summary>
    private static (int Length, int Size) DecodeLength(byte[] buffer, int offset)
    {
        var first = buffer[offset];
        if (first < 0x80) return (first, 1);

        var byteCount = first & 0x7F;
        var length = 0;
        for (var i = 0; i < byteCount; i++) length = (length << 8) | buffer[offset + 1 + i];
        return (length, 1 + byteCount);
    }
}
