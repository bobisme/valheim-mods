using System;
using System.Collections.Generic;
using System.Linq;

namespace Jarldom
{
    // The colony's colours (with Homespun's dyes): one or two, chosen by the jarl in the colony window, kept in the colony's flags as
    // "colour:1" and "colour:2" = the dye's name hashed (CropChoice.Hash; no flag: no colours). The weaver dyes the colony's undyed
    // garments in the stores to them at a Homespun dye vat (Villager/Dyeing.cs), and villagers take the dyed ones in place of their plain
    // clothes, the jarls' sworn and the worthiest first.
    public static class Colours
    {
        public const int Most = 2;
        public static readonly string[] Keys = { "colour:1", "colour:2" };
        public const int Batch = 4;      // (garments a vat-load: the vat holds eight stacks, the dyestuff too)
        public const int StuffKept = 10; // (of a dyestuff, left in the stores: berries are the kitchen's and the brewer's too)

        // The colours chosen, among the dyes known, first colour first.
        public static List<string> Chosen(IDictionary<string, int> flags, IEnumerable<string> dyes)
        {
            var known = (dyes ?? Enumerable.Empty<string>()).ToList(); var list = new List<string>();
            foreach (string k in Keys)
                if (flags != null && flags.TryGetValue(k, out int h) && h != 0 && known.FirstOrDefault(d => CropChoice.Hash(d) == h) is string d && !list.Contains(d)) list.Add(d);
            return list;
        }
        public static void Set(IDictionary<string, int> flags, IEnumerable<string> colours)
        {
            var list = (colours ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrEmpty(x)).Distinct().Take(Most).ToList();
            for (int i = 0; i < Keys.Length; i++) if (i < list.Count) flags[Keys[i]] = CropChoice.Hash(list[i]); else flags.Remove(Keys[i]);
        }
        // Words to colours ("blue red", "Blue|Red"; "off" or "none": no colours), as the dyes are named; words not a dye left out.
        public static List<string> Parse(string words, IEnumerable<string> dyes)
        {
            var known = (dyes ?? Enumerable.Empty<string>()).ToList();
            return (words ?? "").Split(new[] { ' ', '|', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(w => known.FirstOrDefault(d => string.Equals(d, w, StringComparison.OrdinalIgnoreCase))).Where(d => d != null).Distinct().Take(Most).ToList();
        }
        // A dye clicked in the window: off if chosen; else on, as the second colour (with two already, it takes the second's place).
        public static List<string> Toggle(IList<string> now, string dye)
        {
            var list = (now ?? new List<string>()).ToList();
            if (string.IsNullOrEmpty(dye) || list.Remove(dye)) return list;
            if (list.Count >= Most) list.RemoveAt(Most - 1);
            list.Add(dye);
            return list;
        }

        // The colour the weaver dyes to next: the one the colony has fewest garments in (the first colour on a tie).
        public static string Next(IList<string> colours, Func<string, int> garmentsIn) =>
            colours == null || colours.Count == 0 ? null : colours.OrderBy(garmentsIn).ThenBy(colours.IndexOf).First();
        // Which of the garments waiting a batch dyes (in the order given, up to Batch, as far as the dyestuff over what's kept goes), and
        // the dyestuff it takes.
        public static (List<int> picked, int stuff) Pick(IList<int> measures, int perMeasure, int stuffHeld, int kept = StuffKept)
        {
            var picked = new List<int>(); int free = stuffHeld - kept, used = 0;
            for (int i = 0; i < measures.Count && picked.Count < Batch; i++)
            {
                int each = Math.Max(1, measures[i]) * Math.Max(1, perMeasure);
                if (used + each > free) continue;
                picked.Add(i); used += each;
            }
            return (picked, used);
        }
        // Is what's in a vat steeping (Homespun's rule: one dyestuff and something it dyes, with dye enough for one of it)? dyeOf: the dye a
        // dyestuff gives (null: not one); need: the dyestuff one of a thing takes to dye that colour (0: it can't be).
        public static bool Steeping(IList<(string prefab, int stack)> contents, Func<string, string> dyeOf, Func<string, string, int> need)
        {
            var dyes = contents.Select(x => dyeOf(x.prefab)).Where(d => d != null).Distinct().ToList();
            if (dyes.Count != 1) return false;
            int stuff = contents.Where(x => dyeOf(x.prefab) == dyes[0]).Sum(x => x.stack);
            return contents.Any(x => need(x.prefab, dyes[0]) is int n && n > 0 && n <= stuff);
        }

        // Who gets the finer cloth first: the jarls' sworn, then the worthiest, then the longest in the colony.
        public static IOrderedEnumerable<T> ByStanding<T>(IEnumerable<T> souls, Func<T, bool> sworn, Func<T, float> worth, Func<T, double> arrived) =>
            souls.OrderByDescending(s => sworn(s) ? 1 : 0).ThenByDescending(worth).ThenBy(arrived);
        // Their turn for something fine from the stores: more of it spare than there are above them who want one.
        public static bool Turn(int aboveWanting, int spare) => spare > aboveWanting;
    }

    // Fine clothes: dyed is finer than plain, the colony's colours finer still. Any day a villager wears the sturdiest they have (the finer
    // of two alike); on a festival's day the finest, for the day (a guard keeps their armour on), and is glad of it. A finer garment than
    // what they wear is kept by them for the festivals, one a slot, not put in the stores. A feast under the hall's tapestries is the finer
    // feast.
    public static class Finery
    {
        // Undyed 0, dyed 1, the colony's second colour 2, its first (or only) 3.
        public static int Of(string dye, IList<string> colours)
        {
            if (string.IsNullOrEmpty(dye)) return 0;
            int at = colours?.IndexOf(dye) ?? -1;
            return at < 0 ? 1 : at == 0 ? 3 : 2;
        }
        public static bool FirstToday(bool festivalDay, Prof p) => festivalDay && p != Prof.Guard;
        // The "In festival finery" moodlet: on the day, something dyed worn.
        public static bool Glad(bool festivalDay, int finestWorn) => festivalDay && finestWorn > 0;
        // Kept by for the festivals (not spare for the stores): finer than what's worn in its slot, and the finest of the rest there.
        public static bool KeptBy(int finery, int wornFinery, int finestOfRest, bool firstOfThose) => finery > wornFinery && finery == finestOfRest && firstOfThose;

        public const int HungFeast = 2; // (tapestries in the great hall for the finer feast)
        public static string FeastMood(int tapestries) => tapestries >= HungFeast ? "feasthung" : "feast";
    }
}
