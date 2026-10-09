using UnityEngine;

namespace MostWanted
{
    /// <summary>
    /// Hikaye arayüzü: sağ üstte para altında Carbon tarzı hedef satırı (+ mesafe), sol altta SMS kartları,
    /// HİKAYE / SERBEST SÜRÜŞ seçim penceresi ve duraklatma menüsündeki HİKAYE / MESAJLAR sayfaları.
    /// </summary>
    public static class StoryHud
    {
        static float msgScroll;

        /// <summary>HUD koordinatı: yükseklik 1080, genişlik ekran oranına göre (HUD.cs ile aynı yerleşim).</summary>
        static float BeginHud()
        {
            UIKit.Begin();
            float sh = Mathf.Max(1f, Screen.height);
            UIKit.S = Mathf.Max(0.05f, sh / 1080f);
            UIKit.OX = 0f; UIKit.OY = 0f;
            return Mathf.Max(1f, Screen.width) / UIKit.S;
        }

        public static void Draw(StoryManager m)
        {
            var g = Game.I;
            if (g == null || g.player == null) return;
            var oldM = GUI.matrix; float oldA = UIKit.Alpha;
            if (StoryManager.ChoiceOpen && !Cutscene.Active) { GUI.depth = -30; DrawChoice(); }
            else if (g.menu == Game.Menu.None && !TitleScreen.Active && !Cutscene.Active)
            {
                GUI.depth = -5;
                float W = BeginHud();
                UIKit.Alpha = 1f;
                if (StoryManager.Enabled && !string.IsNullOrEmpty(Story.CurrentObjective) && !(g.race != null && g.race.Active)) DrawObjective(g, W);
                StoryMessages.DrawPopups(W, 1080f);
            }
            UIKit.Alpha = oldA;
            GUI.matrix = oldM;
        }

        static void DrawObjective(Game g, float W)
        {
            float y = g.delivery != null && g.delivery.Active ? 186f : 104f;
            string txt = Story.CurrentObjective;
            string dist = Story.HasTarget && Story.Distance >= 0f ? (Story.Distance >= 1000f ? (Story.Distance / 1000f).ToString("0.0") + " km" : Mathf.RoundToInt(Story.Distance) + " m") : "";
            float w = Mathf.Clamp(txt.Length * 11.5f + (dist.Length > 0 ? 110f : 0f) + 120f, 360f, 900f);
            float x = W - 20f - w;
            UIKit.Bar(x, y, w, 40, new Color(0.02f, 0.05f, 0.07f, 0.72f));
            UIKit.Fill(x + 10, y + 8, 4, 24, UIKit.Teal);
            UIKit.Text(x + 22, y, 90, 40, "HİKAYE", 15, UIKit.Teal, TextAnchor.MiddleLeft, true, false);
            float tw = w - 112f - (dist.Length > 0 ? 100f : 0f);
            UIKit.Text(x + 100, y, tw, 40, txt, 18, Color.white, TextAnchor.MiddleLeft, false, true);
            if (dist.Length > 0) UIKit.Text(x + w - 112, y, 96, 40, dist, 20, UIKit.Teal, TextAnchor.MiddleRight, true, true);
        }

        // ------------------------------------------------------------ mod seçimi
        static void DrawChoice()
        {
            UIKit.Begin();
            UIKit.Alpha = 1f;
            UIKit.Tex(UIKit.Full(), UIKit.White, new Color(0f, 0.01f, 0.02f, 0.72f));
            if (UIKit.VignetteTex != null) UIKit.Tex(UIKit.Full(), UIKit.VignetteTex, Color.white);
            float pw = 1120, ph = 470, px = (UIKit.DW - pw) / 2f, py = (UIKit.DH - ph) / 2f;
            UIKit.Panel(px, py, pw, ph, 0.96f);
            UIKit.Fill(px + 40, py + 40, 6, 40, UIKit.Teal);
            UIKit.Text(px + 60, py + 32, pw - 100, 56, "NASIL OYNAMAK İSTERSİN?", 38, Color.white);
            UIKit.Text(px + 60, py + 88, pw - 100, 30, "Seçimini sonra duraklatma menüsündeki HİKAYE sayfasından değiştirebilirsin.", 18, UIKit.Grey, TextAnchor.MiddleLeft, false, false);
            string[] t = { "HİKAYE MODU", "SERBEST SÜRÜŞ" };
            string[] d =
            {
                "M4'ün elinden alındı. Mini ile Kara Liste'ye tırman, Kartal'ı yen ve aracını geri al.",
                "Hikaye yok, sınır yok. Yarışlar, takipler ve Kara Liste her zaman açık."
            };
            bool clicked = Event.current != null && Event.current.type == EventType.MouseDown && Event.current.button == 0;
            for (int i = 0; i < 2; i++)
            {
                float bx = px + 50 + i * 520, by = py + 150, bw = 500, bh = 230;
                bool hov = UIKit.Hit(bx, by, bw, bh);
                if (hov) StoryManager.choiceSel = i;
                bool sel = StoryManager.choiceSel == i;
                float a = UIKit.Anim("story_choice" + i, sel ? 1f : 0f, 10f);
                UIKit.Bar(bx, by, bw, bh, new Color(0.02f, 0.05f, 0.07f, 0.85f));
                if (a > 0.01f) UIKit.Bar(bx, by, bw * (0.2f + 0.8f * a), 70, new Color(UIKit.Teal.r, UIKit.Teal.g, UIKit.Teal.b, a));
                UIKit.Text(bx + 30, by + 6, bw - 60, 60, t[i], 30, a > 0.5f ? UIKit.Dark : Color.white, TextAnchor.MiddleLeft, true, a <= 0.5f);
                UIKit.Text(bx + 30, by + 86, bw - 60, 130, d[i], 20, new Color(1, 1, 1, 0.85f), TextAnchor.UpperLeft, false, false, true);
                if (sel) UIKit.Fill(bx + 20, by + bh - 6, bw - 40, 3, UIKit.Teal);
                if (clicked && hov) { Event.current.Use(); StoryManager.Choose(i == 0); return; }
            }
            float hx = px + 50, hy = py + ph - 62;
            hx += UIKit.KeyCap(hx, hy, "←→") + 8; UIKit.Text(hx, hy, 100, 36, "Seç", 18, UIKit.Grey, TextAnchor.MiddleLeft, false, false); hx += 80;
            hx += UIKit.KeyCap(hx, hy, "ENTER") + 8; UIKit.Text(hx, hy, 160, 36, "Onayla", 18, UIKit.Grey, TextAnchor.MiddleLeft, false, false);
        }

        // ------------------------------------------------------------ duraklatma menüsü: HİKAYE
        /// <summary>PauseMenu sayfa paneli içine çizer (tasarım koordinatı; x = panel solu, y = içerik üstü).</summary>
        public static void DrawStoryPage(Game g, float x, float y, float w, float h)
        {
            var s = StoryManager.S;
            if (s == null) return;
            bool on = StoryManager.Enabled;
            int cur = s.chapter;
            // sol: bölümler
            float lx = x + 50, ly = y;
            string[] names = new string[StoryData.Chapters.Length + 1];
            for (int c = 0; c < StoryData.Chapters.Length; c++) names[c] = StoryData.Chapters[c].title + "  —  " + StoryData.Chapters[c].sub
                + (StoryData.Chapters[c].rival >= 0 && c > 0 ? "  (" + StoryData.Rivals[StoryData.Chapters[c].rival].nick + ")" : "");
            names[StoryData.Chapters.Length] = "FİNAL  —  Otoyol ve Son Takip";
            for (int c = 0; c < names.Length; c++)
            {
                bool done = cur > c, now = cur == c && cur < StoryManager.Done;
                Color col = done ? UIKit.Green : now ? UIKit.Teal : new Color(1, 1, 1, 0.35f);
                if (now) UIKit.Bar(lx - 10, ly, 600, 46, new Color(UIKit.Teal.r, UIKit.Teal.g, UIKit.Teal.b, 0.16f));
                UIKit.Text(lx, ly, 40, 46, done ? "✓" : now ? "▶" : "•", 22, col, TextAnchor.MiddleCenter, false, false);
                UIKit.Text(lx + 44, ly, 540, 46, names[c], 21, done || now ? Color.white : new Color(1, 1, 1, 0.45f), TextAnchor.MiddleLeft, true, false);
                ly += 52;
            }
            ly += 10;
            UIKit.Fill(lx, ly, 580, 1, new Color(1, 1, 1, 0.12f));
            ly += 12;
            string obj = !s.offered || !s.enabled ? "Hikaye kapalı — serbest sürüş." : cur >= StoryManager.Done ? "Hikaye tamamlandı. Şehir senin!" : (string.IsNullOrEmpty(Story.CurrentObjective) ? "-" : Story.CurrentObjective);
            UIKit.Text(lx, ly, 120, 30, "HEDEF", 16, UIKit.Grey, TextAnchor.MiddleLeft, true, false);
            UIKit.Text(lx, ly + 28, 600, 70, obj, 20, Color.white, TextAnchor.UpperLeft, false, false, true);

            // butonlar
            float by = y + h - 90;
            if (!on)
            {
                if (UIKit.Button("st_start", lx, by, 280, 54, s.chapter > 0 && s.chapter < StoryManager.Done ? "HİKAYEYE DEVAM" : "HİKAYEYİ BAŞLAT")) StoryManager.StartStory(false);
            }
            else if (UIKit.Button("st_pause", lx, by, 280, 54, "SERBEST SÜRÜŞ")) { StoryManager.PauseStory(); g.Toast("Hikaye duraklatıldı. Buradan devam edebilirsin."); }
            if (UIKit.Button("st_restart", lx + 300, by, 280, 54, "BAŞTAN BAŞLA", true, UIKit.Danger)) StoryManager.StartStory(true);

            // sağ: ara sahneler
            float rx = x + 700, ry = y;
            UIKit.Text(rx, ry, 480, 36, "ARA SAHNELER", 20, UIKit.Teal, TextAnchor.MiddleLeft, true, false);
            ry += 44;
            var all = StoryData.AllCutscenes();
            int n = 0;
            foreach (var kv in all)
            {
                bool unlocked = cur > kv.Key || (cur == kv.Key && kv.Key == 0 && s.step > 0) || cur >= StoryManager.Done;
                float bx = rx + (n % 2) * 250, byy = ry + (n / 2) * 60;
                string label = kv.Value.title.Replace("Bölüm ", "B").Replace("Prolog — ", "P: ").Replace("Final — ", "F: ");
                if (label.Length > 22) label = label.Substring(0, 21) + "…";
                if (UIKit.Button("st_cs" + n, bx, byy, 236, 50, unlocked ? label : "KİLİTLİ", unlocked)) StoryManager.Replay(kv.Value);
                n++;
            }
            UIKit.Text(rx, ry + ((n + 1) / 2) * 60 + 6, 500, 30, "İzlemek için tıkla (oyun sırasında açılır).", 15, UIKit.Grey, TextAnchor.MiddleLeft, false, false);
        }

        // ------------------------------------------------------------ duraklatma menüsü: MESAJLAR
        public static void DrawMessagesPage(Game g, float x, float y, float w, float h)
        {
            var s = StoryManager.S;
            if (s != null) s.unread = 0;
            int n = StoryMessages.Count;
            if (n == 0) { UIKit.Text(x + 60, y, w - 120, 60, "Henüz mesaj yok.", 24, UIKit.Grey, TextAnchor.MiddleLeft, true, false); return; }
            if (Event.current != null && Event.current.type == EventType.ScrollWheel) { msgScroll += Event.current.delta.y * 40f; Event.current.Use(); }
            const float ch = 96, gap = 10;
            float total = n * (ch + gap);
            msgScroll = Mathf.Clamp(msgScroll, 0f, Mathf.Max(0f, total - h + 20f));
            float cy = y - msgScroll;
            for (int i = n - 1; i >= 0; i--)   // en yeni üstte
            {
                if (cy + ch >= y - 4 && cy <= y + h - ch + 4)
                {
                    string who, text;
                    StoryMessages.Get(i, out who, out text);
                    StoryMessages.Card(x + 60, cy, w - 120, ch, who, text, false);
                }
                cy += ch + gap;
            }
            UIKit.Text(x + 60, y + h, w - 120, 30, n + " / " + StoryMessages.MaxLog + " mesaj  •  fare tekerleği ile kaydır", 15, UIKit.Grey, TextAnchor.MiddleRight, false, false);
        }
    }
}
