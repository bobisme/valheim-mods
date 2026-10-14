using System;

namespace Jarldom
{
    // The great hall's sound: a murmur of talk as loud as the hall is full, quieter and broken up when the colony is low, a roar on a feast
    // or festival night, and nothing at all when the colony is miserable (the hall is quiet). Plus the murmur itself: babble made here,
    // voices of men and women talking over each other, so no sound file is shipped.
    public static class Soundscape
    {
        public const int MiserableBelow = -40;
        public static bool Miserable(double mood) => mood < MiserableBelow;

        public struct Hall { public float Volume, Sparse, LaughsPerMinute; public bool Roar; }
        // present: villagers in the hall now; population: the colony's; mood: their average (-100..100).
        public static Hall HallLevel(int present, int population, double mood, bool feasting)
        {
            var h = new Hall();
            if (Miserable(mood) || present < 2) return h; // (one alone doesn't murmur)
            float full = Math.Min(1f, present / (float)Math.Max(6, population));
            h.Volume = 0.25f + 0.55f * (float)Math.Sqrt(full);
            if (mood < 0) { h.Volume *= 1f + (float)mood / 100f; h.Sparse = Math.Min(1f, (float)-mood / 40f); }
            else if (mood > 30) h.Volume = Math.Min(0.85f, h.Volume * 1.1f);
            h.LaughsPerMinute = mood < -20 ? 0 : present * (mood > 0 ? 0.25f : 0.08f);
            if (feasting) { h.Roar = true; h.Sparse = 0; h.Volume = Math.Max(h.Volume, 0.6f + 0.4f * (float)Math.Sqrt(full)); h.LaughsPerMinute = Math.Max(2, h.LaughsPerMinute * 3); }
            return h;
        }

        // Babble: voices talking in phrases (syllables of shaped vowels over a voiced pulse), muffled as across a hall.
        // It loops: the end is faded into the start. Peak 0.8.
        public static float[] Babble(int seed, int rate = 22050, float seconds = 12f, int voices = 9)
        {
            var rng = new Random(seed);
            int n = (int)(rate * seconds), fade = rate; // (a second's crossfade at the loop)
            var mix = new float[n + fade];
            // Vowels' first three formants (Hz), men's; women's are ~15% higher.
            float[][] vowels = { new[] { 730f, 1090f, 2440f }, new[] { 530f, 1840f, 2480f }, new[] { 270f, 2290f, 3010f }, new[] { 570f, 840f, 2410f },
                new[] { 300f, 870f, 2240f }, new[] { 660f, 1720f, 2410f }, new[] { 440f, 1020f, 2240f }, new[] { 490f, 1350f, 1690f } };
            for (int v = 0; v < voices; v++)
            {
                bool woman = v % 3 == 1;
                float f0 = woman ? 185f + (float)rng.NextDouble() * 50f : 95f + (float)rng.NextDouble() * 40f, fs = woman ? 1.15f : 1f;
                float gain = 0.5f + (float)rng.NextDouble() * 0.5f, near = 0.35f + (float)rng.NextDouble() * 0.45f; // (near: how much of the high end is left)
                var res = new Resonator[3]; for (int k = 0; k < 3; k++) res[k] = new Resonator();
                float lp = 0, phase = 0, nseed = (float)rng.NextDouble();
                int i = (int)(rng.NextDouble() * rate * 2); // (they don't all start at once)
                float[] cur = vowels[rng.Next(vowels.Length)], target = cur;
                while (i < n + fade)
                {
                    // A phrase: some syllables, rising then falling in pitch, then a pause.
                    int syll = 3 + rng.Next(12); float lift = 0.1f + (float)rng.NextDouble() * 0.3f, key = 0.85f + (float)rng.NextDouble() * 0.3f; // (each phrase in its own key)
                    for (int s = 0; s < syll && i < n + fade; s++)
                    {
                        int len = (int)(rate * (0.11f + (float)rng.NextDouble() * 0.17f));
                        target = vowels[rng.Next(vowels.Length)];
                        float loud = 0.55f + (float)rng.NextDouble() * 0.45f, at = s / (float)syll;
                        float pitch = f0 * key * (1f + lift * (float)Math.Sin(Math.PI * at) - 0.15f * at) * (0.92f + 0.16f * (float)rng.NextDouble()), glide = ((float)rng.NextDouble() - 0.5f) * 0.25f;
                        bool hiss = rng.NextDouble() < 0.35; // (a consonant: a short breath of noise at the start)
                        for (int j = 0; j < len && i < n + fade; j++, i++)
                        {
                            float t = j / (float)len;
                            float env = (float)Math.Sin(Math.PI * Math.Min(1f, t * 1.15f)); env *= env;
                            if ((j & 31) == 0)
                            {
                                cur = Blend(cur, target, 0.18f);
                                for (int k = 0; k < 3; k++) res[k].Tune(cur[k] * fs, 60f + 30f * k, rate);
                            }
                            phase += pitch * (1f + glide * t + 0.01f * (float)Math.Sin(i * 0.0021f + nseed * 9)) / rate;
                            if (phase >= 1f) phase -= 1f;
                            float pulse = phase < 0.6f ? 0.5f * (1f - (float)Math.Cos(Math.PI * phase / 0.6f)) : (float)Math.Cos(Math.PI * (phase - 0.6f) / 0.8f);
                            float src = pulse - 0.4f;
                            if (hiss && t < 0.18f) src += ((float)rng.NextDouble() - 0.5f) * 1.4f * (1f - t / 0.18f);
                            float y = res[0].Run(src) + 0.6f * res[1].Run(src) + 0.25f * res[2].Run(src);
                            lp += near * (y - lp);
                            mix[i] += lp * env * loud * gain;
                        }
                    }
                    i += (int)(rate * (0.25f + (float)rng.NextDouble() * (rng.NextDouble() < 0.3 ? 2.5f : 0.9f)));
                }
            }
            // Muffled a little, the deep hum taken off (as heard across a hall), and gently squeezed: a crowd, not one loud voice.
            // (The room's echo is the game's: an AudioReverbFilter on the source.)
            float l1 = 0, hp = 0, prev = 0, peak = 1e-6f;
            for (int i = 0; i < mix.Length; i++)
            {
                l1 += 0.6f * (mix[i] - l1);
                hp = 0.97f * (hp + l1 - prev); prev = l1;
                mix[i] = (float)Math.Tanh(hp * 1.5f);
            }
            var output = new float[n];
            for (int i = 0; i < n; i++)
            {
                output[i] = mix[i];
                if (i < fade) { float k = i / (float)fade; output[i] = mix[i] * k + mix[n + i] * (1 - k); }
            }
            float mean = 0; foreach (float x in output) mean += x; mean /= n;
            for (int i = 0; i < n; i++) { output[i] -= mean; peak = Math.Max(peak, Math.Abs(output[i])); }
            for (int i = 0; i < n; i++) output[i] *= 0.8f / peak;
            return output;
        }
        private static float[] Blend(float[] a, float[] b, float k) => new[] { a[0] + (b[0] - a[0]) * k, a[1] + (b[1] - a[1]) * k, a[2] + (b[2] - a[2]) * k };
        // A two-pole resonator (a formant).
        private sealed class Resonator
        {
            private float _a1, _a2, _g, _y1, _y2;
            public void Tune(float f, float bandwidth, int rate)
            {
                double r = Math.Exp(-Math.PI * bandwidth / rate), w = 2 * Math.PI * Math.Min(f, rate * 0.45f) / rate;
                _a1 = (float)(2 * r * Math.Cos(w)); _a2 = (float)(-r * r); _g = (float)(1 - r);
            }
            public float Run(float x) { float y = _g * x + _a1 * _y1 + _a2 * _y2; _y2 = _y1; _y1 = y; return y; }
        }
    }
}
