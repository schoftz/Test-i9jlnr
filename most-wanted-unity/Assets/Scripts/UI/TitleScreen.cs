using UnityEngine;

namespace MostWanted
{
    /// <summary>
    /// Açılış başlık ekranı (yalnızca oyun ilk açıldığında, Game.InitCo(true) sonunda): oyuncu aracının etrafında
    /// garajda yavaş sinematik yörünge kamerası, sinema bantları, "MOST WANTED" logosu, yanıp sönen
    /// "Başlamak için bir tuşa bas" ve ardından ana menü (OYUNA BAŞLA / GARAJ / AYARLAR / EMEĞİ GEÇENLER / ÇIKIŞ).
    /// Başlık açıkken Game.menu == Menu.Title: zaman durur, oyun girişleri ve HUD kapalı, Esc hiçbir şey yapmaz.
    /// Sahne kökünde yaşar: harita değişimi (Rebuild) olursa kendiliğinden yok olur.
    /// </summary>
    public class TitleScreen : MonoBehaviour
    {
        static TitleScreen inst;

        enum State { Intro, Menu, Sub, Out, In }
        State state = State.Intro;
        float stateT, blackA = 1f, bars;
        int cursor;
        int after;              // Out bitince: 0 oyun, 1 garaj
        float orbit;
        float savedFov = 60f;
        Game g;

        static readonly string[] Items = { "OYUNA BAŞLA", "GARAJ", "AYARLAR", "EMEĞİ GEÇENLER", "ÇIKIŞ" };
        const float MX = 140, MY = 560, MW = 520, MH = 60, MG = 12;
        const float OutDur = 0.55f, InDur = 0.7f;

        /// <summary>Başlık ekranı oyunu tutuyor mu (HUD/girişler kapalı olmalı).</summary>
        public static bool Active { get { return inst != null && inst.state != State.In; } }

        public static void Show(Game game)
        {
            if (inst != null || game == null) return;
            var go = new GameObject("BaslikEkrani");
            inst = go.AddComponent<TitleScreen>();
            inst.g = game;
            inst.Begin();
        }

        void Begin()
        {
            PauseMenu.Get();   // UIKit hover/tık seslerini bağlar
            g.OpenMenu(Game.Menu.Title);
            if (g.rig != null) g.rig.enabled = false;
            if (g.cam != null) savedFov = g.cam.fieldOfView;
            if (g.player != null) orbit = g.player.transform.eulerAngles.y * Mathf.Deg2Rad + 2.2f;
            state = State.Intro; stateT = 0f; blackA = 1f; bars = 0f;
            UpdateCamera(0f);
        }

        void OnDestroy()
        {
            if (inst == this) inst = null;
            if (state != State.In) RestoreGameplay(false);
        }

        void Update()
        {
            if (g == null) { Destroy(gameObject); return; }
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            stateT += dt;
            bars = Mathf.MoveTowards(bars, state == State.In ? 0f : 1f, dt * (state == State.In ? 1.6f : 0.8f));

            switch (state)
            {
                case State.Intro:
                    blackA = Mathf.MoveTowards(blackA, 0f, dt / 1.2f);
                    if (stateT > 0.6f && AnyKey()) { Go(State.Menu); Snd(true); }
                    break;
                case State.Menu:
                    blackA = Mathf.MoveTowards(blackA, 0f, dt / 0.6f);
                    if (g.menu != Game.Menu.Title) { g.menu = Game.Menu.Title; g.ApplyTimeScale(); }
                    UIKit.Click |= Input.GetMouseButtonDown(0);
                    UIKit.MouseHeld = Input.GetMouseButton(0);
                    int dir = 0;
                    if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) dir = -1;
                    if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) dir = 1;
                    if (dir != 0) { cursor = (cursor + dir + Items.Length) % Items.Length; Snd(false); }
                    if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space)) Activate(cursor);
                    break;
                case State.Sub:
                    // Ayarlar / Emeği Geçenler (PauseMenu) kapanınca başlığa dön
                    if (g.menu == Game.Menu.None) { g.menu = Game.Menu.Title; g.ApplyTimeScale(); Go(State.Menu); }
                    break;
                case State.Out:
                    blackA = Mathf.Clamp01(stateT / OutDur);
                    if (stateT >= OutDur)
                    {
                        RestoreGameplay(true);
                        Go(State.In);
                    }
                    break;
                case State.In:
                    blackA = 1f - Mathf.Clamp01(stateT / InDur);
                    if (stateT >= InDur) { Destroy(gameObject); return; }
                    break;
            }
        }

        void LateUpdate()
        {
            if (state != State.In) UpdateCamera(Mathf.Min(Time.unscaledDeltaTime, 0.1f));
        }

        static readonly KeyCode[] AnyKeys = { KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Space, KeyCode.W, KeyCode.A, KeyCode.S, KeyCode.D,
            KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow, KeyCode.E, KeyCode.Q, KeyCode.LeftShift, KeyCode.RightShift };
        static bool AnyKey()
        {
            if (Input.GetKeyDown(KeyCode.Escape)) return false;   // Esc başlıkta hiçbir şey yapmaz
            if (Input.GetMouseButtonDown(0)) return true;
            foreach (var k in AnyKeys) if (Input.GetKeyDown(k)) return true;
            return false;
        }

        void Go(State s) { state = s; stateT = 0f; }

        void Snd(bool high)
        {
            if (high) { if (UIKit.OnClickSound != null) UIKit.OnClickSound(); }
            else if (UIKit.OnHoverSound != null) UIKit.OnHoverSound();
        }

        void Activate(int i)
        {
            cursor = i;
            Snd(true);
            switch (i)
            {
                case 0: after = 0; Go(State.Out); break;
                case 1: after = 1; Go(State.Out); break;
                case 2: Go(State.Sub); PauseMenu.Get().OpenAt(g, 3, true); break;   // Ayarlar sayfası
                case 3: Go(State.Sub); PauseMenu.Get().OpenAt(g, 4, true); break;   // Emeği Geçenler
                case 4:
                    SaveSystem.Save();
                    Application.Quit();
#if UNITY_EDITOR
                    UnityEditor.EditorApplication.isPlaying = false;
#endif
                    break;
            }
        }

        /// <summary>Takip kamerası, HUD ve oyun zamanı geri gelir.</summary>
        void RestoreGameplay(bool openAfter)
        {
            if (g == null) return;
            if (g.cam != null) g.cam.fieldOfView = savedFov;
            if (g.rig != null) { g.rig.enabled = true; g.rig.Snap(); }
            if (g.menu == Game.Menu.Title) g.CloseMenu();
            if (!openAfter) return;
            g.WelcomeToast();
            if (after == 1 && g.menu == Game.Menu.None) g.OpenMenu(Game.Menu.Garage);
        }

        // ------------------------------------------------------------ sinematik yörünge kamerası
        void UpdateCamera(float dt)
        {
            if (g == null || g.cam == null || g.player == null) return;
            if (state == State.Sub) return;   // ayarlar açıkken (bulanık arka plan) kamera sabit
            orbit += dt * 0.12f;
            float tt = Time.unscaledTime;
            Vector3 c = g.player.transform.position + Vector3.up * 0.7f;
            float r = 7.2f + Mathf.Sin(tt * 0.21f) * 1.3f;
            float h = 1.3f + Mathf.Sin(tt * 0.17f + 1f) * 0.6f;
            Vector3 pos = c + new Vector3(Mathf.Sin(orbit) * r, h, Mathf.Cos(orbit) * r);
            // duvara gömülmesin
            RaycastHit hit;
            Vector3 d = pos - c;
            int mask = ~((1 << OptimizationManager.TrafficLayer) | (1 << U.IconLayer));
            if (Physics.Raycast(c, d.normalized, out hit, d.magnitude + 0.3f, mask, QueryTriggerInteraction.Ignore) && hit.rigidbody != g.player.rb)
                pos = c + d.normalized * Mathf.Max(2.5f, hit.distance - 0.4f);
            var t = g.cam.transform;
            t.position = pos;
            t.rotation = Quaternion.LookRotation((c + Vector3.up * -0.15f) - pos);
            g.cam.fieldOfView = Mathf.Lerp(g.cam.fieldOfView, 38f, 1f - Mathf.Exp(-Mathf.Max(dt, 0.0001f) * 3f));
        }

        // ------------------------------------------------------------ çizim
        void OnGUI()
        {
            if (g == null) return;
            GUI.depth = -50;
            var oldM = GUI.matrix;
            float oldA = UIKit.Alpha;
            UIKit.Begin();
            UIKit.Alpha = 1f;
            float sw = Screen.width, sh = Screen.height;

            // sinema bantları
            float bh = state == State.Sub ? 0f : sh * 0.11f * Ease(bars);   // ayarlar açıkken bantlar menüyü örtmesin
            UIKit.Tex(new Rect(0, 0, sw, bh), UIKit.White, Color.black);
            UIKit.Tex(new Rect(0, sh - bh, sw, bh), UIKit.White, Color.black);

            if (state != State.Sub && state != State.In)
            {
                float vis = state == State.Out ? 1f - Mathf.Clamp01(stateT / (OutDur * 0.6f)) : 1f;
                UIKit.Alpha = vis;
                UIKit.Tex(UIKit.Full(), UIKit.VignetteTex, new Color(1, 1, 1, 0.8f));
                DrawLogo();
                if (state == State.Intro) DrawPress();
                else DrawMenu();
            }

            if (blackA > 0f) { UIKit.Alpha = 1f; UIKit.Tex(UIKit.Full(), UIKit.White, new Color(0, 0, 0, blackA)); }
            UIKit.End();
            UIKit.Alpha = oldA;
            GUI.matrix = oldM;
        }

        static float Ease(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x); }

        void DrawLogo()
        {
            float t = Time.unscaledTime;
            bool menuMode = state != State.Intro;
            float x = 140, y = menuMode ? 190 : 330;
            // yeşil chevronlar (sırayla parlar)
            for (int k = 0; k < 3; k++)
            {
                float a = 0.45f + 0.55f * Mathf.Clamp01(Mathf.Sin(t * 2.2f - k * 0.8f) * 0.5f + 0.5f);
                UIKit.Tex(UIKit.R(x + k * 56, y + 32, 96, 48), UIKit.ChevronTex, new Color(1, 1, 1, a));
            }
            UIKit.Text(x + 220, y, 1400, 120, "MOST WANTED", 104, Color.white);
            UIKit.Fill(x + 226, y + 122, 640, 4, UIKit.Green);
            UIKit.Text(x + 226, y + 130, 900, 50, "SOKAK YARIŞI", 34, UIKit.Green);
        }

        void DrawPress()
        {
            float t = Time.unscaledTime;
            float a = 0.35f + 0.65f * (0.5f + 0.5f * Mathf.Sin(t * 4f));
            UIKit.Text(0, 820, UIKit.DW, 60, "Başlamak için bir tuşa bas", 32, new Color(1, 1, 1, a), TextAnchor.MiddleCenter);
        }

        void DrawMenu()
        {
            float appear = Ease(state == State.Menu ? stateT / 0.35f : 1f);
            for (int i = 0; i < Items.Length; i++)
            {
                float y = MY + i * (MH + MG);
                float x = MX - (1f - appear) * 60f * (1 + i * 0.3f);
                if (state == State.Menu && UIKit.Hit(x, y, MW, MH) && cursor != i) { cursor = i; Snd(false); }
                bool sel = cursor == i;
                float s = UIKit.Anim("title_" + i, sel ? 1f : 0f, 12f);
                UIKit.Bar(x, y, MW, MH, new Color(0.02f, 0.05f, 0.07f, 0.7f));
                if (s > 0.01f) UIKit.Bar(x, y, MW * s, MH, new Color(UIKit.Teal.r, UIKit.Teal.g, UIKit.Teal.b, 0.9f));
                if (sel) UIKit.Tex(UIKit.R(x - 70, y + 14, 64, 32), UIKit.ChevronTex, Color.white);
                Color tc = i == 4 ? (s > 0.5f ? UIKit.Dark : UIKit.Danger) : (s > 0.5f ? UIKit.Dark : Color.white);
                UIKit.Text(x + 28 + s * 10, y, MW - 40, MH, Items[i], 28, tc, TextAnchor.MiddleLeft, true, s <= 0.5f);
                if (state == State.Menu && UIKit.Clicked(x, y, MW, MH)) Activate(i);
            }
            // alt ipuçları
            float hx = 140, hy = 960;
            hx += UIKit.KeyCap(hx, hy, "↑↓") + 8;
            UIKit.Text(hx, hy, 160, 36, "Seç", 18, UIKit.Grey, TextAnchor.MiddleLeft, false, false);
            hx += 70; hx += UIKit.KeyCap(hx, hy, "ENTER") + 8;
            UIKit.Text(hx, hy, 160, 36, "Onayla", 18, UIKit.Grey, TextAnchor.MiddleLeft, false, false);
        }
    }
}
