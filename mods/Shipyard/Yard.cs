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
            foreach (string name in new[] { Names.Faering, Names.Slipway })
            {
                GameObject prefab = Get(name);
                if (prefab == null)
                {
                    try { prefab = name == Names.Slipway ? MakeSlipway(scene) : MakeFaering(scene); }
                    catch (Exception e) { Plugin.Warn($"Could not make {name}: {e}"); _broken = true; return; }
                    if (prefab == null) { every = false; continue; }
                    Made[name] = prefab;
                }
                PieceTable menu = name == Names.Slipway && Plugin.Instance.Enabled.Value ? table : null;
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
        private static void Place(GameObject go, string path, Vector3 at)
        {
            Transform t = go.transform.Find(path);
            if (t == null) { Plugin.Warn($"The karve has no {path} any more"); return; }
            t.localPosition = at;
        }

        internal static bool IsFaering(Component c) => c != null && Utils.GetPrefabName(c.gameObject) == Names.Faering;
    }

    // The færing is rowed, never sailed: forward stops at rowing pace.
    [HarmonyPatch(typeof(Ship), "RPC_Forward")]
    internal static class RowOnly
    {
        private static readonly FieldInfo Speed = AccessTools.Field(typeof(Ship), "m_speed");
        private static bool Prefix(Ship __instance) => !(Yard.IsFaering(__instance) && (Ship.Speed)Speed.GetValue(__instance) == Ship.Speed.Slow);
    }
}
