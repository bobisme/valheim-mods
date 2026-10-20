using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Homespun
{
    // Homespun's items, each a copy of a game item so it behaves as one: the fibres, thread, cloth and bands copies of linen thread
    // (stacked, light, dropped and picked up as it is) with models of their own; the drop spindle a copy of the club (held in the right
    // hand); and the garments copies of the game's clothes (their worn look, their slot) in undyed wool and linen. Then the recipes:
    // the spindle at the workbench, bolts and bands at the loom, garments and Hildir's clothes at the sewing table (Core/Chain.cs).
    internal static class Things
    {
        internal sealed class ItemDef { public string Prefab, Title, Description, Base, Model; public float Weight; public int Stack; public Color? Tint, Dyed; public bool Utility; }
        internal static readonly ItemDef[] Materials =
        {
            new ItemDef { Prefab = Names.RettedFlax, Base = "LinenThread", Model = "retted", Title = "Retted flax", Weight = 0.3f, Stack = 50,
                Description = "Flax soaked till the stalks rot loose from the fibre in them. Break and comb it at a flax brake." },
            new ItemDef { Prefab = Names.LineFlax, Base = "LinenThread", Model = "line", Title = "Line flax", Weight = 0.2f, Stack = 50,
                Description = "The long fibre combed out of retted flax, a pale gold strick of it. Spun on a drop spindle or a spinning wheel, a strick makes two linen threads." },
            // (nettle: the plant's own bundle; retted and combed, flax's looks in nettle's greyer green)
            new ItemDef { Prefab = Names.Nettle, Base = "LinenThread", Model = "nettle", Title = "Nettle", Weight = 0.3f, Stack = 50,
                Description = "Stinging nettle, cut with a glove on. Soaked and combed as flax is, it spins into linen thread: coarser and shorter in the fibre, a nettle gives half what a flax does. Sow it with the cultivator." },
            new ItemDef { Prefab = Names.RettedNettle, Base = "LinenThread", Model = "retted", Tint = new Color(0.85f, 0.97f, 0.8f), Title = "Retted nettle", Weight = 0.3f, Stack = 50,
                Description = "Nettle soaked till the stalks rot loose from the fibre, and the sting with them. Break and comb it at a flax brake." },
            new ItemDef { Prefab = Names.LineNettle, Base = "LinenThread", Model = "line", Tint = new Color(0.85f, 0.97f, 1.15f), Title = "Line nettle", Weight = 0.2f, Stack = 50,
                Description = "The fibre combed out of retted nettle, greyer and coarser than flax's. Spun on a drop spindle or a spinning wheel, a strick makes one linen thread." },
            new ItemDef { Prefab = Names.Wool, Base = "LinenThread", Model = "wool", Title = "Combed wool", Weight = 0.1f, Stack = 50,
                Description = "Wool combed out of a lox's pelt, ready to spin into yarn." },
            new ItemDef { Prefab = Names.Yarn, Base = "LinenThread", Model = "yarn", Title = "Wool yarn", Weight = 0.1f, Stack = 50,
                Description = "A skein of spun wool. Woven on a loom into bolts of wool and bands." },
            new ItemDef { Prefab = Names.LinenBolt, Base = "LinenThread", Model = "linenbolt", Title = "Bolt of linen", Weight = 1f, Stack = 10,
                Description = "A roll of linen cloth off the loom. Sewn into shirts and Hildir's tunics and dresses." },
            new ItemDef { Prefab = Names.WoolBolt, Base = "LinenThread", Model = "woolbolt", Title = "Bolt of wool", Weight = 1.5f, Stack = 10,
                Description = "A roll of vadmal, coarse woollen cloth, the north's own: warm, and once as good as money." },
            new ItemDef { Prefab = Names.Band, Base = "LinenThread", Model = "band", Title = "Tablet-woven band", Weight = 0.1f, Stack = 20,
                Description = "A narrow patterned band woven on cards, for the neck and hem of a tunic or a dress." },
            new ItemDef { Prefab = Names.Lichen, Base = "LinenThread", Model = "lichen", Title = "Lichen", Weight = 0.1f, Stack = 50,
                Description = "Crusty lichen scraped from a stone in the mountains or the deep forest. Steeped in a dye vat, it dyes cloth purple." },
            new ItemDef { Prefab = Names.Sail, Base = "LinenThread", Model = "sail", Title = "Wool sail", Weight = 6f, Stack = 1,
                Description = "A square sail of wool cloth, its edges roped. A knarr built on a slipway is rigged with one; dyed, she sails under its colour." },
        };
        // Nalbound mittens and socks: worn in the utility slot (as a belt is), nothing to see.
        internal static readonly ItemDef NalbindingDef = new ItemDef { Prefab = Names.Nalbinding, Base = "BeltStrength", Model = "nalbinding", Title = "Nalbound mittens and socks", Weight = 0.3f, Stack = 1, Utility = true,
            Description = "Mittens and socks looped from wool yarn with a bone needle, thick and warm. Worn, they keep off the chill of a night or a cold wind; not the mountains' freezing." };
        // Each colour of the bolts (Core/Dye.cs), as the undyed bolt with its look repainted.
        internal static readonly ItemDef[] DyedBolts = Materials.Where(d => Dye.Read(d.Prefab) != null)
            .SelectMany(d => Dye.Dyes.Select(c => new ItemDef { Prefab = Dye.Variant(d.Prefab, c), Base = d.Base, Model = d.Model, Title = Dye.Title(d.Title, c), Weight = d.Weight, Stack = d.Stack,
                Description = Dye.Description(d.Description, c), Dyed = Paint(c) })).ToArray();
        // Each colour of the garments: the undyed garment in the dye's colour, worn and folded.
        internal static readonly Garment[] DyedGarments = Chain.Garments.SelectMany(g => Dye.Dyes.Select(c => new Garment
        {
            Prefab = Dye.Variant(g.Prefab, c), Base = g.Base, Title = Dye.Title(g.Title, c), Description = Dye.Description(g.Description, c),
            Armor = g.Armor, Weight = g.Weight, Durability = g.Durability, Tint = c.Colour, Model = g.Model, Frost = g.Frost, Hood = g.Hood,
        })).ToArray();
        internal static Color Paint(Dyestuff d) => new Color(d.Colour.r, d.Colour.g, d.Colour.b);
        internal static readonly ItemDef SpindleDef = new ItemDef { Prefab = Names.Spindle, Base = "Club", Model = "spindle", Title = "Drop spindle", Weight = 0.3f, Stack = 1,
            Description = "A stick through a stone whorl. Hold it, and with line flax, line nettle or wool in your bag you spin as you go, standing or walking: slower than a spinning wheel, but anywhere." };
        internal static readonly (string item, int n)[] SpindleCost = { ("Wood", 2), ("Stone", 1) };

        private static GameObject _holder;
        private static readonly Dictionary<string, GameObject> Built = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, Recipe> Recipes = new Dictionary<string, Recipe>();
        private static readonly MethodInfo CloneShared = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic);
        private static bool _registering;

        internal static IEnumerable<string> AllItems => Materials.Concat(DyedBolts).Select(d => d.Prefab).Append(Names.Spindle).Append(Names.Nalbinding).Concat(Chain.Garments.Concat(DyedGarments).Select(g => g.Prefab));
        internal static GameObject Get(string prefab) => Built.TryGetValue(prefab, out GameObject go) ? go : null;
        internal static bool Ours(ItemDrop.ItemData item) => item?.m_dropPrefab != null && Built.ContainsKey(item.m_dropPrefab.name);
        internal static Transform Holder
        {
            get
            {
                if (_holder == null) { _holder = new GameObject("Homespun items"); _holder.SetActive(false); Object.DontDestroyOnLoad(_holder); }
                return _holder.transform;
            }
        }

        internal static void BindCosts(BepInEx.Configuration.ConfigFile config) { } // (costs are fixed in H1: every player's must match the server's)

        // Builds everything once and puts it (back) where the game looks: ZNetScene, ObjectDB, the recipes, and the stations (Kit), whose
        // queues need these items. Called from the scene's and the database's Awake and the database's copy at a world load, and the
        // plugin's Awake: safe to repeat.
        internal static void Register()
        {
            if (_registering) return;
            ZNetScene scene = ZNetScene.instance;
            if (scene == null || scene.GetPrefab("Wood") == null) return;
            _registering = true;
            try
            {
                var items = new List<GameObject>();
                foreach (ItemDef d in Materials.Append(SpindleDef).Append(NalbindingDef).Concat(DyedBolts))
                {
                    try { if (Make(scene, d) is GameObject go) items.Add(go); }
                    catch (Exception e) { Plugin.Warn($"Could not make the {d.Title}: {e}"); }
                }
                foreach (Garment g in Chain.Garments.Concat(DyedGarments))
                {
                    try { if (Sew(scene, g) is GameObject go) items.Add(go); }
                    catch (Exception e) { Plugin.Warn($"Could not make the {g.Title}: {e}"); }
                }
                ObjectDB db = ObjectDB.instance != null && ObjectDB.instance.GetItemPrefab("Wood") != null ? ObjectDB.instance : null;
                ItemReload.Replace(scene, db, items);
                Plants.Register(scene); // (nettle: they give and are sown from the item)
                Kit.Ensure(force: true); // (the stations: their queues name these items)
                if (db != null) AddRecipes(scene, db);
            }
            finally { _registering = false; }
        }

        // A fibre, thread, cloth or the spindle: the base game item with its model taken out and ours in.
        private static GameObject Make(ZNetScene scene, ItemDef d)
        {
            if (Built.TryGetValue(d.Prefab, out GameObject had) && had != null) return had;
            ItemDrop sourceDrop = scene.GetPrefab(d.Base)?.GetComponent<ItemDrop>();
            if (sourceDrop == null) { Plugin.Warn($"{d.Title}: the game has no {d.Base} to copy"); return null; }
            GameObject go = Object.Instantiate(sourceDrop.gameObject, Holder); go.name = d.Prefab;
            foreach (LODGroup lod in go.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lod);
            for (int i = go.transform.childCount - 1; i >= 0; i--) Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
            foreach (Collider c in go.GetComponents<Collider>()) Object.DestroyImmediate(c);
            foreach (Renderer r in go.GetComponents<Renderer>()) Object.DestroyImmediate(r);
            foreach (MeshFilter m in go.GetComponents<MeshFilter>()) Object.DestroyImmediate(m);
            // (the model under "attach": what the hand holds, and what lies on the ground)
            GameObject attach = Looks.Add(go.transform, scene, d.Model, "attach");
            if (attach == null) attach = new GameObject("attach");
            else if (d.Tint is Color tint) Looks.Tint(attach, tint);
            else if (d.Dyed is Color dyed) Looks.Repaint(attach, dyed);
            attach.transform.SetParent(go.transform, false); attach.layer = go.layer;
            Bounds b = attach.GetComponent<MeshFilter>()?.sharedMesh?.bounds ?? new Bounds(Vector3.zero, Vector3.one * 0.2f);
            var box = go.AddComponent<BoxCollider>(); box.center = b.center; box.size = Vector3.Max(b.size, Vector3.one * 0.08f); // (what it's picked up by)
            if (d == SpindleDef)
            {
                // (how the hand holds it, as Godforged's hammer: the model's shaft stands along y with the grip at the origin. Untuned in the game.)
                var grip = new GameObject("equipoffset") { layer = go.layer }; grip.transform.SetParent(go.transform, false);
                grip.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            }
            ItemDrop drop = go.GetComponent<ItemDrop>();
            var s = (ItemDrop.ItemData.SharedData)CloneShared.Invoke(sourceDrop.m_itemData.m_shared, null); // (its own copy: never write into the game item's)
            s.m_name = d.Title; s.m_description = d.Description; s.m_weight = d.Weight; s.m_maxStackSize = d.Stack; s.m_value = 0; // (0: the trader never buys it)
            s.m_maxQuality = 1; s.m_variants = 0; s.m_dlc = ""; s.m_questItem = false; s.m_teleportable = true;
            if (d == SpindleDef)
            {
                // (a club no more: no damage, no blocking to speak of, and it never wears out)
                s.m_damages = new HitData.DamageTypes(); s.m_damagesPerLevel = new HitData.DamageTypes();
                s.m_attack = s.m_attack?.Clone(); s.m_secondaryAttack = s.m_secondaryAttack?.Clone();
                s.m_useDurability = false; s.m_maxDurability = 100; s.m_blockPower = 0; s.m_deflectionForce = 0; s.m_timedBlockBonus = 0;
                s.m_skillType = Skills.SkillType.None; s.m_attackForce = 0; s.m_backstabBonus = 1;
                s.m_setName = ""; s.m_setSize = 0; s.m_setStatusEffect = null; s.m_equipStatusEffect = null;
            }
            if (d.Utility)
            {
                // (a belt no more: none of its strength, nothing worn to see; what it does is Warmth.cs's)
                s.m_setName = ""; s.m_setSize = 0; s.m_setStatusEffect = null; s.m_equipStatusEffect = null;
                s.m_armor = 0; s.m_armorPerLevel = 0; s.m_useDurability = false; s.m_movementModifier = 0;
                s.m_damageModifiers = new List<HitData.DamageModPair>();
            }
            drop.m_itemData.m_shared = s;
            drop.m_itemData.m_dropPrefab = go; // (without it the item is saved with no name and lost)
            drop.m_itemData.m_stack = 1; drop.m_itemData.m_quality = 1; drop.m_itemData.m_durability = s.m_maxDurability;
            Built[d.Prefab] = go;
            if (Icon.MakeItem(go, setIcon: true) is Sprite icon) { Looks.Owned.Add(icon); Looks.Owned.Add(icon.texture); }
            Icon.Later(() => { if (go != null && Icon.MakeItem(go, setIcon: true) is Sprite again) { Looks.Owned.Add(again); Looks.Owned.Add(again.texture); } });
            return go;
        }

        // A garment: the game's own (its look when worn, its slot), renamed, its colour that of undyed wool or bleached linen, its armour ours.
        private static GameObject Sew(ZNetScene scene, Garment g)
        {
            if (Built.TryGetValue(g.Prefab, out GameObject had) && had != null) return had;
            ItemDrop sourceDrop = scene.GetPrefab(g.Base)?.GetComponent<ItemDrop>();
            if (sourceDrop == null) { Plugin.Warn($"{g.Title}: the game has no {g.Base} to make it on"); return null; }
            GameObject go = Object.Instantiate(sourceDrop.gameObject, Holder); go.name = g.Prefab;
            // (on the ground and in the bag: ours, folded, in place of the base's own. What's worn, the attach_ children, stays the base's.)
            foreach (LODGroup lod in go.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lod);
            for (int i = go.transform.childCount - 1; i >= 0; i--) if (!go.transform.GetChild(i).name.StartsWith("attach_")) Object.DestroyImmediate(go.transform.GetChild(i).gameObject);
            foreach (Collider c in go.GetComponents<Collider>()) Object.DestroyImmediate(c);
            foreach (Renderer r in go.GetComponents<Renderer>()) Object.DestroyImmediate(r);
            foreach (MeshFilter m in go.GetComponents<MeshFilter>()) Object.DestroyImmediate(m);
            GameObject folded = Looks.Add(go.transform, scene, g.Model, "attach");
            if (folded != null && Dye.Read(g.Prefab)?.dye != null) Looks.Repaint(folded, new Color(g.Tint.r, g.Tint.g, g.Tint.b));
            Bounds b = folded?.GetComponent<MeshFilter>()?.sharedMesh?.bounds ?? new Bounds(Vector3.zero, new Vector3(0.36f, 0.08f, 0.3f));
            var box = go.AddComponent<BoxCollider>(); box.center = b.center; box.size = Vector3.Max(b.size, Vector3.one * 0.08f);
            ItemDrop drop = go.GetComponent<ItemDrop>();
            var s = (ItemDrop.ItemData.SharedData)CloneShared.Invoke(sourceDrop.m_itemData.m_shared, null);
            s.m_name = g.Title; s.m_description = g.Description; s.m_weight = g.Weight; s.m_value = 0;
            s.m_armor = g.Armor; s.m_armorPerLevel = 1; s.m_maxQuality = 2; s.m_maxDurability = g.Durability; s.m_durabilityPerLevel = 50;
            s.m_variants = 0; s.m_dlc = ""; s.m_questItem = false; s.m_teleportable = true;
            s.m_setName = ""; s.m_setSize = 0; s.m_setStatusEffect = null; s.m_equipStatusEffect = null;
            s.m_damageModifiers = new List<HitData.DamageModPair>(); s.m_movementModifier = 0;
            if (g.Frost) s.m_damageModifiers.Add(new HitData.DamageModPair { m_type = HitData.DamageType.Frost, m_modifier = HitData.DamageModifier.Resistant }); // (as a wolf fur cape: no cold, no freezing)
            Looks.Recolour(go, s, new Color(g.Tint.r, g.Tint.g, g.Tint.b)); // (worn: on our copy of the shared data, never the base's)
            drop.m_itemData.m_shared = s;
            drop.m_itemData.m_dropPrefab = go;
            drop.m_itemData.m_stack = 1; drop.m_itemData.m_quality = 1; drop.m_itemData.m_durability = s.m_maxDurability; drop.m_itemData.m_variant = 0;
            Built[g.Prefab] = go;
            if (Icon.MakeItem(go, setIcon: true) is Sprite icon) { Looks.Owned.Add(icon); Looks.Owned.Add(icon.texture); }
            Icon.Later(() => { if (go != null && Icon.MakeItem(go, setIcon: true) is Sprite again) { Looks.Owned.Add(again); Looks.Owned.Add(again.texture); } });
            return go;
        }

        private static void AddRecipes(ZNetScene scene, ObjectDB db)
        {
            db.m_recipes.RemoveAll(r => r == null || r.name.StartsWith("Recipe_Homespun_") && !Recipes.ContainsValue(r));
            CraftingStation bench = scene.GetPrefab("piece_workbench")?.GetComponent<CraftingStation>();
            Add(db, "Recipe_Homespun_Spindle", Names.Spindle, 1, bench, SpindleCost);
            foreach (Make m in Chain.AllMakes())
            {
                CraftingStation at = Kit.Station(m.Station);
                if (at == null) { Plugin.Warn($"No {m.Station} to make {m.Item} at"); continue; }
                Add(db, "Recipe_Homespun_" + (m.Item.StartsWith("Homespun_") ? m.Item.Substring(9) : "Sew_" + m.Item), m.Item, m.Amount, at, m.Needs);
            }
        }
        private static void Add(ObjectDB db, string name, string item, int amount, CraftingStation station, (string item, int n)[] needs)
        {
            if (!Recipes.TryGetValue(name, out Recipe r) || r == null)
            {
                ItemDrop made = (Get(item) ?? db.GetItemPrefab(item))?.GetComponent<ItemDrop>();
                if (made == null) { Plugin.Warn($"{name}: no {item}"); return; }
                var reqs = new List<Piece.Requirement>();
                foreach (var (res, n) in needs)
                {
                    ItemDrop d = (Get(res) ?? db.GetItemPrefab(res))?.GetComponent<ItemDrop>();
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
            Plants.Unload();
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
