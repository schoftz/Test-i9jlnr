// Arcade Car Physics — drift handling, ported and condensed.
// Original: https://github.com/Saarg/Arcade_Car_Physics (WheelVehicle.cs)
// Copyright (c) 2018 Saarg — MIT License (see LICENSE.md in this folder).
//
// Used only for the "Drift Araçları" setup (Supra, R34, R35, M4 with drift setup on). The car then
// runs on Unity WheelColliders with Saarg's friction curves; engine torque/gearbox/nitro stay ours.
// Changes vs. the original: inputs come from MostWanted.CarController instead of Input axes,
// the drift flag is "handbrake held, or already sliding under throttle", and the handbrake lowers
// the rear sideways stiffness (drift) instead of locking all four wheels.
using UnityEngine;

namespace VehicleBehaviour
{
    public static class ArcadeDrift
    {
        // WheelVehicle demo prefab (Car01): forward ext 0.4/1 asym 0.8/0.5, sideways ext 0.2/1 asym 0.5/0.75, stiffness 2
        public static WheelFrictionCurve Forward(float stiffness)
        {
            return new WheelFrictionCurve { extremumSlip = 0.4f, extremumValue = 1f, asymptoteSlip = 0.8f, asymptoteValue = 0.5f, stiffness = stiffness };
        }
        public static WheelFrictionCurve Sideways(float stiffness)
        {
            return new WheelFrictionCurve { extremumSlip = 0.2f, extremumValue = 1f, asymptoteSlip = 0.5f, asymptoteValue = 0.75f, stiffness = stiffness };
        }

        /// <summary>WheelVehicle: wheel.steerAngle = Lerp(wheel.steerAngle, steering, steerSpeed) — steerSpeed 0.2 per 50 Hz step.</summary>
        public static float Steer(float current, float target, float steerSpeed, float dt)
        {
            return Mathf.Lerp(current, target, 1f - Mathf.Pow(1f - steerSpeed, dt / 0.02f));
        }

        /// <summary>WheelVehicle drift block:
        /// driftForce = -right (flat) * mass * speed/7 * throttle * steering/steerAngle; driftTorque = up * 0.1 * steering/steerAngle (VelocityChange).</summary>
        public static void Apply(Rigidbody rb, Transform tr, float speedKmh, float throttle, float steering, float steerAngle, float intensity, float dt)
        {
            if (steerAngle <= 0f) return;
            Vector3 driftForce = -tr.right;
            driftForce.y = 0f;
            driftForce.Normalize();
            float s = steering / steerAngle;
            if (steering != 0f) driftForce *= rb.mass * speedKmh / 7f * throttle * s;
            else driftForce = Vector3.zero;
            Vector3 driftTorque = tr.up * 0.1f * s * (dt / 0.02f);   // VelocityChange per 50 Hz step → scaled to our step
            rb.AddForce(driftForce * intensity, ForceMode.Force);
            rb.AddTorque(driftTorque * intensity, ForceMode.VelocityChange);
        }

        /// <summary>WheelVehicle downforce: rb.AddForce(-up * speed * downforce).</summary>
        public static void Downforce(Rigidbody rb, Transform tr, float speedKmh, float downforce)
        {
            rb.AddForce(-tr.up * speedKmh * downforce * rb.mass / 1400f * 10f);   // demo car mass 1400 kg, scaled ×10 for our heavier sets
        }

        /// <summary>WheelVehicle boost: rb.AddForce(forward * boostForce) (demo: 5000 N on a 1400 kg car → scaled by mass).</summary>
        public static void Boost(Rigidbody rb, Transform tr, float power)
        {
            rb.AddForce(tr.forward * 5000f * rb.mass / 1400f * power);
        }
    }
}
