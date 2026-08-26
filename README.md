# id-scanner

T.C. kimlik kartını optik olarak tarayan, MRZ'den BAC anahtarlarını çıkarıp
temassız çipi okuyan, çip verisinin gerçekliğini doğrulayan ve okunan bilgilerle
[Evolis KC Prime](https://www.evolis.com/solutions/card-printers/kc-essential-kc-prime-kc-max-kiosk-card-printers/)
kart yazıcısına baskı hazırlayan .NET tabanlı uygulama.

Ana uygulama .NET 10 / WinForms sürümüdür. Eski Java/Maven sürümü repoda
referans ve geri dönüş noktası olarak durur; günlük kullanım için
`run-dotnet.bat` kullanılmalıdır.

```text
Kart yerleştir
  -> IDSIF.dll ile 300 DPI ön/arka tarama + MRZ OCR
  -> kart NFC pozisyonuna alınır
  -> MRZ'den BAC anahtarları türetilir
  -> PC/SC üzerinden DG1, DG2, DG11, DG12, DG15 ve EF.SOD okunur
  -> Passive Authentication + Active Authentication raporlanır
  -> kart görseli veya hazır kart üst baskısı üretilir
  -> evolis.dll ile prova PRN ya da gerçek baskı alınır
```

## Gereksinimler

| Bileşen | Sürüm / Not |
|---|---|
| İşletim sistemi | Windows x64 |
| .NET SDK | .NET 10 SDK (`Microsoft.DotNet.SDK.10`) |
| Okuyucu donanımı | [CRT-603-7005](https://www.china-creator.com/card-scanner/double-sided-card-scanner-crt-603-7005.html), USB VID `0483`, PID `5710` |
| Okuyucu USB sürücüsü | `libusb-win32` / `libusb0.sys` |
| Okuyucu native dosyaları | `native_x64/` |
| Model dosyaları | `native_x64/depends/f/` |
| Çip erişimi | PC/SC akıllı kart okuyucu servisi |
| Yazıcı donanımı | Evolis KC Prime, USB VID `0f49`, PID `0b91` |
| Yazıcı sürücüsü | Evolis Premium Suite |
| Evolis SDK | `native_evolis/evolis.dll` |
| Sertifikalar | `certs/` altındaki CSCA kök sertifikaları |

`native_x64/`, `native_evolis/`, `assets/` ve `certs/` çalışma zamanı
varlıkları .NET uygulamasının çıktı klasörüne otomatik kopyalanır. Bu klasörler
eksikse uygulama açılabilir ama tarama, baskı veya sertifika zinciri doğrulaması
çalışmaz.

## Kurulum

### 1. .NET 10 SDK

Windows'ta:

```powershell
winget install --id Microsoft.DotNet.SDK.10
```

Kurulumdan sonra açık terminali veya Visual Studio/VS Code penceresini kapatıp
yeniden açın.

### 2. Okuyucu sürücüsü

CRT okuyucu, üretici SDK'sındaki `libusb0_x64.dll` ile konuşur. Bu yüzden
Windows tarafında cihazın `libusb-win32` sürücüsüne bağlanması gerekir.

1. Zadig'i yönetici olarak açın.
2. `Options -> List All Devices` seçeneğini işaretleyin.
3. Listeden `USB CDC in HS mode` cihazını seçin.
4. USB ID'nin `0483 5710` olduğunu doğrulayın.
5. Sürücü olarak `libusb-win32` seçin.
6. `Install Driver` veya `Replace Driver` deyip USB kablosunu çıkarıp takın.

`WinUSB` veya `libusbK` seçilirse cihaz Windows'ta sağlıklı görünebilir ama
IDSIF SDK cihazı bulamaz; tipik belirti `OpenDev failed: -3` / `ERR_NODEVICE`.

Doğrulama:

```powershell
Get-WmiObject Win32_PnPEntity |
  Where-Object { $_.DeviceID -match "VID_0483&PID_5710" } |
  Select-Object Name, ConfigManagerErrorCode, Service
```

Beklenen: `ConfigManagerErrorCode: 0`, `Service: libusb0`.

### 3. Yazıcı sürücüsü

Evolis KC Prime için Evolis Premium Suite kurulmalıdır. Uygulama önce yazıcıya
`DIRECT` modda bağlanmayı dener; olmazsa `SUPERVISED` mod denenir.

## Kullanım

Ana uygulamayı derleyip çalıştırmak için:

```powershell
run-dotnet.bat
```

Sadece derlemek için:

```powershell
run-dotnet.bat build
```

Testleri çalıştırmak için:

```powershell
run-dotnet.bat test
```

Uygulama açıldığında okuyucu ve yazıcı otomatik aranır. Cihazlar sonradan
takılırsa `Cihazları Yeniden Bağla` düğmesiyle bağlantı yenilenir.

## Arayüz

### Kart okuyucu

Okuyucu paneli kart durumunu 800 ms aralıkla yoklar. Kart cihazdayken
`KARTI TARA` akışı şu işleri tek seferde yapar:

- Ön/arka yüz taraması ve MRZ OCR
- MRZ ayrıştırma ve BAC anahtarı üretimi
- Kartın NFC pozisyonuna taşınması
- Temassız çipten veri gruplarının okunması
- Çip doğrulama sonucunun loglanması
- Kimlik alanları ve biyometrik fotoğrafın ekrana basılması

DG11 okunabildiğinde ad/soyad bilgisi çipten gelir; böylece MRZ'nin ASCII
kısıtı yüzünden kaybolan Türkçe karakterler doğru gösterilir.

### Kart yazıcı

Yazıcı panelinde model, seri no, firmware, ribon bilgisi, sayaçlar, besleyici
durumu ve açık hata/uyarı bayrakları gösterilir.

Kullanılan işlemler:

| Düğme | İşlev |
|---|---|
| `Bilgileri Yenile` | Yazıcı durumunu, ribon ve sayaç bilgisini tekrar okur |
| `Hatayı Temizle` | Evolis mekanik hata bayrağını temizler |
| `Ön Yüz Önizle` | Okunan veriyle ön yüz PNG önizlemesi üretir |
| `Ön + Arka Önizle` | Ön ve arka kart tasarımını birlikte gösterir |
| `Prova Bas (kart harcamaz)` | Baskı işini PRN dosyasına yazar |
| `KART BAS (boş karta, çift yüz)` | Boş karta çift yüz gerçek baskı yapar |
| `Hazır Kart Önizle` | Matbaadan gelen hazır kart üstüne basılacak katmanı gösterir |
| `HAZIR KARTA BAS (tek yüz)` | Hazır basılı karta yalnızca fotoğraf ve ad/soyad basar |
| `Kalibrasyon Kartı Bas` | Yarım panel ribon bandının konumunu ölçmek için kart basar |

Gerçek baskıdan önce onay diyaloğu çıkar. Yazıcıda aktif `ERR_` bayrağı varsa
baskı reddedilir; önce hata giderilip `Hatayı Temizle` çalıştırılmalıdır.

## Hazır Kart Üst Baskısı

Mevcut üretim akışında matbaada basılmış hazır kartın üzerine yalnızca
kişiselleştirme katmanı basılabilir. Bu katman:

- Fotoğrafı basar
- Ad ve soyadı basar
- Geri kalan alanları beyaz bırakır

Beyaz alanlara mürekkep basılmadığı için matbaa baskısı korunur. Yerleşim
ölçüleri `overlay-layout.txt` dosyasından okunur. Dosya yoksa uygulama çıktı
klasöründe varsayılan şablonu oluşturur.

Ölçüler milimetredir. Kartın sol üst köşesi `(0,0)` kabul edilir; X sağa, Y
aşağı artar. Değişiklikten sonra uygulamayı yeniden başlatın.

Varsayılan ölçüler:

```text
photo_x   = 4.0
photo_y   = 51.0
photo_w   = 14.5
photo_h   = 15.5
name_x    = 32.0
name_y    = 61.0
surname_x = 32.0
surname_y = 65.5
text_size = 2.7
```

Hazır kart baskısı için kartı düz takın: kırmızı baskılı yüz yukarı, otobüs
görsellerinin olduğu uç önde. Besleme yönü zorunlu olarak değişirse arayüzdeki
`180° döndür` seçeneği kullanılabilir.

## Çip Doğrulama

.NET sürümünde doğrulama iki aşamalıdır ve tamamen çevrimdışı çalışır.

### Passive Authentication

`IdScanner.Crypto` içindeki `PassiveAuthVerifier` şu kontrolleri yapar:

- EF.SOD okunur ve LDS Security Object ayrıştırılır
- Okunan her DG'nin hash'i SOD içindeki hash ile karşılaştırılır
- SOD CMS imzası Document Signer sertifikasıyla doğrulanır
- Document Signer sertifikası `certs/` altındaki CSCA köklerine zincirlenir

Yüklü kökler:

- `certs/CSCA_TR.crt`
- `certs/CSCA_TR_v2.crt`
- `certs/SBMK_KOK.crt`

### Active Authentication

DG15 varsa uygulama çipe 8 bayt rastgele challenge gönderir. Çipin döndürdüğü
imza DG15'teki public key ile doğrulanır. RSA / ISO9796-2 ve ECDSA-Plain
kombinasyonları desteklenir.

Doğrulama sonucu şu an bilgilendirici moddadır: başarısızlık loglanır ama okuma
ve baskı akışı otomatik durdurulmaz. Üretim politikasında "doğrulama geçmezse
reddet" istenirse karar noktası `CardIssuanceService.LogVerificationSummary`
etrafında sıkılaştırılmalıdır.

## Çıktılar

.NET uygulaması dosyaları `AppContext.BaseDirectory` altına yazar. Geliştirme
modunda bu genelde:

```text
dotnet/src/IdScanner.App/bin/Debug/net10.0-windows/
```

Önemli klasörler:

| Klasör | İçerik |
|---|---|
| `output/` | Fotoğraf PNG'si, baskı BMP'leri ve prova PRN dosyaları |
| `scan_output/` | IDSIF tarama çıktı yolu; SDK bazı görüntüleri kendi sabit yollarına yazabilir |
| `logs/` | `idscanner-yyyyMMdd.log` uygulama logları |
| `diag/yyyyMMdd-HHmmss/` | Ham DG/SOD baytları ve APDU izi |

`diag/` gerçek kişisel veri içerir: ad, fotoğraf, T.C. no ve ham çip dosyaları.
Bu klasör depoya girmez ve dışarı paylaşılmadan önce temizlenmelidir.

## Mimari

```text
dotnet/
├── src/
│   ├── IdScanner.App       # WinForms arayüz ve composition root
│   ├── IdScanner.Workflow  # Tara -> oku -> doğrula -> çiz -> bas akışı
│   ├── IdScanner.Core      # Model, MRZ parser, yollar, log soyutlamaları
│   ├── IdScanner.Chip      # PC/SC, BAC, Secure Messaging, DG çözümleme
│   ├── IdScanner.Crypto    # Passive + Active Authentication
│   ├── IdScanner.Native    # IDSIF.dll ve evolis.dll P/Invoke katmanı
│   └── IdScanner.Render    # Kart görseli, JPEG/JPEG2000 fotoğraf çözme
└── tests/
    └── IdScanner.Tests     # MRZ, BAC, Secure Messaging, render ve struct testleri
```

Bağımlılık yönü bilinçli olarak içeri doğrudur:

```text
App -> Workflow -> Core
App -> Chip / Crypto / Native / Render
Chip / Crypto / Native / Render -> Workflow + Core
Core -> hiçbir proje veya NuGet paketi
```

Donanım arayüzleri `IdScanner.Workflow.Abstractions` içinde tanımlanır.
Somut uygulamalar ilgili infrastructure projelerinde durur. Böylece form
donanım sınıflarını doğrudan tanımaz; ileride kiosk UI veya fake donanım ile
test akışı bağlamak kolaylaşır.

## Testler

```powershell
run-dotnet.bat test
```

Test kapsamı:

- MRZ ayrıştırma, check digit ve OCR düzeltmeleri
- BAC anahtar türetme
- Secure Messaging APDU sarma/açma
- Bozuk MAC ve bozuk padding reddi
- Evolis ve IDSIF P/Invoke struct boyutları
- Evolis durum bayrağı eşlemesi
- JPEG ve JPEG2000 fotoğraf çözme
- Kart bitmap ölçüleri ve JavaRandom uyumluluğu

Donanım gerektiren uçtan uca tarama/baskı testleri otomatik testte yoktur;
sahada gerçek cihazla denenmelidir.

## Yayınlama

Repoda henüz .NET için ayrı bir paketleme script'i yok. Yayın almak gerekirse
şu komut kullanılabilir:

```powershell
"C:\Program Files\dotnet\dotnet.exe" publish `
  dotnet\src\IdScanner.App\IdScanner.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -o publish\KimlikKartSistemi
```

Yayın klasöründe `native_x64/`, `native_evolis/`, `assets/` ve `certs/`
klasörlerinin oluştuğunu kontrol edin. Hedef makinede yine okuyucu için
`libusb-win32`, yazıcı için Evolis Premium Suite gerekir.

`build-exe.bat` Java/jpackage sürümüne aittir; .NET yayınlama için
kullanılmamalıdır.

## Eski Java Sürümü

Java sürümü hâlâ repoda durur:

- `pom.xml`
- `src/main/java/com/mobiloby/`
- `run.bat`
- `build-exe.bat`

Geri dönüş veya davranış karşılaştırması gerektiğinde kullanılabilir. Yeni
geliştirme ve günlük kullanım .NET tarafında yapılmalıdır.

## DLL Hata Kodları ve Bayraklar

Bu bölüm sahada hızlı teşhis için tutulur. .NET tarafındaki karşılıklar:

- Evolis dönüş kodları: `dotnet/src/IdScanner.Native/Interop/EvolisNative.cs`
- Evolis durum bayrakları: `dotnet/src/IdScanner.Native/Interop/EvolisFlags.cs`
- IDSIF dönüş kodları: `dotnet/src/IdScanner.Native/Interop/IdsifReturnCode.cs`

### Yazıcı dönüş kodları (`evolis.dll`)

Kodlar ön eke göre gruplanır; ön ek hatanın hangi katmanda olduğunu söyler.

**Genel** (`0 ... -8`)

| Kod | Sabit | Anlamı |
|---|---|---|
| `0` | `OK` | Sorun yok |
| `-1` | `EUNDEFINED` | Tanımsız hata |
| `-2` | `EINTERNAL` | Kütüphane içi mantık hatası |
| `-3` | `ECANCELLED` | İşlem tamamlanmadan iptal edildi |
| `-4` | `EDISABLED` | İstenen özellik devre dışı |
| `-5` | `EUNSUPPORTED` | Kütüphane veya yazıcı bu özelliği desteklemiyor |
| `-6` | `EPARAMS` | API'ye geçersiz parametre verildi |
| `-7` | `ETIMEOUT` | Çağrı zaman aşımına uğradı |
| `-8` | `ENEEDACTION` | Yazıcı hazır değil; ribon, kapak ve hazneyi kontrol edin |

**Baskı** (`PRINT_`, `-20 ... -29`)

| Kod | Sabit | Anlamı |
|---|---|---|
| `-20` | `PRINT_EDATA` | Geçersiz girdi; görsel ve ayarları kontrol edin |
| `-21` | `PRINT_NEEDACTION` | Yazıcı basmaya hazır değil; ribon, kapak veya hazne |
| `-22` | `PRINT_EMECHANICAL` | Baskı sırasında mekanik hata |
| `-25` | `PRINT_EUNKNOWNRIBBON` | `GRibbonType` ayarı eksik |
| `-26` | `PRINT_ENOIMAGE` | Görsel verilmemiş |
| `-27` | `PRINT_WSETTING` | Sürücüden alınan ayarlardan biri okunamadı |
| `-28` | `PRINT_EJOB` | Baskı işi oluşturulmamış veya süresi dolmuş |
| `-29` | `PRINT_ESESSION` | Aktif oturum yok |

**Oturum** (`SESSION_`, `-10 ... -14`)

| Kod | Sabit | Anlamı |
|---|---|---|
| `-10` | `SESSION_ETIMEOUT` | Yazıcı rezervasyonu süresi doldu |
| `-11` | `SESSION_EBUSY` | Yazıcı kullanımda, başka oturum var |
| `-12` | `SESSION_DISABLED` | Oturum yönetimi kapalı |
| `-13` | `SESSION_FAILED` | Rezervasyon başarısız |

**Yazıcı iletişimi** (`PRINTER_`, `-60 ... -81`)

| Kod | Sabit | Anlamı |
|---|---|---|
| `-60` | `PRINTER_ENOCOM` | Yazıcı çevrimdışı |
| `-61` | `PRINTER_EREPLY` | Yazıcı yanıtı `ERR` içeriyor |
| `-64` | `PRINTER_NOSTATUS` | Yazıcıda durum bildirimi kapalı |
| `-65` | `PRINTER_EMODEL` | Geçersiz yazıcı modeli |
| `-80` | `PRINTER_NETWORK_ERROR` | Ağ işlemi hatası |

**Kart hareketi, çip ve manyetik alan**

| Kod | Sabit | Anlamı |
|---|---|---|
| `-200` | `CARD_ERROR` | Kart hareketi sırasında hata |
| `-201` | `CARD_EINSERTION` | Kart girişi sırasında hata |
| `-300` | `SMART_ERROR` | Akıllı kart işlemi hatası |
| `-301` | `SMART_ENOCOM` | PC/SC okuyucu ile iletişim kurulamadı |
| `-302` | `SMART_ENOCARD` | Okuma için kart yok |
| `-303` | `SMART_EBUSY` | Kart zaten bir PC/SC kodlayıcıya bağlı |
| `-50` | `MAG_ERROR` | Manyetik veri okuma/yazma hatası |
| `-51` | `MAG_EDATA` | Manyetik ize yazılacak veri geçersiz |
| `-52` | `MAG_EBLANK` | Manyetik iz boş |

Diğer gruplar: `LAM_` (`-40 ... -43`), `SYSTEM_` (`-500 ... -504`),
`USER_` (`-600 ... -604`), `SVC_` (`-10000 ... -10006`) ve `HTTP_`
(`-20000 ...`). Bu projede ana akışta kullanılmıyorlar.

### Yazıcı durum bayrakları (`evolis.dll`)

`evolis_status_is_on(status, id)` ile sorgulanır. Toplam 256 bayrak vardır;
bayrak ID'si dizideki indeksidir.

| Ön ek | Anlamı | Ne yapmalı |
|---|---|---|
| `CFG_` | Yapılandırma; hangi donanım özellikleri var | Bilgi amaçlı |
| `INF_` | Bilgi; anlık durum | Genelde aksiyon gerekmez |
| `WAR_` | Uyarı; çalışmaya devam eder ama dikkat ister | Duruma göre kontrol edin |
| `ERR_` | Hata; yazıcı iş kabul etmez | Giderilmeli, sonra temizlenmeli |
| `RSV_` | Rezerve | Yok sayın |

Sık karşılaşılan bayraklar:

| ID | Bayrak | Anlamı |
|---|---|---|
| `184` | `ERR_MECHANICAL` | Kart veya ribon sıkışması; `ClearMechanicalErrors()` ile temizlenir |
| `185` | `ERR_REJECT_BOX_FULL` | Ret kutusu dolu |
| `203` | `ERR_HARDWARE` | Donanım arızası |
| `95` | `INF_FEEDER_NEAR_EMPTY` | Kart haznesi boşalmak üzere |
| `54` | `INF_CARD_FEEDER` | Hazne mevcut |
| `-` | `ERR_FEEDER_EMPTY` | Hazne boş |
| `-` | `ERR_COVER_OPEN` | Kapak açık |
| `-` | `ERR_RIBBON_ENDED` | Ribon bitti |
| `-` | `WAR_COOLING` | Baskı kafası soğuyor |
| `-` | `WAR_NO_RIBBON` | Ribon takılı değil |

Tam liste `EvolisFlags.Names` içinde tutulur. Arayüz yalnızca raporlanabilir
olanları gösterir; `CFG_` ve `RSV_` gibi gürültü üreten bayraklar filtrelenir.

### Okuyucu dönüş kodları (`IDSIF.dll`)

Kaynak: `native_x64/crtIDS.h`.

| Kod | Sabit | Anlamı |
|---|---|---|
| `1` | `OK_Back` | Başarılı; arka yüzden MRZ okundu |
| `0` | `OK` | Başarılı |
| `-1` | `ERR_NOOPEN` | Cihaz açılmamış (`OpenDev` çağrılmadı) |
| `-2` | `ERR_ALREADYOPEN` | Cihaz zaten açık |
| `-3` | `ERR_NODEVICE` | Cihaz bulunamadı; USB/sürücü kontrol edin |
| `-4` | `ERR_OPEN` | Cihaz açılamadı |
| `-5` | `ERR_COMMAND` | Komut çalıştırma hatası |
| `-6` | `ERR_RECVTIMEOUT` | Veri alma zaman aşımı |
| `-7` | `ERR_RECVFAILD` | Veri alma başarısız |
| `-8` | `ERR_SENDFAILD` | Veri gönderme başarısız |
| `-9` | `COMM_ERR` | İletişim hatası |
| `-11` | `ERR_SCAN_GETCARD` | Tarama sırasında kart durumu alınamadı |
| `-12` | `ERR_SCAN_CARD_IN_GATE` | Kart girişte; taramayı tekrarlayın |
| `-13` | `ERR_SCAN_NOCARD` | Tarama sırasında kart yok |
| `-14` | `ERR_IMG_INVALID` | Görüntü dosyası geçersiz |
| `-18` | `ERR_MRZOCR` | MRZ OCR hatası |
| `-19` | `PARAM` | Geçersiz parametre |
| `-20` | `ERR_NOCARD` | Kart yok |
| `-22` | `ERR_MRZ` | MRZ hatası |
| `-23` | `ERR_MRZ_DGKEY` | MRZ'den BAC anahtarı türetilemedi |
| `-25` | `ERR_READ` | Okuma başarısız |
| `-27` | `ERR_MEDIA` | Medya hatası |
| `-98` | `ERR` | Genel hata |
| `-99` | `ERR_UNAUTHORIZED` | Yetkisiz |
| `-106` | `ERR_JAM` | Kart sıkıştı |
| `-110` | `ERR_UNSUPPORTED` | Desteklenmeyen işlev |
| `-111 ... -115` | `ERR_SIDE/MODE/TYPE/DPI/DELAY` | Desteklenmeyen tarama parametresi |
| `-116` | `ERR_RF` | RF/NFC okuma başarısız |
| `-160 ... -162` | `ERR_CISDATA` / `SetCISParam` / `LoadDllFail` | Tarayıcı sensörü veya DLL yükleme hatası |

## Bilinen Operasyonel Notlar

- Yarım panel `Color Half YMCKO` ribonda renkli bant kartın tamamını kaplamaz.
  Fotoğraf bandın içinde tutulur; diğer metinler saf siyah basılır.
- `ShortPanelShift` değerinin bandı pratikte oynatmadığı gözlendi. Bu yüzden
  tasarım bandın ölçülen konumuna göre ayarlanmıştır.
- Evolis mekanik hata bayrağı yapışkandır. Kart/ribon sıkışması giderildikten
  sonra `Hatayı Temizle` çalıştırılmalıdır.
- `diag/` çıktıları gerçek kişisel veri içerir; Git'e eklenmemeli ve
  paylaşılmamalıdır.
