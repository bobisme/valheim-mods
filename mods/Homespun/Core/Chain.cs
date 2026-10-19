using System;
using System.Collections.Generic;
using System.Linq;

namespace Homespun
{
    // The whole of cloth-making, as data and nothing of the game's: what each step takes, gives and how long it is, so tests can walk
    // the chain from a garment back to the field, and the game side only carries it out.
    //
    //   Flax ─ retting pool (a day's soak for a full pool) ─▶ retted flax ─ flax brake ─▶ line flax ─ spindle / spinning wheel ─▶ 2 linen thread
    //   Lox pelt ─ flax brake's combs ─▶ 4 combed wool        Byre's fleece (ByreWool), or combed wool ─ spindle / wheel ─▶ wool yarn
    //   thread, yarn ─ loom ─▶ bolts of linen and wool, tablet-woven bands ─ sewing table ─▶ garments (and Hildir's clothes)
    //
    //   Nettle (wild in meadow clearings and at the forest's edge, or sown) ─ pool ─▶ retted nettle ─ brake ─▶ line nettle ─ spindle / wheel ─▶ 1 linen thread
    //
    // The game's own spinning wheel still spins raw flax into one thread, as it always has; retted and combed, a flax gives two.
    // Nettle is the cloth before flax: it grows from the start, but its fibre is shorter and coarser, so a nettle gives half a flax's thread.
    public static class Names
    {
        // Stations
        public const string Pool = "Homespun_RettingPool", Brake = "Homespun_FlaxBrake", Loom = "Homespun_Loom", Table = "Homespun_SewingTable";
        public const string Wheel = "piece_spinningwheel"; // (the game's: it takes our fibres too)
        public const string Spindle = "Homespun_Spindle";  // (an item: spun in the hand)
        // Fibres, thread and cloth
        public const string RettedFlax = "Homespun_RettedFlax", LineFlax = "Homespun_LineFlax", Wool = "Homespun_Wool", Yarn = "Homespun_WoolYarn";
        public const string Nettle = "Homespun_Nettle", RettedNettle = "Homespun_RettedNettle", LineNettle = "Homespun_LineNettle";
        // Nettle growing: wild (grows back), the field crop (picked, gone), and the cultivator's sapling (Plants.cs)
        public const string WildNettle = "Homespun_Pickable_Nettle", NettleCrop = "Homespun_Pickable_NettleCrop", NettleSapling = "Homespun_sapling_nettle";
        public const string LinenBolt = "Homespun_LinenBolt", WoolBolt = "Homespun_WoolBolt", Band = "Homespun_Band";
        // Garments
        public const string Undertunic = "Homespun_LinenUndertunic", Tunic = "Homespun_WoolTunic", Dress = "Homespun_WoolDress", Trousers = "Homespun_WoolTrousers", Cloak = "Homespun_HoodedCloak";
        // The game's (and Byre's) that the chain uses
        public const string Flax = "Flax", Thread = "LinenThread", LoxPelt = "LoxPelt", Fleece = "ByreWool";
    }

    // A station that works through a queue as the game's smelters do (the pool, the brake, the wheel) or the spindle in the hand: one
    // From in, Yield of To out, Seconds each.
    public sealed class Conversion
    {
        public string Station, From, To; public int Yield = 1; public float Seconds;
        public bool Optional; // (From comes from another mod: Byre's fleece)
    }

    // A recipe at a crafting station (the loom, the sewing table).
    public sealed class Make
    {
        public string Station, Item, Title; public int Amount = 1; public (string item, int n)[] Needs;
    }

    // A garment of Homespun's own: the game item it's made on (its worn look, its slot), its colour, its armour.
    public sealed class Garment
    {
        public string Prefab, Base, Title, Description; public float Armor, Weight = 1f, Durability = 200f;
        public (float r, float g, float b) Tint;   // (the colour its look is recoloured to, worn and folded: bleached linen, undyed wool)
        public string Model;                       // (its own folded look on the ground and in the bag: Models/<Model>.json.gz)
    }

    public static class Chain
    {
        public const int PoolHolds = 40, BrakeHolds = 20;
        public const float PoolSeconds = 45, BrakeSeconds = 10; // (a station works every one of its things at one pace, as the game's smelters do)
        public static readonly Conversion[] Conversions =
        {
            new Conversion { Station = Names.Pool, From = Names.Flax, To = Names.RettedFlax, Seconds = PoolSeconds },
            new Conversion { Station = Names.Brake, From = Names.RettedFlax, To = Names.LineFlax, Seconds = BrakeSeconds },
            new Conversion { Station = Names.Brake, From = Names.LoxPelt, To = Names.Wool, Yield = 4, Seconds = BrakeSeconds },
            new Conversion { Station = Names.Pool, From = Names.Nettle, To = Names.RettedNettle, Seconds = PoolSeconds },
            new Conversion { Station = Names.Brake, From = Names.RettedNettle, To = Names.LineNettle, Seconds = BrakeSeconds },
            // (the game's wheel: raw flax stays its own 1 thread; its time is the wheel's, whatever it is)
            new Conversion { Station = Names.Wheel, From = Names.LineFlax, To = Names.Thread, Yield = 2 },
            new Conversion { Station = Names.Wheel, From = Names.LineNettle, To = Names.Thread },
            new Conversion { Station = Names.Wheel, From = Names.Wool, To = Names.Yarn },
            new Conversion { Station = Names.Wheel, From = Names.Fleece, To = Names.Yarn, Optional = true },
            // (by hand: slow, but anywhere, and walking)
            new Conversion { Station = Names.Spindle, From = Names.LineFlax, To = Names.Thread, Yield = 2, Seconds = 60 },
            new Conversion { Station = Names.Spindle, From = Names.LineNettle, To = Names.Thread, Seconds = 60 },
            new Conversion { Station = Names.Spindle, From = Names.Wool, To = Names.Yarn, Seconds = 45 },
            new Conversion { Station = Names.Spindle, From = Names.Fleece, To = Names.Yarn, Seconds = 45, Optional = true },
        };
        public static IEnumerable<Conversion> At(string station) => Conversions.Where(c => c.Station == station);
        public static Conversion Of(string station, string from) => Conversions.FirstOrDefault(c => c.Station == station && c.From == from);
        // How many come out of one put in at a station (1 if it isn't one of ours: the game's own raw flax on the wheel).
        public static int Yield(string station, string from) => Of(station, from)?.Yield ?? 1;

        public static readonly Make[] Makes =
        {
            new Make { Station = Names.Loom, Item = Names.LinenBolt, Title = "Bolt of linen", Needs = new[] { (Names.Thread, 8) } },
            new Make { Station = Names.Loom, Item = Names.WoolBolt, Title = "Bolt of wool", Needs = new[] { (Names.Yarn, 8) } },
            new Make { Station = Names.Loom, Item = Names.Band, Title = "Tablet-woven band", Amount = 2, Needs = new[] { (Names.Thread, 2), (Names.Yarn, 2) } },
            new Make { Station = Names.Table, Item = Names.Undertunic, Needs = new[] { (Names.LinenBolt, 1), (Names.Thread, 2) } },
            new Make { Station = Names.Table, Item = Names.Tunic, Needs = new[] { (Names.WoolBolt, 2), (Names.Band, 1) } },
            new Make { Station = Names.Table, Item = Names.Dress, Needs = new[] { (Names.WoolBolt, 2), (Names.Band, 1) } },
            new Make { Station = Names.Table, Item = Names.Trousers, Needs = new[] { (Names.WoolBolt, 2), (Names.Thread, 2) } },
            new Make { Station = Names.Table, Item = Names.Cloak, Needs = new[] { (Names.WoolBolt, 3), (Names.Band, 1) } },
        };

        public static readonly Garment[] Garments =
        {
            new Garment { Prefab = Names.Undertunic, Base = "ArmorTunic10", Title = "Linen undertunic", Armor = 1, Weight = 0.5f, Tint = (0.9f, 0.87f, 0.78f), Model = "undertunic",
                Description = "A long shirt of bleached linen, worn next to the skin. Light, and it doesn't itch." },
            new Garment { Prefab = Names.Tunic, Base = "ArmorTunic10", Title = "Wool tunic", Armor = 3, Weight = 1.5f, Tint = (0.72f, 0.62f, 0.48f), Model = "tunic",
                Description = "A knee-length tunic of undyed wool, the neck and hem edged with a woven band." },
            new Garment { Prefab = Names.Dress, Base = "ArmorDress10", Title = "Wool dress", Armor = 3, Weight = 1.5f, Tint = (0.72f, 0.62f, 0.48f), Model = "dress",
                Description = "A long dress of undyed wool, edged with a woven band." },
            new Garment { Prefab = Names.Trousers, Base = "ArmorRagsLegs", Title = "Wool trousers", Armor = 2, Weight = 1f, Tint = (0.45f, 0.38f, 0.3f), Model = "trousers",
                Description = "Loose trousers of grey-brown wool, gathered below the knee." },
            new Garment { Prefab = Names.Cloak, Base = "CapeLinen", Title = "Hooded cloak", Armor = 1, Weight = 2f, Tint = (0.33f, 0.28f, 0.23f), Model = "cloak",
                Description = "A heavy cloak of coarse wool, vadmal, with a hood." },
        };
        public static Garment GarmentOf(string prefab) => Garments.FirstOrDefault(g => g.Prefab == prefab);

        // Hildir's tunics, dresses and hats at the sewing table: the game's own items (so they look as they should), each colour from
        // what the land gives (blueberries blue as woad, raspberries red as madder, dandelions yellow as weld, bark brown, thistle
        // green). Jarldom's weaver makes the same with the same colours (its Villager/Weaving.cs).
        private const string L = Names.LinenBolt, T = Names.Thread, B = Names.Band;
        public static readonly (string item, (string res, int n)[] needs)[] Hildir =
        {
            ("ArmorTunic10", new[] { (L, 1) }), ("ArmorDress10", new[] { (L, 1) }),
            ("ArmorTunic1", new[] { (L, 1), ("Blueberries", 5) }), ("ArmorTunic4", new[] { (L, 1), ("Raspberry", 5) }), ("ArmorTunic7", new[] { (L, 1), ("Dandelion", 5) }),
            ("ArmorDress1", new[] { (L, 1), ("Wood", 4) }), ("ArmorDress4", new[] { (L, 1), ("Blueberries", 5) }), ("ArmorDress7", new[] { (L, 1), ("Dandelion", 5) }),
            ("ArmorTunic2", new[] { (L, 1), (B, 1), ("Blueberries", 5), ("LeatherScraps", 2) }), ("ArmorTunic5", new[] { (L, 1), (B, 1), ("Raspberry", 5), ("LeatherScraps", 2) }), ("ArmorTunic8", new[] { (L, 1), (B, 1), ("Dandelion", 5), ("LeatherScraps", 2) }),
            ("ArmorDress2", new[] { (L, 1), (B, 1), ("Wood", 4) }), ("ArmorDress5", new[] { (L, 1), (B, 1), ("Blueberries", 5) }), ("ArmorDress8", new[] { (L, 1), (B, 1), ("Dandelion", 5) }),
            ("ArmorTunic3", new[] { (L, 1), ("Blueberries", 5), ("Amber", 1) }), ("ArmorTunic6", new[] { (L, 1), ("Raspberry", 5), ("Amber", 1) }), ("ArmorTunic9", new[] { (L, 1), ("Dandelion", 5), ("Amber", 1) }),
            ("ArmorDress3", new[] { (L, 1), ("Wood", 4), ("Amber", 1) }), ("ArmorDress6", new[] { (L, 1), ("Blueberries", 5), ("Amber", 1) }), ("ArmorDress9", new[] { (L, 1), ("Dandelion", 5), ("Amber", 1) }),
            ("HelmetHat3", new[] { ("DeerHide", 2) }), ("HelmetHat8", new[] { ("WolfPelt", 1) }),
            ("HelmetHat1", new[] { (T, 2), ("Blueberries", 3) }), ("HelmetHat2", new[] { (T, 2), ("Thistle", 3) }), ("HelmetHat6", new[] { (T, 2), ("Dandelion", 3) }), ("HelmetHat7", new[] { (T, 2), ("Raspberry", 3) }),
            ("HelmetHat5", new[] { (T, 2), ("Raspberry", 3), ("LeatherScraps", 1) }),
        };

        // Every recipe at our stations: ours, and Hildir's clothes at the sewing table.
        public static IEnumerable<Make> AllMakes() => Makes.Concat(Hildir.Select(h => new Make { Station = Names.Table, Item = h.item, Needs = h.needs }));

        // What a thing comes to in what's gathered (flax, fleece, pelts, berries...), following the cheapest of our ways to make each
        // step (for the tests, and `spun chain`). Fractions: a band is made two at a time.
        public static Dictionary<string, double> Raw(string item, bool fleece = true)
        {
            var total = new Dictionary<string, double>();
            void Add(string what, double n, int depth)
            {
                if (depth > 12) throw new InvalidOperationException("The chain loops at " + what);
                Make m = AllMakes().FirstOrDefault(x => x.Item == what);
                if (m != null)
                {
                    foreach (var (res, k) in m.Needs) Add(res, n * k / m.Amount, depth + 1);
                    return;
                }
                // (spun, broken or retted: the hand's way gives what the wheel's does; yarn from Byre's fleece if it's in, else the lox's wool)
                Conversion c = Conversions.Where(x => x.To == what && x.Station != Names.Spindle && (fleece || !x.Optional))
                    .OrderByDescending(x => x.Optional).ThenByDescending(x => x.Yield).FirstOrDefault();
                if (c != null) { Add(c.From, n / c.Yield, depth + 1); return; }
                total[what] = (total.TryGetValue(what, out double had) ? had : 0) + n;
            }
            Add(item, 1, 0);
            return total;
        }
        public static string RawText(Dictionary<string, double> raw) => string.Join(", ", raw.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key} {Math.Round(kv.Value, 2)}"));

        // The spindle in the hand: of the fibres in the bag, the first it spins (line flax before line nettle before wool before fleece).
        public static Conversion SpindleNext(Func<string, int> count) => At(Names.Spindle).FirstOrDefault(c => count(c.From) > 0);
        // Spinning by hand: time spun so far, plus this frame's; the thread or yarn is done when it reaches the conversion's time.
        public static (float spun, bool done) Spin(float spun, float dt, Conversion c)
        {
            if (c == null || dt <= 0) return (spun, false);
            spun += dt;
            return spun >= c.Seconds ? (0, true) : (spun, false);
        }
        public static string Span(float seconds) => seconds >= 60 ? $"{(int)(seconds / 60)}m {(int)seconds % 60:00}s" : $"{Math.Max(0, (int)Math.Ceiling(seconds))}s";
    }
}
