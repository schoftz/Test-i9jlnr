# Most Wanted – Açık Dünya Sokak Yarışı (Unity)

Need for Speed: Most Wanted tarzında, tamamen kodla üretilen bir açık dünya sokak yarışı oyunu.
**Sahne kurmana gerek yok:** Projeyi aç, Play'e bas – şehir, arabalar, polis, ışıklar, sesler ve arayüz
çalışma anında otomatik oluşturulur. Hiçbir dış model/doku/ses dosyası yoktur.

## 1. Kurulum (MacBook)

1. **Unity Hub'ı indir:** https://unity.com/download adresinden macOS için Unity Hub'ı indir,
   `.dmg` dosyasını aç ve Unity Hub'ı *Applications* klasörüne sürükle.
2. Unity Hub'ı aç, Unity hesabınla giriş yap (ücretsiz *Personal* lisans yeterli).
3. **Installs → Install Editor** bölümünden **Unity 2022.3 LTS** (önerilen) ya da **Unity 6** sürümünü kur.
   Apple Silicon (M1/M2/M3/M4) Mac'lerde "Apple silicon" sürümünü seç. Ek modül gerekmez.
4. **Projects → Add → Add project from disk** ile bu klasörü (`most-wanted-unity`) seç.
   - Sürüm farkı uyarısı çıkarsa kurduğun sürümü seçip **Continue / Change Version**'a bas.
5. Proje açıldıktan sonra (ilk açılış birkaç dakika sürebilir) **Project** penceresinden
   `Assets/Scenes/Main.unity` sahnesini çift tıkla (boş bir sahnede de çalışır).
6. Üstteki **▶ Play** düğmesine bas. Hepsi bu!

> İpucu: Daha akıcı bir deneyim için Game penceresinde çözünürlüğü *Full HD (1920x1080)* yap
> ve "Play Maximized / Maximize On Play" seçeneğini aç.

### Olası sorunlar
- **"You are trying to read Input using the UnityEngine.Input class..." hatası:**
  *Edit → Project Settings → Player → Other Settings → Active Input Handling* değerini
  **Input Manager (Old)** veya **Both** yap; Unity yeniden başlar.
- **Pembe/mor malzemeler:** Proje Built-in Render Pipeline kullanır. URP/HDRP şablonu ile açmayın;
  *Project Settings → Graphics → Default Render Pipeline* boş (None) olmalı.
- `Assets/Resources/MW_Standard.mat` ve `MW_Sky.mat` dosyalarını **silme**. Bunlar "Standard" ve
  "Skybox/Procedural" shader'larının build'e dahil edilmesini garanti eder.
- Unity 6'da bazı "obsolete" uyarıları görebilirsin; zararsızdır.

## 2. Kontroller

| Tuş | İşlev |
|---|---|
| W / ↑ | Gaz |
| S / ↓ | Fren / geri vites |
| A D / ← → | Direksiyon |
| Space | El freni (drift) |
| Sol Shift | Nitro |
| C | Kovalama kamerası / tampon kamerası |
| E | Garaj (yeşil işaretin üzerindeyken) |
| J | İşler menüsü (yarışlar, teslimat) |
| R | Aracı düzelt (takla attıysan, yavaşken) |
| Esc | Duraklat menüsü / menüyü kapat |

## 3. Özellikler

- **Şehir:** 6x6 bloklu sokak ağı, kaldırımlar, gece pencereleri yanan binalar, sokak lambaları
  (gece yanan ışıklar), parklar, ağaçlar, şehrin dışında **çevre yolu**, uzak tepeler.
- **Gün/Gece döngüsü:** Prosedürel gökyüzü (Skybox/Procedural), hareket eden güneş/ay, gölgeler, sis.
  Duraklat menüsünden hızlı döngü açılabilir.
- **Sürüş:** WheelCollider fiziği, arcade/drift ayarı: el freniyle arka tutuş düşer, hıza duyarlı
  direksiyon, devrilme önleyici, downforce, nitro (FOV etkisiyle), prosedürel motor sesi.
- **Garaj (E):** 7 araç (fiyat ve istatistiklerle). Satın al, %60'a sat, seç, boya (8 renk),
  performans ayarı (Motor / Nitro / Yol Tutuş, 3 seviye). İlerleme PlayerPrefs'e JSON olarak kaydedilir.
- **Trafik:** Şeritte giden, kavşaklarda dönen, öndeki araca fren yapan sivil araçlar.
- **Polis takibi:** Polislerin yanında 95 km/sa üstü hız veya polise çarpma → aranma (1–5 yıldız).
  Kırmızı/mavi yanıp sönen ışıklar ve siren, yıldız arttıkça daha fazla ekip, 3+ yıldızda **barikat**.
  Düşük hızda polisler tarafından ~3 sn sıkıştırılırsan **YAKALANIRSIN** (para cezası).
  Görüş alanından çıkıp uzaklaşırsan **sakinleşme** çubuğu dolar → kaçarsın ve **ödül** kazanırsın.
- **Yarışlar (J):** 2 sprint, 2 tur yarışı; 3 YZ rakip, geri sayım, sıralama, tur, para ödülü.
- **Teslimat işleri (J):** Süreli paket teslimatı.
- **HUD:** Hız göstergesi, vites, nitro çubuğu, para, mini harita (ikinci ortografik kamera →
  RenderTexture), yıldızlar, sakinleşme çubuğu, bildirimler; duraklat menüsü.

## 4. Teknik notlar

- Tüm oyun `Assets/Scripts/Game.cs` içindeki
  `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]` ile başlar;
  hangi sahne açık olursa olsun Play'de kurulur (sahnedeki varsayılan kamera/ışık kaldırılır).
- Arayüz IMGUI (`OnGUI`) ile çizilir; ek paket gerekmez. Girdi: eski Input Manager (`Input.GetKey`).
- Kayıt: `PlayerPrefs` anahtarı `MW_SAVE_V1`. Sıfırlamak için Duraklat → "Kaydı Sıfırla".
- Dosyalar: `Util.cs` (yardımcılar), `Data.cs` (araç kataloğu, kayıt), `City.cs` (şehir + yol ağı),
  `Car.cs` (araç fiziği, araç üretimi, polis ışıkları), `Drivers.cs` (oyuncu, trafik, polis, yarışçı YZ),
  `Managers.cs` (trafik, polis, yarış, teslimat), `Game.cs` (başlatma, kamera, gün/gece),
  `HUD.cs` (arayüz ve menüler), `AudioSynth.cs` (prosedürel sesler).
