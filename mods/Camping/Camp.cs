using System;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Camping
{
    // How the kit's beds behave: a bedroll wants no roof (but still a fire, and a dry sleeper: the game's own checks), a tent is its own
    // roof and warmth; either packs up again with Shift+E, what it cost going back in the bag (on the ground, if the bag is full).
    internal static class Camp
    {
        private static bool Ours(Component c) => c != null && Kit.Packs(Utils.GetPrefabName(c.gameObject));

        [HarmonyPatch(typeof(Bed), "CheckExposure")]
        private static class UnderTheSky
        {
            private static bool Prefix(Bed __instance, ref bool __result)
            {
                if (!Ours(__instance)) return true;
                __result = true; return false; // (a bedroll under the stars; a tent its own roof)
            }
        }

        [HarmonyPatch(typeof(Bed), nameof(Bed.Interact))]
        private static class PackUp
        {
            private static bool Prefix(Bed __instance, Humanoid human, bool repeat, bool alt, ref bool __result)
            {
                if (!alt || repeat || !Ours(__instance) || !(human is Player player) || player != Player.m_localPlayer) return true;
                __result = true;
                try { Pack(__instance, player); } catch (Exception e) { Plugin.Warn("Packing up: " + e); }
                return false;
            }
        }

        [HarmonyPatch(typeof(Bed), nameof(Bed.GetHoverText))]
        private static class Hint
        {
            private static void Postfix(Bed __instance, ref string __result)
            {
                if (!Ours(__instance)) return;
                string what = Utils.GetPrefabName(__instance.gameObject) == Kit.Tent ? "Pack up" : "Roll up";
                __result += Localization.instance.Localize($"\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] {what}");
            }
        }

        private static readonly System.Reflection.MethodInfo OwnerOf = AccessTools.Method(typeof(Bed), "GetOwner");
        private static void Pack(Bed bed, Player player)
        {
            Piece piece = bed.GetComponent<Piece>(); ZNetView view = bed.GetComponent<ZNetView>();
            if (piece == null || view == null || !view.IsValid()) return;
            string title = Localization.instance.Localize(piece.m_name).ToLower();
            if (!PrivateArea.CheckAccess(bed.transform.position)) return; // (a ward not ours: the game says so)
            long owner = OwnerOf != null ? (long)OwnerOf.Invoke(bed, null) : 0L;
            if (owner != 0L && owner != Game.instance.GetPlayerProfile().GetPlayerID()) { player.Message(MessageHud.MessageType.Center, $"This {title} is someone else's."); return; }
            if (player.IsAttached() || player.InBed()) return;
            Inventory bag = player.GetInventory();
            foreach (Piece.Requirement r in piece.m_resources)
            {
                if (r?.m_resItem == null || r.m_amount <= 0) continue;
                int left = r.m_amount;
                while (left > 0)
                {
                    int n = Math.Min(left, r.m_resItem.m_itemData.m_shared.m_maxStackSize);
                    if (!bag.AddItem(r.m_resItem.gameObject, n))
                    {
                        // (no room: on the ground beside it)
                        GameObject dropped = Object.Instantiate(r.m_resItem.gameObject, bed.transform.position + Vector3.up * 0.5f, Quaternion.identity);
                        dropped.GetComponent<ItemDrop>()?.SetStack(n);
                    }
                    left -= n;
                }
            }
            WearNTear wear = bed.GetComponent<WearNTear>();
            if (wear != null) wear.Remove(); else { view.ClaimOwnership(); ZNetScene.instance.Destroy(bed.gameObject); }
            player.Message(MessageHud.MessageType.Center, Utils.GetPrefabName(bed.gameObject) == Kit.Tent ? "You pack up the tent." : "You roll up the bedroll.");
        }
    }
}
