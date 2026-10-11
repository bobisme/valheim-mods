using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Jarldom
{
    // A point on the navmesh near a spot. The game's own Pathfinding.FindValidPoint queries with the agent type's enum value instead of the
    // navmesh agent's id, so for humanoids it nearly always says no; this asks the way GetPath does (its private SnapToNavMesh).
    internal static class Nav
    {
        private static readonly MethodInfo GetSettings = AccessTools.Method(typeof(Pathfinding), "GetSettings");
        private static readonly MethodInfo Snap = AccessTools.Method(typeof(Pathfinding), "SnapToNavMesh");
        private static readonly MethodInfo PokePoint = AccessTools.Method(typeof(Pathfinding), "PokePoint");
        public static bool Valid(out Vector3 point, Vector3 near, float range = 1.5f, Pathfinding.AgentType agent = Pathfinding.AgentType.Humanoid)
        {
            point = near;
            Pathfinding pf = Pathfinding.instance;
            if (pf == null || GetSettings == null || Snap == null) return false;
            try
            {
                PokePoint?.Invoke(pf, new object[] { near, agent });
                object settings = GetSettings.Invoke(pf, new object[] { agent });
                var args = new object[] { near, true, settings };
                if (!(bool)Snap.Invoke(pf, args)) return false;
                Vector3 p = (Vector3)args[0];
                if (Vector3.Distance(p, near) > range + 1f) return false; // (it searches a little wider than asked)
                point = p;
                return true;
            }
            catch { return false; }
        }
    }
}
