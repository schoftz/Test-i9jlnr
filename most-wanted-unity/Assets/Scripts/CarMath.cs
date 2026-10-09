using System;

namespace MostWanted
{
    /// <summary>
    /// Unity'den bağımsız araç matematiği (headless fizik testinde de derlenir).
    /// Birimler: m, kg, s, N, Nm, rpm.
    /// </summary>
    public static class CarMath
    {
        public static readonly float[] Ratios = { 3.10f, 2.05f, 1.52f, 1.20f, 0.98f, 0.82f };
        public const float ReverseRatio = 3.0f;
        public const float IdleRpm = 950f;
        public const float Efficiency = 0.85f;
        public const float Gravity = 9.81f;

        /// <summary>Normalize tork eğrisi, x = rpm/redline.</summary>
        public static float TorqueCurve(float x)
        {
            if (x < 0f) x = 0f;
            if (x > 1.02f) return 0f; // devir kesici
            float c = 0.55f + 1.15f * x - 0.75f * x * x;
            return c < 0.3f ? 0.3f : c;
        }

        public static float WheelRpm(float speedMs, float radius)
        {
            return speedMs / (2f * (float)Math.PI * radius) * 60f;
        }

        /// <summary>Son vites redline'da azami hıza ulaşacak şekilde diferansiyel oranı.</summary>
        public static float FinalDrive(float topSpeedKmh, float redline, float radius)
        {
            float wr = WheelRpm(topSpeedKmh / 3.6f * 1.02f, radius);
            return redline * 0.97f / (wr * Ratios[Ratios.Length - 1]);
        }

        public static float EngineRpm(float speedMs, int gear, float finalDrive, float radius)
        {
            float r = gear <= 0 ? ReverseRatio : Ratios[gear - 1];
            return WheelRpm(Math.Abs(speedMs), radius) * r * finalDrive;
        }

        public static float WheelForce(float engineTorque, int gear, float finalDrive, float radius)
        {
            float r = gear <= 0 ? ReverseRatio : Ratios[gear - 1];
            return engineTorque * r * finalDrive * Efficiency / radius;
        }

        /// <summary>Azami hızda itiş = sürükleme olacak şekilde aerodinamik katsayı (F = k v²).</summary>
        public static float DragCoef(float peakTorque, float topSpeedKmh, float redline, float radius)
        {
            float fd = FinalDrive(topSpeedKmh, redline, radius);
            float v = topSpeedKmh / 3.6f * 1.05f; // asimptot: gerçek tepe hıza ulaşılabilsin
            float x = EngineRpm(v, Ratios.Length, fd, radius) / redline;
            float f = WheelForce(peakTorque * TorqueCurve(x), Ratios.Length, fd, radius);
            return f / (v * v);
        }

        /// <summary>Yuvarlanma direnci ivmesi (m/s², sabit).</summary>
        public const float RollingDecel = 0.15f;

        /// <summary>Hıza duyarlı direksiyon: ~32° (dur) → ~7° (150 km/sa).</summary>
        public static float SteerLimit(float kmh, float maxSteer)
        {
            float t = kmh / 150f; if (t > 1f) t = 1f; if (t < 0f) t = 0f;
            float s = t * t * (3f - 2f * t);
            float lo = 7f * maxSteer / 32f;
            float v = maxSteer + (lo - maxSteer) * s;
            if (kmh > 150f) v = Math.Max(lo * 0.8f, lo - (kmh - 150f) * 0.01f);
            return v;
        }

        /// <summary>
        /// Devrilme momenti kontrolü: yanal kuvvet kuvvet uygulama noktasından (yer + forceAppDist) etki eder.
        /// Dönen değer: iç tekerleğin yükü / statik yük (0 altı = teker kalkar).
        /// </summary>
        public static float InnerWheelLoadRatio(float mass, float latAccel, float comHeight, float forceAppHeight, float track, float antiRollTransfer)
        {
            float latForce = mass * latAccel;
            float rollMoment = latForce * (comHeight - forceAppHeight);
            float transfer = rollMoment / track;                  // N, iç→dış
            float stat = mass * Gravity * 0.5f;                   // bir taraf statik yük
            return (stat - transfer * (1f - antiRollTransfer)) / stat;
        }

        public static int Clamp(int v, int a, int b) { return v < a ? a : v > b ? b : v; }
    }
}
