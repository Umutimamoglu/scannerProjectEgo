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

## Çalıştırma

```powershell
run.bat
```

Akış:

1. `KARTI CİHAZA YERLEŞTİRİN, sonra ENTER` → kartı koyup **Enter** (Q = iptal)
2. Tarama + OCR (~2.5 sn), MRZ ekrana basılır
3. Kart otomatik NFC pozisyonuna taşınır (`Move=EJECT_HALF`)
4. BAC ile çipe bağlanılır, DG'ler okunur ve `output/` klasörüne yazılır

### MRZ'yi elle vererek taramayı atlama

Tarayıcı olmadan yalnızca çip okumak için:

```powershell
run.bat app --mrz <belgeNo>,<doğumYYAAGG>,<sonGeçerlilikYYAAGG>
```

Örnek: `run.bat app --mrz A12345678,030505,310209`

Bu modda tarayıcı hiç kullanılmaz, doğrudan NFC okuyucuya geçilir.

## Kart baskısı (Evolis KC Prime)

### Komutlar

| Komut | Ne yapar |
|---|---|
| `run.bat printer` | Yazıcı bağlantı testi: cihaz listesi, durum bayrakları, ribon bilgisi |
| `run.bat card` | Kart görselini üretir → `output/card_preview.png` + `card_print.bmp` |
| `run.bat print` | **Prova** — baskı hattını çalıştırır, PRN üretir, **kart harcamaz** |
| `run.bat print --onayla` | **Gerçek baskı** — kartı harcar |

Türkçe karakterler için isimleri elle verin (MRZ sadece ASCII taşır, `İ`/`Ğ`/`Ş` yok):

```powershell
run.bat card --ad UMUT --soyad İMAMOĞLU
run.bat card --baslik "BAŞKENT KART" --altbaslik "ULAŞIM"
```

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

### 6. Baskı sonrası `ERR_MECHANICAL`

İlk gerçek baskı denemesinde kart başarıyla basıldı ancak `evolis_print_exect` `-22` (`PRINT_EMECHANICAL`) döndü ve `ERR_MECHANICAL` bayrağı set oldu. Baskının kendisi çıktı, hata kart çıkışı/besleme aşamasında oluştu — hazne tek kartla çalıştığı için boşalmış olması muhtemel sebep (`INF_FEEDER_NEAR_EMPTY` de set).

Daha fazla kartla tekrar denenip doğrulanması gerekiyor. Hata bayrağı set kaldığı sürece sonraki baskılar reddedilebilir; kapağı açıp kapatarak temizlenir.

### 7. Kart tasarımı Türkçe isimleri MRZ'den alamaz

MRZ standardı yalnızca ASCII taşır — `İMAMOĞLU` yerine `IMAMOGLU` gelir. Doğru yazım çipte **DG11'de var** ama `App.java` DG11'i ekrana yazıyor, dosyaya kaydetmiyor. Geçici çözüm: `run.bat card --ad ... --soyad ...` ile elle vermek. Kalıcı çözüm: `App.java`'da DG11'i de `output/` altına kaydetmek.

### 8. Kart yerleşimi kalibre edilmedi

`CardRenderer` içindeki mm cinsinden koordinatlar referans görselin oranlarından tahmin edildi. Gerçek baskı üzerinde ölçülüp düzeltilmesi gerekir.

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
