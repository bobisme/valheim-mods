using System;
using System.Collections.Generic;
using System.Linq;

namespace Homespun
{
    // Dyeing, as data (the vat itself is DyeVat.cs). Cloth and garments go into a dye vat with a dyestuff from the land (blueberries
    // blue, as woad; raspberries red, as madder; dandelions yellow, as weld; lichen purple, as the north's cudbear) and come out dyed.
    // A dyed thing is an item of its own (Homespun_WoolBolt_Blue): its colour then goes everywhere the game takes an item (worn, dropped,
    // in a chest, seen by every player) with nothing of ours to carry it. Dyed bolts sew into garments of their colour at the sewing
    // table, and a garment can be dyed again in another colour. Each thing takes its measures of dye: a bolt one, a garment the bolts in it.
    public sealed class Dyestuff
    {
        public string Name, Title, Stuff, Of; public int PerMeasure; public (float r, float g, float b) Colour;
    }

    public static class Dye
    {
        public const float SteepSeconds = 60; // (a batch, from when there's something to dye and a dyestuff for it)
        public const int VatWidth = 4, VatHeight = 2;
        public static readonly Dyestuff[] Dyes =
        {
            new Dyestuff { Name = "Blue", Title = "blue", Stuff = "Blueberries", Of = "blueberries", PerMeasure = 5, Colour = (0.18f, 0.27f, 0.52f) },
            new Dyestuff { Name = "Red", Title = "red", Stuff = "Raspberry", Of = "raspberries", PerMeasure = 5, Colour = (0.62f, 0.15f, 0.11f) },
            new Dyestuff { Name = "Yellow", Title = "yellow", Stuff = "Dandelion", Of = "dandelions", PerMeasure = 5, Colour = (0.84f, 0.68f, 0.2f) },
            new Dyestuff { Name = "Purple", Title = "purple", Stuff = Names.Lichen, Of = "lichen", PerMeasure = 3, Colour = (0.4f, 0.2f, 0.44f) },
        };
        // What can be dyed, and how many measures of dye it takes.
        public static readonly (string prefab, int measures)[] Dyeable =
        {
            (Names.LinenBolt, 1), (Names.WoolBolt, 1),
            (Names.Undertunic, 1), (Names.Tunic, 2), (Names.Dress, 2), (Names.Trousers, 2), (Names.Cloak, 3),
            (Names.Sail, 6),
        };

        public static Dyestuff Of(string name) => Dyes.FirstOrDefault(d => d.Name == name);
        public static Dyestuff ByStuff(string stuff) => Dyes.FirstOrDefault(d => d.Stuff == stuff);
        public static string Variant(string undyed, Dyestuff d) => undyed + "_" + d.Name;
        public static IEnumerable<string> Variants => Dyeable.SelectMany(x => Dyes.Select(d => Variant(x.prefab, d)));

        // A prefab's undyed self and its colour (null: undyed), or null for what can't be dyed.
        public static (string undyed, Dyestuff dye)? Read(string prefab)
        {
            if (prefab == null) return null;
            foreach (var (p, _) in Dyeable)
            {
                if (prefab == p) return (p, null);
                if (prefab.StartsWith(p + "_") && Of(prefab.Substring(p.Length + 1)) is Dyestuff d) return (p, d);
            }
            return null;
        }
        public static string Undyed(string prefab) => Read(prefab)?.undyed ?? prefab;
        public static int Measures(string prefab) => Read(prefab) is var r && r != null ? Dyeable.First(x => x.prefab == r.Value.undyed).measures : 0;
        // Dyed this colour, what it becomes (null: it can't be dyed, or is that colour already).
        public static string Into(string prefab, Dyestuff d)
        {
            var r = Read(prefab);
            if (r == null || d == null || r.Value.dye == d) return null;
            return Variant(r.Value.undyed, d);
        }

        // The names a dyed thing goes by: "Bolt of blue wool", "Blue wool tunic", "Purple hooded cloak".
        public static string Title(string undyedTitle, Dyestuff d)
        {
            const string bolt = "Bolt of ";
            if (undyedTitle.StartsWith(bolt)) return bolt + d.Title + " " + undyedTitle.Substring(bolt.Length);
            string t = char.ToUpperInvariant(d.Title[0]) + d.Title.Substring(1);
            return t + " " + char.ToLowerInvariant(undyedTitle[0]) + undyedTitle.Substring(1);
        }
        public static string Description(string undyed, Dyestuff d) => undyed.Replace("undyed ", "").Replace("grey-brown ", "").Replace("bleached ", "") + $" Dyed {d.Title} with {d.Of}.";

        // A batch: of what's in the vat, how many of each it dyes with the dyestuff there, in the order they lie (whole stacks where
        // the dye runs to them, then part of one), and the dyestuff it takes. Only one dyestuff at a time: with two, nothing.
        public static (List<(int index, int n, string into)> dyed, int stuffUsed, Dyestuff dye) Batch(IList<(string prefab, int stack)> contents)
        {
            var none = (new List<(int, int, string)>(), 0, (Dyestuff)null);
            var dyes = contents.Select(c => ByStuff(c.prefab)).Where(d => d != null).Distinct().ToList();
            if (dyes.Count != 1) return none;
            Dyestuff dye = dyes[0];
            int stuff = contents.Where(c => c.prefab == dye.Stuff).Sum(c => c.stack), used = 0;
            var dyed = new List<(int, int, string)>();
            for (int i = 0; i < contents.Count; i++)
            {
                string into = Into(contents[i].prefab, dye);
                if (into == null) continue;
                int each = Measures(contents[i].prefab) * dye.PerMeasure;
                int n = Math.Min(contents[i].stack, (stuff - used) / each);
                if (n <= 0) continue;
                dyed.Add((i, n, into)); used += n * each;
            }
            return (dyed, used, dye);
        }
        // What the vat says it's waiting for (null: a batch is ready to steep).
        public static string Wants(IList<(string prefab, int stack)> contents)
        {
            var dyes = contents.Select(c => ByStuff(c.prefab)).Where(d => d != null).Distinct().ToList();
            bool cloth = contents.Any(c => Read(c.prefab) != null);
            if (dyes.Count > 1) return "Two dyes in the vat: it takes one at a time";
            if (!cloth && dyes.Count == 0) return "Put in cloth or clothes, and a dyestuff: " + string.Join(", ", Dyes.Select(d => $"{d.Of} {d.Title}"));
            if (!cloth) return "Put in a bolt of cloth or clothes to dye " + dyes[0].Title;
            if (dyes.Count == 0) return "Put in a dyestuff: " + string.Join(", ", Dyes.Select(d => $"{d.Of} {d.Title}"));
            if (!contents.Any(c => Into(c.prefab, dyes[0]) != null)) return "All of it is " + dyes[0].Title + " already";
            if (Batch(contents).dyed.Count == 0) return $"Not dye enough: {dyes[0].PerMeasure} {dyes[0].Of} a measure, a bolt takes one, a garment the bolts in it";
            return null;
        }

        // The sewing table's recipes in each colour: a garment from dyed bolts comes out that colour (the rest of what it takes as undyed).
        public static IEnumerable<Make> DyedMakes() =>
            Chain.Makes.Where(m => m.Station == Names.Table && Read(m.Item) != null && m.Needs.Any(n => n.item == Names.LinenBolt || n.item == Names.WoolBolt))
                .SelectMany(m => Dyes.Select(d => new Make
                {
                    Station = m.Station, Item = Variant(m.Item, d), Amount = m.Amount,
                    Needs = m.Needs.Select(n => n.item == Names.LinenBolt || n.item == Names.WoolBolt ? (Variant(n.item, d), n.n) : n).ToArray(),
                }));
    }
}
