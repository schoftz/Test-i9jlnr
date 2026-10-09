using System.Collections.Generic;
using UnityEngine;

namespace MostWanted
{
    /// <summary>Yarışlar: sprint, tur, hız kamerası (speedtrap), gişe (tollbooth), drag.</summary>
    public class RaceManager : MonoBehaviour
    {
        public class Entry
        {
            public CarController car;
            public string name;
            public int idx = 1, lap;
            public bool finished;
            public int place;
            public float trapTotal;
            public float finishTime;
            public RacerDriver ai;
        }

        [System.NonSerialized] public RaceDef def;
        [System.NonSerialized] public List<Entry> entries = new List<Entry>();
        public bool circuit;
        public int laps;
        public float countdown, raceTime;
        public float tollTime;
        public float lastTrapSpeed;
        public int rivalIndex = -1;        // kara liste yarışı ise rakip
        public bool Active { get { return def != null; } }
        public bool Counting { get { return Active && countdown > 0f; } }
        public bool IsDrag { get { return def != null && def.type == RaceType.Drag; } }
        Entry playerEntry;
        GameObject gate, gate2;
        readonly List<GameObject> markers = new List<GameObject>();
        int lastBeep;
        float endTimer;
        Vector3 dragDir, dragRight, dragOrigin;

        public Entry PlayerEntry { get { return playerEntry; } }
        public float lapStart;
        float[] cumDist;

        /// <summary>Rota boyunca alınan mesafe (m) — sıralama farkları için.</summary>
        public float DistanceAlong(Entry e)
        {
            if (def == null || e == null || e.car == null) return 0f;
            if (IsDrag) return Vector3.Dot(e.car.transform.position - dragOrigin, dragDir);
            var r = def.route;
            if (cumDist == null || cumDist.Length != r.Count + 1)
            {
                cumDist = new float[r.Count + 1];
                for (int i = 1; i < r.Count; i++) cumDist[i] = cumDist[i - 1] + U.FlatDist(r[i - 1], r[i]);
                cumDist[r.Count] = cumDist[r.Count - 1] + (circuit ? U.FlatDist(r[r.Count - 1], r[0]) : 0f);
            }
            float lapLen = cumDist[r.Count];
            int idx = Mathf.Clamp(e.idx, 0, r.Count - 1);
            float toNext = U.FlatDist(e.car.transform.position, r[idx]);
            float d = e.lap * lapLen + cumDist[idx] - toNext;
            if (e.finished) d += 1e6f - e.place * 1000f;
            return d;
        }

        /// <summary>Sıralama: (giriş, lidere saniye farkı).</summary>
        public List<KeyValuePair<Entry, float>> Standings()
        {
            var l = new List<KeyValuePair<Entry, float>>();
            if (!Active) return l;
            Entry lead = null; float ld = float.MinValue;
            foreach (var e in entries) { float d = def.type == RaceType.Speedtrap ? e.trapTotal : DistanceAlong(e); if (d > ld) { ld = d; lead = e; } }
            foreach (var e in entries)
            {
                float d = def.type == RaceType.Speedtrap ? e.trapTotal : DistanceAlong(e);
                float v = lead != null && lead.car != null ? Mathf.Max(8f, U.Vel(lead.car.rb).magnitude) : 30f;
                l.Add(new KeyValuePair<Entry, float>(e, def.type == RaceType.Speedtrap ? ld - d : (ld - d) / v));
            }
            l.Sort((a, b) => a.Value.CompareTo(b.Value));
            return l;
        }

        // ---------------------------------------------------------------- başlatma
        public void StartRace(RaceDef d, int rival = -1)
        {
            if (Active) Abort();
            var g = Game.I;
            if (d == null || d.route.Count < 2) { g.Toast("Bu yarış bu haritada kullanılamıyor."); return; }
            g.police.EndPursuit(false);
            g.delivery.Cancel();
            def = d;
            rivalIndex = rival;
            circuit = d.type == RaceType.Circuit;
            laps = circuit ? d.laps : 1;
            raceTime = 0f; endTimer = 0f; lapStart = 0f; cumDist = null;
            entries.Clear();

            Vector3 start = d.route[0];
            Vector3 dir = U.Flat(d.route[1] - d.route[0]).normalized;
            Vector3 right = new Vector3(dir.z, 0, -dir.x);
            Quaternion rot = Quaternion.LookRotation(dir);
            dragDir = dir; dragRight = right; dragOrigin = start;

            int opponents = d.type == RaceType.Tollbooth ? 0 : rival >= 0 ? 1 : 3;
            if (d.type == RaceType.Drag) opponents = Mathf.Min(opponents, d.dragLanes.Length - 1);
            var slots = new List<Vector3>();
            if (d.type == RaceType.Drag)
            {
                for (int i = 0; i < d.dragLanes.Length; i++) slots.Add(start + right * d.dragLanes[i] + Vector3.up * 0.5f);
                while (slots.Count < 4) slots.Add(slots[slots.Count - 1] - dir * 10f);
            }
            else
            {
                // rota merkez hattından sola kaydırılmış ızgara (şerit noktaları zaten sağda)
                Vector3 baseP = start - right * 2.2f;
                // ızgara başlangıç noktasının ARKASINA değil, ilk yol parçasının üstüne kurulur (arkası kavşak/kaldırım/çim olabiliyor)
                float segLen = U.FlatDist(d.route[0], d.route[1]);
                float front = Mathf.Max(11f, Mathf.Min(13f, segLen * 0.6f));
                for (int s = 0; s < 4; s++)
                    slots.Add(baseP + dir * (front - (s / 2) * 9f) + right * ((s % 2 == 0) ? -2.6f : 2.6f) + Vector3.up * 0.5f);
            }
            g.traffic.ClearAround(start, 60f);

            int playerSlot = d.type == RaceType.Drag ? Mathf.Min(1, d.dragLanes.Length - 1) : Mathf.Min(opponents, 3);
            g.player.Teleport(slots[playerSlot], rot);
            playerEntry = new Entry { car = g.player, name = "Sen" };
            entries.Add(playerEntry);
            var pd = g.player.GetComponent<PlayerDriver>();
            if (pd != null && d.type == RaceType.Drag) pd.dragLaneX = d.dragLanes[playerSlot];

            string[] names = { "Kobra", "Gölge", "Baron" };
            int si = 0;
            for (int i = 0; i < opponents; i++)
            {
                if (si == playerSlot) si++;
                CarEntry spec;
                string nm;
                float skill;
                if (rival >= 0)
                {
                    var rv = Career.Rivals[rival];
                    spec = g.career.RivalCar(rival);
                    nm = rv.name;
                    skill = rv.skill;
                }
                else
                {
                    int my = Catalog.Garage.IndexOf(Catalog.Get(SaveSystem.Data.selected));
                    int idx = Mathf.Clamp(my + Random.Range(-1, 2), 0, Catalog.Garage.Count - 1);
                    spec = Catalog.Garage[idx];
                    nm = names[i];
                    skill = 0.86f + i * 0.04f;
                }
                var car = CarFactory.Build(spec, rival >= 0 ? Career.RivalPaint(rival) : Catalog.Paints[(i * 5 + 3) % Catalog.Paints.Length], slots[si], rot, CarRole.Racer, SaveSystem.Get(SaveSystem.Data.selected) != null ? SaveSystem.Get(SaveSystem.Data.selected).tune : null, "Yarisci_" + nm);
                EngineAudio.Attach(car, false);
                var rd = car.gameObject.AddComponent<RacerDriver>();
                rd.skill = skill;
                var e = new Entry { car = car, name = nm, ai = rd };
                rd.entry = e;
                if (d.type == RaceType.Drag) { rd.dragLaneX = d.dragLanes[si]; car.manualGearbox = true; }
                entries.Add(e);
                si++;
            }
            foreach (var e in entries) e.car.locked = true;
            countdown = 3.99f;
            lastBeep = 4;
            tollTime = 0f;
            if (d.type == RaceType.Tollbooth) tollTime = SegmentTime(0, d.special.Count > 0 ? d.special[0] : d.route.Count - 1) + 4f;

            if (gate == null) gate = MakeGate(new Color(1f, 0.5f, 0f), "KontrolNoktasi");
            gate.SetActive(true);
            foreach (var m in markers) if (m != null) Destroy(m);
            markers.Clear();
            if (d.type == RaceType.Speedtrap || d.type == RaceType.Tollbooth)
                foreach (int si2 in d.special)
                {
                    var m = MakeGate(d.type == RaceType.Speedtrap ? new Color(0.2f, 0.6f, 1f) : new Color(0.2f, 1f, 0.4f), d.type == RaceType.Speedtrap ? "HizKamerasi" : "Gise");
                    PlaceAt(m, si2);
                    U.Text3D(d.type == RaceType.Speedtrap ? "RADAR" : "GİŞE", m.transform, new Vector3(0, 9.5f, 0), Quaternion.identity, 2.5f, Color.white);
                    markers.Add(m);
                }
            if (d.type == RaceType.Drag)
            {
                var fin = MakeGate(Color.white, "Bitis");
                fin.transform.SetPositionAndRotation(d.route[1], rot);
                fin.transform.localScale = new Vector3(1.2f, 1f, 1f);
                markers.Add(fin);
            }
            PlaceGate();
            g.rig.Snap();
            string tname = d.type == RaceType.Sprint ? "Sprint" : d.type == RaceType.Circuit ? "Tur" : d.type == RaceType.Speedtrap ? "Hız Kamerası" : d.type == RaceType.Tollbooth ? "Gişe" : "Drag";
            g.Toast(d.name + " (" + tname + ") başlıyor!" + (d.type == RaceType.Drag ? "  Vites: E yukarı, Q aşağı, A/D şerit" : ""));
        }

        float SegmentTime(int from, int to)
        {
            float len = 0f;
            for (int i = from; i < to && i + 1 < def.route.Count; i++) len += Vector3.Distance(def.route[i], def.route[i + 1]);
            return len / 32f;
        }

        GameObject MakeGate(Color c, string name)
        {
            var root = new GameObject(name);
            var m = U.Emissive(c * 0.6f, c * 1.8f);
            float half = 10f;
            U.Prim(PrimitiveType.Cube, "SolDirek", root.transform, new Vector3(-half, 4f, 0), new Vector3(0.6f, 8f, 0.6f), m);
            U.Prim(PrimitiveType.Cube, "SagDirek", root.transform, new Vector3(half, 4f, 0), new Vector3(0.6f, 8f, 0.6f), m);
            U.Prim(PrimitiveType.Cube, "Ust", root.transform, new Vector3(0, 8f, 0), new Vector3(half * 2 + 0.6f, 0.6f, 0.6f), m);
            U.Icon(root.transform, c, 8f);
            return root;
        }

        void PlaceAt(GameObject go, int i)
        {
            var r = def.route;
            i = Mathf.Clamp(i, 0, r.Count - 1);
            Vector3 p = r[i];
            Vector3 prev = i > 0 ? r[i - 1] : (circuit ? r[r.Count - 1] : r[0] - (r[1] - r[0]));
            Vector3 d = U.Flat(p - prev);
            if (d.sqrMagnitude < 0.01f) d = Vector3.forward;
            go.transform.SetPositionAndRotation(p - new Vector3(d.normalized.z, 0, -d.normalized.x) * 2.2f, Quaternion.LookRotation(d.normalized));
        }

        void PlaceGate()
        {
            if (gate == null || playerEntry == null || IsDrag) { if (gate != null && IsDrag) gate.SetActive(false); if (gate2 != null) gate2.SetActive(false); return; }
            PlaceAt(gate, playerEntry.idx % def.route.Count);
            // bir sonraki kapı soluk önizleme: yolun nereye gittiği önceden görünsün
            if (gate2 == null) gate2 = MakeGate(new Color(1f, 0.85f, 0.4f) * 0.45f, "SonrakiKontrol");
            int nx = playerEntry.idx + 1;
            bool has = circuit || nx < def.route.Count;
            gate2.SetActive(has);
            if (has) { PlaceAt(gate2, nx % def.route.Count); gate2.transform.localScale = new Vector3(0.8f, 0.8f, 0.8f); }
        }

        public void Abort()
        {
            if (!Active) return;
            foreach (var e in entries) if (e.car != null && e.car != Game.I.player) Destroy(e.car.gameObject);
            if (Game.I.player != null) { Game.I.player.locked = false; Game.I.player.manualGearbox = false; }
            entries.Clear();
            playerEntry = null;
            def = null;
            rivalIndex = -1;
            if (gate != null) gate.SetActive(false);
            if (gate2 != null) gate2.SetActive(false);
            foreach (var m in markers) if (m != null) Destroy(m);
            markers.Clear();
        }

        // ---------------------------------------------------------------- ilerleme
        public float Progress(Entry e)
        {
            if (e.finished) return 100000f - e.place;
            if (IsDrag) return Vector3.Dot(e.car.transform.position - dragOrigin, dragDir) / 1000f;
            var r = def.route;
            Vector3 t = r[e.idx % r.Count];
            return e.lap * r.Count + e.idx - U.FlatDist(e.car.transform.position, t) / 1000f;
        }

        public float PlayerProgress() { return playerEntry != null ? Progress(playerEntry) : 0f; }

        public int PlayerPosition()
        {
            if (playerEntry == null) return 0;
            if (playerEntry.finished) return playerEntry.place;
            if (def.type == RaceType.Speedtrap)
            {
                int p = 1;
                foreach (var e in entries) if (e != playerEntry && e.trapTotal > playerEntry.trapTotal) p++;
                return p;
            }
            float pp = Progress(playerEntry);
            int pos = 1;
            foreach (var e in entries) if (e != playerEntry && Progress(e) > pp) pos++;
            return pos;
        }

        public int PlayerLap { get { return playerEntry != null ? Mathf.Min(playerEntry.lap + 1, laps) : 0; } }
        /// <summary>Sıradaki dönüş bilgisi (HUD): dir -1 sol / +1 sağ / 0 düz veya bitiş; dist oyuncudan dönüş noktasına metre; finish = son kapı.</summary>
        public bool NextTurn(out int dir, out float dist, out bool finish)
        {
            dir = 0; dist = 0f; finish = false;
            if (playerEntry == null || def == null || IsDrag || playerEntry.car == null) return false;
            var r = def.route; int n = r.Count;
            Vector3 from = playerEntry.car.transform.position;
            int i = playerEntry.idx % n;
            for (int k = 0; k < 4; k++)
            {
                dist += U.FlatDist(from, r[i]);
                bool last = !circuit && i >= n - 1;
                if (last) { finish = true; return true; }
                Vector3 a = U.Flat(r[i] - (i > 0 ? r[i - 1] : circuit ? r[n - 1] : from));
                Vector3 b = U.Flat(r[(i + 1) % n] - r[i]);
                float ang = Vector3.SignedAngle(a, b, Vector3.up);
                if (Mathf.Abs(ang) > 25f) { dir = ang > 0f ? 1 : -1; return true; }
                from = r[i]; i = (i + 1) % n;
            }
            return true;
        }

        public Vector3 NextCheckpoint { get { return playerEntry != null ? (IsDrag ? def.route[1] : def.route[playerEntry.idx % def.route.Count]) : Vector3.zero; } }

        // ---------------------------------------------------------------- drag yardımcıları
        public float DragLane(float current, int dirSign)
        {
            var lanes = def.dragLanes;
            int best = 0; float bd = float.MaxValue;
            for (int i = 0; i < lanes.Length; i++) { float d = Mathf.Abs(lanes[i] - current); if (d < bd) { bd = d; best = i; } }
            best = Mathf.Clamp(best + dirSign, 0, lanes.Length - 1);
            return lanes[best];
        }

        public float DragSteer(CarController car, float laneX)
        {
            Vector3 rel = car.transform.position - dragOrigin;
            float lat = Vector3.Dot(rel, dragRight);
            float latV = Vector3.Dot(U.Vel(car.rb), dragRight);
            float heading = Vector3.SignedAngle(dragDir, U.Flat(car.transform.forward), Vector3.up);
            return Mathf.Clamp((laneX - lat) * 0.18f - latV * 0.12f - heading * 0.06f, -1f, 1f);
        }

        // ---------------------------------------------------------------- güncelleme
        void Update()
        {
            if (!Active) return;
            float dt = Time.deltaTime;
            if (countdown > 0f)
            {
                countdown -= dt;
                int c = Mathf.CeilToInt(countdown);
                if (c != lastBeep && c > 0) { lastBeep = c; Game.I.Beep(false); }
                if (countdown <= 0f)
                {
                    foreach (var e in entries) e.car.locked = false;
                    Game.I.Beep(true);
                    // mükemmel kalkış: devir %55-80 arası
                    var p = playerEntry.car;
                    float x = p.rpm / p.redline;
                    if (x > 0.55f && x < 0.8f) { p.SetSpeed(18f); p.nitro = Mathf.Min(1f, p.nitro + 0.15f); Game.I.Toast("Mükemmel kalkış!"); }
                    else if (x >= 0.8f) Game.I.Toast("Patinaj! Kalkışta devri düşük tut.");
                }
                return;
            }
            raceTime += dt;

            if (def.type == RaceType.Tollbooth)
            {
                tollTime -= dt;
                if (tollTime <= 0f) { Game.I.Toast("Süre doldu! Gişe yarışı başarısız."); Abort(); return; }
            }

            int finishedCount = 0;
            foreach (var e in entries) if (e.finished) finishedCount++;

            foreach (var e in entries)
            {
                if (e.finished || e.car == null) continue;
                if (IsDrag)
                {
                    float along = Vector3.Dot(e.car.transform.position - dragOrigin, dragDir);
                    float total = Vector3.Dot(def.route[1] - dragOrigin, dragDir);
                    if (along >= total) Finish(e, ++finishedCount);
                    continue;
                }
                var r = def.route;
                int i = e.idx % r.Count;
                Vector3 t = r[i];
                Vector3 nd = U.Flat(r[(i + 1) % r.Count] - t);
                Vector3 rel = U.Flat(e.car.transform.position - t);
                bool passed = rel.magnitude < 14f || (rel.magnitude < 40f && nd.sqrMagnitude > 0.1f && Vector3.Dot(rel, nd.normalized) > 0f);
                if (!passed) continue;
                // özel noktalar
                if (def.special.Contains(i))
                {
                    if (def.type == RaceType.Speedtrap)
                    {
                        float sp = e.car.SpeedKmh;
                        e.trapTotal += sp;
                        if (e == playerEntry) { lastTrapSpeed = sp; Game.I.Toast("RADAR: " + Mathf.RoundToInt(sp) + " km/sa"); Game.I.Beep(true); }
                    }
                    else if (def.type == RaceType.Tollbooth && e == playerEntry)
                    {
                        int k = def.special.IndexOf(i);
                        int nextIdx = k + 1 < def.special.Count ? def.special[k + 1] : r.Count - 1;
                        float add = SegmentTime(i, nextIdx) + 2f;
                        tollTime += add;
                        Game.I.Toast("GİŞE! +" + add.ToString("0.0") + " sn");
                        Game.I.Beep(true);
                    }
                }
                e.idx++;
                if (circuit)
                {
                    if (e.idx >= r.Count) { e.idx = 0; }
                    if (e.idx == 1 && i == 0)
                    {
                        e.lap++;
                        if (e.lap >= laps) Finish(e, ++finishedCount);
                        else if (e == playerEntry) { Game.I.Toast("Tur " + (e.lap + 1) + "/" + laps + "  —  " + FormatTime(raceTime - lapStart)); lapStart = raceTime; }
                    }
                }
                else if (e.idx >= r.Count) Finish(e, ++finishedCount);
                if (e == playerEntry) PlaceGate();
            }

            if (playerEntry != null && playerEntry.finished)
            {
                endTimer += dt;
                if (endTimer > 4f) Abort();
            }
        }

        public static string FormatTime(float t)
        {
            t = Mathf.Max(0f, t);
            int m = Mathf.FloorToInt(t / 60f), s = Mathf.FloorToInt(t % 60f), cs = Mathf.FloorToInt((t * 100f) % 100f);
            return m.ToString("00") + ":" + s.ToString("00") + ":" + cs.ToString("00");
        }

        void Finish(Entry e, int place)
        {
            e.finished = true;
            e.place = place;
            e.finishTime = raceTime;
            if (e == playerEntry) PlayerFinished();
        }

        void PlayerFinished()
        {
            int place = playerEntry.place;
            if (def.type == RaceType.Speedtrap)
            {
                // diğer yarışçıların kalan radarlarını mevcut hızla tahmin et
                foreach (var e in entries)
                {
                    if (e == playerEntry || e.finished) continue;
                    int remaining = 0;
                    foreach (int s in def.special) if (s >= e.idx) remaining++;
                    e.trapTotal += remaining * Mathf.Max(80f, e.car.SpeedKmh);
                }
                place = 1;
                foreach (var e in entries) if (e != playerEntry && e.trapTotal > playerEntry.trapTotal) place++;
                playerEntry.place = place;
            }
            if (def.type == RaceType.Tollbooth) place = 1;
            int prize = place == 1 ? def.prize : place == 2 ? def.prize * 2 / 5 : place == 3 ? def.prize / 7 : 0;
            var g = Game.I;
            if (StoryManager.OnRaceResult(def, rivalIndex, place)) { }   // hikaye (prolog) sonucu kendisi işledi
            else if (rivalIndex >= 0)
            {
                if (place == 1) g.career.RivalBeaten(rivalIndex);
                else g.Toast("Kara liste rakibini yenemedin. Tekrar dene!");
            }
            else
            {
                if (place == 1) SaveSystem.Data.racesWon++;
                SaveSystem.AddMoney(prize);
                g.Toast(place + ". oldun! " + (prize > 0 ? "Ödül: " + U.Money(prize) : "Ödül yok.") + (def.type == RaceType.Speedtrap ? "  Toplam: " + Mathf.RoundToInt(playerEntry.trapTotal) + " km/sa" : ""));
            }
            SaveSystem.Save();
            if (gate != null) gate.SetActive(false);
            if (gate2 != null) gate2.SetActive(false);
            playerEntry.car.locked = false;
            playerEntry.car.manualGearbox = false;
        }
    }

    /// <summary>Zamanlı teslimat görevleri.</summary>
    public class DeliveryManager : MonoBehaviour
    {
        public bool Active { get; private set; }
        public float timeLeft;
        public int reward;
        public Vector3 target;
        GameObject marker;

        public void StartJob()
        {
            var g = Game.I;
            if (g.race.Active) g.race.Abort();
            var graph = g.world.graph;
            Vector3 pp = g.player.transform.position;
            int n = graph.RandomNodeAround(pp, 350f, 800f);
            target = graph.nodes[n];
            float dist = U.FlatDist(pp, target);
            timeLeft = 25f + dist / 16f;
            reward = 600 + Mathf.RoundToInt(dist * 3f / 50f) * 50;
            Active = true;
            if (marker == null)
            {
                marker = new GameObject("TeslimatNoktasi");
                var m = U.Emissive(new Color(0f, 0.6f, 1f), new Color(0f, 1f, 2f));
                var beam = U.Prim(PrimitiveType.Cylinder, "Isin", marker.transform, new Vector3(0, 30, 0), new Vector3(3, 30, 3), m);
                beam.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                U.Icon(marker.transform, new Color(0f, 0.7f, 1f), 10f);
            }
            marker.transform.position = target;
            marker.SetActive(true);
            g.Toast("Teslimat: paketi " + Mathf.RoundToInt(timeLeft) + " sn içinde mavi noktaya götür!");
        }

        public void Cancel()
        {
            Active = false;
            if (marker != null) marker.SetActive(false);
        }

        void Update()
        {
            if (!Active || Game.I.player == null) return;
            timeLeft -= Time.deltaTime;
            if (U.FlatDist(Game.I.player.transform.position, target) < 10f)
            {
                SaveSystem.AddMoney(reward);
                Game.I.Toast("Teslimat tamam! +" + U.Money(reward));
                Game.I.Beep(true);
                Cancel();
            }
            else if (timeLeft <= 0f) { Game.I.Toast("Teslimat süresi doldu!"); Cancel(); }
        }
    }
}
