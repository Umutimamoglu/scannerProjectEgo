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
/// <b>Ölçüler nereden geldi:</b> Basılı kartın fotoğrafından ölçüldü, cetvelle
/// doğrulanmadı. Kartın sol üst köşesi (0,0) kabul edilir; X sağa, Y aşağı
/// artar — kart dik dururken, otobüs görselleri üstte. Kaydırma gerekirse
/// yalnızca bu sayılar değişir, kodun geri kalanına dokunulmaz.
///
/// <b>Renkli bant kısıtı çözüldü:</b> Yarım panel ribonda renkli paneller
/// yalnızca 47,5–85,6 mm arasını kaplıyor ve bu aralık <i>görüntü</i>
/// uzayında sabit — kart nasıl takılırsa takılsın değişmiyor. Kartın boş
/// fotoğraf kutusu 55–68 mm arasında, yani bandın tam içinde. Bu yüzden
/// fotoğraf döndürmeye gerek kalmadan renkli basılır.
/// </summary>
public sealed record OverlayLayout
{
    /// <summary>Fotoğraf kutusunun sol kenarı.</summary>
    public double PhotoXMm { get; init; } = 4.0;

    /// <summary>
    /// Fotoğraf kutusunun üst kenarı — renkli bandın (47,5+) içinde.
    /// İlk gerçek baskıda 2 mm aşağıda kaldığı görüldü, yukarı çekildi.
    /// </summary>
    public double PhotoYMm { get; init; } = 53.0;

    /// <summary>Fotoğraf kutusunun genişliği.</summary>
    public double PhotoWidthMm { get; init; } = 14.5;

    /// <summary>Fotoğraf kutusunun yüksekliği.</summary>
    public double PhotoHeightMm { get; init; } = 13.0;

    /// <summary>
    /// Ad değerinin yazılacağı X konumu ("Adı:" etiketinin sağı).
    /// İlk baskıda etiketle arasında fazla boşluk kaldı, 3 mm sola alındı.
    /// </summary>
    public double NameXMm { get; init; } = 32.0;

    /// <summary>Ad değerinin taban çizgisi — "Adı:" etiketiyle aynı satır.</summary>
    public double NameYMm { get; init; } = 61.0;

    /// <summary>Soyad değerinin yazılacağı X konumu — ad ile aynı hizada.</summary>
    public double SurnameXMm { get; init; } = 32.0;

    /// <summary>Soyad değerinin taban çizgisi — "Soyadı:" etiketiyle aynı satır.</summary>
    public double SurnameYMm { get; init; } = 65.5;

    /// <summary>Ad/soyad yazı boyutu — iki satır 4,5 mm arayla, sığması için küçük.</summary>
    public double TextSizeMm { get; init; } = 2.4;

    /// <summary>
    /// Baskıdan önce görseli 180° döndür.
    ///
    /// <b>Varsayılan kapalı.</b> İlk denemede hem bu bayrak açıktı hem de kart
    /// fiziksel ters takıldı; iki dönüş birbirini götürdüğü için içerik ham
    /// koordinatlara düştü. Kart düz takıldığında kart ekseni ile görüntü
    /// ekseni birebir örtüşür, döndürmeye gerek kalmaz. Yalnızca kartın
    /// beslenme yönü zorunlu olarak değişirse açılır.
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
