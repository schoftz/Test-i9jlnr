using System;
using UnityEngine;

namespace MostWanted
{
    /// <summary>
    /// Ara sahne oynatıcı: çekim listesi (çapaya göre kamera konumu/bakış, kaydırma), sinema bantları, kararma,
    /// konuşmacı adı teal altyazı. Space/Enter: sonraki satır, Esc: tümünü geç. Ölçeksiz zaman.
    /// Oynarken oyuncu girişi ve HUD kapalı (StoryManager.Cinematic), bitince takip kamerası geri gelir.
    /// </summary>
    public class Cutscene : MonoBehaviour
    {
        static Cutscene inst;
        public static bool Active { get { return inst != null && inst.def != null; } }

        CutsceneDef def;
        Action onEnd;
        int shot;
        float shotT, totalT, bars, black = 1f;
        bool ending;
        float endT;
        CarController prop;
        Vector3 basePos, garagePos; Quaternion baseRot, garageRot;
        float savedFov = 60f;
        bool rigWas = true, destroying;

        const float FadeDur = 0.45f;

        public static void Play(CutsceneDef d, Action done)
        {
            var g = Game.I;
            if (d == null || g == null || g.player == null || g.cam == null || d.shots.Count == 0)
            {
                SafeInvoke(done);
                return;
            }
            if (inst != null) inst.Finish(false);
            var go = new GameObject("AraSahne");
            inst = go.AddComponent<Cutscene>();
            inst.Begin(g, d, done);
        }

        public static void Skip() { if (inst != null) inst.StartEnd(); }

        static void SafeInvoke(Action a)
        {
            if (a == null) return;
            try { a(); } catch (Exception e) { Debug.LogWarning("[MW] Ara sahne sonu hatası: " + e.Message); }
        }

        void Begin(Game g, CutsceneDef d, Action done)
        {
            def = d; onEnd = done; shot = 0; shotT = 0f; totalT = 0f; bars = 0f; black = 1f; ending = false;
            var pt = g.player.transform;
            basePos = pt.position; baseRot = Quaternion.LookRotation(U.Flat(pt.forward).sqrMagnitude > 0.01f ? U.Flat(pt.forward).normalized : Vector3.forward);
            garagePos = g.world != null ? g.world.garagePos : basePos;
            garageRot = g.world != null ? g.world.garageRot : baseRot;
            savedFov = g.cam.fieldOfView;
            if (g.rig != null) { rigWas = g.rig.enabled; g.rig.enabled = false; }
            try { g.player.locked = true; U.SetVel(g.player.rb, Vector3.zero); } catch { }
            StoryMessages.ClearPopups();
            if (d.prop) SpawnProp(g, d.rival);
            Apply(0f);
        }

        void SpawnProp(Game g, int rival)
        {
            try
            {
                if (rival < 0 || rival >= StoryData.Rivals.Length) return;
                var spec = g.career != null ? g.career.RivalCar(rival) : null;
                if (spec == null) return;
                Vector3 f = baseRot * Vector3.forward, r = baseRot * Vector3.right;
                Vector3 p = Game.GroundSnap(basePos + f * 11f + r * 3.2f) - Vector3.up * 0.6f;
                if (g.traffic != null) g.traffic.ClearAround(p, 30f);
                prop = CarFactory.Build(spec, Career.RivalPaint(rival), p, Quaternion.LookRotation(-f), CarRole.Racer, null, "Hikaye_" + StoryData.Rivals[rival].nick);
                if (prop != null) prop.locked = true;
            }
            catch (Exception e) { Debug.LogWarning("[MW] Ara sahne aracı oluşturulamadı: " + e.Message); prop = null; }
        }

        void Update()
        {
            if (def == null) return;
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            totalT += dt;
            bars = Mathf.MoveTowards(bars, ending ? 0f : 1f, dt / 0.5f);
            var g = Game.I;
            if (g == null || g.player == null) { Finish(true); return; }
            try { g.player.locked = true; } catch { }

            if (ending)
            {
                endT += dt;
                black = Mathf.Clamp01(endT / FadeDur);
                if (endT >= FadeDur) Finish(true);
                return;
            }
            black = Mathf.MoveTowards(black, 0f, dt / FadeDur);
            if (Input.GetKeyDown(KeyCode.Escape)) { StartEnd(); return; }
            bool next = totalT > 0.3f && (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetMouseButtonDown(0));
            shotT += dt;
            if (next || shotT >= def.shots[shot].dur)
            {
                shot++; shotT = 0f;
                if (shot >= def.shots.Count) { shot = def.shots.Count - 1; StartEnd(); return; }
            }
        }

        void LateUpdate()
        {
            if (def == null) return;
            Apply(Mathf.Min(Time.unscaledDeltaTime, 0.1f));
        }

        void StartEnd() { if (!ending) { ending = true; endT = 0f; } }

        void Anchor(int a, out Vector3 p, out Quaternion r)
        {
            p = basePos; r = baseRot;
            var g = Game.I;
            if (a == 0 && g != null && g.player != null) { p = g.player.transform.position; r = baseRot; }
            else if (a == 1) { if (prop != null) { p = prop.transform.position; r = prop.transform.rotation; } else { p = basePos + baseRot * Vector3.forward * 11f; r = baseRot * Quaternion.Euler(0, 180, 0); } }
            else if (a == 2) { p = garagePos; r = garageRot; }
        }

        void Apply(float dt)
        {
            var g = Game.I;
            if (g == null || g.cam == null || def == null) return;
            var s = def.shots[Mathf.Clamp(shot, 0, def.shots.Count - 1)];
            Vector3 ap; Quaternion ar;
            Anchor(s.anchor, out ap, out ar);
            float k = s.dur > 0f ? Mathf.Clamp01(shotT / s.dur) : 1f;
            k = k * k * (3f - 2f * k);
            Vector3 pos = ap + ar * Vector3.Lerp(s.pos, s.pos2, k);
            Vector3 look = ap + ar * s.look;
            // duvara gömülmesin
            RaycastHit hit;
            Vector3 d = pos - look;
            int mask = ~((1 << OptimizationManager.TrafficLayer) | (1 << U.IconLayer));
            if (d.sqrMagnitude > 0.01f && Physics.Raycast(look, d.normalized, out hit, d.magnitude, mask, QueryTriggerInteraction.Ignore)
                && (g.player == null || hit.rigidbody != g.player.rb) && (prop == null || hit.rigidbody != prop.rb))
                pos = look + d.normalized * Mathf.Max(1.5f, hit.distance - 0.3f);
            var t = g.cam.transform;
            t.position = pos;
            Vector3 dir = look - pos;
            if (dir.sqrMagnitude > 0.0001f) t.rotation = Quaternion.LookRotation(dir);
            g.cam.fieldOfView = Mathf.Lerp(g.cam.fieldOfView, 42f, dt <= 0f ? 1f : 1f - Mathf.Exp(-dt * 4f));
        }

        void Finish(bool invoke)
        {
            var g = Game.I;
            var done = onEnd;
            onEnd = null;
            def = null;
            try
            {
                if (prop != null) Destroy(prop.gameObject);
                prop = null;
                if (g != null)
                {
                    if (g.cam != null) g.cam.fieldOfView = savedFov;
                    if (g.player != null && !(g.race != null && g.race.Counting)) g.player.locked = false;
                    if (g.rig != null) { g.rig.enabled = true; if (g.player != null) g.rig.target = g.player; g.rig.Snap(); }
                }
            }
            catch (Exception e) { Debug.LogWarning("[MW] Ara sahne kapanışı: " + e.Message); }
            if (inst == this) inst = null;
            if (!destroying) Destroy(gameObject);
            if (invoke) SafeInvoke(done);
        }

        void OnDestroy()
        {
            destroying = true;
            if (def != null) Finish(false);
            if (inst == this) inst = null;
        }

        // ------------------------------------------------------------ çizim
        void OnGUI()
        {
            if (def == null) return;
            GUI.depth = -40;
            var oldM = GUI.matrix; float oldA = UIKit.Alpha;
            UIKit.Begin();
            UIKit.Alpha = 1f;
            float sw = Screen.width, sh = Screen.height;
            float e = 1f - (1f - bars) * (1f - bars);
            float bh = sh * 0.12f * e;
            UIKit.Tex(new Rect(0, 0, sw, bh), UIKit.White, Color.black);
            UIKit.Tex(new Rect(0, sh - bh, sw, bh), UIKit.White, Color.black);

            var s = def.shots[Mathf.Clamp(shot, 0, def.shots.Count - 1)];
            float la = ending ? 1f - black : Mathf.Clamp01(shotT / 0.25f) * (1f - black);
            UIKit.Alpha = la;
            // başlık (ilk çekimde)
            if (shot == 0 && !string.IsNullOrEmpty(def.title))
            {
                UIKit.Alpha = la * Mathf.Clamp01(1.6f - shotT / 2f);
                UIKit.Fill(80, 150, 6, 40, UIKit.Teal);
                UIKit.Text(100, 140, 1200, 60, def.title.ToUpper(), 34, Color.white);
                UIKit.Alpha = la;
            }
            if (!string.IsNullOrEmpty(s.line))
            {
                UIKit.Text(0, 880, UIKit.DW, 40, string.IsNullOrEmpty(s.who) ? "" : s.who.ToUpper(), 26, UIKit.Teal, TextAnchor.MiddleCenter);
                UIKit.Text(260, 918, UIKit.DW - 520, 70, s.line, 32, Color.white, TextAnchor.UpperCenter, false, true, true);
            }
            UIKit.Alpha = 0.55f * (1f - black);
            UIKit.Text(UIKit.DW - 640, 1030, 600, 30, "SPACE/ENTER: sonraki   •   ESC: geç", 16, Color.white, TextAnchor.MiddleRight, false, false);
            if (black > 0f) { UIKit.Alpha = 1f; UIKit.Tex(UIKit.Full(), UIKit.White, new Color(0, 0, 0, black)); }
            UIKit.Alpha = oldA;
            GUI.matrix = oldM;
        }
    }
}
