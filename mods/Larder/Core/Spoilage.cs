using System;
using System.Linq;

namespace Larder
{
    // Spoilage (L3), as data and nothing of the game's: how long a food keeps, and how much faster or slower it goes off where it's kept.
    // A server setting, OFF by default (Bob, bn-1bn): with it off, nothing here is ever asked.
    //
    // Each stack carries how much of its life it has used, in seconds as it would go in a warm room under the open sky, and when that was
    // last worked out. It's worked out again only when someone looks (a chest opened, the bag checked now and then): the time since, at the
    // rate where it lay. When the life is used up, the stack turns into as much spoiled food (never quietly gone).
    //
    // What never spoils: what isn't food, Larder's preserved foods and the bread that keeps, the game's jerky, honey and jam, eggs (they hatch)
    // and anything eaten for what it does rather than for food (meads, potions).
    public enum Kind { Never, RawFish, RawMeat, Berries, Cooked, Dish, Baked, Dairy, Milk, Vegetable, Other }

    // Where a stack lies: in someone's bag, a chest out in the open, under a roof, or in a root cellar; and whether it's cold there.
    public enum Kept { Bag, Open, Roofed, Cellar }

    public static class Spoilage
    {
        public const float Day = 1800f; // (a game day, in seconds)
        public const string Spoiled = "Larder_SpoiledFood";
        // What spoiled food turns into: not eaten (a material; fit for the midden, or for a field's muck later).
        public static readonly Food Rot = new Food { Prefab = Spoiled, Title = "Spoiled food", Model = "spoiled", Weight = 0.3f, Stack = 50,
            Description = "Food left too long, gone off and mouldy. Not fit to eat." };

        // How many game days each kind keeps in the open, warm.
        public static float Days(Kind k)
        {
            switch (k)
            {
                case Kind.RawFish: return 1f;
                case Kind.Milk: return 1.5f;
                case Kind.RawMeat: return 2f;
                case Kind.Berries: case Kind.Cooked: return 3f;
                case Kind.Dish: case Kind.Other: return 4f;
                case Kind.Baked: return 5f;
                case Kind.Dairy: return 6f;
                case Kind.Vegetable: return 10f;
                default: return float.PositiveInfinity;
            }
        }
        public static float Life(Kind k) => Days(k) * Day;

        // How fast it goes where it lies (1: warm, in the open). A roof keeps the sun and rain off; a cellar is cool and dark; cold (the
        // Mountains, the Deep North, a freezing night) slows any of them.
        public static float Rate(Kept where, bool cold)
        {
            float r = where == Kept.Roofed ? 0.75f : where == Kept.Cellar ? 0.35f : 1f;
            return cold ? r * 0.4f : r;
        }

        // The life used after this many more seconds at that rate (time going back, as after a world's time is set back, counts as none).
        public static float Age(float used, double seconds, float rate) => used + (float)Math.Max(0, seconds) * Math.Max(0, rate);
        public static bool IsSpoiled(Kind k, float used) => k != Kind.Never && used >= Life(k);
        // How many game days it has left (infinite for what never spoils; 0 once spoiled).
        public static float DaysLeft(Kind k, float used) => k == Kind.Never ? float.PositiveInfinity : Math.Max(0, Life(k) - used) / Day;
        // What the tooltip says.
        public static string Freshness(Kind k, float used)
        {
            if (k == Kind.Never) return "Keeps";
            float left = DaysLeft(k, used), of = Days(k);
            string when = left >= 1.5f ? $"about {Math.Round(left)} days" : left >= 0.75f ? "about a day" : "less than a day";
            return (left > of / 2 ? "Fresh" : left > of / 5 ? "Getting old" : "Going off") + $": keeps {when} more";
        }

        // A root cellar: a roofed room dug into the ground. Where a chest stands, the ground all round (the game measures it a few metres
        // out, eight ways) stands at least this much above its floor on most sides; one or two may be open (the way down).
        public const float CellarDepth = 1f;
        public static bool IsCellar(float floor, float[] ground, bool roofed) => roofed && ground != null && ground.Length > 0 && ground.Count(h => h - floor >= CellarDepth) * 8 >= ground.Length * 6;

        // ---- what kind of food an item is, by its prefab name (the game's, Byre's, Larder's); and whether it's food at all, by the
        // game's own numbers: a food that's eaten for its health or stamina, not for a status effect ----
        private static readonly string[] Never =
        {
            "BoarJerky", "WolfJerky", "Honey", "QueensJam", "ChickenEgg", "CookedEgg", "Eyescream", "Larder_SpoiledFood",
        };
        private static readonly string[] Fish = { "FishRaw", "Fish1", "Fish2", "Fish3", "Fish4_cave", "Fish5", "Fish6", "Fish7", "Fish8", "Fish9", "Fish10", "Fish11", "Fish12", "SerpentMeat" };
        private static readonly string[] Berries = { "Raspberry", "Blueberries", "Cloudberry", "Mushroom", "MushroomYellow", "MushroomBlue", "MushroomJotunPuffs", "MushroomMagecap", "MushroomSmokePuff", "Vineberry", "Fiddleheadfern" };
        private static readonly string[] Vegetables = { "Carrot", "Turnip", "Onion", "Barley" };
        private static readonly string[] Breads = { "Bread", "VikingCupcake", "OvenPancake" };
        private static readonly string[] Milks = { Names.GoatMilk, Names.CowMilk };

        // isFood: the game's item is eaten for health or stamina (m_food > 0 or m_foodStamina > 0) and isn't a mead or potion (no status
        // effect it's eaten for). Only foods ever spoil, and raw meat and fish (the game's are materials, not eaten until cooked).
        public static Kind KindOf(string prefab, bool isFood)
        {
            if (string.IsNullOrEmpty(prefab) || Never.Contains(prefab) || Preserves.IsPreserved(prefab) || Preserves.Items.Contains(prefab)) return Kind.Never;
            if (Fish.Contains(prefab)) return Kind.RawFish;
            if (Raw(prefab)) return Kind.RawMeat;
            if (Milks.Contains(prefab)) return Kind.Milk; // (Byre's: drunk, whatever the game calls it)
            if (!isFood) return Kind.Never;
            if (prefab == Names.Butter || prefab == Names.Cheese) return Kind.Dairy;
            if (prefab == Names.Skyr) return Kind.Cooked;
            if (Berries.Contains(prefab)) return Kind.Berries;
            if (Vegetables.Contains(prefab)) return Kind.Vegetable;
            if (Breads.Contains(prefab)) return Kind.Baked;
            if (prefab.EndsWith("Uncooked")) return Kind.Other; // (a pie or platter waiting for the oven)
            if (prefab.StartsWith("Cooked") || prefab.EndsWith("Cooked") || prefab.EndsWith("Grilled") || prefab == "FishCooked" || prefab == "Sausages") return Kind.Cooked;
            if (prefab.EndsWith("Soup") || prefab.EndsWith("Stew") || prefab.EndsWith("Pie") || prefab.EndsWith("Pudding") || prefab.EndsWith("Sauce") || prefab.EndsWith("Wraps")
                || prefab.EndsWith("Platter") || prefab.EndsWith("Smoothie") || prefab.EndsWith("Salad") || prefab.EndsWith("Chicken")) return Kind.Dish;
            return Kind.Other;
        }
        // Raw meat by its name: the game's (RawMeat, DeerMeat, WolfMeat, LoxMeat, ChickenMeat, HareMeat, BugMeat, ... NeckTail), not cooked.
        private static bool Raw(string prefab) => !prefab.StartsWith("Cooked") && !prefab.Contains("Jerky") && (prefab.EndsWith("Meat") || prefab.EndsWith("Raw") || prefab == "NeckTail");
    }
}
