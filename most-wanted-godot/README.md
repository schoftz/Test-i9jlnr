# Most Wanted — Godot 4 Açık Dünya Sokak Yarışı (v2)

Need for Speed: Most Wanted tarzında, kodla üretilen küçük bir **ülke** haritasında geçen sokak yarışı oyunu. Godot 4.3 ve üzeri (4.7.2 ile test edildi), GDScript, Forward+ renderer.

## Mac'e Godot Kurulumu

1. https://godotengine.org/download/macos/ adresinden **Godot Engine** (standart sürüm, .NET olmayan) 4.3 veya daha yenisini indir.
2. `.zip` dosyasını aç, `Godot.app` dosyasını **Uygulamalar (Applications)** klasörüne sürükle.
3. İlk açılışta "geliştirici doğrulanamadı" uyarısı çıkarsa: `Godot.app` → sağ tık → **Aç** (veya *Sistem Ayarları → Gizlilik ve Güvenlik → Yine de Aç*).

## Projeyi Açma

1. Godot'yu aç → Proje Yöneticisi → **İçe Aktar (Import)** → `most-wanted-godot/project.godot` → **İçe Aktar ve Düzenle**.
2. **F5** (veya sağ üstteki ▶) ile başlat.

## Kontroller

| Tuş | İşlev |
|---|---|
| W / ↑ | Gaz |
| S / ↓ | Fren / geri vites |
| A D / ← → | Direksiyon |
| Boşluk | El freni (drift) |
| Shift | Nitro |
| C | Kamera (takip / tampon / uzak) |
| E | Garaj (Merkez'deki sarı halkada) |
| J | Yarışlar ve işler |
| M / Tab | Büyük harita (fare tekeri / + - ile yakınlaştır, sürükleyerek kaydır) |
| F | FPS göstergesi |
| R | Aracı en yakın yola geri koy |
| Esc | Duraklat menüsü (grafik kalitesi, gün döngüsü, emeği geçenler) |

## Harita

- **Merkez**: gökdelenli şehir merkezi (garaj burada).
- **Sahilkent**: deniz kenarında alçak, renkli evli kasaba; kumsal boyunca **Sahil Yolu**.
- **Dağköy**: tepelerin arasında köy.
- **O-1 otoyolu**: Merkez ↔ Sahilkent, nehrin üzerinden kuleli **asma köprü**.
- **O-2 otoyolu**: Merkez ↔ Dağköy, dağın içinden geçen ışıklı **tünel**.
- **O-3 otoyolu**: Dağköy ↔ Sahilkent, **kemer köprü**.
- Otoyollar 2x3 şeritli, ortası bariyerli, kenarları korkuluklu ve aydınlatma direkli. Uçlarında yön tabelaları var. Her şehre **dönel kavşak** bağlantısıyla girilip çıkılır.
- **Eski yol**: Merkez'den Dağköy'e kırsal iki şeritli yol. Arada tepeler, tarlalar ve ağaçlar var.
- Oyun gündüz başlar. Gece kısa sürer (4 kat hızlı geçer). Duraklat menüsünden gün döngüsü kapatılırsa hep gündüz olur.

## Araçlar

| Garajdaki araç | Model dosyası adı (`models/custom/` içine) |
|---|---|
| Volkswagen Golf GTI Mk5 (başlangıç aracı) | `golf_gti.glb` |
| Mitsubishi Lancer Evolution IX | `evo_ix.glb` |
| Nissan Skyline GT-R R34 | `skyline_r34.glb` |
| BMW M3 GTR (E46) | `bmw_m3.glb` |
| Porsche 911 GT3 (997) | `porsche_911.glb` |
| Mercedes-AMG GT | `amg_gt.glb` |
| Audi R8 V10 | `audi_r8.glb` |
| Lamborghini Gallardo | `gallardo.glb` |
| Polis Ford Crown Victoria (polis araçları) | `police_cvpi.glb` |

Model dosyası yoksa her araç, kendi silüetine göre (hatchback / sedan / coupe / 911 / uzun kaput / süper spor / kama) kodla üretilmiş yumuşak hatlı bir gövdeyle gelir. Bu gövdelerde çamurluk boşlukları, far/stop şekilleri ve kanat bulunur. **Gerçek görünüm için kendi GLB modelini eklemelisin:**

### Sketchfab'dan ücretsiz model ekleme (adım adım)

1. https://sketchfab.com adresine git ve ücretsiz üye ol.
2. Aracı ara (ör. "Nissan Skyline R34"). Arama sonuçlarında **Downloadable** filtresini aç. Lisansın *CC Attribution* veya *CC0* olduğuna bak.
3. Modelin sayfasında **Download 3D Model** → **glTF** (veya **GLB**) formatını indir.
4. **.glb** indirdiysen dosyanın adını tablodaki ada çevir (ör. `skyline_r34.glb`).
   **.gltf** (zip) indirdiysen zip'i aç, `scene.gltf` dosyasını `skyline_r34.gltf` yap. `scene.bin` ve `textures` klasörünü de aynı klasörde bırak.
5. Dosyayı `most-wanted-godot/models/custom/` klasörüne koy.
6. Godot'yu kapatıp projeyi yeniden aç (veya editörde dosyaların içe aktarılmasını bekle), sonra F5.

Notlar:
- Model otomatik olarak ölçeklenir (araç boyuna göre), ortalanır ve yere oturtulur.
- İsminde *wheel / tire / rim / tekerlek* geçen parçalar gizlenir; yerlerine dönen tekerlekler konur. Böyle parçalar yoksa modelin kendi tekerlekleri kalır.
- Model ters yöne bakıyorsa dosya adının sonuna `_ters` ekle: `bmw_m3_ters.glb`.
- Malzeme adında *paint/body* geçen modellerde garajdaki boya seçimi çalışır.
- `models/custom/` içine tablodakilerden farklı isimle bir model koyarsan (ör. `supra.glb`), garajda **"Özel: Supra"** olarak yeni bir araç çıkar.

### Araç paketleri (tek dosyada birçok araç)

Sketchfab'daki "car pack" modellerini (ör. *Low Poly 22 Sedan Pack*) `models/packs/` klasörüne koy (ör. `models/packs/sedan_pack.glb`). Paketteki her araç garajda ayrı bir araç olarak görünür (adı model içindeki düğüm adından gelir). Bu araçlar trafikte ve rakiplerde de kullanılır.

### Lisans / atıf

CC-BY modeller kullanırsan `models/CREDITS.txt` dosyasına model adını, yazarını ve linkini yaz. Bu metin oyunda **Duraklat → Emeği Geçenler** ekranında görünür. Oyunun kendisinde hazır gelen dış model yoktur; her şey kodla üretilir.

## Özellikler

- **Fizik:** VehicleBody3D + VehicleWheel3D, arcade ayarlı. El freniyle drift, hıza duyarlı direksiyon, araca göre önden/arkadan/dört çeker. Nitro FOV artışı, sarsıntı ve alev efekti yapar; drift ve yüksek hız nitroyu doldurur.
- **Garaj:** Araç satın alma, %60 fiyatına satma, seçme, 10 boya rengi, motor / nitro / yol tutuş yükseltmeleri (3'er seviye). Kayıt dosyası `user://most_wanted_save.json`.
- **Trafik:** Şehirde ve otoyolda (3 şerit) şeridini koruyan, kavşaklarda dönen, öndeki araca göre yavaşlayan araçlar.
- **Polis:** 1–5 yıldız aranma seviyesi, siren ve tepe lambaları. Polisler önünü kesip çarpar, 3+ yıldızda barikat kurar (otoyolda 5 araçlık). Yakalanırsan ceza ödersin; görüşten çıkıp soğuma süresini doldurursan ödül alırsın.
- **Yarışlar (J):** Merkez sprint/devre, Asma Köprü sprinti, Tünel sprinti, Eski Yol sprinti, Sahil Yolu sprinti ve tüm otoyollardan geçen **Ülke Turu**. 3 rakip, geri sayım, sıralama, para ödülü. Ayrıca zamana karşı **teslimat işleri** var.
- **HUD:** Hız, nitro, para, yıldızlar, takip/soğuma çubuğu, dönen mini harita, büyük harita (M/Tab), FPS (F).

## Performans (MacBook / Retina)

- Duraklat menüsünde **Grafik: Düşük / Orta / Yüksek** seçenekleri var (varsayılan: Orta).
- Retina ekranlarda 3B çözünürlük otomatik düşürülür ve FSR ile ölçeklenir. Gölgeler kısa mesafeli tutulur, SSR kapalı, SSAO yalnızca Yüksek'te açık. Kenar yumuşatma FXAA ile yapılır, VSync açık.
- Ağaçlar, lambalar, köprü kabloları ve direkler parçalara (chunk) bölünmüş MultiMesh olarak çizilir ve uzaklık sınırları vardır. Uzaktaki trafik araçlarında fizik kapatılır, araçlar yalnızca yol boyunca kaydırılır.
- Hâlâ yavaşsa **Düşük** kaliteyi seç. FPS'i F tuşuyla görebilirsin.

## Bilinen Sınırlamalar

- Prosedürel araç gövdeleri silüete benzer ama fotogerçekçi değildir. Gerçek görünüm için GLB ekle.
- Otoyol girişleri ayrı rampalar yerine dönel kavşaklarla yapılır.
- Yol dışındaki tepelerden geçmek mümkündür ama zor; R tuşu seni yola geri koyar.
