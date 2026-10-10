using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Farmhand
{
    internal static class GameAccess
    {
        private static readonly MethodInfo TakeInput = AccessTools.Method(typeof(Player), "TakeInput");
        private static readonly MethodInfo BuildStamina = AccessTools.Method(typeof(Player), "GetBuildStamina");
        private static readonly MethodInfo PlaceDurability = AccessTools.Method(typeof(Player), "GetPlaceDurability");
        private static readonly FieldInfo GhostField = AccessTools.Field(typeof(Player), "m_placementGhost");
        private static readonly FieldInfo LastUse = AccessTools.Field(typeof(Player), "m_lastToolUseTime");
        private static readonly FieldInfo BuildDebt = AccessTools.Field(typeof(Player), "m_buildRemoveDebt");
        private static readonly FieldInfo RightItemField = AccessTools.Field(typeof(Humanoid), "m_rightItem");
        internal static Vector3? Target;
        internal static Vector3? ValidatedPosition;
        internal static ItemDrop.ItemData RightItem(Player p) => RightItemField.GetValue(p) as ItemDrop.ItemData;
        internal static GameObject Ghost(Player p) => GhostField.GetValue(p) as GameObject;
        internal static bool CanTakeInput(Player p) => (bool)TakeInput.Invoke(p, null);

        internal static bool PlanningActive()
        {
            // Optional compatibility; ScriptEngine does not put reloaded plugins in Chainloader.PluginInfos.
            var info = Harmony.GetPatchInfo(AccessTools.Method(typeof(Player), nameof(Player.TryPlacePiece)));
            if (info == null) return false;
            foreach (Patch patch in info.Prefixes)
            {
                if (patch.owner != "com.quad.buildorders" && patch.owner != "com.dhack.buildorders") continue; // (its id before 1.12.1)
                Type type = patch.PatchMethod.DeclaringType.Assembly.GetType("BuildOrders.Plugin");
                object instance = AccessTools.Field(type, "Instance")?.GetValue(null);
                PropertyInfo planning = AccessTools.Property(type, "PlanKeyHeld");
                // Fail closed if this optional mod changes its API.
                if (instance == null || planning == null || (bool)planning.GetValue(instance, null)) return true;
            }
            return false;
        }

        internal static bool TryGround(Player p, Piece piece, Vector3 target, out RaycastHit hit)
        {
            if (!Physics.Raycast(target + Vector3.up * 2f, Vector3.down, out hit, 4f,
                LayerMask.GetMask("terrain"), QueryTriggerInteraction.Ignore)) return false;
            return Vector3.Distance(p.GetEyePoint(), hit.point) < p.m_maxPlaceDistance + piece.m_extraPlacementDistance;
        }

        internal static bool TryPlant(Player p, Piece piece, Vector3 target, out string reason)
        {
            reason = "Invalid placement";
            ItemDrop.ItemData tool = GameAccess.RightItem(p);
            if (tool == null || tool.m_shared.m_name != "$item_cultivator" || PlanningActive()) return false;
            if (!p.IsRecipeKnown(piece.m_name) || !p.IsPieceAvailable(piece) || !p.SetSelectedPiece(piece)) return false;
            if (Plugin.Instance != null) Plugin.Instance.ExpectedPiece = piece;
            if (tool.m_shared.m_useDurability && tool.m_durability <= 0f) { reason = "Cultivator is broken"; return false; }
            if (!p.HaveStamina(tool.m_shared.m_attack.m_attackStamina)) { reason = "Not enough stamina"; return false; }
            bool free = ZoneSystem.instance.GetGlobalKey(piece.FreeBuildKey());
            if (!p.HaveRequirements(piece, Player.RequirementMode.CanBuild)) { reason = "Missing seeds/materials"; return false; }
            if (!TryGround(p, piece, target, out RaycastHit hit)) { reason = "Out of reach"; return false; }
            // Resolve reflective cost calculations BEFORE any world mutation.
            float stamina = (float)BuildStamina.Invoke(p, null);
            float durability = (float)PlaceDurability.Invoke(p, new object[] { tool }) * Game.m_durabilityRate;
            try
            {
                Target = hit.point;
                ValidatedPosition = null;
                // Native ghost validation, ward/terrain/biome checks, prefab spawn, effects, creator and stats.
                if (!p.TryPlacePiece(piece)) return false;
                // Costs live in Player.UpdatePlacement, outside TryPlacePiece. Keep that same order here.
                if (!free) p.ConsumeResources(piece.m_resources, 0);
                p.UseStamina(stamina);
                if (tool.m_shared.m_useDurability) tool.m_durability -= durability;
                LastUse.SetValue(p, Time.time);
                PieceTable table = p.GetBuildTool();
                if (table.m_skill != Skills.SkillType.None)
                {
                    int debt = (int)BuildDebt.GetValue(p);
                    if (debt > 0) BuildDebt.SetValue(p, debt - 1);
                    else p.RaiseSkill(table.m_skill);
                }
                return true;
            }
            finally { Target = null; ValidatedPosition = null; }
        }
    }
}
