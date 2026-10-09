using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MostWanted.Render
{
    /// <summary>
    /// Grafik sorumlusu: global post-process Volume'u, güneş/ay ışığını, sis/gökyüzü renk eşleşmesini,
    /// URP gölge/SSAO/AA/atmosfer kalite ayarlarını ve oyuncu aracı yansıma probunu yönetir.
    /// Game.cs yalnızca tek satırlık çağrılar yapar (SetupPost / UpdatePostFx / UpdateDayNight / SetPostQuality / ApplyAtmosphere).
    ///
    /// Kalite ön ayarları (OptimizationManager.Apply → Game.SetPostQuality → ApplyQuality):
    ///  0 Düşük : FXAA, bloom/grain/flare/SSAO/atmosfer/prob yok, 1024 gölge.
    ///  1 Orta  : SMAA (STP aktifken STP), düşük SSAO (yarım çözünürlük), 2048 gölge 2 kaskad, atmosfer (12 örnek ışık huzmesi),
    ///            yansıma probu 64px / 2 sn, bloom+kir+grain, ekran-uzayı lens flare.
    ///  2 Yüksek: TAA (yoksa SMAA High), yüksek SSAO, 4096 gölge 4 kaskad, atmosfer (24 örnek), prob 128px / 0.4 sn, bokeh DoF.
    /// </summary>
    public class RenderSetup : MonoBehaviour
    {
        public static RenderSetup I;

        /// <summary>Foto modu / ara sahne için alan derinliği: odak mesafesi (m). 0 = kapalı (garajda otomatik açılır).</summary>
        public static float FocusOverride;

        public Volume volume;
        VolumeProfile prof;
        Bloom bloom;
        ColorAdjustments colorAdj;
        Vignette vignette;
        ChromaticAberration chroma;
        MotionBlur motionBlur;
        WhiteBalance whiteBalance;
        SplitToning splitToning;
        object filmGrain, dof, lensFlare, liftGammaGain;

        public int quality = 1;
        int atmosphere;
        float baseSat = 8f, baseContrast = 10f, nightExposure, nightAmount;
        public float Night { get; private set; }
        public Color HorizonColor { get; private set; }

        ReflectionProbe probe;
        float probeTimer;
        Texture2D lensDirt;

        // ------------------------------------------------------------------ kurulum
        public static RenderSetup Create()
        {
            var vg = new GameObject("PostFX");
            var r = vg.AddComponent<RenderSetup>();
            r.Build();
            return r;
        }

        void Awake() { I = this; }
        void OnDestroy() { if (I == this) I = null; if (lensDirt != null) Destroy(lensDirt); }
        void OnEnable() { RenderPipelineManager.beginCameraRendering += OnBeginCamera; }
        void OnDisable() { RenderPipelineManager.beginCameraRendering -= OnBeginCamera; }

        /// <summary>Atmosfer geçişi yalnız ana oyun kamerasında (garaj/harita/prob kameralarında kapalı).</summary>
        void OnBeginCamera(ScriptableRenderContext ctx, Camera c)
        {
            bool main = Game.I != null && c == Game.I.cam;
            RR.GlobalFloat("_MW_AtmosOn", main ? 1f : 0f);
        }

        void Build()
        {
            I = this;
            volume = gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            prof = ScriptableObject.CreateInstance<VolumeProfile>();
            volume.sharedProfile = prof;

            bloom = prof.Add<Bloom>(true);
            bloom.intensity.Override(0.32f);
            bloom.threshold.Override(1.1f);
            RR.Param(bloom, "scatter", 0.62f);
            RR.Param(bloom, "clamp", 40f);
            RR.Param(bloom, "highQualityFiltering", false);
            lensDirt = MakeLensDirt(256);
            if (RR.Param(bloom, "dirtTexture", lensDirt)) RR.Param(bloom, "dirtIntensity", 1.4f);

            var tm = prof.Add<Tonemapping>(true);
            tm.mode.Override(TonemappingMode.ACES);

            colorAdj = prof.Add<ColorAdjustments>(true);
            colorAdj.postExposure.Override(0f);
            colorAdj.contrast.Override(baseContrast);
            colorAdj.saturation.Override(baseSat);

            vignette = prof.Add<Vignette>(true);
            vignette.intensity.Override(0.2f);
            RR.Param(vignette, "smoothness", 0.45f);
            RR.Param(vignette, "rounded", false);

            chroma = prof.Add<ChromaticAberration>(true);
            chroma.intensity.Override(0.04f);   // çok hafif lens saçağı (kenarlarda)

            motionBlur = prof.Add<MotionBlur>(true);
            motionBlur.intensity.Override(0f);
            RR.Param(motionBlur, "mode", "CameraOnly");
            RR.Param(motionBlur, "quality", "Low");
            RR.Param(motionBlur, "clamp", 0.04f);

            whiteBalance = prof.Add<WhiteBalance>(true);
            splitToning = prof.Add<SplitToning>(true);

            filmGrain = AddByName("FilmGrain");
            RR.Param(filmGrain, "type", "Thin1");
            RR.Param(filmGrain, "intensity", 0.12f);
            RR.Param(filmGrain, "response", 0.85f);

            liftGammaGain = AddByName("LiftGammaGain");

            dof = AddByName("DepthOfField");
            RR.Param(dof, "mode", "Off");

            // URP 17: bloom tabanlı ekran-uzayı lens flare (güneş ve far parlamaları için ucuz, ek veri gerektirmez)
            lensFlare = AddByName("ScreenSpaceLensFlare");
            RR.Param(lensFlare, "intensity", 0.18f);
            RR.Param(lensFlare, "firstFlareIntensity", 0.6f);
            RR.Param(lensFlare, "secondaryFlareIntensity", 0.5f);
            RR.Param(lensFlare, "warpedFlareIntensity", 0.4f);
            RR.Param(lensFlare, "streaksIntensity", 0.15f);
            RR.Param(lensFlare, "streaksLength", 0.4f);
            RR.Param(lensFlare, "resolution", "Quarter");

            ApplyQuality(SaveSystem.Data != null ? SaveSystem.Data.quality : 1);
        }

        /// <summary>VolumeProfile.Add(Type, bool) — stub/sürüm farklarına karşı yansıma ile.</summary>
        object AddByName(string shortName)
        {
            try
            {
                var t = RR.FindType("UnityEngine.Rendering.Universal." + shortName, typeof(Bloom));
                if (t == null) return null;
                foreach (var m in prof.GetType().GetMethods())
                {
                    if (m.Name != "Add" || m.IsGenericMethod) continue;
                    var ps = m.GetParameters();
                    if (ps.Length == 2 && ps[0].ParameterType == typeof(Type)) return m.Invoke(prof, new object[] { t, true });
                }
            }
            catch (Exception e) { Debug.LogWarning("[MW] Volume bileşeni eklenemedi: " + shortName + " " + e.Message); }
            return null;
        }

        static float Smooth(float a, float b, float t) { t = Mathf.Clamp01(t); t = t * t * (3f - 2f * t); return a + (b - a) * t; }

        static void SetActive(object comp, bool on) { if (comp != null) RR.Set(comp, "active", on); }

        /// <summary>Lens kiri dokusu: yumuşak lekeler + birkaç altıgen bokeh izi (bloom ile çarpılır, yalnız parlak ışıkta görünür).</summary>
        static Texture2D MakeLensDirt(int n)
        {
            var px = new Color[n * n];
            var rnd = new System.Random(1337);
            var blobs = new List<Vector4>();
            for (int i = 0; i < 70; i++)
            {
                float r = i < 10 ? 0.06f + (float)rnd.NextDouble() * 0.08f : 0.008f + (float)rnd.NextDouble() * 0.03f;
                blobs.Add(new Vector4((float)rnd.NextDouble(), (float)rnd.NextDouble(), r, 0.15f + (float)rnd.NextDouble() * 0.5f));
            }
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n, v = (y + 0.5f) / n;
                    // kenarlara doğru daha kirli
                    float du = u - 0.5f, dv = v - 0.5f;
                    float edge = Mathf.Clamp01(Mathf.Sqrt(du * du + dv * dv) * 1.6f);
                    float a = 0.04f + 0.1f * edge * Mathf.PerlinNoise(u * 6f, v * 6f);
                    foreach (var b in blobs)
                    {
                        float dx = u - b.x, dy = v - b.y;
                        float d = Mathf.Sqrt(dx * dx + dy * dy) / b.z;
                        if (d >= 1f) continue;
                        // altıgen benzeri kenar: büyük lekelerde hafif belirgin halka
                        float ring = b.z > 0.05f ? 0.6f + 0.4f * Smooth(0f, 1f, (d - 0.7f) / 0.25f) : 1f;
                        a += b.w * (1f - d * d) * ring * (b.z > 0.05f ? 0.35f : 1f);
                    }
                    a = Mathf.Clamp01(a);
                    px[y * n + x] = new Color(a, a * 0.97f, a * 0.92f, 1f);
                }
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true);
            t.name = "MW_LensDirt";
            t.wrapMode = TextureWrapMode.Clamp;
            t.SetPixels(px);
            t.Apply(true);
            return t;
        }

        // ------------------------------------------------------------------ renk derecelendirme
        /// <summary>Atmosfer: 0 Most Wanted (altın/sepya, teal gölgeler), 1 Normal (nötr), 2 Gün batımı, 3 Gece (teal/turuncu).</summary>
        public void ApplyGrading(int a)
        {
            atmosphere = Mathf.Clamp(a, 0, 3);
            float temp = 0f, tint = 0f, balance = 0f;
            Color sh = new Color(0.5f, 0.5f, 0.5f), hi = new Color(0.5f, 0.5f, 0.5f), filter = Color.white;
            switch (atmosphere)
            {
                case 0: temp = 14f; tint = 4f; sh = new Color(0.40f, 0.48f, 0.52f); hi = new Color(0.72f, 0.60f, 0.42f); balance = 12f; filter = new Color(1f, 0.95f, 0.86f); baseContrast = 14f; baseSat = 4f; break;
                case 1: baseContrast = 8f; baseSat = 8f; break;
                case 2: temp = 20f; tint = 3f; hi = new Color(0.78f, 0.55f, 0.36f); sh = new Color(0.45f, 0.45f, 0.55f); balance = 5f; filter = new Color(1f, 0.94f, 0.88f); baseContrast = 12f; baseSat = 10f; break;
                case 3: temp = -10f; sh = new Color(0.32f, 0.44f, 0.56f); hi = new Color(0.74f, 0.56f, 0.38f); balance = -10f; baseContrast = 16f; baseSat = 6f; break;
            }
            if (whiteBalance != null) { whiteBalance.temperature.Override(temp); whiteBalance.tint.Override(tint); }
            if (splitToning != null) { splitToning.shadows.Override(sh); splitToning.highlights.Override(hi); splitToning.balance.Override(balance); }
            if (colorAdj != null) { colorAdj.colorFilter.Override(filter); colorAdj.contrast.Override(baseContrast); }
            // gölgeleri hafif kaldır (film tonu), MW'de sıcak gain
            if (liftGammaGain != null)
            {
                RR.Param(liftGammaGain, "lift", atmosphere == 3 ? new Vector4(0.96f, 1.0f, 1.04f, 0.0f) : new Vector4(1f, 1f, 1f, 0.0f));
                RR.Param(liftGammaGain, "gain", atmosphere == 0 ? new Vector4(1.03f, 1.0f, 0.95f, 0f) : new Vector4(1f, 1f, 1f, 0f));
            }
        }

        // ------------------------------------------------------------------ güneş / ay / gökyüzü / sis
        /// <summary>Kelvin → doğrusal RGB (Tanner Helland yaklaşıklığı, en parlak kanal 1'e normalize).</summary>
        public static Color Kelvin(float k)
        {
            float t = k / 100f, r, g, b;
            if (t <= 66f) { r = 255f; g = 99.4708f * (float)Math.Log(t) - 161.1196f; }
            else { r = 329.6987f * Mathf.Pow(t - 60f, -0.1332f); g = 288.1222f * Mathf.Pow(t - 60f, -0.0755f); }
            if (t >= 66f) b = 255f; else if (t <= 19f) b = 0f; else b = 138.5177f * (float)Math.Log(t - 10f) - 305.0448f;
            var c = new Color(Mathf.Clamp01(r / 255f), Mathf.Clamp01(g / 255f), Mathf.Clamp01(b / 255f));
            // sRGB → doğrusal (yaklaşık)
            c = new Color(Mathf.Pow(c.r, 2.2f), Mathf.Pow(c.g, 2.2f), Mathf.Pow(c.b, 2.2f));
            float m = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
            return m > 0f ? new Color(c.r / m, c.g / m, c.b / m) : Color.white;
        }

        /// <summary>
        /// Gün saatinden (0..1, 0.25 gün doğumu, 0.75 gün batımı) fiziksel olarak makul güneş/ay ışığı, ortam, gökyüzü ve sis.
        /// Gece değerini (0..1) döner.
        /// </summary>
        public float UpdateSun(Light sun, Material sky, float dayTime)
        {
            float sunAngle = (dayTime - 0.25f) * 360f;
            float elev = Mathf.Sin(sunAngle * Mathf.Deg2Rad);
            float day = Mathf.Clamp01((elev + 0.08f) / 0.3f);
            Night = 1f - day;
            float hi = Mathf.Clamp01((elev - 0.04f) / 0.5f);   // 0 ufuk .. 1 yüksek güneş
            Vector3 sunDir;
            if (sun != null)
            {
                if (elev > -0.05f)
                {
                    sun.transform.rotation = Quaternion.Euler(sunAngle, -35f, 0f);
                    // atmosferde yol uzadıkça şiddet düşer ve renk ısınır (alçak güneş ≈ 2000 K, öğle ≈ 5800 K)
                    float k = Mathf.Lerp(1900f, 5800f, Smooth(0f, 1f, Mathf.Clamp01(elev / 0.6f)));
                    sun.color = Kelvin(k);
                    sun.intensity = Mathf.Max(0.05f, 2.0f * Mathf.Pow(Mathf.Clamp01((elev + 0.03f) / 0.4f), 0.75f));
                    sun.shadowStrength = Mathf.Lerp(0.55f, 0.92f, day);
                }
                else
                {
                    // ay: soğuk, zayıf, yumuşak gölgeli
                    sun.transform.rotation = Quaternion.Euler(sunAngle - 180f, -35f, 0f);
                    sun.intensity = 0.2f;
                    sun.color = new Color(0.55f, 0.66f, 1f);
                    sun.shadowStrength = 0.6f;
                }
                sunDir = -sun.transform.forward;
                RR.GlobalVector("_MW_SunDir", new Vector4(sunDir.x, sunDir.y, sunDir.z, elev));
                var sc = sun.color * sun.intensity;
                RR.GlobalColor("_MW_SunColor", new Color(sc.r, sc.g, sc.b, day));
            }

            // ufuk / sis rengi gökyüzüyle eşleşir: alçak güneşte sıcak pus, yüksekte mavi-gri, gece lacivert
            Color dayHorizon = Color.Lerp(new Color(0.88f, 0.70f, 0.52f), new Color(0.66f, 0.75f, 0.86f), hi);
            if (atmosphere == 0) dayHorizon = Color.Lerp(dayHorizon, new Color(0.86f, 0.76f, 0.6f), 0.35f);
            Color nightHorizon = new Color(0.04f, 0.05f, 0.085f);
            HorizonColor = Color.Lerp(nightHorizon, dayHorizon, day);
            RenderSettings.fogColor = HorizonColor;
            RR.GlobalColor("_MW_Horizon", HorizonColor);
            RR.GlobalFloat("_MW_Night", Night);

            if (sky != null)
            {
                if (sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", Mathf.Lerp(0.18f, 1.25f, day));
                if (sky.HasProperty("_AtmosphereThickness")) sky.SetFloat("_AtmosphereThickness", Mathf.Lerp(1.25f, 0.95f, hi));
                if (sky.HasProperty("_SkyTint")) sky.SetColor("_SkyTint", atmosphere == 0 ? new Color(0.55f, 0.5f, 0.45f) : new Color(0.5f, 0.5f, 0.5f));
                if (sky.HasProperty("_GroundColor")) sky.SetColor("_GroundColor", HorizonColor * 0.45f);
            }
            RenderSettings.ambientIntensity = Mathf.Lerp(0.55f, 1f, day);
            // basit göz adaptasyonu: gece biraz pozlama artışı
            nightExposure = Mathf.Lerp(0f, 0.55f, Night);
            nightAmount = Night;
            return Night;
        }

        // ------------------------------------------------------------------ oyun içi post efektleri
        public void UpdatePostFx(float kmh, bool nitro, bool speedbreaker)
        {
            float dt = Time.unscaledDeltaTime;
            if (colorAdj != null)
            {
                colorAdj.saturation.value = Mathf.Lerp(colorAdj.saturation.value, speedbreaker ? -45f : baseSat, dt * 5f);
                colorAdj.postExposure.value = Mathf.Lerp(colorAdj.postExposure.value, nightExposure, dt * 2f);
            }
            if (vignette != null) vignette.intensity.value = Mathf.Lerp(vignette.intensity.value, speedbreaker ? 0.45f : (nitro ? 0.3f : 0.2f), dt * 5f);
            if (chroma != null) chroma.intensity.value = Mathf.Lerp(chroma.intensity.value, nitro ? 0.55f : 0.04f, dt * 4f);
            // hıza bağlı hafif kamera hareket bulanıklığı (yalnız kamera, düşük yoğunluk)
            if (motionBlur != null) motionBlur.intensity.value = Mathf.Clamp01((kmh - 110f) / 220f) * 0.25f + (nitro ? 0.12f : 0f);
        }

        float fogBaseTimer, fogBaseY;
        bool fogBaseInit;

        void LateUpdate()
        {
            fogBaseTimer -= Time.unscaledDeltaTime;
            if (fogBaseTimer <= 0f && Game.I != null && Game.I.player != null)
            {
                // yükseklik sisi tabanı: oyuncunun bulunduğu zemin seviyesinin biraz altı (köprü/tepe farkları için yavaş takip)
                fogBaseTimer = 0.5f;
                fogBaseY = Mathf.Lerp(fogBaseY, Game.I.player.transform.position.y - 3f, fogBaseInit ? 0.15f : 1f);
                fogBaseInit = true;
                RR.GlobalFloat("_MW_FogBaseY", fogBaseY);
            }
            UpdateDof();
            UpdateProbe();
        }

        void UpdateDof()
        {
            if (dof == null) return;
            float focus = FocusOverride;
            if (focus <= 0f && GarageStage.I != null && GarageStage.I.Active)
            {
                Vector3 target = GarageStage.Origin + Vector3.up * 0.7f;
                var cams = Camera.allCameras;
                if (cams != null)
                    foreach (var c in cams)
                        if (c != null && c.enabled && c.transform.position.y < -1000f) { focus = Vector3.Distance(c.transform.position, target); break; }
                if (focus <= 0f) focus = 6f;
            }
            bool on = focus > 0f && quality >= 1;
            SetActive(dof, on);
            if (!on) { RR.Param(dof, "mode", "Off"); return; }
            if (quality >= 2)
            {
                RR.Param(dof, "mode", "Bokeh");
                RR.Param(dof, "focusDistance", focus);
                RR.Param(dof, "aperture", 4f);
                RR.Param(dof, "focalLength", 55f);
                RR.Param(dof, "bladeCount", 6);
            }
            else
            {
                RR.Param(dof, "mode", "Gaussian");
                RR.Param(dof, "gaussianStart", focus + 2f);
                RR.Param(dof, "gaussianEnd", focus + 18f);
                RR.Param(dof, "gaussianMaxRadius", 1.2f);
                RR.Param(dof, "highQualitySampling", false);
            }
        }

        // ------------------------------------------------------------------ oyuncu yansıma probu
        void UpdateProbe()
        {
            var g = Game.I;
            bool want = quality >= 1 && g != null && g.player != null;
            if (!want)
            {
                if (probe != null && probe.gameObject.activeSelf) probe.gameObject.SetActive(false);
                return;
            }
            if (probe == null)
            {
                var pg = new GameObject("MW_AracYansimaProbu");
                pg.transform.SetParent(transform, false);
                probe = pg.AddComponent<ReflectionProbe>();
                probe.mode = ReflectionProbeMode.Realtime;
                probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
                probe.size = new Vector3(600f, 300f, 600f);
                probe.farClipPlane = 450f;
                RR.Set(probe, "nearClipPlane", 0.5f);
                RR.Set(probe, "importance", 10);
                RR.Set(probe, "hdr", true);
                RR.Set(probe, "shadowDistance", 0f);   // prob render'ında gölge yok (ucuz)
                RR.Set(probe, "boxProjection", false);
                RR.Set(probe, "cullingMask", ~((1 << U.IconLayer) | (1 << OptimizationManager.DetailLayer)));
                ConfigureProbe();
            }
            if (!probe.gameObject.activeSelf) probe.gameObject.SetActive(true);
            // aracın biraz üstünde: kendi gövdesini değil çevreyi görsün
            probe.transform.position = g.player.transform.position + Vector3.up * 2.6f;
            probeTimer -= Time.unscaledDeltaTime;
            if (probeTimer <= 0f)
            {
                probeTimer = quality >= 2 ? 0.4f : 2f;
                probe.RenderProbe();
            }
        }

        void ConfigureProbe()
        {
            if (probe == null) return;
            probe.resolution = quality >= 2 ? 128 : 64;
            probe.timeSlicingMode = quality >= 2 ? ReflectionProbeTimeSlicingMode.IndividualFaces : ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
        }

        // ------------------------------------------------------------------ kalite
        /// <summary>Post efekt + URP gölge/SSAO/AA/atmosfer kalitesi. OptimizationManager.Apply sonunda (Game.SetPostQuality) çağrılır.</summary>
        public void ApplyQuality(int q)
        {
            quality = Mathf.Clamp(q, 0, 2);
            if (bloom != null) { bloom.active = quality >= 1; bloom.threshold.value = 1.1f; }
            if (motionBlur != null) { motionBlur.active = quality >= 1; RR.Param(motionBlur, "quality", quality >= 2 ? "Medium" : "Low"); }
            if (chroma != null) chroma.active = quality >= 1;
            SetActive(filmGrain, quality >= 1);
            SetActive(lensFlare, quality >= 1);
            RR.Param(lensFlare, "resolution", quality >= 2 ? "Half" : "Quarter");
            QualitySettings.realtimeReflectionProbes = quality >= 1;
            ConfigureProbe();
            probeTimer = 0f;

            var a = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (a != null) PipelineQuality(a, quality);
            CameraQuality(Game.I != null ? Game.I.cam : null, quality);
            // atmosfer (yükseklik sisi + ışık huzmeleri) gölgelendirici globalleri
            RR.GlobalFloat("_MW_ShaftSamples", quality >= 2 ? 24f : 12f);
            RR.GlobalFloat("_MW_ShaftIntensity", quality >= 2 ? 0.55f : 0.45f);
            RR.GlobalFloat("_MW_FogHeightDensity", 0.0018f);
            RR.GlobalFloat("_MW_FogHeightFalloff", 0.06f);
        }

        /// <summary>Gölge kaskadları, bias, yumuşak gölge kalitesi, SSAO ve atmosfer renderer özellikleri.</summary>
        public static void PipelineQuality(UniversalRenderPipelineAsset a, int q)
        {
            if (q >= 1)
            {
                a.shadowCascadeCount = q >= 2 ? 4 : 2;
                RR.Set(a, "mainLightShadowmapResolution", q >= 2 ? 4096 : 2048);
                RR.Set(a, "additionalLightsShadowmapResolution", q >= 2 ? 2048 : 1024);
                RR.Set(a, "supportsSoftShadows", true);
                RR.Set(a, "softShadowQuality", q >= 2 ? "High" : "Medium");
            }
            else
            {
                a.shadowCascadeCount = 1;
                RR.Set(a, "mainLightShadowmapResolution", 1024);
                RR.Set(a, "softShadowQuality", "Low");
            }
            // ilk kaskad ~arabanın etrafı kadar dar: keskin yakın gölgeler; sınırda yumuşak geçiş
            RR.Set(a, "cascade2Split", 0.16f);
            RR.Set(a, "cascade3Split", new Vector2(0.08f, 0.3f));
            RR.Set(a, "cascade4Split", new Vector3(0.05f, 0.15f, 0.4f));
            RR.Set(a, "cascadeBorder", 0.15f);
            // URP birimi: texel cinsinden; 1/1 acne yapmaz, düşük değerler peter-panning'i azaltır
            RR.Set(a, "shadowDepthBias", q >= 2 ? 0.6f : 0.8f);
            RR.Set(a, "shadowNormalBias", q >= 2 ? 0.5f : 0.7f);
            RR.Set(a, "supportsMainLightShadows", true);

            foreach (var rd in RendererDatas(a))
            {
                var feats = RR.Get(rd, "rendererFeatures") as System.Collections.IList;
                if (feats == null) continue;
                foreach (var fo in feats)
                {
                    var feat = fo as ScriptableRendererFeature;
                    if (feat == null) continue;
                    string tn = feat.GetType().Name;
                    if (tn == "ScreenSpaceAmbientOcclusion")
                    {
                        feat.SetActive(q >= 1);
                        var s = RR.Get(feat, "m_Settings");
                        if (s != null)
                        {
                            RR.Set(s, "Downsample", q < 2);
                            RR.Set(s, "Intensity", q >= 2 ? 0.9f : 0.7f);
                            RR.Set(s, "Radius", q >= 2 ? 0.35f : 0.3f);
                            RR.Set(s, "DirectLightingStrength", 0.2f);
                            RR.Set(s, "Falloff", 60f);
                            RR.Set(s, "Samples", q >= 2 ? "Medium" : "Low");
                            RR.Set(s, "BlurQuality", q >= 2 ? "High" : "Low");
                            RR.Set(s, "Source", "DepthNormals");
                            RR.Set(s, "AfterOpaque", false);
                        }
                    }
                    else if (feat.name == MWAtmosphereFeatureName) feat.SetActive(q >= 1);
                }
            }
        }

        public const string MWAtmosphereFeatureName = "MW_Atmosfer";

        public static IEnumerable<ScriptableRendererData> RendererDatas(UniversalRenderPipelineAsset a)
        {
            var list = RR.Get(a, "m_RendererDataList") as ScriptableRendererData[];
            if (list == null) yield break;
            foreach (var rd in list) if (rd != null) yield return rd;
        }

        /// <summary>Kenar yumuşatma: Düşük FXAA, Orta SMAA (STP açıkken STP), Yüksek TAA (yoksa SMAA High).</summary>
        public static void CameraQuality(Camera cam, int q)
        {
            if (cam == null) return;
            var cd = cam.GetUniversalAdditionalCameraData();
            if (cd == null) return;
            cd.renderPostProcessing = true;
            if (q >= 2)
            {
                if (!RR.Set(cd, "antialiasing", "TemporalAntiAliasing"))
                {
                    cd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                    RR.Set(cd, "antialiasingQuality", "High");
                }
                else
                {
                    string tf = "m_TaaSettings";
                    var ts = RR.Get(cd, tf);
                    if (ts == null) { tf = "taaSettings"; ts = RR.Get(cd, tf); }
                    if (ts != null)
                    {
                        // yüksek hızda bulaşmayı sınırla; keskinlik ince çizgilerde parıltıyı keser
                        RR.Set(ts, "quality", "High");
                        RR.Set(ts, "baseBlendFactor", 0.88f);
                        RR.Set(ts, "varianceClampScale", 0.9f);
                        RR.Set(ts, "contrastAdaptiveSharpening", 0.35f);
                        RR.Set(cd, tf, ts);   // yapı (struct): kutulu kopyayı geri yaz
                    }
                }
            }
            else if (q == 1)
            {
                cd.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                RR.Set(cd, "antialiasingQuality", "Medium");
            }
            else cd.antialiasing = AntialiasingMode.FastApproximateAntialiasing;
            // FSR/STP ölçeklemede keskinleştirme
            var a = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (a != null)
            {
                RR.Set(a, "fsrOverrideSharpness", true);
                RR.Set(a, "fsrSharpness", 0.75f);
            }
        }
    }
}
