using System;
using System.Collections.Generic;
using System.Linq;

namespace Jarldom
{
    // Newcomers: who Odin sends, drawn from a seed so every game makes the same soul.
    public static class Souls
    {
        public static Newcomer Make(int seed, string group, string battle)
        {
            var rng = new Random(seed);
            bool female = rng.Next(2) == 0;
            var traits = new List<string>();
            while (traits.Count < 2)
            {
                string t = Tables.Traits[rng.Next(Tables.Traits.Length)].Id;
                if (traits.All(x => !Tables.Clash(x, t))) traits.Add(t);
            }
            return new Newcomer
            {
                Name = Names.PickName(rng, female), Female = female, Traits = string.Join(",", traits),
                Prof = Tables.Starting[rng.Next(Tables.Starting.Length)], Story = Names.Backstory(rng, female, battle), Seed = seed, Group = group,
            };
        }
        // A group of one to three who fell in the same battle.
        public static List<Newcomer> Group(Random rng, int maxSize)
        {
            int size = Math.Max(1, Math.Min(maxSize, 1 + rng.Next(3)));
            string group = rng.Next(1 << 30).ToString("x");
            string battle = Names.Battle(rng);
            var list = new List<Newcomer>();
            var used = new HashSet<string>();
            for (int i = 0; i < size; i++)
            {
                Newcomer n = Make(rng.Next(), group, battle);
                if (used.Contains(n.Name)) n = Make(rng.Next(), group, battle);
                used.Add(n.Name); list.Add(n);
            }
            return list;
        }
        public static VillagerState Become(Newcomer n, string vid, string colony, double now)
        {
            var v = new VillagerState
            {
                Vid = vid, Colony = colony, Name = n.Name, Female = n.Female, Story = n.Story, Group = n.Group,
                Traits = n.Traits.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).ToList(), Prof = Prof.Hauler, Wants = n.Prof, Arrived = now,
            };
            Mood.Add(v, "arrived", now);
            return v;
        }
        public static string Describe(Newcomer n) =>
            $"{n.Name}, {string.Join(" and ", n.Traits.Split(',').Select(t => Tables.Trait(t)?.Name.ToLower() ?? t))}, would be a {Tables.Of(n.Prof).Name.ToLower()}. {n.Story}";
    }

    // The saga, gossip and petitions: built only from what the event log says happened.
    public static class Stories
    {
        // One saga line for an event, or null for events not worth a line.
        public static string Line(LogEntry e)
        {
            switch (e.Kind)
            {
                case "found": return $"{e.A} raised the high-seat pillars, and a hall was founded.";
                case "arrive": return $"{e.A} came to the hall on a Valkyrie's wings.";
                case "refuse": return $"{e.A} was turned away and flew on.";
                case "death": return $"{e.A} fell{(string.IsNullOrEmpty(e.B) ? "" : " to " + e.B)}, and rose again at their bed.";
                case "lost": return $"{e.A} was lost to Hel{(string.IsNullOrEmpty(e.B) ? "" : " at the hands of " + e.B)}.";
                case "ascend": return $"{e.A} went to Valhalla on a burning ship.";
                case "kill": return null;
                case "starkill": return $"{e.A} slew a mighty {e.B}.";
                case "raid": return $"A raid came: {e.B}.";
                case "raidheld": return $"The raid was held.";
                case "feast": return $"A feast was held in the hall{(string.IsNullOrEmpty(e.A) ? "" : ", and " + e.A + " ate the most")}.";
                case "byname": return $"{e.A} was named {e.B}.";
                case "payday": return $"Wages were paid: {e.B} coins.";
                case "unpaid": return $"Payday came and the treasury was short.";
                case "rank": return $"The colony became a {e.B}.";
                case "room": return $"A {e.B} was hallowed.";
                case "brawl": return $"{e.A} and {e.B} came to blows in the hall.";
                case "theft": return $"{e.A} was caught stealing.";
                case "ring": return $"{e.B} gave {e.A} a gold arm ring.";
                case "offering": return $"An offering was made to {e.B}.";
                case "festival": return $"A festival was held for {e.B}.";
                case "sworn": return $"{e.A} and {e.B} swore brotherhood.";
                case "desert": return $"{e.A} deserted in the night.";
                case "exile": return $"{e.A} was exiled by the Thing.";
                case "thing": return $"The Thing met{(string.IsNullOrEmpty(e.B) ? "" : ": " + e.B)}.";
                case "tribute": return $"The Fulings were paid {e.B} coins to go away.";
                case "worthy": return $"{e.A} was found worthy of Valhalla.";
                case "injured": return $"{e.A} was wounded.";
                case "inspired": return $"{e.A} woke inspired.";
                case "text": return e.Text;
                case "petition": return null;
                default: return string.IsNullOrEmpty(e.Text) ? null : e.Text;
            }
        }

        // The chronicle: the log as saga lines grouped by day, newest day last.
        public static List<string> Chronicle(IEnumerable<LogEntry> log, int maxDays = 30)
        {
            var lines = new List<string>();
            foreach (var day in log.GroupBy(e => e.Day).OrderBy(g => g.Key).Reverse().Take(maxDays).Reverse())
            {
                // (the everyday told once a day: "Odd and Alfhild woke inspired", "Two offerings were made to Odin")
                var said = new List<string>();
                foreach (LogEntry e in day)
                {
                    if (Inspired(e) != null || e.Kind == "offering") continue;
                    string l = Line(e);
                    if (l != null && !said.Contains(l)) said.Add(l);
                }
                var woke = day.Select(Inspired).Where(n => n != null).Distinct().ToList();
                if (woke.Count > 0) said.Add($"{And(woke)} woke inspired.");
                foreach (var g in day.Where(e => e.Kind == "offering").GroupBy(e => e.B))
                    said.Add(g.Count() == 1 ? $"An offering was made to {g.Key}." : $"{Count(g.Count())} offerings were made to {g.Key}.");
                int kills = day.Count(e => e.Kind == "kill");
                if (kills > 0) said.Add(kills == 1 ? "One foe was slain." : $"{kills} foes were slain.");
                if (said.Count == 0) continue;
                lines.Add($"Day {day.Key}: " + string.Join(" ", said));
            }
            return lines;
        }

        private const string Woke = " woke inspired.";
        private static string Inspired(LogEntry e) => e.Kind == "inspired" ? e.A : e.Kind == "text" && e.Text != null && e.Text.EndsWith(Woke) ? e.Text.Substring(0, e.Text.Length - Woke.Length) : null; // (older sagas told it as text)
        public static string And(IList<string> names) => names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[names.Count - 1];
        private static readonly string[] Numbers = { "No", "One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine", "Ten" };
        public static string Count(int n) => n < Numbers.Length ? Numbers[n] : n.ToString();

        // A line of gossip from the last two days, by one villager about another. Only about things that happened.
        private static readonly string[] Openers = { "{0} says", "{0} swears", "{0} heard that", "{0} laughs that", "{0} won't stop saying" };
        public static string Gossip(Random rng, string speaker, IEnumerable<LogEntry> log, double now)
        {
            var recent = log.Where(e => now - e.Time <= Clock.DayLength * 2 && Line(e) != null && e.A != speaker && e.Kind != "text" && e.Kind != "payday").ToList();
            if (recent.Count == 0) return null;
            LogEntry e = recent[rng.Next(recent.Count)];
            string line = Line(e);
            if (line.StartsWith("A ") || line.StartsWith("The ") || line.StartsWith("Wages") || line.StartsWith("Payday")) line = char.ToLower(line[0]) + line.Substring(1); // names keep their capital
            return string.Format(Openers[rng.Next(Openers.Length)], speaker) + " " + line;
        }
    }

    // The Thing's petitions: each made from the colony as it is, with what each ruling would do.
    public sealed class Petition
    {
        public string Id, Text, Yes, No; public string About = ""; public int YesMood, NoMood, YesFavour; public string YesDeed = "";
    }

    public sealed class ColonyView
    {
        public int Population; public bool HasBeautyRoom, HasShrine; public int Treasury, Due; public List<(string name, int thefts, int brawls)> Troublemakers = new List<(string, int, int)>();
        public List<(string a, string b)> Feuds = new List<(string, string)>(); public int GuardWage; public int MissedPaydays; public bool Hungry; public int Beds;
        public List<(string name, int seen)> Suspects = new List<(string, int)>();
    }

    public static class Petitions
    {
        public static List<Petition> Raise(Random rng, ColonyView c, int max = 3)
        {
            var all = new List<Petition>();
            if (!c.HasBeautyRoom) all.Add(new Petition { Id = "beauty", Text = "Build us something fine to look at: a hall worth the name, with banners and carvings.", Yes = "Promise it", No = "Refuse", YesMood = 4, NoMood = -4 });
            foreach (var t in c.Troublemakers.Where(t => t.thefts >= 2))
                all.Add(new Petition { Id = "exile", About = t.name, Text = $"Exile {t.name}: they have stolen {t.thefts} times.", Yes = "Exile them", No = "Forgive", YesMood = 3, NoMood = -3, YesDeed = "exiled" });
            foreach (var f in c.Feuds)
                all.Add(new Petition { Id = "feud", About = f.a + "|" + f.b, Text = $"A blood feud: {f.a} and {f.b} must settle it before the Thing.", Yes = "Make them swear peace", No = "Let them fight it out", YesMood = 2, NoMood = -2 });
            if (c.MissedPaydays > 0) all.Add(new Petition { Id = "wages", Text = "Pay what is owed: the treasury came up short.", Yes = "Swear to pay", No = "Times are hard", YesMood = 5, NoMood = -6 });
            if (c.Population >= 4 && c.GuardWage <= 5) all.Add(new Petition { Id = "guardpay", Text = "Raise the guards' wages: they stand in the cold while others sleep.", Yes = "Raise them", No = "No", YesMood = 3, NoMood = -2 });
            if (!c.HasShrine && c.Population >= 3) all.Add(new Petition { Id = "shrine", Text = "Raise a shrine to Odin, that he keeps watching over us.", Yes = "We will", No = "Not yet", YesMood = 3, NoMood = -3, YesFavour = 3 });
            if (c.Hungry) all.Add(new Petition { Id = "food", Text = "Fill the larder: bellies are empty.", Yes = "Promise food", No = "Tighten your belts", YesMood = 4, NoMood = -5 });
            foreach (var s in c.Suspects.Where(s => s.seen >= 2).OrderByDescending(s => s.seen).Take(1))
                all.Add(new Petition { Id = "accuse", About = s.name, Text = $"{s.name} has been seen near the mischief {s.seen} times. Cast them out?", Yes = "Cast them out", No = "Let it be", YesMood = 2, NoMood = -2 });
            if (c.Beds < c.Population) all.Add(new Petition { Id = "beds", Text = "Build beds: some of us sleep on the hall floor.", Yes = "Promise beds", No = "Sleep by the fire", YesMood = 4, NoMood = -4 });
            return all.OrderBy(_ => rng.Next()).Take(max).ToList();
        }
    }
}
