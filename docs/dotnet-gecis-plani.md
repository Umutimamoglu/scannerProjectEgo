# Java → .NET 10 Geçiş Planı

**Branch:** `feature/dotnet-port` (base: `feature/chip-verification`)
**Hedef:** .NET 10 (LTS) — *not: ".NET Core" adı .NET 5'ten sonra bırakıldı, doğru ad sadece ".NET 10"*
**Karar verilenler:** Çip okuma **tam yerli .NET** olacak (Java kalmayacak) · Arayüz **WinForms**

---

## 0. Bu planın mantığı

Soru şuydu: *"hiçbir şey bozulmadan nasıl çeviririz?"*

Cevap tek cümlede: **her parçayı, çevirmeden önce ölçülebilir hale getiriyoruz.**

Şu an "doğru çalışıyor"un tanımı yok. Elle bakıp "evet oldu" diyoruz. Bu şekilde çeviri yaparsak bozulduğunu ancak EGO testinde fark ederiz. Onun yerine:

1. Java tarafından **referans veri** (fixture) topluyoruz — gerçek karttan okunan ham byte'lar, üretilen kart görselleri.
2. .NET tarafı aynı girdiden **aynı çıktıyı** üretiyor mu, otomatik karşılaştırıyoruz.
3. Bir faz ancak bu karşılaştırma geçince "bitti" sayılıyor.

Bunun ikinci faydası: fixture'lar elde olunca **fiziksel kart ve yazıcı olmadan** geliştirme yapılabiliyor. Çip okuma kodunun %80'i kartsız yazılıp doğrulanabilir.

### Değişmeyen kurallar

- **Java sürümü silinmez.** `feature/chip-verification` çalışır halde durur. .NET her şeyi geçene kadar üretim sürümü odur.
- **Fazlar sırayla.** Her faz kendinden öncekine dayanıyor; atlanırsa doğrulama zinciri kopar.
- **Donanımsız işler önce.** Kart/yazıcı gerektiren iş en sona bırakılır, çünkü en yavaş ilerleyen kısım odur.
- **Fixture'lar git'e girmez.** Gerçek kart verisi = gerçek kişinin adı, fotoğrafı, TC no'su. Repo GitHub'da. `fixtures/` klasörü `.gitignore`'a girer; nasıl yeniden üretileceği dokümante edilir.

---

## Kod standartları (bağlayıcı)

Mevcut Java kodunda düzeltilecek somut sorunlar var — geçiş bunları taşımanın değil, **bırakmanın** fırsatı:

- Her şey tek pakette (`com.mobiloby`), 14 sınıf yan yana; katman yok.
- `MainUI` 837 satır ve hem arayüz çiziyor, hem cihaz bağlıyor, hem baskı akışı yönetiyor, hem log tutuyor.
- `IdCardReader` hem APDU konuşuyor, hem TLV çözüyor, hem dosyaya PNG yazıyor.
- Hata yönetimi `catch (Exception e) { log(...) }` — hata yutuluyor, çağıran ne olduğunu bilmiyor.

.NET tarafında uyulacak kurallar:

**1. Katman sınırları tek yönlü.** Bağımlılık oku hep içeri bakar:
`App → Render/Chip/Native/Crypto → Core`. `Core` hiçbir şeye bağlı değil, hiçbir DLL tanımaz. Bir katman kendinden üsttekini **çağıramaz**. Bu yüzden 7 ayrı proje var — kural derleyici tarafından zorlanıyor, iyi niyete bırakılmıyor.

**2. Arayüz iş mantığı içermez.** WinForms formu yalnızca: girdi topla → servisi çağır → sonucu göster. Baskı akışı, doğrulama kararı, cihaz durumu yorumlama formun içinde olmaz. `MainUI`'nin 837 satırı böyle oluştu; tekrarlanmayacak.

**3. Donanım arkasında arayüz (interface) olur.** `ICardReader`, `IPrinter`, `IScanner`. Somut sınıflar `Native` katmanında. Böylece fixture'larla çalışan sahte (fake) uygulamalar yazılabiliyor — Faz 2 ve 3'ün kartsız doğrulanması buna dayanıyor. Test edilebilirlik burada bir yan fayda değil, planın taşıyıcı direği.

**4. Hata yutulmaz.** Beklenen başarısızlık (kart yok, DG okunamadı, doğrulama tutmadı) `Result`-benzeri bir dönüş tipiyle taşınır; beklenmeyen hata yukarı fırlar. `catch { }` ve boş `catch (Exception)` yasak. Loglama hata *yönetimi* değildir.

**5. P/Invoke izole edilir.** `[DllImport]` imzaları yalnızca `Native` katmanında, `internal` olarak durur. Üst katmanlar `IntPtr`, `Marshal`, struct görmez — temiz C# tipleri görür. Native tarafın çirkinliği tek bir yerde hapsedilir.

**6. Metot boyu.** Bir metot ekranı aşıyorsa bölünür. `CardRenderer.render` gibi 60 satırlık çizim metotları anlamlı parçalara ayrılır (başlık, alanlar, fotoğraf, kenarlık).

**7. Sihirli sayı olmaz.** `85.6`, `54.0`, `300`, `0x77` gibi değerler adlandırılmış sabit olur, nereden geldiği yorumda yazar (ISO 7810 ID-1, ICAO 9303 §x).

**8. İsimlendirme C# kurallarına uyar.** `PascalCase` metot/özellik, `camelCase` yerel değişken, `I` önekli arayüz. Java alışkanlığı taşınmaz.

**9. Yorumlar Türkçe kalır** — mevcut kodun dili bu, tutarlılık bozulmaz. *Ne* yaptığını değil *neden* öyle yaptığını anlatır.

**Sınır:** Bunlar **yapıyı** iyileştirir, **davranışı** değiştirmez. "Madem elimiz değdi" diye algoritma iyileştirme, özellik ekleme, hata düzeltme yapılmaz — o zaman .NET çıktısı Java ile karşılaştırılamaz hale gelir ve tüm doğrulama zinciri anlamını yitirir. Java'daki bir hata .NET'e aynen taşınır, ayrıca not edilir.

---

## Faz 0 — Emniyet ağı (Java tarafında, .NET'e hiç dokunmadan)

> **Neden ilk sırada:** Fizikî kart ve yazıcı şu an elde. EGO testleri Ağustos sonunda. Referans veriyi *şimdi* toplamazsak, sonra çip kodunu doğrulayacak hiçbir şeyimiz olmaz.

| # | İş | Bitti sayılma kriteri |
|---|---|---|
| 0.1 | .NET 10 SDK kurulumu | `dotnet --list-sdks` çıktısında 10.x görünüyor |
| 0.2 | Java'ya fixture dökümü ekle: ham DG1/2/11/12/15, SOD, BAC girdileri (belge no + doğum + son kullanma), okunan MRZ metni | `IdCardReader` çalıştığında `fixtures/` altına dosyalar düşüyor |
| 0.3 | Gerçek kartla bir kez çalıştır, çıktıyı dondur | `fixtures/` içinde en az bir tam set var, hash'leri kayıtlı |
| 0.4 | Java'nın bu fixture'lardan ürettiği alan değerlerini JSON olarak yaz (ad, soyad, TC, tarihler, doğrulama sonuçları) | `fixtures/expected.json` — .NET'in hedefi bu |
| 0.5 | `CardRenderer` referans görselleri — **sahte veriyle** (Ahmet Yılmaz vb.), ön + arka + kalibrasyon | `fixtures/render/*.png` donduruldu, kişisel veri içermiyor |
| 0.6 | Evolis ve IDSIF struct'larının Java'daki gerçek boyutlarını yazdır | `fixtures/struct-sizes.txt` — P/Invoke doğrulaması için |

**Faz 0 çıktısı:** Bundan sonra kart olmadan da çalışabiliriz.

---

## Faz 1 — İskelet ve saf mantık (donanım gerekmez)

Çözüm yapısı:

```
IdScanner.sln
├─ IdScanner.Core     → donanımsız mantık (MRZ parse, model sınıfları, yollar)
├─ IdScanner.Crypto   → çip doğrulama (PA + AA)
├─ IdScanner.Chip     → PC/SC, BAC, secure messaging, DG çözümleme
├─ IdScanner.Native   → P/Invoke: evolis.dll, IDSIF.dll
├─ IdScanner.Render   → kart görseli üretimi
├─ IdScanner.App      → WinForms arayüz
└─ IdScanner.Tests    → hepsinin testleri
```

Bu ayrımın sebebi: Java'da her şey tek pakette (`com.mobiloby`), UI ile çip okuma birbirine karışmış. Sınırları burada çiziyoruz ki her katman ayrı test edilebilsin.

| # | İş | Bitti sayılma kriteri |
|---|---|---|
| 1.1 | Solution + 7 proje iskeleti, .NET 10 hedefli | `dotnet build` temiz |
| 1.2 | `AppPaths` → `AppContext.BaseDirectory` (jpackage numarasına gerek yok, .NET'te bu hazır) | Paketlenmiş ve geliştirme modunda aynı kökü buluyor |
| 1.3 | Veri modelleri: `IdData`, `CardData`, `VerificationResult` | — |
| 1.4 | `MrzReader.parse` + check digit mantığı → C# | Java'daki aynı MRZ metinleri aynı sonucu veriyor (birim test) |

**Faz 1 çıktısı:** Donanımsız, saf mantık testleri yeşil.

---

## Faz 2 — Çip doğrulama (ChipVerifier)

> **Neden bu kadar erken:** Fixture'lar sayesinde **kart olmadan tamamen doğrulanabilir**. Ayrıca .NET karşılığı en yakın olan parça — BouncyCastle.NET, Java BC ile neredeyse aynı API'ye sahip. Yani en yüksek getirili, en düşük riskli faz. Momentum buradan gelir.

| # | İş | Bitti sayılma kriteri |
|---|---|---|
| 2.1 | `BouncyCastle.Cryptography` NuGet, SOD dış sarmalını (APPLICATION 23) soyma | Fixture SOD'undan CMS çıkıyor |
| 2.2 | DG hash karşılaştırması (PA kontrol 1) | Java ile aynı sonuç |
| 2.3 | SOD imza doğrulaması, CMS signed attributes (PA kontrol 2) | Java ile aynı sonuç |
| 2.4 | `certs/` köklerine zincir doğrulama (PA kontrol 3) | Aynı kök CN'e bağlanıyor |
| 2.5 | Active Authentication doğrulaması — ISO 9796-2 ve ECDSA-Plain | Fixture challenge/response ile geçiyor |

**Faz 2 çıktısı:** Doğrulama motoru bitti, kart olmadan kanıtlandı.

---

## Faz 3 — Çip okuma (IdCardReader) — **en riskli faz**

JMRTD'nin .NET karşılığı yok. Onun bizim için yaptığı işi elle yazacağız. Bu yüzden faz küçük ve tek tek doğrulanabilir adımlara bölünüyor — hepsi bitmeden hiçbiri "çalışıyor" sayılmaz.

| # | İş | Bitti sayılma kriteri |
|---|---|---|
| 3.0 | **Keşif:** NuGet'te ICAO 9303 / eMRTD kütüphanesi var mı? Bulunursa 3.2–3.4 kısalır | Yazılı karar notu — bulundu/bulunmadı, gerekçesiyle |
| 3.1 | PC/SC bağlantısı (`PCSC` NuGet), kart algılama, ATR okuma | Kart takılınca görülüyor |
| 3.2 | BAC anahtar türetme (MRZ → seed → 3DES anahtarları) | **Ara test:** fixture'daki MRZ, Java'nın ürettiği anahtarların aynısını veriyor |
| 3.3 | Secure Messaging — APDU sarma/açma, SSC sayacı | Fixture APDU trafiği birebir eşleşiyor |
| 3.4 | DG TLV/ASN.1 çözümleme: DG1, DG11, DG12, DG15 | Fixture ham byte'ları → `expected.json` ile birebir aynı alanlar |
| 3.5 | DG2 yüz görüntüsü — CBEFF sarmalı + **JPEG2000 çözme** | Fixture DG2'den Java ile aynı görüntü çıkıyor |
| 3.6 | Uçtan uca: gerçek kart tak → tüm alanlar + doğrulama | Java sürümüyle yan yana aynı sonuç |

**Bilinen risk — JPEG2000 (3.5):** Java `jai-imageio-jpeg2000` kullanıyor. .NET'te JPEG2000 desteği zayıf; SkiaSharp bunu çözmez. Aday çözüm `CSJ2K` (JJ2000'in C# portu), ama doğrulanması gerekiyor. Bu tıkanırsa fotoğraf gelmez — kart basılamaz. **3.5'e erken bakılacak, faz sonuna bırakılmayacak.**

---

## Faz 4 — Native cihazlar (P/Invoke)

Bu faz aslında *kolaylaşıyor*: JNA'nın interface + `Structure` numaralarına .NET'te gerek yok, `[DllImport]` ve `[StructLayout]` doğrudan karşılığı.

| # | İş | Bitti sayılma kriteri |
|---|---|---|
| 4.1 | `EvolisSDK` → P/Invoke, struct'lar `[StructLayout(LayoutKind.Sequential)]` | `Marshal.SizeOf` değerleri `fixtures/struct-sizes.txt` ile aynı |
| 4.2 | `EvolisFlags` sabitleri → C# | — |
| 4.3 | `EvolisPrinter` mantığı (bağlan, durum, ribon, temizlik, baskı, duplex) | Yazıcı bilgileri Java ile aynı okunuyor |
| 4.4 | `IDSIF` → P/Invoke, `ScannerBridge` mantığı | Tarama BMP'leri ve MRZ metni Java ile aynı |
| 4.5 | Kuru baskı (`print_to_file`) ile PRN üret | Java'nın ürettiği PRN ile karşılaştırılıyor — **kart harcamadan** |

**Bilinen tuzak (4.1):** JNA'da `boolean` varsayılan olarak 4 byte, C# `bool` ise DllImport'ta Win32 BOOL (4 byte) — ama `[StructLayout]` içinde `MarshalAs` verilmezse 1 byte olabilir. `PrinterInfo` struct'ında 13 tane `boolean` var; biri kayarsa tüm alanlar bozulur ve **sessizce yanlış veri** okunur. 4.1'in bitti kriteri bu yüzden `Marshal.SizeOf` karşılaştırması.

---

## Faz 5 — Kart görseli üretimi (CardRenderer)

675 satır piksel hassas çizim. `System.Drawing.Common` seçiliyor — `Graphics2D` ile kavramları birebir örtüşüyor (ikisi de GDI+ soyundan). Windows'a bağlı ama zaten native DLL'ler yüzünden Windows'tayız.

| # | İş | Bitti sayılma kriteri |
|---|---|---|
| 5.1 | mm→px dönüşümü, tarih formatlama, ölçü sabitleri | Birim test |
| 5.2 | Ön yüz: alanlar, logolar, fotoğraf yerleşimi | `fixtures/render/on.png` ile piksel farkı toleransta |
| 5.3 | Arka yüz: desen, ulaşım ikonları, madde ikonları | `fixtures/render/arka.png` ile toleransta |
| 5.4 | Kalibrasyon kartları, 180° döndürme, duplex hazırlığı | Baseline'larla toleransta |

**Bilinen risk:** Java ve .NET'in font ölçüleri ve kenar yumuşatması birebir aynı değil. Piksel piksel eşitlik beklenmiyor; **tolerans eşiği** belirlenecek ve fark haritası görsel olarak gözden geçirilecek. Metin taşması / yanlış hizalama olursa burada yakalanır — yazıcıda değil.

---

## Faz 6 — Arayüz (MainUI → WinForms)

En son, çünkü altındaki her şey çalışır durumda olmalı. Swing'in `JPanel`/`BorderLayout`/`JButton` yapısı WinForms'ta doğrudan karşılık buluyor.

| # | İş |
|---|---|
| 6.1 | Ana pencere, başlık, okuyucu paneli, yazıcı paneli, log paneli |
| 6.2 | Cihaz bağlama, kart yoklama döngüsü, durum etiketleri |
| 6.3 | Tara-ve-oku akışı, alan gösterimi, doğrulama sonucu gösterimi |
| 6.4 | Yazıcı bilgisi, hata temizleme, önizleme, baskı akışları |
| 6.5 | Swing'in `SwingUtilities.invokeLater` yerine WinForms `Invoke` / `async` |

**Bitti sayılma kriteri:** Java sürümüyle .NET sürümü yan yana açılıyor, aynı kart takılıyor, ekrandaki her alan aynı.

---

## Faz 7 — Paketleme ve devir

| # | İş | Bitti sayılma kriteri |
|---|---|---|
| 7.1 | `jpackage` yerine `dotnet publish` — self-contained, tek dosya | .NET kurulu olmayan makinede çalışıyor |
| 7.2 | `native_x64/`, `native_evolis/`, `assets/`, `certs/` çıktıya kopyalanıyor | Temiz makinede DLL'ler bulunuyor |
| 7.3 | Kurulum dokümanı güncelle (libusb-win32 sürücü notu dahil) | — |
| 7.4 | Java sürümünü emekliye ayır | Ancak 0–6 arası tüm kriterler geçtikten sonra |

---

## Özet: neden bu sıra

```
Faz 0  Emniyet ağı      ← kart eldeyken referans veri topla
  ↓
Faz 1  İskelet          ← donanımsız, hızlı
  ↓
Faz 2  Doğrulama        ← kartsız tam doğrulanabilir, en kolay kazanç
  ↓
Faz 3  Çip okuma        ← EN RİSKLİ, fixture'lar olmadan yapılamaz
  ↓
Faz 4  Native cihazlar  ← mekanik, P/Invoke Java'dan kolay
  ↓
Faz 5  Kart görseli     ← baseline karşılaştırmalı
  ↓
Faz 6  Arayüz           ← altı sağlamken en son
  ↓
Faz 7  Paketleme
```

Faz 3 en riskli olan ama Faz 0 ve 2'den önce başlanamaz — çünkü doğrulayacak referansı onlar üretiyor.

---

## Bu planın kapsamadıkları

Bilinçli olarak dışarıda bırakılanlar, istenirse ayrıca konuşulur:

- **Doğrulamayı zorunlu moda alma.** PA/AA şu an bilgilendirici — sahte kartı tespit ediyor, reddetmiyor. Bu bir ürün kararı, geçiş işi değil.
- **`App.java`, `PrintCard.java`, `PrinterTest.java`** — komut satırı test araçları. Taşınmayacak; karşılığı .NET'te test projesi olacak.
- **Yeni özellik.** Geçiş sırasında davranış değişmeyecek. Java'da olan hata varsa .NET'te de olacak; ayıklama ayrı iş.
