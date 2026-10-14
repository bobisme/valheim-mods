using System;

namespace Jarldom
{
    // The great hall's sound: a murmur of talk as loud as the hall is full (a few talking, then a full room), quieter and broken up when the
    // colony is low, a roar on a feast or festival night, and nothing at all when the colony is miserable (the hall is quiet).
    public static class Soundscape
    {
        public const int MiserableBelow = -40;
        public static bool Miserable(double mood) => mood < MiserableBelow;

        public struct Hall { public float Volume, Full, Sparse, LaughsPerMinute; public bool Roar; } // (Full: 0 a few, 1 a packed hall)
        // present: villagers in the hall now; population: the colony's; mood: their average (-100..100).
        public static Hall HallLevel(int present, int population, double mood, bool feasting)
        {
            var h = new Hall();
            if (Miserable(mood) || present < 2) return h; // (one alone doesn't murmur)
            float full = Math.Min(1f, present / (float)Math.Max(6, population)); h.Full = full;
            h.Volume = 0.25f + 0.55f * (float)Math.Sqrt(full);
            if (mood < 0) { h.Volume *= 1f + (float)mood / 100f; h.Sparse = Math.Min(1f, (float)-mood / 40f); }
            else if (mood > 30) h.Volume = Math.Min(0.85f, h.Volume * 1.1f);
            h.LaughsPerMinute = mood < -20 ? 0 : present * (mood > 0 ? 0.25f : 0.08f);
            if (feasting) { h.Roar = true; h.Sparse = 0; h.Volume = Math.Max(h.Volume, 0.6f + 0.4f * (float)Math.Sqrt(full)); h.LaughsPerMinute = Math.Max(2, h.LaughsPerMinute * 3); }
            return h;
        }
    }
}
