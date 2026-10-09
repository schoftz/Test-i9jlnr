using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MostWanted
{
    /// <summary>
    /// Arcade-sim araç fiziği (NFS MW 2005 hissi): otomatik/manuel şanzıman, tork eğrisi, devir kesici,
    /// nitro, el freni drift'i, güç ile savrulma (sürtünme çemberi), karşı direksiyon yardımı,
    /// devrilme önleme (alçak ağırlık merkezi, kuvvet uygulama noktası, viraj demiri, otomatik doğrultma).
    /// </summary>
    public class CarController : MonoBehaviour
    {
        // ---- Girdiler ----
        public float throttle, steer;
        public bool handbrake, nitroInput, locked;
        public bool isPlayer, manualGearbox;

        // ---- Tanım ----
        [System.NonSerialized] public CarEntry def;
        public int[] tune = new int[Catalog.TuneCount];
        public float peakTorque, redline, topSpeed, finalDrive, dragK, grip = 1f, brakeTorque, downforceK, maxSteer = 32f;
        public float nitroCap = 1f, nitroPower = 1f, shiftTime = 0.28f, antiRoll, wheelRadius = 0.34f, wheelBase = 2.6f;
        public int drive = 1;
        public float stabilityAssist = 0.45f;
        public float comHeight = 0.42f;

        // ---- Durum ----
        public int gear = 1;
        public float rpm = CarMath.IdleRpm;
        public bool revLimiter;
        public float nitro = 1f;
        public bool nitroActive;
        public float health = 100f;
        public bool disabled;
        public float spikeTimer;
        public float gripBoost = 1f, steerBoost = 1f;   // Speedbreaker
        public float slipAngle, driftAmount;
        public float lastShiftQuality;
        public bool braking;

        public Rigidbody rb;
        public WheelCollider[] wheels = new WheelCollider[4]; // FL FR RL RR
        public Transform[] wheelVis = new Transform[4];
        public List<Material> paintMats = new List<Material>();
        public List<Material> brakeMats = new List<Material>();
        public List<Material> headMats = new List<Material>();
        public List<Transform> flames = new List<Transform>();
        public ParticleSystem[] smoke = new ParticleSystem[2];

        [System.NonSerialized] public System.Action<Collision> onHit;
        [System.NonSerialized] public System.Action<int, int> onShift; // vites, kalite (0 normal, 1 iyi, 2 mükemmel)

        float curSteer, uprightTimer, shiftTimer, limiterT;
        float[] sideStiff = { 1.4f, 1.4f, 1.35f, 1.35f };
        float handbrakeBlend;

        public bool TiresBlown { get { return spikeTimer > 0f; } }
        public bool Shifting { get { return shiftTimer > 0f; } }
        public float SpeedKmh { get { return U.Vel(rb).magnitude * 3.6f; } }
        public float ForwardSpeed { get { return Vector3.Dot(U.Vel(rb), transform.forward); } }
        public float Rpm01 { get { return Mathf.Clamp01(rpm / redline); } }

        public void Configure(CarEntry d, int[] t)
        {
            def = d;
            if (t != null && t.Length == Catalog.TuneCount) tune = t;
            float eng = 1f + 0.08f * tune[(int)Tune.Motor] + 0.06f * tune[(int)Tune.Turbo];
            peakTorque = d.torqueNm * eng;
            redline = d.redlineRpm;
            topSpeed = d.topSpeedKmh * (1f + 0.012f * (tune[(int)Tune.Turbo] + tune[(int)Tune.Sanziman] + tune[(int)Tune.Motor]));
            drive = Mathf.Clamp(d.drive, 0, 2);
            grip = d.grip * (1f + 0.045f * tune[(int)Tune.Lastik] + 0.02f * tune[(int)Tune.Suspansiyon]);
            shiftTime = 0.3f - 0.05f * tune[(int)Tune.Sanziman];
            nitroCap = 1f + 0.25f * tune[(int)Tune.Nitro];
            nitroPower = 1f + 0.1f * tune[(int)Tune.Nitro];
            float m = rb.mass;
            antiRoll = m * 9f * (1f + 0.15f * tune[(int)Tune.Suspansiyon]);
            brakeTorque = m * CarMath.Gravity * wheelRadius * 0.42f * (1f + 0.12f * tune[(int)Tune.Fren]);
            downforceK = 0.85f * d.downforce * (1f + 0.05f * tune[(int)Tune.Suspansiyon]);
            finalDrive = CarMath.FinalDrive(topSpeed, redline, wheelRadius);
            dragK = CarMath.DragCoef(peakTorque, topSpeed, redline, wheelRadius);

            // süspansiyon: ~1.7 Hz, %40 sönüm
            float corner = m / 4f;
            float freq = 1.7f + 0.1f * tune[(int)Tune.Suspansiyon];
            float k = corner * Mathf.Pow(2f * Mathf.PI * freq, 2f);
            float c = 2f * 0.42f * Mathf.Sqrt(k * corner);
            foreach (var w in wheels)
            {
                if (w == null) continue;
                w.suspensionSpring = new JointSpring { spring = k, damper = c, targetPosition = 0.5f };
            }
        }

        public void ShiftUp()
        {
            if (gear >= CarMath.Ratios.Length || Shifting || gear <= 0) return;
            float x = rpm / redline;
            int q = x >= 0.88f && x <= 0.98f ? 2 : x >= 0.76f ? 1 : 0;
            lastShiftQuality = q;
            gear++;
            shiftTimer = manualGearbox ? (q == 2 ? 0.08f : q == 1 ? 0.18f : 0.35f) : shiftTime;
            if (manualGearbox && q == 2) nitro = Mathf.Min(1f, nitro + 0.08f);
            if (onShift != null) onShift(gear, q);
        }

        public void ShiftDown()
        {
            if (gear <= 1 || Shifting) return;
            gear--;
            shiftTimer = shiftTime * 0.6f;
            if (onShift != null) onShift(gear, 0);
        }

        void FixedUpdate()
        {
            if (rb == null || rb.isKinematic) return;
            float dt = Time.fixedDeltaTime;
            Vector3 v = U.Vel(rb);
            float fwd = Vector3.Dot(v, transform.forward);
            float kmh = v.magnitude * 3.6f;
            if (spikeTimer > 0f) spikeTimer -= dt;
            if (shiftTimer > 0f) shiftTimer -= dt;

            if (locked || disabled)
            {
                for (int i = 0; i < 4; i++) { wheels[i].motorTorque = 0f; wheels[i].brakeTorque = brakeTorque * 3f; }
                // geri sayımda gaz -> devir (burnout hazırlığı)
                rpm = Mathf.Lerp(rpm, CarMath.IdleRpm + Mathf.Max(0f, throttle) * (redline * 0.85f - CarMath.IdleRpm), dt * 6f);
                nitroActive = false;
                UpdateEffects(kmh, 0f);
                return;
            }

            Vector3 lv = transform.InverseTransformDirection(v);
            slipAngle = kmh > 12f && lv.z > 0.5f ? Mathf.Atan2(lv.x, lv.z) * Mathf.Rad2Deg : 0f;
            driftAmount = kmh > 40f ? Mathf.Abs(slipAngle) : 0f;

            // ---- Direksiyon ----
            float limit = CarMath.SteerLimit(kmh, maxSteer) * steerBoost;
            if (handbrake) limit = Mathf.Max(limit, maxSteer * 0.6f);
            float target = steer * limit;
            // karşı direksiyon yardımı
            if (Mathf.Abs(slipAngle) > 4f) target += Mathf.Clamp(slipAngle * 0.55f, -maxSteer * 0.6f, maxSteer * 0.6f);
            target = Mathf.Clamp(target, -maxSteer, maxSteer);
            curSteer = Mathf.MoveTowards(curSteer, target, (90f + kmh * 0.4f) * dt);
            wheels[0].steerAngle = curSteer;
            wheels[1].steerAngle = curSteer;

            // ---- Şanzıman ----
            if (throttle < -0.05f && fwd < 1f && gear > 0) { gear = 0; shiftTimer = 0.2f; }
            if (throttle > 0.05f && gear == 0 && fwd > -1f) { gear = 1; shiftTimer = 0.15f; }
            float speedRpm = CarMath.EngineRpm(fwd, gear, finalDrive, wheelRadius);
            float wheelRpm = 0f; int nd = 0;
            for (int i = 0; i < 4; i++) if (IsDriven(i)) { wheelRpm += Mathf.Abs(wheels[i].rpm); nd++; }
            wheelRpm = nd > 0 ? wheelRpm / nd : 0f;
            float r = gear <= 0 ? CarMath.ReverseRatio : CarMath.Ratios[gear - 1];
            float spinRpm = wheelRpm * r * finalDrive;
            float target_rpm = Mathf.Max(speedRpm, Mathf.Min(spinRpm, redline * 1.05f));
            if (gear == 1 && kmh < 25f) target_rpm = Mathf.Max(target_rpm, CarMath.IdleRpm + Mathf.Abs(throttle) * redline * 0.55f); // debriyaj kaydırma
            target_rpm = Mathf.Max(CarMath.IdleRpm, target_rpm);
            rpm = Mathf.Lerp(rpm, Mathf.Min(target_rpm, redline * 1.02f), dt * 12f);

            if (!manualGearbox && gear > 0 && !Shifting)
            {
                if (rpm > redline * 0.96f && gear < CarMath.Ratios.Length && throttle > 0.1f && GroundedCount() >= 2) ShiftUp();
                else if (gear > 1 && rpm < redline * 0.45f) ShiftDown();
            }

            // ---- Tork ----
            float thAbs = Mathf.Abs(throttle);
            float wantDir = gear == 0 ? -1f : 1f;
            bool accelerating = (gear > 0 && throttle > 0.05f) || (gear == 0 && throttle < -0.05f);
            float engT = 0f;
            revLimiter = rpm >= redline * 0.995f;
            if (accelerating && !Shifting)
            {
                engT = peakTorque * CarMath.TorqueCurve(rpm / redline) * thAbs;
                if (revLimiter) { limiterT += dt; if (Mathf.Repeat(limiterT, 0.1f) < 0.05f) engT = 0f; }
                if (gear == 0 && kmh > 40f) engT = 0f;
            }
            float topNow = topSpeed * (TiresBlown ? 0.55f : 1f);

            nitroActive = nitroInput && nitro > 0.01f && throttle > 0.1f && gear > 0 && !handbrake;
            if (nitroActive)
            {
                nitro = Mathf.Max(0f, nitro - dt / (3.2f * nitroCap));
                engT *= 1f + 0.35f * nitroPower;
                topNow *= 1.12f;
            }

            float wheelT = engT * r * finalDrive * CarMath.Efficiency * wantDir;
            if (kmh > topNow) wheelT = 0f;
            float fShare = drive == 0 ? 0.5f : drive == 1 ? 0f : 0.2f;   // 4x4: %40 ön
            float rShare = drive == 0 ? 0f : drive == 1 ? 0.5f : 0.3f;
            wheels[0].motorTorque = wheels[1].motorTorque = wheelT * fShare;
            wheels[2].motorTorque = wheels[3].motorTorque = wheelT * rShare;

            // ---- Fren ----
            float brk = 0f;
            braking = false;
            if (gear > 0 && throttle < -0.05f && fwd > 1f) { brk = brakeTorque * -throttle; braking = true; }
            if (gear == 0 && throttle > 0.05f && fwd < -1f) { brk = brakeTorque * throttle; braking = true; }
            if (thAbs < 0.05f) brk = brakeTorque * 0.04f; // motor freni
            wheels[0].brakeTorque = wheels[1].brakeTorque = brk * 1.2f;
            wheels[2].brakeTorque = wheels[3].brakeTorque = brk * 0.8f;
            if (handbrake)
            {
                wheels[2].brakeTorque = wheels[3].brakeTorque = brakeTorque * 1.6f;
                wheels[2].motorTorque = wheels[3].motorTorque = 0f;
            }

            // ---- Sürtünme (el freni drift + sürtünme çemberi) ----
            handbrakeBlend = Mathf.MoveTowards(handbrakeBlend, handbrake ? 1f : 0f, dt * (handbrake ? 6f : 1.6f));
            float g = grip * gripBoost * (TiresBlown ? 0.5f : 1f);
            for (int i = 0; i < 4; i++)
            {
                float side = sideStiff[i] * g;
                if (i >= 2) side *= Mathf.Lerp(1f, 0.42f, handbrakeBlend);
                WheelHit hit;
                if (wheels[i].GetGroundHit(out hit))
                {
                    float fs = Mathf.Abs(hit.forwardSlip);
                    if (fs > 0.35f) side *= Mathf.Clamp(1f - (fs - 0.35f) * 0.9f, 0.45f, 1f); // patinajda yanal tutuş düşer -> güçle savrulma
                }
                var sf = wheels[i].sidewaysFriction; sf.stiffness = side; wheels[i].sidewaysFriction = sf;
                var ff = wheels[i].forwardFriction; ff.stiffness = 1.5f * g; wheels[i].forwardFriction = ff;
            }

            int grounded = GroundedCount();
            if (grounded > 0)
            {
                // downforce & aerodinamik sürükleme & yuvarlanma direnci
                rb.AddForce(-transform.up * downforceK * v.sqrMagnitude);
                Vector3 flat = U.Flat(v);
                rb.AddForce(-flat.normalized * dragK * flat.sqrMagnitude);
                if (flat.sqrMagnitude > 0.25f) rb.AddForce(-flat.normalized * rb.mass * CarMath.RollingDecel);
                if (nitroActive) rb.AddForce(transform.forward * rb.mass * 2.6f * nitroPower);

                // drift'te hız koru (arcade)
                if (Mathf.Abs(slipAngle) > 10f && throttle > 0.1f && kmh > 35f)
                    rb.AddForce(transform.forward * rb.mass * 2.2f * Mathf.Sin(Mathf.Abs(slipAngle) * Mathf.Deg2Rad));
                // el freninde dönüş yardımı
                if (handbrake && kmh > 25f) rb.AddTorque(Vector3.up * steer * rb.mass * 1.4f);

                // savrulma kararlılık yardımı (el freni yokken aşırı dönüşü sönümle)
                if (!handbrake && kmh > 20f)
                {
                    float desiredYaw = fwd * Mathf.Tan(curSteer * Mathf.Deg2Rad) / wheelBase;
                    float yaw = rb.angularVelocity.y;
                    float excess = yaw - desiredYaw;
                    if (Mathf.Abs(excess) > 0.25f)
                        rb.AddTorque(Vector3.up * -excess * rb.inertiaTensor.y * stabilityAssist * 4f);
                }
            }
            else
            {
                Vector3 av = rb.angularVelocity;
                rb.angularVelocity = new Vector3(av.x * 0.96f, av.y, av.z * 0.96f);
            }

            AntiRoll(0, 1);
            AntiRoll(2, 3);

            // aşırı yatmayı sınırla
            float roll = Vector3.SignedAngle(Vector3.ProjectOnPlane(Vector3.up, transform.forward), transform.up, transform.forward);
            if (Mathf.Abs(roll) > 18f && grounded < 4)
                rb.AddTorque(transform.forward * -Mathf.Sign(roll) * (Mathf.Abs(roll) - 18f) * rb.mass * 0.6f);

            // sert hız sınırı
            float hard = topNow / 3.6f * 1.08f;
            if (v.magnitude > hard) U.SetVel(rb, v.normalized * hard);

            // otomatik doğrultma: 60°'den fazla yatık 1.5 sn
            if (Vector3.Angle(transform.up, Vector3.up) > 60f)
            {
                uprightTimer += dt;
                if (uprightTimer > 1.5f) Unflip();
            }
            else uprightTimer = 0f;

            UpdateEffects(kmh, Mathf.Abs(slipAngle));
        }

        bool IsDriven(int i) { return drive == 2 || (drive == 0 ? i < 2 : i >= 2); }

        int GroundedCount()
        {
            int n = 0;
            for (int i = 0; i < 4; i++) if (wheels[i].isGrounded) n++;
            return n;
        }

        void AntiRoll(int l, int r)
        {
            WheelCollider wl = wheels[l], wr = wheels[r];
            WheelHit hit;
            float tl = 1f, tr = 1f;
            bool gl = wl.GetGroundHit(out hit);
            if (gl) tl = (-wl.transform.InverseTransformPoint(hit.point).y - wl.radius) / wl.suspensionDistance;
            bool gr = wr.GetGroundHit(out hit);
            if (gr) tr = (-wr.transform.InverseTransformPoint(hit.point).y - wr.radius) / wr.suspensionDistance;
            float force = (tl - tr) * antiRoll;
            if (gl) rb.AddForceAtPosition(wl.transform.up * -force, wl.transform.position);
            if (gr) rb.AddForceAtPosition(wr.transform.up * force, wr.transform.position);
        }

        public void Unflip()
        {
            uprightTimer = 0f;
            Vector3 f = U.Flat(transform.forward);
            if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
            Teleport(transform.position + Vector3.up * 1.2f, Quaternion.LookRotation(f.normalized, Vector3.up));
        }

        public void Teleport(Vector3 pos, Quaternion rot)
        {
            bool k = rb.isKinematic;
            rb.position = pos;
            rb.rotation = rot;
            transform.SetPositionAndRotation(pos, rot);
            if (!k) { U.SetVel(rb, Vector3.zero); rb.angularVelocity = Vector3.zero; }
            gear = 1; rpm = CarMath.IdleRpm;
        }

        public void SetSpeed(float kmh)
        {
            if (!rb.isKinematic) U.SetVel(rb, transform.forward * kmh / 3.6f);
        }

        void UpdateEffects(float kmh, float slip)
        {
            // nitro alevi
            for (int i = 0; i < flames.Count; i++)
            {
                var f = flames[i];
                if (f == null) continue;
                bool on = nitroActive;
                if (f.gameObject.activeSelf != on) f.gameObject.SetActive(on);
                if (on) f.localScale = new Vector3(0.13f, 0.13f, Random.Range(0.5f, 0.95f));
            }
            // lastik dumanı
            bool burn = (gear == 1 && throttle > 0.8f && kmh < 30f && peakTorque > 300f && GroundedCount() >= 2 && !locked);
            bool smokeOn = (slip > 14f && kmh > 30f) || (handbrake && kmh > 25f) || burn;
            for (int i = 0; i < smoke.Length; i++)
            {
                if (smoke[i] == null) continue;
                var em = smoke[i].emission;
                em.rateOverTime = smokeOn ? 40f : 0f;
            }
        }

        void LateUpdate()
        {
            for (int i = 0; i < 4; i++)
            {
                if (wheels[i] == null || wheelVis[i] == null) continue;
                Vector3 p; Quaternion q;
                wheels[i].GetWorldPose(out p, out q);
                wheelVis[i].SetPositionAndRotation(p, q);
            }
            float b = braking || handbrake ? 3f : 0.6f;
            foreach (var m in brakeMats) U.SetEmission(m, new Color(1f, 0.05f, 0.03f) * b);
        }

        void OnCollisionEnter(Collision c)
        {
            if (onHit != null) onHit(c);
        }

        public void Damage(float amount)
        {
            if (disabled) return;
            health -= amount;
            if (health <= 0f) { health = 0f; disabled = true; }
        }

        public void SetPaint(Color c)
        {
            foreach (var m in paintMats) U.SetColor(m, c);
        }

        public void SetHeadlights(bool on)
        {
            foreach (var m in headMats) U.SetEmission(m, on ? new Color(2.5f, 2.4f, 2.1f) : new Color(0.4f, 0.4f, 0.38f));
        }
    }

    /// <summary>Kırmızı/mavi tepe lambaları ve siren.</summary>
    public class PoliceLights : MonoBehaviour
    {
        public bool on;
        Material red, blue;
        Light lr, lb;
        AudioSource siren;
        float t;

        public void Setup(Transform vis, float roofY, bool hidden)
        {
            red = U.NewMat(new Color(0.4f, 0, 0)); blue = U.NewMat(new Color(0, 0, 0.4f));
            float y = hidden ? roofY - 0.35f : roofY + 0.08f;
            float z = hidden ? 0.4f : -0.1f;
            float w = hidden ? 0.25f : 0.5f;
            if (!hidden)
                U.Prim(PrimitiveType.Cube, "TepeLamba", vis, new Vector3(0, roofY + 0.02f, z), new Vector3(1.25f, 0.1f, 0.3f), U.Mat(new Color(0.15f, 0.15f, 0.15f))).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            U.Prim(PrimitiveType.Cube, "Kirmizi", vis, new Vector3(-0.32f, y, z), new Vector3(w, 0.12f, 0.26f), red).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            U.Prim(PrimitiveType.Cube, "Mavi", vis, new Vector3(0.32f, y, z), new Vector3(w, 0.12f, 0.26f), blue).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            lr = MakeLight(vis, new Vector3(-0.5f, roofY + 0.5f, 0), Color.red);
            lb = MakeLight(vis, new Vector3(0.5f, roofY + 0.5f, 0), new Color(0.1f, 0.3f, 1f));
            siren = gameObject.AddComponent<AudioSource>();
            siren.clip = AudioSynth.Siren();
            siren.loop = true;
            siren.spatialBlend = 1f;
            siren.minDistance = 12f;
            siren.maxDistance = 250f;
            siren.rolloffMode = AudioRolloffMode.Linear;
            siren.volume = 0.4f;
            siren.dopplerLevel = 0.5f;
        }

        Light MakeLight(Transform p, Vector3 lp, Color c)
        {
            var g = new GameObject("PolisIsik");
            g.transform.SetParent(p, false);
            g.transform.localPosition = lp;
            var l = g.AddComponent<Light>();
            l.type = LightType.Point; l.color = c; l.range = 16f; l.intensity = 4f; l.shadows = LightShadows.None;
            l.enabled = false;
            return l;
        }

        void Update()
        {
            if (red == null) return;
            var car = GetComponent<CarController>();
            bool active = on && (car == null || !car.disabled);
            if (active)
            {
                t += Time.deltaTime;
                bool phase = Mathf.Repeat(t, 0.5f) < 0.25f;
                bool strobe = Mathf.Repeat(t, 0.125f) < 0.08f;
                U.SetEmission(red, phase && strobe ? new Color(8f, 0, 0) : Color.black);
                U.SetEmission(blue, !phase && strobe ? new Color(0, 0.8f, 8f) : Color.black);
                lr.enabled = phase; lb.enabled = !phase;
                if (!siren.isPlaying) siren.Play();
            }
            else
            {
                U.SetEmission(red, Color.black); U.SetEmission(blue, Color.black);
                lr.enabled = false; lb.enabled = false;
                if (siren.isPlaying) siren.Stop();
            }
        }
    }
}
