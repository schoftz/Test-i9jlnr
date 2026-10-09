using System;
using System.Reflection;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MostWanted
{
    /// <summary>
    /// Performans yönetimi: Düşük/Orta/Yüksek/Otomatik ön ayarları, URP ayarları (render scale + FSR/STP,
    /// gölge, MSAA kapalı, SRP Batcher, GPU Resident Drawer, GPU oklüzyon), kamera uzak düzlemi ve katman
    /// mesafeleri, fizik adımı, trafik sayısı, FPS hedefi ve istatistik ölçümleri.
    /// </summary>
    public class OptimizationManager : MonoBehaviour
    {
        public const int DetailLayer = 28;   // ağaçlar, direkler, küçük objeler
        public const int TrafficLayer = 27;  // trafik araçları
        public static readonly string[] PresetNames = { "Düşük", "Orta", "Yüksek", "Otomatik" };
        public static readonly int[] FpsTargets = { 30, 60, 120, -1 };
        public static readonly string[] FpsNames = { "30 (Pil tasarrufu)", "60", "120 (ProMotion)", "Sınırsız" };

        public int preset = 1;         // 0..3
        public int effective = 1;      // otomatikte uygulanan seviye
        public float renderScale = 0.75f;
        public float avgFps = 60f, frameMs = 16.7f, cpuMs, gpuMs;
        public long batches, setPass, tris;
        public string upscaler = "-";

        ProfilerRecorder recBatches, recSetPass, recTris;
        readonly FrameTiming[] timings = new FrameTiming[1];
        float autoTimer, autoAccum, autoCooldown;
        int autoFrames;

        public static readonly float[] Scales = { 0.6f, 0.75f, 1f };
        static readonly float[] ShadowDist = { 60f, 120f, 200f };
        static readonly int[] Cascades = { 1, 2, 2 };
        static readonly int[] ShadowRes = { 1024, 2048, 2048 };
        static readonly float[] FarClip = { 800f, 1200f, 1800f };
        static readonly float[] DetailCull = { 150f, 250f, 400f };
        public static readonly int[] TrafficCount = { 10, 20, 35 };
        public static readonly int[] PoliceCap = { 7, 9, 11 };

        void OnEnable()
        {
            try
            {
                recBatches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
                recSetPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
                recTris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            }
            catch (Exception) { }
        }

        void OnDisable()
        {
            recBatches.Dispose(); recSetPass.Dispose(); recTris.Dispose();
        }

        public void Init(int savedPreset, int fpsIndex)
        {
            preset = Mathf.Clamp(savedPreset, 0, 3);
            SetFpsTarget(fpsIndex);
            effective = preset == 3 ? 1 : preset;
            renderScale = Scales[effective];
            Apply(effective, renderScale);
            var cfg = AudioSettings.GetConfiguration();
            if (cfg.numRealVoices > 32) { cfg.numRealVoices = 32; AudioSettings.Reset(cfg); }
        }

        public void SetPreset(int p)
        {
            preset = Mathf.Clamp(p, 0, 3);
            effective = preset == 3 ? Mathf.Clamp(effective, 0, 2) : preset;
            renderScale = Scales[effective];
            Apply(effective, renderScale);
            autoCooldown = 6f;
        }

        public static void SetFpsTarget(int i)
        {
            i = Mathf.Clamp(i, 0, FpsTargets.Length - 1);
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = FpsTargets[i];
        }

        /// <summary>Seviyeyi uygular (0..2) — render scale ayrıca verilebilir (otomatik ince ayar).</summary>
        public void Apply(int q, float scale)
        {
            q = Mathf.Clamp(q, 0, 2);
            var g = Game.I;
            var a = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (a != null)
            {
                a.renderScale = scale;
                a.msaaSampleCount = 1;
                a.shadowDistance = ShadowDist[q];
                a.shadowCascadeCount = Cascades[q];
                a.supportsHDR = true;
                a.supportsCameraDepthTexture = false;
                a.supportsCameraOpaqueTexture = false;
                a.useSRPBatcher = true;
                SetProp(a, "mainLightShadowmapResolution", ShadowRes[q]);
                SetProp(a, "maxAdditionalLightsCount", 4);
                SetProp(a, "supportsSoftShadows", q > 0);
                SetEnumProp(a, "hdrColorBufferPrecision", "_32Bits");
                // Upscaler: Unity 6'da STP, yoksa FSR
                if (scale < 0.99f)
                {
                    if (SetEnumProp(a, "upscalingFilter", "STP")) upscaler = "STP";
                    else if (SetEnumProp(a, "upscalingFilter", "FSR")) upscaler = "FSR";
                    else upscaler = "Bilinear";
                }
                else { SetEnumProp(a, "upscalingFilter", "Auto"); upscaler = "Yok (%100)"; }
#if UNITY_6000_0_OR_NEWER
                // GRD sadece tüm renderer'lar Forward+/Deferred+ ise (aksi halde Unity uyarı verir ve kapatır)
                bool grdOk = RenderersSupportGRD(a);
                SetEnumProp(a, "gpuResidentDrawerMode", grdOk ? "InstancedDrawing" : "Disabled");
                SetProp(a, "gpuResidentDrawerEnableOcclusionCullingInCameras", grdOk && q >= 1);
#endif
                SetRendererFeature(a, "ScreenSpaceAmbientOcclusion", q == 2);
            }
            QualitySettings.shadowDistance = ShadowDist[q];
            QualitySettings.shadowCascades = Cascades[q];
            QualitySettings.pixelLightCount = 4;
            QualitySettings.realtimeReflectionProbes = false;
            QualitySettings.lodBias = q == 0 ? 0.7f : q == 1 ? 1f : 1.5f;

            if (g != null) g.baseFixedDelta = q == 0 ? 0.02f : 1f / 60f;
            Physics.defaultSolverIterations = 6;
            Physics.defaultSolverVelocityIterations = 1;

            if (g != null)
            {
                if (g.cam != null)
                {
                    g.cam.farClipPlane = FarClip[q];
                    var d = new float[32];
                    d[DetailLayer] = DetailCull[q];
                    d[TrafficLayer] = 300f;
                    g.cam.layerCullDistances = d;
                    var cd = g.cam.GetUniversalAdditionalCameraData();
                    if (cd != null) cd.antialiasing = q == 2 ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : AntialiasingMode.FastApproximateAntialiasing;
                }
                RenderSettings.fogDensity = q == 0 ? 0.0028f : q == 1 ? 0.0018f : 0.0012f;
                if (g.traffic != null) g.traffic.count = TrafficCount[q];
                if (g.police != null) g.police.maxUnits = PoliceCap[q];
                g.SetPostQuality(q);
            }
        }

        // ---- yansıma yardımcıları (sürüme göre bulunmayabilecek özellikler) ----
        static bool SetProp(object o, string name, object value)
        {
            try
            {
                var p = o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p == null || !p.CanWrite) return false;
                p.SetValue(o, Convert.ChangeType(value, p.PropertyType), null);
                return true;
            }
            catch (Exception) { return false; }
        }

        static bool SetEnumProp(object o, string name, string enumName)
        {
            try
            {
                var p = o.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (p == null || !p.CanWrite || !p.PropertyType.IsEnum) return false;
                if (!Enum.IsDefined(p.PropertyType, enumName)) return false;
                p.SetValue(o, Enum.Parse(p.PropertyType, enumName), null);
                return true;
            }
            catch (Exception) { return false; }
        }

        static bool RenderersSupportGRD(UniversalRenderPipelineAsset a)
        {
            try
            {
                var f = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
                var list = f != null ? f.GetValue(a) as ScriptableRendererData[] : null;
                if (list == null) return false;
                foreach (var rd in list)
                {
                    if (rd == null) continue;
                    var p = rd.GetType().GetProperty("renderingMode");
                    if (p == null) return false;
                    string m = p.GetValue(rd, null).ToString();
                    if (!m.Contains("Plus")) return false;
                }
                return true;
            }
            catch (Exception) { return false; }
        }

        /// <summary>URP renderer özelliğini (ör. SSAO) aç/kapat — renderer verisine yansıma ile erişilir.</summary>
        static void SetRendererFeature(UniversalRenderPipelineAsset a, string typeName, bool on)
        {
            try
            {
                var f = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
                var list = f != null ? f.GetValue(a) as ScriptableRendererData[] : null;
                if (list == null) return;
                foreach (var rd in list)
                {
                    if (rd == null) continue;
                    foreach (var feat in rd.rendererFeatures)
                        if (feat != null && feat.GetType().Name == typeName) feat.SetActive(on);
                }
            }
            catch (Exception) { }
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            avgFps = Mathf.Lerp(avgFps, 1f / Mathf.Max(dt, 0.0001f), 0.05f);
            frameMs = 1000f / Mathf.Max(1f, avgFps);
            try
            {
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timings) > 0)
                {
                    cpuMs = (float)timings[0].cpuFrameTime;
                    gpuMs = (float)timings[0].gpuFrameTime;
                }
            }
            catch (Exception) { }
            if (recBatches.Valid) batches = recBatches.LastValue;
            if (recSetPass.Valid) setPass = recSetPass.LastValue;
            if (recTris.Valid) tris = recTris.LastValue;

            // ---- Otomatik: 5 sn ortalama, histerezisli ----
            if (preset != 3 || Time.timeScale == 0f) return;
            autoCooldown -= dt;
            autoTimer += dt; autoAccum += dt; autoFrames++;
            if (autoTimer < 5f) return;
            float fps = autoFrames / Mathf.Max(0.001f, autoAccum);
            autoTimer = 0f; autoAccum = 0f; autoFrames = 0;
            if (autoCooldown > 0f) return;
            int target = Mathf.Max(30, Application.targetFrameRate > 0 ? Application.targetFrameRate : 60);
            float low = Mathf.Min(50f, target * 0.83f), high = Mathf.Min(75f, target * 1.2f);
            if (fps < low)
            {
                if (renderScale > Scales[effective] - 0.1f && renderScale > 0.55f) renderScale = Mathf.Max(0.5f, renderScale - 0.05f);
                else if (effective > 0) { effective--; renderScale = Scales[effective]; }
                Apply(effective, renderScale);
                autoCooldown = 8f;
                if (Game.I != null) Game.I.Toast("Otomatik grafik: " + PresetNames[effective] + " (ölçek " + Mathf.RoundToInt(renderScale * 100f) + "%)");
            }
            else if (fps > high)
            {
                if (renderScale < Scales[effective] - 0.01f) renderScale = Mathf.Min(Scales[effective], renderScale + 0.05f);
                else if (effective < 2) { effective++; renderScale = Scales[effective]; }
                else return;
                Apply(effective, renderScale);
                autoCooldown = 12f;
            }
        }
    }
}
