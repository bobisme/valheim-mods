using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Byre
{
    // The byre's own pieces beside the trough: the hay rack (Trough.MakeRack: a feeder like the trough) and the stall divider, a plank
    // partition 2 m long and 1.3 m high that snaps end to end, to a wall and to the floor. Stalls are rows of dividers off a wall, closed
    // with the game's own fence gate (the game has gates and fences: Byre adds none).
    internal static class Barn
    {
        internal static GameObject MakeStall(ZNetScene scene, Transform holder)
        {
            GameObject from = scene.GetPrefab("wood_fence");
            if (from == null) { Plugin.Warn("No wood_fence to make the stall divider from."); return null; }
            Material wood = Trough.Wood(scene);
            GameObject go = Object.Instantiate(from, holder, false); go.name = "ByreStall";
            foreach (Collider old in go.GetComponentsInChildren<Collider>(true).Where(x => !x.isTrigger)) Object.DestroyImmediate(old);
            foreach (Transform t in go.GetComponentsInChildren<Transform>(true).Where(t => t != go.transform && (t.name.StartsWith("_snappoint") || t.CompareTag("snappoint"))).ToList()) Object.DestroyImmediate(t.gameObject);
            GameObject look = Models.Replace(go, "stall", wood);
            if (look == null) { Object.DestroyImmediate(go); return null; }
            int layer = LayerMask.NameToLayer("piece");
            Trough.Solid(go, layer, new Vector3(0, 0.7f, 0), new Vector3(2.16f, 1.4f, 0.16f));
            // (its four corners, as a wall's: end to end, onto a wall's side, stacked on a floor's edge)
            foreach (float x in new[] { -1f, 1f })
                foreach (float y in new[] { 0f, 1.3f })
                {
                    var snap = new GameObject("$hud_snappoint_corner") { tag = "snappoint", layer = layer };
                    snap.transform.SetParent(go.transform, false); snap.transform.localPosition = new Vector3(x, y, 0);
                    snap.SetActive(false); // (as the game's own: switched-off direct children)
                }
            if (go.GetComponent<WearNTear>() is WearNTear w) { w.m_new = look; w.m_worn = look; w.m_broken = look; w.m_fragmentRoots = new GameObject[0]; w.m_materialType = WearNTear.MaterialType.Wood; w.m_supports = true; }
            Piece piece = go.GetComponent<Piece>();
            piece.m_name = "Stall divider";
            piece.m_description = "Planks between two posts, chest high: rows of them off a wall make a byre's stalls (close one with a fence gate).";
            piece.m_category = Piece.PieceCategory.Misc;
            piece.m_craftingStation = Costs.FindStation("piece_workbench", out _);
            piece.m_resources = Costs.Parse((string)Plugin.Instance.StallCost.DefaultValue, null, forRecipe: false) ?? new Piece.Requirement[0];
            Costs.Apply(piece, Plugin.Instance.StallCost.Value, Plugin.Instance.Logs);
            piece.m_enabled = true;
            Trough.Icons(go, piece);
            return go;
        }
    }
}
