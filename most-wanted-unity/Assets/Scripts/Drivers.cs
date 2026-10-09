using System.Collections.Generic;
using UnityEngine;

namespace MostWanted
{
    /// <summary>Klavye ile oyuncu sürüşü (eski Input Manager).</summary>
    public class PlayerDriver : MonoBehaviour
    {
        CarController car;
        float steerSmooth;
        AudioSource engine;

        void Awake()
        {
            car = GetComponent<CarController>();
            engine = gameObject.AddComponent<AudioSource>();
            engine.clip = AudioSynth.Engine();
            engine.loop = true;
            engine.spatialBlend = 0f;
            engine.volume = 0.35f;
            engine.Play();
        }

        void Update()
        {
            bool blocked = Game.I != null && Game.I.InputBlocked;
            float th = 0f, st = 0f;
            if (!blocked)
            {
                if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) th += 1f;
                if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) th -= 1f;
                if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) st += 1f;
                if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) st -= 1f;
            }
            steerSmooth = Mathf.MoveTowards(steerSmooth, st, Time.deltaTime * (Mathf.Abs(st) > 0.01f ? 4f : 6f));
            car.throttle = th;
            car.steer = steerSmooth;
            car.handbrake = !blocked && Input.GetKey(KeyCode.Space);
            car.nitroInput = !blocked && Input.GetKey(KeyCode.LeftShift);
            if (blocked && Game.I.menu != Game.Menu.None) car.handbrake = true;

            if (engine != null)
            {
                float rpm = car.EngineRpm01;
                engine.pitch = Mathf.Lerp(engine.pitch, 0.55f + rpm * 1.25f + (car.nitroActive ? 0.15f : 0f), Time.deltaTime * 10f);
                engine.volume = 0.22f + Mathf.Abs(car.throttle) * 0.18f;
            }
        }
    }

    /// <summary>Hedefe sürüş, engel algılama ve takılma kurtarma içeren temel YZ.</summary>
    public abstract class AIDriver : MonoBehaviour
    {
        public CarController car;
        public float cruiseKmh = 50f;
        protected float stuckTimer, reverseTimer;

        protected virtual void Awake() { car = GetComponent<CarController>(); }

        protected abstract void Think();

        void FixedUpdate()
        {
            if (car == null || car.rb == null || car.rb.isKinematic) return;
            Think();
        }

        protected void DriveTo(Vector3 target, float desiredKmh, bool avoidCars, float steerGain = 1f)
        {
            Vector3 local = transform.InverseTransformPoint(target);
            float angle = Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg;
            float kmh = car.SpeedKmh;

            float desired = desiredKmh * Mathf.Lerp(1f, 0.35f, Mathf.Clamp01(Mathf.Abs(angle) / 90f));
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

            float steer = Mathf.Clamp(angle / 28f * steerGain, -1f, 1f);
            float th = Mathf.Clamp((desired - kmh) / 12f, -1f, 1f);
            if (desired < 1f && kmh < 3f) th = 0f;

            // takılma kurtarma
            if (reverseTimer > 0f)
            {
                reverseTimer -= Time.fixedDeltaTime;
                car.throttle = -1f;
                car.steer = -Mathf.Sign(angle);
                car.handbrake = false;
                return;
            }
            if (th > 0.3f && kmh < 3f) stuckTimer += Time.fixedDeltaTime; else stuckTimer = Mathf.Max(0f, stuckTimer - Time.fixedDeltaTime);
            if (stuckTimer > 2.5f) { stuckTimer = 0f; reverseTimer = 1.6f; }
            if (transform.up.y < 0.3f) car.Unflip();

            car.throttle = th;
            car.steer = steer;
            car.handbrake = false;
        }
    }

    /// <summary>Şeritte giden, kavşakta dönen trafik.</summary>
    public class TrafficDriver : AIDriver
    {
        public int prevNode, nextNode;

        public void Init(int a, int b) { prevNode = a; nextNode = b; }

        protected Vector3 LaneTarget()
        {
            var g = Game.I.city.graph;
            return City.LanePoint(g.nodes[prevNode], g.nodes[nextNode], g.nodes[nextNode], City.LaneOffset);
        }

        protected void AdvanceIfReached(Vector3 tgt)
        {
            if (U.FlatDist(transform.position, tgt) < 10f)
            {
                var adj = Game.I.city.graph.adj[nextNode];
                int nn = prevNode;
                if (adj.Count > 1)
                {
                    for (int k = 0; k < 6; k++)
                    {
                        nn = adj[Random.Range(0, adj.Count)];
                        if (nn != prevNode) break;
                    }
                }
                prevNode = nextNode;
                nextNode = nn;
            }
        }

        protected override void Think()
        {
            Vector3 tgt = LaneTarget();
            AdvanceIfReached(tgt);
            tgt = LaneTarget();
            float d = U.FlatDist(transform.position, tgt);
            float desired = d < 28f ? Mathf.Min(cruiseKmh, 28f) : cruiseKmh;
            DriveTo(tgt, desired, true);
        }
    }

    /// <summary>Devriye/takip eden polis.</summary>
    public class PoliceDriver : TrafficDriver
    {
        public bool pursuing;
        public bool roadblock;
        List<int> path = new List<int>();
        int pathIdx;
        float repath;

        protected override void Think()
        {
            if (!pursuing) { cruiseKmh = 45f; base.Think(); return; }
            var player = Game.I.player;
            if (player == null) { base.Think(); return; }

            Vector3 pp = player.transform.position;
            float dist = U.FlatDist(transform.position, pp);
            bool los = dist < 45f || U.LineOfSight(transform.position + Vector3.up * 2.2f, pp + Vector3.up * 1.2f);
            float maxK = car.maxSpeedKmh;

            if (los)
            {
                Vector3 lead = pp + U.Vel(player.rb) * Mathf.Clamp(dist / 35f, 0f, 1.2f);
                float desired = dist < 15f ? Mathf.Max(player.SpeedKmh + 25f, 40f) : maxK;
                car.nitroInput = dist > 40f && dist < 200f;
                DriveTo(lead, desired, false, 1.3f);
                path.Clear();
            }
            else
            {
                car.nitroInput = false;
                var g = Game.I.city.graph;
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
    }

    /// <summary>Kontrol noktalarını takip eden yarışçı.</summary>
    public class RacerDriver : AIDriver
    {
        public RaceManager.Entry entry;
        public float skill = 1f;

        protected override void Think()
        {
            var rm = Game.I.race;
            if (entry == null || rm == null || !rm.Active || entry.finished)
            {
                car.throttle = 0; car.handbrake = true; return;
            }
            var cps = rm.checkpoints;
            Vector3 t = cps[entry.cp % cps.Count];
            Vector3 nxt = cps[(entry.cp + 1) % cps.Count];
            float d = U.FlatDist(transform.position, t);

            // köşe yaklaşırken yavaşla
            Vector3 dirIn = U.Flat(t - transform.position).normalized;
            Vector3 dirOut = U.Flat(nxt - t).normalized;
            float turn = Vector3.Angle(dirIn, dirOut);
            bool last = !rm.circuit && entry.cp >= cps.Count - 1;
            float desired = car.maxSpeedKmh * skill;
            if (!last && turn > 40f && d < 45f) desired = Mathf.Lerp(75f, 110f, skill - 0.8f);

            // lastik bandı: oyuncudan çok gerideyse hızlan, çok öndeyse yavaşla
            var p = Game.I.player;
            if (p != null)
            {
                float lead = rm.Progress(entry) - rm.PlayerProgress();
                if (lead > 2.5f) desired *= 0.88f;
                else if (lead < -2.5f) { desired *= 1.1f; }
            }

            Vector3 aim = t;
            if (!last && d < 20f) aim = Vector3.Lerp(t, nxt, 0.25f);
            car.nitroInput = turn < 30f && d > 80f;
            DriveTo(aim, desired, false, 1.2f);
        }
    }
}
