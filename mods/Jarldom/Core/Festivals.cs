using System;
using System.Collections.Generic;
using System.Linq;

namespace Jarldom
{
    // The four festivals of the year (Calendar): what each wants brought to the great hall's chests in the two days before, the
    // blessing it leaves (until the next festival is held or let slip), and how well it was readied. Readied a quarter or more, it's
    // held at dusk on its day and the goods are used; the blessing is as strong as the readying (a tenth at least of its full strength
    // is never given: a quarter readied is a quarter's blessing). Less, it's let slip: the colony is unhappy for two days and the god
    // it's kept for is a little displeased.
    public static class Festivals
    {
        public const float Held = 0.25f;
        public const int NeglectDays = 2;
        // Goods by kind: wood, dandelion, food (anything edible), mead, sacrifice (a trophy or raw meat).
        public static (string kind, string label, int amount)[] Needs(Festival f, int population)
        {
            int pop = Math.Max(1, population);
            switch (f)
            {
                case Festival.Sigrblot: return new[] { ("sacrifice", "trophies or raw meat for the sacrifice", Math.Max(3, (pop + 2) / 3)), ("mead", "mead", Math.Max(2, (pop + 3) / 4)) };
                case Festival.Midsummer: return new[] { ("wood", "wood for the bonfire", 40), ("dandelion", "dandelions", 10), ("food", "food", pop * 2) };
                case Festival.WinterNights: return new[] { ("food", "food for the dead", pop * 2), ("mead", "mead", Math.Max(2, (pop + 1) / 2)) };
                default: return new[] { ("wood", "wood for the Yule log", 50), ("food", "food", pop * 3), ("mead", "mead", pop) }; // (Yule)
            }
        }
        // How well readied, 0..1: each want met counts alike (half the wood and all else: five sixths).
        public static float Readied(IEnumerable<(string kind, string label, int amount)> needs, Func<string, int> have)
        {
            var list = needs.ToList();
            return list.Count == 0 ? 1f : list.Average(n => Math.Min(1f, have(n.kind) / (float)Math.Max(1, n.amount)));
        }
        public static string God(Festival f) => f == Festival.Sigrblot ? "odin" : f == Festival.Midsummer ? "freyja" : f == Festival.WinterNights ? "hel" : "frigg";
        public static string Blessing(Festival f) => f == Festival.Sigrblot ? "fighters strike harder and trade runs bring more"
            : f == Festival.Midsummer ? "everyone works faster and is merrier" : f == Festival.WinterNights ? "worth grows faster, toward Valhalla" : "warmth and good cheer in every house";
        // The blessing's full strength (at full readying): a fraction more of damage / trade / work / worth.
        public const float SigrDamage = 0.10f, SigrTrade = 0.10f, MidsummerWork = 0.05f, WinterWorth = 0.20f;

        // Kept in the colony's flags: which festival was last held and how strong its blessing (percent), or which was let slip.
        public static void Hold(Dictionary<string, int> flags, Festival f, float readied, int day)
        {
            flags["fest:last"] = (int)f + 1; flags["fest:power"] = (int)Math.Round(Math.Max(0, Math.Min(1, readied)) * 100); flags["fest:day"] = day;
            flags.Remove("fest:neglect");
        }
        public static void Neglect(Dictionary<string, int> flags, Festival f, int day)
        {
            flags["fest:last"] = (int)f + 1; flags["fest:power"] = 0; flags["fest:day"] = day; flags["fest:neglect"] = day;
        }
        // The blessing on the colony now: the festival and its strength (0..1), or none.
        public static (Festival? festival, float power) Active(Dictionary<string, int> flags)
        {
            if (!flags.TryGetValue("fest:last", out int last) || last <= 0 || !flags.TryGetValue("fest:power", out int p) || p <= 0) return (null, 0);
            return ((Festival)(last - 1), p / 100f);
        }
        public static float Of(Dictionary<string, int> flags, Festival f, float full) { var (a, p) = Active(flags); return a == f ? full * p : 0; }
        public static bool Neglected(Dictionary<string, int> flags, int day) => flags.TryGetValue("fest:neglect", out int d) && day - d < NeglectDays;
        // Already held (or let slip) today: once a festival.
        public static bool Done(Dictionary<string, int> flags, int day) => flags.TryGetValue("fest:day", out int d) && d == day;
    }
}
