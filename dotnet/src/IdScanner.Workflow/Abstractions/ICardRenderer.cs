using IdScanner.Core.Model;

namespace IdScanner.Workflow.Abstractions;

/// <summary>
/// Kart görseli üretici.
///
/// Java karşılığı: CardRenderer.java (java.awt.Graphics2D).
/// Uygulaması Render katmanında (System.Drawing.Common).
///
/// Görüntüler PNG byte'ı olarak döner — Application ve Core katmanları çizim
/// kütüphanesi tanımadığı için <c>Bitmap</c> sınır geçmez.
/// </summary>
public interface ICardRenderer
{
    /// <summary>Ön yüz önizlemesi (PNG).</summary>
    byte[] RenderFrontPng(CardData data);

    /// <summary>Arka yüz önizlemesi (PNG).</summary>
    byte[] RenderBackPng();

    /// <summary>Ön ve arka yüzü yan yana koyan önizleme (PNG).</summary>
    byte[] RenderBothPng(CardData data);

    /// <summary>Kalibrasyon kartı önizlemesi (PNG) — renkli bandın yerini ölçmek için.</summary>
    byte[] RenderCalibrationPng();

    /// <summary>
    /// Baskı için ön ve arka yüzü BMP olarak diske yazar.
    /// </summary>
    /// <returns>(ön yüz yolu, arka yüz yolu)</returns>
    (string Front, string Back) RenderFacesToBmp(CardData data);

    /// <summary>Kalibrasyon kartını baskı için BMP olarak diske yazar.</summary>
    /// <returns>(ön yüz yolu, arka yüz yolu)</returns>
    (string Front, string Back) RenderCalibrationToBmp();
}
