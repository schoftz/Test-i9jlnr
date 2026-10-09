// Randomation Vehicle Physics (RVP) — raycast wheel + suspension, ported and condensed.
// Original: https://github.com/JustInvoke/Randomation-Vehicle-Physics
// Copyright (c) 2021 Justin Couch — MIT License (see LICENSE.txt in this folder).
//
// Port of RVP Wheel.GetWheelContact / ApplyFriction / PositionWheel / RotateWheel and
// Suspension.ApplySuspensionForce, merged into one plain class driven by MostWanted.CarController
// (the original uses a VehicleParent + Suspension + Wheel + DriveForce component hierarchy that does
// not fit our runtime-built cars). Engine/gearbox are ours; RVP gets a per-wheel drive/brake
// acceleration request and resolves it through its friction curves.
using UnityEngine;

namespace RVP
{
    public class RvpWheel
    {
        // ---- setup ----
        public Transform anchor;         // top of suspension travel (RVP "maxCompressPoint")
        public Transform vis;            // visual wheel (pack model)
        public float radius = 0.34f;
        public float suspensionDistance = 0.18f;
        public float springForce = 4.9f;      // m/s² per unit of (1 - compression)  (RVP Suspension.springForce, Acceleration mode)
        public float springDampening = 0.5f;  // RVP default
        public float frictionSmoothness = 0.5f;
        public float sidewaysSlipDependence = 1.6f;  // RVP range 0–2: lateral grip is only reduced by real wheelspin/lock (→ 0.6 when fully spinning)
        public bool steered;
        public float extendSpeed = 20f;

        // ---- state ----
        public bool grounded;
        public RaycastHit hit;
        public float compression = 1f, travelDist = 1f, penetration;
        public float load;                // N, current suspension force (tyre normal load)
        public float steerDeg;
        public float sideSlip, fwdSlip;
        public Vector3 frictionForce;
        public float spinRpm;             // visual/engine wheel rpm
        float spinAngle;
        Quaternion visBase = Quaternion.identity;
        bool visInit;

        static readonly RaycastHit[] hits = new RaycastHit[8];

        /// <summary>RVP Wheel.GetWheelContact: RaycastAll along the spring direction, nearest hit that is not part of the vehicle.</summary>
        public void GetContact(Transform car, int mask)
        {
            float castDist = suspensionDistance + radius;
            Vector3 dir = -anchor.up;
            int n = Physics.RaycastNonAlloc(anchor.position, dir, hits, castDist, mask, QueryTriggerInteraction.Ignore);
            int best = -1; float bd = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                if (hits[i].collider == null || hits[i].transform.IsChildOf(car)) continue;
                if (hits[i].distance < bd) { bd = hits[i].distance; best = i; }
            }
            if (best >= 0)
            {
                hit = hits[best];
                grounded = true;
                float contactDist = hit.distance - radius;
                compression = suspensionDistance > 0f ? Mathf.Clamp01(contactDist / suspensionDistance) : 0f;
                penetration = Mathf.Min(0f, contactDist);
            }
            else
            {
                grounded = false;
                compression = 1f;
                penetration = 0f;
                load = 0f;
            }
        }

        /// <summary>RVP Suspension.ApplySuspensionForce (+ hard contact). Returns the applied normal load in N.</summary>
        public float ApplySuspension(Rigidbody rb, Vector3 groundNormal, float dt)
        {
            travelDist = compression < travelDist || grounded ? compression : Mathf.Lerp(travelDist, compression, extendSpeed * dt);
            if (!grounded) { load = 0f; return 0f; }
            Vector3 pv = rb.GetPointVelocity(anchor.position);
            Rigidbody gb = hit.collider.attachedRigidbody;
            if (gb != null) pv -= gb.GetPointVelocity(hit.point);
            float travelVel = Vector3.Dot(pv, groundNormal);
            float a = RvpTire.SpringAccel(springForce, springDampening, compression, travelVel);
            if (compression <= 0f) a += RvpTire.HardContactAccel(travelVel, penetration);
            a = Mathf.Max(0f, a);
            load = a * rb.mass;
            rb.AddForceAtPosition(groundNormal * load, hit.point);
            if (gb != null && !gb.isKinematic) gb.AddForceAtPosition(-groundNormal * load, hit.point);
            return load;
        }

        /// <summary>
        /// RVP Wheel.ApplyFriction. driveAccel/brakeAccel are the requested longitudinal accelerations for this wheel
        /// (m/s², positive = forward); mu = tyre friction coefficient; tcs/abs clamp the request below the peak.
        /// spinOverride ≥ 0 forces a forward slip (locked wheel / handbrake / burnout).
        /// </summary>
        public void ApplyFriction(Rigidbody rb, Transform car, float mu, float sideMul, float driveAccel, float brakeAccel, bool tcs, float spinOverride, float forceHeight, float dt)
        {
            if (!grounded || load <= 0f) { frictionForce = Vector3.zero; fwdSlip = sideSlip = 0f; spinRpm = Mathf.Lerp(spinRpm, 0f, dt * 2f); return; }
            Quaternion steerQ = Quaternion.AngleAxis(steerDeg, car.up);
            Vector3 n = hit.normal;
            Vector3 fwd = Vector3.ProjectOnPlane(steerQ * car.forward, n).normalized;
            Vector3 right = Vector3.Cross(n, fwd);
            Vector3 v = rb.GetPointVelocity(hit.point);
            Rigidbody gb = hit.collider.attachedRigidbody;
            if (gb != null) v -= gb.GetPointVelocity(hit.point);
            float vx = Vector3.Dot(v, fwd), vz = Vector3.Dot(v, right);

            float m = rb.mass;
            float limit = mu * load / m;                 // m/s² this tyre can transmit (per load)
            // ---- longitudinal request → forward slip (RVP: 0.01 * (rawRPM - currentRPM)) ----
            float req = driveAccel;
            float brk = Mathf.Abs(brakeAccel);
            if (brk > 0f) req -= Mathf.Sign(vx) * Mathf.Min(brk, Mathf.Abs(vx) / Mathf.Max(dt, 1e-4f));   // never reverse through braking
            float peak = Mathf.Max(0.01f, limit);
            if (tcs) req = Mathf.Clamp(req, -peak * 0.98f, peak * 0.98f);  // TCS / ABS: stay at or below the curve peak
            float ratio = Mathf.Abs(req) / peak;           // 1 = at the peak of the forward curve
            fwdSlip = spinOverride >= 0f ? spinOverride : ratio <= 1f ? ratio * 0.2013f : 0.2013f + (ratio - 1f) * 0.8f;
            float fx = Mathf.Sign(req) * Mathf.Min(Mathf.Abs(req), RvpTire.FwdCurve(fwdSlip) * peak);
            if (spinOverride >= 0f) fx = -Mathf.Sign(vx) * Mathf.Min(Mathf.Abs(vx) / Mathf.Max(dt, 1e-4f), RvpTire.FwdCurve(fwdSlip) * peak);
            // ---- lateral (RVP sidewaysSlip + slip dependence) ----
            sideSlip = RvpTire.SideSlip(vz);
            float fwdSlipForDep = fwdSlip / 0.2013f - 1f;  // 0 at the forward peak, 1 when fully spinning/locked
            float dep = RvpTire.Dependence(sidewaysSlipDependence, Mathf.Clamp01(fwdSlipForDep));
            float fz = -Mathf.Sign(vz) * RvpTire.SideCurve(sideSlip) * peak * sideMul * dep;
            // low-speed static hold so the car does not creep sideways on cambered roads
            if (Mathf.Abs(vx) < 1.5f) fz = Mathf.Clamp(-vz / Mathf.Max(dt, 1e-4f) * 0.5f, -peak, peak);
            Vector3 target = (fwd * fx + right * fz) * m;
            frictionForce = Vector3.Lerp(frictionForce, target, RvpTire.SmoothFactor(frictionSmoothness, dt));
            Vector3 at = hit.point + car.up * forceHeight;
            rb.AddForceAtPosition(frictionForce, at);
            if (gb != null && !gb.isKinematic) gb.AddForceAtPosition(-frictionForce, hit.point);

            float groundRpm = vx / (2f * Mathf.PI * radius) * 60f;
            float extra = spinOverride >= 0f ? -groundRpm : ratio > 1f ? (ratio - 1f) * 600f * Mathf.Sign(req) : 0f;
            spinRpm = groundRpm + extra;
        }

        /// <summary>RVP Wheel.PositionWheel + RotateWheel (visual only).</summary>
        public void UpdateVisual(Transform car, float dt)
        {
            if (vis == null) return;
            if (!visInit) { visBase = Quaternion.Inverse(car.rotation) * vis.rotation; visInit = true; }
            Vector3 p = anchor.position - anchor.up * (suspensionDistance * travelDist);
            spinAngle = Mathf.Repeat(spinAngle + spinRpm * 6f * dt, 360f);
            Quaternion q = car.rotation * Quaternion.AngleAxis(steerDeg, Vector3.up) * Quaternion.AngleAxis(spinAngle, Vector3.right) * visBase;
            vis.SetPositionAndRotation(p, q);
        }
    }
}
