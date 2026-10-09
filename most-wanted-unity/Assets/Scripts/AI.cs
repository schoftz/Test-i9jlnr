using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MostWanted
{
    /// <summary>Hedefe sürüş, engel algılama ve takılma kurtarma içeren temel YZ.</summary>
    public abstract class AIDriver : MonoBehaviour
    {
        public CarController car;
        public float cruiseKmh = 50f;
        protected float stuckTimer, reverseTimer;

        protected virtual void Awake() { car = GetComponent<CarController>(); }
        protected abstract void Think();

        protected virtual void FixedUpdate()
        {
            if (Game.I == null || Game.I.world == null) return;
            if (car == null || car.rb == null || car.rb.isKinematic) return;
            if (car.disabled) { car.throttle = 0f; car.handbrake = true; return; }
            Think();
        }

        protected void DriveTo(Vector3 target, float desiredKmh, bool avoidCars, float steerGain = 1f)
        {
            Vector3 local = transform.InverseTransformPoint(target);
            float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float kmh = car.SpeedKmh;
            float desired = desiredKmh * Mathf.Lerp(1f, 0.3f, Mathf.Clamp01((Mathf.Abs(angle) - 5f) / 80f));
            if (avoidCars)
            {
                RaycastHit hit;
                Vector3 origin = transform.position + Vector3.up * 0.9f + transform.forward * 2.8f;
                float dist = 7f + kmh * 0.35f;
                if (Physics.SphereCast(origin, 1.1f, transform.forward, out hit, dist) && hit.rigidbody != null && hit.rigidbody != car.rb)
                {
                    if (hit.distance < 5f) desired = 0f;
                    else desired = Mathf.Min(desired, hit.distance * 2.2f);
                }
            }
            float steer = Mathf.Clamp(angle / 25f * steerGain, -1f, 1f);
            float th = Mathf.Clamp((desired - kmh) / 10f, -1f, 1f);
            if (desired < 1f && kmh < 3f) th = 0f;

            if (reverseTimer > 0f)
            {
                reverseTimer -= Time.fixedDeltaTime;
                car.throttle = -1f; car.steer = -Mathf.Sign(angle); car.handbrake = false;
                return;
            }
            if (th > 0.3f && kmh < 3f) stuckTimer += Time.fixedDeltaTime; else stuckTimer = Mathf.Max(0f, stuckTimer - Time.fixedDeltaTime);
            if (stuckTimer > 2.5f) { stuckTimer = 0f; reverseTimer = 1.5f; }
            car.throttle = th; car.steer = steer; car.handbrake = false;
        }
    }

    /// <summary>Şeritte giden, kavşakta dönen trafik. Uzakta kinematik (ucuz) moda geçer.</summary>
    public class TrafficDriver : AIDriver
    {
        public int prevNode, nextNode;
        public bool far;

        public void Init(int a, int b) { prevNode = a; nextNode = b; }

        protected static bool WorldReady { get { return Game.I != null && Game.I.world != null && Game.I.world.graph != null && Game.I.world.graph.nodes.Count > 1; } }

        protected Vector3 LaneTarget() { return WorldReady ? Game.I.world.graph.LanePoint(prevNode, nextNode) : transform.position; }

        protected void AdvanceIfReached(Vector3 tgt, float radius = 9f)
        {
            if (U.FlatDist(transform.position, tgt) < radius)
            {
                var adj = Game.I.world.graph.adj[nextNode];
                int nn = prevNode;
                if (adj.Count > 1)
                    for (int k = 0; k < 6; k++) { nn = adj[Random.Range(0, adj.Count)]; if (nn != prevNode) break; }
                prevNode = nextNode;
                nextNode = nn;
            }
        }

        int frame;
        protected override void Think()
        {
            if (GetType() == typeof(TrafficDriver) && ((++frame + GetHashCode()) & 1) == 0) return; // kademeli YZ
            Vector3 tgt = LaneTarget();
            AdvanceIfReached(tgt);
            tgt = LaneTarget();
            float d = U.FlatDist(transform.position, tgt);
            float want = d < 25f ? Mathf.Min(cruiseKmh, 30f) : cruiseKmh;
            // kırmızı ışıkta dur (polis takipteyken uymaz)
            if (MapDressing.I != null && d < 32f && d > 9f && MapDressing.I.IsRed(prevNode, nextNode)) want = 0f;
            DriveTo(tgt, want, true);
        }

        /// <summary>Uzak trafik: fizik kapalı, şerit boyunca kayar.</summary>
        public void SetFar(bool f)
        {
            if (far == f) return;
            far = f;
            car.rb.isKinematic = f;
            if (!f) car.SetSpeed(cruiseKmh * 0.8f);
        }

        void Update()
        {
            if (!far || !WorldReady) return;
            Vector3 tgt = LaneTarget();
            AdvanceIfReached(tgt, 3f);
            tgt = LaneTarget();
            Vector3 p = transform.position;
            Vector3 to = tgt - p; to.y = 0;
            if (to.sqrMagnitude < 0.01f) return;
            Vector3 np = Vector3.MoveTowards(p, new Vector3(tgt.x, tgt.y + 0.05f, tgt.z), cruiseKmh / 3.6f * Time.deltaTime);
            Quaternion nr = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(to.normalized), Time.deltaTime * 4f);
            car.rb.MovePosition(np);
            car.rb.MoveRotation(nr);
        }
    }

    /// <summary>Polis: devriye, takip, kutulama ve çarpma.</summary>
    public class PoliceDriver : TrafficDriver
    {
        public bool pursuing, roadblock;
        public string role = "patrol";
        public int slot;
        List<int> path = new List<int>();
        int pathIdx;
        float repath;

        protected override void Think()
        {
            if (car.Damage0Check()) return;
            if (!pursuing) { cruiseKmh = 45f; base.Think(); return; }
            var player = Game.I.player;
            if (player == null) { base.Think(); return; }
            Vector3 pp = player.transform.position;
            Vector3 pv = U.Vel(player.rb);
            float dist = U.FlatDist(transform.position, pp);
            bool los = dist < 45f || U.LineOfSight(transform.position + Vector3.up * 2.2f, pp + Vector3.up * 1.2f);
            float maxK = car.topSpeed * 1.05f;

            if (los)
            {
                Vector3 pf = U.Flat(player.transform.forward).normalized;
                Vector3 pr = new Vector3(pf.z, 0, -pf.x);
                Vector3 aim;
                bool ram = role == "suv" || Game.I.police.Stars >= 4;
                if (dist > 30f) aim = pp + pv * Mathf.Clamp(dist / 40f, 0f, 1.5f);
                else if (ram) aim = pp + pv * 0.25f;
                else
                {
                    // kutulama pozisyonları: arka, sol, sağ, ön
                    Vector3 off = slot % 4 == 0 ? -pf * 4f : slot % 4 == 1 ? -pr * 3.2f + pf * 1f : slot % 4 == 2 ? pr * 3.2f + pf * 1f : pf * 8f;
                    aim = pp + off + pv * 0.35f;
                }
                float desired = dist < 12f ? Mathf.Max(player.SpeedKmh + (ram ? 25f : 8f), 30f) : maxK;
                car.nitroInput = dist > 50f && dist < 250f;
                DriveTo(aim, desired, false, 1.4f);
                path.Clear();
            }
            else
            {
                car.nitroInput = false;
                var g = Game.I.world.graph;
                repath -= Time.fixedDeltaTime;
                if (path.Count == 0 || repath <= 0f)
                {
                    repath = 1.5f;
                    path = g.Path(g.Nearest(transform.position), g.Nearest(pp));
                    pathIdx = 0;
                }
                if (path.Count == 0) { DriveTo(pp, maxK * 0.7f, false); return; }
                while (pathIdx < path.Count - 1 && U.FlatDist(transform.position, g.nodes[path[pathIdx]]) < 12f) pathIdx++;
                Vector3 t = g.nodes[path[pathIdx]];
                float dn = U.FlatDist(transform.position, t);
                DriveTo(t, dn < 35f ? 70f : maxK * 0.85f, false, 1.2f);
            }
        }

        void OnCollisionEnter(Collision c)
        {
            if (roadblock || c.rigidbody == null) return;
            float rel = c.relativeVelocity.magnitude;
            if (c.rigidbody.GetComponent<BreakerPiece>() != null && rel > 3f) car.Damage(250f);
        }
    }

    public static class CarDamageExt
    {
        /// <summary>Devrilen/sıkışan polis devre dışı kalır.</summary>
        public static bool Damage0Check(this CarController car)
        {
            if (Vector3.Angle(car.transform.up, Vector3.up) > 70f) car.Damage(500f);
            return car.disabled;
        }
    }

    /// <summary>Yarış rotasını takip eden rakip.</summary>
    public class RacerDriver : AIDriver
    {
        [System.NonSerialized] public RaceManager.Entry entry;
        public float skill = 1f;
        public float dragLaneX;

        protected override void Think()
        {
            var rm = Game.I.race;
            if (entry == null || rm == null || !rm.Active || entry.finished) { car.throttle = 0; car.handbrake = true; return; }
            if (rm.IsDrag)
            {
                car.throttle = 1f;
                car.steer = rm.DragSteer(car, dragLaneX);
                car.nitroInput = car.gear >= 3;
                if (car.manualGearbox && car.rpm > car.redline * Random.Range(0.86f, 0.97f) * Mathf.Lerp(0.95f, 1f, skill)) car.ShiftUp();
                return;
            }
            var route = rm.def.route;
            int i = Mathf.Min(entry.idx, route.Count - 1);
            Vector3 t = route[i];
            Vector3 nxt = route[rm.circuit ? (i + 1) % route.Count : Mathf.Min(i + 1, route.Count - 1)];
            float d = U.FlatDist(transform.position, t);
            Vector3 dirIn = U.Flat(t - transform.position).normalized;
            Vector3 dirOut = U.Flat(nxt - t).normalized;
            float turn = dirOut.sqrMagnitude > 0.1f ? Vector3.Angle(dirIn, dirOut) : 0f;
            float desired = car.topSpeed * skill;
            if (turn > 40f && d < 55f) desired = Mathf.Lerp(80f, 115f, Mathf.Clamp01(skill - 0.8f) * 3f);
            float lead = rm.Progress(entry) - rm.PlayerProgress();
            if (lead > 2.5f) desired *= 0.86f;
            else if (lead < -2.5f) { desired *= 1.12f; car.nitro = Mathf.Min(1f, car.nitro + Time.fixedDeltaTime * 0.05f); }
            Vector3 aim = d < 20f ? Vector3.Lerp(t, nxt, 0.25f) : t;
            car.nitroInput = turn < 25f && d > 90f;
            DriveTo(aim, desired, false, 1.2f);
        }
    }

    public class TrafficManager : MonoBehaviour
    {
        public int count = 14;
        public readonly List<TrafficDriver> cars = new List<TrafficDriver>();
        float check;

        void Update()
        {
            var g = Game.I;
            if (g.player == null || g.world.graph.nodes.Count < 4) return;
            check -= Time.deltaTime;
            if (check > 0f) return;
            check = 0.4f;
            Vector3 pp = g.player.transform.position;
            cars.RemoveAll(c => c == null);
            foreach (var c in cars)
            {
                float d = U.FlatDist(c.transform.position, pp);
                if (d > 400f) Respawn(c, pp);
                else c.SetFar(d > 120f);
            }
            if (cars.Count < count) Spawn(pp);
            while (cars.Count > count) { Destroy(cars[cars.Count - 1].gameObject); cars.RemoveAt(cars.Count - 1); }
        }

        void Spawn(Vector3 near)
        {
            var list = Catalog.Traffic;
            var def = list[Random.Range(0, list.Count)];
            var col = Catalog.Paints[Random.Range(0, Catalog.Paints.Length)];
            var car = CarFactory.Build(def, col, new Vector3(0, -200, 0), Quaternion.identity, CarRole.Traffic, null, "Trafik");
            // trafik: sakin sürüş
            car.peakTorque *= 0.6f;
            car.topSpeed = Mathf.Min(car.topSpeed, 140f);
            foreach (var r in car.GetComponentsInChildren<Renderer>(true))
                if (r.gameObject.layer != U.IconLayer) r.gameObject.layer = OptimizationManager.TrafficLayer;
            var d = car.gameObject.AddComponent<TrafficDriver>();
            d.cruiseKmh = Random.Range(40f, 65f);
            cars.Add(d);
            Respawn(d, near);
        }

        public void Respawn(TrafficDriver d, Vector3 near)
        {
            var graph = Game.I.world.graph;
            int a = graph.RandomNodeAround(near, 90f, 330f);
            var adj = graph.adj[a];
            if (adj.Count == 0) return;
            int b = adj[Random.Range(0, adj.Count)];
            Vector3 pa = graph.nodes[a], pb = graph.nodes[b];
            Vector3 lp = graph.LanePoint(a, b);
            Vector3 pos = Vector3.Lerp(pa + (lp - pb), lp, 0.3f) + Vector3.up * 0.4f;
            d.car.Teleport(pos, Quaternion.LookRotation(U.Flat(pb - pa).normalized));
            d.Init(a, b);
            d.far = false; d.car.rb.isKinematic = false;
            d.SetFar(U.FlatDist(pos, near) > 120f);
        }

        public void ClearAround(Vector3 p, float r)
        {
            foreach (var c in cars) if (c != null && U.FlatDist(c.transform.position, p) < r) Respawn(c, p + new Vector3(500, 0, 500));
        }
    }

    /// <summary>Polis helikopteri: oyuncuyu takip eder, projektörle aydınlatır, görüş sağlar.</summary>
    public class Helicopter : MonoBehaviour
    {
        Transform rotor, tailRotor;
        Light spot;
        Vector3 vel;
        AudioSource snd;
        public bool SeesPlayer { get; private set; }

        public static Helicopter Create(Vector3 pos)
        {
            var go = new GameObject("PolisHelikopteri");
            go.transform.position = pos;
            var h = go.AddComponent<Helicopter>();
            var dark = U.Mat(new Color(0.1f, 0.12f, 0.18f), 0.6f, 0.3f);
            var white = U.Mat(new Color(0.9f, 0.9f, 0.9f), 0.6f);
            U.Prim(PrimitiveType.Capsule, "Govde", go.transform, Vector3.zero, new Vector3(2.2f, 2f, 4.5f), dark).transform.localRotation = Quaternion.Euler(90, 0, 0);
            U.Prim(PrimitiveType.Cube, "Kuyruk", go.transform, new Vector3(0, 0.3f, -4.5f), new Vector3(0.4f, 0.4f, 5f), dark);
            U.Prim(PrimitiveType.Cube, "Serit", go.transform, new Vector3(0, 0, 0.5f), new Vector3(2.25f, 0.3f, 2f), white);
            U.Prim(PrimitiveType.Cube, "KizakL", go.transform, new Vector3(-1f, -1.3f, 0), new Vector3(0.12f, 0.12f, 4f), dark);
            U.Prim(PrimitiveType.Cube, "KizakR", go.transform, new Vector3(1f, -1.3f, 0), new Vector3(0.12f, 0.12f, 4f), dark);
            h.rotor = new GameObject("Rotor").transform; h.rotor.SetParent(go.transform, false); h.rotor.localPosition = new Vector3(0, 1.3f, 0);
            U.Prim(PrimitiveType.Cube, "Pervane1", h.rotor, Vector3.zero, new Vector3(11f, 0.06f, 0.4f), dark);
            U.Prim(PrimitiveType.Cube, "Pervane2", h.rotor, Vector3.zero, new Vector3(0.4f, 0.06f, 11f), dark);
            h.tailRotor = new GameObject("KuyrukRotor").transform; h.tailRotor.SetParent(go.transform, false); h.tailRotor.localPosition = new Vector3(0.3f, 0.6f, -6.8f);
            U.Prim(PrimitiveType.Cube, "KP", h.tailRotor, Vector3.zero, new Vector3(0.05f, 2f, 0.25f), dark);
            var lg = new GameObject("Projektor"); lg.transform.SetParent(go.transform, false); lg.transform.localPosition = new Vector3(0, -1.2f, 1.5f);
            h.spot = lg.AddComponent<Light>();
            h.spot.type = LightType.Spot; h.spot.range = 140f; h.spot.spotAngle = 22f; h.spot.intensity = 18f; h.spot.color = new Color(1f, 0.97f, 0.9f);
            h.spot.shadows = LightShadows.None;
            U.Icon(go.transform, new Color(1f, 0.2f, 0.2f), 12f);
            h.snd = go.AddComponent<AudioSource>();
            h.snd.clip = AudioSynth.Rotor(); h.snd.loop = true; h.snd.spatialBlend = 1f; h.snd.minDistance = 20f; h.snd.maxDistance = 400f; h.snd.volume = 0.5f;
            h.snd.Play();
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;
            return h;
        }

        void Update()
        {
            var p = Game.I.player;
            if (p == null) return;
            Vector3 pp = p.transform.position;
            Vector3 pv = U.Flat(U.Vel(p.rb));
            Vector3 goal = pp + pv * 1.2f - (pv.sqrMagnitude > 1f ? pv.normalized * 25f : Vector3.forward * 25f) + Vector3.up * 55f;
            transform.position = Vector3.SmoothDamp(transform.position, goal, ref vel, 2.2f, 75f);
            Vector3 look = U.Flat(pp - transform.position);
            if (look.sqrMagnitude > 1f) transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look.normalized) * Quaternion.Euler(Mathf.Clamp(vel.magnitude * 0.4f, 0, 15f), 0, 0), Time.deltaTime * 1.5f);
            rotor.Rotate(0, 1400f * Time.deltaTime, 0, Space.Self);
            snd.volume = 0.5f * AudioBus.Get(AudioBus.Bus.Efekt);
            tailRotor.Rotate(2000f * Time.deltaTime, 0, 0, Space.Self);
            spot.transform.LookAt(pp);
            float hd = U.FlatDist(transform.position, pp);
            SeesPlayer = hd < 130f && !Game.I.world.InHiding(pp) && U.LineOfSight(spot.transform.position, pp + Vector3.up * 1.2f);
        }
    }

    /// <summary>Çivili şerit: oyuncunun lastiklerini patlatır.</summary>
    public class SpikeStrip : MonoBehaviour
    {
        void OnTriggerEnter(Collider other)
        {
            var rb = other.attachedRigidbody;
            if (rb == null) return;
            var car = rb.GetComponent<CarController>();
            if (car == null || !car.isPlayer || car.TiresBlown) return;
            car.spikeTimer = 14f;
            SaveSystem.Data.spikesHit++;
            Game.I.Toast("LASTİKLER PATLADI! Yavaşla, kaçmak zorlaştı!");
            Game.I.police.Radio("Çivili şerit işe yaradı, şüphelinin lastikleri patladı!");
            Game.I.rig.Shake(0.6f);
        }
    }

    /// <summary>Pursuit Breaker parçası: düşerken polisleri devre dışı bırakır.</summary>
    public class BreakerPiece : MonoBehaviour { }

    /// <summary>Pursuit Breaker (su kulesi / benzinlik tentesi): oyuncu çarpınca çöker.</summary>
    public class PursuitBreaker : MonoBehaviour
    {
        public int kind;
        readonly List<Rigidbody> pieces = new List<Rigidbody>();
        bool triggered;
        float resetTimer;
        Vector3 origin;

        public static PursuitBreaker Create(Vector3 pos, int kind, Transform parent)
        {
            var go = new GameObject(kind == 0 ? "PB_SuKulesi" : "PB_Benzinlik");
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var pb = go.AddComponent<PursuitBreaker>();
            pb.kind = kind; pb.origin = pos;
            pb.Rebuild();
            var trig = go.AddComponent<BoxCollider>();
            trig.isTrigger = true;
            trig.size = kind == 0 ? new Vector3(14f, 6f, 14f) : new Vector3(18f, 6f, 12f);
            trig.center = new Vector3(0, 3f, 0);
            U.Icon(go.transform, new Color(1f, 0.5f, 0f), 6f);
            return pb;
        }

        void Rebuild()
        {
            foreach (var p in pieces) if (p != null) Destroy(p.gameObject);
            pieces.Clear();
            triggered = false;
            var metal = U.Mat(new Color(0.55f, 0.5f, 0.45f), 0.4f, 0.6f);
            var red = U.Mat(new Color(0.75f, 0.15f, 0.1f), 0.5f, 0.2f);
            if (kind == 0)
            {
                for (int x = -1; x <= 1; x += 2)
                    for (int z = -1; z <= 1; z += 2)
                        Piece(new Vector3(x * 3.5f, 7f, z * 3.5f), new Vector3(0.6f, 14f, 0.6f), metal, 800f);
                Piece(new Vector3(0, 17f, 0), new Vector3(9f, 6f, 9f), red, 6000f);
                Piece(new Vector3(0, 20.5f, 0), new Vector3(7f, 1f, 7f), metal, 1500f);
            }
            else
            {
                for (int x = -1; x <= 1; x += 2)
                    for (int z = -1; z <= 1; z += 2)
                        Piece(new Vector3(x * 6f, 3f, z * 3.5f), new Vector3(0.5f, 6f, 0.5f), metal, 500f);
                Piece(new Vector3(0, 6.4f, 0), new Vector3(16f, 0.8f, 10f), red, 5000f);
                Piece(new Vector3(-3f, 1f, 0), new Vector3(1.2f, 2f, 0.8f), metal, 400f);
                Piece(new Vector3(3f, 1f, 0), new Vector3(1.2f, 2f, 0.8f), metal, 400f);
            }
        }

        void Piece(Vector3 lp, Vector3 size, Material m, float mass)
        {
            var g = U.Prim(PrimitiveType.Cube, "Parca", transform, lp, size, m, true);
            var rb = g.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.isKinematic = true;
            g.AddComponent<BreakerPiece>();
            pieces.Add(rb);
        }

        void OnTriggerEnter(Collider other)
        {
            if (triggered) return;
            var rb = other.attachedRigidbody;
            if (rb == null) return;
            var car = rb.GetComponent<CarController>();
            if (car == null || !car.isPlayer || car.SpeedKmh < 45f) return;
            Collapse(U.Vel(rb));
        }

        void Collapse(Vector3 dir)
        {
            triggered = true;
            resetTimer = 45f;
            foreach (var p in pieces)
            {
                p.isKinematic = false;
                p.AddExplosionForce(p.mass * 6f, transform.position - U.Flat(dir).normalized * 4f, 25f, 1f, ForceMode.Impulse);
            }
            Game.I.Toast("PURSUIT BREAKER!");
            Game.I.rig.Shake(1f);
            Game.I.police.OnBreaker(transform.position);
        }

        void Update()
        {
            if (!triggered) return;
            resetTimer -= Time.deltaTime;
            if (resetTimer <= 0f && U.FlatDist(Game.I.player.transform.position, origin) > 120f) Rebuild();
        }
    }
}
