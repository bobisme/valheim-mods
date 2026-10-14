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
        // ruddy, so the red goes most): greyed a little at stage 1, near ashen at stage 2, no darker (red, green, blue, 0..1 each).
        public static (float r, float g, float b) Tint((float r, float g, float b) skin, int stage)
        {
            if (stage <= 0) return skin;
            // (greyed, not darkened. The game multiplies the face's own ruddy texture by this, so their colour is first drawn toward its grey,
            // then the texture's warmth is cancelled (more red taken than blue), and all brought back to the brightness it was drawn at)
            float s = stage >= 2 ? 0.9f : 0.6f;
            (float r, float g, float b) cancel = stage >= 2 ? (0.58f, 0.82f, 1f) : (0.68f, 0.87f, 1f); // (stage 2: the texture's colour cancelled outright, ash grey)
            float luma = Luma(skin);
            (float r, float g, float b) t = ((skin.r + (luma - skin.r) * s) * cancel.r, (skin.g + (luma - skin.g) * s) * cancel.g, (skin.b + (luma - skin.b) * s) * cancel.b);
            float drawn = Luma(Drawn(t)), k = drawn > 0.001f ? Luma(Drawn(skin)) / drawn : 1f;
            return (t.r * k, t.g * k, t.b * k);
        }
        public static readonly (float r, float g, float b) Face = (0.8f, 0.56f, 0.46f); // (about the skin texture's own colour)
        public static (float r, float g, float b) Drawn((float r, float g, float b) c) => (c.r * Face.r, c.g * Face.g, c.b * Face.b); // (as the face is drawn)
        public static float Luma((float r, float g, float b) c) => 0.299f * c.r + 0.587f * c.g + 0.114f * c.b;
        // At stage 2 the hair goes grey, as a draugr's (their own hair colour toward ash).
        public static (float r, float g, float b) Hair((float r, float g, float b) hair, int stage)
        {
            if (stage < 2) return hair;
            (float r, float g, float b) ash = (0.5f, 0.52f, 0.5f); const float t = 0.6f;
            return (hair.r + (ash.r - hair.r) * t, hair.g + (ash.g - hair.g) * t, hair.b + (ash.b - hair.b) * t);
        }
    }
}
