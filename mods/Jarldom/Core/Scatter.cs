using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Jarldom
{
    // The scattered reborn: now and then (one arrival in five, once the colony is a hamlet) a soul Odin sends is set down out in the
    // wilds instead of at the pillars: at a real place (a ruin, a wreck, a tower, a standing stone) 400 to 1500 m away, in a land the
    // world has opened, holding out there three days. Found, they follow their finder home; missed, the Valkyrie takes them, no blame.
    // Pure: the chance, the choice of place, the time it holds, and the record of one waiting.
    public static class Scatter
    {
        public const double Chance = 0.2, HoldDays = 3;
        public const float Near = 400f, Far = 1500f;
        public static bool Rolls(Rank rank, double roll) => rank >= Rank.Hamlet && roll < Chance;

        // The land a biome is, by the tier its bosses open it at (Meadows 0 ... Ashlands 6; the Deep North and the Ocean never).
        public static int Tier(string biome)
        {
            switch (biome)
            {
                case "Meadows": return 0;
                case "BlackForest": return 1;
                case "Swamp": return 2;
                case "Mountain": return 3;
                case "Plains": return 4;
                case "Mistlands": return 5;
                case "AshLands": return 6;
                default: return 99;
            }
        }
        // Places a soul could hold out at, by the location's prefab name.
        private static readonly string[] Shelters = { "WoodHouse", "Ruin", "StoneTower", "ShipWreck", "Runestone", "StoneHenge", "WoodFarm", "WoodVillage", "Waymarker", "AbandonedLogCabin", "ShipSetting", "DrakeNest" };
        public static bool Shelter(string prefab) => prefab != null && Shelters.Any(s => prefab.StartsWith(s, StringComparison.Ordinal)) && !prefab.Contains("Dungeon");

        // Where: one of the candidates (prefab, x, z, biome) far enough and near enough from the colony, in an opened land; null if none.
        public static (string prefab, float x, float z)? Site(IEnumerable<(string prefab, float x, float z, string biome)> places, float cx, float cz, int opened, Random rng)
        {
            var fit = places.Where(p => Shelter(p.prefab) && Tier(p.biome) <= opened)
                .Where(p => { double d = Math.Sqrt((p.x - cx) * (p.x - cx) + (p.z - cz) * (p.z - cz)); return d >= Near && d <= Far; })
                .OrderBy(p => p.x).ThenBy(p => p.z).ToList(); // (a fixed order: the same seed picks the same place)
            if (fit.Count == 0) return null;
            var c = fit[rng.Next(fit.Count)];
            return (c.prefab, c.x, c.z);
        }
        public static bool Expired(double setDown, double now) => now - setDown >= HoldDays;
        public static double DaysLeft(double setDown, double now) => Math.Max(0, HoldDays - (now - setDown));

        // One waiting in the record: "vid,x,z,day".
        public sealed class Waiting { public string Vid = ""; public float X, Z; public double Day; }
        public static string Save(Waiting w) => w == null ? "" : string.Join(",", w.Vid, w.X.ToString("0.#", CultureInfo.InvariantCulture), w.Z.ToString("0.#", CultureInfo.InvariantCulture), w.Day.ToString("0.###", CultureInfo.InvariantCulture));
        public static Waiting Load(string s)
        {
            string[] f = (s ?? "").Split(',');
            if (f.Length != 4 || f[0] == "") return null;
            if (!float.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) || !float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) || !double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return null;
            return new Waiting { Vid = f[0], X = x, Z = z, Day = d };
        }
    }
}
