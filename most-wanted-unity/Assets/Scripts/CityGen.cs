using System;
using System.Collections.Generic;

namespace MostWanted.Gen
{
    /// <summary>Basit 2B vektör (x, z) — Unity'den bağımsız şehir üretici için.</summary>
    public struct V2
    {
        public float x, z;
        public V2(float x, float z) { this.x = x; this.z = z; }
        public static V2 operator +(V2 a, V2 b) { return new V2(a.x + b.x, a.z + b.z); }
        public static V2 operator -(V2 a, V2 b) { return new V2(a.x - b.x, a.z - b.z); }
        public static V2 operator *(V2 a, float s) { return new V2(a.x * s, a.z * s); }
        public float Len { get { return (float)Math.Sqrt(x * x + z * z); } }
        public V2 Norm { get { float l = Len; return l > 1e-6f ? new V2(x / l, z / l) : new V2(0, 0); } }
        public V2 Right { get { return new V2(z, -x); } }       // Unity sol el: ileri (0,1) → sağ (1,0)
        public static float Dot(V2 a, V2 b) { return a.x * b.x + a.z * b.z; }
        public static float Cross(V2 a, V2 b) { return a.x * b.z - a.z * b.x; }
        public static float Dist(V2 a, V2 b) { return (a - b).Len; }
        public static V2 Lerp(V2 a, V2 b, float t) { return new V2(a.x + (b.x - a.x) * t, a.z + (b.z - a.z) * t); }
        public override string ToString() { return x.ToString("0.0") + "," + z.ToString("0.0"); }
    }

    public enum RoadClass { Street, Avenue, Boulevard, Highway, Coastal }
    public enum Zone { Downtown, Midrise, Residential, Suburb, Industrial, Harbour, Park, Rural }

    public class RNode
    {
        public V2 p; public float y; public List<int> edges = new List<int>();
    }

    public class REdge
    {
        public int a, b;
        public RoadClass cls;
        public List<V2> pts = new List<V2>();   // a'dan b'ye polyline (uçlar dahil)
        public float[] ys;
        public bool[] bridge, tunnel;
        public float Width { get { return CityGen.Width(cls); } }
        public float Length
        {
            get { float l = 0f; for (int i = 1; i < pts.Count; i++) l += V2.Dist(pts[i - 1], pts[i]); return l; }
        }
    }

    public struct Box
    {
        public V2 c; public float w, d, rot, y0, h;   // w: yanal (sağ), d: derinlik (ileri), rot: radyan (ileri yön açısı)
    }

    public class Building
    {
        public List<Box> parts = new List<Box>();
        public Zone zone;
        public int style;          // 0 cam kule, 1 beton, 2 tuğla, 3 sıva, 4 ev (çatılı), 5 depo, 6 otopark
        public float height;
        public bool helipad, waterTank, pitched, garage;
        public V2 front;           // cadde yönü (birim)
    }

    public class RaceSpec
    {
        public string name; public int type; public int laps = 1; public int prize; public List<int> nodes = new List<int>();
        public List<int> special = new List<int>();
    }

    /// <summary>
    /// "Kendi Şehrimiz": ~3 × 3.5 km prosedürel şehir. Tüm yollar tek bir veriden üretilir; trafik/polis/yarış ağı
    /// da aynı veridir (şerit hizası birebir). Sadece System kullanır → Unity dışında da çalışır (test/önizleme).
    /// </summary>
    public class CityGen
    {
        public readonly List<RNode> nodes = new List<RNode>();
        public readonly List<REdge> edges = new List<REdge>();
        public readonly List<Building> buildings = new List<Building>();
        public readonly List<RaceSpec> races = new List<RaceSpec>();
        public readonly List<V2> river = new List<V2>();
        public readonly List<KeyValuePair<string, V2>> districts = new List<KeyValuePair<string, V2>>();
        public readonly List<V2> hiding = new List<V2>();
        public V2 lakeC = new V2(-760f, 640f);
        public float lakeR = 115f;
        public V2 center = new V2(0f, 0f);
        public float waterLevel = -2f;
        public int garageNode = -1;
        public V2 garageDir;
        public float minX = -3000f, maxX = 3200f, minZ = -3300f, maxZ = 3200f;
        readonly Random rnd;

        public CityGen(int seed) { rnd = new Random(seed); }
        float R01() { return (float)rnd.NextDouble(); }

        public static float Width(RoadClass c)
        {
            switch (c)
            {
                case RoadClass.Highway: return 27f;
                case RoadClass.Boulevard: return 18f;
                case RoadClass.Avenue: return 14f;
                case RoadClass.Coastal: return 12f;
                default: return 9f;
            }
        }

        /// <summary>Yol ağı şerit ofseti (sağ şerit merkezi).</summary>
        public static float Lane(RoadClass c)
        {
            switch (c)
            {
                case RoadClass.Highway: return 6.6f;
                case RoadClass.Boulevard: return 4.6f;
                case RoadClass.Avenue: return 3.6f;
                case RoadClass.Coastal: return 3f;
                default: return 2.3f;
            }
        }

        public static float Sidewalk(RoadClass c)
        {
            switch (c)
            {
                case RoadClass.Highway: return 0f;
                case RoadClass.Coastal: return 2.5f;
                case RoadClass.Street: return 3f;
                default: return 4.5f;
            }
        }

        // ------------------------------------------------------------------ arazi
        static float Hash(int x, int z)
        {
            unchecked
            {
                int h = x * 374761393 + z * 668265263;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
            }
        }

        static float Smooth(float t) { return t * t * (3f - 2f * t); }

        public static float Noise(float x, float z)
        {
            int xi = (int)Math.Floor(x), zi = (int)Math.Floor(z);
            float fx = x - xi, fz = z - zi;
            float a = Hash(xi, zi), b = Hash(xi + 1, zi), c = Hash(xi, zi + 1), d = Hash(xi + 1, zi + 1);
            float u = Smooth(fx), v = Smooth(fz);
            return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
        }

        public float SeaX(float z) { return 1440f + 60f * (float)Math.Sin(z / 420f) + (z < -900f ? (z + 900f) * -0.15f : 0f); }

        /// <summary>Doğal arazi yüksekliği (yollar bunu yumuşatarak izler).</summary>
        public float H(float x, float z)
        {
            float r = (float)Math.Sqrt(x * x + z * z);
            float h = 6f;
            h += 16f * (float)Math.Exp(-(r * r) / (2f * 380f * 380f));                      // merkez tepesi (gökdelenler)
            h += 9f * (Noise(x / 520f + 11.3f, z / 520f + 7.1f) - 0.5f) * 2f;               // hafif engebe
            float dw = (float)Math.Sqrt((x + 1520f) * (x + 1520f) + (z - 180f) * (z - 180f)); // batı tepesi (tünel)
            h += 62f * (float)Math.Exp(-(dw * dw) / (2f * 230f * 230f));
            // uzak dağlar
            float edge = Math.Max(Math.Max(-x - 1900f, x - 2600f), Math.Max(-z - 2200f, z - 2100f));
            if (edge > 0f) h += Math.Min(220f, edge * 0.25f) * (0.6f + 0.4f * Noise(x / 400f, z / 400f));
            // deniz
            float sx = SeaX(z);
            if (x > sx - 60f) h = Lerp(h, waterLevel - 12f, Clamp01((x - (sx - 60f)) / 160f));
            // nehir
            float rz = RiverZ(x);
            float dr = Math.Abs(z - rz);
            if (x > -1700f && dr < 70f) h = Lerp(waterLevel - 5f, h, Clamp01((dr - 28f) / 42f));
            // göl
            float dl = V2.Dist(new V2(x, z), lakeC);
            if (dl < lakeR + 40f) h = Lerp(waterLevel + 1f - 4f, h, Clamp01((dl - lakeR + 10f) / 50f));
            return h;
        }

        public float RiverZ(float x) { return 1240f + 45f * (float)Math.Sin(x / 260f) + (x < -900f ? (-900f - x) * 0.25f : 0f); }

        public static float Lerp(float a, float b, float t) { return a + (b - a) * t; }
        public static float Clamp01(float t) { return t < 0f ? 0f : t > 1f ? 1f : t; }

        public Zone ZoneAt(V2 p)
        {
            float r = p.Len;
            if (V2.Dist(p, lakeC) < lakeR + 150f) return Zone.Park;
            if (p.x > 1000f && p.z > -650f && p.z < 1150f) return Zone.Harbour;
            if (p.x > 320f && p.z < -780f) return Zone.Industrial;
            if (r < 340f) return Zone.Downtown;
            if (r < 640f) return Zone.Midrise;
            if (p.z < -1120f || r > 1120f) return Zone.Suburb;
            return Zone.Residential;
        }

        // ------------------------------------------------------------------ üretim
        readonly List<KeyValuePair<RoadClass, List<V2>>> polys = new List<KeyValuePair<RoadClass, List<V2>>>();

        void Line(RoadClass c, params V2[] pts) { polys.Add(new KeyValuePair<RoadClass, List<V2>>(c, Resample(new List<V2>(pts), 12f))); }
        void Poly(RoadClass c, List<V2> pts) { polys.Add(new KeyValuePair<RoadClass, List<V2>>(c, Resample(pts, 12f))); }

        static List<V2> Resample(List<V2> pts, float step)
        {
            var o = new List<V2> { pts[0] };
            for (int i = 1; i < pts.Count; i++)
            {
                float l = V2.Dist(pts[i - 1], pts[i]);
                int n = Math.Max(1, (int)Math.Ceiling(l / step));
                for (int k = 1; k <= n; k++) o.Add(V2.Lerp(pts[i - 1], pts[i], k / (float)n));
            }
            return o;
        }

        /// <summary>Catmull-Rom ile kapalı/açık eğri.</summary>
        static List<V2> Spline(List<V2> c, bool closed, float step)
        {
            var o = new List<V2>();
            int n = c.Count;
            int segs = closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                V2 p0 = c[closed ? (i - 1 + n) % n : Math.Max(i - 1, 0)], p1 = c[i], p2 = c[(i + 1) % n], p3 = c[closed ? (i + 2) % n : Math.Min(i + 2, n - 1)];
                float len = V2.Dist(p1, p2);
                int k = Math.Max(2, (int)Math.Ceiling(len / step));
                for (int j = 0; j < k; j++)
                {
                    float t = j / (float)k, t2 = t * t, t3 = t2 * t;
                    o.Add((p1 * 2f + (p2 - p0) * t + (p0 * 2f - p1 * 5f + p2 * 4f - p3) * t2 + (p1 * 3f - p0 - p2 * 3f + p3) * t3) * 0.5f);
                }
            }
            if (closed) o.Add(o[0]); else o.Add(c[n - 1]);
            return o;
        }

        public void Generate()
        {
            // nehir
            for (float x = -1800f; x <= 1500f; x += 40f) river.Add(new V2(x, RiverZ(x)));

            // --- merkez ızgarası (Avenue)
            for (int i = -3; i <= 3; i++)
            {
                Line(RoadClass.Avenue, new V2(i * 110f, -330f), new V2(i * 110f, 330f));
                Line(RoadClass.Avenue, new V2(-330f, i * 110f), new V2(330f, i * 110f));
            }
            // --- iç ve dış çevre bulvarları
            var ring1 = new List<V2>(); var ring2 = new List<V2>();
            for (int k = 0; k < 24; k++)
            {
                float a = k / 24f * (float)Math.PI * 2f;
                ring1.Add(new V2((float)Math.Cos(a) * 620f, (float)Math.Sin(a) * 600f));
                float r2 = 1080f + 70f * (float)Math.Sin(a * 3f);
                ring2.Add(new V2((float)Math.Cos(a) * r2 * 0.95f, (float)Math.Sin(a) * r2 * 1.02f));
            }
            Poly(RoadClass.Boulevard, Spline(ring1, true, 12f));
            Poly(RoadClass.Boulevard, Spline(ring2, true, 12f));
            // --- radyal caddeler (8 yön) — merkez ızgara kenarından otoyola
            for (int k = 0; k < 8; k++)
            {
                float a = k / 8f * (float)Math.PI * 2f + 0.0f;
                V2 d = new V2((float)Math.Cos(a), (float)Math.Sin(a));
                V2 s = k % 2 == 0 ? d * (k % 4 == 0 ? 330f : 330f) : d * 466f;
                if (k % 2 == 0) s = new V2(Math.Sign(Math.Round(d.x)) * 330f, Math.Sign(Math.Round(d.z)) * 330f);
                if (k % 2 == 0) s = new V2(Math.Abs(d.x) > 0.5f ? Math.Sign(d.x) * 330f : 0f, Math.Abs(d.z) > 0.5f ? Math.Sign(d.z) * 330f : 0f);
                else s = new V2(Math.Sign(d.x) * 330f, Math.Sign(d.z) * 330f);
                V2 e = d * 1500f;
                var mid = V2.Lerp(s, e, 0.55f) + d.Right * (35f * (k % 2 == 0 ? 1f : -1f));
                var curve = Spline(new List<V2> { s, mid, e }, false, 12f);
                // otoyolun içinde kalsın
                var clipped = new List<V2>();
                foreach (var p in curve) { if (!InsideHighway(p, 25f)) break; clipped.Add(p); }
                if (clipped.Count > 2) Poly(RoadClass.Avenue, clipped);
            }
            // --- konut ızgarası (batı/kuzeybatı), park ve göl hariç
            Grid(RoadClass.Street, -1080f, -320f, -560f, 1060f, 95f, 95f);
            Grid(RoadClass.Street, -300f, 700f, 680f, 1100f, 100f, 105f);
            // --- sanayi
            Grid(RoadClass.Avenue, 360f, 1240f, -1560f, -820f, 175f, 185f);
            // --- banliyö (güney): kıvrımlı sokaklar
            for (int j = 0; j < 4; j++)
            {
                float z0 = -1200f - j * 135f;
                var l = new List<V2>();
                for (float x = -1250f; x <= 260f; x += 60f) l.Add(new V2(x, z0 + 28f * (float)Math.Sin(x / 140f + j)));
                Poly(RoadClass.Street, Spline(l, false, 12f));
            }
            for (float x = -1150f; x <= 250f; x += 280f) Line(RoadClass.Street, new V2(x, -1150f), new V2(x + 30f, -1640f));
            // --- liman: sahil yolu + iskeleler
            var coast = new List<V2>();
            for (float z = -1700f; z <= 1600f; z += 80f) coast.Add(new V2(SeaX(z) - 150f, z));
            Poly(RoadClass.Coastal, Spline(coast, false, 12f));
            for (float z = -400f; z <= 800f; z += 300f) Line(RoadClass.Street, new V2(1020f, z), new V2(SeaX(z) - 150f, z));
            // --- otoyol halkası (deniz kıyısı + batı tepesinde tünel + nehir üzerinde asma köprü)
            var hw = new List<V2>
            {
                new V2(-1480f, -1500f), new V2(-1520f, -400f), new V2(-1500f, 650f), new V2(-1350f, 1500f),
                new V2(-500f, 1750f), new V2(600f, 1720f), new V2(1250f, 1600f), new V2(SeaX(1000f) - 40f, 900f),
                new V2(SeaX(0f) - 30f, 0f), new V2(SeaX(-900f) - 40f, -900f), new V2(1200f, -1800f),
                new V2(200f, -1900f), new V2(-900f, -1850f)
            };
            Poly(RoadClass.Highway, Spline(hw, true, 12f));

            Planarize();
            Heights();
            PlaceBuildings();
            Districts();
            Races();
        }

        public bool InsideHighway(V2 p, float margin)
        {
            // kaba: otoyol halkasının içi (dikdörtgensel yaklaşım)
            return p.x > -1450f + margin && p.x < SeaX(p.z) - 120f + margin * 0f && p.z > -1820f + margin && p.z < 1660f - margin;
        }

        void Grid(RoadClass c, float x0, float x1, float z0, float z1, float sx, float sz)
        {
            for (float x = x0; x <= x1 + 0.1f; x += sx) ClippedLine(c, new V2(x, z0), new V2(x, z1));
            for (float z = z0; z <= z1 + 0.1f; z += sz) ClippedLine(c, new V2(x0, z), new V2(x1, z));
        }

        /// <summary>Göl/park ve nehir dışında kalan parçaları ekle.</summary>
        void ClippedLine(RoadClass c, V2 a, V2 b)
        {
            var pts = Resample(new List<V2> { a, b }, 6f);
            var cur = new List<V2>();
            foreach (var p in pts)
            {
                bool ok = V2.Dist(p, lakeC) > lakeR + 60f && Math.Abs(p.z - RiverZ(p.x)) > 40f;
                if (ok) cur.Add(p);
                else { if (cur.Count > 4) polys.Add(new KeyValuePair<RoadClass, List<V2>>(c, cur)); cur = new List<V2>(); }
            }
            if (cur.Count > 4) polys.Add(new KeyValuePair<RoadClass, List<V2>>(c, cur));
        }

        // ------------------------------------------------------------------ düzlemselleştirme → graf
        class Seg { public V2 a, b; public RoadClass c; public int poly; public List<float> cuts = new List<float>(); }

        static bool SegX(V2 p, V2 p2, V2 q, V2 q2, out float t, out float u)
        {
            V2 r = p2 - p, s = q2 - q;
            float d = V2.Cross(r, s);
            t = u = 0f;
            if (Math.Abs(d) < 1e-6f) return false;
            V2 qp = q - p;
            t = V2.Cross(qp, s) / d; u = V2.Cross(qp, r) / d;
            return t > 1e-4f && t < 1f - 1e-4f && u > 1e-4f && u < 1f - 1e-4f;
        }

        void Planarize()
        {
            // 1) segmentler
            var segs = new List<Seg>();
            for (int pi = 0; pi < polys.Count; pi++)
            {
                var pl = polys[pi].Value;
                for (int i = 1; i < pl.Count; i++) if (V2.Dist(pl[i - 1], pl[i]) > 0.01f) segs.Add(new Seg { a = pl[i - 1], b = pl[i], c = polys[pi].Key, poly = pi });
            }
            // 2) kesişimler (uzamsal ızgara)
            const float cell = 60f;
            var grid = new Dictionary<long, List<int>>();
            for (int i = 0; i < segs.Count; i++)
            {
                var s = segs[i];
                int x0 = (int)Math.Floor(Math.Min(s.a.x, s.b.x) / cell), x1 = (int)Math.Floor(Math.Max(s.a.x, s.b.x) / cell);
                int z0 = (int)Math.Floor(Math.Min(s.a.z, s.b.z) / cell), z1 = (int)Math.Floor(Math.Max(s.a.z, s.b.z) / cell);
                for (int x = x0; x <= x1; x++) for (int z = z0; z <= z1; z++)
                    {
                        long k = ((long)x << 32) ^ (uint)z;
                        List<int> l; if (!grid.TryGetValue(k, out l)) grid[k] = l = new List<int>(); l.Add(i);
                    }
            }
            var pairs = new HashSet<long>();
            foreach (var l in grid.Values)
                for (int i = 0; i < l.Count; i++)
                    for (int j = i + 1; j < l.Count; j++)
                    {
                        int a = l[i], b = l[j];
                        if (segs[a].poly == segs[b].poly) continue;
                        long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                        if (!pairs.Add(key)) continue;
                        // otoyol yalnızca radyallerle kesişebilir (kavşak); diğerleriyle üst geçit kabul edilir → kesme
                        float t, u;
                        if (SegX(segs[a].a, segs[a].b, segs[b].a, segs[b].b, out t, out u)) { segs[a].cuts.Add(t); segs[b].cuts.Add(u); }
                    }
            // 3) noktaları birleştirerek düğümler; her poly zincir halinde kenarlara
            var vid = new Dictionary<long, int>();
            var vpos = new List<V2>();
            var vAdj = new List<Dictionary<int, RoadClass>>();
            Func<V2, int> V = p =>
            {
                long k = ((long)Math.Round(p.x / 2.5f) << 32) ^ (uint)(int)Math.Round(p.z / 2.5f);
                int id;
                if (vid.TryGetValue(k, out id)) return id;
                // komşu hücrelerde yakın nokta?
                for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
                    {
                        long k2 = ((long)(Math.Round(p.x / 2.5f) + dx) << 32) ^ (uint)(int)(Math.Round(p.z / 2.5f) + dz);
                        if (vid.TryGetValue(k2, out id) && V2.Dist(vpos[id], p) < 2.6f) { vid[k] = id; return id; }
                    }
                id = vpos.Count; vpos.Add(p); vAdj.Add(new Dictionary<int, RoadClass>()); vid[k] = id;
                return id;
            };
            foreach (var s in segs)
            {
                s.cuts.Add(0f); s.cuts.Add(1f); s.cuts.Sort();
                int prev = -1;
                foreach (float t in s.cuts)
                {
                    int v = V(V2.Lerp(s.a, s.b, t));
                    if (prev >= 0 && prev != v)
                    {
                        RoadClass c0;
                        if (!vAdj[prev].TryGetValue(v, out c0) || c0 < s.c) { vAdj[prev][v] = s.c; vAdj[v][prev] = s.c; }
                    }
                    prev = v;
                }
            }
            // 4) kısa çıkıntıları buda (birleştirme artıkları)
            for (int iter = 0; iter < 4; iter++)
                for (int v = 0; v < vpos.Count; v++)
                {
                    if (vAdj[v].Count != 1) continue;
                    // zincir boyunca 18 m'den kısa uç → sil
                    var chain = new List<int> { v };
                    int cur = v, last = -1; float len = 0f;
                    while (vAdj[cur].Count <= 2 && len < 18f)
                    {
                        int nx = -1; foreach (var kv in vAdj[cur]) if (kv.Key != last) { nx = kv.Key; break; }
                        if (nx < 0) break;
                        len += V2.Dist(vpos[cur], vpos[nx]); last = cur; cur = nx; chain.Add(cur);
                        if (vAdj[cur].Count != 2) break;
                    }
                    if (len < 18f && vAdj[cur].Count >= 3)
                        for (int i = 0; i < chain.Count - 1; i++) { vAdj[chain[i]].Remove(chain[i + 1]); vAdj[chain[i + 1]].Remove(chain[i]); }
                }
            // 5) kavşaklar (derece != 2) düğüm; aradaki derece-2 zincirleri kenar
            var nodeOf = new Dictionary<int, int>();
            for (int v = 0; v < vpos.Count; v++)
                if (vAdj[v].Count != 2 && vAdj[v].Count > 0) { nodeOf[v] = nodes.Count; nodes.Add(new RNode { p = vpos[v] }); }
            // döngü kontrolü: hiç düğüm içermeyen kapalı zincirler için bir düğüm ata
            var visited = new HashSet<long>();
            Func<int, int, long> EK = (a, b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            Action<int, int> Walk = (start, first) =>
            {
                if (visited.Contains(EK(start, first))) return;
                var e = new REdge { a = nodeOf[start], cls = vAdj[start][first] };
                e.pts.Add(vpos[start]);
                int prev = start, cur = first;
                visited.Add(EK(prev, cur));
                while (!nodeOf.ContainsKey(cur))
                {
                    e.pts.Add(vpos[cur]);
                    int nx = -1; foreach (var kv in vAdj[cur]) if (kv.Key != prev) { nx = kv.Key; break; }
                    if (nx < 0) break;
                    RoadClass c2 = vAdj[cur][nx]; if (c2 > e.cls) e.cls = c2;
                    prev = cur; cur = nx;
                    visited.Add(EK(prev, cur));
                    if (cur == start) break;
                }
                if (!nodeOf.ContainsKey(cur)) { nodeOf[cur] = nodes.Count; nodes.Add(new RNode { p = vpos[cur] }); }
                e.pts.Add(vpos[cur]);
                e.b = nodeOf[cur];
                if (e.a == e.b && e.pts.Count < 6) return;
                int id = edges.Count; edges.Add(e);
                nodes[e.a].edges.Add(id); if (e.b != e.a) nodes[e.b].edges.Add(id);
            };
            foreach (var kv in new List<KeyValuePair<int, int>>(nodeOf))
                foreach (var nb in new List<int>(vAdj[kv.Key].Keys)) Walk(kv.Key, nb);
            for (int v = 0; v < vpos.Count; v++)
                if (vAdj[v].Count == 2 && !nodeOf.ContainsKey(v))
                    foreach (var nb in vAdj[v].Keys)
                        if (!visited.Contains(EK(v, nb))) { nodeOf[v] = nodes.Count; nodes.Add(new RNode { p = vpos[v] }); foreach (var nb2 in new List<int>(vAdj[v].Keys)) Walk(v, nb2); break; }
            // yalnızca en büyük bağlı bileşeni tut
            KeepLargestComponent();
        }

        void KeepLargestComponent()
        {
            int n = nodes.Count;
            var comp = new int[n]; for (int i = 0; i < n; i++) comp[i] = -1;
            int best = -1, bestSize = 0, cid = 0;
            for (int s = 0; s < n; s++)
            {
                if (comp[s] >= 0) continue;
                var st = new Stack<int>(); st.Push(s); comp[s] = cid; int size = 0;
                while (st.Count > 0)
                {
                    int u = st.Pop(); size++;
                    foreach (int ei in nodes[u].edges) { var e = edges[ei]; int w = e.a == u ? e.b : e.a; if (comp[w] < 0) { comp[w] = cid; st.Push(w); } }
                }
                if (size > bestSize) { bestSize = size; best = cid; }
                cid++;
            }
            var remap = new Dictionary<int, int>();
            var nn = new List<RNode>();
            for (int i = 0; i < n; i++) if (comp[i] == best) { remap[i] = nn.Count; nn.Add(new RNode { p = nodes[i].p }); }
            var ne = new List<REdge>();
            foreach (var e in edges)
            {
                if (!remap.ContainsKey(e.a) || !remap.ContainsKey(e.b)) continue;
                e.a = remap[e.a]; e.b = remap[e.b];
                int id = ne.Count; ne.Add(e);
                nn[e.a].edges.Add(id); if (e.b != e.a) nn[e.b].edges.Add(id);
            }
            nodes.Clear(); nodes.AddRange(nn); edges.Clear(); edges.AddRange(ne);
        }

        // ------------------------------------------------------------------ yükseklikler (≤ %8 eğim, köprü, tünel)
        void Heights()
        {
            // düğüm yükseklikleri: arazi (yumuşatılmış), köprüde su üstünde
            foreach (var n in nodes) n.y = Math.Max(H(n.p.x, n.p.z), waterLevel + 2f);
            // komşu düğümler arası eğim sınırı (birkaç tur)
            for (int it = 0; it < 12; it++)
                foreach (var e in edges)
                {
                    float len = Math.Max(1f, e.Length);
                    float maxD = len * 0.075f;
                    var na = nodes[e.a]; var nb = nodes[e.b];
                    float d = nb.y - na.y;
                    if (Math.Abs(d) > maxD) { float fix = (Math.Abs(d) - maxD) * 0.5f * Math.Sign(d); na.y += fix; nb.y -= fix; }
                }
            foreach (var e in edges)
            {
                int m = e.pts.Count;
                e.ys = new float[m]; e.bridge = new bool[m]; e.tunnel = new bool[m];
                var s = new float[m];
                for (int i = 1; i < m; i++) s[i] = s[i - 1] + V2.Dist(e.pts[i - 1], e.pts[i]);
                float L = Math.Max(1f, s[m - 1]);
                float ya = nodes[e.a].y, yb = nodes[e.b].y;
                var raw = new float[m];
                for (int i = 0; i < m; i++)
                {
                    float h = H(e.pts[i].x, e.pts[i].z);
                    bool water = h < waterLevel + 1f;
                    e.bridge[i] = water;
                    raw[i] = water ? waterLevel + (e.cls == RoadClass.Highway ? 22f : 8f) : h;
                }
                // köprü çevresini yükselt (rampalar)
                for (int i = 0; i < m; i++)
                    if (e.bridge[i]) for (int k = Math.Max(0, i - 25); k < Math.Min(m, i + 25); k++) raw[k] = Math.Max(raw[k], raw[i] - Math.Abs(s[k] - s[i]) * 0.06f);
                // uçlara bağla + yumuşat + eğim sınırı
                for (int i = 0; i < m; i++)
                {
                    float t = s[i] / L;
                    float wa = Clamp01(1f - s[i] / 60f), wb = Clamp01(1f - (L - s[i]) / 60f);
                    float y = raw[i];
                    y = Lerp(y, ya + (yb - ya) * t, Math.Max(wa, wb));
                    e.ys[i] = y;
                }
                e.ys[0] = ya; e.ys[m - 1] = yb;
                for (int pass = 0; pass < 6; pass++)
                {
                    for (int i = 1; i < m - 1; i++) e.ys[i] = (e.ys[i - 1] + e.ys[i] * 2f + e.ys[i + 1]) * 0.25f;
                    for (int i = 1; i < m; i++)
                    {
                        float ds = s[i] - s[i - 1], md = ds * 0.08f;
                        if (e.ys[i] - e.ys[i - 1] > md) e.ys[i] = e.ys[i - 1] + md;
                        if (e.ys[i - 1] - e.ys[i] > md) e.ys[i] = e.ys[i - 1] - md;
                    }
                    for (int i = m - 2; i >= 0; i--)
                    {
                        float ds = s[i + 1] - s[i], md = ds * 0.08f;
                        if (e.ys[i] - e.ys[i + 1] > md) e.ys[i] = e.ys[i + 1] + md;
                        if (e.ys[i + 1] - e.ys[i] > md) e.ys[i] = e.ys[i + 1] - md;
                    }
                    e.ys[0] = ya; e.ys[m - 1] = yb;
                }
                for (int i = 0; i < m; i++)
                {
                    float h = H(e.pts[i].x, e.pts[i].z);
                    e.bridge[i] = h < e.ys[i] - 5f;
                    e.tunnel[i] = e.cls == RoadClass.Highway && h > e.ys[i] + 10f;
                }
            }
        }

        // ------------------------------------------------------------------ binalar
        readonly Dictionary<long, List<Box>> occ = new Dictionary<long, List<Box>>();
        readonly List<KeyValuePair<V2, float>> roadSegs = new List<KeyValuePair<V2, float>>();

        static V2[] Corners(Box b)
        {
            V2 f = new V2((float)Math.Sin(b.rot), (float)Math.Cos(b.rot)), r = f.Right;
            return new[] { b.c + r * (b.w / 2) + f * (b.d / 2), b.c - r * (b.w / 2) + f * (b.d / 2), b.c - r * (b.w / 2) - f * (b.d / 2), b.c + r * (b.w / 2) - f * (b.d / 2) };
        }

        static bool Overlap(Box a, Box b)
        {
            // ayırma ekseni testi (4 eksen)
            var ca = Corners(a); var cb = Corners(b);
            V2[] axes = { new V2((float)Math.Sin(a.rot), (float)Math.Cos(a.rot)), new V2((float)Math.Cos(a.rot), -(float)Math.Sin(a.rot)), new V2((float)Math.Sin(b.rot), (float)Math.Cos(b.rot)), new V2((float)Math.Cos(b.rot), -(float)Math.Sin(b.rot)) };
            foreach (var ax in axes)
            {
                float amin = float.MaxValue, amax = float.MinValue, bmin = float.MaxValue, bmax = float.MinValue;
                foreach (var p in ca) { float d = V2.Dot(p, ax); amin = Math.Min(amin, d); amax = Math.Max(amax, d); }
                foreach (var p in cb) { float d = V2.Dot(p, ax); bmin = Math.Min(bmin, d); bmax = Math.Max(bmax, d); }
                if (amax <= bmin || bmax <= amin) return false;
            }
            return true;
        }

        bool Free(Box b)
        {
            int cx = (int)Math.Floor(b.c.x / 80f), cz = (int)Math.Floor(b.c.z / 80f);
            for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
                {
                    List<Box> l; if (!occ.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out l)) continue;
                    foreach (var o in l) if (Overlap(o, b)) return false;
                }
            return true;
        }

        void Occupy(Box b)
        {
            long k = ((long)(int)Math.Floor(b.c.x / 80f) << 32) ^ (uint)(int)Math.Floor(b.c.z / 80f);
            List<Box> l; if (!occ.TryGetValue(k, out l)) occ[k] = l = new List<Box>(); l.Add(b);
        }

        void PlaceBuildings()
        {
            // yollar da dolu alan (yol + kaldırım)
            foreach (var e in edges)
                for (int i = 1; i < e.pts.Count; i++)
                {
                    V2 a = e.pts[i - 1], b = e.pts[i], d = b - a;
                    float len = d.Len; if (len < 0.1f) continue;
                    Occupy(new Box { c = V2.Lerp(a, b, 0.5f), w = e.Width + Sidewalk(e.cls) * 2f + 1f, d = len + 1f, rot = (float)Math.Atan2(d.x, d.z) });
                }
            foreach (var n in nodes) Occupy(new Box { c = n.p, w = 24f, d = 24f, rot = 0f });
            // nehir/göl/deniz kenarı da dolu
            for (int i = 1; i < river.Count; i++)
            {
                V2 a = river[i - 1], b = river[i], d = b - a;
                Occupy(new Box { c = V2.Lerp(a, b, 0.5f), w = 140f, d = d.Len + 2f, rot = (float)Math.Atan2(d.x, d.z) });
            }
            int parkingGarages = 0;
            foreach (var e in edges)
            {
                if (e.cls == RoadClass.Highway) continue;
                float acc = 0f;
                for (int i = 1; i < e.pts.Count; i++)
                {
                    V2 a = e.pts[i - 1], b = e.pts[i], d = (b - a);
                    float len = d.Len; if (len < 0.1f) continue;
                    acc += len;
                    if (acc < 26f) continue;
                    acc = 0f;
                    V2 f = d.Norm, r = f.Right;
                    if (e.bridge[i] || e.tunnel[i]) continue;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        V2 mid = V2.Lerp(a, b, 0.5f);
                        Zone z = ZoneAt(mid);
                        if (z == Zone.Park) continue;
                        float off = e.Width / 2f + Sidewalk(e.cls) + 1.5f;
                        float w, dep, h; int style;
                        bool garage = false;
                        switch (z)
                        {
                            case Zone.Downtown:
                                w = 24f + R01() * 20f; dep = 26f + R01() * 22f;
                                float cd = mid.Len;
                                h = 45f + (1f - cd / 340f) * 150f * (0.5f + R01() * 0.7f);
                                style = R01() < 0.55f ? 0 : 1;
                                if (parkingGarages < 3 && R01() < 0.04f) { garage = true; style = 6; h = 14f; parkingGarages++; }
                                break;
                            case Zone.Midrise: w = 20f + R01() * 16f; dep = 18f + R01() * 14f; h = 14f + R01() * 34f; style = 1 + rnd.Next(3); break;
                            case Zone.Industrial: w = 40f + R01() * 40f; dep = 30f + R01() * 30f; h = 9f + R01() * 9f; style = 5; off += 6f; break;
                            case Zone.Harbour: w = 30f + R01() * 30f; dep = 22f + R01() * 20f; h = 8f + R01() * 10f; style = R01() < 0.6f ? 5 : 2; off += 4f; break;
                            case Zone.Suburb: w = 10f + R01() * 4f; dep = 9f + R01() * 4f; h = 5.5f + R01() * 3f; style = 4; off += 6f + R01() * 3f; break;
                            default: w = 14f + R01() * 10f; dep = 12f + R01() * 8f; h = 9f + R01() * 15f; style = 2 + rnd.Next(2); off += 1f; break;
                        }
                        V2 c = mid + r * (side * (off + dep / 2f));
                        float rot = (float)Math.Atan2(-side * r.x, -side * r.z);   // ön cephe yola bakar
                        var main = new Box { c = c, w = w, d = dep, rot = rot, y0 = 0f, h = h };
                        if (!Free(main)) continue;
                        var bld = new Building { zone = z, style = style, height = h, front = r * -side, garage = garage, pitched = style == 4 };
                        bld.parts.Add(main);
                        // L/U biçimleri ve kademeler
                        if ((style == 1 || style == 2 || style == 3) && R01() < 0.45f)
                        {
                            V2 fwd = new V2((float)Math.Sin(rot), (float)Math.Cos(rot));
                            var wing = new Box { c = c + fwd * (-dep / 2f - 6f) + fwd.Right * (w * 0.3f * (R01() < 0.5f ? 1 : -1)), w = w * 0.4f, d = 14f, rot = rot, y0 = 0f, h = h * (0.6f + R01() * 0.4f) };
                            if (Free(wing)) { bld.parts.Add(wing); Occupy(wing); }
                        }
                        if (style == 0 && h > 80f)
                        {
                            bld.parts.Add(new Box { c = c, w = w * 0.78f, d = dep * 0.78f, rot = rot, y0 = h, h = h * 0.25f });
                            if (h > 120f) bld.parts.Add(new Box { c = c, w = w * 0.55f, d = dep * 0.55f, rot = rot, y0 = h * 1.25f, h = h * 0.15f });
                            bld.helipad = R01() < 0.35f;
                        }
                        bld.waterTank = (style == 2 || style == 3) && R01() < 0.3f;
                        Occupy(main);
                        buildings.Add(bld);
                        if (garage) hiding.Add(c);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ semtler, yarışlar, garaj
        void Districts()
        {
            districts.Add(new KeyValuePair<string, V2>("Merkez", new V2(0, 0)));
            districts.Add(new KeyValuePair<string, V2>("Liman", new V2(1200, 250)));
            districts.Add(new KeyValuePair<string, V2>("Sanayi", new V2(800, -1200)));
            districts.Add(new KeyValuePair<string, V2>("Banliyö Güney", new V2(-500, -1400)));
            districts.Add(new KeyValuePair<string, V2>("Banliyö Kuzey", new V2(200, 900)));
            districts.Add(new KeyValuePair<string, V2>("Göl Parkı", lakeC));
            districts.Add(new KeyValuePair<string, V2>("Batı Konutları", new V2(-700, 150)));
            districts.Add(new KeyValuePair<string, V2>("Otoyol", new V2(-1500, -400)));
        }

        public int Nearest(V2 p, Func<int, bool> filter = null)
        {
            int best = -1; float bd = float.MaxValue;
            for (int i = 0; i < nodes.Count; i++)
            {
                if (filter != null && !filter(i)) continue;
                float d = V2.Dist(nodes[i].p, p);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        /// <summary>Kenar ağırlıklı en kısa yol (düğüm listesi).</summary>
        public List<int> Path(int s, int t, Func<REdge, bool> allow = null)
        {
            int n = nodes.Count;
            var dist = new float[n]; var prev = new int[n];
            for (int i = 0; i < n; i++) { dist[i] = float.MaxValue; prev[i] = -1; }
            dist[s] = 0f;
            var open = new SortedSet<KeyValuePair<float, int>>(Comparer<KeyValuePair<float, int>>.Create((a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key) : a.Value.CompareTo(b.Value)));
            open.Add(new KeyValuePair<float, int>(0f, s));
            while (open.Count > 0)
            {
                var top = open.Min; open.Remove(top);
                int u = top.Value;
                if (u == t) break;
                foreach (int ei in nodes[u].edges)
                {
                    var e = edges[ei];
                    if (allow != null && !allow(e)) continue;
                    int w = e.a == u ? e.b : e.a;
                    float nd = dist[u] + e.Length;
                    if (nd < dist[w]) { if (dist[w] < float.MaxValue) open.Remove(new KeyValuePair<float, int>(dist[w], w)); dist[w] = nd; prev[w] = u; open.Add(new KeyValuePair<float, int>(nd, w)); }
                }
            }
            var path = new List<int>();
            if (prev[t] < 0 && s != t) return path;
            for (int c = t; c != -1; c = prev[c]) path.Add(c);
            path.Reverse();
            return path;
        }

        List<int> Chain(params V2[] wps)
        {
            var res = new List<int>();
            for (int i = 0; i < wps.Length - 1; i++)
            {
                var p = Path(Nearest(wps[i]), Nearest(wps[i + 1]));
                for (int k = (i == 0 ? 0 : 1); k < p.Count; k++) res.Add(p[k]);
            }
            return res;
        }

        void Races()
        {
            Func<int, bool> notHw = i => { foreach (int ei in nodes[i].edges) if (edges[ei].cls == RoadClass.Highway) return false; return true; };
            Func<int, bool> isHw = i => { foreach (int ei in nodes[i].edges) if (edges[ei].cls == RoadClass.Highway) return true; return false; };
            // iç çevre turu
            var ring = new List<int>();
            for (int k = 0; k <= 8; k++) { float a = k / 8f * (float)Math.PI * 2f; ring.Add(Nearest(new V2((float)Math.Cos(a) * 620f, (float)Math.Sin(a) * 600f), notHw)); }
            var ringPath = new List<int>();
            for (int k = 0; k < 8; k++) { var p = Path(ring[k], ring[k + 1], e => e.cls == RoadClass.Boulevard); for (int i = (k == 0 ? 0 : 1); i < p.Count; i++) ringPath.Add(p[i]); }
            if (ringPath.Count > 1 && ringPath[ringPath.Count - 1] == ringPath[0]) ringPath.RemoveAt(ringPath.Count - 1);
            races.Add(new RaceSpec { name = "İç Çevre Turu", type = 1, laps = 2, prize = 3700, nodes = ringPath });
            // otoyol turu
            var hwP = new List<int>();
            V2[] hwW = { new V2(-1500, -400), new V2(-1350, 1500), new V2(600, 1720), new V2(SeaX(0) - 30, 0), new V2(200, -1900), new V2(-1480, -1500), new V2(-1500, -400) };
            for (int k = 0; k < hwW.Length - 1; k++) { var p = Path(Nearest(hwW[k], isHw), Nearest(hwW[k + 1], isHw), e => e.cls == RoadClass.Highway); for (int i = (k == 0 ? 0 : 1); i < p.Count; i++) hwP.Add(p[i]); }
            if (hwP.Count > 1 && hwP[hwP.Count - 1] == hwP[0]) hwP.RemoveAt(hwP.Count - 1);
            races.Add(new RaceSpec { name = "Otoyol Turu", type = 1, laps = 1, prize = 7500, nodes = hwP });
            // sprintler
            races.Add(new RaceSpec { name = "Şehir Boyu Sprint", type = 0, prize = 3000, nodes = Chain(new V2(-1050, -500), new V2(0, 0), new V2(1100, 600)) });
            races.Add(new RaceSpec { name = "Sahil Yolu Sprinti", type = 0, prize = 4500, nodes = Chain(new V2(SeaX(-1600) - 150, -1600), new V2(SeaX(1500) - 150, 1500)) });
            races.Add(new RaceSpec { name = "Banliyö Kaçışı", type = 0, prize = 2500, nodes = Chain(new V2(-1200, -1300), new V2(200, -1600), new V2(800, -900)) });
            var trap = new RaceSpec { name = "Radar Avı", type = 2, prize = 4000, nodes = Chain(new V2(-1000, 0), new V2(0, -1050), new V2(1000, 0)) };
            trap.special.Add(trap.nodes.Count / 4); trap.special.Add(trap.nodes.Count / 2); trap.special.Add(3 * trap.nodes.Count / 4);
            races.Add(trap);
            var toll = new RaceSpec { name = "Gişe Koşusu", type = 3, prize = 3500, nodes = Chain(new V2(-600, 1000), new V2(0, 0), new V2(600, -1000)) };
            for (int i = 6; i < toll.nodes.Count - 1; i += 7) toll.special.Add(i);
            races.Add(toll);
            // drag: otoyol batı düzlüğü
            int d0 = Nearest(new V2(-1480, -1400), isHw), d1 = Nearest(new V2(-1515, -500), isHw);
            races.Add(new RaceSpec { name = "Otoyol Dragı", type = 4, prize = 2200, nodes = new List<int> { d0, d1 } });
            // garaj: merkez kenarında, bir caddede
            garageNode = Nearest(new V2(-165f, -330f), notHw);
            var gn = nodes[garageNode];
            if (gn.edges.Count > 0)
            {
                var e = edges[gn.edges[0]];
                V2 other = e.a == garageNode ? e.pts[Math.Min(1, e.pts.Count - 1)] : e.pts[Math.Max(0, e.pts.Count - 2)];
                garageDir = (other - gn.p).Norm;
            }
        }

        // ------------------------------------------------------------------ JSON çıktı (önizleme/test)
        public string ToJson()
        {
            var sb = new System.Text.StringBuilder();
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            sb.Append("{\"nodes\":[");
            for (int i = 0; i < nodes.Count; i++) { if (i > 0) sb.Append(','); sb.AppendFormat(inv, "[{0:0.0},{1:0.0},{2:0.0}]", nodes[i].p.x, nodes[i].y, nodes[i].p.z); }
            sb.Append("],\"edges\":[");
            for (int i = 0; i < edges.Count; i++)
            {
                var e = edges[i]; if (i > 0) sb.Append(',');
                sb.AppendFormat(inv, "{{\"a\":{0},\"b\":{1},\"c\":{2},\"p\":[", e.a, e.b, (int)e.cls);
                for (int k = 0; k < e.pts.Count; k++) { if (k > 0) sb.Append(','); sb.AppendFormat(inv, "[{0:0.0},{1:0.0},{2:0.0},{3}]", e.pts[k].x, e.ys[k], e.pts[k].z, e.bridge[k] ? 1 : e.tunnel[k] ? 2 : 0); }
                sb.Append("]}");
            }
            sb.Append("],\"buildings\":[");
            for (int i = 0; i < buildings.Count; i++)
            {
                var b = buildings[i]; if (i > 0) sb.Append(',');
                sb.AppendFormat(inv, "{{\"z\":{0},\"s\":{1},\"h\":{2:0.0},\"p\":[", (int)b.zone, b.style, b.height);
                for (int k = 0; k < b.parts.Count; k++) { var p = b.parts[k]; if (k > 0) sb.Append(','); sb.AppendFormat(inv, "[{0:0.0},{1:0.0},{2:0.0},{3:0.0},{4:0.000}]", p.c.x, p.c.z, p.w, p.d, p.rot); }
                sb.Append("]}");
            }
            sb.Append("],\"river\":[");
            for (int i = 0; i < river.Count; i++) { if (i > 0) sb.Append(','); sb.AppendFormat(inv, "[{0:0},{1:0}]", river[i].x, river[i].z); }
            sb.AppendFormat(inv, "],\"lake\":[{0},{1},{2}],\"sea\":[", lakeC.x, lakeC.z, lakeR);
            for (float z = -2400f; z <= 2400f; z += 100f) { if (z > -2400f) sb.Append(','); sb.AppendFormat(inv, "[{0:0},{1:0}]", SeaX(z), z); }
            sb.Append("],\"races\":[");
            for (int i = 0; i < races.Count; i++) { if (i > 0) sb.Append(','); sb.Append("{\"name\":\"" + races[i].name + "\",\"type\":" + races[i].type + ",\"nodes\":[" + string.Join(",", races[i].nodes) + "]}"); }
            sb.AppendFormat(inv, "],\"garage\":{0},\"districts\":[", garageNode);
            for (int i = 0; i < districts.Count; i++) { if (i > 0) sb.Append(','); sb.AppendFormat(inv, "[\"{0}\",{1:0},{2:0}]", districts[i].Key, districts[i].Value.x, districts[i].Value.z); }
            sb.Append("]}");
            return sb.ToString();
        }
    }
}
