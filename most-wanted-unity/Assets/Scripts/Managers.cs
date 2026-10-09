using System.Collections.Generic;
using UnityEngine;

namespace MostWanted
{
    public class TrafficManager : MonoBehaviour
    {
        public int count = 18;
        readonly List<TrafficDriver> cars = new List<TrafficDriver>();
        float check;

        void Update()
        {
            var g = Game.I;
            if (g.player == null) return;
            check -= Time.deltaTime;
            if (check > 0f) return;
            check = 0.5f;
            Vector3 pp = g.player.transform.position;
            cars.RemoveAll(c => c == null);
            foreach (var c in cars)
                if (U.FlatDist(c.transform.position, pp) > 380f) Respawn(c, pp);
            if (cars.Count < count) Spawn(pp);
        }

        void Spawn(Vector3 near)
        {
            var spec = Catalog.Traffic;
            var col = Catalog.Paints[Random.Range(0, Catalog.Paints.Length)] * Random.Range(0.6f, 1f);
            col.a = 1f;
            var car = CarFactory.Build(spec, col, new Vector3(0, -100, 0), Quaternion.identity, CarRole.Traffic, "Trafik");
            var d = car.gameObject.AddComponent<TrafficDriver>();
            d.cruiseKmh = Random.Range(38f, 60f);
            cars.Add(d);
            Respawn(d, near);
        }

        void Respawn(TrafficDriver d, Vector3 near)
        {
            var graph = Game.I.city.graph;
            int a = graph.RandomNodeAround(near, 80f, 300f);
            var adj = graph.adj[a];
            int b = adj[Random.Range(0, adj.Count)];
            Vector3 pa = graph.nodes[a], pb = graph.nodes[b];
            Vector3 pos = City.LanePoint(pa, pb, Vector3.Lerp(pa, pb, 0.3f), City.LaneOffset) + Vector3.up * 0.6f;
            if (Physics.CheckSphere(pos + Vector3.up * 0.6f, 2.5f, ~(1 << U.IconLayer), QueryTriggerInteraction.Ignore))
            {
                // kalabalık: biraz ilerisi
                pos = City.LanePoint(pa, pb, Vector3.Lerp(pa, pb, 0.6f), City.LaneOffset) + Vector3.up * 0.6f;
            }
            d.car.Teleport(pos, Quaternion.LookRotation(U.Flat(pb - pa).normalized));
            d.Init(a, b);
        }
    }

    /// <summary>Aranma seviyesi, polis takibi, barikatlar, yakalanma ve kaçış.</summary>
    public class PoliceManager : MonoBehaviour
    {
        public const float SpeedLimit = 95f;
        public bool pursuit;
        public float heat;          // 0..5
        public float cooldown;      // 0..1
        public float bustProgress;  // 0..1
        public int bounty;
        public int Stars { get { return pursuit ? Mathf.Clamp(Mathf.FloorToInt(heat), 1, 5) : 0; } }
        public bool Seen { get; private set; }

        readonly List<PoliceDriver> cops = new List<PoliceDriver>();
        readonly List<GameObject> roadblocks = new List<GameObject>();
        float spawnTimer, rbTimer, grace, bountyAcc, hitCool, checkTimer;
        const int Patrols = 4;

        public int CopCount { get { return cops.Count; } }

        void Update()
        {
            var g = Game.I;
            if (g.player == null) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            grace -= dt; hitCool -= dt;
            cops.RemoveAll(c => c == null);
            Vector3 pp = g.player.transform.position;

            if (g.race.Active)
            {
                if (pursuit) EndPursuit(false);
            }

            int want = pursuit ? Mathf.Min(1 + Stars * 2, 10) : Patrols;
            spawnTimer -= dt;
            if (cops.Count < want && spawnTimer <= 0f)
            {
                spawnTimer = pursuit ? 2.5f : 1f;
                SpawnCop(pp, pursuit);
            }

            checkTimer -= dt;
            if (checkTimer <= 0f)
            {
                checkTimer = 0.5f;
                // uzak polisleri geri dönüştür
                for (int i = cops.Count - 1; i >= 0; i--)
                {
                    float d = U.FlatDist(cops[i].transform.position, pp);
                    if ((!pursuit && d > 450f) || (pursuit && d > 600f) || (!pursuit && cops.Count > Patrols && d > 120f))
                    {
                        Destroy(cops[i].gameObject); cops.RemoveAt(i);
                    }
                }
            }

            float kmh = g.player.SpeedKmh;
            if (!pursuit)
            {
                if (grace > 0f || g.race.Active) return;
                foreach (var c in cops)
                {
                    float d = U.FlatDist(c.transform.position, pp);
                    if (d < 60f && kmh > SpeedLimit && U.LineOfSight(c.transform.position + Vector3.up * 2.2f, pp + Vector3.up * 1.2f))
                    {
                        StartPursuit();
                        g.Toast("Hız ihlali! Polis peşinde!");
                        break;
                    }
                }
                return;
            }

            // --- Takip ---
            heat = Mathf.Min(5f, heat + dt / 40f);
            bountyAcc += dt * Stars * 20f;
            if (bountyAcc >= 1f) { int add = Mathf.FloorToInt(bountyAcc); bounty += add; bountyAcc -= add; }

            bool seen = false;
            float nearest = float.MaxValue;
            foreach (var c in cops)
            {
                c.pursuing = !c.roadblock;
                var pl = c.GetComponent<PoliceLights>(); if (pl) pl.on = true;
                float d = U.FlatDist(c.transform.position, pp);
                if (d < nearest) nearest = d;
                if (d < 40f || (d < 160f && U.LineOfSight(c.transform.position + Vector3.up * 2.2f, pp + Vector3.up * 1.2f))) seen = true;
            }
            Seen = seen;
            if (seen) cooldown = Mathf.Max(0f, cooldown - dt * 0.5f);
            else
            {
                cooldown += dt / (8f + Stars * 2.5f);
                if (cooldown >= 1f) { Escape(); return; }
            }

            // yakalanma
            if (kmh < 7f && nearest < 9f) bustProgress += dt / 3f;
            else bustProgress = Mathf.Max(0f, bustProgress - dt * 0.7f);
            if (bustProgress >= 1f) { Busted(); return; }

            // barikat
            if (Stars >= 3)
            {
                rbTimer -= dt;
                if (rbTimer <= 0f) { rbTimer = 35f; SpawnRoadblock(g.player); }
            }
        }

        public void OnPlayerHit(Collision c)
        {
            if (c.rigidbody == null || hitCool > 0f || Game.I.race.Active) return;
            var cop = c.rigidbody.GetComponent<PoliceDriver>();
            if (cop == null || c.relativeVelocity.magnitude < 4f) return;
            hitCool = 1f;
            if (!pursuit) { StartPursuit(); Game.I.Toast("Polise çarptın! Takip başladı!"); }
            heat = Mathf.Min(5f, heat + 0.35f);
            bounty += 500;
            cooldown = 0f;
        }

        void StartPursuit()
        {
            pursuit = true;
            heat = Mathf.Max(heat, 1f);
            cooldown = 0f;
            bustProgress = 0f;
            bounty = 0;
            rbTimer = 12f;
        }

        void Escape()
        {
            int reward = bounty;
            SaveSystem.Data.escapes++;
            SaveSystem.AddMoney(reward);
            Game.I.Toast("KAÇTIN! Ödül: " + U.Money(reward) + "  (" + Stars + " yıldız)");
            Game.I.Beep(true);
            EndPursuit(true);
        }

        void Busted()
        {
            int fine = 800 * Stars + bounty / 4;
            fine = Mathf.Min(fine, SaveSystem.Data.money);
            SaveSystem.Data.busted++;
            SaveSystem.AddMoney(-fine);
            Game.I.Toast("YAKALANDIN! Ceza: " + U.Money(fine));
            EndPursuit(false);
            // yakındaki polisleri kaldır
            Vector3 pp = Game.I.player.transform.position;
            for (int i = cops.Count - 1; i >= 0; i--)
                if (U.FlatDist(cops[i].transform.position, pp) < 150f) { Destroy(cops[i].gameObject); cops.RemoveAt(i); }
        }

        public void EndPursuit(bool escaped)
        {
            pursuit = false;
            heat = 0f; cooldown = 0f; bustProgress = 0f; bounty = 0; bountyAcc = 0f;
            grace = escaped ? 10f : 15f;
            foreach (var c in cops)
            {
                if (c == null) continue;
                c.pursuing = false;
                var pl = c.GetComponent<PoliceLights>(); if (pl) pl.on = false;
            }
            foreach (var r in roadblocks) if (r != null) Destroy(r);
            roadblocks.Clear();
            for (int i = cops.Count - 1; i >= 0; i--) if (cops[i].roadblock) { Destroy(cops[i].gameObject); cops.RemoveAt(i); }
        }

        void SpawnCop(Vector3 near, bool chasing)
        {
            var graph = Game.I.city.graph;
            int a = graph.RandomNodeAround(near, chasing ? 110f : 120f, chasing ? 260f : 380f);
            var adj = graph.adj[a];
            int b = adj[Random.Range(0, adj.Count)];
            Vector3 pa = graph.nodes[a], pb = graph.nodes[b];
            Vector3 pos = City.LanePoint(pa, pb, Vector3.Lerp(pa, pb, 0.35f), City.LaneOffset) + Vector3.up * 0.6f;
            if (Physics.CheckSphere(pos + Vector3.up * 0.6f, 2.5f, ~(1 << U.IconLayer), QueryTriggerInteraction.Ignore)) return;
            var car = CarFactory.BuildPolice(pos, Quaternion.LookRotation(U.Flat(pb - pa).normalized));
            var d = car.gameObject.AddComponent<PoliceDriver>();
            d.Init(a, b);
            d.pursuing = chasing;
            car.GetComponent<PoliceLights>().on = chasing;
            cops.Add(d);
        }

        void SpawnRoadblock(CarController player)
        {
            var graph = Game.I.city.graph;
            Vector3 pp = player.transform.position;
            Vector3 fwd = U.Flat(U.Vel(player.rb));
            if (fwd.sqrMagnitude < 4f) fwd = U.Flat(player.transform.forward);
            fwd.Normalize();
            int best = -1; float bd = float.MaxValue;
            for (int i = 0; i < graph.nodes.Count; i++)
            {
                Vector3 to = graph.nodes[i] - pp; to.y = 0;
                float d = to.magnitude;
                if (d < 90f || d > 200f) continue;
                if (Vector3.Dot(to / d, fwd) < 0.8f) continue;
                if (d < bd) { bd = d; best = i; }
            }
            if (best < 0) return;
            Vector3 n = graph.nodes[best];
            Vector3 dir = U.Flat(n - pp).normalized;
            // yönü en yakın eksene hizala
            Vector3 axis = Mathf.Abs(dir.x) > Mathf.Abs(dir.z) ? new Vector3(Mathf.Sign(dir.x), 0, 0) : new Vector3(0, 0, Mathf.Sign(dir.z));
            Vector3 side = new Vector3(axis.z, 0, -axis.x);
            Vector3 center = n - axis * 14f;
            for (int k = -1; k <= 1; k += 2)
            {
                var car = CarFactory.BuildPolice(center + side * (k * 4.2f) + Vector3.up * 0.3f, Quaternion.LookRotation(side * k));
                car.rb.isKinematic = true;
                var d = car.gameObject.AddComponent<PoliceDriver>();
                d.roadblock = true;
                car.GetComponent<PoliceLights>().on = true;
                cops.Add(d);
            }
            var bar = U.Prim(PrimitiveType.Cube, "Barikat", null, center - axis * 4f + Vector3.up * 0.6f, Vector3.one, U.Emissive(new Color(1f, 0.45f, 0f), new Color(0.6f, 0.25f, 0f)), true);
            bar.transform.localScale = new Vector3(Mathf.Abs(side.x) * 12f + 0.6f, 1.2f, Mathf.Abs(side.z) * 12f + 0.6f);
            roadblocks.Add(bar);
            Game.I.Toast("Önünde barikat var!");
        }
    }

    /// <summary>Sprint ve tur yarışları.</summary>
    public class RaceManager : MonoBehaviour
    {
        public class Entry
        {
            public CarController car;
            public string name;
            public int cp, lap;
            public bool finished;
            public int place;
        }

        public class RaceDef
        {
            public string name;
            public bool circuit;
            public int laps;
            public int prize;
            public List<Vector3> points;
        }

        public List<RaceDef> races = new List<RaceDef>();
        public List<Vector3> checkpoints = new List<Vector3>();
        public List<Entry> entries = new List<Entry>();
        public RaceDef current;
        public bool circuit;
        public int laps;
        public float countdown;
        public float raceTime;
        public bool Active { get { return current != null; } }
        public bool Counting { get { return Active && countdown > 0f; } }
        Entry playerEntry;
        GameObject gate;
        int lastBeep;
        float endTimer;

        public void Setup(City c)
        {
            var G = c.grid;
            var gr = c.graph;
            races.Add(MakeDef("Liman Sprinti", false, 1, 3000, gr, new[] { G[0, 1], G[6, 1], G[6, 4], G[3, 4], G[3, 6] }, c));
            races.Add(MakeDef("Merkez Turu", true, 2, 4500, gr, new[] { G[1, 1], G[4, 1], G[4, 4], G[1, 4] }, c));
            races.Add(MakeDef("Gece Ekspresi", false, 1, 5500, gr, new[] { G[6, 6], G[6, 3], G[2, 3], G[2, 0], G[0, 0] }, c));
            var ring = new RaceDef { name = "Çevre Yolu Kupası", circuit = true, laps = 1, prize = 8000, points = new List<Vector3>() };
            foreach (int n in c.ringLoop) ring.points.Add(gr.nodes[n]);
            races.Add(ring);
        }

        RaceDef MakeDef(string name, bool circuitRace, int lapCount, int prize, RoadGraph gr, int[] corners, City c)
        {
            var d = new RaceDef { name = name, circuit = circuitRace, laps = lapCount, prize = prize, points = new List<Vector3>() };
            int count = corners.Length + (circuitRace ? 1 : 0);
            for (int k = 0; k < count - 1; k++)
            {
                var p = gr.Path(corners[k % corners.Length], corners[(k + 1) % corners.Length]);
                for (int i = (k == 0 ? 0 : 1); i < p.Count; i++) d.points.Add(gr.nodes[p[i]]);
            }
            if (circuitRace && d.points.Count > 1) d.points.RemoveAt(d.points.Count - 1); // başlangıç tekrarını sil
            return d;
        }

        public float Progress(Entry e)
        {
            if (e.finished) return 100000f - e.place;
            Vector3 t = checkpoints[e.cp % checkpoints.Count];
            return e.lap * checkpoints.Count + e.cp - U.FlatDist(e.car.transform.position, t) / 1000f;
        }

        public float PlayerProgress() { return playerEntry != null ? Progress(playerEntry) : 0f; }

        public int PlayerPosition()
        {
            if (playerEntry == null) return 0;
            if (playerEntry.finished) return playerEntry.place;
            float pp = Progress(playerEntry);
            int pos = 1;
            foreach (var e in entries) if (e != playerEntry && Progress(e) > pp) pos++;
            return pos;
        }

        public int PlayerLap { get { return playerEntry != null ? Mathf.Min(playerEntry.lap + 1, laps) : 0; } }
        public Vector3 NextCheckpoint { get { return playerEntry != null ? checkpoints[playerEntry.cp % checkpoints.Count] : Vector3.zero; } }

        public void StartRace(int idx)
        {
            if (Active) Abort();
            var g = Game.I;
            g.police.EndPursuit(false);
            g.delivery.Cancel();
            current = races[idx];
            circuit = current.circuit;
            laps = current.laps;
            checkpoints.Clear();
            var pts = current.points;
            for (int i = 1; i < pts.Count; i++) checkpoints.Add(pts[i]);
            if (circuit) checkpoints.Add(pts[0]);

            Vector3 start = pts[0];
            Vector3 dir = U.Flat(pts[1] - pts[0]).normalized;
            Vector3 right = new Vector3(dir.z, 0, -dir.x);
            Quaternion rot = Quaternion.LookRotation(dir);
            Vector3[] slots = new Vector3[4];
            for (int s = 0; s < 4; s++)
                slots[s] = start + dir * (12f - (s / 2) * 9f) + right * ((s % 2 == 0) ? -3.5f : 3.5f) + Vector3.up * 0.6f;

            entries.Clear();
            g.player.Teleport(slots[3], rot);
            playerEntry = new Entry { car = g.player, name = "Sen" };
            entries.Add(playerEntry);
            string[] names = { "Razor", "Ronnie", "Bull" };
            for (int i = 0; i < 3; i++)
            {
                int specId = Mathf.Clamp(SaveSystem.Data.selected + Random.Range(-1, 2), 0, Catalog.Cars.Length - 1);
                var spec = Catalog.Cars[specId];
                var car = CarFactory.Build(spec, Catalog.Paints[(i * 3 + 2) % Catalog.Paints.Length], slots[i], rot, CarRole.Racer, "Yarisci_" + names[i]);
                var save = SaveSystem.Get(SaveSystem.Data.selected);
                CarFactory.ApplyTuning(car, spec, save);
                var d = car.gameObject.AddComponent<RacerDriver>();
                d.skill = 0.86f + i * 0.04f;
                var e = new Entry { car = car, name = names[i] };
                d.entry = e;
                entries.Add(e);
            }
            foreach (var e in entries) e.car.locked = true;
            countdown = 3.99f;
            lastBeep = 4;
            raceTime = 0f;
            endTimer = 0f;

            if (gate == null) gate = MakeGate();
            gate.SetActive(true);
            PlaceGate();
            g.Toast(current.name + " başlıyor!");
        }

        GameObject MakeGate()
        {
            var root = new GameObject("KontrolNoktasi");
            var m = U.Emissive(new Color(1f, 0.5f, 0f), new Color(3f, 1.3f, 0f));
            float half = City.RoadW / 2 + 0.5f;
            U.Prim(PrimitiveType.Cube, "SolDirek", root.transform, new Vector3(-half, 4f, 0), new Vector3(0.6f, 8f, 0.6f), m);
            U.Prim(PrimitiveType.Cube, "SagDirek", root.transform, new Vector3(half, 4f, 0), new Vector3(0.6f, 8f, 0.6f), m);
            U.Prim(PrimitiveType.Cube, "Ust", root.transform, new Vector3(0, 8f, 0), new Vector3(half * 2 + 0.6f, 0.6f, 0.6f), m);
            U.Icon(root.transform, new Color(1f, 0.6f, 0f), 8f);
            return root;
        }

        void PlaceGate()
        {
            if (gate == null || playerEntry == null) return;
            int i = playerEntry.cp % checkpoints.Count;
            Vector3 p = checkpoints[i];
            Vector3 prev = i == 0 ? (circuit ? checkpoints[checkpoints.Count - 1] : current.points[0]) : checkpoints[i - 1];
            Vector3 d = U.Flat(p - prev);
            if (d.sqrMagnitude < 0.01f) d = Vector3.forward;
            gate.transform.SetPositionAndRotation(p, Quaternion.LookRotation(d.normalized));
        }

        public void Abort()
        {
            if (!Active) return;
            foreach (var e in entries) if (e.car != null && e.car != Game.I.player) Destroy(e.car.gameObject);
            if (Game.I.player != null) Game.I.player.locked = false;
            entries.Clear();
            playerEntry = null;
            current = null;
            if (gate != null) gate.SetActive(false);
        }

        void Update()
        {
            if (!Active) return;
            float dt = Time.deltaTime;
            if (countdown > 0f)
            {
                countdown -= dt;
                int c = Mathf.CeilToInt(countdown);
                if (c != lastBeep && c > 0) { lastBeep = c; Game.I.Beep(false); }
                if (countdown <= 0f)
                {
                    foreach (var e in entries) e.car.locked = false;
                    Game.I.Beep(true);
                }
                return;
            }
            raceTime += dt;

            int finishedCount = 0;
            foreach (var e in entries) if (e.finished) finishedCount++;

            foreach (var e in entries)
            {
                if (e.finished || e.car == null) continue;
                Vector3 t = checkpoints[e.cp];
                if (U.FlatDist(e.car.transform.position, t) < 14f)
                {
                    e.cp++;
                    if (e == playerEntry) Game.I.Beep(false);
                    if (e.cp >= checkpoints.Count)
                    {
                        e.cp = 0;
                        e.lap++;
                        if (e.lap >= laps)
                        {
                            e.finished = true;
                            finishedCount++;
                            e.place = finishedCount;
                            if (e == playerEntry) PlayerFinished();
                        }
                    }
                    if (e == playerEntry) PlaceGate();
                }
            }

            if (playerEntry != null && playerEntry.finished)
            {
                endTimer += dt;
                if (endTimer > 4f) Abort();
            }
        }

        void PlayerFinished()
        {
            int place = playerEntry.place;
            int prize = place == 1 ? current.prize : place == 2 ? current.prize * 2 / 5 : place == 3 ? current.prize / 7 : 0;
            if (place == 1) SaveSystem.Data.racesWon++;
            SaveSystem.AddMoney(prize);
            Game.I.Toast(place + ". oldun! " + (prize > 0 ? "Ödül: " + U.Money(prize) : "Ödül yok."));
            if (gate != null) gate.SetActive(false);
            playerEntry.car.locked = false;
        }
    }

    /// <summary>Zamanlı teslimat görevleri.</summary>
    public class DeliveryManager : MonoBehaviour
    {
        public bool Active { get; private set; }
        public float timeLeft;
        public int reward;
        public Vector3 target;
        GameObject marker;

        public void StartJob()
        {
            var g = Game.I;
            if (g.race.Active) g.race.Abort();
            var graph = g.city.graph;
            Vector3 pp = g.player.transform.position;
            int n = graph.RandomNodeAround(pp, 350f, 800f);
            target = graph.nodes[n];
            float dist = U.FlatDist(pp, target);
            timeLeft = 25f + dist / 16f;
            reward = 600 + Mathf.RoundToInt(dist * 3f / 50f) * 50;
            Active = true;
            if (marker == null)
            {
                marker = new GameObject("TeslimatNoktasi");
                var m = U.Emissive(new Color(0f, 0.6f, 1f), new Color(0f, 1.5f, 3f));
                var beam = U.Prim(PrimitiveType.Cylinder, "Isin", marker.transform, new Vector3(0, 30, 0), new Vector3(3, 30, 3), m);
                beam.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                U.Icon(marker.transform, new Color(0f, 0.7f, 1f), 10f);
            }
            marker.transform.position = target;
            marker.SetActive(true);
            g.Toast("Teslimat: paketi " + Mathf.RoundToInt(timeLeft) + " sn içinde mavi noktaya götür!");
        }

        public void Cancel()
        {
            Active = false;
            if (marker != null) marker.SetActive(false);
        }

        void Update()
        {
            if (!Active || Game.I.player == null) return;
            timeLeft -= Time.deltaTime;
            if (U.FlatDist(Game.I.player.transform.position, target) < 10f)
            {
                SaveSystem.AddMoney(reward);
                Game.I.Toast("Teslimat tamam! +" + U.Money(reward));
                Game.I.Beep(true);
                Cancel();
            }
            else if (timeLeft <= 0f)
            {
                Game.I.Toast("Teslimat süresi doldu!");
                Cancel();
            }
        }
    }
}
