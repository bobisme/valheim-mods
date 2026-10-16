using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Jarldom
{
    // A shieling: a summer pasture far out (300 m or more from home, on meadow, forest or mountain ground), a claim stake set to it with a
    // camp round it (a bed, a chest and a fire). At the start of summer the herder drives the pasture's tame beasts up there on foot;
    // they summer at the shieling, and when summer ends the herder fetches them home, fatter, with young, and fewer if the wolves had
    // some. The herd's comings and goings are kept in a global key (the shieling lies far from its colony, and on a server another
    // game may have its ground loaded): "jarldom_shieling_<claim> <colony>,<state>,<year>,<placed>,<fetching>,<beasts>", the beasts
    // as "prefab:level:name;...". Placed: the beasts stand at the shieling in the world (else the stake sets them there when its ground
    // loads). Fetching: the herder is out bringing them down.
    public sealed class Herd
    {
        public string Claim = "", Colony = ""; public bool Up, Placed, Fetching; public int Year;
        public List<(string prefab, int level, string name)> Beasts = new List<(string, int, string)>();
        private static string Clean(string s) => (s ?? "").Replace(",", " ").Replace(";", " ").Replace(":", " ").Replace("^", " ").Trim();
        // (the game keeps global keys in lower case: a capital is written "^" and the letter, so "Boar" and a pet's name come back whole)
        public static string Caps(string s) => string.Concat((s ?? "").Select(ch => char.IsUpper(ch) ? "^" + char.ToLowerInvariant(ch) : ch.ToString()));
        public static string Uncaps(string s) { var o = new System.Text.StringBuilder(); for (int i = 0; i < (s ?? "").Length; i++) { if (s[i] == '^' && i + 1 < s.Length) o.Append(char.ToUpperInvariant(s[++i])); else o.Append(s[i]); } return o.ToString(); }
        public string Encode() => $"{Colony},{(Up ? "up" : "home")},{Year},{(Placed ? 1 : 0)},{(Fetching ? 1 : 0)}," + string.Join(";", Beasts.Select(b => $"{Caps(Clean(b.prefab))}:{b.level}:{Caps(Clean(b.name))}"));
        public static Herd Decode(string claim, string value)
        {
            if (string.IsNullOrEmpty(value)) return null;
            string[] f = value.Split(',');
            if (f.Length < 6) return null;
            var h = new Herd { Claim = claim, Colony = f[0], Up = f[1] == "up", Placed = f[3] == "1", Fetching = f[4] == "1" };
            int.TryParse(f[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out h.Year);
            foreach (string part in f[5].Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] p = part.Split(':');
                if (p.Length >= 2 && p[0] != "" && int.TryParse(p[1], out int lvl)) h.Beasts.Add((Uncaps(p[0]), Math.Max(1, lvl), p.Length > 2 ? Uncaps(p[2]) : ""));
            }
            return h;
        }
    }

    public static class Shielings
    {
        public const float Far = 300f;
        public const int MaxHerd = 10;
        public static readonly string[] Grounds = { "meadows", "blackforest", "mountain" };
        public static bool GoodGround(string biome) => Grounds.Contains((biome ?? "").ToLowerInvariant());
        // Young born over a summer, by kind (the game's own young of each).
        public static readonly Dictionary<string, string> Young = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Boar"] = "Boar_piggy", ["Wolf"] = "Wolf_cub", ["Lox"] = "Lox_Calf", ["Asksvin"] = "Asksvin_hatchling", ["Hen"] = "Chicken" };
        // Up this summer: summer, the herd at home, and not taken up yet this year.
        public static bool GoUp(Season season, int year, Herd herd) => season == Season.Summer && (herd == null || !herd.Up && herd.Year < year);
        // Down: the herd up there and summer over.
        public static bool ComeDown(Season season, Herd herd) => herd != null && herd.Up && season != Season.Summer;
        // The losses to wolves and weather: by the ground, per beast.
        public static float Loss(string biome) { switch ((biome ?? "").ToLowerInvariant()) { case "mountain": return 0.12f; case "blackforest": return 0.08f; default: return 0.04f; } }
        // A summer's end (roll: 0..1): who comes home (a third grow a star stronger, to three), how many were lost, and the young (one for
        // every two that came home, of the kinds that have young).
        public static (List<(string prefab, int level, string name)> home, int lost, List<string> young) Summer(IList<(string prefab, int level, string name)> beasts, string biome, Func<double> roll)
        {
            var home = new List<(string, int, string)>(); int lost = 0;
            foreach (var b in beasts)
            {
                if (roll() < Loss(biome)) { lost++; continue; }
                int lvl = b.level < 3 && roll() < 1.0 / 3 ? b.level + 1 : b.level;
                home.Add((b.prefab, lvl, b.name));
            }
            var young = new List<string>();
            foreach (var g in home.GroupBy(b => b.Item1))
                if (Young.TryGetValue(g.Key, out string y)) for (int i = 0; i < g.Count() / 2; i++) young.Add(y);
            return (home, lost, young);
        }
    }
}
