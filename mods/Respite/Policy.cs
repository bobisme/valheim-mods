using System;
using System.Collections.Generic;
using System.Linq;

namespace Respite
{
    // One raid that came: when (world seconds), where, and what sent it.
    public sealed class Raid { public double T; public float X, Z; public string Src = ""; }

    // The rules, free of game types (checked in tests/Respite.Tests).
    internal static class Policy
    {
        // Why no raid may come near (x,z) now, or "" if one may: one came within radius in the last gap seconds, or cap of them in the last week.
        internal static string Blocked(IEnumerable<Raid> raids, double now, float x, float z, float radius, double gap, int cap, double week, double day)
        {
            var near = raids.Where(r => r.T <= now && Near(r, x, z, radius)).ToList();
            Raid last = near.OrderByDescending(r => r.T).FirstOrDefault();
            if (last != null && now - last.T < gap) return $"a raid ({last.Src}) came {Span(now - last.T, day)} ago; the land rests {Span(gap - (now - last.T), day)} more";
            if (cap > 0 && near.Count(r => now - r.T < week) >= cap) return $"{cap} raids here in the last {week / day:0.#} days";
            return "";
        }
        internal static bool Near(Raid r, float x, float z, float radius) => (r.X - x) * (r.X - x) + (r.Z - z) * (r.Z - z) <= radius * radius;
        // The same raid told twice (a mod's note and the game's event, or a player's game and the host): close in time and place.
        internal static bool Same(Raid a, Raid b) => Math.Abs(a.T - b.T) < 120 && Near(a, b.X, b.Z, 150);
        // Adds a raid unless it's one already told; forgets those older than keep seconds. Whether it was new.
        internal static bool Add(List<Raid> raids, Raid r, double keep)
        {
            raids.RemoveAll(o => r.T - o.T > keep);
            if (raids.Any(o => Same(o, r))) return false;
            raids.Add(r); return true;
        }
        // A span of world time in the game's days and hours.
        internal static string Span(double seconds, double day)
        {
            double hours = Math.Max(0, seconds) / day * 24;
            return hours >= 24 ? $"{hours / 24:0.#} days" : hours >= 1 ? $"{Math.Round(hours):0} hours" : "under an hour";
        }

        // ---- the muster: where raiders gather before they come ----
        // The base's edge from the distances of its built pieces: the farthest, leaving out the odd stray (a lone fence post, a far
        // portal) past the 95th; at least clear metres (a colony's own border).
        internal static float Edge(IEnumerable<float> distances, float clear)
        {
            var d = distances.OrderBy(x => x).ToList();
            float edge = d.Count == 0 ? 0 : d[Math.Min(d.Count - 1, (int)Math.Ceiling(d.Count * 0.95) - 1)];
            return Math.Max(edge, clear);
        }
        // How far out they gather: past the edge by the margin, never nearer than least, never so far they're lost (300 m).
        internal static float Distance(float edge, float margin, float least) => Math.Min(300, Math.Max(least, edge + margin));
        // Directions to try (degrees, 0 north, clockwise), spread evenly from a seeded start so every game near picks the same.
        internal static IEnumerable<float> Angles(int seed, int n)
        {
            float start = (uint)seed % 360u;
            for (int i = 0; i < n; i++) yield return (start + i * 360f / n) % 360f;
        }
        // A direction as the compass names it (x east, z north).
        internal static string Compass(float dx, float dz)
        {
            string[] names = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
            double deg = (Math.Atan2(dx, dz) * 180 / Math.PI + 360) % 360;
            return names[(int)Math.Round(deg / 45) % 8];
        }
        // A stable seed for a place, so games that see the same raid choose the same gathering spot.
        internal static int Seed(float x, float z) => unchecked((int)Math.Round(x) * 73856093 ^ (int)Math.Round(z) * 19349663);
    }
}
