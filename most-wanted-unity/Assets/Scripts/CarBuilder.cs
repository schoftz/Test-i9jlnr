using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MostWanted
{
    public enum CarRole { Player, Traffic, Police, Racer }

    /// <summary>Araçları kurar: prefab modeli otomatik oturtur (yön, ölçek, zemin), tekerlekleri bulur, fiziği ekler.</summary>
    public static class CarFactory
    {
        struct WheelInfo { public Vector3 pos; public float radius; public Transform vis; }

        public static CarController Build(CarEntry def, PaintDef? paint, Vector3 pos, Quaternion rot, CarRole role, int[] tune, string name)
        {
            var go = new GameObject(name);
            var vis = new GameObject("Gorsel").transform;
            vis.SetParent(go.transform, false);

            WheelInfo[] wi = null;
            Bounds body = new Bounds(new Vector3(0, 0.75f, 0), new Vector3(1.9f, 1.3f, def.length));
            var paintMats = new List<Material>();
            List<Vector3> exhaustTips = null;
            var brakeMats = new List<Material>();
            var headMats = new List<Material>();
            if (def.prefab != null)
            {
                try { wi = FitModel(def, vis, ref body, paintMats, brakeMats, headMats); exhaustTips = FindExhaustTips(vis); }
                catch (System.Exception e) { Debug.LogWarning("Model oturtulamadı (" + def.id + "): " + e.Message); wi = null; }
                if (wi == null)
                {
                    for (int i = vis.childCount - 1; i >= 0; i--) Object.Destroy(vis.GetChild(i).gameObject);
                    paintMats.Clear(); brakeMats.Clear(); headMats.Clear();
                }
            }
            if (wi == null) wi = ProceduralBody(def, vis, ref body, paintMats, brakeMats, headMats);

            if (paint.HasValue) foreach (var m in paintMats) U.ApplyPaint(m, paint.Value);

            // ---- Fizik ----
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = def.massKg;
            U.SetDamping(rb, 0.01f, 0.25f);
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.maxDepenetrationVelocity = 4f;
            rb.collisionDetectionMode = role == CarRole.Player ? CollisionDetectionMode.ContinuousDynamic : CollisionDetectionMode.Continuous;

            float W = Mathf.Clamp(body.size.x, 1.5f, 2.6f), L = body.size.z;
            AddBodyColliders(go, body, wi);

            // alçak ağırlık merkezi (aks yüksekliği civarı)
            float wheelR = 0f; foreach (var w in wi) wheelR += w.radius; wheelR /= 4f;
            float com = Mathf.Max(0.3f, wheelR * 1.05f);
            rb.centerOfMass = new Vector3(0, com, (wi[0].pos.z + wi[2].pos.z) * 0.5f + 0.05f);
            float Hh = 1.3f;
            rb.inertiaTensor = new Vector3(rb.mass * (Hh * Hh + L * L) / 12f, rb.mass * (W * W + L * L) / 12f, rb.mass * (W * W + Hh * Hh) / 12f * 1.4f);
            rb.inertiaTensorRotation = Quaternion.identity;

            var car = go.AddComponent<CarController>();
            car.rb = rb;
            car.isPlayer = role == CarRole.Player;
            car.comHeight = com;
            car.wheelRadius = wheelR;
            // öz-test: ön aks (direksiyon) araç ilerisinde olmalı
            if ((wi[0].pos.z + wi[1].pos.z) < (wi[2].pos.z + wi[3].pos.z))
            {
                var t0 = wi[0]; var t1 = wi[1]; wi[0] = wi[2]; wi[1] = wi[3]; wi[2] = t0; wi[3] = t1;
                Debug.LogWarning("[MW] " + def.displayName + ": ön/arka aks ters bulundu, düzeltildi.");
            }
            if (wi[0].pos.x > wi[1].pos.x) { var t = wi[0]; wi[0] = wi[1]; wi[1] = t; }
            if (wi[2].pos.x > wi[3].pos.x) { var t = wi[2]; wi[2] = wi[3]; wi[3] = t; }
            car.wheelBase = Mathf.Max(1.8f, Mathf.Abs(wi[0].pos.z - wi[2].pos.z));
            if (role == CarRole.Player)
                Debug.Log("[MW] " + def.displayName + ": ön teker z=" + wi[0].pos.z.ToString("0.00") + "/" + wi[1].pos.z.ToString("0.00") + ", arka z=" + wi[2].pos.z.ToString("0.00") + ", dingil mesafesi " + car.wheelBase.ToString("0.00") + " m — direksiyon OK");
            car.stabilityAssist = role == CarRole.Player ? 0.45f : 0.6f;
            car.paintMats = paintMats; car.brakeMats = brakeMats; car.headMats = headMats;

            string[] wn = { "FL", "FR", "RL", "RR" };
            float susp = CarController.SuspensionTravel;
            for (int i = 0; i < 4; i++)
            {
                // süspansiyon bağlantı noktası (RVP ışın başlangıcı / drift modunda WheelCollider)
                var wgo = new GameObject("WA_" + wn[i]);
                wgo.transform.SetParent(go.transform, false);
                wgo.transform.localPosition = wi[i].pos + Vector3.up * (susp * 0.5f + 0.06f);
                car.wheelAnchor[i] = wgo.transform;
                car.wheelRadii[i] = wi[i].radius;
                car.wheelVis[i] = wi[i].vis;
            }
            car.SetPhysicsMode(false);   // varsayılan: RVP; drift ayarı Game tarafından garaj çıkışında açılır
            car.Configure(def, tune);

            // parça bilgisi + F&F kiti (sadece oyuncunun drift aracı)
            car.parts = MakeParts(def, vis, body, wi, paintMats, headMats, exhaustTips);
            if (role == CarRole.Player && CustomCatalog.IsDriftCar(def))
            {
                var cs = CustomCatalog.Get(SaveSystem.Get(def.id));
                if (cs != null) CustomKit.Apply(car.parts, cs, car, false);
            }

            // efektler
            AddEffects(car, body, role, exhaustTips, def);

            Color ic = role == CarRole.Player ? new Color(1f, 0.85f, 0f) : role == CarRole.Police ? new Color(1f, 0.1f, 0.1f) :
                       role == CarRole.Racer ? new Color(1f, 0.35f, 1f) : new Color(0.75f, 0.75f, 0.75f);
            U.Icon(go.transform, ic, role == CarRole.Player ? 10f : 7f, true);

            // Rigidbody'yi de taşı: interpolasyonlu gövdede sadece Transform taşımak geri alınır (araç orijinde kalıyordu)
            go.transform.SetPositionAndRotation(pos, rot);
            rb.position = pos;
            rb.rotation = rot;
            return car;
        }

        /// <summary>
        /// Gövde çarpıştırıcıları: alt kutu (eşik yüksekliği, tekerlek iz genişliği) + üst kutu (kabin, daha dar/kısa).
        /// Görselden ~6 cm içeride: yanından geçerken yanlış "çarpışma" olmasın. Ayna/kanat/ışık çubuğu dahil değil.
        /// </summary>
        static void AddBodyColliders(GameObject go, Bounds body, WheelInfo[] wi)
        {
            const float inset = 0.06f;
            float track = Mathf.Abs(wi[1].pos.x - wi[0].pos.x) + wi[0].radius * 0.7f;
            float W = Mathf.Min(Mathf.Clamp(body.size.x, 1.4f, 2.5f), track + 0.25f) - inset * 2f;
            float L = body.size.z - inset * 2f;
            float H = Mathf.Clamp(body.size.y, 0.9f, 2.2f);
            float bottom = Mathf.Max(0.3f, body.min.y + 0.15f);
            float lowTop = Mathf.Min(bottom + H * 0.45f, body.max.y - 0.25f);
            var lower = go.AddComponent<BoxCollider>();
            lower.center = new Vector3(body.center.x, (bottom + lowTop) * 0.5f, body.center.z);
            lower.size = new Vector3(W, Mathf.Max(0.2f, lowTop - bottom), L);
            var upper = go.AddComponent<BoxCollider>();
            upper.center = new Vector3(body.center.x, (lowTop + body.max.y - inset) * 0.5f, body.center.z - L * 0.04f);
            upper.size = new Vector3(W * 0.78f, Mathf.Max(0.15f, body.max.y - inset - lowTop), L * 0.5f);
        }

        static readonly string[] AppendageWords = { "mirror", "ayna", "wing", "spoiler", "kanat", "antenna", "extra", "lightbar", "neon", "exhaust", "exhust" };
        static bool IsAppendage(Transform t, Transform root)
        {
            for (var x = t; x != null && x != root; x = x.parent)
            {
                string n = x.name.ToLowerInvariant();
                foreach (var w in AppendageWords) if (n.Contains(w)) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ Model oturtma
        static bool IsWheelName(string n)
        {
            n = n.ToLowerInvariant();
            if (n.Contains("steering")) return false;
            return n.Contains("wheel") || n.Contains("tyre") || n.Contains("tire") || n.Contains("teker");
        }

        static int FrontRear(string n)
        {
            n = n.ToLowerInvariant().Replace("_", " ").Replace("-", " ").Replace(".", " ");
            if (n.Contains("front") || n.Contains("on ")) return 1;
            if (n.Contains("rear") || n.Contains("back") || n.Contains("arka")) return -1;
            string[] tok = n.Split(' ');
            foreach (var t in tok)
            {
                if (t == "fl" || t == "fr" || t == "f") return 1;
                if (t == "rl" || t == "rr" || t == "bl" || t == "br" || t == "r") return -1;
            }
            if (n.EndsWith("fl") || n.EndsWith("fr")) return 1;
            if (n.EndsWith("rl") || n.EndsWith("rr")) return -1;
            return 0;
        }

        static Bounds RendererBounds(Transform t, Transform exclude1 = null)
        {
            bool has = false;
            Bounds b = new Bounds(t.position, Vector3.zero);
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer) continue;
                if (!has) { b = r.bounds; has = true; } else b.Encapsulate(r.bounds);
            }
            return b;
        }

        static WheelInfo[] FitModel(CarEntry def, Transform vis, ref Bounds body, List<Material> paintMats, List<Material> brakeMats, List<Material> headMats)
        {
            var inst = Object.Instantiate(def.prefab);
            inst.name = "Model";
            // gereksiz bileşenleri temizle
            foreach (var c in inst.GetComponentsInChildren<Component>(true))
            {
                if (c == null || c is Transform || c is MeshFilter || c is Renderer || c is LODGroup) continue;
                Object.DestroyImmediate(c);
            }
            inst.transform.SetParent(vis, false);
            inst.transform.localPosition = Vector3.zero;
            inst.transform.localRotation = def.prefab.transform.localRotation;
            // vis kökü dünya orijininde ve dönüşsüz (Build sırasında) -> dünya = yerel

            // tekerlekleri bul
            var wheelTs = new List<Transform>();
            foreach (var t in inst.GetComponentsInChildren<Transform>())
            {
                if (t == inst.transform || !IsWheelName(t.name)) continue;
                if (t.GetComponentInChildren<Renderer>() == null) continue;
                bool nested = false;
                foreach (var o in wheelTs) if (t.IsChildOf(o)) nested = true;
                if (nested) continue;
                wheelTs.RemoveAll(o => o.IsChildOf(t));
                wheelTs.Add(t);
            }

            // ileri yönü belirle
            Bounds all = RendererBounds(inst.transform);
            Vector3 fwd;
            Vector3 fsum = Vector3.zero, rsum = Vector3.zero; int fc = 0, rc = 0;
            foreach (var w in wheelTs)
            {
                int fr = FrontRear(w.name);
                Vector3 c = RendererBounds(w).center;
                if (fr > 0) { fsum += c; fc++; } else if (fr < 0) { rsum += c; rc++; }
            }
            if (fc > 0 && rc > 0) fwd = U.Flat(fsum / fc - rsum / rc);
            else fwd = all.size.x > all.size.z ? Vector3.right : Vector3.forward;
            if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
            float yaw = Vector3.SignedAngle(fwd.normalized, Vector3.forward, Vector3.up);
            inst.transform.rotation = Quaternion.Euler(0, yaw, 0) * inst.transform.rotation;

            // ölçek
            all = RendererBounds(inst.transform);
            float len = Mathf.Max(0.01f, all.size.z);
            float s = def.length / len;
            inst.transform.localScale = inst.transform.localScale * s;
            all = RendererBounds(inst.transform);

            // zemin ve merkez
            float groundY = all.min.y;
            if (wheelTs.Count >= 4)
            {
                groundY = float.MaxValue;
                foreach (var w in wheelTs) groundY = Mathf.Min(groundY, RendererBounds(w).min.y);
            }
            inst.transform.position += new Vector3(-all.center.x, -groundY, -all.center.z);
            all = RendererBounds(inst.transform);

            var result = new WheelInfo[4];
            if (wheelTs.Count >= 4)
            {
                // 4 tekerleği köşelere göre sırala (öndeki en büyük z, sol x<0)
                var list = new List<KeyValuePair<Transform, Bounds>>();
                foreach (var w in wheelTs) list.Add(new KeyValuePair<Transform, Bounds>(w, RendererBounds(w)));
                list.Sort((a, b) => b.Value.center.z.CompareTo(a.Value.center.z));
                var front = new List<KeyValuePair<Transform, Bounds>> { list[0], list[1] };
                var rear = new List<KeyValuePair<Transform, Bounds>> { list[list.Count - 2], list[list.Count - 1] };
                front.Sort((a, b) => a.Value.center.x.CompareTo(b.Value.center.x));
                rear.Sort((a, b) => a.Value.center.x.CompareTo(b.Value.center.x));
                var ordered = new[] { front[0], front[1], rear[0], rear[1] };
                for (int i = 0; i < 4; i++)
                {
                    var b = ordered[i].Value;
                    var pivot = new GameObject("Teker" + i).transform;
                    pivot.SetParent(vis, false);
                    pivot.position = b.center;
                    pivot.rotation = Quaternion.identity;
                    ordered[i].Key.SetParent(pivot, true);
                    result[i] = new WheelInfo { pos = b.center, radius = Mathf.Clamp(b.size.y * 0.5f, 0.25f, 0.55f), vis = pivot };
                }
                // diğer (ara) tekerlek parçaları varsa gizle
                for (int i = 2; i < list.Count - 2; i++) list[i].Key.gameObject.SetActive(false);
            }
            else
            {
                // tekerlek bulunamadı: kendi tekerleklerimizi ekle
                float r = 0.34f;
                float hx = all.extents.x - 0.15f, hz = all.extents.z - 0.85f;
                Vector3[] p = { new Vector3(-hx, r, hz), new Vector3(hx, r, hz), new Vector3(-hx, r, -hz), new Vector3(hx, r, -hz) };
                for (int i = 0; i < 4; i++) result[i] = new WheelInfo { pos = p[i], radius = r, vis = MakeWheel(vis, p[i], r, i % 2 == 1) };
                inst.transform.position += Vector3.up * 0.12f;
                all = RendererBounds(inst.transform);
            }

            // gövde sınırları (tekerlekler hariç)
            bool has = false; Bounds bb = all;
            foreach (var rend in inst.GetComponentsInChildren<Renderer>())
            {
                bool isWheel = false;
                foreach (var w in result) if (w.vis != null && rend.transform.IsChildOf(w.vis)) isWheel = true;
                if (isWheel || IsAppendage(rend.transform, inst.transform)) continue;
                if (!has) { bb = rend.bounds; has = true; } else bb.Encapsulate(rend.bounds);
            }
            body = bb;

            // malzemeler: boya, fren ve far
            // --- boya malzemesi tespiti ---
            // Pakette gövde rengi ALBEDO DOKUSUNDAN gelir (Blue.png, Black.png...) ve bazı araçlarda ana gövde
            // malzemesinin adı "Mehroon"dur; "Body 5" ise bazen siyah trim. Bu yüzden: hariç tutulan isimler dışındaki
            // malzemelerden toplam üçgen sayısı en büyük olan = ana boya. Adında paint/karoser geçenler de eklenir.
            var triCount = new Dictionary<Material, long>();
            var allRends = inst.GetComponentsInChildren<Renderer>(true);
            foreach (var rend in allRends)
            {
                bool isWheel = false;
                foreach (var w in result) if (w.vis != null && rend.transform.IsChildOf(w.vis)) isWheel = true;
                if (isWheel) continue;
                var mf = rend.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : null;
                var mats = rend.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null || IsNonPaint(m.name) || IsNonPaint(rend.name)) continue;
                    long n = 1;
                    if (mesh != null && i < mesh.subMeshCount) n = mesh.GetSubMesh(i).indexCount / 3;
                    long prev; triCount.TryGetValue(m, out prev);
                    triCount[m] = prev + n;
                }
            }
            Material mainPaint = null; long best = -1;
            foreach (var kv in triCount) if (kv.Value > best) { best = kv.Value; mainPaint = kv.Key; }

            var cloned = new Dictionary<Material, Material>();
            foreach (var rend in allRends)
            {
                var mats = rend.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    string mn = m.name.ToLowerInvariant();
                    bool isPaint = m == mainPaint || mn.Contains("paint") || mn.Contains("karoser") || mn.Contains("boya");
                    bool isBrake = mn.Contains("back light") || mn.Contains("rear light") || mn.Contains("red light") || mn.Contains("tail") || mn.Contains("brake") || mn.Contains("stop");
                    bool isHead = mn.Contains("front light") || mn.Contains("head");
                    if (!isPaint && !isBrake && !isHead) continue;
                    Material c;
                    if (!cloned.TryGetValue(m, out c))
                    {
                        c = new Material(m);   // her araç kendi kopyasını boyar
                        cloned[m] = c;
                        if (isPaint) paintMats.Add(c);
                        else if (isBrake) brakeMats.Add(c);
                        else headMats.Add(c);
                    }
                    mats[i] = c; changed = true;
                }
                if (changed) rend.sharedMaterials = mats;
            }
            foreach (var r in inst.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;
            return result;
        }

        static readonly string[] NonPaintWords = { "glass", "cam", "tyre", "tire", "wheel", "rim", "jant", "light", "lamp", "far", "grill", "carbon", "crbon", "exhaust", "exhust", "screen", "neon", "chrome", "mirror", "interior", "seat", "engine" };
        static bool IsNonPaint(string n)
        {
            n = n.ToLowerInvariant();
            foreach (var w in NonPaintWords) if (n.Contains(w)) return true;
            return false;
        }

        static Transform MakeWheel(Transform vis, Vector3 p, float r, bool right)
        {
            var tire = U.Mat(new Color(0.05f, 0.05f, 0.05f), 0.1f);
            var rim = U.Mat(new Color(0.75f, 0.75f, 0.78f), 0.85f, 0.9f);
            var pivot = new GameObject("Teker").transform;
            pivot.SetParent(vis, false);
            pivot.localPosition = p;
            var t = U.Prim(PrimitiveType.Cylinder, "Lastik", pivot, Vector3.zero, new Vector3(r * 2, 0.12f, r * 2), tire);
            t.transform.localRotation = Quaternion.Euler(0, 0, 90);
            var rm = U.Prim(PrimitiveType.Cylinder, "Jant", pivot, new Vector3(right ? 0.03f : -0.03f, 0, 0), new Vector3(r * 1.35f, 0.125f, r * 1.35f), rim);
            rm.transform.localRotation = Quaternion.Euler(0, 0, 90);
            return pivot;
        }

        // ------------------------------------------------------------------ Prosedürel yedek gövde
        static WheelInfo[] ProceduralBody(CarEntry def, Transform vis, ref Bounds body, List<Material> paintMats, List<Material> brakeMats, List<Material> headMats)
        {
            float L = def.length, W = 1.86f + (def.length - 4.2f) * 0.2f;
            bool suv = def.policeRole == "suv" || def.massKg > 1900f;
            float h = suv ? 0.85f : 0.6f;
            float baseY = suv ? 0.45f : 0.32f;
            var paint = U.NewMat(def.defaultColor, 0.85f, 0.45f);
            paintMats.Add(paint);
            var glass = U.Mat(new Color(0.05f, 0.07f, 0.1f), 0.95f, 0.2f);
            var dark = U.Mat(new Color(0.06f, 0.06f, 0.06f), 0.3f);
            U.Prim(PrimitiveType.Cube, "Govde", vis, new Vector3(0, baseY + h / 2, 0), new Vector3(W, h, L), paint);
            U.Prim(PrimitiveType.Cube, "Kaput", vis, new Vector3(0, baseY + h + 0.02f, L * 0.3f), new Vector3(W * 0.95f, 0.06f, L * 0.32f), paint);
            float cabH = suv ? 0.7f : 0.48f;
            U.Prim(PrimitiveType.Cube, "Kabin", vis, new Vector3(0, baseY + h + cabH / 2, -0.15f), new Vector3(W * 0.84f, cabH, L * (suv ? 0.58f : 0.46f)), glass);
            U.Prim(PrimitiveType.Cube, "Tavan", vis, new Vector3(0, baseY + h + cabH + 0.02f, -0.2f), new Vector3(W * 0.8f, 0.05f, L * (suv ? 0.52f : 0.36f)), paint);
            U.Prim(PrimitiveType.Cube, "Tampon", vis, new Vector3(0, baseY + 0.12f, L * 0.5f), new Vector3(W * 1.01f, 0.24f, 0.14f), dark);
            U.Prim(PrimitiveType.Cube, "ArkaTampon", vis, new Vector3(0, baseY + 0.12f, -L * 0.5f), new Vector3(W * 1.01f, 0.24f, 0.14f), dark);
            if (def.topSpeedKmh > 270f)
            {
                U.Prim(PrimitiveType.Cube, "Kanat", vis, new Vector3(0, baseY + h + 0.38f, -L * 0.46f), new Vector3(W * 0.92f, 0.05f, 0.38f), dark);
                U.Prim(PrimitiveType.Cube, "KanatA", vis, new Vector3(W * 0.3f, baseY + h + 0.2f, -L * 0.46f), new Vector3(0.06f, 0.36f, 0.2f), dark);
                U.Prim(PrimitiveType.Cube, "KanatB", vis, new Vector3(-W * 0.3f, baseY + h + 0.2f, -L * 0.46f), new Vector3(0.06f, 0.36f, 0.2f), dark);
            }
            var head = U.NewMat(Color.white); U.SetEmission(head, new Color(0.4f, 0.4f, 0.38f)); headMats.Add(head);
            var tail = U.NewMat(new Color(0.5f, 0, 0)); brakeMats.Add(tail);
            U.Prim(PrimitiveType.Cube, "FarL", vis, new Vector3(-W * 0.34f, baseY + h - 0.12f, L * 0.5f + 0.01f), new Vector3(0.42f, 0.13f, 0.05f), head);
            U.Prim(PrimitiveType.Cube, "FarR", vis, new Vector3(W * 0.34f, baseY + h - 0.12f, L * 0.5f + 0.01f), new Vector3(0.42f, 0.13f, 0.05f), head);
            U.Prim(PrimitiveType.Cube, "StopL", vis, new Vector3(-W * 0.35f, baseY + h - 0.1f, -L * 0.5f - 0.01f), new Vector3(0.46f, 0.12f, 0.05f), tail);
            U.Prim(PrimitiveType.Cube, "StopR", vis, new Vector3(W * 0.35f, baseY + h - 0.1f, -L * 0.5f - 0.01f), new Vector3(0.46f, 0.12f, 0.05f), tail);

            float r = suv ? 0.4f : 0.34f;
            float hx = W / 2 - 0.12f, hz = L / 2 - 0.85f;
            Vector3[] p = { new Vector3(-hx, r, hz), new Vector3(hx, r, hz), new Vector3(-hx, r, -hz), new Vector3(hx, r, -hz) };
            var res = new WheelInfo[4];
            for (int i = 0; i < 4; i++) res[i] = new WheelInfo { pos = p[i], radius = r, vis = MakeWheel(vis, p[i], r, i % 2 == 1) };
            float top = baseY + h + cabH + 0.05f;
            body = new Bounds(new Vector3(0, top / 2, 0), new Vector3(W, top, L));
            return res;
        }

        // ------------------------------------------------------------------ Efektler
        static Material smokeMat, flameMat;

        static Material evGlowMat;

        /// <summary>
        /// Egzoz uçları: "Exhaust/Exhust" malzemeli alt-mesh'ler veya adında exhaust/egzoz/muffler/tip geçen objeler.
        /// Alt-mesh sınırının en arka kısmı genişliğe göre 1/2/4 uca bölünür (çift/dörtlü egzoz).
        /// </summary>
        static List<Vector3> FindExhaustTips(Transform vis)
        {
            var tips = new List<Vector3>();
            bool has = false; Bounds b = new Bounds();
            foreach (var rend in vis.GetComponentsInChildren<Renderer>())
            {
                var mf = rend.GetComponent<MeshFilter>();
                var mesh = mf != null ? mf.sharedMesh : null;
                string rn = rend.name.ToLowerInvariant();
                bool nameHit = rn.Contains("exhaust") || rn.Contains("exhust") || rn.Contains("egzoz") || rn.Contains("muffler") || rn.Contains("tip");
                var mats = rend.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    string mn = mats[i] != null ? mats[i].name.ToLowerInvariant() : "";
                    bool matHit = mn.Contains("exhaust") || mn.Contains("exhust");
                    if (!matHit && !(nameHit && i == 0)) continue;
                    Bounds wb;
                    if (mesh != null && i < mesh.subMeshCount && !nameHit)
                    {
                        var lb = mesh.GetSubMesh(i).bounds;
                        // yerel sınırları vis uzayına çevir (8 köşe)
                        wb = new Bounds(rend.transform.TransformPoint(lb.center), Vector3.zero);
                        for (int k = 0; k < 8; k++)
                        {
                            Vector3 c = lb.center + Vector3.Scale(lb.extents, new Vector3((k & 1) == 0 ? -1 : 1, (k & 2) == 0 ? -1 : 1, (k & 4) == 0 ? -1 : 1));
                            wb.Encapsulate(rend.transform.TransformPoint(c));
                        }
                    }
                    else wb = rend.bounds;
                    if (!has) { b = wb; has = true; } else b.Encapsulate(wb);
                }
            }
            if (!has || b.size.x < 0.02f) return null;
            Vector3 lmin = vis.InverseTransformPoint(b.min), lmax = vis.InverseTransformPoint(b.max);
            float w = Mathf.Abs(lmax.x - lmin.x), x0 = Mathf.Min(lmin.x, lmax.x);
            float z = Mathf.Min(lmin.z, lmax.z), y = (lmin.y + lmax.y) * 0.5f;
            int n = w > 1.1f ? 4 : w > 0.4f ? 2 : 1;
            if (n == 4 && w > 1.4f) n = 2;   // geniş aralık: iki köşe çifti
            for (int i = 0; i < n; i++)
            {
                float t = n == 1 ? 0.5f : (n == 2 ? (i == 0 ? 0.12f : 0.88f) : 0.15f + i * 0.7f / 3f);
                tips.Add(new Vector3(x0 + w * t, y, z - 0.05f));
            }
            return tips;
        }

        static void AddEffects(CarController car, Bounds body, CarRole role, List<Vector3> tips, CarEntry def)
        {
            if (flameMat == null) flameMat = U.Emissive(new Color(0.1f, 0.3f, 1f), new Color(0.6f, 1.6f, 6f));
            if (evGlowMat == null) evGlowMat = U.Emissive(new Color(0.2f, 0.7f, 1f), new Color(0.4f, 2.5f, 5f));
            bool electric = def != null && (def.engineType ?? "").ToUpperInvariant() == "EV";
            car.electric = electric;
            if (tips == null || tips.Count == 0)
            {
                float y = Mathf.Max(0.3f, body.min.y + 0.25f);
                float half = body.size.x * 0.38f;
                tips = new List<Vector3> { new Vector3(-half, y, body.min.z - 0.02f), new Vector3(half, y, body.min.z - 0.02f) };
            }
            if (electric)
            {
                // elektrikli: alev yok, nitroda arka difüzörde mavi elektrik parıltısı
                var gl = U.Prim(PrimitiveType.Cube, "ElektrikParilti", car.transform, new Vector3(0, Mathf.Max(0.35f, body.min.y + 0.3f), body.min.z - 0.05f), new Vector3(body.size.x * 0.7f, 0.06f, 0.06f), evGlowMat);
                gl.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                gl.SetActive(false);
                car.flames.Add(gl.transform);
                car.flameBaseScale = gl.transform.localScale;
            }
            else
            {
                foreach (var tp in tips)
                {
                    var pivot = new GameObject("EgzozUcu").transform;
                    pivot.SetParent(car.transform, false);
                    pivot.localPosition = tp;
                    var f = U.Prim(PrimitiveType.Sphere, "NitroAlev", pivot, new Vector3(0, 0, -0.3f), new Vector3(0.13f, 0.13f, 0.6f), flameMat);
                    f.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                    f.SetActive(false);
                    car.flames.Add(f.transform);
                    car.exhaustTips.Add(pivot);
                }
                // geri tepme ışığı (egzoz uçlarının ortasında tek küçük ışık)
                if (role != CarRole.Traffic)
                {
                    Vector3 avg = Vector3.zero; foreach (var tp in tips) avg += tp; avg /= tips.Count;
                    var lg = new GameObject("PatlamaIsigi"); lg.transform.SetParent(car.transform, false); lg.transform.localPosition = avg + Vector3.back * 0.4f;
                    var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.range = 6f; l.intensity = 5f; l.color = new Color(1f, 0.55f, 0.2f); l.shadows = LightShadows.None; l.enabled = false;
                    car.popLight = l;
                }
            }
            if (role == CarRole.Traffic) return;
            if (smokeMat == null)
            {
                Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
                if (sh == null) sh = Shader.Find("Particles/Standard Unlit");
                if (sh == null) sh = Shader.Find("Sprites/Default");
                smokeMat = new Material(sh);
                var tex = new Texture2D(32, 32, TextureFormat.RGBA32, false);
                for (int x = 0; x < 32; x++)
                    for (int z = 0; z < 32; z++)
                    {
                        float d = Vector2.Distance(new Vector2(x, z), new Vector2(15.5f, 15.5f)) / 16f;
                        tex.SetPixel(x, z, new Color(1, 1, 1, Mathf.Clamp01(1f - d) * 0.6f));
                    }
                tex.Apply();
                U.SetMainTex(smokeMat, tex);
                if (smokeMat.HasProperty("_Surface")) { smokeMat.SetFloat("_Surface", 1f); smokeMat.SetOverrideTag("RenderType", "Transparent"); }
                smokeMat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                smokeMat.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                smokeMat.SetInt("_ZWrite", 0);
                smokeMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                smokeMat.renderQueue = 3000;
            }
            for (int i = 0; i < 2; i++)
            {
                var wa = car.wheelAnchor[2 + i];
                var g = new GameObject("LastikDumani");
                g.transform.SetParent(car.transform, false);
                g.transform.localPosition = wa.localPosition + Vector3.down * 0.3f;
                var ps = g.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.startLifetime = 1.6f;
                main.startSpeed = 1.2f;
                main.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
                main.startColor = new Color(0.85f, 0.85f, 0.85f, 0.5f);
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.maxParticles = 120;
                main.gravityModifier = -0.05f;
                var em = ps.emission; em.rateOverTime = 0f;
                var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.3f;
                var col = ps.colorOverLifetime; col.enabled = true;
                var grad = new Gradient();
                grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                             new[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(0f, 1f) });
                col.color = grad;
                var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 2.2f));
                var pr = g.GetComponent<ParticleSystemRenderer>();
                pr.sharedMaterial = smokeMat;
                pr.shadowCastingMode = ShadowCastingMode.Off;
                car.smoke[i] = ps;
            }
        }

        /// <summary>Park etmiş araç: sadece görsel + kutu çarpıştırıcı (fizik yok).</summary>
        static CarParts MakeParts(CarEntry def, Transform vis, Bounds body, WheelInfo[] wi, List<Material> pm, List<Material> hm, List<Vector3> tips)
        {
            var p = new CarParts { def = def, vis = vis, body = body, paintMats = pm, headMats = hm, tips = tips ?? new List<Vector3>() };
            var w = (WheelInfo[])wi.Clone();
            if ((w[0].pos.z + w[1].pos.z) < (w[2].pos.z + w[3].pos.z)) { var t0 = w[0]; var t1 = w[1]; w[0] = w[2]; w[1] = w[3]; w[2] = t0; w[3] = t1; }
            if (w[0].pos.x > w[1].pos.x) { var t = w[0]; w[0] = w[1]; w[1] = t; }
            if (w[2].pos.x > w[3].pos.x) { var t = w[2]; w[2] = w[3]; w[3] = t; }
            for (int i = 0; i < 4; i++) { p.wPos[i] = w[i].pos; p.wR[i] = w[i].radius; p.wVis[i] = w[i].vis; }
            return p;
        }

        /// <summary>Garaj vitrini için fiziksiz araç (görsel + parça bilgisi). Kök = dönen tabla üstü.</summary>
        public static CarParts BuildShowroom(CarEntry def, PaintDef? paint, Transform parent)
        {
            var go = new GameObject("VitrinAraci");
            go.transform.SetParent(parent, false);
            var vis = new GameObject("Gorsel").transform;
            vis.SetParent(go.transform, false);
            Bounds body = new Bounds(new Vector3(0, 0.75f, 0), new Vector3(1.9f, 1.3f, def.length));
            var pm = new List<Material>(); var bm = new List<Material>(); var hm = new List<Material>();
            WheelInfo[] wi = null; List<Vector3> tips = null;
            if (def.prefab != null) { try { wi = FitModel(def, vis, ref body, pm, bm, hm); tips = FindExhaustTips(vis); } catch (System.Exception) { wi = null; } }
            if (wi == null)
            {
                for (int i = vis.childCount - 1; i >= 0; i--) Object.Destroy(vis.GetChild(i).gameObject);
                pm.Clear(); hm.Clear();
                wi = ProceduralBody(def, vis, ref body, pm, bm, hm);
            }
            if (paint.HasValue) foreach (var m in pm) U.ApplyPaint(m, paint.Value);
            foreach (var m in bm) U.SetEmission(m, new Color(1f, 0.05f, 0.03f) * 0.6f);
            foreach (var m in hm) U.SetEmission(m, new Color(2.5f, 2.4f, 2.1f));
            // tekerlek altları tablaya otursun
            float gy = 0f; foreach (var w in wi) gy += w.pos.y - w.radius; gy /= 4f;
            vis.localPosition = new Vector3(0, -gy, -body.center.z);
            return MakeParts(def, vis, body, wi, pm, hm, tips);
        }

        public static GameObject BuildStatic(CarEntry def, PaintDef paint)
        {
            var go = new GameObject("ParkEtmisArac");
            var vis = new GameObject("Gorsel").transform;
            vis.SetParent(go.transform, false);
            Bounds body = new Bounds(new Vector3(0, 0.75f, 0), new Vector3(1.9f, 1.3f, def.length));
            var pm = new List<Material>(); var bm = new List<Material>(); var hm = new List<Material>();
            WheelInfo[] wi = null;
            if (def.prefab != null) { try { wi = FitModel(def, vis, ref body, pm, bm, hm); } catch (System.Exception) { wi = null; } }
            if (wi == null)
            {
                for (int i = vis.childCount - 1; i >= 0; i--) Object.Destroy(vis.GetChild(i).gameObject);
                pm.Clear();
                wi = ProceduralBody(def, vis, ref body, pm, bm, hm);
            }
            foreach (var m in pm) U.ApplyPaint(m, paint);
            AddBodyColliders(go, body, wi);
            foreach (var r in go.GetComponentsInChildren<Renderer>()) r.gameObject.layer = OptimizationManager.TrafficLayer;
            return go;
        }

        // ------------------------------------------------------------------ Polis
        public static CarController BuildPolice(CarEntry def, string role, Vector3 pos, Quaternion rot)
        {
            bool under = role == "undercover";
            var c = new PaintDef("Polis", under ? new Color(0.09f, 0.09f, 0.1f) : new Color(0.03f, 0.03f, 0.04f), 0.5f, 0.8f);
            var car = Build(def, c, Vector3.zero, Quaternion.identity, CarRole.Police, null, under ? "Polis_Sivil" : role == "suv" ? "Polis_SUV" : "Polis");
            var vis = car.transform.Find("Gorsel");
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            bool has = false;
            foreach (var r in vis.GetComponentsInChildren<Renderer>()) { if (!has) { bounds = r.bounds; has = true; } else bounds.Encapsulate(r.bounds); }
            float roofY = bounds.max.y;
            float W = bounds.size.x, L = bounds.size.z;
            if (!under)
            {
                var white = U.Mat(new Color(0.95f, 0.95f, 0.95f), 0.8f, 0.3f);
                float doorY = bounds.min.y + bounds.size.y * 0.42f;
                for (int s = -1; s <= 1; s += 2)
                {
                    var p = U.Prim(PrimitiveType.Cube, "KapiBeyaz", vis, new Vector3(s * (W / 2 - 0.02f), doorY, bounds.center.z), new Vector3(0.04f, bounds.size.y * 0.22f, L * 0.42f), white);
                    p.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                    U.Text3D("POLİS", vis, new Vector3(s * (W / 2 + 0.005f), doorY, bounds.center.z), Quaternion.Euler(0, s < 0 ? 90 : -90, 0), 0.9f, new Color(0.05f, 0.1f, 0.5f));
                }
            }
            EngineAudio.Attach(car, false);
            var pl = car.gameObject.AddComponent<PoliceLights>();
            pl.Setup(vis, roofY, under);
            car.health = role == "suv" ? 160f : 100f;
            car.Teleport(pos, rot);
            return car;
        }
    }
}
