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

        public static CarController Build(CarEntry def, Color? paint, Vector3 pos, Quaternion rot, CarRole role, int[] tune, string name)
        {
            var go = new GameObject(name);
            var vis = new GameObject("Gorsel").transform;
            vis.SetParent(go.transform, false);

            WheelInfo[] wi = null;
            Bounds body = new Bounds(new Vector3(0, 0.75f, 0), new Vector3(1.9f, 1.3f, def.length));
            var paintMats = new List<Material>();
            var brakeMats = new List<Material>();
            var headMats = new List<Material>();
            if (def.prefab != null)
            {
                try { wi = FitModel(def, vis, ref body, paintMats, brakeMats, headMats); }
                catch (System.Exception e) { Debug.LogWarning("Model oturtulamadı (" + def.id + "): " + e.Message); wi = null; }
                if (wi == null)
                {
                    for (int i = vis.childCount - 1; i >= 0; i--) Object.Destroy(vis.GetChild(i).gameObject);
                    paintMats.Clear(); brakeMats.Clear(); headMats.Clear();
                }
            }
            if (wi == null) wi = ProceduralBody(def, vis, ref body, paintMats, brakeMats, headMats);

            Color col = paint.HasValue ? paint.Value : def.defaultColor;
            foreach (var m in paintMats) U.SetColor(m, col);

            // ---- Fizik ----
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = def.massKg;
            U.SetDamping(rb, 0.01f, 0.25f);
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = role == CarRole.Player ? CollisionDetectionMode.ContinuousDynamic : CollisionDetectionMode.Continuous;

            float W = Mathf.Clamp(body.size.x, 1.5f, 2.6f), L = body.size.z, H = Mathf.Clamp(body.size.y, 0.9f, 2.2f);
            float bottom = Mathf.Max(0.28f, body.min.y + 0.12f);
            var lower = go.AddComponent<BoxCollider>();
            float lowTop = bottom + H * 0.42f;
            lower.center = new Vector3(body.center.x, (bottom + lowTop) * 0.5f, body.center.z);
            lower.size = new Vector3(W * 0.98f, lowTop - bottom, L * 0.98f);
            var upper = go.AddComponent<BoxCollider>();
            upper.center = new Vector3(body.center.x, (lowTop + body.max.y) * 0.5f, body.center.z - L * 0.05f);
            upper.size = new Vector3(W * 0.8f, Mathf.Max(0.2f, body.max.y - lowTop), L * 0.5f);

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
            car.wheelBase = Mathf.Abs(wi[0].pos.z - wi[2].pos.z);
            car.stabilityAssist = role == CarRole.Player ? 0.45f : 0.8f;
            car.paintMats = paintMats; car.brakeMats = brakeMats; car.headMats = headMats;

            string[] wn = { "FL", "FR", "RL", "RR" };
            const float susp = 0.18f;
            for (int i = 0; i < 4; i++)
            {
                var wgo = new GameObject("WC_" + wn[i]);
                wgo.transform.SetParent(go.transform, false);
                wgo.transform.localPosition = wi[i].pos + Vector3.up * (susp * 0.5f + 0.06f);
                var wc = wgo.AddComponent<WheelCollider>();
                wc.radius = wi[i].radius;
                wc.mass = 22f;
                wc.suspensionDistance = susp;
                // kuvvetler ağırlık merkezi yüksekliğine yakın uygulanır -> virajda devrilme momenti ~0
                wc.forceAppPointDistance = Mathf.Max(0.05f, com - 0.08f);
                wc.wheelDampingRate = 0.4f;
                wc.forwardFriction = new WheelFrictionCurve { extremumSlip = 0.35f, extremumValue = 1f, asymptoteSlip = 0.9f, asymptoteValue = 0.7f, stiffness = 1.5f };
                wc.sidewaysFriction = new WheelFrictionCurve { extremumSlip = 0.22f, extremumValue = 1f, asymptoteSlip = 0.6f, asymptoteValue = 0.72f, stiffness = 1.4f };
                if (i == 0) wc.ConfigureVehicleSubsteps(5f, 12, 15);
                car.wheels[i] = wc;
                car.wheelVis[i] = wi[i].vis;
            }
            car.Configure(def, tune);

            // efektler
            AddEffects(car, body, role);

            Color ic = role == CarRole.Player ? new Color(1f, 0.85f, 0f) : role == CarRole.Police ? new Color(1f, 0.1f, 0.1f) :
                       role == CarRole.Racer ? new Color(1f, 0.35f, 1f) : new Color(0.75f, 0.75f, 0.75f);
            U.Icon(go.transform, ic, role == CarRole.Player ? 10f : 7f, true);

            go.transform.SetPositionAndRotation(pos, rot);
            return car;
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
                if (isWheel) continue;
                if (!has) { bb = rend.bounds; has = true; } else bb.Encapsulate(rend.bounds);
            }
            body = bb;

            // malzemeler: boya, fren ve far
            var cloned = new Dictionary<Material, Material>();
            Renderer biggest = null; float bigVol = 0f;
            foreach (var rend in inst.GetComponentsInChildren<Renderer>(true))
            {
                var mats = rend.sharedMaterials;
                bool changed = false;
                string rn = rend.name.ToLowerInvariant();
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;
                    string mn = m.name.ToLowerInvariant();
                    bool isPaint = mn.Contains("body") || mn.Contains("paint") || mn.Contains("boya") || (rn.Contains("body") && i == 0 && !mn.Contains("glass") && !mn.Contains("carbon"));
                    bool isBrake = mn.Contains("back light") || mn.Contains("rear light") || mn.Contains("red light") || mn.Contains("tail") || mn.Contains("brake") || mn.Contains("stop");
                    bool isHead = mn.Contains("front light") || mn.Contains("head");
                    if (!isPaint && !isBrake && !isHead) continue;
                    Material c;
                    if (!cloned.TryGetValue(m, out c))
                    {
                        c = new Material(m);
                        cloned[m] = c;
                        if (isPaint) paintMats.Add(c);
                        else if (isBrake) brakeMats.Add(c);
                        else headMats.Add(c);
                    }
                    mats[i] = c; changed = true;
                }
                if (changed) rend.sharedMaterials = mats;
                float vol = rend.bounds.size.x * rend.bounds.size.y * rend.bounds.size.z;
                if (vol > bigVol && !rend.transform.IsChildOf(vis.Find("Teker0") ?? vis)) { bigVol = vol; biggest = rend; }
            }
            if (paintMats.Count == 0 && biggest != null)
            {
                var mats = biggest.sharedMaterials;
                if (mats.Length > 0 && mats[0] != null)
                {
                    var c = new Material(mats[0]);
                    mats[0] = c; biggest.sharedMaterials = mats; paintMats.Add(c);
                }
            }
            foreach (var r in inst.GetComponentsInChildren<Renderer>()) r.shadowCastingMode = ShadowCastingMode.On;
            return result;
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

        static void AddEffects(CarController car, Bounds body, CarRole role)
        {
            if (flameMat == null) flameMat = U.Emissive(new Color(0.1f, 0.3f, 1f), new Color(0.6f, 1.6f, 6f));
            float y = Mathf.Max(0.3f, body.min.y + 0.25f);
            for (int s = -1; s <= 1; s += 2)
            {
                var f = U.Prim(PrimitiveType.Sphere, "NitroAlev", car.transform, new Vector3(s * 0.35f, y, body.min.z - 0.35f), new Vector3(0.13f, 0.13f, 0.7f), flameMat);
                f.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
                f.SetActive(false);
                car.flames.Add(f.transform);
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
                var wc = car.wheels[2 + i];
                var g = new GameObject("LastikDumani");
                g.transform.SetParent(car.transform, false);
                g.transform.localPosition = wc.transform.localPosition + Vector3.down * 0.3f;
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

        // ------------------------------------------------------------------ Polis
        public static CarController BuildPolice(CarEntry def, string role, Vector3 pos, Quaternion rot)
        {
            bool under = role == "undercover";
            Color c = under ? new Color(0.08f, 0.08f, 0.09f) : role == "suv" ? new Color(0.05f, 0.05f, 0.07f) : new Color(0.04f, 0.04f, 0.05f);
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
            var pl = car.gameObject.AddComponent<PoliceLights>();
            pl.Setup(vis, roofY, under);
            car.health = role == "suv" ? 160f : 100f;
            car.Teleport(pos, rot);
            return car;
        }
    }
}
