using System;
using System.Collections.Generic;
using System.Linq;

namespace Jarldom
{
    // What a villager asks of the jarl: one thing at a time, chosen from who they are (traits, backstory, a friend's killer) and only ever
    // something that exists and can be reached at the world's progress (the bosses beaten). Unanswered, it lapses after ten days, no harm
    // done. A disgraced soul's ask is a deed: done, it washes the shame out.
    public enum Ask { Trophy, Goods, Visit, Kill, Relic }

    public sealed class Request
    {
        public string Id = ""; public Ask Kind; public string Target = "", Label = ""; public int Count = 1, Stars; public float X, Z;
        public int Day; public int Coins; public string Why = ""; public bool Deed;
        public void Write(Writer w)
        {
            w.Str(Id); w.Int((int)Kind); w.Str(Target); w.Str(Label); w.Int(Count); w.Int(Stars); w.Flt(X); w.Flt(Z); w.Int(Day); w.Int(Coins); w.Str(Why); w.Bool(Deed);
        }
        public static Request Read(Reader r) => new Request
        {
            Id = r.Str(), Kind = (Ask)r.Int(), Target = r.Str(), Label = r.Str(), Count = r.Int(), Stars = r.Int(), X = r.Flt(), Z = r.Flt(), Day = r.Int(), Coins = r.Int(), Why = r.Str(), Deed = r.Bool(),
        };
    }

    // A landmark the world knows of (the game's own places), with the stage it can be reached at.
    public sealed class Place { public string Key = "", Label = ""; public float X, Z; public int Stage; }

    // What the generator may know of the world: everything else is in the tables below.
    public sealed class WorldFacts
    {
        public int Stage;                                   // 0 meadows ... 6 ashlands: bosses beaten
        public Func<string, bool> Exists = _ => true;       // the prefab is in this game (a table name a game update dropped is never asked)
        public List<Place> Places = new List<Place>();      // reachable landmarks (nearest first)
        public List<(string friend, string killer)> Avenge = new List<(string, string)>(); // a friend of theirs who fell lately, to what
        public List<(float x, float z)> Stags = new List<(float, float)>(); // great stags an omen set loose, within reach (Omens)
    }

    public static class Requests
    {
        public const int Days = 10;
        public const string GreatStag = "GreatStag"; // (no prefab: a deer Omens marked; the kill is told by Omens' Api)
        public static readonly string[] Biomes = { "the Meadows", "the Black Forest", "the Swamp", "the Mountains", "the Plains", "the Mistlands", "the Ashlands" };
        // The bosses in order: the world's stage is how many of them have fallen.
        public static readonly string[] BossKeys = { "defeated_eikthyr", "defeated_gdking", "defeated_bonemass", "defeated_dragon", "defeated_goblinking", "defeated_queen" };
        public static int StageOf(Func<string, bool> hasKey) => BossKeys.Count(hasKey);

        public static readonly (string prefab, string label, int stage)[] Trophies =
        {
            ("TrophyBoar", "a boar trophy", 0), ("TrophyDeer", "a deer trophy", 0), ("TrophyNeck", "a neck trophy", 0), ("TrophyGreydwarf", "a greydwarf trophy", 0),
            ("TrophyGreydwarfBrute", "a greydwarf brute trophy", 1), ("TrophyGreydwarfShaman", "a greydwarf shaman trophy", 1), ("TrophySkeleton", "a skeleton trophy", 1), ("TrophyForestTroll", "a troll trophy", 1),
            ("TrophyDraugr", "a draugr trophy", 2), ("TrophyBlob", "a blob trophy", 2), ("TrophyLeech", "a leech trophy", 2), ("TrophyWraith", "a wraith trophy", 2), ("TrophySurtling", "a surtling trophy", 2), ("TrophyAbomination", "an abomination trophy", 2),
            ("TrophyWolf", "a wolf trophy", 3), ("TrophyHatchling", "a drake trophy", 3), ("TrophyFenring", "a fenring trophy", 3), ("TrophySGolem", "a stone golem trophy", 3), ("TrophyUlv", "an ulv trophy", 3),
            ("TrophyGoblin", "a fuling trophy", 4), ("TrophyLox", "a lox trophy", 4), ("TrophyDeathsquito", "a deathsquito trophy", 4), ("TrophyGrowth", "a growth trophy", 4), ("TrophyGoblinBrute", "a fuling berserker trophy", 4),
            ("TrophySeeker", "a seeker trophy", 5), ("TrophyTick", "a tick trophy", 5), ("TrophyGjall", "a gjall trophy", 5), ("TrophyHare", "a misthare trophy", 5), ("TrophySeekerBrute", "a seeker soldier trophy", 5),
            ("TrophyAsksvin", "an asksvin trophy", 6), ("TrophyCharredMelee", "a charred warrior trophy", 6), ("TrophyMorgen", "a morgen trophy", 6), ("TrophyVolture", "a volture trophy", 6),
        };
        // (tag: what kind of soul wants it)
        public static readonly (string prefab, string label, int count, int stage, string tag)[] Goods =
        {
            ("Raspberry", "raspberries", 20, 0, "food"), ("Honey", "honey", 10, 0, "food"), ("Mushroom", "mushrooms", 10, 0, "food"), ("Dandelion", "dandelions", 10, 0, "herb"),
            ("Flint", "flint", 20, 0, "ore"), ("Feathers", "feathers", 20, 0, "hide"), ("DeerHide", "deer hides", 10, 0, "hide"), ("LeatherScraps", "leather scraps", 20, 0, "hide"),
            ("Blueberries", "blueberries", 20, 1, "food"), ("MushroomYellow", "yellow mushrooms", 10, 1, "food"), ("Thistle", "thistle", 10, 1, "herb"), ("CarrotSeeds", "carrot seeds", 20, 1, "seed"),
            ("CopperOre", "copper ore", 20, 1, "ore"), ("TinOre", "tin ore", 20, 1, "ore"), ("TrollHide", "troll hides", 5, 1, "hide"), ("SurtlingCore", "surtling cores", 5, 1, "ore"),
            ("Bloodbag", "bloodbags", 10, 2, "food"), ("Entrails", "entrails", 10, 2, "food"), ("TurnipSeeds", "turnip seeds", 20, 2, "seed"), ("Guck", "guck", 5, 2, "herb"),
            ("IronScrap", "scrap iron", 20, 2, "ore"), ("Ooze", "ooze", 10, 2, "herb"), ("FineWood", "fine wood", 20, 2, "ore"),
            ("OnionSeeds", "onion seeds", 20, 3, "seed"), ("FreezeGland", "freeze glands", 5, 3, "herb"), ("WolfPelt", "wolf pelts", 10, 3, "hide"),
            ("Obsidian", "obsidian", 20, 3, "ore"), ("Crystal", "crystals", 10, 3, "ore"), ("SilverOre", "silver ore", 20, 3, "ore"),
            ("Barley", "barley", 20, 4, "food"), ("Cloudberry", "cloudberries", 20, 4, "food"), ("Flax", "flax", 20, 4, "seed"), ("LoxPelt", "lox pelts", 5, 4, "hide"),
            ("Needle", "needles", 10, 4, "herb"), ("BlackMetalScrap", "black metal scrap", 20, 4, "ore"), ("Tar", "tar", 10, 4, "ore"),
            ("Sap", "sap", 10, 5, "food"), ("Magecap", "magecap", 10, 5, "food"), ("JotunPuffs", "jotun puffs", 10, 5, "seed"), ("RoyalJelly", "royal jelly", 10, 5, "food"),
            ("Softtissue", "soft tissue", 10, 5, "herb"), ("Carapace", "carapace", 10, 5, "hide"), ("YggdrasilWood", "yggdrasil wood", 20, 5, "ore"), ("BlackMarble", "black marble", 20, 5, "ore"),
            ("Vineberry", "vineberries", 20, 6, "food"), ("Fiddleheadfern", "fiddlehead ferns", 10, 6, "food"), ("AskHide", "asksvin hides", 5, 6, "hide"), ("CharredBone", "charred bones", 20, 6, "herb"), ("Grausten", "grausten", 20, 6, "ore"),
        };
        public static readonly (string prefab, string label, int stage)[] Relics =
        {
            ("Amber", "a piece of amber", 0), ("AmberPearl", "an amber pearl", 1), ("Ruby", "a ruby", 1), ("SilverNecklace", "a silver necklace", 2),
            ("GoblinTotem", "a fuling totem", 4), ("BlackCore", "a black core", 5), ("MoltenCore", "a molten core", 6),
        };
        // (stars: kills of a creature at the world's own stage want one star; of an easier land, two)
        public static readonly (string prefab, string label, int stage)[] Creatures =
        {
            ("Boar", "boar", 0), ("Neck", "neck", 0), ("Greydwarf", "greydwarf", 0),
            ("Greydwarf_Elite", "greydwarf brute", 1), ("Greydwarf_Shaman", "greydwarf shaman", 1), ("Skeleton", "skeleton", 1), ("Troll", "troll", 1),
            ("Draugr", "draugr", 2), ("Blob", "blob", 2), ("Leech", "leech", 2), ("Wraith", "wraith", 2), ("Draugr_Elite", "draugr elite", 2),
            ("Wolf", "wolf", 3), ("Hatchling", "drake", 3), ("Fenring", "fenring", 3), ("StoneGolem", "stone golem", 3),
            ("Goblin", "fuling", 4), ("Lox", "lox", 4), ("Deathsquito", "deathsquito", 4), ("GoblinBrute", "fuling berserker", 4),
            ("Seeker", "seeker", 5), ("Tick", "tick", 5), ("SeekerBrute", "seeker soldier", 5),
            ("Asksvin", "asksvin", 6), ("Charred_Melee", "charred warrior", 6), ("Morgen", "morgen", 6), ("Volture", "volture", 6),
        };
        // The world's places worth a journey: by the start of their prefab name (the game's own location names), the stage they're for.
        public static readonly (string prefix, string label, int stage)[] Landmarks =
        {
            ("Eikthyrnir", "the altar of Eikthyr", 0), ("ShipWreck", "a shipwreck", 0), ("WoodHouse", "an abandoned house", 0), ("Runestone_Meadows", "a runestone in the Meadows", 0),
            ("GDKing", "the stones where the Elder is called", 1), ("Crypt", "a burial chamber", 1), ("TrollCave", "a troll cave", 1), ("Vendor_BlackForest", "Haldor the trader", 1), ("Runestone_BlackForest", "a runestone in the Black Forest", 1), ("StoneTowerRuins", "a ruined stone tower", 1),
            ("Bonemass", "the altar of Bonemass", 2), ("SunkenCrypt", "a sunken crypt", 2), ("SwampHut", "a hut in the swamp", 2), ("Runestone_Swamps", "a runestone in the Swamp", 2), ("Hildir_camp", "Hildir's camp", 2), ("BogWitch_Camp", "the bog witch", 2),
            ("Dragonqueen", "the altar of Moder", 3), ("MountainCave", "a frost cave", 3), ("DrakeNest", "a drake nest", 3), ("Runestone_Mountains", "a runestone in the Mountains", 3),
            ("GoblinKing", "the altar of Yagluth", 4), ("GoblinCamp", "a fuling village", 4), ("StoneHenge", "the giants' standing stones", 4), ("TarPit", "a tar pit", 4), ("Runestone_Plains", "a runestone in the Plains", 4),
            ("Mistlands_DvergrBossEntrance", "the queen's sealed door", 5), ("Mistlands_DvergrTownEntrance", "an infested mine", 5), ("Mistlands_Giant", "a giant's bones", 5),
        };
        public static (string prefix, string label, int stage)? Landmark(string prefab) =>
            Landmarks.Where(l => prefab != null && prefab.StartsWith(l.prefix, StringComparison.Ordinal)).Select(l => ((string, string, int)?)l).FirstOrDefault();

        // A soul's home land, read from their backstory.
        public static int HomeStage(string story)
        {
            string s = (story ?? "").ToLowerInvariant();
            if (Any(s, "snow", "mountain", "glacier", "blizzard", "rockslide", "summer pasture", "fjord")) return 3;
            if (Any(s, "marsh", "mere", "mound")) return 2;
            if (Any(s, "birch woods", "pine", "bear")) return 1;
            if (Any(s, "harvest", "field", "hill-fort")) return 4;
            return 0;
        }
        private static bool Any(string s, params string[] words) => words.Any(s.Contains);

        // A new ask, or null when nothing fits. deed: a disgraced soul's (always something, and an easy one).
        public static Request Generate(VillagerState v, WorldFacts w, int day, Random rng, bool deed = false)
        {
            int stage = Math.Max(0, Math.Min(6, w.Stage));
            var options = new List<(double weight, Func<Request> make)>();
            int home = HomeStage(v.Story);
            int top = deed ? Math.Max(0, stage - 1) : stage; // (a deed is never the hardest thing in the world)
            bool T(string trait) => v.Has(trait);

            var trophies = Trophies.Where(t => t.stage <= top && w.Exists(t.prefab)).ToList();
            if (trophies.Count > 0) options.Add((T("keeneye") ? 3 : 1, () =>
            {
                var t = Pick(rng, trophies, x => x.stage == top ? 2 : 1);
                int n = t.stage < stage - 1 ? 3 : 1;
                return new Request { Kind = Ask.Trophy, Target = t.prefab, Label = t.label, Count = n, Coins = Coins(t.stage, n > 1), Why = Pick(rng, TrophyWhy) };
            }));

            var goods = Goods.Where(g => g.stage <= top && w.Exists(g.prefab)).ToList();
            if (goods.Count > 0)
            {
                double gw = 1 + (T("glutton") || T("greenthumb") || T("strong") ? 2 : 0);
                options.Add((gw, () =>
                {
                    var g = Pick(rng, goods, x => (T("glutton") && x.tag == "food" ? 4 : 1) * (T("greenthumb") && x.tag == "seed" ? 4 : 1) * (T("strong") && x.tag == "ore" ? 3 : 1) * (x.stage == home ? 3 : 1) * (x.stage == top ? 2 : 1));
                    string why = g.stage == home && home > 0 ? $"I grew up with {g.label} from {Biomes[g.stage]}; I miss the taste of home." : Pick(rng, GoodsWhy);
                    if (g.stage == home && home > 0 && g.tag != "food") why = $"Where I come from, {Biomes[g.stage].Substring(4)}-folk always kept {g.label} by the door.";
                    return new Request { Kind = Ask.Goods, Target = g.prefab, Label = g.label, Count = g.count, Coins = Coins(g.stage, false), Why = why };
                }));
            }

            var relics = Relics.Where(r => r.stage <= top && w.Exists(r.prefab)).ToList();
            bool stones = (v.Story ?? "").Contains("pretty stone");
            if (relics.Count > 0) options.Add(((T("greedy") ? 3 : 0.6) + (T("pious") ? 1 : 0) + (stones ? 3 : 0), () =>
            {
                var r = Pick(rng, relics, x => x.stage == top ? 2 : 1);
                return new Request { Kind = Ask.Relic, Target = r.prefab, Label = r.label, Coins = Coins(r.stage, false) + 10, Why = stones ? "I keep every pretty stone I find. This one I'd treasure most of all." : Pick(rng, RelicWhy) };
            }));

            var places = w.Places.Where(p => p.Stage <= top).Take(6).ToList();
            if (places.Count > 0) options.Add((T("pious") ? 2.5 : (v.Story ?? "").Contains("ravens") ? 2 : 0.8, () =>
            {
                Place p = places[rng.Next(places.Count)];
                return new Request { Kind = Ask.Visit, Target = p.Key, Label = p.Label, X = p.X, Z = p.Z, Coins = Coins(p.Stage, false), Why = Pick(rng, VisitWhy) };
            }));

            if (!T("coward"))
            {
                var foes = Creatures.Where(c => c.stage <= top && w.Exists(c.prefab)).ToList();
                // A friend's killer, when it's one of these: avenge them (the strongest pull there is).
                var avenge = w.Avenge.Select(a => (a.friend, foe: foes.FirstOrDefault(c => string.Equals(c.label, a.killer, StringComparison.OrdinalIgnoreCase)))).Where(a => a.foe.prefab != null).ToList();
                if (avenge.Count > 0 && !deed) options.Add((6, () =>
                {
                    var a = avenge[rng.Next(avenge.Count)];
                    return new Request { Kind = Ask.Kill, Target = a.foe.prefab, Label = a.foe.label, Stars = 0, Coins = Coins(a.foe.stage, false) + 10, Why = $"{Cap(A(a.foe.label))} took {a.friend} from us. Kill one for {a.friend}'s sake." };
                }));
                // A great stag an omen set loose: a hunt worth a song, while one runs.
                if (w.Stags.Count > 0) options.Add((T("brave") || T("keeneye") ? 3 : 1.2, () =>
                {
                    var at = w.Stags[rng.Next(w.Stags.Count)];
                    return new Request { Kind = Ask.Kill, Target = GreatStag, Label = "the great stag", X = at.x, Z = at.z, Coins = 40, Why = Pick(rng, StagWhy) };
                }));
                if (foes.Count > 0) options.Add((T("brave") ? 3 : 0.8, () =>
                {
                    var f = Pick(rng, foes, x => x.stage == top ? 2 : 1);
                    int stars = deed ? 1 : f.stage < stage - 1 ? 2 : 1;
                    return new Request { Kind = Ask.Kill, Target = f.prefab, Label = f.label, Stars = stars, Coins = Coins(f.stage, stars > 1) + 5, Why = Pick(rng, KillWhy) };
                }));
            }
            if (options.Count == 0) return null;
            double sum = options.Sum(o => o.weight), roll = rng.NextDouble() * sum;
            Request made = null;
            foreach (var o in options) { roll -= o.weight; if (roll <= 0) { made = o.make(); break; } }
            made = made ?? options[options.Count - 1].make();
            made.Day = day; made.Deed = deed; made.Id = rng.Next(1 << 30).ToString("x");
            if (deed) made.Why = Pick(rng, DeedWhy);
            return made;
        }
        private static int Coins(int stage, bool more) => (10 + 10 * stage) * (more ? 2 : 1);
        private static T Pick<T>(Random rng, IList<T> list, Func<T, double> weight)
        {
            double sum = list.Sum(weight), roll = rng.NextDouble() * sum;
            foreach (T x in list) { roll -= weight(x); if (roll <= 0) return x; }
            return list[list.Count - 1];
        }
        private static string Pick(Random rng, string[] lines) => lines[rng.Next(lines.Length)];

        public static bool Expired(Request r, int day) => r != null && day >= r.Day + Days;
        public static int DaysLeft(Request r, int day) => r == null ? 0 : Math.Max(0, r.Day + Days - day);

        // What they ask, in their own words ("Bring me ...").
        public static string Describe(Request r)
        {
            switch (r.Kind)
            {
                case Ask.Trophy: return r.Count > 1 ? $"Bring me {Num(r.Count)} {Plural(r.Label)}." : $"Bring me {r.Label}.";
                case Ask.Goods: return $"Bring me {r.Count} {r.Label}.";
                case Ask.Relic: return $"Bring me {r.Label}.";
                case Ask.Visit: return $"Go to {r.Label} and tell me what you saw.";
                case Ask.Kill: return r.Stars <= 0 ? $"Kill {A(r.Label)}." : $"Kill {A(r.Label)} of {Num(r.Stars)} {(r.Stars == 1 ? "star" : "stars")} or more.";
            }
            return "";
        }
        // The short form, for lists: "2 wolf trophies", "visit a burial chamber".
        public static string Short(Request r)
        {
            switch (r.Kind)
            {
                case Ask.Trophy: return r.Count > 1 ? $"{r.Count} {Plural(r.Label)}" : r.Label;
                case Ask.Goods: return $"{r.Count} {r.Label}";
                case Ask.Relic: return r.Label;
                case Ask.Visit: return "a visit to " + r.Label;
                case Ask.Kill: return $"{A(r.Label)} killed{(r.Stars > 0 ? $" ({new string('★', r.Stars)})" : "")}";
            }
            return "";
        }
        public static string A(string noun) => noun.StartsWith("the ") ? noun : ("aeiou".IndexOf(char.ToLowerInvariant(noun.FirstOrDefault())) >= 0 ? "an " : "a ") + noun;
        private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
        private static string Num(int n) => Stories.Count(n).ToLowerInvariant();
        private static string Plural(string label) // "a wolf trophy" → "wolf trophies"
        {
            string s = label.StartsWith("a ") ? label.Substring(2) : label.StartsWith("an ") ? label.Substring(3) : label;
            return (s.EndsWith("y") ? s.Substring(0, s.Length - 1) + "ies" : s + "s");
        }
        // Brought or done in the world (a hand-in needs the things themselves).
        public static bool HandIn(Request r) => r.Kind == Ask.Trophy || r.Kind == Ask.Goods || r.Kind == Ask.Relic;

        // What answering it earns: their opinion of you, worth on their road to Valhalla, coins from their own hoard (if they have them).
        public const int Opinion = 25;
        public static int CoinsFromHoard(Request r, int hoard) => Math.Max(0, Math.Min(hoard, r.Coins));

        // A kill counts when it's the creature asked for, starred enough (level is the game's: stars + 1).
        public static bool KillCounts(Request r, string prefab, int level) => r.Kind == Ask.Kill && prefab == r.Target && level - 1 >= r.Stars;

        private static readonly string[] TrophyWhy =
        {
            "I want it over my bed, so the others know what's out there.", "My father kept one over the door. I never had my own.", "The skald wants a new tale; a trophy would start one.",
            "Hang it in the hall and see who boasts then.", "I'd like to look one in the eye without it biting.", "The young ones don't believe me. A trophy would.",
        };
        private static readonly string[] GoodsWhy =
        {
            "The stores never have enough, and I'm tired of going without.", "I've a thing in mind to make, and I'm short of it.", "It's been long since I had any, and I miss it.",
            "A small thing, but it would mean a lot.", "My grandmother swore by it. I'd like to see if she was right.", "I promised someone. Help me keep the promise.",
        };
        private static readonly string[] RelicWhy =
        {
            "The old dead keep such things. They won't miss one.", "A thing like that would bring luck to the hall.", "I'd wear it at the next feast and be the envy of everyone.",
            "I had one before I fell. It's the one thing I miss.", "Odin likes a bright thing. So do I.",
        };
        private static readonly string[] VisitWhy =
        {
            "I've heard of the place since I was small. Is it true what they say?", "I dreamt of it three nights running. Go and see, and tell me.", "My kin spoke of it. I want to know it's still there.",
            "Someone should see it and come back to tell of it. Why not you?", "The ravens keep going that way. Find out why.",
        };
        private static readonly string[] KillWhy =
        {
            "One of those came near the colony and looked at me. I won't forget it.", "Prove to me the jarl is the strongest of us.", "The skald needs a better tale than the last.",
            "It's a deed worth a song. I'd like to hear it sung.", "Do it, and I'll drink to you at the next feast.",
        };
        private static readonly string[] StagWhy =
        {
            "The ravens set a great stag loose. Bring it down, and the hall will sing of it for a generation.",
            "A stag that size is the gods' own quarry. Hunt it, jarl, before it's gone.",
            "My grandfather saw a stag like that once, and never caught it. Do what he couldn't.",
            "They say the great stag's antlers touch the branches. I want to hear how it fell.",
        };
        private static readonly string[] DeedWhy =
        {
            "I've shamed myself before all of you. Do this for me and I can hold my head up again.", "There's ash on me. A deed done in my name might wash it off.",
            "I can't face the hall like this. Help me make it right.", "They whisper about me. Give them something else to talk about.", "Let it be said the jarl stood by me when I fell low.",
        };
    }
}
