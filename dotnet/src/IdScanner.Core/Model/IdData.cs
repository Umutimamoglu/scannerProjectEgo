using IdScanner.Core.Verification;

namespace IdScanner.Core.Model;

/// <summary>
/// Çipten ve MRZ'den okunan kimlik verisi.
///
/// Java karşılığı: IdCardReader.IdData.
///
/// Türkçe isimler DG11'den gelir — MRZ yalnızca ASCII taşır, İ/Ğ/Ş yoktur.
/// Bu yüzden DG11 okunduğunda DG1'den gelen isimlerin üzerine yazılır.
/// </summary>
public sealed class IdData
{
    public string Name { get; set; } = "";
    public string Surname { get; set; } = "";
    public string TcNo { get; set; } = "";
    public string DocumentNumber { get; set; } = "";

    /// <summary>GG.AA.YYYY biçiminde.</summary>
    public string BirthDate { get; set; } = "";

    public string BirthPlace { get; set; } = "";
    public string Gender { get; set; } = "";
    public string Nationality { get; set; } = "";
    public string IssueDate { get; set; } = "";

    /// <summary>GG.AA.YYYY biçiminde.</summary>
    public string ExpiryDate { get; set; } = "";

    public string IssuingAuthority { get; set; } = "";

    /// <summary>
    /// Yüz fotoğrafı — PNG olarak kodlanmış byte'lar.
    ///
    /// Java'da <c>BufferedImage</c> idi. Core katmanı çizim kütüphanesi
    /// tanımadığı için burada ham byte olarak taşınıyor; görüntüye çevirme
    /// Render/App katmanının işi.
    /// </summary>
    public byte[]? PhotoPng { get; set; }

    public string MrzLine1 { get; set; } = "";
    public string MrzLine2 { get; set; } = "";
    public string MrzLine3 { get; set; } = "";

    /// <summary>BAC için ham MRZ değerleri (YYMMDD).</summary>
    public string BacDocNo { get; set; } = "";

    /// <inheritdoc cref="BacDocNo"/>
    public string BacBirth { get; set; } = "";

    /// <inheritdoc cref="BacDocNo"/>
    public string BacExpiry { get; set; } = "";

    /// <summary>
    /// Çip doğrulama sonucu (veri gerçek mi, klon mu). <c>null</c> = yapılmadı.
    /// </summary>
    public VerificationResult? Verification { get; set; }

    /// <summary>
    /// Bu verinin nereden geldiği. Java'da yok — bkz. <see cref="KartKaynagi"/>.
    /// </summary>
    public KartKaynagi Kaynak { get; set; } = KartKaynagi.Bilinmiyor;
}
