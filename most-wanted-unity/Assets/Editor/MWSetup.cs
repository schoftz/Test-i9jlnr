using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MostWanted.EditorTools
{
    /// <summary>
    /// Editör otomasyonu:
    ///  - URP boru hattı varlığını oluşturur ve atar (yoksa), renk uzayını Linear yapar.
    ///  - "Most Wanted/Arabaları Tara": araç prefablarını bulup Assets/Resources/CarRegistry.asset'e yazar.
    ///  - "Most Wanted/Haritaları Tara": Assets/Maps içindeki .glb/.gltf/.fbx/.prefab'ları MapRegistry'ye yazar.
    /// Proje açılınca ve yeni varlık içe aktarılınca otomatik çalışır.
    /// </summary>
    [InitializeOnLoad]
    public static class MWSetup
    {
        const string CarRegPath = "Assets/Resources/CarRegistry.asset";
        const string MapRegPath = "Assets/Resources/MapRegistry.asset";

        static MWSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                EnsureURP();
                ScanCars(false);
                ScanMaps(false);
            };
        }

        // ------------------------------------------------------------------ URP
        [MenuItem("Most Wanted/URP Kurulumunu Yap")]
        public static void EnsureURPMenu() { EnsureURP(); Debug.Log("[MW] URP kontrol edildi."); }

        public static void EnsureURP()
        {
            if (PlayerSettings.colorSpace != ColorSpace.Linear) PlayerSettings.colorSpace = ColorSpace.Linear;
            var current = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (current != null) { if (QualitySettings.renderPipeline == null) QualitySettings.renderPipeline = current; return; }
            if (!AssetDatabase.IsValidFolder("Assets/Settings")) AssetDatabase.CreateFolder("Assets", "Settings");
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/MW_URP.asset");
            if (asset == null)
            {
                var rd = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rd, "Assets/Settings/MW_URP_Renderer.asset");
                asset = UniversalRenderPipelineAsset.Create(rd);
                asset.renderScale = 0.7f;
                asset.shadowDistance = 120f;
                asset.shadowCascadeCount = 2;
                asset.supportsHDR = true;
                AssetDatabase.CreateAsset(asset, "Assets/Settings/MW_URP.asset");
                AssetDatabase.SaveAssets();
            }
            GraphicsSettings.defaultRenderPipeline = asset;
            int cur = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = asset;
            }
            QualitySettings.SetQualityLevel(cur, false);
            AssetDatabase.SaveAssets();
            Debug.Log("[MW] URP boru hattı oluşturuldu ve atandı: Assets/Settings/MW_URP.asset");
        }

        // ------------------------------------------------------------------ Arabalar
        struct Preset
        {
            public string name; public int price, drive; public float mass, torque, redline, top, grip, down, length; public string police; public bool traffic; public Color color;
        }

        // InvoGames paketi için Most Wanted tarzı isimler ve değerler (Car N -> değerler)
        static readonly Dictionary<string, Preset> Presets = new Dictionary<string, Preset>
        {
            { "car 3",  new Preset { name = "Kompakt Hatch", price = 0,      drive = 0, mass = 1200, torque = 260, redline = 6800, top = 205, grip = 1.00f, down = 0.8f, length = 4.1f,  traffic = true, color = new Color(0.9f,0.9f,0.9f) } },
            { "car 10", new Preset { name = "Sokak Coupe",   price = 15000,  drive = 1, mass = 1300, torque = 330, redline = 7200, top = 225, grip = 1.00f, down = 0.9f, length = 4.3f,  traffic = true, color = new Color(0.08f,0.2f,0.75f) } },
            { "car 1",  new Preset { name = "Tuner S",       price = 26000,  drive = 2, mass = 1350, torque = 400, redline = 7800, top = 245, grip = 1.05f, down = 1.0f, length = 4.4f,  traffic = true, color = new Color(0.95f,0.7f,0.05f) } },
            { "car 4",  new Preset { name = "Coupe RS",      price = 38000,  drive = 1, mass = 1380, torque = 450, redline = 7500, top = 255, grip = 1.06f, down = 1.0f, length = 4.45f, traffic = true, police = "patrol", color = new Color(0.75f,0.04f,0.04f) } },
            { "car 7",  new Preset { name = "Bulldog SUV",   price = 42000,  drive = 2, mass = 2100, torque = 650, redline = 6200, top = 235, grip = 0.95f, down = 0.7f, length = 4.9f,  traffic = true, police = "suv", color = new Color(0.55f,0.57f,0.6f) } },
            { "car 9",  new Preset { name = "Titan Pikap",   price = 46000,  drive = 2, mass = 2300, torque = 700, redline = 5800, top = 230, grip = 0.93f, down = 0.6f, length = 5.0f,  traffic = true, color = new Color(0.1f,0.6f,0.2f) } },
            { "car 2",  new Preset { name = "Muscle V8",     price = 50000,  drive = 1, mass = 1650, torque = 620, redline = 6500, top = 265, grip = 0.97f, down = 0.8f, length = 4.8f,  traffic = true, color = new Color(0.05f,0.05f,0.06f) } },
            { "car 8",  new Preset { name = "Drift Spec",    price = 62000,  drive = 1, mass = 1320, torque = 520, redline = 8200, top = 270, grip = 1.02f, down = 1.1f, length = 4.4f,  traffic = false, color = new Color(1f,0.4f,0f) } },
            { "car 6",  new Preset { name = "Street GT-R",   price = 85000,  drive = 2, mass = 1500, torque = 580, redline = 8000, top = 290, grip = 1.12f, down = 1.2f, length = 4.5f,  traffic = false, police = "undercover", color = new Color(0.5f,0.1f,0.65f) } },
            { "car 5",  new Preset { name = "Süper Kanat",   price = 120000, drive = 1, mass = 1450, torque = 650, redline = 8800, top = 315, grip = 1.16f, down = 1.4f, length = 4.55f, traffic = false, color = new Color(0.1f,0.75f,0.85f) } },
        };

        [MenuItem("Most Wanted/Arabaları Tara")]
        public static void ScanCarsMenu() { ScanCars(true); }

        static bool IsCarFolder(string path)
        {
            string p = path.ToLowerInvariant();
            if (p.Contains("/demo") || p.Contains("/scenes/")) return false;
            return p.Contains("/cars/") || p.Contains("/vehicles/") || p.Contains("/araclar/") || p.Contains("invogames") || p.StartsWith("assets/resources/cars");
        }

        static bool LooksLikeCar(GameObject go)
        {
            if (go == null || go.GetComponentInChildren<Renderer>(true) == null) return false;
            int wheels = 0;
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                string n = t.name.ToLowerInvariant();
                if ((n.Contains("wheel") || n.Contains("tyre") || n.Contains("tire")) && !n.Contains("steering")) wheels++;
            }
            return wheels >= 2;
        }

        public static void ScanCars(bool verbose)
        {
            var found = new List<string>();
            var names = new HashSet<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!IsCarFolder(path)) continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!LooksLikeCar(go)) continue;
                found.Add(path); names.Add(Path.GetFileNameWithoutExtension(path).ToLowerInvariant());
            }
            foreach (var guid in AssetDatabase.FindAssets("t:Model"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.ToLowerInvariant().StartsWith("assets/resources/cars") && !path.ToLowerInvariant().Contains("/cars/")) continue;
                if (names.Contains(Path.GetFileNameWithoutExtension(path).ToLowerInvariant())) continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null || go.GetComponentInChildren<Renderer>(true) == null) continue;
                found.Add(path);
            }

            var reg = AssetDatabase.LoadAssetAtPath<MostWanted.CarRegistry>(CarRegPath);
            bool created = false;
            if (reg == null)
            {
                if (found.Count == 0) { if (verbose) Debug.Log("[MW] Araç prefabı bulunamadı; prosedürel araçlar kullanılacak."); return; }
                if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
                reg = ScriptableObject.CreateInstance<MostWanted.CarRegistry>();
                AssetDatabase.CreateAsset(reg, CarRegPath);
                created = true;
            }
            int added = 0;
            foreach (var path in found)
            {
                string file = Path.GetFileNameWithoutExtension(path);
                string id = file.ToLowerInvariant().Replace(" ", "_");
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                var e = reg.cars.Find(c => c.id == id);
                if (e != null) { if (e.prefab == null) e.prefab = go; continue; }
                e = new MostWanted.CarEntry { id = id, displayName = file, prefab = go };
                Preset pr;
                if (Presets.TryGetValue(file.ToLowerInvariant(), out pr) && path.ToLowerInvariant().Contains("invogames"))
                {
                    e.displayName = pr.name; e.price = pr.price; e.drive = pr.drive; e.massKg = pr.mass; e.torqueNm = pr.torque;
                    e.redlineRpm = pr.redline; e.topSpeedKmh = pr.top; e.grip = pr.grip; e.downforce = pr.down; e.length = pr.length;
                    e.policeRole = pr.police ?? ""; e.inTraffic = pr.traffic; e.defaultColor = pr.color;
                    e.credit = "Store InvoGames — Car Asset Pack for Arcade & Demolition Racing Games (Standard License)";
                }
                else
                {
                    e.price = 30000 + reg.cars.Count * 8000;
                    e.credit = "";
                }
                reg.cars.Add(e);
                added++;
            }
            // silinmiş prefabları temizle
            int removed = reg.cars.RemoveAll(c => c.prefab == null && !string.IsNullOrEmpty(c.id) && !c.id.StartsWith("p_"));
            if (added > 0 || removed > 0 || created)
            {
                reg.cars.Sort((a, b) => a.price.CompareTo(b.price));
                EditorUtility.SetDirty(reg);
                AssetDatabase.SaveAssets();
                Debug.Log("[MW] Araç kaydı güncellendi: +" + added + " / -" + removed + " (toplam " + reg.cars.Count + "). Değerleri Assets/Resources/CarRegistry.asset içinde düzenleyebilirsin.");
            }
            else if (verbose) Debug.Log("[MW] Araç kaydı güncel (" + reg.cars.Count + " araç).");
            if (verbose) Selection.activeObject = reg;
        }

        // ------------------------------------------------------------------ Haritalar
        [MenuItem("Most Wanted/Haritaları Tara")]
        public static void ScanMapsMenu() { ScanMaps(true); }

        public static void ScanMaps(bool verbose)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Maps")) { if (verbose) Debug.Log("[MW] Assets/Maps klasörü yok."); return; }
            var found = new List<string>();
            foreach (var f in Directory.GetFiles("Assets/Maps", "*.*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext == ".glb" || ext == ".gltf" || ext == ".fbx" || ext == ".prefab" || ext == ".obj") found.Add(f.Replace('\\', '/'));
            }
            var reg = AssetDatabase.LoadAssetAtPath<MostWanted.MapRegistry>(MapRegPath);
            if (reg == null)
            {
                if (found.Count == 0) return;
                if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
                reg = ScriptableObject.CreateInstance<MostWanted.MapRegistry>();
                AssetDatabase.CreateAsset(reg, MapRegPath);
            }
            int added = 0;
            foreach (var path in found)
            {
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;
                if (reg.maps.Exists(m => m.prefab == go)) continue;
                string file = Path.GetFileNameWithoutExtension(path);
                string credit = file.ToLowerInvariant().Contains("city_3d_model") ? "\"City 3D Model\" — Optic Idealist (Fab), CC BY 4.0" : "";
                reg.maps.Add(new MostWanted.MapEntry { name = file, prefab = go, credit = credit });
                added++;
            }
            int removed = reg.maps.RemoveAll(m => m.prefab == null);
            if (added > 0 || removed > 0)
            {
                EditorUtility.SetDirty(reg);
                AssetDatabase.SaveAssets();
                Debug.Log("[MW] Harita kaydı güncellendi: " + reg.maps.Count + " harita.");
            }
            else if (verbose) Debug.Log("[MW] Harita kaydı güncel (" + reg.maps.Count + ").");
        }
    }

    /// <summary>Yeni araç/harita içe aktarılınca otomatik tara.</summary>
    public class MWAssetWatcher : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            bool cars = false, maps = false;
            foreach (var arr in new[] { imported, deleted, moved })
                foreach (var p in arr)
                {
                    string l = p.ToLowerInvariant();
                    if (l.EndsWith("registry.asset")) continue;
                    if (l.StartsWith("assets/maps/")) maps = true;
                    if (l.EndsWith(".prefab") || l.EndsWith(".fbx") || l.EndsWith(".glb")) cars = true;
                }
            if (!cars && !maps) return;
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode) return;
                if (cars) MWSetup.ScanCars(false);
                if (maps) MWSetup.ScanMaps(false);
            };
        }
    }
}
