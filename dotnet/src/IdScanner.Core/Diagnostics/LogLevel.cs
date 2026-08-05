namespace IdScanner.Core.Diagnostics;

/// <summary>
/// Log önem seviyeleri.
///
/// Java'da seviye yoktu — her şey tek bir <c>log(String)</c> çağrısıydı ve
/// hepsi arayüzdeki metin kutusuna gidiyordu. Saha testinde "ne oldu" sorusunu
/// cevaplayabilmek için ayrım şart: arayüzde <see cref="Info"/> ve üstü
/// gösterilir, dosyaya <see cref="Trace"/> dahil her şey yazılır.
/// </summary>
public enum LogLevel
{
    /// <summary>Adım adım iz — APDU trafiği, denenen algoritma kombinasyonları.</summary>
    Trace = 0,

    /// <summary>Ayrıntı — ara değerler, boyutlar, süreler.</summary>
    Debug = 1,

    /// <summary>Normal akış — kullanıcıya gösterilebilir.</summary>
    Info = 2,

    /// <summary>Beklenen ama istenmeyen durum — akış devam ediyor.</summary>
    Warn = 3,

    /// <summary>İşlem başarısız oldu.</summary>
    Error = 4,
}
