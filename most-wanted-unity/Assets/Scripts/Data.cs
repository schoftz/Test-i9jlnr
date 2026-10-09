using System;
using System.Collections.Generic;
using UnityEngine;

namespace MostWanted
{
    public class CarSpec
    {
        public string name;
        public int price;
        public float torque;      // Nm (toplam)
        public float maxSpeed;    // km/sa
        public float mass;        // kg
        public float grip;        // yol tutuş çarpanı
        public float nitroCap;    // nitro kapasite çarpanı
        public float width, length, height;
        public bool spoiler;
        public Color color;

        public CarSpec(string n, int p, float tq, float vmax, float m, float g, float nc, float w, float l, float h, bool sp, Color c)
        {
            name = n; price = p; torque = tq; maxSpeed = vmax; mass = m; grip = g; nitroCap = nc;
            width = w; length = l; height = h; spoiler = sp; color = c;
        }
    }

    public static class Catalog
    {
        public static readonly CarSpec[] Cars =
        {
            new CarSpec("Sokak Kurdu",      0,   2600, 190, 1250, 1.00f, 1.0f, 1.85f, 4.2f, 0.55f, false, new Color(0.85f,0.85f,0.85f)),
            new CarSpec("Gece Avcısı",  18000,   3000, 210, 1300, 1.05f, 1.1f, 1.90f, 4.3f, 0.52f, false, new Color(0.15f,0.25f,0.8f)),
            new CarSpec("Fırtına R",    32000,   3400, 228, 1320, 1.10f, 1.2f, 1.92f, 4.4f, 0.50f, true,  new Color(0.95f,0.75f,0.1f)),
            new CarSpec("Yıldırım X",   50000,   3900, 245, 1350, 1.12f, 1.3f, 1.95f, 4.45f,0.48f, true,  new Color(0.1f,0.7f,0.3f)),
            new CarSpec("Kara Şimşek",  75000,   4400, 262, 1380, 1.16f, 1.4f, 1.98f, 4.5f, 0.46f, true,  new Color(0.08f,0.08f,0.08f)),
            new CarSpec("Efsane V12",  110000,   5000, 280, 1420, 1.18f, 1.5f, 2.00f, 4.6f, 0.45f, false, new Color(0.8f,0.05f,0.05f)),
            new CarSpec("Hayalet GTR", 160000,   5600, 300, 1400, 1.24f, 1.7f, 2.00f, 4.55f,0.44f, true,  new Color(0.9f,0.9f,1.0f)),
        };

        public static readonly CarSpec Police = new CarSpec("Polis", 0, 4200, 255, 1450, 1.15f, 1f, 1.95f, 4.6f, 0.55f, false, Color.black);
        public static readonly CarSpec Traffic = new CarSpec("Sivil", 0, 1800, 120, 1300, 1.0f, 1f, 1.85f, 4.3f, 0.65f, false, Color.gray);

        public static readonly Color[] Paints =
        {
            new Color(0.85f,0.85f,0.85f), new Color(0.08f,0.08f,0.08f), new Color(0.8f,0.05f,0.05f), new Color(0.15f,0.25f,0.8f),
            new Color(0.95f,0.75f,0.1f), new Color(0.1f,0.7f,0.3f), new Color(1f,0.4f,0f), new Color(0.55f,0.1f,0.7f),
        };
        public static readonly string[] PaintNames = { "Beyaz", "Siyah", "Kırmızı", "Mavi", "Sarı", "Yeşil", "Turuncu", "Mor" };
        public const int PaintCost = 250;
        public const int MaxTune = 3;

        public static int TuneCost(int carId, int level)
        {
            return 1500 + (int)(Cars[carId].price * 0.08f * (level + 1)) + level * 1500;
        }
    }

    [Serializable]
    public class CarSave
    {
        public int id;
        public bool owned;
        public int color = -1; // -1 = fabrika rengi
        public int engine;
        public int nitro;
        public int handling;
    }

    [Serializable]
    public class SaveData
    {
        public int money = 8000;
        public int selected = 0;
        public int racesWon = 0;
        public int escapes = 0;
        public int busted = 0;
        public List<CarSave> cars = new List<CarSave>();
    }

    public static class SaveSystem
    {
        const string Key = "MW_SAVE_V1";
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
            for (int i = 0; i < Catalog.Cars.Length; i++)
            {
                if (Get(i) == null) Data.cars.Add(new CarSave { id = i, owned = (i == 0) });
            }
            if (Data.selected < 0 || Data.selected >= Catalog.Cars.Length || !Get(Data.selected).owned) Data.selected = 0;
            Get(0).owned = Get(0).owned || OwnedCount() == 0;
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

        public static CarSave Get(int id)
        {
            foreach (var c in Data.cars) if (c.id == id) return c;
            return null;
        }

        public static int OwnedCount()
        {
            int n = 0;
            foreach (var c in Data.cars) if (c.owned) n++;
            return n;
        }

        public static void AddMoney(int v)
        {
            Data.money = Mathf.Max(0, Data.money + v);
            Save();
        }
    }
}
