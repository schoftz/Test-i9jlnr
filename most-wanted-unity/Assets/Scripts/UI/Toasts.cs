using UnityEngine;

namespace MostWanted
{
    /// <summary>Üst ortada açılı, teal kenarlı bildirimler (en fazla 3, kayarak/solarak).</summary>
    public static class Toasts
    {
        const float Life = 4f;   // Game.Toast süresi

        public static void Draw(Game g)
        {
            if (g == null || g.toasts.Count == 0) return;
            var oldM = GUI.matrix; float oldA = UIKit.Alpha;
            UIKit.Begin();
            int n = g.toasts.Count, first = Mathf.Max(0, n - 3);
            float y = 150f;
            for (int i = n - 1; i >= first; i--)     // en yeni en üstte
            {
                float left = g.toastTimes[i], age = Life - left;
                float a = Mathf.Clamp01(Mathf.Min(age / 0.2f, left / 0.5f));
                float slide = (1f - Mathf.Clamp01(age / 0.2f)) * -24f;
                UIKit.Alpha = a;
                const float w = 820, h = 54;
                float x = (UIKit.DW - w) / 2f;
                UIKit.Panel(x, y + slide, w, h, 0.95f);
                UIKit.Fill(x + 14, y + slide + 10, 5, h - 20, UIKit.Teal);
                UIKit.Text(x + 30, y + slide, w - 60, h, g.toasts[i], 24, Color.white, TextAnchor.MiddleCenter);
                y += (h + 8) * a;
            }
            UIKit.Alpha = oldA;
            GUI.matrix = oldM;
        }
    }
}
