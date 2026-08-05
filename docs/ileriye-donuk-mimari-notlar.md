# İleriye Dönük Mimari Notlar

**Durum:** Bunların hiçbiri .NET taşıma kapsamında **değil**. Kaynak: kiosk projesi üzerine yapılan bir mimari sohbet. Amaç, taşıma sırasında alacağımız kararların ileride bu yönde büyümeyi engellememesi.

**Bağlam farkı — en önemli nokta:** O sohbet bir **kiosk ürününü** tarif ediyor (vatandaş dokunmatik ekranda kendi kartını basıyor; EUTS uygunluk sorgusu, POS ödemesi, 6 ekranlık akış, admin paneli). Şu an taşıdığımız uygulama ise bir **operatör aracı** (kart tak, oku, doğrula, çiz, bas). Bunlar aynı ürünün iki hâli değil, iki ayrı ürün.

---

## 1. Mevcut arayüz harcanabilir — ve bu iyi haber

Kiosk gelirse bugünkü panel yerine geçilecek, evrilmeyecek. Vatandaşa dönük 6 ekranlık dokunmatik akış ile operatör paneli farklı şeyler.

**Sonuç:** UI katmanına yatırım yapılmaz. WinForms tercihi tam da bu yüzden doğru — nasılsa atılacak bir katmana WPF/MVVM öğrenme maliyeti ödenmez.

**Tek şart:** İş mantığı formun içine sızmayacak. Sızarsa harcanabilir olan katman, harcanamaz hale gelir. (Geçiş planı, kod standartları md. 2.)

---

## 2. MVVM — evet, ama burada değil

MVVM, WPF/WinUI'ın veri bağlama altyapısı üzerine kurulu. WinForms'ta o altyapı yok; zorlamak fayda değil sürtünme üretir.

MVVM'in doğru yeri kiosk UI'ı yazılırken WPF/WinUI tarafıdır. O gün geldiğinde `Application` katmanı hazır olacağı için ViewModel'ler doğrudan ona bağlanır.

---

## 3. Clean Architecture — zaten uyguluyoruz, isim koymamıştık

Geçiş planındaki 7 projeli yapı ile sohbetteki katmanlar örtüşüyor:

| Sohbetteki katman | Bizdeki karşılığı |
|---|---|
| Domain (en iç) | `IdScanner.Core` — hiçbir DLL tanımaz |
| Application | *(şimdilik ince — bkz. §5)* |
| Infrastructure | `IdScanner.Chip`, `.Native`, `.Crypto` |
| Presentation | `IdScanner.App` (WinForms) |

Bağımlılık oku hep içeri bakıyor, donanım arayüzler arkasında (`ICardReader`, `IPrinter`, `IScanner`), Domain izole. Yani makro mimari kararı zaten alınmış durumda.

---

## 4. Katılmadığım nokta: `IChipVerifier` arkasına PA ve AA koymak

Sohbette `IChipVerifier` arayüzünün arkasına `PassiveAuthVerifier` ve `ActiveAuthVerifier` diye iki uygulama önerilmiş. **Bu modelleme hatalı:**

- PA ve AA birbirinin **alternatifi değil**, sıralı iki ayrı kontrol. Strateji deseni buraya oturmaz.
- PA **çevrimdışı** çalışır: ham DG byte'ları + SOD + kök sertifika yeter, kart takılı olmasa da olur.
- AA **canlı kart** gerektirir: rastgele challenge gönderilip imzalı cevap alınır. Kart olmadan yapılamaz.

Farklı girdi ihtiyaçları olan iki şey aynı arayüzün arkasına giremez. **AA doğrulayıcıya değil okuyucuya aittir.**

Mevcut Java kodundaki ayrım zaten doğru: `ChipVerifier` saf doğrulama yapıyor (girdi: byte'lar), challenge'ı `IdCardReader` üretip çipe gönderiyor. .NET'te bu ayrım korunacak.

---

## 5. Şimdi ucuza alınacak iki şey

Bu ikisi taşıma sırasında neredeyse bedava, sonradan pahalı:

### 5.1 `KartKaynagi` — veri kaynağı izlenebilirliği

Verinin nereden geldiğini kaydeden bir enum: `CipTckk`, `CipMaviKart`, `MrzFallback`, `OptikTarama`.

**Neden:** Şu an `IdData` bunu tutmuyor. "İsim geldi" ile "isim *güvenilir bir yerden* geldi" ayrımı yapılamıyor. Çip bozuksa MRZ'ye düşme senaryosu ileride mutlaka gelecek ve o zaman bu ayrım bir güven kararına dönüşecek.

**Maliyet:** Tek alan. Sonradan eklemek ise veri üreten her akışa dokunmak demek.

### 5.2 İnce bir `Application` katmanı

Akış mantığı (tara → oku → doğrula → çiz → bas) formdan çıkıp buraya alınır. Bugün düz ve sıralı, ~50 satır.

**Neden:** Kiosk geldiğinde state machine tam bu noktaya oturur — UI'a hiç dokunulmadan. Mantık formun içinde kalırsa o gün her şey baştan yazılır.

---

## 6. Şimdi yapılmayacaklar

Var olmayan bir binaya iskele kurmak taşımayı yavaşlatır ve Java ile karşılaştırmaya dayanan doğrulama zincirini bulanıklaştırır. Kiosk kararı netleştiğinde ele alınacaklar:

- **State machine** (`Stateless` kütüphanesi) — "ödeme onaylanmadan kart basılamaz" gibi kuralların koda gömülmesi. Şu anki akış operatör butonlarıyla ilerliyor, durum makinesi gerektirecek karmaşıklık yok.
- **EUTS istemcisi** — uygunluk sorgusu, kart kaydı
- **POS entegrasyonu** — provision/commit, idempotency key
- **Outbox kuyruğu** — EUTS erişilemezken bekleyen kayıtlar
- **Veritabanı** (`ITransactionRepository`) — işlem kayıtları
- **Polly** — retry / circuit-breaker / timeout politikaları
- **KVKK maskeli loglama** (`Serilog`) — TC no ve fotoğrafın açık yazılmaması
- **Admin paneli** — ribon seviyesi, kart stoku, bağlantı durumu
- **MVVM** — kiosk UI'ı WPF ile yazılırken

---

## 7. Zaten hazır olan bir şey

Sohbette "kart tipine göre doğru kök sertifikayı seçme (CSCA_TR vs SBMK_KOK)" mantığından bahsedilmiş. Bu **mevcut kodda var** — `ChipVerifier` `certs/` altındaki tüm kökleri deniyor ve hangisine bağlandığını `matchedRootCN` alanında raporluyor. Ayrıca ele alınmasına gerek yok, .NET'e aynen taşınacak.
