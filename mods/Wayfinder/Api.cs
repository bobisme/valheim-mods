using System.Linq;
using UnityEngine;

namespace Wayfinder
{
    // The feast days other mods burnt into the primstav (Api.AddFeastDay).
    internal static class Feasts
    {
        internal static readonly System.Collections.Generic.List<(string source, int day, string name)> Days = new System.Collections.Generic.List<(string, int, string)>();
        // The next feast within a fortnight: "Midsummer in 3 days", "Midsummer today", or "".
        internal static string Next()
        {
            if (EnvMan.instance == null) return "";
            int today = EnvMan.instance.GetDay();
            var next = Days.Where(d => d.day >= today && d.day - today <= 14).OrderBy(d => d.day).FirstOrDefault();
            if (next.name == null) return "";
            int n = next.day - today;
            return n == 0 ? next.name + " today" : n == 1 ? next.name + " tomorrow" : $"{next.name} in {n} days";
        }
    }

    // For other mods, found by reflection: the type "Wayfinder.Api" in this assembly (see Jarldom's Compat/API-CONVENTION.md).
    // Rules: bump Version when a method is added; never change a method's signature (add a new one).
    public static class Api
    {
        public const int Version = 2;

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

        // Version 2 ----
        // Feast days burnt into the primstav: the next within a fortnight shows under its forecast. A source (a mod's name) owns its days:
        // clear them before adding the new ones. day: the world's day number (EnvMan.GetDay()).
        public static void AddFeastDay(string source, int day, string name) { if (!string.IsNullOrEmpty(source) && !string.IsNullOrEmpty(name)) Feasts.Days.Add((source, day, name)); }
        public static void ClearFeastDays(string source) => Feasts.Days.RemoveAll(d => d.source == source);

        private static BeaconView Nearest(Vector3 at, float radius) =>
            BeaconView.Live.Where(b => b != null && Vector3.Distance(b.transform.position, at) <= radius).OrderBy(b => Vector3.Distance(b.transform.position, at)).FirstOrDefault();
    }
}
