using System;
using System.Collections.Generic;
using UnityEngine;

namespace MostWanted
{
    /// <summary>Garajdaki/trafikteki bir araç tanımı. Değerler CarRegistry varlığında Inspector'dan düzenlenebilir.</summary>
    [Serializable]
    public class CarEntry
    {
        public string id = "araba";
        public string displayName = "Yeni Araba";
        public GameObject prefab;                 // boşsa prosedürel gövde
        public int price = 30000;
        [Header("Performans")]
        public float massKg = 1400f;
        public float torqueNm = 420f;
        public float redlineRpm = 7200f;
        public float topSpeedKmh = 250f;
        [Tooltip("0 = Önden, 1 = Arkadan, 2 = 4x4")]
        public int drive = 1;
        [Range(0.7f, 1.5f)] public float grip = 1f;
        public float downforce = 1f;
        [Header("Ses")]
        [Tooltip("I4, I6, F6, V8, V10 (boşsa tork/devirden tahmin edilir)")]
        public string engineType = "";
        public bool turbo = false;
        [Tooltip("1 = küçük (hızlı dolar), 2 = büyük (gecikmeli, 'stututu')")]
        public int turboSize = 1;
        public bool supercharger = false;
        [Header("Görünüm")]
        public float length = 4.5f;
        public Color defaultColor = new Color(0.8f, 0.1f, 0.1f);
        [Header("Kullanım")]
        public bool inGarage = true;
        public bool inTraffic = true;
        [Tooltip("boş, patrol, undercover, suv")]
        public string policeRole = "";
        public string credit = "";
    }

    /// <summary>Editör menüsü "Most Wanted/Arabaları Tara" tarafından doldurulan araç listesi (Assets/Resources/CarRegistry.asset).</summary>
    [CreateAssetMenu(fileName = "CarRegistry", menuName = "Most Wanted/Araç Kaydı")]
    public class CarRegistry : ScriptableObject
    {
        public List<CarEntry> cars = new List<CarEntry>();
    }
}
