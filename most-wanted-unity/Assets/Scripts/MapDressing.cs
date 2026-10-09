using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MostWanted
{
    /// <summary>
    /// İthal haritayı süsler (pişmiş yol ağına göre, prosedürel):
    ///  - Çevre: arazi eteği (tepeler, tarlalar, dağlar), deniz + nehir (su shader'ı), kumsal, ormanlar
    ///  - Şehir dışından dolaşan 3+3 şeritli otoyol (bariyer, aydınlatma, tabelalar, köprü) — yol ağına eklenir
    ///  - Sokak donatısı: lamba, trafik ışığı (çalışan döngü, trafik uyar), tabela, durak, bank, çöp kutusu,
    ///    yangın musluğu, park etmiş araçlar, bulvar ağaçları, reklam panoları, şantiye bariyerleri, pursuit breaker
    ///  - Yaya geçidi, rögar, ıslak zemin modu, bulut kubbesi, kuşlar/uçak, şehir ambiyans sesi, semt isimleri
    /// Her şey GPU instancing ile çizilir ve kalite ön ayarına göre sayı/mesafe ölçeklenir.
    /// </summary>
    public class MapDressing : MonoBehaviour
    {
        public static MapDressing I;

        // ------------------------------------------------------------------ instanced çizim
        class ISet
        {
            public Mesh mesh; public Material[] mats; public int layer; public ShadowCastingMode shadows = ShadowCastingMode.On;
            public float cull = 400f;
            readonly Dictionary<long, List<Matrix4x4>> cells = new Dictionary<long, List<Matrix4x4>>();
            readonly List<KeyValuePair<Vector3, Matrix4x4[]>> baked = new List<KeyValuePair<Vector3, Matrix4x4[]>>();
            public int Count;
            public bool hidden;
            // zemine oturtma: yol ağı yüksekliği yerine gerçek yüzeye ışın
            public MapDressing snapOwner; public bool align; public float lift;
            public void Add(Vector3 p, Quaternion r, Vector3 s)
            {
                if (snapOwner != null)
                {
                    RaycastHit h;
                    if (!snapOwner.GroundHit(p, out h)) return;
                    p.y = h.point.y + lift;
                    if (align) r = Quaternion.FromToRotation(Vector3.up, h.normal) * r;
                }
                long k = ((long)Mathf.FloorToInt(p.x / 250f) << 32) ^ (uint)Mathf.FloorToInt(p.z / 250f);
                List<Matrix4x4> l;
                if (!cells.TryGetValue(k, out l)) cells[k] = l = new List<Matrix4x4>();
                l.Add(Matrix4x4.TRS(p, r, s));
                Count++;
            }
            public void Bake()
            {
                baked.Clear();
                foreach (var kv in cells)
                {
                    var l = kv.Value;
                    for (int i = 0; i < l.Count; i += 1000)
                    {
                        int n = Mathf.Min(1000, l.Count - i);
                        var arr = l.GetRange(i, n).ToArray();
                        Vector3 c = Vector3.zero;
                        foreach (var m in arr) c += (Vector3)m.GetColumn(3);
                        baked.Add(new KeyValuePair<Vector3, Matrix4x4[]>(c / n, arr));
                    }
                }
                cells.Clear();
            }
            public void Draw(Vector3 cam, float cullMul)
            {
                if (mesh == null || hidden) return;
                float cd = cull * cullMul;
                for (int b = 0; b < baked.Count; b++)
                {
                    var kv = baked[b];
                    if (U.FlatDist(kv.Key, cam) > cd + 180f) continue;
                    for (int s = 0; s < mats.Length && s < mesh.subMeshCount; s++)
                    {
                        var rp = new RenderParams(mats[s]) { shadowCastingMode = shadows, receiveShadows = true, layer = layer, worldBounds = new Bounds(kv.Key, new Vector3(400f, 400f, 400f)) };
                        Graphics.RenderMeshInstanced(rp, mesh, s, kv.Value);
                    }
                }
            }
        }

        readonly List<ISet> sets = new List<ISet>();
        ISet Set(Mesh m, Material[] mats, float cull, bool shadows = true, int layer = OptimizationManager.DetailLayer)
        {
            var s = new ISet { mesh = m, mats = mats, cull = cull, layer = layer, shadows = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off };
            sets.Add(s);
            return s;
        }

        // ------------------------------------------------------------------ durum
        World w;
        RoadGraph g;
        public Bounds city;
        public float waterLevel;
        float seaX, riverZ;
        int quality;
        System.Random rnd = new System.Random(77);
        float R01() { return (float)rnd.NextDouble(); }
        Transform root;
        readonly List<Vector3> hw = new List<Vector3>();     // otoyol merkez hattı
        readonly List<bool> hwBridge = new List<bool>();
        readonly Dictionary<long, List<int>> nodeHash = new Dictionary<long, List<int>>();
        readonly Dictionary<int, float> lightPhase = new Dictionary<int, float>();
        readonly Dictionary<int, Vector3> lightAxis = new Dictionary<int, Vector3>();
        readonly List<int> lightNodes = new List<int>();
        readonly List<Vector3> lampPositions = new List<Vector3>();
        readonly List<Light> lampPool = new List<Light>();
        Material lampHead, tlRed, tlYellow, tlGreen, tlOff;
        Mesh sphere, cube, cylinder, quad;
        Transform clouds;
        public bool wet;
        readonly List<Material> streetMats = new List<Material>();
        readonly List<float> streetSmooth = new List<float>();
        readonly List<Material> facadeMats = new List<Material>();
        ISet puddles;
        ReflectionProbe probe;
        public readonly List<KeyValuePair<string, Vector3>> districts = new List<KeyValuePair<string, Vector3>>();

        // park etmiş araç havuzu
        readonly List<KeyValuePair<Vector3, Quaternion>> parkSpots = new List<KeyValuePair<Vector3, Quaternion>>();
        readonly List<GameObject> parkPool = new List<GameObject>();
        readonly List<int> parkAssigned = new List<int>();
        float parkTimer, lampTimer;

        // yaşam
        readonly List<Vector3> flockCenters = new List<Vector3>();
        ISet birdSet;
        Transform plane;
        Light planeLight;
        float planeT = -1f;
        AudioSource ambience;
        float sirenTimer = 20f;

        public static MapDressing Build(World world, int q)
        {
            var go = new GameObject("HaritaSusleme");
            var d = go.AddComponent<MapDressing>();
            I = d;
            d.w = world; d.g = world.graph; d.quality = Mathf.Clamp(q == 3 ? 1 : q, 0, 2);
            d.root = go.transform;
            if (world.root != null) go.transform.SetParent(world.root, true);
            d.Init();
            return d;
        }

        static Mesh PrimMesh(PrimitiveType t)
        {
            var go = GameObject.CreatePrimitive(t);
            var m = go.GetComponent<MeshFilter>().sharedMesh;
            Destroy(go);
            return m;
        }

        static Mesh Combine(params KeyValuePair<Mesh, Matrix4x4>[] parts)
        {
            var ci = new CombineInstance[parts.Length];
            for (int i = 0; i < parts.Length; i++) ci[i] = new CombineInstance { mesh = parts[i].Key, transform = parts[i].Value };
            var m = new Mesh();
            m.CombineMeshes(ci, false, true);
            return m;
        }

        static KeyValuePair<Mesh, Matrix4x4> P(Mesh m, Vector3 pos, Vector3 scale, Vector3 euler = default(Vector3))
        {
            return new KeyValuePair<Mesh, Matrix4x4>(m, Matrix4x4.TRS(pos, Quaternion.Euler(euler), scale));
        }

        void Init()
        {
            Physics.SyncTransforms(); // harita çarpıştırıcıları ışınlar için hazır olsun
            sphere = PrimMesh(PrimitiveType.Sphere); cube = PrimMesh(PrimitiveType.Cube);
            cylinder = PrimMesh(PrimitiveType.Cylinder); quad = PrimMesh(PrimitiveType.Quad);

            // şehir sınırları
            city = new Bounds(g.nodes[0], Vector3.zero);
            float minY = float.MaxValue;
            for (int i = 0; i < g.nodes.Count; i++)
            {
                city.Encapsulate(g.nodes[i]);
                minY = Mathf.Min(minY, g.nodes[i].y);
                long k = HashKey(g.nodes[i]);
                List<int> l; if (!nodeHash.TryGetValue(k, out l)) nodeHash[k] = l = new List<int>(); l.Add(i);
            }
            waterLevel = minY - 4f;
            seaX = city.max.x + 1100f;
            riverZ = city.min.z + 650f;

            BuildHighwayPath();
            BuildTerrain();
            BuildWater();
            BuildHighwayGeometry();
            BuildForests();
            BuildStreetDressing();
            BuildDistricts();
            BuildSky();
            BuildLife();
            CollectMaterials();
            foreach (var s in sets) s.Bake();
            Debug.Log("[MW] Harita süsleme hazır: " + sets.Count + " instanced set, otoyol " + hw.Count + " nokta, trafik ışığı " + lightNodes.Count);
        }

        static int GroundMask { get { return ~((1 << OptimizationManager.TrafficLayer) | (1 << U.IconLayer) | (1 << OptimizationManager.DetailLayer)); } }

        /// <summary>p'nin altındaki gerçek zemin (araç/ikon/detay katmanları ve tetikleyiciler hariç), ±5 m içinde.</summary>
        public bool GroundHit(Vector3 p, out RaycastHit h)
        {
            if (Physics.Raycast(p + Vector3.up * 4f, Vector3.down, out h, 12f, GroundMask, QueryTriggerInteraction.Ignore) && h.rigidbody == null)
                return Mathf.Abs(h.point.y - p.y) < 5f;
            return false;
        }

        /// <summary>Tekil objeleri zemine oturt; zemin yoksa false.</summary>
        bool SnapPos(ref Vector3 p)
        {
            RaycastHit h;
            if (!GroundHit(p, out h)) return false;
            p.y = h.point.y;
            return true;
        }

        void SnapSet(ISet set, bool align, float lift) { set.snapOwner = this; set.align = align; set.lift = lift; }

        static long HashKey(Vector3 p) { return ((long)Mathf.FloorToInt(p.x / 100f) << 32) ^ (uint)Mathf.FloorToInt(p.z / 100f); }

        /// <summary>En yakın yol düğümüne mesafe (300 m'ye kadar) ve o düğümün yüksekliği.</summary>
        float NearestNode(Vector3 p, out float y)
        {
            y = 0f; float best = float.MaxValue;
            int cx = Mathf.FloorToInt(p.x / 100f), cz = Mathf.FloorToInt(p.z / 100f);
            for (int dx = -3; dx <= 3; dx++)
                for (int dz = -3; dz <= 3; dz++)
                {
                    List<int> l;
                    if (!nodeHash.TryGetValue(((long)(cx + dx) << 32) ^ (uint)(cz + dz), out l)) continue;
                    foreach (int i in l)
                    {
                        float d = U.FlatDist(p, g.nodes[i]);
                        if (d < best) { best = d; y = g.nodes[i].y; }
                    }
                }
            return best;
        }

        // ------------------------------------------------------------------ arazi
        static float Fbm(float x, float z)
        {
            float s = 0f, a = 1f, f = 1f / 900f;
            for (int i = 0; i < 4; i++) { s += (Mathf.PerlinNoise(x * f + 31.7f, z * f + 11.3f) - 0.5f) * a; a *= 0.5f; f *= 2.1f; }
            return s;
        }

        float DistToCity(Vector3 p)
        {
            float dx = Mathf.Max(0f, Mathf.Max(city.min.x - p.x, p.x - city.max.x));
            float dz = Mathf.Max(0f, Mathf.Max(city.min.z - p.z, p.z - city.max.z));
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        float SeaEdge(float z) { return seaX + 220f * Mathf.Sin(z / 900f); }

        /// <summary>Doğal arazi yüksekliği (otoyol düzleştirmesi hariç).</summary>
        float Natural(Vector3 p)
        {
            float dc = DistToCity(p);
            float baseY = waterLevel + 8f;
            float amp = 18f + Mathf.Clamp01((dc - 300f) / 2500f) * 140f;
            float h = baseY + Fbm(p.x, p.z) * amp * 2f + Mathf.Clamp01((dc - 2600f) / 1500f) * 260f * (0.6f + 0.4f * Mathf.PerlinNoise(p.x / 700f, p.z / 700f));
            // şehre yakın: kenar yüksekliğine yumuşak geçiş
            float ny;
            float dn = NearestNode(p, out ny);
            if (dn < 300f)
            {
                float t = Mathf.Clamp01((dn - 40f) / 260f);
                h = Mathf.Lerp(ny - 2.5f, h, t * t * (3f - 2f * t));
            }
            // deniz
            float se = SeaEdge(p.z);
            if (p.x > se - 150f) h = Mathf.Lerp(h, waterLevel - 14f, Mathf.Clamp01((p.x - (se - 150f)) / 250f));
            // nehir (denize akar)
            if (p.x > city.max.x + 120f)
            {
                float rz = riverZ + 60f * Mathf.Sin(p.x / 230f);
                float dr = Mathf.Abs(p.z - rz);
                if (dr < 110f) h = Mathf.Lerp(waterLevel - 6f, h, Mathf.Clamp01((dr - 45f) / 65f));
            }
            return h;
        }

        float hwY(int i) { return hw[i].y; }

        float Final(Vector3 p, out bool nearHighway)
        {
            float h = Natural(p);
            nearHighway = false;
            // otoyol düzleştirme (köprü bölümleri hariç)
            float best = float.MaxValue; int bi = -1;
            for (int i = 0; i < hw.Count; i += 4)
            {
                float d = U.FlatDist(p, hw[i]);
                if (d < best) { best = d; bi = i; }
            }
            if (bi >= 0 && best < 70f && !hwBridge[bi])
            {
                float t = Mathf.Clamp01((best - 18f) / 50f);
                h = Mathf.Lerp(hwY(bi) - 0.4f, h, t * t * (3f - 2f * t));
                nearHighway = best < 25f;
            }
            return h;
        }

        void BuildTerrain()
        {
            float size = 9000f;
            Vector3 c = city.center;
            float x0 = c.x - size / 2, z0 = c.z - size / 2;
            int chunks = 6, res = quality == 0 ? 24 : 36;
            float cs = size / chunks, step = cs / res;
            var tex = GroundTexture(x0, z0, size);
            var mat = U.NewMat(Color.white, 0.08f, 0f);
            U.SetMainTex(mat, tex);
            for (int cx = 0; cx < chunks; cx++)
                for (int cz = 0; cz < chunks; cz++)
                {
                    var verts = new Vector3[(res + 1) * (res + 1)];
                    var uvs = new Vector2[verts.Length];
                    for (int i = 0; i <= res; i++)
                        for (int j = 0; j <= res; j++)
                        {
                            float x = x0 + cx * cs + i * step, z = z0 + cz * cs + j * step;
                            bool nh;
                            var p = new Vector3(x, 0, z);
                            float y = Final(p, out nh);
                            // şehir ayak izi içinde arazi aşağıda kalsın
                            float ny; float dn = NearestNode(p, out ny);
                            if (dn < 35f) y = Mathf.Min(y, ny - 3f);
                            verts[i * (res + 1) + j] = new Vector3(x, y, z);
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
                    var go = new GameObject("Arazi_" + cx + "_" + cz);
                    go.transform.SetParent(root, false);
                    go.AddComponent<MeshFilter>().sharedMesh = m;
                    var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = mat; r.shadowCastingMode = ShadowCastingMode.Off;
                    go.AddComponent<MeshCollider>().sharedMesh = m;
                    go.isStatic = true;
                }
        }

        Texture2D GroundTexture(float x0, float z0, float size)
        {
            int n = quality == 0 ? 256 : 512;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true);
            var cols = new Color[n * n];
            Color grass = new Color(0.30f, 0.42f, 0.20f), dry = new Color(0.55f, 0.50f, 0.30f), field1 = new Color(0.62f, 0.56f, 0.28f),
                  field2 = new Color(0.36f, 0.50f, 0.22f), sand = new Color(0.82f, 0.76f, 0.58f), rock = new Color(0.45f, 0.43f, 0.40f);
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    float x = x0 + (i + 0.5f) / n * size, z = z0 + (j + 0.5f) / n * size;
                    var p = new Vector3(x, 0, z);
                    float h = Natural(p);
                    float dc = DistToCity(p);
                    Color c = Color.Lerp(grass, dry, Mathf.PerlinNoise(x / 600f, z / 600f) * 0.6f);
                    // tarlalar: ızgara parselleri
                    if (dc > 250f && dc < 2600f)
                    {
                        int fx = Mathf.FloorToInt(x / 170f), fz = Mathf.FloorToInt(z / 120f);
                        float hsh = Mathf.Repeat(Mathf.Sin(fx * 12.9898f + fz * 78.233f) * 43758.55f, 1f);
                        if (hsh < 0.35f) c = Color.Lerp(field1, field2, Mathf.Repeat(hsh * 7f, 1f));
                    }
                    if (h > waterLevel + 140f) c = Color.Lerp(c, rock, Mathf.Clamp01((h - waterLevel - 140f) / 80f));
                    if (h < waterLevel + 2.5f) c = sand;
                    cols[j * n + i] = c;
                }
            tex.SetPixels(cols); tex.Apply(true);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.anisoLevel = 4;
            return tex;
        }

        void BuildWater()
        {
            var mat = Resources.Load<Material>("MW_WaterMat");
            Material m = mat != null && mat.shader != null && mat.shader.isSupported ? new Material(mat) : U.NewMat(new Color(0.08f, 0.3f, 0.4f), 0.95f, 0f);
            var go = new GameObject("Su");
            go.transform.SetParent(root, false);
            go.transform.position = new Vector3(city.center.x + 1500f, waterLevel, city.center.z);
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            go.transform.rotation = Quaternion.Euler(90, 0, 0);
            go.transform.localScale = new Vector3(14000f, 14000f, 1f);
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = m; r.shadowCastingMode = ShadowCastingMode.Off;
        }

        // ------------------------------------------------------------------ otoyol
        int southNode = -1, eastNode = -1;

        void BuildHighwayPath()
        {
            float minZ = float.MaxValue, maxX = float.MinValue;
            for (int i = 0; i < g.nodes.Count; i++)
            {
                if (g.adj[i].Count != 1) continue;
                if (g.nodes[i].z < minZ) { minZ = g.nodes[i].z; southNode = i; }
                if (g.nodes[i].x > maxX && Mathf.Abs(g.nodes[i].z - city.center.z) < city.extents.z * 0.6f) { maxX = g.nodes[i].x; eastNode = i; }
            }
            if (southNode < 0 || eastNode < 0) return;
            Vector3 S = g.nodes[southNode], E = g.nodes[eastNode];
            Vector3 sDir = U.Flat(S - g.nodes[g.adj[southNode][0]]).normalized;
            Vector3 eDir = U.Flat(E - g.nodes[g.adj[eastNode][0]]).normalized;
            var ctrl = new List<Vector3>
            {
                S - sDir * 40f, S, S + sDir * 220f,
                S + sDir * 420f + new Vector3(450f, 0, 0),
                new Vector3(city.max.x + 650f, 0, S.z - 150f),
                new Vector3(city.max.x + 850f, 0, riverZ - 350f),
                new Vector3(city.max.x + 850f, 0, riverZ + 350f),
                new Vector3(city.max.x + 700f, 0, E.z - 700f),
                E + eDir * 450f + new Vector3(0, 0, -150f),
                E + eDir * 200f, E, E - eDir * 40f
            };
            // Catmull-Rom
            var pts = new List<Vector3>();
            for (int i = 1; i < ctrl.Count - 2; i++)
            {
                Vector3 p0 = ctrl[i - 1], p1 = ctrl[i], p2 = ctrl[i + 1], p3 = ctrl[i + 2];
                float len = Vector3.Distance(p1, p2);
                int n = Mathf.Max(2, Mathf.CeilToInt(len / 8f));
                for (int k = 0; k < n; k++)
                {
                    float t = k / (float)n, t2 = t * t, t3 = t2 * t;
                    pts.Add(0.5f * (2f * p1 + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3));
                }
            }
            pts.Add(ctrl[ctrl.Count - 2]);
            // yükseklikler: doğal arazi, yumuşatılmış; köprüde su üstünde
            var raw = new float[pts.Count];
            for (int i = 0; i < pts.Count; i++) raw[i] = Natural(pts[i]);
            var sm = new float[pts.Count];
            for (int i = 0; i < pts.Count; i++)
            {
                float s = 0; int c = 0;
                for (int k = -20; k <= 20; k++) { int j = Mathf.Clamp(i + k, 0, pts.Count - 1); s += raw[j]; c++; }
                sm[i] = Mathf.Max(s / c, waterLevel + 3f);
            }
            float totalLen = 0f;
            for (int i = 1; i < pts.Count; i++) totalLen += U.FlatDist(pts[i - 1], pts[i]);
            float acc = 0f;
            for (int i = 0; i < pts.Count; i++)
            {
                if (i > 0) acc += U.FlatDist(pts[i - 1], pts[i]);
                bool bridge = raw[i] < waterLevel + 1.5f;
                float y = bridge ? Mathf.Max(sm[i], waterLevel + 14f) : sm[i];
                // uçlarda şehir yoluna bağlan
                float fs = Mathf.Clamp01(acc / 180f), fe = Mathf.Clamp01((totalLen - acc) / 180f);
                y = Mathf.Lerp(S.y, y, fs * fs * (3 - 2 * fs));
                y = Mathf.Lerp(E.y, y, fe * fe * (3 - 2 * fe));
                hw.Add(new Vector3(pts[i].x, y, pts[i].z));
                hwBridge.Add(bridge);
            }
            // köprü rampaları: köprü öncesi/sonrası 25 noktada yüksekliği yumuşat
            var ys = new float[hw.Count];
            for (int i = 0; i < hw.Count; i++)
            {
                float s = 0; int c = 0;
                for (int k = -25; k <= 25; k++) { int j = Mathf.Clamp(i + k, 0, hw.Count - 1); s += hw[j].y; c++; }
                ys[i] = hwBridge[i] ? Mathf.Max(hw[i].y, s / c) : s / c;
            }
            for (int i = 0; i < hw.Count; i++)
            {
                float fs = Mathf.Clamp01(i / 22f), fe = Mathf.Clamp01((hw.Count - 1 - i) / 22f);
                float y = Mathf.Lerp(S.y, Mathf.Lerp(E.y, ys[i], fe), fs);
                hw[i] = new Vector3(hw[i].x, y, hw[i].z);
            }
        }

        void BuildHighwayGeometry()
        {
            if (hw.Count < 10) return;
            const float W = 26f;
            // yol şeridi meshi
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();
            float v = 0f;
            for (int i = 0; i < hw.Count; i++)
            {
                Vector3 d = U.Flat(hw[Mathf.Min(i + 1, hw.Count - 1)] - hw[Mathf.Max(i - 1, 0)]).normalized;
                Vector3 r = new Vector3(d.z, 0, -d.x);
                if (i > 0) v += U.FlatDist(hw[i - 1], hw[i]) / 12f;
                verts.Add(hw[i] - r * W / 2 + Vector3.up * 0.06f); uvs.Add(new Vector2(0, v));
                verts.Add(hw[i] + r * W / 2 + Vector3.up * 0.06f); uvs.Add(new Vector2(1, v));
                if (i > 0) { int b = i * 2; tris.AddRange(new[] { b - 2, b, b - 1, b - 1, b, b + 1 }); }
            }
            var m = new Mesh { indexFormat = IndexFormat.UInt32 };
            m.SetVertices(verts); m.SetUVs(0, uvs); m.SetTriangles(tris, 0); m.RecalculateNormals(); m.RecalculateBounds();
            var road = new GameObject("Otoyol");
            road.transform.SetParent(root, false);
            road.AddComponent<MeshFilter>().sharedMesh = m;
            var mat = U.NewMat(Color.white, 0.25f, 0f);
            U.SetMainTex(mat, HighwayTexture());
            var rr = road.AddComponent<MeshRenderer>(); rr.sharedMaterial = mat; rr.shadowCastingMode = ShadowCastingMode.Off;
            road.AddComponent<MeshCollider>().sharedMesh = m;
            streetMats.Add(mat); streetSmooth.Add(0.25f);

            // bariyerler (orta + kenar korkuluk) ve köprü — tek mesh, çarpıştırıcılı
            var concrete = U.Mat(new Color(0.62f, 0.62f, 0.6f), 0.2f);
            var steel = U.Mat(new Color(0.7f, 0.72f, 0.75f), 0.6f, 0.8f);
            var parts = new List<CombineInstance>();
            var steelParts = new List<CombineInstance>();
            var poleSet = Set(Combine(P(cylinder, new Vector3(0, 6f, 0), new Vector3(0.25f, 6f, 0.25f)), P(cube, new Vector3(0, 12f, 1.2f), new Vector3(0.2f, 0.2f, 2.6f)), P(cube, new Vector3(0, 12f, -1.2f), new Vector3(0.2f, 0.2f, 2.6f))), new[] { steel }, 500f);
            var headSet = Set(Combine(P(cube, new Vector3(0, 11.85f, 2.4f), new Vector3(0.5f, 0.15f, 0.9f)), P(cube, new Vector3(0, 11.85f, -2.4f), new Vector3(0.5f, 0.15f, 0.9f))), new[] { LampHead() }, 500f, false);
            var pierSet = Set(cube, new[] { concrete }, 1500f, true, 0);
            for (int i = 1; i < hw.Count; i++)
            {
                Vector3 a = hw[i - 1], b = hw[i];
                Vector3 d = (b - a); float len = d.magnitude; if (len < 0.1f) continue;
                Quaternion q = Quaternion.LookRotation(d.normalized);
                Vector3 mid = (a + b) / 2;
                Vector3 r = q * Vector3.right;
                parts.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(mid + Vector3.up * 0.5f, q, new Vector3(0.6f, 1f, len + 0.05f)) });
                steelParts.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(mid + r * (W / 2 + 0.3f) + Vector3.up * 0.75f, q, new Vector3(0.12f, 0.35f, len + 0.05f)) });
                steelParts.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(mid - r * (W / 2 + 0.3f) + Vector3.up * 0.75f, q, new Vector3(0.12f, 0.35f, len + 0.05f)) });
                if (hwBridge[i])
                    parts.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS(mid - Vector3.up * 0.9f, q, new Vector3(W + 1f, 1.8f, len + 0.05f)) });
                if (i % 7 == 0) { poleSet.Add(mid, q, Vector3.one); headSet.Add(mid, q, Vector3.one); }
                if (hwBridge[i] && i % 6 == 0)
                {
                    float hgt = mid.y - (waterLevel - 6f);
                    pierSet.Add(mid + Vector3.down * (hgt / 2 + 1.8f), q, new Vector3(W * 0.8f, hgt, 3f));
                }
                // kenar korkuluk direkleri
            }
            AddCombined("OtoyolBariyer", parts, concrete, true);
            AddCombined("OtoyolKorkuluk", steelParts, steel, true);

            // köprü kuleleri ve kablolar
            int bs = hwBridge.IndexOf(true), be = hwBridge.LastIndexOf(true);
            if (bs > 0 && be > bs)
            {
                var tower = U.Mat(new Color(0.75f, 0.2f, 0.15f), 0.4f, 0.3f);
                int[] towers = { bs + (be - bs) / 4, bs + 3 * (be - bs) / 4 };
                var cables = new List<CombineInstance>();
                foreach (int ti in towers)
                {
                    Vector3 p = hw[ti]; Vector3 d = U.Flat(hw[ti + 1] - hw[ti - 1]).normalized; Vector3 r = new Vector3(d.z, 0, -d.x);
                    float top = 55f;
                    for (int s = -1; s <= 1; s += 2)
                    {
                        var t = U.Prim(PrimitiveType.Cube, "KopruKulesi", root, p + r * s * (W / 2 + 1.5f) + Vector3.up * (top / 2 - 10f), new Vector3(2.5f, top + 20f, 2.5f), tower, true);
                        t.transform.rotation = Quaternion.LookRotation(d);
                        for (int k = -6; k <= 6; k++)
                        {
                            if (k == 0) continue;
                            int ci = Mathf.Clamp(ti + k * 6, 0, hw.Count - 1);
                            Vector3 deck = hw[ci] + r * s * (W / 2 + 0.6f);
                            Vector3 tp = p + r * s * (W / 2 + 1.5f) + Vector3.up * (top - 6f);
                            Vector3 cd = tp - deck;
                            cables.Add(new CombineInstance { mesh = cube, transform = Matrix4x4.TRS((tp + deck) / 2, Quaternion.LookRotation(cd.normalized), new Vector3(0.15f, 0.15f, cd.magnitude)) });
                        }
                    }
                    U.Prim(PrimitiveType.Cube, "KuleKiris", root, p + Vector3.up * (top + 8f), new Vector3(W + 6f, 2f, 2.5f), tower, true).transform.rotation = Quaternion.LookRotation(d);
                }
                AddCombined("KopruKablolari", cables, U.Mat(new Color(0.85f, 0.85f, 0.85f), 0.6f, 0.8f), false);
                U.Text3D("BOĞAZ KÖPRÜSÜ", root, hw[towers[0]] + Vector3.up * 72f, Quaternion.LookRotation(U.Flat(hw[towers[0] + 1] - hw[towers[0]])), 6f, Color.white);
            }

            // tabelalar
            if (hw.Count > 80)
            {
            Sign(hw[30], hw[31], "OTOYOL  •  Liman  →  •  Merkez 4 km");
            Sign(hw[hw.Count - 30], hw[hw.Count - 31], "OTOYOL  •  Güney Banliyö  →");
            Sign(hw[hw.Count / 2], hw[hw.Count / 2 + 1], "Hız Sınırı 120  •  Polis Radarı");
            }

            // yol ağına ekle: sağ şerit ofseti 6.6 m
            int prev = southNode;
            var hwNodes = new List<int> { southNode };
            float acc = 0f;
            for (int i = 1; i < hw.Count - 1; i++)
            {
                acc += U.FlatDist(hw[i - 1], hw[i]);
                if (acc < 32f) continue;
                acc = 0f;
                int n = g.Add(hw[i] + Vector3.up * 0.06f, 6.6f);
                g.Link(prev, n); prev = n; hwNodes.Add(n);
            }
            g.Link(prev, eastNode); hwNodes.Add(eastNode);

            // otoyol yarışları
            var sprint = new RaceDef { name = "Otoyol Sprinti", type = RaceType.Sprint, prize = 5200, route = w.RouteFromNodes(hwNodes, false) };
            w.races.Add(sprint);
            var loop = new List<int>(hwNodes);
            var back = g.Path(eastNode, southNode);
            for (int i = 1; i < back.Count - 1; i++) loop.Add(back[i]);
            w.races.Add(new RaceDef { name = "Otoyol Turu", type = RaceType.Circuit, laps = 1, prize = 9000, route = w.RouteFromNodes(loop, true) });
            var trap = new RaceDef { name = "Köprü Radarı", type = RaceType.Speedtrap, prize = 6000, route = w.RouteFromNodes(hwNodes, false) };
            trap.special.Add(hwNodes.Count / 4); trap.special.Add(hwNodes.Count / 2); trap.special.Add(3 * hwNodes.Count / 4);
            w.races.Add(trap);
        }

        void AddCombined(string name, List<CombineInstance> parts, Material mat, bool collider)
        {
            for (int start = 0; start < parts.Count; start += 4000)
            {
                var m = new Mesh { indexFormat = IndexFormat.UInt32 };
                m.CombineMeshes(parts.GetRange(start, Mathf.Min(4000, parts.Count - start)).ToArray(), true, true);
                var go = new GameObject(name);
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = m;
                go.AddComponent<MeshRenderer>().sharedMaterial = mat;
                if (collider) go.AddComponent<MeshCollider>().sharedMesh = m;
                go.isStatic = true;
            }
        }

        Texture2D HighwayTexture()
        {
            int wpx = 256, hpx = 128;
            var t = new Texture2D(wpx, hpx, TextureFormat.RGBA32, true);
            var cols = new Color[wpx * hpx];
            for (int y = 0; y < hpx; y++)
                for (int x = 0; x < wpx; x++)
                {
                    float u = x / (float)wpx; // 26 m genişlik
                    float m = u * 26f;
                    float n = Mathf.PerlinNoise(x * 0.3f, y * 0.3f) * 0.05f;
                    Color c = new Color(0.2f + n, 0.2f + n, 0.21f + n);
                    bool edge = Mathf.Abs(m - 1.0f) < 0.15f || Mathf.Abs(m - 25.0f) < 0.15f;
                    bool median = Mathf.Abs(m - 12.4f) < 0.12f || Mathf.Abs(m - 13.6f) < 0.12f;
                    bool dash = (Mathf.Abs(m - 4.8f) < 0.1f || Mathf.Abs(m - 8.6f) < 0.1f || Mathf.Abs(m - 17.4f) < 0.1f || Mathf.Abs(m - 21.2f) < 0.1f) && y < hpx / 2;
                    if (edge || dash) c = new Color(0.9f, 0.9f, 0.88f);
                    if (median) c = new Color(0.95f, 0.75f, 0.1f);
                    cols[y * wpx + x] = c;
                }
            t.SetPixels(cols); t.Apply(true);
            t.wrapMode = TextureWrapMode.Repeat; t.anisoLevel = 8;
            return t;
        }

        void Sign(Vector3 at, Vector3 next, string text)
        {
            Vector3 d = U.Flat(next - at).normalized;
            var green = U.Mat(new Color(0.05f, 0.35f, 0.15f), 0.4f);
            var go = new GameObject("Tabela"); go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(at, Quaternion.LookRotation(-d));
            U.Prim(PrimitiveType.Cube, "TabelaDirek", go.transform, new Vector3(-14f, 4.5f, 0), new Vector3(0.4f, 9f, 0.4f), U.Mat(Color.gray), true);
            U.Prim(PrimitiveType.Cube, "TabelaDirek", go.transform, new Vector3(14f, 4.5f, 0), new Vector3(0.4f, 9f, 0.4f), U.Mat(Color.gray), true);
            U.Prim(PrimitiveType.Cube, "TabelaPano", go.transform, new Vector3(0, 8.5f, 0), new Vector3(24f, 2.6f, 0.2f), green);
            U.Text3D(text, go.transform, new Vector3(0, 8.5f, -0.15f), Quaternion.identity, 1.6f, Color.white);
        }

        Material LampHead()
        {
            if (lampHead == null) { lampHead = U.NewMat(new Color(1f, 0.95f, 0.85f), 0.5f); U.SetEmission(lampHead, new Color(0.2f, 0.18f, 0.15f)); }
            return lampHead;
        }

        // ------------------------------------------------------------------ orman
        void BuildForests()
        {
            var trunk = U.Mat(new Color(0.33f, 0.24f, 0.15f), 0.1f);
            var leafA = U.Mat(new Color(0.16f, 0.32f, 0.12f), 0.1f);
            var leafB = U.Mat(new Color(0.22f, 0.38f, 0.14f), 0.1f);
            var pine = Combine(P(cylinder, new Vector3(0, 2f, 0), new Vector3(0.5f, 2f, 0.5f)), P(cylinder, new Vector3(0, 7f, 0), new Vector3(4.5f, 4.5f, 4.5f)));
            var leafy = Combine(P(cylinder, new Vector3(0, 2f, 0), new Vector3(0.45f, 2f, 0.45f)), P(sphere, new Vector3(0, 6f, 0), new Vector3(7f, 6f, 7f)));
            // kendi "koni"miz yok: silindirin üst yarıçapını küçültmek yerine ince uzun küre kullan
            pine = Combine(P(cylinder, new Vector3(0, 2f, 0), new Vector3(0.5f, 2f, 0.5f)), P(sphere, new Vector3(0, 8f, 0), new Vector3(4.5f, 11f, 4.5f)));
            var setA = Set(pine, new[] { trunk, leafA }, 900f);
            var setB = Set(leafy, new[] { trunk, leafB }, 900f);
            int count = new[] { 2500, 6000, 11000 }[quality];
            int placed = 0, tries = 0;
            Vector3 c = city.center;
            while (placed < count && tries < count * 8)
            {
                tries++;
                var p = new Vector3(c.x + (R01() - 0.5f) * 8000f, 0, c.z + (R01() - 0.5f) * 8000f);
                float dc = DistToCity(p);
                if (dc < 120f) continue;
                if (Mathf.PerlinNoise(p.x / 500f + 3f, p.z / 500f + 7f) < 0.52f) continue; // orman lekeleri
                bool nh; float y = Final(p, out nh);
                if (nh || y < waterLevel + 3f) continue;
                float ny; if (NearestNode(p, out ny) < 30f) continue;
                if (NearHighway(p, 30f)) continue;
                float s = Mathf.Lerp(0.8f, 1.4f, R01());
                (R01() < 0.5f ? setA : setB).Add(new Vector3(p.x, y - 0.2f, p.z), Quaternion.Euler(0, R01() * 360f, 0), Vector3.one * s);
                placed++;
            }
        }

        bool NearHighway(Vector3 p, float d)
        {
            for (int i = 0; i < hw.Count; i += 3) if (U.FlatDist(p, hw[i]) < d) return true;
            return false;
        }

        // ------------------------------------------------------------------ sokak donatısı
        void BuildStreetDressing()
        {
            var steel = U.Mat(new Color(0.25f, 0.26f, 0.28f), 0.5f, 0.7f);
            var lamp = Set(Combine(P(cylinder, new Vector3(0, 4f, 0), new Vector3(0.18f, 4f, 0.18f)), P(cube, new Vector3(0, 8f, -0.8f), new Vector3(0.12f, 0.12f, 1.7f))), new[] { steel }, 350f);
            var head = Set(Combine(P(cube, new Vector3(0, 7.9f, -1.55f), new Vector3(0.45f, 0.15f, 0.7f))), new[] { LampHead() }, 350f, false);
            var bench = Set(Combine(P(cube, new Vector3(0, 0.45f, 0), new Vector3(1.8f, 0.08f, 0.5f)), P(cube, new Vector3(0, 0.75f, 0.22f), new Vector3(1.8f, 0.5f, 0.06f)), P(cube, new Vector3(-0.8f, 0.22f, 0), new Vector3(0.08f, 0.45f, 0.45f)), P(cube, new Vector3(0.8f, 0.22f, 0), new Vector3(0.08f, 0.45f, 0.45f))), new[] { U.Mat(new Color(0.4f, 0.28f, 0.16f), 0.3f) }, 150f, false);
            var bin = Set(Combine(P(cylinder, new Vector3(0, 0.5f, 0), new Vector3(0.5f, 0.5f, 0.5f))), new[] { U.Mat(new Color(0.15f, 0.35f, 0.2f), 0.3f) }, 150f, false);
            var hydrant = Set(Combine(P(cylinder, new Vector3(0, 0.4f, 0), new Vector3(0.28f, 0.4f, 0.28f)), P(sphere, new Vector3(0, 0.82f, 0), new Vector3(0.3f, 0.2f, 0.3f))), new[] { U.Mat(new Color(0.75f, 0.1f, 0.08f), 0.4f) }, 150f, false);
            var tree = Set(Combine(P(cylinder, new Vector3(0, 1.8f, 0), new Vector3(0.35f, 1.8f, 0.35f)), P(sphere, new Vector3(0, 5f, 0), new Vector3(5f, 4.5f, 5f))), new[] { U.Mat(new Color(0.33f, 0.24f, 0.15f)), U.Mat(new Color(0.2f, 0.38f, 0.14f)) }, 400f);
            var manhole = Set(Combine(P(cylinder, Vector3.zero, new Vector3(0.9f, 0.01f, 0.9f))), new[] { U.Mat(new Color(0.12f, 0.12f, 0.12f), 0.6f, 0.8f) }, 120f, false);
            var stripe = Set(Combine(P(cube, Vector3.zero, new Vector3(0.6f, 0.02f, 3.2f))), new[] { U.Mat(new Color(0.85f, 0.85f, 0.82f), 0.3f) }, 200f, false);
            var puddle = Set(Combine(P(cylinder, Vector3.zero, new Vector3(1f, 0.005f, 1f))), new[] { U.Mat(new Color(0.05f, 0.06f, 0.08f), 0.97f, 0.1f) }, 150f, false);
            puddles = puddle;
            SnapSet(lamp, false, 0f); SnapSet(head, false, 0f); SnapSet(bench, false, 0f); SnapSet(bin, false, 0f); SnapSet(hydrant, false, 0f);
            SnapSet(tree, false, -0.1f); SnapSet(manhole, true, 0.02f); SnapSet(stripe, true, 0.025f); SnapSet(puddle, true, 0.015f);
            puddle.hidden = true;
            var barrierMat = U.Mat(new Color(1f, 0.45f, 0.05f), 0.4f);
            var barrier = Set(Combine(P(cube, new Vector3(0, 0.5f, 0), new Vector3(1.6f, 1f, 0.4f))), new[] { barrierMat }, 250f);
            SnapSet(barrier, false, 0f);

            float lampAcc = 0f, dressAcc = 0f;
            int quota = new[] { 1, 2, 3 }[quality];
            var edgeDone = new HashSet<long>();
            for (int a = 0; a < g.nodes.Count; a++)
            {
                foreach (int b in g.adj[a])
                {
                    if (b < a) continue;
                    long ek = ((long)a << 32) | (uint)b;
                    if (!edgeDone.Add(ek)) continue;
                    if (g.lane[a] > 6f || g.lane[b] > 6f) continue; // otoyol düğümleri
                    Vector3 pa = g.nodes[a], pb = g.nodes[b];
                    Vector3 d = pb - pa; float len = U.FlatDist(pa, pb); if (len < 1f) continue;
                    Vector3 df = U.Flat(d).normalized; Vector3 r = new Vector3(df.z, 0, -df.x);
                    float half = Mathf.Min(g.lane[a], g.lane[b]) * 2f;
                    Quaternion q = Quaternion.LookRotation(df);
                    lampAcc += len; dressAcc += len;
                    if (lampAcc > 38f)
                    {
                        lampAcc = 0f;
                        float side = (a + b) % 2 == 0 ? 1f : -1f;
                        Vector3 lp = Vector3.Lerp(pa, pb, 0.5f) + r * side * (half + 1.3f);
                        Quaternion lq = Quaternion.LookRotation(r * side);
                        if (SnapPos(ref lp))
                        {
                            lamp.Add(lp, lq, Vector3.one); head.Add(lp, lq, Vector3.one);
                            lampPositions.Add(lp + Vector3.up * 7.5f - r * side * 1.5f);
                        }
                    }
                    if (half >= 7.5f && R01() < 0.8f)
                        for (int s = -1; s <= 1; s += 2) tree.Add(Vector3.Lerp(pa, pb, R01()) + r * s * (half + 2.5f), Quaternion.Euler(0, R01() * 360, 0), Vector3.one * Mathf.Lerp(0.8f, 1.2f, R01()));
                    if (dressAcc > 90f / quota)
                    {
                        dressAcc = 0f;
                        Vector3 p = Vector3.Lerp(pa, pb, R01()) + r * (half + 1.6f);
                        float k = R01();
                        if (k < 0.35f) bench.Add(p, Quaternion.LookRotation(-r), Vector3.one);
                        else if (k < 0.7f) bin.Add(p, Quaternion.identity, Vector3.one);
                        else hydrant.Add(p, Quaternion.identity, Vector3.one);
                        if (R01() < 0.5f) manhole.Add(Vector3.Lerp(pa, pb, R01()) + r * (half * 0.25f) + Vector3.up * 0.03f, Quaternion.identity, Vector3.one);
                        if (R01() < 0.3f) puddle.Add(Vector3.Lerp(pa, pb, R01()) + r * (half * (R01() - 0.5f)) + Vector3.up * 0.02f, Quaternion.Euler(0, R01() * 360, 0), new Vector3(Mathf.Lerp(1.5f, 4f, R01()), 1f, Mathf.Lerp(1f, 3f, R01())));
                        // park yeri
                        // park yeri: kaldırımdan ~0.4 m, şeride taşmaz; dar yol ve kavşak yakını hariç
                        bool nearJunction = (g.adj[a].Count >= 3 && len < 30f) || (g.adj[b].Count >= 3 && len < 30f);
                        if (half >= 5.5f && !nearJunction)
                            parkSpots.Add(new KeyValuePair<Vector3, Quaternion>(Vector3.Lerp(pa, pb, 0.5f) + r * (half - 1.35f), q));
                    }
                }
                // kavşaklar
                int deg = g.adj[a].Count;
                if (deg >= 3 && g.lane[a] <= 6f)
                {
                    foreach (int b in g.adj[a])
                    {
                        Vector3 df = U.Flat(g.nodes[b] - g.nodes[a]).normalized; Vector3 r = new Vector3(df.z, 0, -df.x);
                        float half = g.lane[a] * 2f;
                        Vector3 c = g.nodes[a] + df * (half + 3f) + Vector3.up * 0.04f;
                        for (float o = -half + 0.6f; o < half; o += 1.2f) stripe.Add(c + r * o, Quaternion.LookRotation(df), Vector3.one);
                    }
                    if (deg >= 4 && g.lane[a] >= 2.5f) AddTrafficLight(a);
                }
            }

            // inşaat bariyerleri
            for (int k = 0; k < 10; k++)
            {
                int n = rnd.Next(g.nodes.Count);
                if (g.adj[n].Count == 0 || g.lane[n] > 6f) continue;
                Vector3 df = U.Flat(g.nodes[g.adj[n][0]] - g.nodes[n]).normalized; Vector3 r = new Vector3(df.z, 0, -df.x);
                for (int j = 0; j < 4; j++) barrier.Add(g.nodes[n] + df * (10f + j * 1.8f) + r * g.lane[n] * 1.3f, Quaternion.LookRotation(df), Vector3.one);
                Vector3 tp = g.nodes[n] + df * 9f + r * g.lane[n] * 1.3f;
                if (SnapPos(ref tp)) U.Text3D("YOL ÇALIŞMASI", root, tp + Vector3.up * 1.6f, Quaternion.LookRotation(-df), 0.6f, Color.black);
            }

            // reklam panoları ve duraklar
            string[] ads = { "NITRO+ ENERJİ İÇECEĞİ", "ROCKPORT RADYO 94.6", "TURBO LASTİK — %30 İNDİRİM", "HIZLI KREDİ", "GECE YARIŞI ÇAĞRI", "ENDÜSTRİ YAĞLARI", "SOKAK KRALLARI DERGİSİ" };
            Color[] adCol = { new Color(0.9f, 0.3f, 0.05f), new Color(0.1f, 0.3f, 0.8f), new Color(0.8f, 0.1f, 0.15f), new Color(0.1f, 0.55f, 0.25f), new Color(0.35f, 0.1f, 0.5f) };
            int boards = new[] { 10, 18, 26 }[quality];
            for (int k = 0, tries = 0; k < boards && tries < 500; tries++)
            {
                int n = rnd.Next(g.nodes.Count);
                if (g.adj[n].Count != 2 || g.lane[n] < 3f || g.lane[n] > 6f) continue;
                Vector3 df = U.Flat(g.nodes[g.adj[n][0]] - g.nodes[n]).normalized; Vector3 r = new Vector3(df.z, 0, -df.x);
                Vector3 p = g.nodes[n] + r * (g.lane[n] * 2f + 5f);
                if (!SnapPos(ref p)) continue;
                var bgo = new GameObject("ReklamPanosu"); bgo.transform.SetParent(root, false);
                bgo.transform.SetPositionAndRotation(p, Quaternion.LookRotation(-df));
                U.Prim(PrimitiveType.Cube, "Direk", bgo.transform, new Vector3(0, 4f, 0.3f), new Vector3(0.5f, 8f, 0.5f), steel, true);
                var pm = U.NewMat(adCol[k % adCol.Length], 0.3f); U.SetEmission(pm, adCol[k % adCol.Length] * 0.15f);
                facadeMats.Add(pm);
                U.Prim(PrimitiveType.Cube, "Pano", bgo.transform, new Vector3(0, 10f, 0), new Vector3(12f, 4.5f, 0.3f), pm, true);
                U.Text3D(ads[k % ads.Length], bgo.transform, new Vector3(0, 10f, -0.2f), Quaternion.identity, 0.9f, Color.white);
                k++;
            }
            int stops = new[] { 6, 12, 18 }[quality];
            var glass = U.Mat(new Color(0.5f, 0.6f, 0.65f), 0.9f, 0.1f);
            for (int k = 0, tries = 0; k < stops && tries < 500; tries++)
            {
                int n = rnd.Next(g.nodes.Count);
                if (g.adj[n].Count != 2 || g.lane[n] < 3f || g.lane[n] > 6f) continue;
                Vector3 df = U.Flat(g.nodes[g.adj[n][0]] - g.nodes[n]).normalized; Vector3 r = new Vector3(df.z, 0, -df.x);
                Vector3 p = g.nodes[n] + r * (g.lane[n] * 2f + 2.2f);
                if (!SnapPos(ref p)) continue;
                var s = new GameObject("Durak"); s.transform.SetParent(root, false);
                s.transform.SetPositionAndRotation(p, Quaternion.LookRotation(df));
                U.Prim(PrimitiveType.Cube, "DurakCati", s.transform, new Vector3(0, 2.6f, 0), new Vector3(1.6f, 0.12f, 4f), steel, true);
                U.Prim(PrimitiveType.Cube, "DurakCam", s.transform, new Vector3(0.75f, 1.3f, 0), new Vector3(0.05f, 2.4f, 4f), glass, true);
                U.Prim(PrimitiveType.Cube, "DurakBank", s.transform, new Vector3(0.3f, 0.45f, 0), new Vector3(0.5f, 0.1f, 2.5f), steel);
                U.Text3D("OTOBÜS", s.transform, new Vector3(-0.1f, 2.85f, 0), Quaternion.Euler(0, 90, 0), 0.5f, Color.white);
                k++;
            }

            // pursuit breaker noktaları
            int pbs = 0;
            for (int tries = 0; tries < 2000 && pbs < 8; tries++)
            {
                int n = rnd.Next(g.nodes.Count);
                if (g.adj[n].Count != 3 || g.lane[n] > 6f) continue;
                Vector3 df = U.Flat(g.nodes[g.adj[n][0]] - g.nodes[n]).normalized; Vector3 r = new Vector3(df.z, 0, -df.x);
                Vector3 p = g.nodes[n] + df * 16f + r * (g.lane[n] * 2f + 6f);
                if (!SnapPos(ref p)) continue;
                if (Physics.CheckSphere(p + Vector3.up * 6f, 4f)) continue;
                PursuitBreaker.Create(p, pbs % 2, root);
                pbs++;
            }

            // DUR / hız tabelaları
            int signs = 0;
            for (int a = 0; a < g.nodes.Count && signs < 60 * (quality + 1); a++)
            {
                if (g.adj[a].Count != 3 || g.lane[a] > 6f || (a % 3) != 0) continue;
                int b = g.adj[a][rnd.Next(3)];
                Vector3 df = U.Flat(g.nodes[b] - g.nodes[a]).normalized; Vector3 r = new Vector3(df.z, 0, -df.x);
                Vector3 p = g.nodes[a] + df * (g.lane[a] * 2f + 4f) + r * (g.lane[a] * 2f + 1f);
                if (!SnapPos(ref p)) continue;
                var s = new GameObject("DurTabelasi"); s.transform.SetParent(root, false);
                s.transform.SetPositionAndRotation(p, Quaternion.LookRotation(df));
                U.Prim(PrimitiveType.Cylinder, "Direk", s.transform, new Vector3(0, 1.2f, 0), new Vector3(0.08f, 1.2f, 0.08f), steel);
                bool stop = signs % 3 != 0;
                var plate = U.Prim(PrimitiveType.Cylinder, "Levha", s.transform, new Vector3(0, 2.6f, 0), new Vector3(0.8f, 0.02f, 0.8f), U.Mat(stop ? new Color(0.8f, 0.05f, 0.05f) : Color.white, 0.4f));
                plate.transform.localRotation = Quaternion.Euler(90, 0, 0);
                U.Text3D(stop ? "DUR" : "50", s.transform, new Vector3(0, 2.6f, 0.03f), Quaternion.Euler(0, 180, 0), 0.35f, stop ? Color.white : Color.black);
                signs++;
            }

            // gerçek lamba ışık havuzu (gece, oyuncuya en yakın lambalar)
            int lights = new[] { 2, 4, 6 }[quality];
            for (int i = 0; i < lights; i++)
            {
                var lg = new GameObject("LambaIsik"); lg.transform.SetParent(root, false);
                var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.range = 24f; l.intensity = 2.2f; l.color = new Color(1f, 0.82f, 0.55f); l.shadows = LightShadows.None; l.enabled = false;
                lampPool.Add(l);
            }
        }

        // ------------------------------------------------------------------ trafik ışıkları
        void AddTrafficLight(int n)
        {
            lightNodes.Add(n);
            lightPhase[n] = R01() * 24f;
            lightAxis[n] = U.Flat(g.nodes[g.adj[n][0]] - g.nodes[n]).normalized;
        }

        /// <summary>prev→next yönünde gelen araç için next düğümündeki ışık kırmızı mı?</summary>
        public bool IsRed(int prev, int next)
        {
            float ph;
            if (!lightPhase.TryGetValue(next, out ph)) return false;
            Vector3 dir = U.Flat(g.nodes[next] - g.nodes[prev]).normalized;
            bool groupA = Mathf.Abs(Vector3.Dot(dir, lightAxis[next])) > 0.707f;
            float t = Mathf.Repeat(Time.time + ph, 24f);
            return groupA ? t >= 10.5f : (t < 12f || t >= 22.5f);
        }

        int LightState(int n, bool groupA)
        {
            float t = Mathf.Repeat(Time.time + lightPhase[n], 24f);
            if (groupA) return t < 10f ? 2 : t < 12f ? 1 : 0;
            return t >= 12f && t < 22f ? 2 : t >= 22f ? 1 : 0;
        }

        readonly List<Matrix4x4>[] tlLists = { new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>(), new List<Matrix4x4>() };
        Mesh tlPole;

        void DrawTrafficLights(Vector3 cam)
        {
            if (lightNodes.Count == 0) return;
            if (tlRed == null)
            {
                tlRed = U.NewMat(new Color(0.3f, 0, 0)); U.SetEmission(tlRed, new Color(4f, 0.1f, 0.05f));
                tlYellow = U.NewMat(new Color(0.3f, 0.25f, 0)); U.SetEmission(tlYellow, new Color(3.5f, 2.2f, 0.1f));
                tlGreen = U.NewMat(new Color(0, 0.3f, 0.1f)); U.SetEmission(tlGreen, new Color(0.1f, 3.5f, 0.8f));
                tlOff = U.Mat(new Color(0.08f, 0.08f, 0.08f), 0.6f);
                tlPole = Combine(P(cylinder, new Vector3(0, 2.2f, 0), new Vector3(0.15f, 2.2f, 0.15f)), P(cube, new Vector3(0, 4.9f, 0), new Vector3(0.4f, 1.2f, 0.35f)));
            }
            foreach (var l in tlLists) l.Clear();
            float cull = 220f;
            foreach (int n in lightNodes)
            {
                Vector3 c = g.nodes[n];
                if (U.FlatDist(c, cam) > cull) continue;
                float half = g.lane[n] * 2f + 1.2f;
                foreach (int b in g.adj[n])
                {
                    Vector3 df = U.Flat(g.nodes[b] - c).normalized; Vector3 r = new Vector3(df.z, 0, -df.x);
                    Vector3 p = c + df * (half + 1f) + r * half;
                    Quaternion q = Quaternion.LookRotation(df); // gelen trafiğe bakar
                    tlLists[4].Add(Matrix4x4.TRS(p, q, Vector3.one));
                    bool groupA = Mathf.Abs(Vector3.Dot(df, lightAxis[n])) > 0.707f;
                    int st = LightState(n, groupA);
                    for (int k = 0; k < 3; k++)
                    {
                        Vector3 lp = p + Vector3.up * (5.3f - k * 0.4f) + df * 0.2f;
                        int list = (2 - k) == st ? (st == 0 ? 0 : st == 1 ? 1 : 2) : 3;
                        tlLists[list].Add(Matrix4x4.TRS(lp, q, Vector3.one * 0.28f));
                    }
                }
            }
            Material[] m = { tlRed, tlYellow, tlGreen, tlOff };
            for (int i = 0; i < 4; i++) if (tlLists[i].Count > 0) Graphics.RenderMeshInstanced(new RenderParams(m[i]) { layer = OptimizationManager.DetailLayer }, sphere, 0, tlLists[i]);
            if (tlLists[4].Count > 0) Graphics.RenderMeshInstanced(new RenderParams(U.Mat(new Color(0.2f, 0.2f, 0.2f), 0.4f, 0.6f)) { layer = OptimizationManager.DetailLayer, shadowCastingMode = ShadowCastingMode.On }, tlPole, 0, tlLists[4]);
        }

        // ------------------------------------------------------------------ semtler
        void BuildDistricts()
        {
            Vector3 c = city.center, e = city.extents;
            districts.Add(new KeyValuePair<string, Vector3>("Merkez", new Vector3(c.x - e.x * 0.05f, 0, c.z + e.z * 0.25f)));
            districts.Add(new KeyValuePair<string, Vector3>("Liman", new Vector3(city.max.x - e.x * 0.15f, 0, c.z + e.z * 0.1f)));
            districts.Add(new KeyValuePair<string, Vector3>("Banliyö Kuzey", new Vector3(c.x - e.x * 0.2f, 0, city.max.z - e.z * 0.25f)));
            districts.Add(new KeyValuePair<string, Vector3>("Banliyö Güney", new Vector3(c.x, 0, c.z - e.z * 0.35f)));
            districts.Add(new KeyValuePair<string, Vector3>("Sanayi", new Vector3(city.min.x + e.x * 0.25f, 0, c.z + e.z * 0.05f)));
            if (hw.Count > 0) districts.Add(new KeyValuePair<string, Vector3>("Otoyol", hw[hw.Count / 2]));
            foreach (var d in districts)
            {
                w.labels.Add(d);
                if (d.Key == "Otoyol") continue;
                int n = g.Nearest(d.Value);
                if (g.adj[n].Count == 0) continue;
                Vector3 df = U.Flat(g.nodes[g.adj[n][0]] - g.nodes[n]).normalized; Vector3 r = new Vector3(df.z, 0, -df.x);
                var s = new GameObject("SemtTabelasi"); s.transform.SetParent(root, false);
                Vector3 sp = g.nodes[n] + r * (g.lane[n] * 2f + 2f);
                SnapPos(ref sp);
                s.transform.SetPositionAndRotation(sp, Quaternion.LookRotation(-df));
                U.Prim(PrimitiveType.Cube, "Direk", s.transform, new Vector3(0, 1.5f, 0), new Vector3(0.15f, 3f, 0.15f), U.Mat(Color.gray), true);
                U.Prim(PrimitiveType.Cube, "Levha", s.transform, new Vector3(0, 3.3f, 0), new Vector3(4f, 1f, 0.1f), U.Mat(new Color(0.05f, 0.2f, 0.55f), 0.4f));
                U.Text3D(d.Key, s.transform, new Vector3(0, 3.3f, -0.07f), Quaternion.identity, 0.55f, Color.white);
            }
        }

        public string DistrictAt(Vector3 p)
        {
            string best = ""; float bd = float.MaxValue;
            foreach (var d in districts)
            {
                float dd = U.FlatDist(p, d.Value);
                if (d.Key == "Otoyol") { if (NearHighway(p, 40f)) return "Otoyol"; continue; }
                if (dd < bd) { bd = dd; best = d.Key; }
            }
            if (DistToCity(p) > 150f) return "Kırsal";
            return best;
        }

        // ------------------------------------------------------------------ gökyüzü, yaşam, ses
        void BuildSky()
        {
            var cm = Resources.Load<Material>("MW_CloudsMat");
            if (cm == null || cm.shader == null || !cm.shader.isSupported) return;
            var go = new GameObject("Bulutlar");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = sphere;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = new Material(cm); r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
            go.transform.localScale = Vector3.one * 2800f;
            clouds = go.transform;
        }

        void BuildLife()
        {
            var bird = new Mesh();
            bird.vertices = new[] { new Vector3(0, 0, 0.4f), new Vector3(-0.9f, 0.15f, -0.2f), new Vector3(0.9f, 0.15f, -0.2f), new Vector3(0, 0, -0.3f) };
            bird.triangles = new[] { 0, 1, 3, 0, 3, 2, 0, 3, 1, 0, 2, 3 };
            bird.RecalculateNormals();
            birdSet = new ISet { mesh = bird, mats = new[] { U.Mat(new Color(0.1f, 0.1f, 0.1f)) }, layer = 0, shadows = ShadowCastingMode.Off };
            for (int i = 0; i < 4; i++) flockCenters.Add(city.center + new Vector3((R01() - 0.5f) * city.size.x, 70f + R01() * 40f, (R01() - 0.5f) * city.size.z));

            plane = new GameObject("Ucak").transform;
            plane.SetParent(root, false);
            var white = U.Mat(new Color(0.9f, 0.9f, 0.92f), 0.6f, 0.3f);
            U.Prim(PrimitiveType.Capsule, "Govde", plane, Vector3.zero, new Vector3(3f, 3f, 30f), white).transform.localRotation = Quaternion.Euler(90, 0, 0);
            U.Prim(PrimitiveType.Cube, "Kanat", plane, Vector3.zero, new Vector3(34f, 0.6f, 5f), white);
            U.Prim(PrimitiveType.Cube, "Kuyruk", plane, new Vector3(0, 3f, -13f), new Vector3(0.6f, 6f, 4f), white);
            var lg = new GameObject("Isik"); lg.transform.SetParent(plane, false); lg.transform.localPosition = new Vector3(0, -2f, 0);
            planeLight = lg.AddComponent<Light>(); planeLight.type = LightType.Point; planeLight.color = Color.red; planeLight.range = 60f; planeLight.intensity = 6f;
            plane.gameObject.SetActive(false);

            ambience = gameObject.AddComponent<AudioSource>();
            ambience.clip = AudioSynth.CityHum(); ambience.loop = true; ambience.spatialBlend = 0f; ambience.volume = 0.12f;
            ambience.Play();
        }

        void CollectMaterials()
        {
            if (w.root == null) return;
            foreach (var r in w.root.GetComponentsInChildren<Renderer>(true))
            {
                if (r.transform.IsChildOf(root)) continue;
                string cat = BakedWorld.Category(r.transform, w.root);
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null) continue;
                    string n = r.name.ToLowerInvariant() + (r.transform.parent != null ? r.transform.parent.name.ToLowerInvariant() : "");
                    if (cat == "ground" && (n.Contains("street") || m.name.ToLowerInvariant().Contains("street") || m.name.ToLowerInvariant().Contains("asphalt")))
                    { if (!streetMats.Contains(m)) { streetMats.Add(m); streetSmooth.Add(m.HasProperty("_Smoothness") ? m.GetFloat("_Smoothness") : 0.2f); } }
                    if (cat == "building" && !facadeMats.Contains(m)) facadeMats.Add(m);
                }
            }
        }

        /// <summary>Islak zemin: asfalt parlak, su birikintileri görünür; Yüksek'te yansıma probu.</summary>
        public void SetWet(bool on)
        {
            wet = on;
            if (puddles != null) puddles.hidden = !on;
            for (int i = 0; i < streetMats.Count; i++)
            {
                var m = streetMats[i];
                if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", on ? 0.82f : streetSmooth[i]);
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", on ? 0.82f : streetSmooth[i]);
            }
            if (on && quality >= 2 && probe == null)
            {
                var pg = new GameObject("YansimaProbu"); pg.transform.SetParent(root, false);
                probe = pg.AddComponent<ReflectionProbe>();
                probe.mode = ReflectionProbeMode.Realtime;
                probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
                probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.IndividualFaces;
                probe.resolution = 128;
                probe.size = new Vector3(300f, 200f, 300f);
                probe.farClipPlane = 400f;
            }
            if (probe != null) probe.gameObject.SetActive(on);
        }

        /// <summary>Gece: cephe malzemelerinde pencere ışığı (taban dokusu emisyon olarak).</summary>
        public void SetNight(float night)
        {
            foreach (var m in facadeMats)
            {
                if (m == null || !m.HasProperty("_EmissionColor")) continue;
                if (m.HasProperty("_EmissionMap") && m.HasProperty("_BaseMap") && m.GetTexture("_EmissionMap") == null) m.SetTexture("_EmissionMap", m.GetTexture("_BaseMap"));
                U.SetEmission(m, night > 0.4f ? new Color(1f, 0.85f, 0.6f) * (night * 0.45f) : Color.black);
            }
            if (lampHead != null) U.SetEmission(lampHead, new Color(1f, 0.85f, 0.6f) * (0.15f + night * 3f));
        }

        // ------------------------------------------------------------------ güncelleme
        void Update()
        {
            var game = Game.I;
            if (game == null || game.cam == null) return;
            Vector3 cp = game.cam.transform.position;
            float cullMul = quality == 0 ? 0.6f : quality == 1 ? 0.8f : 1f;
            foreach (var s in sets) s.Draw(cp, cullMul);
            DrawTrafficLights(cp);
            if (clouds != null) { clouds.position = cp; clouds.localScale = Vector3.one * game.cam.farClipPlane * 1.8f; }

            // lamba ışıkları
            lampTimer -= Time.deltaTime;
            if (lampTimer <= 0f && game.player != null)
            {
                lampTimer = 0.5f;
                bool night = game.Night > 0.45f;
                Vector3 pp = game.player.transform.position;
                var near = new List<KeyValuePair<float, Vector3>>();
                if (night)
                    foreach (var l in lampPositions)
                    {
                        float d = U.FlatDist(l, pp);
                        if (d < 120f) near.Add(new KeyValuePair<float, Vector3>(d, l));
                    }
                near.Sort((a, b) => a.Key.CompareTo(b.Key));
                for (int i = 0; i < lampPool.Count; i++)
                {
                    bool on = night && i < near.Count;
                    lampPool[i].enabled = on;
                    if (on) lampPool[i].transform.position = near[i].Value;
                }
                if (probe != null && probe.isActiveAndEnabled) { probe.transform.position = pp + Vector3.up * 3f; probe.RenderProbe(); }
            }

            UpdateParked(game);
            UpdateLife(cp);
        }

        void UpdateParked(Game game)
        {
            if (game.player == null || parkSpots.Count == 0) return;
            parkTimer -= Time.deltaTime;
            if (parkTimer > 0f) return;
            parkTimer = 2f;
            int want = new[] { 20, 40, 70 }[quality];
            Vector3 pp = game.player.transform.position;
            // en yakın park yerlerini seç
            var order = new List<KeyValuePair<float, int>>();
            for (int i = 0; i < parkSpots.Count; i++)
            {
                if (badSpots.Contains(i)) continue;
                float d = U.FlatDist(parkSpots[i].Key, pp);
                if (d < 260f && d > 40f) order.Add(new KeyValuePair<float, int>(d, i));
            }
            order.Sort((a, b) => a.Key.CompareTo(b.Key));
            var target = new HashSet<int>();
            for (int i = 0; i < order.Count && i < want; i++) target.Add(order[i].Value);
            // eşleşmeyenleri boşalt
            for (int i = 0; i < parkPool.Count; i++)
            {
                if (parkAssigned[i] >= 0 && target.Contains(parkAssigned[i])) { target.Remove(parkAssigned[i]); continue; }
                parkAssigned[i] = -1;
            }
            foreach (int spot in target)
            {
                int slot = parkAssigned.IndexOf(-1);
                if (slot < 0)
                {
                    if (parkPool.Count >= want) break;
                    var def = Catalog.Traffic[rnd.Next(Catalog.Traffic.Count)];
                    var col = Catalog.Paints[rnd.Next(Catalog.Paints.Length)];
                    var go = CarFactory.BuildStatic(def, col);
                    go.transform.SetParent(root, true);
                    parkPool.Add(go); parkAssigned.Add(-1);
                    slot = parkPool.Count - 1;
                }
                parkAssigned[slot] = spot;
                var t = parkPool[slot].transform;
                Vector3 p; Quaternion q;
                if (!FitParked(parkSpots[spot].Key, parkSpots[spot].Value, out p, out q)) { parkAssigned[slot] = -1; badSpots.Add(spot); continue; }
                t.SetPositionAndRotation(p, q);
                if (!parkPool[slot].activeSelf) parkPool[slot].SetActive(true);
            }
            for (int i = 0; i < parkPool.Count; i++) if (parkAssigned[i] < 0 && parkPool[i].activeSelf) parkPool[i].SetActive(false);
        }

        readonly HashSet<int> badSpots = new HashSet<int>();

        /// <summary>Park aracını 4 köşeden zemine oturt, eğime hizala, yol kenarına paralel tut; sığmıyorsa false.</summary>
        bool FitParked(Vector3 c, Quaternion q, out Vector3 pos, out Quaternion rot)
        {
            pos = c; rot = q;
            Vector3 f = q * Vector3.forward, r = q * Vector3.right;
            var pts = new Vector3[4];
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = c + r * (i % 2 == 0 ? -0.85f : 0.85f) + f * (i < 2 ? 2.0f : -2.0f);
                RaycastHit h;
                if (!GroundHit(p, out h)) return false;
                pts[i] = h.point;
                minY = Mathf.Min(minY, h.point.y); maxY = Mathf.Max(maxY, h.point.y);
            }
            if (maxY - minY > 0.45f) return false; // kaldırıma bindi / çok eğimli
            Vector3 n = Vector3.Cross(pts[3] - pts[0], pts[2] - pts[1]).normalized;
            if (n.y < 0f) n = -n;
            if (n.y < 0.9f) return false;
            pos = (pts[0] + pts[1] + pts[2] + pts[3]) * 0.25f + Vector3.up * 0.02f;
            rot = Quaternion.FromToRotation(Vector3.up, n) * q;
            // başka bir şeyle (direk, durak, bina) çakışıyor mu?
            if (Physics.CheckBox(pos + Vector3.up * 0.9f, new Vector3(0.85f, 0.5f, 2.0f), rot, GroundMask, QueryTriggerInteraction.Ignore)) return false;
            if (U.CarNearby(pos, 4f, null)) return false;
            return true;
        }

        readonly List<Matrix4x4> birdM = new List<Matrix4x4>();

        void UpdateLife(Vector3 cam)
        {
            float t = Time.time;
            birdM.Clear();
            for (int f = 0; f < flockCenters.Count; f++)
            {
                Vector3 c = flockCenters[f];
                if (U.FlatDist(c, cam) > 700f) continue;
                for (int i = 0; i < 12; i++)
                {
                    float a = t * 0.35f + i * 0.52f + f * 1.7f;
                    float rr = 25f + 8f * Mathf.Sin(i * 1.3f + t * 0.5f);
                    Vector3 p = c + new Vector3(Mathf.Cos(a) * rr, Mathf.Sin(t * 2f + i) * 3f, Mathf.Sin(a) * rr);
                    Vector3 dir = new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a));
                    float flap = Mathf.Sin(t * 12f + i) * 0.4f + 1f;
                    birdM.Add(Matrix4x4.TRS(p, Quaternion.LookRotation(dir), new Vector3(flap, 1f, 1f) * 1.2f));
                }
            }
            if (birdM.Count > 0) Graphics.RenderMeshInstanced(new RenderParams(birdSet.mats[0]), birdSet.mesh, 0, birdM);

            // uçak: ~90 sn'de bir şehir üzerinden geçer
            if (planeT < 0f && Mathf.Repeat(t, 90f) < 0.05f) planeT = 0f;
            if (planeT >= 0f)
            {
                planeT += Time.deltaTime / 60f;
                Vector3 a = city.center + new Vector3(-4000f, 650f, -1500f), b = city.center + new Vector3(4000f, 700f, 1800f);
                plane.gameObject.SetActive(true);
                plane.position = Vector3.Lerp(a, b, planeT);
                plane.rotation = Quaternion.LookRotation(b - a);
                planeLight.enabled = Mathf.Repeat(t, 1.2f) < 0.15f;
                if (planeT > 1f) { planeT = -1f; plane.gameObject.SetActive(false); }
            }

            // uzaktan siren sesi
            sirenTimer -= Time.deltaTime;
            if (ambience != null) ambience.volume = 0.12f * AudioBus.Get(AudioBus.Bus.Efekt);
            if (sirenTimer <= 0f && ambience != null)
            {
                sirenTimer = Random.Range(35f, 80f);
                if (Game.I.police != null && !Game.I.police.pursuit) ambience.PlayOneShot(AudioSynth.Siren(), 0.06f * AudioBus.Get(AudioBus.Bus.Siren));
            }
        }
    }
}
