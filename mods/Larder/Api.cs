using System.Linq;

namespace Larder
{
    // For other mods (Jarldom's cooks and expedition supplies; later, spoilage hooks for Hounds' cats), found by reflection: the type
    // "Larder.Api" in this assembly. Names are prefab names; they never change (Core/PrefabNames.cs).
    public static class Api
    {
        public const int Version = 2; // (2: spoilage)
        // Is this item prefab a preserved food (smoked meat, salt meat, stockfish, flatbread): one that keeps, and that Larder's spoilage passes by?
        public static bool IsPreserved(string prefab) => Preserves.IsPreserved(prefab);
        public static bool IsPreserved(ItemDrop.ItemData item) => item?.m_dropPrefab != null && IsPreserved(item.m_dropPrefab.name);
        // The preserved foods (and the bread that keeps), and every food Larder makes (the dairy's and the bread too), by item prefab name.
        public static string[] Preserved => Preserves.Foods.Select(f => f.Prefab).Append(Baked.Flatbread).ToArray();
        public static string[] Foods => Dairy.Foods.Concat(Preserves.Foods).Append(Baking.Flatbread).Select(f => f.Prefab).ToArray();
        // The preserving stations' piece prefab names, and what each takes in: what it turns into food, and what it burns (null for none).
        public static string[] Stations => Preserves.Stations.Select(w => w.Name).ToArray();
        public static string[] Takes(string station) => Preserves.Stations.FirstOrDefault(w => w.Name == station)?.Makes.Select(m => m.from).ToArray() ?? new string[0];
        public static string Burns(string station) => Preserves.Stations.FirstOrDefault(w => w.Name == station)?.Fuel;
        // Spoilage (2): whether the server has it on; how many game days an item has left before it spoils (infinite for what never does,
        // or with spoilage off); the spoiled food's prefab name. Read only: Larder ages food itself, when a chest is opened.
        public static bool SpoilageOn => Spoiling.On;
        public static float DaysLeft(ItemDrop.ItemData item) => Spoiling.On ? Spoiling.DaysLeft(item) : float.PositiveInfinity;
        public static string SpoiledFood => Spoilage.Spoiled;
    }
}
