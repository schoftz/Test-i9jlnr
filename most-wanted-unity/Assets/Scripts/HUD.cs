using UnityEngine;

namespace MostWanted
{
    /// <summary>IMGUI tabanlı HUD ve menüler (tüm metinler Türkçe).</summary>
    public class HUD : MonoBehaviour
    {
        public int garageSel;
        GUIStyle big, mid, small, center, title, box, btn, toastStyle;
        bool init;
        Vector2 scroll;
        const float RefH = 1080f;

        void Init()
        {
            init = true;
            big = new GUIStyle(GUI.skin.label) { fontSize = 64, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight };
            mid = new GUIStyle(GUI.skin.label) { fontSize = 28, fontStyle = FontStyle.Bold };
            small = new GUIStyle(GUI.skin.label) { fontSize = 20 };
            center = new GUIStyle(GUI.skin.label) { fontSize = 120, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            title = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            box = new GUIStyle(GUI.skin.box);
            box.normal.background = Tex(new Color(0f, 0f, 0f, 0.78f));
            btn = new GUIStyle(GUI.skin.button) { fontSize = 22, fixedHeight = 40 };
            toastStyle = new GUIStyle(GUI.skin.label) { fontSize = 26, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            foreach (var s in new[] { big, mid, small, center, title, toastStyle }) s.normal.textColor = Color.white;
            small.wordWrap = true;
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
            st.normal.textColor = new Color(0, 0, 0, 0.8f);
            GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), s, st);
            st.normal.textColor = c;
            GUI.Label(r, s, st);
        }

        void OnGUI()
        {
            var g = Game.I;
            if (g == null || g.player == null) return;
            if (!init) Init();
            float scale = Screen.height / RefH;
            float W = Screen.width / scale, H = RefH;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));

            DrawHUD(g, W, H);

            switch (g.menu)
            {
                case Game.Menu.Pause: DrawPause(g, W, H); break;
                case Game.Menu.Garage: DrawGarage(g, W, H); break;
                case Game.Menu.Jobs: DrawJobs(g, W, H); break;
            }
        }

        void DrawHUD(Game g, float W, float H)
        {
            var p = g.player;
            var save = SaveSystem.Data;

            // Hız göstergesi
            float kmh = p.SpeedKmh;
            Rect(new Rect(W - 330, H - 190, 310, 170), new Color(0, 0, 0, 0.45f));
            Shadow(new Rect(W - 330, H - 190, 230, 90), Mathf.RoundToInt(kmh).ToString(), big);
            Shadow(new Rect(W - 95, H - 160, 80, 40), "km/sa", small);
            Shadow(new Rect(W - 95, H - 125, 80, 40), "V" + p.Gear, mid);
            Bar(new Rect(W - 315, H - 90, 280, 16), p.EngineRpm01, Color.Lerp(Color.white, Color.red, p.EngineRpm01 > 0.85f ? 1f : 0f));
            Shadow(new Rect(W - 315, H - 72, 280, 30), "NİTRO", small);
            Bar(new Rect(W - 240, H - 66, 205, 20), p.nitro, p.nitroActive ? new Color(1f, 0.5f, 0f) : new Color(0.2f, 0.7f, 1f));

            // Para
            Shadow(new Rect(20, 15, 500, 40), U.Money(save.money), mid);
            Shadow(new Rect(20, 50, 600, 30), Catalog.Cars[save.selected].name + "   " + (g.Night > 0.5f ? "Gece" : "Gündüz") + (g.bumperCam ? "   [Tampon Kamerası]" : ""), small);

            // Minimap
            float ms = 260f;
            Rect mr = new Rect(20, H - ms - 20, ms, ms);
            Rect(new Rect(mr.x - 4, mr.y - 4, ms + 8, ms + 8), new Color(0, 0, 0, 0.7f));
            if (g.mapRT != null) GUI.DrawTexture(mr, g.mapRT);
            // yön göstergesi: hedef (yarış/teslimat/garaj)
            Vector3? target = null; Color tc = Color.green;
            if (g.race.Active) { target = g.race.NextCheckpoint; tc = new Color(1f, 0.6f, 0f); }
            else if (g.delivery.Active) { target = g.delivery.target; tc = new Color(0f, 0.7f, 1f); }
            if (target.HasValue)
            {
                Vector3 d = p.transform.InverseTransformDirection(target.Value - p.transform.position);
                float ang = Mathf.Atan2(d.x, d.z);
                Vector2 c = mr.center;
                Vector2 pos = c + new Vector2(Mathf.Sin(ang), -Mathf.Cos(ang)) * (ms / 2 - 12);
                Rect(new Rect(pos.x - 7, pos.y - 7, 14, 14), tc);
                Shadow(new Rect(mr.x, mr.y - 28, ms, 28), Mathf.RoundToInt(U.FlatDist(p.transform.position, target.Value)) + " m", small);
            }

            // Polis
            var pol = g.police;
            if (pol.pursuit)
            {
                string stars = "";
                for (int i = 0; i < 5; i++) stars += i < pol.Stars ? "★" : "☆";
                var st = new GUIStyle(mid) { fontSize = 44, alignment = TextAnchor.MiddleCenter };
                st.normal.textColor = Mathf.Repeat(Time.unscaledTime, 0.6f) < 0.3f ? new Color(1f, 0.2f, 0.2f) : new Color(0.3f, 0.5f, 1f);
                GUI.Label(new Rect(W / 2 - 200, 10, 400, 60), stars, st);
                Shadow(new Rect(W / 2 - 200, 66, 400, 30), "TAKİP  •  Ödül: " + U.Money(pol.bounty) + "  •  Polis: " + pol.CopCount, new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
                if (pol.Seen)
                    Shadow(new Rect(W / 2 - 200, 96, 400, 26), "GÖRÜLDÜN!", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
                else
                {
                    Shadow(new Rect(W / 2 - 200, 96, 400, 26), "SAKİNLEŞME", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
                    Bar(new Rect(W / 2 - 150, 124, 300, 16), pol.cooldown, new Color(0.2f, 0.9f, 0.4f));
                }
                if (pol.bustProgress > 0.01f)
                {
                    Shadow(new Rect(W / 2 - 200, 146, 400, 26), "YAKALANIYORSUN!", new GUIStyle(small) { alignment = TextAnchor.MiddleCenter });
                    Bar(new Rect(W / 2 - 150, 174, 300, 16), pol.bustProgress, Color.red);
                }
            }

            // Yarış
            var r = g.race;
            if (r.Active)
            {
                Rect(new Rect(W - 330, 15, 310, 130), new Color(0, 0, 0, 0.45f));
                Shadow(new Rect(W - 315, 20, 300, 40), r.current.name, mid);
                Shadow(new Rect(W - 315, 58, 300, 40), "Sıra: " + r.PlayerPosition() + "/4", mid);
                Shadow(new Rect(W - 315, 95, 300, 40), (r.circuit ? "Tur: " + r.PlayerLap + "/" + r.laps + "   " : "") + "Süre: " + r.raceTime.ToString("0.0"), small);
                if (r.Counting)
                    Shadow(new Rect(0, H / 2 - 120, W, 200), Mathf.CeilToInt(r.countdown).ToString(), center);
                else if (r.raceTime < 1.2f)
                    Shadow(new Rect(0, H / 2 - 120, W, 200), "BAŞLA!", center);
            }

            // Teslimat
            if (g.delivery.Active)
            {
                Rect(new Rect(W - 330, 155, 310, 70), new Color(0, 0, 0, 0.45f));
                Shadow(new Rect(W - 315, 160, 300, 30), "TESLİMAT  " + U.Money(g.delivery.reward), small);
                Shadow(new Rect(W - 315, 185, 300, 40), "Kalan: " + Mathf.CeilToInt(g.delivery.timeLeft) + " sn", mid);
            }

            // Garaj ipucu
            if (g.NearGarage && g.menu == Game.Menu.None)
                Shadow(new Rect(0, H - 260, W, 40), "Garaja girmek için [E]", new GUIStyle(toastStyle));

            // Bildirimler
            for (int i = 0; i < g.toasts.Count; i++)
            {
                float a = Mathf.Clamp01(g.toastTimes[i]);
                var tr = new Rect(W / 2 - 450, H * 0.25f + i * 42, 900, 40);
                Rect(tr, new Color(0, 0, 0, 0.55f * a));
                var old = GUI.color; GUI.color = new Color(1, 1, 1, a);
                GUI.Label(tr, g.toasts[i], toastStyle);
                GUI.color = old;
            }

            if (g.menu == Game.Menu.None)
                GUI.Label(new Rect(300, H - 34, 1100, 30), "WASD/Oklar: Sür  Space: El freni  Shift: Nitro  C: Kamera  E: Garaj  J: İşler  R: Düzelt  Esc: Duraklat", small);
        }

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
            var r = Panel(W, H, 520, 560, "DURAKLATILDI");
            GUILayout.BeginArea(new Rect(r.x + 40, r.y + 80, r.width - 80, r.height - 100));
            var d = SaveSystem.Data;
            GUILayout.Label("Para: " + U.Money(d.money) + "   Kazanılan yarış: " + d.racesWon, small);
            GUILayout.Label("Kaçış: " + d.escapes + "   Yakalanma: " + d.busted, small);
            GUILayout.Space(10);
            if (GUILayout.Button("Devam Et", btn)) g.CloseMenu();
            if (GUILayout.Button("Kaydet", btn)) { SaveSystem.Save(); g.Toast("Oyun kaydedildi."); }
            if (GUILayout.Button("Araca Dön / Garaja Işınlan", btn))
            {
                g.police.EndPursuit(false);
                g.race.Abort();
                g.delivery.Cancel();
                g.CloseMenu();
                g.player.Teleport(g.city.garagePos + Vector3.up * 0.6f, g.city.garageRot);
            }
            if (GUILayout.Button("Gün/Gece: " + (g.dayLength > 100f ? "Normal" : "Hızlı"), btn))
                g.dayLength = g.dayLength > 100f ? 60f : 420f;
            if (GUILayout.Button("Kaydı Sıfırla (Yeni Oyun)", btn))
            {
                SaveSystem.Reset();
                g.race.Abort();
                g.police.EndPursuit(false);
                g.CloseMenu();
                g.SpawnPlayer(g.city.garagePos + Vector3.up * 0.6f, g.city.garageRot);
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
            GUILayout.EndArea();
        }

        void DrawJobs(Game g, float W, float H)
        {
            var r = Panel(W, H, 700, 620, "İŞLER VE YARIŞLAR");
            GUILayout.BeginArea(new Rect(r.x + 40, r.y + 80, r.width - 80, r.height - 100));
            if (g.race.Active)
            {
                if (GUILayout.Button("Yarıştan Çekil", btn)) { g.race.Abort(); g.CloseMenu(); }
                GUILayout.Space(10);
            }
            if (g.police.pursuit)
                GUILayout.Label("Polis peşindeyken yarışa başlarsan takip sona erer (ödül alınmaz).", small);
            for (int i = 0; i < g.race.races.Count; i++)
            {
                var rd = g.race.races[i];
                string label = rd.name + "  —  " + (rd.circuit ? "Tur (" + rd.laps + " tur)" : "Sprint") + "  —  Ödül " + U.Money(rd.prize);
                if (GUILayout.Button(label, btn)) { g.CloseMenu(); g.race.StartRace(i); }
            }
            GUILayout.Space(12);
            if (g.delivery.Active)
            {
                if (GUILayout.Button("Teslimatı İptal Et", btn)) { g.delivery.Cancel(); g.CloseMenu(); }
            }
            else if (GUILayout.Button("Teslimat İşi Al (zamanlı)", btn)) { g.CloseMenu(); g.delivery.StartJob(); }
            GUILayout.Space(12);
            if (GUILayout.Button("Kapat (J)", btn)) g.CloseMenu();
            GUILayout.EndArea();
        }

        void StatRow(string name, float v)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(name, small, GUILayout.Width(150));
            var rr = GUILayoutUtility.GetRect(260, 22);
            Bar(rr, v, new Color(1f, 0.6f, 0.1f));
            GUILayout.EndHorizontal();
        }

        void DrawGarage(Game g, float W, float H)
        {
            var r = Panel(W, H, 1200, 800, "GARAJ");
            var d = SaveSystem.Data;
            GUI.Label(new Rect(r.x + 30, r.y + 20, 400, 40), U.Money(d.money), mid);

            // Sol: araç listesi
            GUILayout.BeginArea(new Rect(r.x + 30, r.y + 80, 420, r.height - 110));
            scroll = GUILayout.BeginScrollView(scroll);
            for (int i = 0; i < Catalog.Cars.Length; i++)
            {
                var c = Catalog.Cars[i];
                var cs = SaveSystem.Get(i);
                string tag = i == d.selected ? "  [SEÇİLİ]" : cs.owned ? "  [SAHİP]" : "  " + U.Money(c.price);
                var old = GUI.backgroundColor;
                if (i == garageSel) GUI.backgroundColor = new Color(1f, 0.6f, 0.1f);
                if (GUILayout.Button(c.name + tag, btn)) garageSel = i;
                GUI.backgroundColor = old;
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            // Sağ: detaylar
            var spec = Catalog.Cars[garageSel];
            var sv = SaveSystem.Get(garageSel);
            GUILayout.BeginArea(new Rect(r.x + 480, r.y + 80, r.width - 510, r.height - 110));
            GUILayout.Label(spec.name, mid);
            StatRow("Azami Hız", (spec.maxSpeed * (1 + 0.05f * sv.engine) - 150f) / 170f);
            StatRow("İvme", (spec.torque * (1 + 0.14f * sv.engine) / spec.mass - 1.5f) / 4f);
            StatRow("Yol Tutuş", (spec.grip * (1 + 0.07f * sv.handling) - 0.85f) / 0.55f);
            StatRow("Nitro", (spec.nitroCap * (1 + 0.3f * sv.nitro)) / 3.2f);
            GUILayout.Label(Mathf.RoundToInt(spec.maxSpeed * (1 + 0.05f * sv.engine)) + " km/sa  •  " + Mathf.RoundToInt(spec.torque * (1 + 0.14f * sv.engine)) + " Nm  •  " + spec.mass + " kg", small);
            GUILayout.Space(8);

            if (!sv.owned)
            {
                GUI.enabled = d.money >= spec.price;
                if (GUILayout.Button("Satın Al  " + U.Money(spec.price), btn))
                {
                    d.money -= spec.price; sv.owned = true; d.selected = garageSel; SaveSystem.Save();
                    g.Toast(spec.name + " satın alındı!");
                }
                GUI.enabled = true;
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUI.enabled = d.selected != garageSel;
                if (GUILayout.Button("Seç", btn)) { d.selected = garageSel; SaveSystem.Save(); }
                int sell = Mathf.RoundToInt(spec.price * 0.6f);
                GUI.enabled = d.selected != garageSel && SaveSystem.OwnedCount() > 1;
                if (GUILayout.Button("Sat (+" + U.Money(sell) + ")", btn))
                {
                    d.money += sell; sv.owned = false; sv.engine = sv.nitro = sv.handling = 0; sv.color = -1; SaveSystem.Save();
                    g.Toast(spec.name + " satıldı.");
                }
                GUI.enabled = true;
                GUILayout.EndHorizontal();

                GUILayout.Space(6);
                GUILayout.Label("Boya (" + U.Money(Catalog.PaintCost) + ")", small);
                GUILayout.BeginHorizontal();
                for (int i = 0; i < Catalog.Paints.Length; i++)
                {
                    var old = GUI.backgroundColor;
                    GUI.backgroundColor = Catalog.Paints[i];
                    GUI.enabled = d.money >= Catalog.PaintCost && sv.color != i;
                    if (GUILayout.Button(new GUIContent(sv.color == i ? "✓" : "", Catalog.PaintNames[i]), btn, GUILayout.Width(52)))
                    {
                        d.money -= Catalog.PaintCost; sv.color = i; SaveSystem.Save();
                        g.Toast("Boya: " + Catalog.PaintNames[i]);
                    }
                    GUI.enabled = true;
                    GUI.backgroundColor = old;
                }
                GUILayout.EndHorizontal();

                GUILayout.Space(6);
                GUILayout.Label("Performans Ayarı", small);
                TuneRow(g, "Motor", ref sv.engine, garageSel);
                TuneRow(g, "Nitro", ref sv.nitro, garageSel);
                TuneRow(g, "Yol Tutuş", ref sv.handling, garageSel);
            }

            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Garajdan Çık (E / Esc)", btn)) g.CloseMenu();
            GUILayout.EndArea();
        }

        void TuneRow(Game g, string name, ref int level, int carId)
        {
            var d = SaveSystem.Data;
            GUILayout.BeginHorizontal();
            string lv = "";
            for (int i = 0; i < Catalog.MaxTune; i++) lv += i < level ? "■" : "□";
            GUILayout.Label(name + "  " + lv, small, GUILayout.Width(260));
            if (level < Catalog.MaxTune)
            {
                int cost = Catalog.TuneCost(carId, level);
                GUI.enabled = d.money >= cost;
                if (GUILayout.Button("Yükselt " + U.Money(cost), btn))
                {
                    d.money -= cost; level++; SaveSystem.Save();
                    g.Toast(name + " seviye " + level + "!");
                }
                GUI.enabled = true;
            }
            else GUILayout.Label("MAKS", small);
            GUILayout.EndHorizontal();
        }
    }
}
