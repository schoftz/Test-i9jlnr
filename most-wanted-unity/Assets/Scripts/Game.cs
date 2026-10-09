using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace MostWanted
{
    /// <summary>
    /// Ana oyun nesnesi. Sahne kurmaya gerek yok: Play'e basınca RuntimeInitializeOnLoadMethod ile kendini kurar.
    /// </summary>
    public class Game : MonoBehaviour
    {
        public static Game I;

        public enum Menu { None, Pause, Garage, Jobs, Map, Credits, Blacklist }
        public Menu menu = Menu.None;

        [System.NonSerialized] public World world;
        public CarController player;
        public PlayerDriver playerDriver;
        public Camera cam, mapCam, bigMapCam;
        public CameraRig rig;
        public RenderTexture mapRT, bigMapRT;
        public Light sun;
        public TrafficManager traffic;
        public PoliceManager police;
        public RaceManager race;
        public DeliveryManager delivery;
        public Career career;
        public HUD hud;
        public bool showFps;
        public float fps = 60f;
        public float baseFixedDelta = 1f / 60f;
        public OptimizationManager opt;
        public MapDressing dressing;
        public AudioDirector audioDirector;
        public string district = "";
        float districtTimer;
        WhiteBalance whiteBalance;
        SplitToning splitToning;
        public static readonly string[] AtmosphereNames = { "Most Wanted", "Normal", "Gün Batımı", "Gece" };
        static readonly float[] AtmosphereTime = { 0.40f, 0.45f, 0.70f, 0.93f };
        float mapRenderTimer;
        public float dayTime = 0.36f;
        public float dayLength = 720f;
        public float Night { get; private set; }
        public string mapCredit = "";
        public bool usingImportedMap;
        public int mapMode;   // 0 Kendi Şehrimiz (varsayılan), 1 İthal, 2 Test
        public float bigMapZoom = 600f;
        public Vector3 bigMapPan;

        Material sky;
        AudioSource sfx;
        float envTimer, saveTimer;
        Volume volume;
        Bloom bloom;
        ColorAdjustments colorAdj;
        Vignette vignette;
        ChromaticAberration chroma;
        MotionBlur motionBlur;
        bool fogBackup;
        readonly List<PursuitBreaker> breakers = new List<PursuitBreaker>();

        public readonly List<string> toasts = new List<string>();
        public readonly List<float> toastTimes = new List<float>();
        public static readonly string[] QualityNames = { "Düşük", "Orta", "Yüksek" };

        public bool InputBlocked { get { return menu != Menu.None || (race != null && race.Counting && !race.IsDrag); } }
        public bool NearGarage { get { return player != null && world != null && U.FlatDist(player.transform.position, world.garagePos) < 12f; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I != null) return;
            new GameObject("MW_Oyun").AddComponent<Game>();
        }

        void Awake()
        {
            if (I != null && I != this) { Destroy(gameObject); return; }
            I = this;
            DontDestroyOnLoad(gameObject);
        }

        void Start()
        {
            Catalog.Load();
            SaveSystem.Load();
            AudioBus.Load(SaveSystem.Data);
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.spatialBlend = 0f;
            RenderPipelineManager.beginCameraRendering += OnBeginCam;
            RenderPipelineManager.endCameraRendering += OnEndCam;
            Init();
        }

        void OnDestroy()
        {
            RenderPipelineManager.beginCameraRendering -= OnBeginCam;
            RenderPipelineManager.endCameraRendering -= OnEndCam;
        }

        // minimap kameralarında sis kapalı
        void OnBeginCam(ScriptableRenderContext ctx, Camera c)
        {
            if (c == mapCam || c == bigMapCam) { fogBackup = RenderSettings.fog; RenderSettings.fog = false; }
        }
        void OnEndCam(ScriptableRenderContext ctx, Camera c)
        {
            if (c == mapCam || c == bigMapCam) RenderSettings.fog = fogBackup;
        }

        // ------------------------------------------------------------------ kurulum
        void Init()
        {
            foreach (var c in Camera.allCameras) if (c != null) Destroy(c.gameObject);
            var dl = GameObject.Find("Directional Light");
            if (dl != null) Destroy(dl);

            world = null;
            usingImportedMap = false;
            mapCredit = "";
            var reg = Resources.Load<MapRegistry>("MapRegistry");
            mapMode = Mathf.Clamp(PlayerPrefs.GetInt("MW_MAP2", 0), 0, 2);
            bool wantImported = mapMode == 1;
            if (wantImported && reg != null && reg.maps.Count > 0 && reg.maps[0].prefab != null)
            {
                var baked = Resources.Load<TextAsset>("MapData/" + reg.maps[0].name.Replace("_opt", "") + "_roadgraph");
                if (baked != null)
                {
                    try
                    {
                        var bw = new BakedWorld(reg.maps[0].prefab, baked.text, reg.maps[0].credit);
                        bw.Build();
                        world = bw; usingImportedMap = true; mapCredit = reg.maps[0].credit;
                    }
                    catch (System.Exception e) { Debug.LogWarning("Pişmiş harita yüklenemedi: " + e.Message); if (world == null) CleanupWorldRoot(); }
                }
                if (world == null) try
                {
                    var iw = new ImportedWorld(reg.maps[0].prefab, reg.maps[0].credit);
                    iw.Build();
                    if (iw.graph.nodes.Count >= 10) { world = iw; usingImportedMap = true; mapCredit = reg.maps[0].credit; }
                    else { Debug.LogWarning("İthal haritada yol bulunamadı, test şehrine dönülüyor."); Destroy(iw.root.gameObject); }
                }
                catch (System.Exception e) { Debug.LogWarning("Harita yüklenemedi: " + e.Message); }
            }
            if (world == null && mapMode == 0)
            {
                try { var own = new OwnCity(); own.Build(); world = own; }
                catch (System.Exception e) { Debug.LogWarning("Kendi şehrimiz kurulamadı: " + e); var r0 = GameObject.Find("KendiSehrimiz"); if (r0 != null) Destroy(r0); }
            }
            if (world == null)
            {
                var city = new City();
                city.Build();
                world = city;
                foreach (var p in city.breakerSites) breakers.Add(PursuitBreaker.Create(p, city.breakerKinds[breakers.Count], city.root));
            }

            dressing = null;
            if (!(world is City) && !(world is OwnCity) && SaveSystem.Data.dressing)
            {
                try { dressing = MapDressing.Build(world, SaveSystem.Data.quality); }
                catch (System.Exception e) { Debug.LogWarning("Harita süsleme başarısız: " + e); }
            }

            SetupLighting();
            SetupCameras();
            SetupPost();
            ApplyAtmosphere(SaveSystem.Data.atmosphere);
            if (dressing != null) dressing.SetWet(SaveSystem.Data.wet);

            traffic = gameObject.AddComponent<TrafficManager>();
            police = gameObject.AddComponent<PoliceManager>();
            race = gameObject.AddComponent<RaceManager>();
            delivery = gameObject.AddComponent<DeliveryManager>();
            career = gameObject.AddComponent<Career>();
            hud = gameObject.AddComponent<HUD>();
            opt = gameObject.AddComponent<OptimizationManager>();
            audioDirector = gameObject.AddComponent<AudioDirector>();
            opt.Init(SaveSystem.Data.quality, SaveSystem.Data.fpsTarget);
            ApplyTimeScale();
            if (world is City) ((City)world).MarkDetailLayers();
            SpawnPlayer(world.garagePos + Vector3.up * 0.5f, world.garageRot);
            rig.Snap();
            Toast("Most Wanted'a hoş geldin! Garaj: E  •  İşler: J  •  Kara Liste: B  •  Harita: M");
        }

        void CleanupWorldRoot()
        {
            var r = GameObject.Find("IthalHarita");
            if (r != null) Destroy(r);
        }

        IEnumerator Rebuild()
        {
            CloseMenu();
            race.Abort();
            police.EndPursuit(false);
            foreach (var c in new Component[] { traffic, police, race, delivery, career, hud, opt, audioDirector }) if (c != null) Destroy(c);
            breakers.Clear();
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects()) Destroy(go);
            player = null;
            yield return null;
            yield return null;
            Init();
        }

        public void SwitchMap(bool imported) { SetMap(imported ? 1 : 2); }

        public static readonly string[] MapNames = { "Kendi Şehrimiz", "İthal", "Test" };

        /// <summary>0 Kendi Şehrimiz, 1 İthal harita, 2 Test şehri — dünyayı yeniden kurar.</summary>
        public void SetMap(int mode)
        {
            PlayerPrefs.SetInt("MW_MAP2", Mathf.Clamp(mode, 0, 2));
            PlayerPrefs.Save();
            StartCoroutine(Rebuild());
        }

        public bool HasImportedMap
        {
            get { var reg = Resources.Load<MapRegistry>("MapRegistry"); return reg != null && reg.maps.Count > 0 && reg.maps[0].prefab != null; }
        }

        void SetupLighting()
        {
            var sg = new GameObject("Gunes");
            sun = sg.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;
            sun.intensity = 1.35f;
            RenderSettings.sun = sun;

            var skyRes = Resources.Load<Material>("MW_Sky");
            Shader skyShader = skyRes != null ? skyRes.shader : Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                sky = new Material(skyShader);
                if (sky.HasProperty("_SunSize")) sky.SetFloat("_SunSize", 0.035f);
                if (sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", 1.15f);
                if (sky.HasProperty("_AtmosphereThickness")) sky.SetFloat("_AtmosphereThickness", 0.9f);
                RenderSettings.skybox = sky;
            }
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 1f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.0011f;
            DynamicGI.UpdateEnvironment();
        }

        void SetupCameras()
        {
            var cg = new GameObject("AnaKamera");
            cg.tag = "MainCamera";
            cam = cg.AddComponent<Camera>();
            cam.farClipPlane = 1500f;
            cam.nearClipPlane = 0.2f;
            cam.fieldOfView = 60f;
            cam.cullingMask = ~(1 << U.IconLayer);
            cam.clearFlags = CameraClearFlags.Skybox;
            cg.AddComponent<AudioListener>();
            var cd = cam.GetUniversalAdditionalCameraData();
            if (cd != null) { cd.renderPostProcessing = true; cd.antialiasing = AntialiasingMode.FastApproximateAntialiasing; }
            rig = cg.AddComponent<CameraRig>();
            rig.cam = cam;

            mapRT = new RenderTexture(320, 320, 16) { name = "MinimapRT" };
            mapCam = MakeTopCam("MinimapKamera", mapRT, 140f);
            mapCam.enabled = false; // 15 fps elle render edilir
            bigMapRT = new RenderTexture(1024, 1024, 16) { name = "HaritaRT" };
            bigMapCam = MakeTopCam("HaritaKamera", bigMapRT, 600f);
            bigMapCam.enabled = false;
        }

        Camera MakeTopCam(string name, RenderTexture rt, float size)
        {
            var mg = new GameObject(name);
            var c = mg.AddComponent<Camera>();
            c.orthographic = true;
            c.orthographicSize = size;
            c.targetTexture = rt;
            c.clearFlags = CameraClearFlags.SolidColor;
            c.backgroundColor = new Color(0.05f, 0.08f, 0.05f);
            c.nearClipPlane = 1f;
            c.farClipPlane = 1000f;
            c.depth = -5;
            var d = c.GetUniversalAdditionalCameraData();
            if (d != null) { d.renderShadows = false; d.renderPostProcessing = false; }
            return c;
        }

        void SetupPost()
        {
            var vg = new GameObject("PostFX");
            volume = vg.AddComponent<Volume>();
            volume.isGlobal = true;
            var prof = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.sharedProfile = prof;
            bloom = prof.Add<Bloom>(true);
            bloom.intensity.Override(0.35f);
            bloom.threshold.Override(1.15f);
            var tm = prof.Add<Tonemapping>(true);
            tm.mode.Override(TonemappingMode.ACES);
            colorAdj = prof.Add<ColorAdjustments>(true);
            colorAdj.postExposure.Override(0.25f);
            colorAdj.contrast.Override(8f);
            colorAdj.saturation.Override(10f);
            vignette = prof.Add<Vignette>(true);
            vignette.intensity.Override(0.18f);
            chroma = prof.Add<ChromaticAberration>(true);
            chroma.intensity.Override(0f);
            motionBlur = prof.Add<MotionBlur>(true);
            motionBlur.intensity.Override(0f);
            whiteBalance = prof.Add<WhiteBalance>(true);
            splitToning = prof.Add<SplitToning>(true);
        }

        public void ApplyQuality(int q)
        {
            SaveSystem.Data.quality = Mathf.Clamp(q, 0, 3);
            if (opt != null) opt.SetPreset(SaveSystem.Data.quality);
            ApplyTimeScale();
            SaveSystem.Save();
        }

        public void SetFpsTarget(int i)
        {
            SaveSystem.Data.fpsTarget = i;
            OptimizationManager.SetFpsTarget(i);
            SaveSystem.Save();
        }

        /// <summary>Post efekt kalitesi: Düşük'te bloom/motion blur yok.</summary>
        public void SetPostQuality(int q)
        {
            if (bloom != null) { bloom.active = q >= 1; bloom.threshold.value = 1.2f; }
            if (motionBlur != null) motionBlur.active = q >= 1;
            if (chroma != null) chroma.active = q >= 1;
        }

        // ------------------------------------------------------------------ oyuncu
        public void SpawnPlayer(Vector3 pos, Quaternion rot)
        {
            float nitro = 1f, sb = 1f;
            if (player != null)
            {
                nitro = player.nitro;
                if (playerDriver != null) { sb = playerDriver.speedbreaker; playerDriver.SetSpeedbreaker(false); }
                Destroy(player.gameObject);
            }
            var d = SaveSystem.Data;
            var def = Catalog.Get(d.selected) ?? Catalog.Garage[0];
            var save = SaveSystem.Get(def.id);
            PaintDef? col = null;   // -1: fabrika görünümü (model dokusu korunur)
            if (save != null && save.color >= 0 && save.color < Catalog.Paints.Length) col = Catalog.Paints[save.color];
            else if (def.prefab == null) col = new PaintDef("Fabrika", def.defaultColor, 0.5f, 0.8f);
            pos = GroundSnap(pos);
            player = CarFactory.Build(def, col, pos, rot, CarRole.Player, save != null ? save.tune : null, "Oyuncu");
            // Drift Araçları: drift ayarı açıksa Saarg (Arcade Car Physics) kontrolcüsüne geç — tek kontrolcü
            if (CustomCatalog.IsDriftCar(def) && save != null && save.custom != null && save.custom.DriftSetup)
            {
                player.SetPhysicsMode(true);
                Toast("Drift ayarı aktif — el freni + gaz ile savur!");
            }
            fallTimer = 0f;
            player.nitro = nitro;
            playerDriver = player.gameObject.AddComponent<PlayerDriver>();
            playerDriver.speedbreaker = sb;
            player.onHit = c =>
            {
                police.OnPlayerHit(c);
                if (c.rigidbody != null) playerDriver.MarkTouched(c.rigidbody);
                // gerçek darbe: normal yönünde > 3 m/s ve normal çoğunlukla yatay (bordür/basamak/zemin dikişi sayılmaz)
                Vector3 n = c.contactCount > 0 ? c.GetContact(0).normal : Vector3.up;
                float vn = Mathf.Abs(Vector3.Dot(c.relativeVelocity, n));
                bool realHit = vn > 3f && Mathf.Abs(n.y) < 0.6f;
                if (realHit)
                {
                    var hitCol = c.collider;
                    string path = hitCol != null ? hitCol.transform.name + (hitCol.transform.parent != null ? " < " + hitCol.transform.parent.name : "") : "?";
                    Debug.Log("[MW] Çarpışma: " + path + " katman=" + (hitCol != null ? LayerMask.LayerToName(hitCol.gameObject.layer) + "(" + hitCol.gameObject.layer + ")" : "?") +
                              " v=" + c.relativeVelocity.magnitude.ToString("0.0") + " vn=" + vn.ToString("0.0") + " nokta=" + (c.contactCount > 0 ? c.GetContact(0).point.ToString("F1") : "-") + " normal=" + n.ToString("F2"));
                    if (vn > 4f) career.DriftCrash();
                    if (vn > 8f) rig.Shake(Mathf.Clamp01(vn / 30f));
                    if (playerDriver.engineAudio != null) playerDriver.engineAudio.Impact(vn);
                }
            };
            var hl = new GameObject("Farlar");
            hl.transform.SetParent(player.transform, false);
            hl.transform.localPosition = new Vector3(0, 0.9f, def.length / 2 + 0.2f);
            hl.transform.localRotation = Quaternion.Euler(8, 0, 0);
            headlight = hl.AddComponent<Light>();
            headlight.type = LightType.Spot; headlight.range = 70f; headlight.spotAngle = 70f; headlight.intensity = 3f;
            headlight.color = new Color(1f, 0.96f, 0.85f);
            headlight.shadows = LightShadows.None;
            if (rig != null) rig.target = player;
        }

        Light headlight;

        // ------------------------------------------------------------------ zaman / menü
        public void ApplyTimeScale()
        {
            float s = menu != Menu.None ? 0f : (playerDriver != null && playerDriver.speedbreakerOn ? 0.35f : 1f);
            Time.timeScale = s;
            Time.fixedDeltaTime = baseFixedDelta * Mathf.Max(0.35f, s);
            AudioListener.pause = menu != Menu.None;
        }

        public void OpenMenu(Menu m)
        {
            menu = m;
            if (m == Menu.Garage && hud != null) hud.garageSel = Mathf.Max(0, Catalog.Garage.IndexOf(Catalog.Get(SaveSystem.Data.selected)));
            if (m == Menu.Garage) GarageStage.Get().Enter();
            if (m == Menu.Map) { bigMapCam.enabled = true; bigMapPan = Vector3.zero; }
            ApplyTimeScale();
        }

        public void CloseMenu()
        {
            if (menu == Menu.Garage)
            {
                if (GarageStage.I != null) GarageStage.I.Exit();
                SaveSystem.Save();
                SpawnPlayer(world.garagePos + Vector3.up * 0.5f, world.garageRot);
                rig.Snap();
            }
            if (bigMapCam != null) bigMapCam.enabled = false;
            menu = Menu.None;
            ApplyTimeScale();
        }

        float fallTimer, flyTimer;

        /// <summary>Doğma noktasını alttaki zemine oturtur (araç gövdesi zemine gömülmesin).</summary>
        public static Vector3 GroundSnap(Vector3 p)
        {
            RaycastHit h;
            int mask = ~((1 << OptimizationManager.TrafficLayer) | (1 << U.IconLayer));
            if (Physics.Raycast(p + Vector3.up * 40f, Vector3.down, out h, 200f, mask, QueryTriggerInteraction.Ignore))
                return h.point + Vector3.up * 1.2f;
            return p + Vector3.up * 1.2f;
        }

        /// <summary>Oyuncu haritanın altına düşerse en yakın yola geri koyar.</summary>
        void FallGuard(float dt)
        {
            Vector3 p = player.transform.position;
            float vy = U.Vel(player.rb).y;
            bool groundBelow = Physics.Raycast(p + Vector3.up * 2f, Vector3.down, 300f, ~((1 << OptimizationManager.TrafficLayer) | (1 << U.IconLayer)), QueryTriggerInteraction.Ignore);
            if (vy < -15f && !groundBelow) fallTimer += dt; else fallTimer = 0f;
            float nodeY = world.graph.nodes.Count > 0 ? world.graph.nodes[world.graph.Nearest(p)].y : p.y;
            bool tooLow = world.graph.nodes.Count > 0 && p.y < nodeY - 40f;
            if (p.y > nodeY + 60f) flyTimer += dt; else flyTimer = 0f;
            if (flyTimer > 2f) { flyTimer = 0f; tooLow = true; Debug.LogWarning("[MW] Oyuncu havada kaldı, yola indirildi."); }
            if (fallTimer > 0.8f || tooLow)
            {
                fallTimer = 0f;
                Vector3 n = world.graph.nodes.Count > 0 ? world.graph.nodes[world.graph.Nearest(p)] : world.garagePos;
                player.Teleport(GroundSnap(n), player.transform.rotation);
                rig.Snap();
                Toast("Yola geri alındın");
                Debug.LogWarning("[MW] Oyuncu harita altına düştü, kurtarıldı. Konum: " + p + " → " + n);
            }
        }

        void Update()
        {
            float udt = Time.unscaledDeltaTime;
            fps = Mathf.Lerp(fps, 1f / Mathf.Max(udt, 0.0001f), 0.05f);
            for (int i = toastTimes.Count - 1; i >= 0; i--)
            {
                toastTimes[i] -= udt;
                if (toastTimes[i] <= 0f) { toastTimes.RemoveAt(i); toasts.RemoveAt(i); }
            }
            if (player == null || world == null) return;
            FallGuard(udt);
            HandleKeys();
            UpdateDayNight();
            UpdatePostFx();
            UpdateMapCams();
            districtTimer -= Time.unscaledDeltaTime;
            if (districtTimer <= 0f && dressing != null)
            {
                districtTimer = 2f;
                string d = dressing.DistrictAt(player.transform.position);
                if (d != district) { if (district != "") Toast(d + " bölgesine girdin"); district = d; }
            }
            saveTimer += Time.deltaTime;
            if (saveTimer > 30f) { saveTimer = 0f; SaveSystem.Save(); }
        }

        void HandleKeys()
        {
            if (menu == Menu.Garage) return;   // garaj kendi tuşlarını işler (GarageStage)
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (menu == Menu.None) OpenMenu(Menu.Pause);
                else CloseMenu();
            }
            bool drag = race.Active && race.IsDrag;
            if (Input.GetKeyDown(KeyCode.E) && !drag)
            {
                if (menu == Menu.Garage) CloseMenu();
                else if (menu == Menu.None)
                {
                    if (!NearGarage) Toast("Garaj için haritadaki yeşil işarete git.");
                    else if (police.pursuit) Toast("Polis peşindeyken garaja giremezsin!");
                    else if (race.Active) Toast("Yarış sırasında garaja giremezsin!");
                    else OpenMenu(Menu.Garage);
                }
            }
            if (Input.GetKeyDown(KeyCode.J)) { if (menu == Menu.Jobs) CloseMenu(); else if (menu == Menu.None) OpenMenu(Menu.Jobs); }
            if (Input.GetKeyDown(KeyCode.B)) { if (menu == Menu.Blacklist) CloseMenu(); else if (menu == Menu.None) OpenMenu(Menu.Blacklist); }
            if (Input.GetKeyDown(KeyCode.M) || Input.GetKeyDown(KeyCode.Tab)) { if (menu == Menu.Map) CloseMenu(); else if (menu == Menu.None) OpenMenu(Menu.Map); }
            if (Input.GetKeyDown(KeyCode.C) && menu == Menu.None) rig.Next();
            if (Input.GetKeyDown(KeyCode.F)) showFps = !showFps;
            if (Input.GetKeyDown(KeyCode.R) && menu == Menu.None && !race.Counting) player.Unflip();
            if (Input.GetKeyDown(KeyCode.F9)) { SaveSystem.AddMoney(1000000); Toast("Hile: +" + U.Money(1000000)); }
            if (Input.GetKeyDown(KeyCode.F10) && !race.Active) { police.ForceHeat(5); Toast("Hile: Aranma seviyesi 5 yıldız!"); }

            if (menu == Menu.Map)
            {
                float z = Input.mouseScrollDelta.y;
                if (Input.GetKey(KeyCode.Equals) || Input.GetKey(KeyCode.KeypadPlus)) z += Time.unscaledDeltaTime * 4f;
                if (Input.GetKey(KeyCode.Minus) || Input.GetKey(KeyCode.KeypadMinus)) z -= Time.unscaledDeltaTime * 4f;
                bigMapZoom = Mathf.Clamp(bigMapZoom * (1f - z * 0.12f), 120f, 2500f);
                Vector3 pan = Vector3.zero;
                if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) pan.z += 1f;
                if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) pan.z -= 1f;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) pan.x += 1f;
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) pan.x -= 1f;
                bigMapPan += pan * bigMapZoom * Time.unscaledDeltaTime;
                if (Input.GetMouseButton(0))
                {
                    float px = Input.GetAxisRaw("Mouse X"), py = Input.GetAxisRaw("Mouse Y");
                    bigMapPan -= new Vector3(px, 0, py) * bigMapZoom * 0.02f;
                }
            }
        }

        /// <summary>Atmosfer: 0 Most Wanted (sıcak/sepya), 1 Normal, 2 Gün batımı, 3 Gece.</summary>
        public void ApplyAtmosphere(int a)
        {
            a = Mathf.Clamp(a, 0, 3);
            SaveSystem.Data.atmosphere = a;
            if (SaveSystem.Data.alwaysDay) dayTime = AtmosphereTime[a];
            if (whiteBalance != null)
            {
                whiteBalance.temperature.Override(a == 0 ? 18f : a == 2 ? 25f : a == 3 ? -15f : 0f);
                whiteBalance.tint.Override(a == 0 ? 6f : 0f);
            }
            if (splitToning != null)
            {
                splitToning.shadows.Override(a == 0 ? new Color(0.35f, 0.45f, 0.5f) : a == 3 ? new Color(0.2f, 0.3f, 0.6f) : new Color(0.5f, 0.5f, 0.5f));
                splitToning.highlights.Override(a == 0 ? new Color(0.75f, 0.6f, 0.35f) : a == 2 ? new Color(0.8f, 0.5f, 0.3f) : new Color(0.5f, 0.5f, 0.5f));
                splitToning.balance.Override(a == 0 ? 15f : 0f);
            }
            if (colorAdj != null) colorAdj.colorFilter.Override(a == 0 ? new Color(1f, 0.93f, 0.8f) : Color.white);
            SaveSystem.Save();
        }

        public void SetWet(bool on)
        {
            SaveSystem.Data.wet = on;
            if (dressing != null) dressing.SetWet(on);
            SaveSystem.Save();
        }

        void UpdateDayNight()
        {
            bool always = SaveSystem.Data.alwaysDay;
            if (always) dayTime = Mathf.MoveTowards(dayTime, AtmosphereTime[SaveSystem.Data.atmosphere], Time.deltaTime * 0.02f);
            else
            {
                // kısa geceler: güneş batınca zaman 4 kat hızlı akar
                float speed = Night > 0.5f ? 4f : 1f;
                dayTime = Mathf.Repeat(dayTime + Time.deltaTime / dayLength * speed, 1f);
            }
            float sunAngle = (dayTime - 0.25f) * 360f;
            float elev = Mathf.Sin(sunAngle * Mathf.Deg2Rad);
            float day = Mathf.Clamp01((elev + 0.08f) / 0.3f);
            Night = 1f - day;
            if (elev > -0.05f)
            {
                sun.transform.rotation = Quaternion.Euler(sunAngle, -35f, 0f);
                sun.intensity = Mathf.Lerp(0.15f, 1.35f, day);
                sun.color = Color.Lerp(new Color(1f, 0.6f, 0.38f), new Color(1f, 0.96f, 0.9f), Mathf.Clamp01(elev * 3f));
            }
            else
            {
                sun.transform.rotation = Quaternion.Euler(sunAngle - 180f, -35f, 0f);
                sun.intensity = 0.25f;
                sun.color = new Color(0.6f, 0.7f, 1f);
            }
            RenderSettings.fogColor = Color.Lerp(new Color(0.06f, 0.07f, 0.12f), new Color(0.72f, 0.8f, 0.9f), day);
            if (sky != null && sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", Mathf.Lerp(0.25f, 1.15f, day));
            RenderSettings.ambientIntensity = Mathf.Lerp(0.6f, 1f, day);

            envTimer -= Time.unscaledDeltaTime;
            if (envTimer <= 0f)
            {
                envTimer = always ? 5f : 1f;
                world.SetNight(Night);
                if (dressing != null) dressing.SetNight(Night);
                if (world is City) ((City)world).UpdateLampsNear(player.transform.position, Night);
                bool on = Night > 0.35f;
                if (headlight != null) headlight.enabled = on;
                if (player != null) player.SetHeadlights(on);
                DynamicGI.UpdateEnvironment();
            }
        }

        void UpdatePostFx()
        {
            if (colorAdj == null || player == null) return;
            bool sb = playerDriver != null && playerDriver.speedbreakerOn;
            float kmh = player.SpeedKmh;
            colorAdj.saturation.value = Mathf.Lerp(colorAdj.saturation.value, sb ? -45f : 10f, Time.unscaledDeltaTime * 5f);
            vignette.intensity.value = Mathf.Lerp(vignette.intensity.value, sb ? 0.45f : (player.nitroActive ? 0.3f : 0.18f), Time.unscaledDeltaTime * 5f);
            chroma.intensity.value = Mathf.Lerp(chroma.intensity.value, player.nitroActive ? 0.6f : 0f, Time.unscaledDeltaTime * 4f);
            motionBlur.intensity.value = Mathf.Clamp01((kmh - 120f) / 200f) * 0.35f + (player.nitroActive ? 0.15f : 0f);
        }

        void UpdateMapCams()
        {
            var t = player.transform;
            mapCam.transform.position = t.position + Vector3.up * 300f;
            mapCam.transform.rotation = Quaternion.Euler(90f, t.eulerAngles.y, 0f);
            mapCam.orthographicSize = Mathf.Lerp(mapCam.orthographicSize, 120f + Mathf.Clamp01(player.SpeedKmh / 200f) * 80f, Time.unscaledDeltaTime);
            mapRenderTimer -= Time.unscaledDeltaTime;
            if (mapRenderTimer <= 0f && menu == Menu.None) { mapRenderTimer = 1f / 15f; mapCam.Render(); }
            if (bigMapCam.enabled)
            {
                bigMapCam.transform.position = t.position + bigMapPan + Vector3.up * 500f;
                bigMapCam.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
                bigMapCam.orthographicSize = bigMapZoom;
            }
        }

        public void Toast(string msg)
        {
            toasts.Add(msg);
            toastTimes.Add(4f);
            if (toasts.Count > 5) { toasts.RemoveAt(0); toastTimes.RemoveAt(0); }
        }

        public void Beep(bool high)
        {
            if (sfx != null) sfx.PlayOneShot(AudioSynth.Beep(high), 0.6f);
        }

        public List<string> Credits()
        {
            var l = new List<string>();
            l.Add("Most Wanted (Unity) — prosedürel oyun kodu, sesler ve test şehri");
            l.Add("Araç paketi: \"Car Asset Pack for Arcade & Demolition Racing Games\" — Store InvoGames (Fab, Standard License)");
            l.Add("Harita: \"City 3D Model\" — Optic Idealist (Fab), CC BY 4.0 lisansı");
            l.Add("Sürüş fiziği: Randomation Vehicle Physics — Justin Couch (JustInvoke), MIT Lisansı");
            l.Add("Drift fiziği: Arcade Car Physics — Saarg, MIT Lisansı");
            var reg = Resources.Load<CarRegistry>("CarRegistry");
            if (reg != null) foreach (var c in reg.cars) if (!string.IsNullOrEmpty(c.credit)) l.Add(c.displayName + ": " + c.credit);
            var mreg = Resources.Load<MapRegistry>("MapRegistry");
            if (mreg != null) foreach (var m in mreg.maps) if (!string.IsNullOrEmpty(m.credit)) l.Add(m.name + ": " + m.credit);
            return l;
        }

        void OnApplicationQuit() { SaveSystem.Save(); }
    }
}
