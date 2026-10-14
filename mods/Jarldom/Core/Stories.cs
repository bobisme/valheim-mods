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
        // The same entry is always told the same way: its day and its doer pick the telling.
        private static string Telling(LogEntry e, params string[] tellings) => tellings[(e.Day + e.A.Length) % tellings.Length];

        // One saga line for an event, or null for events not worth a line.
        public static string Line(LogEntry e)
        {
            switch (e.Kind)
            {
                case "found": return Telling(e,
                    $"{e.A} raised the high-seat pillars, and a hall was founded.",
                    $"{e.A} carved the high-seat pillars, and a hall was founded.",
                    $"{e.A} set the first posts, and a hall was founded.",
                    $"{e.A} drove the first stake, and the hall was founded.",
                    $"The high-seat pillars were raised by {e.A}, and a hall was founded.");
                case "arrive": return Telling(e,
                    $"{e.A} came to the hall on a Valkyrie's wings.",
                    $"{e.A} came to the hall with a Valkyrie at their back.",
                    $"{e.A} came to the hall, sent down by the Valkyries.",
                    $"{e.A} came to the hall, set down by a Valkyrie at dusk.",
                    $"{e.A} came to the hall, and the hearth was glad of them.");
                case "refuse": return Telling(e,
                    $"{e.A} was turned away and flew on.",
                    $"{e.A} was turned away at the door and flew on.",
                    $"The hall had no room for {e.A}, and they flew on.",
                    $"{e.A} was refused and flew on.",
                    $"{e.A} was sent back to the sky, the hall being full.");
                case "death":
                {
                    string to = string.IsNullOrEmpty(e.B) ? "" : " to " + e.B;
                    return Telling(e,
                        $"{e.A} fell{to}, and rose again at their bed.",
                        $"{e.A} fell{to}, and woke again at their own bed.",
                        $"{e.A} fell{to}, and got up again at their bed.",
                        $"{e.A} fell{to}, and rose again in their own bed.",
                        $"{e.A} fell{to}, and soon stood up again by their bed.");
                }
                case "lost":
                {
                    string by = string.IsNullOrEmpty(e.B) ? "" : " at the hands of " + e.B;
                    return Telling(e,
                        $"{e.A} was lost to Hel{by}.",
                        $"{e.A} was taken by Hel{by}.",
                        $"{e.A} went down to Hel{by}.",
                        $"Hel took {e.A}{by}.",
                        $"{e.A} was lost to the cold of Hel{by}.");
                }
                case "ascend": return Telling(e,
                    $"{e.A} went to Valhalla on a burning ship.",
                    $"{e.A} sailed to Valhalla on a burning ship.",
                    $"{e.A} rode to Valhalla on a burning ship.",
                    $"{e.A} was sent to Valhalla in a burning boat.",
                    $"{e.A} went to Valhalla, the ship burning under them.");
                case "kill": return null;
                case "starkill": return Telling(e,
                    $"{e.A} slew a mighty {e.B}.",
                    $"{e.A} felled a mighty {e.B}.",
                    $"{e.A} struck down a mighty {e.B}.",
                    $"The mighty {e.B} fell to {e.A}.",
                    $"{e.A} won the day against a mighty {e.B}.");
                case "raid": return Telling(e,
                    $"A raid came: {e.B}.",
                    $"A raid fell upon us: {e.B}.",
                    $"A raiding party came: {e.B}.",
                    $"A raid was on the way: {e.B}.",
                    $"A raid came to the shore: {e.B}.");
                case "raidheld": return Telling(e,
                    "The raid was held.",
                    "The raid was beaten back.",
                    "The raid was held off at the wall.",
                    "The raiders were turned back at the gate.",
                    "The raid was turned back at the wall.");
                case "feast":
                {
                    string s = string.IsNullOrEmpty(e.A) ? "" : ", and " + e.A + " ate the most";
                    return Telling(e,
                        $"A feast was held in the hall{s}.",
                        $"A great feast was laid in the hall{s}.",
                        $"The mead flowed at a feast in the hall{s}.",
                        $"A feast was thrown in the hall{s}.",
                        $"The hall feasted{s}.");
                }
                case "slew": return Telling(e,
                    $"{e.A} slew {e.B}.",
                    $"{e.A} brought down {e.B}.",
                    $"{e.A} made an end of {e.B}.");
                case "home": return Telling(e,
                    $"{e.A} came home after {e.B} days away.",
                    $"{e.A} was home again, {e.B} days gone.",
                    $"After {e.B} days away, {e.A} came home.");
                case "fest": return $"The colony kept {e.B}{e.Text}.";
                case "verse": return $"{e.A} sang: “{e.Text.Replace(" / ", " / ")}”";
                case "byname": return Telling(e,
                    $"{e.A} was named {e.B}.",
                    $"{e.A} was given the name {e.B}.",
                    $"{e.A} earned the name {e.B}.",
                    $"The name {e.B} was given to {e.A}.",
                    $"The hall named {e.A} {e.B}.");
                case "payday": return Telling(e,
                    $"Wages were paid: {e.B} coins.",
                    $"Wages were counted out: {e.B} coins.",
                    $"Payday came, and {e.B} coins were paid out.",
                    $"Wages went out: {e.B} coins.",
                    $"Payday: {e.B} coins went to the hall's folk.");
                case "unpaid": return Telling(e,
                    "Payday came and the treasury was short.",
                    "Payday came, and the chests came up short.",
                    "The treasury was short on payday.",
                    "Payday came and the coins ran short.",
                    "The wages could not all be paid.");
                case "rank": return Telling(e,
                    $"The colony became a {e.B}.",
                    $"The hall grew into a {e.B}.",
                    $"The colony rose to be a {e.B}.",
                    $"The colony was named a {e.B}.",
                    $"The settlement is now a {e.B}.");
                case "room": return Telling(e,
                    $"A {e.B} was hallowed.",
                    $"A {e.B} was blessed and made ready.",
                    $"A {e.B} was raised and hallowed.",
                    $"The {e.B} was consecrated.",
                    $"A {e.B} was hallowed for use.");
                case "brawl": return Telling(e,
                    $"{e.A} and {e.B} came to blows in the hall.",
                    $"{e.A} and {e.B} fell to fighting in the hall.",
                    $"{e.A} and {e.B} traded blows in the hall.",
                    $"{e.A} and {e.B} squared off in the hall.",
                    $"{e.A} and {e.B} brawled in the hall.");
                case "rest": return Telling(e,
                    $"{e.B} brought {e.A} home from Hel's road and laid them to rest in the burial ground.",
                    $"{e.A}, who was lost, was laid to rest at last by {e.B}.",
                    $"{e.B} laid {e.A}'s remains in the burial ground; their friends grieve no more.",
                    $"The draugr that was {e.A} walks no more: {e.B} laid them to rest.",
                    $"{e.A} rests with Hel now, laid in the earth by {e.B}.");
                case "theft": return Telling(e,
                    $"{e.A} was caught stealing.",
                    $"{e.A} was caught stealing from the stores.",
                    $"{e.A} was caught pocketing what was not theirs.",
                    $"{e.A} was caught thieving in the hall.",
                    $"{e.A} was caught with a hand in the stores.");
                case "ask": return Telling(e,
                    $"{e.A} asked a thing of the jarl: {e.Text}.",
                    $"{e.A} came to the jarl with an ask: {e.Text}.",
                    $"{e.A} asked the jarl for {e.Text}.",
                    $"{e.A} has a thing to ask of the jarl: {e.Text}.",
                    $"The jarl was asked by {e.A} for {e.Text}.");
                case "asked": return Telling(e,
                    $"{e.B} saw to what {e.A} asked: {e.Text}.",
                    $"{e.B} did as {e.A} asked: {e.Text}.",
                    $"{e.A} asked for {e.Text}, and {e.B} saw to it.",
                    $"{e.B} kept faith with {e.A}: {e.Text}.",
                    $"What {e.A} asked, {e.B} did: {e.Text}.");
                case "deed": return Telling(e,
                    $"{e.B} did a deed in {e.A}'s name ({e.Text}), and {e.A}'s shame was washed out.",
                    $"{e.A} was disgraced, until {e.B} did a deed for them: {e.Text}. They hold their head up again.",
                    $"For {e.A}'s sake {e.B} saw to {e.Text}, and the hall no longer whispers about {e.A}.",
                    $"{e.B} stood by {e.A} when they fell low ({e.Text}); the disgrace is gone.",
                    $"The ash is washed off {e.A}: {e.B} did the deed they asked, {e.Text}.");
                case "ring": return Telling(e,
                    $"{e.B} gave {e.A} a gold arm ring.",
                    $"{e.B} gifted {e.A} a gold arm ring.",
                    $"{e.A} was given a gold arm ring by {e.B}.",
                    $"A gold arm ring went from {e.B} to {e.A}.",
                    $"{e.B} put a gold arm ring on {e.A}.");
                case "offering": return Telling(e,
                    $"A gift was laid before {e.B}.",
                    $"A bowl of food was offered to {e.B}.",
                    $"The hall made an offering to {e.B}.",
                    $"A portion was set aside for {e.B}.",
                    $"The altar of {e.B} received an offering.");
                case "festival": return Telling(e,
                    $"A festival was held for {e.B}.",
                    $"The hall kept a festival for {e.B}.",
                    $"A feast-day was kept for {e.B}.",
                    $"The people held a festival for {e.B}.",
                    $"A festival was kept for {e.B}.");
                case "sworn": return Telling(e,
                    $"{e.A} and {e.B} swore brotherhood.",
                    $"{e.A} and {e.B} became sworn kin.",
                    $"{e.A} and {e.B} mixed blood and swore brotherhood.",
                    $"{e.A} and {e.B} sealed a sworn oath.",
                    $"{e.A} and {e.B} pledged brotherhood before the hall.");
                case "desert": return Telling(e,
                    $"{e.A} deserted in the night.",
                    $"{e.A} slipped away in the night.",
                    $"{e.A} left the hall in the dark and did not come back.",
                    $"{e.A} deserted under cover of night.",
                    $"{e.A} was gone by morning, having deserted.");
                case "exile": return Telling(e,
                    $"{e.A} was exiled by the Thing.",
                    $"The Thing exiled {e.A}.",
                    $"{e.A} was cast out by the Thing.",
                    $"{e.A} was outlawed by the Thing.",
                    $"The Thing sent {e.A} out of the colony.");
                case "thing":
                {
                    string s = string.IsNullOrEmpty(e.B) ? "" : ": " + e.B;
                    return Telling(e,
                        $"The Thing was held{s}.",
                        $"The Thing met{s}.",
                        $"The folk gathered at the Thing{s}.",
                        $"Judgment was given at the Thing{s}.",
                        $"The Thing sat{s}.");
                }
                case "tribute": return Telling(e,
                    $"The Fulings were paid {e.B} coins to go away.",
                    $"The Fulings were bought off with {e.B} coins.",
                    $"{e.B} coins went to the Fulings to buy peace.",
                    $"The Fulings took {e.B} coins and went away.",
                    $"The Fulings were paid {e.B} coins to leave us be.");
                case "worthy": return Telling(e,
                    $"{e.A} was found worthy of Valhalla.",
                    $"{e.A} was judged worthy of Valhalla.",
                    $"The gods found {e.A} worthy of Valhalla.",
                    $"{e.A} was counted worthy of Valhalla.",
                    $"{e.A} has been marked worthy of Valhalla.");
                case "injured": return Telling(e,
                    $"{e.A} was wounded.",
                    $"{e.A} took a wound.",
                    $"{e.A} was hurt badly.",
                    $"{e.A} was laid low by a wound.",
                    $"{e.A} came back from the fight wounded.");
                case "inspired": return Telling(e,
                    $"{e.A} woke inspired.",
                    $"{e.A} woke up inspired.",
                    $"{e.A} woke with a song in their head.",
                    $"{e.A} woke full of fire.",
                    $"{e.A} woke with new ideas.");
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
        private static readonly string[] Openers =
        {
            "{0} says", "{0} swears", "{0} heard that", "{0} laughs that", "{0} won't stop saying", "{0} whispers that", "{0} is sure that",
            "{0} claims", "{0} mutters that", "{0} tells anyone who will listen that", "{0} bets that", "{0} shouts that",
            "{0} has it from a friend that", "{0} is certain that", "{0} can't believe that", "{0} heard it said that", "{0} wagers that",
        };
        // told: what the hall has gossiped about lately (newest last), so a crowd doesn't all tell the same thing at once.
        public static string Gossip(Random rng, string speaker, IEnumerable<LogEntry> log, double now, List<string> told = null)
        {
            var recent = log.Where(e => now - e.Time <= Clock.DayLength * 2 && Line(e) != null && e.A != speaker && e.Kind != "text" && e.Kind != "payday" && e.Kind != "verse").ToList(); // (the verse was sung for all to hear: not passed on as news)
            if (recent.Count == 0) return null;
            string Key(LogEntry x) => $"{x.Time:0.#}|{x.Kind}|{x.A}";
            var fresh = told == null ? recent : recent.Where(x => !told.Contains(Key(x))).ToList();
            if (fresh.Count == 0) fresh = recent;
            LogEntry e = fresh[rng.Next(fresh.Count)];
            if (told != null) { told.Remove(Key(e)); told.Add(Key(e)); while (told.Count > Math.Max(1, recent.Count - 1) || told.Count > 8) told.RemoveAt(0); }
            string line = Line(e);
            if (line.StartsWith("A ") || line.StartsWith("The ") || line.StartsWith("Wages") || line.StartsWith("Payday")) line = char.ToLower(line[0]) + line.Substring(1); // names keep their capital
            return string.Format(Openers[rng.Next(Openers.Length)], speaker) + " " + line;
        }
    }

    // The Thing's petitions: each made from the colony as it is, with what each ruling would do.
    public sealed class Petition
    {
        public string Id, Text, Yes, No; public string About = ""; public int YesMood, NoMood, YesFavour; public string YesDeed = "";
        public string By = "", ByName = ""; public int Day; // who raised it (vid, name), and when
        public string Key => Id + "|" + About;
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
        public const int Most = 6, UnheardAfter = 5;
        // The day's change to what waits for the Thing: what no longer stands is withdrawn (the beds were built, the thief is gone),
        // and at most one new matter is raised a day, up to Most waiting.
        public static (List<Petition> raised, List<Petition> withdrawn) Update(List<Petition> pending, List<Petition> standing, int day, Random rng)
        {
            var keys = new HashSet<string>(standing.Select(p => p.Key));
            var withdrawn = pending.Where(p => !keys.Contains(p.Key)).ToList();
            var raised = new List<Petition>();
            var waiting = new HashSet<string>(pending.Select(p => p.Key));
            if (pending.Count - withdrawn.Count < Most)
            {
                Petition fresh = standing.Where(p => !waiting.Contains(p.Key)).OrderBy(_ => rng.Next()).FirstOrDefault();
                if (fresh != null) { fresh.Day = day; raised.Add(fresh); }
            }
            return (raised, withdrawn);
        }
        // Days the oldest petition has waited unheard.
        public static int Waited(IEnumerable<Petition> pending, int day) => pending.Select(p => day - p.Day).DefaultIfEmpty(0).Max();
        // A petition's teller: one it touches first (a guard for the guards' pay, one without a bed for beds), else anyone; never the
        // one it accuses.
        public static string Speaker(Petition p, IList<(string vid, string name, string prof, bool bedless)> people, Random rng)
        {
            var can = people.Where(x => !p.About.Split('|').Contains(x.name)).ToList();
            if (can.Count == 0) return "";
            var first = p.Id == "guardpay" ? can.Where(x => x.prof == "Guard").ToList() : p.Id == "beds" ? can.Where(x => x.bedless).ToList() : new List<(string vid, string name, string prof, bool bedless)>();
            var from = first.Count > 0 ? first : can;
            return from[rng.Next(from.Count)].vid;
        }
    }
}
