using System.Runtime.InteropServices;
using IdScanner.Native.Interop;

namespace IdScanner.Tests;

/// <summary>
/// Native struct'ların bellek düzenini kilitler.
///
/// <b>Neden bu test var:</b> P/Invoke'ta bir alan kayarsa derleme hatası
/// olmaz, istisna da fırlamaz — yazıcı künyesi sessizce saçma değerler
/// döndürür ve bunu ancak ekranda "model: ????" görünce fark edersin.
/// En sinsi hâli <c>bool</c> alanlar: C tarafında 1 bayt, .NET'te
/// <c>MarshalAs(UnmanagedType.I1)</c> verilmezse 4 bayt hizalanır ve
/// <see cref="EvolisNative.PrinterInfoNative"/>'daki 13 bool'dan sonraki
/// her alan bozulur.
///
/// Beklenen boyutlar, C başlık dosyasındaki alan sıralamasından x64 doğal
/// hizalama kurallarıyla hesaplandı.
/// </summary>
public class NativeStructLayoutTests
{
    [Fact]
    public void Device_boyutu_beklenen_degerde()
    {
        // char[128] + char[256] + char[256] + char[512] = 1152
        // + int mark(4) + int model(4) + bool(1) + bool(1) + dolgu(2)
        // + int link(4) + char[128]
        Assert.Equal(1296, Marshal.SizeOf<EvolisNative.Device>());
    }

    [Fact]
    public void PrinterInfo_boyutu_beklenen_degerde()
    {
        // 13 bool alanın 1'er bayt olması şart; 4'er bayt olsaydı 36 bayt şişerdi
        Assert.Equal(396, Marshal.SizeOf<EvolisNative.PrinterInfoNative>());
    }

    [Fact]
    public void RibbonInfo_boyutu_beklenen_degerde()
    {
        Assert.Equal(180, Marshal.SizeOf<EvolisNative.RibbonInfoNative>());
    }

    [Fact]
    public void CleaningInfo_boyutu_beklenen_degerde()
    {
        // 7 int(28) + bool(1) + dolgu(3) + 2 int(8)
        Assert.Equal(40, Marshal.SizeOf<EvolisNative.CleaningInfoNative>());
    }

    [Fact]
    public void Status_boyutu_beklenen_degerde()
    {
        // 4 int(16) + int[4](16) + short(2) + dolgu(2)
        Assert.Equal(36, Marshal.SizeOf<EvolisNative.Status>());
    }

    [Fact]
    public void PrinterInfo_bool_alanlari_tek_bayt()
    {
        // Bool'ların hemen öncesindeki ve sonrasındaki alanların ofsetleri,
        // 13 bool'un tam 13 bayt kapladığını doğruluyor.
        var zoneOffset = (int)Marshal.OffsetOf<EvolisNative.PrinterInfoNative>(
            nameof(EvolisNative.PrinterInfoNative.Zone));
        var insertionOffset = (int)Marshal.OffsetOf<EvolisNative.PrinterInfoNative>(
            nameof(EvolisNative.PrinterInfoNative.InsertionCaps));

        // Zone char[16] biter → 13 bool → 4'e hizalama dolgusu → InsertionCaps
        Assert.Equal(zoneOffset + 16 + 13 + 3, insertionOffset);
    }

    [Fact]
    public void ScanConf_boyutu_beklenen_degerde()
    {
        // 4 int(16) + iki ImageParam (3 int = 12 bayt her biri)
        Assert.Equal(40, Marshal.SizeOf<IdsifNative.ScanConf>());
    }

    [Fact]
    public void ScanResult_boyutu_beklenen_degerde()
    {
        // char[260] + char[260] + byte + byte + byte[2]
        Assert.Equal(524, Marshal.SizeOf<IdsifNative.ScanResultNative>());
    }

    [Fact]
    public void Evolis_bayrak_listesi_eksiksiz()
    {
        // Java'daki EvolisFlags.NAMES ile aynı uzunlukta olmalı; bayrak
        // kimliği = dizi indeksi olduğu için eksik eleman tüm eşlemeyi kaydırır.
        Assert.Equal(256, EvolisFlags.Names.Length);
        Assert.Equal("CFG_X01", EvolisFlags.Names[0]);
        Assert.Equal("RSV_EX4_0X00000001", EvolisFlags.Names[^1]);
    }

    [Theory]
    [InlineData("RSV_EX4_0X00000001", false)]
    [InlineData("CFG_WIFI", false)]
    [InlineData("ERR_MECHANICAL", true)]
    [InlineData("WAR_COVER_OPEN", true)]
    public void Bayrak_gosterilebilirligi(string name, bool expected)
    {
        Assert.Equal(expected, EvolisFlags.IsReportable(name));
    }
}
