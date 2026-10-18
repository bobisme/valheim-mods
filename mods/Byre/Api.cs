using System.Linq;
using UnityEngine;

namespace Byre
{
    // For other mods (Homespun spins ByreWool; Jarldom's herders), found by reflection: the type "Byre.Api" in this assembly. Names are
    // prefab names; they never change (Core/PrefabNames.cs).
    public static class Api
    {
        public const int Version = 1;
        // Is this one of Byre's animals (a sheep, goat, lamb or kid; a prefab name, or a live animal)?
        public static bool IsLivestock(string prefab) => Species.Of(prefab) != null;
        public static bool IsLivestock(GameObject go) => go != null && IsLivestock(Utils.GetPrefabName(go));
        // Is this item prefab Byre's wool (what a spinner spins)?
        public static bool IsWool(string prefab) => prefab == Species.Sheep.Product;
        // What the animals give, by item prefab name (wool, goat's milk).
        public static string[] Products => Species.All.Select(s => s.Product).ToArray();
        // The kinds of animal ("sheep", "goat") and each one's grown and young prefab names.
        public static string[] Kinds => Species.All.Select(s => s.Key).ToArray();
        public static string Grown(string kind) => Species.ByKey(kind)?.Prefab;
        public static string Young(string kind) => Species.ByKey(kind)?.Young;
        // A live animal: tame? its wool or milk ready? (false for one that isn't ours)
        public static bool IsTame(GameObject go) => go != null && go.GetComponent<Livestock>() is Livestock l && l.Tamed;
        public static bool IsReady(GameObject go) => go != null && go.GetComponent<Livestock>() is Livestock l && !l.Young && l.Ready();
    }
}
