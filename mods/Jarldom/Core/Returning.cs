using System;
using System.Collections.Generic;
using System.Linq;

namespace Jarldom
{
    // They answer back: in a raid the colony is losing, one of those gone to Valhalla comes back to fight for a while.
    // Losing: a raider at the hall or the pillars, or half the fighters down. Once for each soul, ever; one a raid.
    public static class Returning
    {
        public const float Seconds = 90;
        public const string RaidKey = "einherjar:raid", RaidNo = "raidno";
        public static bool Losing(bool hallAttacked, int fighters, int down) => hallAttacked || fighters > 0 && down * 2 >= fighters;
        // The raid's number, counted up as each begins (one Einherjar a raid is kept by it).
        public static int Begin(Dictionary<string, int> flags) => flags[RaidNo] = (flags.TryGetValue(RaidNo, out int n) ? n : 0) + 1;
        public static bool Returned(Dictionary<string, int> flags, string vid) => flags.TryGetValue("einherjar:" + vid, out int d) && d != 0;
        // Who comes: the worthiest soul in Valhalla not yet come back; none if one already came this raid, or it isn't going badly.
        public static string Pick(Dictionary<string, int> flags, bool losing, IEnumerable<(string vid, float worth)> ascended)
        {
            int raid = flags.TryGetValue(RaidNo, out int n) ? n : 0;
            if (!losing || raid <= 0 || flags.TryGetValue(RaidKey, out int r) && r == raid) return null;
            return ascended.Where(a => !Returned(flags, a.vid)).OrderByDescending(a => a.worth).ThenBy(a => a.vid, StringComparer.Ordinal).Select(a => a.vid).FirstOrDefault();
        }
        public static void Mark(Dictionary<string, int> flags, string vid, int day)
        {
            flags["einherjar:" + vid] = Math.Max(1, day);
            flags[RaidKey] = flags.TryGetValue(RaidNo, out int n) ? n : 0;
        }
        // What they wore, kept at the pyre: "Prefab:quality" joined by commas (small: a handful of names).
        public static string Gear(IEnumerable<(string prefab, int quality)> worn) => string.Join(",", worn.Where(w => !string.IsNullOrEmpty(w.prefab) && w.prefab.IndexOfAny(new[] { ',', ':' }) < 0).Select(w => w.prefab + ":" + Math.Max(1, w.quality)));
        public static List<(string prefab, int quality)> Worn(string kept) => (kept ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Split(':')).Select(p => (p[0], p.Length > 1 && int.TryParse(p[1], out int q) ? Math.Max(1, q) : 1)).ToList();
    }
}
