using System.Collections.Generic;
using UnityEngine;

namespace MostWanted
{
    /// <summary>Yol ağı: düğümler ve bağlantılar (+ düğüm başına şerit ofseti).</summary>
    public class RoadGraph
    {
        public readonly List<Vector3> nodes = new List<Vector3>();
        public readonly List<List<int>> adj = new List<List<int>>();
        public readonly List<float> lane = new List<float>();

        public int Add(Vector3 p, float laneOffset = 4.5f) { nodes.Add(p); adj.Add(new List<int>()); lane.Add(laneOffset); return nodes.Count - 1; }

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
                float dx = p.x - nodes[i].x, dz = p.z - nodes[i].z;
                float d = dx * dx + dz * dz;
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
                if (adj[i].Count == 0) continue;
                float d = U.FlatDist(p, nodes[i]);
                if (d >= minD && d <= maxD) list.Add(i);
            }
            if (list.Count == 0) return Random.Range(0, nodes.Count);
            return list[Random.Range(0, list.Count)];
        }

        /// <summary>a→b yönünde sağ şerit noktası.</summary>
        public Vector3 LanePoint(int a, int b)
        {
            Vector3 d = nodes[b] - nodes[a]; d.y = 0;
            if (d.sqrMagnitude < 0.001f) return nodes[b];
            d.Normalize();
            float off = Mathf.Min(lane[a], lane[b]);
            return nodes[b] + new Vector3(d.z, 0, -d.x) * off;
        }
    }

    public enum RaceType { Sprint, Circuit, Speedtrap, Tollbooth, Drag }

    public class RaceDef
    {
        public string name;
        public RaceType type;
        public int laps = 1;
        public int prize;
        public List<Vector3> route = new List<Vector3>();   // şerit ofsetli rota noktaları
        public List<int> special = new List<int>();          // hız kameraları / gişeler (rota indeksleri)
        public float[] dragLanes;                            // drag: yanal şerit ofsetleri
    }

    public class HidingSpot
    {
        public Bounds bounds;
        public string name;
    }

    /// <summary>Oyun dünyası (prosedürel test şehri veya içe aktarılan harita).</summary>
    public abstract class World
    {
        public RoadGraph graph = new RoadGraph();
        public Transform root;
        public Vector3 garagePos;
        public Quaternion garageRot = Quaternion.identity;
        public List<HidingSpot> hiding = new List<HidingSpot>();
        public List<RaceDef> races = new List<RaceDef>();
        public Bounds area;
        public string title;
        public List<KeyValuePair<string, Vector3>> labels = new List<KeyValuePair<string, Vector3>>();

        public abstract void Build();
        public virtual void SetNight(float night) { }

        public bool InHiding(Vector3 p)
        {
            foreach (var h in hiding) if (h.bounds.Contains(p)) return true;
            return false;
        }

        /// <summary>Düğüm listesinden şerit ofsetli rota.</summary>
        public List<Vector3> RouteFromNodes(List<int> path, bool loop)
        {
            var r = new List<Vector3>();
            for (int i = 0; i < path.Count; i++)
            {
                int a = i > 0 ? path[i - 1] : (loop ? path[path.Count - 1] : -1);
                int b = path[i];
                if (a < 0) { int nx = path.Count > 1 ? path[1] : b; Vector3 d = U.Flat(graph.nodes[nx] - graph.nodes[b]).normalized; r.Add(graph.nodes[b] + new Vector3(d.z, 0, -d.x) * graph.lane[b]); }
                else r.Add(graph.LanePoint(a, b));
            }
            return r;
        }

        public List<int> Chain(params int[] corners)
        {
            var res = new List<int>();
            for (int k = 0; k < corners.Length - 1; k++)
            {
                var p = graph.Path(corners[k], corners[k + 1]);
                for (int i = (k == 0 ? 0 : 1); i < p.Count; i++) res.Add(p[i]);
            }
            return res;
        }

        public void AddHiding(string name, Vector3 center, Vector3 size)
        {
            hiding.Add(new HidingSpot { name = name, bounds = new Bounds(center, size) });
            var marker = new GameObject("Saklanma_" + name);
            marker.transform.position = center;
            U.Icon(marker.transform, new Color(0.2f, 0.6f, 1f), Mathf.Max(size.x, size.z) * 0.5f);
            if (root != null) marker.transform.SetParent(root, true);
        }
    }

    /// <summary>
    /// İçe aktarılan harita (ör. Assets/Maps/city_3d_model.glb). Ölçek/zemin düzeltme, MeshCollider,
    /// yol yüzeylerinden örnekleme ile navigasyon ağı.
    /// </summary>
    public class ImportedWorld : World
    {
        public GameObject prefab;
        public string credit;
        static readonly string[] RoadWords = { "road", "street", "asphalt", "yol", "lane", "highway", "pavement_road", "tarmac" };

        public ImportedWorld(GameObject p, string c) { prefab = p; credit = c; title = "İthal Harita"; }

        public override void Build()
        {
            root = new GameObject("IthalHarita").transform;
            var inst = Object.Instantiate(prefab, root);
            inst.name = prefab.name;
            Bounds b = RB(inst.transform);
            // birim kontrolü: 20 km'den büyükse cm kabul et
            if (Mathf.Max(b.size.x, b.size.z) > 20000f) { inst.transform.localScale *= 0.01f; b = RB(inst.transform); }
            else if (Mathf.Max(b.size.x, b.size.z) < 50f) { inst.transform.localScale *= 100f; b = RB(inst.transform); }
            inst.transform.position += new Vector3(-b.center.x, -b.min.y, -b.center.z);
            b = RB(inst.transform);
            area = b;

            var roadRenderers = new HashSet<Collider>();
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var mc = mf.GetComponent<MeshCollider>();
                if (mc == null) mc = mf.gameObject.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                mc.convex = false;
                mf.gameObject.isStatic = true;
                var r = mf.GetComponent<Renderer>();
                string n = mf.name.ToLowerInvariant();
                if (r != null && r.sharedMaterial != null) n += " " + r.sharedMaterial.name.ToLowerInvariant();
                foreach (var w in RoadWords) if (n.Contains(w)) { roadRenderers.Add(mc); break; }
            }
            StaticBatchingUtility.Combine(inst);

            // güvenlik zemini (haritanın altında)
            var safety = U.Prim(PrimitiveType.Plane, "GuvenlikZemini", root, new Vector3(0, -0.6f, 0), new Vector3(b.size.x / 10f + 50f, 1, b.size.z / 10f + 50f), U.Mat(new Color(0.25f, 0.3f, 0.22f)), true);
            safety.GetComponent<Renderer>().enabled = true;

            SampleRoads(b, roadRenderers);
            if (graph.nodes.Count < 10)
            {
                // yol bulunamadı: tüm düz alanlardan basit ızgara
                SampleRoads(b, null);
            }
            PickGarage();
            BuildRaces();
            labels.Add(new KeyValuePair<string, Vector3>(prefab.name, b.center));
        }

        static Bounds RB(Transform t)
        {
            bool has = false; Bounds b = new Bounds(t.position, Vector3.one);
            foreach (var r in t.GetComponentsInChildren<Renderer>()) { if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds); }
            return b;
        }

        void SampleRoads(Bounds b, HashSet<Collider> roads)
        {
            graph = new RoadGraph();
            float step = 14f;
            int nx = Mathf.Clamp(Mathf.CeilToInt(b.size.x / step), 1, 400);
            int nz = Mathf.Clamp(Mathf.CeilToInt(b.size.z / step), 1, 400);
            var idx = new int[nx, nz];
            float groundMax = b.min.y + Mathf.Max(3f, b.size.y * 0.05f);
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    idx[i, j] = -1;
                    Vector3 o = new Vector3(b.min.x + (i + 0.5f) * step, b.max.y + 5f, b.min.z + (j + 0.5f) * step);
                    RaycastHit h;
                    if (!Physics.Raycast(o, Vector3.down, out h, b.size.y + 20f)) continue;
                    if (h.normal.y < 0.96f) continue;
                    bool ok = roads != null && roads.Count > 0 ? roads.Contains(h.collider) : h.point.y < groundMax;
                    if (!ok) continue;
                    // aracın sığacağı boşluk
                    if (Physics.CheckSphere(h.point + Vector3.up * 1.6f, 1.2f)) continue;
                    idx[i, j] = graph.Add(h.point, roads != null && roads.Count > 0 ? 2.5f : 0f);
                }
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    if (idx[i, j] < 0) continue;
                    if (i + 1 < nx && idx[i + 1, j] >= 0) graph.Link(idx[i, j], idx[i + 1, j]);
                    if (j + 1 < nz && idx[i, j + 1] >= 0) graph.Link(idx[i, j], idx[i, j + 1]);
                }
        }

        void PickGarage()
        {
            int best = 0, deg = -1;
            Vector3 c = area.center;
            float bd = float.MaxValue;
            for (int i = 0; i < graph.nodes.Count; i++)
            {
                if (graph.adj[i].Count < 2) continue;
                float d = U.FlatDist(graph.nodes[i], c);
                if (d < bd) { bd = d; best = i; deg = graph.adj[i].Count; }
            }
            if (graph.nodes.Count == 0) { garagePos = area.center + Vector3.up * 2f; return; }
            garagePos = graph.nodes[best];
            int nb = graph.adj[best].Count > 0 ? graph.adj[best][0] : best;
            Vector3 d2 = U.Flat(graph.nodes[nb] - garagePos);
            garageRot = d2.sqrMagnitude > 0.01f ? Quaternion.LookRotation(d2.normalized) : Quaternion.identity;
            var pad = U.Prim(PrimitiveType.Cylinder, "GarajIsareti", root, garagePos + Vector3.up * 0.06f, new Vector3(10, 0.02f, 10), U.Emissive(new Color(0.1f, 0.4f, 0.1f), new Color(0f, 0.8f, 0.3f)));
            U.Icon(pad.transform, new Color(0f, 1f, 0.3f), 1.4f);
        }

        void BuildRaces()
        {
            int n = graph.nodes.Count;
            if (n < 20) return;
            var rnd = new System.Random(42);
            string[] names = { "Şehir Sprinti", "Bulvar Koşusu", "Liman Hattı", "Gece Turu", "Radar Avı", "Gişe Koşusu" };
            for (int k = 0; k < names.Length; k++)
            {
                int a = rnd.Next(n), bnode = -1; float bestD = 0f;
                for (int t = 0; t < 40; t++)
                {
                    int c = rnd.Next(n);
                    float d = U.FlatDist(graph.nodes[a], graph.nodes[c]);
                    if (d > bestD && d < 1500f) { var p = graph.Path(a, c); if (p.Count > 8) { bestD = d; bnode = c; } }
                }
                if (bnode < 0) continue;
                var path = graph.Path(a, bnode);
                var thin = new List<int>();
                for (int i = 0; i < path.Count; i += 2) thin.Add(path[i]);
                if (thin[thin.Count - 1] != path[path.Count - 1]) thin.Add(path[path.Count - 1]);
                var def = new RaceDef { name = names[k], prize = 3000 + k * 1000, route = RouteFromNodes(thin, false) };
                def.type = k == 4 ? RaceType.Speedtrap : k == 5 ? RaceType.Tollbooth : RaceType.Sprint;
                if (def.type == RaceType.Speedtrap)
                    for (int i = 1; i < 4; i++) def.special.Add(def.route.Count * i / 4);
                if (def.type == RaceType.Tollbooth)
                    for (int i = 1; i < 6; i++) def.special.Add(def.route.Count * i / 6);
                races.Add(def);
            }
        }
    }
}
