using System;
using System.Collections.Generic;
using UnityEngine;

namespace MostWanted
{
    /// <summary>Hikaye hedefi (HUD ve diğer sistemler okur).</summary>
    public static class Story
    {
        public static string CurrentObjective = "";
        public static Vector3 ObjectiveTarget;
        public static bool HasTarget;
        public static float Distance = -1f;
    }

    /// <summary>
    /// Hikaye modu ilerlemesi: Prolog (M4 tuzağı) → 5 bölüm (Kara Liste #5..#1) → Final (otoyol + son takip).
    /// Kendi kalıcı nesnesinde yaşar; hiçbir adım normal oyunu engellemez, her adım hata durumunda atlanır ve kaydedilir.
    /// Bağlantılar: Game.WelcomeToast → OnEnterWorld, RaceManager.PlayerFinished → OnRaceResult,
    /// Career.RivalBeaten → OnRivalBeaten, Career.CanChallenge/Challenge → OverridesChallenge/StartBossRace.
    /// </summary>
    public class StoryManager : MonoBehaviour
    {
        static StoryManager inst;
        public static StoryManager Get()
        {
            if (inst == null)
            {
                var go = new GameObject("Hikaye");
                DontDestroyOnLoad(go);
                inst = go.AddComponent<StoryManager>();
            }
            return inst;
        }

        public const int Prologue = 0, Finale = 6, Done = 7;

        static bool entered;
        static float enterDelay;
        public static bool ChoiceOpen { get; private set; }
        public static int choiceSel;
        /// <summary>Ara sahne / mod seçimi açık: oyuncu girişi, HUD ve oyun tuşları kapalı.</summary>
        public static bool Cinematic { get { return Cutscene.Active || ChoiceOpen; } }

        public static StorySave S { get { return SaveSystem.Data != null ? SaveSystem.Data.story : null; } }
        public static bool Enabled { get { var s = S; return s != null && s.enabled && s.chapter < Done; } }

        // çalışma zamanı (kaydedilmez)
        bool prologueRace;
        float stepTimer, radioTimer, waitFast;
        bool wasPursuit; int maxStars, maxBounty, escAtStart;
        bool armed = true;
        CutsceneDef pendingReplay;
        GameObject marker;
        World cacheWorld;
        readonly Dictionary<string, RaceDef> raceCache = new Dictionary<string, RaceDef>();
        int tauntIdx;

        /// <summary>Oyun başladığında (başlık ekranı kapanınca) bir kez çağrılır.</summary>
        public static void OnEnterWorld(Game g)
        {
            try
            {
                Get();
                entered = true;
                enterDelay = 1.2f;
            }
            catch (Exception e) { Debug.LogWarning("[MW] Hikaye başlatılamadı: " + e.Message); }
        }

        // ================================================================ güncelleme
        void Update()
        {
            try
            {
                StoryMessages.Tick(Time.unscaledDeltaTime);
                Tick();
            }
            catch (Exception e) { Debug.LogWarning("[MW] Hikaye güncellemesi: " + e.Message + "\n" + e.StackTrace); }
        }

        void Tick()
        {
            var g = Game.I;
            var s = S;
            Story.HasTarget = false; Story.Distance = -1f;
            if (g == null || g.player == null || g.world == null || s == null || !entered) { ClearObjective(); UpdateMarker(); return; }
            if (g.world != cacheWorld) { cacheWorld = g.world; raceCache.Clear(); }
            float udt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            if (enterDelay > 0f) { enterDelay -= udt; ClearObjective(); UpdateMarker(); return; }

            // mod seçimi (yeni kayıt / güncelleme sonrası ilk giriş)
            if (!s.offered)
            {
                if (g.menu == Game.Menu.None && !TitleScreen.Active && !Cutscene.Active) { if (!ChoiceOpen) { ChoiceOpen = true; choiceSel = 0; } HandleChoiceKeys(g); }
                ClearObjective(); UpdateMarker(); return;
            }
            ChoiceOpen = false;

            if (pendingReplay != null && g.menu == Game.Menu.None && !Cutscene.Active)
            {
                var r = pendingReplay; pendingReplay = null;
                if (g.race.Active || g.police.pursuit) g.Toast("Yarış veya takip sırasında sahne izlenemez.");
                else Cutscene.Play(r, null);
            }

            TrackPursuit(g, s);
            if (!Enabled) { ClearObjective(); UpdateMarker(); return; }
            if (TitleScreen.Active || g.menu != Game.Menu.None || Cutscene.Active) { UpdateMarker(); return; }

            stepTimer += Time.deltaTime;
            if (s.chapter == Prologue) TickPrologue(g, s);
            else if (s.chapter >= 1 && s.chapter <= 5) TickChapter(g, s);
            else if (s.chapter == Finale) TickFinale(g, s);

            if (Story.HasTarget) Story.Distance = U.FlatDist(g.player.transform.position, Story.ObjectiveTarget);
            HandleProvoke(g, s);
            UpdateMarker();
        }

        static void ClearObjective() { Story.CurrentObjective = ""; Story.HasTarget = false; }

        void SetObjective(string text) { Story.CurrentObjective = text ?? ""; Story.HasTarget = false; }
        void SetObjective(string text, Vector3 target) { Story.CurrentObjective = text ?? ""; Story.ObjectiveTarget = target; Story.HasTarget = true; }

        static void Step(int chapter, int step)
        {
            var s = S;
            if (s == null) return;
            if (s.chapter != chapter) s.reqMask = 0;
            s.chapter = chapter; s.step = step;
            if (inst != null) { inst.stepTimer = 0f; inst.armed = true; inst.waitFast = 0f; }
            SaveSystem.Save();
        }

        /// <summary>Ara sahne için uygun an: menü/yarış/takip yok. Hızlıyken en fazla 8 sn bekler.</summary>
        bool Free(Game g)
        {
            if (g.menu != Game.Menu.None || g.race.Active || g.police.pursuit || Cutscene.Active || TitleScreen.Active) return false;
            if (g.player.SpeedKmh > 60f) { waitFast += Time.unscaledDeltaTime; if (waitFast < 8f) return false; }
            waitFast = 0f;
            return true;
        }

        // ================================================================ prolog
        void TickPrologue(Game g, StorySave s)
        {
            switch (s.step)
            {
                case 0:
                    SetObjective("Defne seni bekliyor...");
                    if (!Free(g)) return;
                    LoanM4(g, s);
                    Step(Prologue, 0);
                    Cutscene.Play(StoryData.ProIntro, () =>
                    {
                        Step(Prologue, 1);
                        StoryMessages.Send("Kartal", "Başlangıç çizgisinde bekliyorum. Geç kalma, yeni gelen.");
                        StoryMessages.Send(StoryData.Mentor, "Haritadaki ışık sütununa git. Yarış orada.");
                    });
                    return;
                case 1:
                {
                    var def = FindRace(RaceType.Sprint, "Şehir", true);
                    if (def == null) { Sabotage(g, s); return; }
                    SetObjective("Kartal ile tanışma sprintine katıl: " + def.name, def.route[0]);
                    if (Approach(g, def.route[0]))
                    {
                        g.race.StartRace(def, 4);
                        if (g.race.Active) { prologueRace = true; Step(Prologue, 2); }
                    }
                    return;
                }
                case 2:
                    SetObjective("Kartal'ı geç!");
                    if (!g.race.Active || !prologueRace) { prologueRace = false; Step(Prologue, 1); return; }
                    if (!g.race.Counting && g.race.raceTime > 18f) Sabotage(g, s);
                    return;
                case 3:
                    SetObjective("Polisten kaç!");
                    if (stepTimer > 30f || (!g.police.pursuit && stepTimer > 2f)) Bust(g, s);
                    return;
                case 4:   // yakalanma sahnesi yarıda kaldıysa (oyundan çıkış) yeniden oynat
                    SetObjective("Yakalandın...");
                    if (Free(g)) Bust(g, s);
                    return;
                default: Step(1, 0); return;
            }
        }

        void Sabotage(Game g, StorySave s)
        {
            prologueRace = false;
            if (g.race.Active) g.race.Abort();
            Step(Prologue, 3);
            Cutscene.Play(StoryData.ProSabotage, () =>
            {
                stepTimer = 0f;
                try { g.police.ForceHeat(3); g.police.Radio(StoryData.Sergeant + ": " + "Mavi BMW! Kenara çek, hemen!"); } catch { }
            });
        }

        void Bust(Game g, StorySave s)
        {
            try { g.police.EndPursuit(false); } catch { }
            Step(Prologue, 4);
            Cutscene.Play(StoryData.ProBust, () =>
            {
                TakeM4(g, s);
                Step(1, 0);
                StoryMessages.Send(StoryData.Mentor, "Mini garajda, depo dolu. Kara Liste #5 Tilki'den başlıyoruz.");
                StoryMessages.Send("Kartal", StoryData.Rivals[4].taunts[0]);
            });
        }

        // ================================================================ bölümler 1-5
        void TickChapter(Game g, StorySave s)
        {
            var ch = StoryData.Chapters[Mathf.Clamp(s.chapter, 1, StoryData.Chapters.Length - 1)];
            var rv = StoryData.Rivals[Mathf.Clamp(ch.rival, 0, StoryData.Rivals.Length - 1)];
            switch (s.step)
            {
                case 0:
                    SetObjective(ch.title + ": " + ch.sub);
                    if (!Free(g)) return;
                    Step(s.chapter, 0);
                    int ci = s.chapter;
                    Cutscene.Play(ch.intro, () =>
                    {
                        Step(ci, 1);
                        if (ch.tips.Length > 0) StoryMessages.Send(StoryData.Mentor, ch.tips[0]);
                        StoryMessages.Send(rv.nick, rv.taunts[0]);
                    });
                    return;
                case 1:
                {
                    int i = NextReq(ch, s);
                    if (i < 0)
                    {
                        if (s.chapter == 5) { Step(Finale, 0); StoryMessages.Send(StoryData.Mentor, "Kartal'ın dikkatini çektin. Otoyola git, son düzlük seni bekliyor."); }
                        else { Step(s.chapter, 2); StoryMessages.Send(StoryData.Mentor, "Hazırsın. " + rv.nick + " seninle yarışmayı kabul etti."); StoryMessages.Send(rv.nick, rv.taunts[3 % rv.taunts.Length]); }
                        return;
                    }
                    var q = ch.reqs[i];
                    string pre = ch.title + "  " + Bits(s.reqMask, ch.reqs.Length) + "/" + ch.reqs.Length + "  •  ";
                    if (q.kind == ReqKind.Race)
                    {
                        var def = FindRace(q.type, q.keyword, false);
                        if (def == null) { MarkReq(s, i, "Bu haritada uygun yarış yok — adım atlandı."); return; }
                        SetObjective(pre + StoryData.ReqText(q, def), def.route[0]);
                        if (Approach(g, def.route[0])) g.race.StartRace(def, -1);
                    }
                    else SetObjective(pre + StoryData.ReqText(q, null) + (g.police.pursuit ? "" : "   (T: polisi kışkırt)"));
                    return;
                }
                case 2:
                {
                    var def = BossRace(ch);
                    if (def == null) { Step(s.chapter + 1, 0); return; }
                    SetObjective("KARA LİSTE #" + Career.Rivals[ch.rival].rank + ": " + rv.nick + " ile yarış  —  " + def.name, def.route[0]);
                    if (Approach(g, def.route[0])) StartBossRace(ch.rival);
                    return;
                }
                case 3:
                {
                    SetObjective(rv.nick + " yenildi!");
                    if (!Free(g)) return;
                    int c = s.chapter;
                    Cutscene.Play(ch.outro, () =>
                    {
                        Step(c + 1, 0);
                        StoryMessages.Send(StoryData.Mentor, StoryData.MentorWins[c % StoryData.MentorWins.Length]);
                    });
                    return;
                }
                default: Step(s.chapter + 1, 0); return;
            }
        }

        static int Bits(int m, int n) { int k = 0; for (int i = 0; i < n; i++) if ((m & (1 << i)) != 0) k++; return k; }

        static int NextReq(ChapterDef ch, StorySave s)
        {
            for (int i = 0; i < ch.reqs.Length; i++) if ((s.reqMask & (1 << i)) == 0) return i;
            return -1;
        }

        void MarkReq(StorySave s, int i, string note)
        {
            s.reqMask |= 1 << i;
            armed = true;
            SaveSystem.Save();
            var g = Game.I;
            if (g != null) g.Toast(string.IsNullOrEmpty(note) ? "HİKAYE: Hedef tamamlandı!" : note);
            var ch = StoryData.Chapters[Mathf.Clamp(s.chapter, 0, StoryData.Chapters.Length - 1)];
            if (ch.rival >= 0 && NextReq(ch, s) >= 0)
            {
                var rv = StoryData.Rivals[ch.rival];
                if (tauntIdx % 2 == 0) StoryMessages.Send(rv.nick, rv.taunts[1 + (tauntIdx / 2) % (rv.taunts.Length - 1)]);
                else if (ch.tips.Length > 1) StoryMessages.Send(StoryData.Mentor, ch.tips[1]);
                tauntIdx++;
            }
        }

        // ================================================================ final
        void TickFinale(Game g, StorySave s)
        {
            var boss = StoryData.Rivals[4];
            switch (s.step)
            {
                case 0:
                    SetObjective("FİNAL: Otoyola git");
                    if (!Free(g)) return;
                    Step(Finale, 0);
                    Cutscene.Play(StoryData.FinalIntro, () => { Step(Finale, 1); StoryMessages.Send("Kartal", boss.taunts[3]); });
                    return;
                case 1:
                {
                    var def = FindRace(RaceType.Circuit, "Otoyol", true) ?? FindRace(RaceType.Sprint, "Otoyol", true);
                    if (def == null) { Step(Finale, 2); return; }
                    SetObjective("FİNAL: Kartal ile otoyol yarışı  —  " + def.name, def.route[0]);
                    if (Approach(g, def.route[0])) StartBossRace(4);
                    return;
                }
                case 2:
                    SetObjective("Kartal yenildi... ama siren sesleri yaklaşıyor.");
                    if (!Free(g)) return;
                    Step(Finale, 2);
                    Cutscene.Play(StoryData.FinalPolice, () =>
                    {
                        Step(Finale, 3);
                        try { g.police.ForceHeat(5); g.police.Radio(StoryData.Sergeant + ": " + StoryData.SergeantLines[4]); } catch { }
                    });
                    return;
                case 3:
                    SetObjective("SON TAKİP: ★4+ seviyede polisten kaç" + (g.police.pursuit ? "" : "   (T: takibi başlat)"));
                    return;
                case 4:
                    SetObjective("Defne'nin garajına dön.");
                    if (!Free(g)) return;
                    Cutscene.Play(StoryData.Ending, () =>
                    {
                        ReturnM4(g, s);
                        Step(Done, 0);
                        SaveSystem.AddMoney(50000);
                        g.Toast("HİKAYE TAMAMLANDI!  M4 geri döndü  +" + U.Money(50000));
                        StoryMessages.Send(StoryData.Mentor, "M4 garajda, cilalı. Şehir senin. Serbest sürüşün tadını çıkar.");
                        StoryMessages.Send(StoryData.Sergeant, "Bu iş bitmedi. Seni izliyorum.");
                    });
                    return;
                default: Step(Done, 0); return;
            }
        }

        // ================================================================ takip / polis
        void TrackPursuit(Game g, StorySave s)
        {
            var pol = g.police;
            if (pol == null) return;
            bool p = pol.pursuit;
            if (p)
            {
                if (!wasPursuit)
                {
                    escAtStart = SaveSystem.Data.escapes; maxStars = 0; maxBounty = 0;
                    radioTimer = 22f;
                    if (Enabled && UnityEngine.Random.value < 0.75f) pol.Radio(StoryData.Sergeant + ": " + StoryData.SergeantLines[UnityEngine.Random.Range(0, StoryData.SergeantLines.Length)]);
                }
                maxStars = Mathf.Max(maxStars, pol.Stars);
                maxBounty = Mathf.Max(maxBounty, pol.bounty);
                radioTimer -= Time.deltaTime;
                if (radioTimer <= 0f && Enabled)
                {
                    radioTimer = UnityEngine.Random.Range(25f, 40f);
                    pol.Radio(StoryData.Sergeant + ": " + StoryData.SergeantLines[UnityEngine.Random.Range(0, StoryData.SergeantLines.Length)]);
                }
            }
            else if (wasPursuit) OnPursuitEnded(g, s, SaveSystem.Data.escapes > escAtStart);
            wasPursuit = p;
        }

        void OnPursuitEnded(Game g, StorySave s, bool escaped)
        {
            if (!Enabled) return;
            if (s.chapter >= 1 && s.chapter <= 5 && s.step == 1)
            {
                if (!escaped) { StoryMessages.Send(StoryData.Mentor, "Yakalandın ama önemli değil. Bir dahakine saklanma noktalarını kullan."); return; }
                var ch = StoryData.Chapters[s.chapter];
                for (int i = 0; i < ch.reqs.Length; i++)
                {
                    if ((s.reqMask & (1 << i)) != 0) continue;
                    var q = ch.reqs[i];
                    if (q.kind == ReqKind.Escape && maxStars >= q.stars) { MarkReq(s, i, "HİKAYE: ★" + q.stars + " kaçış tamam!"); break; }
                    if (q.kind == ReqKind.Bounty && maxBounty >= q.bounty) { MarkReq(s, i, "HİKAYE: " + U.Money(q.bounty) + " ödül hedefi tamam!"); break; }
                }
            }
            else if (s.chapter == Finale && s.step == 3)
            {
                if (escaped && maxStars >= 4) Step(Finale, 4);
                else if (!escaped) StoryMessages.Send(StoryData.Mentor, "Tunç seni yakaladı ama bırakmak zorunda kaldı. Tekrar dene: T ile takibi başlat.");
            }
        }

        void HandleProvoke(Game g, StorySave s)
        {
            if (!Input.GetKeyDown(KeyCode.T) || g.police.pursuit || g.race.Active || g.menu != Game.Menu.None || Cinematic) return;
            int stars = 0;
            if (s.chapter >= 1 && s.chapter <= 5 && s.step == 1)
            {
                var ch = StoryData.Chapters[s.chapter];
                int i = NextReq(ch, s);
                if (i >= 0 && ch.reqs[i].kind == ReqKind.Escape) stars = ch.reqs[i].stars;
                if (i >= 0 && ch.reqs[i].kind == ReqKind.Bounty) stars = 3;
            }
            else if (s.chapter == Finale && s.step == 3) stars = 5;
            if (stars <= 0) return;
            g.police.ForceHeat(stars);
            g.police.Radio(StoryData.Sergeant + ": " + StoryData.SergeantLines[UnityEngine.Random.Range(0, StoryData.SergeantLines.Length)]);
            g.Toast("Polis peşinde! ★" + stars);
        }

        // ================================================================ yarışlar
        /// <summary>Hedef noktasına yavaşça girince true (bir kez; uzaklaşınca yeniden kurulur).</summary>
        bool Approach(Game g, Vector3 p)
        {
            if (g.race.Active || g.police.pursuit || g.menu != Game.Menu.None) return false;
            float d = U.FlatDist(g.player.transform.position, p);
            if (d > 40f) armed = true;
            if (armed && d < 16f && g.player.SpeedKmh < 110f) { armed = false; return true; }
            return false;
        }

        /// <summary>Tür + anahtar kelime (ad veya semt) ile yarış seçer; bulunamazsa aynı türden herhangi biri; boss için sprint/tur.</summary>
        public RaceDef FindRace(RaceType type, string kw, bool boss)
        {
            var g = Game.I;
            if (g == null || g.world == null || g.world.races == null) return null;
            string key = (int)type + "|" + kw + "|" + boss;
            RaceDef cached;
            if (raceCache.TryGetValue(key, out cached) && cached != null && g.world.races.Contains(cached)) return cached;
            RaceDef best = null;
            var races = g.world.races;
            Func<RaceDef, bool> ok = r => r != null && r.route != null && r.route.Count >= 2;
            if (!string.IsNullOrEmpty(kw))
            {
                foreach (var r in races) if (ok(r) && r.type == type && r.name != null && r.name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) { best = r; break; }
                if (best == null && g.dressing != null)
                    foreach (var r in races)
                    {
                        if (!ok(r) || r.type != type) continue;
                        string dist = "";
                        try { dist = g.dressing.DistrictAt(r.route[0]) ?? ""; } catch { }
                        if (dist.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) { best = r; break; }
                    }
                if (best == null && boss)
                    foreach (var r in races) if (ok(r) && (r.type == RaceType.Sprint || r.type == RaceType.Circuit) && r.name != null && r.name.IndexOf(kw, StringComparison.OrdinalIgnoreCase) >= 0) { best = r; break; }
            }
            if (best == null) foreach (var r in races) if (ok(r) && r.type == type) { best = r; break; }
            if (best == null && boss) foreach (var r in races) if (ok(r) && (r.type == RaceType.Sprint || r.type == RaceType.Circuit)) { best = r; break; }
            raceCache[key] = best;
            return best;
        }

        RaceDef BossRace(ChapterDef ch)
        {
            var t = ch.bossType;
            if (t == RaceType.Tollbooth || t == RaceType.Speedtrap) t = RaceType.Sprint;
            return FindRace(t, ch.bossKeyword, true);
        }

        /// <summary>Kara Liste rakibiyle hikaye yarışını başlatır (rakip imza aracında, yüksek yetenekte).</summary>
        public static void StartBossRace(int rival)
        {
            var g = Game.I;
            var m = Get();
            var s = S;
            if (g == null || s == null) return;
            RaceDef def = null;
            if (s.chapter == Finale) def = m.FindRace(RaceType.Circuit, "Otoyol", true) ?? m.FindRace(RaceType.Sprint, "Otoyol", true);
            else if (s.chapter >= 1 && s.chapter <= 5) def = m.BossRace(StoryData.Chapters[s.chapter]);
            if (def == null) def = m.FindRace(RaceType.Sprint, "", true);
            if (def == null) { g.Toast("Bu haritada uygun yarış yok."); return; }
            g.race.StartRace(def, rival);
            if (g.race.Active)
            {
                var rv = StoryData.Rivals[Mathf.Clamp(rival, 0, StoryData.Rivals.Length - 1)];
                g.Toast("KARA LİSTE #" + Career.Rivals[rival].rank + " " + rv.nick + ": \"" + rv.taunts[UnityEngine.Random.Range(0, rv.taunts.Length)] + "\"");
            }
        }

        /// <summary>Hikaye modunda Kara Liste meydan okuma kuralını değiştirir. true: kural bu fonksiyonda.</summary>
        public static bool OverridesChallenge(int i, out bool can, out string why)
        {
            can = false; why = "";
            if (!Enabled) return false;
            var s = S;
            if (s.chapter >= 1 && s.chapter <= 4 && s.step == 2 && StoryData.Chapters[s.chapter].rival == i) { can = true; return true; }
            if (s.chapter == Finale && s.step == 1 && i == 4) { can = true; return true; }
            why = i < SaveSystem.Data.rivalsBeaten ? "Yenildi" : "Hikaye: " + (string.IsNullOrEmpty(Story.CurrentObjective) ? "önce bölüm hedeflerini tamamla" : Story.CurrentObjective);
            return true;
        }

        /// <summary>RaceManager.PlayerFinished çağırır. true: sonuç hikayede işlendi (normal ödül/kara liste atlanır).</summary>
        public static bool OnRaceResult(RaceDef def, int rivalIndex, int place)
        {
            try
            {
                var m = inst;
                var s = S;
                if (m == null || s == null || def == null) return false;
                if (m.prologueRace)
                {
                    m.prologueRace = false;
                    m.Sabotage(Game.I, s);
                    return true;
                }
                if (!Enabled) return false;
                if (rivalIndex >= 0)
                {
                    if (place != 1)
                    {
                        var rv = StoryData.Rivals[Mathf.Clamp(rivalIndex, 0, StoryData.Rivals.Length - 1)];
                        StoryMessages.Send(rv.nick, rv.taunts[2 % rv.taunts.Length]);
                        StoryMessages.Send(StoryData.Mentor, "Yaklaştın. Aracını garajda güçlendir ve tekrar dene.");
                    }
                    return false;
                }
                if (place == 1 && s.chapter >= 1 && s.chapter <= 5 && s.step == 1)
                {
                    var ch = StoryData.Chapters[s.chapter];
                    for (int i = 0; i < ch.reqs.Length; i++)
                        if ((s.reqMask & (1 << i)) == 0 && ch.reqs[i].kind == ReqKind.Race && ch.reqs[i].type == def.type) { m.MarkReq(s, i, "HİKAYE: " + def.name + " kazanıldı!"); break; }
                }
            }
            catch (Exception e) { Debug.LogWarning("[MW] Hikaye yarış sonucu: " + e.Message); }
            return false;
        }

        /// <summary>Career.RivalBeaten çağırır (ödül verildikten sonra).</summary>
        public static void OnRivalBeaten(int i)
        {
            try
            {
                if (!Enabled) return;
                var s = S;
                if (s.chapter >= 1 && s.chapter <= 4 && s.step == 2 && StoryData.Chapters[s.chapter].rival == i)
                {
                    Step(s.chapter, 3);
                    StoryMessages.Send(StoryData.Rivals[i].nick, "Araba senin. Anahtarları Defne'ye bıraktım.");
                }
                else if (s.chapter == Finale && s.step == 1 && i == 4) Step(Finale, 2);
            }
            catch (Exception e) { Debug.LogWarning("[MW] Hikaye rakip: " + e.Message); }
        }

        // ================================================================ M4
        void Respawn(Game g, Vector3 pos, Quaternion rot)
        {
            try { g.SpawnPlayer(pos, rot); if (g.rig != null) g.rig.Snap(); }
            catch (Exception e) { Debug.LogWarning("[MW] Hikaye araç değişimi: " + e.Message); }
        }

        void LoanM4(Game g, StorySave s)
        {
            var m4 = StoryData.M4();
            var cs = m4 != null ? SaveSystem.Get(m4.id) : null;
            if (cs == null) return;
            s.prevSelected = SaveSystem.Data.selected;
            if (!cs.owned) { cs.owned = true; s.m4Loaned = true; }
            if (SaveSystem.Data.selected != m4.id)
            {
                SaveSystem.Data.selected = m4.id;
                var t = g.player.transform;
                Respawn(g, t.position, Quaternion.LookRotation(U.Flat(t.forward).sqrMagnitude > 0.01f ? U.Flat(t.forward).normalized : Vector3.forward));
            }
            SaveSystem.Save();
        }

        void TakeM4(Game g, StorySave s)
        {
            var m4 = StoryData.M4();
            var cs = m4 != null ? SaveSystem.Get(m4.id) : null;
            if (cs != null && s.m4Loaned) { cs.owned = false; s.m4Loaned = false; }
            s.m4Taken = true;
            var mini = StoryData.Mini();
            var ms = mini != null ? SaveSystem.Get(mini.id) : null;
            if (ms != null) { ms.owned = true; SaveSystem.Data.selected = mini.id; }
            else
            {
                var ps = SaveSystem.Get(s.prevSelected);
                if (ps != null && ps.owned) SaveSystem.Data.selected = s.prevSelected;
            }
            var sel = SaveSystem.Get(SaveSystem.Data.selected);
            if (sel == null || !sel.owned) foreach (var c in SaveSystem.Data.cars) if (c.owned && Catalog.Get(c.id) != null) { SaveSystem.Data.selected = c.id; break; }
            SaveSystem.Save();
            Respawn(g, g.world.garagePos + Vector3.up * 0.5f, g.world.garageRot);
        }

        void ReturnM4(Game g, StorySave s)
        {
            var m4 = StoryData.M4();
            var cs = m4 != null ? SaveSystem.Get(m4.id) : null;
            if (cs != null) cs.owned = true;
            s.m4Taken = false;
            SaveSystem.Save();
        }

        // ================================================================ mod seçimi / menü komutları
        void HandleChoiceKeys(Game g)
        {
            if (Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.UpArrow) || Input.GetKeyDown(KeyCode.W)) choiceSel = 0;
            if (Input.GetKeyDown(KeyCode.RightArrow) || Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.DownArrow) || Input.GetKeyDown(KeyCode.S)) choiceSel = 1;
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.Space)) Choose(choiceSel == 0);
            try { if (g.player != null) g.player.locked = true; } catch { }
        }

        public static void Choose(bool story)
        {
            var s = S;
            var g = Game.I;
            if (s == null) return;
            s.offered = true;
            ChoiceOpen = false;
            if (g != null && g.player != null && !(g.race != null && g.race.Counting)) g.player.locked = false;
            if (story) StartStory(false);
            else { s.enabled = false; SaveSystem.Save(); if (g != null) g.Toast("Serbest sürüş. Hikayeyi duraklatma menüsündeki HİKAYE sayfasından başlatabilirsin."); }
        }

        /// <summary>Hikayeyi aç (restart: prologdan yeniden).</summary>
        public static void StartStory(bool restart)
        {
            var s = S;
            if (s == null) return;
            Get();
            s.offered = true;
            s.enabled = true;
            if (restart || s.chapter >= Done) { s.chapter = Prologue; s.step = 0; s.reqMask = 0; }
            if (s.chapter == Prologue && s.step > 1) s.step = 1;
            SaveSystem.Save();
            if (Game.I != null) Game.I.Toast("HİKAYE MODU: " + StoryData.Chapters[Mathf.Clamp(s.chapter, 0, 5)].title);
        }

        public static void PauseStory()
        {
            var s = S;
            if (s == null) return;
            s.enabled = false;
            SaveSystem.Save();
            ClearObjective();
        }

        public static void Replay(CutsceneDef d)
        {
            if (d == null) return;
            Get().pendingReplay = d;
            var g = Game.I;
            if (g != null && g.menu != Game.Menu.None) g.CloseMenu();
        }

        // ================================================================ dünya işareti
        void UpdateMarker()
        {
            var g = Game.I;
            bool show = Story.HasTarget && g != null && g.player != null && !Cutscene.Active && (g.race == null || !g.race.Active);
            if (!show) { if (marker != null && marker.activeSelf) marker.SetActive(false); return; }
            if (marker == null)
            {
                try
                {
                    marker = new GameObject("HikayeHedefi");
                    DontDestroyOnLoad(marker);
                    var m = U.Emissive(new Color(0.1f, 0.5f, 0.45f), new Color(0.6f, 2.4f, 2.1f));
                    var beam = U.Prim(PrimitiveType.Cylinder, "Isin", marker.transform, new Vector3(0, 40, 0), new Vector3(2.2f, 40, 2.2f), m);
                    var rd = beam.GetComponent<Renderer>();
                    if (rd != null) rd.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    var ring = U.Prim(PrimitiveType.Cylinder, "Halka", marker.transform, new Vector3(0, 0.08f, 0), new Vector3(14f, 0.02f, 14f), U.Emissive(new Color(0.05f, 0.3f, 0.28f), new Color(0.3f, 1.4f, 1.2f)));
                    var rr = ring.GetComponent<Renderer>();
                    if (rr != null) rr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    U.Icon(marker.transform, UIKit.Teal, 12f);
                }
                catch (Exception e) { Debug.LogWarning("[MW] Hikaye işareti: " + e.Message); return; }
            }
            if (!marker.activeSelf) marker.SetActive(true);
            marker.transform.position = Story.ObjectiveTarget;
            float pulse = 1f + 0.12f * Mathf.Sin(Time.unscaledTime * 3f);
            marker.transform.localScale = new Vector3(pulse, 1f, pulse);
        }

        void OnGUI()
        {
            try { StoryHud.Draw(this); }
            catch (Exception e) { Debug.LogWarning("[MW] Hikaye HUD: " + e.Message); }
        }

        void OnDestroy()
        {
            if (inst == this) inst = null;
            if (marker != null) Destroy(marker);
        }
    }
}
