using System;

namespace Jarldom
{
    // The Dimming: a soul whose worth hasn't risen in a long while fades. Eight days: stage 1 (a tenth slower at work). Sixteen days:
    // stage 2 (a fifth slower, and restless). Never further, never fatal; any rise in worth (a deed, a kill, a feast, a rite) ends it.
    // A worthy soul, waiting for Valhalla, doesn't dim.
    public static class Dimming
    {
        public const int First = 8, Second = 16;
        public const string Key = "riseday"; // (in the soul's counts: the day their worth last rose)
        public static int Stage(int today, int lastRise, bool worthy)
        {
            if (worthy) return 0;
            int days = today - lastRise;
            return days >= Second ? 2 : days >= First ? 1 : 0;
        }
        public static int Of(VillagerState s, int today) => s != null && s.Counts.TryGetValue(Key, out int d) ? Stage(today, d, s.Worthy) : 0;
        public static int Days(VillagerState s, int today) => s != null && s.Counts.TryGetValue(Key, out int d) ? today - d : 0;
        public static float Work(int stage) => stage >= 2 ? 0.8f : stage == 1 ? 0.9f : 1f;
        public static string Why(int today, int lastRise) => $"Hasn't done anything worthy in {today - lastRise} days";
        // The skin, faded toward a draugr's: the warmth drawn out of their own colour (the game multiplies the face by it, and the face is
        // ruddy, so the red goes most): a little at stage 1, pallid and darker at stage 2 (red, green, blue, 0..1 each).
        public static (float r, float g, float b) Tint((float r, float g, float b) skin, int stage)
        {
            if (stage <= 0) return skin;
            (float r, float g, float b) k = stage >= 2 ? (0.5f, 0.66f, 0.64f) : (0.74f, 0.88f, 0.86f);
            return (skin.r * k.r, skin.g * k.g, skin.b * k.b);
        }
        // At stage 2 the hair goes grey, as a draugr's (their own hair colour toward ash).
        public static (float r, float g, float b) Hair((float r, float g, float b) hair, int stage)
        {
            if (stage < 2) return hair;
            (float r, float g, float b) ash = (0.5f, 0.52f, 0.5f); const float t = 0.6f;
            return (hair.r + (ash.r - hair.r) * t, hair.g + (ash.g - hair.g) * t, hair.b + (ash.b - hair.b) * t);
        }
    }
}
