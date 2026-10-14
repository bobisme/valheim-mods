using System;
using System.Collections.Generic;
using System.Linq;

namespace Jarldom
{
    // Odin the Wanderer watches the colony's jarls go out into the world: each first (a land first set foot in, a landmark first found, a
    // dungeon first entered, the furthest from home) is the colony's saga, and Odin's notice: renown. Kept in the colony's flags
    // ("first:<what>" = the day).
    public static class Wanderer
    {
        public const string Prefix = "first:";
        private static readonly Dictionary<string, int> BiomePoints = new Dictionary<string, int>
        { ["Ocean"] = 2, ["BlackForest"] = 4, ["Swamp"] = 6, ["Mountain"] = 8, ["Plains"] = 10, ["Mistlands"] = 12, ["AshLands"] = 14, ["DeepNorth"] = 16 }; // (all of them: about a boss and a half)
        private static readonly Dictionary<string, string> BiomeNames = new Dictionary<string, string>
        { ["Ocean"] = "the open sea", ["BlackForest"] = "the Black Forest", ["Swamp"] = "the Swamp", ["Mountain"] = "the Mountains", ["Plains"] = "the Plains", ["Mistlands"] = "the Mistlands", ["AshLands"] = "the Ashlands", ["DeepNorth"] = "the Deep North" };
        public static readonly int[] FarKm = { 1, 2, 4, 8 };
        public const int PlacePoints = 2, DungeonPoints = 5;

        // What a first is worth (0: not one the saga keeps).
        public static int Points(string what)
        {
            string[] f = what.Split(new[] { ':' }, 2);
            if (f.Length < 2) return 0;
            switch (f[0])
            {
                case "biome": return BiomePoints.TryGetValue(f[1], out int p) ? p : 0;
                case "place": return Requests.Landmark(f[1]) != null ? PlacePoints : 0;
                case "dungeon": return DungeonPoints;
                case "far": return int.TryParse(f[1], out int km) && FarKm.Contains(km) ? 2 * (Array.IndexOf(FarKm, km) + 1) : 0;
            }
            return 0;
        }
        public static int Saga(IDictionary<string, int> flags) => flags.Keys.Where(k => k.StartsWith(Prefix)).Sum(k => Points(k.Substring(Prefix.Length)));
        public static bool Known(IDictionary<string, int> flags, string what) => flags.ContainsKey(Prefix + what);
        // The furthest milestone reached at this distance from home (0 below a kilometre).
        public static int Far(float metres) => FarKm.Where(k => metres >= k * 1000f).DefaultIfEmpty(0).Max();
        // How the saga tells it.
        public static string Tell(string what, string who)
        {
            string[] f = what.Split(new[] { ':' }, 2);
            string arg = f.Length > 1 ? f[1] : "";
            switch (f[0])
            {
                case "biome": return $"{who} was the first of the hall to set foot in {(BiomeNames.TryGetValue(arg, out string n) ? n : arg)}.";
                case "place": return $"{who} was the first of the hall to find {Requests.Landmark(arg)?.label ?? arg}.";
                case "dungeon": return $"{who} was the first of the hall to go down into {Requests.Landmark(arg)?.label ?? arg}.";
                case "far": return $"{who} went further from the hall than any before: {arg} km.";
            }
            return null;
        }
    }
}
