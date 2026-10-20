using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Larder
{
    // The stations, made from the game's own: the workbench for the butter churn (a crafting station with recipes of its own), and the
    // spinning wheel for the cheese press (its smelter's queue, worked on its owner's game and saved in its ZDO, the game's own E to put
    // milk in and to take cheese out; going on while nobody's near, up to an hour as the game's smelters do). Each has its own look made
    // in Blender (Models/larder.py) and colliders of its own shape.
    // Without Byre's milk the stations are still registered (any standing stay in the world) but kept out of the hammer's menu.
    internal static class Kit
    {
        internal sealed class Def { public string Name, From, Title, Model, Description; public (string item, int n)[] Cost; }
        internal static readonly Def[] All =
        {
            new Def { Name = Names.Churn, From = Names.Workbench, Title = "Butter churn", Model = "churn", Cost = Dairy.ChurnCost,
                Description = "A tall stave churn with a dasher. Churns milk into butter, and sours and strains it into skyr." },
            new Def { Name = Names.Press, From = Names.Wheel, Title = "Cheese press", Model = "press", Cost = Dairy.PressCost,
                Description = "A cheese hoop under a weighted lever. Pour in milk and leave it: the whey runs off, and the curd is pressed and set into cheese, a milk at a time." },
        };

        private static readonly MethodInfo UpdateKnownRecipes = AccessTools.Method(typeof(Player), "UpdateKnownRecipesList"), UpdateAvailablePieces = AccessTools.Method(typeof(Player), "UpdateAvailablePiecesList");
        internal static readonly Dictionary<string, GameObject> Made = new Dictionary<string, GameObject>();
        private static GameObject _holder; private static ZNetScene _scene; private static float _next, _settleAt = -1; private static bool _broken, _named;
        private static bool? _inMenu;
        internal static PieceTable Hammer(ZNetScene scene) => scene?.GetPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
        internal static CraftingStation Station(string name) => Made.TryGetValue(name, out GameObject go) && go != null ? go.GetComponent<CraftingStation>() : null;

        // Once a second (and when the items are registered): made for this world, in the hammer's menu if there's milk, and the churn's
        // recipes and the press's milks in as soon as Byre's milk is there.
        internal static void Ensure(bool force = false)
        {
            if (_broken || !force && Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1;
            ZNetScene scene = ZNetScene.instance; PieceTable table = Hammer(scene);
            if (scene == null || table == null || Plugin.Instance == null || !Plugin.Instance.Enabled.Value) return;
            if (scene != _scene) { Forget(); _scene = scene; }
            if (Things.Get(Names.Cheese) == null) return; // (the press's queue names our items: they come first)
            bool milk = Things.HaveMilk;
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
                if (_inMenu == milk && scene.GetPrefab(d.Name) == prefab && table.m_pieces.Contains(prefab) == milk) continue;
                PieceReload.ReplacePrefab(scene, prefab, milk ? table : null);
                if (!milk) table.m_pieces.Remove(prefab); // (registered, so any standing load; not built without milk to use it)
                PieceReload.Reattach(scene, prefab);
                changed = true;
            }
            _inMenu = milk;
            if (milk)
            {
                if (Made.TryGetValue(Names.Press, out GameObject press) && press != null && press.GetComponent<Smelter>() is Smelter s && s.m_conversion.Count < Names.Milks.Count(m => Things.Milk(m) != null))
                    s.m_conversion = Conversions();
                ObjectDB db = ObjectDB.instance;
                if (db != null && db.GetItemPrefab("Wood") != null && !Things.RecipesIn(db)) { Things.AddRecipes(db); changed = true; }
            }
            if (changed && Player.m_localPlayer is Player p) { UpdateKnownRecipes?.Invoke(p, null); UpdateAvailablePieces?.Invoke(p, null); }
            if (every && !_named)
            {
                // (what's made against what's been shipped: a shipped name no longer made is lost from saves)
                _named = true;
                var made = new HashSet<string>(Dairy.Pieces.Concat(Dairy.Items));
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
        private static void Forget() { Made.Clear(); if (_holder != null) Object.Destroy(_holder); Looks.ForgetWorld(); _holder = null; _named = false; _inMenu = null; }

        private static GameObject Make(ZNetScene scene, Def d)
        {
            GameObject from = scene.GetPrefab(d.From);
            if (from == null) { Plugin.Warn($"No {d.From} to make the {d.Title} from."); return null; }
            if (_holder == null) { _holder = new GameObject("Larder prefabs"); _holder.SetActive(false); Object.DontDestroyOnLoad(_holder); }
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
            if (d.Name == Names.Churn) MakeChurn(go, layer, d); else MakePress(go, layer);
            var piece = go.GetComponent<Piece>();
            piece.m_name = d.Title; piece.m_description = d.Description;
            piece.m_category = Piece.PieceCategory.Crafting;
            piece.m_craftingStation = scene.GetPrefab(Names.Workbench)?.GetComponent<CraftingStation>();
            piece.m_groundPiece = false; piece.m_groundOnly = false; piece.m_clipEverything = false; piece.m_comfort = 0; piece.m_comfortGroup = Piece.ComfortGroup.None;
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

        // (numbers from Models/README.txt: game frame, metres, +z the front where you stand)
        private static void MakeChurn(GameObject go, int layer, Def d)
        {
            CraftingStation c = go.GetComponent<CraftingStation>();
            c.m_name = d.Title; c.m_craftRequireFire = false; c.m_craftRequireRoof = false; c.m_showBasicRecipies = false; c.m_rangeBuild = 0; c.m_extraRangePerLevel = 0;
            c.m_hasCraftTab = true; c.m_canRepair = false; c.m_upgrader = false; c.m_discoverRange = 4f;
            // (the churn's staves, hoops and lid, and the stool with the butter dish at its right; the dasher's shaft is too thin to stop anyone)
            Box(go, layer, new Vector3(0, 0.465f, 0), new Vector3(0.38f, 0.93f, 0.38f));
            Box(go, layer, new Vector3(0.55f, 0.255f, -0.02f), new Vector3(0.38f, 0.51f, 0.38f));
        }

        private static void MakePress(GameObject go, int layer)
        {
            Smelter s = go.GetComponent<Smelter>();
            s.m_name = "Cheese press"; s.m_addOreTooltip = "Pour in milk"; s.m_emptyOreTooltip = "Take out the cheese";
            s.m_maxOre = Dairy.PressHolds; s.m_secPerProduct = Dairy.PressSeconds; s.m_spawnStack = true;
            s.m_fuelItem = null; s.m_maxFuel = 0; s.m_fuelPerProduct = 0; s.m_requiresRoof = false; s.m_windmill = null; s.m_smokeSpawner = null;
            s.m_animators = new Animator[0]; s.m_addOreAnimationDuration = 0;
            s.m_conversion = Conversions();
            // (the wheel's whirr and its turning parts' sounds: not a press's; the sound player first, it needs its AudioSource while it's there)
            foreach (Component z in go.GetComponentsInChildren<Component>(true).Where(c => c != null && c.GetType().Name == "ZSFX").ToList()) Object.DestroyImmediate(z);
            foreach (AudioSource a in go.GetComponentsInChildren<AudioSource>(true).ToList()) Object.DestroyImmediate(a);
            if (s.m_enabledObject == null) { var on = new GameObject("_enabled"); on.transform.SetParent(go.transform, false); s.m_enabledObject = on; } // (the game switches it on and off, never checking it's there)
            s.m_addOreSwitch = Switch(go, s.m_addOreSwitch, "add");
            s.m_emptyOreSwitch = Switch(go, s.m_emptyOreSwitch, "empty");
            s.m_addWoodSwitch = null;
            if (s.m_outputPoint == null) { var o = new GameObject("output"); s.m_outputPoint = o.transform; }
            s.m_outputPoint.SetParent(go.transform, false);
            if (s.m_roofCheckPoint == null) s.m_roofCheckPoint = go.transform;
            // (the bench on its legs, the post with the beam's pivot, the hoop under its follower, the beam, the hanging stone, the whey bucket)
            Box(go, layer, new Vector3(0.07f, 0.225f, 0), new Vector3(1.26f, 0.45f, 0.5f));
            Box(go, layer, new Vector3(-0.625f, 0.475f, 0), new Vector3(0.25f, 0.95f, 0.24f));
            Box(go, layer, new Vector3(-0.25f, 0.625f, 0), new Vector3(0.32f, 0.35f, 0.32f));
            Box(go, layer, new Vector3(-0.02f, 0.835f, 0), new Vector3(1.36f, 0.13f, 0.06f));
            Box(go, layer, new Vector3(0.555f, 0.675f, 0), new Vector3(0.25f, 0.37f, 0.23f));
            Box(go, layer, new Vector3(-0.25f, 0.1f, 0.3f), new Vector3(0.2f, 0.2f, 0.2f));
            // (milk poured in at the hoop: a box round it, looked at before the hoop's own; the cheese taken from the bench's free end)
            Reach(s.m_addOreSwitch, layer, new Vector3(-0.25f, 0.64f, 0), new Vector3(0.42f, 0.42f, 0.42f), "Cheese press");
            Reach(s.m_emptyOreSwitch, layer, new Vector3(0.2f, 0.5f, 0), new Vector3(0.4f, 0.12f, 0.46f), "Cheese press");
            s.m_outputPoint.localPosition = new Vector3(0.15f, 0.3f, 0.55f);
        }
        private static List<Smelter.ItemConversion> Conversions() => Dairy.Pressed()
            .Select(c => new Smelter.ItemConversion { m_from = Things.Milk(c.from)?.GetComponent<ItemDrop>(), m_to = Things.Get(c.to)?.GetComponent<ItemDrop>() })
            .Where(c => c.m_from != null && c.m_to != null).ToList();
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

        private static Piece.Requirement Req(ZNetScene scene, string item, int n)
        {
            ItemDrop drop = scene.GetPrefab(item)?.GetComponent<ItemDrop>();
            return drop == null ? null : new Piece.Requirement { m_resItem = drop, m_amount = n, m_recover = true };
        }
        private static void Box(GameObject go, int layer, Vector3 at, Vector3 size)
        {
            var o = new GameObject("Collider") { layer = layer }; o.transform.SetParent(go.transform, false);
            o.transform.localPosition = at;
            o.AddComponent<BoxCollider>().size = size;
        }
    }
}
