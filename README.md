# id-scanner

T.C. kimlik kartından veri okuyup, okunan bilgilerle kişiselleştirilmiş kart basan uçtan uca sistem.

Pipeline üç aşamalı:

1. **Optik tarama** — `IDSIF.dll` (JNA üzerinden) kartın ön/arka yüzünü 300 DPI tarar ve dahili OCR ile MRZ (Machine Readable Zone) metnini çıkarır.
2. **Çip okuma** — MRZ'den türetilen BAC anahtarıyla NFC üzerinden çipe bağlanılır, JMRTD ile veri grupları (DG) okunur.
3. **Kart baskısı** — okunan veri Java2D ile karta çizilir, `evolis.dll` (JNA) üzerinden Evolis KC Prime yazıcıya gönderilir.

```
OKUMA:  Kart yerleştir → ScanMRZ (tara + OCR) → Move(EJECT_HALF) → BAC → DG'leri oku → output/
BASKI:  output/ verisi → CardRenderer (Java2D) → BMP → evolis.dll → KC Prime
```

## Gereksinimler

| Bileşen | Sürüm / Not |
|---|---|
| JDK | 17+ (test edilen: Temurin 17.0.19) |
| Maven | 3.9.9 — repoda yok, `mvnw.cmd` indirir |
| Okuyucu donanımı | CRT-603-7005 (USB VID `0483`, PID `5710`) |
| Okuyucu USB sürücü | **libusb-win32 v1.4.0.0** — aşağıya bakın, kritik |
| Model dosyaları | `native_x64/depends/` — repoda yok, üretici paketinden gelir |
| Yazıcı donanımı | Evolis KC Prime (USB VID `0f49`, PID `0b91`) |
| Yazıcı yazılımı | Evolis Premium Suite (sürücü için) |
| Evolis SDK | `native_evolis/evolis.dll` — repoda mevcut |

> **Not:** `native_x64/depends/` ve okuyucunun USB sürücüsü repoda bulunmaz. Temiz bir makinede kurulum yapıyorsanız "Kurulum" bölümündeki 3. ve 4. adımlar zorunludur, atlanırsa uygulama çalışmaz.

## Kurulum

### 1. JDK 17

```powershell
winget install --id EclipseAdoptium.Temurin.17.JDK -e
```

Kurulum sonrası **açık olan tüm terminalleri / VS Code'u kapatıp yeniden açın** — Windows, zaten çalışan işlemlerin `PATH`/`JAVA_HOME` değişkenlerini güncellemez.

### 2. Maven bootstrap

`run.bat` doğrudan `.mvn\wrapper\apache-maven\bin\mvn.cmd` arar ama onu indirmez. Temiz kurulumda **önce bir kez** şunu çalıştırın:

```powershell
.\mvnw.cmd -v
```

Bu, Maven 3.9.9'u indirip `.mvn\wrapper\apache-maven\` altına açar. Sonrasında `run.bat` çalışır.

### 3. Model dosyaları (`native_x64/depends/`)

Üreticinin SDK paketindeki `x64/depends/` klasörünü `native_x64/depends/` olarak kopyalayın. İçermesi gerekenler:

```
native_x64/depends/f/
├── deploy.prototxt
├── res10_300x300_ssd_iter_140000_fp16.caffemodel
├── haarcascade_frontalface_alt.xml
├── haarcascade_frontalface_alt2.xml
├── haarcascade_eye.xml
└── haarcascade_mcs_mouth.xml
```

Eksikse log şunu verir ve yüz tespiti devre dışı kalır:

```
[E] Init failed: ... Can't open "...depends\f\deploy.prototxt"
[E] Models not initialized
```

Doğru kurulduğunda: `[I] Models initialized successfully`

### 4. USB sürücü — libusb-win32

Bu adım en kritik olanı. Cihaz sürücüsüz haldeyken Aygıt Yöneticisi'nde sarı ünlemle ve `ConfigManagerErrorCode: 28` ile görünür.

1. [Zadig](https://zadig.akeo.ie/) indirin ve çalıştırın (yönetici izni ister)
2. `Options` → `List All Devices` işaretleyin
3. Listeden **`USB CDC in HS mode`** cihazını seçin
4. **USB ID'nin `0483 5710` olduğunu doğrulayın** — yanlış cihaza sürücü bağlamak başka donanımı devre dışı bırakabilir
5. Sağdaki sürücü kutusundan **`libusb-win32`** seçin
6. `Install Driver` / `Replace Driver` → USB kablosunu çıkarıp takın

> ⚠️ **WinUSB veya libusbK seçmeyin.** SDK `libusb0_x64.dll` (libusb-win32) kullanır ve yalnızca `libusb0.sys` ile konuşabilir. WinUSB ile cihaz "OK" görünür ama SDK onu bulamaz (`num=0`, `OpenDev failed: -3`).

Doğrulama:

```powershell
Get-WmiObject Win32_PnPEntity | Where-Object { $_.DeviceID -match "VID_0483&PID_5710" } |
    Select-Object Name, ConfigManagerErrorCode, Service
```

Beklenen: `ConfigManagerErrorCode: 0`, `Service: libusb0`

## Kullanım

### Grafik arayüz (önerilen)

```powershell
run.bat ui
```

Tek pencereden hem okuyucu hem yazıcı yönetilir. Konsol komutları hâlâ çalışır ama günlük kullanım için arayüz daha pratik.

**Okuyucu paneli:** Kart varlığı 800 ms'de bir yoklanır ve durum satırında gösterilir ("Cihazda kart yok" / "Kart cihazda — TARA'ya basabilirsiniz"). `KARTI TARA` tek düğmede tarama + OCR + BAC + çip okuma yapar; alanlar ve biyometrik fotoğraf dolar. İsimler DG11'den okunduğu için **Türkçe karakterler doğru gelir** (`İMAMOĞLU`, `ÖDEMİŞ`).

**Yazıcı paneli:** Model, seri no, firmware, baskı kafası kiti, ribon tipi/kapasitesi/kalanı, toplam basılan kart, temizliğe kalan kart ve açık durum bayrakları. Düğmeler: `Bilgileri Yenile`, `Hatayı Temizle`, `Kart Önizleme Üret`, `Prova Bas`, `KART BAS`.

**Güvenlik davranışları:**
- Kimlik okutulmadan `KART BAS`'a basılırsa uyarı verir, kart harcamaz
- Gerçek baskı öncesi onay diyaloğu çıkar
- Yazıcıda hata bayrağı varsa baskı reddedilir (önce `Hatayı Temizle`)
- Baskı görseli her seferinde yeniden üretilir — diskte kalan eski dosya basılmaz

Tüm donanım çağrıları `SwingWorker` içinde arka planda çalışır, arayüz donmaz. Cihazlar sonradan takılırsa `Cihazları Yeniden Bağla` ile yeniden aranır.

### Mimari

```
MainUI ──> IdCardReader ──> IDSIF.dll (tarama/OCR) + JMRTD (BAC, DG okuma)
       └─> EvolisPrinter ──> evolis.dll (durum, ribon, sayaç, baskı)
           CardRenderer  ──> Java2D ile kart görseli
```

`IdCardReader`, `App.java`'daki mantığın konsoldan bağımsız hâli — hiçbir yerde `stdin` beklemez, ilerlemeyi callback ile bildirir.

### Bağımsız uygulama (exe)

```powershell
build-exe.bat
```

`dist\KimlikKartSistemi\KimlikKartSistemi.exe` üretir — arayüzün, içinde gömülü JRE bulunan taşınabilir hâli (~250 MB). **Hedef makinede Java kurulu olmasına gerek yok**; klasörün tamamını kopyalamak yeterli.

Hedef makinede yine de gerekli olanlar:
- Okuyucu için **libusb-win32** sürücüsü (Zadig — kurulum adım 4)
- Yazıcı için **Evolis Premium Suite** (sürücü)

Paket `jpackage` ile üretilir (JDK 17 içinde gelir). WiX kurulu olmadığı için MSI değil `app-image` üretilir — kurulum gerektirmeyen taşınabilir klasör.

> **Yol çözümlemesi:** Native klasörler (`native_x64`, `native_evolis`) exe'nin **yanında** olmalı. [AppPaths.java](src/main/java/com/mobiloby/AppPaths.java) kök dizini `jpackage.app-path` sistem özelliğinden (exe'nin konumu) çözer; paketlenmemişse çalışma dizinine düşer. Bu olmadan exe'ye çift tıklandığında DLL'ler bulunamaz.

### Konsol komutları

Arayüz olmadan, tek tek adımları çalıştırmak için:

| Komut | Ne yapar |
|---|---|
| `run.bat` | Tam okuma akışı: tarama → OCR → BAC → DG'ler → `output/` |
| `run.bat app --mrz <belgeNo>,<doğumYYAAGG>,<sonGeçerlilikYYAAGG>` | Taramayı atlar, doğrudan çipi okur |
| `run.bat printer` | Yazıcı testi: cihaz, durum bayrakları, ribon, kart yolu |
| `run.bat printer --temizle` | Mekanik hata bayrağını temizler |
| `run.bat card` | Kart görselini üretir (`output/card_preview.png` + `card_print.bmp`) |
| `run.bat print` | **Prova** — baskı hattını çalıştırır, kart harcamaz |
| `run.bat print --onayla` | **Gerçek baskı** — kart harcar |

`run.bat` akışı: kartı cihaza koyup **Enter** (Q = iptal) → tarama + OCR (~2.5 sn) → kart NFC pozisyonuna taşınır → BAC ile çipe bağlanılır → veriler `output/` altına yazılır.

Türkçe karakterler için isimleri elle verin (MRZ sadece ASCII taşır):

```powershell
run.bat card --ad UMUT --soyad İMAMOĞLU
run.bat card --baslik "BAŞKENT KART" --altbaslik "ULAŞIM"
```

## Kart baskısı (Evolis KC Prime)

### Bağlantı mimarisi

`evolis.dll` doğrudan JNA ile çağrılır — okuyucudaki `IDSIF.dll` ile aynı desen. Üç bağlantı modu var (`OpenMode`):

| Mod | Anlamı |
|---|---|
| `DIRECT` | Yazıcıyla doğrudan iletişim — **kullandığımız mod** |
| `SUPERVISED` | Evolis Supervision Service üzerinden (Premium Suite gerekir) |
| `AUTO` | Otomatik seçer |

`DIRECT` çalıştığı doğrulandı, yani üretimde Premium Suite'e bağımlı değiliz. Suite yine de teşhis aracı olarak faydalı (Print Center ile yazıcıyı görmek, test kartı basmak).

> `evolis.dll`, PyPI'daki resmi `Evolis-SDK` paketinden (v9.4.1, Evolis SDK v3) çıkarıldı. JNA imzaları paketin kendi Python ctypes bağlayıcılarından birebir alındı — tahmin edilmedi.

### Yarım panel ribon kısıtı — önemli

Takılı ribon **Color Half YMCKO** (`R5H004NAA`, tip 3 = YMCKOS). Yarım panel ribonlarda renkli (YMC) paneller kartın yalnızca **~1/3'lük bir bandını** kaplar; K (siyah) ve O (koruyucu) tam boy. Sonuç:

- Fotoğraf **tek renkli öğe** olmalı ve kartın uzun ekseninde **~28 mm'yi aşmamalı** (kodda 26 mm)
- Diğer her şey **saf siyah** (0,0,0) olmalı ki K paneliyle bassın
- Tüm kartı kaplayan renkli zemin bu ribonla **mümkün değil** — onun için tam panel YMCKO gerekir

`GShortPanelManagement=AUTO` ayarıyla yazıcı renkli bölgeyi kendi bulup paneli oraya konumlandırır.

### Baskı ayarları

`PrintCard` şu ayarları veriyor (`evolis_print_set_setting`):

| Anahtar | Değer | Neden |
|---|---|---|
| `Orientation` (128) | `PORTRAIT` | Tasarım dikey — bitmap'i biz döndürmüyoruz |
| `GShortPanelManagement` (55) | `AUTO` | Yarım panel ribonun renkli bandını yazıcı konumlandırsın |

## Çıktılar

### `output/` — çipten okunan veriler

| Dosya | İçerik |
|---|---|
| `dg1_mrz.txt` | DG1 — MRZ metni (3 satır) |
| `dg2_face_1.img` / `.png` | DG2 — biyometrik yüz fotoğrafı (240×320) |
| `dg5_portre_1.jpg` | DG5 — basılı portre |
| `dg7_imza_1.*` | DG7 — imza (T.C. kimlik kartlarında **yok**) |

### `native_x64/Img/` — taranan görüntüler

`front.bmp` ve `back.bmp` (~1.75 MB, 914×637, 24-bit).

> ⚠️ Dosya adları sabittir; **her tarama bir öncekinin üzerine yazar.** Ayrıca `OpenDev`'e verilen `savePath` parametresi DLL tarafından yok sayılır — bu yüzden `scan_output/` klasörü boş kalır.

## Okunan veri grupları

T.C. kimlik kartında bulunan 6 DG (SOD'un `DG hash sayısı: 6` alanıyla doğrulanır):

| DG | İçerik | Durum |
|---|---|---|
| COM | LDS sürümü (1.7), DG listesi | ✅ |
| DG1 | MRZ — ad, soyad, belge no, uyruk, cinsiyet, tarihler | ✅ |
| DG2 | Biyometrik yüz fotoğrafı (JPEG 240×320) | ✅ |
| DG5 | Basılı portre | ✅ |
| DG11 | Kişisel no, tam ad, doğum tarihi/yeri | ✅ |
| DG12 | Veren kurum (İçişleri Bakanlığı) | ✅ |
| DG15 | Active Authentication public key (RSA) | ✅ |
| SOD | İmzalı hash listesi (SHA-256, 6 hash) | ✅ okunuyor, **doğrulanmıyor** |
| DG7 | İmza | ⛔ kartta yok (`6A82 FILE NOT FOUND`) |
| DG14 | Chip Authentication bilgisi | ⛔ kartta yok (`6A82 FILE NOT FOUND`) |

**DG7 ve DG14 için alınan `FILE NOT FOUND` bir hata değildir** — kartın "bende öyle bir dosya yok" yanıtıdır. SOD'un beyan ettiği 6 DG ile başarıyla okunan 6 DG birebir örtüşür.

## Sorun giderme

| Belirti | Neden | Çözüm |
|---|---|---|
| `JAVA_HOME environment variable is not defined correctly` | Terminal, JDK kurulumundan önce açılmış veya Maven indirilmemiş | Terminali yeniden açın; `mvnw.cmd -v` ile Maven'ı bootstrap edin |
| `OpenDev failed: -3` + `usb_init:-1` + `num=0` | USB sürücü yok veya yanlış (WinUSB) | Kurulum adım 4 — libusb-win32 bağlayın |
| `[E] Models not initialized` | `native_x64/depends/` eksik | Kurulum adım 3 |
| `Okuyucu bulunamadı!` | PC/SC okuyucu görünmüyor | Cihazın bağlı olduğundan ve `Smart Card` servisinin çalıştığından emin olun |
| `No line found` (NoSuchElementException) | Program stdin'den Enter bekliyor ama terminal etkileşimli değil | Etkileşimli bir terminalde çalıştırın |
| DG2 PNG'ye çevrilemiyor | JP2 (JPEG 2000) plugin eksik | `jai-imageio-jpeg2000` bağımlılığı `pom.xml`'de mevcut; JPEG çıkan kartlarda sorun olmaz |
| Yazıcı: `Bulunan cihaz sayısı: 0` | Yazıcı kapalı/takılı değil veya sürücü yok | Yazıcıyı açın; Evolis Premium Suite kurulu olduğundan emin olun |
| Yazıcı: `evolis_print_exect → -22` + `ERR_MECHANICAL` | Kart sıkışması / ribon sıkışması / hazne boş | Kartı ve ribonu kontrol edin, hazneye kart koyun; hata bayrağını temizlemek için kapağı açıp kapatın |
| Yazıcı: `-21 PRINT_NEEDACTION` | Yazıcı basmaya hazır değil | Ribon, kapak, hazne durumunu kontrol edin |
| `INF_FEEDER_NEAR_EMPTY` | Kart haznesi boşalmak üzere | Boş kart yükleyin |

## Hata kodu referansı

### Yazıcı — dönüş kodları (`evolis.dll`)

Kodlar ön eke göre gruplanır; ön ek hatanın hangi katmanda olduğunu söyler.

**Genel** (0 … −8)

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
| `-8` | `ENEEDACTION` | Yazıcı hazır değil — ribon, kapak, hazne kontrol edin |

**Baskı** (`PRINT_`, −20 … −29) — en sık karşılaşılanlar

| Kod | Sabit | Anlamı |
|---|---|---|
| `-20` | `PRINT_EDATA` | Geçersiz girdi — görsel ve ayarları kontrol edin |
| `-21` | `PRINT_NEEDACTION` | Yazıcı basmaya hazır değil — ribon/kapak/hazne |
| `-22` | `PRINT_EMECHANICAL` | **Baskı sırasında mekanik hata** (bizim karşılaştığımız) |
| `-25` | `PRINT_EUNKNOWNRIBBON` | `GRibbonType` ayarı eksik |
| `-26` | `PRINT_ENOIMAGE` | Görsel verilmemiş |
| `-27` | `PRINT_WSETTING` | Sürücüden alınan ayarlardan biri okunamadı |
| `-28` | `PRINT_EJOB` | Baskı işi oluşturulmamış veya süresi dolmuş |
| `-29` | `PRINT_ESESSION` | Aktif oturum yok |

**Oturum** (`SESSION_`, −10 … −14)

| Kod | Sabit | Anlamı |
|---|---|---|
| `-10` | `SESSION_ETIMEOUT` | Yazıcı rezervasyonu süresi doldu |
| `-11` | `SESSION_EBUSY` | Yazıcı kullanımda, başka oturum var |
| `-12` | `SESSION_DISABLED` | Oturum yönetimi kapalı |
| `-13` | `SESSION_FAILED` | Rezervasyon başarısız |

**Yazıcı iletişimi** (`PRINTER_`, −60 … −81)

| Kod | Sabit | Anlamı |
|---|---|---|
| `-60` | `PRINTER_ENOCOM` | Yazıcı çevrimdışı |
| `-61` | `PRINTER_EREPLY` | Yazıcı yanıtı "ERR" içeriyor |
| `-64` | `PRINTER_NOSTATUS` | Yazıcıda durum bildirimi kapalı |
| `-65` | `PRINTER_EMODEL` | Geçersiz yazıcı modeli |
| `-80` | `PRINTER_NETWORK_ERROR` | Ağ işlemi hatası |

**Kart hareketi** (`CARD_`) · **Çip** (`SMART_`) · **Manyetik** (`MAG_`)

| Kod | Sabit | Anlamı |
|---|---|---|
| `-200` | `CARD_ERROR` | Kart hareketi sırasında hata |
| `-201` | `CARD_EINSERTION` | Kart girişi sırasında hata |
| `-300` | `SMART_ERROR` | Akıllı kart işlemi hatası |
| `-301` | `SMART_ENOCOM` | PCSC okuyucu ile iletişim kurulamadı |
| `-302` | `SMART_ENOCARD` | Okuma/kodlama için kart yok |
| `-303` | `SMART_EBUSY` | Kart zaten bir PCSC kodlayıcıya bağlı |
| `-50` | `MAG_ERROR` | Manyetik veri okuma/yazma hatası |
| `-51` | `MAG_EDATA` | Manyetik ize yazılacak veri geçersiz |
| `-52` | `MAG_EBLANK` | Manyetik iz boş |

Diğer gruplar: `LAM_` (−40…−43, laminasyon modülü), `SYSTEM_` (−500…−504, işletim sistemi), `USER_` (−600…−604, kimlik doğrulama), `SVC_` (−10000…−10006, Evolis servisi), `HTTP_` (−20000…, ağ). Bunlar bu projede kullanılmıyor.

### Yazıcı — durum bayrakları

`evolis_status_is_on(status, id)` ile sorgulanır, 256 bayrak vardır. Ön ek türü belirler:

| Ön ek | Anlamı | Ne yapmalı |
|---|---|---|
| `CFG_` | Yapılandırma — hangi donanım özellikleri var | Bilgi amaçlı, aksiyon gerekmez |
| `INF_` | Bilgi — anlık durum | Genelde aksiyon gerekmez |
| `WAR_` | Uyarı — çalışmaya devam eder ama dikkat | Duruma göre |
| `ERR_` | **Hata — yazıcı iş kabul etmez** | Giderilmeli, sonra temizlenmeli |
| `RSV_` | Rezerve, kullanılmıyor | Yok sayın |

Sık karşılaşılanlar:

| ID | Bayrak | Anlamı |
|---|---|---|
| 184 | `ERR_MECHANICAL` | Kart veya ribon sıkışması — `evolis_clear_mechanical_errors` ile temizlenir |
| 185 | `ERR_REJECT_BOX_FULL` | Ret kutusu dolu |
| 203 | `ERR_HARDWARE` | Donanım arızası — desteğe başvurun |
| — | `ERR_FEEDER_EMPTY` | Hazne boş |
| — | `ERR_COVER_OPEN` | Kapak açık |
| — | `ERR_RIBBON_ENDED` | Ribon bitti |
| 95 | `INF_FEEDER_NEAR_EMPTY` | Hazne boşalmak üzere |
| 54 | `INF_CARD_FEEDER` | Hazne mevcut |
| — | `WAR_COOLING` | Baskı kafası soğuyor |
| — | `WAR_NO_RIBBON` | Ribon takılı değil |

Tam liste: [EvolisFlags.java](src/main/java/com/mobiloby/EvolisFlags.java) — dizi indeksi bayrak ID'sidir.

### Okuyucu — dönüş kodları (`IDSIF.dll`)

Kaynak: `native_x64/crtIDS.h`.

| Kod | Sabit | Anlamı |
|---|---|---|
| `1` | `OK_Back` | Başarılı — arka yüzden MRZ okundu |
| `0` | `OK` | Başarılı |
| `-1` | `ERR_NOOPEN` | Cihaz açılmamış (`OpenDev` çağrılmadı) |
| `-2` | `ERR_ALREADYOPEN` | Cihaz zaten açık |
| `-3` | `ERR_NODEVICE` | **Cihaz bulunamadı** — USB/sürücü kontrol edin |
| `-4` | `ERR_OPEN` | Cihaz açılamadı |
| `-5` | `ERR_COMMAND` | Komut çalıştırma hatası |
| `-6` | `ERR_RECVTIMEOUT` | Veri alma zaman aşımı |
| `-7` / `-8` | `ERR_RECVFAILD` / `ERR_SENDFAILD` | Veri alma / gönderme başarısız |
| `-9` | `COMM_ERR` | İletişim hatası |
| `-11` | `ERR_SCAN_GETCARD` | Tarama sırasında kart durumu alınamadı |
| `-12` | `ERR_SCAN_CARD_IN_GATE` | Kart girişte — taramayı tekrarlayın |
| `-13` | `ERR_SCAN_NOCARD` | Tarama sırasında kart yok |
| `-14` | `ERR_IMG_INVALID` | Görüntü dosyası geçersiz |
| `-18` | `ERR_MRZOCR` | **MRZ OCR hatası** |
| `-19` | `PARAM` | Geçersiz parametre |
| `-20` | `ERR_NOCARD` | Kart yok |
| `-22` | `ERR_MRZ` | **MRZ hatası** |
| `-23` | `ERR_MRZ_DGKEY` | MRZ'den BAC anahtarı türetilemedi |
| `-25` | `ERR_READ` | Okuma başarısız |
| `-27` | `ERR_MEDIA` | Medya hatası |
| `-98` | `ERR` | Genel hata |
| `-99` | `ERR_UNAUTHORIZED` | Yetkisiz |
| `-106` | `ERR_JAM` | **Kart sıkıştı** |
| `-110` | `ERR_UNSUPPORTED` | Desteklenmeyen işlev |
| `-111` … `-115` | `ERR_SIDE/MODE/TYPE/DPI/DELAY` | Desteklenmeyen tarama parametresi |
| `-116` | `ERR_RF` | **RF/NFC okuma başarısız** |
| `-160` … `-162` | `ERR_CISDATA` / `SetCISParam` / `LoadDllFail` | Tarayıcı sensörü / DLL yükleme |

## Bilinen eksikler

### 1. Kimlik doğrulama (authenticity) yapılmıyor — en önemlisi

Uygulama çipteki veriyi okuyup gösteriyor, ancak **gerçek olup olmadığını doğrulamıyor**:

- SOD okunuyor ama içindeki hash'ler okunan DG'lerle **karşılaştırılmıyor** → veri tahrifatı tespit edilemez
- SOD imzası CSCA / Document Signer sertifikasına karşı **doğrulanmıyor** → sahte çip tespit edilemez
- DG15'teki RSA public key okunuyor ama **Active Authentication challenge-response yapılmıyor** → klonlanmış çip tespit edilemez

Bir kimlik doğrulama ürünü hedefleniyorsa sahte veya klonlanmış bir kart bu kontrollerin tamamını sorunsuz geçer. Başlangıç noktaları: `SODFile.getDataGroupHashes()` ve `PassportService.doAA()`.

### 2. `run.bat scan` çalışmaz

`run.bat`, `com.mobiloby.ScanTest` sınıfını çağırır ama bu sınıf projede **mevcut değil** (`src/main/java/com/mobiloby/` altında yalnızca `App`, `IDSIF`, `MrzReader`, `ScannerBridge` var).

### 3. `--bmp` ve `--latest` seçenekleri işlevsiz

`App.java` bu iki argümanı ayrıştırıyor (satır 51-52) ama sonuçlarını **hiç kullanmıyor**; `--mrz` verilmediği sürece her zaman tarama yapılır. `findLatestBackBmp()` metodu da tanımlı ama hiç çağrılmıyor.

### 4. Tarayıcı kaynakları serbest bırakılmıyor

`App.java` yalnızca `ScannerBridge.scan()` çağırıyor; `ScannerBridge.eject()` ve `ScannerBridge.close()` **hiç çağrılmıyor**. Sonuç: program bitince kart yarı-çıkarılmış pozisyonda kalır ve DLL `Uninit()` edilmez.

### 5. `EF.CardSecurity` okunamıyor

```
ClassCastException: org.bouncycastle.asn1.DLApplicationSpecific cannot be cast to ASN1Sequence
```

BouncyCastle / JMRTD arasında ASN.1 ayrıştırma uyumsuzluğu. PACE için gerekli; BAC kullanıldığı sürece engelleyici değil.

### 6. Her baskıdan sonra `ERR_MECHANICAL` — çözülmedi, araştırılıyor

İki ayrı baskı denemesinde de aynı desen görüldü:

1. Baskı **başarıyla tamamlanıyor** — ribon sayacı düşüyor, kart üzerinde baskıyla çıkıyor
2. `evolis_print_exect` `-22` (`PRINT_EMECHANICAL`) dönüyor
3. `ERR_MECHANICAL` (bayrak ID 184) set oluyor

Hata baskı **sırasında** değil **sonrasında** geliyor — baskının kendisi sağlam.

**Elenen ihtimaller:**

| İhtimal | Nasıl elendi |
|---|---|
| Veri / yazılım hatası | `print_to_file` provası sorunsuz geçiyor |
| Kart bitmesi | `ERR_FEEDER_EMPTY` yanmıyor, yalnızca `INF_FEEDER_NEAR_EMPTY` (bilgi amaçlı) |
| Bezel zaman aşımı | Bezel davranışı `DONOTHING` — gecikme sonunda aksiyon tetiklemiyor |
| Baskının hiç olmaması | Ribon 406 → 404 düştü, kart basılı çıktı |

Evolis Print Center da ek bilgi vermiyor; o da yalnızca "Mekanik hata" diyor.

**Doğrulanmamış hipotez:** Yazıcı baskıdan sonra bir sonraki kartı önden yola almaya çalışıyor, hazne boş olduğu için besleme hareketi başarısız oluyor. Her iki denemede de haznede tek kart vardı — yani değişken hiç değişmedi. Dolu hazneyle test edilmeli. Doğrulanmazsa sıradaki adımlar: Print Center → Araçlar → "Hata ayıklama modu etkinleştirme" ve "Yazıcı düzenli temizlik sihirbazı".

**Bayrak yapışkandır — temizlenmeden yazıcı yeni iş kabul etmez.** SDK'nın ifadesi:

> *"Sometimes a mechanical error happens while printing. In this case, the printer will not accept any other job. Calling this method will help you reset the printer in a ready state."*

| Temizleme yolu | Nasıl |
|---|---|
| Arayüzden | `Hatayı Temizle` düğmesi |
| Konsoldan | `run.bat printer --temizle` |
| Kodla | `evolis_clear_mechanical_errors(context)` |
| Fiziksel | Yazıcının kapağını açıp kapatmak |

Her baskı denemesinden önce `run.bat printer` (veya arayüzde `Bilgileri Yenile`) ile durum kontrolü alışkanlık haline getirilmeli — `ERR_` ile başlayan bir bayrak varsa baskı reddedilir.

### 7. Türkçe isimler — arayüzde çözüldü, CLI'da duruyor

MRZ standardı yalnızca ASCII taşır — `İMAMOĞLU` yerine `IMAMOGLU` gelir. Doğru yazım çipte **DG11'de var**.

Arayüz (`run.bat ui`) DG11'i okuduğu için sorun yok. Ancak CLI tarafında `App.java` DG11'i ekrana yazıp dosyaya kaydetmiyor, bu yüzden `run.bat card` MRZ'ye düşüyor. Geçici çözüm: `run.bat card --ad ... --soyad ...`. Kalıcı çözüm: `App.java`'nın DG11'i `output/` altına kaydetmesi.

### 8. Kart yerleşimi kalibre edilmedi

`CardRenderer` içindeki mm cinsinden koordinatlar referans görselin oranlarından tahmin edildi. Gerçek baskı üzerinde ölçülüp düzeltilmesi gerekir.

### 9. Temassız çip kodlanamıyor

Yazıcının künyesi `hasContactLessEnc = false` diyor — temassız (RFID/MIFARE) kodlayıcı ünitesi bu cihazda takılı değil. Çipli kartın **üzerine baskı yapılabilir** ama **çipine veri yazılamaz**.

KC Prime bu modülü opsiyon olarak destekliyor, yani model kısıtı değil donanım eksikliği. Ulaşım kartı gibi çipin çalışması gereken bir üründe bu modül gerekecek.

## Proje yapısı

```
├── src/main/java/com/mobiloby/
│   ├── App.java              # Ana akış: tarama → BAC → DG okuma
│   ├── ScannerBridge.java    # IDSIF.dll sarmalayıcı (JNA)
│   ├── IDSIF.java            # Okuyucu DLL fonksiyon/struct tanımları
│   ├── MrzReader.java        # MRZ ayrıştırma
│   ├── EvolisSDK.java        # evolis.dll JNA arayüzü (yazıcı)
│   ├── EvolisFlags.java      # 256 durum bayrağı ismi (ID = dizi indeksi)
│   ├── PrinterTest.java      # Yazıcı bağlantı/durum/ribon testi
│   ├── CardRenderer.java     # Kart görselini üretir (Java2D) — baskı YAPMAZ
│   └── PrintCard.java        # Baskı — varsayılan prova, --onayla ile gerçek
├── native_x64/               # Okuyucu SDK (IDSIF.dll, OpenCV, OCR, libusb)
│   ├── depends/              # ⚠️ Repoda yok — üretici paketinden kopyalanmalı
│   └── Img/                  # Taranan BMP'ler (üzerine yazılır)
├── native_evolis/            # Yazıcı SDK (evolis.dll, Evolis SDK v3)
├── native/                   # Eski 32-bit okuyucu SDK (kullanılmıyor)
├── output/                   # Çipten okunan veriler + kart görselleri (gitignore)
├── config.ini                # Tarayıcı ayarları (VID/PID, DPI, görüntü işleme)
├── run.bat                   # Çalıştırma script'i
└── mvnw.cmd                  # Maven bootstrap
```

## Bağımlılıklar

| Kütüphane | Sürüm | Amaç |
|---|---|---|
| `org.jmrtd:jmrtd` | 0.7.31 | ICAO MRTD / çip okuma |
| `net.sf.scuba:scuba-sc-j2se` | 0.0.19 | Akıllı kart soyutlaması |
| `org.bouncycastle:bcprov-jdk15on` | 1.70 | Kriptografi |
| `net.java.dev.jna:jna` | 5.14.0 | `IDSIF.dll` ve `evolis.dll` çağrıları |
| `com.github.jai-imageio:jai-imageio-jpeg2000` | 1.4.0 | JP2 fotoğraf çözme |
