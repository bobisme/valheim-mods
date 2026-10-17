using System;
using System.Collections.Generic;
using System.Linq;

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

        // The skald's song of the evening (Bob's two, each sung by a man and by a woman: the skald's own voice): one a night, from a little after the hall fills; "Drekkum, drengir!" on a
        // feast night, else the two by turns. Worked out from the hour, so every game hears the same song at the same place in it.
        public static readonly string[] Songs = { "song_ves_heill", "song_drekkum" };
        public static string Voiced(int song, bool female) => Songs[song] + (female ? "_f" : "");
        public static IEnumerable<string> AllVoices => Songs.SelectMany(s => new[] { s, s + "_f" });
        public const double SongFrom = Schedule.HallAt + 0.25; // (a quarter hour in: the hall's filled)
        public const int SongMinCrowd = 3; // (the skald and two to hear them)
        public static int SongOf(int day, bool feast) => feast ? 1 : ((day % 2) + 2) % 2;
        // Where in it the evening is now (seconds), or -1 when no song is being sung.
        public static double SongAt(double hour, double hourSeconds, double length) { double at = (hour - SongFrom) * hourSeconds; return at >= 0 && at < length ? at : -1; }
        public static bool Sung(bool skaldInHall, int inHall, double mood) => skaldInHall && inHall >= SongMinCrowd && !Miserable(mood);

        // Heard from outside the hall: quieter and duller (the walls take the high notes) the further off, gone at Fade metres.
        public const float SongFade = 40f;
        public static (float volume, float cutoff) SongHeard(bool inside, float outside)
        {
            if (inside) return (1f, 22000f);
            float t = Math.Min(1f, Math.Max(0f, outside / SongFade));
            return (0.45f * (1 - t) * (1 - t), 1600f * (float)Math.Pow(0.25, t)); // (through the door: 1.6 kHz, down to 400 Hz far off)
        }
    }
}
