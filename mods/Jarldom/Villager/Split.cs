using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Jarldom
{
    // Where a villager's decision spent its time, in parts (needs, gear, the task's tick...): for the "slow decision" log line.
    internal static class Split
    {
        private static long _at;
        private static readonly List<(string part, double ms)> Parts = new List<(string, double)>();
        public static void Begin() { Parts.Clear(); _at = Stopwatch.GetTimestamp(); }
        public static void Mark(string part)
        {
            long now = Stopwatch.GetTimestamp();
            Parts.Add((part, (now - _at) * 1000.0 / Stopwatch.Frequency));
            _at = now;
        }
        // (a part inside another, counted on its own too: the piece lists made again)
        public static void Add(string part, long since) => Parts.Add((part, (Stopwatch.GetTimestamp() - since) * 1000.0 / Stopwatch.Frequency));
        public static string Said() => string.Join(" ", Parts.Where(p => p.ms >= 0.5).Select(p => $"{p.part} {p.ms:0.0}"));
    }
}
