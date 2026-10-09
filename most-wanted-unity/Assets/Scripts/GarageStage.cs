using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MostWanted
{
    /// <summary>
    /// NFS Carbon "ARABA SEÇ" tarzı garaj: dünyadan uzakta (y = -2000) karanlık showroom, yansıtıcı zemin, mor/turkuaz
    /// spotlar, neon şeritler, dönen tabla, kendi kamerası (sürükle = döndür, tekerlek = yakınlaştır). Dünya duraklatılır,
    /// sürüş HUD'u gizlenir. Alt ekranlar: Boya, Performans, Motor Sesi, Özelleştir (sadece drift araçları).
    /// </summary>
    public class GarageStage : MonoBehaviour
    {
        public static GarageStage I;
        public static readonly Vector3 Origin = new Vector3(0f, -2000f, 0f);
        static readonly Color Teal = new Color(0.373f, 0.878f, 0.816f);   // #5fe0d0
        static readonly Color Purple = new Color(0.55f, 0.25f, 1f);

        enum Screen { Main, Paint, Perf, Sound, Custom }
        Screen screen = Screen.Main;
        public bool Active { get; private set; }

        Transform root, table;
        Camera gcam;
        ReflectionProbe probe;
        CarParts preview;
        int sel, shownSel = -1;
        float fade, fadeDir; bool swapPending;
        float yaw = 215f, pitch = 11f, dist = 7.6f;
        Vector2 lastMouse; bool dragging; float idleSpin;
        int paintSel, perfSel, soundSel, custCat, custOpt;
        CustomSave custPreview;
        string toast = ""; float toastT;

        // ortam yedeği
        bool fogB; Color fogColB, ambB; float fogDenB; FogMode fogModeB; AmbientMode ambModeB; bool sunB; Material skyB;
        Camera mainCamB;

        public static GarageStage Get()
        {
            if (I == null) { var g = new GameObject("GarajSahnesi"); I = g.AddComponent<GarageStage>(); }
            return I;
        }

        // ------------------------------------------------------------------ giriş/çıkış
        public void Enter()
        {
            if (root == null) BuildStage();
            root.gameObject.SetActive(true);
            var g = Game.I;
            fogB = RenderSettings.fog; fogColB = RenderSettings.fogColor; fogDenB = RenderSettings.fogDensity; fogModeB = RenderSettings.fogMode;
            ambB = RenderSettings.ambientLight; ambModeB = RenderSettings.ambientMode; skyB = RenderSettings.skybox;
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = 0.035f;
            RenderSettings.fogColor = new Color(0.05f, 0.035f, 0.08f);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(0.09f, 0.08f, 0.13f);
            if (g.sun != null) { sunB = g.sun.enabled; g.sun.enabled = false; }
            mainCamB = g.cam;
            if (mainCamB != null) mainCamB.enabled = false;
            gcam.enabled = true;
            screen = Screen.Main;
            sel = Mathf.Max(0, Catalog.Garage.IndexOf(Catalog.Get(SaveSystem.Data.selected)));
            shownSel = -1; fade = 1f; fadeDir = -1f;
            ShowCar(sel);
            if (probe != null) probe.RenderProbe();
            Active = true;
        }

        public void Exit()
        {
            if (!Active) return;
            Active = false;
            RenderSettings.fog = fogB; RenderSettings.fogColor = fogColB; RenderSettings.fogDensity = fogDenB; RenderSettings.fogMode = fogModeB;
            RenderSettings.ambientMode = ambModeB; RenderSettings.ambientLight = ambB; RenderSettings.skybox = skyB;
            var g = Game.I;
            if (g != null && g.sun != null) g.sun.enabled = sunB;
            if (mainCamB != null) mainCamB.enabled = true;
            if (gcam != null) gcam.enabled = false;
            if (preview != null && preview.vis != null) Destroy(preview.vis.parent.gameObject);
            preview = null; shownSel = -1;
            if (root != null) root.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------ sahne
        void BuildStage()
        {
            root = new GameObject("Showroom").transform;
            root.SetParent(transform, false);
            root.position = Origin;
            var floorMat = U.NewMat(new Color(0.025f, 0.025f, 0.035f), 0.93f, 0.5f);
            var wallMat = U.NewMat(new Color(0.03f, 0.028f, 0.045f), 0.4f, 0.1f);
            var tableMat = U.NewMat(new Color(0.08f, 0.08f, 0.1f), 0.85f, 0.8f);
            var neonT = U.Emissive(Teal * 0.3f, Teal * 5f);
            var neonP = U.Emissive(Purple * 0.3f, Purple * 5f);
            Prim(PrimitiveType.Cylinder, "Zemin", new Vector3(0, -0.05f, 0), new Vector3(60f, 0.05f, 60f), floorMat);
            table = new GameObject("Tabla").transform; table.SetParent(root, false);
            var t = Prim(PrimitiveType.Cylinder, "TablaGovde", Vector3.zero, new Vector3(7f, 0.07f, 7f), tableMat); t.transform.SetParent(table, true);
            t.transform.localPosition = new Vector3(0, 0.07f, 0);
            Prim(PrimitiveType.Cylinder, "TablaNeon", new Vector3(0, 0.02f, 0), new Vector3(7.25f, 0.02f, 7.25f), neonT);
            // arka duvar yayı + neon şeritler
            for (int i = 0; i < 16; i++)
            {
                float a = Mathf.Lerp(-150f, 150f, i / 15f) * Mathf.Deg2Rad;
                Vector3 p = new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * -17f;
                var w = Prim(PrimitiveType.Cube, "Duvar", p + Vector3.up * 5f, new Vector3(7f, 10f, 0.4f), wallMat);
                w.transform.rotation = Quaternion.LookRotation(-p.normalized);
                var n1 = Prim(PrimitiveType.Cube, "NeonSerit", p * 0.97f + Vector3.up * 0.35f, new Vector3(6.6f, 0.06f, 0.06f), i % 2 == 0 ? neonT : neonP);
                n1.transform.rotation = w.transform.rotation;
                var n2 = Prim(PrimitiveType.Cube, "NeonSerit", p * 0.97f + Vector3.up * 4.2f, new Vector3(6.6f, 0.05f, 0.05f), i % 2 == 0 ? neonP : neonT);
                n2.transform.rotation = w.transform.rotation;
            }
            // tavan ışık çubukları
            for (int i = -2; i <= 2; i++) Prim(PrimitiveType.Cube, "TavanIsik", new Vector3(i * 2.2f, 7.5f, 0), new Vector3(0.15f, 0.05f, 6f), i % 2 == 0 ? neonT : neonP);
            Spot(new Vector3(-6f, 8f, 5f), Purple, 60f);
            Spot(new Vector3(6f, 8f, -4f), Teal, 55f);
            Spot(new Vector3(0f, 10f, 0.5f), new Color(0.9f, 0.88f, 1f), 26f);
            PointL(new Vector3(0f, 1.4f, -6f), Teal, 9f, 3f);
            PointL(new Vector3(-5f, 1.0f, 3.5f), Purple, 8f, 3f);
            PointL(new Vector3(5f, 1.0f, 4f), Teal, 7f, 2f);
            var pg = new GameObject("Yansima"); pg.transform.SetParent(root, false); pg.transform.localPosition = new Vector3(0, 1.2f, 0);
            probe = pg.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.size = new Vector3(40f, 14f, 40f);
            // kamera
            var cg = new GameObject("GarajKamerasi"); cg.transform.SetParent(transform, false);
            gcam = cg.AddComponent<Camera>();
            gcam.clearFlags = CameraClearFlags.SolidColor;
            gcam.backgroundColor = new Color(0.02f, 0.015f, 0.035f);
            gcam.nearClipPlane = 0.1f; gcam.farClipPlane = 90f;
            gcam.fieldOfView = 42f;
            gcam.cullingMask = ~((1 << U.IconLayer));
            gcam.enabled = false;
            root.gameObject.SetActive(false);
        }

        GameObject Prim(PrimitiveType t, string n, Vector3 lp, Vector3 ls, Material m)
        {
            var g = U.Prim(t, n, root, lp, ls, m);
            var c = g.GetComponent<Collider>(); if (c != null) Destroy(c);
            return g;
        }

        void Spot(Vector3 lp, Color c, float angle)
        {
            var g = new GameObject("Spot"); g.transform.SetParent(root, false); g.transform.localPosition = lp;
            g.transform.LookAt(root.position + Vector3.up * 0.6f);
            var l = g.AddComponent<Light>(); l.type = LightType.Spot; l.color = c; l.spotAngle = angle; l.range = 30f; l.intensity = 9f;
            l.shadows = LightShadows.Soft;
        }

        void PointL(Vector3 lp, Color c, float range, float inten)
        {
            var g = new GameObject("Kenar"); g.transform.SetParent(root, false); g.transform.localPosition = lp;
            var l = g.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.range = range; l.intensity = inten; l.shadows = LightShadows.None;
        }

        // ------------------------------------------------------------------ vitrin aracı
        CarEntry SelDef { get { var l = Catalog.Garage; return l[Mathf.Clamp(sel, 0, l.Count - 1)]; } }

        PaintDef? SavedPaint(CarEntry def, CarSave sv)
        {
            if (sv != null && sv.color >= 0 && sv.color < Catalog.Paints.Length) return Catalog.Paints[sv.color];
            if (def.prefab == null) return new PaintDef("Fabrika", def.defaultColor, 0.5f, 0.8f);
            return null;
        }

        void ShowCar(int i)
        {
            if (preview != null && preview.vis != null) Destroy(preview.vis.parent.gameObject);
            var def = Catalog.Garage[i];
            var sv = SaveSystem.Get(def.id);
            preview = CarFactory.BuildShowroom(def, SavedPaint(def, sv), table);
            preview.vis.parent.localPosition = new Vector3(0, 0.14f, 0);
            if (CustomCatalog.IsDriftCar(def) && sv != null && sv.owned) CustomKit.Apply(preview, CustomCatalog.Get(sv), null, true);
            shownSel = i;
        }

        void RequestSwap(int newSel)
        {
            sel = (newSel + Catalog.Garage.Count) % Catalog.Garage.Count;
            swapPending = true; fadeDir = 1f;
        }

        void Toast(string s) { toast = s; toastT = 2.5f; }

        // ------------------------------------------------------------------ güncelleme / girdi
        void Update()
        {
            if (!Active) return;
            float dt = Time.unscaledDeltaTime;
            toastT -= dt;
            // geçiş
            fade = Mathf.Clamp01(fade + fadeDir * dt / 0.14f);
            if (swapPending && fade >= 1f) { ShowCar(sel); swapPending = false; fadeDir = -1f; }
            // tabla + kamera
            if (Input.GetMouseButtonDown(0) && Input.mousePosition.y > UnityEngine.Screen.height * 0.25f) { dragging = true; lastMouse = Input.mousePosition; }
            if (Input.GetMouseButtonUp(0)) dragging = false;
            if (dragging)
            {
                Vector2 mp = Input.mousePosition; Vector2 d = mp - lastMouse; lastMouse = mp;
                yaw += d.x * 0.25f; pitch = Mathf.Clamp(pitch - d.y * 0.15f, 2f, 40f); idleSpin = 0f;
            }
            else
            {
                idleSpin += dt;
                if (idleSpin > 1.5f && table != null) table.Rotate(0f, 9f * dt, 0f, Space.World);
            }
            dist = Mathf.Clamp(dist - Input.mouseScrollDelta.y * 0.5f, 4.8f, 12f);
            float orbit = yaw + Mathf.Sin(Time.unscaledTime * 0.25f) * 3f;
            Quaternion q = Quaternion.Euler(pitch, orbit, 0f);
            Vector3 target = Origin + Vector3.up * 0.75f;
            gcam.transform.position = target - q * Vector3.forward * dist;
            gcam.transform.rotation = Quaternion.LookRotation(target - gcam.transform.position);
            HandleKeys();
        }

        void HandleKeys()
        {
            var def = SelDef;
            var sv = SaveSystem.Get(def.id);
            bool owned = sv != null && sv.owned;
            bool drift = CustomCatalog.IsDriftCar(def);
            if (Input.GetKeyDown(KeyCode.F12)) Photo();
            switch (screen)
            {
                case Screen.Main:
                    if (Input.GetKeyDown(KeyCode.LeftArrow)) RequestSwap(sel - 1);
                    if (Input.GetKeyDown(KeyCode.RightArrow)) RequestSwap(sel + 1);
                    if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) BuyOrSelect(def, sv);
                    if (owned && Input.GetKeyDown(KeyCode.P)) OpenPaint(sv);
                    if (owned && Input.GetKeyDown(KeyCode.U)) { screen = Screen.Perf; perfSel = 0; }
                    if (owned && drift && Input.GetKeyDown(KeyCode.C)) OpenCustom(sv);
                    if (owned && Input.GetKeyDown(KeyCode.Delete)) Sell(def, sv);
                    if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.E)) Game.I.CloseMenu();
                    break;
                case Screen.Paint:
                    if (Input.GetKeyDown(KeyCode.LeftArrow)) { paintSel = (paintSel - 1 + Catalog.Paints.Length + 1) % (Catalog.Paints.Length + 1); PreviewPaint(def); }
                    if (Input.GetKeyDown(KeyCode.RightArrow)) { paintSel = (paintSel + 1) % (Catalog.Paints.Length + 1); PreviewPaint(def); }
                    if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) BuyPaint(def, sv);
                    if (Input.GetKeyDown(KeyCode.Escape)) { screen = Screen.Main; ShowCar(sel); }
                    break;
                case Screen.Perf:
                    if (Input.GetKeyDown(KeyCode.UpArrow)) perfSel = (perfSel - 1 + Catalog.ShopTuneCount) % Catalog.ShopTuneCount;
                    if (Input.GetKeyDown(KeyCode.DownArrow)) perfSel = (perfSel + 1) % Catalog.ShopTuneCount;
                    if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) BuyTune(def, sv, perfSel);
                    if (Input.GetKeyDown(KeyCode.Escape)) screen = Screen.Main;
                    break;
                case Screen.Sound:
                    int n = EngineAudio.EngineTypes.Length + 1;
                    if (Input.GetKeyDown(KeyCode.LeftArrow)) soundSel = (soundSel - 1 + n) % n;
                    if (Input.GetKeyDown(KeyCode.RightArrow)) soundSel = (soundSel + 1) % n;
                    if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) BuySound(def, sv);
                    if (Input.GetKeyDown(KeyCode.Escape)) screen = Screen.Main;
                    break;
                case Screen.Custom:
                    if (Input.GetKeyDown(KeyCode.UpArrow)) SetCustCat(custCat - 1, sv);
                    if (Input.GetKeyDown(KeyCode.DownArrow)) SetCustCat(custCat + 1, sv);
                    if (Input.GetKeyDown(KeyCode.LeftArrow)) SetCustOpt(custOpt - 1, def);
                    if (Input.GetKeyDown(KeyCode.RightArrow)) SetCustOpt(custOpt + 1, def);
                    if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) BuyCustom(def, sv);
                    if (Input.GetKeyDown(KeyCode.Escape)) { screen = Screen.Main; ShowCar(sel); }
                    break;
            }
        }

        void Photo()
        {
            string path = System.IO.Path.Combine(Application.persistentDataPath, "garaj_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
            // ScreenCapture modülüne bağımlı olmamak için garaj kamerasını RenderTexture'a çizip PNG kaydediyoruz
            if (gcam == null) return;
            int w = 1920, h = 1080;
            var rt = RenderTexture.GetTemporary(w, h, 24);
            var prevT = gcam.targetTexture; var prevA = RenderTexture.active;
            gcam.targetTexture = rt; gcam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0); tex.Apply();
            gcam.targetTexture = prevT; RenderTexture.active = prevA;
            RenderTexture.ReleaseTemporary(rt);
            System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.Destroy(tex);
            Toast("Fotoğraf kaydedildi: " + path);
        }

        // ------------------------------------------------------------------ işlemler
        void BuyOrSelect(CarEntry def, CarSave sv)
        {
            var d = SaveSystem.Data;
            if (sv == null) return;
            if (!sv.owned)
            {
                if (d.money < def.price) { Toast("Yeterli paran yok (" + U.Money(def.price) + ")"); return; }
                d.money -= def.price; sv.owned = true; d.selected = def.id; SaveSystem.Save();
                Toast(def.displayName + " satın alındı!");
                ShowCar(sel);
            }
            else if (d.selected != def.id) { d.selected = def.id; SaveSystem.Save(); Toast(def.displayName + " seçildi"); }
            else Game.I.CloseMenu();
        }

        void Sell(CarEntry def, CarSave sv)
        {
            var d = SaveSystem.Data;
            if (d.selected == def.id || SaveSystem.OwnedCount() <= 1) { Toast("Seçili aracı satamazsın"); return; }
            int sell = Mathf.RoundToInt(def.price * 0.6f);
            d.money += sell; sv.owned = false; sv.tune = new int[Catalog.TuneCount]; sv.color = -1; sv.custom = new CustomSave(); SaveSystem.Save();
            Toast(def.displayName + " satıldı (+" + U.Money(sell) + ")");
            ShowCar(sel);
        }

        void OpenPaint(CarSave sv) { screen = Screen.Paint; paintSel = sv.color + 1; }

        void PreviewPaint(CarEntry def)
        {
            if (preview == null) return;
            if (paintSel == 0) { ShowCar(sel); return; }
            foreach (var m in preview.paintMats) U.ApplyPaint(m, Catalog.Paints[paintSel - 1]);
        }

        void BuyPaint(CarEntry def, CarSave sv)
        {
            var d = SaveSystem.Data;
            int c = paintSel - 1;
            if (c == sv.color) { Toast("Zaten bu renk"); return; }
            if (c < 0) { sv.color = -1; SaveSystem.Save(); Toast("Fabrika rengi"); ShowCar(sel); return; }
            if (d.money < Catalog.PaintCost) { Toast("Yeterli paran yok"); return; }
            d.money -= Catalog.PaintCost; sv.color = c; SaveSystem.Save();
            Toast("Boya: " + Catalog.Paints[c].name);
        }

        void BuyTune(CarEntry def, CarSave sv, int k)
        {
            var d = SaveSystem.Data;
            int level = sv.tune[k];
            if (level >= Catalog.MaxTune) { Toast("Maksimum seviye"); return; }
            int cost = Catalog.TuneCost(def, level);
            if (d.money < cost) { Toast("Yeterli paran yok (" + U.Money(cost) + ")"); return; }
            d.money -= cost; sv.tune[k]++; SaveSystem.Save();
            Toast(Catalog.TuneNames[k] + " seviye " + sv.tune[k] + "!");
        }

        void OpenSound(CarSave sv)
        {
            screen = Screen.Sound;
            soundSel = string.IsNullOrEmpty(sv.engineSound) ? 0 : System.Array.IndexOf(EngineAudio.EngineTypes, sv.engineSound) + 1;
        }

        void BuySound(CarEntry def, CarSave sv)
        {
            var d = SaveSystem.Data;
            string want = soundSel == 0 ? "" : EngineAudio.EngineTypes[soundSel - 1];
            if (want == (sv.engineSound ?? "")) { Toast("Zaten bu ses"); return; }
            if (soundSel != 0)
            {
                if (d.money < 1000) { Toast("Yeterli paran yok"); return; }
                d.money -= 1000;
            }
            sv.engineSound = want; SaveSystem.Save();
            Toast("Motor sesi: " + (soundSel == 0 ? "Orijinal" : EngineAudio.EngineNames[soundSel - 1]));
        }

        void OpenCustom(CarSave sv)
        {
            screen = Screen.Custom;
            var cs = CustomCatalog.Get(sv);
            custPreview = JsonUtility.FromJson<CustomSave>(JsonUtility.ToJson(cs));
            custPreview.Fix();
            custCat = 0; custOpt = custPreview.v[0];
        }

        void SetCustCat(int c, CarSave sv)
        {
            custCat = (c + CustomCatalog.Count) % CustomCatalog.Count;
            // önizlemeyi kayıtlı duruma döndür (satın alınmamış deneme kalmasın)
            var cs = CustomCatalog.Get(sv);
            custPreview = JsonUtility.FromJson<CustomSave>(JsonUtility.ToJson(cs)); custPreview.Fix();
            custOpt = custPreview.v[custCat];
            ApplyPreviewKit();
        }

        void SetCustOpt(int o, CarEntry def)
        {
            int n = CustomCatalog.Options[custCat].Length;
            custOpt = (o + n) % n;
            custPreview.v[custCat] = custOpt;
            ApplyPreviewKit();
        }

        void ApplyPreviewKit()
        {
            if (preview == null) return;
            CustomKit.Apply(preview, custPreview, null, true);
        }

        void BuyCustom(CarEntry def, CarSave sv)
        {
            var d = SaveSystem.Data;
            var cs = CustomCatalog.Get(sv);
            if (cs.v[custCat] == custOpt) { Toast("Zaten takılı"); return; }
            int price = cs.Owns(custCat, custOpt) ? 0 : CustomCatalog.Price(def, custCat, custOpt);
            if (d.money < price) { Toast("Yeterli paran yok (" + U.Money(price) + ")"); return; }
            d.money -= price;
            cs.own[custCat] |= 1 << custOpt;
            cs.v[custCat] = custOpt;
            SaveSystem.Save();
            Toast(CustomCatalog.Names[custCat] + ": " + CustomCatalog.Options[custCat][custOpt] + (price > 0 ? "  -" + U.Money(price) : ""));
        }

        // ------------------------------------------------------------------ IMGUI (Carbon tarzı)
        GUIStyle sTitle, sName, sBig, sMid, sSmall, sTiny, sHint, sBtn;
        Texture2D panelTex, chevronTex, flameTex, lockTex, whiteTex;

        void InitGui()
        {
            if (sTitle != null) return;
            var f = U.BuiltinFont;
            sTitle = new GUIStyle { font = f, fontSize = 40, fontStyle = FontStyle.BoldAndItalic, alignment = TextAnchor.MiddleLeft };
            sTitle.normal.textColor = Color.white;
            sName = new GUIStyle(sTitle) { fontSize = 48, alignment = TextAnchor.MiddleCenter };
            sBig = new GUIStyle(sTitle) { fontSize = 30 };
            sMid = new GUIStyle(sTitle) { fontSize = 24, fontStyle = FontStyle.Bold };
            sSmall = new GUIStyle(sTitle) { fontSize = 20, fontStyle = FontStyle.Bold };
            sTiny = new GUIStyle(sTitle) { fontSize = 16, fontStyle = FontStyle.Bold };
            sHint = new GUIStyle(sTiny) { alignment = TextAnchor.MiddleCenter, fontSize = 18 };
            sHint.normal.textColor = new Color(0.85f, 0.95f, 0.95f);
            panelTex = MakePanelTex();
            sBtn = new GUIStyle { font = f, fontSize = 22, fontStyle = FontStyle.BoldAndItalic, alignment = TextAnchor.MiddleCenter, border = new RectOffset(14, 14, 14, 14) };
            sBtn.normal.background = panelTex; sBtn.hover.background = panelTex;
            sBtn.normal.textColor = Color.white; sBtn.hover.textColor = Teal;
            chevronTex = MakeChevron();
            flameTex = MakeFlame();
            lockTex = MakeLock();
            whiteTex = Texture2D.whiteTexture;
        }

        static Texture2D MakePanelTex()
        {
            const int S = 48, cut = 12;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    // sol üst ve sağ alt köşe kesik (açılı panel)
                    int yy = S - 1 - y;   // üstten
                    bool outTL = x + yy < cut, outBR = (S - 1 - x) + y < cut;
                    float edge = Mathf.Min(Mathf.Min(x, S - 1 - x), Mathf.Min(y, S - 1 - y));
                    float diag = Mathf.Min(x + yy - cut, (S - 1 - x) + y - cut) * 0.7071f;
                    Color c = new Color(0.02f, 0.05f, 0.07f, 0.78f);
                    if (edge < 2f || (diag >= 0f && diag < 1.6f)) c = new Color(Teal.r, Teal.g, Teal.b, 0.95f);
                    if (outTL || outBR) c = new Color(0, 0, 0, 0);
                    t.SetPixel(x, y, c);
                }
            t.Apply();
            return t;
        }

        static Texture2D MakeChevron()
        {
            const int W = 96, H = 48;
            var t = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
            Color g = new Color(0.45f, 0.95f, 0.25f, 1f);
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    Color c = new Color(0, 0, 0, 0);
                    for (int k = 0; k < 3; k++)
                    {
                        float x0 = 8 + k * 28;
                        float d = Mathf.Abs(y - H / 2f) * 0.8f;
                        float u = x - x0 - d;
                        if (u >= 0 && u < 12) c = new Color(g.r, g.g, g.b, 1f - k * 0.25f);
                    }
                    t.SetPixel(x, y, c);
                }
            t.Apply();
            return t;
        }

        static Texture2D MakeFlame()
        {
            const int S = 40;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (x - S / 2f) / (S / 2f), v = y / (float)S;
                    float w = (1f - v) * 0.9f * Mathf.Sqrt(Mathf.Clamp01(v * 2.5f)) + 0.05f * Mathf.Sin(v * 20f);
                    Color c = Mathf.Abs(u) < w ? Color.Lerp(new Color(1f, 0.85f, 0.2f, 1f), new Color(1f, 0.25f, 0.05f, 1f), v) : new Color(0, 0, 0, 0);
                    t.SetPixel(x, y, c);
                }
            t.Apply();
            return t;
        }

        static Texture2D MakeLock()
        {
            const int S = 32;
            var t = new Texture2D(S, S, TextureFormat.RGBA32, false);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    bool body = y < 18 && x > 5 && x < 26;
                    float dx = x - 15.5f, dy = y - 20f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    bool shackle = y >= 16 && r > 6f && r < 9.5f;
                    bool hole = Mathf.Abs(x - 15.5f) < 2f && y > 6 && y < 12;
                    t.SetPixel(x, y, (body && !hole) || shackle ? new Color(1f, 0.85f, 0.3f, 1f) : new Color(0, 0, 0, 0));
                }
            t.Apply();
            return t;
        }

        void Box(Rect r, Color c) { var o = GUI.color; GUI.color = c; GUI.DrawTexture(r, whiteTex); GUI.color = o; }
        void Panel(Rect r) { GUI.Box(r, GUIContent.none, sBtn); }
        void Label(Rect r, string s, GUIStyle st, Color c)
        {
            var o = st.normal.textColor;
            st.normal.textColor = new Color(0, 0, 0, 0.8f); GUI.Label(new Rect(r.x + 2, r.y + 2, r.width, r.height), s, st);
            st.normal.textColor = c; GUI.Label(r, s, st);
            st.normal.textColor = o;
        }

        void StatBar(float x, float y, string name, float v)
        {
            Label(new Rect(x, y, 200, 30), name, sSmall, Color.white);
            const int seg = 14;
            int lit = Mathf.RoundToInt(Mathf.Clamp01(v) * seg);
            for (int i = 0; i < seg; i++) Box(new Rect(x + 190 + i * 17, y + 7, 14, 16), i < lit ? Teal : new Color(1f, 1f, 1f, 0.12f));
        }

        static string ClassName(CarEntry def)
        {
            string n = def.displayName.ToLowerInvariant();
            if (n.Contains("hellcat") || n.Contains("challenger") || n.Contains("raptor") || n.Contains("mustang") || n.Contains("cybertruck") || n.Contains("camaro")) return "KAS";
            if (n.Contains("chiron") || n.Contains("bugatti") || n.Contains("911") || n.Contains("porsche") || n.Contains("lambo") || n.Contains("ferrari")) return "EGZOTİK";
            return "TUNER";
        }

        void OnGUI()
        {
            if (!Active || Game.I == null) return;
            InitGui();
            GUI.depth = -10;
            float scale = Mathf.Max(0.05f, UnityEngine.Screen.height / 1080f);
            float W = UnityEngine.Screen.width / scale, H = 1080f;
            GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(scale, scale, 1f));
            var d = SaveSystem.Data;
            var def = SelDef;
            var sv = SaveSystem.Get(def.id);
            bool owned = sv != null && sv.owned;
            bool drift = CustomCatalog.IsDriftCar(def);

            // üst sol: logo + başlık
            GUI.DrawTexture(new Rect(40, 34, 96, 48), chevronTex);
            string title = screen == Screen.Main ? "•ARABA SEÇ•" : screen == Screen.Paint ? "•BOYA•" : screen == Screen.Perf ? "•PERFORMANS•" : screen == Screen.Sound ? "•MOTOR SESİ•" : "•ÖZELLEŞTİR•";
            Label(new Rect(150, 30, 700, 56), title, sTitle, Color.white);
            Box(new Rect(150, 86, 420, 3), Teal);

            // üst sağ: istatistikler
            var t = sv != null ? sv.tune : new int[Catalog.TuneCount];
            float top = def.topSpeedKmh * (1f + 0.012f * (t[1] + t[2] + t[0]));
            float tq = def.torqueNm * (1f + 0.08f * t[0] + 0.06f * t[1]);
            float grip = def.grip * (1f + 0.045f * t[4] + 0.02f * t[3]);
            Rect sp = new Rect(W - 560, 26, 530, 210);
            Panel(sp);
            StatBar(sp.x + 22, sp.y + 18, "SON HIZ", (top - 150f) / 200f);
            StatBar(sp.x + 22, sp.y + 52, "İVME", (tq / def.massKg - 0.15f) / 0.35f);
            StatBar(sp.x + 22, sp.y + 86, "YOL TUTUŞ", (grip - 0.8f) / 0.6f);
            Label(new Rect(sp.x + 22, sp.y + 126, 480, 30), "Para: " + U.Money(d.money), sMid, Teal);
            Label(new Rect(sp.x + 22, sp.y + 158, 480, 30), "Değer: " + U.Money(def.price) + (def.powerHp > 0 ? "   •   " + def.powerHp + " hp" : ""), sMid, Color.white);

            // alt orta bant: sınıf + isim + oklar
            Rect band = new Rect(W / 2 - 520, H - 240, 1040, 130);
            Panel(band);
            // ısı simgesi
            GUI.DrawTexture(new Rect(band.x + 26, band.y + 36, 40, 40), flameTex);
            Label(new Rect(band.x + 72, band.y + 36, 60, 40), Mathf.Clamp(d.careerBounty / 25000, 0, 5).ToString(), sBig, new Color(1f, 0.7f, 0.25f));
            int lvl = def.price < 30000 ? 1 : def.price < 120000 ? 2 : 3;
            Label(new Rect(band.x + 26, band.y + 82, 220, 30), ClassName(def) + "  •  SEVİYE " + lvl, sTiny, Teal);
            if (GUI.Button(new Rect(band.x + 180, band.y + 30, 60, 60), "<", sBtn) && screen == Screen.Main) RequestSwap(sel - 1);
            if (GUI.Button(new Rect(band.xMax - 240, band.y + 30, 60, 60), ">", sBtn) && screen == Screen.Main) RequestSwap(sel + 1);
            Label(new Rect(band.x + 250, band.y + 14, band.width - 500, 70), def.displayName.ToUpperInvariant(), sName, Color.white);
            string status = owned ? (d.selected == def.id ? "SAHİP  •  SEÇİLİ" : "SAHİP") : "SATIN AL (Enter)  " + U.Money(def.price);
            Label(new Rect(band.x + 250, band.y + 82, band.width - 500, 34), status, new GUIStyle(sMid) { alignment = TextAnchor.MiddleCenter }, owned ? Teal : new Color(1f, 0.8f, 0.3f));
            if (!owned) GUI.DrawTexture(new Rect(band.xMax - 160, band.y + 40, 40, 40), lockTex);
            if (drift) Label(new Rect(band.xMax - 120, band.y + 82, 110, 30), "DRIFT", sTiny, new Color(1f, 0.45f, 0.9f));

            // alt ekranlar
            if (screen == Screen.Paint) DrawPaint(W, H, def, sv);
            else if (screen == Screen.Perf) DrawPerf(W, H, def, sv);
            else if (screen == Screen.Sound) DrawSound(W, H, def, sv);
            else if (screen == Screen.Custom) DrawCustom(W, H, def, sv);

            // ipuçları
            string hint = screen == Screen.Main
                ? "Geri (Esc)  •  Seç (Enter)  •  Boya (P)  •  Performans (U)" + (drift ? "  •  Özelleştir (C)" : "") + "  •  Sat (Del)  •  Fotoğraf (F12)  •  ←/→ araç  •  Fare: döndür / yakınlaştır"
                : screen == Screen.Perf ? "Geri (Esc)  •  ↑/↓ kategori  •  Yükselt (Enter)"
                : screen == Screen.Custom ? "Geri (Esc)  •  ↑/↓ kategori  •  ←/→ seçenek (canlı önizleme)  •  Uygula/Satın Al (Enter)"
                : "Geri (Esc)  •  ←/→ seç  •  Satın Al (Enter)";
            Box(new Rect(0, H - 56, W, 56), new Color(0, 0, 0, 0.6f));
            Label(new Rect(0, H - 52, W, 48), hint, sHint, sHint.normal.textColor);

            if (toastT > 0f) { Panel(new Rect(W / 2 - 420, 120, 840, 54)); Label(new Rect(W / 2 - 410, 124, 820, 46), toast, new GUIStyle(sMid) { alignment = TextAnchor.MiddleCenter }, Color.white); }
            if (fade > 0.001f) Box(new Rect(0, 0, W, H), new Color(0, 0, 0, fade));
        }

        void DrawPaint(float W, float H, CarEntry def, CarSave sv)
        {
            int n = Catalog.Paints.Length + 1;
            float sw = 54f, gap = 8f, total = n * (sw + gap);
            float x0 = W / 2 - total / 2, y = H - 330;
            Panel(new Rect(x0 - 20, y - 50, total + 40, sw + 70));
            string nm = paintSel == 0 ? "Fabrika" : Catalog.Paints[paintSel - 1].name;
            int price = paintSel - 1 == sv.color ? 0 : paintSel == 0 ? 0 : Catalog.PaintCost;
            Label(new Rect(x0, y - 46, total, 36), nm + (price > 0 ? "   " + U.Money(price) : paintSel - 1 == sv.color ? "   (mevcut)" : ""), new GUIStyle(sMid) { alignment = TextAnchor.MiddleCenter }, Color.white);
            for (int i = 0; i < n; i++)
            {
                Rect r = new Rect(x0 + i * (sw + gap), y, sw, sw);
                if (i == paintSel) Box(new Rect(r.x - 4, r.y - 4, r.width + 8, r.height + 8), Teal);
                Box(r, i == 0 ? new Color(0.5f, 0.5f, 0.5f) : Catalog.Paints[i - 1].color);
                if (i == 0) Label(r, "F", new GUIStyle(sMid) { alignment = TextAnchor.MiddleCenter }, Color.white);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { paintSel = i; PreviewPaint(def); }
            }
        }

        void DrawPerf(float W, float H, CarEntry def, CarSave sv)
        {
            Rect r = new Rect(40, 140, 640, 60 + Catalog.ShopTuneCount * 52);
            Panel(r);
            for (int k = 0; k < Catalog.ShopTuneCount; k++)
            {
                float y = r.y + 24 + k * 52;
                if (k == perfSel) Box(new Rect(r.x + 10, y - 4, r.width - 20, 46), new Color(Teal.r, Teal.g, Teal.b, 0.18f));
                Label(new Rect(r.x + 24, y, 240, 38), Catalog.TuneNames[k].ToUpperInvariant(), sSmall, k == perfSel ? Teal : Color.white);
                int lv = sv.tune[k];
                for (int i = 0; i < Catalog.MaxTune; i++) Box(new Rect(r.x + 270 + i * 34, y + 10, 28, 18), i < lv ? Teal : new Color(1, 1, 1, 0.15f));
                string pr = lv >= Catalog.MaxTune ? "MAKS" : U.Money(Catalog.TuneCost(def, lv));
                Label(new Rect(r.x + 400, y, 220, 38), pr, sSmall, lv >= Catalog.MaxTune ? new Color(1, 1, 1, 0.5f) : new Color(1f, 0.85f, 0.35f));
                if (GUI.Button(new Rect(r.x + 10, y - 4, r.width - 20, 46), GUIContent.none, GUIStyle.none)) { if (perfSel == k) BuyTune(def, sv, k); perfSel = k; }
            }
        }

        void DrawSound(float W, float H, CarEntry def, CarSave sv)
        {
            Rect r = new Rect(W / 2 - 360, H - 340, 720, 80);
            Panel(r);
            string nm = soundSel == 0 ? "ORİJİNAL" : EngineAudio.EngineNames[soundSel - 1].ToUpperInvariant();
            string cur = string.IsNullOrEmpty(sv.engineSound) ? "" : sv.engineSound;
            string want = soundSel == 0 ? "" : EngineAudio.EngineTypes[soundSel - 1];
            Label(new Rect(r.x, r.y + 6, r.width, 40), "<  " + nm + "  >", new GUIStyle(sBig) { alignment = TextAnchor.MiddleCenter }, Color.white);
            Label(new Rect(r.x, r.y + 44, r.width, 30), want == cur ? "TAKILI" : soundSel == 0 ? "Ücretsiz" : U.Money(1000), new GUIStyle(sSmall) { alignment = TextAnchor.MiddleCenter }, Teal);
        }

        void DrawCustom(float W, float H, CarEntry def, CarSave sv)
        {
            var cs = CustomCatalog.Get(sv);
            Rect r = new Rect(40, 130, 620, 40 + CustomCatalog.Count * 36);
            Panel(r);
            for (int k = 0; k < CustomCatalog.Count; k++)
            {
                float y = r.y + 18 + k * 36;
                bool on = k == custCat;
                if (on) Box(new Rect(r.x + 10, y - 2, r.width - 20, 34), new Color(Teal.r, Teal.g, Teal.b, 0.18f));
                Color nc = k == CustomCatalog.Drift ? new Color(1f, 0.45f, 0.9f) : on ? Teal : Color.white;
                Label(new Rect(r.x + 22, y, 260, 30), CustomCatalog.Names[k], sTiny, nc);
                int opt = on ? custOpt : cs.v[k];
                string o = CustomCatalog.Options[k][opt];
                Label(new Rect(r.x + 290, y, 310, 30), (on ? "<  " : "") + o + (on ? "  >" : ""), sTiny, on ? Color.white : new Color(1, 1, 1, 0.7f));
                if (GUI.Button(new Rect(r.x + 10, y - 2, r.width - 20, 34), GUIContent.none, GUIStyle.none)) { if (custCat == k) SetCustOpt(custOpt + 1, def); else SetCustCat(k, sv); }
            }
            // fiyat paneli
            int price = cs.v[custCat] == custOpt ? 0 : cs.Owns(custCat, custOpt) ? 0 : CustomCatalog.Price(def, custCat, custOpt);
            string st = cs.v[custCat] == custOpt ? "TAKILI" : cs.Owns(custCat, custOpt) ? "SAHİP — Uygula (Enter)" : "SATIN AL (Enter)  " + U.Money(price);
            Rect pr = new Rect(W / 2 - 300, H - 330, 600, 64);
            Panel(pr);
            Label(pr, st, new GUIStyle(sMid) { alignment = TextAnchor.MiddleCenter }, cs.v[custCat] == custOpt ? Teal : new Color(1f, 0.85f, 0.35f));
            if (custCat == CustomCatalog.Drift)
                Label(new Rect(W / 2 - 520, H - 380, 1040, 40), "Drift ayarı: düşük arka tutuş, 57° direksiyon, AWD'de arka ağırlıklı, kilitli diferansiyel, el freni drift yardımı (Saarg fiziği)", new GUIStyle(sTiny) { alignment = TextAnchor.MiddleCenter }, new Color(1f, 0.6f, 0.95f));
        }
    }
}
