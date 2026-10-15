using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Jarldom
{
    // The land wears thin: each 64 m zone of the colony keeps a count of what's taken from it (trees felled, game killed, wild things
    // picked). The count ebbs away by itself, all of it over ten days of no use; past a threshold the zone's yields fall, to at most
    // 30% less. Pressure to range wider (outposts, expeditions), not a punishment: never below 0.7. Pure; kept compactly in the record.
    public static class Worn
    {
        public const float ZoneSize = 64f;
        public const double RecoverDays = 10;   // (a count at its peak is gone after this many days of no use)
        public const float Threshold = 40f, Full = 140f, Least = 0.7f;
        // A use's weight: a felled tree or a beast is one, a wild plant picked a quarter.
        public static float Weight(string kind) => kind == "pick" ? 0.25f : 1f;

        public static (int x, int z) ZoneOf(float x, float z) => ((int)Math.Floor(x / ZoneSize), (int)Math.Floor(z / ZoneSize));

        public sealed class Zone { public float Count; public double Day; } // (Day: when it was last counted, in days, fractional)

        // The count left after the days since it was last counted: it ebbs evenly, all of it in RecoverDays.
        public static float Left(Zone z, double day)
        {
            if (z == null) return 0;
            double idle = Math.Max(0, day - z.Day);
            return (float)Math.Max(0, z.Count * (1 - idle / RecoverDays));
        }
        // One more use: what was left, plus its weight.
        public static void Use(Dictionary<(int, int), Zone> zones, (int x, int z) at, string kind, double day)
        {
            if (!zones.TryGetValue(at, out Zone z)) zones[at] = z = new Zone();
            z.Count = Left(z, day) + Weight(kind);
            z.Day = day;
        }
        // What a zone's yields are multiplied by now: 1 up to the threshold, falling evenly to Least at Full and beyond.
        public static float Yield(float count)
        {
            if (count <= Threshold) return 1f;
            float t = Math.Min(1f, (count - Threshold) / (Full - Threshold));
            return 1f - (1f - Least) * t;
        }
        public static float YieldAt(Dictionary<(int, int), Zone> zones, (int x, int z) at, double day) => Yield(zones.TryGetValue(at, out Zone z) ? Left(z, day) : 0);

        // Kept in the record as "x,z,count,day;...", only zones still worn (a recovered one is dropped).
        public static string Save(Dictionary<(int, int), Zone> zones, double day) =>
            string.Join(";", zones.Where(kv => Left(kv.Value, day) > 0.01f).Select(kv => $"{kv.Key.Item1},{kv.Key.Item2},{kv.Value.Count.ToString("0.##", CultureInfo.InvariantCulture)},{kv.Value.Day.ToString("0.###", CultureInfo.InvariantCulture)}"));
        public static Dictionary<(int, int), Zone> Load(string s)
        {
            var zones = new Dictionary<(int, int), Zone>();
            if (string.IsNullOrEmpty(s)) return zones;
            foreach (string part in s.Split(';'))
            {
                string[] f = part.Split(',');
                if (f.Length != 4 || !int.TryParse(f[0], out int x) || !int.TryParse(f[1], out int z)) continue;
                if (!float.TryParse(f[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float n) || !double.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) continue;
                zones[(x, z)] = new Zone { Count = n, Day = d };
            }
            return zones;
        }
    }
}
