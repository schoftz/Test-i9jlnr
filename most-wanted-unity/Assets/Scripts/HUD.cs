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

            if ((g.menu == Game.Menu.None || g.menu == Game.Menu.Pause) && !TitleScreen.Active && !StoryManager.Cinematic) DrawHUD(g, W, H);   // başlık ekranından açılan ayarlarda HUD yok
            switch (g.menu)
            {
                case Game.Menu.Pause: case Game.Menu.Credits: PauseMenu.Get().Draw(g); break;   // UI/PauseMenu.cs
                case Game.Menu.Garage: break;   // GarageStage (Carbon tarzı) çizer
                case Game.Menu.Jobs: DrawJobs(g, W, H); break;
                case Game.Menu.Blacklist: DrawBlacklist(g, W, H); break;
                case Game.Menu.Map: DrawMap(g, W, H); break;
            }
            if ((g.menu == Game.Menu.None || g.menu == Game.Menu.Pause || g.menu == Game.Menu.Credits) && !Cutscene.Active) Toasts.Draw(g);   // UI/Toasts.cs
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
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
        // ---- yeni sürüş HUD'ı (referans düzen: 1920x1080) ----
        GUIStyle numBig, numMid, numSmall, lblBold, lblSmall, rowName, rowGap, boxNum;
        Texture2D arcTex; int arcKey = -1;
        const float ArcStart = 200f, ArcSweep = 260f;   // saat yönü derece (0 = yukarı)

        void InitHud()
        {
            numBig = new GUIStyle(GUI.skin.label) { fontSize = 120, fontStyle = FontStyle.BoldAndItalic, alignment = TextAnchor.MiddleRight };
            numMid = new GUIStyle(GUI.skin.label) { fontSize = 64, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            numSmall = new GUIStyle(GUI.skin.label) { fontSize = 34, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            lblBold = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            lblSmall = new GUIStyle(GUI.skin.label) { fontSize = 18, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            rowName = new GUIStyle(GUI.skin.label) { fontSize = 19, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            rowGap = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Italic, alignment = TextAnchor.MiddleLeft };
            boxNum = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            foreach (var st in new[] { numBig, numMid, numSmall, lblBold, lblSmall, rowName, rowGap, boxNum }) st.normal.textColor = Color.white;
            rowGap.normal.textColor = new Color(0.85f, 0.9f, 1f);
        }

        static readonly Color PanelCol = new Color(0.06f, 0.07f, 0.09f, 0.72f);

        void DrawHUD(Game g, float W, float H)
        {
            if (numBig == null) InitHud();
            var p = g.player;
            var pd = g.playerDriver;
            var d = SaveSystem.Data;
            float kmh = p.SpeedKmh;
            var r = g.race;
            bool racing = r.Active;

            SpeedLines(g, W, H, kmh);
            Tachometer(g, p, pd, W, H, kmh);
            Minimap(g, W, H);

            if (racing) RaceHud(g, r, W, H);
            else
            {
                // serbest sürüş: sağ üstte küçük para + araç/semt
                Shadow(new Rect(W - 420, 14, 400, 36), U.Money(d.money), Align(numSmall, TextAnchor.MiddleRight));
                var def = p.def;
                Shadow(new Rect(W - 620, 50, 600, 26), (def != null ? def.displayName : "") + (g.district != "" ? "  •  " + g.district : "") + "  •  " + CameraRig.ModeNames[(int)g.rig.mode], Align(small, TextAnchor.MiddleRight));
                Shadow(new Rect(W - 620, 74, 600, 24), "Kariyer ödülü " + U.Money(d.careerBounty), Align(tiny, TextAnchor.MiddleRight));
            }

            // takip (yarışta yok)
            var pol = g.police;
            if (pol.pursuit && !racing)
            {
                string stars = "";
                for (int i = 0; i < 5; i++) stars += i < pol.Stars ? "★" : "☆";
                var st = new GUIStyle(mid) { fontSize = 46, alignment = TextAnchor.MiddleCenter };
                st.normal.textColor = Mathf.Repeat(Time.unscaledTime, 0.6f) < 0.3f ? new Color(1f, 0.2f, 0.2f) : new Color(0.3f, 0.5f, 1f);
                Rect(new Rect(W / 2 - 230, 6, 460, 120 + (pol.bustProgress > 0.01f ? 44 : 0)), PanelCol);
                GUI.Label(new Rect(W / 2 - 200, 6, 400, 56), stars, st);
                var c = Align(small, TextAnchor.MiddleCenter);
                Shadow(new Rect(W / 2 - 230, 58, 460, 24), "ÖDÜL " + U.Money(pol.bounty) + "   •   POLİS " + pol.CopCount + (pol.heli != null ? "   •   HELİKOPTER" : "") + "   •   " + RaceManager.FormatTime(pol.pursuitTime).Substring(0, 5), c);
                Shadow(new Rect(W / 2 - 230, 82, 460, 22), pol.Seen ? "GÖRÜLDÜN — Polislerin görüş alanından çık" : pol.Hiding ? "SAKLANIYORSUN" : "SAKİNLEŞME — saklanma noktalarına git (haritada mavi)", Align(tiny, TextAnchor.MiddleCenter));
                Bar(new Rect(W / 2 - 180, 106, 360, 12), pol.Seen ? 0f : pol.cooldown, new Color(0.2f, 0.9f, 0.4f));
                if (pol.bustProgress > 0.01f)
                {
                    Shadow(new Rect(W / 2 - 230, 122, 460, 22), "YAKALANIYORSUN! Hareket et!", Align(tiny, TextAnchor.MiddleCenter));
                    Bar(new Rect(W / 2 - 180, 146, 360, 12), pol.bustProgress, Color.red);
                }
            }
            if (pol.radioTime > 0f && !string.IsNullOrEmpty(pol.radio) && !racing)
            {
                Rect(new Rect(20, 20, 520, 54), PanelCol);
                Shadow(new Rect(30, 22, 500, 50), "TELSİZ: " + pol.radio, radioStyle);
            }
            if (g.delivery.Active)
            {
                Rect(new Rect(W - 340, 108, 320, 70), PanelCol);
                Shadow(new Rect(W - 325, 112, 300, 30), "TESLİMAT  " + U.Money(g.delivery.reward), small);
                Shadow(new Rect(W - 325, 138, 300, 40), "Kalan: " + Mathf.CeilToInt(g.delivery.timeLeft) + " sn", mid);
            }

            // durum yazıları
            float sy = H * 0.62f;
            var cs = Align(toastStyle, TextAnchor.MiddleCenter);
            if (g.career.driftShow > 20f)
            {
                var dc = g.career;
                Shadow(new Rect(0, sy, W, 40), "DRIFT  " + Mathf.RoundToInt(dc.driftShow) + "   ×" + Mathf.RoundToInt(dc.driftMult), cs);
                Rect(new Rect(W / 2 - 120, sy + 40, 240, 6), new Color(0, 0, 0, 0.5f));
                Rect(new Rect(W / 2 - 120, sy + 40, 240 * dc.driftChain, 6), new Color(0.37f, 0.88f, 0.82f, 0.95f));
                sy += 52;
            }
            if (pd != null && pd.drafting) { Shadow(new Rect(0, sy, W, 40), "RÜZGAR TÜNELİ  +Nitro", cs); sy += 40; }
            if (p.TiresBlown) { Shadow(new Rect(0, sy, W, 40), "LASTİKLER PATLAK!", cs); sy += 40; }
            if (pd != null && pd.speedbreakerOn) Shadow(new Rect(0, sy, W, 40), "SPEEDBREAKER", cs);
            if (g.NearGarage && g.menu == Game.Menu.None) Shadow(new Rect(0, H - 300, W, 40), "Garaja girmek için [E]", cs);
            if (racing && r.Counting) Shadow(new Rect(0, H / 2 - 160, W, 200), Mathf.CeilToInt(r.countdown).ToString(), center);
            else if (racing && r.raceTime < 1.2f) Shadow(new Rect(0, H / 2 - 160, W, 200), "BAŞLA!", center);
            if (racing && r.IsDrag) Shadow(new Rect(0, H - 330, W, 30), "E: vites ↑   Q: vites ↓   A/D: şerit", Align(small, TextAnchor.MiddleCenter));

            if (g.menu == Game.Menu.None && Time.timeSinceLevelLoad < 40f)
                GUI.Label(new Rect(0, H - 30, W, 28), "WASD sür  •  Space el freni  •  Shift nitro  •  Q speedbreaker  •  C kamera  •  H korna  •  E garaj  •  J işler  •  B kara liste  •  M harita  •  Esc menü", Align(tiny, TextAnchor.MiddleCenter));
        }

        // ---- yarış blokları: SIRA (sol üst) + sıralama, GEÇERLİ TUR (orta), TUR (sağ üst) ----
        void RaceHud(Game g, RaceManager r, float W, float H)
        {
            var stand = r.Standings();
            int total = r.entries.Count;
            int pos = r.PlayerPosition();
            bool solo = r.def.type == RaceType.Tollbooth;

            if (!solo)
            {
                Shadow(new Rect(30, 14, 200, 30), "SIRA", lblBold);
                Rect(new Rect(30, 46, 74, 84), PanelCol);
                Shadow(new Rect(30, 46, 74, 84), pos.ToString(), numMid);
                Shadow(new Rect(112, 60, 120, 60), "/ " + total, numSmall);
                // canlı sıralama listesi
                for (int i = 0; i < stand.Count && i < 6; i++)
                {
                    var e = stand[i].Key;
                    float y = 150 + i * 30;
                    bool me = e == r.PlayerEntry;
                    Rect(new Rect(30, y, 26, 26), me ? new Color(1f, 0.55f, 0.05f, 0.95f) : new Color(0.9f, 0.9f, 0.92f, 0.95f));
                    var bn = boxNum; var old = bn.normal.textColor; bn.normal.textColor = Color.black;
                    GUI.Label(new Rect(30, y, 26, 26), (i + 1).ToString(), bn);
                    bn.normal.textColor = old;
                    Rect(new Rect(58, y, 180, 26), PanelCol);
                    GUI.Label(new Rect(66, y, 172, 26), me ? "SEN" : e.name, rowName);
                    if (i > 0)
                    {
                        string gap = r.def.type == RaceType.Speedtrap ? "-" + Mathf.RoundToInt(stand[i].Value) + " km/s" : "+" + stand[i].Value.ToString("0.000");
                        Shadow(new Rect(244, y, 120, 26), gap, rowGap);
                    }
                }
            }

            // orta: GEÇERLİ TUR / SÜRE
            string head = r.def.type == RaceType.Tollbooth ? "KALAN SÜRE" : r.circuit ? "GEÇERLİ TUR" : "SÜRE";
            float t = r.def.type == RaceType.Tollbooth ? r.tollTime : r.circuit ? r.raceTime - r.lapStart : r.raceTime;
            Shadow(new Rect(W / 2 - 150, 4, 300, 30), head, Align(lblBold, TextAnchor.MiddleCenter));
            Rect(new Rect(W / 2 - 100, 36, 200, 40), PanelCol);
            Shadow(new Rect(W / 2 - 100, 36, 200, 40), RaceManager.FormatTime(t), Align(numSmall, TextAnchor.MiddleCenter));

            // sağ üst: TUR x / n (tur yarışı) veya radar toplamı
            if (r.circuit)
            {
                Shadow(new Rect(W - 190, 14, 120, 30), "TUR", lblBold);
                Rect(new Rect(W - 190, 46, 74, 84), PanelCol);
                Shadow(new Rect(W - 190, 46, 74, 84), r.PlayerLap.ToString(), numMid);
                Shadow(new Rect(W - 108, 60, 100, 60), "/ " + r.laps, numSmall);
            }
            else if (r.def.type == RaceType.Speedtrap && r.PlayerEntry != null)
            {
                Shadow(new Rect(W - 330, 14, 300, 30), "RADAR TOPLAMI", Align(lblBold, TextAnchor.MiddleRight));
                Rect(new Rect(W - 230, 46, 200, 46), PanelCol);
                Shadow(new Rect(W - 230, 46, 200, 46), Mathf.RoundToInt(r.PlayerEntry.trapTotal) + " km/s", Align(numSmall, TextAnchor.MiddleCenter));
            }
            Shadow(new Rect(W - 520, 136, 500, 26), r.def.name + "   •   " + U.Money(SaveSystem.Data.money), Align(tiny, TextAnchor.MiddleRight));
        }

        // ---- devir saati: üretilmiş yay dokusu + çentikler + ibre; büyük italik hız ----
        Texture2D ArcTexture(int maxK, float redK)
        {
            int key = maxK * 1000 + Mathf.RoundToInt(redK * 10f);
            if (arcTex != null && arcKey == key) return arcTex;
            const int S = 512;
            if (arcTex == null) { arcTex = new Texture2D(S, S, TextureFormat.RGBA32, true); arcTex.wrapMode = TextureWrapMode.Clamp; }
            var px = new Color[S * S];
            float rOut = 0.485f * S, rIn = 0.472f * S, rRedIn = 0.43f * S;
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = x + 0.5f - S / 2f, dy = y + 0.5f - S / 2f;
                    float rr = Mathf.Sqrt(dx * dx + dy * dy);
                    float ang = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg; if (ang < 0f) ang += 360f;   // saat yönü, 0 = yukarı
                    float rel = Mathf.Repeat(ang - ArcStart, 360f);
                    Color c = new Color(1, 1, 1, 0);
                    if (rel <= ArcSweep)
                    {
                        float k = rel / ArcSweep * maxK;
                        float aRing = Mathf.Clamp01(Mathf.Min(rr - rIn + 1f, rOut - rr + 1f));
                        if (aRing > 0f) c = new Color(1f, 1f, 1f, aRing * 0.95f);
                        // çentikler dokuya gömülü (döndürülmüş GUI çizimi yok)
                        float frac = k * 2f - Mathf.Round(k * 2f);
                        bool major = Mathf.Abs(Mathf.Round(k * 2f)) % 2 == 0;
                        float tickW = (major ? 1.6f : 1.0f) / (rr * Mathf.Deg2Rad * ArcSweep / maxK / 2f + 0.001f);
                        float tickLen = major ? 0.06f * S : 0.035f * S;
                        if (Mathf.Abs(frac) < tickW && rr > rOut - tickLen && rr <= rOut)
                            c = k >= redK ? new Color(1f, 0.25f, 0.18f, 1f) : new Color(1f, 1f, 1f, 1f);
                        if (k >= redK && c.a < 0.99f)
                        {
                            float aRed = Mathf.Clamp01(Mathf.Min(rr - rRedIn + 1f, rOut - rr + 1f));
                            if (aRed > c.a) c = new Color(0.95f, 0.12f, 0.1f, aRed * 0.9f);
                        }
                    }
                    px[y * S + x] = c;
                }
            arcTex.SetPixels(px);
            arcTex.Apply(true);
            arcKey = key;
            return arcTex;
        }

        void Tachometer(Game g, CarController p, PlayerDriver pd, float W, float H, float kmh)
        {
            float R = 150f;
            Vector2 c = new Vector2(W - 210, H - 200);
            int maxK = Mathf.Max(8, Mathf.CeilToInt(p.redline / 1000f) + 1);
            float redK = p.redline / 1000f * 0.92f;
            var tex = ArcTexture(maxK, redK);
            GUI.DrawTexture(new Rect(c.x - R, c.y - R, 2 * R, 2 * R), tex);

            // sayılar (çentikler ArcTexture içinde)
            for (int i = 0; i <= maxK; i++)
            {
                float ang = ArcStart + (float)i / maxK * ArcSweep;
                Color col = i >= redK ? new Color(1f, 0.2f, 0.15f) : Color.white;
                float rad = ang * Mathf.Deg2Rad;
                Vector2 np = c + new Vector2(Mathf.Sin(rad), -Mathf.Cos(rad)) * (R - 34f);
                var ns = lblSmall; var o = ns.normal.textColor; ns.normal.textColor = col;
                GUI.Label(new Rect(np.x - 15, np.y - 12, 30, 24), i.ToString(), ns);
                ns.normal.textColor = o;
            }
            // nitro (iç yay) ve speedbreaker (daha iç yay)
            ArcBar(c, R - 58f, p.nitro, p.nitroActive ? Color.white : Blue);
            if (pd != null) ArcBar(c, R - 68f, pd.speedbreaker, pd.speedbreakerOn ? Color.white : Orange);
            Shadow(new Rect(c.x - R + 4, c.y + R - 36, 120, 18), "NİTRO / SB", Align(tiny, TextAnchor.MiddleLeft));
            // ibre: döndürülmüş dikdörtgen yerine merkezden dışa küçük karelerden çizgi (Metal'de güvenli, kadran içinde kırpılı)
            float rpmK = Mathf.Clamp(p.rpm / 1000f, 0f, maxK);
            float na = (ArcStart + rpmK / maxK * ArcSweep) * Mathf.Deg2Rad;
            Vector2 nd = new Vector2(Mathf.Sin(na), -Mathf.Cos(na));
            Color ncol = p.revLimiter ? new Color(1f, 0.15f, 0.1f) : new Color(1f, 0.25f, 0.15f);
            for (float t = 14f; t < R - 20f; t += 2.5f)
            {
                Vector2 q = c + nd * t;
                float w = t < R * 0.6f ? 4f : 3f;
                Rect(new Rect(q.x - w * 0.5f, q.y - w * 0.5f, w, w), ncol);
            }

            // vites (üstte) + hız (büyük italik) + KM/S
            string gear = p.gear == 0 ? "R" : p.gear.ToString();
            var gs = numMid; var go = gs.normal.textColor; gs.normal.textColor = p.Shifting ? Orange : Color.white;
            Shadow(new Rect(c.x - 20, c.y - 70, 80, 64), gear, gs);
            gs.normal.textColor = go;
            Shadow(new Rect(c.x - 150, c.y - 10, 290, 120), Mathf.RoundToInt(kmh).ToString(), numBig);
            Shadow(new Rect(c.x - 40, c.y + 100, 100, 26), "KM/S", lblSmall);
        }

        /// <summary>İnce kavisli çubuk: yay boyunca küçük kareler (döndürme yok).</summary>
        void ArcBar(Vector2 c, float radius, float v, Color col)
        {
            float sweep = ArcSweep * 0.62f;   // yayın sol-alt kısmı (sayılarla çakışmaz)
            int seg = Mathf.Max(24, Mathf.RoundToInt(2f * Mathf.PI * radius * (sweep / 360f) / 2.2f));
            int lit = Mathf.RoundToInt(Mathf.Clamp01(v) * seg);
            for (int i = 0; i < seg; i++)
            {
                float ang = (ArcStart + (i + 0.5f) / seg * sweep) * Mathf.Deg2Rad;
                Vector2 q = c + new Vector2(Mathf.Sin(ang), -Mathf.Cos(ang)) * radius;
                Rect(new Rect(q.x - 2f, q.y - 2f, 4f, 4f), i < lit ? col : new Color(1f, 1f, 1f, 0.1f));
            }
        }

        float speedLineAlpha;
        void SpeedLines(Game g, float W, float H, float kmh)
        {
            bool want = SaveSystem.Data.speedLines && g.menu == Game.Menu.None && (g.player.nitroActive || kmh > 200f);
            speedLineAlpha = Mathf.MoveTowards(speedLineAlpha, want ? Mathf.Clamp01(0.5f + (kmh - 200f) / 200f + (g.player.nitroActive ? 0.4f : 0f)) : 0f, Time.unscaledDeltaTime * 2.5f);
            if (speedLineAlpha <= 0.01f) return;
            Vector2 c = new Vector2(W / 2, H * 0.48f);
            float now = Time.unscaledTime;
            for (int i = 0; i < 26; i++)
            {
                // her çizginin kendi yavaş döngüsü (~0.35 sn) ve yumuşak giriş/çıkışı → titreme yok
                float ph = now * 2.8f + i * 0.618f;
                int cyc = Mathf.FloorToInt(ph);
                float fr = ph - cyc;
                var rnd = new System.Random(cyc * 131 + i * 7919);
                float ang = (float)rnd.NextDouble() * Mathf.PI * 2f;
                float dist = 520f + (float)rnd.NextDouble() * 420f + fr * 120f;   // dışa doğru akar
                float len = 120f + (float)rnd.NextDouble() * 260f;
                Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
                Color lc = new Color(1f, 1f, 1f, 0.10f * speedLineAlpha * Mathf.Sin(fr * Mathf.PI));
                for (float t = 0; t < len; t += 6f) { Vector2 q = c + dir * (dist + t); Rect(new Rect(q.x - 1.2f, q.y - 1.2f, 2.4f, 2.4f), lc); }
            }
        }

        void Minimap(Game g, float W, float H)
        {
            float ms = 270f;
            Rect mr = new Rect(30, H - ms - 40, ms, ms);
            var p = g.player;
            if (g.mapRT != null && g.mapRT.IsCreated())
            {
                // Yuvarlak maske: özel shader yerine yatay şeritler (DrawTextureWithTexCoords) — Metal/tüm platformlarda güvenli
                const int strips = 90;
                float h = ms / strips;
                for (int i = 0; i < strips; i++)
                {
                    float yc = (i + 0.5f) / strips * 2f - 1f;           // -1..1 (üst→alt)
                    float half = Mathf.Sqrt(Mathf.Max(0f, 1f - yc * yc)); // kiriş yarı genişliği (0..1)
                    if (half <= 0.001f) continue;
                    float x0 = 0.5f - half * 0.5f;
                    Rect dst = new Rect(mr.x + x0 * ms, mr.y + i * h, half * ms, h + 0.6f);
                    Rect uv = new Rect(x0, 1f - (i + 1f) / strips, half, 1f / strips);
                    GUI.DrawTextureWithTexCoords(dst, g.mapRT, uv, false);
                }
            }
            // çerçeve halkası (yuvarlak)
            DrawRing(mr.center, ms / 2f, new Color(1f, 1f, 1f, 0.85f));
            Vector2 c = mr.center;
            // hedef yönü
            Vector3? target = null; Color tc = Color.green;
            if (g.race.Active) { target = g.race.NextCheckpoint; tc = Orange; }
            else if (g.delivery.Active) { target = g.delivery.target; tc = Blue; }
            if (target.HasValue)
            {
                Vector3 d = p.transform.InverseTransformDirection(target.Value - p.transform.position);
                Vector2 dir = new Vector2(d.x, -d.z);
                float half = ms / 2f;
                Vector2 pos = dir / Mathf.Max(1f, g.mapCam.orthographicSize) * half;
                if (pos.magnitude > half - 10f) pos = pos.normalized * (half - 10f);
                pos += c;
                Rect(new Rect(pos.x - 7, pos.y - 7, 14, 14), tc);
                Shadow(new Rect(mr.x, mr.y - 28, ms, 28), Mathf.RoundToInt(U.FlatDist(p.transform.position, target.Value)) + " m", Align(small, TextAnchor.MiddleCenter));
            }
            // takipte saklanma noktaları (sakinleşme sırasında)
            if (g.police.pursuit && !g.police.Seen)
            {
                float half = ms / 2f;
                foreach (var h in g.world.hiding)
                {
                    Vector3 d = p.transform.InverseTransformDirection(h.bounds.center - p.transform.position);
                    Vector2 pos = new Vector2(d.x, -d.z) / Mathf.Max(1f, g.mapCam.orthographicSize) * half;
                    if (pos.magnitude > half - 10f) continue;
                    pos += c;
                    bool blink = Mathf.Repeat(Time.unscaledTime, 0.8f) < 0.55f;
                    Rect(new Rect(pos.x - 8, pos.y - 8, 16, 16), blink ? Blue : new Color(0.1f, 0.2f, 0.5f, 0.9f));
                    GUI.Label(new Rect(pos.x - 8, pos.y - 9, 16, 16), "S", lblSmall);
                }
            }
            // kuzey göstergesi (araç yönü yukarı)
            float yaw = p.transform.eulerAngles.y * Mathf.Deg2Rad;
            Vector2 n = c + new Vector2(-Mathf.Sin(yaw), -Mathf.Cos(yaw)) * (ms / 2f - 14f);
            Rect(new Rect(n.x - 12, n.y - 12, 24, 24), new Color(0.1f, 0.1f, 0.12f, 0.9f));
            GUI.Label(new Rect(n.x - 12, n.y - 12, 24, 24), "K", lblSmall);
        }

        Texture2D ringTex;
        void DrawRing(Vector2 c, float r, Color col)
        {
            if (ringTex == null)
            {
                const int S = 256;
                ringTex = new Texture2D(S, S, TextureFormat.RGBA32, true);
                var px = new Color[S * S];
                for (int y = 0; y < S; y++)
                    for (int x = 0; x < S; x++)
                    {
                        float dx = x + 0.5f - S / 2f, dy = y + 0.5f - S / 2f;
                        float rr = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(Mathf.Min(rr - (S / 2f - 4f) + 1f, (S / 2f - 0.5f) - rr + 1f));
                        px[y * S + x] = new Color(1, 1, 1, a);
                    }
                ringTex.SetPixels(px); ringTex.Apply(true);
            }
            var old = GUI.color; GUI.color = col;
            GUI.DrawTexture(new Rect(c.x - r - 2, c.y - r - 2, 2 * r + 4, 2 * r + 4), ringTex);
            GUI.color = old;
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
            GUILayout.Label((spec.powerHp > 0 ? spec.powerHp + " hp  •  " : "") + Mathf.RoundToInt(tq) + " Nm  •  " + Mathf.RoundToInt(top) + " km/sa  •  " + spec.massKg + " kg  •  " + (spec.gears <= 1 ? "tek oran (elektrikli)" : spec.gears + " vites") + (spec.zeroTo100 > 0f ? "  •  0-100: " + spec.zeroTo100.ToString("0.0") + " sn" : ""), small);
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

                // Motor sesi (kozmetik)
                GUILayout.BeginHorizontal();
                string curEng = string.IsNullOrEmpty(sv.engineSound) ? "Orijinal" : EngineAudio.EngineNames[Mathf.Max(0, System.Array.IndexOf(EngineAudio.EngineTypes, sv.engineSound))];
                GUILayout.Label("Motor Sesi: " + curEng + "  (" + U.Money(1000) + ")", small, GUILayout.Width(360));
                GUI.enabled = d.money >= 1000;
                if (GUILayout.Button("◀", btn, GUILayout.Width(50))) SwapEngine(g, spec, sv, -1);
                if (GUILayout.Button("▶", btn, GUILayout.Width(50))) SwapEngine(g, spec, sv, 1);
                GUI.enabled = true;
                if (!string.IsNullOrEmpty(sv.engineSound) && GUILayout.Button("Orijinal", btn, GUILayout.Width(110))) { sv.engineSound = ""; SaveSystem.Save(); RefreshEngine(g, spec); }
                GUILayout.EndHorizontal();
                GUILayout.Label("Performans Paketleri", small);
                for (int k = 0; k < Catalog.TuneCount; k++) TuneRow(g, spec, sv, k);
            }
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Garajdan Çık (E / Esc)", btn)) g.CloseMenu();
            GUILayout.EndArea();
        }

        void SwapEngine(Game g, CarEntry spec, CarSave sv, int dir)
        {
            var types = EngineAudio.EngineTypes;
            int i = System.Array.IndexOf(types, sv.engineSound);
            i = i < 0 ? (dir > 0 ? 0 : types.Length - 1) : (i + dir + types.Length) % types.Length;
            sv.engineSound = types[i];
            SaveSystem.Data.money -= 1000;
            SaveSystem.Save();
            g.Toast("Motor sesi: " + EngineAudio.EngineNames[i]);
            RefreshEngine(g, spec);
        }

        void RefreshEngine(Game g, CarEntry spec)
        {
            // garajdaki araç seçiliyse anında duyulsun
            if (spec.id == SaveSystem.Data.selected && g.playerDriver != null && g.playerDriver.engineAudio != null) g.playerDriver.engineAudio.Reconfigure();
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
                    if (k == (int)Tune.Egzoz) RefreshEngine(g, spec);
                    g.Toast(Catalog.TuneNames[k] + " seviye " + sv.tune[k] + "!");
                }
                GUI.enabled = true;
            }
            else GUILayout.Label("MAKS", small);
            GUILayout.EndHorizontal();
        }
    }
}
