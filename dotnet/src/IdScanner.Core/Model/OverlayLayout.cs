namespace IdScanner.Core.Model;

/// <summary>
/// Hazır basılı kartın üzerine basılacak öğelerin yerleşimi (mm).
///
/// <b>Bu nedir:</b> Kartlar matbaada kırmızı zeminli, logolu ve "Adı:" /
/// "Soyadı:" etiketleri basılı olarak geliyor. Yazıcı bu kartın üzerine
/// yalnızca <b>fotoğrafı ve ad/soyad değerlerini</b> ekliyor. Geri kalan
/// her yer beyaz bırakılıyor — beyaz alana mürekkep basılmaz, yani hazır
/// baskı olduğu gibi kalır.
///
/// <b>Ölçüler ölçülerek doldurulmalı:</b> Aşağıdaki değerler fotoğraftan
/// tahmin edildi, cetvelle doğrulanmadı. Kartın sol üst köşesi (0,0) kabul
/// edilir; X sağa, Y aşağı artar. İlk baskıdan sonra kaydırma varsa yalnızca
/// bu sayılar değişir, kodun geri kalanına dokunulmaz.
///
/// <b>Renkli bant kısıtı:</b> Yarım panel ribonda renkli paneller kartın
/// yalnızca bir bandını kaplıyor. Fotoğraf o bandın dışına düşerse siyah-beyaz
/// basılır. Hazır kartta fotoğraf kutusu üstte olduğu için kart ters
/// besleneceek ve kutu banda denk gelecek — ilk baskıda doğrulanacak.
/// </summary>
public sealed record OverlayLayout
{
    /// <summary>Fotoğraf kutusunun sol kenarı.</summary>
    public double PhotoXMm { get; init; } = 4.0;

    /// <summary>Fotoğraf kutusunun üst kenarı.</summary>
    public double PhotoYMm { get; init; } = 6.0;

    /// <summary>Fotoğraf kutusunun genişliği.</summary>
    public double PhotoWidthMm { get; init; } = 20.0;

    /// <summary>Fotoğraf kutusunun yüksekliği.</summary>
    public double PhotoHeightMm { get; init; } = 26.0;

    /// <summary>Ad değerinin yazılacağı X konumu ("Adı:" etiketinin sağı).</summary>
    public double NameXMm { get; init; } = 14.0;

    /// <summary>Ad değerinin taban çizgisi.</summary>
    public double NameYMm { get; init; } = 36.0;

    /// <summary>Soyad değerinin yazılacağı X konumu.</summary>
    public double SurnameXMm { get; init; } = 14.0;

    /// <summary>Soyad değerinin taban çizgisi.</summary>
    public double SurnameYMm { get; init; } = 42.0;

    /// <summary>Ad/soyad yazı boyutu.</summary>
    public double TextSizeMm { get; init; } = 3.0;

    /// <summary>
    /// Baskıdan önce görseli 180° döndür.
    ///
    /// Hazır kart ters besleneceği için fotoğrafın banda denk gelmesi
    /// gerekiyor; hangi yönün doğru olduğu ilk baskıda belli olacak.
    /// </summary>
    public bool Rotate180 { get; init; }

    /// <summary>
    /// Yerleşimi görsel olarak denetlemek için kılavuz çizgiler çiz.
    ///
    /// Fotoğraf kutusunun ve yazı satırlarının sınırlarını ince gri çizgiyle
    /// gösterir. Önizlemede açılıp hazır kartla karşılaştırılır; <b>baskıda
    /// kapalı olmalı</b>, yoksa karta çizgiler de basılır.
    /// </summary>
    public bool ShowGuides { get; init; }

    /// <summary>Ölçüler doğrulanana kadar kullanılacak varsayılan yerleşim.</summary>
    public static OverlayLayout Default { get; } = new();
}
