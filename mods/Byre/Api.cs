using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Byre
{
    // For other mods (Homespun spins ByreWool; Jarldom's herders), found by reflection: the type "Byre.Api" in this assembly. Names are
    // prefab names; they never change (Core/PrefabNames.cs).
    public static class Api
    {
        public const int Version = 3;
        // Is this one of Byre's animals (a sheep, goat, cow, pig or goose, or one's young; a prefab name, or a live animal)?
        public static bool IsLivestock(string prefab) => Species.Of(prefab) != null;
        public static bool IsLivestock(GameObject go) => go != null && IsLivestock(Utils.GetPrefabName(go));
        // Is this item prefab Byre's wool (what a spinner spins)?
        public static bool IsWool(string prefab) => prefab == Species.Sheep.Product;
        // What the animals give, by item prefab name (wool, goat's and cow's milk, the game's feathers), each once.
        public static string[] Products => Species.All.Where(s => s.Product != null).Select(s => s.Product).Distinct().ToArray();
        // The kinds of animal ("sheep", "goat", "cow", "pig", "goose") and each one's grown and young prefab names.
        public static string[] Kinds => Species.All.Select(s => s.Key).ToArray();
        public static string Grown(string kind) => Species.ByKey(kind)?.Prefab;
        public static string Young(string kind) => Species.ByKey(kind)?.Young;
        // A live animal: tame? its wool or milk ready? (false for one that isn't ours)
        public static bool IsTame(GameObject go) => go != null && go.GetComponent<Livestock>() is Livestock l && l.Tamed;
        public static bool IsReady(GameObject go) => go != null && go.GetComponent<Livestock>() is Livestock l && !l.Young && l.Ready();

        // ---- Version 2: for villagers (Jarldom's herders) ----
        // What a kind gives and what in the bag takes it (ByreWool with ByreShears; ByreGoatMilk and ByreCowMilk with ByreMilkPail); null for
        // an unknown kind. Version 3: a goose's Feathers with no tool (Tool null: plucked by hand); a pig's nothing (Product null too).
        public static string Product(string kind) => Species.ByKey(kind)?.Product;
        public static string Tool(string kind) => Species.ByKey(kind)?.Tool;
        // A live animal's kind ("sheep", "goat", ...), grown or young; null for one that isn't ours.
        public static string KindOf(GameObject go) => go != null && go.GetComponent<Livestock>() is Livestock l ? l.Kind?.Key : null;
        public static bool IsYoung(GameObject go) => go != null && go.GetComponent<Livestock>() is Livestock l && l.Young;
        // Game seconds until its wool or milk is ready (0: ready now).
        public static double Remaining(GameObject go) => go != null && go.GetComponent<Livestock>() is Livestock l ? l.Remaining() : 0;
        // Byre's live animals within radius of a point, nearest first (grown and young, wild and tame).
        public static List<GameObject> HerdsNear(Vector3 at, float radius)
        {
            var near = new List<Livestock>(); Live<Livestock>.Near(at, radius, near);
            return near.Where(l => l != null).OrderBy(l => Vector3.Distance(l.transform.position, at)).Select(l => l.gameObject).ToList();
        }
        // Shear or milk it, as a player with the tool would (no tool asked for: the caller has seen to that): on its owner, once per
        // regrowth, the yield dropped at its feet. False if it isn't a grown, tame animal of ours with its wool or milk ready.
        public static bool Harvest(GameObject go)
        {
            if (go == null || !(go.GetComponent<Livestock>() is Livestock l) || l.Kind == null || !Husbandry.CanHarvest(l.Tamed, l.Young) || !l.Ready() || l.View == null || !l.View.IsValid()) return false;
            l.View.InvokeRPC("Byre_Harvest");
            return true;
        }
        // Can it pull a plow or a cart? A tame grown cow (version 3; false before for every animal).
        public static bool CanPull(GameObject go) => go != null && go.GetComponent<Livestock>() is Livestock l && l.Kind == Species.Cow && l.Tamed && !l.Young;
        // The feeders within radius (troughs and hay racks), as their containers: fill one and hungry tame animals near come to eat.
        public static List<Container> FeedersNear(Vector3 at, float radius)
        {
            var near = new List<Trough>(); Live<Trough>.Near(at, radius, near);
            return near.Where(t => t != null && t.Box != null).Select(t => t.Box).ToList();
        }
    }
}
