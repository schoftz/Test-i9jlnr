using UnityEngine;
using UnityEngine.Rendering;

namespace MostWanted
{
    /// <summary>WheelCollider tabanlı arcade/drift araç fiziği. Girdiler sürücü bileşenlerinden gelir.</summary>
    public class CarController : MonoBehaviour
    {
        // Girdiler
        public float throttle;   // -1..1
        public float steer;      // -1..1
        public bool handbrake;
        public bool nitroInput;
        public bool locked;

        // Ayarlar
        public float maxTorque = 3000f;
        public float maxSpeedKmh = 220f;
        public float brakeTorque = 3500f;
        public float maxSteer = 34f;
        public float grip = 1f;
        public float nitroCapacity = 1f;
        public float nitroPower = 1f;
        public float downforce = 2.5f;
        public float antiRoll = 9000f;
        public bool isPlayer;

        public float nitro = 1f;
        public bool nitroActive;

        public Rigidbody rb;
        public WheelCollider[] wheels = new WheelCollider[4]; // FL FR RL RR
        public Transform[] wheelVis = new Transform[4];
        public Material bodyMat;

        public System.Action<Collision> onHit;

        float curSteer;
        float rearSide = 1.5f;
        float flipTimer;

        public float SpeedKmh { get { return U.Vel(rb).magnitude * 3.6f; } }
        public float ForwardSpeed { get { return Vector3.Dot(U.Vel(rb), transform.forward); } }

        public float EngineRpm01
        {
            get
            {
                float kmh = Mathf.Abs(ForwardSpeed) * 3.6f;
                float span = maxSpeedKmh / 6f;
                int gear = Mathf.Min(5, Mathf.FloorToInt(kmh / span));
                return Mathf.Clamp01((kmh - gear * span) / span * 0.85f + 0.12f + gear * 0.02f);
            }
        }
        public int Gear { get { return Mathf.Min(6, 1 + Mathf.FloorToInt(Mathf.Abs(ForwardSpeed) * 3.6f / (maxSpeedKmh / 6f))); } }

        void FixedUpdate()
        {
            if (rb == null || rb.isKinematic) return;
            float dt = Time.fixedDeltaTime;
            float fwd = ForwardSpeed;
            float kmh = Mathf.Abs(fwd) * 3.6f;

            if (locked)
            {
                for (int i = 0; i < 4; i++) { wheels[i].motorTorque = 0; wheels[i].brakeTorque = brakeTorque * 2f; }
                return;
            }

            // Hıza duyarlı direksiyon
            float steerLimit = Mathf.Lerp(maxSteer, maxSteer * 0.3f, Mathf.Clamp01(kmh / 190f));
            if (handbrake) steerLimit = Mathf.Max(steerLimit, maxSteer * 0.7f);
            curSteer = Mathf.MoveTowards(curSteer, steer * steerLimit, 140f * dt);
            wheels[0].steerAngle = curSteer;
            wheels[1].steerAngle = curSteer;

            float motor = 0f, brake = 0f;
            if (throttle > 0.05f)
            {
                if (fwd < -1.5f) brake = brakeTorque * throttle; else motor = throttle;
            }
            else if (throttle < -0.05f)
            {
                if (fwd > 1.5f) brake = brakeTorque * -throttle; else motor = throttle * 0.6f;
            }

            nitroActive = nitroInput && nitro > 0.01f && throttle > 0.1f && !handbrake;
            if (nitroActive) nitro = Mathf.Max(0f, nitro - dt / (3.5f * nitroCapacity));
            else nitro = Mathf.Min(1f, nitro + dt * (isPlayer ? 0.035f : 0.08f));

            float top = maxSpeedKmh * (nitroActive ? 1.15f : 1f);
            float sf = Mathf.Clamp01(kmh / top);
            float torque = maxTorque * (nitroActive ? 1.6f : 1f) * (1f - sf * sf * sf);
            if (motor < 0 && kmh > 45f) torque = 0f;
            float mt = motor * torque;

            wheels[0].motorTorque = mt * 0.15f;
            wheels[1].motorTorque = mt * 0.15f;
            wheels[2].motorTorque = mt * 0.35f;
            wheels[3].motorTorque = mt * 0.35f;

            for (int i = 0; i < 4; i++) wheels[i].brakeTorque = brake;
            if (handbrake)
            {
                wheels[2].brakeTorque = Mathf.Max(brake, brakeTorque * 0.6f);
                wheels[3].brakeTorque = Mathf.Max(brake, brakeTorque * 0.6f);
            }
            if (Mathf.Abs(throttle) < 0.05f && !handbrake)
            {
                // motor freni
                for (int i = 0; i < 4; i++) wheels[i].brakeTorque = Mathf.Max(wheels[i].brakeTorque, 60f);
            }

            // Sürtünme: el freninde arka yanal tutuş düşer (drift)
            float targetRear = handbrake ? 0.55f : 1.55f;
            rearSide = Mathf.MoveTowards(rearSide, targetRear, dt * (handbrake ? 6f : 1.8f));
            SetStiffness(0, 1.7f * grip, 1.6f * grip);
            SetStiffness(1, 1.7f * grip, 1.6f * grip);
            SetStiffness(2, rearSide * grip, 1.6f * grip);
            SetStiffness(3, rearSide * grip, 1.6f * grip);

            bool grounded = false;
            for (int i = 0; i < 4; i++) if (wheels[i].isGrounded) grounded = true;

            Vector3 v = U.Vel(rb);
            if (grounded)
            {
                // downforce
                rb.AddForce(-transform.up * downforce * v.sqrMagnitude);
                // nitro itişi
                if (nitroActive) rb.AddForce(transform.forward * rb.mass * 6.5f * nitroPower);
                // drift yardımı: el freni + direksiyon -> dönüş momenti
                if (handbrake && kmh > 25f)
                    rb.AddTorque(Vector3.up * steer * rb.mass * 1.6f, ForceMode.Force);
                // drift sırasında hız kaybını azalt (Most Wanted hissi)
                float lateral = Vector3.Dot(v, transform.right);
                if (!handbrake && Mathf.Abs(lateral) > 2f && throttle > 0.1f)
                    rb.AddForce(transform.forward * Mathf.Abs(lateral) * rb.mass * 0.25f);
            }
            else
            {
                // havada dengele
                Vector3 av = rb.angularVelocity;
                rb.angularVelocity = new Vector3(av.x * 0.97f, av.y, av.z * 0.97f);
            }

            AntiRoll(0, 1);
            AntiRoll(2, 3);

            // hız sınırı
            float limit = top / 3.6f * 1.05f;
            if (v.magnitude > limit) U.SetVel(rb, v.normalized * limit);

            // takla kurtarma
            if (transform.up.y < 0.3f && kmh < 8f)
            {
                flipTimer += dt;
                if (flipTimer > 2.5f) Unflip();
            }
            else flipTimer = 0f;
        }

        public void Unflip()
        {
            flipTimer = 0f;
            Vector3 f = U.Flat(transform.forward);
            if (f.sqrMagnitude < 0.01f) f = Vector3.forward;
            transform.position += Vector3.up * 1.5f;
            transform.rotation = Quaternion.LookRotation(f.normalized, Vector3.up);
            U.SetVel(rb, Vector3.zero);
            rb.angularVelocity = Vector3.zero;
        }

        public void Teleport(Vector3 pos, Quaternion rot)
        {
            rb.position = pos;
            rb.rotation = rot;
            transform.SetPositionAndRotation(pos, rot);
            U.SetVel(rb, Vector3.zero);
            rb.angularVelocity = Vector3.zero;
        }

        void SetStiffness(int i, float side, float fwd)
        {
            var s = wheels[i].sidewaysFriction; s.stiffness = side; wheels[i].sidewaysFriction = s;
            var f = wheels[i].forwardFriction; f.stiffness = fwd; wheels[i].forwardFriction = f;
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

        void LateUpdate()
        {
            for (int i = 0; i < 4; i++)
            {
                if (wheels[i] == null || wheelVis[i] == null) continue;
                Vector3 p; Quaternion q;
                wheels[i].GetWorldPose(out p, out q);
                wheelVis[i].SetPositionAndRotation(p, q);
            }
        }

        void OnCollisionEnter(Collision c)
        {
            if (onHit != null) onHit(c);
        }

        public void SetColor(Color c)
        {
            if (bodyMat != null) bodyMat.color = c;
        }
    }

    public enum CarRole { Player, Traffic, Police, Racer }

    public static class CarFactory
    {
        public static CarController Build(CarSpec s, Color color, Vector3 pos, Quaternion rot, CarRole role, string name)
        {
            var go = new GameObject(name);
            go.transform.SetPositionAndRotation(pos, rot);

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = s.mass;
            U.SetDamping(rb, 0.02f, 0.3f);
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = role == CarRole.Player ? CollisionDetectionMode.ContinuousDynamic : CollisionDetectionMode.Discrete;

            float W = s.width, L = s.length;
            var body = go.AddComponent<BoxCollider>();
            body.center = new Vector3(0, 0.78f, 0);
            body.size = new Vector3(W, 0.66f, L);
            var cab = go.AddComponent<BoxCollider>();
            cab.center = new Vector3(0, 1.3f, -0.15f);
            cab.size = new Vector3(W * 0.8f, 0.45f, L * 0.48f);

            rb.centerOfMass = new Vector3(0, 0.42f, 0.05f);

            var car = go.AddComponent<CarController>();
            car.rb = rb;
            car.isPlayer = role == CarRole.Player;
            car.maxTorque = s.torque;
            car.maxSpeedKmh = s.maxSpeed;
            car.grip = s.grip;
            car.nitroCapacity = s.nitroCap;
            car.brakeTorque = s.mass * 2.8f;

            // Görsel
            var vis = new GameObject("Gorsel").transform;
            vis.SetParent(go.transform, false);
            car.bodyMat = U.NewMat(color, 0.85f, 0.45f);
            var glass = U.Mat(new Color(0.05f, 0.07f, 0.1f), 0.95f, 0.2f);
            var dark = U.Mat(new Color(0.06f, 0.06f, 0.06f), 0.3f);
            U.Prim(PrimitiveType.Cube, "Govde", vis, new Vector3(0, 0.78f, 0), new Vector3(W, s.height, L), car.bodyMat);
            U.Prim(PrimitiveType.Cube, "Burun", vis, new Vector3(0, 0.62f + s.height * 0.5f, L * 0.32f), new Vector3(W * 0.96f, 0.08f, L * 0.3f), car.bodyMat);
            U.Prim(PrimitiveType.Cube, "Kabin", vis, new Vector3(0, 0.78f + s.height * 0.5f + 0.22f, -0.15f), new Vector3(W * 0.8f, 0.44f, L * 0.46f), glass);
            U.Prim(PrimitiveType.Cube, "Tampon", vis, new Vector3(0, 0.55f, L * 0.5f), new Vector3(W * 1.01f, 0.22f, 0.12f), dark);
            U.Prim(PrimitiveType.Cube, "ArkaTampon", vis, new Vector3(0, 0.55f, -L * 0.5f), new Vector3(W * 1.01f, 0.22f, 0.12f), dark);
            if (s.spoiler)
            {
                U.Prim(PrimitiveType.Cube, "Spoiler", vis, new Vector3(0, 1.35f, -L * 0.47f), new Vector3(W * 0.9f, 0.06f, 0.35f), dark);
                U.Prim(PrimitiveType.Cube, "SpoilerA", vis, new Vector3(W * 0.3f, 1.2f, -L * 0.47f), new Vector3(0.08f, 0.3f, 0.2f), dark);
                U.Prim(PrimitiveType.Cube, "SpoilerB", vis, new Vector3(-W * 0.3f, 1.2f, -L * 0.47f), new Vector3(0.08f, 0.3f, 0.2f), dark);
            }
            var head = U.Emissive(Color.white, new Color(2f, 2f, 1.8f));
            var tail = U.Emissive(new Color(0.5f, 0, 0), new Color(1.6f, 0f, 0f));
            U.Prim(PrimitiveType.Cube, "FarL", vis, new Vector3(-W * 0.35f, 0.82f, L * 0.5f), new Vector3(0.4f, 0.14f, 0.06f), head);
            U.Prim(PrimitiveType.Cube, "FarR", vis, new Vector3(W * 0.35f, 0.82f, L * 0.5f), new Vector3(0.4f, 0.14f, 0.06f), head);
            U.Prim(PrimitiveType.Cube, "StopL", vis, new Vector3(-W * 0.36f, 0.85f, -L * 0.5f), new Vector3(0.45f, 0.12f, 0.06f), tail);
            U.Prim(PrimitiveType.Cube, "StopR", vis, new Vector3(W * 0.36f, 0.85f, -L * 0.5f), new Vector3(0.45f, 0.12f, 0.06f), tail);

            // Tekerlekler
            float r = 0.36f;
            Vector3[] wp =
            {
                new Vector3(-W / 2 + 0.2f, 0.5f, L / 2 - 0.8f), new Vector3(W / 2 - 0.2f, 0.5f, L / 2 - 0.8f),
                new Vector3(-W / 2 + 0.2f, 0.5f, -L / 2 + 0.8f), new Vector3(W / 2 - 0.2f, 0.5f, -L / 2 + 0.8f)
            };
            string[] wn = { "FL", "FR", "RL", "RR" };
            var tire = U.Mat(new Color(0.05f, 0.05f, 0.05f), 0.1f);
            var rim = U.Mat(new Color(0.7f, 0.7f, 0.75f), 0.8f, 0.9f);
            for (int i = 0; i < 4; i++)
            {
                var wgo = new GameObject("WC_" + wn[i]);
                wgo.transform.SetParent(go.transform, false);
                wgo.transform.localPosition = wp[i];
                var wc = wgo.AddComponent<WheelCollider>();
                wc.radius = r;
                wc.mass = 20f;
                wc.suspensionDistance = 0.22f;
                wc.forceAppPointDistance = 0.1f;
                var sp = new JointSpring { spring = s.mass * 28f, damper = s.mass * 3.2f, targetPosition = 0.45f };
                wc.suspensionSpring = sp;
                wc.forwardFriction = new WheelFrictionCurve { extremumSlip = 0.4f, extremumValue = 1f, asymptoteSlip = 0.8f, asymptoteValue = 0.6f, stiffness = 1.6f };
                wc.sidewaysFriction = new WheelFrictionCurve { extremumSlip = 0.25f, extremumValue = 1f, asymptoteSlip = 0.6f, asymptoteValue = 0.75f, stiffness = 1.6f };
                if (i == 0) wc.ConfigureVehicleSubsteps(5f, 12, 15);
                car.wheels[i] = wc;

                var pivot = new GameObject("Teker_" + wn[i]).transform;
                pivot.SetParent(go.transform, false);
                pivot.localPosition = wp[i];
                var t = U.Prim(PrimitiveType.Cylinder, "Lastik", pivot, Vector3.zero, new Vector3(r * 2, 0.13f, r * 2), tire);
                t.transform.localRotation = Quaternion.Euler(0, 0, 90);
                var rm = U.Prim(PrimitiveType.Cylinder, "Jant", pivot, new Vector3(i % 2 == 0 ? -0.02f : 0.02f, 0, 0), new Vector3(r * 1.3f, 0.135f, r * 1.3f), rim);
                rm.transform.localRotation = Quaternion.Euler(0, 0, 90);
                car.wheelVis[i] = pivot;
            }

            Color ic = role == CarRole.Player ? new Color(1f, 0.9f, 0f) : role == CarRole.Police ? new Color(1f, 0.1f, 0.1f) :
                       role == CarRole.Racer ? new Color(1f, 0.4f, 1f) : new Color(0.7f, 0.7f, 0.7f);
            U.Icon(go.transform, ic, role == CarRole.Player ? 9f : 6f, true);

            return car;
        }

        public static void ApplyTuning(CarController car, CarSpec s, CarSave save)
        {
            car.maxTorque = s.torque * (1f + 0.14f * save.engine);
            car.maxSpeedKmh = s.maxSpeed * (1f + 0.05f * save.engine);
            car.nitroCapacity = s.nitroCap * (1f + 0.3f * save.nitro);
            car.nitroPower = 1f + 0.12f * save.nitro;
            car.grip = s.grip * (1f + 0.07f * save.handling);
            car.maxSteer = 34f + 2f * save.handling;
        }

        public static CarController BuildPolice(Vector3 pos, Quaternion rot)
        {
            var car = Build(Catalog.Police, new Color(0.05f, 0.05f, 0.07f), pos, rot, CarRole.Police, "Polis");
            var vis = car.transform.Find("Gorsel");
            var white = U.Mat(Color.white, 0.8f, 0.3f);
            var s = Catalog.Police;
            U.Prim(PrimitiveType.Cube, "KapiL", vis, new Vector3(-s.width / 2 - 0.005f, 0.8f, 0), new Vector3(0.02f, 0.4f, s.length * 0.45f), white);
            U.Prim(PrimitiveType.Cube, "KapiR", vis, new Vector3(s.width / 2 + 0.005f, 0.8f, 0), new Vector3(0.02f, 0.4f, s.length * 0.45f), white);
            car.gameObject.AddComponent<PoliceLights>().Setup(vis);
            return car;
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

        public void Setup(Transform vis)
        {
            red = U.NewMat(new Color(0.4f, 0, 0)); blue = U.NewMat(new Color(0, 0, 0.4f));
            var bar = U.Prim(PrimitiveType.Cube, "TepeLamba", vis, new Vector3(0, 1.6f, -0.1f), new Vector3(1.2f, 0.12f, 0.3f), U.Mat(Color.gray));
            U.Prim(PrimitiveType.Cube, "Kirmizi", vis, new Vector3(-0.35f, 1.68f, -0.1f), new Vector3(0.5f, 0.14f, 0.28f), red).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            U.Prim(PrimitiveType.Cube, "Mavi", vis, new Vector3(0.35f, 1.68f, -0.1f), new Vector3(0.5f, 0.14f, 0.28f), blue).GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            bar.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            lr = MakeLight(vis, new Vector3(-0.5f, 2.1f, 0), Color.red);
            lb = MakeLight(vis, new Vector3(0.5f, 2.1f, 0), new Color(0.1f, 0.3f, 1f));
            siren = gameObject.AddComponent<AudioSource>();
            siren.clip = AudioSynth.Siren();
            siren.loop = true;
            siren.spatialBlend = 1f;
            siren.minDistance = 12f;
            siren.maxDistance = 220f;
            siren.rolloffMode = AudioRolloffMode.Linear;
            siren.volume = 0.45f;
            siren.dopplerLevel = 0.4f;
        }

        Light MakeLight(Transform p, Vector3 lp, Color c)
        {
            var g = new GameObject("PolisIsik");
            g.transform.SetParent(p, false);
            g.transform.localPosition = lp;
            var l = g.AddComponent<Light>();
            l.type = LightType.Point; l.color = c; l.range = 18f; l.intensity = 3f; l.shadows = LightShadows.None;
            l.enabled = false;
            return l;
        }

        void Update()
        {
            if (red == null) return;
            if (on)
            {
                t += Time.deltaTime;
                bool phase = Mathf.Repeat(t, 0.5f) < 0.25f;
                bool strobe = Mathf.Repeat(t, 0.125f) < 0.08f;
                U.SetEmission(red, phase && strobe ? new Color(6f, 0, 0) : Color.black);
                U.SetEmission(blue, !phase && strobe ? new Color(0, 0.6f, 6f) : Color.black);
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
