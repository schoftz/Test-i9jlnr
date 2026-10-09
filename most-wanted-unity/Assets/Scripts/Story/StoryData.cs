using System;
using System.Collections.Generic;
using UnityEngine;

namespace MostWanted
{
    /// <summary>Hikaye modu kayıt verisi (SaveData.story). Alanlar yalnızca eklenerek genişletilir.</summary>
    [Serializable]
    public class StorySave
    {
        public bool offered;          // HİKAYE / SERBEST seçimi soruldu mu
        public bool enabled;          // hikaye modu açık mı
        public int chapter;           // 0 Prolog, 1..5 bölümler, 6 Final, 7 bitti
        public int step;              // bölüm içi adım
        public int reqMask;           // bölüm gereksinimlerinden tamamlananlar (bit)
        public bool m4Loaned;         // prolog M4'ü geçici verdi (yakalanınca geri alınır)
        public bool m4Taken;          // M4 Kartal'a gitti (finalde geri gelir)
        public string prevSelected = "";
        public List<string> msgs = new List<string>();   // "gönderen\u001Fmetin" — son 50
        public int unread;
    }

    /// <summary>Hikaye karakteri (rakip / yardımcı / polis).</summary>
    public class StoryChar
    {
        public string name, nick, bio, carId, carHint, paint, district;
        public Color color;
        public string[] taunts;
    }

    public enum ReqKind { Race, Escape, Bounty }

    /// <summary>Bölüm gereksinimi: yarış (tür + anahtar kelime/semt), kaçış (★N) veya tek takipte ödül.</summary>
    public class StoryReq
    {
        public ReqKind kind;
        public RaceType type;
        public string keyword = "";   // yarış adı veya semt
        public int stars;
        public int bounty;
        public static StoryReq R(RaceType t, string kw) { return new StoryReq { kind = ReqKind.Race, type = t, keyword = kw }; }
        public static StoryReq E(int s) { return new StoryReq { kind = ReqKind.Escape, stars = s }; }
        public static StoryReq B(int b) { return new StoryReq { kind = ReqKind.Bounty, bounty = b }; }
    }

    /// <summary>Kamera çekimi. anchor: 0 oyuncu, 1 rakip aracı (prop), 2 garaj. Ofsetler çapanın yerel ekseninde.</summary>
    public struct Shot
    {
        public int anchor;
        public Vector3 pos, pos2, look;
        public float dur;
        public string who, line;
    }

    public class CutsceneDef
    {
        public string id, title;
        public bool prop;            // rakip aracı sahneye çıkar
        public int rival = -1;       // prop için rakip indeksi (Career.Rivals)
        public List<Shot> shots = new List<Shot>();
    }

    public class ChapterDef
    {
        public string title, sub;
        public int rival = -1;                 // Career.Rivals indeksi (0 = #5)
        public StoryReq[] reqs = new StoryReq[0];
        public RaceType bossType = RaceType.Sprint;
        public string bossKeyword = "";
        public CutsceneDef intro, outro;
        public string[] tips = new string[0];  // Defne'nin SMS ipuçları
    }

    /// <summary>Tüm hikaye metni, karakterler ve bölümler (özgün; Türkçe).</summary>
    public static class StoryData
    {
        public const string Mentor = "Defne";
        public const string Sergeant = "Başçavuş Tunç";
        public const string Player = "Sen";
        public static readonly Color MentorColor = new Color(0.95f, 0.55f, 0.2f);
        public static readonly Color SergeantColor = new Color(0.3f, 0.5f, 1f);

        // Kara Liste #5 → #1 (Career.Rivals ile aynı sıra)
        public static readonly StoryChar[] Rivals =
        {
            new StoryChar { name = "Cem Alkan", nick = "Tilki", district = "Liman", carId = "car_6", carHint = "Supra", paint = "Turuncu", color = new Color(1f, 0.45f, 0.05f),
                bio = "Limanın şakacısı. Çok konuşur, kısa yoldan şaşmaz.",
                taunts = new[] { "Mini mi o? Annemin market arabası daha hızlı.", "Limanda virajlar bana selam verir, sana değil.", "Tamam, tamam. Bir kere şans eseri kazandın.", "Gel bakalım çaylak, ışıklar yanınca görüşürüz." } },
            new StoryChar { name = "Ozan Demirkol", nick = "Balyoz", district = "Sanayi", carId = "car_1", carHint = "Hellcat", paint = "Mat Siyah", color = new Color(0.55f, 0.55f, 0.6f),
                bio = "Sanayinin ağır topu. İnce iş sevmez, önüne çıkanı iter.",
                taunts = new[] { "Yolumdan çekil, yoksa seni ben çekerim.", "Sanayide fren diye bir şey yok.", "Bu V8 sesi senin kabusun olacak.", "Yan yana gelirsek kapını hatırlatırım." } },
            new StoryChar { name = "Elif Tan", nick = "Sessiz", district = "Banliyö Kuzey", carId = "car_8", carHint = "Skyline", paint = "MW Mavi", color = new Color(0.2f, 0.45f, 1f),
                bio = "Az konuşur, çok hesaplar. Her virajın ideal çizgisini bilir.",
                taunts = new[] { "Sayılar yalan söylemez. Sen yavaşsın.", "Gürültü yapanları hep dikiz aynasında görürüm.", "Hatanı bekliyorum. Hep bir tane olur.", "Sessiz ol ve izle." } },
            new StoryChar { name = "Arda Keskin", nick = "Neon", district = "Merkez", carId = "car_4", carHint = "911", paint = "Mor İnci", color = new Color(0.75f, 0.3f, 1f),
                bio = "Merkezin parlak yıldızı. Kamera sever, kaybetmeyi sevmez.",
                taunts = new[] { "Takipçilerim seni yenişimi canlı izleyecek.", "Bu şehirde ışıklar benim için yanar.", "Arabanı boyatmışsın, yine de sönüksün.", "Fotoğraf çekilmek ister misin? Arkamdan tabii." } },
            new StoryChar { name = "Volkan Sezer", nick = "Kartal", district = "Otoyol", carId = "car_2", carHint = "Chiron", paint = "Parlak Siyah", color = new Color(0.85f, 0.15f, 0.15f),
                bio = "Kara Liste'nin tepesi. Hiç temiz yarışmaz, ama hiç yakalanmaz da.",
                taunts = new[] { "M4'ün garajımda çok güzel duruyor.", "Merdiveni tırmanıyorsun. Tepede ben varım.", "Polis benim kartımı tanır. Seninkini tanımaz.", "Otoyolda görüşeceğiz. Kısa sürecek." } },
        };

        public static readonly string[] SergeantLines =
        {
            "Bu şehirde benim kurallarım geçer. Kenara çek!",
            "Yine sen! Bu sefer kaçamazsın.",
            "Tüm birimler, şüpheli araç ana caddede. Yolu kesin!",
            "Kara Liste'ymiş... Benim listemde tek bir isim var: senin.",
            "Helikopteri kaldırın. Bu gece biri nezarette yatacak.",
            "Hız sınırı süs değil, evlat!",
        };

        public static readonly string[] MentorWins =
        {
            "İşte bu! Lastikler hâlâ dumanlı.", "Temiz sürüş. Böyle devam.", "Adın konuşulmaya başladı.", "Bir adım daha yaklaştık.",
        };

        static Shot S(int anchor, float px, float py, float pz, float lx, float ly, float lz, float dur, string who, string line, float dx = 0f, float dy = 0f, float dz = 0f)
        {
            var p = new Vector3(px, py, pz);
            return new Shot { anchor = anchor, pos = p, pos2 = p + new Vector3(dx, dy, dz), look = new Vector3(lx, ly, lz), dur = dur, who = who, line = line };
        }

        static CutsceneDef C(string id, string title, bool prop, int rival, params Shot[] shots)
        {
            var c = new CutsceneDef { id = id, title = title, prop = prop, rival = rival };
            c.shots.AddRange(shots);
            return c;
        }

        // ---------------------------------------------------------------- prolog sahneleri
        public static readonly CutsceneDef ProIntro = C("pro_intro", "Prolog — Şehre Giriş", true, 4,
            S(0, -6f, 1.2f, 7f, 0f, 0.8f, 0f, 4.5f, Mentor, "Hoş geldin. M4'ün sesi limana kadar geliyor.", 3f, 0.4f, -1f),
            S(0, 3.2f, 0.9f, 4.5f, 0f, 0.7f, -1f, 4f, Mentor, "Burada sokaklar gece uyumaz. Biz de uyumayız.", -1.5f, 0f, 0f),
            S(1, 4f, 1.1f, 5.5f, 0f, 0.7f, 0f, 4.5f, "Kartal", "Yeni gelen, ha? Güzel araba. Yakışmış... şimdilik.", -2f, 0.3f, 0f),
            S(1, -3f, 1.6f, -4f, 0f, 0.8f, 0f, 4.5f, "Kartal", "Bir sprint. Kazanırsan bu şehirde adın olur."),
            S(0, 0f, 6f, -12f, 0f, 0.5f, 10f, 4.5f, Mentor, "Dikkat et. Kartal asla temiz yarışmaz.", 0f, 1.5f, -3f));

        public static readonly CutsceneDef ProSabotage = C("pro_sabotage", "Prolog — Tuzak", false, 4,
            S(0, -4.5f, 1.4f, 6f, 0f, 0.8f, 0f, 3.5f, "Kartal", "Yarışı kazanmak için hep yolda olmak gerekmez.", 1.5f, 0f, 0f),
            S(0, 0f, 22f, -18f, 0f, 0f, 6f, 4f, Sergeant, "Tüm birimler! Mavi BMW, ihbar doğrulandı. Durdurun!", 0f, 3f, 0f),
            S(0, 2.6f, 0.8f, 3.5f, 0f, 0.8f, -2f, 3.5f, Mentor, "Seni ihbar etmiş! Bas gaza, hemen!"));

        public static readonly CutsceneDef ProBust = C("pro_bust", "Prolog — Yakalandın", true, 4,
            S(0, 5f, 1.4f, 5f, 0f, 0.8f, 0f, 4.5f, Sergeant, "Yolun sonu, çaylak. Araç emniyette kalıyor.", -2f, 0f, 0f),
            S(1, -4f, 1.2f, 5f, 0f, 0.8f, 0f, 4.5f, "Kartal", "M4'ün artık benim garajımda. Teşekkürler, Başçavuş.", 2f, 0f, 0f),
            S(1, 0f, 1.8f, 7f, 0f, 0.9f, 0f, 4.5f, "Kartal", "Geri mi istiyorsun? Kara Liste'ye tırman da gel al."),
            S(2, -6f, 2f, 8f, 0f, 0.6f, 0f, 5f, Mentor, "Sana bir Mini bıraktım. Küçük ama hırslı, tıpkı senin gibi.", 2.5f, 0f, -1f),
            S(2, 0f, 4f, -10f, 0f, 0.6f, 4f, 5f, Mentor, "Planı biliyorsun: #5'ten başlıyoruz. M4'ü geri alacağız.", 0f, 1f, -2f));

        // ---------------------------------------------------------------- bölümler
        public static readonly ChapterDef[] Chapters = BuildChapters();

        static ChapterDef[] BuildChapters()
        {
            var l = new List<ChapterDef>();
            l.Add(new ChapterDef { title = "PROLOG", sub = "Tuzak", rival = 4 });

            l.Add(new ChapterDef
            {
                title = "BÖLÜM 1", sub = "Limanın Tilkisi", rival = 0,
                reqs = new[] { StoryReq.R(RaceType.Sprint, "Sahil"), StoryReq.E(2), StoryReq.R(RaceType.Speedtrap, "Liman") },
                bossType = RaceType.Sprint, bossKeyword = "Sahil",
                tips = new[] { "Tilki virajları kısa keser. Sen düzlükte nitroyu sakla.", "Polisi biraz kızdır, sonra saklan. Sokaklar adını duysun." },
                intro = C("c1_intro", "Bölüm 1 — Limanın Tilkisi", true, 0,
                    S(1, 4f, 1f, 5f, 0f, 0.7f, 0f, 4f, "Tilki", "Kartal'ın yeni oyuncağı sen misin? Mini ile mi?", -2f, 0.2f, 0f),
                    S(0, -4f, 1.3f, 5f, 0f, 0.8f, 0f, 4f, Mentor, "Tilki #5. Çok konuşur ama hızlıdır. Önce adını duyurmalısın."),
                    S(1, 0f, 1.5f, 7f, 0f, 0.8f, 0f, 4f, "Tilki", "Birkaç yarış kazan, sonra konuşuruz. Belki.", 0f, 0.5f, -2f)),
                outro = C("c1_outro", "Bölüm 1 — Sonrası", true, 0,
                    S(1, 3.5f, 1f, 5f, 0f, 0.7f, 0f, 4f, "Tilki", "Tamam, kabul. Bu Mini'de motor değil, inat var.", -1.5f, 0f, 0f),
                    S(0, -3f, 1.4f, 5f, 0f, 0.8f, 0f, 4f, Mentor, "Supra artık senin. Sıradaki: Sanayi'nin Balyoz'u."))
            });

            l.Add(new ChapterDef
            {
                title = "BÖLÜM 2", sub = "Sanayi Ağırlığı", rival = 1,
                reqs = new[] { StoryReq.R(RaceType.Circuit, "Sanayi"), StoryReq.R(RaceType.Tollbooth, ""), StoryReq.B(15000) },
                bossType = RaceType.Circuit, bossKeyword = "Sanayi",
                tips = new[] { "Balyoz yanaşmayı sever. Mesafe koy, kapına dokunmasın.", "Gişe yarışında süre her şeydir. Fren yerine çizgi seç." },
                intro = C("c2_intro", "Bölüm 2 — Sanayi Ağırlığı", true, 1,
                    S(1, 5f, 0.9f, 4f, 0f, 0.8f, 0f, 4f, "Balyoz", "Tilki'yi yenmişsin. Tilki zaten yorgundu.", -2f, 0f, 0f),
                    S(1, -2f, 0.6f, 6f, 0f, 0.6f, 0f, 4f, "Balyoz", "Burada kibarlık yok. Yoldan çekil ya da ezil."),
                    S(0, 3f, 1.4f, -5f, 0f, 0.8f, 2f, 4f, Mentor, "Balyoz #4. Gücü çok ama dönemez. Virajlar senin.")),
                outro = C("c2_outro", "Bölüm 2 — Sonrası", true, 1,
                    S(1, 4f, 1.1f, 5f, 0f, 0.7f, 0f, 4f, "Balyoz", "Hah! Seni ezemedim. Kimse ezemiyor demek.", -1.5f, 0f, 0f),
                    S(0, -3f, 1.4f, 5f, 0f, 0.8f, 0f, 4f, Mentor, "Hellcat garajda. Sırada Sessiz var. O başka bir seviye."))
            });

            l.Add(new ChapterDef
            {
                title = "BÖLÜM 3", sub = "Sessiz Hesap", rival = 2,
                reqs = new[] { StoryReq.R(RaceType.Drag, ""), StoryReq.R(RaceType.Sprint, "Banliyö"), StoryReq.E(3) },
                bossType = RaceType.Sprint, bossKeyword = "Banliyö",
                tips = new[] { "Drag'da vites zamanlaması her şey. Devri kırmızıya değdirme.", "Sessiz hata yapmaz, ama trafik yapar. Onu trafiğe sok." },
                intro = C("c3_intro", "Bölüm 3 — Sessiz Hesap", true, 2,
                    S(1, 3f, 1f, 5f, 0f, 0.7f, 0f, 4.5f, "Sessiz", "İki rakip. İki şans eseri. Ortalaman düşük.", -1f, 0.3f, 0f),
                    S(0, -4f, 1.3f, 4f, 0f, 0.8f, 0f, 4f, Mentor, "Elif, #3. Her virajı ezbere bilir. Sen ezberi boz."),
                    S(1, 0f, 1.4f, 7f, 0f, 0.8f, 0f, 4f, "Sessiz", "Kuzey'de görüşürüz. Konuşma, sür.")),
                outro = C("c3_outro", "Bölüm 3 — Sonrası", true, 2,
                    S(1, 3.5f, 1f, 5f, 0f, 0.7f, 0f, 4f, "Sessiz", "Hesabımda bir hata var. O hata sensin.", -1f, 0f, 0f),
                    S(0, -3f, 1.4f, 5f, 0f, 0.8f, 0f, 4f, Mentor, "Skyline senin! Neon'un ışıkları seni bekliyor."))
            });

            l.Add(new ChapterDef
            {
                title = "BÖLÜM 4", sub = "Merkezin Işıkları", rival = 3,
                reqs = new[] { StoryReq.R(RaceType.Circuit, "Çevre"), StoryReq.R(RaceType.Speedtrap, "Merkez"), StoryReq.E(4) },
                bossType = RaceType.Circuit, bossKeyword = "Çevre",
                tips = new[] { "Neon gösteriş için sürer. Son turda hep geniş girer.", "Dört yıldız kolay değil. Saklanma noktalarını haritada işaretle." },
                intro = C("c4_intro", "Bölüm 4 — Merkezin Işıkları", true, 3,
                    S(1, 4f, 1f, 4.5f, 0f, 0.7f, 0f, 4f, "Neon", "Kamera hazır mı? Bu yarış canlı yayında.", -2f, 0.2f, 0f),
                    S(1, -3f, 1.5f, 5f, 0f, 0.8f, 0f, 4f, "Neon", "Merkez benim sahnem. Sen figüransın."),
                    S(0, 2f, 1.4f, -5f, 0f, 0.8f, 3f, 4f, Mentor, "Neon #2. Ego büyük, araba hızlı. İkisini de yen.")),
                outro = C("c4_outro", "Bölüm 4 — Sonrası", true, 3,
                    S(1, 3.5f, 1f, 5f, 0f, 0.7f, 0f, 4f, "Neon", "Kayıt dursun! ...Bunu kimse görmedi, tamam mı?", -1.5f, 0f, 0f),
                    S(0, -3f, 1.4f, 5f, 0f, 0.8f, 0f, 4f, Mentor, "911 senin. Artık sadece Kartal kaldı."))
            });

            l.Add(new ChapterDef
            {
                title = "BÖLÜM 5", sub = "Kartalın Yuvası", rival = 4,
                reqs = new[] { StoryReq.R(RaceType.Sprint, "Şehir"), StoryReq.B(40000), StoryReq.E(4) },
                bossType = RaceType.Circuit, bossKeyword = "Otoyol",
                tips = new[] { "Kartal'ın adamları her yerde. Önce şehri bir uçtan bir uca geç.", "Ona ulaşmak için polisin gözünde büyümelisin. Ödülünü yükselt." },
                intro = C("c5_intro", "Bölüm 5 — Kartalın Yuvası", true, 4,
                    S(1, 5f, 1.2f, 5f, 0f, 0.8f, 0f, 4.5f, "Kartal", "Dördü de gitti demek. Etkileyici... biraz.", -2.5f, 0.3f, 0f),
                    S(1, 0f, 0.7f, 6f, 0f, 0.7f, 0f, 4.5f, "Kartal", "Benimle yarışmak istiyorsan, önce polisin listesinde yüksel."),
                    S(0, -3f, 1.4f, 5f, 0f, 0.8f, 0f, 4.5f, Mentor, "Hazırlan. Bu sefer tuzağı biz kuruyoruz.")),
                outro = null
            });
            return l.ToArray();
        }

        // ---------------------------------------------------------------- final
        public static readonly CutsceneDef FinalIntro = C("fin_intro", "Final — Otoyol", true, 4,
            S(1, 6f, 1.3f, 6f, 0f, 0.8f, 0f, 4.5f, "Kartal", "Otoyol. Gece. Sen ve ben. Tam istediğin gibi.", -3f, 0.3f, 0f),
            S(1, -3f, 0.7f, 5f, 0f, 0.7f, 0f, 4f, "Kartal", "Kazanırsan M4'ün senin. Kaybedersen Mini de benim."),
            S(0, 3f, 1.4f, -5f, 0f, 0.8f, 3f, 4.5f, Mentor, "Tunç da yolda, eminim. Yarıştan sonra kaçmaya hazır ol."));

        public static readonly CutsceneDef FinalPolice = C("fin_police", "Final — Son Takip", false, 4,
            S(0, 0f, 20f, -16f, 0f, 0f, 6f, 4f, Sergeant, "Kartal'ı boşverin! Asıl hedef o araç. Bütün birimler!", 0f, 3f, 0f),
            S(0, -4f, 1.3f, 5f, 0f, 0.8f, 0f, 3.5f, Mentor, "Son bir takip. Kurtul, M4 seni bekliyor!"));

        public static readonly CutsceneDef Ending = C("fin_end", "Final — Şehrin Yeni Adı", true, 4,
            S(2, -6f, 2f, 8f, 0f, 0.7f, 0f, 4.5f, Mentor, "Tunç telsizde hâlâ bağırıyor. Bu ses hiç bu kadar güzel gelmemişti.", 3f, 0.3f, -1f),
            S(1, 4f, 1.1f, 5f, 0f, 0.7f, 0f, 4.5f, "Kartal", "Anahtarlar. M4'ün... temiz teslim, söz.", -1.5f, 0f, 0f),
            S(1, -3f, 1.6f, 5f, 0f, 0.9f, 0f, 4.5f, "Kartal", "Bir gün geri geleceğim. Tepede rahat uyuma."),
            S(2, 0f, 7f, -14f, 0f, 0.5f, 6f, 6f, Mentor, "Kara Liste #1: sen. Şehir artık senin adını konuşuyor.", 0f, 2f, -4f));

        /// <summary>Tekrar izlenebilir sahneler (bölüm ilerlemesine göre kilitli).</summary>
        public static List<KeyValuePair<int, CutsceneDef>> AllCutscenes()
        {
            var l = new List<KeyValuePair<int, CutsceneDef>>();
            l.Add(new KeyValuePair<int, CutsceneDef>(0, ProIntro));
            l.Add(new KeyValuePair<int, CutsceneDef>(1, ProSabotage));
            l.Add(new KeyValuePair<int, CutsceneDef>(1, ProBust));
            for (int c = 1; c < Chapters.Length; c++)
            {
                if (Chapters[c].intro != null) l.Add(new KeyValuePair<int, CutsceneDef>(c, Chapters[c].intro));
                if (Chapters[c].outro != null) l.Add(new KeyValuePair<int, CutsceneDef>(c + 1, Chapters[c].outro));
            }
            l.Add(new KeyValuePair<int, CutsceneDef>(6, FinalIntro));
            l.Add(new KeyValuePair<int, CutsceneDef>(7, FinalPolice));
            l.Add(new KeyValuePair<int, CutsceneDef>(7, Ending));
            return l;
        }

        // ---------------------------------------------------------------- yardımcılar
        /// <summary>Katalogda id ile; yoksa görünen ad ipucu ile araç bulur (null olabilir).</summary>
        public static CarEntry FindCar(string id, string hint)
        {
            var e = Catalog.Get(id);
            if (e != null) return e;
            if (!string.IsNullOrEmpty(hint))
                foreach (var c in Catalog.All)
                    if (c.displayName != null && c.displayName.IndexOf(hint, StringComparison.OrdinalIgnoreCase) >= 0) return c;
            return null;
        }

        public static CarEntry M4() { return FindCar("car_10", "M4"); }
        public static CarEntry Mini() { var m = FindCar("car_3", "Mini"); return m ?? (Catalog.Garage.Count > 0 ? Catalog.Garage[0] : null); }

        public static PaintDef Paint(string name, Color fallback)
        {
            foreach (var p in Catalog.Paints) if (p.name == name) return p;
            return new PaintDef(name, fallback, 0.5f, 0.8f);
        }

        public static Color CharColor(string who)
        {
            if (who == Mentor) return MentorColor;
            if (who == Sergeant) return SergeantColor;
            foreach (var r in Rivals) if (r.nick == who) return r.color;
            int h = 0;
            if (who != null) foreach (char ch in who) h = h * 31 + ch;
            return Color.HSVToRGB(Mathf.Abs(h % 360) / 360f, 0.6f, 0.9f);
        }

        public static string ReqText(StoryReq q, RaceDef def)
        {
            switch (q.kind)
            {
                case ReqKind.Escape: return "★" + q.stars + " seviyede polisten kaç";
                case ReqKind.Bounty: return "Tek takipte " + U.Money(q.bounty) + " ödül topla ve kaç";
                default:
                    string[] tn = { "Sprint", "Tur", "Hız Kamerası", "Gişe", "Drag" };
                    return (def != null ? def.name : tn[(int)q.type]) + " (" + tn[(int)q.type] + ") yarışını kazan";
            }
        }
    }
}
