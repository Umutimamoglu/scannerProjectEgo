using IdScanner.Core.Mrz;

namespace IdScanner.Tests;

/// <summary>
/// MRZ ayrıştırma ve OCR düzeltme testleri.
///
/// Bu katman donanımsız çalışabildiği için taşımanın doğruluğu burada
/// gerçekten kanıtlanabiliyor — çip ve yazıcı olmadan.
/// </summary>
public class MrzParserTests
{
    /// <summary>
    /// Geçerli bir TD1 MRZ'si — 3 satır × 30 karakter.
    ///
    /// Check digit'ler ICAO 9303 7-3-1 kuralıyla hesaplandı:
    ///   A12345678 → 4   (A=10 olmak üzere ağırlıklı toplam 184)
    ///   800101    → 4
    ///   250101    → 7
    ///
    /// Satır 1: I&lt;TUR + belgeNo(9) + check(1) + opsiyonel(15)
    /// Satır 2: dogum(6)+chk + cinsiyet + sonKullanma(6)+chk + uyruk(3) + ops(11) + bileşik
    /// </summary>
    private const string ValidTd1 =
        "I<TURA123456784<<<<<<<<<<<<<<<\n" +
        "8001014M2501017TUR<<<<<<<<<<<8\n" +
        "YILMAZ<<AHMET<MEHMET<<<<<<<<<<";

    [Fact]
    public void Gecerli_td1_ayristirilir()
    {
        var mrz = MrzParser.Parse(ValidTd1);

        Assert.Equal("A12345678", mrz.DocumentNumber);
        Assert.Equal("800101", mrz.DateOfBirth);
        Assert.Equal("250101", mrz.DateOfExpiry);
        Assert.Equal("M", mrz.Sex);
        Assert.Equal("TUR", mrz.Nationality);
        Assert.Equal("YILMAZ", mrz.PrimaryName);
        Assert.Equal("AHMET MEHMET", mrz.SecondaryName);
    }

    [Fact]
    public void Satirlar_arasindaki_bosluklar_atilir()
    {
        // OCR çıktısı satır içinde boşluk üretebiliyor
        var noisy = ValidTd1.Replace("<<<<", "<< <<");
        var mrz = MrzParser.Parse(noisy);
        Assert.Equal("A12345678", mrz.DocumentNumber);
    }

    [Fact]
    public void Mrz_bulunamazsa_istisna_firlatir()
    {
        var ex = Assert.Throws<MrzParseException>(() => MrzParser.Parse("alakasiz metin\nbaska satir"));
        Assert.Contains("MRZ 3 satırı bulunamadı", ex.Message);
    }

    [Fact]
    public void TryParse_bulunamazsa_false_doner()
    {
        Assert.False(MrzParser.TryParse("hicbir sey", out var mrz));
        Assert.Null(mrz);
    }

    // === Check digit ===

    [Theory]
    // ICAO 9303 Part 3, Ek A'daki referans değerler
    [InlineData("D23145890734", 9)]
    [InlineData("340712", 7)]
    [InlineData("950712", 2)]
    public void Check_digit_icao_ornekleriyle_uyusuyor(string input, int expected)
    {
        Assert.Equal(expected, MrzParser.CheckDigit(input));
    }

    [Fact]
    public void Dolgu_karakteri_sifir_sayilir()
    {
        // '<' ICAO'ya göre 0 değerinde
        Assert.Equal(MrzParser.CheckDigit("0<0"), MrzParser.CheckDigit("000"));
    }

    // === OCR düzeltme ===

    [Fact]
    public void Ocr_karisikligi_check_digit_ile_duzeltilir()
    {
        // Belge no A13467934 (check digit 5). OCR '1' yerine 'I' okumuş.
        // Diğer karakterlerin (3,4,6,7,9) OCR karışıklık eşi yok, yani
        // düzeltilebilecek TEK karakter 'I' — sonuç kesin.
        var mistyped =
            "I<TURAI34679345<<<<<<<<<<<<<<<\n" +
            "8001014M2501017TUR<<<<<<<<<<<8\n" +
            "YILMAZ<<AHMET<MEHMET<<<<<<<<<<";

        var mrz = MrzParser.Parse(mistyped);
        Assert.Equal("A13467934", mrz.DocumentNumber);
    }

    [Fact]
    public void Birden_fazla_aday_varsa_ilki_secilir()
    {
        // BİLİNEN ZAYIFLIK — Java'dan aynen taşındı.
        //
        // AI2345678'de dört karakter karıştırılabilir (I↔1, 2↔Z, 5↔S, 8↔B).
        // Check digit '4' ile uyumlu birden fazla kombinasyon var; algoritma
        // sayaç sırasında ilk tutanı döndürüyor ve bu, doğru olan
        // "A12345678" olmayabiliyor.
        //
        // Sonuç yine de geçerli bir check digit taşır — yani düzeltme "sessizce
        // yanlış" olabilir. Bu durumda log'a UYARI düşer (bkz. MrzParser).
        var ambiguous =
            "I<TURAI23456784<<<<<<<<<<<<<<<\n" +
            "8001014M2501017TUR<<<<<<<<<<<8\n" +
            "YILMAZ<<AHMET<MEHMET<<<<<<<<<<";

        var mrz = MrzParser.Parse(ambiguous);

        // Garanti edilen tek şey: dönen değerin check digit'i tutar.
        Assert.Equal(4, MrzParser.CheckDigit(mrz.DocumentNumber));
        Assert.Equal(9, mrz.DocumentNumber.Length);
    }

    [Fact]
    public void Duzeltilemeyen_deger_oldugu_gibi_korunur()
    {
        // 'W' karakterinin OCR karışıklık eşi yok, yani hiçbir kombinasyon
        // denenemiyor. Check digit yanlış olsa bile veri UYDURULMAMALI —
        // OCR'ın okuduğu aynen korunmalı.
        var broken =
            "I<TURWWWWWWWWW1<<<<<<<<<<<<<<<\n" +
            "8001014M2501017TUR<<<<<<<<<<<8\n" +
            "YILMAZ<<AHMET<MEHMET<<<<<<<<<<";

        var mrz = MrzParser.Parse(broken);
        Assert.Equal("WWWWWWWWW", mrz.DocumentNumber);
    }

    // === Cinsiyet normalleştirme ===

    [Theory]
    [InlineData('M', "M")]
    [InlineData('F', "F")]
    [InlineData('6', "M")]   // OCR 'M' yerine '6' okuyabiliyor
    [InlineData('H', "M")]
    [InlineData('X', "<")]   // tanınmayan → belirtilmemiş
    public void Cinsiyet_normallestirilir(char raw, string expected)
    {
        var line2 = $"8001014{raw}2501017TUR<<<<<<<<<<<8";
        var text = $"I<TURA123456784<<<<<<<<<<<<<<<\n{line2}\nYILMAZ<<AHMET<<<<<<<<<<<<<<<<<";

        var mrz = MrzParser.Parse(text);
        Assert.Equal(expected, mrz.Sex);
    }

    // === Satır 3 ===

    [Fact]
    public void Ayirici_yoksa_tamami_soyad_sayilir()
    {
        var text =
            "I<TURA123456784<<<<<<<<<<<<<<<\n" +
            "8001014M2501017TUR<<<<<<<<<<<8\n" +
            "YILMAZ<<<<<<<<<<<<<<<<<<<<<<<<";

        var mrz = MrzParser.Parse(text);
        Assert.Equal("YILMAZ", mrz.PrimaryName);
        Assert.Equal("", mrz.SecondaryName);
    }

    [Fact]
    public void Satir_uzunlugu_otuza_tamamlanir()
    {
        Assert.Equal(30, MrzParser.PadToLineLength("KISA").Length);
        Assert.EndsWith("<<<", MrzParser.PadToLineLength("KISA"));
        Assert.Equal(30, MrzParser.PadToLineLength(new string('A', 40)).Length);
    }
}
