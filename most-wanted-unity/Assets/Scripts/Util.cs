using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MostWanted
{
    /// <summary>Yardımcı fonksiyonlar: malzeme, primitive, mesh, fizik uyumluluğu.</summary>
    public static class U
    {
        public const int IconLayer = 30;

        static Shader _std;
        public static bool IsURP;
        /// <summary>URP Lit (yoksa Standard) shader.</summary>
        public static Shader Std
        {
            get
            {
                if (_std == null)
                {
                    var m = Resources.Load<Material>("MW_Lit");
                    if (m != null && m.shader != null && m.shader.isSupported) _std = m.shader;
                    if (_std == null) { var s = Shader.Find("Universal Render Pipeline/Lit"); if (s != null && s.isSupported) _std = s; }
                    if (_std == null) _std = Shader.Find("Standard");
                    if (_std == null) _std = Shader.Find("Legacy Shaders/Diffuse");
                    IsURP = _std != null && _std.name.StartsWith("Universal");
                }
                return _std;
            }
        }

        static readonly Dictionary<string, Material> cache = new Dictionary<string, Material>();

        public static Material Mat(Color c, float smooth = 0.25f, float metal = 0f)
        {
            string key = c.ToString() + "|" + smooth + "|" + metal;
            Material m;
            if (cache.TryGetValue(key, out m) && m != null) return m;
            m = NewMat(c, smooth, metal);
            cache[key] = m;
            return m;
        }

        public static Material NewMat(Color c, float smooth = 0.25f, float metal = 0f)
        {
            var m = new Material(Std);
            SetColor(m, c);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smooth);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smooth);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", metal);
            m.enableInstancing = true;
            return m;
        }

        public static void SetColor(Material m, Color c)
        {
            if (m == null) return;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }

        /// <summary>
        /// Araç boyası uygula: albedo dokusu kaldırılır (paket rengi dokudan geliyor), normal haritası korunur,
        /// renk + metaliklik + parlaklık ayarlanır (cila görünümü).
        /// </summary>
        public static void ApplyPaint(Material m, PaintDef p)
        {
            if (m == null) return;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", null);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", null);
            if (m.HasProperty("baseColorTexture")) m.SetTexture("baseColorTexture", null);
            SetColor(m, p.color);
            if (m.HasProperty("baseColorFactor")) m.SetColor("baseColorFactor", p.color);
            if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", p.metallic);
            if (m.HasProperty("metallicFactor")) m.SetFloat("metallicFactor", p.metallic);
            if (m.HasProperty("_MetallicGlossMap")) m.SetTexture("_MetallicGlossMap", null);
            m.DisableKeyword("_METALLICSPECGLOSSMAP");
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", p.smoothness);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", p.smoothness);
            if (m.HasProperty("roughnessFactor")) m.SetFloat("roughnessFactor", 1f - p.smoothness);
            if (m.HasProperty("_EnvironmentReflections")) m.SetFloat("_EnvironmentReflections", 1f);
            if (m.HasProperty("_SpecularHighlights")) m.SetFloat("_SpecularHighlights", 1f);
        }

        public static void SetMainTex(Material m, Texture t)
        {
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", t);
            if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", t);
        }

        public static void SetMainTexScale(Material m, Vector2 s)
        {
            if (m.HasProperty("_BaseMap")) m.SetTextureScale("_BaseMap", s);
            if (m.HasProperty("_MainTex")) m.SetTextureScale("_MainTex", s);
        }

        public static Material Emissive(Color baseCol, Color emission)
        {
            string key = "E" + baseCol.ToString() + emission.ToString();
            Material m;
            if (cache.TryGetValue(key, out m) && m != null) return m;
            m = NewMat(baseCol, 0.5f, 0f);
            SetEmission(m, emission);
            cache[key] = m;
            return m;
        }

        public static void SetEmission(Material m, Color e)
        {
            if (m == null || !m.HasProperty("_EmissionColor")) return;
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            m.SetColor("_EmissionColor", e);
        }

        static Font _font;
        public static Font BuiltinFont
        {
            get
            {
                if (_font == null)
                {
                    try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch (System.Exception) { }
                    if (_font == null) { try { _font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch (System.Exception) { } }
                }
                return _font;
            }
        }

        /// <summary>3B yazı (polis yazısı, tabelalar).</summary>
        public static GameObject Text3D(string text, Transform parent, Vector3 lpos, Quaternion lrot, float size, Color c)
        {
            var g = new GameObject("Yazi_" + text);
            g.transform.SetParent(parent, false);
            g.transform.localPosition = lpos;
            g.transform.localRotation = lrot;
            var tm = g.AddComponent<TextMesh>();
            tm.text = text;
            tm.font = BuiltinFont;
            tm.fontSize = 64;
            tm.characterSize = size / 10f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.color = c;
            var r = g.GetComponent<MeshRenderer>();
            if (tm.font != null) r.sharedMaterial = tm.font.material;
            r.shadowCastingMode = ShadowCastingMode.Off;
            return g;
        }

        public static GameObject Prim(PrimitiveType t, string name, Transform parent, Vector3 lpos, Vector3 lscale, Material mat, bool keepCollider = false)
        {
            var g = GameObject.CreatePrimitive(t);
            g.name = name;
            if (!keepCollider)
            {
                var c = g.GetComponent<Collider>();
                if (c != null) Object.DestroyImmediate(c);
            }
            if (parent != null) g.transform.SetParent(parent, false);
            g.transform.localPosition = lpos;
            g.transform.localScale = lscale;
            if (mat != null) g.GetComponent<Renderer>().sharedMaterial = mat;
            return g;
        }

        // ---- Unity 2022 / Unity 6 uyumluluğu ----
        public static Vector3 Vel(Rigidbody rb)
        {
#if UNITY_6000_0_OR_NEWER
            return rb.linearVelocity;
#else
            return rb.velocity;
#endif
        }

        public static void SetVel(Rigidbody rb, Vector3 v)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = v;
#else
            rb.velocity = v;
#endif
        }

        public static void SetDamping(Rigidbody rb, float lin, float ang)
        {
#if UNITY_6000_0_OR_NEWER
            rb.linearDamping = lin;
            rb.angularDamping = ang;
#else
            rb.drag = lin;
            rb.angularDrag = ang;
#endif
        }

        public static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

        public static float FlatDist(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Görüş hattı: sadece statik (rigidbody'siz) objeler engeller.</summary>
        public static bool LineOfSight(Vector3 a, Vector3 b)
        {
            RaycastHit h;
            if (!Physics.Linecast(a, b, out h)) return true;
            return h.rigidbody != null;
        }

        /// <summary>Dünya ölçüsünde UV'li kutu mesh'i (pivot merkezde).</summary>
        public static Mesh BoxMesh(Vector3 size, float uvTile)
        {
            var verts = new List<Vector3>();
            var norms = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            Vector3 half = size * 0.5f;
            Vector3[] ns = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var n in ns)
            {
                Vector3 v = (Mathf.Abs(n.y) > 0.5f) ? Vector3.forward : Vector3.up;
                Vector3 u = Vector3.Cross(v, -n);
                float hu = Mathf.Abs(Vector3.Dot(u, half));
                float hv = Mathf.Abs(Vector3.Dot(v, half));
                float hn = Mathf.Abs(Vector3.Dot(n, half));
                Vector3 c = n * hn;
                int b = verts.Count;
                verts.Add(c - u * hu - v * hv); // BL
                verts.Add(c + u * hu - v * hv); // BR
                verts.Add(c + u * hu + v * hv); // TR
                verts.Add(c - u * hu + v * hv); // TL
                float uw = 2f * hu / uvTile, vh = 2f * hv / uvTile;
                uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(uw, 0)); uvs.Add(new Vector2(uw, vh)); uvs.Add(new Vector2(0, vh));
                for (int i = 0; i < 4; i++) norms.Add(n);
                tris.Add(b); tris.Add(b + 3); tris.Add(b + 2);
                tris.Add(b); tris.Add(b + 2); tris.Add(b + 1);
            }
            var m = new Mesh();
            m.SetVertices(verts);
            m.SetNormals(norms);
            m.SetUVs(0, uvs);
            m.SetTriangles(tris, 0);
            m.RecalculateBounds();
            return m;
        }

        static Mesh _arrow;
        public static Mesh ArrowMesh()
        {
            if (_arrow != null) return _arrow;
            var m = new Mesh();
            m.vertices = new[] { new Vector3(0, 0, 1f), new Vector3(0.75f, 0, -0.8f), new Vector3(0, 0, -0.35f), new Vector3(-0.75f, 0, -0.8f) };
            // iki yüzlü
            m.triangles = new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
            m.RecalculateNormals();
            m.RecalculateBounds();
            _arrow = m;
            return m;
        }

        /// <summary>Minimap için yukarıdan görünen ikon (sadece minimap kamerası görür).</summary>
        public static GameObject Icon(Transform parent, Color c, float size, bool arrow = false)
        {
            GameObject g;
            if (arrow)
            {
                g = new GameObject("MapIcon");
                g.AddComponent<MeshFilter>().sharedMesh = ArrowMesh();
                g.AddComponent<MeshRenderer>();
                g.transform.SetParent(parent, false);
                g.transform.localPosition = new Vector3(0, 200f, 0);
                g.transform.localScale = Vector3.one * size;
            }
            else
            {
                g = Prim(PrimitiveType.Quad, "MapIcon", parent, new Vector3(0, 200f, 0), Vector3.one * size, null);
                g.transform.localRotation = Quaternion.Euler(90, 0, 0);
            }
            var r = g.GetComponent<Renderer>();
            r.sharedMaterial = Emissive(Color.black, c);
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            g.layer = IconLayer;
            return g;
        }

        public static string Money(int v)
        {
            return "$" + v.ToString("N0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', '.');
        }
    }
}
