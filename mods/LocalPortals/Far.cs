using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace LocalPortals
{
    // Mirrors farther apart than the game keeps loaded around the player. The portals near the player are found from the
    // world's saved data, loaded or not (so a far partner is still known and linked); and while the player is near a mirror,
    // the ground and objects around its partner are kept loaded as well, so its view shows a living world and stepping
    // through lands on solid ground. Pieces out there count as fully supported (the game's rule outside the player's area),
    // so nothing falls down. Only on the host (or alone): a client is sent only what is near itself.
    internal static class Far
    {
        internal static readonly List<ZDO> Portals=new List<ZDO>();      // every portal of this kind in the world (as this game knows it)
        internal static readonly HashSet<Vector2s> Zones=new HashSet<Vector2s>(); // far zones kept loaded now
        private const float KeepRange=110;      // a mirror this close to the player has its partner's area kept loaded
        private const float ReadyRange=25;      // ...and this close, all of the area around the partner, ready to arrive in
        private static float _nextScan,_nextKeep;
        private static int _spawnedBefore;
        private static readonly Func<ZoneSystem,Vector2s,bool> Poke=AccessTools.MethodDelegate<Func<ZoneSystem,Vector2s,bool>>(AccessTools.Method(typeof(ZoneSystem),"PokeLocalZone"));
        internal static int Spawned,Reclaims,Reclaimed;            // zones this mod had loaded, this session

        // The world's portals of this kind, found a slice at a time (the game's own search, a few hundred sectors a frame), so
        // the cost does not grow with the link distance; the loaded ones are always in it at once.
        private static readonly List<ZDO> Sweep=new List<ZDO>(),Swept=new List<ZDO>();
        private static int _sweepAt;
        internal static void Scan(bool now=false)
        {
            if(ZDOMan.instance==null)return;
            if(ZDOMan.instance.GetAllZDOsWithPrefabIterative(PortalPrefab.Name,Sweep,ref _sweepAt))
            {
                Swept.Clear();Swept.AddRange(Sweep);Sweep.Clear();_sweepAt=0;
                now=true;
            }
            if(!now&&Time.time<_nextScan)return;
            _nextScan=Time.time+0.5f;
            Portals.Clear();
            foreach(ZDO zdo in Swept)if(zdo!=null&&zdo.IsValid())Portals.Add(zdo);
            foreach(LocalPortal p in LocalPortal.Live)
            {
                ZDO zdo=p!=null&&p.View!=null&&p.View.IsValid()?p.View.GetZDO():null;
                if(zdo!=null&&!Portals.Contains(zdo))Portals.Add(zdo);
            }
        }

        internal static void Tick(bool now=false)
        {
            Scan();
            if(!now&&Time.time<_nextKeep)return;
            _nextKeep=Time.time+0.2f;
            Zones.Clear();
            Player me=Player.m_localPlayer;
            if(me==null||ZNet.instance==null||!ZNet.instance.IsServer()||ZoneSystem.instance==null)return;
            Vector3 at=ZNet.instance.GetReferencePosition();
            float size=ZoneSystem.instance.m_zoneSize;
            foreach(LocalPortal p in LocalPortal.Live)
            {
                if(p==null||(p.transform.position-me.transform.position).sqrMagnitude>KeepRange*KeepRange)continue;
                ZDOID id=p.PartnerId();
                ZDO other=id.IsNone()?null:ZDOMan.instance.GetZDO(id);
                if(other==null)continue;
                Vector3 there=other.GetPosition();
                if(ZNetScene.InActiveArea(there,at))continue; // loaded by the game anyway
                Vector2s zone=ZoneSystem.GetZone(there);
                // about to step through: all the area the game would load around someone standing there, so nothing is left to
                // load (or to generate) when they arrive
                if(Plugin.Preload.Value&&(p.transform.position-me.transform.position).sqrMagnitude<ReadyRange*ReadyRange)
                {
                    int n=ZNet.instance.GetSyncedSimulationDistance().NearSimulationDistance;
                    for(int y=-n;y<=n;y++)
                        for(int x=-n;x<=n;x++)
                        {
                            var z=new Vector2s(zone.x+x,zone.y+y);
                            if(ZoneSystem.instance.ZonesWithinRadius(zone,z,n))Want(z,at);
                        }
                    continue;
                }
                // otherwise its zone, and the next ones over if it stands near their edge (its view and the ground just past it)
                Vector3 centre=ZoneSystem.GetZonePos(zone);
                float dx=there.x-centre.x,dz=there.z-centre.z,edge=size/2-24;
                int sx=dx>edge?1:dx<-edge?-1:0,sz=dz>edge?1:dz<-edge?-1:0;
                Want(zone,at);
                if(sx!=0)Want(new Vector2s(zone.x+sx,zone.y),at);
                if(sz!=0)Want(new Vector2s(zone.x,zone.y+sz),at);
                if(sx!=0&&sz!=0)Want(new Vector2s(zone.x+sx,zone.y+sz),at);
            }
            foreach(Vector2s zone in Zones)
                if(Poke(ZoneSystem.instance,zone)){Spawned++;break;} // (one new zone a tick, as the game does)
            _nextKeep=Time.time+(Spawned>_spawnedBefore?0.1f:0.2f);_spawnedBefore=Spawned;
        }
        // (Zones the game loads around the player are wanted too: past its smaller live area it lets go of what is in them.)
        private static void Want(Vector2s zone,Vector3 at)=>Zones.Add(zone);

        // The game makes and removes the objects around the player each moment: the far zones' objects are added to its list.
        private static readonly AccessTools.FieldRef<ZNetScene,List<ZDO>> NearObjects=AccessTools.FieldRefAccess<ZNetScene,List<ZDO>>("m_tempCurrentObjects");
        private static readonly AccessTools.FieldRef<ZNetScene,List<ZDO>> DistantObjects=AccessTools.FieldRefAccess<ZNetScene,List<ZDO>>("m_tempCurrentDistantObjects");
        private static readonly Action<ZNetScene,List<ZDO>,List<ZDO>> Create=AccessTools.MethodDelegate<Action<ZNetScene,List<ZDO>,List<ZDO>>>(AccessTools.Method(typeof(ZNetScene),"CreateObjects"));
        private static readonly Action<ZNetScene,List<ZDO>,List<ZDO>> Remove=AccessTools.MethodDelegate<Action<ZNetScene,List<ZDO>,List<ZDO>>>(AccessTools.Method(typeof(ZNetScene),"RemoveObjects"));
        private static readonly HashSet<ZDO> Have=new HashSet<ZDO>();
        private static readonly List<ZDO> Extra=new List<ZDO>();
        internal static bool CreateDestroy(ZNetScene scene)
        {
            if(Zones.Count==0||ZNet.instance==null||ZDOMan.instance==null)return true;
            List<ZDO> near=NearObjects(scene),distant=DistantObjects(scene);
            near.Clear();distant.Clear();
            ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(ZNet.instance.GetReferencePosition()),ZNet.instance.GetSyncedSimulationDistance(),near,distant);
            Extra.Clear();
            Vector2s mine=ZoneSystem.GetZone(ZNet.instance.GetReferencePosition());
            int n=ZNet.instance.GetSyncedSimulationDistance().NearSimulationDistance;
            foreach(Vector2s zone in Zones)
                if(!ZoneSystem.instance.ZonesWithinRadius(mine,zone,n))ZDOMan.instance.FindSectorObjects(zone,new SimulationDistance(0,0),Extra); // (the rest are in the game's own list)
            if(Extra.Count>0)
            {
                Have.Clear();
                foreach(ZDO z in near)Have.Add(z);
                foreach(ZDO z in Extra)if(Have.Add(z))near.Add(z);
                Have.Clear();Extra.Clear();
            }
            Create(scene,near,distant);
            Remove(scene,near,distant);
            return false;
        }

        // Every two seconds the game lets go of whatever it owns near the player but outside the area it keeps alive (which
        // takes in a far mirror's area at some distances): out there nothing would move. Taken back for the far zones kept loaded.
        private static readonly AccessTools.FieldRef<ZDOMan,float> ReleaseTimer=AccessTools.FieldRefAccess<ZDOMan,float>("m_releaseZDOTimer");
        private static readonly List<ZDO> Mine=new List<ZDO>();
        internal static void Reclaim(ZDOMan man)
        {
            if(Zones.Count==0||ReleaseTimer(man)!=0f||ZNet.instance==null||!ZNet.instance.IsServer())return; // (only right after the game's release)
            long me=ZDOMan.GetSessionID();
            Mine.Clear();
            foreach(Vector2s zone in Zones)man.FindSectorObjects(zone,new SimulationDistance(0,0),Mine);
            Reclaims++;
            foreach(ZDO zdo in Mine)if(zdo.Persistent&&!zdo.HasOwner()){zdo.SetOwner(me);Reclaimed++;}
            Mine.Clear();
        }

        internal static void Stop(){Zones.Clear();Portals.Clear();Sweep.Clear();Swept.Clear();_sweepAt=0;}
    }

    [HarmonyPatch(typeof(ZDOMan),"ReleaseZDOS")]
    internal static class KeepFarOwned
    {
        private static void Postfix(ZDOMan __instance)=>Far.Reclaim(__instance);
    }
    [HarmonyPatch(typeof(ZNetScene),"CreateDestroyObjects")]
    internal static class KeepFarLoaded
    {
        private static bool Prefix(ZNetScene __instance)=>Far.CreateDestroy(__instance);
    }
}
