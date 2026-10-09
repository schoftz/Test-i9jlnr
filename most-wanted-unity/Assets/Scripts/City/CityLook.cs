using UnityEngine;

namespace MostWanted
{
    /// <summary>
    /// Şehir görünümü malzemeleri (Render ajanı): MW/Facade, MW/Road, MW/Ground, MW/Foliage.
    /// Shader yoksa / desteklenmiyorsa null döner → çağıran eski URP/Lit malzemesini kullanır.
    /// </summary>
    public static class CityLook
    {
        static Material Base(string res)
        {
            var m = Resources.Load<Material>("Render/" + res);
            return m != null && m.shader != null && m.shader.isSupported ? m : null;
        }

        struct FacadeStyle
        {
            public float winW, floorH, lit, dirt, smooth;
            public Vector4 win;
            public Color frame, glass;
        }

        static readonly FacadeStyle[] Styles =
        {
            new FacadeStyle { winW = 1.6f, floorH = 3.8f, win = new Vector4(0.93f, 0.84f, 0.5f, 0), lit = 0.45f, dirt = 0.08f, smooth = 0.5f, frame = new Color(0.30f, 0.32f, 0.35f), glass = new Color(0.09f, 0.14f, 0.20f) },   // 0 cam kule
            new FacadeStyle { winW = 3.2f, floorH = 3.5f, win = new Vector4(0.55f, 0.48f, 0.55f, 0), lit = 0.4f, dirt = 0.4f, smooth = 0.15f, frame = new Color(0.50f, 0.50f, 0.50f), glass = new Color(0.07f, 0.09f, 0.11f) },  // 1 beton
            new FacadeStyle { winW = 2.8f, floorH = 3.2f, win = new Vector4(0.40f, 0.55f, 0.55f, 0), lit = 0.5f, dirt = 0.3f, smooth = 0.12f, frame = new Color(0.90f, 0.88f, 0.84f), glass = new Color(0.07f, 0.08f, 0.10f) },  // 2 tuğla
            new FacadeStyle { winW = 3.0f, floorH = 3.2f, win = new Vector4(0.42f, 0.52f, 0.55f, 0), lit = 0.5f, dirt = 0.3f, smooth = 0.18f, frame = new Color(0.95f, 0.95f, 0.93f), glass = new Color(0.07f, 0.08f, 0.10f) },  // 3 sıva
            new FacadeStyle { winW = 3.6f, floorH = 3.0f, win = new Vector4(0.32f, 0.45f, 0.55f, 0), lit = 0.55f, dirt = 0.2f, smooth = 0.15f, frame = new Color(0.96f, 0.96f, 0.95f), glass = new Color(0.07f, 0.08f, 0.10f) }, // 4 ev
            new FacadeStyle { winW = 6.0f, floorH = 6.0f, win = new Vector4(0.86f, 0.16f, 0.82f, 0), lit = 0.2f, dirt = 0.5f, smooth = 0.1f, frame = new Color(0.45f, 0.46f, 0.48f), glass = new Color(0.10f, 0.12f, 0.14f) },  // 5 depo
            new FacadeStyle { winW = 3.0f, floorH = 3.2f, win = new Vector4(0.95f, 0.45f, 0.55f, 0), lit = 0.0f, dirt = 0.45f, smooth = 0.12f, frame = new Color(0.55f, 0.55f, 0.55f), glass = new Color(0.04f, 0.04f, 0.05f) }, // 6 otopark
            new FacadeStyle { winW = 4.5f, floorH = 6.0f, win = new Vector4(0.9f, 0.5f, 0.5f, 0), lit = 0.8f, dirt = 0.3f, smooth = 0.2f, frame = new Color(0.20f, 0.20f, 0.22f), glass = new Color(0.08f, 0.09f, 0.10f) },     // 7 dükkan
        };

        /// <summary>Stil başına cephe malzemesi (0 cam kule .. 6 otopark, 7 dükkan zemin katı).</summary>
        public static Material Facade(int style, Color wall)
        {
            var b = Base("MW_FacadeMat");
            if (b == null) return null;
            style = Mathf.Clamp(style, 0, Styles.Length - 1);
            var st = Styles[style];
            var m = new Material(b) { name = "MW_Cephe_" + style };
            m.SetColor("_BaseColor", wall);
            m.SetColor("_GlassColor", st.glass);
            m.SetColor("_FrameColor", st.frame);
            m.SetFloat("_Style", style);
            m.SetFloat("_FloorH", st.floorH);
            m.SetFloat("_WinW", st.winW);
            m.SetVector("_WinSize", st.win);
            m.SetFloat("_LitRatio", st.lit);
            m.SetFloat("_Dirt", st.dirt);
            m.SetFloat("_WallSmoothness", st.smooth);
            m.SetFloat("_Seed", style * 13.7f);
            return m;
        }

        /// <summary>Yol yüzeyi: mode 0 şeritli yol (tex: çizgi dokusu, u enine), 1 düz asfalt, 2 kaldırım (tex: taş), 3 beton/bordür.</summary>
        public static Material Road(int mode, Texture tex, Vector2 texScale, Color color, float roadWidth, float laneW, float smoothness)
        {
            var b = Base("MW_RoadMat");
            if (b == null) return null;
            var m = new Material(b) { name = "MW_Yol_" + mode };
            m.SetColor("_BaseColor", color);
            if (tex != null) { m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", texScale); }
            m.SetFloat("_Mode", mode);
            m.SetFloat("_RoadWidth", roadWidth);
            m.SetFloat("_LaneW", laneW);
            m.SetFloat("_Smoothness", smoothness);
            return m;
        }
    
        /// <summary>Arazi: renk haritası + prosedürel çim detayı + uzaklık tonu.</summary>
        public static Material Ground(Texture colorMap)
        {
            var b = Base("MW_GroundMat");
            if (b == null) return null;
            var m = new Material(b) { name = "MW_Arazi" };
            m.SetTexture("_BaseMap", colorMap);
            return m;
        }

        /// <summary>Ağaç malzemesi (instanced): leaf = yaprak (rüzgâr, geçirgenlik), aksi halde gövde.</summary>
        public static Material Foliage(Color color, bool leaf)
        {
            var b = Base("MW_FoliageMat");
            if (b == null) return null;
            var m = new Material(b) { name = leaf ? "MW_Yaprak" : "MW_Govde" };
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Leaf", leaf ? 1f : 0f);
            m.enableInstancing = true;
            return m;
        }
    }
}
