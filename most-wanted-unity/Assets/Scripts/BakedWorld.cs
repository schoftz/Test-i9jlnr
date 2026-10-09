using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace MostWanted
{
    /// <summary>Küçük JSON okuyucu (iç içe diziler için; JsonUtility bunları okuyamaz).</summary>
    public static class MiniJson
    {
        public static object Parse(string s) { int i = 0; return Val(s, ref i); }

        static void Ws(string s, ref int i) { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

        static object Val(string s, ref int i)
        {
            Ws(s, ref i);
            char c = s[i];
            if (c == '{')
            {
                var d = new Dictionary<string, object>(); i++;
                Ws(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i); string k = Str(s, ref i); Ws(s, ref i); i++; // :
                    d[k] = Val(s, ref i); Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    i++; return d;
                }
            }
            if (c == '[')
            {
                var l = new List<object>(); i++;
                Ws(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Val(s, ref i)); Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    i++; return l;
                }
            }
            if (c == '"') return Str(s, ref i);
            if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            return double.Parse(s.Substring(st, i - st), CultureInfo.InvariantCulture);
        }

        static string Str(string s, ref int i)
        {
            var sb = new StringBuilder(); i++;
            while (s[i] != '"')
            {
                if (s[i] == '\\')
                {
                    i++;
                    char e = s[i];
                    if (e == 'u') { sb.Append((char)System.Convert.ToInt32(s.Substring(i + 1, 4), 16)); i += 4; }
                    else sb.Append(e == 'n' ? '\n' : e == 't' ? '\t' : e);
                }
                else sb.Append(s[i]);
                i++;
            }
            i++;
            return sb.ToString();
        }

        public static float F(object o) { return System.Convert.ToSingle(o, CultureInfo.InvariantCulture); }
        public static int I(object o) { return System.Convert.ToInt32(o, CultureInfo.InvariantCulture); }
        public static List<object> L(object o) { return o as List<object>; }
    }

    /// <summary>
    /// Önceden pişirilmiş (baked) yol ağıyla içe aktarılan harita (city_3d_model.glb).
    /// Yol ağı: Assets/Resources/MapData/&lt;harita&gt;_roadgraph.json (Tools/map_bake ile üretildi).
    /// Koordinatlar glTFast kuralına göre: glTF X ekseni aynalanmış (x → -x), ölçek ×100 (dosya 0.01 düğüm ölçeği içeriyor).
    /// </summary>
    public class BakedWorld : World
    {
        public static readonly string[] GroundWords = { "street", "curb", "pavement", "concrete", "parkinglot", "yardground", "uploads_files", "terrain", "ground" };
        public static readonly string[] BuildingWords = { "facade", "roofplane", "flatroof", "building" };
        public static readonly string[] TreeWords = { "tree", "birch" };

        readonly GameObject prefab;
        readonly string json;
        public string credit;

        public BakedWorld(GameObject p, string jsonText, string c) { prefab = p; json = jsonText; credit = c; title = "Şehir (İthal)"; }

        public static string Category(Transform t, Transform root)
        {
            for (var x = t; x != null && x != root; x = x.parent)
            {
                string n = x.name.ToLowerInvariant().Replace("_", "").Replace(".", "");
                foreach (var w in GroundWords) if (n.StartsWith(w.Replace("_", ""))) return "ground";
                foreach (var w in BuildingWords) if (n.StartsWith(w)) return "building";
                foreach (var w in TreeWords) if (n.StartsWith(w)) return "tree";
                if (n.StartsWith("street")) return "ground";
            }
            return "other";
        }

        public override void Build()
        {
            var data = MiniJson.Parse(json) as Dictionary<string, object>;
            root = new GameObject("IthalHarita").transform;
            var inst = Object.Instantiate(prefab, root);
            inst.name = prefab.name;
            inst.transform.localPosition = Vector3.zero;
            float scale = data.ContainsKey("scale") ? MiniJson.F(data["scale"]) : 100f;
            Bounds b = RB(inst.transform);
            // ölçek: haritanın dünya genişliği pişirilmiş genişliğe (≈2744 m) eşitlenir (dosyada 0.01 düğüm ölçeği var → ~×100)
            float wantW = data.ContainsKey("worldWidth") ? MiniJson.F(data["worldWidth"]) : 0f;
            if (wantW > 0f && b.size.x > 0.001f)
            {
                float f = wantW / b.size.x;
                if (Mathf.Abs(f - 1f) > 0.02f) inst.transform.localScale *= f;
                Debug.Log("[MW] Harita ölçek çarpanı: " + f.ToString("0.###") + " (ölçülen genişlik " + b.size.x.ToString("0.#") + ")");
            }
            else if (Mathf.Max(b.size.x, b.size.z) < 200f) inst.transform.localScale *= scale;

            bool optimized = prefab.name.EndsWith("_opt") || inst.GetComponentInChildren<MeshCollider>() != null;
            int detailLayer = OptimizationManager.DetailLayer;
            foreach (var mf in inst.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                string cat = Category(mf.transform, inst.transform);
                var r = mf.GetComponent<Renderer>();
                if (r != null)
                {
                    if (cat == "ground") r.shadowCastingMode = ShadowCastingMode.Off;
                    if (cat == "tree") { mf.gameObject.layer = detailLayer; r.shadowCastingMode = ShadowCastingMode.On; }
                    r.receiveShadows = true;
                }
                if (!optimized && (cat == "ground" || cat == "building"))
                {
                    var mc = mf.gameObject.AddComponent<MeshCollider>();
                    mc.sharedMesh = mf.sharedMesh;
                    mc.convex = false;
                }
                mf.gameObject.isStatic = true;
            }
            // glTF'te metallicFactor yazılmamışsa varsayılan 1'dir → binalar gökyüzünü yansıtan krom gibi görünür.
            // Şehir malzemelerini metal olmayan yüzeye çevir.
            var fixedMats = new HashSet<Material>();
            foreach (var r in inst.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                {
                    if (m == null || !fixedMats.Add(m)) continue;
                    m.enableInstancing = true;
                    if (m.HasProperty("metallicFactor")) m.SetFloat("metallicFactor", 0f);
                    if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
                    if (m.HasProperty("roughnessFactor")) m.SetFloat("roughnessFactor", Mathf.Max(0.75f, m.GetFloat("roughnessFactor")));
                    if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", Mathf.Min(0.25f, m.GetFloat("_Smoothness")));
                    if (m.HasProperty("_EnvironmentReflections")) m.SetFloat("_EnvironmentReflections", 0f);
                }
            Debug.Log("[MW] Harita malzemeleri düzeltildi (metal=0): " + fixedMats.Count);
            StaticBatchingUtility.Combine(inst);
            Physics.SyncTransforms();

            // ---- yol ağı ----
            var nodes = MiniJson.L(data["nodes"]);
            var pts = new List<Vector3>();
            var lanes = new List<float>();
            foreach (var o in nodes)
            {
                var a = MiniJson.L(o);
                pts.Add(new Vector3(MiniJson.F(a[0]), MiniJson.F(a[1]), MiniJson.F(a[2])));
                lanes.Add(a.Count > 3 ? MiniJson.F(a[3]) : 3f);
            }
            // aynalama doğrulaması: yol noktalarının altında Street çarpıştırıcısı olmalı
            bool mirror = false;
            int ok = CountHits(pts, false), okM = 0;
            if (ok < 12) { okM = CountHits(pts, true); if (okM > ok) mirror = true; }
            Debug.Log("[MW] Yol ağı doğrulama: " + ok + "/20 isabet" + (mirror ? (", aynalanmış hal " + okM + "/20 — X ekseni çevrildi") : ""));
            for (int i = 0; i < pts.Count; i++)
            {
                Vector3 p = pts[i];
                if (mirror) p.x = -p.x;
                graph.Add(p + Vector3.up * 0.05f, Mathf.Clamp(lanes[i], 1.6f, 5f));
            }
            foreach (var o in MiniJson.L(data["edges"]))
            {
                var e = MiniJson.L(o);
                graph.Link(MiniJson.I(e[0]), MiniJson.I(e[1]));
            }

            // ---- garaj ----
            var gp = MiniJson.L(data["garage"]);
            garagePos = new Vector3(MiniJson.F(gp[0]) * (mirror ? -1 : 1), MiniJson.F(gp[1]) + 0.3f, MiniJson.F(gp[2]));
            var gd = MiniJson.L(data["garageDir"]);
            garageRot = Quaternion.LookRotation(new Vector3(MiniJson.F(gd[0]) * (mirror ? -1 : 1), 0, MiniJson.F(gd[1])));
            var pad = U.Prim(PrimitiveType.Cylinder, "GarajIsareti", root, garagePos + Vector3.up * 0.05f, new Vector3(9, 0.02f, 9), U.Emissive(new Color(0.1f, 0.4f, 0.1f), new Color(0f, 0.8f, 0.3f)));
            pad.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            U.Icon(pad.transform, new Color(0f, 1f, 0.3f), 14f);
            U.Text3D("GARAJ", pad.transform, new Vector3(0, 60f, 0), Quaternion.identity, 1f, Color.white);

            // ---- yarışlar ----
            foreach (var o in MiniJson.L(data["races"]))
            {
                var r = o as Dictionary<string, object>;
                var def = new RaceDef
                {
                    name = (string)r["name"],
                    type = (RaceType)MiniJson.I(r["type"]),
                    laps = MiniJson.I(r["laps"]),
                    prize = MiniJson.I(r["prize"]),
                };
                var ids = new List<int>();
                foreach (var x in MiniJson.L(r["nodes"])) ids.Add(MiniJson.I(x));
                if (def.type == RaceType.Drag)
                {
                    Vector3 a = graph.nodes[ids[0]], c = graph.nodes[ids[ids.Count - 1]];
                    def.route.Add(a); def.route.Add(c);
                    float lw = graph.lane[ids[0]];
                    def.dragLanes = new[] { -lw * 0.55f, lw * 0.55f };
                }
                else def.route = RouteFromNodes(ids, def.type == RaceType.Circuit);
                foreach (var x in MiniJson.L(r["special"])) def.special.Add(MiniJson.I(x));
                races.Add(def);
            }

            // ---- saklanma ----
            if (data.ContainsKey("hiding"))
                foreach (var o in MiniJson.L(data["hiding"]))
                {
                    var h = MiniJson.L(o);
                    AddHiding("Ara Sokak", new Vector3(MiniJson.F(h[0]) * (mirror ? -1 : 1), MiniJson.F(h[1]), MiniJson.F(h[2])), new Vector3(MiniJson.F(h[3]), 12f, MiniJson.F(h[4])));
                }
            if (data.ContainsKey("labels"))
                foreach (var o in MiniJson.L(data["labels"]))
                {
                    var l = MiniJson.L(o);
                    labels.Add(new KeyValuePair<string, Vector3>((string)l[0], new Vector3(MiniJson.F(l[1]) * (mirror ? -1 : 1), 0, MiniJson.F(l[2]))));
                }
            area = RB(inst.transform);
        }

        static int CountHits(List<Vector3> pts, bool mirror)
        {
            int hits = 0;
            int step = Mathf.Max(1, pts.Count / 20);
            for (int k = 0, i = 0; k < 20 && i < pts.Count; k++, i += step)
            {
                Vector3 p = pts[i];
                if (mirror) p.x = -p.x;
                RaycastHit h;
                if (Physics.Raycast(p + Vector3.up * 30f, Vector3.down, out h, 60f) && Mathf.Abs(h.point.y - p.y) < 4f) hits++;
            }
            return hits;
        }

        static Bounds RB(Transform t)
        {
            bool has = false; Bounds b = new Bounds(t.position, Vector3.one);
            foreach (var r in t.GetComponentsInChildren<Renderer>()) { if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds); }
            return b;
        }
    }
}
