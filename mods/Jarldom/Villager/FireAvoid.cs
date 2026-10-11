using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;

namespace Jarldom
{
    // A fire that warms (a fire pit, a hearth, a bonfire) is cut out of the walking map, so villagers' paths go round it instead of
    // through it (two were burned to death crossing the hall's and the healer's fire pits). Torches stay as they are.
    internal static class FireAvoid
    {
        public static void Ensure(Fireplace f)
        {
            if (f == null || f.GetComponent<NavMeshObstacle>() != null || !Classes.Warms(f)) return;
            float r = 1.1f;
            foreach (EffectArea a in f.GetComponentsInChildren<EffectArea>(true))
                if ((a.m_type & EffectArea.Type.Burning) != 0 && a.GetComponent<Collider>() is Collider col) r = Mathf.Max(r, Mathf.Max(col.bounds.extents.x, col.bounds.extents.z) + 0.3f);
            var o = f.gameObject.AddComponent<NavMeshObstacle>();
            o.shape = NavMeshObstacleShape.Capsule; o.radius = Mathf.Min(r, 3f); o.height = 2f; o.center = Vector3.up;
            o.carving = true; o.carveOnlyStationary = true;
        }
        [HarmonyPatch(typeof(Fireplace), "Start")]
        private static class OnStart { private static void Postfix(Fireplace __instance) => Plugin.Guard(() => Ensure(__instance)); }
    }
}
