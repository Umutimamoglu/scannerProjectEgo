namespace IdScanner.Core.Model;

/// <summary>
/// Tarayıcıdaki kartın konumu (IDSIF <c>CardStatus</c> dönüşü).
///
/// Java karşılığı: IdCardReader.CardStatus arayüzündeki sabitler.
/// </summary>
public enum CardStatus
{
    /// <summary>Durum okunamadı.</summary>
    Unknown = -1,

    /// <summary>Cihazda kart yok.</summary>
    None = 0,

    /// <summary>Kart hareket halinde.</summary>
    Moving = 1,

    /// <summary>Ön pozisyonda.</summary>
    Front = 2,

    /// <summary>İçeride.</summary>
    Inside = 3,

    /// <summary>Arka pozisyonda.</summary>
    Back = 4,
}
