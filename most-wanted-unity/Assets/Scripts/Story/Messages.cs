using System.Collections.Generic;
using UnityEngine;

namespace MostWanted
{
    /// <summary>
    /// Hikaye SMS sistemi: sol altta kayan kartlar (renkli baş harf rozeti, isim, metin), yumuşak bip,
    /// kalıcı kayıt (SaveData.story.msgs, son 50) — duraklatma menüsündeki MESAJLAR sayfası okur.
    /// </summary>
    public static class StoryMessages
    {
        public const int MaxLog = 50;
        const float Life = 7f;
        const char Sep = '\u001F';

        class Pop { public string who, text; public float t; }
        static readonly List<Pop> pops = new List<Pop>();
        static AudioSource src;

        public static void Send(string who, string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            try
            {
                var s = Save;
                if (s != null)
                {
                    s.msgs.Add(who + Sep + text);
                    while (s.msgs.Count > MaxLog) s.msgs.RemoveAt(0);
                    s.unread++;
                }
                pops.Add(new Pop { who = who, text = text, t = 0f });
                if (pops.Count > 3) pops.RemoveAt(0);
                Chime();
                SaveSystem.Save();
            }
            catch (System.Exception e) { Debug.LogWarning("[MW] SMS: " + e.Message); }
        }

        static StorySave Save { get { return SaveSystem.Data != null ? SaveSystem.Data.story : null; } }

        /// <summary>Kayıt (en yeni sonda).</summary>
        public static int Count { get { var s = Save; return s != null && s.msgs != null ? s.msgs.Count : 0; } }
        public static void Get(int i, out string who, out string text)
        {
            who = ""; text = "";
            var s = Save;
            if (s == null || s.msgs == null || i < 0 || i >= s.msgs.Count) return;
            string m = s.msgs[i];
            int k = m.IndexOf(Sep);
            if (k < 0) { text = m; return; }
            who = m.Substring(0, k); text = m.Substring(k + 1);
        }

        static void Chime()
        {
            if (src == null)
            {
                var go = new GameObject("SMS_Ses");
                Object.DontDestroyOnLoad(go);
                src = go.AddComponent<AudioSource>();
                src.playOnAwake = false; src.spatialBlend = 0f; src.ignoreListenerPause = true;
            }
            float v = 0.22f;
            try { v *= AudioBus.Get(AudioBus.Bus.Efekt); } catch { }
            src.pitch = 1.15f;
            src.PlayOneShot(AudioSynth.Beep(true), v);
        }

        public static void Tick(float udt)
        {
            for (int i = pops.Count - 1; i >= 0; i--) { pops[i].t += udt; if (pops[i].t > Life) pops.RemoveAt(i); }
        }

        public static void ClearPopups() { pops.Clear(); }

        /// <summary>Sol alt SMS kartları (HUD koordinatı: yükseklik 1080, genişlik W).</summary>
        public static void DrawPopups(float W, float H)
        {
            float y = H - 320f;   // mini harita sol altta: üstünde
            for (int i = pops.Count - 1; i >= 0; i--)
            {
                var p = pops[i];
                float a = Mathf.Clamp01(Mathf.Min(p.t / 0.25f, (Life - p.t) / 0.6f));
                float slide = (1f - Mathf.Clamp01(p.t / 0.25f)) * -60f;
                UIKit.Alpha = a;
                const float w = 520, h = 92;
                float x = 24 + slide;
                y -= h + 10;
                Card(x, y, w, h, p.who, p.text, true);
            }
            UIKit.Alpha = 1f;
        }

        public static void Card(float x, float y, float w, float h, string who, string text, bool header)
        {
            UIKit.Panel(x, y, w, h, 0.95f);
            Color c = StoryData.CharColor(who);
            UIKit.Fill(x + 14, y + 16, 54, 54, new Color(c.r * 0.35f, c.g * 0.35f, c.b * 0.35f, 0.95f));
            UIKit.Fill(x + 14, y + 66, 54, 4, c);
            string ini = string.IsNullOrEmpty(who) ? "?" : who.Substring(0, 1).ToUpper();
            UIKit.Text(x + 14, y + 16, 54, 54, ini, 30, Color.white, TextAnchor.MiddleCenter);
            UIKit.Text(x + 82, y + 8, w - 100, 26, (header ? "SMS  •  " : "") + who, 17, UIKit.Teal, TextAnchor.MiddleLeft, false, false);
            UIKit.Text(x + 82, y + 32, w - 100, h - 38, text, 19, Color.white, TextAnchor.UpperLeft, false, true, true);
        }
    }
}
