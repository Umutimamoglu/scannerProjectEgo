using IdScanner.Core.Model;

namespace IdScanner.Workflow.Abstractions;

/// <summary>
/// BAC anahtarları — MRZ'den türetilen üç değer.
/// </summary>
/// <param name="DocumentNumber">Belge numarası (9 karakter, dolgu dahil).</param>
/// <param name="DateOfBirth">Doğum tarihi, YYMMDD.</param>
/// <param name="DateOfExpiry">Son kullanma tarihi, YYMMDD.</param>
public readonly record struct BacCredentials(string DocumentNumber, string DateOfBirth, string DateOfExpiry);

/// <summary>
/// Temassız çip okuyucu — BAC ile oturum açar, veri gruplarını okur ve
/// doğrulamayı çalıştırır.
///
/// Java karşılığı: IdCardReader'ın JMRTD kullanan kısmı.
/// Uygulaması Chip katmanında (PC/SC + elle yazılan BAC/Secure Messaging).
/// </summary>
public interface IChipReader
{
    /// <summary>Sistemde temassız okuyucu var mı.</summary>
    bool IsReaderAvailable();

    /// <summary>
    /// Çipe bağlan, BAC oturumu aç, veri gruplarını oku ve doğrula.
    /// </summary>
    /// <param name="credentials">MRZ'den türetilen BAC girdileri.</param>
    /// <returns>Okunan kimlik verisi; doğrulama sonucu içinde taşınır.</returns>
    IdData Read(BacCredentials credentials);
}
