using System;
using System.Collections.Generic;
using System.Linq;

namespace Jarldom
{
    // The memorial stone's names: who went to Valhalla (their deed, from the runestone line the saga keeps), who was lost to Hel and
    // laid to rest, and who was lost and walks still. In the order they went; read a page at a time.
    public sealed class Remembered
    {
        public string Name = "", Deed = ""; public int Day; public bool Hel, Unburied;
    }
    public static class Memorial
    {
        public const int PerPage = 8;
        public static List<Remembered> From(IEnumerable<RosterRow> roster, Dictionary<string, int> flags, IList<LogEntry> log)
        {
            var all = new List<Remembered>();
            foreach (RosterRow r in roster)
            {
                if (r.State == SoulState.Ascended)
                {
                    int day = flags.TryGetValue("ascended:" + r.Vid, out int d) ? d : 0;
                    LogEntry stone = log.LastOrDefault(e => e.Kind == "text" && (e.Text ?? "").StartsWith("A runestone remembers " + r.Name));
                    string deed = stone != null ? stone.Text.Substring(stone.Text.IndexOf(':') + 1).Trim() : $"Went to Valhalla, worth {Math.Round(r.Worth)}.";
                    LogEntry went = log.LastOrDefault(e => e.Kind == "ascend" && (e.A ?? "").StartsWith(r.Name));
                    all.Add(new Remembered { Name = went?.A ?? r.Name, Deed = deed, Day = day > 1 ? day : went?.Day ?? 0 });
                }
                else if (r.State == SoulState.Lost)
                {
                    bool rests = flags.TryGetValue("rested:" + r.Vid, out int rd) && rd != 0;
                    LogEntry rest = log.LastOrDefault(e => e.Kind == "rest" && e.A == r.Name), lost = log.LastOrDefault(e => e.Kind == "lost" && (e.A ?? "").StartsWith(r.Name));
                    all.Add(rests
                        ? new Remembered { Name = r.Name, Hel = true, Day = rd, Deed = $"Lost to Hel{(lost != null && lost.B != "" ? " (" + lost.B + ")" : "")}; laid to rest{(rest != null && rest.B != "" ? " by " + rest.B : "")}. Rests with Hel." }
                        : new Remembered { Name = r.Name, Hel = true, Unburied = true, Day = lost?.Day ?? 0, Deed = $"Lost to Hel{(lost != null && lost.B != "" ? " (" + lost.B + ")" : "")}. Walks still, unburied: they come back by night. Strike them down, and lay their remains in the burial ground." });
                }
            }
            return all.OrderBy(x => x.Day).ThenBy(x => x.Name).ToList();
        }
        public static int Pages(int count) => Math.Max(1, (count + PerPage - 1) / PerPage);
        public static List<T> Page<T>(IList<T> all, int page)
        {
            page = Math.Max(0, Math.Min(page, Pages(all.Count) - 1));
            return all.Skip(page * PerPage).Take(PerPage).ToList();
        }
    }

    // The unburied walk again: a night now and then, one of the lost whose draugr isn't about comes back to the colony's edge, so the
    // jarl can bring them home. One a night, half the nights, each soul at most every other night (the one away longest first).
    public static class Unburied
    {
        public const int Every = 2;
        public static string Walker(IEnumerable<RosterRow> roster, Dictionary<string, int> flags, int day, ICollection<string> about, double roll)
        {
            if (roll >= 0.5) return null;
            return roster.Where(r => r.State == SoulState.Lost && !(flags.TryGetValue("rested:" + r.Vid, out int rd) && rd != 0) && !about.Contains(r.Vid)
                    && day - (flags.TryGetValue("walked:" + r.Vid, out int w) ? w : -100) >= Every)
                .OrderBy(r => flags.TryGetValue("walked:" + r.Vid, out int w) ? w : -100).ThenBy(r => r.Vid, StringComparer.Ordinal).Select(r => r.Vid).FirstOrDefault();
        }
        public static void Walked(Dictionary<string, int> flags, string vid, int day) => flags["walked:" + vid] = day;
    }
}
