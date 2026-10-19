using System;
using System.Collections.Generic;
using System.Linq;

namespace Jarldom
{
    // True legends: told in the hall when a jarl is there, each a real place of the world (a crypt, a troll's cave, a sunken barrow...),
    // with where it lies from the hall. Heard three times, it's marked on the jarls' maps. One at a time; told ones aren't told again.
    public static class TrueLegend
    {
        public const int HeardForPin = 3; public const float Near = 150f, Far = 1500f;
        public const string Cur = "tlg:cur", KindKey = "tlg:kind", Heard = "tlg:heard", ToldPrefix = "tlg:told:", SeenPrefix = "tlg:seen:";
        // No more than MaxOpen pinned and not yet found at once (a found one is one a jarl came within FoundAt of; one told StaleDays ago
        // no longer counts): till then no new legend is told, and a jarl's map keeps only the newest MaxOpen.
        public const int MaxOpen = 4, StaleDays = 10; public const float FoundAt = 40f;
        public static int Open(IDictionary<string, int> flags, int today) => flags.Count(kv => kv.Key.StartsWith(ToldPrefix, StringComparison.Ordinal)
            && kv.Value >= today - StaleDays && !flags.ContainsKey(SeenPrefix + kv.Key.Substring(ToldPrefix.Length)));
        // The oldest of a map's pins past the cap (the list oldest first).
        public static List<string> Excess(IList<string> pinned, int max) => pinned.Take(Math.Max(0, pinned.Count - max)).ToList();
        // (prefab prefix, how the tale names it, the stage of the world it belongs to: the bosses slain)
        public static readonly (string prefix, string name, int stage)[] Kinds =
        {
            ("ShipWreck", "a wrecked ship", 0), ("Crypt", "a burial chamber", 1), ("TrollCave", "a troll's cave", 1), ("StoneTowerRuins", "a fallen stone tower", 1),
            ("SunkenCrypt", "a sunken barrow", 2), ("MountainCave", "a cave in the ice", 3), ("DrakeNest", "a drakes' nest", 3), ("GoblinCamp", "a Fuling village", 4),
            ("Mistlands_DvergrTownEntrance", "a dwarven mine", 5),
        };
        public static int KindOf(string prefab) => prefab == null ? -1 : Array.FindIndex(Kinds, k => prefab.StartsWith(k.prefix, StringComparison.Ordinal));

        // Positions packed to one int (each axis rounded, offset into 15 bits): kept in the colony's flags.
        public static int Pack(float x, float z) => (Clamp(x) << 15) | Clamp(z);
        public static (float x, float z) Unpack(int p) => ((p >> 15) - 16384, (p & 0x7fff) - 16384);
        private static int Clamp(float v) => Math.Max(0, Math.Min(32767, (int)Math.Round(v) + 16384));

        // The next legend: the nearest place of a kind the world is ready for (up to one stage ahead), between 150 m and 1.5 km of the
        // hall, not told before. (index into the list, or -1)
        public static int Pick(IList<(string prefab, float x, float z)> places, float hx, float hz, int stage, ICollection<int> told)
        {
            int best = -1; double bestD = double.MaxValue;
            for (int i = 0; i < places.Count; i++)
            {
                int k = KindOf(places[i].prefab);
                if (k < 0 || Kinds[k].stage > stage + 1 || told.Contains(Pack(places[i].x, places[i].z))) continue;
                double d = Math.Sqrt((places[i].x - hx) * (places[i].x - hx) + (places[i].z - hz) * (places[i].z - hz));
                if (d < Near || d > Far || d >= bestD) continue;
                bestD = d; best = i;
            }
            return best;
        }
        // The tale, told a little differently each time.
        public static string Tale(int kind, float metres, string compass, int telling, Random rng)
        {
            string what = Kinds[Math.Max(0, Math.Min(Kinds.Length - 1, kind))].name;
            string far = metres < 400 ? "not an hour's walk" : metres < 900 ? "a morning's walk" : "a long day's walk";
            string[] open = { "My grandfather swore to this:", "They tell it in every hall from here to the sea:", "Listen, and I'll tell you true:", "An old one told me this, and she never lied:" };
            string[] mid = {
                $"{far} to the {compass} lies {what}, and something there keeps old gold.",
                $"to the {compass}, {far} off, there's {what}. Men went in. Not all came out.",
                $"go {compass}, {far}, and you'll find {what}, where the dead don't sleep easy.",
                $"{what} lies {far} to the {compass}. The ravens circle it, and they know why." };
            string end = telling >= HeardForPin ? " That's three times you've heard it: go and see." : "";
            return open[rng.Next(open.Length)] + " " + mid[(telling + rng.Next(2)) % mid.Length] + end;
        }
    }
}
