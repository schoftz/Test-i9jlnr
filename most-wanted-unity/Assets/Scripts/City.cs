using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MostWanted
{
    /// <summary>Şehri prosedürel olarak kurar.</summary>
    public class City : World
    {
        public const int N = 6;            // blok sayısı (her eksende)
        public const float B = 100f;       // grid aralığı
        public const float RoadW = 18f;    // yol genişliği
        public const float Ring = 130f;    // çevre yolu uzaklığı
        public const float LaneOffset = 4.5f;

        public int[,] grid = new int[N + 1, N + 1];
        public List<int> ringLoop = new List<int>();
        public List<Material> buildingMats = new List<Material>();
        public List<Light> lampLights = new List<Light>();
        public Material lampHeadMat;
        public List<Vector3> breakerSites = new List<Vector3>();
        public List<int> breakerKinds = new List<int>();

        Material roadMat, sidewalkMat, lineMat, grassMat, parkMat;
        System.Random rnd = new System.Random(1234);
        float R01() { return (float)rnd.NextDouble(); }

        public float Size { get { return N * B; } }

        public override void Build()
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
            BuildHidingAndTunnel();
            BuildRaces();
            title = "Test Şehri";
            area = new Bounds(new Vector3(Size / 2, 0, Size / 2), new Vector3(Size + Ring * 2 + 100, 50, Size + Ring * 2 + 100));
            labels.Add(new KeyValuePair<string, Vector3>("Merkez", new Vector3(Size / 2, 0, Size / 2)));
            labels.Add(new KeyValuePair<string, Vector3>("Garaj", garagePos));
            labels.Add(new KeyValuePair<string, Vector3>("Çevre Yolu", new Vector3(Size / 2, 0, -Ring)));
        }

        // --- Saklanma noktaları (otopark), tünel, pursuit breaker yerleri ---
        void BuildHidingAndTunnel()
        {
            var concrete = U.Mat(new Color(0.5f, 0.5f, 0.48f));
            // Otoparklar: (2,2) ve (4,4) blokları
            int[][] lots = { new[] { 2, 2 }, new[] { 4, 4 } };
            foreach (var l in lots)
            {
                Vector3 c = new Vector3(l[0] * B + B / 2, 0, l[1] * B + B / 2);
                float s = B - RoadW - 10f;
                var g = new GameObject("Otopark").transform; g.SetParent(root, false); g.position = c;
                U.Prim(PrimitiveType.Cube, "OtoparkTaban", g, new Vector3(0, 0.22f, 0), new Vector3(s + 8, 0.06f, s + 8), U.Mat(new Color(0.3f, 0.3f, 0.32f)));
                U.Prim(PrimitiveType.Cube, "OtoparkCati", g, new Vector3(0, 5.2f, 0), new Vector3(s, 0.6f, s), concrete, true);
                for (int x = -1; x <= 1; x++)
                    for (int z = -1; z <= 1; z++)
                    {
                        if (x == 0 && z == 0) continue;
                        U.Prim(PrimitiveType.Cube, "Kolon", g, new Vector3(x * s * 0.42f, 2.6f, z * s * 0.42f), new Vector3(0.8f, 5.2f, 0.8f), concrete, true);
                    }
                U.Text3D("OTOPARK", g, new Vector3(0, 6.2f, -s / 2 - 0.4f), Quaternion.identity, 3f, Color.white);
                AddHiding("Otopark", c + Vector3.up * 2.5f, new Vector3(s, 5f, s));
            }
            // Tünel: grid (3,5)-(3,6) arası yol
            Vector3 a = graph.nodes[grid[3, 5]], b = graph.nodes[grid[3, 6]];
            Vector3 mid = (a + b) / 2;
            float len = Vector3.Distance(a, b) - RoadW - 4f;
            var t = new GameObject("Tunel").transform; t.SetParent(root, false); t.position = mid;
            var wall = U.Mat(new Color(0.42f, 0.4f, 0.38f));
            U.Prim(PrimitiveType.Cube, "TunelSol", t, new Vector3(-RoadW / 2 - 0.6f, 4f, 0), new Vector3(1.2f, 8f, len), wall, true);
            U.Prim(PrimitiveType.Cube, "TunelSag", t, new Vector3(RoadW / 2 + 0.6f, 4f, 0), new Vector3(1.2f, 8f, len), wall, true);
            U.Prim(PrimitiveType.Cube, "TunelTavan", t, new Vector3(0, 8.5f, 0), new Vector3(RoadW + 3.6f, 1f, len), wall, true);
            U.Prim(PrimitiveType.Cube, "TunelToprak", t, new Vector3(0, 12f, 0), new Vector3(RoadW + 24f, 6f, len - 6f), U.Mat(new Color(0.25f, 0.38f, 0.2f)), true);
            var lampMat = U.Emissive(new Color(1f, 0.9f, 0.7f), new Color(2f, 1.7f, 1.2f));
            for (int k = -2; k <= 2; k++)
            {
                U.Prim(PrimitiveType.Cube, "TunelLamba", t, new Vector3(0, 7.95f, k * len / 5f), new Vector3(1.2f, 0.1f, 4f), lampMat);
                var lg = new GameObject("TunelIsik"); lg.transform.SetParent(t, false); lg.transform.localPosition = new Vector3(0, 7f, k * len / 5f);
                var li = lg.AddComponent<Light>(); li.type = LightType.Point; li.range = 22f; li.intensity = 2f; li.color = new Color(1f, 0.85f, 0.6f);
            }
            U.Text3D("TÜNEL", t, new Vector3(0, 10f, -len / 2 - 0.2f), Quaternion.identity, 3f, Color.white);
            AddHiding("Tünel", mid + Vector3.up * 4f, new Vector3(RoadW, 8f, len));

            // Pursuit breaker yerleri: blok köşeleri (yol kenarı), 0 = su kulesi, 1 = benzinlik tentesi
            int[][] br = { new[] { 1, 3 }, new[] { 5, 2 }, new[] { 3, 1 }, new[] { 2, 5 }, new[] { 5, 5 } };
            for (int k = 0; k < br.Length; k++)
            {
                Vector3 n = graph.nodes[grid[br[k][0], br[k][1]]];
                breakerSites.Add(n + new Vector3(RoadW / 2 + 7f, 0.2f, RoadW / 2 + 7f));
                breakerKinds.Add(k % 2);
            }
        }

        void BuildRaces()
        {
            var G = grid;
            races.Add(new RaceDef { name = "Liman Sprinti", type = RaceType.Sprint, prize = 3000, route = RouteFromNodes(Chain(G[0, 1], G[6, 1], G[6, 4], G[3, 4], G[3, 6]), false) });
            races.Add(new RaceDef { name = "Merkez Turu", type = RaceType.Circuit, laps = 2, prize = 4500, route = RouteFromNodes(Loop(G[1, 1], G[4, 1], G[4, 4], G[1, 4]), true) });
            races.Add(new RaceDef { name = "Çevre Yolu Kupası", type = RaceType.Circuit, laps = 1, prize = 8000, route = RouteFromNodes(new List<int>(ringLoop), true) });
            var trap = new RaceDef { name = "Radar Avı", type = RaceType.Speedtrap, prize = 5000, route = RouteFromNodes(Chain(G[0, 5], G[6, 5], G[6, 2], G[0, 2]), false) };
            trap.special.Add(3); trap.special.Add(8); trap.special.Add(14);
            races.Add(trap);
            var toll = new RaceDef { name = "Gişe Koşusu", type = RaceType.Tollbooth, prize = 4000, route = RouteFromNodes(Chain(G[5, 0], G[5, 6], G[2, 6], G[2, 0]), false) };
            for (int i = 3; i < toll.route.Count; i += 3) toll.special.Add(i);
            races.Add(toll);
            // Drag: güney çevre yolu düz hattı (4 şerit)
            var drag = new RaceDef { name = "Çevre Yolu Dragı", type = RaceType.Drag, prize = 3500 };
            drag.route.Add(new Vector3(-Ring + 20f, 0, -Ring));
            drag.route.Add(new Vector3(Size + Ring - 40f, 0, -Ring));
            drag.dragLanes = new[] { -6.75f, -2.25f, 2.25f, 6.75f };
            races.Add(drag);
            races.Add(new RaceDef { name = "Gece Ekspresi", type = RaceType.Sprint, prize = 5500, route = RouteFromNodes(Chain(G[6, 6], G[6, 3], G[2, 3], G[2, 0], G[0, 0]), false) });
        }

        List<int> Loop(params int[] corners)
        {
            var c = new int[corners.Length + 1];
            for (int i = 0; i < corners.Length; i++) c[i] = corners[i];
            c[corners.Length] = corners[0];
            var l = Chain(c);
            l.RemoveAt(l.Count - 1);
            return l;
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
                    bool isLot = (i == 2 && j == 2) || (i == 4 && j == 4);
                    if (isLot) continue;
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

        public static Vector3 LanePoint(Vector3 a, Vector3 b, Vector3 at, float offset)
        {
            Vector3 d = (b - a); d.y = 0; d.Normalize();
            Vector3 right = new Vector3(d.z, 0, -d.x);
            return at + right * offset;
        }

        public override void SetNight(float night)
        {
            foreach (var m in buildingMats) U.SetEmission(m, Color.white * (night * 1.3f));
            U.SetEmission(lampHeadMat, new Color(1f, 0.85f, 0.6f) * (0.2f + night * 2.5f));
        }

        /// <summary>Gece sadece oyuncuya yakın (≤160 m) sokak lambası ışıkları açık.</summary>
        public void UpdateLampsNear(Vector3 p, float night)
        {
            bool nightOn = night > 0.45f;
            foreach (var l in lampLights)
            {
                bool on = nightOn && U.FlatDist(l.transform.position, p) < 160f;
                if (l.enabled != on) l.enabled = on;
            }
        }

        /// <summary>Ağaç/lamba gibi detayları "Detay" katmanına taşı (kamera katman mesafesiyle erken kesilir).</summary>
        public void MarkDetailLayers()
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>())
            {
                string n = r.name;
                if (n == "Agac" || n == "Yaprak" || n == "Lamba" || n == "LambaBas" || n == "Serit") r.gameObject.layer = OptimizationManager.DetailLayer;
            }
        }
    }
}
