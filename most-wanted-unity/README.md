# Most Wanted – Açık Dünya Sokak Yarışı (Unity 6 + URP)

Need for Speed: Most Wanted (2005) tarzında açık dünya sokak yarışı. **Sahne kurmana gerek yok:** projeyi aç,
Play'e bas. Oyun her şeyi (dünya, araçlar, polis, arayüz) çalışma anında kendisi kurar.

- Motor: **Unity 6 (6000.6.x)**, **URP** (Universal Render Pipeline)
- Araçlar: InvoGames araç paketi (10 araç) varsa onları kullanır, yoksa prosedürel yedek araçlar
- Harita: `city_3d_model.glb` (Optic Idealist) varsa onu, yoksa prosedürel test şehrini kullanır

---

## 1. Kurulum (MacBook)

1. **Unity Hub** → https://unity.com/download (macOS). Hub'da **Unity 6000.6.x** sürümünü kur (Apple Silicon).
2. Hub → **Projects → Add → Add project from disk** → `most-wanted-unity` klasörünü seç → aç.
3. İlk açılışta otomatik olarak:
   - **URP** paketi yüklenir, `Assets/Settings/MW_URP.asset` oluşturulup Graphics/Quality ayarlarına atanır,
     renk uzayı **Linear** yapılır (menü: *Most Wanted → URP Kurulumunu Yap* ile elle de çalıştırabilirsin).
   - Araç ve harita kayıtları taranır.
4. `Assets/Scenes/Main.unity` sahnesini aç (boş bir sahne de olur) ve **▶ Play**.

### Araç paketini içe aktarma (gerekli – depoda yok, lisans gereği)
1. Fab'dan aldığın **"Car Asset Pack for Arcade & Demolition Racing Games" (Store InvoGames)** `.unitypackage` dosyasını
   Unity'ye sürükle (veya *Assets → Import Package → Custom Package*) → **Import**.
2. Paket `Assets/Store InvoGames/...` altına gelir. Otomatik tarama araçları bulur; olmazsa menüden
   **Most Wanted → Arabaları Tara**. Konsolda `[MW] Araç kaydı güncellendi` yazısını görürsün.
3. Araç değerleri (isim, fiyat, kütle, tork, devir, azami hız, çekiş, tutuş, polis rolü) `Assets/Resources/CarRegistry.asset`
   içinde Inspector'dan düzenlenebilir.

### Yeni araç ekleme (çok kolay)
1. Araç prefabını/FBX'ini `Assets/**/Cars/` veya `Assets/**/Vehicles/` klasörüne (veya `Assets/Resources/Cars/`) koy.
2. **Most Wanted → Arabaları Tara**. Araç garajda yeni bir araba olarak görünür (varsayılan değerlerle).
3. Gerekirse `CarRegistry.asset` içinden değerlerini düzenle.
- Model kuralları: tekerlek objelerinin adında `wheel`/`tire`/`tyre` geçmeli; ön/arka için `front`/`rear` (veya FL/FR/RL/RR).
  Yön, ölçek (araç uzunluğu → ~4.5 m), merkez ve zemin otomatik ayarlanır. Boya: adında `body`/`paint` geçen malzeme.

### Şehir haritasını ekleme (city_3d_model.glb)
1. **glTFast** paketini ekle: *Window → Package Manager → + → Add package by name…* → `com.unity.cloud.gltfast` → Add.
2. `Assets/Maps/` klasörü oluştur ve `city_3d_model.glb` dosyasını içine sürükle (68 MB; depoya eklenmez).
3. Otomatik tarama `Assets/Resources/MapRegistry.asset`'i oluşturur (yoksa: **Most Wanted → Haritaları Tara**).
4. Play: harita otomatik yüklenir. Duraklat menüsünden **Harita: Test / İthal** arasında geçiş yapabilirsin.
- Yol ağı önceden hesaplandı: `Assets/Resources/MapData/city_3d_model_roadgraph.json`
  (2645 düğüm; trafik, polis, yarış rotaları, garaj, saklanma noktaları). Araçlar `Tools/map_bake/` içindeki
  Python betikleriyle üretildi (Street mesh'i 2 m ızgaraya → iskelet → graf).
- Koordinat varsayımı: glTFast, glTF'i Unity'ye aktarırken **X eksenini aynalar**; GLB içinde 0.01 ölçek olduğundan
  oyun haritanın kök ölçeğini genişlik ≈ 2744 m olacak şekilde (~×100) ayarlar. Açılışta 20 yol noktasından aşağı
  ışın atılarak kontrol edilir; tutmazsa aynalanmış hal denenir (Konsol: `[MW] Yol ağı doğrulama`).

---

## 2. Kontroller

| Tuş | İşlev |
|---|---|
| W / ↑, S / ↓ | Gaz, fren / geri |
| A D / ← → | Direksiyon |
| Space | El freni (drift) |
| Shift | Nitro |
| Q / Sağ tık | **Speedbreaker** (ağır çekim) |
| C | Kamera: yakın / uzak takip, kaput, tampon |
| E | Garaj (yeşil işaret üzerinde) |
| J | Yarışlar ve işler |
| B | Kara Liste |
| M / Tab | Büyük harita (tekerlek: yakınlaştır, WASD/sürükle: kaydır) |
| R | Aracı düzelt |
| H | Korna |
| (Garajda) Motor Sesi ◀ ▶ | Her araca istediğin motor sesi (kozmetik) |
| F | FPS / performans göstergesi |
| Esc | Duraklat menüsü |
| Drag yarışında | E vites ↑, Q vites ↓, A/D şerit değiştir |

### Hileler
- **F9**: +₺1.000.000 para ("Hile: +₺1.000.000")
- **F10**: Aranma seviyesi anında 5 yıldız (test için)

---

## 3. Mekanikler

- **Sürüş (arcade-sim):** WheelCollider; tork eğrisi + 6 ileri otomatik şanzıman (vites, devir, devir kesici),
  kalkışta patinaj/burnout, güçle savrulma (patinajda yanal tutuş düşer), el freni drift'i, hıza duyarlı direksiyon
  (32° → 150 km/sa'da 7°), karşı direksiyon ve savrulma yardımı, hıza bağlı downforce ve aerodinamik sürükleme.
- **Devrilmeme:** alçak ağırlık merkezi (aks yüksekliği), lastik kuvvetleri ağırlık merkezi yüksekliğinde uygulanır
  (`forceAppPointDistance`), viraj demiri (süspansiyon sıkışmasından), gerçekçi atalet, 60°'den fazla yatıkta 1.5 sn
  sonra otomatik doğrultma. Trafik, polis ve rakiplerde de aynı fizik.
- **Nitro:** drift, kıl payı geçiş (near miss), rüzgar tüneli (öndeki aracın arkası) ve yüksek hızla dolar;
  mavi egzoz alevi, FOV artışı, hız çizgileri, kamera sarsıntısı, renk sapması.
- **Speedbreaker:** zaman %35'e yavaşlar, tutuş/direksiyon artar, ekran renksizleşir; hız ve kıl paylarıyla dolar.
- **Polis:** 1–5 yıldız. Devriye → sivil polis (3★) → ağır SUV'ler çarpar (4★+), helikopter projektörle izler (4★+),
  barikat (3★+) ve **çivili şerit** (lastikleri patlatır). Görüş hattını kır → **sakinleşme** çubuğu dolar;
  **saklanma noktalarında** (otopark, tünel, ara sokaklar) 2.5 kat hızlı. Yavaşken kutulanırsan **yakalanma** çubuğu.
  Polisleri çarparak devre dışı bırak; **Pursuit Breaker** (su kulesi / benzinlik tentesi) üstlerine çöker.
  Ödül (bounty): süre, devre dışı polis, barikat atlatma, yapı hasarı. Türkçe telsiz konuşmaları.
- **Yarışlar (J):** Sprint, Tur, Hız Kamerası (radar toplamı), Gişe (zamana karşı), Drag (manuel vites,
  mükemmel vites, şerit değiştirme). 3 YZ rakip, lastik bandı, nitro kullanımı. Teslimat işleri.
- **Kara Liste (B):** 5 rakip (#5 Kobra → #1 Kral). Meydan okumak için yarış galibiyeti + kariyer ödülü +
  kilometre taşı gerekir. Yenince ödül ve rakibin arabası.
- **Garaj (E):** satın al, %60'a sat, seç, **18 boya** (inci beyazı, mat siyah, şeker kırmızı, MW gümüş/mavi, bronz,
  altın…; metalik/parlaklık dahil, garajda canlı önizleme; "Fabrika rengine dön"), **Motor Sesi** değişimi,
  8 performans paketi (Motor, Turbo, Şanzıman, Süspansiyon, Lastik, Nitro, Fren, **Egzoz** — egzoz sesi yükselir,
  daha çok patlama, vites atarken "BANG"; her biri 3 seviye). İlerleme PlayerPrefs'e JSON olarak kaydedilir.

### Ekonomi (ilerleme eğrisi)
| Kademe | Araç | Fiyat |
|---|---|---|
| Başlangıç | Mini John Cooper Works | ₺0 |
| Giriş | Ford F-150 Raptor | ₺15.000 |
| Giriş | Toyota Supra MK4 | ₺25.000 |
| Orta | BMW M4 Competition | ₺40.000 |
| Orta | Dodge Challenger Hellcat | ₺60.000 |
| Yüksek | Tesla Cybertruck | ₺85.000 |
| Yüksek | Nissan Skyline GT-R R34 | ₺115.000 |
| Yüksek | Nissan GT-R R35 | ₺150.000 |
| Süper | Porsche 911 Turbo S | ₺200.000 |
| Zirve | Bugatti Chiron | ₺350.000 |

Kazanç: yarış ₺2.200–7.500 (2.: %40, 3.: %14), teslimat ₺600–3.000, polisten kaçış ödülün yarısı nakit
(tamamı kariyer ödülüne), kara liste ₺20.000 → ₺120.000 + rakibin arabası, kilometre taşı ₺2.500.
Ortalama ~₺1.000–1.500/dk → bir sonraki araç yaklaşık 20–40 dk. Performans paketi fiyatı aracın kademesine göre
(₺500 + fiyatın %5'i × seviye); satış %60. Başlangıç parası ₺12.000.

### Garaj (NFS Carbon "ARABA SEÇ" tarzı)
Garaja girince dünya durur ve araç y = -2000'deki karanlık showroom'a geçer: yansıtıcı zemin, mor/turkuaz spotlar,
neon şeritler, sis, dönen tabla, kendi kamerası (fare sürükle = döndür, tekerlek = yakınlaştır).
Üst sağda SON HIZ / İVME / YOL TUTUŞ çubukları, Para ve Değer; altta sınıf (TUNER/KAS/EGZOTİK, SEVİYE 1–3),
`<  ARAÇ ADI  >` (←/→), sahip/satın al durumu. Tuşlar: Geri (Esc) • Seç/Satın Al (Enter) • Boya (P) • Performans (U) •
Motor Sesi (S) • Özelleştir (C, sadece drift araçları) • Sat (Del) • Fotoğraf (F12).

### Drift Araçları + F&F özelleştirme (Supra, R34, R35, M4)
Özelleştir menüsü (canlı önizleme, ↑/↓ kategori, ←/→ seçenek, Enter satın al/uygula; alınan seçenekler kalıcı):
ön lip/karbon splitter, marşpiyel, karbon difüzör, geniş kasa, spoiler (ducktail / GT / dev F&F kanadı), havalandırmalı
veya karbon kaput, tavan scoop, 6 jant stili + 7 renk, sürüş yüksekliği (-2/-4/-6 cm), kamber (2/4/7°), neon alt ışık
(6 renk + nokta ışık), cam filmi, far rengi, vinil (yarış şeritleri, R34 mavi/gümüş, yan grafik, alevler — kutu eşlemeli
ikinci malzeme katmanı, `MostWanted/Vinyl`), NOS / 走り屋 çıkartmaları, "FAST" plaka, egzoz ucu (4 stil) ve
**DRIFT AYARI**. Fiyatlar araç kademesine göre ölçeklenir; araç başına kayıt (`CarSave.custom`, PlayerPrefs JSON).
Kit parçaları tek birleşik mesh + paylaşılan malzemeler; jantlar stil başına tek paylaşılan mesh.
Drift ayarı açıkken garajdan çıkışta araç Saarg (Arcade Car Physics) kontrolcüsüne geçer.

**Drift puanı** (serbest sürüş): açı × hız × süre × çarpan (her 2 sn kesintisiz drift +1, en fazla ×5); düzeltip
1.5 sn içinde yeni drift başlamazsa puan bankalanır (para ödülü), çarpışma zinciri koparır. HUD: puan, çarpan, zincir çubuğu.

### Sürüş hissi
Kamera dönüşe ~0.2 sn gecikmeyle girer, virajın dışına kayar, yanal g ile 1–3° yatar, yüksek yanal g'de FOV nabzı,
kaymada hafif sarsıntı. Görsel gövde (fizik değil) yanal/boyuna ivmeyle 2–4° yatar ve yunuslar. Lastik ciyaklaması orta
şiddette virajda başlar; lastik izleri (tek paylaşılan halka mesh), sert kaymada duman. Arcade'de dönüş girişinde anlık
savrulma tepkisi. Gamepad: sol çubuk analog direksiyon (ölü bölge 0.12, yanıt eğrisi ^1.6) ve gaz/fren.

### Polis dengesi
Duraklat → **Polis zorluğu**: Kolay (varsayılan) / Normal / Zor.
- Aktif takipçi sınırı 1–5 yıldızda 2/3/4/5/6 (Kolay'da bir eksik). Polis azami hızı oyuncunun %90/93/95'i (1–2 yıldız),
  her yıldızda +%5. Tepki gecikmesi 0.4–0.8 sn, direksiyon kusuru, yolda virajdan önce gerçekçi frenleme.
- 1–2 yıldız: polis çoğunlukla arkadan takip eder; araç başına 6–8 sn'de en fazla bir çarpma; oyuncudan 15 km/s'ten
  hızlıysa asla çarpmaz. 3+ yıldız: kutulama, daha sık çarpma, nitro, barikat/çivili şerit.
- Çarpışmada oyuncunun hız değişimi sınırlı (Kolay 3 m/s … + yıldız başına 1.2).
- Görülmek: 40 m içinde ya da 120 m içinde görüş hattında. Görülünce HUD: "Polislerin görüş alanından çık".
  Görülmeyince sakinleşme dolar: 1–2 yıldızda 20/25/30 sn (zorluğa göre), üst yıldızlarda daha kısa; saklanma
  noktasında 2× hızlı. Sakinleşmede mini haritada yanıp sönen **S** saklanma noktaları.
- Yakalanma: sadece 5 km/s altında ve kutulanmışken (2 polis yanında, ya da 1 polis + gaza rağmen ilerleyememe),
  4 sn (Kolay 5, Zor 3.5); ekranda "YAKALANIYORSUN!" çubuğu.

### Sürüş fiziği (iki mod, araç başına tek kontrolcü)
- **Normal mod — Randomation Vehicle Physics (RVP, JustInvoke, MIT)** — tüm araçlar, trafik, polis, rakipler.
  `Assets/ThirdParty/RVP`: ışın-izli süspansiyon (RVP `Suspension`), RVP lastik eğrileri ve kayma hesabı
  (`Wheel.GetSlip/ApplyFriction`, yanal kayma = yanal hız × 0.1, kayma bağımlılığı), RVP direksiyon eğrisi
  (arcade: 0 km/s %100, 50 → %75, 100 → %55, 150 → %38, 200+ → %30; 40° aralık; tam kilit 0.12 sn, merkeze 0.08 sn; hassasiyet hem açıyı hem hızı ölçekler, varsayılan 1.2), yanal tutuş tepe ×1.40, düz eğri kuyruğu, savrulma denetimi yalnızca gerçek oversteer'de, TCS/ABS (talep eğrinin tepesinde kırpılır), RVP savrulma
  yardımı (yalnızca kayarken; hedef savrulma tutuş sınırıyla kırpılır → denge kontrolü). Yanal kuvvet **gazdan bağımsız**;
  sadece gerçek patinaj/kilitlenmede düşer. Gaz bırakınca hafif ağırlık transferi → çok hafif lift-off oversteer.
  El freni = arka tekerler kilitlenir (RVP ebrake) → drift.
- **Sürüş stili** (Duraklat menüsü, sadece oyuncu): **Arcade** (varsayılan, NFS hissi) / **Gerçekçi** (saf RVP ayarı).
  Arcade: direksiyon 0 km/s %100 → 50 %85 → 100 %70 → 150 %60 → 200+ %50; hıza bağlı ek tutuş ("arcade downforce")
  ×1.0 → ×1.6 (120 km/s) → ×1.9 (200); dönüş yardımı (direksiyonun istediği savrulma hızına, tutuşla sınırlı);
  60 km/s üstünde fren+direksiyon → tutuş öne kayar. Aşırı kilit sınırlayıcı: ön lastik tepe kaymasını aşmaz, bu yüzden
  yüksek hassasiyet hiçbir zaman daha az döndürmez. Simülasyon (`RvpSim.cs`, sabit hız tam kilit): 30 → 3.7 m,
  60 → 12.5 m, 120 → 36 m.
- **Süspansiyon** (RVP ışın): köşe başına f = 1.7 Hz, ζ = 0.5, dinlenmede ~%48 sıkışma; sönüm hızı ±6 m/s (RVP ±1 idi),
  dip vurma kuvveti en fazla 2 g. `Tools/physics_sim/SuspSim.cs`: 5 cm bırakma 0.42 sn'de oturur (1 aşım),
  100 km/s'de 2 cm basamakta havada kalan kare yok.
- **Drift modu — Arcade Car Physics (Saarg, MIT)** — sadece Drift Araçları (Supra, R34, R35, M4) drift ayarı açıkken,
  garajdan çıkışta `CarController.SetPhysicsMode(true)` ile değişir. `Assets/ThirdParty/ArcadeCarPhysics`: WheelCollider,
  Saarg sürtünme eğrileri (ileri 0.4/1–0.8/0.5, yanal 0.2/1–0.5/0.75), düşük arka yanal sertlik, el freni arka yanal
  sertliği düşürür, Saarg drift kuvveti/torku, downforce.
- Bizim kalanlar: motor/şanzıman/devir → ses, nitro, speedbreaker, fırlama koruması, devrilme sınırı, otomatik doğrultma,
  oyuncu için yokuş yardımı (sadece boyuna kuvvet). Eski özel katmanlar (savrulma yardımı, karşı direksiyon,
  sürtünme çemberi, drift hız koruma) kaldırıldı.
- Doğrulama: `Tools/physics_sim/RvpSim.cs` (oyundaki `RvpTire` ile): 30/60/100 km/sa tam direksiyon, gaz 0 ve 1.
  Duraklat menüsünde **Direksiyon hassasiyeti** (0.6–2.0).

---

## 3b. Harita süsleme (ithal şehirde, otomatik)

İthal harita yüklendiğinde `MapDressing` şehri prosedürel olarak tamamlar (Duraklat → **Harita Süsleme** ile kapatılabilir):

- **Çevre:** şehrin etrafında ~9×9 km arazi (tepeler, tarla parselleri, uzakta dağlar), doğuda **deniz** ve
  denize akan **nehir**, kumsal şeridi, instanced **ormanlar** (Düşük 2.500 / Orta 6.000 / Yüksek 11.000 ağaç).
  Su: `MostWanted/Water` URP shader'ı (kayan dalga normalleri, fresnel, güneş parlaması). Bulut kubbesi: `MostWanted/Clouds`.
- **Otoyol:** güneydeki uzun yolun ucundan çıkıp şehrin dışından dolaşan ve doğu bulvarından şehre dönen
  3+3 şeritli otoyol: orta bariyer, korkuluklar, aydınlatma direkleri, yeşil tabelalar ve nehir üstünde
  **Boğaz Köprüsü** (kuleler + askı kabloları). Yol ağına eklenir → trafik/polis kullanır.
  Yeni yarışlar: **Otoyol Sprinti**, **Otoyol Turu**, **Köprü Radarı**.
- **Sokak donatısı:** lambalar (gece parlar; oyuncuya en yakın 2/4/6 tanesi gerçek ışık), büyük kavşaklarda
  **çalışan trafik ışıkları** (trafik kırmızıda durur), DUR / 50 tabelaları, otobüs durakları, banklar, çöp kutuları,
  yangın muslukları, geniş bulvarlarda ağaçlar, reklam panoları (sahte NFS tarzı reklamlar), yol çalışması bariyerleri,
  8 adet **Pursuit Breaker**, oyuncunun etrafında yer değiştiren **park etmiş araçlar** (paket araçları, 20/40/70).
- **Yol görünümü:** yaya geçitleri, rögar kapakları; **Islak Zemin** modu (asfalt parlar, su birikintileri,
  Yüksek'te gerçek zamanlı yansıma probu).
- **Atmosfer** (Duraklat menüsü): **Most Wanted** (sıcak/sepya renk), Normal, Gün Batımı, Gece
  (cephe dokuları gece pencere ışığı olarak parlar).
- **Yaşam:** kuş sürüleri, ara sıra geçen uçak (yanıp sönen ışık), şehir uğultusu ve uzaktan siren sesleri.
- **Semtler:** Merkez, Liman, Banliyö Kuzey, Banliyö Güney, Sanayi, Otoyol — tabelalar, büyük haritada isimler,
  semte girince bildirim.
- Hepsi GPU instancing ile çizilir, 250 m hücrelerde mesafeyle kesilir; sayılar kalite ön ayarına göre ölçeklenir
  (ön ayar değişikliğinin süslemeye tam yansıması için haritayı yeniden yükle).

---

## 3c. Ses sistemi (motor sesleri)

**Ses seviyeleri:** Duraklat menüsünde **Motor / Efekt / Müzik / Siren** kaydırıcıları (kaydedilir).
**Korna:** H. Müzik: `Assets/Resources/Music/` içine `.ogg/.mp3/.wav` koy → karışık sırayla çalar.

### Gerçek motor sesleri ekleme (önerilen)
Klasör: `Assets/Resources/EngineSounds/<araç_id>/` (araç id'si CarRegistry'de görünür, ör. `car_5`) veya tüm araçlar
için `Assets/Resources/EngineSounds/default/`. Ortak efektler: `Assets/Resources/EngineSounds/common/`.

| Dosya adı | Ne | Not |
|---|---|---|
| `idle.wav` | rölanti döngüsü | ~900 rpm kabul edilir |
| `1500_on`, `3000_on`, `4500_on`, `6000_on`, `7500_on` | gazda, sabit devir döngüleri | adın başındaki sayı = kaydın devri |
| `1500_off`, `3000_off`, … | gaz kesik (motor freni) döngüleri | yoksa sadece `_on` kullanılır |
| `turbo` | turbo ıslığı döngüsü | |
| `bov` | blow-off "pssh" (tek seferlik) | |
| `shift` | vites "klonk" | |
| `pop1`, `pop2`, … | egzoz patlamaları | gaz kesince rastgele |
| `impact1`, `impact2`, … | çarpma sesleri | |
| `thump1`, … | süspansiyon darbesi | |
| `squeal`, `wind`, `gravel`, `nitro` | lastik, rüzgar, çakıl, nitro döngüleri | |
| `horn` | korna döngüsü | |

- En az 2 devir döngüsü varsa örnek tabanlı mod açılır: en yakın iki devir eşit güçte çapraz geçişle karışır,
  her biri `güncelDevir / kayıtDevri` ile perdelenir, gaz pedalı on/off kayıtlarını karıştırır.
- Döngüler kesintisiz (loop) olmalı; Unity'de *Load Type = Decompress On Load* (kısa) veya *Compressed In Memory*.
- **Ücretsiz kaynaklar:** freesound.org ("engine loop", "car engine rpm", CC0 filtresi), Unity Asset Store'da ücretsiz
  araç ses paketleri ("Realistic Car Sounds", "Car Engine Sound Pack" vb. — lisansı kontrol et), Fab'daki ücretsiz ses paketleri.
  Kaynağı ve lisansı CarRegistry'deki `credit` alanına yaz (Emeği Geçenler ekranında görünür).

### Motor tipleri (prosedürel)
3 silindir, 4 silindir, 5 silindir (Audi tarzı "warble"), Boxer 4 (Subaru "rumble"), sıralı 6, V6 (VQ "rasp"),
V8 muscle (crossplane "burble"), V8 düz krank (çığlık), V10, V12, Rotary (Wankel "brap"), Dizel (takırtı),
Elektrik (ıslık + inverter tonu), Boxer 6 (Porsche), W16 (Bugatti: derin, pürüzsüz, dört turbo hava akışı). Ek: küçük turbo (hızlı dolar, ince ıslık), büyük turbo (gecikmeli, gaz kesince
"stututu"), supercharger uğultusu. Aynı tip araçlar da formant/emme/düzensizlik açısından hafifçe farklıdır; trafik ve
polis rastgele varyasyon alır. Egzoz patlamaları: perdesi düşen kalın "thud" (70–120 Hz) + 300–3000 Hz çıtırtı + kısa oda yankısı, "bap-bap-brrap" dizileri; sadece 3000 rpm üstünde gaz kesince, kalkışta yok. Alevler aracın gerçek egzoz uçlarından çıkar (Cybertruck: mavi elektrik parıltısı).
Paket araçları (gerçek modeller, FBX kaynak yollarından): **Mini John Cooper Works** (başlangıç, I4 turbo), **Dodge
Challenger SRT Hellcat** (V8 + supercharger, polis devriyesi), **Ford F-150 Raptor** (EcoBoost V6), **BMW M4 Competition**
(I6 twin-turbo, sivil polis), **Tesla Cybertruck** (elektrik, ağır polis), **Nissan GT-R R35** (V6 twin-turbo),
**Toyota Supra MK4 A80** (2JZ I6, büyük turbo), **Porsche 911 Turbo S** (boxer 6 turbo), **Nissan Skyline GT-R R34**
(RB26 I6 twin-turbo), **Bugatti Chiron** (W16 dört turbo — kara liste #1).
Tork/ağırlık/vites sayısı/devir gerçek değerler; fiyatlar oyun içi ilerleme eğrisine göre (aşağıdaki tablo).
`Tools/physics_sim/RealCarsSim.cs` gerçek ve oyun 0-100/azami hız değerlerini karşılaştırır.

### Prosedürel yedek (dosya yoksa)
Silindir ateşleme darbeleri (devir/60 × silindir/2), her darbe gürültü + basınç vuruşu, 3 rezonanslı egzoz
formantı, krank harmonikleri, emme ve mekanik gürültü. Karakter: **I4** (Hatch, Tuner S), **I6** (Coupe, GT-R,
Drift Spec), **V8** düzensiz "burble" (Muscle, SUV, Pikap, polis), **V10** (Süper Kanat). Turbo'lu araçlarda ıslık ve
BOV. Gaz kesince egzoz patlamaları + küçük alev. Lastik sesi (kaymaya göre), rüzgar (hıza göre), çakıl (asfalt dışı),
nitro tıslaması, süspansiyon darbeleri, çarpma sesleri. CarRegistry'de `engineType` (I4/I6/F6/V8/V10) ve `turbo`
alanlarıyla değiştirilebilir.
YZ araçları 3B ses (mesafe zayıflaması, hafif doppler); sadece en yakın 4 YZ motoru çalınır (CPU tasarrufu).
Polis sireni "wail" ve "yelp" arasında geçiş yapar.

---

## 3d. Arayüz (HUD)
Sağ altta analog devir saati (0–9 ×1000 rpm, kırmızı bölge, ibre), büyük italik hız (KM/S), üstte vites;
devir yayının içinde ince **nitro** (mavi) ve **speedbreaker** (turuncu) çubukları. Sol altta yuvarlak minimap
(araç yönü yukarı, K = kuzey). Sağ üstte para. Takipte üst ortada yıldızlar + sakinleşme/yakalanma çubukları.
**Sadece yarışta:** sol üstte **SIRA x / n** ve canlı sıralama listesi (fark saniye), üst ortada **GEÇERLİ TUR**
süresi, sağ üstte **TUR x / n** (tur yarışında) veya radar toplamı.

## 4. Performans (MacBook / Retina)

Duraklat menüsü → **Grafik: Düşük / Orta / Yüksek / Otomatik**, **FPS hedefi: 30 (Pil tasarrufu) / 60 / 120 (ProMotion) / Sınırsız**.

| | Düşük | Orta (varsayılan) | Yüksek |
|---|---|---|---|
| Render ölçeği (STP/FSR ile büyütme) | %60 | %75 | %100 |
| Gölge mesafesi / kademe | 60 m / 1 | 120 m / 2 | 200 m / 2 |
| Uzak düzlem | 800 m | 1200 m | 1800 m |
| Trafik | 10 | 20 | 35 |
| Bloom / hareket bulanıklığı | kapalı | açık | açık + SSAO (varsa) |

- **Otomatik:** 5 sn ortalama FPS < 50 ise ölçeği/ön ayarı düşürür, > 75 ise yükseltir.
- **F** göstergesi: FPS, kare süresi, CPU/GPU ms, batch, SetPass, üçgen, render ölçeği.
- MSAA kapalı (FXAA/SMAA), SRP Batcher açık, GPU Resident Drawer isteğe bağlı, uzak trafik fiziği
  kapatılır (kinematik), minimap 15 fps'de çizilir, ağaç/direk gibi detaylar katman mesafesiyle erken kesilir.
- **Öneriler:**
  - Gerçek performans için **Build** al: *File → Build Profiles → macOS → Build And Run*. Editör, özellikle Retina
    Game görünümünde çok ek yük getirir.
  - Editörde oynarken Game görünümünde "Low Resolution Aspect Ratios" seç veya pencereyi küçült.
  - **Occlusion Culling** pişir (büyük şehirde ciddi kazanç): harita sahnede açıkken
    *Window → Rendering → Occlusion Culling → Bake*. (Harita çalışma anında yüklendiği için, istersen haritayı
    Main sahnesine sürükleyip statik işaretleyerek pişir.)
  - **GPU Resident Drawer** (deneysel, varsayılan kapalı): önce *Project Settings → Graphics → BatchRendererGroup Variants = Keep All*
    yap (URP renderer'ı otomatik Forward+ yapılır), sonra Duraklat menüsünden aç. Çok sayıda statik mesh'te büyük kazanç sağlar.

---

## 5. Sorun giderme
- **Pembe malzemeler:** URP atanmamış. *Most Wanted → URP Kurulumunu Yap* menüsünü çalıştır.
- **Input hatası:** *Project Settings → Player → Active Input Handling = Both*.
- **URP paket sürümü hatası:** Package Manager'da *Universal RP* paketini editörün önerdiği sürüme güncelle.
- **Araçlar prosedürel kutu görünüyor:** paket içe aktarılmamış veya tarama yapılmamış → *Most Wanted → Arabaları Tara*.
- **Harita yüklenmiyor:** glTFast kurulu mu? `Assets/Maps/city_3d_model.glb` var mı? *Most Wanted → Haritaları Tara*.

---

## 6. Emeği Geçenler
- Araçlar: "Car Asset Pack for Arcade & Demolition Racing Games" — **Store InvoGames** (Fab, Standard License)
- Harita: "City 3D Model" — **Optic Idealist** (Fab/Sketchfab), **CC BY 4.0**
- Oyun kodu, sesler, test şehri: prosedürel (bu proje)
- Sürüş fiziği (normal): **Randomation Vehicle Physics** — Justin Couch (JustInvoke), MIT — `Assets/ThirdParty/RVP/LICENSE.txt`
- Sürüş fiziği (drift): **Arcade Car Physics** — Saarg, MIT — `Assets/ThirdParty/ArcadeCarPhysics/LICENSE.md`

## 7. Dosyalar
`Assets/Scripts`: `Game` (başlatma, ışık, post, menüler), `Car`/`CarBuilder`/`CarMath` (fizik, model oturtma),
`PlayerDriver` (girdi, speedbreaker, nitro doldurma), `CameraRig`, `AI` (trafik, polis, rakip, helikopter, çivili şerit,
pursuit breaker), `Police`, `Race`, `Career` (kara liste), `City` (test şehri), `World`/`BakedWorld` (harita, yol ağı),
`OptimizationManager`, `EngineAudio` (motor sesi, ses grupları, müzik), `MapDressing` (harita süsleme), `HUD`, `AudioSynth`, `Data`/`CarRegistry`/`MapRegistry`.
`Assets/Shaders`: su ve bulut shader'ları. `Assets/Editor/MWSetup.cs`: URP kurulumu + araç/harita tarama. `Tools/physics_sim`: fizik testi. `Tools/map_bake`: yol ağı üretimi.
