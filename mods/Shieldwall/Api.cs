using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Shieldwall
{
    // For other mods (Jarldom: villagers defend a colony's stone), found by reflection: the type "Shieldwall.Api" in this assembly.
    // Rules: bump Version when a method is added; never change a method's signature (add a new one).
    public static class Api
    {
        public const int Version = 2; // (1: IsSiegeAt, CallOff, StoneAt; 2: TowerPosts, SetWardBonus, WardBonus)
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
        // The stave sockets within radius metres of a point (loaded on this game), the raised ones first (on a wall or a tower top: the
        // height of the socket over the ground under it), then the nearest: where defenders take their places in a siege.
        public static IList<Vector3> TowerPosts(Vector3 at, float radius) =>
            Planted.Loaded.Where(p => p != null && Vector3.Distance(p.transform.position, at) <= radius).Select(p => p.transform.position)
                .OrderByDescending(p => Mathf.Round(Raised(p) * 2) / 2).ThenBy(p => Vector3.Distance(p, at)).ToList();
        private static float Raised(Vector3 p) => ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(p, out float g) ? Mathf.Max(0, p.y - g) : 0;
        // A ward bonus at the stones within radius metres: the ward works this many times as strong (1.1: a tenth stronger) for this
        // many seconds, from this source (a source's newer call replaces its older; several sources multiply, to 1.5 at most). Kept on
        // the stone, so every player in its reach feels it; from any game (sent to the stone's owner). How many stones took it.
        public static int SetWardBonus(Vector3 at, float radius, string source, float mult, float seconds)
        {
            int n = 0;
            foreach (Warstone w in Warstone.Loaded.ToList())
                if (w != null && w.Z != null && Vector3.Distance(w.transform.position, at) <= radius) { Net.WardBonus(w, source, mult, seconds); n++; }
            return n;
        }
        // The ward bonus at the strongest stone within radius metres now (1: none).
        public static float WardBonus(Vector3 at, float radius) =>
            Warstone.Loaded.Where(w => w != null && w.Z != null && Vector3.Distance(w.transform.position, at) <= radius).Select(Ward.Bonus).DefaultIfEmpty(1f).Max();
        // Where that stone stands (Vector3.zero if none).
        public static Vector3 StoneAt(Vector3 at, float radius) =>
            Warstone.Loaded.Where(w => w != null && w.Z != null && Vector3.Distance(w.transform.position, at) <= radius).Select(w => w.transform.position).DefaultIfEmpty(Vector3.zero).First();
    }
}
