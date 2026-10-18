using System.Collections.Generic;
using System.Linq;

namespace Homespun
{
    // For other mods (Jarldom's weaver, Byre, Shipyard), found by reflection: the type "Homespun.Api" in this assembly. Small in 0.1.0:
    // the chain's names and what each step takes and gives; the stations' own state is the game's (a smelter's queue, a crafting
    // station's recipes), so another mod works them as a player does.
    public static class Api
    {
        public const int Version = 1;
        // The prefab names of Homespun's items and stations.
        public static string[] Items => Things.AllItems.ToArray();
        public static string[] Stations => Kit.All.Select(d => d.Name).ToArray();
        public static string[] Garments => Chain.Garments.Select(g => g.Prefab).ToArray();
        // Can this item be spun into yarn (combed wool, or Byre's fleece)?
        public static bool IsWool(string prefab) => prefab == Names.Wool || prefab == Names.Fleece;
        // What one of `from` put in at `station` gives (prefab and how many), or null if that station doesn't take it.
        public static (string to, int n)? Gives(string station, string from) => Chain.Of(station, from) is Conversion c ? (c.To, c.Yield) : ((string, int)?)null;
        // What a thing comes to in what's gathered (flax, fleece or lox pelts, berries...).
        public static Dictionary<string, double> Raw(string item, bool fleece = true) => Chain.Raw(item, fleece);
    }
}
