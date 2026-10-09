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

    /// <summary>Trafik ışığı sağlayıcısı (MapDressing ya da ileride kendi şehrimizin ışıkları).</summary>
    public interface ITrafficLights
    {
        /// <summary>prev→next yönünde gelen araç için next düğümündeki ışık kırmızı mı?</summary>
        bool IsRed(int prev, int next);
    }

    /// <summary>
    /// Şeritte giden, kavşakta dönen trafik. Yol: şerit çizgisi (yöne göre sağ şerit) → kavşakta şerit çıkışından
    /// yeni şerit girişine ikinci derece Bezier eğrisi → sonraki şerit. Viraj ve öndeki araç için yavaşlar,
    /// kırmızıda durur, dönüşten 1.5 sn önce sinyal verir. Uzakta kinematik (ucuz) moda geçer ve aynı yolda kayar.
    /// </summary>
    public class TrafficDriver : AIDriver
    {
        public int prevNode, nextNode;
        public bool far, hidden;

        // ---- planlı yol (yalnızca sivil trafik; polis devriyesi eski basit mantığı kullanır)
        int afterNode = -1;
        readonly List<Vector3> path = new List<Vector3>(16);
        int seg, idxE, idxX;          // geçerli parça, şerit çıkışı (E) ve yeni şerit girişi (X) indeksleri
        float turnSign;               // -1 sol, +1 sağ, 0 düz
        float turnSpeed = 99f;        // virajın güvenli hızı (km/s)
        bool redLatched;              // çizgiyi geçtiyse artık ışığa bakma
        float signalOnUntil;
        public bool braking;          // dış (ışık/öndeki araç) frenleme
        public TurnSignals signals;

        public void Init(int a, int b) { prevNode = a; nextNode = b; afterNode = -1; path.Clear(); seg = 0; redLatched = false; }

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

        bool Civil { get { return GetType() == typeof(TrafficDriver); } }

        static ITrafficLights Lights
        {
            get
            {
                if (MapDressing.I != null) return MapDressing.I;
                return Game.I != null ? Game.I.world as ITrafficLights : null;
            }
        }

        int frame;
        protected override void Think()
        {
            if (!Civil) { LegacyThink(); return; }
            if (((++frame + GetHashCode()) & 1) == 0) return; // kademeli YZ (2 fizik adımında bir)
            if (!WorldReady) return;
            Vector3 p = transform.position;
            if (!Track(p)) { DriveTo(LaneTarget(), cruiseKmh * 0.5f, true); return; }

            float v = U.Vel(car.rb).magnitude;
            float kmh = v * 3.6f;
            float look = 5f + v * 0.55f;
            Vector3 aim = Lookahead(p, look);

            // ---- hız planı
            float want = cruiseKmh;
            float dE = DistAlong(p, idxE);       // şerit çıkışına (kavşak girişine) kalan yol
            bool inTurn = seg >= idxE && seg < idxX;
            const float decel = 3.2f;            // m/s² konforlu fren
            if (turnSign != 0f)
            {
                if (inTurn) want = Mathf.Min(want, turnSpeed);
                else want = Mathf.Min(want, Mathf.Sqrt(turnSpeed / 3.6f * turnSpeed / 3.6f + 2f * decel * Mathf.Max(0f, dE - 2f)) * 3.6f);
            }
            // kırmızı ışık: çıkış çizgisinde dur
            braking = false;
            var L = Lights;
            if (!inTurn && !redLatched && L != null && dE < 45f)
            {
                if (dE < 1.5f && kmh > 8f) redLatched = true;   // çizgiyi geçti, devam
                else if (L.IsRed(prevNode, nextNode))
                {
                    float stop = Mathf.Sqrt(2f * decel * Mathf.Max(0f, dE - 2.5f)) * 3.6f;
                    if (stop < want) { want = stop; braking = true; }
                }
            }
            // öndeki araç (yol yönünde küre taraması)
            Vector3 fwdDir = U.Flat(aim - p);
            if (fwdDir.sqrMagnitude > 0.01f)
            {
                fwdDir.Normalize();
                RaycastHit hit;
                Vector3 origin = p + Vector3.up * 0.9f + transform.forward * 2.4f;
                float dist = 8f + v * 1.6f;
                if (Physics.SphereCast(origin, 1.0f, fwdDir, out hit, dist) && hit.rigidbody != null && hit.rigidbody != car.rb)
                {
                    float other = Vector3.Dot(U.Vel(hit.rigidbody), fwdDir) * 3.6f;
                    float gap = hit.distance - 3.5f;
                    float safe = Mathf.Max(0f, Mathf.Max(0f, other) + gap * 2.4f - 4f);
                    if (gap < 1f) safe = 0f;
                    if (safe < want) { want = safe; braking = braking || kmh > safe + 3f; }
                }
            }

            // ---- sinyal: dönüşten 1.5 sn önce ve viraj boyunca
            bool signal = turnSign != 0f && (inTurn || dE / Mathf.Max(v, 2f) < 1.5f);
            if (signal) signalOnUntil = Time.time + 0.4f;
            if (signals != null) signals.Set(Time.time < signalOnUntil ? turnSign : 0f);

            DriveTo(aim, want, false, 1.15f);
            if (want < 1f && kmh < 3f) { car.throttle = 0f; car.handbrake = true; }   // bekle (fren lambası yanar)
            if (car.throttle < -0.05f) braking = true;
        }

        void LegacyThink()
        {
            Vector3 tgt = LaneTarget();
            AdvanceIfReached(tgt);
            tgt = LaneTarget();
            float d = U.FlatDist(transform.position, tgt);
            float want = d < 25f ? Mathf.Min(cruiseKmh, 30f) : cruiseKmh;
            // kırmızı ışıkta dur (polis takipteyken uymaz)
            if (MapDressing.I != null && d < 32f && d > 9f && MapDressing.I.IsRed(prevNode, nextNode)) want = 0f;
            DriveTo(tgt, want, true);
        }

        // ------------------------------------------------------------ yol planı
        static Vector3 Right(Vector3 d) { return new Vector3(d.z, 0f, -d.x); }

        int PickAfter(RoadGraph g, int prev, int node)
        {
            var adj = g.adj[node];
            if (adj.Count == 0) return prev;
            if (adj.Count == 1) return adj[0];
            for (int k = 0; k < 6; k++) { int n = adj[Random.Range(0, adj.Count)]; if (n != prev) return n; }
            foreach (int n in adj) if (n != prev) return n;
            return prev;
        }

        void BuildPath()
        {
            var g = Game.I.world.graph;
            path.Clear(); seg = 0; redLatched = false;
            if (afterNode < 0 || afterNode >= g.nodes.Count) afterNode = PickAfter(g, prevNode, nextNode);
            Vector3 A = g.nodes[prevNode], B = g.nodes[nextNode], C = g.nodes[afterNode];
            Vector3 dIn = U.Flat(B - A), dOut = U.Flat(C - B);
            float lIn = dIn.magnitude, lOut = dOut.magnitude;
            if (lIn < 0.01f) dIn = transform.forward; else dIn /= lIn;
            if (lOut < 0.01f) dOut = dIn; else dOut /= lOut;
            Vector3 rIn = Right(dIn), rOut = Right(dOut);
            float offIn = Mathf.Min(g.lane[prevNode], g.lane[nextNode]);
            float offOut = Mathf.Min(g.lane[nextNode], g.lane[afterNode]);
            float ang = Vector3.SignedAngle(dIn, dOut, Vector3.up);
            float absAng = Mathf.Abs(ang);
            bool junction = g.adj[nextNode].Count > 2;
            float r = absAng < 12f ? 2f : Mathf.Max(offIn, offOut) * 1.4f + (junction ? 3f : 1.5f);
            r = Mathf.Clamp(r, 1.5f, 0.45f * Mathf.Max(2f, Mathf.Min(lIn, lOut)));

            Vector3 S = A + rIn * offIn;
            Vector3 E = B - dIn * r + rIn * offIn;
            Vector3 X = B + dOut * r + rOut * offOut;
            // kontrol noktası: iki şerit doğrusunun kesişimi
            Vector3 ctrl;
            float cross = dIn.x * dOut.z - dIn.z * dOut.x;
            if (absAng > 150f) ctrl = B + dIn * r;                         // çıkmaz: U dönüşü
            else if (Mathf.Abs(cross) < 0.08f) ctrl = (E + X) * 0.5f;      // düz
            else
            {
                Vector3 w = X - E;
                float t = (w.x * dOut.z - w.z * dOut.x) / cross;
                ctrl = E + dIn * t;
                if (U.FlatDist(ctrl, B) > r * 3f) ctrl = (E + X) * 0.5f;
            }
            float yB = B.y;
            path.Add(new Vector3(S.x, A.y, S.z));
            path.Add(new Vector3(E.x, Mathf.Lerp(A.y, yB, lIn > 0.01f ? 1f - r / lIn : 1f), E.z));
            idxE = path.Count - 1;
            for (int i = 1; i < 6; i++)
            {
                float t = i / 6f, u = 1f - t;
                Vector3 q = u * u * E + 2f * u * t * ctrl + t * t * X;
                path.Add(new Vector3(q.x, yB, q.z));
            }
            path.Add(new Vector3(X.x, Mathf.Lerp(yB, C.y, lOut > 0.01f ? r / lOut : 0f), X.z));
            idxX = path.Count - 1;
            Vector3 F = C + rOut * offOut;
            path.Add(new Vector3(F.x, C.y, F.z));

            turnSign = absAng > 25f && absAng <= 150f ? Mathf.Sign(ang) : 0f;
            if (absAng > 150f) turnSign = -1f;   // U dönüşü sola
            // güvenli viraj hızı: yarıçap ≈ r / tan(θ/2), yanal ivme 3 m/s²
            float rad = absAng < 5f ? 999f : (r + 0.5f) / Mathf.Max(0.05f, Mathf.Tan(absAng * 0.5f * Mathf.Deg2Rad));
            turnSpeed = Mathf.Clamp(Mathf.Sqrt(3f * rad) * 3.6f, 14f, cruiseKmh);
        }

        /// <summary>Yolu günceller; X'i geçince bir sonraki parçaya kayar. false: yol yok.</summary>
        bool Track(Vector3 p)
        {
            var g = Game.I.world.graph;
            if (prevNode < 0 || nextNode < 0 || prevNode >= g.nodes.Count || nextNode >= g.nodes.Count) return false;
            if (path.Count < 2) BuildPath();
            for (int guard = 0; guard < 4; guard++)
            {
                while (seg < path.Count - 2 && SegT(p, seg) >= 1f) seg++;
                if (seg < idxX) break;
                // yeni şeride geçti: bir sonraki kavşağı planla
                int a = nextNode, b = afterNode;
                prevNode = a; nextNode = b; afterNode = -1;
                BuildPath();
            }
            // yoldan çok saptıysa (çarpışma vb.) en yakın düğümden yeniden başla
            Vector3 q = Project(p, seg);
            if (U.FlatDist(p, q) > 22f)
            {
                int n = g.Nearest(p);
                if (g.adj[n].Count == 0) return false;
                int best = g.adj[n][0]; float bd = -2f;
                foreach (int m in g.adj[n]) { float dd = Vector3.Dot(U.Flat(g.nodes[m] - g.nodes[n]).normalized, transform.forward); if (dd > bd) { bd = dd; best = m; } }
                Init(n, best);
                BuildPath();
            }
            return true;
        }

        float SegT(Vector3 p, int i)
        {
            Vector3 a = path[i], b = path[i + 1];
            float dx = b.x - a.x, dz = b.z - a.z, l2 = dx * dx + dz * dz;
            if (l2 < 0.0001f) return 1f;
            return ((p.x - a.x) * dx + (p.z - a.z) * dz) / l2;
        }

        Vector3 Project(Vector3 p, int i)
        {
            float t = Mathf.Clamp01(SegT(p, i));
            return Vector3.Lerp(path[i], path[i + 1], t);
        }

        Vector3 Lookahead(Vector3 p, float dist)
        {
            Vector3 cur = Project(p, seg);
            for (int i = seg; i < path.Count - 1; i++)
            {
                Vector3 nx = path[i + 1];
                float l = U.FlatDist(cur, nx);
                if (l >= dist) return Vector3.Lerp(cur, nx, dist / Mathf.Max(l, 0.001f));
                dist -= l; cur = nx;
            }
            return cur;
        }

        float DistAlong(Vector3 p, int idx)
        {
            if (seg >= idx) return 0f;
            Vector3 cur = Project(p, seg);
            float d = 0f;
            for (int i = seg; i < idx; i++) { d += U.FlatDist(cur, path[i + 1]); cur = path[i + 1]; }
            return d;
        }

        /// <summary>Uzak trafik: fizik kapalı, şerit boyunca kayar.</summary>
        public void SetFar(bool f)
        {
            if (hidden) return;
            if (far == f) return;
            far = f;
            car.rb.isKinematic = f;
            if (!f) car.SetSpeed(cruiseKmh * 0.8f);
            if (f && signals != null) signals.Set(0f);
        }

        void Update()
        {
            if (!far || hidden || !WorldReady) return;
            if (!Civil) { LegacyFarUpdate(); return; }
            Vector3 p = transform.position;
            if (!Track(p)) return;
            float step = cruiseKmh / 3.6f * Time.deltaTime;
            if (seg >= idxE && seg < idxX) step *= Mathf.Clamp01(turnSpeed / Mathf.Max(1f, cruiseKmh));
            Vector3 tgt = Lookahead(p, step);
            Vector3 dir = U.Flat(Lookahead(p, 4f) - p);
            Vector3 np = new Vector3(tgt.x, tgt.y + 0.05f, tgt.z);
            car.rb.MovePosition(np);
            if (dir.sqrMagnitude > 0.01f) car.rb.MoveRotation(Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir.normalized), Time.deltaTime * 5f));
        }

        void LegacyFarUpdate()
        {
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

    /// <summary>Sivil trafik sinyal lambaları: dönüş tarafında yanıp sönen amber emisyon (paylaşılan iki malzeme).</summary>
    public class TurnSignals : MonoBehaviour
    {
        static Material onMat, offMat;
        static Mesh cube;
        Renderer[] left, right;
        float side;

        public static TurnSignals Attach(CarController car, Bounds localBounds)
        {
            if (onMat == null) onMat = U.NewMat(new Color(1f, 0.55f, 0.05f), 0.6f, 0f);
            U.SetEmission(onMat, new Color(1f, 0.5f, 0.02f) * 6f);
            if (offMat == null) offMat = U.NewMat(new Color(0.35f, 0.2f, 0.05f), 0.6f, 0f);
            if (cube == null)
            {
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube = tmp.GetComponent<MeshFilter>().sharedMesh;
                Object.Destroy(tmp);
            }
            var s = car.gameObject.AddComponent<TurnSignals>();
            Vector3 c = localBounds.center, e = localBounds.extents;
            float y = c.y + e.y * 0.05f;
            float x = Mathf.Max(0.5f, e.x - 0.04f), zf = e.z - 0.03f, zr = -e.z + 0.03f;
            s.left = new[] { s.Lamp(car.transform, new Vector3(c.x - x, y, c.z + zf)), s.Lamp(car.transform, new Vector3(c.x - x, y, c.z + zr)) };
            s.right = new[] { s.Lamp(car.transform, new Vector3(c.x + x, y, c.z + zf)), s.Lamp(car.transform, new Vector3(c.x + x, y, c.z + zr)) };
            s.Apply(false, false);
            return s;
        }

        Renderer Lamp(Transform parent, Vector3 lp)
        {
            var go = new GameObject("Sinyal");
            go.layer = OptimizationManager.TrafficLayer;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = lp;
            go.transform.localScale = new Vector3(0.16f, 0.09f, 0.1f);
            go.AddComponent<MeshFilter>().sharedMesh = cube;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = offMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return r;
        }

        /// <summary>-1 sol, +1 sağ, 0 kapalı.</summary>
        public void Set(float s) { side = s; if (s == 0f) Apply(false, false); }

        void Update()
        {
            if (side == 0f) return;
            bool on = Mathf.Repeat(Time.time, 0.8f) < 0.42f;
            Apply(side < 0f && on, side > 0f && on);
        }

        void Apply(bool l, bool r)
        {
            if (left != null) foreach (var x in left) if (x != null) x.sharedMaterial = l ? onMat : offMat;
            if (right != null) foreach (var x in right) if (x != null) x.sharedMaterial = r ? onMat : offMat;
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
        // tepki gecikmesi (0.4–0.8 sn): oyuncunun geçmiş konum/hızı
        const int Hist = 64;
        readonly Vector3[] hPos = new Vector3[Hist], hVel = new Vector3[Hist];
        readonly float[] hT = new float[Hist];
        int hHead, hCount;
        float reaction = -1f, noiseSeed, ramTimer, ramTry;

        void Record(Vector3 p, Vector3 v)
        {
            hHead = (hHead + 1) % Hist; hPos[hHead] = p; hVel[hHead] = v; hT[hHead] = Time.time;
            if (hCount < Hist) hCount++;
        }

        void Delayed(out Vector3 p, out Vector3 v)
        {
            float want = Time.time - reaction;
            int idx = hHead;
            for (int k = 0; k < hCount; k++)
            {
                int i = (hHead - k + Hist) % Hist;
                idx = i;
                if (hT[i] <= want) break;
            }
            p = hPos[idx]; v = hVel[idx];
        }

        protected override void Think()
        {
            if (car.Damage0Check()) return;
            if (reaction < 0f) { reaction = Random.Range(0.4f, 0.8f); noiseSeed = Random.value * 100f; ramTimer = Random.Range(2f, 5f); }
            if (!pursuing) { cruiseKmh = 45f; base.Think(); return; }
            var player = Game.I.player;
            if (player == null) { base.Think(); return; }
            var pm = Game.I.police;
            float dt = Time.fixedDeltaTime;
            ramTimer -= dt;
            Record(player.transform.position, U.Vel(player.rb));
            Vector3 pp, pv;
            Delayed(out pp, out pv);
            float dist = U.FlatDist(transform.position, pp);
            bool los = dist < 45f || U.LineOfSight(transform.position + Vector3.up * 2.2f, pp + Vector3.up * 1.2f);
            float maxK = car.topSpeed;
            float pk = player.SpeedKmh;
            bool aggressive = pm.Stars >= 3;
            // direksiyon kusuru: yavaş değişen yanal hedef sapması
            Vector3 fw = U.Flat(transform.forward).normalized;
            Vector3 side = new Vector3(fw.z, 0, -fw.x);
            float wobble = (Mathf.PerlinNoise(Time.time * 0.45f + noiseSeed, noiseSeed) - 0.5f) * (aggressive ? 2.2f : 3.6f);

            if (los)
            {
                Vector3 pf = U.Flat(player.transform.forward).normalized;
                Vector3 pr = new Vector3(pf.z, 0, -pf.x);
                Vector3 aim;
                float desired;
                // çarpma: araç başına sınırlı sıklıkta, ve bu polis oyuncudan +15 km/s'ten hızlıysa asla
                bool ramming = ramTimer <= 0f && dist < 16f && car.SpeedKmh <= pk + 15f && (aggressive || role == "suv" || pm.Stars >= 1);
                if (ramming)
                {
                    ramTry += dt;
                    if (ramTry > 2f) { ramTry = 0f; ramTimer = pm.RamInterval; }
                    aim = pp + pv * 0.25f;
                    desired = pk + 12f;
                }
                else if (dist > 30f)
                {
                    aim = pp + pv * Mathf.Clamp(dist / 40f, 0f, 1.5f) + side * wobble;
                    desired = maxK;
                }
                else if (!aggressive)
                {
                    // 1–2 yıldız: çoğunlukla takip — arkada ~10 m, hızı eşle
                    aim = pp - pf * 10f + pv * 0.3f + side * wobble * 0.5f;
                    desired = pk + Mathf.Clamp(dist - 10f, -15f, 15f);
                }
                else
                {
                    // 3+ yıldız: kutulama pozisyonları (arka, sol, sağ, ön)
                    Vector3 off = slot % 4 == 0 ? -pf * 4f : slot % 4 == 1 ? -pr * 3.2f + pf * 1f : slot % 4 == 2 ? pr * 3.2f + pf * 1f : pf * 8f;
                    aim = pp + off + pv * 0.35f;
                    desired = Mathf.Max(pk + 8f, 30f);
                }
                if (dist < 30f) desired = Mathf.Min(desired, pk + 15f);
                desired = Mathf.Min(desired, maxK);
                car.nitroInput = aggressive && dist > 60f && dist < 250f;
                DriveTo(aim, desired, false, 1.0f);
                path.Clear();
            }
            else
            {
                car.nitroInput = false;
                var g = Game.I.world.graph;
                repath -= dt;
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
                float want = maxK * 0.85f;
                // gerçekçi viraj frenlemesi: sonraki düğümdeki dönüş açısına göre, frenleme mesafesi içinde yavaşla
                if (pathIdx < path.Count - 1)
                {
                    Vector3 n1 = g.nodes[path[pathIdx + 1]];
                    float turn = Vector3.Angle(U.Flat(t - transform.position), U.Flat(n1 - t));
                    float cornerK = Mathf.Lerp(want, 35f, Mathf.Clamp01((turn - 15f) / 75f));
                    float v = car.SpeedKmh / 3.6f, vc = cornerK / 3.6f;
                    float brakeDist = Mathf.Max(0f, (v * v - vc * vc) / (2f * 7f)) + v * reaction;
                    if (dn < brakeDist + 8f) want = Mathf.Min(want, cornerK);
                }
                DriveTo(t + side * wobble * 0.4f, want, false, 1.0f);
            }
        }

        void OnCollisionEnter(Collision c)
        {
            if (roadblock || c.rigidbody == null) return;
            float rel = c.relativeVelocity.magnitude;
            if (c.rigidbody.GetComponent<BreakerPiece>() != null && rel > 3f) car.Damage(250f);
            if (Game.I != null && Game.I.player != null && c.rigidbody == Game.I.player.rb) { ramTimer = Game.I.police.RamInterval; ramTry = 0f; }
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
        /// <summary>Havuz üst sınırı (OptimizationManager kaliteye göre ayarlar). Etkin sayı bölge yoğunluğuyla çarpılır.</summary>
        public int count = 14;
        public readonly List<TrafficDriver> cars = new List<TrafficDriver>();
        public float density = 1f;
        public const float SpawnMin = 120f, SpawnMax = 330f;
        float check, densityTimer;

        void Update()
        {
            var g = Game.I;
            if (g == null || g.player == null || g.world == null || g.world.graph.nodes.Count < 4) return;
            check -= Time.deltaTime;
            if (check > 0f) return;
            check = 0.4f;
            Vector3 pp = g.player.transform.position;
            densityTimer -= 0.4f;
            if (densityTimer <= 0f) { densityTimer = 2f; density = DensityAt(g, pp); }
            int target = Mathf.Clamp(Mathf.RoundToInt(count * density), 2, count);

            cars.RemoveAll(c => c == null);
            int active = 0;
            foreach (var c in cars) if (!c.hidden) active++;
            Vector3 fwd = U.Flat(g.player.transform.forward).normalized;
            foreach (var c in cars)
            {
                if (c.hidden)
                {
                    if (active < target && Respawn(c, pp)) active++;
                    continue;
                }
                Vector3 to = U.Flat(c.transform.position - pp);
                float d = to.magnitude;
                bool behind = d > 1f && Vector3.Dot(to / d, fwd) < -0.3f;
                bool farBehind = d > 220f && behind && !Visible(c.transform.position);
                if (d > 420f || farBehind || (active > target && d > SpawnMin && !Visible(c.transform.position)))
                {
                    Park(c); active--;
                    if (active < target && Respawn(c, pp)) active++;
                }
                else c.SetFar(d > Mathf.Max(120f, (Game.I.player != null ? Game.I.player.SpeedKmh / 3.6f : 0f) * 4f));   // hızlıyken yakındaki araçlar erken fiziğe geçer
            }
            if (cars.Count < count && active < target) Spawn(pp);
        }

        /// <summary>Bölge trafik yoğunluğu: merkez kalabalık, banliyö/otoyol seyrek.</summary>
        static float DensityAt(Game g, Vector3 p)
        {
            string name = "";
            var own = g.world as OwnCity;
            if (own != null && own.gen != null && own.gen.districts.Count > 0)
            {
                float bd = float.MaxValue;
                foreach (var d in own.gen.districts)
                {
                    float dx = d.Value.x - p.x, dz = d.Value.z - p.z, dd = dx * dx + dz * dz;
                    if (dd < bd) { bd = dd; name = d.Key; }
                }
            }
            else if (g.dressing != null) name = g.dressing.DistrictAt(p);
            if (string.IsNullOrEmpty(name)) return 1f;
            if (name.Contains("Merkez") || name.Contains("Downtown") || name.Contains("Center")) return 1f;
            if (name.Contains("Liman")) return 0.8f;
            if (name.Contains("Sanayi")) return 0.65f;
            if (name.Contains("Otoyol")) return 0.45f;
            if (name.Contains("Banliyö") || name.Contains("Konut") || name.Contains("Park")) return 0.5f;
            return 0.75f;
        }

        static bool Visible(Vector3 p)
        {
            var cam = Game.I != null ? Game.I.cam : null;
            if (cam == null) return false;
            Vector3 v = cam.WorldToViewportPoint(p + Vector3.up * 0.8f);
            return v.z > -3f && v.x > -0.15f && v.x < 1.15f && v.y > -0.2f && v.y < 1.2f;
        }

        void Spawn(Vector3 near)
        {
            var list = Catalog.Traffic;
            if (list.Count == 0) return;
            var def = list[Random.Range(0, list.Count)];
            var col = Catalog.Paints[Random.Range(0, Catalog.Paints.Length)];
            Vector3 at = new Vector3(0, -200, 0);
            var car = CarFactory.Build(def, col, at, Quaternion.identity, CarRole.Traffic, null, "Trafik");
            // trafik: sakin sürüş
            car.peakTorque *= 0.6f;
            car.topSpeed = Mathf.Min(car.topSpeed, 140f);
            car.brakeGlow = 2.5f;   // fren lambaları frenlerken bloom yapsın
            bool any = false; Bounds b = new Bounds(at, Vector3.zero);
            foreach (var r in car.GetComponentsInChildren<Renderer>(true))
            {
                if (r.gameObject.layer != U.IconLayer) r.gameObject.layer = OptimizationManager.TrafficLayer;
                else continue;
                if (r is ParticleSystemRenderer) continue;
                if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
            }
            var d = car.gameObject.AddComponent<TrafficDriver>();
            d.cruiseKmh = Random.Range(40f, 65f);
            if (!any || b.size.x < 1f || b.size.z < 2f) b = new Bounds(at + Vector3.up * 0.7f, new Vector3(1.8f, 1.4f, def.length));
            b.center -= at;
            try { d.signals = TurnSignals.Attach(car, b); } catch (System.Exception e) { Debug.LogWarning("Sinyal lambası eklenemedi: " + e.Message); }
            cars.Add(d);
            Park(d);
            Respawn(d, near);
        }

        /// <summary>Aracı gizler (havuzda bekler).</summary>
        void Park(TrafficDriver d)
        {
            d.car.rb.isKinematic = true; d.far = true; d.hidden = true;
            if (d.signals != null) d.signals.Set(0f);
            Vector3 p = d.transform.position;
            d.car.Teleport(new Vector3(p.x, -500f, p.z), Quaternion.identity);
        }

        /// <summary>Oyuncudan ≥120 m uzakta ve kamera görüşü dışında bir şeride yerleştirir. false: uygun yer yok (gizli kalır).</summary>
        public bool Respawn(TrafficDriver d, Vector3 near)
        {
            var graph = Game.I.world.graph;
            Vector3 pp = Game.I.player != null ? Game.I.player.transform.position : near;
            // yüksek hızda trafik daha uzağa doğar (200 km/s'de ~5 sn yol) — burnunun dibinde araç belirmesin
            float pv = Game.I.player != null ? Game.I.player.SpeedKmh / 3.6f : 0f;
            float minD = Mathf.Max(SpawnMin, pv * 5f), maxD = Mathf.Max(SpawnMax, minD + 150f);
            for (int tries = 0; tries < 6; tries++)
            {
                int a = graph.RandomNodeAround(near, minD, maxD);
                var adj = graph.adj[a];
                if (adj.Count == 0) continue;
                int b = adj[Random.Range(0, adj.Count)];
                Vector3 pa = graph.nodes[a], pb = graph.nodes[b];
                Vector3 dir = U.Flat(pb - pa);
                if (dir.sqrMagnitude < 1f) continue;
                dir.Normalize();
                float off = Mathf.Min(graph.lane[a], graph.lane[b]);
                Vector3 pos = Vector3.Lerp(pa, pb, Random.Range(0.25f, 0.6f)) + new Vector3(dir.z, 0, -dir.x) * off + Vector3.up * 0.4f;
                if (U.FlatDist(pos, pp) < minD || Visible(pos)) continue;
                if (U.CarNearby(pos, 6f, d.car.rb)) continue;
                d.hidden = false;
                d.car.Teleport(pos, Quaternion.LookRotation(dir));
                d.Init(a, b);
                if (Random.value < 0.5f && d.car.paintMats != null)
                {
                    var col = Catalog.Paints[Random.Range(0, Catalog.Paints.Length)];
                    foreach (var m in d.car.paintMats) U.ApplyPaint(m, col);
                }
                d.far = false; d.car.rb.isKinematic = false;
                d.SetFar(U.FlatDist(pos, pp) > 120f);
                return true;
            }
            if (!d.hidden) Park(d);
            return false;
        }

        public void ClearAround(Vector3 p, float r)
        {
            foreach (var c in cars) if (c != null && !c.hidden && U.FlatDist(c.transform.position, p) < r) { Park(c); Respawn(c, p + new Vector3(500, 0, 500)); }
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
            rb.maxDepenetrationVelocity = 4f;
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
