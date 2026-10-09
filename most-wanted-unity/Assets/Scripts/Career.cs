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
            new Rival { rank = 5, name = "Kobra",  races = 2,  bounty = 10000,  milestones = 1, skill = 0.92f, raceIndex = 0, prize = 20000 },
            new Rival { rank = 4, name = "Gölge",  races = 4,  bounty = 30000,  milestones = 2, skill = 0.96f, raceIndex = 1, prize = 35000 },
            new Rival { rank = 3, name = "Baron",  races = 6,  bounty = 70000,  milestones = 3, skill = 1.0f,  raceIndex = 6, prize = 50000 },
            new Rival { rank = 2, name = "Duman",  races = 8,  bounty = 130000, milestones = 5, skill = 1.04f, raceIndex = 3, prize = 80000 },
            new Rival { rank = 1, name = "Kral",   races = 10, bounty = 250000, milestones = 7, skill = 1.08f, raceIndex = 2, prize = 120000 },
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
            if (i != NextRival) { why = i < NextRival ? "Yenildi" : "Önce sıradaki rakibi yen"; return false; }
            var list = new List<string>();
            if (d.racesWon < r.races) list.Add("Yarış " + d.racesWon + "/" + r.races);
            if (d.careerBounty < r.bounty) list.Add("Ödül " + U.Money(d.careerBounty) + "/" + U.Money(r.bounty));
            if (MilestoneCount < r.milestones) list.Add("Kilometre taşı " + MilestoneCount + "/" + r.milestones);
            why = string.Join(", ", list.ToArray());
            return list.Count == 0;
        }

        public CarEntry RivalCar(int i)
        {
            var g = Catalog.Garage;
            int idx = Mathf.Clamp(g.Count - 1 - (Rivals[i].rank - 1), 0, g.Count - 1);
            return g[idx];
        }

        public void Challenge(int i)
        {
            string why;
            if (!CanChallenge(i, out why)) { Game.I.Toast("Henüz meydan okuyamazsın: " + why); return; }
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
            Game.I.Toast("#" + r.rank + " " + r.name + " YENİLDİ! +" + U.Money(r.prize) + (got ? "  Araç kazanıldı: " + car.displayName : ""));
            if (i == Rivals.Length - 1) Game.I.Toast("ŞEHRİN EN ARANAN SÜRÜCÜSÜ SENSİN!");
        }

        public void OnPursuitTick(PoliceManager p)
        {
            if (p.bounty >= 20000) Award("bounty20k");
            if (p.pursuitTime >= 180f) Award("time3");
            if (p.Stars >= 5) Award("heat5");
        }

        public void AddDrift(float pts) { driftScore += pts; driftIdle = 0f; }

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
            if (driftScore > 0f && driftIdle > 1.2f)
            {
                if (driftScore > 200f)
                {
                    int bonus = Mathf.RoundToInt(driftScore / 10f) * 5;
                    SaveSystem.AddMoney(bonus);
                    g.Toast("Drift: " + Mathf.RoundToInt(driftScore) + " puan  +" + U.Money(bonus));
                    if (driftScore >= 3000f) Award("drift");
                }
                driftShow = 0f;
                driftScore = 0f;
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
