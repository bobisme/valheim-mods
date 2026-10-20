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
    // Without Byre's milk the dairy's stations are still registered (any standing stay in the world) but kept out of the hammer's menu.
    // The preserving stations (Core/Preserves.cs: smokehouse, salt pan, salting barrel, drying rack) are the smelter's and the charcoal
    // kiln's queues made over the same way, and need nothing of another mod's.
    internal static class Kit
    {
        internal sealed class Def { public string Name, From, Title, Model, Description; public (string item, int n)[] Cost; public bool Milk, Oven; public Works Works; }
        internal static readonly Def[] All = new[]
        {
            new Def { Name = Names.Churn, From = Names.Workbench, Title = "Butter churn", Model = "churn", Cost = Dairy.ChurnCost, Milk = true,
                Description = "A tall stave churn with a dasher. Churns milk into butter, and sours and strains it into skyr." },
            new Def { Name = Names.Press, From = Names.Wheel, Title = "Cheese press", Model = "press", Cost = Dairy.PressCost, Milk = true,
                Description = "A cheese hoop under a weighted lever. Pour in milk and leave it: the whey runs off, and the curd is pressed and set into cheese, a milk at a time." },
        }.Concat(Preserves.Stations.Select(w => new Def { Name = w.Name, From = w.From, Title = w.Title, Model = w.Model, Cost = w.Cost, Description = w.Description, Works = w }))
        .Append(new Def { Name = Baked.Oven, From = Baked.GameOven, Title = Baking.OvenTitle, Model = "bakeoven", Cost = Baking.OvenCost, Description = Baking.OvenDescription, Oven = true }).ToArray();
        internal static Works WorksOf(string prefab) => All.FirstOrDefault(d => d.Name == prefab)?.Works;

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
            if (Dairy.Items.Concat(Preserves.Items).Concat(Baking.ItemNames).Append(Spoilage.Spoiled).Any(i => Things.Get(i) == null)) return; // (the stations' queues name our items: they come first)
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
                bool show = milk || !d.Milk;
                if (_inMenu == milk && scene.GetPrefab(d.Name) == prefab && table.m_pieces.Contains(prefab) == show) continue;
                PieceReload.ReplacePrefab(scene, prefab, show ? table : null);
                if (!show) table.m_pieces.Remove(prefab); // (registered, so any standing load; not built without milk to use it)
                PieceReload.Reattach(scene, prefab);
                changed = true;
            }
            _inMenu = milk;
            if (milk)
            {
                if (Made.TryGetValue(Names.Press, out GameObject press) && press != null && press.GetComponent<Smelter>() is Smelter s && s.m_conversion.Count < Names.Milks.Count(m => Things.Milk(m) != null))
                    s.m_conversion = Conversions();
            }
            // (the dough's recipe at the cauldron, milk or none; the churn's once there's milk)
            ObjectDB db = ObjectDB.instance;
            if (db != null && db.GetItemPrefab("Wood") != null && !Things.RecipesIn(db, milk)) { Things.AddRecipes(db); changed = true; }
            if (changed && Player.m_localPlayer is Player p) { UpdateKnownRecipes?.Invoke(p, null); UpdateAvailablePieces?.Invoke(p, null); }
            if (every && !_named)
            {
                // (what's made against what's been shipped: a shipped name no longer made is lost from saves)
                _named = true;
                var made = new HashSet<string>(Dairy.Pieces.Concat(Dairy.Items).Concat(Preserves.Pieces).Concat(Preserves.Items).Concat(Baking.Pieces).Concat(Baking.ItemNames).Append(Spoilage.Spoiled));
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
            if (d.Works != null) { if (!MakeWorks(go, layer, scene, d.Works)) { Object.DestroyImmediate(go); return null; } }
            else if (d.Oven) { if (!MakeOven(go, layer, scene)) { Object.DestroyImmediate(go); return null; } }
            else if (d.Name == Names.Churn) MakeChurn(go, layer, d); else MakePress(go, layer);
            var piece = go.GetComponent<Piece>();
            piece.m_name = d.Title; piece.m_description = d.Description;
            piece.m_category = Piece.PieceCategory.Crafting;
            piece.m_craftingStation = scene.GetPrefab(Names.Workbench)?.GetComponent<CraftingStation>();
            piece.m_groundPiece = false; piece.m_groundOnly = false; piece.m_clipEverything = false; piece.m_comfort = 0; piece.m_comfortGroup = Piece.ComfortGroup.None;
            if (d.Works?.Needs == Needs.Sea) piece.m_noInWater = false; // (the kiln's fire can't be built in water; the pan must be)
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
        // Where things are on each preserving station's model (Models/README.txt, game frame): the solid boxes (centre, size), where the
        // hand goes to put things in (and fuel, for the smelter's two), to take them out, the output on the ground in front, where the
        // fire burns and the smoke rises, and for the rack where its fish hang (and how a stockfish is turned to hang by its tail).
        private sealed class Shape
        {
            public (Vector3 at, Vector3 size)[] Boxes;
            public Vector3 Add, Fuel, Empty, Output, Fire, Smoke;
            public Vector3[] Hang = new Vector3[0]; public Vector3 HangTurn;
            public Vector3[] Slots = new Vector3[0]; // (the oven's: where a loaf lies on its floor)
        }
        private static readonly Dictionary<string, Shape> Shapes = new Dictionary<string, Shape>
        {
            // (the stone firebox and its slab, the plank hut, the roof; wood in at the firebox mouth, meat in and out at the hatch)
            [Preserved.Smokehouse] = new Shape
            {
                Boxes = new[] { (new Vector3(0, 0.25f, 0), new Vector3(1.32f, 0.5f, 1.32f)), (new Vector3(0, 0.97f, 0), new Vector3(1.22f, 0.94f, 1.22f)),
                    (new Vector3(0, 1.615f, 0), new Vector3(1.56f, 0.57f, 1.56f)) },
                Add = new Vector3(-0.1f, 1.09f, 0.66f), Fuel = new Vector3(0, 0.15f, 0.68f), Empty = new Vector3(0.17f, 1.09f, 0.66f), // (the hatch's left; its latch side)
                Output = new Vector3(0.4f, 0, 1.0f), Fire = new Vector3(0, 0.12f, 0.3f), Smoke = new Vector3(0, 1.86f, 0),
            },
            // (the hearth and pan; wood in at the gap in the stones, the salt scraped out of the pan)
            [Preserved.SaltPan] = new Shape
            {
                Boxes = new[] { (new Vector3(0, 0.255f, -0.02f), new Vector3(1.04f, 0.51f, 0.76f)) },
                Add = new Vector3(0, 0.15f, 0.42f), Empty = new Vector3(0, 0.5f, 0), Output = new Vector3(0.35f, 0, 0.85f),
                Fire = new Vector3(0, 0.12f, 0.1f), Smoke = new Vector3(0, 0.46f, 0),
            },
            // (the barrel and its leaning lid; meat in and out at the open top, salt by the scoop)
            [Preserved.SaltBarrel] = new Shape
            {
                Boxes = new[] { (new Vector3(0, 0.4f, 0), new Vector3(0.56f, 0.8f, 0.56f)), (new Vector3(0.225f, 0.23f, 0.315f), new Vector3(0.41f, 0.46f, 0.33f)) },
                Add = new Vector3(0.12f, 0.82f, 0.1f), Fuel = new Vector3(-0.1f, 0.88f, -0.1f), Empty = new Vector3(0, 0.45f, 0.3f), // (the open top; the scoop; the barrel's front)
                Output = new Vector3(-0.3f, 0, 0.55f),
            },
            // (the two trestles and the bars; fish hung and taken down at the lower bar's middle; 8 stockfish hung by the tail)
            // (the stone plinth with the fire chamber in it, the clay dome low and high, the ledge before the mouth, the firewood at its left;
            // dough in and bread out at the mouth, wood in at the fire hole under the ledge, the fire on the ash under the floor, the smoke
            // from the pot-chimney at the back; four loaves on the floor inside, the one by the mouth the most seen)
            [Baked.Oven] = new Shape
            {
                Boxes = new[] { (new Vector3(0, 0.23f, 0), new Vector3(1.48f, 0.46f, 1.48f)), (new Vector3(0, 0.63f, 0), new Vector3(1.24f, 0.34f, 1.24f)),
                    (new Vector3(0, 0.93f, 0), new Vector3(0.84f, 0.26f, 0.84f)), (new Vector3(0, 0.435f, 0.765f), new Vector3(0.66f, 0.07f, 0.31f)),
                    (new Vector3(-0.925f, 0.095f, 0.035f), new Vector3(0.25f, 0.19f, 0.53f)) },
                Add = new Vector3(0, 0.6f, 0.62f), Fuel = new Vector3(0, 0.15f, 0.75f),
                Output = new Vector3(-0.45f, 0, 1.15f), Fire = new Vector3(0, 0.08f, 0.15f), Smoke = new Vector3(0, 1.08f, -0.35f),
                Slots = new[] { new Vector3(0, 0.478f, 0.22f), new Vector3(-0.22f, 0.478f, -0.02f), new Vector3(0.22f, 0.478f, -0.02f), new Vector3(0, 0.478f, -0.26f) },
            },
            [Preserved.DryingRack] = new Shape
            {
                Boxes = new[] { (new Vector3(-1.1f, 0.97f, 0), new Vector3(0.22f, 1.94f, 1.14f)), (new Vector3(1.1f, 0.97f, 0), new Vector3(0.22f, 1.94f, 1.14f)),
                    (new Vector3(0, 1.305f, 0), new Vector3(2.64f, 0.09f, 0.09f)), (new Vector3(0, 1.745f, 0), new Vector3(2.64f, 0.09f, 0.09f)) },
                Add = new Vector3(0, 1.3f, 0.06f), Empty = new Vector3(0, 1.75f, 0.06f), // (the lower bar's middle; the upper's)
                Output = new Vector3(0, 0, 0.9f),
                Hang = new[] { new Vector3(-0.81f, 1.171f, 0.054f), new Vector3(-0.27f, 1.173f, 0.056f), new Vector3(0.27f, 1.177f, 0.054f), new Vector3(0.81f, 1.179f, 0.052f),
                    new Vector3(-0.81f, 1.62f, 0.052f), new Vector3(-0.27f, 1.619f, 0.052f), new Vector3(0.27f, 1.615f, 0.05f), new Vector3(0.81f, 1.613f, 0.05f) },
                HangTurn = new Vector3(0, 90, 90),
            },
        };

        // A preserving station: the smelter's or the kiln's queue with our fuel, conversions, times and switches, on our model.
        private static bool MakeWorks(GameObject go, int layer, ZNetScene scene, Works w)
        {
            Smelter s = go.GetComponent<Smelter>();
            if (s == null || !Shapes.TryGetValue(w.Name, out Shape at)) { Plugin.Warn($"The {w.Title}: no smelter in {w.From}"); return false; }
            s.m_name = w.Title; s.m_addOreTooltip = w.AddText; s.m_emptyOreTooltip = w.EmptyText;
            s.m_maxOre = w.Holds; s.m_secPerProduct = w.Seconds; s.m_spawnStack = true;
            s.m_fuelItem = w.Fuel != null ? (Things.Get(w.Fuel) ?? scene.GetPrefab(w.Fuel))?.GetComponent<ItemDrop>() : null;
            if (w.Fuel != null && s.m_fuelItem == null) { Plugin.Warn($"The {w.Title}: no {w.Fuel} to burn"); return false; }
            s.m_maxFuel = w.Fuel != null ? w.MaxFuel : 0; s.m_fuelPerProduct = w.Fuel != null ? w.FuelPer : 0;
            s.m_requiresRoof = w.Needs != Needs.Nothing; // (what "roof" means for ours: Sky.UpdateRoof says)
            s.m_windmill = null; s.m_animators = new Animator[0]; s.m_addOreAnimationDuration = 0;
            s.m_conversion = w.Makes.Select(c => new Smelter.ItemConversion { m_from = (Things.Get(c.from) ?? scene.GetPrefab(c.from))?.GetComponent<ItemDrop>(), m_to = Things.Get(c.to)?.GetComponent<ItemDrop>() })
                .Where(c => c.m_from != null && c.m_to != null).ToList();
            if (s.m_conversion.Count == 0) { Plugin.Warn($"The {w.Title}: nothing it can make"); return false; }
            // The fire (its flames, light, warmth and crackle) moved to our firebox, or gone where there's no fire; the smoke to our chimney.
            if (s.m_enabledObject == null) { var on = new GameObject("_enabled"); on.transform.SetParent(go.transform, false); s.m_enabledObject = on; } // (the game switches it on and off, never checking it's there)
            Transform fire = s.m_enabledObject.transform;
            SmokeSpawner smoke = go.GetComponentInChildren<SmokeSpawner>(true);
            if (w.Smoke && smoke != null) { smoke.transform.SetParent(go.transform, false); smoke.transform.localPosition = at.Smoke; smoke.transform.localRotation = Quaternion.identity; s.m_smokeSpawner = smoke; }
            else { if (smoke != null) Object.DestroyImmediate(smoke.gameObject); s.m_smokeSpawner = null; }
            if (w.Fuel == "Wood" || w.Makes.Any(m => m.from == "Wood"))
            {
                // (the kiln's flames, light, warmth and crackle, all at our firebox; its sparks and its own smoke go, the spawner makes ours)
                fire.SetParent(go.transform, false); fire.localPosition = at.Fire; fire.localRotation = Quaternion.identity; fire.localScale = Vector3.one;
                foreach (Transform t in fire.Cast<Transform>().ToList())
                {
                    if (t.name.StartsWith("flare") || t.name.StartsWith("smok")) { Object.DestroyImmediate(t.gameObject); continue; }
                    t.localPosition = Vector3.zero;
                    // (the kiln's flames come from a wide emitter set off to one side of its own firebox: ours, a small bed of embers)
                    if (t.GetComponent<ParticleSystem>() is ParticleSystem ps)
                    {
                        var shape = ps.shape; shape.position = Vector3.zero; shape.rotation = Vector3.zero;
                        shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(0.5f, 0.35f, 0.05f);
                    }
                }
            }
            else
            {
                foreach (Component z in fire.GetComponentsInChildren<Component>(true).Where(c => c != null && c.GetType().Name == "ZSFX").ToList()) Object.DestroyImmediate(z);
                for (int i = fire.childCount - 1; i >= 0; i--) Object.DestroyImmediate(fire.GetChild(i).gameObject);
            }
            s.m_addOreSwitch = Switch(go, s.m_addOreSwitch, "add");
            s.m_emptyOreSwitch = Switch(go, s.m_emptyOreSwitch, "empty");
            s.m_addWoodSwitch = w.Fuel != null ? Switch(go, s.m_addWoodSwitch, "add_fuel") : null;
            foreach (Switch old in go.GetComponentsInChildren<Switch>(true).Where(x => x != s.m_addOreSwitch && x != s.m_emptyOreSwitch && x != s.m_addWoodSwitch).ToList()) Object.DestroyImmediate(old.gameObject);
            if (s.m_outputPoint == null) { var o = new GameObject("output"); s.m_outputPoint = o.transform; }
            s.m_outputPoint.SetParent(go.transform, false); s.m_outputPoint.localPosition = at.Output;
            if (s.m_roofCheckPoint == null) { var r = new GameObject("roofcheck"); r.transform.SetParent(go.transform, false); s.m_roofCheckPoint = r.transform; }
            else s.m_roofCheckPoint.SetParent(go.transform, false);
            // (over its own top: the rack's bars would be a roof to a check from under them)
            if (s.m_roofCheckPoint != go.transform) s.m_roofCheckPoint.localPosition = new Vector3(0, at.Boxes.Max(b => b.at.y + b.size.y / 2) + 0.15f, 0);
            foreach (var (c, size) in at.Boxes) Box(go, layer, c, size);
            Vector3 hand = new Vector3(0.24f, 0.24f, 0.24f);
            Reach(s.m_addOreSwitch, layer, at.Add, hand, w.Title);
            Reach(s.m_emptyOreSwitch, layer, at.Empty, hand, w.Title);
            if (s.m_addWoodSwitch != null) Reach(s.m_addWoodSwitch, layer, at.Fuel, hand, w.Title);
            // (the rack's fish: stockfish hung on its bars, as many showing as it holds; Rack shows and hides them)
            for (int i = 0; i < at.Hang.Length; i++)
            {
                GameObject fish = Looks.Add(go.transform, scene, "stockfish", Rack.Hung + i);
                if (fish == null) break;
                fish.transform.localPosition = at.Hang[i]; fish.transform.localRotation = Quaternion.Euler(at.HangTurn); fish.SetActive(false);
            }
            if (at.Hang.Length > 0) go.AddComponent<Rack>();
            return true;
        }

        // The bake oven: the game's oven (a cooking station: four slots, wood for fuel, a bread taken out when done or left to burn to coal)
        // on our model. Its front was +x, ours is +z: everything it uses is moved onto ours, and its hatch doors and their animation go.
        private static bool MakeOven(GameObject go, int layer, ZNetScene scene)
        {
            CookingStation c = go.GetComponent<CookingStation>();
            if (c == null || !Shapes.TryGetValue(Baked.Oven, out Shape at)) { Plugin.Warn($"The {Baking.OvenTitle}: no cooking station in {Baked.GameOven}"); return false; }
            c.m_name = Baking.OvenTitle; c.m_addItemTooltip = "Put in dough";
            c.m_conversion = Baking.Bakes().Select(b => new CookingStation.ItemConversion { m_from = Things.Get(b.from)?.GetComponent<ItemDrop>(), m_to = Things.Get(b.to)?.GetComponent<ItemDrop>(), m_cookTime = Baking.BakeSeconds })
                .Where(b => b.m_from != null && b.m_to != null).ToList();
            if (c.m_conversion.Count == 0) { Plugin.Warn($"The {Baking.OvenTitle}: nothing it can bake"); return false; }
            c.m_incompatibleItems = new List<CookingStation.ItemMessage>(); // (the game's oven turns away what's for the cooking station: none of ours)
            c.m_useFuel = true; c.m_requireFire = false; c.m_fuelItem = scene.GetPrefab(Baking.Fuel)?.GetComponent<ItemDrop>(); c.m_maxFuel = Baking.MaxFuel;
            if (c.m_fuelItem == null) { Plugin.Warn($"The {Baking.OvenTitle}: no {Baking.Fuel} to burn"); return false; }
            foreach (Transform t in go.transform.Cast<Transform>().Where(t => t.name == "HatchProxy").ToList()) Object.DestroyImmediate(t.gameObject);
            // (its fire, light, warmth and crackle in our firebox, small; its sparks go; the smoke from our vent)
            if (c.m_haveFireObject != null)
            {
                Transform fire = c.m_haveFireObject.transform;
                SmokeSpawner smoke = fire.GetComponentInChildren<SmokeSpawner>(true);
                fire.localPosition = at.Fire; fire.localRotation = Quaternion.Euler(0, -90, 0); // (the game's faced +x: its flames blew out that way)
                if (smoke != null) { Place(smoke.transform, go, at.Smoke); smoke.transform.SetParent(fire, true); } // (under the fire still: it smokes while it burns)
                foreach (Transform t in fire.GetComponentsInChildren<Transform>(true).Where(t => t != fire && (smoke == null || t != smoke.transform)).ToList())
                {
                    if (t == null) continue;
                    if (t.name.StartsWith("flare") || t.name.StartsWith("sparc")) { Object.DestroyImmediate(t.gameObject); continue; }
                    t.localPosition = Vector3.zero;
                    if (t.GetComponent<ParticleSystem>() is ParticleSystem ps)
                    {
                        var shape = ps.shape; shape.position = Vector3.zero; shape.rotation = Vector3.zero;
                        shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(0.45f, 0.05f, 0.3f);
                    }
                }
            }
            // (the loaves on its floor, each with its done and burnt puffs; out in front of the ledge when taken)
            int n = Mathf.Min(c.m_slots.Length, at.Slots.Length);
            if (n == 0) { Plugin.Warn($"The {Baking.OvenTitle}: no slots"); return false; }
            for (int i = 0; i < c.m_slots.Length; i++) if (i >= n) Object.DestroyImmediate(c.m_slots[i].gameObject);
            c.m_slots = c.m_slots.Take(n).ToArray();
            for (int i = 0; i < n; i++)
            {
                Place(c.m_slots[i], go, at.Slots[i]);
                if (i < c.m_donePS.Length && c.m_donePS[i] != null) Place(c.m_donePS[i].transform, go, at.Slots[i]);
                if (i < c.m_burntPS.Length && c.m_burntPS[i] != null) Place(c.m_burntPS[i].transform, go, at.Slots[i]);
            }
            foreach (ParticleSystem extra in c.m_donePS.Skip(n).Concat(c.m_burntPS.Skip(n)).Where(p => p != null).ToList()) Object.DestroyImmediate(extra.gameObject);
            c.m_donePS = c.m_donePS.Take(n).ToArray(); c.m_burntPS = c.m_burntPS.Take(n).ToArray();
            if (c.m_spawnPoint == null) { var o = new GameObject("SpawnPoint"); c.m_spawnPoint = o.transform; }
            Place(c.m_spawnPoint, go, at.Output);
            c.m_addFoodSwitch = Switch(go, c.m_addFoodSwitch, "add_food");
            c.m_addFuelSwitch = Switch(go, c.m_addFuelSwitch, "add_fuel");
            foreach (Switch old in go.GetComponentsInChildren<Switch>(true).Where(x => x != c.m_addFoodSwitch && x != c.m_addFuelSwitch).ToList()) Object.DestroyImmediate(old.gameObject);
            foreach (MeshFilter f in c.m_addFuelSwitch.GetComponents<MeshFilter>()) Object.DestroyImmediate(f); // (its wood ball's mesh: nothing draws it now)
            foreach (var (centre, size) in at.Boxes) Box(go, layer, centre, size);
            Vector3 hand = new Vector3(0.3f, 0.26f, 0.24f);
            Reach(c.m_addFoodSwitch, layer, at.Add, hand, Baking.OvenTitle);
            Reach(c.m_addFuelSwitch, layer, at.Fuel, hand, Baking.OvenTitle);
            return true;
        }
        private static void Place(Transform t, GameObject go, Vector3 at) { t.SetParent(go.transform, false); t.localPosition = at; t.localRotation = Quaternion.identity; }

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
            foreach (Collider c in s.GetComponents<Collider>()) Object.DestroyImmediate(c); // (the game's, sized and set off for its own model)
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
