using System.Linq;
using UnityEngine;

namespace Wayfinder
{
    // For other mods, found by reflection: the type "Wayfinder.Api" in this assembly (see Jarldom's Compat/API-CONVENTION.md).
    // Rules: bump Version when a method is added; never change a method's signature (add a new one).
    public static class Api
    {
        public const int Version = 1;

        // The nearest Beacon Fire within the radius that's loaded on this game; null if there's none.
        public static Vector3? NearestBeacon(Vector3 at, float radius)
        {
            BeaconView b = Nearest(at, radius);
            return b != null ? b.transform.position : (Vector3?)null;
        }
        // Lights the nearest Beacon Fire within the radius: filled with fuel by the game's own call, sent to whichever game owns it (safe
        // from any client). False if there's none loaded here, or it's lit already.
        public static bool LightBeacon(Vector3 at, float radius)
        {
            BeaconView b = Nearest(at, radius);
            Fireplace fire = b != null ? b.GetComponent<Fireplace>() : null;
            ZNetView v = b != null ? b.GetComponent<ZNetView>() : null;
            if (fire == null || v == null || !v.IsValid() || b.Lit) return false;
            v.InvokeRPC("RPC_SetFuelAmount", fire.m_maxFuel);
            return true;
        }
        // Whether the nearest Beacon Fire within the radius burns; false if there's none.
        public static bool BeaconLit(Vector3 at, float radius) { BeaconView b = Nearest(at, radius); return b != null && b.Lit; }

        private static BeaconView Nearest(Vector3 at, float radius) =>
            BeaconView.Live.Where(b => b != null && Vector3.Distance(b.transform.position, at) <= radius).OrderBy(b => Vector3.Distance(b.transform.position, at)).FirstOrDefault();
    }
}
