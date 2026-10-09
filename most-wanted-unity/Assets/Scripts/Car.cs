using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using RVP;
using VehicleBehaviour;

namespace MostWanted
{
    /// <summary>
    /// Araç kontrolcüsü. İki fizik modu (araç başına aynı anda yalnızca biri aktif):
    ///  • Normal (tüm araçlar, trafik, polis, yarışçılar): Randomation Vehicle Physics (MIT) — ışın-izli süspansiyon,
    ///    RVP lastik eğrileri (yanal/boyuna kayma + kayma bağımlılığı), RVP direksiyon eğrisi, TCS/ABS, RVP savrulma yardımı.
    ///  • Drift (Drift Araçları + drift ayarı açık): Saarg Arcade Car Physics (MIT) — WheelCollider, Saarg sürtünme eğrileri,
    ///    el freni = arka yanal sertlik düşer, drift kuvveti/torku, downforce.
    /// Motor/şanzıman/devir (ses kancaları), nitro, speedbreaker, devrilme/fırlama korumaları bizim.
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
        public float peakTorque, redline, topSpeed, finalDrive, dragK, grip = 1f, brakeTorque, downforceK, maxSteer = 40f;
        public float steerSens = 1f;   // oyuncu: direksiyon hassasiyeti (0.6–1.6)
        public float nitroCap = 1f, nitroPower = 1f, shiftTime = 0.28f, antiRoll, wheelRadius = 0.34f, wheelBase = 2.6f;
        public int drive = 1;
        [System.NonSerialized] public float[] ratios = CarMath.MakeRatios(6);
        public float stabilityAssist = 0.45f;   // RVP driftSpinAssist ölçeği
        public float comHeight = 0.42f;

        // ---- Fizik modu ----
        public bool driftMode;
        public bool arcade;                     // oyuncu: Arcade sürüş stili (RVP + yardımlar)
        public bool mw;                         // oyuncu: "MW Sürüş" (varsayılan) — doğrudan savrulma/tutuş modeli (MwDrive)
        [System.NonSerialized] public float mwYaw, mwLat;   // MW: hedeflenen savrulma (rad/s), yanal hız (m/s)
        bool mwDynHit;
        float lastBurstLog = -10f; string lastHitName = "-";
        public float camberDeg, rideDrop;       // F&F stance
        public Color headColor = new Color(1f, 0.96f, 0.85f);                  // true: Saarg (WheelCollider), false: RVP
        public float driftLock = 57f;           // drift ayarı direksiyon açısı
        public const float SuspensionTravel = 0.18f;

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
        [System.NonSerialized] public CarParts parts;
        public Transform[] wheelAnchor = new Transform[4];          // FL FR RL RR — süspansiyon üst noktası
        public float[] wheelRadii = { 0.34f, 0.34f, 0.34f, 0.34f };
        public WheelCollider[] wheels = new WheelCollider[4];        // yalnızca drift modunda dolu
        [System.NonSerialized] public RvpWheel[] rvp = new RvpWheel[4]; // yalnızca normal modda dolu
        public Transform[] wheelVis = new Transform[4];
        public List<Material> paintMats = new List<Material>();
        public List<Material> brakeMats = new List<Material>();
        /// <summary>Fren lambası parlaklık çarpanı (trafik: 2.5 → bloom).</summary>
        [System.NonSerialized] public float brakeGlow = 1f;
        public List<Material> headMats = new List<Material>();
        public List<Transform> flames = new List<Transform>();
        public List<Transform> exhaustTips = new List<Transform>();
        public Light popLight;
        public bool electric;
        [System.NonSerialized] public Vector3 flameBaseScale = Vector3.one;
        public ParticleSystem[] smoke = new ParticleSystem[2];

        [System.NonSerialized] public System.Action<Collision> onHit;
        [System.NonSerialized] public System.Action<int, int> onShift;
        [System.NonSerialized] public System.Action<int, int> onShiftAudio; // vites, kalite (0 normal, 1 iyi, 2 mükemmel)

        // ---- his katmanı ----
        [System.NonSerialized] public float latG, longG, tyreSlip;     // filtrelenmiş ivmeler (g), en büyük lastik kayması (1 = tepe)
        readonly float[] wSlip = new float[4];
        Vector3 gPrevVel; bool gHas;
        float bodyRoll, bodyRollV, bodyPitch, bodyPitchV, prevSteerIn;
        float curSteer, uprightTimer, shiftTimer, limiterT;
        Vector3 lastVel; bool hasLastVel; int burstCount;
        float sinceShift = 1f;
        float handbrakeBlend, liftBlend;
        float suspK, suspC;

        // tekerlek ışınları: ikon/detay katmanlarını yok say
        static int WheelMask { get { return ~((1 << 30) | (1 << 28) | (1 << 2)); } }

        public bool TiresBlown { get { return spikeTimer > 0f; } }
        public bool Shifting { get { return shiftTimer > 0f; } }
        public float SpeedKmh { get { return U.Vel(rb).magnitude * 3.6f; } }
        public float ForwardSpeed { get { return Vector3.Dot(U.Vel(rb), transform.forward); } }
        public float Rpm01 { get { return Mathf.Clamp01(rpm / redline); } }
        public float SteerDeg { get { return curSteer; } }
        public Vector3 PrevVel { get { return hasLastVel ? lastVel : U.Vel(rb); } }

        // ---- tekerlek sorguları (iki mod için ortak; ses/efekt kodu bunları kullanır) ----
        public bool WheelGrounded(int i)
        {
            if (driftMode) return wheels[i] != null && wheels[i].isGrounded;
            return rvp[i] != null && rvp[i].grounded;
        }
        public Collider WheelGroundCollider(int i)
        {
            if (driftMode) { WheelHit h; return wheels[i] != null && wheels[i].GetGroundHit(out h) ? h.collider : null; }
            return rvp[i] != null && rvp[i].grounded ? rvp[i].hit.collider : null;
        }
        public float WheelLoad(int i)
        {
            if (driftMode) { WheelHit h; return wheels[i] != null && wheels[i].GetGroundHit(out h) ? h.force : 0f; }
            return rvp[i] != null ? rvp[i].load : 0f;
        }

        /// <summary>Fizik modunu kur/değiştir. Eski moddaki tekerlek bileşenleri tamamen kaldırılır (tek kontrolcü).</summary>
        public void SetPhysicsMode(bool drift)
        {
            driftMode = drift;
            for (int i = 0; i < 4; i++)
            {
                if (wheelAnchor[i] == null) continue;
                var old = wheelAnchor[i].GetComponent<WheelCollider>();
                if (!drift && old != null) Object.DestroyImmediate(old);
                wheels[i] = null; rvp[i] = null;
                if (drift)
                {
                    var wc = old != null ? old : wheelAnchor[i].gameObject.AddComponent<WheelCollider>();
                    wc.radius = wheelRadii[i];
                    wc.mass = 22f;
                    wc.suspensionDistance = SuspensionTravel;
                    wc.center = Vector3.zero;
                    wc.forceAppPointDistance = Mathf.Max(0.05f, comHeight - 0.08f);
                    wc.wheelDampingRate = 0.4f;
                    wc.forwardFriction = ArcadeDrift.Forward(2f);
                    wc.sidewaysFriction = ArcadeDrift.Sideways(2f);
                    if (i == 0) wc.ConfigureVehicleSubsteps(5f, 12, 15);
                    wc.motorTorque = 0.0001f;
                    wheels[i] = wc;
                }
                else
                {
                    rvp[i] = new RvpWheel { anchor = wheelAnchor[i], vis = wheelVis[i], radius = wheelRadii[i], suspensionDistance = SuspensionTravel, steered = i < 2 };
                }
            }
            if (rb != null && def != null) ApplySuspension();
        }

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
            ratios = CarMath.MakeRatios(d.gears > 0 ? d.gears : 6);
            finalDrive = CarMath.FinalDrive(topSpeed, redline, wheelRadius, ratios);
            dragK = CarMath.DragCoef(peakTorque, topSpeed, redline, wheelRadius, ratios);
            ApplySuspension();
        }

        void ApplySuspension()
        {
            // ~1.7 Hz, ζ 0.5
            float m = rb.mass;
            float corner = m / 4f;
            float freq = 1.7f + 0.1f * tune[(int)Tune.Suspansiyon];
            suspK = corner * Mathf.Pow(2f * Mathf.PI * freq, 2f);
            suspC = 2f * 0.5f * Mathf.Sqrt(suspK * corner);   // ζ = 0.5 (zıplamasın)
            for (int i = 0; i < 4; i++)
            {
                if (wheels[i] != null) wheels[i].suspensionSpring = new JointSpring { spring = suspK, damper = suspC, targetPosition = 0.5f };
                if (rvp[i] != null)
                {
                    // RVP springForce (ivme): kütle başına k * mesafe → aynı frekans; sönüm oranı RVP springDampening ile
                    rvp[i].springForce = suspK * SuspensionTravel / m;           // a = k·x / m (yük = a · m)
                    rvp[i].springDampening = suspC / (m * rvp[i].springForce);   // a_sönüm = c·v / m
                }
            }
        }

        public void ShiftUp()
        {
            if (gear >= ratios.Length || Shifting || gear <= 0) return;
            float x = rpm / redline;
            int q = x >= 0.88f && x <= 0.98f ? 2 : x >= 0.76f ? 1 : 0;
            lastShiftQuality = q;
            gear++;
            sinceShift = 0f;
            shiftTimer = manualGearbox ? (q == 2 ? 0.08f : q == 1 ? 0.18f : 0.35f) : shiftTime;
            if (manualGearbox && q == 2) nitro = Mathf.Min(1f, nitro + 0.08f);
            if (onShift != null) onShift(gear, q);
            if (onShiftAudio != null) onShiftAudio(gear, q);
        }

        public void ShiftDown()
        {
            if (gear <= 1 || Shifting) return;
            gear--;
            sinceShift = 0f;
            shiftTimer = shiftTime * 0.6f;
            if (onShift != null) onShift(gear, 0);
        }

        void FixedUpdate()
        {
            if (rb == null) return;
            if (rb.isKinematic) { hasLastVel = false; gHas = false; return; }   // kinematik→dinamik geçişte eski hız "patlama" sanılmasın (trafik uyarı spamı)
            if (!driftMode && rvp[0] == null) return;
            if (driftMode && wheels[0] == null) return;
            float dt = Time.fixedDeltaTime;
            Vector3 v = U.Vel(rb);
            // PhysX itme patlaması koruması: tek adımda anormal hız artışı/fırlama → geri al
            if (hasLastVel && !locked)
            {
                bool burst = v.magnitude - lastVel.magnitude > 15f || v.y - lastVel.y > 10f || Mathf.Abs(rb.angularVelocity.x) + Mathf.Abs(rb.angularVelocity.z) > 10f;
                if (burst)
                {
                    v = new Vector3(lastVel.x, Mathf.Min(lastVel.y, 0f), lastVel.z);
                    U.SetVel(rb, v);
                    Vector3 av0 = rb.angularVelocity; rb.angularVelocity = new Vector3(Mathf.Clamp(av0.x, -1f, 1f), av0.y, Mathf.Clamp(av0.z, -1f, 1f));
                    burstCount++;
                    if (Time.time - lastBurstLog > 5f)
                    {
                        lastBurstLog = Time.time;
                        string under = "?";
                        for (int wi = 0; wi < 4; wi++) { var gc2 = WheelGroundCollider(wi); if (gc2 != null) { under = gc2.name; break; } }
                        Debug.LogWarning("[MW] Fizik patlaması engellendi: " + name + " (" + burstCount + ". kez) zemin=" + under + " son çarpışma=" + lastHitName + " konum=" + transform.position.ToString("F0"));
                    }
                }
            }
            // takla koruması: çarpışma darbesi gövdeyi devirmesin (360 dönme/zıplama). Yuvarlanma/yunuslama hızı sınırlı,
            // tek adımda gelen yukarı itme kırpılır, havadayken araç kendini düz tutar.
            if (!locked)
            {
                Vector3 lav0 = transform.InverseTransformDirection(rb.angularVelocity);
                float capRP = isPlayer ? 1.6f : 2.5f;
                if (Mathf.Abs(lav0.x) > capRP || Mathf.Abs(lav0.z) > capRP)
                {
                    lav0.x = Mathf.Clamp(lav0.x, -capRP, capRP); lav0.z = Mathf.Clamp(lav0.z, -capRP, capRP);
                    rb.angularVelocity = transform.TransformDirection(lav0);
                }
                if (hasLastVel && v.y - lastVel.y > 3f && v.y > 2f)
                {
                    v.y = Mathf.Max(lastVel.y, 0f) + 1f;
                    U.SetVel(rb, v);
                }
                if (transform.up.y < 0.9f)
                {
                    Vector3 axis = Vector3.Cross(transform.up, Vector3.up);
                    rb.AddTorque(axis * (isPlayer ? 14f : 8f), ForceMode.Acceleration);
                }
            }
            lastVel = v; hasLastVel = true;
            float fwd = Vector3.Dot(v, transform.forward);
            float kmh = v.magnitude * 3.6f;
            if (spikeTimer > 0f) spikeTimer -= dt;
            if (shiftTimer > 0f) shiftTimer -= dt;

            Vector3 lv = transform.InverseTransformDirection(v);
            slipAngle = kmh > 12f && lv.z > 0.5f ? Mathf.Atan2(lv.x, lv.z) * Mathf.Rad2Deg : 0f;
            driftAmount = kmh > 40f ? Mathf.Abs(slipAngle) : 0f;
            handbrakeBlend = Mathf.MoveTowards(handbrakeBlend, handbrake ? 1f : 0f, dt * (handbrake ? 6f : 1.6f));
            float g = grip * gripBoost * (TiresBlown ? 0.5f : 1f);

            if (locked || disabled)
            {
                rpm = Mathf.Lerp(rpm, CarMath.IdleRpm + Mathf.Max(0f, throttle) * (redline * 0.85f - CarMath.IdleRpm), dt * 6f);
                nitroActive = false;
                if (driftMode)
                    for (int i = 0; i < 4; i++) { wheels[i].motorTorque = 0f; wheels[i].brakeTorque = brakeTorque * 3f; }
                else
                    RvpStep(dt, kmh, fwd, lv, 0f, 0f, true, g);
                UpdateEffects(kmh, 0f);
                return;
            }

            // ---- Direksiyon ----
            float target;
            if (driftMode)
            {
                // Saarg: sabit kilit açısı (drift ayarı 55–60°), kayarken tam açı; düz giderken hızla biraz azalır
                float lockDeg = driftLock * steerSens * steerBoost;
                float sc = Mathf.Abs(slipAngle) > 10f || handbrake ? 1f : Mathf.Max(0.3f, RvpTire.SteerCurve(fwd) * 1.4f);
                target = steer * Mathf.Min(driftLock * 1.05f, lockDeg * sc);
                curSteer = ArcadeDrift.Steer(curSteer, target, 0.2f, dt);
            }
            else
            {
                // RVP SteeringControl: steerCurve(hız) * aralık — gazdan bağımsız; hızlı tepki (tam kilit 0.12 sn, dönüş 0.08 sn)
                float limit = maxSteer * (arcade ? RvpTire.SteerCurveArcade(fwd) : RvpTire.SteerCurve(fwd)) * steerSens * steerBoost;
                // aşırı kilit sınırı (hassasiyetle birlikte büyür → yüksek değer her zaman ≥ düşük değer)
                float muS = 1.05f * grip * (arcade ? RvpTire.ArcadeGrip(kmh) * 1.4f : 1.4f);
                limit = Mathf.Min(limit, RvpTire.SteerOptimal(fwd, wheelBase, muS) * (0.95f + 0.25f * steerSens));
                if (handbrake) limit = Mathf.Max(limit, maxSteer * 0.6f);
                target = Mathf.Clamp(steer * limit, -maxSteer * 1.1f, maxSteer * 1.1f);
                bool ret = Mathf.Abs(target) < Mathf.Abs(curSteer) || target * curSteer < 0f;
                curSteer = Mathf.MoveTowards(curSteer, target, RvpTire.SteerRateDeg(maxSteer, ret, Mathf.Max(0.6f, steerSens)) * dt);
            }

            // ---- Şanzıman ----
            if (throttle < -0.05f && fwd < 1f && gear > 0) { gear = 0; shiftTimer = 0.2f; }
            if (throttle > 0.05f && gear == 0 && fwd > -1f) { gear = 1; shiftTimer = 0.15f; }
            float speedRpm = CarMath.EngineRpm(fwd, gear, finalDrive, wheelRadius, ratios);
            float wheelRpm = 0f; int nd = 0;
            for (int i = 0; i < 4; i++) if (IsDriven(i)) { wheelRpm += Mathf.Abs(driftMode ? wheels[i].rpm : rvp[i].spinRpm); nd++; }
            wheelRpm = nd > 0 ? wheelRpm / nd : 0f;
            float r = CarMath.GearRatio(gear, ratios);
            float spinRpm = wheelRpm * r * finalDrive;
            float target_rpm = Mathf.Max(speedRpm, Mathf.Min(spinRpm, redline * 1.05f));
            if (gear == 1 && kmh < 30f) target_rpm = Mathf.Max(target_rpm, CarMath.IdleRpm + Mathf.Abs(throttle) * Mathf.Min(redline * 0.55f, 4300f)); // debriyaj kaydırma
            target_rpm = Mathf.Max(CarMath.IdleRpm, target_rpm);
            rpm = Mathf.Lerp(rpm, Mathf.Min(target_rpm, redline * 1.02f), dt * 12f);

            sinceShift += dt;
            if (!manualGearbox && gear > 0 && !Shifting && sinceShift > 0.45f)
            {
                if (rpm > redline * 0.96f && speedRpm > redline * 0.9f && gear < ratios.Length && throttle > 0.1f && GroundedCount() >= 2) ShiftUp();
                else if (gear > 1 && rpm < redline * 0.45f) ShiftDown();
            }

            // ---- Motor torku ----
            float thAbs = Mathf.Abs(throttle);
            float wantDir = gear == 0 ? -1f : 1f;
            bool accelerating = (gear > 0 && throttle > 0.05f) || (gear == 0 && throttle < -0.05f);
            float engT = 0f;
            revLimiter = rpm >= redline * 0.995f;
            if (accelerating && !Shifting)
            {
                engT = peakTorque * CarMath.TorqueCurve(rpm / redline) * thAbs;
                if (gear == 1) engT *= CarMath.LaunchAssist(kmh);
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

            // ---- Fren talebi (tork) ----
            float brk = 0f;
            braking = false;
            if (gear > 0 && throttle < -0.05f && fwd > 1f) { brk = brakeTorque * -throttle; braking = true; }
            if (gear == 0 && throttle > 0.05f && fwd < -1f) { brk = brakeTorque * throttle; braking = true; }
            if (thAbs < 0.05f) brk = brakeTorque * 0.04f; // motor freni

            if (driftMode) ArcadeStep(dt, kmh, fwd, wheelT, brk, g);
            else RvpStep(dt, kmh, fwd, lv, wheelT, brk, false, g);

            int grounded = GroundedCount();
            if (grounded > 0)
            {
                // aerodinamik: downforce, sürükleme, yuvarlanma direnci
                rb.AddForce(-transform.up * downforceK * v.sqrMagnitude);
                Vector3 flat = U.Flat(v);
                rb.AddForce(-flat.normalized * dragK * flat.sqrMagnitude);
                if (flat.sqrMagnitude > 0.25f) rb.AddForce(-flat.normalized * rb.mass * CarMath.RollingDecel);
                if (nitroActive) rb.AddForce(transform.forward * rb.mass * 2.6f * nitroPower);
                // yokuş yardımı (oyuncu, sadece boyuna — direksiyona karışmaz)
                if (isPlayer && throttle > 0.1f && gear > 0 && transform.forward.y > 0.01f)
                    rb.AddForce(transform.forward * rb.mass * CarMath.Gravity * transform.forward.y * CarMath.HillAssist(kmh));
            }
            else
            {
                Vector3 av = rb.angularVelocity;
                rb.angularVelocity = new Vector3(av.x * 0.96f, av.y, av.z * 0.96f);
            }

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

            // his: ivmeler (kamera/gövde yatışı), lastik izleri
            if (gHas)
            {
                Vector3 acc = (v - gPrevVel) / dt;
                float k = 1f - Mathf.Exp(-dt * 10f);
                latG = Mathf.Lerp(latG, Mathf.Clamp(Vector3.Dot(acc, transform.right) / CarMath.Gravity, -3f, 3f), k);
                longG = Mathf.Lerp(longG, Mathf.Clamp(Vector3.Dot(acc, transform.forward) / CarMath.Gravity, -3f, 3f), k);
            }
            gPrevVel = v; gHas = true;
            Skids(kmh);
            UpdateEffects(kmh, Mathf.Abs(slipAngle));
        }

        void Skids(float kmh)
        {
            tyreSlip = 0f;
            for (int i = 0; i < 4; i++) tyreSlip = Mathf.Max(tyreSlip, wSlip[i]);
            if (!isPlayer && !(GetComponent<RacerDriver>() != null)) return;   // iz: oyuncu + rakipler
            var sm = SkidMarks.Get();
            int id = GetHashCode() * 4;
            for (int i = 0; i < 4; i++)
            {
                float inten = kmh > 8f ? Mathf.Clamp01((wSlip[i] - 1.0f) / 0.8f) : 0f;
                Vector3 p, n;
                if (inten > 0f && GroundPoint(i, out p, out n)) sm.Add(id + i, p, n, transform.right, 0.24f, inten);
                else sm.Add(id + i, Vector3.zero, Vector3.up, Vector3.right, 0f, 0f);
            }
        }

        bool GroundPoint(int i, out Vector3 p, out Vector3 n)
        {
            p = Vector3.zero; n = Vector3.up;
            if (driftMode) { WheelHit h; if (wheels[i] != null && wheels[i].GetGroundHit(out h)) { p = h.point; n = h.normal; return true; } return false; }
            if (rvp[i] != null && rvp[i].grounded) { p = rvp[i].hit.point; n = rvp[i].hit.normal; return true; }
            return false;
        }

        /// <summary>Normal mod: Randomation Vehicle Physics tekerlekleri.</summary>
        void RvpStep(float dt, float kmh, float fwd, Vector3 lv, float wheelT, float brk, bool hold, float g)
        {
            // temas + ortalama zemin normali (RVP VehicleParent.norm)
            Vector3 nAvg = Vector3.zero; int gc = 0;
            for (int i = 0; i < 4; i++)
            {
                rvp[i].GetContact(transform, WheelMask);
                if (rvp[i].grounded) { nAvg += rvp[i].hit.normal; gc++; }
            }
            nAvg = gc > 0 ? nAvg.normalized : transform.up;
            for (int i = 0; i < 4; i++) rvp[i].ApplySuspension(rb, nAvg, dt);
            AntiRollRvp(0, 1); AntiRollRvp(2, 3);

            float m = rb.mass;
            float mu = 1.05f * g;
            float fShare = drive == 0 ? 0.5f : drive == 1 ? 0f : 0.2f;   // 4x4: %40 ön
            float rShare = drive == 0 ? 0f : drive == 1 ? 0.5f : 0.3f;
            // gazı bırakınca hafif ağırlık transferi → çok hafif lift-off oversteer
            liftBlend = Mathf.MoveTowards(liftBlend, Mathf.Abs(throttle) < 0.05f && kmh > 40f && !hold ? 1f : 0f, dt * 3f);
            bool tcs = kmh > 15f || hold;   // kalkışta biraz patinaja izin ver
            float forceH = Mathf.Max(0.05f, comHeight - 0.08f);
            for (int i = 0; i < 4; i++)
            {
                var w = rvp[i];
                w.steerDeg = i < 2 ? curSteer : 0f;
                bool front = i < 2;
                float drv = wheelT * (front ? fShare : rShare) / wheelRadius / m;
                float bAcc = hold ? 12f : brk * (front ? 1.2f : 0.8f) / wheelRadius / m;
                float spin = -1f;
                float gF, gR; RvpTire.SideGrip(kmh, out gF, out gR);
                if (arcade)
                {
                    float ag = RvpTire.ArcadeGrip(kmh); gF *= handbrake ? 1f : ag; gR *= ag;   // el freninde ön ek tutuş yok → tutulabilir drift
                    // fren + direksiyon (60+ km/s): tutuş öne kayar → daha keskin dönüşe giriş
                    if (throttle < -0.1f && Mathf.Abs(steer) > 0.2f && kmh > 60f) { gF *= 1.15f; gR *= 0.93f; }
                }
                float sideMul = front ? gF * (1f + 0.05f * liftBlend) : gR * (1f - 0.04f * liftBlend);   // yüksek hızda arka ≥ ön; gaz bırakınca hafif oversteer
                if (!front && handbrake && !hold)
                {
                    spin = 1f;           // kilitli arka teker (RVP ebrake)
                    drv = 0f;
                    sideMul *= Mathf.Lerp(1f, 0.75f, handbrakeBlend);
                }
                if (mw && !hold) sideMul = 0f;   // MW Sürüş: yanal lastik kuvveti yok (MwStep)
                w.ApplyFriction(rb, transform, mu, sideMul, drv, bAcc, tcs, spin, forceH, dt);
                // lastik kayması (1 = yanal tepe): ses, iz, duman için
                float lat = w.grounded ? Mathf.Abs(w.sideSlip) / 0.1932f : 0f;
                float lng = w.grounded ? Mathf.Max(0f, w.fwdSlip / 0.2013f - 1f) * 1.5f : 0f;
                wSlip[i] = Mathf.Max(lat, lng + (spin >= 0f && kmh > 15f ? 1.4f : 0f));
            }

            if (mw && !hold) { MwStep(dt, kmh, gc); return; }
            // RVP VehicleAssist: savrulma yardımı (sadece kayarken etkin) — gazdan bağımsız
            if (gc > 0 && !hold)
            {
                float muEff = arcade ? mu * RvpTire.ArcadeGrip(kmh) * 1.3f : mu;
                float yawAcc = RvpTire.SpinAssist(steer, fwd, lv.x, rb.angularVelocity.y, 2.2f, 1.6f * stabilityAssist / 0.45f, curSteer, wheelBase, muEff);
                if (arcade && !handbrake)
                {
                    yawAcc += RvpTire.ArcadeTurnIn(steer, fwd, rb.angularVelocity.y, curSteer, wheelBase, muEff, 8f);
                    // dönüşe giriş tekmesi: girdi başladığı anda küçük anlık savrulma (gecikmesiz tepki)
                    if (Mathf.Abs(steer) > 0.3f && Mathf.Abs(prevSteerIn) < 0.15f && kmh > 20f)
                        rb.AddTorque(transform.up * Mathf.Sign(steer) * Mathf.Sign(fwd) * 0.12f * Mathf.Min(1f, kmh / 60f), ForceMode.VelocityChange);
                }
                prevSteerIn = steer;
                rb.AddTorque(transform.up * yawAcc, ForceMode.Acceleration);
            }
        }

        /// <summary>MW Sürüş: savrulma hızı doğrudan (ivme sınırlı), yanal hız üstel sönüm; lastik yanal kuvveti yok → spin yok.</summary>
        void MwStep(float dt, float kmh, int gc)
        {
            Vector3 lav = transform.InverseTransformDirection(rb.angularVelocity);
            if (gc < 2) { mwYaw = lav.y; return; }   // havada: fizik
            // fiziğin ürettiği savrulma (bordür/basamak/duvar) yok sayılır; sadece araç çarpışmasında kısmen kabul
            if (mwDynHit) { mwYaw = Mathf.Lerp(mwYaw, lav.y, 0.5f); mwDynHit = false; }
            Vector3 lv = transform.InverseTransformDirection(U.Vel(rb));
            bool braking = throttle < -0.1f && lv.z > 2f;
            float sens = Mathf.Max(0.3f, steerSens) / 1.2f;   // varsayılan 1.2 = tablo
            float target = MwDrive.TargetYaw(steer, lv.z, sens * steerBoost, braking, handbrakeBlend);
            mwYaw = MwDrive.StepYaw(mwYaw, target, dt);
            float lat = lv.x, fw = lv.z;
            MwDrive.StepVelocity(ref lat, ref fw, mwYaw, kmh, handbrakeBlend, dt);
            mwLat = lat;
            U.SetVel(rb, transform.TransformDirection(new Vector3(lat, lv.y, fw)));
            rb.angularVelocity = transform.TransformDirection(new Vector3(lav.x * 0.9f, mwYaw, lav.z * 0.9f));
            // his değişkenleri: yanal kayma ve savrulma
            float s = Mathf.Abs(lat) / 2.2f + (handbrakeBlend > 0.5f && kmh > 20f ? 1.2f : 0f);
            for (int i = 0; i < 4; i++) wSlip[i] = Mathf.Max(wSlip[i] * (i < 2 ? 0.5f : 1f), s);
        }

        void OnCollisionStay(Collision c) { if (mw && c.rigidbody != null && !c.rigidbody.isKinematic) mwDynHit = true; }

        /// <summary>Drift modu: Saarg Arcade Car Physics (WheelCollider).</summary>
        void ArcadeStep(float dt, float kmh, float fwd, float wheelT, float brk, float g)
        {
            for (int i = 0; i < 2; i++) wheels[i].steerAngle = curSteer;
            // drift ayarı: AWD'de arka ağırlıklı (%15 ön), RWD aynen; kilitli diferansiyel (eşit tork = LSD)
            float fShare = drive == 0 ? 0.5f : drive == 1 ? 0f : 0.075f;
            float rShare = drive == 0 ? 0f : drive == 1 ? 0.5f : 0.425f;
            for (int i = 0; i < 4; i++) { wheels[i].motorTorque = 0.0001f; wheels[i].brakeTorque = 0f; }
            wheels[0].motorTorque = wheels[1].motorTorque = Mathf.Max(0.0001f, Mathf.Abs(wheelT * fShare)) * Mathf.Sign(wheelT == 0f ? 1f : wheelT);
            wheels[2].motorTorque = wheels[3].motorTorque = Mathf.Max(0.0001f, Mathf.Abs(wheelT * rShare)) * Mathf.Sign(wheelT == 0f ? 1f : wheelT);
            wheels[0].brakeTorque = wheels[1].brakeTorque = brk * 1.2f;
            wheels[2].brakeTorque = wheels[3].brakeTorque = brk * 0.8f;
            if (handbrake)
            {
                wheels[2].brakeTorque = wheels[3].brakeTorque = brakeTorque * 1.2f;
                wheels[2].motorTorque = wheels[3].motorTorque = 0.0001f;
            }
            // Saarg sürtünme: ön 2.0, arka düşük (drift ayarı), el freninde arka yanal daha da düşer
            // gaz bırakılınca (el freni yok) arka tutuş öne eşitlenir → gaz kesince ani 360 dönme yok
            liftBlend = Mathf.MoveTowards(liftBlend, throttle < 0.2f && !handbrake ? 1f : 0f, dt * 4f);
            float rearSide = Mathf.Lerp(Mathf.Lerp(1.45f, 2.1f, liftBlend), 0.85f, handbrakeBlend);
            for (int i = 0; i < 4; i++)
            {
                wheels[i].forwardFriction = ArcadeDrift.Forward(2f * g);
                wheels[i].sidewaysFriction = ArcadeDrift.Sideways((i < 2 ? 2f : rearSide) * g);
            }
            AntiRoll(0, 1); AntiRoll(2, 3);
            for (int i = 0; i < 4; i++)
            {
                WheelHit h;
                wSlip[i] = wheels[i].GetGroundHit(out h) ? Mathf.Max(Mathf.Abs(h.sidewaysSlip) / 0.2f, Mathf.Abs(h.forwardSlip) / 0.4f - 0.5f) : 0f;
            }
            // dönme koruması: el freni yokken aşırı savrulma hızı yumuşakça kırpılır (spin/360 olmasın)
            if (!handbrake && GroundedCount() >= 2)
            {
                Vector3 la = transform.InverseTransformDirection(rb.angularVelocity);
                float maxYaw = Mathf.Lerp(2.6f, 1.4f, Mathf.Clamp01(kmh / 140f)) * (throttle < 0.2f ? 0.8f : 1f);
                if (Mathf.Abs(la.y) > maxYaw) { la.y = Mathf.Lerp(la.y, Mathf.Sign(la.y) * maxYaw, 0.35f); rb.angularVelocity = transform.TransformDirection(la); }
            }
            if (GroundedCount() >= 2)
            {
                bool drift = (handbrake && kmh > 36f) || (Mathf.Abs(slipAngle) > 10f && throttle > 0.3f && kmh > 36f);
                if (drift) ArcadeDrift.Apply(rb, transform, Mathf.Max(0f, fwd) * 3.6f, Mathf.Max(0.3f, throttle), curSteer, driftLock, 0.6f, dt);
                ArcadeDrift.Downforce(rb, transform, Mathf.Abs(fwd) * 3.6f, 1f);
            }
        }

        bool IsDriven(int i) { return drive == 2 || (drive == 0 ? i < 2 : i >= 2); }

        int GroundedCount()
        {
            int n = 0;
            for (int i = 0; i < 4; i++) if (WheelGrounded(i)) n++;
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

        void AntiRollRvp(int l, int r)
        {
            var wl = rvp[l]; var wr = rvp[r];
            float force = (wl.compression - wr.compression) * antiRoll;
            if (wl.grounded) rb.AddForceAtPosition(wl.anchor.up * -force, wl.anchor.position);
            if (wr.grounded) rb.AddForceAtPosition(wr.anchor.up * force, wr.anchor.position);
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
            hasLastVel = false;
            gear = 1; rpm = CarMath.IdleRpm;
            for (int i = 0; i < 4; i++) if (rvp[i] != null) { rvp[i].frictionForce = Vector3.zero; rvp[i].travelDist = 1f; }
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
                if (on) f.localScale = electric ? new Vector3(flameBaseScale.x, flameBaseScale.y * Random.Range(0.8f, 1.6f), flameBaseScale.z) : new Vector3(0.13f, 0.13f, Random.Range(0.5f, 0.95f));
            }
            // lastik dumanı
            bool burn = (gear == 1 && throttle > 0.8f && kmh < 15f && peakTorque > 300f && GroundedCount() >= 2 && !locked);
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
            // görsel gövde yatışı/yunuslama (fizik gövdesi değil): yanal/boyuna ivmeye yay-sönümleyici
            if (parts != null && parts.vis != null && (isPlayer || GetComponent<RacerDriver>() != null))
            {
                float dtv = Mathf.Clamp(Time.deltaTime, 0f, 0.05f);
                if (dtv > 0f)
                {
                    float tr = Mathf.Clamp(latG * 2.4f, -4f, 4f), tp = Mathf.Clamp(-longG * 1.6f, -3f, 3f);
                    const float w0 = 9f, z = 1.0f;   // kritik sönüm: salınım yok
                    bodyRollV += (w0 * w0 * (tr - bodyRoll) - 2f * z * w0 * bodyRollV) * dtv; bodyRoll += bodyRollV * dtv;
                    bodyPitchV += (w0 * w0 * (tp - bodyPitch) - 2f * z * w0 * bodyPitchV) * dtv; bodyPitch += bodyPitchV * dtv;
                }
                Quaternion q = Quaternion.Euler(bodyPitch, 0f, bodyRoll);
                Vector3 piv = new Vector3(0f, wheelRadius, (parts.wPos[0].z + parts.wPos[2].z) * 0.5f);
                parts.vis.localRotation = q;
                parts.vis.localPosition = piv - q * piv;
            }
            for (int i = 0; i < 4; i++)
            {
                if (rvp[i] != null) { rvp[i].UpdateVisual(transform, Time.deltaTime); continue; }
                if (wheels[i] == null || wheelVis[i] == null) continue;
                Vector3 p; Quaternion q;
                wheels[i].GetWorldPose(out p, out q);
                if (camberDeg != 0f) q = Quaternion.AngleAxis((i % 2 == 0 ? -1f : 1f) * camberDeg, transform.forward) * q;
                wheelVis[i].SetPositionAndRotation(p, q);
            }
            float b = braking || handbrake ? 3f * brakeGlow : 0.6f;
            foreach (var m in brakeMats) U.SetEmission(m, new Color(1f, 0.05f, 0.03f) * b);
        }

#if UNITY_EDITOR
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0f, 1f, 0.3f, 0.6f);
            Gizmos.matrix = transform.localToWorldMatrix;
            foreach (var b in GetComponents<BoxCollider>()) Gizmos.DrawWireCube(b.center, b.size);
        }
#endif

        void OnCollisionEnter(Collision c)
        {
            if (c.collider != null) lastHitName = c.collider.name;
            // sokak lambası: araç direğe takılıp takla atmasın — direk devrilir, araç hızını büyük ölçüde korur (NFS gibi)
            if (c.collider != null && c.collider.name == "Lamba" && c.rigidbody == null && hasLastVel && lastVel.magnitude > 4f)
            {
                KnockLamp(c.collider);
                U.SetVel(rb, new Vector3(lastVel.x, Mathf.Min(lastVel.y, 0f), lastVel.z) * 0.85f);
                Vector3 av = rb.angularVelocity; rb.angularVelocity = new Vector3(0f, Mathf.Clamp(av.y, -1f, 1f), 0f);
                return;
            }
            if (mw && c.rigidbody != null && !c.rigidbody.isKinematic) mwDynHit = true;
            if (onHit != null) onHit(c);
        }

        void KnockLamp(Collider pole)
        {
            var pt = pole.transform;
            var prb = pole.gameObject.AddComponent<Rigidbody>();
            prb.mass = 60f;
            Vector3 push = U.Flat(lastVel); push.y = 0f;
            U.SetVel(prb, push * 0.6f);
            prb.angularVelocity = Vector3.Cross(Vector3.up, push.normalized) * 2.5f;
            foreach (var col in GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(pole, col);
            // lamba başı ve ışığı da düşsün/sönsün
            if (pt.parent != null)
                foreach (Transform ch in pt.parent)
                {
                    Vector3 d = ch.position - pt.position; d.y = 0f;
                    if (d.sqrMagnitude > 16f) continue;
                    if (ch.name == "LambaBas" && ch.GetComponent<Rigidbody>() == null)
                    {
                        if (ch.GetComponent<Collider>() == null) ch.gameObject.AddComponent<BoxCollider>();
                        var hrb = ch.gameObject.AddComponent<Rigidbody>(); hrb.mass = 15f; U.SetVel(hrb, push * 0.5f);
                    }
                    else if (ch.name == "LambaIsik") ch.gameObject.SetActive(false);
                }
            Destroy(pole.gameObject, 20f);
        }

        public void Damage(float amount)
        {
            if (disabled) return;
            health -= amount;
            if (health <= 0f) { health = 0f; disabled = true; }
        }

        public void SetPaint(PaintDef p)
        {
            foreach (var m in paintMats) U.ApplyPaint(m, p);
        }

        /// <summary>F&amp;F stance: gövdeyi alçalt (süspansiyon bağlantıları yukarı) ve görsel kamber.</summary>
        public void SetStance(float drop, float camber)
        {
            for (int i = 0; i < 4; i++) if (wheelAnchor[i] != null) wheelAnchor[i].localPosition += Vector3.up * (drop - rideDrop);
            rideDrop = drop; camberDeg = camber;
            for (int i = 0; i < 4; i++) if (rvp[i] != null) rvp[i].camber = (i % 2 == 0 ? -1f : 1f) * camber;
        }

        public void SetHeadlights(bool on)
        {
            foreach (var m in headMats) U.SetEmission(m, on ? headColor * 2.5f : headColor * 0.4f);
        }
    }

    /// <summary>Kırmızı/mavi tepe lambaları ve siren.</summary>
    public class PoliceLights : MonoBehaviour
    {
        public bool on;
        Material red, blue;
        Light lr, lb;
        AudioSource siren;
        float t, modeTimer;

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
                siren.volume = 0.4f * AudioBus.Get(AudioBus.Bus.Siren);
                modeTimer -= Time.deltaTime;
                if (modeTimer <= 0f)
                {
                    modeTimer = Random.Range(4f, 9f);
                    var want = Random.value < 0.35f ? AudioSynth.SirenYelp() : AudioSynth.Siren();
                    if (siren.clip != want) { siren.clip = want; siren.Play(); }
                }
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
