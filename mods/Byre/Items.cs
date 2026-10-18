using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Byre
{
    // The herd's things: wool and goat's milk (what the animals give), the shears and the milk pail (what takes it). Each a copy of a game
    // item of its kind with its own Blender model and icon. ByreWool is the wool other mods spin (Homespun, through Byre.Api.IsWool).
    internal static class Items
    {
        internal sealed class Def
        {
            public string Name, From, Model, Title, Description; public ItemDrop.ItemData.ItemType Type; public int Stack; public float Weight;
            public float Food, Stamina, Regen, Burn;
        }
        internal static readonly Def[] All =
        {
            new Def { Name = "ByreWool", From = "LeatherScraps", Model = "wool", Title = "Wool", Type = ItemDrop.ItemData.ItemType.Material, Stack = 50, Weight = 0.3f,
                Description = "Shorn from a tame sheep: greasy, warm, smelling of the field. Kept for spinning." },
            new Def { Name = "ByreGoatMilk", From = "Honey", Model = "milk", Title = "Goat's milk", Type = ItemDrop.ItemData.ItemType.Consumable, Stack = 10, Weight = 0.5f,
                Food = 12, Stamina = 30, Regen = 2, Burn = 1200,
                Description = "A jug of milk from a tame goat, rich and a little sour. Good for the stamina." },
            new Def { Name = "ByreShears", From = "LeatherScraps", Model = "shears", Title = "Shears", Type = ItemDrop.ItemData.ItemType.Misc, Stack = 1, Weight = 0.5f,
                Description = "Spring shears of one bent bar. Carry them and use a tame sheep in full fleece (E) to shear it." },
            new Def { Name = "ByreMilkPail", From = "LeatherScraps", Model = "pail", Title = "Milk pail", Type = ItemDrop.ItemData.ItemType.Misc, Stack = 1, Weight = 1f,
                Description = "A stave-built pail. Carry it and use a tame goat (E) to milk it." },
        };
        internal static Recipe ShearsRecipe, PailRecipe;
        private static readonly MethodInfo CloneShared = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

        internal static GameObject Make(Transform holder, Def d)
        {
            GameObject source = Costs.FindItem(d.From)?.gameObject;
            ItemDrop sourceDrop = source != null ? source.GetComponent<ItemDrop>() : null;
            if (sourceDrop == null) { Plugin.Warn($"No {d.From} to make the {d.Title} from."); return null; }
            Material basis = source.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).Select(r => r.sharedMaterial).FirstOrDefault(m => m != null);
            GameObject go = Object.Instantiate(source, holder, false); go.name = d.Name;
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            GameObject look = Models.Replace(go, d.Model, basis);
            if (look == null) { Object.DestroyImmediate(go); return null; }
            Bounds b = look.GetComponent<MeshFilter>().sharedMesh.bounds;
            var box = go.AddComponent<BoxCollider>(); box.center = b.center; box.size = Vector3.Max(b.size, Vector3.one * 0.06f);

            ItemDrop drop = go.GetComponent<ItemDrop>();
            // (a copy of the shared data: the scraps and the honey keep their own name, icon and stack)
            var shared = (ItemDrop.ItemData.SharedData)CloneShared.Invoke(sourceDrop.m_itemData.m_shared, null);
            shared.m_name = d.Title; shared.m_description = d.Description; shared.m_itemType = d.Type;
            shared.m_maxStackSize = d.Stack; shared.m_autoStack = d.Stack > 1; shared.m_weight = d.Weight;
            shared.m_value = 0; // (never sold: shearing would be a coin machine)
            shared.m_teleportable = true; shared.m_questItem = false; shared.m_maxQuality = 1; shared.m_useDurability = false;
            shared.m_food = d.Food; shared.m_foodStamina = d.Stamina; shared.m_foodRegen = d.Regen; shared.m_foodBurnTime = d.Burn; shared.m_foodEitr = 0;
            shared.m_consumeStatusEffect = null;
            drop.m_itemData.m_shared = shared; drop.m_itemData.m_stack = 1; drop.m_itemData.m_quality = 1; drop.m_itemData.m_dropPrefab = go;
            Sprite icon = Icon.MakeItem(go, setIcon: false);
            shared.m_icons = icon != null ? new[] { icon } : sourceDrop.m_itemData.m_shared.m_icons;
            if (icon != null) { Models.Owned.Add(icon); Models.Owned.Add(icon.texture); }
            return go;
        }

        // The two made ones' recipes (wool and milk come only from the animals). Into the given database (null: the world's).
        internal static void Recipes(IDictionary<string, GameObject> made, ObjectDB db)
        {
            Plugin p = Plugin.Instance;
            if (p == null || ZNetScene.instance == null) return; // (no stations yet: the scene's turn comes)
            CraftingStation forge = Costs.FindStation("forge", out _), bench = Costs.FindStation("piece_workbench", out _);
            // (the default cost first: a setting with a mistake in it then keeps that, never nothing)
            Piece.Requirement[] Default(BepInEx.Configuration.ConfigEntry<string> e) => Costs.Parse((string)e.DefaultValue, null, forRecipe: true);
            if (made.TryGetValue("ByreShears", out GameObject shears)) ShearsRecipe = Content.Recipe(shears, forge, 1, 1, Default(p.ShearsCost), p.ShearsCost.Value, p.Logs, db: db);
            if (made.TryGetValue("ByreMilkPail", out GameObject pail)) PailRecipe = Content.Recipe(pail, bench, 1, 1, Default(p.PailCost), p.PailCost.Value, p.Logs, db: db);
            if (ShearsRecipe != null) p.Synced.NeedsServer(ShearsRecipe);
            if (PailRecipe != null) p.Synced.NeedsServer(PailRecipe);
        }
    }
}
