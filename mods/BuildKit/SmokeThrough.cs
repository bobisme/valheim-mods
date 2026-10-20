using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace BuildKit
{
    // A ridge vent keeps the game's ridge beam for its support (Kit.RidgeColliders), but the beam lies across its smoke slot. Every
    // smoke ball the game makes ignores the beams of the vents near where it's made, so the smoke rises out through the slot as before.
    internal static class SmokeThrough
    {
        internal const string BeamName = "ridgevent beam";
        internal static readonly HashSet<Collider> Beams = new HashSet<Collider>();
        private const float Reach = 16f; // (smoke rises from a hearth to a ridge a few metres up; a vent beyond this it won't reach)

        internal static void Add(GameObject piece)
        {
            foreach (Collider c in piece.GetComponentsInChildren<Collider>(true)) if (c.name == BeamName) Beams.Add(c);
        }
        internal static void Remove(GameObject piece)
        {
            foreach (Collider c in piece.GetComponentsInChildren<Collider>(true)) if (c.name == BeamName) Beams.Remove(c);
        }
        internal static void Pass(Collider smoke)
        {
            if (smoke == null || Beams.Count == 0) return;
            Vector3 at = smoke.transform.position;
            Beams.RemoveWhere(b => b == null);
            foreach (Collider b in Beams)
                if ((b.transform.position - at).sqrMagnitude < Reach * Reach) Physics.IgnoreCollision(smoke, b);
        }
    }

    [HarmonyPatch(typeof(Smoke), "Awake")]
    internal static class SmokeThroughBeams
    {
        private static void Postfix(Smoke __instance) => SmokeThrough.Pass(__instance.GetComponent<Collider>());
    }
}
