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
        public const string RingName = "JarldomArmRing", RelicName = "JarldomRelic", DrumName = "JarldomDrum", RemainsPrefab = "JarldomRemains";
        private static Recipe _drumRecipe;
        private static readonly Dictionary<string, GameObject> Prefabs = new Dictionary<string, GameObject>();
        private static Recipe _ringRecipe;
        private static readonly MethodInfo UpdateRegisters = AccessTools.Method(typeof(ObjectDB), "UpdateRegisters");
        private static readonly MethodInfo CloneShared = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
        public static IEnumerable<GameObject> All => Prefabs.Values.Where(p => p != null);
        public static GameObject Ring => Ensure();

        // The gods' relics that are Jarldom's own (Core's Relics: the others are the game's rare things): each a vanilla thing of the
        // right shape, recoloured and named for its god. Worth nothing to a trader.
        private static readonly (string name, string source, string title, string text, Color tint, Color glow)[] GodRelics =
        {
            (Relics.StormStone, "Thunderstone", "Storm stone", "A stone fused glassy where the lightning struck a mountain's peak in a storm: Thor's own. Lay it on Thor's altar and his temple may be more than B.", new Color(0.55f, 0.65f, 1.1f), new Color(0.1f, 0.18f, 0.45f)),
            (Relics.GoldenFlower, "Dandelion", "Golden wildflower", "A flower of gold that grew among the meadow's own, once in a long while: Freyja's. Lay it on Freyja's altar and her temple may be more than B.", new Color(1.4f, 1.1f, 0.35f), new Color(0.3f, 0.2f, 0.0f)),
            (Relics.Spindle, "LinenThread", "Golden spindle", "A spindle wound with gold thread, from a burial chamber in the Black Forest: Frigg spins the clouds with one like it. Lay it on Frigg's altar and her temple may be more than B.", new Color(1.3f, 1.0f, 0.4f), new Color(0.25f, 0.16f, 0.0f)),
            (Relics.RavenFeather, "Feathers", "Raven feather", "A raven's feather, black and blue, from a ruin in the Mistlands: Huginn's or Muninn's. Lay it on Odin's altar and his temple may be more than B.", new Color(0.25f, 0.27f, 0.4f), new Color(0.03f, 0.04f, 0.12f)),
        };
        public static GameObject GodRelic(string name)
        {
            if (Prefabs.TryGetValue(name, out GameObject made) && made != null) return made;
            var def = GodRelics.FirstOrDefault(r => r.name == name);
            if (def.name == null) return null;
            GameObject source = Assets.Find(def.source);
            ItemDrop sourceDrop = source != null ? source.GetComponent<ItemDrop>() : null;
            if (sourceDrop == null) return null;
            GameObject go = Object.Instantiate(source, Assets.Holder);
            go.name = def.name;
            var shared = (ItemDrop.ItemData.SharedData)CloneShared.Invoke(sourceDrop.m_itemData.m_shared, null);
            Assets.Tint(go, def.tint, def.glow);
            shared.m_name = def.title; shared.m_description = def.text;
            shared.m_itemType = ItemDrop.ItemData.ItemType.Misc; shared.m_maxStackSize = 1; shared.m_weight = 0.5f; shared.m_value = 0; shared.m_teleportable = true;
            shared.m_icons = new[] { shared.m_icons?.FirstOrDefault() };
            Assets.Icon(go, def.name, Quaternion.Euler(25, 30, 0), icon => shared.m_icons[0] = icon);
            ItemDrop drop = go.GetComponent<ItemDrop>();
            drop.m_itemData.m_shared = shared; drop.m_itemData.m_stack = 1; drop.m_itemData.m_quality = 1; drop.m_itemData.m_dropPrefab = go;
            drop.m_autoDestroy = false; // (a god's relic: never tidied away off the ground)
            Prefabs[def.name] = go;
            return go;
        }
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
            shared.m_icons = new[] { shared.m_icons?.FirstOrDefault() }; // (its own: the clone shares the source item's)
            Assets.Icon(go, RelicName, Quaternion.Euler(20, 30, 0), icon => shared.m_icons[0] = icon); // (in place: items already made share the array)
            ItemDrop drop = go.GetComponent<ItemDrop>();
            drop.m_itemData.m_shared = shared; drop.m_itemData.m_stack = 1; drop.m_itemData.m_quality = 1; drop.m_itemData.m_dropPrefab = go;
            drop.m_autoDestroy = false; // (a colony's whole record: never tidied away)
            Prefabs[RelicName] = go;
            return go;
        }
        // A lost soul's remains, left when their draugr falls (whose, in the item's own data): laid to rest in the burial ground.
        public static GameObject Remains => EnsureRemains();
        private static GameObject EnsureRemains()
        {
            if (Prefabs.TryGetValue(RemainsPrefab, out GameObject made) && made != null) return made;
            GameObject source = Assets.Find("BoneFragments");
            ItemDrop sourceDrop = source != null ? source.GetComponent<ItemDrop>() : null;
            if (sourceDrop == null) return null;
            GameObject go = Object.Instantiate(source, Assets.Holder);
            go.name = RemainsPrefab;
            var shared = (ItemDrop.ItemData.SharedData)CloneShared.Invoke(sourceDrop.m_itemData.m_shared, null);
            Assets.Tint(go, new Color(0.75f, 0.8f, 0.72f), new Color(0.06f, 0.12f, 0.08f));
            // (bigger than a handful of bone fragments, and a pale glow about them: found in the grass at night, not missed among the entrails)
            Transform look = go.transform.childCount > 0 ? go.transform.GetChild(0) : null;
            if (look != null) look.localScale *= 1.8f;
            Glow(go.transform);
            shared.m_name = "Remains of the lost";
            shared.m_description = "What was left when one of the colony's own, taken by Hel, fell for the last time. Bring them to the colony's burial ground and use them there (a hotbar key, or right-click) to lay them to rest: their friends' grief ends, and Hel is pleased.";
            shared.m_itemType = ItemDrop.ItemData.ItemType.Misc; shared.m_maxStackSize = 1; shared.m_weight = 1f; shared.m_value = 0; shared.m_teleportable = true;
            shared.m_icons = new[] { shared.m_icons?.FirstOrDefault() };
            Assets.Icon(go, RemainsPrefab, Quaternion.Euler(30, 20, 0), icon => shared.m_icons[0] = icon);
            ItemDrop drop = go.GetComponent<ItemDrop>();
            drop.m_itemData.m_shared = shared; drop.m_itemData.m_stack = 1; drop.m_itemData.m_quality = 1; drop.m_itemData.m_dropPrefab = go;
            drop.m_autoDestroy = false; // (someone's remains: never tidied away)
            Prefabs[RemainsPrefab] = go;
            return go;
        }
        // Whose remains, at the top of the tooltip.
        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip), typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool))]
        private static class Whose
        {
            private static void Postfix(ItemDrop.ItemData item, ref string __result)
            {
                if (item?.m_customData != null && item.m_customData.TryGetValue(Lost.RemainsName, out string name)) __result = $"<color=#c9d6c2>{name}'s remains.</color>\n" + __result;
            }
        }
        public static void Glow(Transform remains)
        {
            var glow = new GameObject("remains_glow"); glow.transform.SetParent(remains, false); glow.transform.localPosition = Vector3.up * 0.3f;
            Light l = glow.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(0.7f, 0.9f, 0.75f); l.range = 3.5f; l.intensity = 1.4f; l.shadows = LightShadows.None;
        }
        // On the ground too: "Hervor's remains".
        [HarmonyPatch(typeof(ItemDrop), nameof(ItemDrop.GetHoverName))]
        private static class WhoseOnGround
        {
            private static void Postfix(ItemDrop __instance, ref string __result)
            {
                if (__instance?.m_itemData?.m_customData != null && __instance.m_itemData.m_customData.TryGetValue(Lost.RemainsName, out string name)) __result = $"{name}'s remains";
            }
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
            // A drum of the tankard's own wood: a broad, shallow shell, hide heads top and bottom, dark cords laced round it.
            MeshRenderer mug = go.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault();
            if (mug != null)
            {
                Material wood = mug.sharedMaterials.FirstOrDefault(m => m != null && m.name.StartsWith("mug")) ?? mug.sharedMaterial;
                Material hide = Pieces.Plain(wood, new Color(0.86f, 0.78f, 0.62f)), cord = Pieces.Plain(wood, new Color(0.18f, 0.12f, 0.08f)), shell = Pieces.Plain(wood, new Color(0.45f, 0.27f, 0.14f)); // (the mug's texture is an atlas: plain colours)
                Transform at = mug.transform.parent;
                Vector3 centre = mug.transform.localPosition;
                mug.gameObject.SetActive(false);
                var drum = new GameObject("drum"); drum.transform.SetParent(at, false); drum.transform.localPosition = centre; drum.transform.localRotation = Quaternion.Euler(90, 0, 0);
                Part(drum.transform, PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.42f, 0.11f, 0.42f), shell);
                Part(drum.transform, PrimitiveType.Cylinder, new Vector3(0, 0.1f, 0), new Vector3(0.43f, 0.012f, 0.43f), cord); // rims
                Part(drum.transform, PrimitiveType.Cylinder, new Vector3(0, -0.1f, 0), new Vector3(0.43f, 0.012f, 0.43f), cord);
                Part(drum.transform, PrimitiveType.Cylinder, new Vector3(0, 0.112f, 0), new Vector3(0.40f, 0.004f, 0.40f), hide);
                Part(drum.transform, PrimitiveType.Cylinder, new Vector3(0, -0.112f, 0), new Vector3(0.40f, 0.004f, 0.40f), hide);
                for (int k = 0; k < 8; k++)
                {
                    float t = k * Mathf.PI / 4;
                    Transform c = Part(drum.transform, PrimitiveType.Cube, new Vector3(Mathf.Cos(t) * 0.212f, 0, Mathf.Sin(t) * 0.212f), new Vector3(0.01f, 0.2f, 0.01f), cord);
                    c.localRotation = Quaternion.Euler(0, -t * Mathf.Rad2Deg, 8f); // (laced at a slant)
                }
            }
            var shared = (ItemDrop.ItemData.SharedData)CloneShared.Invoke(sourceDrop.m_itemData.m_shared, null);
            shared.m_name = "Skald's drum";
            shared.m_description = "Deer hide stretched over a hoop of wood. Give it to a skald: at dusk they play it in the great hall, and everyone who hears it is the merrier.";
            shared.m_itemType = ItemDrop.ItemData.ItemType.Misc; shared.m_maxStackSize = 1; shared.m_weight = 2f; shared.m_value = 0; shared.m_teleportable = true;
            shared.m_consumeStatusEffect = null; shared.m_food = 0; shared.m_foodStamina = 0; shared.m_foodEitr = 0;
            shared.m_icons = new[] { shared.m_icons?.FirstOrDefault() }; // (its own: the clone shares the source item's)
            Assets.Icon(go, DrumName, Quaternion.Euler(40, 20, 0), icon => shared.m_icons[0] = icon); // (in place: items already made share the array)
            ItemDrop drop = go.GetComponent<ItemDrop>();
            drop.m_itemData.m_shared = shared; drop.m_itemData.m_stack = 1; drop.m_itemData.m_quality = 1; drop.m_itemData.m_dropPrefab = go;
            Prefabs[DrumName] = go;
            return go;
        }
        private static Transform Part(Transform parent, PrimitiveType shape, Vector3 at, Vector3 size, Material material)
        {
            GameObject g = GameObject.CreatePrimitive(shape);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false); g.transform.localPosition = at; g.transform.localScale = size;
            if (material != null) g.GetComponent<MeshRenderer>().sharedMaterial = material;
            return g.transform;
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
            shared.m_icons = new[] { shared.m_icons?.FirstOrDefault() }; // (its own: the clone shares the source item's)
            Assets.Icon(go, RingName, Quaternion.Euler(30, 20, 0), icon => shared.m_icons[0] = icon); // (in place: items already made share the array)
            ItemDrop drop = go.GetComponent<ItemDrop>();
            drop.m_itemData.m_shared = shared; drop.m_itemData.m_stack = 1; drop.m_itemData.m_quality = 1; drop.m_itemData.m_dropPrefab = go;
            Prefabs[RingName] = go;
            return go;
        }

        public static void RegisterScene(ZNetScene scene) { GameObject ring = Ensure(); if (ring != null) Assets.Register(scene, ring); GameObject relic = EnsureRelic(); if (relic != null) Assets.Register(scene, relic); GameObject drum = EnsureDrum(); if (drum != null) Assets.Register(scene, drum); GameObject remains = EnsureRemains(); if (remains != null) Assets.Register(scene, remains); foreach (string n in Relics.Ours) { GameObject r = GodRelic(n); if (r != null) Assets.Register(scene, r); } }
        public static void BeforeRegisters(ObjectDB db)
        {
            db.m_items.RemoveAll(i => i == null || i.name == RingName && i != Ensure() || i.name == RelicName && i != EnsureRelic() || i.name == DrumName && i != EnsureDrum() || i.name == RemainsPrefab && i != EnsureRemains() || Relics.Ours.Contains(i.name) && i != GodRelic(i.name));
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
            GameObject remains = EnsureRemains();
            if (remains != null && !db.m_items.Contains(remains)) db.m_items.Add(remains);
            foreach (string n in Relics.Ours) { GameObject r = GodRelic(n); if (r != null && !db.m_items.Contains(r)) db.m_items.Add(r); }
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
