# Gece Raporu — 9 Ekim 2026

## Yedek
`yedekler/most-wanted-yedek-d392cd3.zip` — gece öncesi çalışan sürüm. Geri dönüş komutu: `yedekler/README.md`.

## Güncelleme
`most-wanted-unity` klasöründe:
```
curl -L -o /tmp/mw.zip https://github.com/schoftz/Test-i9jlnr/archive/refs/heads/claude/car-driving-game-macos-od30rc.zip && rm -rf /tmp/mw && unzip -q /tmp/mw.zip -d /tmp/mw && rsync -a /tmp/mw/*/most-wanted-unity/Assets/ Assets/ && echo TAMAM
```

## Gece eklenenler
| Alan | Yenilikler |
|---|---|
| Işık & atmosfer | Gün saatine göre güneş rengi/gücü, altın saat, SSAO, yükseklik sisi, güneş ışınları, lens parlaması, gece ay/yıldız, FXAA/SMAA/TAA |
| Arabalar | Metalik pullu + vernikli boya, yansıyan cam, lastik/jant malzemeleri, araç altı gölge |
| Şehir | Prosedürel bina cepheleri (pencereler, perdeler, tuğla/beton/cam, gece ışıkları, vitrin/neon), gerçekçi asfalt + ıslak zemin birikintileri, çim/ağaç/su köpüğü, sokak lambaları + ışık havuzları |
| Arayüz | Yükleme ekranı (ipuçları), sinematik açılış ekranı + ana menü |
| Trafik | Kavisli kavşak dönüşleri, sinyal, parlak fren lambaları, görüş dışında doğma, bölgeye göre yoğunluk |
| Hikaye | Giriş + 5 Kara Liste bölümü + final, ara sahneler, SMS, görev takipçisi |

## Bilinmesi gerekenler
- Hepsi derleniyor ama Unity'de çalıştırılarak doğrulanmadı.
- Shader çalışmazsa otomatik olarak eski (Lit) malzemeye dönülür; tam ekran atmosfer efekti desteklenmezse kapanır.
- Yükleme 25 sn ilerlemezse ekran kapanır ve hata Console'a yazılır.
- Kasarsa: Esc → Ayarlar → Grafik → Orta/Düşük.
