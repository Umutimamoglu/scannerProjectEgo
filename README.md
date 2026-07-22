# id-scanner

CRT-603-7005 kart okuyucu ile T.C. kimlik kartı / e-pasaport okuma uygulaması.

Pipeline iki aşamalı:

1. **Optik tarama** — `IDSIF.dll` (JNA üzerinden) kartın ön/arka yüzünü 300 DPI tarar ve dahili OCR ile MRZ (Machine Readable Zone) metnini çıkarır.
2. **Çip okuma** — MRZ'den türetilen BAC anahtarıyla NFC üzerinden çipe bağlanılır, JMRTD ile veri grupları (DG) okunur.

```
Kart yerleştir → ScanMRZ (tara + OCR) → Move(EJECT_HALF) → BAC → DG'leri oku → output/
```

## Gereksinimler

| Bileşen | Sürüm / Not |
|---|---|
| JDK | 17+ (test edilen: Temurin 17.0.19) |
| Maven | 3.9.9 — repoda yok, `mvnw.cmd` indirir |
| Donanım | CRT-603-7005 kart okuyucu (USB VID `0483`, PID `5710`) |
| USB sürücü | **libusb-win32 v1.4.0.0** — aşağıya bakın, kritik |
| Model dosyaları | `native_x64/depends/` — repoda yok, üretici paketinden gelir |

> **Not:** `native_x64/depends/` ve USB sürücüsü repoda bulunmaz. Temiz bir makinede kurulum yapıyorsanız "Kurulum" bölümündeki 3. ve 4. adımlar zorunludur, atlanırsa uygulama çalışmaz.

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

## Proje yapısı

```
├── src/main/java/com/mobiloby/
│   ├── App.java              # Ana akış: tarama → BAC → DG okuma
│   ├── ScannerBridge.java    # IDSIF.dll sarmalayıcı (JNA)
│   ├── IDSIF.java            # DLL fonksiyon/struct tanımları
│   └── MrzReader.java        # MRZ ayrıştırma
├── native_x64/               # 64-bit SDK (IDSIF.dll, OpenCV, OCR, libusb)
│   ├── depends/              # ⚠️ Repoda yok — üretici paketinden kopyalanmalı
│   └── Img/                  # Taranan BMP'ler (üzerine yazılır)
├── native/                   # Eski 32-bit SDK (kullanılmıyor)
├── output/                   # Çipten okunan veriler
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
| `net.java.dev.jna:jna` | 5.14.0 | `IDSIF.dll` çağrıları |
| `com.github.jai-imageio:jai-imageio-jpeg2000` | 1.4.0 | JP2 fotoğraf çözme |
