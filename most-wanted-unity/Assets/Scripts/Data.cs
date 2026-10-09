using System;
using System.Collections.Generic;
using UnityEngine;

namespace MostWanted
{
    public enum Tune { Motor, Turbo, Sanziman, Suspansiyon, Lastik, Nitro, Fren, Egzoz }

    public static class Catalog
    {
        public static readonly List<CarEntry> All = new List<CarEntry>();
        public static readonly List<CarEntry> Garage = new List<CarEntry>();
        public static readonly List<CarEntry> Traffic = new List<CarEntry>();
        public static CarEntry PolicePatrol, PoliceUndercover, PoliceSuv;

        public static readonly string[] TuneNames = { "Motor", "Turbo", "Şanzıman", "Süspansiyon", "Lastikler", "Nitro", "Frenler", "Egzoz" };
        public const int TuneCount = 8;
        public const int MaxTune = 3;
        public const int PaintCost = 500;

        /// <summary>Boya kataloğu: renk + metaliklik + parlaklık (mat, metalik, inci, şeker...).</summary>
        public static readonly PaintDef[] Paints =
        {
            new PaintDef("İnci Beyazı",     new Color(0.93f, 0.92f, 0.88f), 0.35f, 0.88f),
            new PaintDef("Parlak Siyah",    new Color(0.02f, 0.02f, 0.025f), 0.4f, 0.9f),
            new PaintDef("Mat Siyah",       new Color(0.04f, 0.04f, 0.045f), 0.0f, 0.18f),
            new PaintDef("Şeker Kırmızı",   new Color(0.62f, 0.02f, 0.03f), 0.75f, 0.9f),
            new PaintDef("Yarış Kırmızısı", new Color(0.8f, 0.06f, 0.04f), 0.3f, 0.8f),
            new PaintDef("MW Gümüş",        new Color(0.72f, 0.74f, 0.77f), 0.85f, 0.78f),
            new PaintDef("MW Mavi",         new Color(0.06f, 0.2f, 0.62f), 0.7f, 0.82f),
            new PaintDef("Gece Mavisi",     new Color(0.02f, 0.05f, 0.18f), 0.75f, 0.85f),
            new PaintDef("Turkuaz",         new Color(0.05f, 0.62f, 0.7f), 0.6f, 0.8f),
            new PaintDef("Limon Sarısı",    new Color(0.95f, 0.78f, 0.04f), 0.4f, 0.82f),
            new PaintDef("Turuncu",         new Color(0.98f, 0.36f, 0.02f), 0.5f, 0.82f),
            new PaintDef("Zehir Yeşili",    new Color(0.3f, 0.85f, 0.08f), 0.5f, 0.8f),
            new PaintDef("Askeri Yeşil",    new Color(0.18f, 0.24f, 0.12f), 0.1f, 0.35f),
            new PaintDef("Mor İnci",        new Color(0.36f, 0.06f, 0.5f), 0.8f, 0.86f),
            new PaintDef("Pembe",           new Color(0.95f, 0.3f, 0.6f), 0.45f, 0.82f),
            new PaintDef("Bronz",           new Color(0.45f, 0.27f, 0.12f), 0.9f, 0.7f),
            new PaintDef("Altın",           new Color(0.85f, 0.65f, 0.25f), 0.95f, 0.8f),
            new PaintDef("Mat Gri",         new Color(0.3f, 0.31f, 0.33f), 0.05f, 0.25f),
        };

        public static void Load()
        {
            All.Clear(); Garage.Clear(); Traffic.Clear();
            var reg = Resources.Load<CarRegistry>("CarRegistry");
            if (reg != null)
                foreach (var e in reg.cars)
                    if (e != null && !string.IsNullOrEmpty(e.id)) All.Add(e);
            if (All.Count == 0) AddFallback();

            foreach (var e in All)
            {
                if (e.inGarage) Garage.Add(e);
                if (e.inTraffic) Traffic.Add(e);
                if (e.policeRole == "patrol" && PolicePatrol == null) PolicePatrol = e;
                if (e.policeRole == "undercover" && PoliceUndercover == null) PoliceUndercover = e;
                if (e.policeRole == "suv" && PoliceSuv == null) PoliceSuv = e;
            }
            if (Garage.Count == 0) Garage.AddRange(All);
            if (Traffic.Count == 0) Traffic.AddRange(All);
            Garage.Sort((a, b) => a.price.CompareTo(b.price));
            if (PolicePatrol == null) PolicePatrol = Garage[Garage.Count / 2];
            if (PoliceUndercover == null) PoliceUndercover = PolicePatrol;
            if (PoliceSuv == null) PoliceSuv = PolicePatrol;
        }

        static void AddFallback()
        {
            // Araç paketi yoksa prosedürel gövdeli yedek araçlar
            All.Add(new CarEntry { id = "p_hatch", displayName = "Sokak Hatch", price = 0, massKg = 1250, torqueNm = 280, redlineRpm = 6800, topSpeedKmh = 215, drive = 0, grip = 1f, length = 4.2f, defaultColor = Paints[0].color });
            All.Add(new CarEntry { id = "p_tuner", displayName = "Tuner S", price = 25000, massKg = 1350, torqueNm = 380, redlineRpm = 7800, topSpeedKmh = 245, drive = 2, grip = 1.05f, length = 4.4f, defaultColor = Paints[3].color });
            All.Add(new CarEntry { id = "p_muscle", displayName = "Muscle V8", price = 45000, massKg = 1600, torqueNm = 560, redlineRpm = 6500, topSpeedKmh = 260, drive = 1, grip = 0.97f, length = 4.8f, defaultColor = Paints[1].color, policeRole = "patrol" });
            All.Add(new CarEntry { id = "p_gt", displayName = "Street GT", price = 80000, massKg = 1450, torqueNm = 520, redlineRpm = 8200, topSpeedKmh = 290, drive = 1, grip = 1.1f, length = 4.5f, defaultColor = Paints[4].color, policeRole = "undercover" });
            All.Add(new CarEntry { id = "p_super", displayName = "Süper V10", price = 140000, massKg = 1550, torqueNm = 620, redlineRpm = 8700, topSpeedKmh = 320, drive = 2, grip = 1.18f, length = 4.5f, defaultColor = Paints[2].color });
            All.Add(new CarEntry { id = "p_suv", displayName = "Bulldog SUV", price = 38000, massKg = 2100, torqueNm = 620, redlineRpm = 6000, topSpeedKmh = 230, drive = 2, grip = 0.95f, length = 4.9f, defaultColor = Paints[8].color, policeRole = "suv" });
        }

        public static CarEntry Get(string id)
        {
            foreach (var e in All) if (e.id == id) return e;
            return null;
        }

        public static int TuneCost(CarEntry e, int level)
        {
            // araç kademesine göre: ucuz araçta ucuz, pahalıda pahalı (en az 10.000 fiyat varsayımı)
            return 500 + (int)(Mathf.Max(e.price, 10000) * 0.05f * (level + 1));
        }
    }

    /// <summary>Boya tanımı.</summary>
    public struct PaintDef
    {
        public string name; public Color color; public float metallic, smoothness;
        public PaintDef(string n, Color c, float m, float s) { name = n; color = c; metallic = m; smoothness = s; }
    }

    [Serializable]
    public class CarSave
    {
        public string id;
        public bool owned;
        public int color = -1;
        public int[] tune = new int[Catalog.TuneCount];
        public string engineSound = "";
        public CustomSave custom = new CustomSave();   // F&F özelleştirme (sadece drift araçları)   // garajdan seçilen motor sesi (boş = aracın kendi sesi)
    }

    [Serializable]
    public class SaveData
    {
        public int money = 12000;
        public int careerBounty = 0;
        public string selected = "";
        public int racesWon = 0;
        public int rivalsBeaten = 0;      // kara liste: yenilen rakip sayısı (#5'ten başlar)
        public int escapes = 0, busted = 0;
        public int copsDisabled = 0, roadblocksEvaded = 0, breakersUsed = 0, heliEscapes = 0, spikesHit = 0;
        public float longestPursuit = 0f;
        public int bestBounty = 0;
        public List<string> milestones = new List<string>();
        public List<CarSave> cars = new List<CarSave>();
        public int quality = 1;      // 0 Düşük, 1 Orta, 2 Yüksek, 3 Otomatik
        public int fpsTarget = 1;    // 30/60/120/sınırsız
        public bool alwaysDay = true;
        public int atmosphere = 0;   // 0 Most Wanted, 1 Normal, 2 Gün batımı, 3 Gece
        public bool wet = false;
        public bool dressing = true;
        public bool gpuResidentDrawer = false;
        public float steerSens = 1.2f;
        public int policeDiff = 0;
        public int driveStyle = 0;                // 0 Arcade (varsayılan), 1 Gerçekçi (RVP)                // 0 Kolay, 1 Normal, 2 Zor
        public bool speedLines = true;           // direksiyon hassasiyeti 0.6–1.6  // Unity 6 GPU Resident Drawer (Keep All ayarı gerekir)
        public float[] volumes = { 1f, 1f, 0.6f, 0.8f }; // Motor, Efekt, Müzik, Siren
    }

    public static class SaveSystem
    {
        const string Key = "MW_SAVE_V2";
        public static SaveData Data;

        public static void Load()
        {
            Data = null;
            string json = PlayerPrefs.GetString(Key, "");
            if (!string.IsNullOrEmpty(json))
            {
                try { Data = JsonUtility.FromJson<SaveData>(json); } catch (Exception) { Data = null; }
            }
            if (Data == null) Data = new SaveData();
            if (Data.cars == null) Data.cars = new List<CarSave>();
            if (Data.milestones == null) Data.milestones = new List<string>();
            foreach (var e in Catalog.Garage)
            {
                var cs = Get(e.id);
                if (cs == null) Data.cars.Add(cs = new CarSave { id = e.id });
                if (cs.tune == null || cs.tune.Length != Catalog.TuneCount)
                {
                    var t = new int[Catalog.TuneCount];
                    if (cs.tune != null) for (int i = 0; i < Mathf.Min(t.Length, cs.tune.Length); i++) t[i] = cs.tune[i];
                    cs.tune = t;
                }
            }
            if (OwnedCount() == 0) Get(Catalog.Garage[0].id).owned = true;
            var sel = Get(Data.selected);
            if (sel == null || !sel.owned || Catalog.Get(Data.selected) == null)
            {
                foreach (var c in Data.cars) if (c.owned && Catalog.Get(c.id) != null) { Data.selected = c.id; break; }
            }
        }

        public static void Save()
        {
            if (Data == null) return;
            PlayerPrefs.SetString(Key, JsonUtility.ToJson(Data));
            PlayerPrefs.Save();
        }

        public static void Reset()
        {
            PlayerPrefs.DeleteKey(Key);
            Load();
            Save();
        }

        public static CarSave Get(string id)
        {
            if (Data == null || id == null) return null;
            foreach (var c in Data.cars) if (c.id == id) return c;
            return null;
        }

        public static int OwnedCount()
        {
            int n = 0;
            foreach (var c in Data.cars) if (c.owned && Catalog.Get(c.id) != null) n++;
            return n;
        }

        public static void AddMoney(int v)
        {
            Data.money = Mathf.Max(0, Data.money + v);
            Save();
        }

        public static bool HasMilestone(string id) { return Data.milestones.Contains(id); }
    }
}
