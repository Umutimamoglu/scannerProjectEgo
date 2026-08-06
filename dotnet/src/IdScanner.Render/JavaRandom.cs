namespace IdScanner.Render;

/// <summary>
/// <c>java.util.Random</c>'ın birebir aynısı.
///
/// <b>Neden gerekli:</b> Arka yüzdeki low-poly desen, sabit tohumlu
/// (<c>new Random(7)</c>) bir rastgele diziyle üretiliyor — yani Java'da her
/// çalıştırmada <i>aynı</i> desen çıkıyor. .NET'in <see cref="System.Random"/>
/// sınıfı bambaşka bir algoritma kullandığı için aynı tohumla farklı sayılar
/// verir ve desen tutmaz.
///
/// Java'nın algoritması belgelenmiş bir doğrusal eşleşik üreteç (LCG);
/// aynısını kurmak deseni birebir korumanın tek yolu.
/// </summary>
internal sealed class JavaRandom
{
    private const long Multiplier = 0x5DEECE66DL;
    private const long Addend = 0xBL;
    private const long Mask = (1L << 48) - 1;

    private long _seed;

    internal JavaRandom(long seed)
    {
        _seed = (seed ^ Multiplier) & Mask;
    }

    /// <summary>Java'nın <c>next(int bits)</c> metodu.</summary>
    private int Next(int bits)
    {
        _seed = (_seed * Multiplier + Addend) & Mask;
        return (int)((ulong)_seed >> (48 - bits));
    }

    /// <summary>Java'nın <c>nextDouble()</c> metodu — [0.0, 1.0) aralığında.</summary>
    internal double NextDouble()
    {
        return (((long)Next(26) << 27) + Next(27)) * (1.0 / (1L << 53));
    }

    /// <summary>Java'nın <c>nextInt(int bound)</c> metodu — [0, bound) aralığında.</summary>
    internal int NextInt(int bound)
    {
        if (bound <= 0) throw new ArgumentOutOfRangeException(nameof(bound), "Sınır pozitif olmalı");

        // İkinin kuvvetiyse doğrudan üst bitler kullanılır
        if ((bound & -bound) == bound)
        {
            return (int)((bound * (long)Next(31)) >> 31);
        }

        // Değilse, modulo sapmasını önlemek için yeniden denenir
        int bits, value;
        do
        {
            bits = Next(31);
            value = bits % bound;
        }
        while (bits - value + (bound - 1) < 0);

        return value;
    }
}
