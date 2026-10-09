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
        volatile int bovTrigger, popRequest;
        volatile int popFlash;
        int cylinders = 4;
        float[] firePattern = { 1f }, ampPattern = { 1f };
        float formant1 = 180f, formant2 = 620f, formant3 = 1500f;
        bool turbo;
        bool synthEngine = true, synthTurbo = true, synthSqueal = true, synthWind = true, synthGravel = true, synthNitro = true;

        // DSP değişkenleri
        double firePhase, crankPhase, whistlePhase, squealPhase, squealLfo;
        float env, dc, lp1, lp2, lp3, bovEnv, popEnv, hp1, hp2;
        int cyl;
        uint rng = 2463534242u;
        Biquad f1, f2, f3, gravelBp;

        // ana iş parçacığı
        float boost, lastThrottle = 0f, popFlameTimer, surfaceTimer, thumpCd;
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
            synthSrc = gameObject.AddComponent<AudioSource>();
            synthSrc.clip = AudioSynth.DcLoop();
            synthSrc.loop = true;
            ApplySpatial(synthSrc);
            synthSrc.volume = isPlayer ? 0.55f : 0.9f;
            synthSrc.Play();

            f1.Bandpass(formant1, 1.6f, sr); f2.Bandpass(formant2, 2.2f, sr); f3.Bandpass(formant3, 3f, sr);
            gravelBp.Bandpass(900f, 0.8f, sr);
            car.onShiftAudio = (gear, q) => PlayShift();
        }

        /// <summary>Motor karakteri: silindir sayısı, ateşleme düzeni ve egzoz formantları.</summary>
        void ConfigureCharacter(CarEntry def)
        {
            string t = def != null && !string.IsNullOrEmpty(def.engineType) ? def.engineType.ToUpperInvariant() : "";
            if (t == "" && def != null)
            {
                if (def.redlineRpm >= 8400f) t = "V10";
                else if (def.torqueNm >= 600f) t = "V8";
                else if (def.torqueNm >= 450f) t = "I6";
                else t = "I4";
            }
            turbo = def != null && def.turbo;
            switch (t)
            {
                case "V8":
                    cylinders = 8; formant1 = 110f; formant2 = 340f; formant3 = 950f;
                    firePattern = new[] { 0.82f, 1.18f, 1.0f, 0.9f, 1.12f, 0.95f, 1.05f, 0.98f }; // crossplane "burble"
                    ampPattern = new[] { 1.15f, 0.8f, 1.05f, 0.9f, 1.2f, 0.85f, 1.0f, 0.95f };
                    break;
                case "V10":
                    cylinders = 10; formant1 = 260f; formant2 = 820f; formant3 = 2300f;
                    firePattern = new[] { 0.96f, 1.04f }; ampPattern = new[] { 1f, 0.92f };
                    break;
                case "F6":
                    cylinders = 6; formant1 = 210f; formant2 = 700f; formant3 = 1900f;
                    firePattern = new[] { 1f, 1f, 1f }; ampPattern = new[] { 1f, 0.95f, 1.05f };
                    break;
                case "I6":
                    cylinders = 6; formant1 = 170f; formant2 = 560f; formant3 = 1600f;
                    firePattern = new[] { 1f }; ampPattern = new[] { 1f, 0.97f };
                    break;
                default:
                    cylinders = 4; formant1 = 190f; formant2 = 650f; formant3 = 1350f;
                    firePattern = new[] { 1f, 1.02f, 0.98f, 1f }; ampPattern = new[] { 1f, 0.9f, 1.05f, 0.95f };
                    break;
            }
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
            if (turbo) boost = Mathf.MoveTowards(boost, th * Mathf.Clamp01((rpm / redline - 0.3f) / 0.5f), dt * (th > 0.5f ? 0.8f : 3f));
            if (turbo && lastThrottle > 0.6f && th < 0.2f && boost > 0.35f)
            {
                bovTrigger = 1;
                AudioClip bov; if (extra.TryGetValue("bov", out bov)) fx.PlayOneShot(bov, 0.6f * fxVol);
            }
            // geri tepme (overrun) patlamaları
            bool overrun = th < 0.1f && rpm > redline * 0.55f && kmh > 30f;
            if (overrun && lastThrottle > 0.5f) popRequest = Random.Range(2, 6);
            if (popFlash > 0)
            {
                popFlash = 0;
                popFlameTimer = 0.08f;
                List<AudioClip> pops; if (groups.TryGetValue("pop", out pops) && pops.Count > 0) fx.PlayOneShot(pops[Random.Range(0, pops.Count)], 0.7f * fxVol);
            }
            if (popFlameTimer > 0f)
            {
                popFlameTimer -= dt;
                foreach (var f in car.flames) if (f != null && !car.nitroActive) { f.gameObject.SetActive(popFlameTimer > 0f); f.localScale = new Vector3(0.12f, 0.12f, 0.45f); }
            }
            lastThrottle = th;

            // yüzey: çakıl / asfalt
            surfaceTimer -= dt;
            if (surfaceTimer <= 0f && isPlayer)
            {
                surfaceTimer = 0.25f;
                pGravel = 0f;
                WheelHit h;
                if (car.wheels[2] != null && car.wheels[2].GetGroundHit(out h) && h.collider != null)
                {
                    string n = h.collider.name.ToLowerInvariant();
                    bool asphalt = n.Contains("street") || n.Contains("otoyol") || n.Contains("yol") || n.Contains("kavsak") || n.Contains("object") || n.Contains("curb") || n.Contains("concrete") || n.Contains("pavement");
                    pGravel = asphalt ? 0f : Mathf.Clamp01(kmh / 80f);
                }
            }

            // süspansiyon darbeleri
            thumpCd -= dt;
            if (isPlayer)
                for (int i = 0; i < 4; i++)
                {
                    WheelHit h;
                    float force = car.wheels[i] != null && car.wheels[i].GetGroundHit(out h) ? h.force : 0f;
                    if (force - lastForce[i] > car.rb.mass * 9f && thumpCd <= 0f)
                    {
                        thumpCd = 0.15f;
                        List<AudioClip> th2; AudioClip c = groups.TryGetValue("thump", out th2) && th2.Count > 0 ? th2[Random.Range(0, th2.Count)] : AudioSynth.Thump();
                        fx.PlayOneShot(c, 0.5f * fxVol);
                    }
                    lastForce[i] = force;
                }

            // DSP parametreleri
            float slipDeg = Mathf.Abs(car.slipAngle);
            float slip = Mathf.Clamp01((slipDeg - 8f) / 25f) * Mathf.Clamp01(kmh / 30f);
            if (car.handbrake && kmh > 20f) slip = Mathf.Max(slip, 0.7f);
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

        void OnAudioFilterRead(float[] data, int channels)
        {
            float rate = sr;
            float rpm = pRpm, th = pThrottle, gain = pGain;
            float fire = rpm / 60f * cylinders * 0.5f;
            float crank = rpm / 60f;
            float rpm01 = Mathf.Clamp01(rpm / 8000f);
            float decay = Mathf.Exp(-1f / (rate * Mathf.Clamp(0.55f / Mathf.Max(fire, 1f), 0.0015f, 0.03f)));
            float load = 0.35f + 0.65f * th;
            float whistleF = 1800f + pBoost * 5200f;
            float bovDecay = Mathf.Exp(-1f / (rate * 0.28f));
            float popDecay = Mathf.Exp(-1f / (rate * 0.045f));
            if (bovTrigger != 0) { bovTrigger = 0; bovEnv = 1f; }
            float lpA = Mathf.Clamp01(400f / rate * 6.28f), lpW = Mathf.Clamp01(250f / rate * 6.28f);
            float squealF = 950f + pSpeed01 * 250f;

            for (int i = 0; i < data.Length; i += channels)
            {
                float s = 0f;
                if (synthEngine)
                {
                    // ateşleme darbesi
                    int pi = cyl % firePattern.Length;
                    firePhase += fire / rate / firePattern[pi];
                    if (firePhase >= 1.0)
                    {
                        firePhase -= 1.0;
                        cyl++;
                        float amp = ampPattern[cyl % ampPattern.Length] * (0.9f + 0.2f * (Noise() * 0.5f + 0.5f));
                        if (pLimiter > 0.5f && (cyl & 3) == 0) amp *= 0.2f; // devir kesici sekmesi
                        env = amp * load;
                        if (popRequest > 0 && th < 0.1f && Noise() > 0.3f) { popRequest--; popEnv = 1.6f; popFlash = 1; }
                    }
                    env *= decay;
                    float n = Noise();
                    float pulse = env * (0.55f + 0.45f * n);
                    // DC giderme
                    dc += (pulse - dc) * 0.002f;
                    float x = pulse - dc;
                    float exh = f1.Process(x) * 1.6f + f2.Process(x) * 0.9f + f3.Process(x) * (0.25f + 0.35f * th) + x * 0.25f;
                    crankPhase += crank * 0.5f / rate; if (crankPhase > 1.0) crankPhase -= 1.0;
                    float harm = Mathf.Sin((float)(crankPhase * 2.0 * System.Math.PI)) * 0.12f * load + Mathf.Sin((float)(crankPhase * 2.0 * System.Math.PI * cylinders)) * 0.05f;
                    lp1 += (n - lp1) * lpA;
                    float intake = lp1 * 0.25f * th * rpm01;
                    float mech = n * 0.012f * rpm01;
                    s += exh + harm + intake + mech;
                    // geri tepme
                    if (popEnv > 0.001f) { popEnv *= popDecay; s += popEnv * (n * 0.8f + Mathf.Sin((float)(firePhase * 40.0)) * 0.4f); }
                }
                if (turbo && synthTurbo)
                {
                    whistlePhase += whistleF / rate; if (whistlePhase > 1.0) whistlePhase -= 1.0;
                    s += Mathf.Sin((float)(whistlePhase * 2.0 * System.Math.PI)) * 0.035f * pBoost;
                    if (bovEnv > 0.001f)
                    {
                        bovEnv *= bovDecay;
                        float nn = Noise();
                        hp1 = nn - hp2; hp2 = nn; // basit yüksek geçiren
                        s += hp1 * bovEnv * 0.35f;
                    }
                }
                if (synthSqueal && pSlip > 0.01f)
                {
                    squealLfo += 7.0 / rate;
                    squealPhase += (squealF + 90f * Mathf.Sin((float)(squealLfo * 6.283))) / rate; if (squealPhase > 1.0) squealPhase -= 1.0;
                    s += (Mathf.Sin((float)(squealPhase * 6.283)) * 0.6f + Noise() * 0.15f) * pSlip * 0.22f;
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
                // yumuşak sınırlayıcı
                s *= gain * 0.8f;
                s = s / (1f + Mathf.Abs(s));
                for (int c = 0; c < channels; c++) data[i + c] *= s; // giriş: 3B zayıflatılmış DC (1.0)
            }
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
