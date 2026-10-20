using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Shipyard
{
    // The naust: a boat shed at the shore, open to the sea at one end. A ship moored inside is out of the weather and off the rocks: whatever
    // harm comes to her with nobody behind it (bumping the shore or the dock as she rides, the sea slamming her) passes her by. Someone's
    // blow still lands. (The game wears a ship no other way while she's moored.)
    internal sealed class Naust : MonoBehaviour
    {
        internal static readonly List<Naust> All = new List<Naust>();
        // (Models/NAUST.txt: the shed's inside, in its own frame: between the mooring rails, from below the water up under the roof, land-end
        // frame to sea-end frame)
        internal static Vector3 InsideCentre = new Vector3(0, 1.25f, 0), InsideSize = new Vector3(6.4f, 6.5f, 21.5f);
        private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        private void OnDisable() => All.Remove(this);

        internal bool Holds(Vector3 world)
        {
            Vector3 p = transform.InverseTransformPoint(world) - InsideCentre;
            return Mathf.Abs(p.x) <= InsideSize.x / 2 && Mathf.Abs(p.y) <= InsideSize.y / 2 && Mathf.Abs(p.z) <= InsideSize.z / 2;
        }
        internal static Naust Sheltering(Vector3 world) => All.FirstOrDefault(n => n != null && n.Holds(world));
    }

    [HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Damage))]
    internal static class NaustShelter
    {
        private static bool Prefix(WearNTear __instance, HitData hit)
        {
            if (hit == null || !hit.m_attacker.IsNone() || __instance.GetComponent<Ship>() == null) return true;
            return Naust.Sheltering(__instance.transform.position) == null;
        }
    }
}
