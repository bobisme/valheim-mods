using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Homespun
{
    // The stations, made from the game's own: the spinning wheel for the retting pool and the flax brake (its smelter's queue, worked
    // on its owner's game and saved in its ZDO, the game's own E to put in and to take out; going on while nobody's near, up to an hour
    // as the game's smelters do), and the workbench for the loom and the sewing table (crafting stations with recipes of their own).
    // Each has its own look made in Blender (Models/spun.py) and colliders of its own shape.
    internal static class Kit
    {
        internal sealed class Def
        {
            public string Name, From, Title, Model, Description; public (string item, int n)[] Cost;
            public bool Hung; // (a tapestry: furniture, hung as the game's banners are, and comfort)
        }
        internal static readonly Def[] All =
        {
            new Def { Name = Names.Pool, From = Names.Wheel, Title = "Retting pool", Model = "pool", Cost = new[] { ("Wood", 16), ("Stone", 6), ("Resin", 4) },
                Description = "A plank trough of water with stones to hold the flax under. Lay flax or nettle in it and leave it to soak: the stalks rot loose from the fibre inside them. A full pool takes a day." },
            new Def { Name = Names.Brake, From = Names.Wheel, Title = "Flax brake", Model = "brake", Cost = new[] { ("Wood", 12), ("IronNails", 10) },
                Description = "A hinged wooden blade to break retted flax and nettle, and an iron-toothed hackle to comb the fibre out, ready to spin. Its combs card the wool out of a lox pelt too." },
            new Def { Name = Names.Loom, From = "piece_workbench", Title = "Warp-weighted loom", Model = "loom", Cost = new[] { ("Wood", 20), ("Stone", 10), ("LeatherScraps", 2) },
                Description = "A standing loom leaning on the wall, its warp hung from the beam and kept taut by stone weights. Weaves linen thread and wool yarn into bolts of cloth, and patterned bands on cards." },
            new Def { Name = Names.Table, From = "piece_workbench", Title = "Sewing table", Model = "table", Cost = new[] { ("Wood", 12), ("LeatherScraps", 2), ("Bronze", 1) },
                Description = "A plank table with shears, needles and thread. Cuts and sews bolts of cloth into shirts, tunics, dresses, trousers and cloaks, and Hildir's clothes besides." },
            new Def { Name = Names.Vat, From = "piece_chest_wood", Title = "Dye vat", Model = "vat", Cost = new[] { ("Wood", 12), ("Stone", 3), ("Resin", 2) },
                Description = "A stave tub for dyeing. Put in bolts of cloth or clothes with a dyestuff: blueberries dye blue, raspberries red, dandelions yellow, and the lichen off mountain and forest stones purple. A bolt takes a measure of dye, a garment the bolts it was sewn from." },
            new Def { Name = Names.Frieze, From = "piece_banner01", Title = "Procession tapestry", Model = "frieze", Hung = true, Cost = Tapestry.FriezeCost,
                Description = "A long woven band for a hall's wall, as the one buried with the Oseberg ship: horses, a wagon and spearmen in procession, in madder red, woad blue and weld yellow on linen. Woven from dyed bolts." },
            new Def { Name = Names.Hanging, From = "piece_banner01", Title = "Wall hanging", Model = "hanging", Hung = true, Cost = Tapestry.HangingCost,
                Description = "A woven hanging as at Överhogdal: rows of elk and horses, a ship and birds round a tree, red and blue on linen. Woven from dyed bolts." },
        };
        internal static bool Ours(string name) => All.Any(d => d.Name == name);

        private static readonly MethodInfo UpdateKnownRecipes = AccessTools.Method(typeof(Player), "UpdateKnownRecipesList"), UpdateAvailablePieces = AccessTools.Method(typeof(Player), "UpdateAvailablePiecesList");
        internal static readonly Dictionary<string, GameObject> Made = new Dictionary<string, GameObject>();
        private static GameObject _holder; private static ZNetScene _scene; private static float _next, _settleAt = -1; private static bool _broken, _named;
        internal static PieceTable Hammer(ZNetScene scene) => scene?.GetPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
        internal static CraftingStation Station(string name) => Made.TryGetValue(name, out GameObject go) && go != null ? go.GetComponent<CraftingStation>() : null;

        // Once a second (and when the items are registered): made for this world, and in the hammer's menu.
        internal static void Ensure(bool force = false)
        {
            if (_broken || !force && Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1;
            ZNetScene scene = ZNetScene.instance; PieceTable table = Hammer(scene);
            if (scene == null || table == null || Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
            if (scene != _scene) { Forget(); _scene = scene; }
            if (All.Any(d => d.From == Names.Wheel) && Things.Get(Names.RettedFlax) == null) return; // (the queues name our items: they come first)
            table.m_pieces.RemoveAll(p => p == null);
            bool changed = false, every = true;
            foreach (Def d in All)
            {
                if (!Made.TryGetValue(d.Name, out GameObject prefab) || prefab == null)
                {
                    prefab = Make(scene, d);
                    if (prefab == null) { every = false; continue; }
                    Made[d.Name] = prefab;
                }
                if (table.m_pieces.Contains(prefab) && scene.GetPrefab(d.Name) == prefab) continue;
                PieceReload.ReplacePrefab(scene, prefab, table);
                PieceReload.Reattach(scene, prefab);
                changed = true;
            }
            if (changed && Player.m_localPlayer is Player p) { UpdateKnownRecipes?.Invoke(p, null); UpdateAvailablePieces?.Invoke(p, null); }
            if (every && !_named)
            {
                // (what's made against what's been shipped: a shipped name no longer made is lost from saves)
                _named = true;
                var made = new HashSet<string>(All.Select(d => d.Name).Concat(Things.AllItems).Concat(Plants.All));
                foreach (string n in PrefabNames.Shipped.Where(n => !made.Contains(n))) Plugin.Warn($"ERROR: {n} was shipped but isn't made any more: it's gone from every save.");
                foreach (string n in made.Where(n => !PrefabNames.Shipped.Contains(n))) Plugin.Warn($"{n} isn't in Core/PrefabNames.cs yet.");
            }
            if (every) { if (_settleAt < 0) _settleAt = Time.unscaledTime + 3; else if (Time.unscaledTime >= _settleAt) Leftovers.TakeOver(Looks.AllOwned()); }
        }
        internal static void Broken() => _broken = true;
        internal static void Unload()
        {
            PieceTable table = Hammer(ZNetScene.instance);
            foreach (GameObject prefab in Made.Values) PieceReload.UnloadPiece(prefab, table);
            // (the meshes and materials stay: what's standing, and the items, still draw with them till the next copy puts its own in their place)
            Leftovers.HandOver(Looks.AllOwned().Concat(new Object[] { _holder }));
        }
        private static void Forget() { Made.Clear(); if (_holder != null) Object.Destroy(_holder); Looks.ForgetWorld(); _holder = null; _named = false; }

        private static GameObject Make(ZNetScene scene, Def d)
        {
            GameObject from = scene.GetPrefab(d.From);
            if (from == null) { Plugin.Warn($"No {d.From} to make the {d.Title} from."); return null; }
            if (_holder == null) { _holder = new GameObject("Homespun prefabs"); _holder.SetActive(false); Object.DontDestroyOnLoad(_holder); }
            GameObject go = Object.Instantiate(from, _holder.transform, false); go.name = d.Name;
            // Everything of the game's look goes (its LODs, its renderers, its colliders, the wheel's turning parts and whirr) and its
            // snap points; the model and our colliders in their place. The objects the station's code holds (switches, output point, the
            // enabled object, the roof check) stay, moved to where they belong on our model.
            foreach (LODGroup lod in go.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lod);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true).Where(r => r.GetType().Name != "ParticleSystemRenderer").ToList()) { GameObject o = r.gameObject; Object.DestroyImmediate(r); if (o.GetComponent<MeshFilter>() is MeshFilter f) Object.DestroyImmediate(f); }
            // (the solid ones: the triggers stay, the workbench's use area, where standing counts as at the station, and its base's)
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true).Where(c => !c.isTrigger || c.GetComponent<Switch>() != null).ToList()) Object.DestroyImmediate(c);
            foreach (Animator a in go.GetComponentsInChildren<Animator>(true).ToList()) Object.DestroyImmediate(a);
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true).Where(t => t != go.transform && (t.name.StartsWith("_snappoint") || t.name.StartsWith("$hud_snappoint") || t.CompareTag("snappoint"))).ToList()) Object.DestroyImmediate(t.gameObject);
            GameObject look = Looks.Add(go.transform, scene, d.Model);
            if (look == null) { Object.DestroyImmediate(go); return null; }
            WearNTear wear = go.GetComponent<WearNTear>();
            if (wear != null) { wear.m_new = look; wear.m_worn = look; wear.m_broken = look; wear.m_fragmentRoots = new GameObject[0]; wear.m_materialType = WearNTear.MaterialType.Wood; }
            int layer = LayerMask.NameToLayer("piece");
            switch (d.Name)
            {
                case Names.Pool: MakePool(go, layer); break;
                case Names.Brake: MakeBrake(go, layer); break;
                case Names.Loom: MakeLoom(go, layer, d); break;
                case Names.Table: MakeTable(go, layer, d); break;
                case Names.Vat: MakeVat(go, layer, scene, d); break;
                case Names.Frieze: Box(go, layer, Tapestry.FriezeCentre, Tapestry.FriezeSize); break;
                case Names.Hanging: Box(go, layer, Tapestry.HangingCentre, Tapestry.HangingSize); break;
            }
            var piece = go.GetComponent<Piece>();
            piece.m_name = d.Title; piece.m_description = d.Description;
            piece.m_category = d.Hung ? Piece.PieceCategory.Furniture : Piece.PieceCategory.Crafting;
            piece.m_craftingStation = scene.GetPrefab("piece_workbench")?.GetComponent<CraftingStation>();
            piece.m_groundPiece = false; piece.m_groundOnly = false; piece.m_clipEverything = false; piece.m_comfort = d.Hung ? Tapestry.Comfort : 0; piece.m_comfortGroup = d.Hung ? Piece.ComfortGroup.Banner : Piece.ComfortGroup.None;
            piece.m_resources = d.Cost.Select(c => Req(scene, c.item, c.n)).Where(r => r != null).ToArray();
            piece.m_enabled = true;
            try { if (Icon.Make(go) is Sprite icon) { piece.m_icon = icon; Looks.WorldOwned.Add(icon); Looks.WorldOwned.Add(icon.texture); } }
            catch (Exception e) { Plugin.Warn($"No icon for the {d.Title}: {e.Message}"); }
            if (go.GetComponent<CraftingStation>() is CraftingStation station) station.m_icon = piece.m_icon; // (the crafting window's picture of where you are)
            Icon.Later(() =>
            {
                if (go == null || !(Icon.Make(go) is Sprite again)) return;
                piece.m_icon = again; Looks.WorldOwned.Add(again); Looks.WorldOwned.Add(again.texture);
                if (go.GetComponent<CraftingStation>() is CraftingStation st) st.m_icon = again;
            });
            return go;
        }

        // ---- the smelter-like two: a queue, an E to put in, an E to take out ----
        private static Smelter Queue(GameObject go, string title, string add, int holds, float seconds, string station)
        {
            Smelter s = go.GetComponent<Smelter>();
            s.m_name = title; s.m_addOreTooltip = add; s.m_emptyOreTooltip = "Take out";
            s.m_maxOre = holds; s.m_secPerProduct = seconds; s.m_spawnStack = true;
            s.m_fuelItem = null; s.m_maxFuel = 0; s.m_fuelPerProduct = 0; s.m_requiresRoof = false; s.m_windmill = null; s.m_smokeSpawner = null;
            s.m_animators = new Animator[0]; s.m_addOreAnimationDuration = 0;
            s.m_conversion = Chain.At(station).Select(c => new Smelter.ItemConversion { m_from = Things.Get(c.From)?.GetComponent<ItemDrop>() ?? ZNetScene.instance.GetPrefab(c.From)?.GetComponent<ItemDrop>(), m_to = Things.Get(c.To)?.GetComponent<ItemDrop>() })
                .Where(c => c.m_from != null && c.m_to != null).ToList();
            // (the wheel's whirr and its turning parts' sounds: not a pool's or a brake's)
            // (the sound player first: it needs its AudioSource while it's there)
            foreach (Component z in go.GetComponentsInChildren<Component>(true).Where(c => c != null && c.GetType().Name == "ZSFX").ToList()) Object.DestroyImmediate(z);
            foreach (AudioSource a in go.GetComponentsInChildren<AudioSource>(true).ToList()) Object.DestroyImmediate(a);
            if (s.m_enabledObject == null) { var on = new GameObject("_enabled"); on.transform.SetParent(go.transform, false); s.m_enabledObject = on; } // (the game switches it on and off, never checking it's there)
            s.m_addOreSwitch = Switch(go, s.m_addOreSwitch, "add");
            s.m_emptyOreSwitch = Switch(go, s.m_emptyOreSwitch, "empty");
            s.m_addWoodSwitch = null;
            if (s.m_outputPoint == null) { var o = new GameObject("output"); s.m_outputPoint = o.transform; }
            s.m_outputPoint.SetParent(go.transform, false);
            if (s.m_roofCheckPoint == null) s.m_roofCheckPoint = go.transform;
            return s;
        }
        private static Switch Switch(GameObject go, Switch had, string name)
        {
            if (had != null) return had;
            var o = new GameObject(name); o.transform.SetParent(go.transform, false);
            return o.AddComponent<Switch>();
        }
        // An E-able patch on a switch: a box (not solid: looked at, never stood on) where the hand goes.
        private static void Reach(Switch s, int layer, Vector3 at, Vector3 size, string name)
        {
            s.transform.SetParent(s.GetComponentInParent<Piece>(true).transform, false); // (wherever the wheel had it: on our model now; true: the prefab sits under an inactive holder)
            s.gameObject.layer = layer; s.transform.localPosition = at; s.transform.localRotation = Quaternion.identity; s.transform.localScale = Vector3.one;
            var box = s.gameObject.AddComponent<BoxCollider>(); box.size = size; box.isTrigger = true;
            s.m_name = name; s.m_hoverText = name;
        }

        // Numbers from spun.py (game frame, metres, +z the front where you stand).
        private static void MakePool(GameObject go, int layer)
        {
            Smelter s = Queue(go, "Retting pool", "Lay in flax or nettle", Chain.PoolHolds, Chain.PoolSeconds, Names.Pool);
            // (the trough's walls and floor; the drying rack at its right end)
            Box(go, layer, new Vector3(-0.25f, 0.02f, 0), new Vector3(1.9f, 0.04f, 0.9f));
            Box(go, layer, new Vector3(-0.25f, 0.25f, 0.43f), new Vector3(1.9f, 0.5f, 0.06f));
            Box(go, layer, new Vector3(-0.25f, 0.25f, -0.43f), new Vector3(1.9f, 0.5f, 0.06f));
            Box(go, layer, new Vector3(-1.17f, 0.25f, 0), new Vector3(0.06f, 0.5f, 0.9f));
            Box(go, layer, new Vector3(0.67f, 0.25f, 0), new Vector3(0.06f, 0.5f, 0.9f));
            Box(go, layer, new Vector3(1.05f, 0.6f, 0), new Vector3(0.5f, 1.2f, 0.7f));
            Reach(s.m_addOreSwitch, layer, new Vector3(-0.25f, 0.42f, 0), new Vector3(1.75f, 0.12f, 0.8f), "Retting pool");
            Reach(s.m_emptyOreSwitch, layer, new Vector3(1.05f, 0.75f, 0), new Vector3(0.55f, 0.9f, 0.75f), "Retting pool");
            s.m_outputPoint.localPosition = new Vector3(1.05f, 0.3f, 0.55f);
        }
        private static void MakeBrake(GameObject go, int layer)
        {
            Smelter s = Queue(go, "Flax brake", "Break and comb", Chain.BrakeHolds, Chain.BrakeSeconds, Names.Brake);
            // (the brake on its legs, the hackle on its stand, the basket below)
            Box(go, layer, new Vector3(-0.3f, 0.42f, 0), new Vector3(1.4f, 0.84f, 0.3f));
            Box(go, layer, new Vector3(0.75f, 0.45f, 0), new Vector3(0.35f, 0.9f, 0.35f));
            Reach(s.m_addOreSwitch, layer, new Vector3(-0.3f, 0.9f, 0), new Vector3(1.4f, 0.25f, 0.4f), "Flax brake");
            Reach(s.m_emptyOreSwitch, layer, new Vector3(0.75f, 0.95f, 0), new Vector3(0.4f, 0.25f, 0.4f), "Flax brake");
            s.m_outputPoint.localPosition = new Vector3(0.3f, 0.35f, 0.45f);
        }
        private static void MakeLoom(GameObject go, int layer, Def d)
        {
            Craft(go, d.Title);
            // (the uprights leaning back, the beam, the warp and weights: one box leaning with it, stood in front of)
            Box(go, layer, new Vector3(0, 1.0f, -0.2f), new Vector3(1.7f, 2.0f, 0.35f), Quaternion.Euler(-12, 0, 0));
        }
        private static void MakeTable(GameObject go, int layer, Def d)
        {
            Craft(go, d.Title);
            Box(go, layer, new Vector3(0, 0.4f, 0), new Vector3(1.5f, 0.8f, 0.75f));
        }
        // (Models/README: the tub 0.43 m round to the rim at 0.66, the dye at 0.52 inside 0.38; the cloth over the rim at +x)
        private static void MakeVat(GameObject go, int layer, ZNetScene scene, Def d)
        {
            Container box = go.GetComponent<Container>();
            box.m_name = d.Title; box.m_width = Dye.VatWidth; box.m_height = Dye.VatHeight; box.m_privacy = Container.PrivacySetting.Public; box.m_checkGuardStone = false;
            box.m_autoDestroyEmpty = false; box.m_defaultItems = new DropTable();
            Box(go, layer, new Vector3(0, 0.33f, 0), new Vector3(0.84f, 0.66f, 0.84f));
            VatLook.AddDye(go, scene, 0.38f, 0.52f);
            go.AddComponent<DyeVat>();
        }
        private static void Craft(GameObject go, string title)
        {
            CraftingStation c = go.GetComponent<CraftingStation>();
            c.m_name = title; c.m_craftRequireFire = false; c.m_craftRequireRoof = true; c.m_showBasicRecipies = false; c.m_rangeBuild = 0; c.m_extraRangePerLevel = 0;
            c.m_hasCraftTab = true; c.m_canRepair = true; c.m_upgrader = false; c.m_discoverRange = 4f;
        }

        private static Piece.Requirement Req(ZNetScene scene, string item, int n)
        {
            ItemDrop drop = scene.GetPrefab(item)?.GetComponent<ItemDrop>();
            return drop == null ? null : new Piece.Requirement { m_resItem = drop, m_amount = n, m_recover = true };
        }
        private static void Box(GameObject go, int layer, Vector3 at, Vector3 size, Quaternion? turn = null)
        {
            var o = new GameObject("Collider") { layer = layer }; o.transform.SetParent(go.transform, false);
            o.transform.localPosition = at; o.transform.localRotation = turn ?? Quaternion.identity;
            o.AddComponent<BoxCollider>().size = size;
        }
    }
}
