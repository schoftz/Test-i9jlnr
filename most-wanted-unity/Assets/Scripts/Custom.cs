using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MostWanted
{
    /// <summary>Bir aracın görsel parçaları (özelleştirme kiti bunlara göre kurulur).</summary>
    public class CarParts
    {
        public CarEntry def;
        public Transform vis;
        public Bounds body;
        public Vector3[] wPos = new Vector3[4];
        public float[] wR = new float[4];
        public Transform[] wVis = new Transform[4];
        public List<Material> paintMats = new List<Material>();
        public List<Material> headMats = new List<Material>();
        public List<Vector3> tips = new List<Vector3>();
        // özgün durum (yeniden uygulamada geri dönmek için)
        public Vector3[] wVisLocal;
        public Dictionary<Material, Color> glassOrig;
        public float GroundY { get { return wPos[0].y - wR[0]; } }
    }

    /// <summary>Fast &amp; Furious özelleştirme kataloğu (sadece Drift Araçları: Supra, R34, R35, M4).</summary>
    public static class CustomCatalog
    {
        public const int Count = 19;
        public const int Lip = 0, Skirt = 1, Diffuser = 2, Wide = 3, Spoiler = 4, Hood = 5, Scoop = 6, Rim = 7, RimColor = 8,
            Ride = 9, Camber = 10, Neon = 11, Tint = 12, Head = 13, Vinyl = 14, Sticker = 15, Plate = 16, Exhaust = 17, Drift = 18;

        public static readonly string[] Names =
        {
            "Ön Lip / Splitter", "Yan Marşpiyel", "Difüzör", "Geniş Kasa", "Spoiler", "Kaput", "Tavan Scoop", "Jant", "Jant Rengi",
            "Sürüş Yüksekliği", "Kamber", "Neon Alt Işık", "Cam Filmi", "Far Rengi", "Vinil", "Çıkartma", "Plaka", "Egzoz Ucu", "DRIFT AYARI",
        };
        public static readonly string[][] Options =
        {
            new[] { "Yok", "Lip", "Karbon Splitter" },
            new[] { "Yok", "Marşpiyel" },
            new[] { "Yok", "Karbon Difüzör" },
            new[] { "Yok", "Çamurluk Genişletme" },
            new[] { "Yok", "Ducktail", "GT Kanat", "Dev F&F Kanat" },
            new[] { "Orijinal", "Havalandırmalı", "Karbon" },
            new[] { "Yok", "Scoop" },
            new[] { "Orijinal", "5 Kollu", "6 Kollu", "Ağ (Mesh)", "Derin Çanak", "Türbin", "3 Kollu" },
            new[] { "Gümüş", "Siyah", "Altın", "Bronz", "Beyaz", "Kırmızı", "Turkuaz" },
            new[] { "Orijinal", "-2 cm", "-4 cm", "-6 cm" },
            new[] { "0°", "2°", "4°", "7°" },
            new[] { "Yok", "Turkuaz", "Mor", "Yeşil", "Kırmızı", "Mavi", "Pembe" },
            new[] { "Yok", "%35", "%20", "%5 Limo" },
            new[] { "Beyaz", "Ksenon Mavi", "Sarı", "Mor" },
            new[] { "Yok", "Yarış Şeritleri", "R34 Mavi/Gümüş", "Yan Grafik", "Alevler" },
            new[] { "Yok", "NOS", "走り屋", "NOS + 走り屋" },
            new[] { "Orijinal", "\"FAST\"" },
            new[] { "Orijinal", "Büyük Yuvarlak", "Çift", "Kare", "Top (Canon)" },
            new[] { "Kapalı", "Açık" },
        };
        static readonly int[] BaseCost = { 900, 800, 1100, 2500, 1500, 1200, 600, 1800, 400, 900, 500, 1200, 300, 400, 1500, 300, 200, 700, 3000 };

        public static readonly Color[] RimColors = { new Color(0.75f, 0.76f, 0.78f), new Color(0.05f, 0.05f, 0.06f), new Color(0.85f, 0.65f, 0.2f), new Color(0.55f, 0.35f, 0.18f), new Color(0.92f, 0.92f, 0.92f), new Color(0.8f, 0.06f, 0.05f), new Color(0.2f, 0.85f, 0.8f) };
        public static readonly Color[] NeonColors = { Color.black, new Color(0.2f, 1f, 0.9f), new Color(0.65f, 0.2f, 1f), new Color(0.2f, 1f, 0.3f), new Color(1f, 0.1f, 0.1f), new Color(0.15f, 0.35f, 1f), new Color(1f, 0.25f, 0.7f) };
        public static readonly Color[] HeadColors = { new Color(1f, 0.96f, 0.85f), new Color(0.65f, 0.8f, 1f), new Color(1f, 0.82f, 0.35f), new Color(0.7f, 0.45f, 1f) };

        public static bool IsDriftCar(CarEntry def)
        {
            if (def == null) return false;
            string n = (def.displayName + " " + def.id).ToLowerInvariant();
            return n.Contains("supra") || n.Contains("r34") || n.Contains("r35") || n.Contains("skyline") || n.Contains("gt-r") || n.Contains("gtr") || n.Contains(" m4") || n.StartsWith("m4") || n.Contains("bmw m4");
        }

        /// <summary>Kademe ölçekli fiyat (pahalı araçta parçalar da pahalı).</summary>
        public static int Price(CarEntry def, int cat, int opt)
        {
            if (opt <= 0) return 0;
            float tier = 1f + (def != null ? def.price : 50000) / 60000f;
            float p = BaseCost[cat] * tier * (1f + 0.25f * (opt - 1));
            return Mathf.RoundToInt(p / 50f) * 50;
        }

        public static CustomSave Get(CarSave cs)
        {
            if (cs == null) return null;
            if (cs.custom == null) cs.custom = new CustomSave();
            cs.custom.Fix();
            return cs.custom;
        }
    }

    [System.Serializable]
    public class CustomSave
    {
        public int[] v = new int[CustomCatalog.Count];
        public int[] own = new int[CustomCatalog.Count];   // satın alınan seçenekler (bit maskesi)
        public void Fix()
        {
            if (v == null || v.Length != CustomCatalog.Count) { var n = new int[CustomCatalog.Count]; if (v != null) for (int i = 0; i < Mathf.Min(v.Length, n.Length); i++) n[i] = v[i]; v = n; }
            if (own == null || own.Length != CustomCatalog.Count) { var n = new int[CustomCatalog.Count]; if (own != null) for (int i = 0; i < Mathf.Min(own.Length, n.Length); i++) n[i] = own[i]; own = n; }
            for (int i = 0; i < CustomCatalog.Count; i++) { v[i] = Mathf.Clamp(v[i], 0, CustomCatalog.Options[i].Length - 1); own[i] |= 1; }
        }
        public bool Owns(int cat, int opt) { return (own[cat] & (1 << opt)) != 0; }
        public bool DriftSetup { get { return v[CustomCatalog.Drift] == 1; } }
    }

    /// <summary>
    /// Prosedürel F&amp;F kiti: tüm gövde parçaları tek birleşik mesh (alt-mesh: boya/karbon/plastik/krom/neon), paylaşılan
    /// malzemeler; jantlar stil başına tek paylaşılan mesh; vinil = kutu eşlemeli ikinci malzeme katmanı (MW_Vinyl).
    /// </summary>
    public static class CustomKit
    {
        static Material carbon, plastic, chrome, titanium, clearVinyl, plateMat;
        static readonly Dictionary<int, Material> rimMats = new Dictionary<int, Material>();
        static readonly Dictionary<int, Material> neonMats = new Dictionary<int, Material>();
        static readonly Dictionary<int, Mesh> rimMeshes = new Dictionary<int, Mesh>();
        static readonly Dictionary<int, Texture2D> vinylTex = new Dictionary<int, Texture2D>();
        static Material vinylBase; static bool vinylTried;

        static void InitMats()
        {
            if (carbon != null) return;
            carbon = U.NewMat(new Color(0.12f, 0.12f, 0.13f), 0.85f, 0.3f);
            var t = new Texture2D(32, 32, TextureFormat.RGBA32, true);
            for (int y = 0; y < 32; y++) for (int x = 0; x < 32; x++)
                {
                    bool a = ((x / 4) + (y / 4)) % 2 == 0;
                    float v = a ? 0.55f + 0.1f * ((x % 4) / 4f) : 0.3f + 0.1f * ((y % 4) / 4f);
                    t.SetPixel(x, y, new Color(v, v, v, 1));
                }
            t.Apply(true); t.wrapMode = TextureWrapMode.Repeat;
            U.SetMainTex(carbon, t); U.SetMainTexScale(carbon, new Vector2(6f, 6f));
            plastic = U.NewMat(new Color(0.04f, 0.04f, 0.045f), 0.35f, 0f);
            chrome = U.NewMat(new Color(0.85f, 0.86f, 0.88f), 0.95f, 1f);
            titanium = U.NewMat(new Color(0.45f, 0.35f, 0.55f), 0.9f, 1f);
            plateMat = U.NewMat(new Color(0.95f, 0.95f, 0.9f), 0.4f, 0f);
        }

        static Material RimMat(int c)
        {
            Material m;
            if (!rimMats.TryGetValue(c, out m)) rimMats[c] = m = U.NewMat(CustomCatalog.RimColors[Mathf.Clamp(c, 0, CustomCatalog.RimColors.Length - 1)], 0.85f, 0.9f);
            return m;
        }

        static Material NeonMat(int c)
        {
            Material m;
            if (!neonMats.TryGetValue(c, out m)) { var col = CustomCatalog.NeonColors[c]; neonMats[c] = m = U.Emissive(col * 0.3f, col * 6f); }
            return m;
        }

        /// <summary>Kiti uygula (önceki kit kaldırılır). car null olabilir (garaj önizleme).</summary>
        public static void Apply(CarParts p, CustomSave cs, CarController car, bool showroom)
        {
            InitMats();
            if (p == null || p.vis == null || cs == null) return;
            var old = p.vis.Find("FF_Kit"); if (old != null) Object.Destroy(old.gameObject);
            for (int i = 0; i < 4; i++)
                if (p.wVis[i] != null)
                {
                    var r = p.wVis[i].Find("FF_Rim"); if (r != null) Object.Destroy(r.gameObject);
                }
            foreach (var r in p.vis.GetComponentsInChildren<Transform>(true)) if (r.name == "FF_Vinyl") Object.Destroy(r.gameObject);
            if (p.wVisLocal == null) { p.wVisLocal = new Vector3[4]; for (int i = 0; i < 4; i++) if (p.wVis[i] != null) p.wVisLocal[i] = p.wVis[i].localPosition; }

            var v = cs.v;
            var root = new GameObject("FF_Kit").transform;
            root.SetParent(p.vis, false);
            Bounds b = p.body;
            float W = b.size.x, L = b.size.z, H = b.size.y, gy = p.GroundY;
            float front = b.max.z, rear = b.min.z;
            float trunkY = gy + Mathf.Max(0.8f, (b.max.y - gy) * 0.66f);
            float hoodY = gy + Mathf.Max(0.75f, (b.max.y - gy) * 0.62f);
            var kit = new MeshKit(5);   // 0 boya, 1 karbon, 2 plastik, 3 krom, 4 neon
            Quaternion id = Quaternion.identity;

            // ön lip / splitter
            if (v[CustomCatalog.Lip] == 1) kit.OBox(2, new Vector3(0, gy + 0.11f, front - 0.08f), id, new Vector3(W * 0.88f, 0.05f, 0.24f));
            if (v[CustomCatalog.Lip] == 2)
            {
                kit.OBox(1, new Vector3(0, gy + 0.09f, front - 0.04f), id, new Vector3(W * 0.96f, 0.035f, 0.34f));
                for (int s = -1; s <= 1; s += 2) kit.OBox(1, new Vector3(s * W * 0.44f, gy + 0.3f, front - 0.12f), Quaternion.Euler(0, 0, s * 12f), new Vector3(0.18f, 0.02f, 0.12f));
            }
            // yan marşpiyel
            if (v[CustomCatalog.Skirt] == 1)
            {
                float z0 = p.wPos[2].z + p.wR[2] + 0.06f, z1 = p.wPos[0].z - p.wR[0] - 0.06f;
                for (int s = -1; s <= 1; s += 2) kit.OBox(0, new Vector3(s * (W * 0.5f - 0.02f), gy + 0.17f, (z0 + z1) * 0.5f), id, new Vector3(0.1f, 0.12f, Mathf.Max(0.3f, z1 - z0)));
            }
            // difüzör
            if (v[CustomCatalog.Diffuser] == 1)
            {
                kit.OBox(1, new Vector3(0, gy + 0.1f, rear + 0.14f), Quaternion.Euler(-10f, 0, 0), new Vector3(W * 0.7f, 0.04f, 0.34f));
                for (int f = -2; f <= 2; f++) kit.OBox(1, new Vector3(f * W * 0.13f, gy + 0.15f, rear + 0.12f), id, new Vector3(0.02f, 0.12f, 0.3f));
            }
            // geniş kasa (çamurluk kemerleri)
            if (v[CustomCatalog.Wide] == 1)
                for (int i = 0; i < 4; i++)
                {
                    float s = p.wPos[i].x < 0 ? -1f : 1f, R = p.wR[i];
                    Vector3 c = new Vector3(s * (Mathf.Abs(p.wPos[i].x) + 0.1f), p.wPos[i].y, p.wPos[i].z);
                    for (int k = 0; k < 7; k++)
                    {
                        float a = Mathf.Lerp(-80f, 80f, k / 6f) * Mathf.Deg2Rad;
                        Vector3 o = new Vector3(0, Mathf.Cos(a), Mathf.Sin(a)) * (R + 0.06f);
                        kit.OBox(0, c + o, Quaternion.Euler(-a * Mathf.Rad2Deg, 0, 0), new Vector3(0.16f, 0.07f, R * 0.5f));
                    }
                }
            // spoiler
            int sp = v[CustomCatalog.Spoiler];
            if (sp == 1)
            {
                kit.OBox(0, new Vector3(0, trunkY + 0.03f, rear + 0.16f), Quaternion.Euler(-14f, 0, 0), new Vector3(W * 0.78f, 0.05f, 0.26f));
            }
            else if (sp >= 2)
            {
                bool big = sp == 3;
                float hgt = big ? 0.5f : 0.3f, wz = rear + (big ? 0.32f : 0.28f);
                for (int s = -1; s <= 1; s += 2) kit.OBox(2, new Vector3(s * W * 0.27f, trunkY + hgt * 0.5f, wz), id, new Vector3(0.04f, hgt, 0.12f));
                kit.OBox(big ? 0 : 1, new Vector3(0, trunkY + hgt + 0.02f, wz - 0.02f), Quaternion.Euler(-8f, 0, 0), new Vector3(W * (big ? 1.02f : 0.94f), 0.04f, big ? 0.42f : 0.3f));
                if (big) kit.OBox(1, new Vector3(0, trunkY + hgt + 0.12f, wz - 0.12f), Quaternion.Euler(-14f, 0, 0), new Vector3(W * 0.98f, 0.03f, 0.2f));
                for (int s = -1; s <= 1; s += 2) kit.OBox(big ? 1 : 0, new Vector3(s * W * (big ? 0.51f : 0.47f), trunkY + hgt - 0.02f, wz - 0.02f), id, new Vector3(0.02f, big ? 0.3f : 0.18f, big ? 0.5f : 0.36f));
            }
            // kaput
            float hz0 = p.wPos[0].z - 0.1f, hz1 = front - 0.3f;
            if (v[CustomCatalog.Hood] == 1)
                for (int s = -1; s <= 1; s += 2)
                    for (int k = 0; k < 4; k++) kit.OBox(2, new Vector3(s * W * 0.17f, hoodY + 0.01f, Mathf.Lerp(hz0, hz1, 0.25f) + k * 0.09f), id, new Vector3(W * 0.17f, 0.025f, 0.04f));
            if (v[CustomCatalog.Hood] == 2) kit.OBox(1, new Vector3(0, hoodY, (hz0 + hz1) * 0.5f), Quaternion.Euler(4f, 0, 0), new Vector3(W * 0.7f, 0.02f, Mathf.Max(0.4f, hz1 - hz0)));
            // tavan scoop
            if (v[CustomCatalog.Scoop] == 1)
            {
                kit.OBox(0, new Vector3(0, b.max.y + 0.03f, b.center.z + 0.1f), id, new Vector3(0.5f, 0.08f, 0.55f));
                kit.OBox(2, new Vector3(0, b.max.y + 0.04f, b.center.z + 0.38f), id, new Vector3(0.42f, 0.05f, 0.02f));
            }
            // neon
            int ne = v[CustomCatalog.Neon];
            if (ne > 0)
            {
                kit.OBox(4, new Vector3(0, gy + 0.13f, b.center.z), id, new Vector3(W * 0.78f, 0.01f, L * 0.7f));
                var lg = new GameObject("FF_Neon"); lg.transform.SetParent(root, false); lg.transform.localPosition = new Vector3(0, gy + 0.16f, b.center.z);
                var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = CustomCatalog.NeonColors[ne]; l.range = 4.5f; l.intensity = 4f; l.shadows = LightShadows.None;
            }
            // egzoz uçları
            int ex = v[CustomCatalog.Exhaust];
            if (ex > 0 && p.tips != null)
                foreach (var t in p.tips)
                {
                    Vector3 c = t + new Vector3(0, 0, -0.04f);
                    if (ex == 1) kit.Cyl(3, c, Vector3.forward, 0.055f, 0.2f);
                    else if (ex == 2) { kit.Cyl(3, c + Vector3.right * 0.045f, Vector3.forward, 0.035f, 0.2f); kit.Cyl(3, c - Vector3.right * 0.045f, Vector3.forward, 0.035f, 0.2f); }
                    else if (ex == 3) kit.OBox(3, c, id, new Vector3(0.11f, 0.07f, 0.2f));
                    else kit.Cyl(3, c + new Vector3(0, 0, -0.04f), Vector3.forward, 0.075f, 0.32f, 16);
                }

            if (!kit.Empty)
            {
                var mg = new GameObject("FF_KitMesh");
                mg.transform.SetParent(root, false);
                mg.AddComponent<MeshFilter>().sharedMesh = kit.Build("FF_Kit");
                var mr = mg.AddComponent<MeshRenderer>();
                Material paint = p.paintMats.Count > 0 ? p.paintMats[0] : plastic;
                mr.sharedMaterials = new[] { paint, carbon, plastic, ex == 4 ? titanium : chrome, ne > 0 ? NeonMat(ne) : plastic };
                mr.shadowCastingMode = ShadowCastingMode.On;
            }

            // plaka + çıkartmalar (Text3D)
            if (v[CustomCatalog.Plate] == 1)
            {
                var pl = U.Prim(PrimitiveType.Cube, "FF_Plate", root, new Vector3(0, gy + 0.45f, rear - 0.005f), new Vector3(0.52f, 0.12f, 0.01f), plateMat);
                Object.Destroy(pl.GetComponent<Collider>());
                U.Text3D("FAST", root, new Vector3(0, gy + 0.45f, rear - 0.015f), Quaternion.Euler(0, 180, 0), 0.9f, new Color(0.05f, 0.05f, 0.1f));
            }
            int st = v[CustomCatalog.Sticker];
            for (int s = -1; s <= 1; s += 2)
            {
                Quaternion q = Quaternion.Euler(0, s < 0 ? 90 : -90, 0);
                if (st == 1 || st == 3) U.Text3D("NOS", root, new Vector3(s * (W * 0.5f + 0.01f), gy + 0.62f, rear + L * 0.22f), q, 1.1f, new Color(0.15f, 0.45f, 1f));
                if (st == 2 || st == 3) U.Text3D("走り屋", root, new Vector3(s * (W * 0.5f + 0.01f), gy + 0.66f, front - L * 0.2f), q, 1.2f, new Color(0.95f, 0.95f, 0.95f));
            }

            // jantlar
            int rs = v[CustomCatalog.Rim];
            for (int i = 0; i < 4; i++)
            {
                if (p.wVis[i] == null) continue;
                if (rs > 0)
                {
                    float s = p.wPos[i].x < 0 ? -1f : 1f;
                    var rg = new GameObject("FF_Rim");
                    rg.AddComponent<MeshFilter>().sharedMesh = RimMesh(rs);
                    rg.AddComponent<MeshRenderer>().sharedMaterial = RimMat(v[CustomCatalog.RimColor]);
                    rg.transform.position = p.wVis[i].position + p.vis.right * s * 0.1f;
                    rg.transform.rotation = p.vis.rotation * Quaternion.Euler(0, s > 0 ? 0 : 180, 0);
                    rg.transform.localScale = Vector3.one * p.wR[i];
                    rg.transform.SetParent(p.wVis[i], true);
                }
            }

            // sürüş yüksekliği / kamber / far / cam filmi
            float drop = new[] { 0f, 0.02f, 0.04f, 0.06f }[v[CustomCatalog.Ride]];
            float camber = new[] { 0f, 2f, 4f, 7f }[v[CustomCatalog.Camber]];
            Color head = CustomCatalog.HeadColors[v[CustomCatalog.Head]];
            if (car != null)
            {
                car.SetStance(drop, camber);
                car.headColor = head;
            }
            else
            {
                // önizleme: tekerlekler yukarı (gövde alçalır), kamber eğimi
                for (int i = 0; i < 4; i++)
                {
                    if (p.wVis[i] == null) continue;
                    float s = p.wPos[i].x < 0 ? -1f : 1f;
                    p.wVis[i].localPosition = p.wVisLocal[i] + p.vis.InverseTransformVector(Vector3.up * drop);
                    p.wVis[i].localRotation = Quaternion.Euler(0, 0, s * camber);
                }
                foreach (var m in p.headMats) U.SetEmission(m, head * 2.2f);
            }
            ApplyTint(p, v[CustomCatalog.Tint]);
            if (v[CustomCatalog.Vinyl] > 0) ApplyVinyl(p, v[CustomCatalog.Vinyl]);
        }

        static void ApplyTint(CarParts p, int level)
        {
            if (p.glassOrig == null)
            {
                p.glassOrig = new Dictionary<Material, Color>();
                foreach (var r in p.vis.GetComponentsInChildren<Renderer>(true))
                {
                    if (r.name.StartsWith("FF_")) continue;
                    var mats = r.sharedMaterials; bool changed = false;
                    for (int k = 0; k < mats.Length; k++)
                    {
                        if (mats[k] == null) continue;
                        string n = mats[k].name.ToLowerInvariant();
                        if (!(n.Contains("glass") || n.Contains("window") || n.Contains("cam") || n.Contains("glas") || n.Contains("windshield"))) continue;
                        if (!p.glassOrig.ContainsKey(mats[k]))
                        {
                            var cl = new Material(mats[k]); mats[k] = cl; changed = true;
                            p.glassOrig[cl] = cl.HasProperty("_BaseColor") ? cl.GetColor("_BaseColor") : cl.HasProperty("_Color") ? cl.color : Color.gray;
                        }
                    }
                    if (changed) r.sharedMaterials = mats;
                }
            }
            float t = new[] { 0f, 0.45f, 0.7f, 0.92f }[Mathf.Clamp(level, 0, 3)];
            foreach (var kv in p.glassOrig)
            {
                Color o = kv.Value;
                Color c = Color.Lerp(o, new Color(0.02f, 0.02f, 0.03f, Mathf.Max(o.a, 0.85f)), t);
                U.SetColor(kv.Key, c);
            }
        }

        static void ApplyVinyl(CarParts p, int style)
        {
            if (!vinylTried) { vinylTried = true; var m = Resources.Load<Material>("MW_VinylMat"); if (m != null && m.shader != null && m.shader.isSupported) vinylBase = m; }
            if (vinylBase == null || p.paintMats.Count == 0) return;
            if (clearVinyl == null) { clearVinyl = new Material(vinylBase); clearVinyl.SetFloat("_Alpha", 0f); }
            var tex = VinylTexture(style);
            foreach (var r in p.vis.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.name.StartsWith("FF_")) continue;
                var mf = r.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                var mats = r.sharedMaterials;
                bool any = false;
                var vm = new Material[mats.Length];
                Material inst = null;
                for (int k = 0; k < mats.Length; k++)
                {
                    if (mats[k] != null && p.paintMats.Contains(mats[k]))
                    {
                        if (inst == null)
                        {
                            inst = new Material(vinylBase);
                            inst.SetTexture("_MainTex", tex);
                            inst.SetFloat("_Alpha", 1f);
                            inst.SetVector("_BMin", p.body.min);
                            inst.SetVector("_BSize", p.body.size);
                            inst.SetMatrix("_ObjToCar", p.vis.worldToLocalMatrix * r.transform.localToWorldMatrix);
                        }
                        vm[k] = inst; any = true;
                    }
                    else vm[k] = clearVinyl;
                }
                if (!any) continue;
                var g = new GameObject("FF_Vinyl");
                g.transform.SetParent(r.transform, false);
                g.AddComponent<MeshFilter>().sharedMesh = mf.sharedMesh;
                var vr = g.AddComponent<MeshRenderer>();
                vr.sharedMaterials = vm;
                vr.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        /// <summary>Vinil atlası: üst yarı yanlar (u: arka→ön, v: alt→üst), alt yarı üst yüzeyler (u: arka→ön, v: sol→sağ).</summary>
        static Texture2D VinylTexture(int style)
        {
            Texture2D t;
            if (vinylTex.TryGetValue(style, out t)) return t;
            const int Wd = 256, Ht = 256;
            t = new Texture2D(Wd, Ht, TextureFormat.RGBA32, true);
            var px = new Color[Wd * Ht];
            Color clear = new Color(0, 0, 0, 0);
            for (int y = 0; y < Ht; y++)
                for (int x = 0; x < Wd; x++)
                {
                    float u = x / (Wd - 1f);
                    bool side = y >= Ht / 2;
                    float v = side ? (y - Ht / 2) / (Ht / 2 - 1f) : y / (Ht / 2 - 1f);
                    Color c = clear;
                    switch (style)
                    {
                        case 1: // yarış şeritleri
                            if (!side && ((v > 0.40f && v < 0.47f) || (v > 0.53f && v < 0.60f))) c = new Color(0.97f, 0.97f, 0.97f, 1f);
                            if (side && v > 0.18f && v < 0.21f) c = new Color(0.97f, 0.97f, 0.97f, 1f);
                            break;
                        case 2: // R34: alt gümüş + mavi çizgi
                            if (side && v < 0.36f) c = new Color(0.72f, 0.74f, 0.78f, 1f);
                            if (side && v >= 0.36f && v < 0.40f) c = new Color(0.1f, 0.3f, 0.85f, 1f);
                            break;
                        case 3: // yan grafik (dalgalı şerit)
                            {
                                float mid = 0.35f + 0.18f * Mathf.Sin(u * 7.5f) * u;
                                float w = 0.05f + 0.12f * u;
                                if (side && Mathf.Abs(v - mid) < w) c = new Color(0.1f, 0.95f, 0.3f, 1f);
                                if (side && Mathf.Abs(v - mid) < w * 0.45f) c = new Color(0.03f, 0.03f, 0.04f, 1f);
                            }
                            break;
                        case 4: // alevler (önden arkaya)
                            {
                                float k = 1f - u;   // önden uzaklık
                                float tongue = 0.5f + 0.45f * Mathf.Abs(Mathf.Sin(v * 9f + 1.3f)) * (1f - Mathf.Abs(v - 0.4f));
                                if ((side && k < tongue * 0.55f && v < 0.8f) || (!side && k < 0.25f + 0.1f * Mathf.Sin(v * 14f)))
                                {
                                    float h = Mathf.Clamp01(k / Mathf.Max(0.05f, tongue * 0.55f));
                                    c = Color.Lerp(new Color(1f, 0.9f, 0.2f, 1f), new Color(0.9f, 0.1f, 0.05f, 1f), h);
                                }
                            }
                            break;
                    }
                    px[y * Wd + x] = c;
                }
            t.SetPixels(px);
            t.Apply(true);
            t.wrapMode = TextureWrapMode.Clamp;
            vinylTex[style] = t;
            return t;
        }

        /// <summary>Birim yarıçaplı jant (dış yüz +x). Stil başına bir kez üretilir, tüm araçlar paylaşır.</summary>
        static Mesh RimMesh(int style)
        {
            Mesh m;
            if (rimMeshes.TryGetValue(style, out m)) return m;
            var k = new MeshKit(1);
            float rOut = 0.7f, depth = style == 4 ? 0.12f : 0.05f;
            // dudak halkası
            for (int i = 0; i < 24; i++)
            {
                float a = i * Mathf.PI * 2f / 24f;
                Vector3 c = new Vector3(depth * 0.5f, Mathf.Cos(a) * rOut, Mathf.Sin(a) * rOut);
                k.OBox(0, c, Quaternion.Euler(-a * Mathf.Rad2Deg, 0, 0), new Vector3(depth, 0.06f, 0.2f));
            }
            k.Cyl(0, new Vector3(0.03f, 0, 0), Vector3.right, 0.14f, 0.06f, 12);
            int spokes = style == 1 ? 5 : style == 2 ? 6 : style == 3 ? 10 : style == 4 ? 5 : style == 5 ? 12 : 3;
            float sw = style == 3 ? 0.035f : style == 5 ? 0.05f : style == 6 ? 0.13f : 0.09f;
            for (int i = 0; i < spokes; i++)
            {
                float a = i * 360f / spokes;
                Quaternion q = Quaternion.Euler(a, 0, 0) * Quaternion.Euler(0, style == 5 ? 25f : 0f, 0);
                Vector3 c = Quaternion.Euler(a, 0, 0) * new Vector3(style == 4 ? -0.02f : 0.02f, 0.42f, 0);
                k.OBox(0, c, q, new Vector3(0.04f, 0.56f, sw));
                if (style == 3) k.OBox(0, Quaternion.Euler(a + 18f, 0, 0) * new Vector3(0.02f, 0.42f, 0), Quaternion.Euler(a + 18f, 0, 0), new Vector3(0.03f, 0.56f, sw));
            }
            m = k.Build("FF_Rim_" + style);
            rimMeshes[style] = m;
            return m;
        }
    }
}
