using System.Collections.Generic;
using UnityEngine;

namespace MostWanted
{
    /// <summary>Ses grupları (Motor, Efekt, Müzik, Siren) — duraklat menüsündeki kaydırıcılar.</summary>
    public static class AudioBus
    {
        public enum Bus { Motor, Efekt, Muzik, Siren }
        public static readonly string[] Names = { "Motor", "Efekt", "Müzik", "Siren" };
        public static float[] Volumes = { 1f, 1f, 0.6f, 0.8f };
        public static float Get(Bus b) { return Volumes[(int)b]; }

        public static void Load(SaveData d)
        {
            if (d.volumes != null && d.volumes.Length == 4) Volumes = (float[])d.volumes.Clone();
        }
        public static void Store(SaveData d) { d.volumes = (float[])Volumes.Clone(); }
    }

    /// <summary>
    /// Yarış oyunu tarzı motor sesi.
    /// 1) Örnek tabanlı: Resources/EngineSounds/&lt;araç_id&gt;/ (yoksa EngineSounds/default/) içindeki
    ///    "&lt;rpm&gt;_on" / "&lt;rpm&gt;_off" döngüleri (ör. 1000_on.wav, 3000_off.ogg, idle.wav). En yakın iki devir
    ///    döngüsü eşit güçte çapraz geçişle karıştırılır, her biri rpm/örnekRpm ile perdelenir, gaz ile on/off karışır.
    ///    Ek katmanlar (isteğe bağlı): turbo, bov, shift, limiter, pop1..n, impact1..n, horn, squeal, wind, gravel, nitro.
    /// 2) Prosedürel (örnek yoksa): silindir ateşleme darbeleri + rezonanslı egzoz formantları, krank harmonikleri,
    ///    emme gürültüsü, V8 düzensiz ateşleme, turbo ıslığı, BOV, geri tepme patlamaları, lastik, rüzgar, çakıl, nitro.
    ///    OnAudioFilterRead ile, ayırmasız (allocation-free) DSP.
    /// </summary>
    public class EngineAudio : MonoBehaviour
    {
        public CarController car;
        public bool isPlayer;
        public static readonly List<EngineAudio> All = new List<EngineAudio>();

        // ---- örnek tabanlı ----
        class Loop { public float rpm; public bool on; public AudioSource src; }
        readonly List<Loop> loops = new List<Loop>();
        readonly Dictionary<string, AudioClip> extra = new Dictionary<string, AudioClip>();
        readonly Dictionary<string, List<AudioClip>> groups = new Dictionary<string, List<AudioClip>>();
        AudioSource turboSrc, squealSrc, windSrc, gravelSrc, nitroSrc, hornSrc, fx;
        bool sampleMode;

        // ---- prosedürel DSP durumu (ses iş parçacığı) ----
        AudioSource synthSrc;
        int sr = 48000;
        volatile float pRpm = 900f, pThrottle, pBoost, pSlip, pSpeed01, pGravel, pNitro, pGain = 1f, pLimiter;
        volatile int bovTrigger, popRequest, bangRequest, surgeTrigger;
        volatile int popFlash;
        int cylinders = 4;
        float[] firePattern = { 1f }, ampPattern = { 1f };
        float formant1 = 180f, formant2 = 620f, formant3 = 1500f;
        bool turbo;

        // ---- motor sesi karakteri ----
        public static readonly string[] EngineTypes = { "I3", "I4", "I5", "F4", "F6", "I6", "V6", "V8", "V8FP", "V10", "V12", "W16", "ROTARY", "DIESEL", "EV" };
        public static readonly string[] EngineNames = { "3 Silindir", "4 Silindir", "5 Silindir", "Boxer 4", "Boxer 6 (Porsche)", "Sıralı 6", "V6", "V8 (Muscle)", "V8 Düz Krank", "V10", "V12", "W16 (Bugatti)", "Rotary (Wankel)", "Dizel", "Elektrik" };
        public string engineType = "I4";
        int turboSize;            // 0 yok, 1 küçük (hızlı), 2 büyük (gecikmeli)
        bool supercharger, rotary, diesel, electric;
        float noiseMix = 0.45f, jitter = 0.1f, intakeAmt = 0.25f, exhaustGain = 1f, popChance = 0.6f, f3Gain = 0.35f, orderHarm = 0f, formantQ = 1f, decayMul = 1f;
        int exhaustLevel;
        bool synthEngine = true, synthTurbo = true, synthSqueal = true, synthWind = true, synthGravel = true, synthNitro = true;

        // DSP değişkenleri
        double firePhase, crankPhase, whistlePhase, squealPhase, squealLfo;
        float env, dc, lp1, lp2, lp3, bovEnv, hp1, hp2, knockEnv, surgeEnv, atk = 1f, whoosh;
        float sRpm = 900f, sTh, sGain, sBoost, sSpeed, sSlip;   // örnek başına yumuşatılmış parametreler
        double scPhase, evPhase, invPhase, surgePhase;
        // patlama (backfire) üreteci
        float popThump, popThumpAtk, popCrackle, popFreq = 90f, popAmp, popGap;
        double popPhase;
        int popQueue;
        float[] delayBuf; int delayIdx;
        int cyl;
        uint rng = 2463534242u;
        Biquad f1, f2, f3, gravelBp, crackleBp, knockBp;

        // ana iş parçacığı
        float boost, lastThrottle = 0f, popFlameTimer, surfaceTimer, thumpCd, popCooldown;
        readonly float[] lastForce = new float[4];

        struct Biquad
        {
            float b0, b1, b2, a1, a2, z1, z2;
            public void Bandpass(float f, float q, float rate)
            {
                float w0 = 2f * Mathf.PI * Mathf.Min(f, rate * 0.45f) / rate;
                float alpha = Mathf.Sin(w0) / (2f * q);
                float a0 = 1f + alpha;
                b0 = alpha / a0; b1 = 0f; b2 = -alpha / a0;
                a1 = -2f * Mathf.Cos(w0) / a0; a2 = (1f - alpha) / a0;
            }
            public float Process(float x)
            {
                float y = b0 * x + z1;
                z1 = b1 * x - a1 * y + z2;
                z2 = b2 * x - a2 * y;
                return y;
            }
        }

        // ------------------------------------------------------------------ kurulum
        public static EngineAudio Attach(CarController car, bool player)
        {
            var ea = car.gameObject.AddComponent<EngineAudio>();
            ea.car = car;
            ea.isPlayer = player;
            ea.Setup();
            return ea;
        }

        void OnEnable() { All.Add(this); }
        void OnDisable() { All.Remove(this); }

        void Setup()
        {
            sr = AudioSettings.outputSampleRate;
            var def = car.def;
            ConfigureCharacter(def);

            // örnekleri yükle
            string id = def != null ? def.id : "default";
            var clips = new List<AudioClip>(Resources.LoadAll<AudioClip>("EngineSounds/" + id));
            if (clips.Count == 0) clips.AddRange(Resources.LoadAll<AudioClip>("EngineSounds/default"));
            foreach (var c in Resources.LoadAll<AudioClip>("EngineSounds/common")) clips.Add(c);
            foreach (var c in clips) Classify(c);
            sampleMode = loops.Count >= 2;
            synthEngine = !sampleMode;

            fx = NewSource("Efekt", false, AudioBus.Bus.Efekt);
            if (sampleMode)
            {
                foreach (var l in loops) l.src.Play();
            }
            AudioClip ac;
            if (extra.TryGetValue("turbo", out ac)) { turboSrc = NewLoop(ac, AudioBus.Bus.Motor); synthTurbo = false; }
            if (extra.TryGetValue("squeal", out ac)) { squealSrc = NewLoop(ac, AudioBus.Bus.Efekt); synthSqueal = false; }
            if (extra.TryGetValue("wind", out ac)) { windSrc = NewLoop(ac, AudioBus.Bus.Efekt); synthWind = false; }
            if (extra.TryGetValue("gravel", out ac)) { gravelSrc = NewLoop(ac, AudioBus.Bus.Efekt); synthGravel = false; }
            if (extra.TryGetValue("nitro", out ac)) { nitroSrc = NewLoop(ac, AudioBus.Bus.Efekt); synthNitro = false; }
            if (!isPlayer) { synthSqueal = synthWind = synthGravel = synthNitro = false; }

            // prosedürel kaynak: sabit 1.0 DC döngüsü + OnAudioFilterRead (böylece 3B zayıflama/panning korunur)
            // ÖNEMLİ: OnAudioFilterRead kullanan bileşen, TEK AudioSource'u olan kendi alt objesinde olmalı
            var sg = new GameObject("MotorSentez");
            sg.transform.SetParent(transform, false);
            synthSrc = sg.AddComponent<AudioSource>();
            sg.AddComponent<SynthVoice>().owner = this;
            synthSrc.clip = AudioSynth.DcLoop();
            synthSrc.loop = true;
            ApplySpatial(synthSrc);
            synthSrc.volume = isPlayer ? 0.55f : 0.9f;
            synthSrc.Play();

            f1.Bandpass(formant1, 1.6f * formantQ, sr); f2.Bandpass(formant2, 2.2f * formantQ, sr); f3.Bandpass(formant3, 3f * formantQ, sr);
            gravelBp.Bandpass(900f, 0.8f, sr);
            crackleBp.Bandpass(1300f, 0.6f, sr);
            knockBp.Bandpass(3600f, 1.4f, sr);
            delayBuf = new float[Mathf.Max(64, (int)(sr * 0.06f))];
            car.onShiftAudio = (gear, q) => PlayShift();
        }

        static uint Hash(string t)
        {
            uint h = 2166136261u;
            if (t != null) foreach (char c in t) { h ^= c; h *= 16777619u; }
            return h;
        }

        /// <summary>Motor karakteri: tip + araç başına küçük farklar (formant, düzensizlik, emme, egzoz).</summary>
        void ConfigureCharacter(CarEntry def)
        {
            // motor/egzoz sesi satın alınmaz: önce gerçek araca göre (ad), sonra katalog motor tipi, sonra güç verisi
            string t = EngineByName(def);
            if (t == "" && def != null && !string.IsNullOrEmpty(def.engineType)) t = def.engineType.ToUpperInvariant();
            if (t == "" && def != null)
            {
                if (def.redlineRpm >= 8400f) t = "V10";
                else if (def.torqueNm >= 600f) t = "V8";
                else if (def.torqueNm >= 450f) t = "I6";
                else t = "I4";
            }
            engineType = t;
            turboSize = def != null && def.turbo ? Mathf.Max(1, def.turboSize) : 0;
            supercharger = def != null && def.supercharger;
            turbo = turboSize > 0;
            rotary = diesel = electric = false;
            noiseMix = 0.45f; jitter = 0.1f; intakeAmt = 0.25f; f3Gain = 0.35f; orderHarm = 0f; formantQ = 1f; decayMul = 1f; popChance = 0.6f;
            switch (t)
            {
                case "I3":
                    cylinders = 3; formant1 = 150f; formant2 = 520f; formant3 = 1300f; orderHarm = 1.5f;
                    firePattern = new[] { 1f }; ampPattern = new[] { 1.05f, 0.95f, 1f };
                    break;
                case "I5":
                    cylinders = 5; formant1 = 200f; formant2 = 690f; formant3 = 1700f; jitter = 0.12f;
                    firePattern = new[] { 1.05f, 0.95f, 1.03f, 0.97f, 1f }; ampPattern = new[] { 1.12f, 0.84f, 1.06f, 0.9f, 1.0f }; // Audi "warble"
                    break;
                case "F4":
                    cylinders = 4; formant1 = 135f; formant2 = 470f; formant3 = 1100f; noiseMix = 0.5f;
                    firePattern = new[] { 0.86f, 1.14f, 0.9f, 1.1f }; ampPattern = new[] { 1.28f, 0.72f, 1.18f, 0.82f };  // eşit olmayan manifold "rumble"
                    break;
                case "I6":
                    cylinders = 6; formant1 = 170f; formant2 = 560f; formant3 = 1600f; jitter = 0.06f;
                    firePattern = new[] { 1f }; ampPattern = new[] { 1f, 0.97f };
                    break;
                case "V6":
                    cylinders = 6; formant1 = 240f; formant2 = 820f; formant3 = 2700f; noiseMix = 0.7f; f3Gain = 0.6f; formantQ = 1.3f; // VQ "rasp"
                    firePattern = new[] { 1f, 0.97f, 1.03f }; ampPattern = new[] { 1f, 0.92f, 1.06f };
                    break;
                case "V8":
                    cylinders = 8; formant1 = 110f; formant2 = 340f; formant3 = 950f; popChance = 0.8f;
                    firePattern = new[] { 0.82f, 1.18f, 1.0f, 0.9f, 1.12f, 0.95f, 1.05f, 0.98f }; // crossplane "burble"
                    ampPattern = new[] { 1.15f, 0.8f, 1.05f, 0.9f, 1.2f, 0.85f, 1.0f, 0.95f };
                    break;
                case "V8FP":
                    cylinders = 8; formant1 = 320f; formant2 = 950f; formant3 = 2800f; f3Gain = 0.75f; noiseMix = 0.35f; jitter = 0.04f; // düz krank "çığlık"
                    firePattern = new[] { 1f }; ampPattern = new[] { 1f, 0.96f, 1.02f, 0.98f };
                    break;
                case "F6":
                    cylinders = 6; formant1 = 230f; formant2 = 760f; formant3 = 2100f; f3Gain = 0.55f; jitter = 0.05f; noiseMix = 0.4f;
                    firePattern = new[] { 1f, 0.98f, 1.02f }; ampPattern = new[] { 1.04f, 0.96f, 1f };
                    break;
                case "W16":
                    cylinders = 16; formant1 = 85f; formant2 = 260f; formant3 = 700f; noiseMix = 0.3f; jitter = 0.02f; f3Gain = 0.3f; decayMul = 1.4f; popChance = 0.2f;
                    firePattern = new[] { 1f }; ampPattern = new[] { 1f, 0.98f };
                    if (turboSize < 3) turboSize = 3; turbo = true;
                    break;
                case "V10":
                    cylinders = 10; formant1 = 260f; formant2 = 820f; formant3 = 2300f; f3Gain = 0.55f;
                    firePattern = new[] { 0.96f, 1.04f }; ampPattern = new[] { 1f, 0.92f };
                    break;
                case "V12":
                    cylinders = 12; formant1 = 360f; formant2 = 1100f; formant3 = 3200f; noiseMix = 0.25f; jitter = 0.03f; f3Gain = 0.7f; decayMul = 1.3f;
                    firePattern = new[] { 1f }; ampPattern = new[] { 1f };
                    break;
                case "ROTARY":
                    rotary = true; cylinders = 4; formant1 = 300f; formant2 = 1200f; formant3 = 3000f; noiseMix = 0.7f; decayMul = 0.55f; f3Gain = 0.6f; popChance = 0.9f;
                    firePattern = new[] { 1f, 1.02f }; ampPattern = new[] { 1.1f, 0.9f };
                    break;
                case "DIESEL":
                    diesel = true; cylinders = 4; formant1 = 105f; formant2 = 360f; formant3 = 850f; noiseMix = 0.55f; popChance = 0f; intakeAmt = 0.15f;
                    firePattern = new[] { 1f, 1.04f, 0.96f, 1f }; ampPattern = new[] { 1f, 0.9f, 1.05f, 0.95f };
                    if (turboSize == 0) turboSize = 2;
                    turbo = true;
                    break;
                case "EV":
                    electric = true; cylinders = 4; popChance = 0f; turboSize = 0; turbo = false; supercharger = false;
                    break;
                default:
                    engineType = "I4";
                    cylinders = 4; formant1 = 190f; formant2 = 650f; formant3 = 1350f;
                    firePattern = new[] { 1f, 1.02f, 0.98f, 1f }; ampPattern = new[] { 1f, 0.9f, 1.05f, 0.95f };
                    break;
            }
            // araç başına sabit küçük farklar; trafik/polis için ayrıca rastgele
            uint h = Hash(def != null ? def.id : "x") ^ (isPlayer ? 0u : (uint)Random.Range(0, 100000));
            float r1 = (h & 1023) / 1023f - 0.5f, r2 = ((h >> 10) & 1023) / 1023f - 0.5f, r3 = ((h >> 20) & 1023) / 1023f - 0.5f;
            formant1 *= 1f + r1 * 0.16f; formant2 *= 1f + r2 * 0.16f; formant3 *= 1f + r3 * 0.16f;
            jitter *= 1f + r2 * 0.6f;
            intakeAmt *= 1f + r3 * 0.6f;
            // "Egzoz" performans paketi: daha yüksek ses, daha çok patlama
            exhaustLevel = ExhaustClass(def, engineType);
            exhaustGain = 1f + 0.18f * exhaustLevel;
            popChance = Mathf.Clamp01(popChance * 0.3f * (1f + 0.3f * exhaustLevel));   // seyrek: patlamalar nadir olsun
        }

        /// <summary>Garajdaki "Motor Sesi" değişimi sonrası yeniden yapılandır.</summary>
        public void Reconfigure()
        {
            ConfigureCharacter(car.def);
            f1.Bandpass(formant1, 1.6f * formantQ, sr); f2.Bandpass(formant2, 2.2f * formantQ, sr); f3.Bandpass(formant3, 3f * formantQ, sr);
        }

        void Classify(AudioClip c)
        {
            string n = c.name.ToLowerInvariant();
            if (n == "idle" || n == "idle_on" || n == "idle_off")
            {
                AddLoop(c, 900f, n != "idle_off");
                if (n == "idle") AddLoop(c, 900f, false);
                return;
            }
            int us = n.IndexOf('_');
            float rpm;
            if (us > 0 && float.TryParse(n.Substring(0, us), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out rpm))
            {
                AddLoop(c, rpm, !n.EndsWith("_off"));
                return;
            }
            // pop1, impact2 ... gruplar
            string key = n.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '_');
            if (key == "pop" || key == "impact" || key == "thump" || key == "backfire")
            {
                if (key == "backfire") key = "pop";
                List<AudioClip> l; if (!groups.TryGetValue(key, out l)) groups[key] = l = new List<AudioClip>(); l.Add(c);
                return;
            }
            extra[n] = c;
        }

        void AddLoop(AudioClip c, float rpm, bool on)
        {
            var src = NewSource("Loop_" + c.name, true, AudioBus.Bus.Motor);
            src.clip = c; src.loop = true; src.volume = 0f;
            loops.Add(new Loop { rpm = rpm, on = on, src = src });
        }

        AudioSource NewSource(string name, bool child, AudioBus.Bus bus)
        {
            AudioSource s;
            if (child) { var g = new GameObject(name); g.transform.SetParent(transform, false); s = g.AddComponent<AudioSource>(); }
            else s = gameObject.AddComponent<AudioSource>();
            ApplySpatial(s);
            return s;
        }

        AudioSource NewLoop(AudioClip c, AudioBus.Bus bus)
        {
            var s = NewSource("Katman_" + c.name, true, bus);
            s.clip = c; s.loop = true; s.volume = 0f; s.Play();
            return s;
        }

        void ApplySpatial(AudioSource s)
        {
            s.playOnAwake = false;
            if (isPlayer) { s.spatialBlend = 0f; s.dopplerLevel = 0f; }
            else
            {
                s.spatialBlend = 1f; s.dopplerLevel = 0.3f; s.rolloffMode = AudioRolloffMode.Linear;
                s.minDistance = 6f; s.maxDistance = 140f;
            }
        }

        // ------------------------------------------------------------------ ana iş parçacığı
        void Update()
        {
            if (car == null) return;
            float dt = Time.deltaTime;
            float redline = Mathf.Max(3000f, car.redline);
            float rpm = car.rpm;
            float th = Mathf.Clamp01(Mathf.Abs(car.throttle));
            if (car.Shifting) th *= 0.2f;
            float kmh = car.SpeedKmh;
            float motorVol = AudioBus.Get(AudioBus.Bus.Motor), fxVol = AudioBus.Get(AudioBus.Bus.Efekt);

            // turbo basıncı ve BOV
            if (turbo)
            {
                // küçük turbo: hızlı dolar; büyük turbo: gecikmeli ama güçlü
                float spool = turboSize == 2 ? 0.55f : 2.6f;
                float onset = turboSize == 2 ? 0.45f : 0.25f;
                boost = Mathf.MoveTowards(boost, th * Mathf.Clamp01((rpm / redline - onset) / 0.4f), dt * (th > 0.5f ? spool : 3f));
            }
            if (turbo && lastThrottle > 0.6f && th < 0.2f && boost > 0.35f)
            {
                if (turboSize == 2 && Random.value < 0.6f) surgeTrigger = 1; else bovTrigger = 1;
                AudioClip bov; if (extra.TryGetValue("bov", out bov)) fx.PlayOneShot(bov, 0.6f * fxVol);
            }
            // geri tepme (overrun) patlamaları
            bool overrun = th < 0.1f && rpm > redline * 0.55f && kmh > 30f;
            popCooldown -= dt;
            // patlama: yüksek devirden gaz kesince; kalkışta/düşük devirde yok, en az 0.25 sn arayla
            if (PopsEnabled && overrun && rpm > 4500f && kmh > 50f && lastThrottle > 0.8f && popCooldown <= 0f && Random.value < popChance)
            {
                popRequest = 1;
                popCooldown = 2.5f;
            }
            if (popFlash > 0)
            {
                popFlash = 0;
                if (car.electric) popFlameTimer = -1f;
                popFlameTimer = 0.08f;
                List<AudioClip> pops; if (groups.TryGetValue("pop", out pops) && pops.Count > 0) fx.PlayOneShot(pops[Random.Range(0, pops.Count)], 0.7f * fxVol);
            }
            if (popFlameTimer > 0f)
            {
                popFlameTimer -= dt;
                if (!car.electric)
                    foreach (var f in car.flames) if (f != null && !car.nitroActive) { f.gameObject.SetActive(popFlameTimer > 0f); f.localScale = new Vector3(0.12f, 0.12f, 0.45f); }
                if (car.popLight != null) car.popLight.enabled = popFlameTimer > 0f;
            }
            lastThrottle = th;

            // yüzey: çakıl / asfalt
            surfaceTimer -= dt;
            if (surfaceTimer <= 0f && isPlayer)
            {
                surfaceTimer = 0.25f;
                pGravel = 0f;
                Collider gc = car.WheelGroundCollider(2);
                if (gc != null)
                {
                    string n = gc.name.ToLowerInvariant();
                    bool asphalt = n.Contains("street") || n.Contains("otoyol") || n.Contains("yol") || n.Contains("kavsak") || n.Contains("object") || n.Contains("curb") || n.Contains("concrete") || n.Contains("pavement");
                    pGravel = asphalt ? 0f : Mathf.Clamp01(kmh / 80f);
                }
            }

            // süspansiyon darbeleri
            thumpCd -= dt;
            if (isPlayer)
                for (int i = 0; i < 4; i++)
                {
                    float force = car.WheelLoad(i);
                    if (force - lastForce[i] > car.rb.mass * 15f && thumpCd <= 0f && kmh > 25f)
                    {
                        thumpCd = 0.6f;
                        List<AudioClip> th2; AudioClip c = groups.TryGetValue("thump", out th2) && th2.Count > 0 ? th2[Random.Range(0, th2.Count)] : AudioSynth.Thump();
                        fx.PlayOneShot(c, 0.5f * fxVol);
                    }
                    lastForce[i] = force;
                }

            // DSP parametreleri
            float slipDeg = Mathf.Abs(car.slipAngle);
            float slip = Mathf.Clamp01((slipDeg - 8f) / 25f) * Mathf.Clamp01(kmh / 30f);
            if (car.handbrake && kmh > 20f) slip = Mathf.Max(slip, 0.7f);
            // orta şiddette virajda da hafif ciyaklama: lastik yanal kayması tepe değerin %60'ından itibaren
            slip = Mathf.Max(slip, Mathf.Clamp01((car.tyreSlip - 0.6f) / 0.9f) * 0.75f * Mathf.Clamp01(kmh / 35f));
            if (car.gear == 1 && car.throttle > 0.8f && kmh < 30f && car.peakTorque > 300f && !car.locked) slip = Mathf.Max(slip, 0.6f);
            pRpm = Mathf.Max(600f, rpm);
            pThrottle = th;
            pBoost = boost;
            pSlip = slip;
            pSpeed01 = Mathf.Clamp01(kmh / 300f);
            pNitro = car.nitroActive ? 1f : 0f;
            pLimiter = car.revLimiter ? 1f : 0f;
            float sb = Game.I != null && Game.I.playerDriver != null && Game.I.playerDriver.speedbreakerOn ? 0.75f : 1f;
            pGain = motorVol * sb;
            synthSrc.pitch = sb < 1f && isPlayer ? 0.8f : 1f;

            // örnek tabanlı motor karışımı
            if (sampleMode) MixLoops(rpm, th, motorVol * (isPlayer ? 1f : 0.8f));
            if (turboSrc != null) { turboSrc.volume = boost * 0.4f * motorVol; turboSrc.pitch = 0.7f + boost * 0.8f; }
            if (squealSrc != null) { squealSrc.volume = slip * 0.6f * fxVol; squealSrc.pitch = 0.9f + pSpeed01 * 0.3f; }
            if (windSrc != null) windSrc.volume = pSpeed01 * pSpeed01 * 0.5f * fxVol;
            if (gravelSrc != null) gravelSrc.volume = pGravel * 0.5f * fxVol;
            if (nitroSrc != null) nitroSrc.volume = pNitro * 0.4f * fxVol;
        }

        void MixLoops(float rpm, float th, float vol)
        {
            for (int pass = 0; pass < 2; pass++)
            {
                bool on = pass == 0;
                Loop lo = null, hi = null;
                foreach (var l in loops)
                {
                    if (l.on != on) continue;
                    if (l.rpm <= rpm && (lo == null || l.rpm > lo.rpm)) lo = l;
                    if (l.rpm >= rpm && (hi == null || l.rpm < hi.rpm)) hi = l;
                }
                if (lo == null) lo = hi; if (hi == null) hi = lo;
                float t = (lo == null || hi == null || hi.rpm <= lo.rpm) ? 0f : Mathf.Clamp01((rpm - lo.rpm) / (hi.rpm - lo.rpm));
                // eşit güç geçişi
                float wl = Mathf.Cos(t * Mathf.PI * 0.5f), wh = Mathf.Sin(t * Mathf.PI * 0.5f);
                float onOff = on ? Mathf.Sin(th * Mathf.PI * 0.5f) : Mathf.Cos(th * Mathf.PI * 0.5f);
                foreach (var l in loops)
                {
                    if (l.on != on) continue;
                    float w = (l == lo ? wl : 0f) + (l == hi && hi != lo ? wh : 0f);
                    if (lo == hi && l == lo) w = 1f;
                    l.src.volume = w * onOff * vol;
                    l.src.pitch = Mathf.Clamp(rpm / Mathf.Max(100f, l.rpm), 0.3f, 3f);
                }
            }
        }

        public void PlayShift()
        {
            AudioClip c; fx.PlayOneShot(extra.TryGetValue("shift", out c) ? c : AudioSynth.Shift(), 0.5f * AudioBus.Get(AudioBus.Bus.Efekt));
            if (turbo && boost > 0.4f) bovTrigger = 1;
            // Egzoz paketiyle vites atarken "BANG"
            if (PopsEnabled && exhaustLevel >= 2 && !electric && car != null && car.rpm > car.redline * 0.7f) bangRequest = 1;
        }

        public void Impact(float relSpeed)
        {
            if (relSpeed < 2f) return;
            List<AudioClip> l;
            AudioClip c = groups.TryGetValue("impact", out l) && l.Count > 0 ? l[Random.Range(0, l.Count)] : AudioSynth.Impact();
            fx.PlayOneShot(c, Mathf.Clamp01(relSpeed / 25f) * AudioBus.Get(AudioBus.Bus.Efekt));
        }

        public void Horn(bool on)
        {
            if (hornSrc == null)
            {
                AudioClip c; if (!extra.TryGetValue("horn", out c)) c = AudioSynth.Horn();
                hornSrc = NewSource("Korna", true, AudioBus.Bus.Efekt); hornSrc.clip = c; hornSrc.loop = true;
            }
            hornSrc.volume = 0.5f * AudioBus.Get(AudioBus.Bus.Efekt);
            if (on && !hornSrc.isPlaying) hornSrc.Play();
            if (!on && hornSrc.isPlaying) hornSrc.Stop();
        }

        public void SetAudible(bool on)
        {
            // durdurulan kaynakta OnAudioFilterRead çağrılmaz -> CPU tasarrufu
            if (synthSrc != null) { if (on && !synthSrc.isPlaying) synthSrc.Play(); else if (!on && synthSrc.isPlaying) synthSrc.Stop(); }
            foreach (var l in loops) l.src.mute = !on;
            if (turboSrc != null) turboSrc.mute = !on;
        }

        // ------------------------------------------------------------------ DSP (ses iş parçacığı)
        float Noise()
        {
            rng ^= rng << 13; rng ^= rng >> 17; rng ^= rng << 5;
            return (rng & 0xFFFFFF) / 8388608f - 1f;
        }

        /// <summary>Ses iş parçacığında SynthVoice tarafından çağrılır.</summary>
        public void Render(float[] data, int channels)
        {
            if (delayBuf == null) return;
            float rate = sr;
            // parametreleri tampon başına bir kez oku, örnek başına yumuşat (ani sıçrama → tık olmasın)
            float tRpm = pRpm, tTh = pThrottle, tGain = pGain, tBoost = pBoost, tSpeed = pSpeed01, tSlip = pSlip;
            float sm = 1f - Mathf.Exp(-1f / (rate * 0.012f));
            float rpm = sRpm, th = sTh, gain = sGain;
            float fire = rpm / 60f * cylinders * 0.5f;
            float crank = rpm / 60f;
            float rpm01 = Mathf.Clamp01(rpm / 8000f);
            float decay = Mathf.Exp(-1f / (rate * Mathf.Clamp(0.55f * decayMul / Mathf.Max(fire, 1f), 0.0012f, 0.03f)));
            float load = 0.35f + 0.65f * th;
            bool big = turboSize == 2;
            float whistleF = big ? 1500f + pBoost * 3800f : 2800f + pBoost * 6500f;
            float whistleAmp = big ? 0.045f : 0.03f;
            float bovDecay = Mathf.Exp(-1f / (rate * 0.28f));
            float surgeDecay = Mathf.Exp(-1f / (rate * 0.55f));
            float knockDecay = Mathf.Exp(-1f / (rate * 0.004f));
            float atkStep = 1f / (rate * 0.002f); // ≥2 ms atak: tıklama yok
            if (bovTrigger != 0) { bovTrigger = 0; bovEnv = 1f; }
            if (surgeTrigger != 0) { surgeTrigger = 0; surgeEnv = 1f; }
            if (bangRequest != 0) { bangRequest = 0; StartPop(2.2f); }
            float lpA = Mathf.Clamp01(400f / rate * 6.28f), lpW = Mathf.Clamp01(250f / rate * 6.28f);
            float squealF = 950f + pSpeed01 * 250f;
            float twoPi = 6.2831853f;

            float atkInc = 1f / (rate * 0.0015f);   // ateşleme darbesi ≥1.5 ms atak
            for (int i = 0; i < data.Length; i += channels)
            {
                sRpm += (tRpm - sRpm) * sm; sTh += (tTh - sTh) * sm; sGain += (tGain - sGain) * sm;
                sBoost += (tBoost - sBoost) * sm; sSpeed += (tSpeed - sSpeed) * sm; sSlip += (tSlip - sSlip) * sm;
                rpm = sRpm; th = sTh; gain = sGain;
                fire = rpm / 60f * cylinders * 0.5f; crank = rpm / 60f; load = 0.35f + 0.65f * th;
                float s = 0f;
                float n = Noise();
                if (synthEngine && electric)
                {
                    // elektrik: hıza bağlı motor ıslığı + inverter tonu, vites yok
                    float mf = 60f + pSpeed01 * 2800f;
                    evPhase += mf / rate; if (evPhase > 1.0) evPhase -= 1.0;
                    invPhase += (1750f + 300f * pSpeed01) / rate; if (invPhase > 1.0) invPhase -= 1.0;
                    float ev = Mathf.Sin((float)(evPhase * twoPi)) * 0.22f + Mathf.Sin((float)(evPhase * twoPi * 3.0)) * 0.05f;
                    float inv = Mathf.Sin((float)(invPhase * twoPi)) * 0.05f * (0.3f + 0.7f * th);
                    s += (ev + inv + n * 0.01f) * (0.4f + 0.6f * Mathf.Max(th, pSpeed01));
                }
                else if (synthEngine)
                {
                    int pi = cyl % firePattern.Length;
                    firePhase += fire / rate / firePattern[pi];
                    if (firePhase >= 1.0)
                    {
                        firePhase -= 1.0;
                        cyl++;
                        float amp = ampPattern[cyl % ampPattern.Length] * (1f - jitter + 2f * jitter * (Noise() * 0.5f + 0.5f));
                        if (rotary && rpm < 2600f && Noise() < -0.4f) amp *= 0.3f;        // rotary rölanti "brap"
                        if (pLimiter > 0.5f && (cyl & 3) == 0) amp *= 0.2f;                // devir kesici sekmesi
                        env = amp * load;
                        atk = 0f;
                        if (diesel) knockEnv = 1f;
                        if (popRequest > 0 && th < 0.1f && popQueue == 0 && Noise() > 0.2f) { popRequest--; StartPop(1f); }
                    }
                    env *= decay;
                    atk = Mathf.Min(1f, atk + atkInc);
                    float pulse = env * atk * ((1f - noiseMix) + noiseMix * n);
                    dc += (pulse - dc) * 0.002f;
                    float x = pulse - dc;
                    float exh = (f1.Process(x) * 1.6f + f2.Process(x) * 0.9f + f3.Process(x) * (f3Gain * (0.6f + 0.6f * th)) + x * 0.12f) * exhaustGain;
                    crankPhase += crank * 0.5f / rate; if (crankPhase > 1.0) crankPhase -= 1.0;
                    float cp = (float)(crankPhase * twoPi);
                    float harm = Mathf.Sin(cp) * 0.12f * load + Mathf.Sin(cp * cylinders) * 0.05f;
                    if (orderHarm > 0f) harm += Mathf.Sin(cp * orderHarm * 2f) * 0.08f * load;   // I3 1.5. derece salınımı
                    lp1 += (n - lp1) * lpA;
                    float intake = lp1 * intakeAmt * th * rpm01;
                    float mech = n * 0.012f * rpm01;
                    s += exh + harm + intake + mech;
                    if (diesel && knockEnv > 0.001f) { knockEnv *= knockDecay; s += knockBp.Process(n) * knockEnv * 0.5f * (1f - 0.5f * rpm01); }
                    if (supercharger)
                    {
                        scPhase += crank * 9f / rate; if (scPhase > 1.0) scPhase -= 1.0;
                        s += Mathf.Sin((float)(scPhase * twoPi)) * 0.035f * rpm01 * (0.4f + 0.6f * th);
                    }
                }
                // ---- egzoz patlamaları: kalın gövde (60–150 Hz) + çıtırtı + yankı ----
                float popOut = 0f;
                if (popThump > 0.0005f || popCrackle > 0.0005f)
                {
                    popThumpAtk = Mathf.Min(1f, popThumpAtk + atkStep);
                    popPhase += popFreq / rate;
                    popFreq = Mathf.Max(45f, popFreq * popDrop);   // perde düşüşü: "thud"
                    popOut += Mathf.Sin((float)(popPhase * twoPi)) * popThump * popThumpAtk * popAmp;
                    crackLp += (crackleBp.Process(n) - crackLp) * crackA;   // 300–3000 Hz bandı, keskin tık yok
                    popOut += crackLp * popCrackle * popThumpAtk * popAmp * 1.1f * (popRasp > 0f ? 0.65f + 0.35f * Mathf.Sin((float)(popPhase * 3.0)) : 1f);
                    popThump *= popThumpDecay; popCrackle *= popCrackDecay;
                }
                if (popQueue > 0)
                {
                    popGap -= 1f / rate;
                    if (popGap <= 0f) { popQueue--; StartPop(Mathf.Lerp(0.6f, 1.1f, Noise() * 0.5f + 0.5f), false); }
                }
                // basit geri beslemeli gecikme (~45 ms) sadece patlamalar için
                // kısa oda yankısı: 2 tap (~45 ve ~70 ms)
                int L = delayBuf.Length;
                float d1 = delayBuf[(delayIdx + L - (int)(L * 0.64f)) % L], d2 = delayBuf[delayIdx];
                delayBuf[delayIdx] = popOut + (d1 * 0.22f + d2 * 0.15f);
                delayIdx = (delayIdx + 1) % L;
                s += (popOut + d1 * 0.28f + d2 * 0.18f) * exhaustGain;

                if (turbo && synthTurbo && synthEngine)
                {
                    whistlePhase += whistleF / rate; if (whistlePhase > 1.0) whistlePhase -= 1.0;
                    s += Mathf.Sin((float)(whistlePhase * twoPi)) * whistleAmp * sBoost;
                    if (turboSize >= 3) { whoosh += (n - whoosh) * 0.08f; s += whoosh * sBoost * 0.35f; }   // dört turbo "hava akışı"
                    if (bovEnv > 0.001f)
                    {
                        bovEnv *= bovDecay;
                        hp1 = n - hp2; hp2 = n;
                        s += hp1 * bovEnv * 0.35f;
                    }
                    if (surgeEnv > 0.001f)
                    {
                        // büyük turbo kompresör dalgalanması "stututu" (~18 Hz)
                        surgeEnv *= surgeDecay;
                        surgePhase += 18.0 / rate; if (surgePhase > 1.0) surgePhase -= 1.0;
                        float gate = surgePhase < 0.35 ? 1f : 0.1f;
                        hp1 = n - hp2; hp2 = n;
                        s += hp1 * surgeEnv * gate * 0.3f;
                    }
                }
                if (synthSqueal && pSlip > 0.01f)
                {
                    squealLfo += 7.0 / rate;
                    squealPhase += (squealF + 90f * Mathf.Sin((float)(squealLfo * twoPi))) / rate; if (squealPhase > 1.0) squealPhase -= 1.0;
                    s += (Mathf.Sin((float)(squealPhase * twoPi)) * 0.6f + Noise() * 0.15f) * pSlip * 0.22f;
                }
                if (synthWind && pSpeed01 > 0.05f)
                {
                    lp2 += (Noise() - lp2) * lpW;
                    s += lp2 * pSpeed01 * pSpeed01 * 0.9f;
                }
                if (synthGravel && pGravel > 0.01f)
                {
                    float g = gravelBp.Process(Noise());
                    s += g * pGravel * (Noise() > 0.6f ? 1.2f : 0.4f) * 0.25f;
                }
                if (synthNitro && pNitro > 0.5f)
                {
                    float nn = Noise();
                    lp3 += (nn - lp3) * 0.5f;
                    s += (nn - lp3) * 0.12f;
                }
                s *= gain * 0.8f;
                s = s / (1f + Mathf.Abs(s));
                for (int c = 0; c < channels; c++) data[i + c] *= s; // giriş: 3B zayıflatılmış DC (1.0)
            }
        }

        /// <summary>Patlama başlat (ses iş parçacığı). Bazen 2–3'lü "brap-pap-pap" dizisi kuyruğa eklenir.</summary>
        float crackLp, crackA = 0.3f;
        float popDrop = 0.9998f, popThumpDecay = 0.999f, popCrackDecay = 0.998f, popRasp;

        /// <summary>Patlama: perdesi düşen kalın "thud" (70–120 Hz, 40–90 ms) + çıtırtı (300–3000 Hz, 15–40 ms).
        /// Gaz kesince 2–4'lü "bap-bap-brrap" dizileri; sonuncusu uzun hırıltılı.</summary>
        /// <summary>Gerçek araca göre motor tipi (ses karakteri). Bilinmiyorsa "".</summary>
        public static string EngineByName(CarEntry def)
        {
            if (def == null) return "";
            string n = (" " + def.displayName + " " + def.id + " ").ToLowerInvariant();
            System.Func<string[], bool> any = ks => { foreach (var k in ks) if (n.Contains(k)) return true; return false; };
            if (any(new[] { "tesla", "taycan", "rimac", "e-tron gt", "ioniq", "model s", "model 3" })) return "EV";
            if (any(new[] { "bugatti", "chiron", "veyron" })) return "W16";
            if (any(new[] { "rx-7", "rx7", "rx-8", "rx8", "787b" })) return "ROTARY";
            if (any(new[] { "aventador", "revuelto", "812", "f12", "pagani", "zonda", "huayra", "valkyrie", "db11 v12", "vanquish", "sf90", "lfa" }))
                return n.Contains("lfa") ? "V10" : "V12";
            if (any(new[] { "huracan", "huracán", "gallardo", " r8", "carrera gt", "m5 e60", "viper" })) return "V10";
            if (any(new[] { "mustang", "camaro", "challenger", "charger", "corvette", "amg gt", "c63", "e63", "m5", "m8", "rs6", "rs7", "f8", "488", "458", "296", "roma", "720s", "750s", "p1", "senna", "gt500", "hellcat", "f-150", "ford gt", "vantage", "dbs", "cts-v", "escalade" }))
                return any(new[] { "458", "f8", "488", "720s", "750s", "senna", "p1", "gt500", "ford gt" }) ? "V8FP" : "V8";
            if (any(new[] { "911", "cayman", "boxster", "porsche" })) return "F6";
            if (any(new[] { "wrx", "impreza", "sti", "brz", "gt86", "gr86", "subaru" })) return "F4";
            if (any(new[] { "gt-r", "gtr", "r35", "nsx", "370z", "350z", "400z", " z " })) return "V6";
            if (any(new[] { "supra", "r34", "r33", "r32", "skyline", " m2", " m3", " m4", "bmw", "z4" })) return "I6";
            if (any(new[] { "rs3", "tt rs", "ttrs", "quattro" })) return "I5";
            if (any(new[] { "yaris", "gr yaris", "i8", "fiesta" })) return "I3";
            if (any(new[] { "civic", "type r", "golf", "gti", "focus", "evo", "lancer", "mini", "cooper", "megane", "i30", "a45", "corolla", "s2000", "miata", "mx-5", "integra" })) return "I4";
            return "";
        }

        /// <summary>Aracın egzoz sınıfı (satın alınmaz): 0 sessiz/ekonomik, 1 spor, 2 süper, 3 hiper/yarış.
        /// Fiyat sınıfı + motor tipi: V8/V10/V12/W16 ve rotary bir kademe daha gürültülü, dizel/elektrik 0.</summary>
        public static int ExhaustClass(CarEntry def, string eng)
        {
            if (def == null) return 0;
            string e = (eng ?? "").ToUpperInvariant();
            if (e == "EV" || e.Contains("DIESEL")) return 0;
            int lv = def.price < 40000 ? 0 : def.price < 110000 ? 1 : def.price < 400000 ? 2 : 3;
            if (e.StartsWith("V8") || e.StartsWith("V10") || e.StartsWith("V12") || e.StartsWith("W16") || e.Contains("ROTARY") || e.StartsWith("R2") || e.StartsWith("R3")) lv++;
            return Mathf.Clamp(lv, 0, 3);
        }

        /// <summary>Egzoz patlamaları (geri tepme) tamamen kapalı (kullanıcı isteği).</summary>
        public const bool PopsEnabled = false;

        void StartPop(float strength, bool chain = true)
        {
            if (!PopsEnabled) { popQueue = 0; return; }
            float r1 = Noise() * 0.5f + 0.5f, r2 = Noise() * 0.5f + 0.5f, r3 = Noise() * 0.5f + 0.5f;
            popThump = 1f; popCrackle = 0.9f; popThumpAtk = 0f;
            popFreq = 70f + 50f * r1;
            popDrop = Mathf.Exp(-1f / (sr * 0.06f) * 0.6f);
            popThumpDecay = Mathf.Exp(-1f / (sr * Mathf.Lerp(0.04f, 0.09f, r2)));
            bool last = !chain && popQueue == 0;
            popCrackDecay = Mathf.Exp(-1f / (sr * (last ? 0.06f : Mathf.Lerp(0.015f, 0.04f, r3))));
            popRasp = last ? 1f : 0f;
            crackleBp.Bandpass(Mathf.Lerp(700f, 1800f, r3), 0.9f, sr);
            crackA = 1f - Mathf.Exp(-2f * Mathf.PI * 3000f / sr);
            popAmp = strength * (0.45f + 0.3f * r2);
            popPhase = 0.0;
            popFlash = 1;
            if (chain && popQueue == 0 && r1 > 0.75f) popQueue = 1;
            popGap = Mathf.Lerp(0.06f, 0.12f, r3);
        }
    }

    /// <summary>Tek AudioSource'lu alt objede OnAudioFilterRead → EngineAudio.Render.</summary>
    public class SynthVoice : MonoBehaviour
    {
        [System.NonSerialized] public EngineAudio owner;
        void OnAudioFilterRead(float[] data, int channels)
        {
            var o = owner;
            if (o != null) o.Render(data, channels);
        }
    }

    /// <summary>Ses yönetimi: en yakın 4 YZ motoru duyulur, müzik çalar (Resources/Music).</summary>
    public class AudioDirector : MonoBehaviour
    {
        float timer;
        AudioSource music;
        AudioClip[] tracks;
        int track = -1;
        readonly List<KeyValuePair<float, EngineAudio>> tmp = new List<KeyValuePair<float, EngineAudio>>();

        void Start()
        {
            tracks = Resources.LoadAll<AudioClip>("Music");
            music = gameObject.AddComponent<AudioSource>();
            music.spatialBlend = 0f; music.loop = false; music.ignoreListenerPause = false;
        }

        void Update()
        {
            timer -= Time.unscaledDeltaTime;
            if (timer <= 0f)
            {
                timer = 0.5f;
                var g = Game.I;
                if (g != null && g.player != null)
                {
                    Vector3 p = g.player.transform.position;
                    tmp.Clear();
                    foreach (var e in EngineAudio.All) if (e != null && !e.isPlayer) tmp.Add(new KeyValuePair<float, EngineAudio>(U.FlatDist(e.transform.position, p), e));
                    tmp.Sort((a, b) => a.Key.CompareTo(b.Key));
                    for (int i = 0; i < tmp.Count; i++) tmp[i].Value.SetAudible(i < 4 && tmp[i].Key < 160f);
                }
            }
            if (music != null && tracks != null && tracks.Length > 0)
            {
                music.volume = 0.5f * AudioBus.Get(AudioBus.Bus.Muzik);
                if (!music.isPlaying && !AudioListener.pause)
                {
                    track = (track + 1 + Random.Range(0, Mathf.Max(1, tracks.Length - 1))) % tracks.Length;
                    music.clip = tracks[track];
                    music.Play();
                }
            }
        }
    }
}
