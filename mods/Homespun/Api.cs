using System.Collections.Generic;
using System.Linq;

namespace Homespun
{
    // For other mods (Jarldom's weaver, Byre, Shipyard), found by reflection: the type "Homespun.Api" in this assembly. Small in 0.1.0:
    // the chain's names and what each step takes and gives; the stations' own state is the game's (a smelter's queue, a crafting
    // station's recipes), so another mod works them as a player does.
    public static class Api
    {
        public const int Version = 2; // (2: dyeing)
        // The prefab names of Homespun's items and stations.
        public static string[] Items => Things.AllItems.ToArray();
        public static string[] Stations => Kit.All.Select(d => d.Name).ToArray();
        public static string[] Garments => Chain.Garments.Concat(Things.DyedGarments).Select(g => g.Prefab).ToArray(); // (2: and every dyed colour of them)
        // Can this item be spun into yarn (combed wool, or Byre's fleece)?
        public static bool IsWool(string prefab) => prefab == Names.Wool || prefab == Names.Fleece;
        // What one of `from` put in at `station` gives (prefab and how many), or null if that station doesn't take it.
        public static (string to, int n)? Gives(string station, string from) => Chain.Of(station, from) is Conversion c ? (c.To, c.Yield) : ((string, int)?)null;
        // Dyeing (2): the colours ("Blue", "Red", "Yellow", "Purple"); a thing's undyed prefab and its colour (null: undyed), or null if it
        // can't be dyed; what it becomes dyed a colour (null: it can't be, or is already); the dyestuff for a colour and how many a measure.
        public static string[] Dyes => Dye.Dyes.Select(d => d.Name).ToArray();
        public static (string undyed, string dye)? Dyed(string prefab) => Dye.Read(prefab) is var r && r != null ? (r.Value.undyed, r.Value.dye?.Name) : ((string, string)?)null;
        public static string DyedAs(string prefab, string dye) => Dye.Into(prefab, Dye.Of(dye));
        public static (string stuff, int perMeasure)? Dyestuff(string dye) => Dye.Of(dye) is Dyestuff d ? (d.Stuff, d.PerMeasure) : ((string, int)?)null;
        public static int Measures(string prefab) => Dye.Measures(prefab);
        // What a thing comes to in what's gathered (flax, fleece or lox pelts, berries...).
        public static Dictionary<string, double> Raw(string item, bool fleece = true) => Chain.Raw(item, fleece);
    }
}
