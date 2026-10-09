using System.Collections.Generic;
using UnityEngine;

namespace MostWanted
{
    /// <summary>
    /// Aranma seviyesi (1-5), takip, birim çeşitleri (devriye → sivil → SUV), barikat + çivili şerit,
    /// helikopter, saklanma noktaları, sakinleşme, yakalanma, ödül (bounty) ve telsiz konuşmaları.
    /// </summary>
    public class PoliceManager : MonoBehaviour
    {
        public const float SpeedLimit = 95f;
        public bool pursuit;
        public float heat;            // 1..5.99
        public float cooldown;        // 0..1
        public float bustProgress;    // 0..1
        public int bounty;
        public float pursuitTime;
        public int disabledThisPursuit;
        public bool Seen { get; private set; }
        public bool Hiding { get; private set; }
        public int Stars { get { return pursuit ? Mathf.Clamp(Mathf.FloorToInt(heat), 1, 5) : 0; } }
        public int CopCount { get { return cops.Count; } }
        public Helicopter heli;
        public int maxUnits = 9;
        public string radio = "";
        public float radioTime;

        readonly List<PoliceDriver> cops = new List<PoliceDriver>();
        readonly List<GameObject> roadblockObjs = new List<GameObject>();
        readonly List<KeyValuePair<Vector3, Vector3>> roadblocks = new List<KeyValuePair<Vector3, Vector3>>(); // merkez, eksen
        float spawnTimer, rbTimer, grace, bountyAcc, hitCool, checkTimer, radioTimer;
        int slotCounter;
        bool heliSeenOnce;
        const int Patrols = 4;

        static readonly string[] Chatter =
        {
            "Merkez, şüpheli araç yüksek hızla ilerliyor!",
            "Tüm birimler, şüpheliyi kıstırın!",
            "Destek istiyorum, araç çok hızlı!",
            "Şüpheliyi gözden kaybetmeyin!",
            "Birimler kavşakları kapatsın!",
            "Bu adam deli gibi sürüyor!",
        };

        public void Radio(string s) { radio = s; radioTime = 5f; }

        void Update()
        {
            var g = Game.I;
            if (g.player == null || g.world.graph.nodes.Count < 4) return;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            grace -= dt; hitCool -= dt; radioTime -= dt;
            cops.RemoveAll(c => c == null);
            Vector3 pp = g.player.transform.position;

            if (g.race.Active && pursuit) EndPursuit(false);

            int want = pursuit ? Mathf.Min(2 + Stars * 2, maxUnits) : Patrols;
            spawnTimer -= dt;
            if (cops.Count < want && spawnTimer <= 0f)
            {
                spawnTimer = pursuit ? 2.2f : 1f;
                SpawnCop(pp, pursuit);
            }

            checkTimer -= dt;
            if (checkTimer <= 0f)
            {
                checkTimer = 0.5f;
                for (int i = cops.Count - 1; i >= 0; i--)
                {
                    var c = cops[i];
                    float d = U.FlatDist(c.transform.position, pp);
                    bool remove = (!pursuit && d > 450f) || (pursuit && d > 650f) || (!pursuit && cops.Count > Patrols && d > 140f) || (c.car.disabled && d > 160f);
                    if (remove) { Destroy(c.gameObject); cops.RemoveAt(i); }
                }
            }

            float kmh = g.player.SpeedKmh;
            if (!pursuit)
            {
                if (grace > 0f || g.race.Active) return;
                foreach (var c in cops)
                {
                    if (c.car.disabled) continue;
                    float d = U.FlatDist(c.transform.position, pp);
                    if (d < 60f && kmh > SpeedLimit && U.LineOfSight(c.transform.position + Vector3.up * 2.2f, pp + Vector3.up * 1.2f))
                    {
                        StartPursuit();
                        g.Toast("Hız ihlali! Polis peşinde!");
                        Radio("Merkez, hız ihlali yapan bir araç tespit edildi. Takibe başlıyorum!");
                        break;
                    }
                }
                return;
            }

            // ---- Takip ----
            pursuitTime += dt;
            heat = Mathf.Min(5.99f, heat + dt / 45f);
            bountyAcc += dt * Stars * 25f;
            if (bountyAcc >= 1f) { int add = Mathf.FloorToInt(bountyAcc); bounty += add; bountyAcc -= add; }

            bool seen = false;
            float nearest = float.MaxValue;
            int nearCount = 0;
            for (int i = cops.Count - 1; i >= 0; i--)
            {
                var c = cops[i];
                if (c.car.disabled)
                {
                    if (c.pursuing)
                    {
                        c.pursuing = false;
                        disabledThisPursuit++;
                        SaveSystem.Data.copsDisabled++;
                        bounty += 1000 * Stars;
                        g.Toast("Polis aracı devre dışı! +" + U.Money(1000 * Stars));
                        Radio("Bir birim düştü! Tekrar ediyorum, bir birim düştü!");
                        heat = Mathf.Min(5.99f, heat + 0.15f);
                    }
                    var plx = c.GetComponent<PoliceLights>(); if (plx) plx.on = false;
                    continue;
                }
                c.pursuing = !c.roadblock;
                var pl = c.GetComponent<PoliceLights>(); if (pl) pl.on = true;
                float d = U.FlatDist(c.transform.position, pp);
                if (d < nearest) nearest = d;
                if (d < 9f) nearCount++;
                if (d < 40f || (d < 170f && U.LineOfSight(c.transform.position + Vector3.up * 2.2f, pp + Vector3.up * 1.2f))) seen = true;
            }
            Hiding = g.world.InHiding(pp);
            if (Hiding && nearest > 25f) seen = false;

            // helikopter
            if (Stars >= 4 && heli == null) { heli = Helicopter.Create(pp + new Vector3(150, 80, 150)); Radio("Helikopter havada, şüpheliyi görüyoruz!"); }
            if (heli != null)
            {
                if (heli.SeesPlayer) { seen = true; heliSeenOnce = true; }
            }
            Seen = seen;
            if (seen) cooldown = Mathf.Max(0f, cooldown - dt * 0.6f);
            else
            {
                cooldown += dt / (9f + Stars * 3f) * (Hiding ? 2.5f : 1f);
                if (cooldown >= 1f) { Escape(); return; }
            }

            // yakalanma: düşük hız + yakında polis (kutulanmışsan daha hızlı)
            if (kmh < 8f && nearest < 9f) bustProgress += dt / (nearCount >= 2 ? 2.2f : 3.2f);
            else bustProgress = Mathf.Max(0f, bustProgress - dt * 0.6f);
            if (bustProgress >= 1f) { Busted(); return; }

            // barikat
            if (Stars >= 3)
            {
                rbTimer -= dt;
                if (rbTimer <= 0f) { rbTimer = 32f; SpawnRoadblock(g.player); }
            }
            CheckRoadblocksPassed(pp);

            // telsiz
            radioTimer -= dt;
            if (radioTimer <= 0f) { radioTimer = Random.Range(9f, 15f); Radio(Chatter[Random.Range(0, Chatter.Length)]); }

            g.career.OnPursuitTick(this);
        }

        public void OnPlayerHit(Collision c)
        {
            if (c.rigidbody == null || Game.I.race.Active) return;
            var cop = c.rigidbody.GetComponent<PoliceDriver>();
            if (cop == null) return;
            float rel = c.relativeVelocity.magnitude;
            if (c.contactCount > 0 && Mathf.Abs(c.GetContact(0).normal.y) > 0.7f) return; // üstten/alttan temas sayılmaz
            if (rel > 4f) cop.car.Damage(rel * (cop.role == "suv" ? 1.6f : 2.6f));
            Game.I.rig.Shake(Mathf.Clamp01(rel / 25f));
            if (hitCool > 0f || rel < 4f) return;
            hitCool = 1f;
            if (!pursuit) { StartPursuit(); Game.I.Toast("Polise çarptın! Takip başladı!"); Radio("Polis aracına saldırı! Tüm birimler!"); }
            heat = Mathf.Min(5.99f, heat + 0.25f);
            bounty += 250;
            cooldown = 0f;
        }

        public void OnBreaker(Vector3 pos)
        {
            SaveSystem.Data.breakersUsed++;
            if (pursuit) { bounty += 5000; Radio("Yapı üstümüze çöktü! Birimler devre dışı!"); }
        }

        public void StartPursuit()
        {
            if (pursuit) return;
            pursuit = true;
            heat = Mathf.Max(heat, 1f);
            cooldown = 0f; bustProgress = 0f; bounty = 0; pursuitTime = 0f; disabledThisPursuit = 0;
            rbTimer = 15f; radioTimer = 6f; heliSeenOnce = false;
        }

        public void ForceHeat(int stars)
        {
            StartPursuit();
            heat = Mathf.Clamp(stars, 1, 5) + 0.5f;
        }

        void Escape()
        {
            int reward = bounty;
            var d = SaveSystem.Data;
            d.escapes++;
            d.careerBounty += reward;
            d.bestBounty = Mathf.Max(d.bestBounty, reward);
            d.longestPursuit = Mathf.Max(d.longestPursuit, pursuitTime);
            if (heli != null && heliSeenOnce) d.heliEscapes++;
            SaveSystem.AddMoney(reward);
            Game.I.Toast("KAÇTIN! Ödül: " + U.Money(reward) + "  (" + Stars + " yıldız)");
            Radio("Şüpheliyi kaybettik... Tüm birimler devriyeye dönsün.");
            Game.I.Beep(true);
            EndPursuit(true);
        }

        void Busted()
        {
            var d = SaveSystem.Data;
            int fine = Mathf.Min(1000 * Stars + bounty / 3, d.money);
            d.busted++;
            SaveSystem.AddMoney(-fine);
            Game.I.Toast("YAKALANDIN! Ceza: " + U.Money(fine));
            Radio("Şüpheli gözaltında.");
            EndPursuit(false);
            Vector3 pp = Game.I.player.transform.position;
            for (int i = cops.Count - 1; i >= 0; i--)
                if (U.FlatDist(cops[i].transform.position, pp) < 160f) { Destroy(cops[i].gameObject); cops.RemoveAt(i); }
            Game.I.player.Teleport(Game.I.world.garagePos + Vector3.up * 0.6f, Game.I.world.garageRot);
            Game.I.rig.Snap();
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
            foreach (var r in roadblockObjs) if (r != null) Destroy(r);
            roadblockObjs.Clear(); roadblocks.Clear();
            for (int i = cops.Count - 1; i >= 0; i--) if (cops[i].roadblock) { Destroy(cops[i].gameObject); cops.RemoveAt(i); }
            if (heli != null) { Destroy(heli.gameObject); heli = null; }
        }

        void SpawnCop(Vector3 near, bool chasing)
        {
            var graph = Game.I.world.graph;
            int a = graph.RandomNodeAround(near, chasing ? 110f : 120f, chasing ? 260f : 380f);
            var adj = graph.adj[a];
            if (adj.Count == 0) return;
            int b = adj[Random.Range(0, adj.Count)];
            Vector3 lp = graph.LanePoint(a, b);
            Vector3 pos = Vector3.Lerp(graph.nodes[a] + (lp - graph.nodes[b]), lp, 0.35f) + Vector3.up * 0.4f;
            if (U.CarNearby(pos, 5f, null) || U.FlatDist(pos, Game.I.player.transform.position) < 60f) return;
            string role = "patrol";
            int s = Stars;
            float r = Random.value;
            if (chasing && s >= 3 && r < 0.3f) role = "undercover";
            if (chasing && s >= 4 && r > 0.55f) role = "suv";
            var def = role == "undercover" ? Catalog.PoliceUndercover : role == "suv" ? Catalog.PoliceSuv : Catalog.PolicePatrol;
            var car = CarFactory.BuildPolice(def, role, pos, Quaternion.LookRotation(U.Flat(graph.nodes[b] - graph.nodes[a]).normalized));
            // polis performansı: aranma seviyesiyle artar
            car.peakTorque *= 1f + 0.05f * s;
            car.topSpeed = Mathf.Max(car.topSpeed, 230f + s * 12f);
            car.dragK = CarMath.DragCoef(car.peakTorque, car.topSpeed, car.redline, car.wheelRadius, car.ratios);
            car.finalDrive = CarMath.FinalDrive(car.topSpeed, car.redline, car.wheelRadius, car.ratios);
            var d = car.gameObject.AddComponent<PoliceDriver>();
            d.Init(a, b);
            d.role = role;
            d.slot = slotCounter++;
            d.pursuing = chasing;
            car.GetComponent<PoliceLights>().on = chasing;
            cops.Add(d);
            if (chasing && role == "suv") Radio("Ağır birimler bölgede, şüpheliye çarpın!");
        }

        void SpawnRoadblock(CarController player)
        {
            var graph = Game.I.world.graph;
            Vector3 pp = player.transform.position;
            Vector3 fwd = U.Flat(U.Vel(player.rb));
            if (fwd.sqrMagnitude < 4f) fwd = U.Flat(player.transform.forward);
            fwd.Normalize();
            int best = -1; float bd = float.MaxValue;
            for (int i = 0; i < graph.nodes.Count; i++)
            {
                Vector3 to = graph.nodes[i] - pp; to.y = 0;
                float d = to.magnitude;
                if (d < 110f || d > 220f) continue;
                if (Vector3.Dot(to / d, fwd) < 0.85f) continue;
                if (d < bd) { bd = d; best = i; }
            }
            if (best < 0) return;
            Vector3 n = graph.nodes[best];
            Vector3 dir = U.Flat(n - pp).normalized;
            Vector3 axis = Mathf.Abs(dir.x) > Mathf.Abs(dir.z) ? new Vector3(Mathf.Sign(dir.x), 0, 0) : new Vector3(0, 0, Mathf.Sign(dir.z));
            Vector3 side = new Vector3(axis.z, 0, -axis.x);
            Vector3 center = n - axis * 16f;
            for (int k = -1; k <= 1; k += 2)
            {
                var car = CarFactory.BuildPolice(Catalog.PolicePatrol, "patrol", center + side * (k * 4.5f) + Vector3.up * 0.2f, Quaternion.LookRotation(side * k));
                car.rb.isKinematic = true;
                var d = car.gameObject.AddComponent<PoliceDriver>();
                d.roadblock = true;
                car.GetComponent<PoliceLights>().on = true;
                cops.Add(d);
            }
            if (Stars >= 3)
            {
                // çivili şerit: barikatın önünde, ortadaki boşlukta
                var spike = U.Prim(PrimitiveType.Cube, "CiviliSerit", null, center - axis * 9f + Vector3.up * 0.06f, Vector3.one, U.Mat(new Color(0.12f, 0.12f, 0.12f), 0.6f, 0.8f), true);
                spike.transform.rotation = Quaternion.LookRotation(axis);
                spike.transform.localScale = new Vector3(7f, 0.08f, 0.8f);
                var bc = spike.GetComponent<BoxCollider>();
                bc.isTrigger = true;
                bc.size = new Vector3(1f, 14f, 2.5f);
                spike.AddComponent<SpikeStrip>();
                for (int t = -3; t <= 3; t++)
                    U.Prim(PrimitiveType.Cube, "Civi", spike.transform, new Vector3(t / 7f, 2f, 0), new Vector3(0.03f, 3f, 0.6f), U.Mat(new Color(0.7f, 0.7f, 0.7f), 0.8f, 1f));
                roadblockObjs.Add(spike);
            }
            var bar = U.Prim(PrimitiveType.Cube, "Barikat", null, center + axis * 4f + Vector3.up * 0.6f, Vector3.one, U.Emissive(new Color(1f, 0.45f, 0f), new Color(0.3f, 0.12f, 0f)), true);
            bar.transform.localScale = new Vector3(Mathf.Abs(side.x) * 8f + 0.6f, 1.2f, Mathf.Abs(side.z) * 8f + 0.6f);
            roadblockObjs.Add(bar);
            roadblocks.Add(new KeyValuePair<Vector3, Vector3>(center, axis));
            Game.I.Toast("Önünde barikat var!" + (Stars >= 3 ? " Çivili şeride dikkat!" : ""));
            Radio("Barikat kuruldu, şüpheli yaklaşıyor!");
        }

        void CheckRoadblocksPassed(Vector3 pp)
        {
            for (int i = roadblocks.Count - 1; i >= 0; i--)
            {
                var rb = roadblocks[i];
                Vector3 rel = pp - rb.Key;
                float along = Vector3.Dot(rel, rb.Value);
                if (along > 12f && Mathf.Abs(Vector3.Dot(rel, new Vector3(rb.Value.z, 0, -rb.Value.x))) < 20f)
                {
                    roadblocks.RemoveAt(i);
                    SaveSystem.Data.roadblocksEvaded++;
                    bounty += 1500;
                    Game.I.Toast("Barikat atlatıldı! +" + U.Money(1500));
                    Radio("Barikatı geçti! Tekrar ediyorum, barikatı geçti!");
                }
            }
        }
    }
}
