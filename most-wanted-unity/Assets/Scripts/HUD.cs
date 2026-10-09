using UnityEngine;

namespace MostWanted
{
    /// <summary>IMGUI HUD (MW tarzı gösterge) ve menüler. Tüm metinler Türkçe.</summary>
    public class HUD : MonoBehaviour
    {
        public int garageSel;
        float fpsTimer;
        string fpsText;
        GUIStyle fpsStyle;
        GUIStyle big, mid, small, center, title, box, btn, toastStyle, tiny, radioStyle;
        bool init;
        Vector2 scroll, scroll2;
        const float RefH = 1080f;
        static readonly Color Orange = new Color(1f, 0.55f, 0.05f);
        static readonly Color Blue = new Color(0.25f, 0.65f, 1f);

        void Init()
        {
            init = true;
            big = new GUIStyle(GUI.skin.label) { fontSize = 70, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            mid = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold };
            small = new GUIStyle(GUI.skin.label) { fontSize = 20, wordWrap = true };
            tiny = new GUIStyle(GUI.skin.label) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
            center = new GUIStyle(GUI.skin.label) { fontSize = 130, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            title = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            box = new GUIStyle(GUI.skin.box);
            box.normal.background = Tex(new Color(0.02f, 0.02f, 0.03f, 0.86f));
            btn = new GUIStyle(GUI.skin.button) { fontSize = 21, fixedHeight = 40 };
            toastStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            radioStyle = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Italic, alignment = TextAnchor.MiddleLeft, wordWrap = true };
            foreach (var s in new[] { big, mid, small, center, title, toastStyle, tiny, radioStyle }) s.normal.textColor = Color.white;
            radioStyle.normal.textColor = new Color(0.7f, 0.9f, 1f);
            fpsStyle = new GUIStyle(GUI.skin.label) { fontSize = 18 };
            fpsStyle.normal.textColor = new Color(0.6f, 1f, 0.6f);
        }

        static Texture2D Tex(Color c)
        {
            var t = new Texture2D(1, 1);
            t.SetPixel(0, 0, c);
            t.Apply();
            return t;
        }

        static void Rect(Rect r, Color c)
        {
            var old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = old;
        }

        static void Bar(Rect r, float v, Color c)
        {
            Rect(r, new Color(0, 0, 0, 0.6f));
            Rect(new Rect(r.x + 2, r.y + 2, (r.width - 4) * Mathf.Clamp01(v), r.height - 4), c);
        }

        void Shadow(Rect r, string s, GUIStyle st)
        {
            var c = st.normal.textColor;
            st.normal.textColor = new Color(0, 0, 0, 0.85f);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), s, st);
            st.normal.textColor = c;
            GUI.Label(r, s, st);
        }

        readonly System.Collections.Generic.Dictionary<long, GUIStyle> alignCache = new System.Collections.Generic.Dictionary<long, GUIStyle>();
        GUIStyle Align(GUIStyle s, TextAnchor a)
        {
            long k = ((long)(uint)s.GetHashCode() << 8) | (long)(uint)a;
            GUIStyle r;
            if (!alignCache.TryGetValue(k, out r)) { r = new GUIStyle(s) { alignment = a }; alignCache[k] = r; }
            return r;
        }

        void OnGUI()
        {
            var g = Game.I;
            if (g == null || g.player == null) return;
            if (!init) Init();
            if (Screen.width <= 0 || Screen.height <= 0) return;
            float scale = Mathf.Max(0.05f, Screen.height / RefH);
            float W = Screen.width / scale, H = RefH;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            if (g.menu == Game.Menu.None || g.menu == Game.Menu.Pause) DrawHUD(g, W, H);
            switch (g.menu)
            {
                case Game.Menu.Pause: DrawPause(g, W, H); break;
                case Game.Menu.Garage: DrawGarage(g, W, H); break;
                case Game.Menu.Jobs: DrawJobs(g, W, H); break;
                case Game.Menu.Blacklist: DrawBlacklist(g, W, H); break;
                case Game.Menu.Map: DrawMap(g, W, H); break;
                case Game.Menu.Credits: DrawCredits(g, W, H); break;
            }
            if (g.showFps && g.opt != null)
            {
                var o = g.opt;
                fpsTimer -= Time.unscaledDeltaTime;
                if (fpsTimer <= 0f || fpsText == null)
                {
                    fpsTimer = 0.25f;
                    fpsText = Mathf.RoundToInt(o.avgFps) + " FPS  (" + o.frameMs.ToString("0.0") + " ms)\nCPU " + o.cpuMs.ToString("0.0") + " ms  GPU " + o.gpuMs.ToString("0.0") + " ms\n"
                        + "Batch " + o.batches + "  SetPass " + o.setPass + "\nÜçgen " + (o.tris / 1000) + "k\nÖlçek %" + Mathf.RoundToInt(o.renderScale * 100f) + " " + o.upscaler + "  [" + OptimizationManager.PresetNames[o.preset] + (o.preset == 3 ? "→" + OptimizationManager.PresetNames[o.effective] : "") + "]";
                }
                Rect(new Rect(W - 330, 0, 330, 128), new Color(0, 0, 0, 0.55f));
                GUI.Label(new Rect(W - 320, 2, 320, 126), fpsText, fpsStyle);
            }
        }

        // ------------------------------------------------------------------ HUD
        void DrawHUD(Game g, float W, float H)
        {
            var p = g.player;
            var pd = g.playerDriver;
            var d = SaveSystem.Data;
            float kmh = p.SpeedKmh;

            SpeedLines(g, W, H, kmh);
            Speedometer(g, p, pd, W, H, kmh);

            // sol üst
            Shadow(new Rect(20, 12, 600, 40), U.Money(d.money), mid);
            Shadow(new Rect(20, 46, 700, 30), "Kariyer ödülü: " + U.Money(d.careerBounty) + "   Kara Liste: #" + (5 - Mathf.Min(d.rivalsBeaten, 5) > 0 ? (5 - d.rivalsBeaten).ToString() : "1 ✓"), small);
            var def = p.def;
            Shadow(new Rect(20, 72, 700, 30), (def != null ? def.displayName : "") + "   •   " + CameraRig.ModeNames[(int)g.rig.mode] + (g.usingImportedMap ? "   •   İthal harita" : "") + (g.district != "" ? "   •   " + g.district : ""), small);

            Minimap(g, W, H);

            // takip
            var pol = g.police;
            if (pol.pursuit)
            {
                string stars = "";
                for (int i = 0; i < 5; i++) stars += i < pol.Stars ? "★" : "☆";
                var st = new GUIStyle(mid) { fontSize = 46, alignment = TextAnchor.MiddleCenter };
                st.normal.textColor = Mathf.Repeat(Time.unscaledTime, 0.6f) < 0.3f ? new Color(1f, 0.2f, 0.2f) : new Color(0.3f, 0.5f, 1f);
                GUI.Label(new Rect(W / 2 - 200, 8, 400, 60), stars, st);
                var c = Align(small, TextAnchor.MiddleCenter);
                Shadow(new Rect(W / 2 - 300, 64, 600, 28), "TAKİP  •  Ödül: " + U.Money(pol.bounty) + "  •  Polis: " + pol.CopCount + (pol.heli != null ? "  •  HELİKOPTER" : "") + "  •  " + Mathf.FloorToInt(pol.pursuitTime / 60f) + ":" + Mathf.FloorToInt(pol.pursuitTime % 60f).ToString("00"), c);
                if (pol.Seen) Shadow(new Rect(W / 2 - 200, 92, 400, 26), "GÖRÜLDÜN!", c);
                else
                {
                    Shadow(new Rect(W / 2 - 200, 92, 400, 26), pol.Hiding ? "SAKLANIYORSUN — SAKİNLEŞME HIZLI" : "SAKİNLEŞME", c);
                    Bar(new Rect(W / 2 - 160, 120, 320, 16), pol.cooldown, new Color(0.2f, 0.9f, 0.4f));
                }
                if (pol.bustProgress > 0.01f)
                {
                    Shadow(new Rect(W / 2 - 200, 142, 400, 26), "YAKALANIYORSUN!", c);
                    Bar(new Rect(W / 2 - 160, 170, 320, 16), pol.bustProgress, Color.red);
                }
            }
            if (pol.radioTime > 0f && !string.IsNullOrEmpty(pol.radio))
            {
                Rect(new Rect(20, 110, 520, 54), new Color(0, 0, 0, 0.45f));
                Shadow(new Rect(30, 112, 500, 50), "TELSİZ: " + pol.radio, radioStyle);
            }

            // yarış
            var r = g.race;
            if (r.Active)
            {
                Rect(new Rect(W - 340, 110, 320, 140), new Color(0, 0, 0, 0.45f));
                Shadow(new Rect(W - 325, 114, 300, 36), r.def.name, mid);
                if (r.def.type == RaceType.Tollbooth)
                    Shadow(new Rect(W - 325, 150, 300, 40), "Kalan: " + Mathf.Max(0f, r.tollTime).ToString("0.0") + " sn", mid);
                else
                    Shadow(new Rect(W - 325, 150, 300, 40), "Sıra: " + r.PlayerPosition() + "/" + r.entries.Count, mid);
                string extra = r.circuit ? "Tur: " + r.PlayerLap + "/" + r.laps + "   " : "";
                if (r.def.type == RaceType.Speedtrap && r.PlayerEntry != null) extra = "Radar toplamı: " + Mathf.RoundToInt(r.PlayerEntry.trapTotal) + "   ";
                Shadow(new Rect(W - 325, 188, 300, 30), extra + "Süre: " + r.raceTime.ToString("0.0"), small);
                if (r.IsDrag) Shadow(new Rect(W - 325, 214, 300, 30), "E: vites ↑  Q: vites ↓  A/D: şerit", small);
                if (r.Counting) Shadow(new Rect(0, H / 2 - 160, W, 200), Mathf.CeilToInt(r.countdown).ToString(), center);
                else if (r.raceTime < 1.2f) Shadow(new Rect(0, H / 2 - 160, W, 200), "BAŞLA!", center);
            }
            if (g.delivery.Active)
            {
                Rect(new Rect(W - 340, 260, 320, 70), new Color(0, 0, 0, 0.45f));
                Shadow(new Rect(W - 325, 264, 300, 30), "TESLİMAT  " + U.Money(g.delivery.reward), small);
                Shadow(new Rect(W - 325, 290, 300, 40), "Kalan: " + Mathf.CeilToInt(g.delivery.timeLeft) + " sn", mid);
            }

            // durum yazıları
            float sy = H * 0.62f;
            var cs = Align(toastStyle, TextAnchor.MiddleCenter);
            if (g.career.driftShow > 50f) { Shadow(new Rect(0, sy, W, 40), "DRIFT  " + Mathf.RoundToInt(g.career.driftShow), cs); sy += 40; }
            if (pd != null && pd.drafting) { Shadow(new Rect(0, sy, W, 40), "RÜZGAR TÜNELİ  +Nitro", cs); sy += 40; }
            if (p.TiresBlown) { Shadow(new Rect(0, sy, W, 40), "LASTİKLER PATLAK!", cs); sy += 40; }
            if (pd != null && pd.speedbreakerOn) Shadow(new Rect(0, sy, W, 40), "SPEEDBREAKER", cs);
            if (g.NearGarage && g.menu == Game.Menu.None) Shadow(new Rect(0, H - 300, W, 40), "Garaja girmek için [E]", cs);

            for (int i = 0; i < g.toasts.Count; i++)
            {
                float a = Mathf.Clamp01(g.toastTimes[i]);
                var tr = new Rect(W / 2 - 480, H * 0.24f + i * 42, 960, 40);
                Rect(tr, new Color(0, 0, 0, 0.55f * a));
                var old = GUI.color; GUI.color = new Color(1, 1, 1, a);
                GUI.Label(tr, g.toasts[i], toastStyle);
                GUI.color = old;
            }
            if (g.menu == Game.Menu.None)
                GUI.Label(new Rect(360, H - 32, 1300, 30), "WASD: Sür  Space: El freni  Shift: Nitro  Q/Sağ tık: Speedbreaker  C: Kamera  E: Garaj  J: İşler  B: Kara Liste  M: Harita  R: Düzelt  F: FPS  Esc: Menü", Align(tiny, TextAnchor.MiddleLeft));
        }

        void Speedometer(Game g, CarController p, PlayerDriver pd, float W, float H, float kmh)
        {
            float R = 150f;
            Vector2 c = new Vector2(W - 200, H - 200);
            Rect(new Rect(c.x - R - 10, c.y - R - 10, 2 * R + 20, 2 * R + 60), new Color(0, 0, 0, 0.0f));
            // devir yayı: -225° .. +45°
            int ticks = 40;
            float redFrom = 0.85f;
            float rpm01 = p.Rpm01;
            var oldM = GUI.matrix;
            for (int i = 0; i <= ticks; i++)
            {
                float t = i / (float)ticks;
                float ang = -225f + t * 270f;
                bool major = i % 5 == 0;
                Color col = t >= redFrom ? new Color(1f, 0.15f, 0.1f) : Color.white;
                if (t > rpm01) col.a = 0.25f;
                GUIUtility.RotateAroundPivot(ang + 90f, c);
                Rect(new Rect(c.x - (major ? 3 : 1.5f), c.y - R, major ? 6 : 3, major ? 22 : 12), col);
                GUI.matrix = oldM;
            }
            // ibre
            float na = -225f + rpm01 * 270f;
            GUIUtility.RotateAroundPivot(na + 90f, c);
            Rect(new Rect(c.x - 3, c.y - R + 10, 6, R - 10), p.revLimiter ? Color.red : Orange);
            GUI.matrix = oldM;
            Rect(new Rect(c.x - 12, c.y - 12, 24, 24), new Color(0.1f, 0.1f, 0.1f, 0.9f));
            // dijital
            Shadow(new Rect(c.x - 120, c.y + 10, 240, 80), Mathf.RoundToInt(kmh).ToString(), big);
            Shadow(new Rect(c.x - 120, c.y + 78, 240, 30), "km/sa", tiny);
            string gear = p.gear == 0 ? "R" : p.gear.ToString();
            var gs = new GUIStyle(big) { fontSize = 46 };
            gs.normal.textColor = p.Shifting ? Orange : Color.white;
            Shadow(new Rect(c.x - 40, c.y - 80, 80, 60), gear, gs);
            Shadow(new Rect(c.x - 60, c.y - 30, 120, 26), "x1000 rpm " + (p.rpm / 1000f).ToString("0.0"), tiny);
            // nitro ve speedbreaker çubukları
            Shadow(new Rect(c.x - R, c.y + R + 4, 140, 24), "NİTRO", Align(tiny, TextAnchor.MiddleLeft));
            Bar(new Rect(c.x - R + 62, c.y + R + 8, 2 * R - 62, 16), p.nitro, p.nitroActive ? Color.white : Blue);
            if (pd != null)
            {
                Shadow(new Rect(c.x - R, c.y + R + 28, 160, 24), "SPEEDBRK", Align(tiny, TextAnchor.MiddleLeft));
                Bar(new Rect(c.x - R + 92, c.y + R + 32, 2 * R - 92, 16), pd.speedbreaker, pd.speedbreakerOn ? Color.white : Orange);
            }
        }

        void SpeedLines(Game g, float W, float H, float kmh)
        {
            float k = Mathf.Clamp01((kmh - 170f) / 130f) + (g.player.nitroActive ? 0.6f : 0f);
            if (k <= 0.01f || g.menu != Game.Menu.None) return;
            var rnd = new System.Random(Mathf.FloorToInt(Time.unscaledTime * 30f));
            Vector2 c = new Vector2(W / 2, H * 0.45f);
            var oldM = GUI.matrix;
            int n = Mathf.RoundToInt(30 * Mathf.Min(1.5f, k));
            for (int i = 0; i < n; i++)
            {
                float ang = (float)rnd.NextDouble() * 360f;
                float dist = 380f + (float)rnd.NextDouble() * 500f;
                float len = 80f + (float)rnd.NextDouble() * 220f;
                GUIUtility.RotateAroundPivot(ang, c);
                Rect(new Rect(c.x + dist, c.y - 1, len, 2), new Color(1f, 1f, 1f, 0.12f * Mathf.Min(1f, k)));
                GUI.matrix = oldM;
            }
        }

        void Minimap(Game g, float W, float H)
        {
            float ms = 280f;
            Rect mr = new Rect(20, H - ms - 46, ms, ms);
            Rect(new Rect(mr.x - 4, mr.y - 4, ms + 8, ms + 8), new Color(0, 0, 0, 0.75f));
            GUI.BeginGroup(mr);
            if (g.mapRT != null) GUI.DrawTexture(new Rect(0, 0, ms, ms), g.mapRT);
            // yön hedefi
            var p = g.player;
            Vector3? target = null; Color tc = Color.green;
            if (g.race.Active) { target = g.race.NextCheckpoint; tc = Orange; }
            else if (g.delivery.Active) { target = g.delivery.target; tc = Blue; }
            if (target.HasValue)
            {
                Vector3 d = p.transform.InverseTransformDirection(target.Value - p.transform.position);
                Vector2 dir = new Vector2(d.x, -d.z);
                float half = ms / 2f;
                float worldHalf = g.mapCam.orthographicSize;
                Vector2 pos = new Vector2(half, half) + dir / worldHalf * half;
                pos.x = Mathf.Clamp(pos.x, 8, ms - 8); pos.y = Mathf.Clamp(pos.y, 8, ms - 8);
                Rect(new Rect(pos.x - 7, pos.y - 7, 14, 14), tc);
            }
            // kuzey göstergesi
            float yaw = p.transform.eulerAngles.y * Mathf.Deg2Rad;
            Vector2 n = new Vector2(ms / 2f - Mathf.Sin(yaw) * (ms / 2f - 16f), ms / 2f - Mathf.Cos(yaw) * (ms / 2f - 16f));
            Shadow(new Rect(n.x - 15, n.y - 15, 30, 30), "K", Align(small, TextAnchor.MiddleCenter));
            GUI.EndGroup();
            if (target.HasValue) Shadow(new Rect(mr.x, mr.y - 28, ms, 28), Mathf.RoundToInt(U.FlatDist(p.transform.position, target.Value)) + " m", small);
        }

        // ------------------------------------------------------------------ menüler
        Rect Panel(float W, float H, float w, float h, string head)
        {
            var r = new Rect((W - w) / 2, (H - h) / 2, w, h);
            Rect(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.35f));
            GUI.Box(r, GUIContent.none, box);
            GUI.Label(new Rect(r.x, r.y + 10, r.width, 50), head, title);
            return r;
        }

        void DrawPause(Game g, float W, float H)
        {
            if (Event.current.type == EventType.MouseUp) SaveSystem.Save();
            var r = Panel(W, H, 780, 1060, "DURAKLATILDI");
            GUILayout.BeginArea(new Rect(r.x + 40, r.y + 70, r.width - 80, r.height - 90));
            var d = SaveSystem.Data;
            GUILayout.Label("Para: " + U.Money(d.money) + "   Kazanılan yarış: " + d.racesWon + "   Kaçış: " + d.escapes + "   Yakalanma: " + d.busted, small);
            if (GUILayout.Button("Devam Et", btn)) g.CloseMenu();
            if (GUILayout.Button("Kaydet", btn)) { SaveSystem.Save(); g.Toast("Oyun kaydedildi."); }
            GUILayout.BeginHorizontal();
            GUILayout.Label("Grafik:", small, GUILayout.Width(90));
            for (int i = 0; i < 4; i++)
            {
                var old = GUI.backgroundColor;
                if (d.quality == i) GUI.backgroundColor = Orange;
                if (GUILayout.Button(OptimizationManager.PresetNames[i], btn)) g.ApplyQuality(i);
                GUI.backgroundColor = old;
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("FPS hedefi:", small, GUILayout.Width(120));
            for (int i = 0; i < OptimizationManager.FpsNames.Length; i++)
            {
                var old = GUI.backgroundColor;
                if (d.fpsTarget == i) GUI.backgroundColor = Orange;
                if (GUILayout.Button(OptimizationManager.FpsNames[i], btn)) g.SetFpsTarget(i);
                GUI.backgroundColor = old;
            }
            GUILayout.EndHorizontal();
            if (GUILayout.Button("Hep Gündüz: " + (d.alwaysDay ? "Açık" : "Kapalı (kısa geceler)"), btn)) { d.alwaysDay = !d.alwaysDay; SaveSystem.Save(); }
            GUILayout.BeginHorizontal();
            GUILayout.Label("Atmosfer:", small, GUILayout.Width(110));
            for (int i = 0; i < Game.AtmosphereNames.Length; i++)
            {
                var old = GUI.backgroundColor;
                if (d.atmosphere == i) GUI.backgroundColor = Orange;
                if (GUILayout.Button(Game.AtmosphereNames[i], btn)) g.ApplyAtmosphere(i);
                GUI.backgroundColor = old;
            }
            GUILayout.EndHorizontal();
            GUI.enabled = g.dressing != null;
            if (GUILayout.Button("Islak Zemin (yağmur sonrası): " + (d.wet ? "Açık" : "Kapalı"), btn)) g.SetWet(!d.wet);
            GUI.enabled = g.usingImportedMap;
            if (GUILayout.Button("Harita Süsleme: " + (d.dressing ? "Açık" : "Kapalı") + "  (haritayı yeniden yükler)", btn)) { d.dressing = !d.dressing; SaveSystem.Save(); g.SwitchMap(true); }
            GUI.enabled = true;
            if (GUILayout.Button("GPU Resident Drawer (deneysel): " + (d.gpuResidentDrawer ? "Açık" : "Kapalı"), btn)) { d.gpuResidentDrawer = !d.gpuResidentDrawer; SaveSystem.Save(); g.ApplyQuality(d.quality); }
            if (GUILayout.Button("FPS Göstergesi: " + (g.showFps ? "Açık" : "Kapalı") + " (F)", btn)) g.showFps = !g.showFps;
            GUI.enabled = g.HasImportedMap;
            if (GUILayout.Button("Harita: " + (g.usingImportedMap ? "İthal" : "Test") + (g.HasImportedMap ? "  (değiştir)" : "  (ithal harita yok)"), btn)) g.SwitchMap(!g.usingImportedMap);
            GUI.enabled = true;
            if (GUILayout.Button("Garaja Işınlan", btn))
            {
                g.police.EndPursuit(false); g.race.Abort(); g.delivery.Cancel();
                g.CloseMenu();
                g.player.Teleport(g.world.garagePos + Vector3.up * 0.5f, g.world.garageRot);
                g.rig.Snap();
            }
            for (int i = 0; i < 4; i++)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("Ses — " + AudioBus.Names[i] + ": %" + Mathf.RoundToInt(AudioBus.Volumes[i] * 100f), small, GUILayout.Width(250));
                float v = GUILayout.HorizontalSlider(AudioBus.Volumes[i], 0f, 1f, GUILayout.Width(320));
                if (Mathf.Abs(v - AudioBus.Volumes[i]) > 0.001f) { AudioBus.Volumes[i] = v; AudioBus.Store(d); }
                GUILayout.EndHorizontal();
            }
            if (GUILayout.Button("Emeği Geçenler", btn)) g.OpenMenu(Game.Menu.Credits);
            if (GUILayout.Button("Kaydı Sıfırla (Yeni Oyun)", btn))
            {
                int q = d.quality;
                SaveSystem.Reset();
                SaveSystem.Data.quality = q;
                g.race.Abort(); g.police.EndPursuit(false);
                g.CloseMenu();
                g.SpawnPlayer(g.world.garagePos + Vector3.up * 0.5f, g.world.garageRot);
                g.Toast("Yeni oyun başladı.");
            }
            if (GUILayout.Button("Çıkış", btn))
            {
                SaveSystem.Save();
                Application.Quit();
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
            }
            GUILayout.Space(8);
            GUILayout.Label("Kontroller: W/S gaz-fren, A/D direksiyon, Space el freni, Shift nitro, Q / sağ tık Speedbreaker, C kamera, R düzelt, E garaj, J işler, B kara liste, M/Tab harita, F FPS.  Drag: E/Q vites, A/D şerit.  Hileler: F9 para, F10 5 yıldız.", small);
            GUILayout.EndArea();
        }

        void DrawCredits(Game g, float W, float H)
        {
            var r = Panel(W, H, 1000, 640, "EMEĞİ GEÇENLER");
            GUILayout.BeginArea(new Rect(r.x + 40, r.y + 80, r.width - 80, r.height - 100));
            scroll2 = GUILayout.BeginScrollView(scroll2);
            foreach (var l in g.Credits()) GUILayout.Label("• " + l, small);
            GUILayout.EndScrollView();
            if (GUILayout.Button("Geri", btn)) g.OpenMenu(Game.Menu.Pause);
            GUILayout.EndArea();
        }

        void DrawJobs(Game g, float W, float H)
        {
            var r = Panel(W, H, 820, 760, "YARIŞLAR VE İŞLER");
            GUILayout.BeginArea(new Rect(r.x + 40, r.y + 80, r.width - 80, r.height - 100));
            if (g.race.Active && GUILayout.Button("Yarıştan Çekil", btn)) { g.race.Abort(); g.CloseMenu(); }
            if (g.police.pursuit) GUILayout.Label("Polis peşindeyken yarışa başlarsan takip sona erer (ödül alınmaz).", small);
            string[] tn = { "Sprint", "Tur", "Hız Kamerası", "Gişe", "Drag" };
            scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(470));
            foreach (var rd in g.world.races)
            {
                string label = rd.name + "  —  " + tn[(int)rd.type] + (rd.type == RaceType.Circuit ? " (" + rd.laps + " tur)" : "") + "  —  Ödül " + U.Money(rd.prize);
                if (GUILayout.Button(label, btn)) { g.CloseMenu(); g.race.StartRace(rd); }
            }
            GUILayout.EndScrollView();
            GUILayout.Space(8);
            if (g.delivery.Active) { if (GUILayout.Button("Teslimatı İptal Et", btn)) { g.delivery.Cancel(); g.CloseMenu(); } }
            else if (GUILayout.Button("Teslimat İşi Al (zamanlı)", btn)) { g.CloseMenu(); g.delivery.StartJob(); }
            if (GUILayout.Button("Kara Liste (B)", btn)) g.OpenMenu(Game.Menu.Blacklist);
            if (GUILayout.Button("Kapat (J)", btn)) g.CloseMenu();
            GUILayout.EndArea();
        }

        void DrawBlacklist(Game g, float W, float H)
        {
            var r = Panel(W, H, 1000, 820, "KARA LİSTE");
            var d = SaveSystem.Data;
            GUILayout.BeginArea(new Rect(r.x + 40, r.y + 70, r.width - 80, r.height - 90));
            GUILayout.Label("Kazanılan yarış: " + d.racesWon + "   Kariyer ödülü: " + U.Money(d.careerBounty) + "   Kilometre taşı: " + g.career.MilestoneCount + "/" + Career.Milestones.Length, small);
            for (int i = 0; i < Career.Rivals.Length; i++)
            {
                var rv = Career.Rivals[i];
                string why;
                bool can = g.career.CanChallenge(i, out why);
                GUILayout.BeginHorizontal();
                GUILayout.Label("#" + rv.rank + "  " + rv.name + "  (" + g.career.RivalCar(i).displayName + ")", mid, GUILayout.Width(470));
                if (i < d.rivalsBeaten) GUILayout.Label("YENİLDİ ✓", small);
                else
                {
                    GUI.enabled = can && !g.race.Active;
                    if (GUILayout.Button(can ? "Meydan Oku" : why, btn)) { g.CloseMenu(); g.career.Challenge(i); }
                    GUI.enabled = true;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.Space(10);
            GUILayout.Label("Kilometre Taşları", mid);
            foreach (var m in Career.Milestones)
                GUILayout.Label((SaveSystem.HasMilestone(m.id) ? "✓ " : "○ ") + m.text, small);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Kapat (B)", btn)) g.CloseMenu();
            GUILayout.EndArea();
        }

        void DrawMap(Game g, float W, float H)
        {
            float s = Mathf.Min(W - 100, H - 140);
            var r = new Rect((W - s) / 2, 80, s, s);
            Rect(new Rect(0, 0, W, H), new Color(0, 0, 0, 0.7f));
            GUI.Label(new Rect(0, 15, W, 50), "HARİTA — " + g.world.title, title);
            GUI.DrawTexture(r, g.bigMapRT);
            GUI.BeginGroup(r);
            var mc = g.bigMapCam;
            foreach (var kv in g.world.labels) Label(mc, kv.Value, kv.Key, s, Color.white);
            Label(mc, g.player.transform.position, "SEN", s, new Color(1f, 0.85f, 0f));
            Label(mc, g.world.garagePos, "GARAJ", s, Color.green);
            foreach (var h in g.world.hiding) Label(mc, h.bounds.center, h.name, s, Blue);
            if (g.race.Active) Label(mc, g.race.NextCheckpoint, "HEDEF", s, Orange);
            if (g.delivery.Active) Label(mc, g.delivery.target, "TESLİMAT", s, Blue);
            GUI.EndGroup();
            GUI.Label(new Rect(0, H - 50, W, 40), "Yakınlaştır: fare tekerleği veya +/-   •   Kaydır: WASD / sürükle   •   Kapat: M, Tab veya Esc", Align(small, TextAnchor.MiddleCenter));
        }

        void Label(Camera mc, Vector3 world, string text, float size, Color c)
        {
            Vector3 v = mc.WorldToViewportPoint(world);
            if (v.x < 0 || v.x > 1 || v.y < 0 || v.y > 1) return;
            float x = v.x * size, y = (1f - v.y) * size;
            Rect(new Rect(x - 5, y - 5, 10, 10), c);
            var st = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter, wordWrap = false };
            st.normal.textColor = c;
            Shadow(new Rect(x - 120, y + 6, 240, 26), text, st);
        }

        void StatRow(string name, float v)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, small, GUILayout.Width(150));
            var rr = GUILayoutUtility.GetRect(260, 22);
            Bar(rr, v, Orange);
            GUILayout.EndHorizontal();
        }

        void DrawGarage(Game g, float W, float H)
        {
            var r = Panel(W, H, 1300, 900, "GARAJ");
            var d = SaveSystem.Data;
            GUI.Label(new Rect(r.x + 30, r.y + 20, 400, 40), U.Money(d.money), mid);
            var cars = Catalog.Garage;
            garageSel = Mathf.Clamp(garageSel, 0, cars.Count - 1);

            GUILayout.BeginArea(new Rect(r.x + 30, r.y + 80, 430, r.height - 110));
            scroll = GUILayout.BeginScrollView(scroll);
            for (int i = 0; i < cars.Count; i++)
            {
                var c = cars[i];
                var cs = SaveSystem.Get(c.id);
                string tag = c.id == d.selected ? "  [SEÇİLİ]" : cs != null && cs.owned ? "  [SAHİP]" : "  " + U.Money(c.price);
                var old = GUI.backgroundColor;
                if (i == garageSel) GUI.backgroundColor = Orange;
                if (GUILayout.Button(c.displayName + tag, btn)) garageSel = i;
                GUI.backgroundColor = old;
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            var spec = cars[garageSel];
            var sv = SaveSystem.Get(spec.id);
            if (sv == null) return;
            var t = sv.tune;
            float top = spec.topSpeedKmh * (1f + 0.012f * (t[1] + t[2] + t[0]));
            float tq = spec.torqueNm * (1f + 0.08f * t[0] + 0.06f * t[1]);
            float grip = spec.grip * (1f + 0.045f * t[4] + 0.02f * t[3]);
            GUILayout.BeginArea(new Rect(r.x + 490, r.y + 80, r.width - 520, r.height - 110));
            GUILayout.Label(spec.displayName + "   (" + (spec.drive == 0 ? "Önden çekiş" : spec.drive == 1 ? "Arkadan itiş" : "4x4") + ")", mid);
            StatRow("Azami Hız", (top - 150f) / 200f);
            StatRow("İvme", (tq / spec.massKg - 0.15f) / 0.35f);
            StatRow("Yol Tutuş", (grip - 0.8f) / 0.6f);
            StatRow("Nitro", (1f + 0.25f * t[5]) / 1.75f);
            GUILayout.Label(Mathf.RoundToInt(top) + " km/sa  •  " + Mathf.RoundToInt(tq) + " Nm  •  " + spec.massKg + " kg  •  " + spec.redlineRpm + " rpm", small);
            GUILayout.Space(6);
            if (!sv.owned)
            {
                GUI.enabled = d.money >= spec.price;
                if (GUILayout.Button("Satın Al  " + U.Money(spec.price), btn))
                {
                    d.money -= spec.price; sv.owned = true; d.selected = spec.id; SaveSystem.Save();
                    g.Toast(spec.displayName + " satın alındı!");
                }
                GUI.enabled = true;
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUI.enabled = d.selected != spec.id;
                if (GUILayout.Button("Seç", btn)) { d.selected = spec.id; SaveSystem.Save(); }
                int sell = Mathf.RoundToInt(spec.price * 0.6f);
                GUI.enabled = d.selected != spec.id && SaveSystem.OwnedCount() > 1;
                if (GUILayout.Button("Sat (+" + U.Money(sell) + ")", btn))
                {
                    d.money += sell; sv.owned = false; sv.tune = new int[Catalog.TuneCount]; sv.color = -1; SaveSystem.Save();
                    g.Toast(spec.displayName + " satıldı.");
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();

                GUILayout.Label("Boya (" + U.Money(Catalog.PaintCost) + ")" + (sv.color >= 0 && sv.color < Catalog.Paints.Length ? "  —  " + Catalog.Paints[sv.color].name : "  —  Fabrika"), small);
                for (int row = 0; row < 2; row++)
                {
                    GUILayout.BeginHorizontal();
                    int per = (Catalog.Paints.Length + 1) / 2;
                    for (int i = row * per; i < Mathf.Min(Catalog.Paints.Length, (row + 1) * per); i++)
                    {
                        var old = GUI.backgroundColor;
                        GUI.backgroundColor = Catalog.Paints[i].color;
                        GUI.enabled = d.money >= Catalog.PaintCost && sv.color != i;
                        if (GUILayout.Button(new GUIContent(sv.color == i ? "✓" : "", Catalog.Paints[i].name), btn, GUILayout.Width(44)))
                        {
                            d.money -= Catalog.PaintCost; sv.color = i; SaveSystem.Save();
                            g.Toast("Boya: " + Catalog.Paints[i].name);
                            if (spec.id == d.selected && g.player != null) g.player.SetPaint(Catalog.Paints[i]); // canlı önizleme
                        }
                        GUI.enabled = true;
                        GUI.backgroundColor = old;
                    }
                    GUILayout.EndHorizontal();
                }
                if (sv.color >= 0 && GUILayout.Button("Fabrika rengine dön (ücretsiz)", btn)) { sv.color = -1; SaveSystem.Save(); g.Toast("Fabrika rengi — garajdan çıkınca uygulanır"); }
                if (GUI.tooltip != "") GUILayout.Label(GUI.tooltip, small);

                GUILayout.Label("Performans Paketleri", small);
                for (int k = 0; k < Catalog.TuneCount; k++) TuneRow(g, spec, sv, k);
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Garajdan Çık (E / Esc)", btn)) g.CloseMenu();
            GUILayout.EndArea();
        }

        void TuneRow(Game g, CarEntry spec, CarSave sv, int k)
        {
            var d = SaveSystem.Data;
            int level = sv.tune[k];
            GUILayout.BeginHorizontal();
            string lv = "";
            for (int i = 0; i < Catalog.MaxTune; i++) lv += i < level ? "■" : "□";
            GUILayout.Label(Catalog.TuneNames[k] + "  " + lv, small, GUILayout.Width(280));
            if (level < Catalog.MaxTune)
            {
                int cost = Catalog.TuneCost(spec, level);
                GUI.enabled = d.money >= cost;
                if (GUILayout.Button("Yükselt " + U.Money(cost), btn))
                {
                    d.money -= cost; sv.tune[k]++; SaveSystem.Save();
                    g.Toast(Catalog.TuneNames[k] + " seviye " + sv.tune[k] + "!");
                }
                GUI.enabled = true;
            }
            else GUILayout.Label("MAKS", small);
            GUILayout.EndHorizontal();
        }
    }
}
