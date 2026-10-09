using System.Collections.Generic;
using UnityEngine;

namespace MostWanted
{
    /// <summary>Kara Liste (Blacklist) ve kilometre taşları.</summary>
    public class Career : MonoBehaviour
    {
        public class Rival
        {
            public int rank; public string name; public int races; public int bounty; public int milestones; public float skill; public int raceIndex; public int prize;
        }

        public static readonly Rival[] Rivals =
        {
            new Rival { rank = 5, name = "Tilki",   races = 2,  bounty = 10000,  milestones = 1, skill = 0.92f, raceIndex = 0, prize = 20000 },
            new Rival { rank = 4, name = "Balyoz",  races = 4,  bounty = 30000,  milestones = 2, skill = 0.96f, raceIndex = 1, prize = 35000 },
            new Rival { rank = 3, name = "Sessiz",  races = 6,  bounty = 70000,  milestones = 3, skill = 1.0f,  raceIndex = 6, prize = 50000 },
            new Rival { rank = 2, name = "Neon",    races = 8,  bounty = 130000, milestones = 5, skill = 1.04f, raceIndex = 3, prize = 80000 },
            new Rival { rank = 1, name = "Kartal",  races = 10, bounty = 250000, milestones = 7, skill = 1.08f, raceIndex = 2, prize = 120000 },
        };

        public class Milestone { public string id, text; }
        public static readonly Milestone[] Milestones =
        {
            new Milestone { id = "bounty20k", text = "Tek takipte " + U.Money(20000) + " ödül topla" },
            new Milestone { id = "cops5",     text = "Toplam 5 polis aracını devre dışı bırak" },
            new Milestone { id = "time3",     text = "3 dakika takipte kal" },
            new Milestone { id = "rb2",       text = "2 barikatı atlat" },
            new Milestone { id = "breaker1",  text = "Bir Pursuit Breaker kullan" },
            new Milestone { id = "heli",      text = "Helikopter peşindeyken kaç" },
            new Milestone { id = "heat5",     text = "5 yıldız aranma seviyesine ulaş" },
            new Milestone { id = "speed250",  text = "250 km/sa hıza ulaş" },
            new Milestone { id = "drift",     text = "Tek seferde 3000 drift puanı topla" },
        };

        public int nearMisses;
        public float driftScore, driftShow;
        float driftIdle, checkT;

        public int MilestoneCount { get { return SaveSystem.Data.milestones.Count; } }
        public int NextRival { get { return Mathf.Clamp(SaveSystem.Data.rivalsBeaten, 0, Rivals.Length); } }

        public bool CanChallenge(int i, out string why)
        {
            var d = SaveSystem.Data;
            var r = Rivals[i];
            why = "";
            bool storyCan;
            if (StoryManager.OverridesChallenge(i, out storyCan, out why)) return storyCan;   // hikaye modu kendi kuralını uygular
            if (i != NextRival) { why = i < NextRival ? "Yenildi" : "Önce sıradaki rakibi yen"; return false; }
            var list = new List<string>();
            if (d.racesWon < r.races) list.Add("Yarış " + d.racesWon + "/" + r.races);
            if (d.careerBounty < r.bounty) list.Add("Ödül " + U.Money(d.careerBounty) + "/" + U.Money(r.bounty));
            if (MilestoneCount < r.milestones) list.Add("Kilometre taşı " + MilestoneCount + "/" + r.milestones);
            why = string.Join(", ", list.ToArray());
            return list.Count == 0;
        }

        /// <summary>Rakibin imza aracı (Story/StoryData.Rivals); katalogda yoksa fiyat sırasına göre yedek.</summary>
        public CarEntry RivalCar(int i)
        {
            if (i >= 0 && i < StoryData.Rivals.Length)
            {
                var sc = StoryData.FindCar(StoryData.Rivals[i].carId, StoryData.Rivals[i].carHint);
                if (sc != null) return sc;
            }
            var g = Catalog.Garage;
            int idx = Mathf.Clamp(g.Count - 1 - (Rivals[i].rank - 1), 0, g.Count - 1);
            return g[idx];
        }

        /// <summary>Rakibin imza boyası.</summary>
        public static PaintDef RivalPaint(int i)
        {
            if (i < 0 || i >= StoryData.Rivals.Length) return Catalog.Paints[(i * 5 + 3 + Catalog.Paints.Length * 4) % Catalog.Paints.Length];
            return StoryData.Paint(StoryData.Rivals[i].paint, StoryData.Rivals[i].color);
        }

        public void Challenge(int i)
        {
            string why;
            if (!CanChallenge(i, out why)) { Game.I.Toast("Henüz meydan okuyamazsın: " + why); return; }
            if (StoryManager.Enabled) { StoryManager.StartBossRace(i); return; }
            var races = Game.I.world.races;
            if (races.Count == 0) return;
            var def = races[Mathf.Clamp(Rivals[i].raceIndex, 0, races.Count - 1)];
            if (def.type == RaceType.Tollbooth || def.type == RaceType.Speedtrap) def = races[0];
            Game.I.race.StartRace(def, i);
            Game.I.Toast("KARA LİSTE #" + Rivals[i].rank + " " + Rivals[i].name + " ile kapışma!");
        }

        public void RivalBeaten(int i)
        {
            var d = SaveSystem.Data;
            var r = Rivals[i];
            d.rivalsBeaten = Mathf.Max(d.rivalsBeaten, i + 1);
            var car = RivalCar(i);
            var cs = SaveSystem.Get(car.id);
            bool got = false;
            if (cs != null && !cs.owned) { cs.owned = true; got = true; }
            SaveSystem.AddMoney(r.prize);
            Game.I.Toast("#" + r.rank + " " + r.name + " YENİLDİ! +" + U.Money(r.prize) + (got ? "  Araç kazanıldı: " + car.displayName : cs != null && cs.owned ? "  (" + car.displayName + " zaten garajında)" : ""));
            if (i == Rivals.Length - 1) Game.I.Toast("ŞEHRİN EN ARANAN SÜRÜCÜSÜ SENSİN!");
            SaveSystem.Save();
            StoryManager.OnRivalBeaten(i);
        }

        public void OnPursuitTick(PoliceManager p)
        {
            if (p.bounty >= 20000) Award("bounty20k");
            if (p.pursuitTime >= 180f) Award("time3");
            if (p.Stars >= 5) Award("heat5");
        }

        // ---- Serbest sürüş drift puanı: açı × hız × süre × çarpan; zincir: drift bitince 1.5 sn içinde yenisi başlarsa sürer ----
        public float driftMult = 1f, driftChain;   // driftChain: 0..1 (zincir süresi göstergesi)
        float driftHold;
        /// <summary>Serbest sürüş drift puanlaması kapalı (kullanıcı isteği).</summary>
        public const bool DriftScoringEnabled = false;
        public void DriftTick(float angle, float kmh, float dt)
        {
            if (!DriftScoringEnabled) return;
            if (angle > 12f && kmh > 40f)
            {
                driftIdle = 0f;
                driftHold += dt;
                driftMult = Mathf.Min(5f, 1f + Mathf.Floor(driftHold / 2f));      // her 2 sn kesintisiz drift +1 çarpan
                driftScore += Mathf.Min(angle, 60f) * kmh * dt * 0.02f * driftMult;
            }
        }
        /// <summary>Çarpışma: zincir kopar, puan kaybolur.</summary>
        public void DriftCrash()
        {
            if (!DriftScoringEnabled || driftScore <= 0f) return;
            Game.I.Toast("Drift zinciri koptu! (" + Mathf.RoundToInt(driftScore) + " puan kayıp)");
            driftScore = 0f; driftShow = 0f; driftMult = 1f; driftHold = 0f; driftIdle = 0f;
        }
        public void AddDrift(float pts) { if (!DriftScoringEnabled) return; driftScore += pts; driftIdle = 0f; }

        void Award(string id)
        {
            var d = SaveSystem.Data;
            if (d.milestones.Contains(id)) return;
            d.milestones.Add(id);
            foreach (var m in Milestones) if (m.id == id) Game.I.Toast("KİLOMETRE TAŞI: " + m.text + "  +" + U.Money(2500));
            SaveSystem.AddMoney(2500);
        }

        void Update()
        {
            var g = Game.I;
            if (g == null || g.player == null) return;
            // drift puanı: drift bitince bankala
            driftIdle += Time.deltaTime;
            driftChain = driftScore > 0f ? Mathf.Clamp01(1f - driftIdle / 1.5f) : 0f;
            if (driftIdle > 0.4f) driftHold = Mathf.Max(0f, driftHold - Time.deltaTime * 2f);   // düzelince çarpan birikimi söner
            if (driftScore > 0f && driftIdle > 1.5f)
            {
                if (driftScore > 200f)
                {
                    int bonus = Mathf.RoundToInt(driftScore / 10f) * 5;
                    SaveSystem.AddMoney(bonus);
                    g.Toast("Drift: " + Mathf.RoundToInt(driftScore) + " puan  +" + U.Money(bonus));
                    if (driftScore >= 3000f) Award("drift");
                }
                driftShow = 0f;
                driftScore = 0f; driftMult = 1f; driftHold = 0f;
            }
            else driftShow = driftScore;

            checkT -= Time.deltaTime;
            if (checkT > 0f) return;
            checkT = 1f;
            var d = SaveSystem.Data;
            if (d.copsDisabled >= 5) Award("cops5");
            if (d.roadblocksEvaded >= 2) Award("rb2");
            if (d.breakersUsed >= 1) Award("breaker1");
            if (d.heliEscapes >= 1) Award("heli");
            if (g.player.SpeedKmh >= 250f) Award("speed250");
        }
    }
}
