using UnityEngine;
using UnityEngine.Rendering;

namespace MostWanted
{
    /// <summary>
    /// Lastik izleri: tek paylaşılan halka tampon mesh (en fazla Max dörtgen), tekerlek başına son nokta.
    /// Tek çizim çağrısı; en eski izler yenileriyle değiştirilir.
    /// </summary>
    public class SkidMarks : MonoBehaviour
    {
        public static SkidMarks I;
        const int Max = 2400;
        Vector3[] verts = new Vector3[Max * 4];
        Vector3[] norms = new Vector3[Max * 4];
        Vector2[] uvs = new Vector2[Max * 4];
        Color32[] cols = new Color32[Max * 4];
        int[] tris = new int[Max * 6];
        int next;
        bool dirty;
        Mesh mesh;

        struct Last { public Vector3 pos, l, r; public byte a; public bool valid; public int frame; }
        readonly System.Collections.Generic.Dictionary<int, Last> last = new System.Collections.Generic.Dictionary<int, Last>();

        public static SkidMarks Get()
        {
            if (I != null) return I;
            var g = new GameObject("LastikIzleri");
            I = g.AddComponent<SkidMarks>();
            I.Init();
            return I;
        }

        void Init()
        {
            for (int i = 0; i < Max; i++)
            {
                int v = i * 4, t = i * 6;
                tris[t] = v; tris[t + 1] = v + 1; tris[t + 2] = v + 2; tris[t + 3] = v; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
                uvs[v] = new Vector2(0, 0); uvs[v + 1] = new Vector2(0, 1); uvs[v + 2] = new Vector2(1, 1); uvs[v + 3] = new Vector2(1, 0);
            }
            mesh = new Mesh { name = "LastikIzi" };
            mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = verts; mesh.normals = norms; mesh.uv = uvs; mesh.colors32 = cols; mesh.triangles = tris;
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            mesh.MarkDynamic();
            gameObject.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = gameObject.AddComponent<MeshRenderer>();
            Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (sh == null) sh = Shader.Find("Sprites/Default");
            var m = new Material(sh);
            var tex = new Texture2D(16, 16, TextureFormat.RGBA32, false);
            for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++) { float e = Mathf.Min(x, 15 - x) / 3f; tex.SetPixel(x, y, new Color(0.04f, 0.04f, 0.04f, Mathf.Clamp01(e) * (0.75f + 0.25f * Mathf.PerlinNoise(x * 0.7f, y * 0.3f)))); }
            tex.Apply();
            U.SetMainTex(m, tex);
            if (m.HasProperty("_Surface")) { m.SetFloat("_Surface", 1f); m.SetOverrideTag("RenderType", "Transparent"); }
            m.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = 2990;
            mr.sharedMaterial = m;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        /// <summary>İz ekle (key: tekerlek kimliği). intensity 0..1; 0 = izi kes.</summary>
        public void Add(int key, Vector3 pos, Vector3 normal, Vector3 right, float width, float intensity)
        {
            Last l;
            last.TryGetValue(key, out l);
            if (intensity <= 0.02f) { if (l.valid) { l.valid = false; last[key] = l; } return; }
            pos += normal * 0.025f;
            Vector3 hw = right.normalized * (width * 0.5f);
            byte a = (byte)Mathf.Clamp(intensity * 230f, 0f, 230f);
            if (l.valid && (pos - l.pos).sqrMagnitude < 0.09f) return;   // en az 30 cm aralık
            if (l.valid && (pos - l.pos).sqrMagnitude < 16f && Time.frameCount - l.frame < 20)
            {
                int v = next * 4;
                verts[v] = l.l; verts[v + 1] = pos - hw; verts[v + 2] = pos + hw; verts[v + 3] = l.r;
                for (int k = 0; k < 4; k++) norms[v + k] = normal;
                cols[v] = cols[v + 3] = new Color32(255, 255, 255, l.a);
                cols[v + 1] = cols[v + 2] = new Color32(255, 255, 255, a);
                next = (next + 1) % Max;
                dirty = true;
            }
            l.pos = pos; l.l = pos - hw; l.r = pos + hw; l.a = a; l.valid = true; l.frame = Time.frameCount;
            last[key] = l;
        }

        void LateUpdate()
        {
            if (!dirty) return;
            dirty = false;
            mesh.vertices = verts; mesh.normals = norms; mesh.colors32 = cols;
        }
    }
}
