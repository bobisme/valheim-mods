using System.Linq;
using UnityEngine;

namespace Shieldwall
{
    // For other mods (Jarldom: villagers defend a colony's stone), found by reflection: the type "Shieldwall.Api" in this assembly.
    public static class Api
    {
        // A siege (warning, marching or fighting) at a Warstone within radius metres of a point, on any game that has the stone loaded.
        public static bool IsSiegeAt(Vector3 at, float radius) =>
            Warstone.Loaded.Any(w => w != null && w.Z != null && w.Phase != Phase.Idle && Vector3.Distance(w.transform.position, at) <= radius);
        // Where that stone stands (Vector3.zero if none).
        public static Vector3 StoneAt(Vector3 at, float radius) =>
            Warstone.Loaded.Where(w => w != null && w.Z != null && Vector3.Distance(w.transform.position, at) <= radius).Select(w => w.transform.position).DefaultIfEmpty(Vector3.zero).First();
    }
}
