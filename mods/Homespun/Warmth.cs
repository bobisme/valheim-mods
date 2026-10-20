using System.Linq;
using HarmonyLib;

namespace Homespun
{
    // What the clothes do worn (Core/Wear.cs): the cloak's frost resistance is its own (Things.Sew); here the hood against the rain, the
    // mittens against the chill, and dyed clothes for comfort.
    internal static class Warmth
    {
        private static bool Wearing(Player p, System.Func<string, bool> what) =>
            p != null && p.GetInventory() != null && p.GetInventory().GetEquippedItems().Any(i => i.m_dropPrefab != null && what(i.m_dropPrefab.name));

        // The chill (not the mountains' freezing: that's a status of its own) and the rain's wet (not the sea's: in the water you're soaked).
        [HarmonyPatch(typeof(SEMan), nameof(SEMan.AddStatusEffect), typeof(int), typeof(bool), typeof(int), typeof(float), typeof(short))]
        private static class Weather
        {
            private static bool Prefix(int nameHash, ref StatusEffect __result, Character ___m_character)
            {
                if (!(___m_character is Player p)) return true;
                if (nameHash == SEMan.s_statusEffectCold && Wearing(p, Wear.Mittens)) { __result = null; return false; }
                if (nameHash == SEMan.s_statusEffectWet && !p.InWater() && Wearing(p, Wear.Hooded)) { __result = null; return false; }
                return true;
            }
        }

        // Mittens put on in the cold: the chill already felt goes (the game only ever adds it while cold, and takes it away when warm).
        [HarmonyPatch(typeof(Player), "UpdateEnvStatusEffects")]
        private static class Warmed
        {
            private static void Postfix(Player __instance)
            {
                SEMan se = __instance.GetSEMan();
                if (se.HaveStatusEffect(SEMan.s_statusEffectCold) && Wearing(__instance, Wear.Mittens)) se.RemoveStatusEffect(SEMan.s_statusEffectCold, true);
            }
        }

        // Dressed in dyed clothes: comfort (the game reckons it every few seconds while resting).
        [HarmonyPatch(typeof(SE_Rested), nameof(SE_Rested.CalculateComfortLevel), typeof(Player))]
        private static class Fine
        {
            private static void Postfix(Player player, ref int __result)
            {
                if (player?.GetInventory() == null) return;
                __result += Wear.Comfort(player.GetInventory().GetEquippedItems().Where(i => i.m_dropPrefab != null).Select(i => i.m_dropPrefab.name));
            }
        }
    }
}
