using System;

namespace MostWanted
{
    /// <summary>
    /// "MW Sürüş": basit, deterministik arcade direksiyon modeli (oyuncu varsayılanı). Lastik yanal kuvvetleri yerine:
    ///  • savrulma hızı doğrudan hedeflenir: ω* = girdi × MaxYaw(hız) × hassasiyet, ivme sınırlı (ani dönüş yok),
    ///  • yanal hız üstel olarak sönümlenir (tutuş), sönen yanal hızın bir kısmı ileri hıza eklenir (virajda hız kaybı az),
    ///  • el freni: tutuş düşer + savrulma artar → kontrollü drift.
    /// Saf C# (UnityEngine yok) — Tools/physics_sim/MwSim.cs aynı fonksiyonları kullanır.
    /// </summary>
    public static class MwDrive
    {
        // azami savrulma hızı tablosu (km/s → °/s); düzenlenebilir
        public static float[] YawK = { 0f, 10f, 30f, 60f, 100f, 120f, 160f, 220f };
        public static float[] YawV = { 0f, 70f, 95f, 80f, 58f, 48f, 38f, 28f };

        public const float TurnInAccel = 300f;   // °/s² (girdi yönünde)
        public const float ReleaseAccel = 450f;  // °/s² (bırakma / ters)
        public const float BrakeTurnBonus = 0.15f, HandbrakeBonus = 0.35f;
        public const float ForwardRecover = 0.85f;    // sönen yanal hızın ileri yöne döndürülen oranı (hız korunur)
        public const float TurnSpeedLoss = 0.025f;    // |ω|·v başına hız kaybı (90° dönüşte ≈ %4)

        public static float MaxYawDeg(float kmh)
        {
            kmh = Math.Abs(kmh);
            if (kmh <= YawK[0]) return YawV[0];
            for (int i = 1; i < YawK.Length; i++)
                if (kmh <= YawK[i]) return YawV[i - 1] + (YawV[i] - YawV[i - 1]) * (kmh - YawK[i - 1]) / (YawK[i] - YawK[i - 1]);
            return YawV[YawV.Length - 1];
        }

        /// <summary>Hedef savrulma hızı (rad/s, + = sağa). fwdMs: ileri hız (geri viteste ters döner).</summary>
        public static float TargetYaw(float steerInput, float fwdMs, float sensitivity, bool braking, float handbrake01)
        {
            float k = MaxYawDeg(fwdMs * 3.6f) * sensitivity;
            if (braking && Math.Abs(fwdMs) > 8f) k *= 1f + BrakeTurnBonus;
            k *= 1f + HandbrakeBonus * handbrake01;
            return steerInput * k * (float)(Math.PI / 180.0) * (fwdMs < -0.5f ? -1f : 1f);
        }

        /// <summary>Savrulma hızını hedefe ivme sınırıyla yaklaştır (rad/s).</summary>
        public static float StepYaw(float current, float target, float dt)
        {
            bool turnIn = Math.Abs(target) > Math.Abs(current) && Math.Sign(target) == Math.Sign(current == 0f ? target : current);
            float lim = (turnIn ? TurnInAccel : ReleaseAccel) * (float)(Math.PI / 180.0) * dt;
            float d = target - current;
            if (d > lim) d = lim; if (d < -lim) d = -lim;
            return current + d;
        }

        /// <summary>Yanal tutuş (1/s): normal 9, çok yüksek hızda 6, el freninde 1.8.</summary>
        public static float Grip(float kmh, float handbrake01)
        {
            float t = Math.Max(0f, Math.Min(1f, (Math.Abs(kmh) - 120f) / 100f));
            float g = 9f + (6f - 9f) * t;
            return g + (1.8f - g) * handbrake01;
        }

        /// <summary>Yanal/ileri hız güncellemesi (araç ekseni). Döner: yeni (yanal, ileri).</summary>
        public static void StepVelocity(ref float lat, ref float fwd, float yawRate, float kmh, float handbrake01, float dt)
        {
            float g = Grip(kmh, handbrake01);
            float newLat = lat * (float)Math.Exp(-g * dt);
            float sgn = fwd >= 0f ? 1f : -1f;
            float speed = (float)Math.Sqrt(lat * lat + fwd * fwd);
            // sönen yanal hız ileri yöne "döndürülür" (hız büyüklüğü korunur, enerji üretilmez); ForwardRecover kadarı
            float fwdKeep = (float)Math.Sqrt(Math.Max(0f, speed * speed - newLat * newLat));
            float rec = ForwardRecover * (1f - handbrake01 * 0.6f);
            float af = Math.Abs(fwd);
            if (fwdKeep > af) af += (fwdKeep - af) * rec;
            af -= Math.Min(af, Math.Abs(yawRate) * speed * TurnSpeedLoss * dt);
            fwd = sgn * af;
            lat = newLat;
        }
    }
}
