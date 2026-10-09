using System;
using System.Collections.Generic;
using UnityEngine;

namespace MostWanted
{
    [Serializable]
    public class MapEntry
    {
        public string name = "Harita";
        public GameObject prefab;
        public string credit = "";
    }

    /// <summary>"Most Wanted/Haritaları Tara" ile Assets/Maps içinden doldurulur (Assets/Resources/MapRegistry.asset).</summary>
    [CreateAssetMenu(fileName = "MapRegistry", menuName = "Most Wanted/Harita Kaydı")]
    public class MapRegistry : ScriptableObject
    {
        public List<MapEntry> maps = new List<MapEntry>();
    }
}
