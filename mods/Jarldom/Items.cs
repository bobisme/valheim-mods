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
        public const string RingName = "JarldomArmRing", RelicName = "JarldomRelic", DrumName = "JarldomDrum";
        private static Recipe _drumRecipe;
        private static readonly Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>();
        private static Recipe _ringRecipe;
        private static readonly MethodInfo UpdateRegisters = AccessTools.Method(typeof(ObjectDB), "UpdateRegisters");
        private static readonly MethodInfo CloneShared = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
        public static IEnumerable<GameObject> All => Prefabs.Values.Where(p => p != null);
        public static GameObject Ring => Ensure();
        public static GameObject Relic => EnsureRelic();

        // The heart of the high seat: what's left when the pillars come down, holding the whole colony (its record in the item's own data).
        private static GameObject EnsureRelic()
        {
            if (Prefabs.TryGetValue(RelicName, out GameObject made) && made != null) return made;
            GameObject source = Assets.Find("Thunderstone") ?? Assets.Find("Ruby");
            ItemDrop sourceDrop = source != null ? source.GetComponent<ItemDrop>() : null;
            if (sourceDrop == null) return null;
            GameObject go = Object.Instantiate(source, Assets.Holder);
            go.name = RelicName;
            var shared = (ItemDrop.ItemData.SharedData)CloneShared.Invoke(sourceDrop.m_itemData.m_shared, null);
            Assets.Tint(go, new Color(1.2f, 0.9f, 0.5f), new Color(0.25f, 0.12f, 0.02f));
            shared.m_name = "Heart of the high seat";
            shared.m_description = "All that's left of fallen high-seat pillars, and all a colony was: its people, rooms, saga and the gods' favour. " +
                "Carry it and raise new High-seat pillars (anywhere colonies may stand) to bring the colony back there.";
            shared.m_itemType = ItemDrop.ItemData.ItemType.Misc; shared.m_maxStackSize = 1; shared.m_weight = 2f; shared.m_value = 0; shared.m_teleportable = true;
            Sprite icon = Assets.Icon(go, RelicName, Quaternion.Euler(20, 30, 0));
            if (icon != null) shared.m_icons = new[] { icon };
            ItemDrop drop = go.GetComponent<ItemDrop>();
            drop.m_itemData.m_shared = shared; drop.m_itemData.m_stack = 1; drop.m_itemData.m_quality = 1; drop.m_itemData.m_dropPrefab = go;
            drop.m_autoDestroy = false; // (a colony's whole record: never tidied away)
            Prefabs[RelicName] = go;
            return go;
        }
        // The skald's drum (workbench; deer hide and wood): a skald plays it in the hall at dusk.
        public static GameObject Drum => EnsureDrum();
        private static GameObject EnsureDrum()
        {
            if (Prefabs.TryGetValue(DrumName, out GameObject made) && made != null) return made;
            GameObject source = Assets.Find("Tankard");
            ItemDrop sourceDrop = source != null ? source.GetComponent<ItemDrop>() : null;
            if (sourceDrop == null) return null;
            GameObject go = Object.Instantiate(source, Assets.Holder);
            go.name = DrumName;
            foreach (Transform t in go.transform) if (t.GetComponentInChildren<MeshRenderer>() != null) t.localScale = Vector3.Scale(t.localScale, new Vector3(2.4f, 1.1f, 2.4f)); // a broad, shallow drum
            var shared = (ItemDrop.ItemData.SharedData)CloneShared.Invoke(sourceDrop.m_itemData.m_shared, null);
            Assets.Tint(go, new Color(0.95f, 0.85f, 0.7f), new Color(0.05f, 0.03f, 0.0f));
            shared.m_name = "Skald's drum";
            shared.m_description = "Deer hide stretched over a hoop of wood. Give it to a skald: at dusk they play it in the great hall, and everyone who hears it is the merrier.";
            shared.m_itemType = ItemDrop.ItemData.ItemType.Misc; shared.m_maxStackSize = 1; shared.m_weight = 2f; shared.m_value = 0; shared.m_teleportable = true;
            shared.m_consumeStatusEffect = null; shared.m_food = 0; shared.m_foodStamina = 0; shared.m_foodEitr = 0;
            Sprite icon = Assets.Icon(go, DrumName, Quaternion.Euler(40, 20, 0));
            if (icon != null) shared.m_icons = new[] { icon };
            ItemDrop drop = go.GetComponent<ItemDrop>();
            drop.m_itemData.m_shared = shared; drop.m_itemData.m_stack = 1; drop.m_itemData.m_quality = 1; drop.m_itemData.m_dropPrefab = go;
            Prefabs[DrumName] = go;
            return go;
        }
        public static bool IsDrum(ItemDrop.ItemData item) => item?.m_dropPrefab != null && item.m_dropPrefab.name == DrumName;
        public static bool IsRelic(ItemDrop.ItemData item) => item != null && (item.m_dropPrefab != null ? item.m_dropPrefab.name == RelicName : item.m_customData.ContainsKey(global::Jarldom.Relic.RecordKey));

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

        public static void RegisterScene(ZNetScene scene) { GameObject ring = Ensure(); if (ring != null) Assets.Register(scene, ring); GameObject relic = EnsureRelic(); if (relic != null) Assets.Register(scene, relic); GameObject drum = EnsureDrum(); if (drum != null) Assets.Register(scene, drum); }
        public static void BeforeRegisters(ObjectDB db)
        {
            db.m_items.RemoveAll(i => i == null || i.name == RingName && i != Ensure() || i.name == RelicName && i != EnsureRelic() || i.name == DrumName && i != EnsureDrum());
            db.m_recipes.RemoveAll(r => r != null && r.name == "Recipe_" + DrumName && r != _drumRecipe);
            db.m_recipes.RemoveAll(r => r == null || r.name == "Recipe_" + RingName && r != _ringRecipe);
            if (db.GetItemPrefab("Coins") == null && db.m_items.All(i => i == null || i.name != "Coins")) return; // the main menu's empty database
            GameObject ring = Ensure();
            if (ring == null) return;
            if (!db.m_items.Contains(ring)) db.m_items.Add(ring);
            GameObject relic = EnsureRelic();
            if (relic != null && !db.m_items.Contains(relic)) db.m_items.Add(relic);
            GameObject drum = EnsureDrum();
            if (drum != null && !db.m_items.Contains(drum)) db.m_items.Add(drum);
            CraftingStation bench = Assets.Find("piece_workbench")?.GetComponent<CraftingStation>();
            ItemDrop hide = db.m_items.FirstOrDefault(i => i != null && i.name == "DeerHide")?.GetComponent<ItemDrop>(), wood = db.m_items.FirstOrDefault(i => i != null && i.name == "Wood")?.GetComponent<ItemDrop>();
            if (drum != null && bench != null && hide != null && wood != null)
            {
                if (_drumRecipe == null)
                {
                    _drumRecipe = ScriptableObject.CreateInstance<Recipe>();
                    _drumRecipe.name = "Recipe_" + DrumName; _drumRecipe.hideFlags = HideFlags.HideAndDontSave;
                    _drumRecipe.m_item = drum.GetComponent<ItemDrop>(); _drumRecipe.m_amount = 1; _drumRecipe.m_enabled = true;
                    _drumRecipe.m_craftingStation = bench; _drumRecipe.m_repairStation = bench; _drumRecipe.m_minStationLevel = 1;
                    _drumRecipe.m_resources = new[] { new Piece.Requirement { m_resItem = hide, m_amount = 2, m_recover = true }, new Piece.Requirement { m_resItem = wood, m_amount = 4, m_recover = true } };
                }
                if (!db.m_recipes.Contains(_drumRecipe)) db.m_recipes.Add(_drumRecipe);
            }
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
                ObjectDB.instance.m_recipes.RemoveAll(r => r != null && (r == _ringRecipe || r == _drumRecipe));
                if (had) UpdateRegisters.Invoke(ObjectDB.instance, null);
            }
            if (_ringRecipe != null) Object.Destroy(_ringRecipe);
            if (_drumRecipe != null) Object.Destroy(_drumRecipe);
            _ringRecipe = null; _drumRecipe = null; Prefabs.Clear();
        }
        public static bool IsRing(ItemDrop.ItemData item) => item?.m_dropPrefab != null && item.m_dropPrefab.name == RingName;
    }

    [HarmonyPatch(typeof(ObjectDB), "UpdateRegisters")]
    internal static class RegisterRing { private static void Prefix(ObjectDB __instance) => Plugin.Guard(() => Items.BeforeRegisters(__instance)); }
}
