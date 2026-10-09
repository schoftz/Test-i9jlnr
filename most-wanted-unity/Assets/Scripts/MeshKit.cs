using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MostWanted
{
    /// <summary>Alt-mesh destekli basit mesh oluşturucu (parça birleştirme için).</summary>
    public class MeshKit
    {
        public readonly List<Vector3> v = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<Vector3> n = new List<Vector3>();
        public readonly List<List<int>> sub = new List<List<int>>();

        public MeshKit(int subMeshes) { for (int i = 0; i < subMeshes; i++) sub.Add(new List<int>()); }
        public int Count { get { return v.Count; } }
        public bool Empty { get { foreach (var s in sub) if (s.Count > 0) return false; return true; } }

        public int Vert(Vector3 p, Vector2 t, Vector3 nn) { v.Add(p); uv.Add(t); n.Add(nn); return v.Count - 1; }

        public void Tri(int s, int a, int b, int c) { var l = sub[s]; l.Add(a); l.Add(b); l.Add(c); }

        /// <summary>Dörtgen (saat yönü: a sol-alt, b sol-üst, c sağ-üst, d sağ-alt — yukarıdan bakınca).</summary>
        public void Quad(int s, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ta, Vector2 tb, Vector2 tc, Vector2 td)
        {
            Vector3 nn = Vector3.Cross(b - a, d - a).normalized;
            int i0 = Vert(a, ta, nn), i1 = Vert(b, tb, nn), i2 = Vert(c, tc, nn), i3 = Vert(d, td, nn);
            Tri(s, i0, i1, i2); Tri(s, i0, i2, i3);
        }

        /// <summary>Döndürülmüş kutu (y0 tabandan h yükseklik). Duvar UV'leri dünya ölçüsünde (m).</summary>
        public void Box(int sideSub, int topSub, Vector3 center, float w, float d, float rotY, float y0, float h, bool bottom = false)
        {
            Quaternion q = Quaternion.Euler(0, rotY, 0);
            Vector3 r = q * Vector3.right * (w * 0.5f), f = q * Vector3.forward * (d * 0.5f);
            Vector3 b0 = center + Vector3.up * y0, t0 = center + Vector3.up * (y0 + h);
            Vector3[] c = { -r - f, -r + f, r + f, r - f }; // sol-arka, sol-ön, sağ-ön, sağ-arka
            float u = 0f;
            for (int i = 0; i < 4; i++)
            {
                Vector3 p0 = c[i], p1 = c[(i + 1) % 4];
                float len = (p1 - p0).magnitude;
                // dışa bakan yüz: p1→p0 sırası (saat yönü dışarıdan)
                Quad(sideSub, b0 + p1, t0 + p1, t0 + p0, b0 + p0,
                     new Vector2(u + len, y0), new Vector2(u + len, y0 + h), new Vector2(u, y0 + h), new Vector2(u, y0));
                u += len;
            }
            Quad(topSub, t0 + c[0], t0 + c[1], t0 + c[2], t0 + c[3],
                 new Vector2(c[0].x, c[0].z), new Vector2(c[1].x, c[1].z), new Vector2(c[2].x, c[2].z), new Vector2(c[3].x, c[3].z));
            if (bottom) Quad(topSub, b0 + c[3], b0 + c[2], b0 + c[1], b0 + c[0], Vector2.zero, Vector2.zero, Vector2.zero, Vector2.zero);
        }

        /// <summary>Dışa bakan dörtgen: sarım yönü dış normale göre otomatik düzeltilir.</summary>
        public void QuadOut(int s, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, d - a), outward) < 0f) { var t = b; b = d; d = t; }
            Quad(s, a, b, c, d, new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0));
        }

        /// <summary>Herhangi bir yönelimde kutu (merkez, dönüş, boyut).</summary>
        public void OBox(int s, Vector3 c, Quaternion q, Vector3 size)
        {
            Vector3 x = q * Vector3.right * (size.x * 0.5f), y = q * Vector3.up * (size.y * 0.5f), z = q * Vector3.forward * (size.z * 0.5f);
            QuadOut(s, c - x - y + z, c - x + y + z, c + x + y + z, c + x - y + z, z);
            QuadOut(s, c - x - y - z, c - x + y - z, c + x + y - z, c + x - y - z, -z);
            QuadOut(s, c + x - y - z, c + x + y - z, c + x + y + z, c + x - y + z, x);
            QuadOut(s, c - x - y - z, c - x + y - z, c - x + y + z, c - x - y + z, -x);
            QuadOut(s, c - x + y - z, c - x + y + z, c + x + y + z, c + x + y - z, y);
            QuadOut(s, c - x - y - z, c - x - y + z, c + x - y + z, c + x - y - z, -y);
        }

        /// <summary>Silindir: merkez, eksen yönü, yarıçap, uzunluk.</summary>
        public void Cyl(int s, Vector3 c, Vector3 axis, float r, float len, int seg = 14, bool caps = true)
        {
            axis.Normalize();
            Vector3 u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized, w = Vector3.Cross(axis, u);
            Vector3 h = axis * (len * 0.5f);
            for (int i = 0; i < seg; i++)
            {
                float a0 = i * Mathf.PI * 2f / seg, a1 = (i + 1) * Mathf.PI * 2f / seg;
                Vector3 p0 = (u * Mathf.Cos(a0) + w * Mathf.Sin(a0)) * r, p1 = (u * Mathf.Cos(a1) + w * Mathf.Sin(a1)) * r;
                QuadOut(s, c - h + p0, c + h + p0, c + h + p1, c - h + p1, p0 + p1);
                if (caps)
                {
                    int a = Vert(c + h, Vector2.zero, axis), b = Vert(c + h + p0, Vector2.zero, axis), d = Vert(c + h + p1, Vector2.zero, axis);
                    if (Vector3.Dot(Vector3.Cross(p0, p1 - p0), axis) > 0f) Tri(s, a, b, d); else Tri(s, a, d, b);
                    int e = Vert(c - h, Vector2.zero, -axis), f = Vert(c - h + p0, Vector2.zero, -axis), gg = Vert(c - h + p1, Vector2.zero, -axis);
                    if (Vector3.Dot(Vector3.Cross(p0, p1 - p0), -axis) > 0f) Tri(s, e, f, gg); else Tri(s, e, gg, f);
                }
            }
        }

        public Mesh Build(string name)
        {
            var m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv);
            m.subMeshCount = sub.Count;
            for (int i = 0; i < sub.Count; i++) m.SetTriangles(sub[i], i);
            m.RecalculateBounds();
            return m;
        }

        /// <summary>Tek alt-mesh, sadece pozisyon/üçgen (çarpıştırıcı için).</summary>
        public Mesh BuildCollider(string name)
        {
            var m = new Mesh { name = name };
            if (v.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(v);
            var all = new List<int>();
            foreach (var s in sub) all.AddRange(s);
            m.SetTriangles(all, 0);
            m.RecalculateBounds();
            return m;
        }
    }

    /// <summary>GPU instancing ile çok sayıda aynı obje (250 m hücreler, mesafe ile kesme).</summary>
    public class InstancedBatch
    {
        public Mesh mesh; public Material[] mats; public float cull = 500f; public int layer = OptimizationManager.DetailLayer;
        public ShadowCastingMode shadows = ShadowCastingMode.On;
        readonly Dictionary<long, List<Matrix4x4>> cells = new Dictionary<long, List<Matrix4x4>>();
        readonly List<KeyValuePair<Vector3, Matrix4x4[]>> baked = new List<KeyValuePair<Vector3, Matrix4x4[]>>();

        public void Add(Vector3 p, Quaternion r, Vector3 s)
        {
            long k = ((long)Mathf.FloorToInt(p.x / 250f) << 32) ^ (uint)Mathf.FloorToInt(p.z / 250f);
            List<Matrix4x4> l;
            if (!cells.TryGetValue(k, out l)) cells[k] = l = new List<Matrix4x4>();
            l.Add(Matrix4x4.TRS(p, r, s));
        }

        public void Bake()
        {
            foreach (var kv in cells)
            {
                var l = kv.Value;
                for (int i = 0; i < l.Count; i += 1000)
                {
                    var arr = l.GetRange(i, Mathf.Min(1000, l.Count - i)).ToArray();
                    Vector3 c = Vector3.zero;
                    foreach (var m in arr) c += (Vector3)m.GetColumn(3);
                    baked.Add(new KeyValuePair<Vector3, Matrix4x4[]>(c / arr.Length, arr));
                }
            }
            cells.Clear();
        }

        public void Draw(Vector3 cam, float cullMul)
        {
            if (mesh == null) return;
            float cd = cull * cullMul + 180f;
            for (int b = 0; b < baked.Count; b++)
            {
                var kv = baked[b];
                if (U.FlatDist(kv.Key, cam) > cd) continue;
                for (int s = 0; s < mats.Length && s < mesh.subMeshCount; s++)
                {
                    var rp = new RenderParams(mats[s]) { shadowCastingMode = shadows, receiveShadows = true, layer = layer, worldBounds = new Bounds(kv.Key, new Vector3(400f, 400f, 400f)) };
                    Graphics.RenderMeshInstanced(rp, mesh, s, kv.Value);
                }
            }
        }
    }
}
