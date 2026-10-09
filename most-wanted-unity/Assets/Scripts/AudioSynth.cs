using UnityEngine;

namespace MostWanted
{
    /// <summary>Prosedürel ses klipleri (dış dosya yok).</summary>
    public static class AudioSynth
    {
        const int Rate = 44100;
        static AudioClip engine, siren, beep, beepHi;

        public static AudioClip Engine()
        {
            if (engine != null) return engine;
            int n = Rate; // 1 sn, 60 Hz tam döngü -> kesintisiz loop
            var d = new float[n];
            var rng = new System.Random(7);
            float f = 60f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float ph = t * f;
                float saw = 2f * (ph - Mathf.Floor(ph + 0.5f));
                float s = 0.45f * saw
                        + 0.30f * Mathf.Sin(2 * Mathf.PI * f * 2f * t)
                        + 0.18f * Mathf.Sin(2 * Mathf.PI * f * 0.5f * t)
                        + 0.10f * Mathf.Sin(2 * Mathf.PI * f * 3f * t)
                        + 0.04f * (float)(rng.NextDouble() * 2 - 1);
                // silindir ateşleme darbesi
                s *= 0.75f + 0.25f * Mathf.Abs(Mathf.Sin(Mathf.PI * f * 2f * t));
                d[i] = Mathf.Clamp(s * 0.8f, -1f, 1f);
            }
            engine = AudioClip.Create("Motor", n, 1, Rate, false);
            engine.SetData(d, 0);
            return engine;
        }

        public static AudioClip Siren()
        {
            if (siren != null) return siren;
            int n = Rate * 2;
            var d = new float[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                // 2 sn'lik yükselip alçalan "wail"
                float f = 700f + 500f * (0.5f - 0.5f * Mathf.Cos(Mathf.PI * t));
                phase += f / Rate;
                float sq = Mathf.Sin((float)(phase * 2 * System.Math.PI));
                d[i] = Mathf.Clamp(sq * 1.6f, -1f, 1f) * 0.5f;
            }
            siren = AudioClip.Create("Siren", n, 1, Rate, false);
            siren.SetData(d, 0);
            return siren;
        }

        public static AudioClip Beep(bool high)
        {
            if (high && beepHi != null) return beepHi;
            if (!high && beep != null) return beep;
            int n = Rate / 4;
            var d = new float[n];
            float f = high ? 1320f : 880f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate;
                float env = Mathf.Clamp01(1f - t / 0.25f);
                d[i] = Mathf.Sin(2 * Mathf.PI * f * t) * env * 0.5f;
            }
            var c = AudioClip.Create(high ? "BipYuksek" : "Bip", n, 1, Rate, false);
            c.SetData(d, 0);
            if (high) beepHi = c; else beep = c;
            return c;
        }
    }
}
