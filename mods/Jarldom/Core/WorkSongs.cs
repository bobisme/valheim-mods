using System;
using System.Collections.Generic;
using System.Linq;

namespace Jarldom
{
    // Work songs: a few villagers at the same work close together keep a rhythm, a chant in Old Norse with the English under it.
    public static class WorkSongs
    {
        public const int Together = 3; public const float Reach = 15f, Every = 20f, Pace = 1.1f;
        public static readonly (string norse, string english)[] Any =
        {
            ("Hó! Hó! Allir saman!", "Ho! Ho! All together!"),
            ("Eitt, tvau, þrjú, nú!", "One, two, three, now!"),
            ("Tökum á, bræðr!", "Put your backs in, brothers!"),
            ("Vinnum vel, vinnum hratt!", "Work well, work fast!"),
            ("Syngjum ok strítum!", "Sing and strive!"),
        };
        public static readonly Dictionary<string, (string norse, string english)[]> ByWork = new Dictionary<string, (string, string)[]>
        {
            ["chop"] = new[] { ("Fellum eikina!", "Fell the oak!"), ("Öxin bítr, tréit fellr!", "The axe bites, the tree falls!"), ("Högg ok högg, ok niðr hann ferr!", "Chop and chop, and down he goes!") },
            ["mine"] = new[] { ("Höggvum grjótit!", "Hew the stone!"), ("Járn ok grjót, grjót ok járn!", "Iron and stone, stone and iron!"), ("Bergit brestr!", "The rock is cracking!") },
            ["sow"] = new[] { ("Sáum, sáum, sól skín!", "Sow, sow, the sun shines!"), ("Fræ í mold, matr í búr!", "Seed in the soil, food in the store!") },
            ["harvest"] = new[] { ("Skerum kornit, fyllum búrit!", "Cut the crop, fill the store!"), ("Góð er uppskeran!", "A good harvest!") },
            ["haul"] = new[] { ("Berum þungt, berum saman!", "Carry it heavy, carry it together!"), ("Upp með byrðina!", "Up with the load!") },
        };
        // The chant for that work (its own lines, or the ones for any work).
        public static (string norse, string english) Pick(string work, Random rng)
        {
            var lines = ByWork.TryGetValue(work ?? "", out var own) ? own.Concat(Any).ToArray() : Any;
            return lines[rng.Next(lines.Length)];
        }
        public static IEnumerable<char> Letters() => Any.Concat(ByWork.Values.SelectMany(a => a)).SelectMany(l => l.norse + l.english).Distinct();
        // Whether this many at the same work sing, given how they feel (a miserable lot work in silence).
        public static bool Sing(int together, double mood) => together >= Together && mood > -40;
    }
}
