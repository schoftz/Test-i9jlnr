// Randomation Vehicle Physics (RVP) — tire / steering math, ported and condensed.
// Original: https://github.com/JustInvoke/Randomation-Vehicle-Physics
// Copyright (c) 2021 Justin Couch — MIT License (see LICENSE.txt in this folder).
//
// Ported from RVP Wheel.cs (GetSlip / ApplyFriction), Suspension.cs (ApplySuspensionForce)
// and SteeringControl.cs (steerCurve). Pure C# (no UnityEngine) so the headless sims in
// Tools/physics_sim can use exactly the same functions as the game.
//
// Changes vs. the original:
//  * AnimationCurves replaced with fixed key tables taken from the RVP "Car RWD" prefab
//    (normalised so the peak is 1.0; the sideways curve starts at 0 instead of 0.30 so the force is
//    continuous through zero slip at low speed).
//  * Friction magnitudes are expressed per unit of tyre load (μ), so the caller multiplies by the
//    wheel's normal load instead of RVP's fixed per-wheel acceleration.
using System;

namespace RVP
{
    public static class RvpTire
    {
        // RVP Car RWD prefab: sidewaysFrictionCurve (0,0.304) (0.193,1.999) (1,1.052) → normalised
        static readonly float[] SideT = { 0f, 0.0965f, 0.1932f, 0.45f, 1f };
        static readonly float[] SideV = { 0f, 0.80f, 1.0f, 0.97f, 0.90f };   // arcade: düz kuyruk (RVP 0.80/0.526) — aşırı direksiyonda ön kaymaz
        // RVP forwardFrictionCurve (0,0.307) (0.201,0.998) (0.396,0.859) (1,0.802)
        static readonly float[] FwdT = { 0f, 0.1f, 0.2013f, 0.3962f, 1f };
        static readonly float[] FwdV = { 0f, 0.78f, 1.0f, 0.861f, 0.803f };

        /// <summary>RVP Wheel.GetSlip: sidewaysSlip = lateral contact velocity (m/s) * 0.1 / sidewaysCurveStretch.</summary>
        public static float SideSlip(float lateralVel, float curveStretch = 1f) { return lateralVel * 0.1f / curveStretch; }

        public static float SideCurve(float slip) { return Eval(SideT, SideV, Math.Abs(slip)); }
        public static float FwdCurve(float slip) { return Eval(FwdT, FwdV, Math.Abs(slip)); }

        /// <summary>RVP slip dependence: other-axis slip reduces this axis' force
        /// (Mathf.Clamp01(dependence - Mathf.Clamp01(|otherSlip|))).</summary>
        public static float Dependence(float dependence, float otherSlip)
        {
            return Clamp01(dependence - Clamp01(Math.Abs(otherSlip)));
        }

        /// <summary>RVP frictionSmoothness: frictionForce = Lerp(frictionForce, target, 1 - smoothness).
        /// Here made time-step independent (reference step 1/50 s as in RVP's default physics rate).</summary>
        public static float SmoothFactor(float smoothness, float dt)
        {
            return 1f - (float)Math.Pow(smoothness, dt / 0.02f);
        }

        /// <summary>RVP SteeringControl.steerCurve, re-keyed for an arcade (NFS MW) feel:
        /// 0 km/h 100%, 50 → 75%, 100 → 55%, 150 → 38%, 200+ → 30% (original: Linear 0 m/s → 1, 30 m/s → 0.198).</summary>
        static readonly float[] SteerK = { 0f, 50f, 100f, 150f, 200f };
        static readonly float[] SteerV = { 1f, 0.75f, 0.55f, 0.38f, 0.30f };
        public static float SteerCurve(float speedMs)
        {
            float k = Math.Abs(speedMs) * 3.6f;
            for (int i = 1; i < SteerK.Length; i++)
                if (k <= SteerK[i]) return SteerV[i - 1] + (SteerV[i] - SteerV[i - 1]) * (k - SteerK[i - 1]) / (SteerK[i] - SteerK[i - 1]);
            return SteerV[SteerV.Length - 1];
        }

        /// <summary>Steering rate (deg/s) replacing RVP's lerp: full lock in 0.12 s, back to centre in 0.08 s (× sensitivity).</summary>
        public static float SteerRateDeg(float maxLock, bool returning, float sens)
        {
            return maxLock / (returning ? 0.08f : 0.12f) * sens;
        }

        /// <summary>Lateral grip multipliers (front, rear) over speed: +18% peak; front slightly stronger at low speed,
        /// rear ≥ front above 120 km/h for stability.</summary>
        public static void SideGrip(float kmh, out float front, out float rear)
        {
            float t = Clamp01((kmh - 80f) / 40f);
            front = 1.40f * (1.03f + (1.0f - 1.03f) * t);
            rear = 1.40f * (1.05f + (1.15f - 1.05f) * t);
        }

        /// <summary>RVP SteeringControl: steerAngle = Lerp(steerAngle, target, steerRate * timeFactor). steerRate 0.1 at 50 Hz.</summary>
        public static float SteerLerp(float dt) { return 1f - (float)Math.Pow(1f - 0.1f, dt / 0.02f); }

        /// <summary>RVP Suspension.ApplySuspensionForce (targetCompression = 1, linear spring curve, exponent 1):
        /// accel = springForce * ((1 - compression) - springDampening * Clamp(travelVel, -1, 1)).
        /// compression = 0 → fully compressed, 1 → fully extended.</summary>
        public static float SpringAccel(float springForce, float damping, float compression, float travelVel)
        {
            return springForce * ((1f - compression) - damping * Math.Max(-1f, Math.Min(1f, travelVel)));
        }

        /// <summary>RVP hard contact (when compression hits 0): -(Clamp(travelVel, -sensitivity, 0) + penetration) * hardContactForce.</summary>
        public static float HardContactAccel(float travelVel, float penetration, float sensitivity = 2f, float force = 50f)
        {
            return -(Math.Max(-sensitivity, Math.Min(0f, travelVel)) + penetration) * force;
        }

        /// <summary>RVP VehicleAssist.ApplySpinAssist (non auto-steer branch): yaw-rate target from steering,
        /// applied only in proportion to how much the car is sliding (driftSpinCurve over |lateral velocity|).
        /// Returns yaw angular acceleration (rad/s²).</summary>
        /// Modification: the yaw target is capped to what the tyres can hold (μ·g / v) and to the kinematic
        /// yaw of the current steer angle, so in normal mode it acts as stability control instead of a spin inducer.
        public static float SpinAssist(float steerInput, float fwdVel, float lateralVel, float yawRate, float spinSpeed, float spinAssist, float steerDeg, float wheelBase, float mu)
        {
            float v = Math.Abs(fwdVel);
            float cap = 1.25f * Math.Min(mu * 9.81f / Math.Max(v, 3f), v * (float)Math.Tan(Math.Abs(steerDeg) * Math.PI / 180.0) / Math.Max(1.5f, wheelBase));   // +25% pay: dönüşe girişi asla engellemez
            float target = steerInput * spinSpeed * (fwdVel < 0f ? -1f : 1f);
            if (target > cap) target = cap; if (target < -cap) target = -cap;
            // aşırı savrulma (oversteer) denetimi: savrulma hızı tutuş sınırını aşınca ya da gövde kayma açısı 10°'yi
            // geçip arka dışarı kaçınca güçlü düzeltme (dönüşe girişe karışmaz — yalnızca gerçek oversteer'de)
            float beta = v > 3f ? (float)(Math.Atan2(lateralVel, Math.Abs(fwdVel)) * 180.0 / Math.PI) : 0f;
            bool tailOut = Math.Abs(beta) > 10f && Math.Sign(beta) == -Math.Sign(yawRate);
            float corr = 0f;
            if (Math.Abs(yawRate) > cap) corr += (Math.Abs(yawRate) - cap) * 6f;
            if (tailOut) corr += (Math.Abs(beta) - 10f) * 0.12f;
            if (corr > 0f) return -corr * Math.Sign(yawRate) * Math.Min(1f, spinAssist);
            float curve = Clamp01(Math.Abs(lateralVel) / 10f);   // driftSpinCurve = Linear(0,0,10,1)
            return (target - yawRate) * spinAssist * curve;
        }

        /// <summary>RVP VehicleAssist.ApplyDriftPush: keeps speed while sliding under throttle (m/s², along velocity).</summary>
        public static float DriftPush(float accelInput, float lateralVel, float push, float rightVelDot)
        {
            return Math.Abs(accelInput * Math.Abs(lateralVel) * push * (1f - Math.Abs(rightVelDot)));
        }

        static float Eval(float[] t, float[] v, float x)
        {
            if (x <= t[0]) return v[0];
            for (int i = 1; i < t.Length; i++)
            {
                if (x <= t[i])
                {
                    float u = (x - t[i - 1]) / (t[i] - t[i - 1]);
                    if (i > 1) u = u * u * (3f - 2f * u);   // ilk parça doğrusal: sıfır kaymada sertlik ≠ 0
                    return v[i - 1] + (v[i] - v[i - 1]) * u;
                }
            }
            return v[v.Length - 1];
        }

        static float Clamp01(float x) { return x < 0f ? 0f : x > 1f ? 1f : x; }
    }
}
