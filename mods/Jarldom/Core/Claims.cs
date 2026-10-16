using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Jarldom
{
    // An ore claim: a stake the jarl drives in beside a deposit found in the wilds (copper, tin, silver; iron in a sunken crypt). Its
    // miners go out to it from home and come back with ore. A bare stake claims the one deposit beside it; with a camp round it (a bed,
    // a chest and a fire) it claims everything within reach, as many miners work it as it has beds, and they stay longer. Claims lie far
    // from their colony, so each is kept in a global key: "jarldom_claim_<id> <colony>,<x>,<z>,<biome>,<crypt>,<beds>,<left>,<taken>",
    // left and taken as "ore:n:tier;ore:n:tier". What's left is surveyed by the stake whenever its ground is loaded; what the miners
    // took is taken out of the world there then (worked-out deposits go).
    public sealed class Claim
    {
        public string Id = "", Colony = ""; public float X, Z; public string Biome = ""; public bool Crypt, Hunt; public int Beds; // (Hunt: a hunting camp, no ore: "h" where a crypt is "1")
        public Dictionary<string, (int n, int tier)> Left = new Dictionary<string, (int, int)>();
        public Dictionary<string, int> Taken = new Dictionary<string, int>();
        public bool Camp => Beds > 0;
        public int Ore => Left.Values.Sum(v => v.n);
        public int Tier => Left.Where(kv => kv.Value.n > 0).Select(kv => kv.Value.tier).DefaultIfEmpty(0).Min(); // (the easiest ore there)

        private static string F(float f) => Math.Round(f).ToString(CultureInfo.InvariantCulture);
        public string Encode() =>
            $"{Colony},{F(X)},{F(Z)},{Biome.ToLowerInvariant()},{(Hunt ? "h" : Crypt ? "1" : "0")},{Beds}," +
            string.Join(";", Left.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}:{kv.Value.n}:{kv.Value.tier}")) + "," + string.Join(";", Taken.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}:{kv.Value}")); // (sorted: the same claim, the same key)
        public static Claim Decode(string id, string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            string[] f = value.Split(',');
            if (f.Length < 8) return null;
            var c = new Claim { Id = id, Colony = f[0], Biome = f[3], Crypt = f[4] == "1", Hunt = f[4] == "h" };
            if (!float.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out c.X) || !float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out c.Z)) return null;
            int.TryParse(f[5], out c.Beds);
            foreach (string part in f[6].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] p = part.Split(':');
                if (p.Length >= 3 && int.TryParse(p[1], out int n) && int.TryParse(p[2], out int t)) c.Left[p[0]] = (n, t);
            }
            foreach (string part in f[7].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] p = part.Split(':');
                if (p.Length >= 2 && int.TryParse(p[1], out int n)) c.Taken[p[0]] = n;
            }
            return c;
        }
    }

    // What a miner picks on the way home from a claim, from what grows (or is had) in its biome, when the stores are short of it: the
    // Black Forest's berries, mushrooms and thistle the foragers can't reach from a meadow colony, a swamp's bloodbags for mead. Only
    // what the stores hold less of than they keep, a handful each (roll: 0..1 per item).
    // A hunting camp: a claim stake set to hunting, out where game is (it keeps away from homes). Its hunters go out as miners do and
    // come back with what the ground there gives: the meat, hides and odd trophy of its beasts, more for a better archer, a day's
    // hunting at a camp (a bed, a chest and a fire) and half a day from a bare stake. They go when the stores are short of meat.
    public static class Hunting
    {
        public const float NearHome = 30f; // (a camp closer than this past the colony's edge finds nothing: its own hunting grounds are there)
        public const int Arrows = 6; // (spent a trip)
        public static readonly Dictionary<string, (string item, int min, int max, float chance)[]> Game = new Dictionary<string, (string, int, int, float)[]>
        {
            ["meadows"] = new[] { ("DeerMeat", 2, 4, 1f), ("DeerHide", 2, 4, 1f), ("RawMeat", 1, 3, 0.8f), ("LeatherScraps", 2, 5, 0.8f), ("TrophyDeer", 1, 1, 0.15f), ("TrophyBoar", 1, 1, 0.1f) },
            ["blackforest"] = new[] { ("DeerMeat", 2, 5, 1f), ("DeerHide", 2, 5, 1f), ("RawMeat", 1, 2, 0.5f), ("LeatherScraps", 1, 3, 0.5f), ("TrophyDeer", 1, 1, 0.15f) },
            ["swamp"] = new[] { ("Bloodbag", 2, 5, 1f), ("Entrails", 2, 4, 0.7f), ("RawMeat", 1, 2, 0.4f) },
            ["mountain"] = new[] { ("WolfMeat", 2, 4, 1f), ("WolfPelt", 1, 3, 1f), ("WolfFang", 1, 3, 0.7f), ("FreezeGland", 1, 2, 0.4f), ("TrophyWolf", 1, 1, 0.1f) },
            ["plains"] = new[] { ("LoxMeat", 2, 4, 1f), ("LoxPelt", 1, 2, 0.8f), ("Needle", 1, 4, 0.6f), ("TrophyLox", 1, 1, 0.05f) },
            ["mistlands"] = new[] { ("HareMeat", 2, 4, 1f), ("ScaleHide", 1, 2, 0.6f), ("Feathers", 2, 6, 0.6f) },
        };
        // Game thins where it's hunted: each day's hunting at a camp (half a day from a bare stake) is a point of pressure, a day's rest
        // takes one off; what a hunter brings home is the plenty left, 1 / (1 + 0.35 x pressure): a camp hunted two days running
        // gives about 60%, one left a few days is whole again.
        public const double RecoverPerDay = 1.0, MaxPressure = 8.0;
        public static double Plenty(double pressure) => 1.0 / (1.0 + 0.35 * Math.Max(0.0, pressure));
        public static double Rested(double pressure, double days) => Math.Max(0.0, pressure - Math.Max(0.0, days) * RecoverPerDay);
        public static double AfterTrip(double pressure, bool camp) => Math.Min(MaxPressure, pressure + (camp ? 1.0 : 0.5));
        public static string Word(double pressure) { double p = Plenty(pressure); return p > 0.85 ? "plentiful" : p > 0.6 ? "thinning" : p > 0.4 ? "thin" : "hunted out for now"; }
        // Meat the stores should hold before the hunters stop going out: three meals a head, at least twenty.
        public static int Keep(int people) => Math.Max(20, people * 3);
        // One hunter's bag home: each beast's yield rolled, a day at a camp twice a bare stake's half day, a better archer more (bows
        // skill 0..100: up to half again). (roll: 0..1)
        public static Dictionary<string, int> Yield(string biome, bool camp, float bows, Func<double> roll, double plenty = 1.0)
        {
            var got = new Dictionary<string, int>();
            if (biome == null || !Game.TryGetValue(biome.ToLowerInvariant(), out var beasts)) return got;
            double k = (camp ? 2.0 : 1.0) * (1.0 + Math.Max(0f, Math.Min(100f, bows)) / 200.0) * Math.Max(0.0, Math.Min(1.0, plenty));
            foreach (var (item, min, max, chance) in beasts)
            {
                if (roll() >= chance * (item.StartsWith("Trophy") ? plenty : 1.0)) continue;
                bool trophy = item.StartsWith("Trophy");
                int n = trophy ? 1 : (int)Math.Round((min + Math.Floor(roll() * (max - min + 1))) * k);
                if (n > 0) got[item] = (got.TryGetValue(item, out int had) ? had : 0) + n;
            }
            return got;
        }
    }

    public static class Gathering
    {
        public static readonly Dictionary<string, (string item, int min, int max)[]> ByBiome = new Dictionary<string, (string, int, int)[]>
        {
            ["meadows"] = new[] { ("Raspberry", 4, 10), ("Mushroom", 2, 5), ("Dandelion", 3, 8) },
            ["blackforest"] = new[] { ("Blueberries", 4, 10), ("MushroomYellow", 2, 6), ("Mushroom", 2, 5), ("Thistle", 3, 8) },
            ["swamp"] = new[] { ("Bloodbag", 2, 5), ("Thistle", 2, 5), ("Turnip", 2, 6) },
            ["mountain"] = new[] { ("Onion", 2, 6) },
            ["plains"] = new[] { ("Cloudberry", 4, 10), ("Barley", 4, 10) },
        };
        public static readonly Dictionary<string, int> Keep = new Dictionary<string, int>
        {
            ["Raspberry"] = 20, ["Blueberries"] = 20, ["Mushroom"] = 10, ["MushroomYellow"] = 12, ["Thistle"] = 15, ["Dandelion"] = 10,
            ["Bloodbag"] = 12, ["Turnip"] = 10, ["Onion"] = 10, ["Cloudberry"] = 15, ["Barley"] = 20,
        };
        public static List<(string item, int n)> Along(string biome, Func<string, int> stock, Func<double> roll)
        {
            var got = new List<(string, int)>();
            if (biome == null || !ByBiome.TryGetValue(biome.ToLowerInvariant(), out var there)) return got;
            foreach (var (item, min, max) in there)
            {
                int keep = Keep.TryGetValue(item, out int k) ? k : 10;
                if (stock(item) >= keep) continue;
                got.Add((item, Math.Min(max, min + (int)Math.Floor(roll() * (max - min + 1)))));
            }
            return got;
        }
    }

    public static class Mining
    {
        public const float BareReach = 8f, CampReach = 60f, CampNear = 15f, CryptReach = 45f, Walk = 2.5f;
        public const float Back = 250f, CartLoad = 900f; // weight a miner carries home on their back (a player carries 300 with nothing else), and on a cart
        public const int CryptDig = 20; // what one miner can dig in a day in a crypt's dark, draugr about
        // Walking pace by the ground: a swamp's roots and water, a mountain's climb.
        public static float Pace(string biome) { switch ((biome ?? "").ToLowerInvariant()) { case "swamp": return 1.4f; case "mountain": case "mistlands": case "ashlands": case "deepnorth": return 1.6f; default: return Walk; } }
        // Away: there and back at the ground's pace, and the digging (half a day at a bare claim; a camp's miners stay the day).
        public static double TripSeconds(float distance, bool camp, double dayLength, string biome = "") => 2 * distance / Pace(biome) + dayLength * (camp ? 1.0 : 0.4);
        // A cart goes where the ground is open: meadows, forest, plains; never into a swamp, up a mountain, or down a crypt.
        public static bool CartGoes(string biome, bool crypt) { string b = (biome ?? "").ToLowerInvariant(); return !crypt && (b == "meadows" || b == "blackforest" || b == "plains"); }
        // What one miner brings home of an ore of that weight: a back's load, or a cart's where a cart can go; a crypt's dig is less.
        public static int Carry(float weight, bool cart, bool crypt)
        {
            int n = Math.Max(1, (int)Math.Floor((cart ? CartLoad : Back) / Math.Max(0.1f, weight)));
            return crypt ? Math.Min(n, CryptDig) : n;
        }
        // The chance of coming back wounded, and of not coming back on their feet (they rise at home, without their pickaxe).
        public static (float wound, float death) Risk(string biome, bool crypt, bool camp, bool guard)
        {
            float w, d;
            switch ((biome ?? "").ToLowerInvariant()) // (global keys come back in lower case)
            {
                case "meadows": w = 0f; d = 0f; break;
                case "blackforest": w = 0.04f; d = 0.01f; break;
                case "swamp": case "mountain": w = 0.08f; d = 0.02f; break;
                case "plains": case "mistlands": w = 0.12f; d = 0.04f; break;
                case "ashlands": case "deepnorth": w = 0.15f; d = 0.05f; break;
                default: w = 0.02f; d = 0f; break;
            }
            if (crypt) { w += 0.08f; d += 0.03f; }
            float k = (camp ? 0.7f : 1f) * (guard ? 0.5f : 1f);
            return (w * k, d * k);
        }
        // A load from what's left: the most plentiful ore first, a miner's pickaxe permitting.
        public static Dictionary<string, int> Take(Dictionary<string, (int n, int tier)> left, int carry, int pickTier)
        {
            var got = new Dictionary<string, int>();
            foreach (var kv in left.Where(kv => kv.Value.n > 0 && kv.Value.tier <= pickTier).OrderByDescending(kv => kv.Value.n).ToList())
            {
                if (carry <= 0) break;
                int n = Math.Min(carry, kv.Value.n);
                got[kv.Key] = n; carry -= n;
            }
            return got;
        }
        // The claim after a load: less left, more to be taken out of the world when it's next loaded.
        public static void Apply(Claim c, Dictionary<string, int> got)
        {
            foreach (var kv in got)
            {
                if (c.Left.TryGetValue(kv.Key, out var v)) c.Left[kv.Key] = (Math.Max(0, v.n - kv.Value), v.tier);
                c.Taken[kv.Key] = (c.Taken.TryGetValue(kv.Key, out int t) ? t : 0) + kv.Value;
            }
        }
        // How much the colony wants an ore, 0 to about 1.3: what the stores lack of what's to be kept (300 of each kind unless the jarl says
        // otherwise; nothing at all once that's held), and for bronze, tin wanted while there's less than half as much as copper.
        public static double Need(string ore, int held, int keep, int copper, int tin)
        {
            if (keep > 0 && held >= keep) return 0;
            double need = keep > 0 ? 1.0 - (double)held / keep : 1.0 / (1.0 + held / 100.0);
            if (ore == "tinore" && tin * 2 < copper) need += 0.3;
            if (ore == "copperore" && copper < tin * 2) need += 0.15;
            return need;
        }
        // A claim's worth going to: its most wanted ore, less for the walk (twice as far wants a clearly bigger lack).
        public static double Score(double need, float distance) => need / (1.0 + distance / 500.0);

        // Miners a claim takes at once: one at a bare stake, one a bed at a camp.
        public static int Room(Claim c) => Math.Max(1, c.Beds);

        // In a miner's away record: "mine:<claim>:<seconds>:<guard>".
        public const string Prefix = "mine:", Escort = "escort:";
        public static string Encode(string claim, double seconds, bool guard) => $"{Prefix}{claim}:{Math.Round(seconds).ToString(CultureInfo.InvariantCulture)}:{(guard ? 1 : 0)}";
        public static bool Decode(string why, out string claim, out double seconds, out bool guard)
        {
            claim = ""; seconds = 0; guard = false;
            if (why == null || !why.StartsWith(Prefix)) return false;
            string[] p = why.Substring(Prefix.Length).Split(':');
            if (p.Length < 3 || !double.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds)) return false;
            claim = p[0]; guard = p[2] == "1";
            return true;
        }
    }
}
