using UnityEngine;

namespace MostWanted
{
    /// <summary>
    /// Carbon tarzı tam ekran yükleme ekranı: koyu arka plan + yavaş kayan soluk chevronlar, teal ilerleme çubuğu,
    /// o anki kurulum adımı ve dönen Türkçe ipuçları. Game.InitCo adımları Begin/Set/End çağırır.
    /// DontDestroyOnLoad: harita değişiminde (Rebuild sahne köklerini silerken) yaşamaya devam eder.
    /// </summary>
    public class LoadingScreen : MonoBehaviour
    {
        static LoadingScreen inst;
        static readonly string[] Tips =
        {
            "İpucu: Polis 180 km/s üstünde seni fark eder.",
            "İpucu: El freni + direksiyon = drift.",
            "İpucu: Garajda (E) arabanı boyayıp tuning yapabilirsin.",
            "İpucu: Saklanma yerleri minimapte mavi S ile görünür.",
            "İpucu: Takipten kaçmak için görüş alanından çık ve bir saklanma yerinde bekle.",
            "İpucu: Pursuit Breaker'lara çarparak peşindeki polisleri devre dışı bırak.",
            "İpucu: Q ile Speedbreaker zamanı yavaşlatır — dar virajlarda hayat kurtarır.",
            "İpucu: Haritada (M) bir yarışa tıklayınca GPS rotası çizilir.",
            "İpucu: Ters döndüysen R ile aracı düzelt.",
        };
        const float TipDur = 5f;

        float progress, shown, fade = 1f;
        string step = "";
        int tip;
        float tipT;
        bool active;
        Camera blackCam;

        /// <summary>Yükleme ekranı açık mı (kapanış animasyonu dahil).</summary>
        public static bool Active { get { return inst != null && (inst.active || inst.fade > 0f); } }

        public static void Begin()
        {
            if (inst == null)
            {
                var go = new GameObject("YuklemeEkrani");
                DontDestroyOnLoad(go);
                inst = go.AddComponent<LoadingScreen>();
            }
            inst.active = true; inst.progress = 0f; inst.shown = 0f; inst.fade = 1f; inst.step = "";
            inst.tip = Random.Range(0, Tips.Length); inst.tipT = 0f;
            if (inst.blackCam == null)
            {
                // sahnede kamera yokken "kamera yok" uyarısı yerine siyah ekran
                var cgo = new GameObject("YuklemeKamerasi");
                cgo.transform.SetParent(inst.transform);
                inst.blackCam = cgo.AddComponent<Camera>();
                inst.blackCam.clearFlags = CameraClearFlags.SolidColor;
                inst.blackCam.backgroundColor = Color.black;
                inst.blackCam.cullingMask = 0;
                inst.blackCam.depth = 100;
            }
        }

        public static void Set(float p, string s)
        {
            if (inst == null) return;
            inst.progress = Mathf.Max(inst.progress, Mathf.Clamp01(p));
            if (!string.IsNullOrEmpty(s)) inst.step = s;
        }

        /// <summary>Yüklemeyi bitirir; ekran 0.45 sn'de kararır/kaybolur.</summary>
        public static void End()
        {
            if (inst == null) return;
            inst.active = false;
            inst.progress = 1f;
            if (inst.blackCam != null) { Destroy(inst.blackCam.gameObject); inst.blackCam = null; }
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            shown = Mathf.MoveTowards(shown, progress, dt * 1.5f);
            if (!active) fade = Mathf.MoveTowards(fade, 0f, dt / 0.45f);
            tipT += dt;
            if (tipT > TipDur) { tipT = 0f; tip = (tip + 1) % Tips.Length; }
        }

        void OnGUI()
        {
            if (!active && fade <= 0f) return;
            GUI.depth = -100;
            var oldM = GUI.matrix;
            float oldA = UIKit.Alpha;
            UIKit.Begin();
            UIKit.Alpha = fade;
            float t = Time.unscaledTime;

            // arka plan: koyu lacivert-siyah, hafif dikey ton farkı
            UIKit.Tex(UIKit.Full(), UIKit.White, new Color(0.012f, 0.022f, 0.03f, 1f));
            UIKit.Tex(new Rect(0, Screen.height * 0.55f, Screen.width, Screen.height * 0.45f), UIKit.White, new Color(0.02f, 0.06f, 0.07f, 0.5f));

            // soluk, sağa kayan chevron şeritleri (Carbon)
            for (int row = 0; row < 7; row++)
            {
                float y = 60 + row * 150;
                float speed = 30f + (row % 3) * 14f;
                float off = Mathf.Repeat(t * speed + row * 97f, 240f);
                float a = 0.035f + 0.025f * Mathf.Sin(t * 0.7f + row);
                for (int k = -1; k < 10; k++)
                    UIKit.Tex(UIKit.R(k * 240 + off - 120, y, 192, 96), UIKit.ChevronTex, new Color(1, 1, 1, a));
            }
            UIKit.Tex(UIKit.Full(), UIKit.VignetteTex, Color.white);

            // logo
            UIKit.Tex(UIKit.R(120, 120, 120, 60), UIKit.ChevronTex, Color.white);
            UIKit.Text(260, 100, 1200, 100, "MOST WANTED", 84, Color.white);
            UIKit.Text(266, 190, 1200, 40, "SOKAK YARIŞI", 30, UIKit.Teal);

            // yükleniyor: üç chevron sırayla yanar
            for (int k = 0; k < 3; k++)
            {
                float a = 0.25f + 0.75f * Mathf.Clamp01(Mathf.Sin(t * 5f - k * 0.9f));
                UIKit.Tex(UIKit.R(1676 + k * 44, 846, 40, 20), UIKit.ChevronTex, new Color(1, 1, 1, a));
            }
            UIKit.Text(120, 830, 1200, 50, "YÜKLENİYOR", 36, Color.white);
            UIKit.Text(1280, 880, 520, 30, Mathf.RoundToInt(shown * 100f) + "%", 24, UIKit.Teal, TextAnchor.MiddleRight);

            // ilerleme çubuğu (teal) + parlayan uç
            UIKit.Bar(120, 890, 1680, 14, new Color(0.15f, 0.18f, 0.2f, 0.9f));
            float bw = Mathf.Max(20f, 1680f * shown);
            UIKit.Bar(120, 890, bw, 14, UIKit.Teal);
            float glow = 0.5f + 0.5f * Mathf.Sin(t * 6f);
            UIKit.Fill(120 + bw - 6, 886, 6, 22, new Color(1, 1, 1, 0.5f + 0.4f * glow));
            UIKit.Text(120, 910, 1200, 34, step, 18, UIKit.Grey, TextAnchor.MiddleLeft, false, false);

            // ipucu paneli (yumuşak geçişli)
            float ta = Mathf.Clamp01(Mathf.Min(tipT / 0.4f, (TipDur - tipT) / 0.4f));
            UIKit.Panel(120, 960, 1680, 70, 0.9f);
            UIKit.Fill(140, 975, 5, 40, UIKit.Green);
            UIKit.Alpha = fade * ta;
            UIKit.Text(165, 960, 1620, 70, Tips[tip], 22, Color.white);

            UIKit.Alpha = oldA;
            GUI.matrix = oldM;
        }
    }
}
