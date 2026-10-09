using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MostWanted.Render
{
    /// <summary>
    /// Araç görünüm kancası (araç ajanı tek satırla çağırır):
    ///   CarLook.Apply(vis, paintMats, body);
    /// - Boya malzemelerinin shader'ını MW/CarPaint yapar (vernik + metalik pul + kir). Aynı Material nesneleri kalır,
    ///   U.ApplyPaint / vinil / renk değişimi aynen çalışır (özellik adları URP/Lit ile aynı).
    /// - Cam → MW/Glass, lastik → MW/Tire, jant/krom → parlak metal (paylaşılan kopyalar; prefab varlıkları değiştirilmez).
    /// - Araç altına yumuşak temas gölgesi ekler (zemini ışınla izler).
    /// Shader bulunamaz/desteklenmezse hiçbir şey değiştirmez (URP/Lit kalır).
    /// </summary>
    public static class CarLook
    {
        static bool init;
        static Shader paintSh, glassSh, tireSh;
        static Material shadowMat;
        static readonly Dictionary<Material, Material> converted = new Dictionary<Material, Material>();

        static Shader Load(string res)
        {
            var m = Resources.Load<Material>(res);
            var s = m != null ? m.shader : null;
            return s != null && s.isSupported ? s : null;
        }

        static void Init()
        {
            if (init) return;
            init = true;
            paintSh = Load("Render/MW_CarPaintMat");
            glassSh = Load("Render/MW_GlassMat");
            tireSh = Load("Render/MW_TireMat");
            var sm = Resources.Load<Material>("Render/MW_CarPaintShadowMat");
            if (sm != null && sm.shader != null && sm.shader.isSupported) shadowMat = sm;
            if (paintSh == null) Debug.LogWarning("[MW] MW/CarPaint shader bulunamadı — araçlar URP/Lit ile çizilecek.");
        }

        static bool Has(string n, params string[] words)
        {
            foreach (var w in words) if (n.Contains(w)) return true;
            return false;
        }

        /// <summary>Araç görselini gerçekçi malzemelere çevirir ve temas gölgesi ekler. vis: araç görsel kökü (CarBuilder "Gorsel").</summary>
        public static void Apply(Transform vis, List<Material> paintMats, Bounds body)
        {
            if (vis == null) return;
            try
            {
                Init();
                if (paintMats != null && paintSh != null)
                    foreach (var m in paintMats) ToPaint(m);

                foreach (var r in vis.GetComponentsInChildren<Renderer>(true))
                {
                    if (r == null || r is ParticleSystemRenderer) continue;
                    var mats = r.sharedMaterials;
                    bool changed = false;
                    string rn = r.name.ToLowerInvariant();
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        if (m == null || (paintMats != null && paintMats.Contains(m))) continue;
                        var c = Convert(m, rn);
                        if (c != null && c != m) { mats[i] = c; changed = true; }
                    }
                    if (changed) r.sharedMaterials = mats;
                }
                AddContactShadow(vis, body);
            }
            catch (System.Exception e) { Debug.LogWarning("[MW] CarLook uygulanamadı: " + e.Message); }
        }

        /// <summary>Boya malzemesine MW/CarPaint uygula (yerinde). Metaliklik/pürüzsüzlük korunur.</summary>
        public static void ToPaint(Material m)
        {
            Init();
            if (m == null || paintSh == null || m.shader == paintSh) return;
            int q = m.renderQueue;
            m.shader = paintSh;
            if (q >= 2450) m.renderQueue = -1;   // saydam/alpha-test kuyruğundan çıkar
            if (m.HasProperty("_ClearCoat")) m.SetFloat("_ClearCoat", 1f);
            if (m.HasProperty("_ClearCoatSmoothness")) m.SetFloat("_ClearCoatSmoothness", 0.94f);
            if (m.HasProperty("_FlakeDensity")) m.SetFloat("_FlakeDensity", 0.35f);
        }

        /// <summary>Kir/toz miktarı (0..1) — hasar ve arazi sürüşü için.</summary>
        public static void SetDirt(IEnumerable<Material> mats, float dirt)
        {
            if (mats == null) return;
            foreach (var m in mats) if (m != null && m.HasProperty("_Dirt")) m.SetFloat("_Dirt", Mathf.Clamp01(dirt));
        }

        static Material Convert(Material m, string rendererName)
        {
            Material c;
            if (converted.TryGetValue(m, out c)) return c;
            string n = m.name.ToLowerInvariant();
            c = null;
            bool glass = Has(n, "glass", "window", "windscreen", "windshield", "cam_", "_cam") || Has(rendererName, "glass", "window", "windshield");
            bool tire = Has(n, "tyre", "tire", "rubber", "lastik");
            bool rim = Has(n, "rim", "jant", "chrome", "krom");
            if (glass && glassSh != null)
            {
                c = new Material(m) { name = m.name + " (MW Cam)" };
                c.shader = glassSh;
                Color bc = c.HasProperty("_BaseColor") ? c.GetColor("_BaseColor") : new Color(0.06f, 0.08f, 0.09f, 0.45f);
                // fazla açık/opak cam renklerini koyu, hafif yeşil-gri renkli cama çek
                float a = Mathf.Clamp(bc.a, 0.35f, 0.7f);
                bc = Color.Lerp(new Color(0.05f, 0.07f, 0.075f), bc, 0.25f);
                bc.a = a;
                c.SetColor("_BaseColor", bc);
                c.renderQueue = 3000;
            }
            else if (tire && tireSh != null)
            {
                c = new Material(m) { name = m.name + " (MW Lastik)" };
                c.shader = tireSh;
                c.renderQueue = -1;
            }
            else if (rim)
            {
                c = new Material(m) { name = m.name + " (MW Jant)" };
                if (c.HasProperty("_Metallic")) c.SetFloat("_Metallic", 1f);
                if (c.HasProperty("_Smoothness")) c.SetFloat("_Smoothness", 0.86f);
                if (c.HasProperty("_EnvironmentReflections")) c.SetFloat("_EnvironmentReflections", 1f);
            }
            converted[m] = c ?? m;
            return c ?? m;
        }

        static void AddContactShadow(Transform vis, Bounds body)
        {
            if (shadowMat == null || vis.parent == null) return;
            var root = vis.parent;
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            q.name = "MW_TemasGolgesi";
            var col = q.GetComponent<Collider>();
            if (col != null) Object.Destroy(col);
            q.transform.SetParent(root, false);
            q.layer = root.gameObject.layer;
            var mr = q.GetComponent<MeshRenderer>();
            mr.sharedMaterial = shadowMat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var cs = q.AddComponent<ContactShadow>();
            cs.root = root;
            cs.size = new Vector2(Mathf.Clamp(body.size.x, 1.5f, 2.8f) * 1.12f, Mathf.Max(3f, body.size.z) * 1.06f);
            cs.center = new Vector3(body.center.x, 0f, body.center.z);
            cs.Place(true);
        }
    }

    /// <summary>Araç altı yumuşak gölge: zemini aşağı ışınla bulur, eğime hizalanır, araç havalanınca küçülür/kaybolur.</summary>
    public class ContactShadow : MonoBehaviour
    {
        public Transform root;
        public Vector2 size = new Vector2(2f, 4.5f);
        public Vector3 center;
        Rigidbody rb;
        Renderer rend;
        int frame;
        static readonly RaycastHit[] hits = new RaycastHit[8];

        void Start()
        {
            rb = root != null ? root.GetComponent<Rigidbody>() : null;
            rend = GetComponent<Renderer>();
            frame = Random.Range(0, 3);
        }

        void LateUpdate()
        {
            if (root == null) return;
            // oyuncu her kare, diğerleri 3 karede bir (konum yine her kare araçla birlikte taşınır çünkü çocuk nesne)
            bool player = Game.I != null && Game.I.player != null && Game.I.player.transform == root;
            if (!player && (++frame % 3) != 0) return;
            Place(false);
        }

        public void Place(bool initial)
        {
            if (root == null) return;
            Vector3 c = root.position + root.rotation * center;
            int mask = ~((1 << OptimizationManager.TrafficLayer) | (1 << U.IconLayer));
            int n = Physics.RaycastNonAlloc(c + Vector3.up * 1.5f, Vector3.down, hits, 6f, mask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue; int bi = -1;
            for (int i = 0; i < n; i++)
            {
                var h = hits[i];
                if (h.collider == null) continue;
                if (rb != null && h.collider.attachedRigidbody == rb) continue;
                if (h.distance < best) { best = h.distance; bi = i; }
            }
            if (bi < 0)
            {
                if (rend != null) rend.enabled = false;
                return;
            }
            var hit = hits[bi];
            float height = Mathf.Max(0f, (c.y - hit.point.y));   // kök (aks/zemin civarı) ile zemin arası
            bool vis = height < 2.5f;
            if (rend != null) rend.enabled = vis;
            if (!vis) return;
            float grow = 1f + Mathf.Clamp01(height / 2.5f) * 0.35f;
            float shrink = 1f - Mathf.Clamp01((height - 0.8f) / 1.7f) * 0.6f;
            transform.position = hit.point + hit.normal * 0.03f;
            transform.rotation = Quaternion.FromToRotation(Vector3.up, hit.normal) * Quaternion.Euler(0f, root.eulerAngles.y, 0f) * Quaternion.Euler(90f, 0f, 0f);
            transform.localScale = new Vector3(size.x * grow * shrink, size.y * grow * shrink, 1f);
        }
    }
}
