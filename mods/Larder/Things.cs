using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Larder
{
    // Larder's foods, each a copy of the game's honey (a food that stacks, eaten from the bag) with a model of its own and its own
    // numbers (Core/Dairy.cs). Then the churn's recipes, once Byre's milk is there to make them from: without Byre there is none, and
    // no recipe is added (the foods stay registered, so any already made are kept).
    internal static class Things
    {
        private const string Base = "Honey";
        private static GameObject _holder;
        private static readonly Dictionary<string, GameObject> Built = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, Recipe> Recipes = new Dictionary<string, Recipe>();
        private static readonly MethodInfo CloneShared = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
        private static bool _registering;

        internal static GameObject Get(string prefab) => Built.TryGetValue(prefab, out GameObject go) ? go : null;
        private static Transform Holder
        {
            get
            {
                if (_holder == null) { _holder = new GameObject("Larder items"); _holder.SetActive(false); Object.DontDestroyOnLoad(_holder); }
                return _holder.transform;
            }
        }

        // Byre's milk, if Byre is installed (in the scene, or the database).
        internal static GameObject Milk(string name) => ZNetScene.instance?.GetPrefab(name) ?? (ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(name) : null);
        internal static bool HaveMilk => Names.Milks.Any(m => Milk(m) != null);

        // Builds everything once and puts it (back) where the game looks: ZNetScene, ObjectDB and the recipes. Called from the scene's
        // and the database's Awake and the database's copy at a world load, and the plugin's Awake: safe to repeat.
        internal static void Register()
        {
            if (_registering) return;
            ZNetScene scene = ZNetScene.instance;
            if (scene == null || scene.GetPrefab("Wood") == null) return;
            _registering = true;
            try
            {
                var items = new List<GameObject>();
                foreach (Food f in Dairy.Foods)
                {
                    try { if (Make(scene, f) is GameObject go) items.Add(go); }
                    catch (Exception e) { Plugin.Warn($"Could not make the {f.Title}: {e}"); }
                }
                ObjectDB db = ObjectDB.instance != null && ObjectDB.instance.GetItemPrefab("Wood") != null ? ObjectDB.instance : null;
                ItemReload.Replace(scene, db, items);
                Kit.Ensure(force: true); // (the stations: the press's queue names these items)
                if (db != null) AddRecipes(db);
            }
            finally { _registering = false; }
        }

        private static GameObject Make(ZNetScene scene, Food f)
        {
            if (Built.TryGetValue(f.Prefab, out GameObject had) && had != null) return had;
            ItemDrop sourceDrop = scene.GetPrefab(Base)?.GetComponent<ItemDrop>();
            if (sourceDrop == null) { Plugin.Warn($"{f.Title}: the game has no {Base} to copy"); return null; }
            GameObject go = Object.Instantiate(sourceDrop.gameObject, Holder); go.name = f.Prefab;
            foreach (LODGroup lod in go.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lod);
            for (int i = go.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
            foreach (Collider c in go.GetComponents<Collider>()) Object.DestroyImmediate(c);
            foreach (Renderer r in go.GetComponents<Renderer>()) Object.DestroyImmediate(r);
            foreach (MeshFilter m in go.GetComponents<MeshFilter>()) Object.DestroyImmediate(m);
            // (the model under "attach": what lies on the ground)
            GameObject attach = Looks.Add(go.transform, scene, f.Model, "attach") ?? new GameObject("attach");
            attach.transform.SetParent(go.transform, false); attach.layer = go.layer;
            Bounds b = attach.GetComponent<MeshFilter>()?.sharedMesh?.bounds ?? new Bounds(Vector3.zero, Vector3.one * 0.12f);
            var box = go.AddComponent<BoxCollider>(); box.center = b.center; box.size = Vector3.Max(b.size, Vector3.one * 0.06f); // (what it's picked up by)
            ItemDrop drop = go.GetComponent<ItemDrop>();
            var s = (ItemDrop.ItemData.SharedData)CloneShared.Invoke(sourceDrop.m_itemData.m_shared, null); // (its own copy: never write into the honey's)
            s.m_name = f.Title; s.m_description = f.Description; s.m_weight = f.Weight; s.m_maxStackSize = f.Stack; s.m_autoStack = true; s.m_value = 0;
            s.m_itemType = ItemDrop.ItemData.ItemType.Consumable;
            s.m_maxQuality = 1; s.m_variants = 0; s.m_dlc = ""; s.m_questItem = false; s.m_teleportable = true; s.m_useDurability = false;
            s.m_food = f.Health; s.m_foodStamina = f.Stamina; s.m_foodRegen = f.Regen; s.m_foodBurnTime = f.Seconds; s.m_foodEitr = 0; s.m_consumeStatusEffect = null;
            drop.m_itemData.m_shared = s;
            drop.m_itemData.m_dropPrefab = go; // (without it the item is saved with no name and lost)
            drop.m_itemData.m_stack = 1; drop.m_itemData.m_quality = 1;
            Built[f.Prefab] = go;
            if (Icon.MakeItem(go, setIcon: true) is Sprite icon) { Looks.Owned.Add(icon); Looks.Owned.Add(icon.texture); }
            Icon.Later(() => { if (go != null && Icon.MakeItem(go, setIcon: true) is Sprite again) { Looks.Owned.Add(again); Looks.Owned.Add(again.texture); } });
            return go;
        }

        // The churn's recipes, for the milk there is. Called again once a second by the kit until they're all in (Byre may come
        // after us), and at every world load (the database's lists are new then).
        internal static void AddRecipes(ObjectDB db)
        {
            if (db == null) return;
            db.m_recipes.RemoveAll(r => r == null || r.name.StartsWith("Recipe_Larder_") && !Recipes.ContainsValue(r));
            CraftingStation churn = Kit.Station(Names.Churn);
            if (churn == null) return;
            foreach (Make m in Dairy.Churned())
            {
                string milk = m.Needs[0].item;
                if (Milk(milk) == null) continue; // (no Byre, or not this milk: nothing to make it from)
                Add(db, "Recipe_" + m.Item, m.Item, m.Amount, churn, m.Needs);
            }
        }
        internal static bool RecipesIn(ObjectDB db) => db != null && Recipes.Count > 0 && Recipes.Values.All(r => r != null && db.m_recipes.Contains(r));
        private static void Add(ObjectDB db, string name, string item, int amount, CraftingStation station, (string item, int n)[] needs)
        {
            if (!Recipes.TryGetValue(name, out Recipe r) || r == null)
            {
                ItemDrop made = (Get(item) ?? db.GetItemPrefab(item))?.GetComponent<ItemDrop>();
                if (made == null) { Plugin.Warn($"{name}: no {item}"); return; }
                var reqs = new List<Piece.Requirement>();
                foreach (var (res, n) in needs)
                {
                    ItemDrop d = (Get(res) ?? Milk(res))?.GetComponent<ItemDrop>();
                    if (d == null) { Plugin.Warn($"{name}: the game has no {res}; not made"); return; }
                    reqs.Add(new Piece.Requirement { m_resItem = d, m_amount = n, m_recover = true });
                }
                r = ScriptableObject.CreateInstance<Recipe>(); r.name = name; r.hideFlags = HideFlags.HideAndDontSave;
                r.m_item = made; r.m_amount = amount; r.m_enabled = true; r.m_craftingStation = station; r.m_repairStation = station; r.m_minStationLevel = 1;
                r.m_resources = reqs.ToArray();
                Recipes[name] = r;
            }
            if (!db.m_recipes.Contains(r)) db.m_recipes.Add(r);
        }

        // Unloading: recipes out; the items stay registered inside a world (ItemReload: the next copy replaces them by name).
        internal static void Unload()
        {
            if (ObjectDB.instance != null) foreach (Recipe r in Recipes.Values) ObjectDB.instance.m_recipes.Remove(r);
            foreach (Recipe r in Recipes.Values) if (r != null) Object.Destroy(r);
            Recipes.Clear();
            if (!ItemReload.KeepOnUnload && _holder != null) Object.Destroy(_holder);
            Built.Clear(); _holder = null;
        }

        // After a hot reload: items in bags, chests and on the ground point at this copy's prefabs.
        internal static void Repoint() => ItemReload.Repoint(Get);
    }
}
