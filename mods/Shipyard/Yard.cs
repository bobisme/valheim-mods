using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Shipyard
{
    // The prefabs: the slipway (a piece in the hammer's Misc tab, with the ships) made from a plain floor piece with our stocks in its
    // look, and each hull's boat (the færing: the game's karve with its look, sail and cargo taken out and ours in). Made once a world
    // is up, and (back) in the scene and the hammer's menu.
    internal static class Yard
    {
        internal static readonly (string item, int n)[] SlipwayCost = { ("Wood", 20), ("Stone", 6) };
        private static readonly Dictionary<string, GameObject> Made = new Dictionary<string, GameObject>();
        private static readonly Dictionary<string, float> RetryAt = new Dictionary<string, float>();
        private static GameObject _holder; private static ZNetScene _scene; private static float _next, _settleAt = -1; private static bool _broken;
        private static readonly MethodInfo UpdateAvailablePieces = AccessTools.Method(typeof(Player), "UpdateAvailablePiecesList");
        internal static PieceTable Hammer(ZNetScene scene) => scene?.GetPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
        internal static GameObject Get(string name) => Made.TryGetValue(name, out GameObject go) ? go : null;
        private static Transform Holder
        {
            get
            {
                if (_holder == null) { _holder = new GameObject("Shipyard prefabs"); _holder.SetActive(false); Object.DontDestroyOnLoad(_holder); }
                return _holder.transform;
            }
        }

        internal static void Ensure(bool force = false)
        {
            if (_broken || !force && Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1;
            ZNetScene scene = ZNetScene.instance; PieceTable table = Hammer(scene);
            if (scene == null || table == null || Plugin.Instance == null) return;
            if (scene != _scene) { Made.Clear(); if (_holder != null) Object.Destroy(_holder); _holder = null; _scene = scene; }
            bool every = true, changed = false;
            foreach (string name in new[] { Names.Faering, Names.Knarr, Names.Slipway, Names.Naust })
            {
                GameObject prefab = Get(name);
                if (prefab == null)
                {
                    if (RetryAt.TryGetValue(name, out float retry) && Time.unscaledTime < retry) { every = false; continue; } // (a look missing: not every second)
                    try { prefab = name == Names.Slipway ? MakeSlipway(scene) : name == Names.Naust ? MakeNaust(scene) : name == Names.Knarr ? MakeKnarr(scene) : MakeFaering(scene); }
                    catch (Exception e) { Plugin.Warn($"Could not make {name}: {e}"); _broken = true; return; }
                    if (prefab == null) { every = false; RetryAt[name] = Time.unscaledTime + 30; continue; }
                    Made[name] = prefab;
                }
                PieceTable menu = (name == Names.Slipway || name == Names.Naust) && Plugin.Instance.Enabled.Value ? table : null;
                if (scene.GetPrefab(name) == prefab && (menu == null || menu.m_pieces.Contains(prefab))) continue;
                PieceReload.ReplacePrefab(scene, prefab, menu);
                PieceReload.Reattach(scene, prefab);
                changed = true;
            }
            if (changed && Player.m_localPlayer is Player p) UpdateAvailablePieces?.Invoke(p, null);
            if (every) { if (_settleAt < 0) _settleAt = Time.unscaledTime + 3; else if (Time.unscaledTime >= _settleAt) Leftovers.TakeOver(Looks.Owned); }
        }
        internal static void Unload()
        {
            PieceTable table = Hammer(ZNetScene.instance);
            foreach (GameObject prefab in Made.Values) PieceReload.UnloadPiece(prefab, table);
            Leftovers.HandOver(Looks.Owned.Concat(new Object[] { _holder }));
        }

        private static void Strip(GameObject go)
        {
            foreach (LODGroup lod in go.GetComponentsInChildren<LODGroup>(true)) Object.DestroyImmediate(lod);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true).Where(r => r.GetType().Name != "ParticleSystemRenderer").ToList()) { GameObject o = r.gameObject; Object.DestroyImmediate(r); if (o.GetComponent<MeshFilter>() is MeshFilter f) Object.DestroyImmediate(f); }
            foreach (MeshFilter f in go.GetComponents<MeshFilter>()) Object.DestroyImmediate(f);
        }
        private static void Box(Transform parent, int layer, Vector3 at, Vector3 size, bool trigger = false, string name = "Collider")
        {
            var o = new GameObject(name) { layer = layer }; o.transform.SetParent(parent, false); o.transform.localPosition = at;
            var b = o.AddComponent<BoxCollider>(); b.size = size; b.isTrigger = trigger;
        }
        private static Piece.Requirement Req(ZNetScene scene, string item, int n)
        {
            ItemDrop drop = scene.GetPrefab(item)?.GetComponent<ItemDrop>();
            return drop == null ? null : new Piece.Requirement { m_resItem = drop, m_amount = n, m_recover = true };
        }

        // ---- the slipway ----
        // (Models/README: runners along z about 8 m long and 2 m apart, keel blocks down the middle)
        private static GameObject MakeSlipway(ZNetScene scene)
        {
            GameObject from = scene.GetPrefab("wood_floor");
            if (from == null) { Plugin.Warn("No wood_floor to make the slipway from"); return null; }
            GameObject go = Object.Instantiate(from, Holder, false); go.name = Names.Slipway;
            Strip(go);
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true).ToList()) Object.DestroyImmediate(c);
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true).Where(t => t != go.transform).ToList()) if (t != null) Object.DestroyImmediate(t.gameObject);
            GameObject look = Looks.Add(go.transform, scene, "slipway");
            if (look == null) { Object.DestroyImmediate(go); return null; }
            int layer = LayerMask.NameToLayer("piece");
            Box(go.transform, layer, new Vector3(-1f, 0.15f, 0), new Vector3(0.4f, 0.3f, 8f));
            Box(go.transform, layer, new Vector3(1f, 0.15f, 0), new Vector3(0.4f, 0.3f, 8f));
            Box(go.transform, layer, new Vector3(0, Slipway.BlockTop / 2, 0), new Vector3(0.4f, Slipway.BlockTop, 5f));
            Box(go.transform, layer, new Vector3(0, 1.0f, 0), new Vector3(2.2f, 1.6f, 6.4f), trigger: true, name: "use"); // (where E finds it, over the boat on the stocks)
            WearNTear wear = go.GetComponent<WearNTear>();
            wear.m_new = look; wear.m_worn = look; wear.m_broken = look; wear.m_fragmentRoots = new GameObject[0]; wear.m_materialType = WearNTear.MaterialType.Wood;
            wear.m_noSupportWear = false; wear.m_supports = false; wear.m_health = 1000;
            var piece = go.GetComponent<Piece>();
            piece.m_name = "Slipway"; piece.m_description = "Building stocks at the shore. Lay down a keel and build a boat on them in stages, each paid in full (anyone can put in); when she's finished, launch her into the water ahead of the bow end. Take the slipway down and what's been put in comes back.";
            piece.m_category = Piece.PieceCategory.Misc;
            piece.m_craftingStation = scene.GetPrefab("piece_workbench")?.GetComponent<CraftingStation>();
            piece.m_groundPiece = false; piece.m_groundOnly = true; piece.m_clipEverything = false; piece.m_comfort = 0; piece.m_waterPiece = false; piece.m_noInWater = false;
            piece.m_resources = SlipwayCost.Select(c => Req(scene, c.item, c.n)).Where(r => r != null).ToArray();
            piece.m_enabled = true;
            go.AddComponent<Slipway>();
            try { if (Icon.Make(go) is Sprite icon) { piece.m_icon = icon; Looks.Owned.Add(icon); Looks.Owned.Add(icon.texture); } } catch (Exception e) { Plugin.Warn("No slipway icon: " + e.Message); }
            Icon.Later(() => { if (go != null && Icon.Make(go) is Sprite again) { piece.m_icon = again; Looks.Owned.Add(again); Looks.Owned.Add(again.texture); } });
            return go;
        }

        // ---- the naust ----
        internal static readonly (string item, int n)[] NaustCost = { ("RoundLog", 24), ("Wood", 40), ("Resin", 8) };
        // (Models/NAUST.txt: the roof halves solid and walkable, the ridge slot left open for masts, the side walls and the land-end wall)
        internal static readonly (Vector3 at, Vector3 size, Vector3 turn)[] NaustBoxes =
        {
            (new Vector3(2.38f, 4.65f, 0), new Vector3(3.97f, 0.2f, 22f), new Vector3(0, 0, -36.3f)),
            (new Vector3(-2.38f, 4.65f, 0), new Vector3(3.97f, 0.2f, 22f), new Vector3(0, 0, 36.3f)),
            (new Vector3(0.65f, 5.85f, 0), new Vector3(0.3f, 0.3f, 22f), Vector3.zero),
            (new Vector3(-0.65f, 5.85f, 0), new Vector3(0.3f, 0.3f, 22f), Vector3.zero),
            (new Vector3(3.57f, 1.525f, 0), new Vector3(0.36f, 4.05f, 22f), Vector3.zero),
            (new Vector3(-3.57f, 1.525f, 0), new Vector3(0.36f, 4.05f, 22f), Vector3.zero),
            (new Vector3(0, 1.5f, -10.93f), new Vector3(7.5f, 4f, 0.16f), Vector3.zero),
            (new Vector3(0, 4.6f, -10.93f), new Vector3(1.8f, 2.2f, 0.16f), Vector3.zero),
            (new Vector3(3.295f, 0.57f, 0.15f), new Vector3(0.15f, 0.3f, 21.7f), Vector3.zero), // (the mooring rails)
            (new Vector3(-3.295f, 0.57f, 0.15f), new Vector3(0.15f, 0.3f, 21.7f), Vector3.zero),
        };
        private static GameObject MakeNaust(ZNetScene scene)
        {
            GameObject from = scene.GetPrefab("wood_floor");
            if (from == null) { Plugin.Warn("No wood_floor to make the naust from"); return null; }
            GameObject go = Object.Instantiate(from, Holder, false); go.name = Names.Naust;
            Strip(go);
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true).ToList()) Object.DestroyImmediate(c);
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true).Where(t => t != go.transform).ToList()) if (t != null) Object.DestroyImmediate(t.gameObject);
            GameObject look = Looks.Add(go.transform, scene, "naust");
            if (look == null) { Object.DestroyImmediate(go); return null; }
            int layer = LayerMask.NameToLayer("piece");
            foreach (var (at, size, turn) in NaustBoxes)
            {
                var o = new GameObject("Collider") { layer = layer }; o.transform.SetParent(go.transform, false); o.transform.localPosition = at; o.transform.localRotation = Quaternion.Euler(turn);
                o.AddComponent<BoxCollider>().size = size;
            }
            WearNTear wear = go.GetComponent<WearNTear>();
            wear.m_new = look; wear.m_worn = look; wear.m_broken = look; wear.m_fragmentRoots = new GameObject[0]; wear.m_materialType = WearNTear.MaterialType.Wood;
            wear.m_noSupportWear = false; wear.m_supports = true; wear.m_noRoofWear = false; wear.m_health = 2000;
            var piece = go.GetComponent<Piece>();
            piece.m_name = "Naust"; piece.m_description = "A boat shed at the shore, open to the sea. Build it at the water's edge, its open end to the sea; a ship moored inside is safe from bumping the shore and the sea's slamming, and her mast stands up through the slot in the ridge.";
            piece.m_category = Piece.PieceCategory.Misc;
            piece.m_craftingStation = scene.GetPrefab("piece_workbench")?.GetComponent<CraftingStation>();
            piece.m_groundPiece = false; piece.m_groundOnly = false; piece.m_clipEverything = true; piece.m_comfort = 0; piece.m_waterPiece = false; piece.m_noInWater = false;
            piece.m_resources = NaustCost.Select(c => Req(scene, c.item, c.n)).Where(r => r != null).ToArray();
            piece.m_enabled = true;
            go.AddComponent<Naust>();
            try { if (Icon.Make(go) is Sprite icon) { piece.m_icon = icon; Looks.Owned.Add(icon); Looks.Owned.Add(icon.texture); } } catch (Exception e) { Plugin.Warn("No naust icon: " + e.Message); }
            Icon.Later(() => { if (go != null && Icon.Make(go) is Sprite again) { piece.m_icon = again; Looks.Owned.Add(again); Looks.Owned.Add(again.texture); } });
            return go;
        }

        // ---- the færing ----
        // (Models/README: 6 m stem to stem, 1.4 m beam, waterline at the origin, keel bottom -0.45, gunwale +0.40; thwarts, steering oar)
        internal const float RowForce = 0.6f;
        private static GameObject MakeFaering(ZNetScene scene)
        {
            GameObject from = scene.GetPrefab("Karve");
            Hull hull = Hulls.Of(Hulls.Faering);
            if (from == null) { Plugin.Warn("No Karve to make the færing from"); return null; }
            GameObject go = Object.Instantiate(from, Holder, false); go.name = Names.Faering;
            Ship ship = go.GetComponent<Ship>();
            // (the karve's look, sail, ropes, chest and colliders go; its trigger, float box, effects, seats and helm stay, moved)
            Strip(go);
            Transform shipRoot = go.transform.Find("ship");
            foreach (Transform t in shipRoot.Cast<Transform>().ToList()) Object.DestroyImmediate(t.gameObject);
            var mast = new GameObject("mast"); mast.transform.SetParent(shipRoot, false); // (Ship turns it with the wind whatever: an empty one)
            foreach (string gone in new[] { "piece_chest", "interactive/sit_box_fl", "interactive/sit_box_back", "interactive/mast", "interactive/sitboard_front", "interactive/sitboard_back" })
                if (go.transform.Find(gone) is Transform t) Object.DestroyImmediate(t.gameObject);
            int layer = go.layer;
            var look = new GameObject("visual"); look.transform.SetParent(shipRoot, false); look.layer = layer;
            foreach (Stage s in hull.Stages) if (Looks.Add(look.transform, scene, s.Model, s.Name) == null) { Object.DestroyImmediate(go); return null; }
            // what's stood on and bumped: the bottom boards, the two sides, the stems
            int vehicle = LayerMask.NameToLayer("vehicle");
            Box(shipRoot, vehicle, new Vector3(0, -0.3f, 0), new Vector3(1.0f, 0.2f, 4.6f));
            Box(shipRoot, vehicle, new Vector3(-0.66f, 0.05f, 0), new Vector3(0.12f, 0.7f, 4.2f));
            Box(shipRoot, vehicle, new Vector3(0.66f, 0.05f, 0), new Vector3(0.12f, 0.7f, 4.2f));
            Box(shipRoot, vehicle, new Vector3(0, 0.1f, 2.6f), new Vector3(0.5f, 1.0f, 0.8f));
            Box(shipRoot, vehicle, new Vector3(0, 0.1f, -2.6f), new Vector3(0.5f, 1.0f, 0.8f));
            if (go.transform.Find("FloatCollider")?.GetComponent<BoxCollider>() is BoxCollider fl) fl.size = new Vector3(3.0f, 1f, 5.4f); // (Ship rights her from its corners: wider than the hull, or she rolls over)
            if (go.transform.Find("OnboardTrigger") is Transform onboard) { onboard.localPosition = new Vector3(0, 0.6f, 0); onboard.localScale = new Vector3(1.6f, 2f, 6f); }
            // the seats: the helm at the stern, one on the fore thwart; the ladder over the port side
            Place(go, "interactive/controlls", new Vector3(0.15f, -0.45f, -1.9f));
            Place(go, "interactive/control_attach", new Vector3(0.15f, -0.32f, -1.9f));
            Place(go, "interactive/sit_box_fr", new Vector3(0, -0.28f, 0.9f));
            Place(go, "ControlGui", new Vector3(0.7f, 0.9f, -1.7f));
            if (go.transform.Find("interactive/ladder") is Transform ladder) { ladder.localPosition = new Vector3(-0.72f, -0.1f, -0.2f); }
            if (go.transform.Find("interactive/ladder_TargetPoint") is Transform lt) lt.localPosition = new Vector3(-0.3f, 0.2f, -0.2f);
            if (go.transform.Find("SurfaceEffects") is Transform fx) fx.localScale = new Vector3(0.45f, 1, 0.6f);
            WaterMask(from, shipRoot, FaeringMask);
            ship.m_mastObject = mast; ship.m_rudderObject = null; ship.m_sailObject = null;
            ship.m_hasSail = false; // (its cloth went with the karve's mast)
            ship.m_backwardForce = RowForce; ship.m_stearForce = 0.4f; ship.m_stearForceOffset = -2.4f; ship.m_stearVelForceFactor = 0.25f;
            ship.m_dampingSideway = 0.2f; ship.m_dampingForward = 0.004f;
            ship.m_waterLevelOffset = 0.27f; // (tuned live, with her centre of mass at -0.3: the waterline at the hull's origin)
            ship.m_forceDistance = 1.5f; ship.m_force = 1f;
            ship.m_floatCollider = go.transform.Find("FloatCollider")?.GetComponent<BoxCollider>();
            WearNTear wear = go.GetComponent<WearNTear>();
            if (wear != null) { wear.m_new = look; wear.m_worn = look; wear.m_broken = look; wear.m_fragmentRoots = new GameObject[0]; wear.m_health = 500; }
            if (go.GetComponent<Rigidbody>() is Rigidbody body) { body.mass = Mathf.Max(1, body.mass * 0.4f); body.automaticCenterOfMass = false; body.centerOfMass = new Vector3(0, -0.3f, 0); }
            var piece = go.GetComponent<Piece>();
            piece.m_name = hull.Title; piece.m_description = "A little four-oared rowing boat, quick and light: two aboard, no sail.";
            piece.m_resources = Hulls.Total(hull).Select(kv => Req(scene, kv.Key, kv.Value)).Where(r => r != null).ToArray(); // (broken up, it gives its makings back)
            piece.m_enabled = true;
            try { if (Icon.Make(go) is Sprite icon) { piece.m_icon = icon; Looks.Owned.Add(icon); Looks.Owned.Add(icon.texture); } } catch (Exception) { }
            return go;
        }
        // ---- the knarr ----
        // (Models/README: 16 m stem to stem, 4.8 m beam, waterline at the origin, keel bottom -1.10, gunwale +1.07 amidships; half-decks fore and
        // aft at +0.35, the open hold between them with its floor at -0.6; the mast at z 0.6, the yard at 11 m)
        private static GameObject MakeKnarr(ZNetScene scene)
        {
            GameObject from = scene.GetPrefab("Karve");
            Hull hull = Hulls.Of(Hulls.Knarr);
            if (from == null) { Plugin.Warn("No Karve to make the knarr from"); return null; }
            GameObject go = Object.Instantiate(from, Holder, false); go.name = Names.Knarr;
            Ship ship = go.GetComponent<Ship>();
            Strip(go);
            Transform shipRoot = go.transform.Find("ship");
            foreach (Transform t in shipRoot.Cast<Transform>().ToList()) Object.DestroyImmediate(t.gameObject);
            foreach (string gone in new[] { "interactive/mast", "interactive/sitboard_front", "interactive/sitboard_back" })
                if (go.transform.Find(gone) is Transform t) Object.DestroyImmediate(t.gameObject);
            int layer = go.layer;
            var look = new GameObject("visual"); look.transform.SetParent(shipRoot, false); look.layer = layer;
            foreach (Stage s in hull.Stages) if (Looks.Add(look.transform, scene, s.Model, s.Name) == null) { Object.DestroyImmediate(go); return null; }
            // the yard and the sail turn with the wind about the mast (Ship turns m_mastObject); the sail is set and furled by Rig
            var mast = new GameObject("mast"); mast.transform.SetParent(shipRoot, false); mast.transform.localPosition = new Vector3(0, 0, hull.MastZ);
            if (Looks.Add(mast.transform, scene, hull.Yard, "yard") == null) { Object.DestroyImmediate(go); return null; }
            var sailAt = new GameObject("sail"); sailAt.transform.SetParent(mast.transform, false); sailAt.transform.localPosition = new Vector3(0, Rig.SailTop, 0); // (Models/README: the sail hangs from its head, y 10.9)
            if (Looks.Add(sailAt.transform, scene, hull.Sail, "cloth") == null) { Object.DestroyImmediate(go); return null; }
            // what's stood on and bumped: the hold's floor, the two half-decks, the sides, the stems
            int vehicle = LayerMask.NameToLayer("vehicle");
            foreach (var (at, size, yaw) in KnarrBoxes)
            {
                var o = new GameObject("Collider") { layer = vehicle }; o.transform.SetParent(shipRoot, false); o.transform.localPosition = at; o.transform.localRotation = Quaternion.Euler(0, yaw, 0);
                o.AddComponent<BoxCollider>().size = size;
            }
            if (go.transform.Find("FloatCollider")?.GetComponent<BoxCollider>() is BoxCollider fl) fl.size = new Vector3(7f, 1.5f, 15f);
            if (go.transform.Find("OnboardTrigger") is Transform onboard) { onboard.localPosition = new Vector3(0, 1.5f, 0); onboard.localScale = new Vector3(5.2f, 4f, 16f); }
            // (Models/README "Places on the finished knarr") the steersman on the starboard aft sea chest by the tiller, three more on the other chests
            // (a seat's attach point stands 0.42 below the seat, as the karve's do); a ladder over each side amidships, down into the hold
            Place(go, "interactive/control_attach", KnarrHelm);
            Place(go, "interactive/controlls", new Vector3(0.75f, 1.0f, -5.9f)); // (the tiller's end)
            Place(go, "interactive/sit_box_fr", new Vector3(0.95f, 0.35f, 5.10f));
            Place(go, "interactive/sit_box_fl", new Vector3(-0.95f, 0.35f, 5.10f));
            Place(go, "interactive/sit_box_back", new Vector3(-0.95f, 0.35f, -4.70f));
            Place(go, "ControlGui", new Vector3(1.6f, 2.0f, -5.2f));
            if (go.transform.Find("interactive/ladder") is Transform ladder && go.transform.Find("interactive/ladder_TargetPoint") is Transform lt)
            {
                ladder.localPosition = new Vector3(-2.45f, 0.45f, -1.0f); lt.localPosition = new Vector3(-0.6f, -0.5f, -1.0f);
                Transform other = Object.Instantiate(ladder.gameObject, ladder.parent, false).transform; other.name = "ladder_starboard";
                other.localPosition = new Vector3(2.45f, 0.45f, -1.0f); other.localRotation = Quaternion.Euler(0, 180, 0) * ladder.localRotation;
                Transform otherTarget = Object.Instantiate(lt.gameObject, lt.parent, false).transform; otherTarget.name = "ladder_starboard_TargetPoint";
                otherTarget.localPosition = new Vector3(0.6f, -0.5f, -1.0f);
                if (other.GetComponent<Ladder>() is Ladder l) l.m_targetPos = otherTarget;
            }
            if (go.transform.Find("SurfaceEffects") is Transform fx) fx.localScale = new Vector3(1.6f, 1, 1.6f);
            WaterMask(from, shipRoot, KnarrMask);
            // the hold: the karve's chest, as big as a knarr's hold, opened from its floor
            if (go.transform.Find("piece_chest") is Transform chest)
            {
                chest.localPosition = new Vector3(0, -0.75f, 0.2f); chest.localRotation = Quaternion.identity; chest.localScale = Vector3.one;
                foreach (Collider c in chest.GetComponents<Collider>()) Object.DestroyImmediate(c);
                var hold = chest.gameObject.AddComponent<BoxCollider>(); hold.size = new Vector3(1.8f, 0.3f, 7.4f); // (its top the hold's floor, y -0.6)
                Container box = chest.GetComponent<Container>();
                box.m_width = hull.CargoWidth; box.m_height = hull.CargoHeight; box.m_name = "Hold";
            }
            ship.m_mastObject = mast; ship.m_rudderObject = null; ship.m_sailObject = null;
            ship.m_hasSail = false; // (no cloth: Rig sets our sail)
            ship.m_sailForceFactor = Sailing.KnarrForceFactor; ship.m_sailForceOffset = 2f;
            ship.m_backwardForce = 0.12f; ship.m_stearForce = 1.0f; ship.m_stearForceOffset = -7f; ship.m_stearVelForceFactor = 0.6f;
            ship.m_dampingSideway = 0.2f; ship.m_dampingForward = 0.0015f; ship.m_angularDamping = 0.3f;
            ship.m_waterLevelOffset = KnarrWaterLevel;
            ship.m_forceDistance = 2.5f; ship.m_force = 1f;
            ship.m_floatCollider = go.transform.Find("FloatCollider")?.GetComponent<BoxCollider>();
            go.AddComponent<Rig>();
            WearNTear wear = go.GetComponent<WearNTear>();
            if (wear != null) { wear.m_new = look; wear.m_worn = look; wear.m_broken = look; wear.m_fragmentRoots = new GameObject[0]; wear.m_health = 1500; }
            if (go.GetComponent<Rigidbody>() is Rigidbody body) { body.mass = body.mass * 2.5f; body.automaticCenterOfMass = false; body.centerOfMass = new Vector3(0, -0.8f, 0); }
            var piece = go.GetComponent<Piece>();
            piece.m_name = hull.Title; piece.m_description = "A broad, deep cargo ship with one big square sail. Her hold carries more than twice a longship's; slow in light airs, strong in a gale.";
            piece.m_resources = Hulls.Total(hull).Select(kv => Req(scene, kv.Key, kv.Value)).Where(r => r != null).ToArray();
            piece.m_enabled = true;
            try { if (Icon.Make(go) is Sprite icon) { piece.m_icon = icon; Looks.Owned.Add(icon); Looks.Owned.Add(icon.texture); } } catch (Exception) { }
            return go;
        }
        // The sea kept out of a hull: the karve's water mask (stripped with its looks; its material hides the water behind it), made
        // again to our hull's shape: a closed volume inside the planking from under the waterline to over it (as high as the sea comes in
        // a swell), its half-widths along the hull at the bottom and top a little inside the strakes (as measured from Models/*_strakes).
        private sealed class Mask { public string Name; public float[] Z, Low, High; public float Bottom, Top; }
        private static readonly Mask KnarrMask = new Mask
        {
            Name = "knarr_watermask", Bottom = -0.45f, Top = 0.7f,
            Z = new[] { -7f, -6f, -5f, -4f, -3f, -2f, -1f, 0f, 1f, 2f, 3f, 4f, 5f, 6f, 7f },
            Low = new[] { 0.02f, 0.15f, 1.3f, 1.5f, 1.54f, 1.55f, 1.55f, 1.55f, 1.55f, 1.55f, 1.54f, 1.5f, 1.3f, 0.15f, 0.02f },
            High = new[] { 0.35f, 1.3f, 1.8f, 2.05f, 2.11f, 2.15f, 2.16f, 2.16f, 2.16f, 2.15f, 2.11f, 2.05f, 1.8f, 1.3f, 0.35f },
        };
        private static readonly Mask FaeringMask = new Mask
        {
            Name = "faering_watermask", Bottom = -0.18f, Top = 0.4f,
            Z = new[] { -2.5f, -2f, -1.5f, -1f, -0.5f, 0f, 0.5f, 1f, 1.5f, 2f, 2.5f },
            Low = new[] { 0.01f, 0.15f, 0.24f, 0.28f, 0.31f, 0.32f, 0.31f, 0.28f, 0.24f, 0.15f, 0.01f },
            High = new[] { 0.06f, 0.33f, 0.52f, 0.59f, 0.62f, 0.62f, 0.62f, 0.59f, 0.52f, 0.33f, 0.06f },
        };
        private static void WaterMask(GameObject karve, Transform shipRoot, Mask shape)
        {
            if (!(karve.transform.Find("ship/visual_new/watermask_waternmask")?.GetComponent<MeshRenderer>()?.sharedMaterial is Material wm))
            { Plugin.Warn($"No water mask on the Karve: the sea will show inside the hull ({shape.Name})"); return; }
            var go = new GameObject("watermask") { layer = shipRoot.gameObject.layer }; go.transform.SetParent(shipRoot, false);
            Mesh m = MaskMesh(shape); Looks.Owned.Add(m);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r = go.AddComponent<MeshRenderer>(); r.sharedMaterial = wm; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        private static Mesh MaskMesh(Mask shape)
        {
            int n = shape.Z.Length;
            var v = new List<Vector3>();
            for (int i = 0; i < n; i++)
            {
                v.Add(new Vector3(-shape.Low[i], shape.Bottom, shape.Z[i])); v.Add(new Vector3(shape.Low[i], shape.Bottom, shape.Z[i]));
                v.Add(new Vector3(-shape.High[i], shape.Top, shape.Z[i])); v.Add(new Vector3(shape.High[i], shape.Top, shape.Z[i]));
            }
            var t = new List<int>();
            void Quad(int a, int b, int c, int d) { t.AddRange(new[] { a, b, c, a, c, d }); } // (clockwise seen from outside: Unity's front)
            for (int i = 0; i + 1 < n; i++)
            {
                int o = i * 4, q = o + 4; // (0 low port, 1 low starboard, 2 high port, 3 high starboard)
                Quad(o + 2, q + 2, q + 3, o + 3); // top
                Quad(o + 0, o + 1, q + 1, q + 0); // bottom
                Quad(o + 0, q + 0, q + 2, o + 2); // port
                Quad(o + 1, o + 3, q + 3, q + 1); // starboard
            }
            int e = (n - 1) * 4;
            Quad(0, 2, 3, 1); Quad(e + 0, e + 1, e + 3, e + 2); // the ends
            var m = new Mesh { name = shape.Name }; m.SetVertices(v); m.SetTriangles(t, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }
        internal static Vector3 KnarrHelm = new Vector3(0.95f, 0.35f, -5.40f);
        internal static float KnarrWaterLevel = 0.42f; // (her waterline at the hull's origin: 0.18 rode her 0.24 low)
        // (Models/README's suggested boxes: the bottom and hold floor, the sides, the half-decks, the closed-in ends, the stems)
        internal static readonly (Vector3 at, Vector3 size, float yaw)[] KnarrBoxes =
        {
            (new Vector3(0, -0.85f, 0), new Vector3(2.8f, 0.5f, 11f), 0),
            (new Vector3(-1.85f, -0.3f, 0), new Vector3(0.5f, 0.6f, 9f), 0), (new Vector3(1.85f, -0.3f, 0), new Vector3(0.5f, 0.6f, 9f), 0),
            (new Vector3(-2.3f, 0.5f, 0), new Vector3(0.2f, 1.1f, 8f), 0), (new Vector3(2.3f, 0.5f, 0), new Vector3(0.2f, 1.1f, 8f), 0),
            (new Vector3(1.75f, 0.55f, 5.6f), new Vector3(0.2f, 1.5f, 2.9f), -21), (new Vector3(-1.75f, 0.55f, -5.6f), new Vector3(0.2f, 1.5f, 2.9f), -21),
            (new Vector3(1.75f, 0.55f, -5.6f), new Vector3(0.2f, 1.5f, 2.9f), 21), (new Vector3(-1.75f, 0.55f, 5.6f), new Vector3(0.2f, 1.5f, 2.9f), 21),
            (new Vector3(0, 0.3f, -5.2f), new Vector3(3.4f, 0.1f, 3.2f), 0), (new Vector3(0, 0.3f, 5.4f), new Vector3(3.4f, 0.1f, 2.8f), 0),
            (new Vector3(0, 0.4f, 7.15f), new Vector3(1.2f, 1.8f, 0.9f), 0), (new Vector3(0, 0.4f, -7.15f), new Vector3(1.2f, 1.8f, 0.9f), 0),
            (new Vector3(0, 1.6f, 7.75f), new Vector3(0.25f, 2.9f, 0.6f), 0), (new Vector3(0, 1.6f, -7.75f), new Vector3(0.25f, 2.9f, 0.6f), 0),
            (new Vector3(0, -0.55f, 6.4f), new Vector3(0.25f, 0.9f, 1.6f), 0), (new Vector3(0, -0.55f, -6.4f), new Vector3(0.25f, 0.9f, 1.6f), 0),
        };

        private static void Place(GameObject go, string path, Vector3 at)
        {
            Transform t = go.transform.Find(path);
            if (t == null) { Plugin.Warn($"The karve has no {path} any more"); return; }
            t.localPosition = at;
        }

        internal static bool IsFaering(Component c) => c != null && Utils.GetPrefabName(c.gameObject) == Names.Faering;
        internal static bool IsOurs(Component c) => HullOf(c) != null;
        internal static Hull HullOf(Component c)
        {
            if (c == null) return null;
            string name = Utils.GetPrefabName(c.gameObject);
            return Hulls.All.FirstOrDefault(h => h.Prefab == name);
        }
    }

    // The færing is rowed, never sailed: forward stops at rowing pace.
    [HarmonyPatch(typeof(Ship), "RPC_Forward")]
    internal static class RowOnly
    {
        private static readonly FieldInfo Speed = AccessTools.Field(typeof(Ship), "m_speed");
        private static bool Prefix(Ship __instance) => !(Yard.IsFaering(__instance) && (Ship.Speed)Speed.GetValue(__instance) == Ship.Speed.Slow);
    }

    // A hull's own way with the wind (Core/Hulls.cs's Sailing): the game's sail force, times its curve.
    [HarmonyPatch(typeof(Ship), "GetSailForce")]
    internal static class HullWind
    {
        private static void Postfix(Ship __instance, ref Vector3 __result)
        {
            if (EnvMan.instance == null || !(Yard.HullOf(__instance) is Hull h)) return;
            __result *= Sailing.WindFactor(h, EnvMan.instance.GetWindIntensity());
        }
    }

    // A knarr's sail, set and furled with the speed asked of her: furled up on the yard at rest and under oars, half set, all set. (The yard
    // turns with the wind with the game's mast.) The sail hangs from its top edge, so it's furled by squashing it up to there.
    internal sealed class Rig : MonoBehaviour
    {
        internal const float SailTop = 10.9f;
        private Ship _ship; private Transform _sail; private float _set;
        private ZNetView _view; private string _painted = "?"; private float _look;
        private void Awake() { _ship = GetComponent<Ship>(); _sail = transform.Find("ship/mast/sail"); _view = GetComponent<ZNetView>(); }
        private void Update()
        {
            if (_ship == null || _sail == null) return;
            // (her sail's colour, as the slipway set it: yard and cloth both)
            if (Time.time > _look && _view != null && _view.IsValid())
            {
                _look = Time.time + 2f; Sails.Bind();
                string colour = _view.GetZDO().GetString(Sails.Key, null);
                if (colour != _painted)
                {
                    _painted = colour;
                    Hull h = Hulls.Of(Hulls.Knarr);
                    Sails.Paint(transform.Find("ship/mast/yard")?.gameObject, h.Yard, colour);
                    Sails.Paint(transform.Find("ship/mast/sail/cloth")?.gameObject, h.Sail, colour);
                }
            }
            Ship.Speed speed = _ship.GetSpeedSetting();
            float want = speed == Ship.Speed.Full ? 1f : speed == Ship.Speed.Half ? 0.5f : 0f;
            _set = Mathf.MoveTowards(_set, want, Time.deltaTime * 0.6f);
            _sail.localScale = new Vector3(1, Mathf.Max(0.02f, _set), 1);
            _sail.gameObject.SetActive(_set > 0.03f);
        }
    }
}
