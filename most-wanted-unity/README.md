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
- **Garaj (E):** satın al, %60'a sat, seç, boya, 7 performans paketi (Motor, Turbo, Şanzıman, Süspansiyon, Lastik,
  Nitro, Fren; her biri 3 seviye). İlerleme PlayerPrefs'e JSON olarak kaydedilir.

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
- MSAA kapalı (FXAA/SMAA), SRP Batcher açık, GPU Resident Drawer (Unity 6) açılmaya çalışılır, uzak trafik fiziği
  kapatılır (kinematik), minimap 15 fps'de çizilir, ağaç/direk gibi detaylar katman mesafesiyle erken kesilir.
- **Öneriler:**
  - Gerçek performans için **Build** al: *File → Build Profiles → macOS → Build And Run*. Editör, özellikle Retina
    Game görünümünde çok ek yük getirir.
  - Editörde oynarken Game görünümünde "Low Resolution Aspect Ratios" seç veya pencereyi küçült.
  - **Occlusion Culling** pişir (büyük şehirde ciddi kazanç): harita sahnede açıkken
    *Window → Rendering → Occlusion Culling → Bake*. (Harita çalışma anında yüklendiği için, istersen haritayı
    Main sahnesine sürükleyip statik işaretleyerek pişir.)
  - GPU Resident Drawer için: *Project Settings → Graphics → BatchRendererGroup Variants = Keep All* ve URP
    renderer'ında *Rendering Path = Forward+*.

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

## 7. Dosyalar
`Assets/Scripts`: `Game` (başlatma, ışık, post, menüler), `Car`/`CarBuilder`/`CarMath` (fizik, model oturtma),
`PlayerDriver` (girdi, speedbreaker, nitro doldurma), `CameraRig`, `AI` (trafik, polis, rakip, helikopter, çivili şerit,
pursuit breaker), `Police`, `Race`, `Career` (kara liste), `City` (test şehri), `World`/`BakedWorld` (harita, yol ağı),
`OptimizationManager`, `MapDressing` (harita süsleme), `HUD`, `AudioSynth`, `Data`/`CarRegistry`/`MapRegistry`.
`Assets/Shaders`: su ve bulut shader'ları. `Assets/Editor/MWSetup.cs`: URP kurulumu + araç/harita tarama. `Tools/physics_sim`: fizik testi. `Tools/map_bake`: yol ağı üretimi.
