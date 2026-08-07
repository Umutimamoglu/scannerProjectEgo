using IdScanner.Chip.Pcsc;
using IdScanner.Core.Diagnostics;

namespace IdScanner.Chip.Bac;

/// <summary>
/// BAC karşılıklı kimlik doğrulaması — ICAO 9303 Part 11, §4.3.
///
/// Java'da JMRTD'nin <c>doBAC</c> çağrısıydı; burada elle yazıldı.
///
/// Akış:
///   1. Çipten 8 baytlık rastgele sayı iste (GET CHALLENGE)
///   2. Kendi rastgele sayımızı ve anahtar tohumumuzu üret
///   3. Üçünü birleştirip K_enc ile şifrele, K_mac ile imzala
///   4. EXTERNAL AUTHENTICATE ile gönder
///   5. Çipin cevabını doğrula, kendi tohumunu çıkar
///   6. İki tohumu XOR'la → oturum anahtarları
///
/// Bu, iki tarafın da MRZ'yi bildiğini karşılıklı kanıtlamasıdır. Sonrasındaki
/// tüm iletişim <see cref="SecureMessaging"/> ile korunur.
/// </summary>
internal static class BacHandshake
{
    private const byte ClaPlain = 0x00;
    private const byte InsGetChallenge = 0x84;
    private const byte InsExternalAuthenticate = 0x82;

    private const int ChallengeLength = 8;
    private const int KeySeedLength = 16;

    /// <summary>Doğrulama verisi: RND.IFD(8) + RND.ICC(8) + K.IFD(16).</summary>
    private const int AuthDataLength = 32;

    /// <summary>Kurulan oturum.</summary>
    /// <param name="Messaging">Bundan sonraki komutları saracak katman.</param>
    internal readonly record struct Session(SecureMessaging Messaging);

    /// <summary>
    /// BAC el sıkışmasını yürüt ve Secure Messaging oturumu döndür.
    /// </summary>
    /// <exception cref="ChipProtocolException">Çip doğrulamayı reddederse.</exception>
    internal static Session Perform(
        PcscConnection connection, BacKeyDerivation.KeyPair keys, IAppLogger logger)
    {
        var log = logger.ForComponent("Bac");
        using var op = log.BeginOperation("BAC karşılıklı doğrulama");

        try
        {
            return Run(connection, keys, log, op);
        }
        catch (Exception e)
        {
            // İstisna ile çıkarken sonucu bildir; yoksa log'da sebep kaybolur.
            op.Failed(e);
            throw;
        }
    }

    private static Session Run(
        PcscConnection connection, BacKeyDerivation.KeyPair keys, IAppLogger log, OperationScope op)
    {
        var rndIcc = GetChallenge(connection, log);
        var rndIfd = ChipCrypto.RandomBytes(ChallengeLength);
        var kIfd = ChipCrypto.RandomBytes(KeySeedLength);

        log.Trace($"RND.ICC={Hex.ToHex(rndIcc)}, RND.IFD={Hex.ToHex(rndIfd)}");

        // S = RND.IFD || RND.ICC || K.IFD
        var s = new byte[AuthDataLength];
        rndIfd.CopyTo(s, 0);
        rndIcc.CopyTo(s, ChallengeLength);
        kIfd.CopyTo(s, ChallengeLength * 2);

        var eIfd = ChipCrypto.TripleDesEncrypt(keys.Encryption, s);
        var mIfd = ChipCrypto.RetailMac(keys.Mac, eIfd);

        var authData = new byte[eIfd.Length + mIfd.Length];
        eIfd.CopyTo(authData, 0);
        mIfd.CopyTo(authData, eIfd.Length);

        var responseData = ExternalAuthenticate(connection, authData, log);

        // Cevap: E.ICC(32) || M.ICC(8)
        if (responseData.Length < AuthDataLength + ChipCrypto.MacLength)
        {
            op.Failure("cevap kısa");
            throw new ChipProtocolException(
                $"BAC cevabı beklenenden kısa: {responseData.Length} bayt " +
                $"(beklenen {AuthDataLength + ChipCrypto.MacLength})");
        }

        var eIcc = responseData[..AuthDataLength];
        var mIcc = responseData[AuthDataLength..(AuthDataLength + ChipCrypto.MacLength)];

        var expectedMac = ChipCrypto.RetailMac(keys.Mac, eIcc);
        if (!ChipCrypto.ConstantTimeEquals(expectedMac, mIcc))
        {
            op.Failure("çipin MAC'i tutmadı");
            throw new ChipProtocolException(
                "Çipin cevabındaki MAC doğrulanamadı — anahtarlar uyuşmuyor olabilir");
        }

        var decrypted = ChipCrypto.TripleDesDecrypt(keys.Encryption, eIcc);

        // R = RND.ICC || RND.IFD || K.ICC — kendi rastgele sayımız geri gelmeli
        var echoedRndIfd = decrypted[ChallengeLength..(ChallengeLength * 2)];
        if (!ChipCrypto.ConstantTimeEquals(echoedRndIfd, rndIfd))
        {
            op.Failure("RND.IFD geri gelmedi");
            throw new ChipProtocolException(
                "Çip bizim rastgele sayımızı doğru döndürmedi — yeniden oynatma (replay) belirtisi");
        }

        var kIcc = decrypted[(ChallengeLength * 2)..(ChallengeLength * 2 + KeySeedLength)];

        // Oturum tohumu = K.IFD XOR K.ICC — iki taraf da katkı verir
        var sessionSeed = new byte[KeySeedLength];
        for (var i = 0; i < KeySeedLength; i++) sessionSeed[i] = (byte)(kIfd[i] ^ kIcc[i]);

        var sessionEnc = BacKeyDerivation.DeriveKey(sessionSeed, 1);
        var sessionMac = BacKeyDerivation.DeriveKey(sessionSeed, 2);

        // SSC = RND.ICC'nin son 4 baytı || RND.IFD'nin son 4 baytı
        var ssc = BuildInitialSsc(rndIcc, rndIfd);
        log.Debug($"Oturum kuruldu, başlangıç SSC = {ssc}");

        op.Success();
        return new Session(new SecureMessaging(sessionEnc, sessionMac, ssc, log));
    }

    /// <summary>Çipten 8 baytlık rastgele sayı iste.</summary>
    private static byte[] GetChallenge(PcscConnection connection, IAppLogger log)
    {
        var apdu = CommandApdu.Get(ClaPlain, InsGetChallenge, 0x00, 0x00, ChallengeLength);
        var response = connection.Transmit(apdu.ToBytes());
        var (data, sw) = StatusWord.Split(response);

        if (!StatusWord.IsSuccess(sw))
        {
            throw new ChipProtocolException($"GET CHALLENGE reddedildi: {StatusWord.Describe(sw)}");
        }
        if (data.Length != ChallengeLength)
        {
            throw new ChipProtocolException(
                $"GET CHALLENGE {data.Length} bayt döndürdü, {ChallengeLength} bekleniyordu");
        }

        log.Trace("GET CHALLENGE tamam");
        return data;
    }

    /// <summary>Doğrulama verisini gönder ve çipin cevabını al.</summary>
    private static byte[] ExternalAuthenticate(PcscConnection connection, byte[] authData, IAppLogger log)
    {
        var apdu = CommandApdu.Send(
            ClaPlain, InsExternalAuthenticate, 0x00, 0x00, authData,
            le: 0x28, hasLe: true);

        var response = connection.Transmit(apdu.ToBytes());
        var (data, sw) = StatusWord.Split(response);

        if (!StatusWord.IsSuccess(sw))
        {
            // En sık sebep: MRZ'den türetilen anahtar yanlış. Yani ya OCR
            // yanlış okudu ya da belge no dolgusu hatalı.
            log.Error($"EXTERNAL AUTHENTICATE reddedildi: {StatusWord.Describe(sw)}. " +
                      "En olası sebep: BAC anahtarları yanlış — MRZ'deki belge no / " +
                      "doğum tarihi / son kullanma tarihi hatalı okunmuş olabilir.");
            throw new ChipProtocolException($"BAC reddedildi: {StatusWord.Describe(sw)}");
        }

        return data;
    }

    /// <summary>SSC = RND.ICC son 4 baytı || RND.IFD son 4 baytı (big-endian 64 bit).</summary>
    private static ulong BuildInitialSsc(byte[] rndIcc, byte[] rndIfd)
    {
        ulong ssc = 0;
        for (var i = 4; i < 8; i++) ssc = (ssc << 8) | rndIcc[i];
        for (var i = 4; i < 8; i++) ssc = (ssc << 8) | rndIfd[i];
        return ssc;
    }
}
