using System;
using System.Collections.Generic;
using System.Linq;

namespace Jarldom
{
    // Expectations rise: what a colony of its standing wants on the table now and then. Each dish has a tier, by the land its makings
    // come from (Meadows 0 ... Ashlands 6); a rank wants dishes of a tier, but never past what the world has opened up (the bosses
    // beaten), and only now and then (one meal in four), not every meal. Pure.
    public static class Fare
    {
        public static readonly string[] Lands = { "Meadows", "Black Forest", "Swamp", "Mountains", "Plains", "Mistlands", "Ashlands" };
        public static readonly string[] Examples =
        {
            "cooked meat, raspberries, honey",
            "carrot soup, deer stew, queen's jam",
            "sausages, turnip stew, black soup",
            "onion soup, wolf skewers, eyescream",
            "bread, lox pie, fish wraps",
            "meat platter, honey-glazed chicken, misthare supreme",
            "piquant pie, scorching medley, mashed meat",
        };
        private static readonly Dictionary<string, int> Tier = new Dictionary<string, int>();
        static Fare()
        {
            void T(int t, params string[] names) { foreach (string n in names) Tier[n] = t; }
            T(0, "Raspberry", "Mushroom", "Honey", "CookedMeat", "CookedDeerMeat", "NeckTailGrilled", "FishCooked");
            T(1, "Blueberries", "Carrot", "CarrotSoup", "MushroomYellow", "QueensJam", "BoarJerky", "DeerStew", "MinceMeatSauce");
            T(2, "Turnip", "TurnipStew", "Sausages", "BlackSoup", "ShocklateSmoothie", "SerpentMeatCooked", "SerpentStew");
            T(3, "Onion", "OnionSoup", "WolfMeatSkewer", "WolfJerky", "EyeScream", "CookedWolfMeat", "MushroomBlue");
            T(4, "Bread", "LoxPie", "FishWraps", "BloodPudding", "CookedLoxMeat", "Cloudberry");
            T(5, "MeatPlatter", "HoneyGlazedChicken", "MisthareSupreme", "Salad", "MushroomOmelette", "MagicallyStuffedShroom", "YggdrasilPorridge",
                "SeekerAspic", "CookedHareMeat", "CookedBugMeat", "CookedEgg", "FishAndBread", "CookedChickenMeat", "MushroomJotunPuffs", "MushroomMagecap");
            T(6, "MashedMeat", "PiquantPie", "RoastedCrustPie", "ScorchingMedley", "SizzlingBerryBroth", "SparklingShroomshake", "MarinatedGreens",
                "CookedAsksvinMeat", "CookedVoltureMeat", "CookedBoneMawSerpentMeat", "Vineberry", "FierySvinstew", "SpicyMarmalade", "Fiddleheadfern");
        }
        // A dish's tier: the vanilla ones by name, any other (another mod's) by how much it feeds.
        public static int Of(string prefab, float food)
        {
            if (prefab != null && Tier.TryGetValue(prefab, out int t)) return t;
            return food < 25 ? 0 : food < 35 ? 1 : food < 45 ? 2 : food < 55 ? 3 : food < 65 ? 4 : food < 78 ? 5 : 6;
        }
        // The lands the world has opened up: one past each boss beaten, in order.
        public static readonly string[] Keys = { "defeated_eikthyr", "defeated_gdking", "defeated_bonemass", "defeated_dragon", "defeated_goblinking", "defeated_queen" };
        public static int Opened(Func<string, bool> hasKey)
        {
            int open = 0;
            for (int i = 0; i < Keys.Length; i++) if (hasKey(Keys[i])) open = i + 1;
            return open;
        }
        // What a rank wants now and then, at most what's open: a camp and a homestead eat what they can, a hold wants Mistlands dishes.
        public static int Wanted(Rank rank, int opened) => Math.Min(opened, rank switch { Rank.Camp => 0, Rank.Homestead => 0, Rank.Hamlet => 1, Rank.Village => 2, Rank.Jarldom => 4, _ => 5 });
        public const int Window = 8, Share = 4; // (of the last eight meals, one in four at least)
        // Fed as their standing wants: nothing's wanted, too few meals yet to tell, or enough of the recent ones were of the tier.
        public static bool Met(IList<int> recentTiers, int wanted)
        {
            if (wanted <= 0) return true;
            var last = recentTiers.Skip(Math.Max(0, recentTiers.Count - Window)).ToList();
            if (last.Count < Share) return true;
            return last.Count(t => t >= wanted) * Share >= last.Count;
        }
        // How much a meal fills them: one well below what's wanted a little less (never under 70%).
        public static float Filling(int tier, int wanted) => wanted <= tier ? 1f : Math.Max(0.7f, 1f - 0.1f * (wanted - tier));
        public static string Wants(int wanted) => $"{Lands[wanted]} dishes now and then ({Examples[wanted]})";
    }
}
