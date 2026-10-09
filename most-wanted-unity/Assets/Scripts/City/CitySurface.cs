using System.Collections.Generic;
using UnityEngine;
using MostWanted.Gen;

namespace MostWanted
{
    /// <summary>
    /// Kendi Şehrimiz sürüş yüzeyi: yol örnekleri, kavşak çokgenleri, arazi yüksekliği ve TEK parça çarpışma ağı.
    /// Yalnızca Vector3/Mathf kullanır (Unity dışı başsız denetleyici de aynı kodu derler → görsel/çarpışma/denetim tek kaynak).
    /// Çarpışma kuralları: bordürler 45°'den yatık rampa (dikey yüz yok), kaldırım dış kenarı araziye rampa,
    /// arazi yol yüzeyinin ≥ 35 cm altında, bina kutuları yol + kaldırım koridoruna girmez.
    /// </summary>
    public class CitySurface
    {
        public struct Sample { public Vector3 p; public Vector3 r; public bool bridge, tunnel; public float s; }

        /// <summary>Kavşak ucu: yol ucunun örneği (r: dışa bakan yönün sağı), genişlik, kaldırım.</summary>
        public struct JEnd { public Sample s; public float hw, sw, trim; public int edge; public Vector3 outDir; }

        public class Junction
        {
            public int node;
            public Vector3 center;
            public List<JEnd> ends = new List<JEnd>();           // açıya göre sıralı
            public List<Vector3> poly = new List<Vector3>();     // kaplama çokgeni (fan merkezi center)
            public List<Corner> corners = new List<Corner>();
            public List<int> tris;                               // kaplama üçgenleri (poly indisleri); null → merkezden fan    // k: uç k sağ köşe → uç k+1 sol köşe
        }

        public const float CurbH = 0.15f;
        public const float CurbRun = 0.22f;       // çarpışmada bordür rampası (yatay) → ~34°
        public const float TerrainDrop = 0.35f;   // arazi yol yüzeyinin altında
        public const float TerrainX0 = -3200f, TerrainZ0 = -3400f, TerrainSize = 6800f;
        public const int TerrainChunks = 12;

        /// <summary>Yalnızca başsız denetleyici: eski (düzeltme öncesi) geometriyi üretir.</summary>
        public static bool LegacyForCheck;
        public readonly CityGen gen;
        public readonly float[] junctionR;
        readonly Dictionary<REdge, List<Sample>> trimCache = new Dictionary<REdge, List<Sample>>();
        public readonly List<Junction> junctions = new List<Junction>();

        /// <summary>Yol ucu kırpma mesafesi: [kenar*2 + 0] a ucu, [kenar*2 + 1] b ucu.</summary>
        public readonly float[] endTrim;
        readonly Dictionary<REdge, int> edgeIndex = new Dictionary<REdge, int>();

        public CitySurface(CityGen g)
        {
            gen = g;
            junctionR = new float[g.nodes.Count];
            endTrim = new float[g.edges.Count * 2];
            for (int ei = 0; ei < g.edges.Count; ei++) edgeIndex[g.edges[ei]] = ei;
            for (int i = 0; i < g.nodes.Count; i++)
            {
                var n = g.nodes[i];
                if (n.edges.Count < 2) continue;
                if (n.edges.Count == 2)
                {
                    // kavşaksız bağlantı: yalnızca keskin dönüşte (> ~25°) kavşak kaplaması kullan
                    var e0 = g.edges[n.edges[0]]; var e1 = g.edges[n.edges[1]];
                    if (LegacyForCheck || e0 == e1 || Vector3.Dot(OutDir2(e0, i), OutDir2(e1, i)) < -0.9f) continue;
                }
                float r = 0f;
                foreach (int ei in n.edges) r = Mathf.Max(r, CityGen.Width(g.edges[ei].cls) * 0.5f + 3f);
                junctionR[i] = r;
                // açıya göre sıralı uçlar; dar açılı komşu çiftlerinde ikisini de, koridorlar ayrılana kadar kırp
                var ends = new List<KeyValuePair<float, int>>();
                foreach (int ei in n.edges) { var d = OutDir2(g.edges[ei], i); ends.Add(new KeyValuePair<float, int>(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg, ei)); }
                ends.Sort((x, y) => x.Key.CompareTo(y.Key));
                var trim = new float[ends.Count];
                for (int k = 0; k < ends.Count; k++) trim[k] = r;
                for (int k = 0; k < ends.Count && !LegacyForCheck; k++)
                {
                    int k1 = (k + 1) % ends.Count;
                    float da = (k1 == 0 ? ends[0].Key + 360f : ends[k1].Key) - ends[k].Key;
                    if (da >= 80f) continue;
                    var e0 = g.edges[ends[k].Value]; var e1 = g.edges[ends[k1].Value];
                    float w = Mathf.Max(e0.Width * 0.5f + CityGen.Sidewalk(e0.cls), e1.Width * 0.5f + CityGen.Sidewalk(e1.cls));
                    float need = Mathf.Min(60f, w / Mathf.Max(0.2f, Mathf.Tan(da * 0.5f * Mathf.Deg2Rad)) + 1f);
                    trim[k] = Mathf.Max(trim[k], need); trim[k1] = Mathf.Max(trim[k1], need);
                }
                for (int k = 0; k < ends.Count; k++)
                {
                    int ei = ends[k].Value; var e = g.edges[ei];
                    if (e.a == i) endTrim[ei * 2] = trim[k];
                    if (e.b == i) endTrim[ei * 2 + 1] = trim[k];
                }
            }
            IndexRoads();
            BuildJunctionGeo();
        }

        public float NodeTrim(int ni)
        {
            float t = junctionR[ni];
            foreach (int ei in gen.nodes[ni].edges) { var e = gen.edges[ei]; if (e.a == ni) t = Mathf.Max(t, endTrim[ei * 2]); if (e.b == ni) t = Mathf.Max(t, endTrim[ei * 2 + 1]); }
            return t;
        }

        /// <summary>Kenar üzerinde, düğümden itibaren yay uzunluğu dist'teki nokta; off: dışa bakan yönün sağına ofset.</summary>
        public Vector3 EdgePoint(int ei, int node, float dist, float off)
        {
            var e = gen.edges[ei];
            int m = e.pts.Count;
            bool fromA = e.a == node;
            float acc = 0f;
            for (int k = 1; k < m; k++)
            {
                int i0 = fromA ? k - 1 : m - k, i1 = fromA ? k : m - k - 1;
                float l = V2.Dist(e.pts[i0], e.pts[i1]);
                if (acc + l >= dist || k == m - 1)
                {
                    float t = l > 1e-4f ? Mathf.Clamp01((dist - acc) / l) : 0f;
                    V2 p = V2.Lerp(e.pts[i0], e.pts[i1], t);
                    V2 d = (e.pts[i1] - e.pts[i0]).Norm;
                    float y = Mathf.Lerp(e.ys[i0], e.ys[i1], t);
                    return new Vector3(p.x + d.z * off, y, p.z - d.x * off);
                }
                acc += l;
            }
            return P3(e.pts[fromA ? 0 : m - 1], fromA ? e.ys[0] : e.ys[m - 1]);
        }

        static Vector3 OutDir2(REdge e, int node)
        {
            V2 a, b;
            if (e.a == node) { a = e.pts[0]; b = e.pts[Mathf.Min(2, e.pts.Count - 1)]; }
            else { a = e.pts[e.pts.Count - 1]; b = e.pts[Mathf.Max(0, e.pts.Count - 3)]; }
            return new Vector3(b.x - a.x, 0, b.z - a.z).normalized;
        }

        public static Vector3 P3(V2 p, float y) { return new Vector3(p.x, y, p.z); }

        // ------------------------------------------------------------------ yol örnekleri
        public List<Sample> Trimmed(REdge e)
        {
            List<Sample> o;
            if (trimCache.TryGetValue(e, out o)) return o;
            int m = e.pts.Count;
            var s = new float[m];
            for (int i = 1; i < m; i++) s[i] = s[i - 1] + V2.Dist(e.pts[i - 1], e.pts[i]);
            float L = s[m - 1];
            int eidx = edgeIndex[e];
            float ta = endTrim[eidx * 2], tb = endTrim[eidx * 2 + 1];
            o = new List<Sample>();
            trimCache[e] = o;
            if (L - ta - tb < 1f) return o;
            System.Func<float, Sample> At = (d) =>
            {
                int i = 1; while (i < m - 1 && s[i] < d) i++;
                float t = Mathf.InverseLerp(s[i - 1], s[i], d);
                V2 p = V2.Lerp(e.pts[i - 1], e.pts[i], t);
                V2 dir = (e.pts[i] - e.pts[i - 1]).Norm;
                float y = Mathf.Lerp(e.ys[i - 1], e.ys[i], t);
                return new Sample { p = P3(p, y), r = new Vector3(dir.z, 0, -dir.x), bridge = e.bridge[i] || e.bridge[i - 1], tunnel = e.tunnel[i] && e.tunnel[i - 1], s = d };
            };
            o.Add(At(ta));
            for (int i = 1; i < m - 1; i++) if (s[i] > ta + 0.5f && s[i] < L - tb - 0.5f) o.Add(At(s[i]));
            o.Add(At(L - tb));
            for (int i = 1; i < o.Count - 1; i++)
            {
                var a = o[i - 1].p; var b = o[i + 1].p;
                Vector3 d = new Vector3(b.x - a.x, 0, b.z - a.z).normalized;
                var x = o[i]; x.r = new Vector3(d.z, 0, -d.x); o[i] = x;
            }
            // 2'li (kavşaksız) düğümlerde iki yolun ucu aynı kesiti paylaşsın: dikişsiz devam
            for (int end = 0; end < 2 && !LegacyForCheck; end++)
            {
                int node = end == 0 ? e.a : e.b;
                if (gen.nodes[node].edges.Count != 2 || o.Count < 2 || endTrim[eidx * 2 + end] > 0f) continue;
                int other = gen.nodes[node].edges[0] == eidx ? gen.nodes[node].edges[1] : gen.nodes[node].edges[0];
                var oe = gen.edges[other];
                V2 np = gen.nodes[node].p;
                V2 q = oe.a == node ? oe.pts[1] : oe.pts[oe.pts.Count - 2];          // diğer yolun düğüme komşu noktası
                V2 mine = end == 0 ? e.pts[1] : e.pts[m - 2];
                // ortak teğet: komşu iki nokta arası (benim yönümde, a→b)
                V2 dir = end == 0 ? (mine - q).Norm : (q - mine).Norm;
                var x = end == 0 ? o[0] : o[o.Count - 1];
                x.r = new Vector3(dir.z, 0, -dir.x);
                x.p = P3(np, x.p.y);
                if (end == 0) o[0] = x; else o[o.Count - 1] = x;
            }
            return o;
        }

        // ------------------------------------------------------------------ kavşaklar
        void BuildJunctionGeo()
        {
            for (int ni = 0; ni < gen.nodes.Count; ni++)
            {
                if (junctionR[ni] <= 0f) continue;
                var n = gen.nodes[ni];
                var J = new Junction { node = ni };
                Vector3 c = P3(n.p, n.y);
                var list = new List<KeyValuePair<float, JEnd>>();
                foreach (int ei in n.edges)
                {
                    var e = gen.edges[ei];
                    var smp = Trimmed(e);
                    if (smp.Count < 2) continue;
                    Sample end = e.a == ni ? smp[0] : smp[smp.Count - 1];
                    Vector3 d = new Vector3(end.p.x - c.x, 0, end.p.z - c.z).normalized;
                    if (e.a != ni) end.r = -end.r;
                    list.Add(new KeyValuePair<float, JEnd>(Mathf.Atan2(d.x, d.z), new JEnd { s = end, hw = e.Width * 0.5f, sw = CityGen.Sidewalk(e.cls), trim = endTrim[ei * 2 + (e.a == ni ? 0 : 1)], edge = ei, outDir = d }));
                }
                if (list.Count < 2) continue;
                list.Sort((x, y) => x.Key.CompareTo(y.Key));
                float ySum = 0f;
                foreach (var kv in list) { J.ends.Add(kv.Value); ySum += kv.Value.s.p.y; }
                // merkez yüksekliği uçların ortalaması: fan üçgenlerinde kırık (sırt) olmasın
                Vector3 cen = Vector3.zero;
                foreach (var je in J.ends) cen += je.s.p;
                cen /= J.ends.Count;
                J.center = LegacyForCheck ? c : new Vector3(cen.x, ySum / list.Count, cen.z);   // fan merkezi çokgenin içinde
                for (int k = 0; k < J.ends.Count; k++) J.corners.Add(MakeCorner(J, k));
                for (int k = 0; k < J.ends.Count; k++)
                {
                    var je = J.ends[k];
                    J.poly.Add(je.s.p - je.s.r * je.hw);
                    var arc = J.corners[k].inArc;          // r0 ... l1 (sağ köşe dahil, sol köşe hariç)
                    for (int i = 0; i < arc.Count - 1; i++) J.poly.Add(arc[i]);
                }
                if (!LegacyForCheck && !StarShaped(J.poly, J.center)) J.tris = EarClip(J.poly);
                junctions.Add(J);
            }
        }

        /// <summary>XZ düzleminde kulak kırpma üçgenleme (basit çokgen). Başarısızsa null. Üçgenler yukarı bakar (Unity saat yönü).</summary>
        public static List<int> EarClip(List<Vector3> poly)
        {
            int n = poly.Count;
            if (n < 3) return null;
            var idx = new List<int>();
            // tekrarlayan ardışık noktaları at
            for (int i = 0; i < n; i++)
            {
                var a = poly[i]; var b = poly[(i + 1) % n];
                if ((a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z) > 1e-4f) idx.Add(i);
            }
            float area = 0f;
            for (int i = 0; i < idx.Count; i++) { var a = poly[idx[i]]; var b = poly[idx[(i + 1) % idx.Count]]; area += a.x * b.z - b.x * a.z; }
            if (area < 0f) idx.Reverse();      // saat yönü tersi (x→z) yap
            var o = new List<int>();
            int guard = 0;
            while (idx.Count > 3 && guard++ < 5000)
            {
                bool cut = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int ia = idx[(i + idx.Count - 1) % idx.Count], ib = idx[i], ic = idx[(i + 1) % idx.Count];
                    Vector3 a = poly[ia], b = poly[ib], c = poly[ic];
                    float cr = (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);
                    if (cr <= 1e-5f) continue;   // içbükey
                    bool inside = false;
                    for (int j = 0; j < idx.Count && !inside; j++)
                    {
                        int ip = idx[j]; if (ip == ia || ip == ib || ip == ic) continue;
                        if (InTri(poly[ip], a, b, c)) inside = true;
                    }
                    if (inside) continue;
                    o.Add(ia); o.Add(ic); o.Add(ib);   // XZ'de CCW → Unity'de yukarı bakan sarım için ters
                    idx.RemoveAt(i); cut = true; break;
                }
                if (!cut) return null;
            }
            if (idx.Count == 3) { o.Add(idx[0]); o.Add(idx[2]); o.Add(idx[1]); }
            return o;
        }

        /// <summary>Çokgenin tüm kenarları merkezden aynı yönde görünüyor mu (fan üçgenlemesi geçerli)?</summary>
        static bool StarShaped(List<Vector3> poly, Vector3 c)
        {
            int pos = 0, neg = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                Vector3 a = poly[i], b = poly[(i + 1) % poly.Count];
                float cr = (a.x - c.x) * (b.z - c.z) - (a.z - c.z) * (b.x - c.x);
                if (cr > 1e-3f) pos++; else if (cr < -1e-3f) neg++;
            }
            return pos == 0 || neg == 0;
        }

        static bool InTri(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            float d1 = (b.x - a.x) * (p.z - a.z) - (b.z - a.z) * (p.x - a.x);
            float d2 = (c.x - b.x) * (p.z - b.z) - (c.z - b.z) * (p.x - b.x);
            float d3 = (a.x - c.x) * (p.z - c.z) - (a.z - c.z) * (p.x - c.x);
            return d1 >= -1e-6f && d2 >= -1e-6f && d3 >= -1e-6f;
        }

        // ------------------------------------------------------------------ arazi
        struct GSeg { public float ax, az, ay, bx, bz, by, half, core; public bool tunnel; }
        readonly Dictionary<long, List<GSeg>> gHash = new Dictionary<long, List<GSeg>>();
        const float GCell = 40f;
        public const float ClampBand = 22f, BlendBand = 30f;

        static long GKey(int x, int z) { return ((long)x << 32) ^ (uint)z; }

        void IndexRoads()
        {
            foreach (var e in gen.edges)
            {
                float sw = CityGen.Sidewalk(e.cls);
                float half = e.Width * 0.5f + sw + (sw > 0f ? 1.4f : 0.5f);
                for (int i = 1; i < e.pts.Count; i++)
                {
                    bool tun = e.tunnel[i] && e.tunnel[i - 1];
                    var g = new GSeg { ax = e.pts[i - 1].x, az = e.pts[i - 1].z, ay = e.ys[i - 1], bx = e.pts[i].x, bz = e.pts[i].z, by = e.ys[i], half = half, core = tun ? e.Width * 0.5f + 2.5f : e.Width * 0.5f + sw + 0.3f, tunnel = tun };
                    AddSeg(g);
                }
            }
            // kavşak alanları (köşe kaldırımları dahil)
            for (int ni = 0; ni < gen.nodes.Count; ni++)
            {
                var n = gen.nodes[ni];
                if (NodeTrim(ni) <= 0f) continue;
                float sw = 0f; foreach (int ei in n.edges) sw = Mathf.Max(sw, CityGen.Sidewalk(gen.edges[ei].cls));
                AddSeg(new GSeg { ax = n.p.x, az = n.p.z, ay = n.y, bx = n.p.x + 0.01f, bz = n.p.z, by = n.y, half = NodeTrim(ni) + sw * 1.8f + 2f, core = 0f });
            }
        }

        void AddSeg(GSeg g)
        {
            float r = g.half + ClampBand + BlendBand;
            int x0 = Mathf.FloorToInt((Mathf.Min(g.ax, g.bx) - r) / GCell), x1 = Mathf.FloorToInt((Mathf.Max(g.ax, g.bx) + r) / GCell);
            int z0 = Mathf.FloorToInt((Mathf.Min(g.az, g.bz) - r) / GCell), z1 = Mathf.FloorToInt((Mathf.Max(g.az, g.bz) + r) / GCell);
            for (int x = x0; x <= x1; x++)
                for (int z = z0; z <= z1; z++)
                {
                    List<GSeg> l; long k = GKey(x, z);
                    if (!gHash.TryGetValue(k, out l)) gHash[k] = l = new List<GSeg>();
                    l.Add(g);
                }
        }

        /// <summary>Arazi yüksekliği: doğal H; yol koridoru + ClampBand içinde yolun TerrainDrop altında, sonra yumuşak geçiş.</summary>
        public float Ground(float x, float z)
        {
            float h = gen.H(x, z);
            List<GSeg> l;
            if (!gHash.TryGetValue(GKey(Mathf.FloorToInt(x / GCell), Mathf.FloorToInt(z / GCell)), out l)) return h;
            float cap = float.MaxValue;      // yol kaynaklı üst sınır
            float bestBlend = float.MaxValue, blendY = 0f;
            foreach (var g in l)
            {
                if (g.tunnel) continue;
                float dx = g.bx - g.ax, dz = g.bz - g.az;
                float L2 = dx * dx + dz * dz;
                float t = L2 > 1e-6f ? Mathf.Clamp01(((x - g.ax) * dx + (z - g.az) * dz) / L2) : 0f;
                float px = g.ax + dx * t - x, pz = g.az + dz * t - z;
                float d = Mathf.Sqrt(px * px + pz * pz) - g.half;
                float y = g.ay + (g.by - g.ay) * t - TerrainDrop;
                if (d < ClampBand) cap = Mathf.Min(cap, y);
                else if (d < ClampBand + BlendBand && d < bestBlend) { bestBlend = d; blendY = y; }
            }
            if (cap < float.MaxValue) return Mathf.Min(h, cap);
            if (bestBlend < float.MaxValue)
            {
                float t = Mathf.Clamp01((bestBlend - ClampBand) / BlendBand); t = t * t * (3f - 2f * t);
                return Mathf.Lerp(Mathf.Min(h, blendY), h, t);
            }
            return h;
        }

        /// <summary>Arazi köşesi bir yolun/tünelin sürüş hacmine giriyor mu? (o üçgen arazide çizilmez → delik, tünel ağzı)</summary>
        public bool TerrainConflict(float x, float y, float z) { bool near; return TerrainConflict(x, y, z, out near); }

        bool TerrainConflict(float x, float y, float z, out bool nearTunnel)
        {
            nearTunnel = false;
            List<GSeg> l;
            if (!gHash.TryGetValue(GKey(Mathf.FloorToInt(x / GCell), Mathf.FloorToInt(z / GCell)), out l)) return false;
            foreach (var g in l)
            {
                if (g.core <= 0f) continue;
                float dx = g.bx - g.ax, dz = g.bz - g.az;
                float L2 = dx * dx + dz * dz;
                float t = L2 > 1e-6f ? Mathf.Clamp01(((x - g.ax) * dx + (z - g.az) * dz) / L2) : 0f;
                float px = g.ax + dx * t - x, pz = g.az + dz * t - z;
                if (g.tunnel && px * px + pz * pz < (g.core + 16f) * (g.core + 16f)) nearTunnel = true;
                if (px * px + pz * pz > g.core * g.core) continue;
                float ry = g.ay + (g.by - g.ay) * t;
                if (y > ry - 0.05f && (!g.tunnel || y < ry + 9.6f)) return true;
            }
            return false;
        }

        /// <summary>Arazi üçgeni çizilsin mi?</summary>
        public bool TerrainTriOk(Vector3 a, Vector3 b, Vector3 c)
        {
            bool n0, n1, n2;
            if (TerrainConflict(a.x, a.y, a.z, out n0) | TerrainConflict(b.x, b.y, b.z, out n1) | TerrainConflict(c.x, c.y, c.z, out n2)) return false;
            if (!(n0 || n1 || n2)) return true;
            // tünel yakınında üçgen içini de örnekle
            bool dummy;
            for (int i = 0; i <= 4; i++)
                for (int j = 0; i + j <= 4; j++)
                {
                    float u = i / 4f, v = j / 4f;
                    Vector3 p = a + (b - a) * u + (c - a) * v;
                    if (TerrainConflict(p.x, p.y, p.z, out dummy)) return false;
                }
            return true;
        }

        /// <summary>Arazi parçası çözünürlüğü (şehir içinde ince).</summary>
        public static int TerrainRes(int cx, int cz)
        {
            float cs = TerrainSize / TerrainChunks;
            float wx = TerrainX0 + cx * cs, wz = TerrainZ0 + cz * cs;
            bool inner = wx > -2000f && wx + cs < 2100f && wz > -2200f && wz + cs < 2000f;
            return inner ? 40 : 14;
        }

        // ------------------------------------------------------------------ çarpışma ağı
        /// <summary>Etiketli üçgen çorbası. tag 0 = sürüş yüzeyi, 1 = bordür/kaldırım/bariyer/duvar.</summary>
        public class Soup
        {
            public readonly List<Vector3> v = new List<Vector3>();
            public readonly List<int> t = new List<int>();
            public readonly List<byte> tag = new List<byte>();
            public void Tri(Vector3 a, Vector3 b, Vector3 c, byte g) { int i = v.Count; v.Add(a); v.Add(b); v.Add(c); t.Add(i); t.Add(i + 1); t.Add(i + 2); tag.Add(g); }
            /// <summary>Dörtgen; yatay-ise yukarı bakacak şekilde sarılır.</summary>
            public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, byte g)
            {
                Vector3 n = Vector3.Cross(b - a, d - a);
                if (n.y < 0f) { var tmp = b; b = d; d = tmp; }
                Tri(a, b, c, g); Tri(a, c, d, g);
            }
            public void Box(Vector3 c, Vector3 fwd, Vector3 size, byte g)
            {
                fwd = fwd.normalized;
                Vector3 rgt = Vector3.Cross(Vector3.up, fwd).normalized;
                Vector3 up = Vector3.Cross(fwd, rgt);
                Vector3 hx = rgt * (size.x * 0.5f), hy = up * (size.y * 0.5f), hz = fwd * (size.z * 0.5f);
                Vector3[] p =
                {
                    c - hx - hy - hz, c - hx + hy - hz, c + hx + hy - hz, c + hx - hy - hz,
                    c - hx - hy + hz, c - hx + hy + hz, c + hx + hy + hz, c + hx - hy + hz
                };
                int[][] f = { new[] { 0, 1, 2, 3 }, new[] { 7, 6, 5, 4 }, new[] { 4, 5, 1, 0 }, new[] { 3, 2, 6, 7 }, new[] { 1, 5, 6, 2 }, new[] { 4, 0, 3, 7 } };
                foreach (var q in f) { Tri(p[q[0]], p[q[1]], p[q[2]], g); Tri(p[q[0]], p[q[2]], p[q[3]], g); }
            }

            /// <summary>Aynı konumdaki köşeleri birleştir (indeksler yeniden eşlenir).</summary>
            public void Weld(out List<Vector3> verts, out List<int> tris)
            {
                var map = new Dictionary<long, int>();
                verts = new List<Vector3>();
                tris = new List<int>(t.Count);
                var remap = new int[v.Count];
                for (int i = 0; i < v.Count; i++)
                {
                    var p = v[i];
                    long key = ((long)Mathf.RoundToInt(p.x * 100f) * 73856093L) ^ ((long)Mathf.RoundToInt(p.y * 100f) * 19349663L) ^ ((long)Mathf.RoundToInt(p.z * 100f) * 83492791L);
                    int id;
                    if (!map.TryGetValue(key, out id)) { id = verts.Count; verts.Add(p); map[key] = id; }
                    remap[i] = id;
                }
                for (int i = 0; i < t.Count; i += 3)
                {
                    int a = remap[t[i]], b = remap[t[i + 1]], c = remap[t[i + 2]];
                    if (a == b || b == c || a == c) continue;   // dejenere
                    tris.Add(a); tris.Add(b); tris.Add(c);
                }
            }
        }

        /// <summary>Tüm yol ağının çarpışma geometrisi (yüzey + rampalı bordür + bariyer + tünel).</summary>
        public Soup BuildRoadCollider()
        {
            var S = new Soup();
            Vector3 up = Vector3.up * CurbH;
            foreach (var e in gen.edges)
            {
                var smp = Trimmed(e);
                if (smp.Count < 2) continue;
                float W = e.Width, hw = W * 0.5f, sw = CityGen.Sidewalk(e.cls);
                bool highway = e.cls == RoadClass.Highway;
                for (int i = 1; i < smp.Count; i++)
                {
                    var a = smp[i - 1]; var b = smp[i];
                    bool deck = a.bridge || b.bridge;
                    S.Quad(a.p - a.r * hw, b.p - b.r * hw, b.p + b.r * hw, a.p + a.r * hw, 0);
                    if (sw > 0f && !a.tunnel)
                    {
                        for (int side = -1; side <= 1; side += 2)
                        {
                            Vector3 ai = a.p + a.r * (side * hw), bi = b.p + b.r * (side * hw);
                            Vector3 ak = a.p + a.r * (side * (hw + CurbRun)) + up, bk = b.p + b.r * (side * (hw + CurbRun)) + up;
                            Vector3 ao = a.p + a.r * (side * (hw + sw)) + up, bo = b.p + b.r * (side * (hw + sw)) + up;
                            S.Quad(ai, bi, bk, ak, 1);              // bordür rampası
                            S.Quad(ak, bk, bo, ao, 1);              // kaldırım
                            if (deck)
                            {
                                Vector3 dn = Vector3.down * 1.4f;
                                S.Quad(ao, bo, bo + dn, ao + dn, 1);
                            }
                            else
                            {
                                // dış kenar: araziye inen rampa (çimden geri çıkarken duvar yok)
                                Vector3 ar = a.p + a.r * (side * (hw + sw + 1.3f)) + Vector3.down * (TerrainDrop + 0.45f);
                                Vector3 br = b.p + b.r * (side * (hw + sw + 1.3f)) + Vector3.down * (TerrainDrop + 0.45f);
                                S.Quad(ao, bo, br, ar, 1);
                            }
                        }
                    }
                    else
                    {
                        Vector3 down = Vector3.down * (deck ? 1.6f : 0.8f);
                        S.Quad(a.p + a.r * hw, b.p + b.r * hw, b.p + b.r * hw + down, a.p + a.r * hw + down, 1);
                        S.Quad(a.p - a.r * hw + down, b.p - b.r * hw + down, b.p - b.r * hw, a.p - a.r * hw, 1);
                    }
                    if (highway)
                    {
                        Vector3 dir = b.p - a.p;
                        float len = dir.magnitude + 0.05f;
                        Vector3 mid = (a.p + b.p) * 0.5f, rr = (a.r + b.r).normalized;
                        S.Box(mid + Vector3.up * 0.45f, dir, new Vector3(0.6f, 0.9f, len), 1);
                        // korkuluk: çarpışmada yere kadar (altından kayıp düşmesin)
                        S.Box(mid + rr * (hw - 0.3f) + Vector3.up * 0.5f, dir, new Vector3(0.14f, 1.0f, len), 1);
                        S.Box(mid - rr * (hw - 0.3f) + Vector3.up * 0.5f, dir, new Vector3(0.14f, 1.0f, len), 1);
                        if (a.tunnel)
                        {
                            S.Box(mid + rr * (hw + 0.6f) + Vector3.up * 4f, dir, new Vector3(1.2f, 8f, len), 1);
                            S.Box(mid - rr * (hw + 0.6f) + Vector3.up * 4f, dir, new Vector3(1.2f, 8f, len), 1);
                            S.Box(mid + Vector3.up * 8.5f, dir, new Vector3(W + 3.6f, 1f, len), 1);
                        }
                    }
                    if (deck && sw > 0f)
                    {
                        for (int side = -1; side <= 1; side += 2)
                        {
                            Vector3 ao = a.p + a.r * (side * (hw + sw)) + up, bo = b.p + b.r * (side * (hw + sw)) + up;
                            Vector3 d = bo - ao; if (d.sqrMagnitude < 0.01f) continue;
                            S.Box((ao + bo) * 0.5f + Vector3.up * 0.5f, d, new Vector3(0.3f, 1f, d.magnitude + 0.05f), 1);
                        }
                    }
                }
            }
            foreach (var J in junctions) JunctionCollider(S, J);
            return S;
        }

        /// <summary>Kavşak köşesi: iç (bordür) ve dış (kaldırım arkası) yay noktaları; görsel ve çarpışma aynı noktaları kullanır.</summary>
        public class Corner
        {
            public Vector3 r0, l1, r0o, l1o, cornerOut;
            public List<Vector3> inArc = new List<Vector3>(), outArc = new List<Vector3>();   // r0→l1, r0o→l1o (uçlar dahil)
            public bool sidewalk;
        }
        public const int ArcSegs = 6;

        /// <summary>Kenar çizgileri (p0, -d0) ve (p1, -d1) kesişimine kontrol noktalı ikinci derece eğri; olmuyorsa düz.</summary>
        static List<Vector3> Fillet(Vector3 p0, Vector3 d0, Vector3 p1, Vector3 d1, float maxT, ref int mode)
        {
            var o = new List<Vector3>();
            Vector3 a = -d0, b = -d1;   // kavşağa doğru
            float den = a.x * b.z - a.z * b.x;
            Vector3 c = (p0 + p1) * 0.5f; bool ok = false;
            if (Mathf.Abs(den) > 0.08f)
            {
                float t = ((p1.x - p0.x) * b.z - (p1.z - p0.z) * b.x) / den;
                float u = ((p1.x - p0.x) * a.z - (p1.z - p0.z) * a.x) / den;
                if (mode != 0 && t > 0.2f && u > 0.2f && t < maxT && u < maxT) { c = p0 + a * t; ok = true; }
            }
            mode = ok ? 1 : 0;
            c.y = (p0.y + p1.y) * 0.5f;
            for (int i = 0; i <= ArcSegs; i++)
            {
                float t = i / (float)ArcSegs;
                o.Add(ok ? p0 * ((1 - t) * (1 - t)) + c * (2 * t * (1 - t)) + p1 * (t * t) : Vector3.Lerp(p0, p1, t));
            }
            return o;
        }

        Corner MakeCorner(Junction J, int k)
        {
            var c = new Corner();
            var e0 = J.ends[k]; var e1 = J.ends[(k + 1) % J.ends.Count];
            c.r0 = e0.s.p + e0.s.r * e0.hw; c.l1 = e1.s.p - e1.s.r * e1.hw;
            float maxT = Mathf.Max(e0.trim, e1.trim) * 1.6f + 4f;
            if (LegacyForCheck) { c.inArc.Add(c.r0); c.inArc.Add(c.l1); }
            int mode = -1;
            if (!LegacyForCheck) c.inArc = Fillet(c.r0, e0.outDir, c.l1, e1.outDir, maxT, ref mode);
            c.sidewalk = e0.sw > 0f && e1.sw > 0f;
            bool straight = !LegacyForCheck && mode == 0 && Vector3.Dot(e0.outDir, e1.outDir) < -0.75f;
            if (straight)
            {
                // düz geçiş (kavisli yol olabilir): kiriş yerine yolun gerçek kenarını izle
                c.inArc = StraightCorner(J, e0, e1, 0f);
                if (c.sidewalk) c.outArc = StraightCorner(J, e0, e1, 1f);
            }
            c.r0o = e0.s.p + e0.s.r * (e0.hw + e0.sw); c.l1o = e1.s.p - e1.s.r * (e1.hw + e1.sw);
            Vector3 mid = (c.r0o + c.l1o) * 0.5f;
            c.cornerOut = mid + new Vector3(mid.x - J.center.x, 0, mid.z - J.center.z).normalized * (Mathf.Max(e0.sw, e1.sw) * 0.6f);
            c.cornerOut.y = (c.r0o.y + c.l1o.y) * 0.5f;
            if (c.sidewalk)
            {
                if (LegacyForCheck) { c.outArc.Add(c.r0o); c.outArc.Add(c.cornerOut); c.outArc.Add(c.l1o); }
                else if (!straight)
                {
                    // iç yay eğriyse dış da eğri (aynı geometri, dışa kaymış); düzse düz
                    c.outArc = Fillet(c.r0o, e0.outDir, c.l1o, e1.outDir, maxT + 12f, ref mode);
                }
            }
            return c;
        }

        List<Vector3> StraightCorner(Junction J, JEnd e0, JEnd e1, float swMul)
        {
            var o = new List<Vector3>();
            int n0 = Mathf.Max(1, Mathf.CeilToInt(e0.trim / 4f)), n1 = Mathf.Max(1, Mathf.CeilToInt(e1.trim / 4f));
            float off0 = e0.hw + e0.sw * swMul, off1 = e1.hw + e1.sw * swMul;
            for (int i = 0; i <= n0; i++) o.Add(i == 0 ? e0.s.p + e0.s.r * off0 : EdgePoint(e0.edge, J.node, e0.trim * (1f - i / (float)n0), off0));
            for (int i = 1; i <= n1; i++) o.Add(i == n1 ? e1.s.p - e1.s.r * off1 : EdgePoint(e1.edge, J.node, e1.trim * (i / (float)n1), -off1));
            return o;
        }

        public Corner CornerAt(Junction J, int k) { return J.corners[k]; }

        void JunctionCollider(Soup S, Junction J)
        {
            if (J.tris != null)
                for (int k = 0; k < J.tris.Count; k += 3) S.Tri(J.poly[J.tris[k]], J.poly[J.tris[k + 1]], J.poly[J.tris[k + 2]], 0);
            else
                for (int k = 0; k < J.poly.Count; k++) S.Tri(J.center, J.poly[k], J.poly[(k + 1) % J.poly.Count], 0);
            Vector3 up = Vector3.up * CurbH;
            Vector3 dn = Vector3.down * (TerrainDrop + 0.45f);
            foreach (var c in J.corners)
            {
                if (!c.sidewalk) continue;
                if (LegacyForCheck) continue;
                int n = c.inArc.Count;
                var ik = new Vector3[n]; var od = new Vector3[n]; var oo = new Vector3[n];
                for (int i = 0; i < n; i++)
                {
                    Vector3 d = c.outArc[i] - c.inArc[i]; d.y = 0f; d = d.normalized;
                    ik[i] = c.inArc[i] + d * CurbRun + up;
                    oo[i] = c.outArc[i] + up;
                    od[i] = c.outArc[i] + d * 1.3f + dn;
                }
                for (int i = 1; i < n; i++)
                {
                    S.Quad(c.inArc[i - 1], c.inArc[i], ik[i], ik[i - 1], 1);   // bordür rampası
                    S.Quad(ik[i - 1], ik[i], oo[i], oo[i - 1], 1);              // kaldırım
                    S.Quad(oo[i - 1], oo[i], od[i], od[i - 1], 1);              // dış rampa
                }
            }
        }

        // ------------------------------------------------------------------ sürüş yüzeyi sorgusu (süs yerleşimi)
        List<Vector3> dv; List<int> dt; Dictionary<long, List<int>> dHash;

        /// <summary>(x,z) çevresinde (yarıçap) sürülebilir yüzey (yol/kavşak kaplaması) var mı?</summary>
        public bool Drivable(float x, float z, float radius)
        {
            if (dHash == null)
            {
                var S = BuildRoadCollider();
                dv = S.v; dt = new List<int>(); dHash = new Dictionary<long, List<int>>();
                for (int i = 0; i < S.tag.Count; i++)
                {
                    if (S.tag[i] != 0) continue;
                    int id = dt.Count / 3; dt.Add(S.t[3 * i]); dt.Add(S.t[3 * i + 1]); dt.Add(S.t[3 * i + 2]);
                    Vector3 a = dv[S.t[3 * i]], b = dv[S.t[3 * i + 1]], c = dv[S.t[3 * i + 2]];
                    int x0 = Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x)) / 4f), x1 = Mathf.FloorToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x)) / 4f);
                    int z0 = Mathf.FloorToInt(Mathf.Min(a.z, Mathf.Min(b.z, c.z)) / 4f), z1 = Mathf.FloorToInt(Mathf.Max(a.z, Mathf.Max(b.z, c.z)) / 4f);
                    for (int xx = x0; xx <= x1; xx++) for (int zz = z0; zz <= z1; zz++)
                        {
                            List<int> l; long k = GKey(xx, zz);
                            if (!dHash.TryGetValue(k, out l)) dHash[k] = l = new List<int>();
                            l.Add(id);
                        }
                }
            }
            for (int s = 0; s < 9; s++)
            {
                float px = x, pz = z;
                if (s < 8) { float a = s * 0.785398f; px += Mathf.Cos(a) * radius; pz += Mathf.Sin(a) * radius; }
                List<int> l;
                if (!dHash.TryGetValue(GKey(Mathf.FloorToInt(px / 4f), Mathf.FloorToInt(pz / 4f)), out l)) continue;
                foreach (int id in l)
                {
                    Vector3 a = dv[dt[3 * id]], b = dv[dt[3 * id + 1]], c = dv[dt[3 * id + 2]];
                    float d = (b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z);
                    if (Mathf.Abs(d) < 1e-7f) continue;
                    float u = ((px - a.x) * (c.z - a.z) - (c.x - a.x) * (pz - a.z)) / d, v = ((b.x - a.x) * (pz - a.z) - (px - a.x) * (b.z - a.z)) / d;
                    if (u >= 0f && v >= 0f && u + v <= 1f) return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ binalar
        public struct OBB { public Vector3 c; public float rotDeg; public Vector3 size; }

        readonly Dictionary<long, List<int>> edgeHash = new Dictionary<long, List<int>>();

        /// <summary>Kutu (V2 merkez, w×d, rot rad) yol + kaldırım koridoruna (marj dahil) giriyor mu?</summary>
        public bool BoxOnRoad(V2 c, float w, float d, float rot, float margin)
        {
            if (edgeHash.Count == 0)
            {
                for (int ei = 0; ei < gen.edges.Count; ei++)
                    foreach (var p in gen.edges[ei].pts)
                    {
                        long k = GKey(Mathf.FloorToInt(p.x / GCell), Mathf.FloorToInt(p.z / GCell));
                        List<int> l; if (!edgeHash.TryGetValue(k, out l)) edgeHash[k] = l = new List<int>();
                        if (l.Count == 0 || l[l.Count - 1] != ei) l.Add(ei);
                    }
            }
            float sn = Mathf.Sin(rot), cs = Mathf.Cos(rot);
            float rad = Mathf.Sqrt(w * w + d * d) * 0.5f;
            var seen = new HashSet<int>();
            int cx = Mathf.FloorToInt(c.x / GCell), cz = Mathf.FloorToInt(c.z / GCell);
            int span = 1 + Mathf.CeilToInt(rad / GCell);
            for (int dx = -span; dx <= span; dx++)
                for (int dz = -span; dz <= span; dz++)
                {
                    List<int> l;
                    if (!edgeHash.TryGetValue(GKey(cx + dx, cz + dz), out l)) continue;
                    foreach (int ei in l)
                    {
                        if (!seen.Add(ei)) continue;
                        var e = gen.edges[ei];
                        float half = e.Width * 0.5f + CityGen.Sidewalk(e.cls) + margin;
                        for (int i = 1; i < e.pts.Count; i++)
                        {
                            if (e.tunnel[i] && e.tunnel[i - 1]) continue;
                            if (SegRectDist(e.pts[i - 1], e.pts[i], c, w * 0.5f, d * 0.5f, sn, cs) < half) return true;
                        }
                    }
                }
            // kavşak köşeleri
            foreach (var J in junctions)
            {
                float dx = J.center.x - c.x, dz = J.center.z - c.z;
                if (dx * dx + dz * dz > (rad + 60f) * (rad + 60f)) continue;
                foreach (var co in J.corners)
                {
                    var arc = co.sidewalk ? co.outArc : co.inArc;
                    var jc = new V2(J.center.x, J.center.z);
                    foreach (var q in arc)
                        if (SegRectDist(new V2(q.x, q.z), jc, c, w * 0.5f, d * 0.5f, sn, cs) < margin + 0.5f) return true;
                }
            }
            return false;
        }

        /// <summary>2B doğru parçası ile döndürülmüş dikdörtgen arası mesafe (0 = kesişir).</summary>
        static float SegRectDist(V2 a, V2 b, V2 c, float hx, float hz, float sn, float cs)
        {
            // yerel eksen: sağ = (cos,-sin), ileri = (sin,cos)
            System.Func<V2, V2> L = (p) => { float x = p.x - c.x, z = p.z - c.z; return new V2(x * cs - z * sn, x * sn + z * cs); };
            V2 la = L(a), lb = L(b);
            float best = float.MaxValue;
            int n = Mathf.Max(2, Mathf.CeilToInt(V2.Dist(a, b) / 0.75f));
            for (int i = 0; i <= n; i++)
            {
                V2 p = V2.Lerp(la, lb, i / (float)n);
                float dx = Mathf.Max(0f, Mathf.Abs(p.x) - hx), dz = Mathf.Max(0f, Mathf.Abs(p.z) - hz);
                best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz));
                if (best <= 0f) return 0f;
            }
            return best;
        }

        /// <summary>Bina temeli: köşelerdeki arazinin en düşüğü.</summary>
        public float BaseHeight(Box b)
        {
            float min = float.MaxValue;
            float s = Mathf.Sin(b.rot), co = Mathf.Cos(b.rot);
            for (int i = 0; i < 4; i++)
            {
                float lx = (i % 2 == 0 ? -0.5f : 0.5f) * b.w, lz = (i < 2 ? -0.5f : 0.5f) * b.d;
                float x = b.c.x + lx * co + lz * s, z = b.c.z - lx * s + lz * co;
                min = Mathf.Min(min, Ground(x, z));
            }
            return min;
        }
    }
}
