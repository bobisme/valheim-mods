using HarmonyLib;
using UnityEngine;

namespace Byre
{
    // Pigs from the game's boars: a tame boar's litter is, by chance (PigFromBoarChance), a pig's piglet in place of the boar's own. Each
    // birth rolls on its own. Pigs breed pigs from then on (their own Procreation). Only the game's tame boars, only at a birth.
    internal static class Breeding
    {
        private static readonly AccessTools.FieldRef<Procreation, GameObject> Offspring = AccessTools.FieldRefAccess<Procreation, GameObject>("m_offspringPrefab");
        private static readonly AccessTools.FieldRef<Procreation, GameObject> Mine = AccessTools.FieldRefAccess<Procreation, GameObject>("m_myPrefab");
        private static readonly AccessTools.FieldRef<Procreation, ZNetView> View = AccessTools.FieldRefAccess<Procreation, ZNetView>("m_nview");

        // (before the game's: the young it's about to have chosen, its prefab looked up as the game would have; the game then makes it)
        [HarmonyPatch(typeof(Procreation), "Procreate")]
        private static class Litter
        {
            private static void Prefix(Procreation __instance) => Plugin.Guard(() =>
            {
                ZNetView view = View(__instance);
                if (view == null || !view.IsValid() || !view.IsOwner() || Utils.GetPrefabName(__instance.gameObject) != "Boar") return;
                long pregnant = view.GetZDO().GetLong(ZDOVars.s_pregnant, 0L);
                if (pregnant == 0L || (ZNet.instance.GetTime() - new System.DateTime(pregnant)).TotalSeconds <= __instance.m_pregnancyDuration) return; // (not due)
                ZNetScene scene = ZNetScene.instance;
                GameObject piglet = scene.GetPrefab(Species.Pig.Young), own = scene.GetPrefab(Utils.GetPrefabName(__instance.m_offspring));
                if (piglet == null || own == null) return;
                if (Mine(__instance) == null) Mine(__instance) = scene.GetPrefab(view.GetZDO().GetPrefab());
                float chance = Plugin.Instance != null ? Plugin.Instance.PigFromBoarChance.Value / 100f : 0.25f;
                Offspring(__instance) = Random.value < chance ? piglet : own;
            });
        }
    }
}
