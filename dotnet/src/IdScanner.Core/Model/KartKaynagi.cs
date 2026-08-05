namespace IdScanner.Core.Model;

/// <summary>
/// Okunan kimlik verisinin nereden geldiği.
///
/// Java'da yok — .NET'e taşınırken eklendi. Sebebi: "isim geldi" ile
/// "isim <b>güvenilir bir yerden</b> geldi" ayrımı yapılabilsin. Çip
/// okunamayıp MRZ'ye düşüldüğünde veri hâlâ doğru olabilir ama artık
/// kriptografik olarak doğrulanmış değildir; bu ayrım ileride bir güven
/// kararına dönüşecek.
///
/// Şu an yalnızca bilgi taşır, hiçbir davranışı değiştirmez.
/// </summary>
public enum KartKaynagi
{
    /// <summary>Kaynak belirtilmemiş (varsayılan).</summary>
    Bilinmiyor = 0,

    /// <summary>T.C. Kimlik Kartı çipinden okundu (DG1/DG11/DG12).</summary>
    CipTckk,

    /// <summary>Mavi Kart çipinden okundu.</summary>
    CipMaviKart,

    /// <summary>Çip okunamadı, MRZ'den ayrıştırıldı — doğrulanmamış.</summary>
    MrzFallback,

    /// <summary>Optik tarama / OCR sonucu — doğrulanmamış.</summary>
    OptikTarama,
}
