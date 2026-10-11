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
        public string Id = "", Colony = ""; public float X, Z; public string Biome = ""; public bool Crypt; public int Beds;
        public Dictionary<string, (int n, int tier)> Left = new Dictionary<string, (int, int)>();
        public Dictionary<string, int> Taken = new Dictionary<string, int>();
        public bool Camp => Beds > 0;
        public int Ore => Left.Values.Sum(v => v.n);
        public int Tier => Left.Where(kv => kv.Value.n > 0).Select(kv => kv.Value.tier).DefaultIfEmpty(0).Min(); // (the easiest ore there)

        private static string F(float f) => Math.Round(f).ToString(CultureInfo.InvariantCulture);
        public string Encode() =>
            $"{Colony},{F(X)},{F(Z)},{Biome.ToLowerInvariant()},{(Crypt ? 1 : 0)},{Beds}," +
            string.Join(";", Left.OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}:{kv.Value.n}:{kv.Value.tier}")) + "," + string.Join(";", Taken.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => $"{kv.Key}:{kv.Value}")); // (sorted: the same claim, the same key)
        public static Claim Decode(string id, string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            string[] f = value.Split(',');
            if (f.Length < 8) return null;
            var c = new Claim { Id = id, Colony = f[0], Biome = f[3], Crypt = f[4] == "1" };
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

    public static class Mining
    {
        public const float BareReach = 8f, CampReach = 60f, CampNear = 15f, CryptReach = 45f, Walk = 2.5f;
        // Away: there and back at a walk, and the digging (half a day at a bare claim; a camp's miners stay the day).
        public static double TripSeconds(float distance, bool camp, double dayLength) => 2 * distance / Walk + dayLength * (camp ? 1.0 : 0.4);
        // What one miner brings home: a back's load, a cart's with the colony's cart; twice that from a camp (they dug the day).
        public static int Carry(bool cart, bool camp) => (cart ? 90 : 30) * (camp ? 2 : 1);
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
