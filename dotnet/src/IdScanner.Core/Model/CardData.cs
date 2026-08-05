namespace IdScanner.Core.Model;

/// <summary>
/// Karta basılacak veriler.
///
/// Java karşılığı: CardRenderer.CardData.
/// </summary>
public sealed class CardData
{
    public string Title { get; set; } = "BAŞKENT KART";
    public string Subtitle { get; set; } = "ULAŞIM";
    public string Name { get; set; } = "";
    public string Surname { get; set; } = "";
    public string IdNumber { get; set; } = "";
    public string BirthDate { get; set; } = "";
    public string ExpiryDate { get; set; } = "";

    /// <summary>Fotoğraf dosya yolu — yoksa fotoğraf alanı boş çizilir.</summary>
    public string? PhotoPath { get; set; }
}
