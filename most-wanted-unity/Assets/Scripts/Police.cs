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
        public const float SpeedLimit = 180f;   // takip SADECE polis seni bu hızın üstünde görürse başlar
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
        bool hintShown;
        const int Patrols = 3;   // şehirde aynı anda en fazla 3 devriye

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

        // ---- denge (Polis zorluğu: 0 Kolay, 1 Normal, 2 Zor) ----
        public static int Diff { get { return Mathf.Clamp(SaveSystem.Data.policeDiff, 0, 2); } }
        static readonly int[] PursuerCap = { 2, 3, 4, 5, 6 };
        /// <summary>Aktif takipçi sınırı: 1–5 yıldız → 2/3/4/5/6 (Kolay bir eksik, en az 2).</summary>
        public int MaxPursuers { get { int c = PursuerCap[Mathf.Clamp(Stars, 1, 5) - 1]; return Diff == 0 ? Mathf.Max(2, c - 1) : c; } }
        /// <summary>Polis azami hızı / oyuncu azami hızı: 1–2 yıldızda %90–95, sonra artar.</summary>
        public float SpeedFactor { get { float[] low = { 0.90f, 0.93f, 0.95f }; return Stars <= 2 ? low[Diff] : low[Diff] + 0.05f * (Stars - 2); } }
        /// <summary>Görülmeden sakinleşme süresi (sn): 1–2 yıldızda 20/25/30, yüksek yıldızda daha kısa.</summary>
        public float CooldownTime { get { float[] low = { 20f, 25f, 30f }; float b = low[Diff]; return Stars <= 2 ? b : b * (1f - 0.12f * (Stars - 2)); } }
        /// <summary>Çarpma arası bekleme (sn, araç başına): 1–2 yıldızda 6–8, 3+ yıldızda kısa.</summary>
        public float RamInterval { get { return Stars <= 2 ? Random.Range(6f, 8f) + (Diff == 0 ? 1f : Diff == 2 ? -0.5f : 0f) : Mathf.Max(2f, 5f - (Stars - 3) - Diff * 0.5f); } }
        /// <summary>Oyuncuya çarpışmada izin verilen azami hız değişimi (m/s).</summary>
        public float ImpulseCap { get { float[] b = { 3f, 4f, 5f }; return b[Diff] + Mathf.Max(0, Stars - 2) * 1.2f; } }
        public float BustTime { get { float[] b = { 5f, 4f, 3.5f }; return b[Diff]; } }
        public int PursuerCount { get { int n = 0; foreach (var c in cops) if (c != null && !c.roadblock && !c.car.disabled) n++; return n; } }

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

            int want = pursuit ? Mathf.Min(MaxPursuers, maxUnits) : Patrols;
            int have = pursuit ? PursuerCount : cops.Count;
            spawnTimer -= dt;
            if (have < want && spawnTimer <= 0f)
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
                    if (pursuit && !c.roadblock && g.player != null) c.car.topSpeed = Mathf.Max(150f, g.player.topSpeed * SpeedFactor);
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
                    if (d < 220f && !hintShown && PlayerPrefs.GetInt("MW_PolHint", 0) == 0)
                    {
                        hintShown = true; PlayerPrefs.SetInt("MW_PolHint", 1);
                        g.Toast("İpucu: Polis " + Mathf.RoundToInt(SpeedLimit) + " km/s üstünde seni fark eder");
                    }
                    if (d < 120f && kmh > SpeedLimit && U.LineOfSight(c.transform.position + Vector3.up * 2.2f, pp + Vector3.up * 1.2f))
                    {
                        StartPursuit();
                        g.Toast("Hız ihlali! Polis peşinde!");
                        Radio("Merkez, " + Mathf.RoundToInt(kmh) + " km/s ile giden bir araç tespit edildi. Takibe başlıyorum!");
                        break;
                    }
                }
                return;
            }

            // ---- Takip ----
            pursuitTime += dt;
            heat = Mathf.Min(5.99f, heat + dt / (Diff == 0 ? 70f : Diff == 1 ? 55f : 40f));
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
                if (d < 40f || (d < 120f && U.LineOfSight(c.transform.position + Vector3.up * 2.2f, pp + Vector3.up * 1.2f))) seen = true;
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
            if (seen) cooldown = Mathf.Max(0f, cooldown - dt * 0.5f);
            else
            {
                cooldown += dt / CooldownTime * (Hiding ? 2f : 1f);
                if (cooldown >= 1f) { Escape(); return; }
            }

            // yakalanma: düşük hız + yakında polis (kutulanmışsan daha hızlı)
            // yakalanma: sadece 5 km/s altında VE kutulanmışken (2+ polis yanında, ya da 1 polis + gaza rağmen ilerleyemiyor) BustTime sn
            bool boxed = nearCount >= 2 || (nearCount >= 1 && Mathf.Abs(g.player.throttle) > 0.5f);
            if (kmh < 5f && nearest < 9f && boxed) bustProgress += dt / BustTime;
            else bustProgress = Mathf.Max(0f, bustProgress - dt * (kmh > 15f ? 1.2f : 0.6f));
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
            // çarpışma itkisini sınırla: oyuncunun tek çarpışmadaki hız değişimi ImpulseCap'i aşmasın
            var pl = Game.I.player;
            if (pl != null && !pl.rb.isKinematic)
            {
                Vector3 before = pl.PrevVel, now = U.Vel(pl.rb), dv = now - before;
                float cap = ImpulseCap;
                if (dv.magnitude > cap) U.SetVel(pl.rb, before + dv.normalized * cap);
                Vector3 av = pl.rb.angularVelocity;
                pl.rb.angularVelocity = new Vector3(av.x, Mathf.Clamp(av.y, -1.6f, 1.6f), av.z);
            }
            if (rel > 6f) Game.I.rig.Shake(Mathf.Clamp01((rel - 6f) / 24f));
            if (hitCool > 0f || rel < 4f) return;
            hitCool = 1f;
            // 0 yıldızda sadece kasıtlı çarpma (> 40 km/s göreli, yandan/önden) takip başlatır
            if (!pursuit && rel < 40f / 3.6f) return;
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
            SaveSystem.AddMoney(reward / 2);   // ödülün yarısı nakit, tamamı kariyer ödülü
            Game.I.Toast("KAÇTIN! Ödül: " + U.Money(reward) + " (nakit " + U.Money(reward / 2) + ")" + "  (" + Stars + " yıldız)");
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
            if (U.CarNearby(pos, 5f, null) || U.FlatDist(pos, Game.I.player.transform.position) < (chasing ? 60f : 150f)) return;
            if (!chasing) foreach (var o in cops) if (o != null && U.FlatDist(o.transform.position, pos) < 250f) return;   // devriyeler dağınık
            string role = "patrol";
            int s = Stars;
            float r = Random.value;
            if (chasing && s >= 3 && r < 0.3f) role = "undercover";
            if (chasing && s >= 4 && r > 0.55f) role = "suv";
            var def = role == "undercover" ? Catalog.PoliceUndercover : role == "suv" ? Catalog.PoliceSuv : Catalog.PolicePatrol;
            var car = CarFactory.BuildPolice(def, role, pos, Quaternion.LookRotation(U.Flat(graph.nodes[b] - graph.nodes[a]).normalized));
            // polis performansı: azami hız oyuncunun azami hızına bağlı (1–2 yıldızda %90–95), tork 3+ yıldızda artar
            float pTop = Game.I.player != null ? Game.I.player.topSpeed : 220f;
            if (s >= 3) car.peakTorque *= 1f + 0.05f * (s - 2);
            car.topSpeed = Mathf.Max(150f, pTop * SpeedFactor);
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
