using System;
using System.Collections.Generic;
using System.Linq;

namespace Jarldom
{
    // The skald's verse for a night: two to four lines from the day's saga, the weightiest deeds first, each kind once, each line
    // alliterating on the name it sings (words chosen to share its first sound). The same day and seed give the same verse.
    public static class Verse
    {
        // (kind: weight) - what the hall wants sung first.
        private static readonly Dictionary<string, int> Weight = new Dictionary<string, int>
        {
            ["slew"] = 10, ["home"] = 9, ["raidheld"] = 8, ["ascend"] = 8, ["lost"] = 7, ["death"] = 7, ["rest"] = 6, ["arrive"] = 6,
            ["byname"] = 5, ["feast"] = 5, ["fest"] = 6, ["lore"] = 4, ["first"] = 7, ["graveitem"] = 8, ["marvel"] = 5, ["einherjar"] = 9, ["barrowjarl"] = 10, ["beacon"] = 6, ["champion"] = 4, ["starkill"] = 4, ["ring"] = 4, ["worthy"] = 3,
        };
        public static bool Sings(string kind) => Weight.ContainsKey(kind);

        // Words for alliteration, by first letter (a line picks one that starts like the name it's about, else any).
        private static readonly string[] Bold = { "bold", "battle-glad", "bright-helmed", "brave", "broad-shouldered", "fearless", "far-famed", "fierce", "grim", "gold-giving", "hard", "high-hearted",
            "keen", "kind", "lion-hearted", "strong", "steadfast", "stern", "storm-tested", "swift", "true", "tireless", "valiant", "wise", "wolf-hearted", "red-handed", "ready", "spear-skilled",
            "doughty", "dread", "noble", "iron-willed", "eager", "ever-ready", "mighty", "moon-pale", "proud", "ring-rich", "oak-strong", "young", "unbowed", "cunning", "clever", "zealous", "quiet",
            "lucky", "loyal", "open-handed", "able", "awful", "gentle", "honest", "wary", "wild", "patient", "joyful", "jaunty", "vengeful", "thrifty", "thane-worthy", "hale", "heavy-handed", "nimble",
            "silent", "sharp-eyed", "battle-bright", "blood-red", "fair", "free", "gallant", "good", "kin-proud", "careful", "unafraid", "able-bodied", "ash-strong", "eagle-eyed", "elm-tall", "ice-hard" };
        private static readonly string[] Felled = { "felled", "slew", "struck down", "brought low", "broke", "battered down", "beat down", "laid low", "ended", "overcame", "made an end of",
            "cut down", "smote", "sent to Hel", "hewed down", "toppled", "threw down", "mastered", "quelled", "ruined", "gave to the ravens", "won against", "killed" };
        private static readonly string[] Sea = { "the black-prowed wave", "the whale-road", "the swan-road", "the grey sea", "the gull's bath", "the sail-road", "the storm-tossed sea", "the long road",
            "the high hills", "the wide world", "the wolf-dark woods", "the far fells" };

        public static List<string> Make(IEnumerable<LogEntry> today, int seed, Func<string, string> named = null)
        {
            named = named ?? (s => s);
            var rng = new Random(seed);
            var picked = today.Where(e => e != null && Sings(e.Kind) && (e.A != "" || e.Kind == "raidheld" || e.Kind == "feast" || e.Kind == "fest" || e.Kind == "barrowjarl"))
                .GroupBy(e => e.Kind).Select(g => g.OrderByDescending(e => e.Time).First()) // (each kind once: its latest)
                .OrderByDescending(e => Weight[e.Kind]).ThenBy(e => e.Time).Take(4).ToList();
            var lines = picked.Select(e => Line(e, rng, named)).Where(l => !string.IsNullOrEmpty(l)).ToList();
            if (lines.Count == 1) lines.Add(Pick(rng, "and the hall-fire burned on through the night.", "and the mead went round the hall.", "and the hall heard it, and was glad.", "so the skald sings it, so it was."));
            return lines;
        }

        private static string Line(LogEntry e, Random rng, Func<string, string> named)
        {
            string a = e.A, b = e.B;
            switch (e.Kind)
            {
                case "slew": { string who = named(a); return $"{who}, {Alliter(rng, Bold, a)}, {Alliter(rng, Felled, b)} {b}."; }
                case "home": return $"{named(a)} came home over {Pick(rng, Sea)}, {Alliter(rng, Bold, a)} and weary.";
                case "raidheld": { string foe = b == "" ? "Raiders" : char.ToUpper(b[0]) + b.Substring(1); return Pick(rng, $"{foe} came, and the hall held fast.", $"{foe} came to the wall; the wall stood.", $"{foe} came; they found us waiting."); }
                case "ascend": return $"{a}, {Alliter(rng, Bold, a)}, rode the flame to Valhalla.";
                case "lost": return $"{a} went down the road to Hel; {Pick(rng, "we will bring them home.", "Hel keeps them now.", "the dark took them.")}";
                case "death": return $"{a} fell{(b == "" ? "" : " to " + b)}, {Alliter(rng, Bold, a)} to the last.";
                case "rest": return $"{a}, who was lost, lies at rest; {named(b)} brought them home.";
                case "arrive": return $"{a} the {Alliter(rng, Bold, a)} came down with the Valkyries.";
                case "byname": return $"{a} is {b} now; let the hall remember it.";
                case "feast": return a == "" ? "The hall feasted, and no bowl stood empty." : $"The hall feasted, and {a} ate like a bear.";
                case "starkill": return $"{a} the {Alliter(rng, Bold, a)} {Alliter(rng, Felled, b)} a mighty {b}.";
                case "ring": return $"{named(b)} gave gold to {a} the {Alliter(rng, Bold, a)}.";
                case "worthy": return $"{a} is worthy of Odin's hall.";
                case "einherjar": return $"{a} rode down from Valhalla when the hall had need, {Alliter(rng, Bold, a)} as ever.";
                case "barrowjarl": return $"{b} lies cut down in his barrow; the dead keep their own now.";
                case "beacon": return a == "the watch" ? $"The watch lit the beacon for {named(b)}, and the dark gave way." : $"{a} saw the sail and lit the beacon for {named(b)}.";
                case "champion": return $"{a} the {Alliter(rng, Bold, a)} has no match at {b} in this hall.";
                case "graveitem": return $"{a} took {b} to Odin's hall, and the Allfather smiled.";
                case "marvel": return $"{named(a)} brought home {b}, and the hall fell silent to see it.";
                case "first": return $"{named(a)} the {Alliter(rng, Bold, a)} went where none of us had gone.";
                case "lore": return $"{named(a)} read the old runes, and brought their words to us.";
                case "fest": return Pick(rng, $"We kept {b} as our fathers kept it.", $"{b} was kept, and the gods looked on.", $"The fires of {b} burned high.");
            }
            return null;
        }
        // A word starting as the name does (its first letter), if there is one; else any.
        public static string Alliter(Random rng, string[] words, string name)
        {
            char first = string.IsNullOrEmpty(name) ? ' ' : char.ToLowerInvariant(name.TrimStart()[0]);
            var match = words.Where(w => w[0] == first).ToArray();
            return match.Length > 0 ? match[rng.Next(match.Length)] : words[rng.Next(words.Length)];
        }
        private static string Pick(Random rng, params string[] options) => options[rng.Next(options.Length)];
    }
}
