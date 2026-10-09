using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using MostWanted.Gen;

namespace MostWanted
{
    /// <summary>
    /// Kendi Şehrimiz sokak lambaları (Render ajanı): yol kenarı direk + kol + ışıklı başlık (instanced),
    /// zeminde ucuz toplamalı ışık havuzları (MW/SkyLampPool) ve gece oyuncuya en yakın N lambaya gerçek nokta ışığı
    /// (Düşük 0, Orta 6, Yüksek 12; gölgesiz). Gündüz başlıklar söner.
    /// </summary>
    public class StreetLights : MonoBehaviour
    {
        public static StreetLights I;
        readonly List<Vector3> lampHeads = new List<Vector3>();
        InstancedBatch poles, heads, pools;
        Material headMat;
        readonly List<Light> lights = new List<Light>();
        float timer, night = -1f;
        static readonly Color Sodium = new Color(1f, 0.74f, 0.45f);

        static Mesh Prim(PrimitiveType t)
        {
            var go = GameObject.CreatePrimitive(t);
            var m = go.GetComponent<MeshFilter>().sharedMesh;
            Destroy(go);
            return m;
        }

        public static StreetLights Build(OwnCity city, Transform root)
        {
            var sl = root.gameObject.AddComponent<StreetLights>();
            I = sl;
            try { sl.Place(city); }
            catch (System.Exception e) { Debug.LogWarning("[MW] Sokak lambaları kurulamadı: " + e.Message); }
            return sl;
        }

        void Place(OwnCity city)
        {
            var gen = city.gen;
            var cube = Prim(PrimitiveType.Cube);
            var cyl = Prim(PrimitiveType.Cylinder);
            var quad = Prim(PrimitiveType.Quad);
            var poleMat = U.NewMat(new Color(0.22f, 0.23f, 0.25f), 0.45f, 0.6f);
            poleMat.enableInstancing = true;
            headMat = U.NewMat(new Color(0.9f, 0.88f, 0.8f), 0.5f, 0f);
            headMat.enableInstancing = true;
            U.SetEmission(headMat, Color.black);
            poles = new InstancedBatch { mesh = cyl, mats = new[] { poleMat }, cull = 450f };
            heads = new InstancedBatch { mesh = cube, mats = new[] { headMat }, cull = 900f, shadows = ShadowCastingMode.Off, layer = 0 };
            var pm = Resources.Load<Material>("Render/MW_SkyLampPoolMat");
            if (pm != null && pm.shader != null && pm.shader.isSupported)
            {
                var pmi = new Material(pm) { enableInstancing = true };
                pools = new InstancedBatch { mesh = quad, mats = new[] { pmi }, cull = 600f, shadows = ShadowCastingMode.Off, layer = 0 };
            }

            foreach (var e in gen.edges)
            {
                var smp = city.surf.Trimmed(e);
                if (smp == null || smp.Count < 2) continue;
                float hw = CityGen.Width(e.cls) * 0.5f;
                bool both = e.cls != RoadClass.Street;
                float spacing = e.cls == RoadClass.Highway ? 45f : 34f;
                float off = hw + (e.cls == RoadClass.Highway ? 1.2f : Mathf.Min(1.2f, CityGen.Sidewalk(e.cls) * 0.6f + 0.4f));
                float acc = spacing * 0.5f;
                int k = 0;
                for (int i = 1; i < smp.Count; i++)
                {
                    acc += (smp[i].p - smp[i - 1].p).magnitude;
                    if (acc < spacing) continue;
                    acc = 0f;
                    var s = smp[i];
                    if (s.tunnel) continue;
                    k++;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        if (!both && (side == 1) != (k % 2 == 0)) continue;   // tek taraf: karşılıklı sıra
                        Vector3 r = s.r.normalized * side;
                        Vector3 basePos = s.p + r * off;
                        Vector3 inward = -r;
                        float H = e.cls == RoadClass.Highway ? 10f : 8f;
                        poles.Add(basePos + Vector3.up * (H * 0.5f), Quaternion.identity, new Vector3(0.18f, H * 0.5f, 0.18f));
                        Vector3 armC = basePos + Vector3.up * H + inward * 1.1f;
                        poles.Add(armC, Quaternion.LookRotation(inward) * Quaternion.Euler(90f, 0f, 0f), new Vector3(0.1f, 1.1f, 0.1f));
                        Vector3 head = basePos + Vector3.up * (H - 0.1f) + inward * 2.1f;
                        heads.Add(head, Quaternion.LookRotation(inward), new Vector3(0.45f, 0.14f, 0.8f));
                        lampHeads.Add(head);
                        if (pools != null)
                        {
                            Vector3 g = s.p + r * (off - 2.6f);
                            g.y = s.p.y + 0.06f;
                            float sz = e.cls == RoadClass.Highway ? 18f : 14f;
                            pools.Add(g, Quaternion.Euler(90f, 0f, 0f), new Vector3(sz, sz, 1f));
                        }
                    }
                }
            }
            poles.Bake(); heads.Bake(); if (pools != null) pools.Bake();
            Debug.Log("[MW] Sokak lambaları: " + lampHeads.Count);
        }

        public void SetNight(float n)
        {
            if (Mathf.Abs(n - night) < 0.02f) return;
            night = n;
            if (headMat != null) U.SetEmission(headMat, n > 0.3f ? Sodium * (3.5f * n) : Color.black);
        }

        void Update()
        {
            var g = Game.I;
            if (g == null || g.cam == null) return;
            int q = SaveSystem.Data != null ? Mathf.Clamp(SaveSystem.Data.quality, 0, 2) : 1;
            if (SaveSystem.Data != null && SaveSystem.Data.quality == 3 && g.opt != null) q = 1;
            float mul = q == 0 ? 0.6f : q == 2 ? 1f : 0.8f;
            Vector3 cp = g.cam.transform.position;
            if (poles != null) poles.Draw(cp, mul);
            if (heads != null) heads.Draw(cp, mul);
            if (pools != null && night > 0.05f) pools.Draw(cp, mul);

            timer -= Time.unscaledDeltaTime;
            if (timer > 0f) return;
            timer = 0.4f;
            int want = night > 0.3f ? (q == 0 ? 0 : q == 1 ? 6 : 12) : 0;
            while (lights.Count < want)
            {
                var lg = new GameObject("SokakIsigi");
                lg.transform.SetParent(transform, false);
                var l = lg.AddComponent<Light>();
                l.type = LightType.Point; l.range = 20f; l.color = Sodium; l.shadows = LightShadows.None;
                lights.Add(l);
            }
            // en yakın lambaları seç (kısmi seçim; birkaç bin lamba için yeterince ucuz, 0.4 sn'de bir)
            var best = new List<KeyValuePair<float, int>>(want + 1);
            for (int i = 0; i < lampHeads.Count && want > 0; i++)
            {
                float d = (lampHeads[i] - cp).sqrMagnitude;
                if (best.Count == want && d >= best[best.Count - 1].Key) continue;
                int at = best.Count;
                while (at > 0 && best[at - 1].Key > d) at--;
                best.Insert(at, new KeyValuePair<float, int>(d, i));
                if (best.Count > want) best.RemoveAt(best.Count - 1);
            }
            for (int i = 0; i < lights.Count; i++)
            {
                bool on = i < best.Count;
                lights[i].enabled = on;
                if (!on) continue;
                lights[i].transform.position = lampHeads[best[i].Value] + Vector3.down * 0.4f;
                lights[i].intensity = 2.6f * night;
            }
        }
    }
}
