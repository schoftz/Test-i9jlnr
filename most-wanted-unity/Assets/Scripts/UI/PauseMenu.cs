using System.Collections.Generic;
using UnityEngine;

namespace MostWanted
{
    /// <summary>
    /// Carbon tarzı duraklatma menüsü: bulanık oyun arka planı, sol dikey menü, üst istatistik çipleri,
    /// sağda sayfa paneli (Harita / Kariyer / Ayarlar sekmeleri / Emeği Geçenler), onay pencereleri.
    /// HUD.OnGUI → PauseMenu.Get().Draw(g). Esc → Game.HandleKeys → PauseMenu.HandleEscape(g).
    /// </summary>
    public class PauseMenu : MonoBehaviour
    {
        static PauseMenu inst;
        public static PauseMenu Get()
        {
            if (inst == null)
            {
                var go = new GameObject("PauseMenu");
                DontDestroyOnLoad(go);
                inst = go.AddComponent<PauseMenu>();
            }
            return inst;
        }

        enum Page { Overview = 0, Map = 1, Career = 2, Settings = 3, Credits = 4, Save = 5, Quit = 6 }
        static readonly string[] Items = { "DEVAM ET", "HARİTA", "KARİYER", "AYARLAR", "EMEĞİ GEÇENLER", "KAYDET", "ÇIKIŞ" };
        static readonly string[] Tabs = { "GRAFİK", "SES", "SÜRÜŞ", "HARİTA", "KONTROLLER" };

        int cursor, page, tab;
        float openT;              // 0..1 aç/kapa animasyonu
        bool closing, wasOpen;
        int confirm;              // 0 yok, 1 kaydı sıfırla, 2 çıkış
        int confirmSel = 1;       // 0 EVET, 1 HAYIR (güvenli varsayılan)
        float creditScroll, creditStart;
        bool mapCamOn;
        AudioSource sfx;

        // bulanık arka plan
        RenderTexture blurA, blurB, blurC;
        bool blurOk, wantCapture;

        const float Dur = 0.15f;
        const float MenuX = 80, MenuY = 230, ItemH = 62, ItemGap = 10, MenuW = 440;
        const float PX = 600, PY = 200, PW = 1240, PH = 770;

        static bool IsOpen(Game g) { return g != null && (g.menu == Game.Menu.Pause || g.menu == Game.Menu.Credits); }

        // ------------------------------------------------------------ yaşam döngüsü
        void Awake()
        {
            sfx = gameObject.AddComponent<AudioSource>();
            sfx.playOnAwake = false; sfx.spatialBlend = 0f; sfx.ignoreListenerPause = true;
            UIKit.OnHoverSound = () => Snd(false, 0.12f);
            UIKit.OnClickSound = () => Snd(true, 0.3f);
        }

        void Snd(bool high, float v)
        {
            try { if (sfx != null) sfx.PlayOneShot(AudioSynth.Beep(high), v * AudioBus.Get(AudioBus.Bus.Efekt)); } catch { }
        }

        void Update()
        {
            var g = Game.I;
            bool open = IsOpen(g);
            if (open && !wasOpen) OnOpen(g);
            if (!open && wasOpen) OnClosed(g);
            wasOpen = open;
            if (!open) return;

            UIKit.Click |= Input.GetMouseButtonDown(0);
            UIKit.MouseHeld = Input.GetMouseButton(0);
            float dt = Time.unscaledDeltaTime;
            openT = Mathf.MoveTowards(openT, closing ? 0f : 1f, dt / Dur);
            if (closing && openT <= 0f) { closing = false; g.CloseMenu(); return; }
            if (closing) return;

            if (Input.mouseScrollDelta.y != 0f) creditScroll -= Input.mouseScrollDelta.y * 40f;

            if (confirm != 0)
            {
                if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) { confirmSel = 0; Snd(false, 0.15f); }
                if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) { confirmSel = 1; Snd(false, 0.15f); }
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) { if (confirmSel == 0) DoConfirm(g); else confirm = 0; Snd(true, 0.3f); }
                return;
            }
            int dir = 0;
            if (Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) dir = -1;
            if (Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) dir = 1;
            if (dir != 0) { cursor = (cursor + dir + Items.Length) % Items.Length; Snd(false, 0.15f); if (cursor <= (int)Page.Credits) SetPage(g, cursor); }
            if (page == (int)Page.Settings)
            {
                if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A)) { tab = (tab + Tabs.Length - 1) % Tabs.Length; Snd(false, 0.15f); }
                if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D)) { tab = (tab + 1) % Tabs.Length; Snd(false, 0.15f); }
            }
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) { Snd(true, 0.3f); Activate(g, cursor); }
        }

        void LateUpdate()
        {
            if (wantCapture) { wantCapture = false; Capture(Game.I); }
        }

        void OnOpen(Game g)
        {
            openT = 0f; closing = false; confirm = 0;
            cursor = g.menu == Game.Menu.Credits ? (int)Page.Credits : 0;
            page = cursor;
            creditScroll = 0f; creditStart = Time.unscaledTime;
            wantCapture = true;
            Snd(true, 0.25f);
        }

        void OnClosed(Game g)
        {
            closing = false; confirm = 0; openT = 0f;
            SetMapCam(g, false);
            ReleaseBlur();
        }

        void OnDestroy() { ReleaseBlur(); }

        /// <summary>Game Esc'e bastığında çağırır. true: menü işledi (Game menüyü kapatmasın).</summary>
        public static bool HandleEscape(Game g)
        {
            if (inst == null || !IsOpen(g)) return false;
            return inst.Escape(g);
        }

        bool Escape(Game g)
        {
            Snd(false, 0.2f);
            if (confirm != 0) { confirm = 0; return true; }
            if (closing) return true;
            if (g.menu == Game.Menu.Credits) g.menu = Game.Menu.Pause;
            if (page != (int)Page.Overview) { SetPage(g, 0); cursor = 0; return true; }
            BeginClose(g);
            return true;
        }

        void BeginClose(Game g)
        {
            SaveSystem.Save();
            SetMapCam(g, false);
            closing = true;
        }

        void SetPage(Game g, int p)
        {
            page = p;
            if (p == (int)Page.Credits) { creditScroll = 0f; creditStart = Time.unscaledTime; }
            SetMapCam(g, p == (int)Page.Map);
        }

        void SetMapCam(Game g, bool on)
        {
            if (g == null || g.bigMapCam == null) return;
            if (on && !mapCamOn) { g.bigMapPan = Vector3.zero; g.bigMapCam.enabled = true; }
            if (!on && mapCamOn && g.menu != Game.Menu.Map) g.bigMapCam.enabled = false;
            mapCamOn = on;
        }

        void Activate(Game g, int i)
        {
            cursor = i;
            switch ((Page)i)
            {
                case Page.Overview: BeginClose(g); break;
                case Page.Save: SaveSystem.Save(); g.Toast("Oyun kaydedildi."); break;
                case Page.Quit: confirm = 2; confirmSel = 1; break;
                default: SetPage(g, i); break;
            }
        }

        void DoConfirm(Game g)
        {
            int c = confirm; confirm = 0;
            if (c == 1)
            {
                var d = SaveSystem.Data;
                int q = d.quality;
                SaveSystem.Reset();
                SaveSystem.Data.quality = q;
                g.race.Abort(); g.police.EndPursuit(false);
                SetMapCam(g, false);
                g.CloseMenu();
                g.SpawnPlayer(g.world.garagePos + Vector3.up * 0.5f, g.world.garageRot);
                g.Toast("Yeni oyun başladı.");
            }
            else if (c == 2)
            {
                SaveSystem.Save();
                Application.Quit();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
            }
        }

        // ------------------------------------------------------------ bulanık arka plan
        void Capture(Game g)
        {
            ReleaseBlur();
            blurOk = false;
            try
            {
                var cam = g != null ? g.cam : null;
                if (cam == null || Screen.width < 16 || Screen.height < 16) return;
                int w = Mathf.Max(8, Screen.width / 4), h = Mathf.Max(8, Screen.height / 4);
                blurA = new RenderTexture(w, h, 24); blurA.filterMode = FilterMode.Bilinear;
                blurB = new RenderTexture(Mathf.Max(4, w / 2), Mathf.Max(4, h / 2), 0); blurB.filterMode = FilterMode.Bilinear;
                blurC = new RenderTexture(Mathf.Max(2, w / 4), Mathf.Max(2, h / 4), 0); blurC.filterMode = FilterMode.Bilinear;
                var old = cam.targetTexture;
                cam.targetTexture = blurA;
                cam.Render();
                cam.targetTexture = old;
                Graphics.Blit(blurA, blurB);     // 1/4 → 1/8
                Graphics.Blit(blurB, blurC);     // 1/8 → 1/16
                blurOk = true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[MW] Duraklatma bulanıklığı alınamadı, koyu arka plan kullanılıyor: " + e.Message);
                ReleaseBlur();
            }
        }

        void ReleaseBlur()
        {
            blurOk = false;
            foreach (var rt in new[] { blurA, blurB, blurC }) if (rt != null) { rt.Release(); Destroy(rt); }
            blurA = blurB = blurC = null;
        }

        void DrawBackground()
        {
            var full = UIKit.Full();
            float a = openT;
            var oc = GUI.color;
            if (blurOk && blurC != null)
            {
                // 1/16 doku + 4 kaydırılmış örnek = ucuz çadır bulanıklığı
                GUI.color = new Color(1, 1, 1, a);
                GUI.DrawTexture(full, blurC);
                float o = Screen.height / 140f;
                GUI.color = new Color(1, 1, 1, 0.25f * a);
                GUI.DrawTexture(new Rect(full.x - o, full.y - o, full.width, full.height), blurC);
                GUI.DrawTexture(new Rect(full.x + o, full.y - o, full.width, full.height), blurC);
                GUI.DrawTexture(new Rect(full.x - o, full.y + o, full.width, full.height), blurC);
                GUI.DrawTexture(new Rect(full.x + o, full.y + o, full.width, full.height), blurC);
                GUI.color = new Color(0.01f, 0.03f, 0.05f, 0.55f * a);
            }
            else GUI.color = new Color(0.01f, 0.025f, 0.04f, 0.82f * a);
            GUI.DrawTexture(full, Texture2D.whiteTexture);
            GUI.color = new Color(1, 1, 1, a);
            if (UIKit.VignetteTex != null) GUI.DrawTexture(full, UIKit.VignetteTex);
            GUI.color = oc;
        }

        // ------------------------------------------------------------ çizim
        public void Draw(Game g)
        {
            if (!IsOpen(g)) return;
            var oldM = GUI.matrix;
            UIKit.Begin();
            if (UIKit.Rep && !wasOpen) { GUI.matrix = oldM; return; }   // ilk kare: Update henüz açmadı
            float e = 1f - (1f - openT) * (1f - openT);   // ease-out
            DrawBackground();
            UIKit.Alpha = e;
            bool modal = confirm != 0 || closing;
            bool click = UIKit.Click;
            if (modal) UIKit.Click = false;   // alt katman tık almasın

            float slide = (1f - e) * 40f;
            DrawTopBar(g, slide);
            DrawMainMenu(g, -slide);
            DrawPageFrame(g, slide);
            DrawHints();

            if (confirm != 0) { UIKit.Click = click; DrawConfirm(g); }
            UIKit.End();
            UIKit.Alpha = 1f;
            GUI.matrix = oldM;
        }

        void DrawTopBar(Game g, float sl)
        {
            var d = SaveSystem.Data;
            UIKit.Tex(UIKit.R(80, 58 - sl, 72, 36), UIKit.ChevronTex, Color.white);
            UIKit.Text(166, 46 - sl, 600, 56, "DURAKLATILDI", 46, Color.white);
            UIKit.Text(168, 98 - sl, 600, 26, (g.world != null ? g.world.title : "") + (string.IsNullOrEmpty(g.district) ? "" : "  •  " + g.district), 18, UIKit.Grey);
            UIKit.Fill(80, 140 - sl, 1760, 2, new Color(UIKit.Teal.r, UIKit.Teal.g, UIKit.Teal.b, 0.35f));

            string[] vals = { U.Money(d.money), d.racesWon.ToString(), d.escapes.ToString(), d.busted.ToString(), "#" + PlayerRank() };
            string[] lbl = { "PARA", "KAZANILAN YARIŞ", "KAÇIŞ", "YAKALANMA", "KARA LİSTE" };
            Texture2D[] ico = { UIKit.IcoCoin, UIKit.IcoFlag, UIKit.IcoEscape, UIKit.IcoCuffs, UIKit.IcoRank };
            float[] ws = { 250, 200, 150, 170, 170 };
            float x = 1840;
            for (int i = ws.Length - 1; i >= 0; i--)
            {
                x -= ws[i];
                float y = 54 - sl;
                UIKit.Panel(x, y, ws[i] - 12, 66, 0.9f);
                UIKit.Icon(ico[i], x + 14, y + 17, 32, i == 4 ? UIKit.Green : UIKit.Teal);
                UIKit.Text(x + 54, y + 6, ws[i] - 70, 22, lbl[i], 13, UIKit.Grey, TextAnchor.MiddleLeft, false, false);
                UIKit.Text(x + 54, y + 26, ws[i] - 70, 34, vals[i], 26, Color.white);
            }
        }

        static int PlayerRank()
        {
            int b = Mathf.Clamp(SaveSystem.Data.rivalsBeaten, 0, Career.Rivals.Length);
            return b > 0 ? Career.Rivals[b - 1].rank : Career.Rivals[0].rank + 1;
        }

        void DrawMainMenu(Game g, float sl)
        {
            for (int i = 0; i < Items.Length; i++)
            {
                float x = MenuX + sl, y = MenuY + i * (ItemH + ItemGap);
                if (i == 5) y += 30;  // KAYDET / ÇIKIŞ ayrı grup
                if (i == 5) UIKit.Fill(x, y - 22, MenuW - 60, 1, new Color(1, 1, 1, 0.15f));
                bool hov = UIKit.Hit(x, y, MenuW, ItemH);
                bool sel = i == cursor;
                bool act = i == page && i <= (int)Page.Credits;
                float t = UIKit.Anim("mi" + i, sel ? 1f : 0f, 9f);
                float h = UIKit.Anim("mh" + i, hov ? 1f : 0f, 12f);
                if (h > 0f) UIKit.Bar(x, y, MenuW, ItemH, new Color(UIKit.Teal.r, UIKit.Teal.g, UIKit.Teal.b, 0.16f * h));
                if (t > 0f) UIKit.Bar(x, y, MenuW * (0.35f + 0.65f * t), ItemH, new Color(UIKit.Teal.r, UIKit.Teal.g, UIKit.Teal.b, t));
                if (act && !sel) UIKit.Fill(x - 14, y + 8, 5, ItemH - 16, UIKit.Teal);
                Color tc = t > 0.5f ? UIKit.Dark : i == 6 ? new Color(1f, 0.75f, 0.72f) : Color.white;
                UIKit.Text(x + 30 + 10 * t, y, MenuW - 60, ItemH, Items[i], 32, tc, TextAnchor.MiddleLeft, true, t <= 0.5f);
                if (sel) UIKit.Tex(UIKit.R(x + MenuW - 76, y + ItemH / 2 - 12, 48, 24), UIKit.ChevronTex, new Color(0.02f, 0.05f, 0.07f, 0.8f));
                if (hov && UIKit.Rep && hoverIdx != i) { hoverIdx = i; Snd(false, 0.1f); }
                if (!hov && hoverIdx == i && UIKit.Rep) hoverIdx = -1;
                if (UIKit.Clicked(x, y, MenuW, ItemH)) { Snd(true, 0.3f); Activate(g, i); }
            }
        }
        int hoverIdx = -1;

        void DrawHints()
        {
            float x = 80, y = 1012;
            x += UIKit.KeyCap(x, y, "↑↓") + 10; UIKit.Text(x, y, 90, 36, "SEÇ", 18, UIKit.Grey); x += 90;
            x += UIKit.KeyCap(x, y, "ENTER") + 10; UIKit.Text(x, y, 120, 36, "ONAYLA", 18, UIKit.Grey); x += 120;
            x += UIKit.KeyCap(x, y, "ESC") + 10; UIKit.Text(x, y, 160, 36, "GERİ / DEVAM", 18, UIKit.Grey); x += 170;
            if (page == (int)Page.Settings) { x += UIKit.KeyCap(x, y, "←→") + 10; UIKit.Text(x, y, 160, 36, "SEKME", 18, UIKit.Grey); }
            UIKit.Text(1240, y, 600, 36, "Oyun duraklatıldı — zaman durdu", 16, new Color(1, 1, 1, 0.35f), TextAnchor.MiddleRight, true, false);
        }

        void DrawPageFrame(Game g, float sl)
        {
            float x = PX + sl;
            UIKit.Panel(x, PY, PW, PH, 0.92f);
            string head = page == 0 ? "GENEL BAKIŞ" : Items[page];
            UIKit.Fill(x + 40, PY + 34, 6, 40, UIKit.Teal);
            UIKit.Text(x + 60, PY + 26, 800, 56, head, 38, Color.white);
            switch ((Page)page)
            {
                case Page.Map: DrawMapPage(g, x); break;
                case Page.Career: DrawCareer(g, x); break;
                case Page.Settings: DrawSettings(g, x); break;
                case Page.Credits: DrawCredits(g, x); break;
                default: DrawOverview(g, x); break;
            }
        }

        // ------------------------------------------------------------ sayfalar
        void DrawOverview(Game g, float x)
        {
            var d = SaveSystem.Data;
            var car = Catalog.Get(d.selected);
            float y = PY + 110;
            Row2(x, ref y, "ARAÇ", car != null ? car.displayName : "-");
            Row2(x, ref y, "HARİTA", Game.MapNames[Mathf.Clamp(g.mapMode, 0, 2)]);
            Row2(x, ref y, "BÖLGE", string.IsNullOrEmpty(g.district) ? "-" : g.district);
            Row2(x, ref y, "KARA LİSTE SIRASI", "#" + PlayerRank() + (d.rivalsBeaten < Career.Rivals.Length ? "   (sıradaki: " + Career.Rivals[Mathf.Clamp(d.rivalsBeaten, 0, Career.Rivals.Length - 1)].name + ")" : "   (ZİRVEDESİN)"));
            Row2(x, ref y, "KARİYER ÖDÜLÜ", U.Money(d.careerBounty));
            Row2(x, ref y, "KİLOMETRE TAŞI", g.career.MilestoneCount + " / " + Career.Milestones.Length);
            if (g.race.Active) Row2(x, ref y, "DURUM", "Yarış sürüyor");
            else if (g.police.pursuit) Row2(x, ref y, "DURUM", "Polis takibi sürüyor!");
            if (UIKit.Button("ov_go", x + 60, PY + PH - 110, 320, 56, "OYUNA DÖN")) BeginClose(g);
            if (UIKit.Button("ov_save", x + 400, PY + PH - 110, 240, 56, "KAYDET")) { SaveSystem.Save(); g.Toast("Oyun kaydedildi."); }
        }

        void Row2(float x, ref float y, string k, string v)
        {
            UIKit.Text(x + 60, y, 360, 52, k, 20, UIKit.Grey, TextAnchor.MiddleLeft, true, false);
            UIKit.Text(x + 420, y, PW - 480, 52, v, 26, Color.white);
            UIKit.Fill(x + 60, y + 54, PW - 120, 1, new Color(1, 1, 1, 0.08f));
            y += 62;
        }

        void DrawMapPage(Game g, float x)
        {
            float s = 600, mx = x + 40, my = PY + 100;
            UIKit.Fill(mx - 3, my - 3, s + 6, s + 6, new Color(UIKit.Teal.r, UIKit.Teal.g, UIKit.Teal.b, 0.6f));
            UIKit.Fill(mx, my, s, s, new Color(0, 0, 0, 0.9f));
            if (g.bigMapRT != null && g.bigMapCam != null && g.bigMapCam.enabled)
            {
                UIKit.Tex(UIKit.R(mx, my, s, s), g.bigMapRT, Color.white);
                Marker(g, mx, my, s, g.player.transform.position, "SEN", new Color(1f, 0.85f, 0f));
                Marker(g, mx, my, s, g.world.garagePos, "GARAJ", UIKit.Green);
                if (g.race.Active) Marker(g, mx, my, s, g.race.NextCheckpoint, "HEDEF", new Color(1f, 0.55f, 0.05f));
                if (g.delivery.Active) Marker(g, mx, my, s, g.delivery.target, "TESLİMAT", new Color(0.25f, 0.65f, 1f));
            }
            else UIKit.Text(mx, my, s, s, "Harita önizlemesi yok", 22, UIKit.Grey, TextAnchor.MiddleCenter);

            float rx = mx + s + 40, rw = PW - s - 120, y = my;
            UIKit.Text(rx, y, rw, 30, "ŞEHİR", 20, UIKit.Grey, TextAnchor.MiddleLeft, true, false); y += 36;
            var mask = new[] { true, g.HasImportedMap, true };
            int nm = UIKit.Segmented("map_sel", rx, y, rw, Game.MapNames, Mathf.Clamp(g.mapMode, 0, 2), mask);
            if (nm != g.mapMode) { SetMapCam(g, false); g.SetMap(nm); SetMapCam(g, true); }
            y += 60;
            UIKit.Text(rx, y, rw, 50, "Harita değişince şehir yeniden yüklenir.", 16, UIKit.Grey, TextAnchor.UpperLeft, false, false, true); y += 70;
            if (UIKit.Button("map_full", rx, y, rw, 56, "TAM EKRAN HARİTA  (M)")) { SetMapCam(g, false); g.CloseMenu(); g.OpenMenu(Game.Menu.Map); return; }
            y += 72;
            if (UIKit.Button("map_tp", rx, y, rw, 56, "GARAJA IŞINLAN"))
            {
                g.police.EndPursuit(false); g.race.Abort(); g.delivery.Cancel();
                SetMapCam(g, false);
                g.CloseMenu();
                g.player.Teleport(g.world.garagePos + Vector3.up * 0.5f, g.world.garageRot);
                g.rig.Snap();
                return;
            }
            y += 80;
            UIKit.Text(rx, y, rw, 90, "Işınlanma; takibi, yarışı ve teslimatı iptal eder.", 16, UIKit.Grey, TextAnchor.UpperLeft, false, false, true);
        }

        void Marker(Game g, float mx, float my, float s, Vector3 world, string text, Color c)
        {
            Vector3 v = g.bigMapCam.WorldToViewportPoint(world);
            if (v.x < 0 || v.x > 1 || v.y < 0 || v.y > 1) return;
            float x = mx + v.x * s, y = my + (1f - v.y) * s;
            UIKit.Icon(UIKit.DotTex, x - 7, y - 7, 14, c);
            UIKit.Text(x - 100, y + 6, 200, 24, text, 16, c, TextAnchor.MiddleCenter);
        }

        void DrawCareer(Game g, float x)
        {
            var d = SaveSystem.Data;
            float y = PY + 100, cw = 224, gap = 8;
            for (int i = 0; i < Career.Rivals.Length; i++)
            {
                var rv = Career.Rivals[i];
                float cx = x + 40 + i * (cw + gap);
                bool beaten = i < d.rivalsBeaten, next = i == d.rivalsBeaten;
                string why; bool can = g.career.CanChallenge(i, out why);
                UIKit.Panel(cx, y, cw, 300, beaten ? 0.6f : 0.95f);
                if (next) UIKit.Fill(cx + 10, y + 6, cw - 20, 3, UIKit.Teal);
                UIKit.Text(cx + 16, y + 10, 100, 56, "#" + rv.rank, 44, beaten ? UIKit.Grey : next ? UIKit.Teal : Color.white);
                if (beaten) UIKit.Icon(UIKit.IcoCheck, cx + cw - 56, y + 20, 36, UIKit.Green);
                UIKit.Text(cx + 16, y + 66, cw - 32, 34, rv.name.ToUpper(), 26, Color.white);
                UIKit.Text(cx + 16, y + 98, cw - 32, 24, g.career.RivalCar(i).displayName, 15, UIKit.Grey, TextAnchor.MiddleLeft, false, false);
                float ry = y + 130;
                Req(cx, ref ry, "Yarış", d.racesWon + "/" + rv.races, beaten || d.racesWon >= rv.races);
                Req(cx, ref ry, "Ödül", U.Money(rv.bounty), beaten || d.careerBounty >= rv.bounty);
                Req(cx, ref ry, "Taş", g.career.MilestoneCount + "/" + rv.milestones, beaten || g.career.MilestoneCount >= rv.milestones);
                if (beaten) UIKit.Text(cx, y + 246, cw, 40, "YENİLDİ", 22, UIKit.Green, TextAnchor.MiddleCenter);
                else if (next)
                {
                    if (UIKit.Button("ch" + i, cx + 14, y + 240, cw - 28, 46, can ? "MEYDAN OKU" : "KİLİTLİ", can && !g.race.Active))
                    { SetMapCam(g, false); g.CloseMenu(); g.career.Challenge(i); return; }
                }
                else UIKit.Text(cx, y + 246, cw, 40, "SIRADA DEĞİL", 16, UIKit.Grey, TextAnchor.MiddleCenter, false, false);
            }

            y += 330;
            UIKit.Text(x + 40, y, 500, 36, "KİLOMETRE TAŞLARI  " + g.career.MilestoneCount + "/" + Career.Milestones.Length, 22, UIKit.Teal);
            y += 42;
            for (int i = 0; i < Career.Milestones.Length; i++)
            {
                var m = Career.Milestones[i];
                bool ok = SaveSystem.HasMilestone(m.id);
                float mx = x + 40 + (i % 2) * 560, my = y + (i / 2) * 36;
                UIKit.Icon(ok ? UIKit.IcoCheck : UIKit.IcoRing, mx, my + 6, 24, ok ? UIKit.Green : UIKit.Grey);
                UIKit.Text(mx + 34, my, 520, 36, m.text, 17, ok ? Color.white : UIKit.Grey, TextAnchor.MiddleLeft, false, false);
            }
            float by = PY + PH - 84;
            UIKit.Text(x + 40, by, 820, 56, "Ödül " + U.Money(d.careerBounty) + "   •   Yarış " + d.racesWon + "   •   Kaçış " + d.escapes + "   •   Yakalanma " + d.busted, 20, Color.white);
            if (UIKit.Button("reset", x + PW - 330, by, 290, 52, "KAYDI SIFIRLA", true, UIKit.Danger)) { confirm = 1; confirmSel = 1; }
        }

        void Req(float cx, ref float y, string k, string v, bool ok)
        {
            UIKit.Icon(ok ? UIKit.IcoCheck : UIKit.IcoCross, cx + 16, y + 6, 18, ok ? UIKit.Green : UIKit.Danger);
            UIKit.Text(cx + 40, y, 70, 30, k, 15, UIKit.Grey, TextAnchor.MiddleLeft, false, false);
            UIKit.Text(cx + 100, y, 112, 30, v, 15, Color.white, TextAnchor.MiddleRight, false, false);
            y += 34;
        }

        // ---- AYARLAR
        float rowY;
        void DrawSettings(Game g, float x)
        {
            float tx = x + 40, ty = PY + 96, tw = (PW - 80) / Tabs.Length;
            for (int i = 0; i < Tabs.Length; i++)
            {
                bool sel = i == tab, hov = UIKit.Hit(tx + i * tw, ty, tw, 50);
                if (hov && !sel) UIKit.Fill(tx + i * tw, ty, tw - 6, 50, new Color(1, 1, 1, 0.06f));
                UIKit.Text(tx + i * tw, ty, tw - 6, 50, Tabs[i], 22, sel ? UIKit.Teal : hov ? Color.white : UIKit.Grey, TextAnchor.MiddleCenter);
                if (UIKit.Clicked(tx + i * tw, ty, tw, 50)) { tab = i; Snd(true, 0.3f); }
            }
            UIKit.Fill(tx, ty + 50, PW - 80, 2, new Color(1, 1, 1, 0.12f));
            float ul = UIKit.Anim("tabul", tab, 10f);
            UIKit.Fill(tx + ul * tw, ty + 48, tw - 6, 4, UIKit.Teal);

            var d = SaveSystem.Data;
            rowY = ty + 72;
            float cx = x + PW - 560, cw = 500;   // kontrol sütunu
            switch (tab)
            {
                case 0:
                    {
                        RowLabel(x, "Grafik kalitesi", "Otomatik: FPS'e göre ayarlanır.");
                        int q = UIKit.Segmented("q", cx, rowY + 13, cw, OptimizationManager.PresetNames, d.quality);
                        if (q != d.quality) g.ApplyQuality(q);
                        NextRow();
                        RowLabel(x, "FPS hedefi", "Kare hızı sınırı.");
                        int f = UIKit.Segmented("fps", cx, rowY + 13, cw, OptimizationManager.FpsNames, d.fpsTarget);
                        if (f != d.fpsTarget) g.SetFpsTarget(f);
                        NextRow();
                        RowLabel(x, "Atmosfer", "Renk tonu ve ışık havası.");
                        int a = UIKit.Segmented("atm", cx, rowY + 13, cw, Game.AtmosphereNames, d.atmosphere);
                        if (a != d.atmosphere) g.ApplyAtmosphere(a);
                        NextRow();
                        RowLabel(x, "Hep gündüz", "Kapalı: kısa geceler olur.");
                        bool ad = UIKit.Toggle("day", cx, rowY + 16, d.alwaysDay);
                        if (ad != d.alwaysDay) { d.alwaysDay = ad; SaveSystem.Save(); }
                        NextRow();
                        RowLabel(x, "Islak zemin", g.dressing != null ? "Yağmur sonrası parlak asfalt." : "Bu haritada kullanılamaz.");
                        bool wt = UIKit.Toggle("wet", cx, rowY + 16, d.wet, g.dressing != null);
                        if (wt != d.wet) g.SetWet(wt);
                        NextRow();
                        RowLabel(x, "GPU Resident Drawer", "Deneysel. Kaliteyi yeniden uygular.");
                        bool gr = UIKit.Toggle("grd", cx, rowY + 16, d.gpuResidentDrawer);
                        if (gr != d.gpuResidentDrawer) { d.gpuResidentDrawer = gr; SaveSystem.Save(); g.ApplyQuality(d.quality); }
                        NextRow();
                        RowLabel(x, "FPS göstergesi", "Kare süresi, CPU/GPU ve çizim istatistikleri (F).");
                        g.showFps = UIKit.Toggle("fpsg", cx, rowY + 16, g.showFps);
                        break;
                    }
                case 1:
                    for (int i = 0; i < 4; i++)
                    {
                        string[] desc = { "Motor, turbo ve egzoz sesi.", "Çarpma, korna, lastik ve arayüz sesleri.", "Radyo / müzik seviyesi.", "Polis sireni seviyesi." };
                        RowLabel(x, AudioBus.Names[i], desc[i]);
                        float v = UIKit.Slider("vol" + i, cx, rowY + 13, cw, AudioBus.Volumes[i], 0f, 1f, "%" + Mathf.RoundToInt(AudioBus.Volumes[i] * 100f));
                        if (Mathf.Abs(v - AudioBus.Volumes[i]) > 0.001f) { AudioBus.Volumes[i] = v; AudioBus.Store(d); }
                        NextRow();
                    }
                    if (UIKit.Rep && !UIKit.MouseHeld && volDirty) { volDirty = false; SaveSystem.Save(); }
                    if (UIKit.MouseHeld) volDirty = true;
                    break;
                case 2:
                    {
                        RowLabel(x, "Sürüş stili", "MW Sürüş: klasik; Arcade: kolay; Gerçekçi: simülasyon.");
                        int ds = UIKit.Segmented("ds", cx, rowY + 13, cw, new[] { "MW Sürüş", "Arcade", "Gerçekçi" }, Mathf.Clamp(d.driveStyle, 0, 2));
                        if (ds != d.driveStyle) { d.driveStyle = ds; SaveSystem.Save(); }
                        NextRow();
                        RowLabel(x, "Polis zorluğu", "Takip saldırganlığı ve barikat sıklığı.");
                        int pd = UIKit.Segmented("pd", cx, rowY + 13, cw, new[] { "Kolay", "Normal", "Zor" }, Mathf.Clamp(d.policeDiff, 0, 2));
                        if (pd != d.policeDiff) { d.policeDiff = pd; SaveSystem.Save(); }
                        NextRow();
                        RowLabel(x, "Hız çizgileri", "Yüksek hızda ekran kenarı efektleri.");
                        bool sl = UIKit.Toggle("sl", cx, rowY + 16, d.speedLines);
                        if (sl != d.speedLines) { d.speedLines = sl; SaveSystem.Save(); }
                        NextRow();
                        RowLabel(x, "Direksiyon hassasiyeti", "0.60 – 2.00 arası.");
                        float ns = UIKit.Slider("steer", cx, rowY + 13, cw, d.steerSens, 0.6f, 2.0f, d.steerSens.ToString("0.00"));
                        if (Mathf.Abs(ns - d.steerSens) > 0.001f) { d.steerSens = Mathf.Round(ns * 20f) / 20f; volDirty = true; }
                        if (UIKit.Rep && !UIKit.MouseHeld && volDirty) { volDirty = false; SaveSystem.Save(); }
                        break;
                    }
                case 3:
                    {
                        RowLabel(x, "Harita", "Kendi Şehrimiz / İthal harita / Test pisti.");
                        var mask = new[] { true, g.HasImportedMap, true };
                        int nm = UIKit.Segmented("set_map", cx, rowY + 13, cw, Game.MapNames, Mathf.Clamp(g.mapMode, 0, 2), mask);
                        if (nm != g.mapMode) g.SetMap(nm);
                        NextRow();
                        RowLabel(x, "Harita süsleme", g.usingImportedMap ? "Ağaç, tabela, ışık süsleri. Haritayı yeniden yükler." : "Yalnızca ithal haritada.");
                        bool dr = UIKit.Toggle("dress", cx, rowY + 16, d.dressing, g.usingImportedMap);
                        if (dr != d.dressing) { d.dressing = dr; SaveSystem.Save(); g.SetMap(g.mapMode); }
                        NextRow();
                        RowLabel(x, "Garaja ışınlan", "Takibi, yarışı ve teslimatı iptal eder.");
                        if (UIKit.Button("tp", cx, rowY + 12, 260, 46, "IŞINLAN"))
                        {
                            g.police.EndPursuit(false); g.race.Abort(); g.delivery.Cancel();
                            g.CloseMenu();
                            g.player.Teleport(g.world.garagePos + Vector3.up * 0.5f, g.world.garageRot);
                            g.rig.Snap();
                        }
                        break;
                    }
                default: DrawControls(x); break;
            }
        }
        bool volDirty;

        void RowLabel(float x, string label, string desc)
        {
            UIKit.Text(x + 60, rowY + 6, 560, 34, label, 24, Color.white);
            UIKit.Text(x + 60, rowY + 38, 600, 26, desc, 15, UIKit.Grey, TextAnchor.MiddleLeft, false, false);
        }
        void NextRow()
        {
            UIKit.Fill(PX + 60, rowY + 72, PW - 120, 1, new Color(1, 1, 1, 0.07f));
            rowY += 80;
        }

        static readonly string[,] Keys =
        {
            { "W / S", "Gaz / Fren-geri" }, { "A / D", "Direksiyon" }, { "SPACE", "El freni" }, { "SHIFT", "Nitro" },
            { "Q", "Speedbreaker (sağ tık)" }, { "C", "Kamera" }, { "H", "Korna" }, { "E", "Garaj (yakındayken)" },
            { "J", "Yarışlar ve işler" }, { "B", "Kara liste" }, { "M / TAB", "Harita" }, { "F", "FPS göstergesi" },
            { "R", "Aracı düzelt" }, { "F9", "Hile: +1.000.000 para" }, { "F10", "Hile: 5 yıldız aranma" }, { "ESC", "Menü / geri" },
        };
        void DrawControls(float x)
        {
            int n = Keys.GetLength(0);
            for (int i = 0; i < n; i++)
            {
                float cx = x + 60 + (i / 8) * 580, cy = rowY + (i % 8) * 64;
                UIKit.KeyCap(cx, cy + 8, Keys[i, 0]);
                UIKit.Text(cx + 150, cy, 420, 52, Keys[i, 1], 20, Color.white, TextAnchor.MiddleLeft, false);
            }
            UIKit.Text(x + 60, PY + PH - 70, PW - 120, 40, "Drag yarışı: E / Q vites, A / D şerit.", 17, UIKit.Grey, TextAnchor.MiddleLeft, false, false);
        }

        // ---- EMEĞİ GEÇENLER
        List<string> creditLines;
        void DrawCredits(Game g, float x)
        {
            if (creditLines == null || creditLines.Count == 0)
            {
                creditLines = new List<string>();
                foreach (var l in g.Credits()) creditLines.Add(l);
                creditLines.Add("");
                creditLines.Add("#MIT LİSANS NOTLARI");
                creditLines.Add("Randomation Vehicle Physics — Copyright (c) Justin Couch. Arcade Car Physics — Copyright (c) Saarg.");
                creditLines.Add("Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the \"Software\"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions: The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.");
                creditLines.Add("THE SOFTWARE IS PROVIDED \"AS IS\", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.");
                creditLines.Add("");
                creditLines.Add("#CC BY 4.0");
                creditLines.Add("\"City 3D Model\" — Optic Idealist. Creative Commons Attribution 4.0 International (https://creativecommons.org/licenses/by/4.0/).");
                creditLines.Add("");
                creditLines.Add("#TEŞEKKÜRLER");
                creditLines.Add("Oynadığın için teşekkürler!");
            }
            float vx = x + 60, vy = PY + 100, vw = PW - 120, vh = PH - 140;
            // satır yükseklikleri: uzun paragraflar daha uzun
            float total = 0f;
            foreach (var l in creditLines) total += LineH(l);
            float auto = (Time.unscaledTime - creditStart - 1.5f) * 30f;
            float off = Mathf.Max(0f, auto) + creditScroll;
            float maxOff = Mathf.Max(0f, total - vh + 40f);
            if (off > maxOff + 200f) { creditStart = Time.unscaledTime; creditScroll = 0f; off = 0f; }   // döngü
            if (off < 0f) { creditScroll -= off; off = 0f; }

            GUI.BeginGroup(UIKit.R(vx, vy, vw, vh));
            float sOX = UIKit.OX, sOY = UIKit.OY;
            UIKit.OX = sOX - vx * UIKit.S; UIKit.OY = sOY - vy * UIKit.S;   // grup içi: tasarım koordinatı hâlâ geçerli
            float y = vy - off;
            foreach (var l in creditLines)
            {
                float h = LineH(l);
                if (y + h > vy - 10 && y < vy + vh + 10)
                {
                    if (l.StartsWith("#")) UIKit.Text(vx, y, vw, h, l.Substring(1), 24, UIKit.Teal, TextAnchor.MiddleLeft);
                    else if (l.Length > 0)
                    {
                        bool para = l.Length > 160;
                        if (!para) UIKit.Tex(UIKit.R(vx, y + 10, 32, 16), UIKit.ChevronTex, new Color(1, 1, 1, 0.8f));
                        UIKit.Text(para ? vx : vx + 44, y, para ? vw : vw - 44, h, l, para ? 15 : 20, para ? UIKit.Grey : Color.white, TextAnchor.UpperLeft, false, !para, true);
                    }
                }
                y += h;
            }
            UIKit.OX = sOX; UIKit.OY = sOY;
            GUI.EndGroup();
        }

        static float LineH(string l)
        {
            if (l.Length == 0) return 24f;
            if (l.StartsWith("#")) return 48f;
            if (l.Length > 160) return 26f * Mathf.Ceil(l.Length / 130f) + 16f;
            return l.Length > 85 ? 66f : 40f;
        }

        // ---- onay penceresi
        void DrawConfirm(Game g)
        {
            UIKit.Tex(UIKit.Full(), Texture2D.whiteTexture, new Color(0, 0, 0, 0.6f));
            float w = 760, h = 300, x = (UIKit.DW - w) / 2, y = (UIKit.DH - h) / 2;
            UIKit.Panel(x, y, w, h, 1f);
            bool reset = confirm == 1;
            UIKit.Fill(x + 30, y + 30, 6, 40, reset ? UIKit.Danger : UIKit.Teal);
            UIKit.Text(x + 50, y + 22, w - 80, 56, reset ? "KAYDI SIFIRLA?" : "OYUNDAN ÇIK?", 34, Color.white);
            UIKit.Text(x + 50, y + 86, w - 100, 80, reset ? "Tüm para, araçlar, yarışlar ve kara liste ilerlemesi silinir. Bu işlem geri alınamaz." : "İlerlemen kaydedilecek ve oyun kapanacak.", 19, UIKit.Grey, TextAnchor.UpperLeft, false, false, true);
            float by = y + h - 90;
            if (UIKit.Hit(x + 50, by, 300, 56)) confirmSel = 0;
            if (UIKit.Hit(x + w - 350, by, 300, 56)) confirmSel = 1;
            if (confirmSel == 0) UIKit.Bar(x + 46, by - 4, 308, 64, new Color(1, 1, 1, 0.25f));
            if (confirmSel == 1) UIKit.Bar(x + w - 354, by - 4, 308, 64, new Color(1, 1, 1, 0.25f));
            if (UIKit.Button("cf_yes", x + 50, by, 300, 56, reset ? "EVET, SIFIRLA" : "EVET, ÇIK", true, reset ? UIKit.Danger : UIKit.Teal)) DoConfirm(g);
            else if (UIKit.Button("cf_no", x + w - 350, by, 300, 56, "VAZGEÇ")) confirm = 0;
        }
    }
}
