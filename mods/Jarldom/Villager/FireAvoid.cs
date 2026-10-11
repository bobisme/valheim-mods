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
            if (f == null || !Classes.Warms(f)) return;
            if (f.GetComponent<NavMeshObstacle>() is NavMeshObstacle had) { if (had.radius > 1.2f) had.radius = 1.2f; return; } // (one cut too wide by an earlier build)
            // (the fire itself, its stones, not the warmth round it: a wide cut walled the hall's door off from the rest of the floor,
            // and the hearth no longer burns them anyway)
            float r = 0.8f;
            foreach (Collider col in f.GetComponentsInChildren<Collider>(true))
                if (!col.isTrigger && col.enabled) r = Mathf.Max(r, Mathf.Max(col.bounds.extents.x, col.bounds.extents.z));
            var o = f.gameObject.AddComponent<NavMeshObstacle>();
            o.shape = NavMeshObstacleShape.Capsule; o.radius = Mathf.Min(r, 1.2f); o.height = 2f; o.center = Vector3.up;
            o.carving = true; o.carveOnlyStationary = true;
        }
        [HarmonyPatch(typeof(Fireplace), "Start")]
        private static class OnStart { private static void Postfix(Fireplace __instance) => Plugin.Guard(() => Ensure(__instance)); }
    }
}
