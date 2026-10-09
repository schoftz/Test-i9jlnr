using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using MostWanted.Gen;

namespace MostWanted
{
    /// <summary>
    /// "Kendi Şehrimiz": CityGen verisinden (yollar, binalar, bölgeler) Unity sahnesi kurar.
    /// Yol yüzeyleri + kavşaklar + kaldırım/bordür tek veri kaynağından, 250 m parçalar halinde birleştirilir;
    /// her parçada TEK kaynaklı (dikişsiz) MeshCollider. Trafik/polis/yarış ağı aynı veriden (şerit hizası birebir).
    /// </summary>
    public class OwnCity : World
    {
        public CityGen gen;
        const float Chunk = 250f;
        const float CurbH = 0.15f;
        readonly Dictionary<int, int> junctionNode = new Dictionary<int, int>();   // gen düğüm → graf düğüm
        readonly List<List<int>> edgeNodes = new List<List<int>>();                  // gen kenar → graf düğümleri (a→b)
        readonly float[] junctionR;
        Material[] roadMats;      // 0..4 sınıf, 5 düz asfalt, 6 kaldırım, 7 beton, 8 çelik, 9 tünel lambası
        readonly Dictionary<long, MeshKit> roadKits = new Dictionary<long, MeshKit>();
        readonly List<Material> windowMats = new List<Material>();
        readonly List<InstancedBatch> batches = new List<InstancedBatch>();
        readonly List<Vector3> tunnelLights = new List<Vector3>();
        CityRuntime runtime;

        public OwnCity() { title = "Kendi Şehrimiz"; gen = new CityGen(7); gen.Generate(); junctionR = new float[gen.nodes.Count]; }

        static Vector3 P3(V2 p, float y) { return new Vector3(p.x, y, p.z); }
        static long Key(Vector3 p) { return ((long)Mathf.FloorToInt(p.x / Chunk) << 32) ^ (uint)Mathf.FloorToInt(p.z / Chunk); }

        MeshKit Kit(Vector3 p)
        {
            long k = Key(p);
            MeshKit m;
            if (!roadKits.TryGetValue(k, out m)) roadKits[k] = m = new MeshKit(10);
            return m;
        }

        public override void Build()
        {
            root = new GameObject("KendiSehrimiz").transform;
            runtime = root.gameObject.AddComponent<CityRuntime>();
            runtime.city = this;
            MakeMaterials();
            BuildGraph();
            BuildRoads();
            BuildTerrain();
            BuildBuildings();
            BuildWaterAndTrees();
            BuildMeta();
            foreach (var b in batches) b.Bake();
            runtime.batches = batches;
            area = new Bounds(Vector3.zero, new Vector3(4400f, 400f, 4400f));
            Debug.Log("[MW] Kendi Şehrimiz: " + gen.nodes.Count + " kavşak, " + gen.edges.Count + " yol, " + gen.buildings.Count + " bina, " + graph.nodes.Count + " ağ düğümü");
        }

        // ------------------------------------------------------------------ malzemeler
        void MakeMaterials()
        {
            roadMats = new Material[10];
            RoadClass[] cls = { RoadClass.Street, RoadClass.Avenue, RoadClass.Boulevard, RoadClass.Highway, RoadClass.Coastal };
            for (int i = 0; i < 5; i++)
            {
                var m = U.NewMat(Color.white, 0.22f, 0f);
                U.SetMainTex(m, RoadTexture(cls[i]));
                roadMats[i] = m;
            }
            var plain = U.NewMat(Color.white, 0.2f, 0f);
            U.SetMainTex(plain, AsphaltTexture());
            U.SetMainTexScale(plain, new Vector2(1f / 12f, 1f / 12f));
            roadMats[5] = plain;
            var side = U.NewMat(new Color(0.66f, 0.65f, 0.62f), 0.15f, 0f);
            U.SetMainTex(side, PavingTexture());
            U.SetMainTexScale(side, new Vector2(0.5f, 0.5f));
            roadMats[6] = side;
            roadMats[7] = U.NewMat(new Color(0.6f, 0.6f, 0.58f), 0.2f, 0f);
            roadMats[8] = U.NewMat(new Color(0.72f, 0.74f, 0.77f), 0.6f, 0.8f);
            roadMats[9] = U.Emissive(new Color(1f, 0.92f, 0.75f), new Color(2.2f, 1.9f, 1.4f));
        }

        static Texture2D AsphaltTexture()
        {
            int n = 128;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true);
            var c = new Color[n * n];
            var rng = new System.Random(3);
            for (int i = 0; i < c.Length; i++)
            {
                float v = 0.19f + (float)rng.NextDouble() * 0.05f;
                c[i] = new Color(v, v, v * 1.03f);
            }
            t.SetPixels(c); t.Apply(true); t.wrapMode = TextureWrapMode.Repeat; t.anisoLevel = 8;
            return t;
        }

        static Texture2D PavingTexture()
        {
            int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true);
            var c = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    bool joint = (x % 32) < 1 || (y % 32) < 1;
                    float v = joint ? 0.55f : 0.82f + ((x / 32 + y / 32) % 2) * 0.04f;
                    c[y * n + x] = new Color(v, v, v * 0.98f);
                }
            t.SetPixels(c); t.Apply(true); t.wrapMode = TextureWrapMode.Repeat;
            return t;
        }

        /// <summary>Şerit çizgili asfalt dokusu (u: enine 0..1, v: boyuna 10 m).</summary>
        static Texture2D RoadTexture(RoadClass c)
        {
            int wpx = 256, hpx = 128;
            float W = CityGen.Width(c);
            var t = new Texture2D(wpx, hpx, TextureFormat.RGBA32, true);
            var cols = new Color[wpx * hpx];
            var rng = new System.Random((int)c + 11);
            for (int y = 0; y < hpx; y++)
                for (int x = 0; x < wpx; x++)
                {
                    float m = (x + 0.5f) / wpx * W;               // metre (sol kenardan)
                    float vv = 0.19f + (float)rng.NextDouble() * 0.045f;
                    Color col = new Color(vv, vv, vv * 1.03f);
                    bool dashOn = y < hpx * 0.45f;
                    float ctr = W * 0.5f;
                    bool white = false, yellow = false;
                    if (c == RoadClass.Highway)
                    {
                        if (Mathf.Abs(m - 1.0f) < 0.12f || Mathf.Abs(m - (W - 1.0f)) < 0.12f) white = true;
                        if (Mathf.Abs(m - (ctr - 0.6f)) < 0.1f || Mathf.Abs(m - (ctr + 0.6f)) < 0.1f) yellow = true;
                        foreach (float lm in new[] { ctr - 4.6f, ctr - 8.2f, ctr + 4.6f, ctr + 8.2f }) if (Mathf.Abs(m - lm) < 0.1f && dashOn) white = true;
                    }
                    else if (c == RoadClass.Boulevard || c == RoadClass.Avenue)
                    {
                        if (Mathf.Abs(m - (ctr - 0.15f)) < 0.07f || Mathf.Abs(m - (ctr + 0.15f)) < 0.07f) yellow = true;
                        float lane = c == RoadClass.Boulevard ? 4.2f : 3.4f;
                        if ((Mathf.Abs(m - (ctr - lane)) < 0.08f || Mathf.Abs(m - (ctr + lane)) < 0.08f) && dashOn) white = true;
                        if (Mathf.Abs(m - 0.4f) < 0.08f || Mathf.Abs(m - (W - 0.4f)) < 0.08f) white = true;
                    }
                    else
                    {
                        if (Mathf.Abs(m - ctr) < 0.07f && dashOn) white = true;
                        if (c == RoadClass.Coastal && (Mathf.Abs(m - 0.35f) < 0.08f || Mathf.Abs(m - (W - 0.35f)) < 0.08f)) white = true;
                    }
                    if (white) col = new Color(0.86f, 0.86f, 0.84f);
                    if (yellow) col = new Color(0.9f, 0.72f, 0.12f);
                    cols[y * wpx + x] = col;
                }
            t.SetPixels(cols); t.Apply(true); t.wrapMode = TextureWrapMode.Repeat; t.anisoLevel = 8;
            return t;
        }

        // ------------------------------------------------------------------ yol ağı (trafik/polis/yarış)
        void BuildGraph()
        {
            for (int i = 0; i < gen.nodes.Count; i++)
            {
                var n = gen.nodes[i];
                float lane = 2.3f;
                foreach (int ei in n.edges) lane = Mathf.Max(lane, CityGen.Lane(gen.edges[ei].cls));
                junctionNode[i] = graph.Add(P3(n.p, n.y) + Vector3.up * 0.05f, lane);
                // kavşak yarıçapı (yol uçlarının kırpılacağı mesafe)
                float r = 0f;
                if (n.edges.Count >= 3) foreach (int ei in n.edges) r = Mathf.Max(r, CityGen.Width(gen.edges[ei].cls) * 0.5f + 3f);
                junctionR[i] = r;
            }
            foreach (var e in gen.edges)
            {
                var list = new List<int> { junctionNode[e.a] };
                float acc = 0f, L = e.Length;
                float lane = CityGen.Lane(e.cls);
                for (int i = 1; i < e.pts.Count - 1; i++)
                {
                    acc += V2.Dist(e.pts[i - 1], e.pts[i]);
                    float remain = L - acc;
                    if (acc >= 24f && remain > 12f)
                    {
                        acc = 0f; L = remain;
                        int id = graph.Add(P3(e.pts[i], e.ys[i]) + Vector3.up * 0.05f, lane);
                        graph.Link(list[list.Count - 1], id);
                        list.Add(id);
                    }
                }
                int end = junctionNode[e.b];
                if (list[list.Count - 1] != end) { graph.Link(list[list.Count - 1], end); list.Add(end); }
                edgeNodes.Add(list);
            }
        }

        // ------------------------------------------------------------------ yollar
        struct Sample { public Vector3 p; public Vector3 r; public bool bridge, tunnel; public float s; }

        List<Sample> Trimmed(REdge e)
        {
            int m = e.pts.Count;
            var s = new float[m];
            for (int i = 1; i < m; i++) s[i] = s[i - 1] + V2.Dist(e.pts[i - 1], e.pts[i]);
            float L = s[m - 1];
            float ta = junctionR[e.a], tb = junctionR[e.b];
            var o = new List<Sample>();
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
            // köşe yumuşatma: yön vektörlerini komşularla ortala (bindirme yok)
            for (int i = 1; i < o.Count - 1; i++)
            {
                var a = o[i - 1].p; var b = o[i + 1].p;
                Vector3 d = new Vector3(b.x - a.x, 0, b.z - a.z).normalized;
                var x = o[i]; x.r = new Vector3(d.z, 0, -d.x); o[i] = x;
            }
            return o;
        }

        void BuildRoads()
        {
            var bridgeRail = new List<KeyValuePair<Vector3, Vector3>>();
            var pierBatch = new InstancedBatch { mesh = PrimMesh(PrimitiveType.Cylinder), mats = new[] { roadMats[7] }, cull = 1500f, layer = 0 };
            batches.Add(pierBatch);
            for (int ei = 0; ei < gen.edges.Count; ei++)
            {
                var e = gen.edges[ei];
                var smp = Trimmed(e);
                if (smp.Count < 2) continue;
                float W = e.Width, hw = W * 0.5f, sw = CityGen.Sidewalk(e.cls);
                int cls = (int)e.cls;
                bool highway = e.cls == RoadClass.Highway;
                for (int i = 1; i < smp.Count; i++)
                {
                    var a = smp[i - 1]; var b = smp[i];
                    var kit = Kit((a.p + b.p) * 0.5f);
                    float va = a.s / 10f, vb = b.s / 10f;
                    // yol yüzeyi
                    kit.Quad(cls, a.p - a.r * hw, b.p - b.r * hw, b.p + b.r * hw, a.p + a.r * hw,
                        new Vector2(0, va), new Vector2(0, vb), new Vector2(1, vb), new Vector2(1, va));
                    bool deck = a.bridge || b.bridge;
                    if (sw > 0f && !a.tunnel)
                    {
                        Vector3 up = Vector3.up * CurbH;
                        for (int side = -1; side <= 1; side += 2)
                        {
                            Vector3 ai = a.p + a.r * (side * hw), bi = b.p + b.r * (side * hw);
                            Vector3 ao = a.p + a.r * (side * (hw + sw)), bo = b.p + b.r * (side * (hw + sw));
                            if (side > 0)
                            {
                                kit.Quad(6, ai + up, bi + up, bo + up, ao + up, new Vector2(ai.x, ai.z), new Vector2(bi.x, bi.z), new Vector2(bo.x, bo.z), new Vector2(ao.x, ao.z));
                                kit.Quad(7, ai, bi, bi + up, ai + up, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);       // bordür yüzü
                                kit.Quad(7, ao + up, bo + up, bo + Vector3.down * (deck ? 1.4f : 0.8f), ao + Vector3.down * (deck ? 1.4f : 0.8f), Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero); // etek
                            }
                            else
                            {
                                kit.Quad(6, ao + up, bo + up, bi + up, ai + up, new Vector2(ao.x, ao.z), new Vector2(bo.x, bo.z), new Vector2(bi.x, bi.z), new Vector2(ai.x, ai.z));
                                kit.Quad(7, ai + up, bi + up, bi, ai, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
                                kit.Quad(7, ao + Vector3.down * (deck ? 1.4f : 0.8f), bo + Vector3.down * (deck ? 1.4f : 0.8f), bo + up, ao + up, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
                            }
                            if (deck) bridgeRail.Add(new KeyValuePair<Vector3, Vector3>(ao + up, bo + up));
                        }
                    }
                    else
                    {
                        // otoyol / tünel: kenar etekleri
                        Vector3 down = Vector3.down * (deck ? 1.6f : 0.8f);
                        kit.Quad(7, a.p + a.r * hw, b.p + b.r * hw, b.p + b.r * hw + down, a.p + a.r * hw + down, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
                        kit.Quad(7, a.p - a.r * hw + down, b.p - b.r * hw + down, b.p - b.r * hw, a.p - a.r * hw, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
                    }
                    if (deck)
                    {
                        // köprü tabliyesi alt yüzü
                        float ww = hw + sw;
                        Vector3 d1 = Vector3.down * (sw > 0 ? 1.4f : 1.6f);
                        kit.Quad(7, a.p + a.r * ww + d1, b.p + b.r * ww + d1, b.p - b.r * ww + d1, a.p - a.r * ww + d1, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
                        if (i % 3 == 0)
                        {
                            float ground = Mathf.Min(gen.H(a.p.x, a.p.z), a.p.y - 2f);
                            float hh = a.p.y - 1.6f - (ground - 6f);
                            if (hh > 2f) pierBatch.Add(new Vector3(a.p.x, ground - 6f + hh * 0.5f, a.p.z), Quaternion.identity, new Vector3(2.2f, hh * 0.5f, 2.2f));
                        }
                    }
                    if (highway)
                    {
                        Quaternion q = Quaternion.LookRotation(new Vector3(b.p.x - a.p.x, b.p.y - a.p.y, b.p.z - a.p.z));
                        float len = Vector3.Distance(a.p, b.p) + 0.05f;
                        Vector3 mid = (a.p + b.p) * 0.5f, rr = (a.r + b.r).normalized;
                        BoxInto(kit, 7, mid + Vector3.up * 0.45f, q, new Vector3(0.6f, 0.9f, len));                    // orta bariyer
                        BoxInto(kit, 8, mid + rr * (hw - 0.3f) + Vector3.up * 0.75f, q, new Vector3(0.12f, 0.35f, len));  // korkuluk
                        BoxInto(kit, 8, mid - rr * (hw - 0.3f) + Vector3.up * 0.75f, q, new Vector3(0.12f, 0.35f, len));
                        if (a.tunnel)
                        {
                            BoxInto(kit, 7, mid + rr * (hw + 0.6f) + Vector3.up * 4f, q, new Vector3(1.2f, 8f, len));
                            BoxInto(kit, 7, mid - rr * (hw + 0.6f) + Vector3.up * 4f, q, new Vector3(1.2f, 8f, len));
                            BoxInto(kit, 7, mid + Vector3.up * 8.5f, q, new Vector3(W + 3.6f, 1f, len));
                            BoxInto(kit, 9, mid + Vector3.up * 7.95f + rr * 3f, q, new Vector3(0.6f, 0.08f, len * 0.6f));
                            BoxInto(kit, 9, mid + Vector3.up * 7.95f - rr * 3f, q, new Vector3(0.6f, 0.08f, len * 0.6f));
                            if (i % 6 == 0) tunnelLights.Add(mid + Vector3.up * 7f);
                        }
                    }
                }
                // asma köprü: otoyolun uzun köprü bölümü
                if (highway) SuspensionBridges(smp, hw);
            }
            // köprü korkulukları
            foreach (var rl in bridgeRail)
            {
                Vector3 d = rl.Value - rl.Key; if (d.sqrMagnitude < 0.01f) continue;
                BoxInto(Kit((rl.Key + rl.Value) * 0.5f), 7, (rl.Key + rl.Value) * 0.5f + Vector3.up * 0.5f, Quaternion.LookRotation(d), new Vector3(0.3f, 1f, d.magnitude + 0.05f));
            }
            BuildJunctions();
            // parçaları sahneye
            var roads = new GameObject("Yollar").transform; roads.SetParent(root, false);
            foreach (var kv in roadKits)
            {
                if (kv.Value.Empty) continue;
                var go = new GameObject("YolParcasi");
                go.transform.SetParent(roads, false);
                go.AddComponent<MeshFilter>().sharedMesh = kv.Value.Build("Yol");
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = roadMats;
                mr.shadowCastingMode = ShadowCastingMode.On;
                // tek, kaynaklı çarpıştırıcı (yüzey + bordür + bariyer); tünel lambaları hariç
                var col = new MeshKit(1);
                for (int s = 0; s < 9; s++)
                {
                    var src = kv.Value.sub[s];
                    foreach (int idx in src) { col.v.Add(kv.Value.v[idx]); col.uv.Add(Vector2.zero); col.n.Add(Vector3.up); col.sub[0].Add(col.v.Count - 1); }
                }
                WeldInto(col);
                go.AddComponent<MeshCollider>().sharedMesh = col.BuildCollider("YolCarpisma");
                go.isStatic = true;
            }
            for (int i = 0; i < tunnelLights.Count; i++)
            {
                if (i % 2 == 1) continue;
                var lg = new GameObject("TunelIsik"); lg.transform.SetParent(root, false); lg.transform.position = tunnelLights[i];
                var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.range = 26f; l.intensity = 2.2f; l.color = new Color(1f, 0.86f, 0.62f); l.shadows = LightShadows.None;
            }
            if (tunnelLights.Count > 0)
            {
                Vector3 c = tunnelLights[tunnelLights.Count / 2];
                AddHiding("Tünel", c + Vector3.down * 3f, new Vector3(60f, 10f, 60f));
            }
        }

        /// <summary>Aynı konumdaki köşeleri birleştir: parçalar arası dikiş/basamak olmasın.</summary>
        static void WeldInto(MeshKit k)
        {
            var map = new Dictionary<long, int>();
            var nv = new List<Vector3>();
            var remap = new int[k.v.Count];
            for (int i = 0; i < k.v.Count; i++)
            {
                var p = k.v[i];
                long key = ((long)Mathf.RoundToInt(p.x * 50f) * 73856093L) ^ ((long)Mathf.RoundToInt(p.y * 50f) * 19349663L) ^ ((long)Mathf.RoundToInt(p.z * 50f) * 83492791L);
                int id;
                if (!map.TryGetValue(key, out id)) { id = nv.Count; nv.Add(p); map[key] = id; }
                remap[i] = id;
            }
            var tris = k.sub[0];
            for (int i = 0; i < tris.Count; i++) tris[i] = remap[tris[i]];
            k.v.Clear(); k.v.AddRange(nv);
            k.uv.Clear(); k.n.Clear();
            for (int i = 0; i < nv.Count; i++) { k.uv.Add(Vector2.zero); k.n.Add(Vector3.up); }
        }

        static void BoxInto(MeshKit kit, int sub, Vector3 c, Quaternion q, Vector3 size)
        {
            Vector3 hx = q * Vector3.right * (size.x * 0.5f), hy = q * Vector3.up * (size.y * 0.5f), hz = q * Vector3.forward * (size.z * 0.5f);
            Vector3[] p =
            {
                c - hx - hy - hz, c - hx + hy - hz, c + hx + hy - hz, c + hx - hy - hz,
                c - hx - hy + hz, c - hx + hy + hz, c + hx + hy + hz, c + hx - hy + hz
            };
            int[][] f = { new[] { 0, 1, 2, 3 }, new[] { 7, 6, 5, 4 }, new[] { 4, 5, 1, 0 }, new[] { 3, 2, 6, 7 }, new[] { 1, 5, 6, 2 }, new[] { 4, 0, 3, 7 } };
            foreach (var q4 in f) kit.Quad(sub, p[q4[0]], p[q4[1]], p[q4[2]], p[q4[3]], Vector2.zero, Vector2.up, Vector2.one, Vector2.right);
        }

        void SuspensionBridges(List<Sample> smp, float hw)
        {
            int bs = -1;
            for (int i = 0; i <= smp.Count; i++)
            {
                bool br = i < smp.Count && smp[i].bridge;
                if (br && bs < 0) bs = i;
                if (!br && bs >= 0)
                {
                    int be = i - 1;
                    if (smp[be].s - smp[bs].s > 120f)
                    {
                        var tower = U.Mat(new Color(0.78f, 0.2f, 0.14f), 0.4f, 0.3f);
                        var cable = U.Mat(new Color(0.85f, 0.85f, 0.85f), 0.6f, 0.8f);
                        int[] tw = { bs + (be - bs) / 4, bs + 3 * (be - bs) / 4 };
                        foreach (int ti in tw)
                        {
                            var t = smp[ti]; Vector3 fwd = Vector3.Cross(t.r, Vector3.up) * -1f;
                            float top = 58f;
                            for (int s2 = -1; s2 <= 1; s2 += 2)
                            {
                                var go = U.Prim(PrimitiveType.Cube, "KopruKulesi", root, t.p + t.r * s2 * (hw + 1.6f) + Vector3.up * (top / 2f - 14f), new Vector3(2.6f, top + 28f, 2.6f), tower, true);
                                go.transform.rotation = Quaternion.LookRotation(fwd);
                                for (int k = -7; k <= 7; k++)
                                {
                                    if (k == 0) continue;
                                    int ci = Mathf.Clamp(ti + k * 2, 0, smp.Count - 1);
                                    Vector3 dk = smp[ci].p + smp[ci].r * s2 * (hw + 0.6f);
                                    Vector3 tp = t.p + t.r * s2 * (hw + 1.6f) + Vector3.up * (top - 6f);
                                    Vector3 cd = tp - dk;
                                    var c = U.Prim(PrimitiveType.Cube, "Kablo", root, (tp + dk) * 0.5f, new Vector3(0.16f, 0.16f, cd.magnitude), cable);
                                    c.transform.rotation = Quaternion.LookRotation(cd.normalized);
                                    c.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                                }
                            }
                            U.Prim(PrimitiveType.Cube, "KuleKiris", root, t.p + Vector3.up * (top + 10f), new Vector3((hw + 2f) * 2f, 2.2f, 2.6f), tower, true).transform.rotation = Quaternion.LookRotation(fwd);
                        }
                        U.Text3D("BOĞAZ KÖPRÜSÜ", root, smp[tw[0]].p + Vector3.up * 74f, Quaternion.LookRotation(Vector3.Cross(smp[tw[0]].r, Vector3.up) * -1f), 6f, Color.white);
                    }
                    bs = -1;
                }
            }
        }

        void BuildJunctions()
        {
            for (int ni = 0; ni < gen.nodes.Count; ni++)
            {
                var n = gen.nodes[ni];
                if (junctionR[ni] <= 0f) continue;
                var ends = new List<KeyValuePair<float, Sample>>();
                var widths = new List<float>(); var sws = new List<float>();
                foreach (int ei in n.edges)
                {
                    var e = gen.edges[ei];
                    var smp = Trimmed(e);
                    if (smp.Count < 2) continue;
                    // kavşağa bakan uç; "dışa" yön
                    Sample end = e.a == ni ? smp[0] : smp[smp.Count - 1];
                    Vector3 c = P3(n.p, n.y);
                    Vector3 d = new Vector3(end.p.x - c.x, 0, end.p.z - c.z).normalized;
                    // yol ucundaki köşelerle birebir aynı noktalar (kaynak/dikişsiz): örneğin kendi sağ vektörü, dışa yöne göre işaretli
                    if (e.a != ni) end.r = -end.r;
                    float ang = Mathf.Atan2(d.x, d.z);
                    ends.Add(new KeyValuePair<float, Sample>(ang, end));
                    widths.Add(e.Width); sws.Add(CityGen.Sidewalk(e.cls));
                }
                if (ends.Count < 2) continue;
                // açıya göre sırala (saat yönü)
                var idx = new List<int>(); for (int i = 0; i < ends.Count; i++) idx.Add(i);
                idx.Sort((x, y) => ends[x].Key.CompareTo(ends[y].Key));
                Vector3 center = P3(n.p, n.y);
                var kit = Kit(center);
                var poly = new List<Vector3>();
                var info = new List<int>();
                foreach (int i in idx)
                {
                    var s = ends[i].Value; float hw = widths[i] * 0.5f;
                    poly.Add(s.p - s.r * hw); info.Add(i);   // sol köşe
                    poly.Add(s.p + s.r * hw); info.Add(i);   // sağ köşe
                }
                for (int k = 0; k < poly.Count; k++)
                {
                    Vector3 a = poly[k], b = poly[(k + 1) % poly.Count];
                    int ia = kit.Vert(center, new Vector2(center.x, center.z), Vector3.up);
                    int ib = kit.Vert(a, new Vector2(a.x, a.z), Vector3.up);
                    int ic = kit.Vert(b, new Vector2(b.x, b.z), Vector3.up);
                    kit.Tri(5, ia, ib, ic);
                }
                // köşe kaldırımları (sağ köşe i → sol köşe i+1)
                for (int k = 0; k < idx.Count; k++)
                {
                    int i0 = idx[k], i1 = idx[(k + 1) % idx.Count];
                    float sw0 = sws[i0], sw1 = sws[i1];
                    if (sw0 <= 0f || sw1 <= 0f) continue;
                    var s0 = ends[i0].Value; var s1 = ends[i1].Value;
                    Vector3 r0 = s0.p + s0.r * (widths[i0] * 0.5f), l1 = s1.p - s1.r * (widths[i1] * 0.5f);
                    Vector3 r0o = s0.p + s0.r * (widths[i0] * 0.5f + sw0), l1o = s1.p - s1.r * (widths[i1] * 0.5f + sw1);
                    Vector3 up = Vector3.up * CurbH;
                    // köşe dolgusu (dışa doğru üçgenler)
                    Vector3 cornerOut = (r0o + l1o) * 0.5f + ((r0o + l1o) * 0.5f - center).normalized * (Mathf.Max(sw0, sw1) * 0.6f);
                    cornerOut.y = (r0o.y + l1o.y) * 0.5f;
                    kit.Quad(6, r0 + up, r0o + up, cornerOut + up, l1 + up, new Vector2(r0.x, r0.z), new Vector2(r0o.x, r0o.z), new Vector2(cornerOut.x, cornerOut.z), new Vector2(l1.x, l1.z));
                    kit.Quad(6, l1 + up, cornerOut + up, l1o + up, l1 + up, new Vector2(l1.x, l1.z), new Vector2(cornerOut.x, cornerOut.z), new Vector2(l1o.x, l1o.z), new Vector2(l1.x, l1.z));
                    kit.Quad(7, r0, r0 + up, l1 + up, l1, Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);   // bordür yüzü (kavşak tarafı)
                }
            }
        }

        static Mesh PrimMesh(PrimitiveType t)
        {
            var go = GameObject.CreatePrimitive(t);
            var m = go.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(go);
            return m;
        }

        // ------------------------------------------------------------------ arazi
        readonly Dictionary<long, List<Vector4>> segHash = new Dictionary<long, List<Vector4>>();   // x,z,y,yarıGenişlik (segment orta noktaları)

        void IndexRoads()
        {
            foreach (var e in gen.edges)
            {
                float half = e.Width * 0.5f + CityGen.Sidewalk(e.cls) + 1.5f;
                for (int i = 0; i < e.pts.Count; i++)
                {
                    var p = e.pts[i];
                    long k = ((long)Mathf.FloorToInt(p.x / 40f) << 32) ^ (uint)Mathf.FloorToInt(p.z / 40f);
                    List<Vector4> l; if (!segHash.TryGetValue(k, out l)) segHash[k] = l = new List<Vector4>();
                    l.Add(new Vector4(p.x, p.z, e.ys[i], e.tunnel[i] ? -999f : half));
                }
            }
        }

        /// <summary>Arazi yüksekliği: doğal H, yollara yakınsa yol seviyesinin biraz altına yumuşak geçiş.</summary>
        public float Ground(float x, float z)
        {
            float h = gen.H(x, z);
            int cx = Mathf.FloorToInt(x / 40f), cz = Mathf.FloorToInt(z / 40f);
            float best = float.MaxValue, by = 0f, bh = 0f;
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    List<Vector4> l;
                    if (!segHash.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out l)) continue;
                    foreach (var s in l)
                    {
                        if (s.w < 0f) continue;
                        float d = Mathf.Sqrt((s.x - x) * (s.x - x) + (s.y - z) * (s.y - z)) - s.w;
                        if (d < best) { best = d; by = s.z; bh = s.w; }
                    }
                }
            if (best < 30f)
            {
                float t = Mathf.Clamp01(best / 30f); t = t * t * (3f - 2f * t);
                float roadLevel = by - 0.35f;
                h = Mathf.Lerp(roadLevel, h, t);
                if (best < 2f) h = Mathf.Min(h, roadLevel);
            }
            return h;
        }

        void BuildTerrain()
        {
            IndexRoads();
            var tex = GroundTexture();
            var mat = U.NewMat(Color.white, 0.06f, 0f);
            U.SetMainTex(mat, tex);
            float x0 = -3200f, z0 = -3400f, size = 6800f;
            int chunks = 12;
            float cs = size / chunks;
            for (int cx = 0; cx < chunks; cx++)
                for (int cz = 0; cz < chunks; cz++)
                {
                    float wx = x0 + cx * cs, wz = z0 + cz * cs;
                    bool inner = wx > -2000f && wx + cs < 2100f && wz > -2200f && wz + cs < 2000f;
                    int res = inner ? 40 : 14;     // şehir içinde ~14 m, dışında ~40 m
                    float step = cs / res;
                    var verts = new Vector3[(res + 1) * (res + 1)];
                    var uvs = new Vector2[verts.Length];
                    for (int i = 0; i <= res; i++)
                        for (int j = 0; j <= res; j++)
                        {
                            float x = wx + i * step, z = wz + j * step;
                            verts[i * (res + 1) + j] = new Vector3(x, Ground(x, z), z);
                            uvs[i * (res + 1) + j] = new Vector2((x - x0) / size, (z - z0) / size);
                        }
                    var tris = new int[res * res * 6];
                    int t = 0;
                    for (int i = 0; i < res; i++)
                        for (int j = 0; j < res; j++)
                        {
                            int a = i * (res + 1) + j, b = a + 1, d = a + res + 1, e = d + 1;
                            tris[t++] = a; tris[t++] = b; tris[t++] = d;
                            tris[t++] = b; tris[t++] = e; tris[t++] = d;
                        }
                    var m = new Mesh { vertices = verts, uv = uvs, triangles = tris };
                    m.RecalculateNormals(); m.RecalculateBounds();
                    var go = new GameObject("Arazi");
                    go.transform.SetParent(root, false);
                    go.AddComponent<MeshFilter>().sharedMesh = m;
                    var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off;
                    go.AddComponent<MeshCollider>().sharedMesh = m;
                    go.isStatic = true;
                }
        }

        Texture2D GroundTexture()
        {
            int n = 1024;
            float x0 = -3200f, z0 = -3400f, size = 6800f;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true);
            var px = new Color[n * n];
            Color grass = new Color(0.33f, 0.45f, 0.22f), lawn = new Color(0.38f, 0.52f, 0.26f), dirt = new Color(0.45f, 0.42f, 0.35f),
                  concrete = new Color(0.55f, 0.55f, 0.52f), sand = new Color(0.84f, 0.78f, 0.6f), field1 = new Color(0.62f, 0.56f, 0.28f),
                  field2 = new Color(0.4f, 0.52f, 0.22f), rock = new Color(0.48f, 0.46f, 0.43f);
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    float x = x0 + (i + 0.5f) / n * size, z = z0 + (j + 0.5f) / n * size;
                    var p = new V2(x, z);
                    float h = gen.H(x, z);
                    Zone zn = gen.ZoneAt(p);
                    bool inside = gen.InsideHighway(p, 0f);
                    Color c = grass;
                    if (inside)
                    {
                        switch (zn)
                        {
                            case Zone.Downtown: case Zone.Midrise: c = Color.Lerp(concrete, lawn, 0.25f); break;
                            case Zone.Industrial: case Zone.Harbour: c = Color.Lerp(concrete, dirt, 0.4f); break;
                            case Zone.Park: c = lawn; break;
                            default: c = Color.Lerp(lawn, grass, CityGen.Noise(x / 60f, z / 60f)); break;
                        }
                    }
                    else
                    {
                        int fx = Mathf.FloorToInt(x / 160f), fz = Mathf.FloorToInt(z / 110f);
                        float hs = Mathf.Repeat(Mathf.Sin(fx * 12.9898f + fz * 78.233f) * 43758.55f, 1f);
                        c = hs < 0.4f ? Color.Lerp(field1, field2, Mathf.Repeat(hs * 7f, 1f)) : Color.Lerp(grass, dirt, CityGen.Noise(x / 300f, z / 300f) * 0.4f);
                    }
                    if (h > 60f) c = Color.Lerp(c, rock, Mathf.Clamp01((h - 60f) / 80f));
                    if (h < gen.waterLevel + 2.5f) c = sand;
                    px[j * n + i] = c;
                }
            tex.SetPixels(px); tex.Apply(true);
            tex.wrapMode = TextureWrapMode.Clamp; tex.anisoLevel = 4;
            return tex;
        }

        // ------------------------------------------------------------------ binalar
        Material[] styleMats;   // 0 cam kule, 1 beton, 2 tuğla, 3 sıva, 4 ev, 5 depo, 6 otopark
        Material roofMat, roofTileMat, shopMat, helipadMat;

        void MakeBuildingMaterials()
        {
            // 4x4 pencerelik doku = 16 m (UV metre cinsinden); ışıklar gece emisyon
            var rng = new System.Random(5);
            styleMats = new Material[7];
            Color[] base_ = { new Color(0.55f, 0.66f, 0.78f), new Color(0.72f, 0.71f, 0.68f), new Color(0.62f, 0.36f, 0.27f), new Color(0.86f, 0.8f, 0.68f), new Color(0.9f, 0.86f, 0.78f), new Color(0.6f, 0.62f, 0.66f), new Color(0.6f, 0.6f, 0.58f) };
            for (int s = 0; s < 7; s++)
            {
                Texture2D alb, emi;
                FacadeTextures(s, rng, out alb, out emi);
                var m = U.NewMat(base_[s], s == 0 ? 0.88f : 0.25f, s == 0 ? 0.6f : 0.05f);
                U.SetMainTex(m, alb);
                U.SetMainTexScale(m, new Vector2(1f / 16f, 1f / 16f));
                if (m.HasProperty("_EmissionMap")) { m.SetTexture("_EmissionMap", emi); m.SetTextureScale("_EmissionMap", new Vector2(1f / 16f, 1f / 16f)); }
                U.SetEmission(m, Color.black);
                styleMats[s] = m;
                windowMats.Add(m);
            }
            roofMat = U.Mat(new Color(0.32f, 0.32f, 0.34f), 0.2f, 0f);
            roofTileMat = U.Mat(new Color(0.55f, 0.2f, 0.14f), 0.3f, 0f);
            shopMat = U.NewMat(new Color(0.08f, 0.1f, 0.12f), 0.9f, 0.3f);
            U.SetEmission(shopMat, new Color(0.15f, 0.12f, 0.08f));
            windowMats.Add(shopMat);
            helipadMat = U.Emissive(new Color(0.15f, 0.15f, 0.16f), new Color(0.6f, 0.5f, 0.1f));
        }

        static void FacadeTextures(int style, System.Random rng, out Texture2D alb, out Texture2D emi)
        {
            const int S = 256;
            alb = new Texture2D(S, S, TextureFormat.RGBA32, true);
            emi = new Texture2D(S, S, TextureFormat.RGBA32, true);
            var a = new Color[S * S]; var e = new Color[S * S];
            int cells = 4, cs = S / cells;
            for (int cx = 0; cx < cells; cx++)
                for (int cy = 0; cy < cells; cy++)
                {
                    bool lit = rng.NextDouble() < (style == 5 ? 0.25 : 0.5);
                    Color litCol = Color.Lerp(new Color(1f, 0.82f, 0.5f), new Color(0.75f, 0.85f, 1f), (float)rng.NextDouble() * 0.6f);
                    for (int x = 0; x < cs; x++)
                        for (int y = 0; y < cs; y++)
                        {
                            int px = cx * cs + x, py = cy * cs + y;
                            bool win;
                            if (style == 0) win = x > cs * 0.04f && x < cs * 0.96f && y > cs * 0.12f && y < cs * 0.9f;        // cam giydirme
                            else if (style == 5) win = y > cs * 0.7f && y < cs * 0.85f && x > cs * 0.1f && x < cs * 0.9f;  // depo bant pencere
                            else win = x > cs * 0.2f && x < cs * 0.8f && y > cs * 0.28f && y < cs * 0.82f;
                            bool band = y < cs * 0.06f;   // kat bandı
                            float nn = (float)rng.NextDouble() * 0.05f;
                            Color wall = style == 2 ? (((py / 6 + (px / 12) % 2) % 2 == 0) ? new Color(0.95f, 0.95f, 0.95f) : new Color(0.85f, 0.85f, 0.85f)) : new Color(0.93f - nn, 0.93f - nn, 0.93f - nn);
                            if (band) wall *= 0.8f;
                            a[py * S + px] = win ? (style == 0 ? new Color(0.35f, 0.45f, 0.55f) : new Color(0.14f, 0.18f, 0.24f)) : wall;
                            e[py * S + px] = (win && lit) ? litCol : Color.black;
                        }
                }
            alb.SetPixels(a); alb.Apply(true); alb.wrapMode = TextureWrapMode.Repeat; alb.anisoLevel = 4;
            emi.SetPixels(e); emi.Apply(true); emi.wrapMode = TextureWrapMode.Repeat;
        }

        void BuildBuildings()
        {
            MakeBuildingMaterials();
            var kits = new Dictionary<long, MeshKit>();
            var colliderRoots = new Dictionary<long, Transform>();
            var acBatch = new InstancedBatch { mesh = PrimMesh(PrimitiveType.Cube), mats = new[] { U.Mat(new Color(0.7f, 0.7f, 0.72f), 0.4f, 0.4f) }, cull = 600f };
            var tankBatch = new InstancedBatch { mesh = PrimMesh(PrimitiveType.Cylinder), mats = new[] { U.Mat(new Color(0.45f, 0.36f, 0.28f), 0.3f, 0f) }, cull = 700f };
            var antBatch = new InstancedBatch { mesh = PrimMesh(PrimitiveType.Cube), mats = new[] { U.Mat(new Color(0.3f, 0.3f, 0.3f), 0.5f, 0.8f) }, cull = 900f, shadows = ShadowCastingMode.Off };
            batches.Add(acBatch); batches.Add(tankBatch); batches.Add(antBatch);
            var bRoot = new GameObject("Binalar").transform; bRoot.SetParent(root, false);
            var rng = new System.Random(9);
            foreach (var b in gen.buildings)
            {
                var mp = b.parts[0];
                float baseY = BaseHeight(mp);
                Vector3 c0 = P3(mp.c, baseY);
                long k = Key(c0);
                MeshKit kit;
                // alt-mesh: 0..6 stil cephe, 7 çatı, 8 kiremit, 9 dükkan, 10 helipad
                if (!kits.TryGetValue(k, out kit)) kits[k] = kit = new MeshKit(11);
                Transform croot;
                if (!colliderRoots.TryGetValue(k, out croot)) { croot = new GameObject("BinaCarpisma").transform; croot.SetParent(bRoot, false); colliderRoots[k] = croot; }
                if (b.style == 6) { ParkingGarage(kit, croot, mp, baseY); continue; }
                for (int pi = 0; pi < b.parts.Count; pi++)
                { var part = b.parts[pi];
                    float rotDeg = part.rot * Mathf.Rad2Deg;
                    Vector3 c = P3(part.c, baseY);
                    float y0 = part.y0 - 1.5f;    // temel: eğimli zemine gömülsün
                    float h = part.h + (part.y0 > 0f ? 0f : 1.5f);
                    bool shop = part.y0 <= 0f && (b.zone == Zone.Downtown || b.zone == Zone.Midrise) && b.style != 0;
                    if (shop)
                    {
                        kit.Box(9, 7, c, part.w + 0.2f, part.d + 0.2f, rotDeg, y0, 1.5f + 4.2f);
                        kit.Box(b.style, 7, c, part.w, part.d, rotDeg, 4.2f, part.h - 4.2f);
                    }
                    else kit.Box(b.style, 7, c, part.w, part.d, rotDeg, y0, h);
                    // çarpıştırıcı
                    var cg = new GameObject("B"); cg.transform.SetParent(croot, false);
                    cg.transform.SetPositionAndRotation(c + Vector3.up * (part.y0 + part.h * 0.5f), Quaternion.Euler(0, rotDeg, 0));
                    var bc = cg.AddComponent<BoxCollider>(); bc.size = new Vector3(part.w, part.h + 3f, part.d);
                    // çatı detayları
                    float top = part.y0 + part.h;
                    Quaternion q = Quaternion.Euler(0, rotDeg, 0);
                    if (b.pitched)
                    {
                        PitchedRoof(kit, c, part.w, part.d, rotDeg, top, Mathf.Min(part.w, part.d) * 0.35f);
                    }
                    else
                    {
                        // parapet
                        kit.Box(7, 7, c + q * new Vector3(0, 0, part.d * 0.5f - 0.15f), part.w, 0.3f, rotDeg, top, 0.9f);
                        kit.Box(7, 7, c + q * new Vector3(0, 0, -part.d * 0.5f + 0.15f), part.w, 0.3f, rotDeg, top, 0.9f);
                        kit.Box(7, 7, c + q * new Vector3(part.w * 0.5f - 0.15f, 0, 0), 0.3f, part.d, rotDeg, top, 0.9f);
                        kit.Box(7, 7, c + q * new Vector3(-part.w * 0.5f + 0.15f, 0, 0), 0.3f, part.d, rotDeg, top, 0.9f);
                        int ac = 1 + rng.Next(4);
                        for (int i = 0; i < ac; i++)
                        {
                            Vector3 lp = new Vector3(((float)rng.NextDouble() - 0.5f) * part.w * 0.6f, 0, ((float)rng.NextDouble() - 0.5f) * part.d * 0.6f);
                            acBatch.Add(c + q * lp + Vector3.up * (top + 0.7f), q, new Vector3(2.2f, 1.4f, 1.6f));
                        }
                        if (b.waterTank && part.y0 <= 0f) tankBatch.Add(c + q * new Vector3(part.w * 0.2f, 0, -part.d * 0.15f) + Vector3.up * (top + 2.8f), Quaternion.identity, new Vector3(3.2f, 2.4f, 3.2f));
                        if (b.style == 0 && pi == b.parts.Count - 1)
                        {
                            if (b.helipad) kit.Box(10, 10, c, Mathf.Min(part.w, part.d) * 0.7f, Mathf.Min(part.w, part.d) * 0.7f, rotDeg, top, 0.3f);
                            else antBatch.Add(c + Vector3.up * (top + 9f), Quaternion.identity, new Vector3(0.4f, 18f, 0.4f));
                        }
                    }
                }
            }
            var mats = new Material[11];
            for (int i = 0; i < 7; i++) mats[i] = styleMats[i];
            mats[7] = roofMat; mats[8] = roofTileMat; mats[9] = shopMat; mats[10] = helipadMat;
            foreach (var kv in kits)
            {
                if (kv.Value.Empty) continue;
                var go = new GameObject("BinaParcasi");
                go.transform.SetParent(bRoot, false);
                go.AddComponent<MeshFilter>().sharedMesh = kv.Value.Build("Binalar");
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterials = mats;
                go.isStatic = true;
                // uzakta kes (LOD)
                var lod = go.AddComponent<LODGroup>();
                lod.SetLODs(new[] { new LOD(0.012f, new Renderer[] { mr }) });
                lod.RecalculateBounds();
            }
        }

        float BaseHeight(Box b)
        {
            // binanın oturduğu zemin: köşelerin en düşüğü
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

        static void PitchedRoof(MeshKit kit, Vector3 c, float w, float d, float rotDeg, float top, float rise)
        {
            Quaternion q = Quaternion.Euler(0, rotDeg, 0);
            Vector3 r = q * Vector3.right * (w * 0.5f + 0.3f), f = q * Vector3.forward * (d * 0.5f + 0.3f), up = Vector3.up * top;
            Vector3 ridgeA = c + up + Vector3.up * rise - f, ridgeB = c + up + Vector3.up * rise + f;
            Vector3 la = c + up - r - f, lb = c + up - r + f, ra = c + up + r - f, rb = c + up + r + f;
            kit.Quad(8, la, ridgeA, ridgeB, lb, Vector2.zero, new Vector2(0, 1), Vector2.one, new Vector2(1, 0));
            kit.Quad(8, rb, ridgeB, ridgeA, ra, Vector2.zero, new Vector2(0, 1), Vector2.one, new Vector2(1, 0));
            // alınlıklar
            int i0 = kit.Vert(la, Vector2.zero, -f.normalized), i1 = kit.Vert(ra, Vector2.right, -f.normalized), i2 = kit.Vert(ridgeA, Vector2.up, -f.normalized);
            kit.Tri(4, i0, i2, i1);
            int j0 = kit.Vert(lb, Vector2.zero, f.normalized), j1 = kit.Vert(rb, Vector2.right, f.normalized), j2 = kit.Vert(ridgeB, Vector2.up, f.normalized);
            kit.Tri(4, j0, j1, j2);
        }

        void ParkingGarage(MeshKit kit, Transform croot, Box b, float baseY)
        {
            float rotDeg = b.rot * Mathf.Rad2Deg;
            Quaternion q = Quaternion.Euler(0, rotDeg, 0);
            Vector3 c = P3(b.c, baseY);
            kit.Box(7, 7, c, b.w, b.d, rotDeg, 5.2f, 0.6f, true);    // tavan
            kit.Box(1, 7, c, b.w * 0.9f, b.d * 0.9f, rotDeg, 5.8f, 3.2f); // üst kat cephesi
            for (int x = -1; x <= 1; x += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 cp = c + q * new Vector3(x * b.w * 0.42f, 0, z * b.d * 0.42f);
                    kit.Box(7, 7, cp, 0.8f, 0.8f, rotDeg, -1f, 6.2f);
                    var cg = new GameObject("Kolon"); cg.transform.SetParent(croot, false); cg.transform.position = cp + Vector3.up * 2.6f;
                    cg.AddComponent<BoxCollider>().size = new Vector3(0.8f, 5.2f, 0.8f);
                }
            var roof = new GameObject("OtoparkTavan"); roof.transform.SetParent(croot, false);
            roof.transform.SetPositionAndRotation(c + Vector3.up * 7.2f, q);
            roof.AddComponent<BoxCollider>().size = new Vector3(b.w, 4f, b.d);
            U.Text3D("OTOPARK", root, c + q * new Vector3(0, 0, b.d * 0.5f + 0.3f) + Vector3.up * 7.4f, q * Quaternion.Euler(0, 180, 0), 2.4f, Color.white);
            AddHiding("Otopark", c + Vector3.up * 2.6f, new Vector3(b.w * 0.95f, 5.2f, b.d * 0.95f));
        }

        // ------------------------------------------------------------------ su ve ağaçlar
        void BuildWaterAndTrees()
        {
            var wm = Resources.Load<Material>("MW_WaterMat");
            Material m = wm != null && wm.shader != null && wm.shader.isSupported ? new Material(wm) : U.NewMat(new Color(0.08f, 0.3f, 0.4f), 0.95f, 0f);
            var go = new GameObject("Su"); go.transform.SetParent(root, false);
            go.transform.position = new Vector3(0, gen.waterLevel, 0);
            go.transform.rotation = Quaternion.Euler(90, 0, 0);
            go.transform.localScale = new Vector3(16000f, 16000f, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = PrimMesh(PrimitiveType.Quad);
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off;

            var trunk = U.Mat(new Color(0.33f, 0.24f, 0.15f), 0.1f);
            var leafA = U.Mat(new Color(0.18f, 0.36f, 0.13f), 0.1f);
            var leafB = U.Mat(new Color(0.26f, 0.42f, 0.16f), 0.1f);
            var cyl = PrimMesh(PrimitiveType.Cylinder); var sph = PrimMesh(PrimitiveType.Sphere);
            var treeMesh = Combine(cyl, Matrix4x4.TRS(new Vector3(0, 2f, 0), Quaternion.identity, new Vector3(0.45f, 2f, 0.45f)), sph, Matrix4x4.TRS(new Vector3(0, 6.2f, 0), Quaternion.identity, new Vector3(6.5f, 6f, 6.5f)));
            var pineMesh = Combine(cyl, Matrix4x4.TRS(new Vector3(0, 2f, 0), Quaternion.identity, new Vector3(0.5f, 2f, 0.5f)), sph, Matrix4x4.TRS(new Vector3(0, 8f, 0), Quaternion.identity, new Vector3(4.5f, 11f, 4.5f)));
            var leafy = new InstancedBatch { mesh = treeMesh, mats = new[] { trunk, leafA }, cull = 700f };
            var pines = new InstancedBatch { mesh = pineMesh, mats = new[] { trunk, leafB }, cull = 900f };
            batches.Add(leafy); batches.Add(pines);
            var rnd = new System.Random(21);
            int want = 9000, placed = 0, tries = 0;
            while (placed < want && tries < want * 6)
            {
                tries++;
                float x = -3000f + (float)rnd.NextDouble() * 6000f, z = -3200f + (float)rnd.NextDouble() * 6200f;
                var p = new V2(x, z);
                Zone zn = gen.ZoneAt(p);
                bool inside = gen.InsideHighway(p, 0f);
                if (inside && zn != Zone.Park && zn != Zone.Suburb && zn != Zone.Residential) continue;
                if (inside && zn != Zone.Park && rnd.NextDouble() < 0.7) continue;
                if (!inside && CityGen.Noise(x / 380f + 3f, z / 380f + 9f) < 0.48f) continue;
                if (NearRoad(x, z, 6f)) continue;
                float h = gen.H(x, z);
                if (h < gen.waterLevel + 1.5f) continue;
                if (NearBuilding(p)) continue;
                float y = Ground(x, z);
                float s = 0.8f + (float)rnd.NextDouble() * 0.6f;
                (rnd.NextDouble() < 0.5 ? leafy : pines).Add(new Vector3(x, y - 0.2f, z), Quaternion.Euler(0, (float)rnd.NextDouble() * 360f, 0), Vector3.one * s);
                placed++;
            }
        }

        static Mesh Combine(Mesh a, Matrix4x4 ma, Mesh b, Matrix4x4 mb)
        {
            var m = new Mesh();
            m.CombineMeshes(new[] { new CombineInstance { mesh = a, transform = ma }, new CombineInstance { mesh = b, transform = mb } }, false, true);
            return m;
        }

        bool NearRoad(float x, float z, float margin)
        {
            int cx = Mathf.FloorToInt(x / 40f), cz = Mathf.FloorToInt(z / 40f);
            for (int dx = -1; dx <= 1; dx++)
                for (int dz = -1; dz <= 1; dz++)
                {
                    List<Vector4> l;
                    if (!segHash.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out l)) continue;
                    foreach (var s in l)
                    {
                        float hw = s.w < 0f ? 15f : s.w;
                        if ((s.x - x) * (s.x - x) + (s.y - z) * (s.y - z) < (hw + margin) * (hw + margin)) return true;
                    }
                }
            return false;
        }

        bool NearBuilding(V2 p)
        {
            foreach (var b in gen.buildings)
            {
                var c = b.parts[0].c;
                if (Mathf.Abs(c.x - p.x) < 40f && Mathf.Abs(c.z - p.z) < 40f && V2.Dist(c, p) < Mathf.Max(b.parts[0].w, b.parts[0].d) * 0.75f + 3f) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ garaj, yarışlar, semtler
        void BuildMeta()
        {
            int gn = junctionNode[gen.garageNode];
            garagePos = graph.nodes[gn];
            Vector3 gd = new Vector3(gen.garageDir.x, 0, gen.garageDir.z);
            garageRot = gd.sqrMagnitude > 0.01f ? Quaternion.LookRotation(gd.normalized) : Quaternion.identity;
            var pad = U.Prim(PrimitiveType.Cylinder, "GarajIsareti", root, garagePos + Vector3.up * 0.06f, new Vector3(9, 0.02f, 9), U.Emissive(new Color(0.1f, 0.4f, 0.1f), new Color(0f, 0.8f, 0.3f)));
            pad.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            U.Icon(pad.transform, new Color(0f, 1f, 0.3f), 14f);
            U.Text3D("GARAJ", root, garagePos + Vector3.up * 9f, garageRot, 2.5f, new Color(1f, 0.6f, 0.1f));

            foreach (var rs in gen.races)
            {
                var def = new RaceDef { name = rs.name, type = (RaceType)rs.type, laps = rs.laps, prize = rs.prize };
                if (def.type == RaceType.Drag)
                {
                    Vector3 a = P3(rs.dragA, rs.dragYA) + Vector3.up * 0.05f, b = P3(rs.dragB, rs.dragYB) + Vector3.up * 0.05f;
                    if ((a - b).sqrMagnitude < 100f) continue;
                    def.route.Add(a); def.route.Add(b);
                    def.dragLanes = new[] { 2.8f, 6.4f, 10.0f };
                    races.Add(def);
                    continue;
                }
                var full = ExpandPath(rs.nodes, def.type == RaceType.Circuit);
                if (full.Count < 3) continue;
                def.route = RouteFromNodes(full, def.type == RaceType.Circuit);
                // özel noktaları (radar/gişe) yeni rota uzunluğuna ölçekle
                foreach (int sp in rs.special) def.special.Add(Mathf.Clamp(Mathf.RoundToInt(sp / (float)Mathf.Max(1, rs.nodes.Count) * full.Count), 1, full.Count - 2));
                races.Add(def);
            }
            foreach (var d in gen.districts) labels.Add(new KeyValuePair<string, Vector3>(d.Key, P3(d.Value, 0)));
            labels.Add(new KeyValuePair<string, Vector3>("Garaj", garagePos));
        }

        /// <summary>Kavşak düğüm listesini ara düğümlerle tam rotaya çevir.</summary>
        List<int> ExpandPath(List<int> junctions, bool loop)
        {
            var o = new List<int>();
            int count = junctions.Count + (loop ? 1 : 0);
            for (int k = 0; k < count - 1; k++)
            {
                int u = junctions[k % junctions.Count], v = junctions[(k + 1) % junctions.Count];
                int bestE = -1; float bl = float.MaxValue;
                foreach (int ei in gen.nodes[u].edges)
                {
                    var e = gen.edges[ei];
                    if ((e.a == u && e.b == v) || (e.b == u && e.a == v)) { float l = e.Length; if (l < bl) { bl = l; bestE = ei; } }
                }
                if (bestE < 0) continue;
                var nodesE = edgeNodes[bestE];
                bool fwd = gen.edges[bestE].a == u;
                for (int i = 0; i < nodesE.Count; i++)
                {
                    int id = fwd ? nodesE[i] : nodesE[nodesE.Count - 1 - i];
                    if (o.Count > 0 && o[o.Count - 1] == id) continue;
                    o.Add(id);
                }
            }
            if (loop && o.Count > 1 && o[o.Count - 1] == o[0]) o.RemoveAt(o.Count - 1);
            return o;
        }

        public override void SetNight(float night)
        {
            foreach (var m in windowMats) U.SetEmission(m, Color.white * (night > 0.35f ? night * 1.1f : 0f));
        }
    }

    /// <summary>Kendi şehrimiz için instanced çizimler (her kare).</summary>
    public class CityRuntime : MonoBehaviour
    {
        [System.NonSerialized] public OwnCity city;
        [System.NonSerialized] public List<InstancedBatch> batches;

        void Update()
        {
            var g = Game.I;
            if (g == null || g.cam == null || batches == null) return;
            int q = SaveSystem.Data != null ? SaveSystem.Data.quality : 1;
            float mul = q == 0 ? 0.6f : q == 2 ? 1f : 0.8f;
            Vector3 cp = g.cam.transform.position;
            foreach (var b in batches) b.Draw(cp, mul);
        }
    }
}
