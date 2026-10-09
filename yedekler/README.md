# Yedekler

## most-wanted-yedek-d392cd3.zip
Gece güncellemelerinden ÖNCEKİ çalışan sürümün tam yedeği (commit `d392cd3`, 9 Ekim 2026).

Geri dönmek istersen iki yol:

1. **Zip ile:** zip'i aç, içindeki `most-wanted-unity/Assets` klasörünü projendeki `Assets` klasörünün üstüne kopyala.
2. **Terminal ile** (`most-wanted-unity` klasöründeyken):
   ```
   curl -L -o /tmp/yedek.zip https://github.com/schoftz/Test-i9jlnr/raw/claude/car-driving-game-macos-od30rc/yedekler/most-wanted-yedek-d392cd3.zip && rm -rf /tmp/yedek && unzip -q /tmp/yedek.zip -d /tmp/yedek && rm -rf Assets/Scripts Assets/ThirdParty Assets/Shaders && rsync -a /tmp/yedek/most-wanted-unity/Assets/ Assets/ && echo YEDEK GERI YUKLENDI
   ```
