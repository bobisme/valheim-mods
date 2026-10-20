using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Byre
{
    // The herd's things: wool, goat's and cow's milk, goose eggs and truffles (what the animals give), the shears and the milk pail (what
    // takes it). Each a copy of a game item of its kind with its own model (Models/goods.py, baked) and icon; the shears and the pail copies
    // of the club with no harm in them, so they can be held as well as carried; the goose egg the hen's egg, bigger (it hatches a gosling
    // as the hen's hatches a chick). ByreWool is the wool other mods spin (Homespun, through Byre.Api.IsWool).
    internal static class Items
    {
        internal sealed class Def
        {
            public string Name, From, Model, Title, Description; public ItemDrop.ItemData.ItemType? Type; public int Stack; public float Weight;
            public float Scale = 1; // (Model null: the source's own look kept, this much bigger)
            public float Food, Stamina, Regen, Burn;
            // Held: the turn of the model in the hand (the equipoffset's), and the point of the model the fist closes on (the model's own
            // frame); null: never held. Untuned in the game: byre grip tries others live.
            public Vector3? Hand; public Vector3 Grip;
            public Quaternion? IconLook; // (the icon camera's turn; null: Icon's own choice)
        }
        internal static readonly Def[] All =
        {
            new Def { Name = "ByreWool", From = "LeatherScraps", Model = "wool", Title = "Wool", Type = ItemDrop.ItemData.ItemType.Material, Stack = 50, Weight = 0.3f,
                IconLook = Quaternion.Euler(22, -28, 30), // (from above one end: the roll's spiral and its band, corner to corner)
                Description = "Shorn from a tame sheep: greasy, warm, smelling of the field. Kept for spinning." },
            new Def { Name = "ByreGoatMilk", From = "Honey", Model = "milk", Title = "Goat's milk", Type = ItemDrop.ItemData.ItemType.Consumable, Stack = 10, Weight = 0.5f,
                Food = 12, Stamina = 30, Regen = 2, Burn = 1200,
                IconLook = Quaternion.Euler(15, 90, 0), // (side on, a little from above: the spout, the handle and the milk in its mouth)
                Description = "A jug of milk from a tame goat, rich and a little sour. Good for the stamina." },
            new Def { Name = "ByreCowMilk", From = "Honey", Model = "milk", Title = "Cow's milk", Type = ItemDrop.ItemData.ItemType.Consumable, Stack = 10, Weight = 0.5f,
                Food = 10, Stamina = 38, Regen = 2, Burn = 1500,
                IconLook = Quaternion.Euler(15, -90, 0), // (from the other side: the goat's milk's icon shows the spout to the right, this one to the left)
                Description = "A jug of milk from a tame cow, sweet and creamy. Good for the stamina, and it lasts." },
            new Def { Name = "ByreGooseEgg", From = "ChickenEgg", Model = null, Scale = 1.35f, Title = "Goose egg", Stack = 20, Weight = 0.3f,
                Description = "A big white egg from a tame goose. Kept warm, under a roof by a fire, it hatches a gosling; or boil it in the cauldron." },
            new Def { Name = "ByreTruffle", From = "Mushroom", Model = "truffle", Title = "Truffle", Type = ItemDrop.ItemData.ItemType.Consumable, Stack = 20, Weight = 0.1f,
                Food = 22, Stamina = 16, Regen = 3, Burn = 1800, IconLook = Quaternion.Euler(30, 20, 0),
                Description = "A black truffle, rooted up by a pig in the Black Forest. Earthy, rich: a little goes a long way." },
            // (the shears stand on their bow, blades up: as the spindle and Godforged's hammer, a shaft along y turned to lie in the fist)
            new Def { Name = "ByreShears", From = "Club", Model = "shears", Title = "Shears", Type = ItemDrop.ItemData.ItemType.OneHandedWeapon, Stack = 1, Weight = 0.5f,
                Hand = new Vector3(180, 0, 0), Grip = new Vector3(0, 0.078f, 0), // (tuned live 2026-10-18: blades forward past the fingers)
                Description = "Spring shears of one bent bar, the grips bound in leather. Carry them and use a tame sheep in full fleece (E) to shear it." },
            // (the pail stands on its foot, the handle bar along x at the top: turned so the bar lies where the shears' grips do)
            new Def { Name = "ByreMilkPail", From = "Club", Model = "pail", Title = "Milk pail", Type = ItemDrop.ItemData.ItemType.OneHandedWeapon, Stack = 1, Weight = 1f,
                Hand = new Vector3(180, 0, 90), Grip = new Vector3(0, 0.29f, 0), IconLook = Quaternion.Euler(28, -35, 0), // (Hand tuned live 2026-10-18: the handle bar through the fist, the pail hanging below)
                Description = "A stave pail, two staves standing up to hold its handle. Carry it and use a tame goat (E) to milk it." },
        };
        internal static Recipe ShearsRecipe, PailRecipe, EggRecipe;
        private static readonly MethodInfo CloneShared = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);

        internal static GameObject Make(ZNetScene scene, Transform holder, Def d)
        {
            GameObject source = Costs.FindItem(d.From)?.gameObject;
            ItemDrop sourceDrop = source != null ? source.GetComponent<ItemDrop>() : null;
            if (sourceDrop == null) { Plugin.Warn($"No {d.From} to make the {d.Title} from."); return null; }
            // (drawn as the trough is, with wood_pole's piece material wearing the bake; the source's own if that's missing)
            Material basis = Trough.Wood(scene) ?? source.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).Select(r => r.sharedMaterial).FirstOrDefault(m => m != null);
            GameObject go = Object.Instantiate(source, holder, false); go.name = d.Name;
            if (d.Model == null) go.transform.localScale = source.transform.localScale * d.Scale; // (the source's look, made bigger)
            else
            {
                foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
                foreach (Transform t in go.transform.Cast<Transform>().Where(t => t.name.StartsWith("attach") || t.name == "equipoffset").ToList()) Object.DestroyImmediate(t.gameObject);
                // (the model under "attach": what lies on the ground, and what the hand holds)
                GameObject look = Models.Replace(go, d.Model, basis, "attach");
                if (look == null) { Object.DestroyImmediate(go); return null; }
                Bounds b = look.GetComponent<MeshFilter>().sharedMesh.bounds;
                var box = go.AddComponent<BoxCollider>(); box.center = b.center; box.size = Vector3.Max(b.size, Vector3.one * 0.06f);
                if (d.Hand.HasValue) Hold(go, d.Hand.Value, d);
            }

            ItemDrop drop = go.GetComponent<ItemDrop>();
            // (a copy of the shared data: the scraps and the honey keep their own name, icon and stack)
            var shared = (ItemDrop.ItemData.SharedData)CloneShared.Invoke(sourceDrop.m_itemData.m_shared, null);
            shared.m_name = d.Title; shared.m_description = d.Description; shared.m_itemType = d.Type ?? shared.m_itemType;
            shared.m_maxStackSize = d.Stack; shared.m_autoStack = d.Stack > 1; shared.m_weight = d.Weight;
            shared.m_value = 0; // (never sold: shearing would be a coin machine)
            shared.m_teleportable = true; shared.m_questItem = false; shared.m_maxQuality = 1; shared.m_useDurability = false;
            shared.m_food = d.Food; shared.m_foodStamina = d.Stamina; shared.m_foodRegen = d.Regen; shared.m_foodBurnTime = d.Burn; shared.m_foodEitr = 0;
            shared.m_consumeStatusEffect = null;
            if (d.Hand.HasValue)
            {
                // (a club no more: no damage, no blocking to speak of, and it never wears out)
                shared.m_damages = new HitData.DamageTypes(); shared.m_damagesPerLevel = new HitData.DamageTypes();
                shared.m_attack = shared.m_attack?.Clone(); shared.m_secondaryAttack = shared.m_secondaryAttack?.Clone();
                shared.m_maxDurability = 100; shared.m_blockPower = 0; shared.m_deflectionForce = 0; shared.m_timedBlockBonus = 0;
                shared.m_skillType = Skills.SkillType.None; shared.m_attackForce = 0; shared.m_backstabBonus = 1; shared.m_variants = 0; shared.m_dlc = "";
                shared.m_setName = ""; shared.m_setSize = 0; shared.m_setStatusEffect = null; shared.m_equipStatusEffect = null;
            }
            drop.m_itemData.m_shared = shared; drop.m_itemData.m_stack = 1; drop.m_itemData.m_quality = 1; drop.m_itemData.m_dropPrefab = go;
            drop.m_itemData.m_durability = shared.m_maxDurability;
            Sprite icon = Icon.MakeItem(go, setIcon: false, look: d.IconLook);
            shared.m_icons = icon != null ? new[] { icon } : sourceDrop.m_itemData.m_shared.m_icons;
            if (icon != null) { Models.Owned.Add(icon); Models.Owned.Add(icon.texture); }
            Icon.Later(() => { if (go != null && Icon.MakeItem(go, setIcon: false, look: d.IconLook) is Sprite again) { shared.m_icons = new[] { again }; Models.Owned.Add(again); Models.Owned.Add(again.texture); } });
            return go;
        }

        // The goose egg hatches a gosling (made after the items: set once it is), tame, as the hen's egg hatches a chick.
        internal static void Hatches(GameObject egg, GameObject young)
        {
            if (egg != null && young != null && egg.GetComponent<EggGrow>() is EggGrow grow) { grow.m_grownPrefab = young; grow.m_tamed = true; }
        }

        // How the hand holds it: the game puts the attach child at the hand joint, then moves it by an "equipoffset" child's position and
        // turns it by its rotation; the position brings the grip point to the joint.
        internal static void Hold(GameObject go, Vector3 euler, Def d)
        {
            Transform grip = go.transform.Find("equipoffset");
            if (grip == null) { grip = new GameObject("equipoffset") { layer = go.layer }.transform; grip.SetParent(go.transform, false); }
            grip.localRotation = Quaternion.Euler(euler);
            grip.localPosition = -(grip.localRotation * d.Grip);
        }

        // The two made ones' recipes (wool and milk come only from the animals), and the goose egg boiled. Into the given database (null: the world's).
        internal static void Recipes(IDictionary<string, GameObject> made, ObjectDB db)
        {
            Plugin p = Plugin.Instance;
            if (p == null || ZNetScene.instance == null) return; // (no stations yet: the scene's turn comes)
            CraftingStation forge = Costs.FindStation("forge", out _), bench = Costs.FindStation("piece_workbench", out _);
            // (the default cost first: a setting with a mistake in it then keeps that, never nothing)
            Piece.Requirement[] Default(BepInEx.Configuration.ConfigEntry<string> e) => Costs.Parse((string)e.DefaultValue, null, forRecipe: true);
            if (made.TryGetValue("ByreShears", out GameObject shears)) ShearsRecipe = Content.Recipe(shears, forge, 1, 1, Default(p.ShearsCost), p.ShearsCost.Value, p.Logs, db: db);
            if (made.TryGetValue("ByreMilkPail", out GameObject pail)) PailRecipe = Content.Recipe(pail, bench, 1, 1, Default(p.PailCost), p.PailCost.Value, p.Logs, db: db);
            // (goose eggs boiled as the hen's are, two to an egg: the cauldron's own cooked egg)
            CraftingStation cauldron = Costs.FindStation("piece_cauldron", out _);
            GameObject cooked = Costs.FindItem("CookedEgg")?.gameObject;
            if (made.TryGetValue("ByreGooseEgg", out GameObject egg) && cooked != null && cauldron != null)
                EggRecipe = Content.Recipe(cooked, cauldron, 1, 2, new[] { new Piece.Requirement { m_resItem = egg.GetComponent<ItemDrop>(), m_amount = 1, m_recover = false } }, way: "ByreGooseEgg", db: db);
            if (ShearsRecipe != null) p.Synced.NeedsServer(ShearsRecipe);
            if (PailRecipe != null) p.Synced.NeedsServer(PailRecipe);
        }
    }
}
