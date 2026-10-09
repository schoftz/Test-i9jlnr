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
            if (current != null)
            {
                if (QualitySettings.renderPipeline == null) QualitySettings.renderPipeline = current;
                ConfigureForGRD(current);
                EnsureRenderFeatures(current);
                return;
            }
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
            ConfigureForGRD(asset);
            EnsureRenderFeatures(asset);
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

        /// <summary>
        /// GPU Resident Drawer için: renderer'lar Forward+ olmalı ve BatchRendererGroup varyantları korunmalı.
        /// Sürüm farklarına karşı yansıma ile yapılır.
        /// </summary>
        static void ConfigureForGRD(UniversalRenderPipelineAsset asset)
        {
            try
            {
                var f = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var list = f != null ? f.GetValue(asset) as ScriptableRendererData[] : null;
                if (list != null)
                    foreach (var rd in list)
                    {
                        if (rd == null) continue;
                        var p = rd.GetType().GetProperty("renderingMode");
                        if (p == null || !p.PropertyType.IsEnum) continue;
                        string cur = p.GetValue(rd, null).ToString();
                        if (cur.Contains("Plus") || cur.StartsWith("Deferred")) continue;
                        if (System.Enum.IsDefined(p.PropertyType, "ForwardPlus"))
                        {
                            p.SetValue(rd, System.Enum.Parse(p.PropertyType, "ForwardPlus"), null);
                            EditorUtility.SetDirty(rd);
                            Debug.Log("[MW] URP renderer Forward+ yapıldı (GPU Resident Drawer için).");
                        }
                    }
                var egs = System.Type.GetType("UnityEditor.Rendering.EditorGraphicsSettings, UnityEditor");
                var prop = egs != null ? egs.GetProperty("batchRendererGroupShaderStrippingMode", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static) : null;
                if (prop != null && prop.PropertyType.IsEnum && System.Enum.IsDefined(prop.PropertyType, "KeepAll"))
                {
                    var keep = System.Enum.Parse(prop.PropertyType, "KeepAll");
                    if (!prop.GetValue(null, null).Equals(keep)) { prop.SetValue(null, keep, null); Debug.Log("[MW] BatchRendererGroup varyantları: Keep All"); }
                }
                // GRD varsayılan kapalı (Keep All ayarı yapılmadıysa uyarı verir); oyun içinden açılabilir
                var gp = typeof(UniversalRenderPipelineAsset).GetProperty("gpuResidentDrawerMode");
                if (gp != null && gp.PropertyType.IsEnum && System.Enum.IsDefined(gp.PropertyType, "Disabled") && !gp.GetValue(asset, null).ToString().Equals("Disabled"))
                {
                    gp.SetValue(asset, System.Enum.Parse(gp.PropertyType, "Disabled"), null);
                    EditorUtility.SetDirty(asset);
                    Debug.Log("[MW] GPU Resident Drawer varsayılan olarak kapatıldı.");
                }
                AssetDatabase.SaveAssets();
            }
            catch (System.Exception e) { Debug.LogWarning("[MW] GRD ayarı yapılamadı: " + e.Message); }
        }

        // ------------------------------------------------------------------ Grafik (Render ajanı)
        /// <summary>
        /// Renderer verisine SSAO ve MW atmosfer (tam ekran yükseklik sisi + güneş huzmeleri) özelliklerini ekler (yoksa),
        /// varsayılan gölge kaskad/bias ayarlarını yapar. Çalışma zamanında kalite ön ayarına göre RenderSetup açar/kapatır.
        /// URP sürüm farklarına karşı yansıma ile.
        /// </summary>
        static void EnsureRenderFeatures(UniversalRenderPipelineAsset asset)
        {
            try
            {
                MostWanted.Render.RenderSetup.PipelineQuality(asset, 1);
                EditorUtility.SetDirty(asset);
                var atmMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Resources/Render/MW_SkyAtmosphereMat.mat");
                foreach (var rd in MostWanted.Render.RenderSetup.RendererDatas(asset))
                {
                    bool hasSsao = false, hasAtm = false;
                    foreach (var f in rd.rendererFeatures)
                    {
                        if (f == null) continue;
                        if (f.GetType().Name == "ScreenSpaceAmbientOcclusion") hasSsao = true;
                        if (f.name == MostWanted.Render.RenderSetup.MWAtmosphereFeatureName) hasAtm = true;
                    }
                    if (!hasSsao) AddFeature(rd, "UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion", "SSAO", null);
                    if (!hasAtm && atmMat != null)
                        AddFeature(rd, "UnityEngine.Rendering.Universal.FullScreenPassRendererFeature", MostWanted.Render.RenderSetup.MWAtmosphereFeatureName, feat =>
                        {
                            MostWanted.Render.RR.Set(feat, "passMaterial", atmMat);
                            MostWanted.Render.RR.Set(feat, "injectionPoint", "BeforeRenderingPostProcessing");
                            MostWanted.Render.RR.Set(feat, "requirements", "Depth");
                            MostWanted.Render.RR.Set(feat, "fetchColorBuffer", true);
                            MostWanted.Render.RR.Set(feat, "bindDepthStencilAttachment", false);
                            MostWanted.Render.RR.Set(feat, "passIndex", 0);
                        });
                }
                AssetDatabase.SaveAssets();
            }
            catch (System.Exception e) { Debug.LogWarning("[MW] Renderer özellikleri eklenemedi: " + e.Message); }
        }

        static void AddFeature(ScriptableRendererData rd, string typeName, string name, System.Action<ScriptableRendererFeature> cfg)
        {
            var t = typeof(UniversalRendererData).Assembly.GetType(typeName);
            if (t == null) { Debug.LogWarning("[MW] URP özelliği bulunamadı: " + typeName); return; }
            var feat = ScriptableObject.CreateInstance(t) as ScriptableRendererFeature;
            if (feat == null) return;
            feat.name = name;
            if (cfg != null) cfg(feat);
            AssetDatabase.AddObjectToAsset(feat, rd);
            string guid; long localId;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feat, out guid, out localId);
            rd.rendererFeatures.Add(feat);
            var mapF = typeof(ScriptableRendererData).GetField("m_RendererFeatureMap", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var map = mapF != null ? mapF.GetValue(rd) as List<long> : null;
            if (map != null) map.Add(localId);
            rd.SetDirty();
            EditorUtility.SetDirty(rd);
            Debug.Log("[MW] URP renderer özelliği eklendi: " + name);
        }

        // ------------------------------------------------------------------ Arabalar
        struct Preset
        {
            public string name; public int price, drive, gears, hp; public float mass, torque, redline, top, grip, down, length, zero100;
            public string police; public bool traffic; public Color color; public string eng; public bool turbo; public int turboSize; public bool sc;
        }

        // InvoGames paketi: FBX kaynak yollarından (D:\\Cars\\<marka>\\...) çıkarılan gerçek modeller ve gerçek dünya verileri.
        // Fiyatlar oyun içi ilerleme eğrisine göre (performans kademesi), README'deki tabloya bak. Tork/devir/vites/ağırlık üreticiden; 0-100 bilgi amaçlı.
        static readonly Dictionary<string, Preset> Presets = new Dictionary<string, Preset>
        {
            { "car 3",  new Preset { name = "Mini John Cooper Works",        price = 0,       drive = 0, gears = 8,  hp = 231,  mass = 1300, torque = 320,  redline = 6500,  top = 246, zero100 = 6.1f, grip = 1.00f, down = 0.8f, length = 3.9f,  traffic = true,  eng = "I4",  turbo = true, turboSize = 1, color = new Color(0.8f,0.05f,0.05f) } },
            { "car 1",  new Preset { name = "Dodge Challenger SRT Hellcat",  price = 60000,   drive = 1, gears = 8,  hp = 717,  mass = 1950, torque = 889,  redline = 6200,  top = 315, zero100 = 3.6f, grip = 1.15f, down = 0.8f, length = 5.0f,  traffic = true,  eng = "V8",  sc = true, police = "patrol", color = new Color(0.05f,0.05f,0.06f) } },
            { "car 7",  new Preset { name = "Ford F-150 Raptor",             price = 15000,   drive = 2, gears = 10, hp = 450,  mass = 2600, torque = 691,  redline = 6000,  top = 180, zero100 = 5.5f, grip = 0.95f, down = 0.5f, length = 5.9f,  traffic = true,  eng = "V6",  turbo = true, turboSize = 1, color = new Color(0.55f,0.57f,0.6f) } },
            { "car 10", new Preset { name = "BMW M4 Competition (G82)",      price = 40000,   drive = 1, gears = 8,  hp = 510,  mass = 1725, torque = 650,  redline = 7200,  top = 290, zero100 = 3.9f, grip = 1.12f, down = 1.0f, length = 4.8f,  traffic = true,  eng = "I6",  turbo = true, turboSize = 1, police = "undercover", color = new Color(0.08f,0.2f,0.75f) } },
            { "car 9",  new Preset { name = "Tesla Cybertruck (Cyberbeast)", price = 85000,  drive = 2, gears = 1,  hp = 845,  mass = 3100, torque = 1500, redline = 16000, top = 209, zero100 = 2.7f, grip = 1.05f, down = 0.6f, length = 5.7f,  traffic = true,  eng = "EV",  police = "suv", color = new Color(0.7f,0.72f,0.74f) } },
            { "car 5",  new Preset { name = "Nissan GT-R (R35)",             price = 150000,  drive = 2, gears = 6,  hp = 565,  mass = 1752, torque = 633,  redline = 7100,  top = 315, zero100 = 2.9f, grip = 1.18f, down = 1.2f, length = 4.7f,  traffic = false, eng = "V6",  turbo = true, turboSize = 1, color = new Color(0.9f,0.9f,0.92f) } },
            { "car 6",  new Preset { name = "Toyota Supra MK4 (A80)",        price = 25000,  drive = 1, gears = 6,  hp = 330,  mass = 1510, torque = 441,  redline = 6800,  top = 285, zero100 = 4.6f, grip = 1.08f, down = 1.0f, length = 4.5f,  traffic = false, eng = "I6",  turbo = true, turboSize = 2, color = new Color(1f,0.4f,0f) } },
            { "car 4",  new Preset { name = "Porsche 911 Turbo S (992)",     price = 200000,  drive = 2, gears = 8,  hp = 650,  mass = 1640, torque = 800,  redline = 7200,  top = 330, zero100 = 2.7f, grip = 1.22f, down = 1.3f, length = 4.55f, traffic = false, eng = "F6",  turbo = true, turboSize = 1, color = new Color(0.75f,0.04f,0.04f) } },
            { "car 8",  new Preset { name = "Nissan Skyline GT-R (R34)",     price = 115000,  drive = 2, gears = 6,  hp = 330,  mass = 1560, torque = 360,  redline = 8000,  top = 265, zero100 = 4.9f, grip = 0.98f, down = 1.1f, length = 4.6f,  traffic = false, eng = "I6",  turbo = true, turboSize = 2, color = new Color(0.1f,0.3f,0.8f) } },
            { "car 2",  new Preset { name = "Bugatti Chiron",                price = 350000, drive = 2, gears = 7,  hp = 1500, mass = 1995, torque = 1600, redline = 6700,  top = 420, zero100 = 2.4f, grip = 1.12f, down = 1.6f, length = 4.55f, traffic = false, eng = "W16", turbo = true, turboSize = 3, color = new Color(0.08f,0.15f,0.4f) } },
        };

        static void ApplyPreset(MostWanted.CarEntry e, Preset pr)
        {
            e.displayName = pr.name; e.price = pr.price; e.drive = pr.drive; e.massKg = pr.mass; e.torqueNm = pr.torque;
            e.redlineRpm = pr.redline; e.topSpeedKmh = pr.top; e.grip = pr.grip; e.downforce = pr.down; e.length = pr.length;
            e.gears = pr.gears; e.powerHp = pr.hp; e.zeroTo100 = pr.zero100;
            e.policeRole = pr.police ?? ""; e.inTraffic = pr.traffic; e.defaultColor = pr.color;
            ApplyAudioPreset(e, pr);
            e.credit = "Store InvoGames — Car Asset Pack for Arcade & Demolition Racing Games (Standard License)";
        }

        static void ApplyAudioPreset(MostWanted.CarEntry e, Preset pr)
        {
            e.engineType = pr.eng; e.turbo = pr.turbo; e.turboSize = pr.turbo ? Mathf.Max(1, pr.turboSize) : 0; e.supercharger = pr.sc;
        }

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
                    ApplyPreset(e, pr);
                }
                else
                {
                    e.price = 30000 + reg.cars.Count * 8000;
                    e.credit = "";
                }
                reg.cars.Add(e);
                added++;
            }
            // ses ön ayarlarını güncelle (eski kayıtlar için bir kez)
            if (reg.presetVersion < 5)
            {
                foreach (var e in reg.cars)
                {
                    if (e.prefab == null) continue;
                    string pth = AssetDatabase.GetAssetPath(e.prefab).ToLowerInvariant();
                    Preset pr;
                    if (pth.Contains("invogames") && Presets.TryGetValue(System.IO.Path.GetFileNameWithoutExtension(pth), out pr)) ApplyPreset(e, pr);
                }
                reg.presetVersion = 5;
                Debug.Log("[MW] Araç kaydı gerçek modellere güncellendi (isim, fiyat, motor, şanzıman).");
                created = true;
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
