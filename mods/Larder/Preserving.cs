using HarmonyLib;
using UnityEngine;

namespace Larder
{
    // A drying rack shows what's on it: a stockfish on its bars for each fish hung (drying, or dry and waiting to be taken down), read from
    // the queue the game's smelter keeps on its ZDO, so everyone sees the same. Its children are made with the prefab (Kit.MakeWorks); this
    // holds nothing of its own (a hot reload swaps it for the next copy's, fields at their defaults).
    internal sealed class Rack : MonoBehaviour
    {
        internal const string Hung = "hung_";
        private ZNetView _view; private int _shown = -1;
        private void Awake() { _view = GetComponent<ZNetView>(); InvokeRepeating(nameof(Show), 0.5f, 2f); }
        private void Show()
        {
            if (_view == null || !_view.IsValid()) return;
            ZDO z = _view.GetZDO();
            int n = z.GetInt(ZDOVars.s_queued) + z.GetInt(ZDOVars.s_spawnAmount);
            if (n == _shown) return;
            _shown = n;
            for (int i = 0; ; i++)
            {
                Transform t = transform.Find(Hung + i);
                if (t == null) break;
                t.gameObject.SetActive(i < n);
            }
        }
    }

    // What our stations need where they stand, in the smelter's own "needs a roof" check (Smelter.m_requiresRoof, set on ours that need
    // anything): for the rack, the open sky over it (it doesn't dry under a roof); for the salt pan, sea water round its feet. When it
    // isn't met the queue waits, as the game's smelters do without their roof, and the hover says why.
    [HarmonyPatch(typeof(Smelter), "UpdateRoof")]
    internal static class Sky
    {
        private static void Postfix(Smelter __instance, ref bool ___m_haveRoof)
        {
            if (!__instance.name.StartsWith("Larder_") || !(Kit.WorksOf(Utils.GetPrefabName(__instance.gameObject)) is Works w)) return;
            Vector3 at = __instance.m_roofCheckPoint != null ? __instance.m_roofCheckPoint.position : __instance.transform.position;
            if (w.Needs == Needs.OpenSky) ___m_haveRoof = !Cover.IsUnderRoof(at);
            else if (w.Needs == Needs.Sea) ___m_haveRoof = InSea(__instance.transform.position);
        }
        // Standing in the shallows: its foot under the sea's level, not drowned (the pan's rim stays above the water).
        internal static bool InSea(Vector3 foot)
        {
            float sea = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f, depth = sea - foot.y;
            return depth > 0.05f && depth < SeaDepth;
        }
        internal const float SeaDepth = 0.6f;
        internal static string Why(Needs n) => n == Needs.OpenSky ? "Needs the open sky over it: it won't dry under a roof" : n == Needs.Sea ? "Needs to stand in the sea, in the shallows" : "";
    }

    [HarmonyPatch(typeof(Smelter), "OnHoverAddOre")]
    internal static class SkyHover
    {
        private static void Postfix(Smelter __instance, ref string __result, bool ___m_haveRoof)
        {
            if (___m_haveRoof || !__instance.name.StartsWith("Larder_") || !(Kit.WorksOf(Utils.GetPrefabName(__instance.gameObject)) is Works w) || w.Needs == Needs.Nothing) return;
            string roof = Localization.instance.Localize("$piece_smelter_reqroof");
            __result = __result.Replace(" <color=yellow>" + roof + "</color>", "");
            int line = __result.IndexOf('\n');
            string why = "\n<color=yellow>" + Sky.Why(w.Needs) + "</color>";
            __result = line < 0 ? __result + why : __result.Insert(line, why);
        }
    }
}
