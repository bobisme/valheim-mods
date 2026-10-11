using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jarldom
{
    // The mod's item: the gold arm ring a jarl gives (forge; 25 coins and a silver bar). Worth nothing at the trader, so it can't turn coins
    // into more coins.
    internal static class Items
    {
        public const string RingName = "JarldomArmRing";
        private static readonly Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>();
        private static Recipe _ringRecipe;
        private static readonly MethodInfo UpdateRegisters = AccessTools.Method(typeof(ObjectDB), "UpdateRegisters");
        private static readonly MethodInfo CloneShared = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
        public static IEnumerable<GameObject> All => Prefabs.Values.Where(p => p != null);
        public static GameObject Ring => Ensure();

        private static GameObject Ensure()
        {
            if (Prefabs.TryGetValue(RingName, out GameObject made) && made != null) return made;
            GameObject source = Assets.Find("SilverNecklace");
            ItemDrop sourceDrop = source != null ? source.GetComponent<ItemDrop>() : null;
            if (sourceDrop == null) return null;
            GameObject go = Object.Instantiate(source, Assets.Holder);
            go.name = RingName;
            var shared = (ItemDrop.ItemData.SharedData)CloneShared.Invoke(sourceDrop.m_itemData.m_shared, null);
            Assets.Tint(go, new Color(1.3f, 1.05f, 0.45f), new Color(0.15f, 0.1f, 0.0f));
            shared.m_name = "Gold arm ring";
            shared.m_description = "A ring of twisted gold, given by a jarl to the loyal. Give it to a villager from their panel (best at a feast): they'll remember it, " +
                "and a guard with a ring never runs early. A gift, not a treasure: no trader will buy it.";
            shared.m_itemType = ItemDrop.ItemData.ItemType.Misc; shared.m_maxStackSize = 10; shared.m_weight = 0.2f; shared.m_value = 0; shared.m_teleportable = true;
            Sprite icon = Assets.Icon(go, RingName, Quaternion.Euler(30, 20, 0));
            if (icon != null) shared.m_icons = new[] { icon };
            ItemDrop drop = go.GetComponent<ItemDrop>();
            drop.m_itemData.m_shared = shared; drop.m_itemData.m_stack = 1; drop.m_itemData.m_quality = 1; drop.m_itemData.m_dropPrefab = go;
            Prefabs[RingName] = go;
            return go;
        }

        public static void RegisterScene(ZNetScene scene) { GameObject ring = Ensure(); if (ring != null) Assets.Register(scene, ring); }
        public static void BeforeRegisters(ObjectDB db)
        {
            db.m_items.RemoveAll(i => i == null || i.name == RingName && i != Ensure());
            db.m_recipes.RemoveAll(r => r == null || r.name == "Recipe_" + RingName && r != _ringRecipe);
            if (db.GetItemPrefab("Coins") == null && db.m_items.All(i => i == null || i.name != "Coins")) return; // the main menu's empty database
            GameObject ring = Ensure();
            if (ring == null) return;
            if (!db.m_items.Contains(ring)) db.m_items.Add(ring);
            CraftingStation forge = Assets.Find("forge")?.GetComponent<CraftingStation>();
            ItemDrop coins = db.m_items.FirstOrDefault(i => i != null && i.name == "Coins")?.GetComponent<ItemDrop>(), silver = db.m_items.FirstOrDefault(i => i != null && i.name == "Silver")?.GetComponent<ItemDrop>();
            if (forge == null || coins == null || silver == null) return;
            if (_ringRecipe == null)
            {
                _ringRecipe = ScriptableObject.CreateInstance<Recipe>();
                _ringRecipe.name = "Recipe_" + RingName; _ringRecipe.hideFlags = HideFlags.HideAndDontSave;
                _ringRecipe.m_item = ring.GetComponent<ItemDrop>(); _ringRecipe.m_amount = 1; _ringRecipe.m_enabled = true;
                _ringRecipe.m_craftingStation = forge; _ringRecipe.m_repairStation = forge; _ringRecipe.m_minStationLevel = 1;
                _ringRecipe.m_resources = new[] { new Piece.Requirement { m_resItem = coins, m_amount = 25, m_recover = true }, new Piece.Requirement { m_resItem = silver, m_amount = 1, m_recover = true } };
            }
            if (!db.m_recipes.Contains(_ringRecipe)) db.m_recipes.Add(_ringRecipe);
        }
        public static void RegisterLoaded() { if (ObjectDB.instance != null) UpdateRegisters.Invoke(ObjectDB.instance, null); }
        public static void Unregister()
        {
            if (ObjectDB.instance != null)
            {
                bool had = ObjectDB.instance.m_items.RemoveAll(i => i != null && Prefabs.Values.Contains(i)) > 0;
                ObjectDB.instance.m_recipes.RemoveAll(r => r != null && r == _ringRecipe);
                if (had) UpdateRegisters.Invoke(ObjectDB.instance, null);
            }
            if (_ringRecipe != null) Object.Destroy(_ringRecipe);
            _ringRecipe = null; Prefabs.Clear();
        }
        public static bool IsRing(ItemDrop.ItemData item) => item?.m_dropPrefab != null && item.m_dropPrefab.name == RingName;
    }

    [HarmonyPatch(typeof(ObjectDB), "UpdateRegisters")]
    internal static class RegisterRing { private static void Prefix(ObjectDB __instance) => Plugin.Guard(() => Items.BeforeRegisters(__instance)); }
}
