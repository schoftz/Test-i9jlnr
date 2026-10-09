using System.Collections.Generic;
using UnityEngine;

namespace MostWanted
{
    /// <summary>
    /// Carbon tarzı IMGUI yardımcıları. Tasarım tuvali 1920x1080; ölçek GUI.matrix ile DEĞİL,
    /// koordinat + font boyutu ölçeklenerek uygulanır (Retina'da keskin metin, sıfır ölçekli matris yok).
    /// Girdi Repaint geçişinde işlenir (OnGUI çok kez çağrılsa da bir tık bir kez sayılır).
    /// </summary>
    public static class UIKit
    {
        public const float DW = 1920f, DH = 1080f;
        public static readonly Color Teal = new Color(0.373f, 0.878f, 0.816f);   // #5fe0d0
        public static readonly Color Green = new Color(0.45f, 0.95f, 0.25f);
        public static readonly Color Grey = new Color(0.6f, 0.66f, 0.7f);
        public static readonly Color Dark = new Color(0.02f, 0.05f, 0.07f);
        public static readonly Color Danger = new Color(1f, 0.33f, 0.28f);

        public static float S = 1f, OX, OY;      // ölçek ve ofset (piksel)
        public static float Mx, My;              // fare (tasarım koordinatı)
        public static bool Rep;                  // bu geçiş Repaint mi
        public static bool Click, MouseHeld;     // Update tarafından doldurulur
        public static float Alpha = 1f;          // genel opaklık (aç/kapa animasyonu)

        public static Texture2D PanelTex, BarTex, PillTex, DotTex, VignetteTex, ChevronTex, White;
        public static Texture2D IcoCoin, IcoFlag, IcoEscape, IcoCuffs, IcoRank, IcoCheck, IcoCross, IcoRing;
        static GUIStyle panelStyle, barStyle, pillStyle;
        static Font font;
        static bool ready;

        // ------------------------------------------------------------ çerçeve
        /// <summary>Tuvali ekrana yerleştirir. Mutlaka geçerli (sıfır olmayan) ölçek üretir.</summary>
        public static void Begin()
        {
            if (!ready) Init();
            float sw = Mathf.Max(1, Screen.width), sh = Mathf.Max(1, Screen.height);
            S = Mathf.Max(0.05f, Mathf.Min(sw / DW, sh / DH));
            OX = (sw - DW * S) * 0.5f; OY = (sh - DH * S) * 0.5f;
            Vector3 m = Input.mousePosition;
            Mx = (m.x - OX) / S; My = (sh - m.y - OY) / S;
            Rep = Event.current != null && Event.current.type == EventType.Repaint;
            GUI.matrix = Matrix4x4.identity;
        }
        /// <summary>Repaint geçişinin sonunda çağrılır: bekleyen tık tüketilir.</summary>
        public static void End() { if (Rep) Click = false; }

        public static Rect R(float x, float y, float w, float h) { return new Rect(OX + x * S, OY + y * S, w * S, h * S); }
        public static Rect Full() { return new Rect(0, 0, Screen.width, Screen.height); }
        public static bool Hit(float x, float y, float w, float h) { return Mx >= x && Mx <= x + w && My >= y && My <= y + h; }
        public static bool Clicked(float x, float y, float w, float h) { if (Rep && Click && Hit(x, y, w, h)) { Click = false; return true; } return false; }

        // ------------------------------------------------------------ çizim
        public static void Tex(Rect px, Texture t, Color c)
        {
            if (t == null) return;
            var o = GUI.color; GUI.color = new Color(c.r, c.g, c.b, c.a * Alpha); GUI.DrawTexture(px, t); GUI.color = o;
        }
        public static void Fill(float x, float y, float w, float h, Color c) { Tex(R(x, y, w, h), White, c); }
        public static void Icon(Texture2D t, float x, float y, float sz, Color c) { Tex(R(x, y, sz, sz), t, c); }
        static void Sliced(GUIStyle st, float x, float y, float w, float h, Color c)
        {
            var o = GUI.color; GUI.color = new Color(c.r, c.g, c.b, c.a * Alpha);
            GUI.Box(R(x, y, w, h), GUIContent.none, st); GUI.color = o;
        }
        /// <summary>Açılı köşeli koyu panel, teal kenar.</summary>
        public static void Panel(float x, float y, float w, float h, float a = 1f) { Sliced(panelStyle, x, y, w, h, new Color(1, 1, 1, a)); }
        /// <summary>Paralelkenar şerit (seçim çubuğu, segment, buton).</summary>
        public static void Bar(float x, float y, float w, float h, Color c) { Sliced(barStyle, x, y, w, h, c); }
        public static void Pill(float x, float y, float w, float h, Color c) { Sliced(pillStyle, x, y, w, h, c); }

        static readonly Dictionary<int, GUIStyle> styles = new Dictionary<int, GUIStyle>();
        static GUIStyle St(int size, bool italic, TextAnchor a, bool wrap)
        {
            int px = Mathf.Max(6, Mathf.RoundToInt(size * S));
            int key = px * 1000 + (int)a * 10 + (italic ? 1 : 0) + (wrap ? 2 : 0) * 100000000;
            GUIStyle st;
            if (!styles.TryGetValue(key, out st))
            {
                st = new GUIStyle { font = font, fontSize = px, fontStyle = italic ? FontStyle.BoldAndItalic : FontStyle.Bold, alignment = a, wordWrap = wrap };
                st.normal = new GUIStyleState();
                styles[key] = st;
            }
            return st;
        }
        public static void Text(float x, float y, float w, float h, string s, int size, Color c, TextAnchor a = TextAnchor.MiddleLeft, bool italic = true, bool shadow = true, bool wrap = false)
        {
            var st = St(size, italic, a, wrap);
            if (shadow)
            {
                st.normal.textColor = new Color(0, 0, 0, 0.7f * c.a * Alpha);
                float d = Mathf.Max(1f, 2f * S);
                var r = R(x, y, w, h); r.x += d; r.y += d;
                GUI.Label(r, s, st);
            }
            st.normal.textColor = new Color(c.r, c.g, c.b, c.a * Alpha);
            GUI.Label(R(x, y, w, h), s, st);
        }

        // ------------------------------------------------------------ animasyon
        static readonly Dictionary<string, float> anim = new Dictionary<string, float>();
        public static float Anim(string id, float target, float speed = 12f)
        {
            float v;
            if (!anim.TryGetValue(id, out v)) v = target;
            if (Rep) v = Mathf.MoveTowards(v, target, Time.unscaledDeltaTime * speed);
            anim[id] = v;
            return v;
        }

        // ------------------------------------------------------------ kontroller
        public static System.Action OnHoverSound, OnClickSound;
        static string lastHover = "";
        static void HoverSnd(string id, bool hov)
        {
            if (!Rep) return;
            if (hov && lastHover != id) { lastHover = id; if (OnHoverSound != null) OnHoverSound(); }
            else if (!hov && lastHover == id) lastHover = "";
        }
        static void ClickSnd() { if (OnClickSound != null) OnClickSound(); }

        /// <summary>Hap şeklinde anahtar (animasyonlu topuz).</summary>
        public static bool Toggle(string id, float x, float y, bool on, bool enabled = true)
        {
            const float w = 84, h = 38;
            bool hov = enabled && Hit(x, y, w, h);
            HoverSnd(id, hov);
            float t = Anim(id, on ? 1f : 0f, 8f);
            Color off = new Color(0.25f, 0.28f, 0.31f, 0.95f);
            Color bg = Color.Lerp(off, Teal, t);
            if (!enabled) bg = new Color(0.2f, 0.2f, 0.22f, 0.6f);
            Pill(x, y, w, h, bg);
            if (hov) Pill(x - 2, y - 2, w + 4, h + 4, new Color(1, 1, 1, 0.12f));
            float k = h - 8;
            Icon(DotTex, x + 4 + (w - k - 8) * t, y + 4, k, enabled ? Color.white : new Color(0.6f, 0.6f, 0.6f));
            Text(x + w + 14, y, 90, h, on ? "AÇIK" : "KAPALI", 18, enabled ? (on ? Teal : Grey) : new Color(0.4f, 0.4f, 0.4f));
            if (enabled && Clicked(x, y, w + 100, h)) { ClickSnd(); return !on; }
            return on;
        }

        /// <summary>Segmentli seçim. Devre dışı segmentler için mask (null = hepsi açık).</summary>
        public static int Segmented(string id, float x, float y, float w, string[] opts, int sel, bool[] enabledMask = null)
        {
            const float h = 44;
            int n = opts.Length;
            float sw = w / n;
            Bar(x, y, w, h, new Color(0.08f, 0.1f, 0.12f, 0.9f));
            float pos = Anim(id, sel, 10f);
            Bar(x + pos * sw + 2, y + 2, sw - 4, h - 4, Teal);
            int res = sel;
            for (int i = 0; i < n; i++)
            {
                bool en = enabledMask == null || enabledMask[i];
                float sx = x + i * sw;
                bool hov = en && Hit(sx, y, sw, h);
                HoverSnd(id + i, hov);
                if (hov && i != sel) Bar(sx + 2, y + 2, sw - 4, h - 4, new Color(1, 1, 1, 0.1f));
                Color tc = i == sel ? Dark : en ? Color.white : new Color(0.45f, 0.45f, 0.45f);
                Text(sx, y, sw, h, opts[i].ToUpper(), opts[i].Length > 9 ? 15 : 18, tc, TextAnchor.MiddleCenter, true, i != sel);
                if (en && i != sel && Clicked(sx, y, sw, h)) { ClickSnd(); res = i; }
            }
            return res;
        }

        static string dragId;
        /// <summary>Teal dolgulu kaydırıcı + değer yazısı.</summary>
        public static float Slider(string id, float x, float y, float w, float v, float min, float max, string valueText)
        {
            const float h = 44;
            float tw = w - 110, ty = y + h / 2 - 4;
            float t = Mathf.InverseLerp(min, max, v);
            bool hov = Hit(x - 10, y, tw + 20, h);
            HoverSnd(id, hov);
            if (Rep && Click && hov) { dragId = id; Click = false; ClickSnd(); }
            if (!MouseHeld && dragId == id) dragId = null;
            if (dragId == id && Rep) { t = Mathf.Clamp01((Mx - x) / tw); v = Mathf.Lerp(min, max, t); }
            Pill(x, ty, tw, 8, new Color(0.22f, 0.25f, 0.28f, 0.95f));
            Pill(x, ty, Mathf.Max(8f, tw * t), 8, Teal);
            float k = hov || dragId == id ? 28 : 24;
            Icon(DotTex, x + tw * t - k / 2, y + h / 2 - k / 2, k, Color.white);
            Text(x + tw + 20, y, 90, h, valueText, 22, Teal, TextAnchor.MiddleRight);
            return v;
        }

        public static bool Button(string id, float x, float y, float w, float h, string label, bool enabled = true, Color? accent = null)
        {
            Color ac = accent ?? Teal;
            bool hov = enabled && Hit(x, y, w, h);
            HoverSnd(id, hov);
            float t = Anim(id, hov ? 1f : 0f, 10f);
            Bar(x, y, w, h, enabled ? new Color(ac.r * 0.25f, ac.g * 0.25f, ac.b * 0.25f, 0.85f) : new Color(0.15f, 0.15f, 0.16f, 0.7f));
            if (t > 0f) Bar(x, y, w * t, h, ac);
            Fill(x + 8, y + h - 3, w - 16, 2, new Color(ac.r, ac.g, ac.b, enabled ? 0.8f : 0.2f));
            Text(x, y, w, h, label, 20, !enabled ? new Color(0.45f, 0.45f, 0.45f) : t > 0.5f ? Dark : Color.white, TextAnchor.MiddleCenter, true, t <= 0.5f);
            if (enabled && Clicked(x, y, w, h)) { ClickSnd(); return true; }
            return false;
        }

        /// <summary>Klavye tuşu kapakçığı.</summary>
        public static float KeyCap(float x, float y, string key)
        {
            float w = Mathf.Max(44f, 16f + key.Length * 13f);
            Pill(x, y, w, 36, new Color(0.85f, 0.9f, 0.92f, 0.95f));
            Text(x, y, w, 36, key, 18, Dark, TextAnchor.MiddleCenter, false, false);
            return w;
        }

        // ------------------------------------------------------------ dokular
        static void Init()
        {
            ready = true;
            font = U.BuiltinFont;
            White = Texture2D.whiteTexture;
            PanelTex = MakePanel();
            BarTex = MakeBar();
            PillTex = MakePill();
            DotTex = MakeSdf(48, (x, y) => Disc(x, y, 0, 0, 0.92f));
            VignetteTex = MakeVignette();
            ChevronTex = MakeChevron();
            IcoCoin = MakeSdf(64, (x, y) => Mathf.Max(Ring(x, y, 0.85f, 0.12f), Disc(x, y, 0, 0, 0.5f)));
            IcoFlag = MakeSdf(64, (x, y) =>
            {
                float pole = Box(x, y, -0.78f, 0f, 0.07f, 0.92f);
                bool inFlag = x > -0.7f && x < 0.85f && y > 0.05f && y < 0.85f;
                float checker = inFlag && ((Mathf.FloorToInt((x + 1f) * 4f) + Mathf.FloorToInt((y + 1f) * 4f)) % 2 == 0) ? 1f : inFlag ? 0.35f : 0f;
                return Mathf.Max(pole, checker);
            });
            IcoEscape = MakeSdf(64, (x, y) =>
            {
                // iki ileri ok (kaçış)
                float a = 0f;
                for (int k = 0; k < 2; k++) { float u = x + 0.55f - k * 0.6f - Mathf.Abs(y) * 0.8f; if (u > 0f && u < 0.3f && Mathf.Abs(y) < 0.75f) a = 1f - k * 0.35f; }
                return a;
            });
            IcoCuffs = MakeSdf(64, (x, y) => Mathf.Max(Mathf.Max(Ring(x + 0.42f, y + 0.15f, 0.4f, 0.12f), Ring(x - 0.42f, y + 0.15f, 0.4f, 0.12f)), Box(x, y, 0, 0.45f, 0.35f, 0.07f)));
            IcoRank = MakeSdf(64, (x, y) => (Mathf.Abs(x) + Mathf.Abs(y) < 0.9f && Mathf.Abs(x) + Mathf.Abs(y) > 0.62f) || Mathf.Abs(x) + Mathf.Abs(y) < 0.38f ? 1f : 0f);
            IcoCheck = MakeSdf(64, (x, y) => Mathf.Max(Seg(x, y, -0.65f, 0.05f, -0.2f, -0.45f, 0.14f), Seg(x, y, -0.2f, -0.45f, 0.7f, 0.55f, 0.14f)));
            IcoCross = MakeSdf(64, (x, y) => Mathf.Max(Seg(x, y, -0.55f, -0.55f, 0.55f, 0.55f, 0.13f), Seg(x, y, -0.55f, 0.55f, 0.55f, -0.55f, 0.13f)));
            IcoRing = MakeSdf(64, (x, y) => Ring(x, y, 0.7f, 0.1f));
            panelStyle = new GUIStyle { border = new RectOffset(22, 22, 22, 22) }; panelStyle.normal = new GUIStyleState { background = PanelTex };
            barStyle = new GUIStyle { border = new RectOffset(18, 18, 2, 2) }; barStyle.normal = new GUIStyleState { background = BarTex };
            pillStyle = new GUIStyle { border = new RectOffset(16, 16, 15, 15) }; pillStyle.normal = new GUIStyleState { background = PillTex };
        }

        static float Disc(float x, float y, float cx, float cy, float r) { float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)); return Mathf.Clamp01((r - d) * 24f + 0.5f); }
        static float Ring(float x, float y, float r, float th) { float d = Mathf.Abs(Mathf.Sqrt(x * x + y * y) - r); return Mathf.Clamp01((th - d) * 24f + 0.5f); }
        static float Box(float x, float y, float cx, float cy, float hx, float hy) { return Mathf.Abs(x - cx) < hx && Mathf.Abs(y - cy) < hy ? 1f : 0f; }
        static float Seg(float x, float y, float ax, float ay, float bx, float by, float th)
        {
            float px = x - ax, py = y - ay, dx = bx - ax, dy = by - ay;
            float t = Mathf.Clamp01((px * dx + py * dy) / (dx * dx + dy * dy));
            float ex = px - dx * t, ey = py - dy * t;
            return Mathf.Clamp01((th - Mathf.Sqrt(ex * ex + ey * ey)) * 24f + 0.5f);
        }

        static Texture2D NewTex(int w, int h)
        {
            return new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
        }

        /// <summary>f(x,y) [-1,1] uzayında (y yukarı) alfa döndürür; beyaz doku (GUI.color ile boyanır). 2x2 süper örnekleme.</summary>
        static Texture2D MakeSdf(int n, System.Func<float, float, float> f)
        {
            var t = NewTex(n, n);
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float a = 0f;
                    for (int sy = 0; sy < 2; sy++)
                        for (int sx = 0; sx < 2; sx++)
                            a += f(((x + 0.25f + sx * 0.5f) / n) * 2f - 1f, ((y + 0.25f + sy * 0.5f) / n) * 2f - 1f);
                    px[y * n + x] = new Color(1, 1, 1, Mathf.Clamp01(a * 0.25f));
                }
            t.SetPixels(px); t.Apply();
            return t;
        }

        static Texture2D MakePanel()
        {
            const int N = 64, cut = 18;
            var t = NewTex(N, N);
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    int yy = N - 1 - y;   // üstten
                    float dTL = (x + yy - cut) * 0.7071f, dBR = ((N - 1 - x) + y - cut) * 0.7071f;
                    float edge = Mathf.Min(Mathf.Min(x, N - 1 - x), Mathf.Min(y, N - 1 - y));
                    float diag = Mathf.Min(dTL, dBR);
                    float inside = Mathf.Clamp01(diag + 0.5f);
                    float border = Mathf.Max(Mathf.Clamp01(2f - edge), Mathf.Clamp01(2f - Mathf.Abs(diag - 0.8f)));
                    float grad = 0.82f + 0.1f * (y / (float)N);   // üstte biraz daha koyu/opak
                    Color c = Color.Lerp(new Color(0.02f, 0.045f, 0.06f, grad), new Color(Teal.r, Teal.g, Teal.b, 0.9f), border);
                    c.a *= inside;
                    px[y * N + x] = c;
                }
            t.SetPixels(px); t.Apply();
            return t;
        }

        /// <summary>Paralelkenar (sol/sağ kenar eğik) beyaz şerit.</summary>
        static Texture2D MakeBar()
        {
            const int W = 40, H = 32, slant = 12;
            var t = NewTex(W, H);
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float off = slant * (y / (float)(H - 1));            // alt solda, üst sağa eğik
                    float l = x + 0.5f - off, r = (W - slant) + off - (x + 0.5f);
                    float a = Mathf.Clamp01(Mathf.Min(l, r) + 0.5f);
                    px[y * W + x] = new Color(1, 1, 1, a);
                }
            t.SetPixels(px); t.Apply();
            return t;
        }

        static Texture2D MakePill()
        {
            const int W = 34, H = 32; float rr = 15.5f;
            var t = NewTex(W, H);
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    float cx = Mathf.Clamp(x + 0.5f, rr + 0.5f, W - rr - 0.5f), cy = H / 2f;
                    float d = Mathf.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy));
                    px[y * W + x] = new Color(1, 1, 1, Mathf.Clamp01(rr - d + 0.5f));
                }
            t.SetPixels(px); t.Apply();
            return t;
        }

        static Texture2D MakeVignette()
        {
            const int N = 128;
            var t = NewTex(N, N);
            var px = new Color[N * N];
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = (x + 0.5f) / N * 2f - 1f, v = (y + 0.5f) / N * 2f - 1f;
                    float d = Mathf.Sqrt(u * u * 0.8f + v * v);
                    float a = Mathf.Clamp01((d - 0.45f) / 0.9f);
                    px[y * N + x] = new Color(0, 0, 0, a * a * 0.95f);
                }
            t.SetPixels(px); t.Apply();
            return t;
        }

        /// <summary>Yeşil üçlü chevron logo (garaj ile aynı stil).</summary>
        static Texture2D MakeChevron()
        {
            const int W = 96, H = 48;
            var t = NewTex(W, H);
            var px = new Color[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    Color c = new Color(0, 0, 0, 0);
                    for (int k = 0; k < 3; k++)
                    {
                        float x0 = 8 + k * 28, d = Mathf.Abs(y + 0.5f - H / 2f) * 0.8f, u = x + 0.5f - x0 - d;
                        float a = Mathf.Clamp01(Mathf.Min(u, 12f - u) + 0.5f) * (1f - k * 0.25f);
                        if (a > c.a) c = new Color(Green.r, Green.g, Green.b, a);
                    }
                    px[y * W + x] = c;
                }
            t.SetPixels(px); t.Apply();
            return t;
        }
    }
}
