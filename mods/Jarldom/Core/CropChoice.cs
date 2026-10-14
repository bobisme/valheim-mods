using System.Collections.Generic;
using System.Linq;

namespace Jarldom
{
    // What a fields zone is sown with, season by season: chosen by the jarl in the zone's window (a crop, or anything), kept in the colony's
    // flags as "crop:<zone>:<season>" = the crop's sapling prefab, hashed (0, or no flag: anything, whatever seeds the stores hold).
    public static class CropChoice
    {
        public const string Prefix = "crop:";
        public static string Key(string zone, Season s) => Prefix + zone + ":" + (int)s;

        // A prefab name's hash, the same on every game (not string.GetHashCode, which isn't).
        public static int Hash(string name)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char ch in name ?? "") { h ^= ch; h *= 16777619; }
                int v = (int)h;
                return v == 0 ? 1 : v;
            }
        }

        // The sapling chosen for the zone this season, among those known; null for anything.
        public static string Chosen(IDictionary<string, int> flags, string zone, Season s, IEnumerable<string> saplings)
        {
            if (!flags.TryGetValue(Key(zone, s), out int h) || h == 0) return null;
            return saplings.FirstOrDefault(n => Hash(n) == h);
        }
        public static void Set(IDictionary<string, int> flags, string zone, Season s, string sapling)
        {
            if (string.IsNullOrEmpty(sapling)) flags.Remove(Key(zone, s)); else flags[Key(zone, s)] = Hash(sapling);
        }
        // A zone removed: its choices with it.
        public static void Forget(IDictionary<string, int> flags, string zone)
        {
            foreach (string k in flags.Keys.Where(k => k.StartsWith(Prefix + zone + ":")).ToList()) flags.Remove(k);
        }

        // The next choice from the current one, cycling: anything, then each crop in order, then anything again (step -1: backwards).
        public static string Cycle(IList<string> saplings, string current, int step)
        {
            int n = saplings.Count + 1, at = current == null ? 0 : saplings.IndexOf(current) + 1; // (0: anything)
            int next = ((at + step) % n + n) % n;
            return next == 0 ? null : saplings[next - 1];
        }
    }
}
