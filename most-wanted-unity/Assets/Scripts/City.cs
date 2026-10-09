using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MostWanted
{
    /// <summary>Yol ağı: kavşak düğümleri ve bağlantıları.</summary>
    public class RoadGraph
    {
        public readonly List<Vector3> nodes = new List<Vector3>();
        public readonly List<List<int>> adj = new List<List<int>>();

        public int Add(Vector3 p) { nodes.Add(p); adj.Add(new List<int>()); return nodes.Count - 1; }

        public void Link(int a, int b)
        {
            if (a == b || adj[a].Contains(b)) return;
            adj[a].Add(b); adj[b].Add(a);
        }

        public int Nearest(Vector3 p)
        {
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < nodes.Count; i++)
            {
                float d = U.FlatDist(p, nodes[i]);
                if (d < bd) { bd = d; best = i; }
            }
            return best;
        }

        public List<int> Path(int from, int to)
        {
            var prev = new int[nodes.Count];
            for (int i = 0; i < prev.Length; i++) prev[i] = -2;
            var q = new Queue<int>();
            q.Enqueue(from); prev[from] = -1;
            while (q.Count > 0)
            {
                int c = q.Dequeue();
                if (c == to) break;
                foreach (int n in adj[c]) if (prev[n] == -2) { prev[n] = c; q.Enqueue(n); }
            }
            var path = new List<int>();
            if (prev[to] == -2) return path;
            for (int c = to; c != -1; c = prev[c]) path.Add(c);
            path.Reverse();
            return path;
        }

        public int RandomNodeAround(Vector3 p, float minD, float maxD)
        {
            var list = new List<int>();
            for (int i = 0; i < nodes.Count; i++)
            {
                float d = U.FlatDist(p, nodes[i]);
                if (d >= minD && d <= maxD) list.Add(i);
            }
            if (list.Count == 0) return Random.Range(0, nodes.Count);
            return list[Random.Range(0, list.Count)];
        }
    }

    /// <summary>Şehri prosedürel olarak kurar.</summary>
    public class City
    {
        public const int N = 6;            // blok sayısı (her eksende)
        public const float B = 100f;       // grid aralığı
        public const float RoadW = 18f;    // yol genişliği
        public const float Ring = 130f;    // çevre yolu uzaklığı
        public const float LaneOffset = 4.5f;

        public RoadGraph graph = new RoadGraph();
        public int[,] grid = new int[N + 1, N + 1];
        public List<int> ringLoop = new List<int>();
        public Transform root;
        public List<Material> buildingMats = new List<Material>();
        public List<Light> lampLights = new List<Light>();
        public Material lampHeadMat;
        public Vector3 garagePos;
        public Quaternion garageRot;

        Material roadMat, sidewalkMat, lineMat, grassMat, parkMat;
        System.Random rnd = new System.Random(1234);
        float R01() { return (float)rnd.NextDouble(); }

        public float Size { get { return N * B; } }

        public void Build()
        {
            root = new GameObject("Sehir").transform;
            roadMat = U.Mat(new Color(0.16f, 0.16f, 0.17f), 0.15f);
            sidewalkMat = U.Mat(new Color(0.55f, 0.55f, 0.53f), 0.1f);
            lineMat = U.Emissive(new Color(0.9f, 0.8f, 0.3f), new Color(0.15f, 0.12f, 0.02f));
            grassMat = U.Mat(new Color(0.22f, 0.36f, 0.16f), 0.05f);
            parkMat = U.Mat(new Color(0.25f, 0.45f, 0.2f), 0.05f);
            lampHeadMat = U.NewMat(new Color(1f, 0.95f, 0.8f));

            BuildBuildingMaterials();

            // Zemin
            var ground = U.Prim(PrimitiveType.Plane, "Zemin", root, new Vector3(Size / 2, 0, Size / 2), new Vector3(240, 1, 240), grassMat, true);
            ground.GetComponent<Renderer>().receiveShadows = true;

            BuildGraph();
            BuildRoads();
            BuildBlocks();
            BuildLamps();
            BuildOutskirts();
        }

        void BuildGraph()
        {
            for (int i = 0; i <= N; i++)
                for (int j = 0; j <= N; j++)
                    grid[i, j] = graph.Add(new Vector3(i * B, 0, j * B));
            for (int i = 0; i <= N; i++)
                for (int j = 0; j <= N; j++)
                {
                    if (i < N) graph.Link(grid[i, j], grid[i + 1, j]);
                    if (j < N) graph.Link(grid[i, j], grid[i, j + 1]);
                }

            // Çevre yolu (saat yönünün tersi)
            float lo = -Ring, hi = Size + Ring;
            var south = new int[N + 1]; var north = new int[N + 1]; var west = new int[N + 1]; var east = new int[N + 1];
            ringLoop.Add(graph.Add(new Vector3(lo, 0, lo)));
            for (int i = 0; i <= N; i++) { south[i] = graph.Add(new Vector3(i * B, 0, lo)); ringLoop.Add(south[i]); }
            ringLoop.Add(graph.Add(new Vector3(hi, 0, lo)));
            for (int j = 0; j <= N; j++) { east[j] = graph.Add(new Vector3(hi, 0, j * B)); ringLoop.Add(east[j]); }
            ringLoop.Add(graph.Add(new Vector3(hi, 0, hi)));
            for (int i = N; i >= 0; i--) { north[i] = graph.Add(new Vector3(i * B, 0, hi)); ringLoop.Add(north[i]); }
            ringLoop.Add(graph.Add(new Vector3(lo, 0, hi)));
            for (int j = N; j >= 0; j--) { west[j] = graph.Add(new Vector3(lo, 0, j * B)); ringLoop.Add(west[j]); }
            for (int k = 0; k < ringLoop.Count; k++) graph.Link(ringLoop[k], ringLoop[(k + 1) % ringLoop.Count]);

            for (int i = 0; i <= N; i += 3)
            {
                graph.Link(grid[i, 0], south[i]);
                graph.Link(grid[i, N], north[i]);
                graph.Link(grid[0, i], west[i]);
                graph.Link(grid[N, i], east[i]);
            }
        }

        void BuildRoads()
        {
            var roads = new GameObject("Yollar").transform;
            roads.SetParent(root, false);
            for (int a = 0; a < graph.nodes.Count; a++)
            {
                // kavşak karesi
                U.Prim(PrimitiveType.Cube, "Kavsak", roads, graph.nodes[a] + Vector3.up * 0.02f, new Vector3(RoadW, 0.04f, RoadW), roadMat);
                foreach (int b in graph.adj[a])
                {
                    if (b < a) continue;
                    Vector3 pa = graph.nodes[a], pb = graph.nodes[b];
                    Vector3 d = pb - pa;
                    float len = d.magnitude - RoadW;
                    if (len <= 0.1f) continue;
                    Vector3 mid = (pa + pb) * 0.5f;
                    var rot = Quaternion.LookRotation(d.normalized, Vector3.up);
                    var seg = U.Prim(PrimitiveType.Cube, "Yol", roads, mid + Vector3.up * 0.02f, new Vector3(RoadW, 0.04f, len), roadMat);
                    seg.transform.rotation = rot;
                    // orta şerit çizgisi (kesik)
                    int dashes = Mathf.FloorToInt(len / 8f);
                    for (int k = 0; k < dashes; k++)
                    {
                        float t = (k + 0.5f) / dashes - 0.5f;
                        var dash = U.Prim(PrimitiveType.Cube, "Serit", roads, mid + d.normalized * (t * len) + Vector3.up * 0.045f, new Vector3(0.3f, 0.02f, 3.5f), lineMat);
                        dash.transform.rotation = rot;
                        dash.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                    }
                }
            }
        }

        void BuildBuildingMaterials()
        {
            const int S = 256;
            var alb = new Texture2D(S, S, TextureFormat.RGBA32, true);
            var emi = new Texture2D(S, S, TextureFormat.RGBA32, true);
            var aCols = new Color[S * S];
            var eCols = new Color[S * S];
            int cells = 4, cs = S / cells;
            for (int cx = 0; cx < cells; cx++)
                for (int cy = 0; cy < cells; cy++)
                {
                    bool lit = R01() < 0.55f;
                    Color litCol = Color.Lerp(new Color(1f, 0.85f, 0.5f), new Color(0.7f, 0.85f, 1f), R01() * 0.6f);
                    for (int x = 0; x < cs; x++)
                        for (int y = 0; y < cs; y++)
                        {
                            int px = cx * cs + x, py = cy * cs + y;
                            bool win = x > cs * 0.18f && x < cs * 0.82f && y > cs * 0.25f && y < cs * 0.85f;
                            aCols[py * S + px] = win ? new Color(0.12f, 0.16f, 0.22f) : new Color(0.9f, 0.9f, 0.9f);
                            eCols[py * S + px] = (win && lit) ? litCol : Color.black;
                        }
                }
            alb.SetPixels(aCols); alb.Apply(true);
            emi.SetPixels(eCols); emi.Apply(true);
            alb.wrapMode = TextureWrapMode.Repeat; emi.wrapMode = TextureWrapMode.Repeat;

            Color[] tints = { new Color(0.75f, 0.72f, 0.68f), new Color(0.55f, 0.6f, 0.68f), new Color(0.7f, 0.55f, 0.45f), new Color(0.45f, 0.45f, 0.5f), new Color(0.8f, 0.78f, 0.7f) };
            foreach (var t in tints)
            {
                var m = U.NewMat(t, 0.35f, 0.1f);
                m.mainTexture = alb;
                if (m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", emi);
                U.SetEmission(m, Color.black);
                buildingMats.Add(m);
            }
        }

        void BuildBlocks()
        {
            var blocks = new GameObject("Bloklar").transform;
            blocks.SetParent(root, false);
            float bs = B - RoadW; // blok kenar uzunluğu
            var roofMat = U.Mat(new Color(0.3f, 0.3f, 0.32f));
            for (int i = 0; i < N; i++)
                for (int j = 0; j < N; j++)
                {
                    Vector3 c = new Vector3(i * B + B / 2, 0, j * B + B / 2);
                    U.Prim(PrimitiveType.Cube, "Kaldirim", blocks, c + Vector3.up * 0.1f, new Vector3(bs, 0.2f, bs), sidewalkMat, true);

                    bool garage = (i == 0 && j == 0);
                    bool park = !garage && (R01() < 0.12f);
                    if (park)
                    {
                        U.Prim(PrimitiveType.Cube, "Park", blocks, c + Vector3.up * 0.205f, new Vector3(bs - 6, 0.01f, bs - 6), parkMat);
                        for (int k = 0; k < 10; k++)
                            Tree(blocks, c + new Vector3((R01() - 0.5f) * (bs - 14), 0.2f, (R01() - 0.5f) * (bs - 14)));
                        continue;
                    }
                    if (garage)
                    {
                        BuildGarage(blocks, c, bs);
                        continue;
                    }
                    // 2x2 parsel
                    float lot = (bs - 8) / 2f;
                    for (int a = 0; a < 2; a++)
                        for (int b = 0; b < 2; b++)
                        {
                            Vector3 lc = c + new Vector3((a - 0.5f) * (lot + 1f), 0, (b - 0.5f) * (lot + 1f));
                            float dist = Vector3.Distance(c, new Vector3(Size / 2, 0, Size / 2));
                            float hMax = Mathf.Lerp(90f, 20f, Mathf.Clamp01(dist / (Size * 0.7f)));
                            float h = Mathf.Lerp(12f, hMax, R01());
                            float w = lot * Mathf.Lerp(0.75f, 0.98f, R01());
                            float d = lot * Mathf.Lerp(0.75f, 0.98f, R01());
                            Building(blocks, lc + Vector3.up * 0.2f, new Vector3(w, h, d), roofMat);
                        }
                }
        }

        void Building(Transform parent, Vector3 basePos, Vector3 size, Material roofMat)
        {
            var g = new GameObject("Bina");
            g.transform.SetParent(parent, false);
            g.transform.position = basePos + Vector3.up * size.y / 2;
            g.AddComponent<MeshFilter>().sharedMesh = U.BoxMesh(size, 16f);
            g.AddComponent<MeshRenderer>().sharedMaterial = buildingMats[rnd.Next(buildingMats.Count)];
            g.AddComponent<BoxCollider>();
            // çatı detayı
            if (R01() < 0.6f)
                U.Prim(PrimitiveType.Cube, "Cati", g.transform, new Vector3(0, size.y / 2 + 1.2f, 0),
                    new Vector3(size.x * 0.3f, 2.4f, size.z * 0.3f), roofMat);
        }

        void BuildGarage(Transform parent, Vector3 c, float bs)
        {
            var wall = U.Mat(new Color(0.25f, 0.25f, 0.28f));
            var g = new GameObject("Garaj").transform;
            g.SetParent(parent, false);
            g.position = c;
            // arka bina
            var b = U.Prim(PrimitiveType.Cube, "GarajBina", g, new Vector3(0, 6, 12), new Vector3(50, 12, 30), wall, true);
            var sign = U.Prim(PrimitiveType.Cube, "Tabela", g, new Vector3(0, 10, -3.2f), new Vector3(30, 3, 0.4f), U.Emissive(new Color(1f, 0.5f, 0f), new Color(2.5f, 1.1f, 0f)));
            sign.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            b.GetComponent<Renderer>().sharedMaterial = wall;
            // giriş platformu (bloğun güney kenarındaki yolun üzerinde)
            garagePos = new Vector3(c.x, 0, c.z - B / 2 + 0f);
            garageRot = Quaternion.Euler(0, 90, 0);
            var pad = U.Prim(PrimitiveType.Cylinder, "GarajIsareti", root, garagePos + Vector3.up * 0.06f, new Vector3(10, 0.02f, 10),
                U.Emissive(new Color(0.1f, 0.4f, 0.1f), new Color(0f, 0.9f, 0.3f)));
            pad.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            U.Icon(pad.transform, new Color(0f, 1f, 0.3f), 1.4f);
        }

        void Tree(Transform parent, Vector3 p)
        {
            var trunk = U.Mat(new Color(0.35f, 0.24f, 0.14f));
            var leaf = U.Mat(new Color(0.15f + R01() * 0.1f, 0.4f + R01() * 0.15f, 0.12f));
            float s = Mathf.Lerp(0.8f, 1.4f, R01());
            var t = U.Prim(PrimitiveType.Cylinder, "Agac", parent, p + Vector3.up * 1.5f * s, new Vector3(0.4f, 1.5f, 0.4f) * s, trunk, true);
            U.Prim(PrimitiveType.Sphere, "Yaprak", t.transform, new Vector3(0, 1.4f, 0), new Vector3(9f, 3.2f, 9f), leaf);
        }

        void BuildLamps()
        {
            var lamps = new GameObject("Lambalar").transform;
            lamps.SetParent(root, false);
            var pole = U.Mat(new Color(0.2f, 0.2f, 0.22f), 0.5f, 0.6f);
            for (int a = 0; a < graph.nodes.Count; a++)
            {
                bool inner = a < (N + 1) * (N + 1);
                Vector3 p = graph.nodes[a] + new Vector3(RoadW / 2 + 1.2f, 0, RoadW / 2 + 1.2f);
                var l = U.Prim(PrimitiveType.Cylinder, "Lamba", lamps, p + Vector3.up * 4f, new Vector3(0.25f, 4f, 0.25f), pole, true);
                var head = U.Prim(PrimitiveType.Cube, "LambaBas", lamps, p + new Vector3(-1.2f, 8f, -1.2f), new Vector3(1.2f, 0.25f, 1.2f), lampHeadMat);
                head.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                if (inner)
                {
                    var lg = new GameObject("LambaIsik");
                    lg.transform.SetParent(lamps, false);
                    lg.transform.position = p + new Vector3(-3f, 7.4f, -3f);
                    var li = lg.AddComponent<Light>();
                    li.type = LightType.Point;
                    li.range = 28f;
                    li.intensity = 1.6f;
                    li.color = new Color(1f, 0.8f, 0.55f);
                    li.shadows = LightShadows.None;
                    li.enabled = false;
                    lampLights.Add(li);
                }
                if (l == null) continue;
            }
        }

        void BuildOutskirts()
        {
            var o = new GameObject("Kenar").transform;
            o.SetParent(root, false);
            int placed = 0, tries = 0;
            while (placed < 260 && tries < 4000)
            {
                tries++;
                Vector3 p = new Vector3(Mathf.Lerp(-Ring - 160, Size + Ring + 160, R01()), 0, Mathf.Lerp(-Ring - 160, Size + Ring + 160, R01()));
                // şehir içi değil
                if (p.x > -15 && p.x < Size + 15 && p.z > -15 && p.z < Size + 15) continue;
                if (NearRoad(p, 14f)) continue;
                Tree(o, p);
                placed++;
            }
            // uzak tepeler
            var hill = U.Mat(new Color(0.2f, 0.3f, 0.18f));
            for (int k = 0; k < 24; k++)
            {
                float ang = k / 24f * Mathf.PI * 2;
                float r = Size / 2 + Ring + 320 + R01() * 80;
                Vector3 p = new Vector3(Size / 2 + Mathf.Cos(ang) * r, -10, Size / 2 + Mathf.Sin(ang) * r);
                U.Prim(PrimitiveType.Sphere, "Tepe", o, p, new Vector3(220, 90 + R01() * 80, 220), hill);
            }
        }

        public bool NearRoad(Vector3 p, float margin)
        {
            for (int a = 0; a < graph.nodes.Count; a++)
                foreach (int b in graph.adj[a])
                {
                    if (b < a) continue;
                    if (DistToSeg(p, graph.nodes[a], graph.nodes[b]) < RoadW / 2 + margin) return true;
                }
            return false;
        }

        static float DistToSeg(Vector3 p, Vector3 a, Vector3 b)
        {
            p.y = a.y = b.y = 0;
            Vector3 ab = b - a;
            float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / Mathf.Max(0.001f, ab.sqrMagnitude));
            return Vector3.Distance(p, a + ab * t);
        }

        /// <summary>Yön a→b için sağ şerit noktası.</summary>
        public static Vector3 LanePoint(Vector3 a, Vector3 b, Vector3 at, float offset)
        {
            Vector3 d = (b - a); d.y = 0; d.Normalize();
            Vector3 right = new Vector3(d.z, 0, -d.x);
            return at + right * offset;
        }

        public void SetNight(float night)
        {
            foreach (var m in buildingMats) U.SetEmission(m, Color.white * (night * 1.3f));
            U.SetEmission(lampHeadMat, new Color(1f, 0.85f, 0.6f) * (0.2f + night * 2.5f));
            bool on = night > 0.45f;
            foreach (var l in lampLights) if (l.enabled != on) l.enabled = on;
        }
    }
}
