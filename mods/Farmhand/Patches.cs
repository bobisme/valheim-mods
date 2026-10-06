using HarmonyLib;
using UnityEngine;

namespace Farmhand
{
    [HarmonyPatch(typeof(Player), "PieceRayTest")]
    internal static class PlantingRay
    {
        private static bool Prefix(Player __instance, ref Vector3 point, ref Vector3 normal, ref Piece piece,
            ref Heightmap heightmap, ref Collider waterSurface, ref bool __result)
        {
            if (__instance != Player.m_localPlayer || !GameAccess.Target.HasValue) return true;
            piece = null;
            heightmap = null;
            waterSurface = null;
            point = normal = Vector3.zero;
            Piece selected = __instance.GetSelectedPiece();
            RaycastHit hit = default;
            __result = selected != null && GameAccess.TryGround(__instance, selected, GameAccess.Target.Value, out hit);
            if (__result)
            {
                point = hit.point;
                normal = hit.normal;
                heightmap = hit.collider.GetComponent<Heightmap>();
                if (Physics.Raycast(point + Vector3.up * 2f, Vector3.down, out RaycastHit water, 2f,
                    LayerMask.GetMask("Water"), QueryTriggerInteraction.Ignore)) waterSurface = water.collider;
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
    internal static class ValidateCrop
    {
        [HarmonyPriority(Priority.First)]
        [HarmonyBefore("com.dhack.buildorders")]
        private static void Postfix(Player __instance, GameObject ___m_placementGhost, ref Player.PlacementStatus ___m_placementStatus)
        {
            if (__instance != Player.m_localPlayer || !GameAccess.Target.HasValue || ___m_placementGhost == null ||
                ___m_placementStatus != Player.PlacementStatus.Valid) return;
            // Native placement can adjust a crop pivot above the terrain. Save its validated final position.
            GameAccess.ValidatedPosition = ___m_placementGhost.transform.position;
            Plant plant = ___m_placementGhost.GetComponent<Plant>();
            if (plant == null) { ___m_placementStatus = Player.PlacementStatus.Invalid; return; }
            // The game's first 10 seconds always say Healthy, so evaluate past that grace period.
            plant.UpdateHealth(11.0);
            if (plant.GetStatus() != Plant.Status.Healthy) ___m_placementStatus = Player.PlacementStatus.MoreSpace;
        }
    }

    [HarmonyPatch(typeof(Player), "UpdatePlacementGhost")]
    internal static class RejectRedirectedCrop
    {
        [HarmonyPriority(Priority.Last)]
        [HarmonyAfter("com.dhack.buildorders")]
        private static void Postfix(Player __instance, GameObject ___m_placementGhost, ref Player.PlacementStatus ___m_placementStatus)
        {
            if (__instance != Player.m_localPlayer || !GameAccess.Target.HasValue) return;
            // Reject missing validation or a ghost moved after validation by another mod's snapping.
            if (___m_placementGhost == null || !GameAccess.ValidatedPosition.HasValue ||
                (___m_placementGhost.transform.position - GameAccess.ValidatedPosition.Value).sqrMagnitude > 0.0001f)
                ___m_placementStatus = Player.PlacementStatus.Invalid;
        }
    }

    [HarmonyPatch(typeof(Player), "UpdatePlacement")]
    internal static class PauseManualPlanting
    {
        private static void Prefix(Player __instance, ref bool takeInput)
        {
            if (__instance == Player.m_localPlayer && Plugin.Instance != null && Plugin.Instance.Busy) takeInput = false;
        }
    }
}
