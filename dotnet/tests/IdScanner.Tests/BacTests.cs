using IdScanner.Chip.Bac;
using IdScanner.Core.Diagnostics;

namespace IdScanner.Tests;

/// <summary>
/// BAC ve Secure Messaging'i ICAO 9303 Part 11, Ek D'deki <b>yayımlanmış
/// çalışılmış örnek</b> ile doğrular.
///
/// <b>Bu dosya neden önemli:</b> Çip kodunun geri kalanı fiziksel kart
/// olmadan sınanamıyor. Ama ICAO, tüm ara değerleriyle birlikte referans bir
/// oturum yayımlamış: MRZ'den anahtar türetme, şifreleme, MAC, korumalı APDU
/// ve korumalı cevap. Buradaki testler geçiyorsa BAC uygulaması doğrudur —
/// kart takmadan.
/// </summary>
public class BacTests
{
    // === ICAO 9303 Part 11, Ek D.2 — anahtar türetme ===

    private const string DocumentNumber = "L898902C<";
    private const string DateOfBirth = "690806";
    private const string DateOfExpiry = "940623";

    private static byte[] FromHex(string hex) => Convert.FromHexString(hex.Replace(" ", ""));
    private static string ToHex(byte[] bytes) => Convert.ToHexString(bytes);

    [Fact]
    public void Mrz_bilgisi_icao_ornegiyle_uyusuyor()
    {
        var mrzInfo = BacKeyDerivation.BuildMrzInformation(DocumentNumber, DateOfBirth, DateOfExpiry);
        Assert.Equal("L898902C<369080619406236", mrzInfo);
    }

    [Fact]
    public void Bac_anahtarlari_icao_ornegiyle_uyusuyor()
    {
        var keys = BacKeyDerivation.Derive(DocumentNumber, DateOfBirth, DateOfExpiry, NullLogger.Instance);

        Assert.Equal("AB94FDECF2674FDFB9B391F85D7F76F2", ToHex(keys.Encryption));
        Assert.Equal("7962D9ECE03D1ACD4C76089DCE131543", ToHex(keys.Mac));
    }

    [Fact]
    public void Kisa_belge_numarasi_dolgu_ile_tamamlanir()
    {
        // MRZ'de belge no alanı 9 karakter; dolgu anahtara dahil edilmezse
        // çip erişimi reddeder.
        var mrzInfo = BacKeyDerivation.BuildMrzInformation("AB123", "800101", "250101");
        Assert.StartsWith("AB123<<<<", mrzInfo);
        Assert.Equal(24, mrzInfo.Length); // 9+1 + 6+1 + 6+1
    }

    // === ICAO 9303 Part 11, Ek D.3 — karşılıklı doğrulama ara değerleri ===

    [Fact]
    public void Dogrulama_verisi_icao_ornegiyle_sifreleniyor()
    {
        var keys = BacKeyDerivation.Derive(DocumentNumber, DateOfBirth, DateOfExpiry, NullLogger.Instance);

        // S = RND.IFD || RND.ICC || K.IFD
        var s = FromHex(
            "781723860C06C226" +
            "4608F91988702212" +
            "0B795240CB7049B01C19B33E32804F0B");

        var encrypted = ChipCrypto.TripleDesEncrypt(keys.Encryption, s);
        Assert.Equal(
            "72C29C2371CC9BDB65B779B8E8D37B29ECC154AA56A8799FAE2F498F76ED92F2",
            ToHex(encrypted));
    }

    [Fact]
    public void Retail_mac_icao_ornegiyle_uyusuyor()
    {
        var keys = BacKeyDerivation.Derive(DocumentNumber, DateOfBirth, DateOfExpiry, NullLogger.Instance);

        var eIfd = FromHex(
            "72C29C2371CC9BDB65B779B8E8D37B29ECC154AA56A8799FAE2F498F76ED92F2");

        var mac = ChipCrypto.RetailMac(keys.Mac, eIfd);
        Assert.Equal("5F1448EEA8AD90A7", ToHex(mac));
    }

    // === ICAO 9303 Part 11, Ek D.4 — Secure Messaging ===

    /// <summary>Örnekteki oturum anahtarları ve başlangıç sayacı.</summary>
    private static readonly byte[] SessionEnc = FromHex("979EC13B1CBFE9DCD01AB0FED307EAE5");
    private static readonly byte[] SessionMac = FromHex("F1CB1F1FB5ADF208806B89DC579DC1F8");
    private const ulong InitialSsc = 0x887022120C06C226;

    [Fact]
    public void Korumali_komut_icao_ornegiyle_uyusuyor()
    {
        var sm = new SecureMessaging(SessionEnc, SessionMac, InitialSsc, NullLogger.Instance);

        // SELECT EF.COM — 00 A4 02 0C 02 011E
        var apdu = CommandApdu.Send(0x00, 0xA4, 0x02, 0x0C, FromHex("011E"));

        var protectedApdu = sm.Protect(apdu);

        // ICAO 9303 Part 11, Ek D.4'teki korumalı APDU:
        //   0C A4 02 0C          → CLA'ya Secure Messaging biti eklenmiş başlık
        //   15                   → Lc (21 bayt gövde)
        //   87 09 01 6375...44F6 → DO'87': dolgu göstergesi + şifreli "011E"
        //   8E 08 BF8B...24F8    → DO'8E': Retail MAC
        //   00                   → Le
        Assert.Equal(
            "0CA4020C158709016375432908C044F68E08BF8B92D635FF24F800",
            ToHex(protectedApdu));
    }

    [Fact]
    public void Korumali_cevap_dogrulanip_cozulur()
    {
        var sm = new SecureMessaging(SessionEnc, SessionMac, InitialSsc, NullLogger.Instance);

        // Sayaç, komut gönderilirken bir artmış olmalı
        sm.Protect(CommandApdu.Send(0x00, 0xA4, 0x02, 0x0C, FromHex("011E")));

        // Cevap: DO'99' (durum 9000) + DO'8E' (MAC) — veri yok
        var response = FromHex("990290008E08FA855A5D4C50A8ED");

        var plain = sm.Unprotect(response);
        Assert.Empty(plain);
    }

    [Fact]
    public void Bozuk_mac_reddedilir()
    {
        var sm = new SecureMessaging(SessionEnc, SessionMac, InitialSsc, NullLogger.Instance);
        sm.Protect(CommandApdu.Send(0x00, 0xA4, 0x02, 0x0C, FromHex("011E")));

        // Son MAC baytı değiştirildi
        var tampered = FromHex("990290008E08FA855A5D4C50A8EE");

        var ex = Assert.Throws<ChipProtocolException>(() => sm.Unprotect(tampered));
        Assert.Contains("MAC", ex.Message);
    }

    [Fact]
    public void Sayac_her_komut_ve_cevapta_artar()
    {
        var sm = new SecureMessaging(SessionEnc, SessionMac, InitialSsc, NullLogger.Instance);
        Assert.Equal(InitialSsc, sm.SendSequenceCounter);

        sm.Protect(CommandApdu.Send(0x00, 0xA4, 0x02, 0x0C, FromHex("011E")));
        Assert.Equal(InitialSsc + 1, sm.SendSequenceCounter);

        sm.Unprotect(FromHex("990290008E08FA855A5D4C50A8ED"));
        Assert.Equal(InitialSsc + 2, sm.SendSequenceCounter);
    }

    // === Dolgu ===

    [Fact]
    public void Dolgu_tam_blokta_bile_eklenir()
    {
        // ISO 9797-1 yöntem 2: girdi zaten tam blok olsa da 0x80 eklenir,
        // yoksa alıcı dolgunun nerede bittiğini bilemez.
        var full = new byte[8];
        var padded = ChipCrypto.Pad(full);

        Assert.Equal(16, padded.Length);
        Assert.Equal(0x80, padded[8]);
    }

    [Fact]
    public void Dolgu_geri_alinir()
    {
        byte[] original = [1, 2, 3, 4, 5];
        Assert.Equal(original, ChipCrypto.Unpad(ChipCrypto.Pad(original)));
    }

    [Fact]
    public void Bozuk_dolgu_reddedilir()
    {
        byte[] broken = [1, 2, 3, 4, 5, 6, 7, 8];
        var ex = Assert.Throws<ChipProtocolException>(() => ChipCrypto.Unpad(broken));
        Assert.Contains("Dolgu bozuk", ex.Message);
    }

    // === Eşlik bitleri ===

    [Fact]
    public void Des_eslik_bitleri_duzeltilir()
    {
        // Her baytta tek sayıda 1 biti olmalı
        byte[] key = [0x00, 0xFF, 0x0F, 0xF0, 0x55, 0xAA, 0x01, 0x80];
        BacKeyDerivation.AdjustParity(key);

        foreach (var b in key)
        {
            var ones = System.Numerics.BitOperations.PopCount((uint)b);
            Assert.True(ones % 2 == 1, $"0x{b:X2} çift sayıda 1 biti taşıyor");
        }
    }
}
