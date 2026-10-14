using System;
using System.Collections.Generic;
using System.Linq;

namespace Jarldom
{
    // What each jarl (player) has done for a colony, kept in its flags ("tally:<player>|<what>" = n): bosses slain, arm rings given,
    // raids fought at home, ascensions overseen, feasts held, days away. The record's shape doesn't change, so games of other versions
    // still read and write it.
    public static class Tally
    {
        public const string Prefix = "tally:";
        public const string Bosses = "bosses", Rings = "rings", Raids = "raids", Ascensions = "ascensions", Feasts = "feasts", Away = "away",
            LastAway = "lastaway", Seen = "seen", Back = "back", Bane = "bane", Title = "title";
        public const int AbsentFades = 10;
        public static string Key(string player, string what) => Prefix + player + "|" + what;
        public static int Get(IDictionary<string, int> flags, string player, string what) => flags.TryGetValue(Key(player, what), out int n) ? n : 0;
        public static void Set(IDictionary<string, int> flags, string player, string what, int n) { if (n == 0) flags.Remove(Key(player, what)); else flags[Key(player, what)] = n; }
        public static int Add(IDictionary<string, int> flags, string player, string what, int by = 1) { int n = Get(flags, player, what) + by; Set(flags, player, what, n); return n; }
        // The players the colony keeps a tally for.
        public static List<string> Players(IDictionary<string, int> flags) => flags.Keys.Where(k => k.StartsWith(Prefix) && k.IndexOf('|') > Prefix.Length)
            .Select(k => k.Substring(Prefix.Length, k.LastIndexOf('|') - Prefix.Length)).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToList();
        public static Dictionary<string, int> Of(IDictionary<string, int> flags, string player)
        {
            string head = Prefix + player + "|";
            return flags.Where(kv => kv.Key.StartsWith(head)).ToDictionary(kv => kv.Key.Substring(head.Length), kv => kv.Value);
        }

        // A boss slain: counted, and the mightiest so far remembered (by its place in Bynames.Bosses).
        public static void Boss(IDictionary<string, int> flags, string player, string prefab)
        {
            Add(flags, player, Bosses);
            int tier = Bynames.Tier(prefab);
            if (tier > Get(flags, player, Bane)) Set(flags, player, Bane, tier);
        }
        // Seen at home on this day: a stretch away of two days or more is counted (and remembered as the last one).
        public static int Home(IDictionary<string, int> flags, string player, int day)
        {
            int seen = Get(flags, player, Seen), away = seen > 0 ? day - seen : 0;
            Set(flags, player, Seen, Math.Max(1, day));
            if (away < 2)
            {
                // ("the Absent" is forgotten ten days after coming back)
                if (Get(flags, player, Back) > 0 && day - Get(flags, player, Back) >= AbsentFades) { Set(flags, player, LastAway, 0); Set(flags, player, Back, 0); }
                return 0;
            }
            Add(flags, player, Away, away);
            Set(flags, player, LastAway, away); Set(flags, player, Back, day);
            return away;
        }
    }

    // A jarl's byname from their tally: the highest title they qualify for.
    public static class Bynames
    {
        // The bosses in the order the world gives them (a "-bane" for the mightiest slain).
        public static readonly (string prefab, string name)[] Bosses = { ("Eikthyr", "Eikthyr"), ("gd_king", "Elder"), ("Bonemass", "Bonemass"), ("Dragon", "Moder"), ("GoblinKing", "Yagluth"), ("SeekerQueen", "Queen"), ("Fader", "Fader") };
        public static int Tier(string prefab) { int i = Array.FindIndex(Bosses, b => b.prefab.Equals(prefab, StringComparison.OrdinalIgnoreCase)); return i + 1; } // (0: not a boss we know)
        public const int Rings = 5, Raids = 5, Ascensions = 3, Feasts = 5, Absent = 20, NotableBane = 3; // (Bonemass and on outrank the rest)

        // (id, title, kind): the id is kept in the tally to notice a change; the kind keys the co-jarl comparisons.
        public static (int id, string title, string kind) For(IDictionary<string, int> t)
        {
            int Get(string k) => t.TryGetValue(k, out int v) ? v : 0;
            int bane = Get(Tally.Bane);
            if (bane >= NotableBane && bane <= Bosses.Length) return (10 + bane, Bosses[bane - 1].name + "-bane", "bane");
            if (Get(Tally.Rings) >= Rings) return (2, "Ring-giver", "rings");
            if (Get(Tally.Raids) >= Raids) return (3, "Shield of the hall", "raids");
            if (Get(Tally.Ascensions) >= Ascensions) return (4, "Soul-guide", "ascensions");
            if (Get(Tally.Feasts) >= Feasts) return (5, "Feast-giver", "feasts");
            if (bane >= 1 && bane <= Bosses.Length) return (10 + bane, Bosses[bane - 1].name + "-bane", "bane");
            if (Get(Tally.LastAway) >= Absent) return (1, "the Absent", "away");
            return (0, "", "");
        }
        // What a byname says they do, for small talk ("gives rings").
        public static string Deed(string kind, string title)
        {
            switch (kind)
            {
                case "bane": return "brought down " + title.Replace("-bane", "");
                case "rings": return "gives rings";
                case "raids": return "holds the wall";
                case "ascensions": return "sends our dead to Valhalla";
                case "feasts": return "fills the hall with feasts";
                case "away": return "is never home";
                default: return "";
            }
        }
        // Two jarls compared by a villager: the one they think better of has the last word (or both praised, if it's close). Null unless
        // both have bynames.
        public static string Compare(string a, (int id, string title, string kind) ta, int opA, string b, (int id, string title, string kind) tb, int opB, Random rng)
        {
            if (ta.id == 0 || tb.id == 0 || a == b) return null;
            string da = Deed(ta.kind, ta.title), db = Deed(tb.kind, tb.title);
            if (Math.Abs(opA - opB) < 15)
                return new[] { $"{a} {da}, and {b} {db}. We're well served.", $"Two jarls: {a} {da}, {b} {db}. Lucky hall.", $"Between {a}, who {da}, and {b}, who {db}, we want for little." }[rng.Next(3)];
            if (opA > opB) { var (n, d, m, e) = (a, da, b, db); (a, da, b, db) = (m, e, n, d); } // (the favourite last)
            if (ta.kind == "away" && opA > opB || tb.kind == "away" && opA < opB) return $"{(ta.kind == "away" && opA > opB ? b : a)} {Deed("away", "")}. At least {(ta.kind == "away" && opA > opB ? a : b)} is here."; // (absence is no praise)
            return new[] { $"They say {a} {da}, but {b} {db}.", $"{a} {da}, sure. {b} {db}, and that's worth more.", $"Give me {b}, who {db}, over {a}, who {da}." }[rng.Next(3)];
        }
        // "Bob Bonemass-bane", or just "Bob".
        public static string Named(string player, IDictionary<string, int> tally) { string t = For(tally).title; return t == "" ? player : player + " " + t; }
    }
}
