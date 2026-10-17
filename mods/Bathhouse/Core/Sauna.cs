using System;

namespace Bathhouse
{
    // What one room has, as the game sees it round a stove (or the game's hot tub): read by Room in the world, judged here.
    public sealed class RoomFacts
    {
        public bool Stove, HotTub;   // a sauna stove / the game's hot tub in it
        public bool Lit;             // that heat burning (the stove's fire, the hot tub's fire)
        public int Benches;          // sauna benches in it
        public float Cover;          // how covered (the game's own reckoning: 0.8 and a roof is shelter)
        public bool UnderRoof;
        public int Walls, Rays;      // horizontal rays from the heat that met a wall, of how many
        public float Across;         // the mean distance those rays went (metres), misses counting as far as they reach
    }

    // The sauna's rules, with nothing of the game in them (tested in tests/Bathhouse.Tests).
    public static class Sauna
    {
        public const float Reach = 6f;            // how far from its heat a room's benches and bathers may be (metres)
        public const float RayReach = 8f;         // how far the walls are looked for
        public const float MaxAcross = 6f;        // a room wider than this round its stove is a hall, not a sauna: the heat won't fill it
        public const int MinWalls = 7;            // of 8 rays: walls all round (a door's gap is fine)
        public const float SitSeconds = 60f;      // sitting in the heat this long: Bathed
        public const float SteamSeconds = 30f;    // water thrown on the stones lingers this long, and the heat works twice as fast in it
        public const float PlungeSeconds = 180f;  // straight from the heat into cold water: this long after leaving it
        public const float BathedSeconds = 1200f; // 20 minutes
        public const float InvigoratedSeconds = 600f; // 10 minutes
        public const float RestedSeconds = 600f;  // Bathed adds this to Rested
        public const int Comfort = 2;             // Bathed adds this to comfort
        public const float StaminaRegen = 1.1f;   // Bathed: stamina comes back 10% faster
        public const float Speed = 0.05f;         // Invigorated: 5% faster on foot
        public const float Stamina = 1.1f;        // Invigorated: 10% more stamina
        public const int MostCompany = 4;         // company past this adds nothing more

        // Why the room isn't a sauna ("" when it is).
        public static string WhyNot(RoomFacts r)
        {
            if (r == null || !r.Stove && !r.HotTub) return "There's no sauna stove here.";
            if (!r.UnderRoof || r.Cover < 0.8f) return "A sauna needs a roof over it.";
            if (r.Rays > 0 && r.Walls < Math.Min(MinWalls, r.Rays)) return "A sauna needs walls all round to hold the heat (a door is fine).";
            if (r.Across > MaxAcross) return "The room is too big for the stove to heat: build the sauna small.";
            if (r.Benches == 0 && !r.HotTub) return "A sauna needs a sauna bench to sit on.";
            if (!r.Lit) return r.Stove ? "The stove is cold: give it wood." : "The hot tub's fire is out: give it wood.";
            return "";
        }
        public static bool Works(RoomFacts r) => WhyNot(r) == "";

        // A sitter's time in the heat after dt more seconds: twice as fast in steam.
        public static float Sit(float sat, float dt, bool steam) => sat + dt * (steam ? 2f : 1f);
        public static bool Steaming(double now, double steamedAt) => steamedAt > 0 && now - steamedAt >= 0 && now - steamedAt < SteamSeconds;
        public static bool Done(float sat) => sat >= SitSeconds;

        // How much longer the buffs last for bathing in company (other players, villagers): a quarter for each, to twice as long.
        public static float Lengthen(int company) => 1f + 0.25f * Math.Max(0, Math.Min(company, MostCompany));

        // Straight from the heat: the plunge counts.
        public static bool Plunge(double now, double leftHeatAt) => leftHeatAt > 0 && now - leftHeatAt >= 0 && now - leftHeatAt <= PlungeSeconds;

        // The Rested left after Bathed adds its time: what's left, plus RestedSeconds.
        public static float Rested(float left) => Math.Max(0, left) + RestedSeconds;

        // A short "1m 20s"-style time for the hover and the commands.
        public static string Span(float seconds)
        {
            int s = (int)Math.Ceiling(Math.Max(0, seconds));
            return s >= 60 ? $"{s / 60}m {s % 60:00}s" : $"{s}s";
        }
    }
}
