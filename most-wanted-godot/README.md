# Most Wanted — Godot 4 Sokak Yarışı

Need for Speed: Most Wanted tarzında, tamamen **prosedürel** (hiçbir dış model, doku veya ses dosyası olmadan) üretilen açık dünya sokak yarışı oyunu. Godot 4.3+ ve GDScript ile yazıldı, Forward+ renderer kullanır.

## Mac'e Godot Kurulumu

1. https://godotengine.org/download/macos/ adresine git.
2. **Godot Engine** (standart sürüm, .NET olmayan) 4.3 veya daha yeni sürümü indir.
3. İnen `.zip` dosyasını aç, çıkan `Godot.app` dosyasını **Uygulamalar (Applications)** klasörüne sürükle.
4. İlk açılışta macOS "geliştirici doğrulanamadı" derse: Uygulamalar klasöründe `Godot.app`'e **sağ tıkla → Aç** de ya da *Sistem Ayarları → Gizlilik ve Güvenlik* bölümünden "Yine de Aç"a bas.

## Projeyi Açma ve Çalıştırma

1. Godot'yu aç. Proje Yöneticisi'nde **İçe Aktar (Import)** düğmesine tıkla.
2. `most-wanted-godot` klasöründeki `project.godot` dosyasını seç, **İçe Aktar ve Düzenle**.
3. Editör açılınca **F5** (Mac'te `Cmd+B` de olur) ya da sağ üstteki ▶ düğmesine bas.

İlk açılışta Godot kısa bir içe aktarma yapar; şehir oyun başlarken kodla üretildiği için yükleme birkaç saniye sürebilir.

## Kontroller

| Tuş | İşlev |
|---|---|
| W / ↑ | Gaz |
| S / ↓ | Fren / Geri vites |
| A D / ← → | Direksiyon |
| Boşluk | El freni (drift) |
| Shift | Nitro |
| C | Kamera değiştir (takip / tampon / uzak) |
| E | Garaja gir (sarı halkanın içindeyken) |
| J | Yarışlar ve işler menüsü |
| R | Aracı düzelt (takla atarsan) |
| Esc | Duraklat menüsü |

## Özellikler

- **Şehir:** 8x8 kavşaklı sokak ızgarası, kaldırımlar, farklı yükseklikte binalar (geceleri ışıklı pencereler), sokak lambaları, ağaçlar, parklar, şehri çevreleyen geniş **otoban halkası**, bariyerler ve şehir dışı tepeler.
- **Grafik:** WorldEnvironment ile glow/bloom, SSAO, SSR, ACES tonemapping, sis; güneşin döndüğü gece/gündüz döngüsü (8 dakikada bir gün) ve gölgeler. Oyun gece başlar.
- **Araç fiziği:** VehicleBody3D + VehicleWheel3D, arcade ayarlı; el freniyle drift, hıza duyarlı direksiyon, nitro (FOV artışı, kamera sarsıntısı, mavi alev parçacıkları). Drift ve yüksek hız nitroyu doldurur.
- **Araçlar:** 7 araç (Kompakt GT'den Efsane Carrera GT'ye), fiyat ve istatistiklerle. Garajda satın alma, %60'a satma, seçme, boya ve motor / nitro / yol tutuş yükseltmeleri (3'er seviye).
- **Kayıt:** Para, araçlar ve istatistikler `user://most_wanted_save.json` dosyasına otomatik kaydedilir (Mac'te `~/Library/Application Support/Godot/app_userdata/Most Wanted - Sokak Yarışı/`). Sıfırdan başlamak için bu dosyayı sil.
- **Trafik:** Sağ şeritte giden, kavşaklarda dönen, öndeki araca göre fren yapan yapay zeka araçları.
- **Polis:** Polis yakınında 110 km/s üstü hız veya polise çarpmak takibi başlatır. 1–5 yıldız aranma seviyesi, yanıp sönen kırmızı/mavi tepe lambaları ve siren. Polisler önünü kesip çarpmaya çalışır; seviye yükseldikçe birim sayısı artar, 3+ yıldızda **barikat** kurulur. Etrafın sarılı ve yavaşken ~3 sn = **YAKALANDIN** (ceza). Görüş dışına çıkınca soğuma sayacı başlar; dolarsa **KAÇTIN** ve ödül parası.
- **Yarışlar (J):** 2 sprint + 3 devre yarışı, 3 yapay zeka rakip, geri sayım, sıra, tur, süre ve para ödülü (1.: tam, 2.: %40, 3.: %15).
- **İşler:** Zamana karşı teslimat görevleri.
- **HUD:** Hız göstergesi, nitro çubuğu, para, aranma yıldızları, takip/soğuma çubuğu, dönen mini harita (polis, trafik, rakipler, garaj ve hedef), bildirimler.
- **Ses:** AudioStreamGenerator ile gerçek zamanlı üretilen motor sesi (vites simülasyonlu) ve polis sireni.

## Dosya Yapısı

```
project.godot          Proje ayarları + giriş haritası
icon.svg               Simge
scenes/main.tscn       Ana sahne (her şey koddan kurulur)
scripts/game_data.gd   Global veri (autoload "Game"): araç kataloğu, para, kayıt
scripts/main.gd        Ortam, gece/gündüz, kamera, trafik yönetimi
scripts/city.gd        Prosedürel şehir + yol grafiği
scripts/car_base.gd    Ortak araç fiziği ve prosedürel araç modeli
scripts/player_car.gd  Oyuncu kontrolü, nitro
scripts/ai_car.gd      Trafik / polis / yarışçı yapay zekası
scripts/police_manager.gd  Aranma seviyesi, takip, barikat
scripts/race_manager.gd    Yarışlar ve teslimat işleri
scripts/hud.gd         Arayüz, mini harita, garaj/duraklat/yarış menüleri
scripts/engine_audio.gd    Prosedürel motor sesi ve siren
```

## Bilinen Sınırlamalar

- Araç ve binalar basit kutu şekillerinden oluşur (dış model kullanılmadığı için).
- Yapay zeka sürücüleri yol grafiğini takip eder; sert çarpışmalardan sonra bazen takılıp geri vitese geçmeleri gerekir.
- Eski / zayıf Mac'lerde düşük FPS olursa `scripts/main.gd` içinde `env.ssr_enabled` ve `env.ssao_enabled` değerlerini `false` yapabilirsin.
