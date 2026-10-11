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
        // Ends the sieges at stones within radius metres that this game runs (their owner's game), as if the horde were paid to go.
        // Returns how many it ended.
        public static int CallOff(Vector3 at, float radius)
        {
            int n = 0;
            foreach (Warstone w in Warstone.Loaded.ToList())
                if (w != null && w.Z != null && w.View.IsOwner() && w.Phase != Phase.Idle && Vector3.Distance(w.transform.position, at) <= radius) { Director.CallOff(w); n++; }
            return n;
        }
        // Where that stone stands (Vector3.zero if none).
        public static Vector3 StoneAt(Vector3 at, float radius) =>
            Warstone.Loaded.Where(w => w != null && w.Z != null && Vector3.Distance(w.transform.position, at) <= radius).Select(w => w.transform.position).DefaultIfEmpty(Vector3.zero).First();
    }
}
