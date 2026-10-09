using System.Collections.Generic;
using UnityEngine;

namespace MostWanted
{
    /// <summary>
    /// Oyuncu girdisi (eski Input Manager) + oyuncuya özel mekanikler:
    /// Speedbreaker, kıl payı (near miss), rüzgar tüneli (drafting), nitro doldurma, drag kontrolleri, motor sesi.
    /// </summary>
    public class PlayerDriver : MonoBehaviour
    {
        public CarController car;
        public float speedbreaker = 1f;          // 0..1
        public bool speedbreakerOn;
        public bool drafting;
        public float dragLaneX;                  // drag: hedef şerit (yerel yanal ofset)
        float steerSmooth;
        public EngineAudio engineAudio;
        readonly Dictionary<Rigidbody, float> nearTrack = new Dictionary<Rigidbody, float>();
        readonly HashSet<Rigidbody> touched = new HashSet<Rigidbody>();
        readonly Collider[] overlap = new Collider[32];
        float nearCooldown;
        readonly HashSet<Rigidbody> seenTmp = new HashSet<Rigidbody>();
        readonly List<Rigidbody> doneTmp = new List<Rigidbody>();

        void Awake()
        {
            car = GetComponent<CarController>();
            engineAudio = EngineAudio.Attach(car, true);
            car.onShift = (g, q) =>
            {
                if (car.manualGearbox && q == 2 && Game.I != null) Game.I.Toast("Mükemmel vites!");
                else if (car.manualGearbox && q == 1 && Game.I != null) Game.I.Toast("İyi vites");
            };
        }

        float padCheckT; bool hasPad;
        bool HasPad()
        {
            padCheckT -= Time.unscaledDeltaTime;
            if (padCheckT <= 0f)
            {
                padCheckT = 2f; hasPad = false;
                try { foreach (var n in Input.GetJoystickNames()) if (!string.IsNullOrEmpty(n)) hasPad = true; } catch (System.Exception) { hasPad = false; }
            }
            return hasPad;
        }
        static float SafeAxis(string a) { try { return Input.GetAxisRaw(a); } catch (System.Exception) { return 0f; } }

        void Update()
        {
            var g = Game.I;
            bool blocked = g == null || g.InputBlocked;
            bool drag = g != null && g.race.IsDrag && g.race.Active;
            float th = 0f, st = 0f;
            if (!blocked)
            {
                if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) th += 1f;
                if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) th -= 1f;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) st += 1f;
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) st -= 1f;
            }
            // gamepad: analog direksiyon (ölü bölge + yanıt eğrisi), sol çubuk dikey = gaz/fren
            if (!blocked && st == 0f && HasPad())
            {
                float ax = SafeAxis("Horizontal"), ay = SafeAxis("Vertical");
                if (Mathf.Abs(ax) > 0.12f) st = Mathf.Sign(ax) * Mathf.Pow((Mathf.Abs(ax) - 0.12f) / 0.88f, 1.6f);
                if (th == 0f && Mathf.Abs(ay) > 0.15f) th = Mathf.Sign(ay) * (Mathf.Abs(ay) - 0.15f) / 0.85f;
            }
            // countdown sırasında gaz verilebilir (burnout / kalkış devri)
            if (g != null && g.race.Counting && (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow))) th = 1f;

            float dtU = Time.unscaledDeltaTime;
            steerSmooth = Mathf.MoveTowards(steerSmooth, st, dtU * (Mathf.Abs(st) > Mathf.Abs(steerSmooth) ? 8f : 12f));
            car.steerSens = SaveSystem.Data != null ? SaveSystem.Data.steerSens : 1f;
            car.arcade = SaveSystem.Data == null || SaveSystem.Data.driveStyle == 0;
            car.throttle = th;
            car.manualGearbox = drag;

            if (drag)
            {
                // drag: sadece şerit değiştirme, manuel vites
                if (!blocked)
                {
                    if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) dragLaneX = g.race.DragLane(dragLaneX, -1);
                    if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) dragLaneX = g.race.DragLane(dragLaneX, 1);
                    if (Input.GetKeyDown(KeyCode.E)) car.ShiftUp();
                    if (Input.GetKeyDown(KeyCode.Q)) car.ShiftDown();
                }
                car.steer = g.race.DragSteer(car, dragLaneX);
                car.handbrake = false;
            }
            else
            {
                car.steer = steerSmooth;
                car.handbrake = !blocked && Input.GetKey(KeyCode.Space);
            }
            car.nitroInput = !blocked && (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
            if (blocked && g != null && g.menu != Game.Menu.None) car.handbrake = true;

            // ---- Speedbreaker ----
            bool sbKey = !blocked && !drag && (Input.GetKeyDown(KeyCode.Q) || Input.GetMouseButtonDown(1));
            if (sbKey)
            {
                if (speedbreakerOn) SetSpeedbreaker(false);
                else if (speedbreaker > 0.15f) SetSpeedbreaker(true);
            }
            if (speedbreakerOn)
            {
                speedbreaker -= dtU / 6f;
                if (speedbreaker <= 0f || drag) { speedbreaker = Mathf.Max(0f, speedbreaker); SetSpeedbreaker(false); }
            }

            // ---- Doldurma ----
            float kmh = car.SpeedKmh;
            float dt = Time.deltaTime;
            if (!speedbreakerOn && kmh > 140f) speedbreaker = Mathf.Min(1f, speedbreaker + dt * 0.02f * (kmh / 140f));
            if (!car.nitroActive)
            {
                float fill = 0f;
                if (car.driftAmount > 12f && kmh > 50f) fill += 0.09f;
                if (kmh > 180f) fill += 0.015f;
                if (drafting) fill += 0.08f;
                car.nitro = Mathf.Min(1f, car.nitro + fill * dt);
            }
            if (g != null && !g.race.Active) g.career.DriftTick(car.driftAmount, kmh, dt);

            NearMiss(kmh);
            Draft(kmh);
            if (nearCooldown > 0f) nearCooldown -= dt;

            // ---- Korna ----
            if (engineAudio != null) engineAudio.Horn(!blocked && Input.GetKey(KeyCode.H));
        }

        public void SetSpeedbreaker(bool on)
        {
            speedbreakerOn = on;
            car.gripBoost = on ? 1.3f : 1f;
            car.steerBoost = on ? 1.3f : 1f;
            if (Game.I != null) Game.I.ApplyTimeScale();
        }

        void NearMiss(float kmh)
        {
            if (kmh < 60f) { nearTrack.Clear(); return; }
            int n = Physics.OverlapSphereNonAlloc(transform.position, 7f, overlap);
            var seen = seenTmp; seen.Clear();
            for (int i = 0; i < n; i++)
            {
                var rb = overlap[i].attachedRigidbody;
                if (rb == null || rb == car.rb || seen.Contains(rb)) continue;
                if (rb.GetComponent<CarController>() == null) continue;
                seen.Add(rb);
                float d = U.FlatDist(rb.position, transform.position);
                float prev;
                if (!nearTrack.TryGetValue(rb, out prev) || d < prev) nearTrack[rb] = d;
            }
            var done = doneTmp; done.Clear();
            foreach (var kv in nearTrack) if (kv.Key == null || !seen.Contains(kv.Key)) done.Add(kv.Key);
            foreach (var rb in done)
            {
                float minD = nearTrack[rb];
                nearTrack.Remove(rb);
                if (rb == null || touched.Contains(rb)) { touched.Remove(rb); continue; }
                Vector3 rel = U.Vel(car.rb) - U.Vel(rb);
                if (minD < 3.4f && rel.magnitude * 3.6f > 40f && nearCooldown <= 0f)
                {
                    nearCooldown = 0.4f;
                    car.nitro = Mathf.Min(1f, car.nitro + 0.12f);
                    speedbreaker = Mathf.Min(1f, speedbreaker + 0.1f);
                    if (Game.I != null) { Game.I.Toast("Kıl payı! +Nitro"); Game.I.career.nearMisses++; }
                }
            }
        }

        void Draft(float kmh)
        {
            drafting = false;
            if (kmh < 90f) return;
            RaycastHit hit;
            Vector3 o = transform.position + Vector3.up * 0.8f + transform.forward * 3f;
            if (Physics.SphereCast(o, 1f, transform.forward, out hit, 22f) && hit.rigidbody != null && hit.rigidbody != car.rb)
            {
                if (Vector3.Dot(hit.rigidbody.transform.forward, transform.forward) > 0.8f && U.Vel(hit.rigidbody).magnitude * 3.6f > 60f)
                {
                    drafting = true;
                    car.rb.AddForce(transform.forward * car.rb.mass * 0.8f);
                }
            }
        }

        public void MarkTouched(Rigidbody rb) { if (rb != null) touched.Add(rb); }
    }
}
