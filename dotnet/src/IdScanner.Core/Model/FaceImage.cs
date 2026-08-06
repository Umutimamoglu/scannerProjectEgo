namespace IdScanner.Core.Model;

/// <summary>
/// DG2'deki yüz görüntüsünün kodlaması.
///
/// ICAO 9303 her ikisine de izin veriyor; T.C. Kimlik Kartı'nda JPEG2000
/// görülüyor. Fark önemli çünkü .NET JPEG'i kendi çözer, JPEG2000 için ayrı
/// bir çözücü gerekir.
/// </summary>
public enum FaceImageFormat
{
    Unknown = 0,
    Jpeg,
    Jpeg2000,
}

/// <summary>
/// Çipten çıkarılan yüz görüntüsü — ham hâliyle.
///
/// Çözme işi Render katmanında yapılır; Core çizim kütüphanesi tanımaz.
/// </summary>
/// <param name="Bytes">Görüntünün ham baytları.</param>
/// <param name="Format">Kodlama.</param>
public sealed record FaceImage(byte[] Bytes, FaceImageFormat Format);
