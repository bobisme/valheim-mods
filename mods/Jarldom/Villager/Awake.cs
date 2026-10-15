using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Jarldom
{
    // Villagers kept awake across the whole of a game's loaded ground, not only the inner part the game itself runs. The game loads
    // ground and buildings two zones out but runs things only about one and a half (the rest wait unowned): a colony spread over more
    // than that (a longhouse 100 m from the hall) had everyone who walked over to it stand frozen there until a player came by. Its own
    // ownership pass, as the game's, but a villager anywhere in a game's loaded ground is that game's to run, and is only taken over
    // by another game when it's out of the first one's loaded ground.
    internal static class KeepAwake
    {
        private static readonly int Villager = VillagerPrefab.Name.GetStableHashCode();
        private static readonly List<ZDO> Near = new List<ZDO>();

        [HarmonyPatch(typeof(ZDOMan), "ReleaseNearbyZDOS")]
        private static class Release
        {
            private static bool Prefix(ZDOMan __instance, Vector3 refPosition, long uid)
            {
                if (ZNet.instance == null || ZoneSystem.instance == null) return true;
                Vector2s zone = ZoneSystem.GetZone(refPosition);
                SimulationDistance sim = ZNet.instance.GetSyncedSimulationDistance();
                Near.Clear();
                __instance.FindSectorObjects(zone, new SimulationDistance(sim.NearSimulationDistance, 0, sim.IsClassic), Near);
                foreach (ZDO zdo in Near)
                {
                    if (!zdo.Persistent) continue;
                    Vector3 p = zdo.GetPosition();
                    if (zdo.GetPrefab() == Villager)
                    {
                        if (zdo.GetOwner() != uid && (!zdo.HasOwner() || !Loaded(p, zdo.GetOwner(), sim))) zdo.SetOwner(uid);
                        continue;
                    }
                    // (everything else, the game's own way)
                    if (zdo.GetOwner() == uid) { if (!ZNetScene.InActiveArea(p, zone)) zdo.SetOwner(0L); }
                    else if ((!zdo.HasOwner() || !Active(p, zdo.GetOwner())) && ZNetScene.InActiveArea(p, zone)) zdo.SetOwner(uid);
                }
                return false;
            }
        }
        private static Vector3? RefOf(long uid) => uid == ZDOMan.GetSessionID() ? ZNet.instance.GetReferencePosition() : ZNet.instance.GetPeer(uid)?.GetRefPos();
        private static bool Active(Vector3 p, long uid) => RefOf(uid) is Vector3 r && ZNetScene.InActiveArea(p, r);
        // In that game's loaded ground (the zones the game loads round its player, as it finds them).
        private static bool Loaded(Vector3 p, long uid, SimulationDistance sim)
        {
            if (!(RefOf(uid) is Vector3 r)) return false;
            Vector2s a = ZoneSystem.GetZone(r), b = ZoneSystem.GetZone(p);
            int n = sim.NearSimulationDistance;
            if (Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y)) > n) return false;
            return sim.IsClassic || ZoneSystem.instance.ZonesWithinRadius(a, b, n);
        }
    }
}
