using System;
using System.Collections.Generic;
using System.Linq;

namespace Jarldom
{
    // The Thing's judgement on a thief, as the old law had it: the bót (a fine: their next wages go to the treasury), a season's
    // outlawry (sent off for some days, back after with all they had), full outlawry (exile), or forgiveness. A thief fined once and
    // stealing again isn't fined twice. Why they stole is kept by each theft (the worst of what weighed on them then), so the Thing
    // hears it: a thief steals only when miserable, and the cause may want mending more than the thief.
    public static class Justice
    {
        public const string WhyKey = "stolewhy:", FineKey = "fine", JudgedKey = "judged", OutlawPrefix = "outlaw:";
        public const int OutlawDays = 3;

        // What weighed on them most, short and lowercase: "unpaid", "hungry", "a friend lost to Hel".
        public static List<string> Why(VillagerState v, double now) =>
            Mood.Reasons(v, now).Where(r => r.value <= -4).Take(2).Select(r => Short(r.text)).ToList();
        public static string Short(string text)
        {
            int x = text.IndexOf(" (x", StringComparison.Ordinal); if (x > 0) text = text.Substring(0, x); // (stacks: "Unpaid (x2)")
            int colon = text.IndexOf(':'); if (colon > 0) text = text.Substring(0, colon); // ("Restless: nothing worthy...")
            return text.Length > 0 ? char.ToLowerInvariant(text[0]) + text.Substring(1) : text;
        }
        // The causes over all their thefts, the commonest first: "unpaid and hungry".
        public static string Causes(VillagerState v)
        {
            var why = v.Counts.Where(kv => kv.Key.StartsWith(WhyKey) && kv.Value > 0).OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key).Take(3).Select(kv => kv.Key.Substring(WhyKey.Length)).ToList();
            return why.Count <= 1 ? why.FirstOrDefault() ?? "" : string.Join(", ", why.Take(why.Count - 1)) + " and " + why[why.Count - 1];
        }

        // The bót: a payday's wages, kept back from their pay into the treasury.
        public static int Fine(VillagerState v, int guardExtra = 0) => (Tables.Wage(v.Prof, v.Has("greedy")) + (v.Prof == Prof.Guard ? guardExtra : 0)) * Payroll.EveryDays;
        // Of a wage due, what's kept back for a fine still owed (and what of it is left owing).
        public static int Dock(VillagerState v, int wage)
        {
            int owed = v.Count(FineKey);
            if (owed <= 0 || wage <= 0) return 0;
            int docked = Math.Min(owed, wage);
            v.Counts[FineKey] = owed - docked;
            return docked;
        }
        // Fined once and stealing again: not fined twice.
        public static bool Fineable(VillagerState v) => v.Count(JudgedKey) == 0;

        // Judged (fined or outlawed): their slate wiped, so the matter is closed until they steal again.
        public static void Judged(VillagerState v)
        {
            v.Counts["thefts"] = 0;
            foreach (string k in v.Counts.Keys.Where(k => k.StartsWith(WhyKey)).ToList()) v.Counts.Remove(k);
            v.Bump(JudgedKey);
        }

        public static string OutlawCode(double seconds) => OutlawPrefix + seconds.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
        public static bool Decode(string why, out double seconds)
        {
            seconds = 0;
            return why != null && why.StartsWith(OutlawPrefix) && double.TryParse(why.Substring(OutlawPrefix.Length), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out seconds);
        }
    }
}
