using System;

namespace MostWanted
{
    /// <summary>
    /// Unity'den bağımsız araç matematiği (headless fizik testinde de derlenir).
    /// Birimler: m, kg, s, N, Nm, rpm.
    /// </summary>
    public static class CarMath
    {
        public static readonly float[] Ratios = { 3.45f, 2.15f, 1.56f, 1.21f, 0.98f, 0.82f };
        public const float ReverseRatio = 3.0f;
        public const float IdleRpm = 950f;
        public const float Efficiency = 0.85f;
        public const float Gravity = 9.81f;

        /// <summary>Normalize tork eğrisi, x = rpm/redline.</summary>
        public static float TorqueCurve(float x)
        {
            if (x < 0f) x = 0f;
            if (x > 1.02f) return 0f; // devir kesici
            // güçlü alt devir (yokuş kalkışları için), tepe ~%70 devirde
            float c = 0.72f + 0.8f * x - 0.57f * x * x;
            return c < 0.45f ? 0.45f : c;
        }

        /// <summary>Vites sayısına göre oranlar (geometrik dizi). 1 vites = elektrikli (tek oran).</summary>
        public static float[] MakeRatios(int gears)
        {
            if (gears <= 1) return new[] { 1f };
            gears = Math.Min(10, gears);
            float g1 = gears >= 8 ? 4.4f : 3.45f, top = gears >= 7 ? 0.68f : 0.82f;
            var r = new float[gears];
            for (int i = 0; i < gears; i++) r[i] = g1 * (float)Math.Pow(top / g1, i / (double)(gears - 1));
            return r;
        }

        static float[] R(float[] ratios) { return ratios ?? Ratios; }

        public static float WheelRpm(float speedMs, float radius)
        {
            return speedMs / (2f * (float)Math.PI * radius) * 60f;
        }

        /// <summary>Son vites redline'da azami hıza ulaşacak şekilde diferansiyel oranı.</summary>
        public static float FinalDrive(float topSpeedKmh, float redline, float radius, float[] ratios = null)
        {
            var rt = R(ratios);
            float wr = WheelRpm(topSpeedKmh / 3.6f * 1.02f, radius);
            return redline * 0.97f / (wr * rt[rt.Length - 1]);
        }

        public static float GearRatio(int gear, float[] ratios = null)
        {
            var rt = R(ratios);
            return gear <= 0 ? (rt.Length == 1 ? rt[0] : ReverseRatio) : rt[Math.Min(gear, rt.Length) - 1];
        }

        public static float EngineRpm(float speedMs, int gear, float finalDrive, float radius, float[] ratios = null)
        {
            return WheelRpm(Math.Abs(speedMs), radius) * GearRatio(gear, ratios) * finalDrive;
        }

        public static float WheelForce(float engineTorque, int gear, float finalDrive, float radius, float[] ratios = null)
        {
            return engineTorque * GearRatio(gear, ratios) * finalDrive * Efficiency / radius;
        }

        /// <summary>Azami hızda itiş = sürükleme olacak şekilde aerodinamik katsayı (F = k v²).</summary>
        public static float DragCoef(float peakTorque, float topSpeedKmh, float redline, float radius, float[] ratios = null)
        {
            var rt = R(ratios);
            float fd = FinalDrive(topSpeedKmh, redline, radius, rt);
            float v = topSpeedKmh / 3.6f * 1.05f; // asimptot: gerçek tepe hıza ulaşılabilsin
            float x = EngineRpm(v, rt.Length, fd, radius, rt) / redline;
            float f = WheelForce(peakTorque * TorqueCurve(x), rt.Length, fd, radius, rt);
            return f / (v * v);
        }

        /// <summary>Kalkış yardımı: 30 km/sa altında debriyaj kaydırma tork çarpanı (1.4 → 1.0).</summary>
        public static float LaunchAssist(float kmh) { float t = kmh / 30f; if (t > 1f) t = 1f; if (t < 0f) t = 0f; return 1.4f - 0.4f * t; }

        /// <summary>Yokuş yardımı: 40 km/sa altında gazdayken eğim kuvvetinin bu oranı telafi edilir.</summary>
        public static float HillAssist(float kmh) { return kmh < 40f ? 0.6f * (1f - kmh / 40f * 0.5f) : 0f; }

        /// <summary>Yuvarlanma direnci ivmesi (m/s², sabit).</summary>
        public const float RollingDecel = 0.15f;

        /// <summary>
        /// Hıza duyarlı direksiyon (MW arcade): 38° (dur) → 16° (100 km/sa) → 9° (200 km/sa) → 7° (300).
        /// maxSteer farklıysa oranlanır.
        /// </summary>
        public static float SteerLimit(float kmh, float maxSteer)
        {
            float k = maxSteer / 38f;
            float v;
            if (kmh <= 100f) { float t = kmh / 100f; t = t * t * (3f - 2f * t); v = 38f + (16f - 38f) * t; }
            else if (kmh <= 200f) v = 16f + (9f - 16f) * ((kmh - 100f) / 100f);
            else v = Math.Max(7f, 9f - (kmh - 200f) * 0.02f);
            return v * k;
        }

        /// <summary>
        /// Yanal lastik sertliği: düşük hızda ön ≥ arka (keskin dönüş), yüksek hızda arka artar (kararlılık, spin yok).
        /// </summary>
        public static float SideStiffness(bool front, float kmh)
        {
            if (front) return 1.6f;
            float t = (kmh - 50f) / 90f; if (t < 0f) t = 0f; if (t > 1f) t = 1f;
            return 1.45f + (1.85f - 1.45f) * t;
        }

        /// <summary>Direksiyon dönüş hızı (°/sn): içeri ~0.15 sn'de tam kilit, geri dönüş daha hızlı.</summary>
        public static float SteerRate(bool returning) { return returning ? 380f : 260f; }

        /// <summary>
        /// Arcade dönüş yardımı (savrulma momenti, N·m). Direksiyon girdisi varken araç hedef savrulma hızının
        /// altındaysa (understeer) girdi yönünde moment EKLER; girdi yokken yalnızca aşırı savrulmayı sönümler.
        /// Hedef = v·tan(δ)/L, tutuşla sınırlı (μg/v).
        /// </summary>
        public static float YawAssistTorque(float fwdSpeed, float steerAngleDeg, float yawRate, float wheelBase, float inertiaY, float steerInput, float mu)
        {
            float v = Math.Abs(fwdSpeed);
            if (v < 1.5f) return 0f;
            float target = fwdSpeed * (float)Math.Tan(steerAngleDeg * Math.PI / 180.0) / Math.Max(1.5f, wheelBase);
            float cap = mu * Gravity / Math.Max(v, 3f);
            if (target > cap) target = cap; if (target < -cap) target = -cap;
            if (Math.Abs(steerInput) > 0.1f)
            {
                float sgn = Math.Sign(target);
                if (sgn != 0 && yawRate * sgn < target * sgn)
                {
                    float kIn = 3.2f * Math.Min(1f, v / 8f);
                    return (target - yawRate) * inertiaY * kIn;
                }
                return 0f;
            }
            float ex = yawRate - target;
            return Math.Abs(ex) > 0.15f ? -ex * inertiaY * 2.5f : 0f;
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
