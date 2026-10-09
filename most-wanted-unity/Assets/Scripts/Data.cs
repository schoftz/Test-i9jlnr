using System;
using System.Collections.Generic;
using UnityEngine;

namespace MostWanted
{
    public enum Tune { Motor, Turbo, Sanziman, Suspansiyon, Lastik, Nitro, Fren }

    public static class Catalog
    {
        public static readonly List<CarEntry> All = new List<CarEntry>();
        public static readonly List<CarEntry> Garage = new List<CarEntry>();
        public static readonly List<CarEntry> Traffic = new List<CarEntry>();
        public static CarEntry PolicePatrol, PoliceUndercover, PoliceSuv;

        public static readonly string[] TuneNames = { "Motor", "Turbo", "Şanzıman", "Süspansiyon", "Lastikler", "Nitro", "Frenler" };
        public const int TuneCount = 7;
        public const int MaxTune = 3;
        public const int PaintCost = 500;

        public static readonly Color[] Paints =
        {
            new Color(0.9f,0.9f,0.9f), new Color(0.05f,0.05f,0.06f), new Color(0.75f,0.04f,0.04f), new Color(0.08f,0.2f,0.75f),
            new Color(0.95f,0.7f,0.05f), new Color(0.1f,0.6f,0.2f), new Color(1f,0.4f,0f), new Color(0.5f,0.1f,0.65f),
            new Color(0.55f,0.57f,0.6f), new Color(0.1f,0.75f,0.85f),
        };
        public static readonly string[] PaintNames = { "Beyaz", "Siyah", "Kırmızı", "Mavi", "Sarı", "Yeşil", "Turuncu", "Mor", "Gümüş", "Turkuaz" };

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
            All.Add(new CarEntry { id = "p_hatch", displayName = "Sokak Hatch", price = 0, massKg = 1250, torqueNm = 280, redlineRpm = 6800, topSpeedKmh = 215, drive = 0, grip = 1f, length = 4.2f, defaultColor = Paints[0] });
            All.Add(new CarEntry { id = "p_tuner", displayName = "Tuner S", price = 25000, massKg = 1350, torqueNm = 380, redlineRpm = 7800, topSpeedKmh = 245, drive = 2, grip = 1.05f, length = 4.4f, defaultColor = Paints[3] });
            All.Add(new CarEntry { id = "p_muscle", displayName = "Muscle V8", price = 45000, massKg = 1600, torqueNm = 560, redlineRpm = 6500, topSpeedKmh = 260, drive = 1, grip = 0.97f, length = 4.8f, defaultColor = Paints[1], policeRole = "patrol" });
            All.Add(new CarEntry { id = "p_gt", displayName = "Street GT", price = 80000, massKg = 1450, torqueNm = 520, redlineRpm = 8200, topSpeedKmh = 290, drive = 1, grip = 1.1f, length = 4.5f, defaultColor = Paints[4], policeRole = "undercover" });
            All.Add(new CarEntry { id = "p_super", displayName = "Süper V10", price = 140000, massKg = 1550, torqueNm = 620, redlineRpm = 8700, topSpeedKmh = 320, drive = 2, grip = 1.18f, length = 4.5f, defaultColor = Paints[2] });
            All.Add(new CarEntry { id = "p_suv", displayName = "Bulldog SUV", price = 38000, massKg = 2100, torqueNm = 620, redlineRpm = 6000, topSpeedKmh = 230, drive = 2, grip = 0.95f, length = 4.9f, defaultColor = Paints[8], policeRole = "suv" });
        }

        public static CarEntry Get(string id)
        {
            foreach (var e in All) if (e.id == id) return e;
            return null;
        }

        public static int TuneCost(CarEntry e, int level)
        {
            return 1000 + (int)(e.price * 0.05f * (level + 1)) + level * 1500;
        }
    }

    [Serializable]
    public class CarSave
    {
        public string id;
        public bool owned;
        public int color = -1;
        public int[] tune = new int[Catalog.TuneCount];
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
