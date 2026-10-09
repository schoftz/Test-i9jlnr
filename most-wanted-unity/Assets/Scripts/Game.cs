using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MostWanted
{
    /// <summary>
    /// Ana oyun nesnesi. Sahne kurmaya gerek yok: Play'e basınca RuntimeInitializeOnLoadMethod ile kendini oluşturur.
    /// </summary>
    public class Game : MonoBehaviour
    {
        public static Game I;

        public enum Menu { None, Pause, Garage, Jobs }
        public Menu menu = Menu.None;

        public City city;
        public CarController player;
        public Camera cam, mapCam;
        public RenderTexture mapRT;
        public Light sun;
        public TrafficManager traffic;
        public PoliceManager police;
        public RaceManager race;
        public DeliveryManager delivery;
        public HUD hud;

        public bool bumperCam;
        public float dayTime = 0.30f;      // 0..1 (0.25 = gündoğumu, 0.75 = günbatımı)
        public float dayLength = 420f;     // saniye
        public float Night { get; private set; }

        Material sky;
        AudioSource sfx;
        float camFov = 60f;
        Vector3 camVel;
        float saveTimer, envTimer;

        public readonly List<string> toasts = new List<string>();
        public readonly List<float> toastTimes = new List<float>();

        public bool InputBlocked { get { return menu != Menu.None || (race != null && race.Counting); } }
        public bool NearGarage { get { return player != null && U.FlatDist(player.transform.position, city.garagePos) < 12f; } }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (I != null) return;
            var go = new GameObject("MW_Oyun");
            go.AddComponent<Game>();
        }

        void Awake()
        {
            if (I != null && I != this) { Destroy(gameObject); return; }
            I = this;
            DontDestroyOnLoad(gameObject);
        }

        void Start()
        {
            Application.targetFrameRate = 60;
            Time.fixedDeltaTime = 1f / 60f;
            QualitySettings.shadowDistance = 160f;
            QualitySettings.pixelLightCount = 8;
            SaveSystem.Load();

            // Sahnedeki varsayılan kamera/ışıkları temizle
            foreach (var c in Camera.allCameras) if (c != null) Destroy(c.gameObject);
            var dl = GameObject.Find("Directional Light");
            if (dl != null) Destroy(dl);

            city = new City();
            city.Build();

            SetupLighting();
            SetupCameras();

            sfx = gameObject.AddComponent<AudioSource>();
            sfx.spatialBlend = 0f;

            traffic = gameObject.AddComponent<TrafficManager>();
            police = gameObject.AddComponent<PoliceManager>();
            race = gameObject.AddComponent<RaceManager>();
            race.Setup(city);
            delivery = gameObject.AddComponent<DeliveryManager>();
            hud = gameObject.AddComponent<HUD>();

            SpawnPlayer(city.garagePos + Vector3.up * 0.6f, city.garageRot);
            Toast("Most Wanted'a hoş geldin! Garaj için E, işler için J.");
        }

        void SetupLighting()
        {
            var sg = new GameObject("Gunes");
            sun = sg.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.8f;
            RenderSettings.sun = sun;

            var skyRes = Resources.Load<Material>("MW_Sky");
            Shader skyShader = skyRes != null ? skyRes.shader : Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                sky = new Material(skyShader);
                if (sky.HasProperty("_SunSize")) sky.SetFloat("_SunSize", 0.04f);
                RenderSettings.skybox = sky;
            }
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.0028f;
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

            mapRT = new RenderTexture(256, 256, 16);
            mapRT.name = "MinimapRT";
            var mg = new GameObject("MinimapKamera");
            mapCam = mg.AddComponent<Camera>();
            mapCam.orthographic = true;
            mapCam.orthographicSize = 140f;
            mapCam.targetTexture = mapRT;
            mapCam.clearFlags = CameraClearFlags.SolidColor;
            mapCam.backgroundColor = new Color(0.05f, 0.08f, 0.05f);
            mapCam.nearClipPlane = 1f;
            mapCam.farClipPlane = 400f;
            mapCam.depth = -5;
            mg.AddComponent<MinimapCamera>();
        }

        public void SpawnPlayer(Vector3 pos, Quaternion rot)
        {
            float nitro = 1f;
            if (player != null) { nitro = player.nitro; Destroy(player.gameObject); }
            var d = SaveSystem.Data;
            var spec = Catalog.Cars[d.selected];
            var save = SaveSystem.Get(d.selected);
            Color col = save.color >= 0 ? Catalog.Paints[save.color] : spec.color;
            player = CarFactory.Build(spec, col, pos, rot, CarRole.Player, "Oyuncu");
            CarFactory.ApplyTuning(player, spec, save);
            player.nitro = nitro;
            player.gameObject.AddComponent<PlayerDriver>();
            player.onHit = c => police.OnPlayerHit(c);
            // gece farları
            var hl = new GameObject("Farlar");
            hl.transform.SetParent(player.transform, false);
            hl.transform.localPosition = new Vector3(0, 0.9f, spec.length / 2 + 0.2f);
            hl.transform.localRotation = Quaternion.Euler(8, 0, 0);
            var l = hl.AddComponent<Light>();
            l.type = LightType.Spot; l.range = 70f; l.spotAngle = 70f; l.intensity = 2.2f; l.color = new Color(1f, 0.96f, 0.85f);
            l.shadows = LightShadows.None;
            playerHeadlight = l;
        }

        Light playerHeadlight;

        void Update()
        {
            float udt = Time.unscaledDeltaTime;
            for (int i = toastTimes.Count - 1; i >= 0; i--)
            {
                toastTimes[i] -= udt;
                if (toastTimes[i] <= 0f) { toastTimes.RemoveAt(i); toasts.RemoveAt(i); }
            }

            HandleKeys();
            UpdateDayNight();

            saveTimer += Time.deltaTime;
            if (saveTimer > 30f) { saveTimer = 0f; SaveSystem.Save(); }
        }

        void HandleKeys()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (menu == Menu.None) OpenMenu(Menu.Pause);
                else CloseMenu();
            }
            if (Input.GetKeyDown(KeyCode.E))
            {
                if (menu == Menu.Garage) CloseMenu();
                else if (menu == Menu.None)
                {
                    if (!NearGarage) Toast("Garaja gitmek için yeşil işarete git (haritada yeşil kare).");
                    else if (police.pursuit) Toast("Polis peşindeyken garaja giremezsin!");
                    else if (race.Active) Toast("Yarış sırasında garaja giremezsin!");
                    else OpenMenu(Menu.Garage);
                }
            }
            if (Input.GetKeyDown(KeyCode.J))
            {
                if (menu == Menu.Jobs) CloseMenu();
                else if (menu == Menu.None) OpenMenu(Menu.Jobs);
            }
            if (Input.GetKeyDown(KeyCode.C) && menu == Menu.None) bumperCam = !bumperCam;
            if (Input.GetKeyDown(KeyCode.R) && menu == Menu.None && player != null && player.SpeedKmh < 10f) player.Unflip();
        }

        public void OpenMenu(Menu m)
        {
            menu = m;
            Time.timeScale = 0f;
            AudioListener.pause = true;
            if (m == Menu.Garage && hud != null) hud.garageSel = SaveSystem.Data.selected;
        }

        public void CloseMenu()
        {
            if (menu == Menu.Garage)
            {
                SaveSystem.Save();
                SpawnPlayer(city.garagePos + Vector3.up * 0.6f, city.garageRot);
            }
            menu = Menu.None;
            Time.timeScale = 1f;
            AudioListener.pause = false;
        }

        void UpdateDayNight()
        {
            dayTime = Mathf.Repeat(dayTime + Time.deltaTime / dayLength, 1f);
            float sunAngle = (dayTime - 0.25f) * 360f; // 0 = ufuk (doğu)
            Quaternion sunRot = Quaternion.Euler(sunAngle, -30f, 0f);
            float elev = Mathf.Sin(sunAngle * Mathf.Deg2Rad);
            float day = Mathf.Clamp01((elev + 0.08f) / 0.35f);
            Night = 1f - day;

            if (elev > -0.05f)
            {
                sun.transform.rotation = sunRot;
                sun.intensity = Mathf.Lerp(0.05f, 1.15f, day);
                sun.color = Color.Lerp(new Color(1f, 0.55f, 0.3f), new Color(1f, 0.96f, 0.88f), Mathf.Clamp01(elev * 3f));
            }
            else
            {
                // ay ışığı
                sun.transform.rotation = Quaternion.Euler(sunAngle - 180f, -30f, 0f);
                sun.intensity = 0.18f;
                sun.color = new Color(0.55f, 0.65f, 1f);
            }

            RenderSettings.ambientLight = Color.Lerp(new Color(0.08f, 0.09f, 0.16f), new Color(0.55f, 0.58f, 0.62f), day);
            RenderSettings.fogColor = Color.Lerp(new Color(0.04f, 0.05f, 0.1f), new Color(0.68f, 0.75f, 0.85f), day);
            if (sky != null)
            {
                if (sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", Mathf.Lerp(0.12f, 1.25f, day));
                if (sky.HasProperty("_AtmosphereThickness")) sky.SetFloat("_AtmosphereThickness", Mathf.Lerp(0.6f, 1.0f, day));
            }
            if (cam != null) cam.backgroundColor = RenderSettings.fogColor;

            envTimer -= Time.deltaTime;
            if (envTimer <= 0f)
            {
                envTimer = 0.25f;
                city.SetNight(Night);
                if (playerHeadlight != null) playerHeadlight.enabled = Night > 0.35f;
            }
        }

        void LateUpdate()
        {
            if (player == null || cam == null) return;
            var t = player.transform;
            float kmh = player.SpeedKmh;
            float targetFov = 60f + Mathf.Clamp01(kmh / 250f) * 10f + (player.nitroActive ? 14f : 0f);
            if (bumperCam)
            {
                cam.transform.position = t.TransformPoint(new Vector3(0, 1.05f, Catalog.Cars[SaveSystem.Data.selected].length / 2 + 0.1f));
                cam.transform.rotation = Quaternion.LookRotation(t.forward, Vector3.up);
                targetFov += 8f;
            }
            else
            {
                Vector3 fwd = U.Flat(t.forward);
                if (fwd.sqrMagnitude < 0.01f) fwd = Vector3.forward;
                fwd.Normalize();
                Vector3 vel = U.Flat(U.Vel(player.rb));
                // geri giderken kamera arkada kalsın, drift'te araç yönüne doğru yumuşakça dönsün
                Vector3 look = vel.magnitude > 5f && Vector3.Dot(vel.normalized, fwd) > 0.2f ? Vector3.Slerp(fwd, vel.normalized, 0.35f) : fwd;
                float dist = 6.8f + Mathf.Clamp01(kmh / 250f) * 1.5f;
                Vector3 desired = t.position - look * dist + Vector3.up * 2.4f;
                float dt = Mathf.Max(Time.deltaTime, 0.0001f);
                cam.transform.position = Vector3.SmoothDamp(cam.transform.position, desired, ref camVel, 0.12f, Mathf.Infinity, dt);
                cam.transform.rotation = Quaternion.LookRotation((t.position + Vector3.up * 1.2f + look * 3f) - cam.transform.position, Vector3.up);
            }
            camFov = Mathf.Lerp(camFov, targetFov, Time.deltaTime * 4f);
            cam.fieldOfView = camFov;

            // minimap kamerası: araç yönü yukarı
            if (mapCam != null)
            {
                mapCam.transform.position = t.position + Vector3.up * 200f;
                mapCam.transform.rotation = Quaternion.Euler(90f, t.eulerAngles.y, 0f);
                mapCam.orthographicSize = Mathf.Lerp(mapCam.orthographicSize, 110f + Mathf.Clamp01(kmh / 200f) * 70f, Time.deltaTime);
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

        void OnApplicationQuit()
        {
            SaveSystem.Save();
        }
    }

    /// <summary>Minimap kamerası render ederken sisi kapatır (Built-in pipeline).</summary>
    public class MinimapCamera : MonoBehaviour
    {
        bool fog;
        void OnPreRender() { fog = RenderSettings.fog; RenderSettings.fog = false; }
        void OnPostRender() { RenderSettings.fog = fog; }
    }
}
