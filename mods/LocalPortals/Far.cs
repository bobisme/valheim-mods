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
    // so nothing falls down. A joined player is sent only what is near them: the host works out each one's far side as its
    // own, sends them its objects, and gives them what nobody there is looking after, so it moves for them.
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
        private static int _sweepAt; private static float _sweepRest;
        private static bool _sweepWild;
        internal static void Scan(bool now=false)
        {
            if(ZDOMan.instance==null)return;
            if(Time.time>=_sweepRest&&ZDOMan.instance.GetAllZDOsWithPrefabIterative(_sweepWild?PortalPrefab.WildName:PortalPrefab.Name,Sweep,ref _sweepAt))
            {
                _sweepAt=0;
                if(!_sweepWild)_sweepWild=true; // the built ones done: now the wild ones, into the same list
                else{_sweepWild=false;Swept.Clear();Swept.AddRange(Sweep);Sweep.Clear();now=true;_sweepRest=Time.time+3f;} // (a rest between sweeps: ~1 ms a frame otherwise, always)
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
            TickPeers();
            if(!now&&Time.time<_nextKeep)return;
            _nextKeep=Time.time+0.2f;
            Zones.Clear();
            Player me=Player.m_localPlayer;
            if(me==null||ZNet.instance==null||ZoneSystem.instance==null||ZDOMan.instance==null)return;
            Vector3 at=ZNet.instance.GetReferencePosition();
            foreach(LocalPortal p in LocalPortal.Live)
            {
                if(p==null||(p.transform.position-me.transform.position).sqrMagnitude>KeepRange*KeepRange)continue;
                ZDOID id=p.PartnerId();
                ZDO other=id.IsNone()?null:ZDOMan.instance.GetZDO(id);
                if(other==null)continue;
                Vector3 there=other.GetPosition();
                if(ZNetScene.InActiveArea(there,at))continue; // loaded by the game anyway
                AreaAround(there,Plugin.Preload.Value&&(p.transform.position-me.transform.position).sqrMagnitude<ReadyRange*ReadyRange,Zones);
            }
            foreach(Vector2s zone in Zones)
                if(Poke(ZoneSystem.instance,zone)){Spawned++;break;} // (one new zone a tick, as the game does; on a client, its ground only)
            _nextKeep=Time.time+(Spawned>_spawnedBefore?0.1f:0.2f);_spawnedBefore=Spawned;
        }
        // The zones kept round a far mirror. About to step through (`ready`): all the area the game would load around someone
        // standing there, so nothing is left to load (or to generate) when they arrive. Otherwise its zone, and the next ones
        // over if it stands near their edge (its view and the ground just past it).
        // (Zones the game loads around the player are wanted too: past its smaller live area it lets go of what is in them.)
        private static void AreaAround(Vector3 there,bool ready,HashSet<Vector2s> into)
        {
            Vector2s zone=ZoneSystem.GetZone(there);
            if(ready)
            {
                int n=ZNet.instance.GetSyncedSimulationDistance().NearSimulationDistance;
                for(int y=-n;y<=n;y++)
                    for(int x=-n;x<=n;x++)
                    {
                        var z=new Vector2s(zone.x+x,zone.y+y);
                        if(ZoneSystem.instance.ZonesWithinRadius(zone,z,n))into.Add(z);
                    }
                return;
            }
            float size=ZoneSystem.instance.m_zoneSize;
            Vector3 centre=ZoneSystem.GetZonePos(zone);
            float dx=there.x-centre.x,dz=there.z-centre.z,edge=size/2-24;
            int sx=dx>edge?1:dx<-edge?-1:0,sz=dz>edge?1:dz<-edge?-1:0;
            into.Add(zone);
            if(sx!=0)into.Add(new Vector2s(zone.x+sx,zone.y));
            if(sz!=0)into.Add(new Vector2s(zone.x,zone.y+sz));
            if(sx!=0&&sz!=0)into.Add(new Vector2s(zone.x+sx,zone.y+sz));
        }

        // Each joined player's far side, as the host works it out from where they are (they report that every two seconds,
        // and straight after stepping through: Announce): the zones round the partners of the mirrors near them, and the
        // mirrors within linking range (so theirs link as the host's do).
        internal sealed class PeerArea
        {
            internal readonly HashSet<Vector2s> Zones=new HashSet<Vector2s>();
            internal readonly Dictionary<Vector2s,(Vector3 there,float mirror)> Through=new Dictionary<Vector2s,(Vector3,float)>(); // per far zone: its mirror's partner, and how far the player is from the mirror
            internal readonly List<ZDO> Portals=new List<ZDO>();
            internal Vector3 At;
        }
        internal static readonly Dictionary<long,PeerArea> Peers=new Dictionary<long,PeerArea>();
        private static float _nextPeers;
        internal static int PeerGenerated;
        private static readonly Func<ZoneSystem,Vector3,bool> Generate=AccessTools.MethodDelegate<Func<ZoneSystem,Vector3,bool>>(AccessTools.Method(typeof(ZoneSystem),"CreateGhostZones"));
        private static readonly HashSet<Vector2s> Some=new HashSet<Vector2s>();
        private static readonly List<long> Gone=new List<long>();
        private static void TickPeers()
        {
            if(ZNet.instance==null||!ZNet.instance.IsServer()||ZDOMan.instance==null||ZoneSystem.instance==null){if(Peers.Count>0)Peers.Clear();return;}
            if(Time.time<_nextPeers)return;
            _nextPeers=Time.time+0.5f;
            List<ZNetPeer> peers=ZNet.instance.GetPeers();
            Gone.Clear();
            foreach(long uid in Peers.Keys)if(!peers.Exists(x=>x!=null&&x.m_uid==uid))Gone.Add(uid);
            foreach(long uid in Gone)Peers.Remove(uid);
            bool generated=false;
            foreach(ZNetPeer peer in peers)
            {
                if(peer==null||!peer.IsReady())continue;
                if(!Peers.TryGetValue(peer.m_uid,out PeerArea area))Peers[peer.m_uid]=area=new PeerArea();
                area.Zones.Clear();area.Through.Clear();area.Portals.Clear();
                Vector3 at=area.At=peer.GetRefPos();
                foreach(ZDO zdo in Portals)
                {
                    if(zdo==null||!zdo.IsValid())continue;
                    float d=Vector3.Distance(zdo.GetPosition(),at);
                    if(d<=Policy.LinkRange+KeepRange)area.Portals.Add(zdo);
                    if(d>KeepRange)continue;
                    ZDOID id=LocalPortal.PartnerOf(zdo,out _);
                    ZDO other=id.IsNone()?null:ZDOMan.instance.GetZDO(id);
                    if(other==null)continue;
                    Vector3 there=other.GetPosition();
                    if(ZNetScene.InActiveArea(there,at))continue;
                    Some.Clear();
                    AreaAround(there,Plugin.Preload.Value&&d<ReadyRange,Some);
                    foreach(Vector2s z in Some)
                    {
                        area.Zones.Add(z);
                        if(!area.Through.TryGetValue(z,out var known)||known.mirror>d)area.Through[z]=(there,d);
                    }
                    if(!generated&&Generate(ZoneSystem.instance,there)){generated=true;PeerGenerated++;} // (never visited: made now, a zone at a time)
                }
            }
        }

        // The host sends a joined player the objects near them: those of their far zones and the mirrors in linking range are
        // added to what it finds there, and sorted by how far they are through the mirror, not across the world.
        private static readonly System.Reflection.FieldInfo PeerOf=AccessTools.Field(AccessTools.Inner(typeof(ZDOMan),"ZDOPeer"),"m_peer");
        private static PeerArea _syncing;
        private static Vector2s _syncZone;
        private static int _syncNear,_syncTotal;
        private static bool _adding;
        internal static int SentExtra;
        internal static void SyncFor(object peer)
        {
            _syncing=null;
            if(Peers.Count==0||peer==null||PeerOf==null)return;
            ZNetPeer net=PeerOf.GetValue(peer) as ZNetPeer;
            if(net==null||!Peers.TryGetValue(net.m_uid,out PeerArea area)||area.Zones.Count==0&&area.Portals.Count==0)return;
            _syncing=area;
            _syncZone=ZoneSystem.GetZone(net.GetRefPos());
            _syncNear=net.m_simulationDistance.NearSimulationDistance;
            _syncTotal=net.m_simulationDistance.TotalSimulationDistance;
        }
        internal static void SyncDone()=>_syncing=null;
        private static readonly List<ZDO> Found=new List<ZDO>();
        internal static void AddPeerObjects(ZDOMan man,Vector2s sector,List<ZDO> objects,List<ZDO> distant)
        {
            PeerArea area=_syncing;
            if(area==null||_adding||distant==null||sector.x!=_syncZone.x||sector.y!=_syncZone.y)return;
            _syncing=null; // (once: only the search for the player's own area)
            _adding=true;
            try
            {
                ZoneSystem zs=ZoneSystem.instance;
                foreach(Vector2s zone in area.Zones)
                {
                    if(zs.ZonesWithinRadius(sector,zone,_syncNear))continue; // (in the game's own list)
                    bool ring=zs.ZonesWithinRadius(sector,zone,_syncTotal,ghostZone:true);
                    Found.Clear();
                    man.FindSectorObjects(zone,new SimulationDistance(0,0),Found);
                    foreach(ZDO zdo in Found)if(!(ring&&zdo.Distant)){objects.Add(zdo);SentExtra++;} // (a distant one there is in the game's list of those)
                }
                foreach(ZDO zdo in area.Portals)
                {
                    Vector2s zone=ZoneSystem.GetZone(zdo.GetPosition());
                    if(zs.ZonesWithinRadius(sector,zone,_syncNear)||area.Zones.Contains(zone))continue;
                    if(zdo.Distant&&zs.ZonesWithinRadius(sector,zone,_syncTotal,ghostZone:true))continue;
                    objects.Add(zdo);
                }
            }
            finally{_adding=false;Found.Clear();}
            _sorting=area;
        }
        private static PeerArea _sorting;
        private static readonly Comparison<ZDO> SendOrder=AccessTools.MethodDelegate<Comparison<ZDO>>(AccessTools.Method(typeof(ZDOMan),"ServerSendCompare"));
        internal static void Reorder(List<ZDO> objects,Vector3 refPos)
        {
            PeerArea area=_sorting;
            _sorting=null;
            if(area==null||area.Through.Count==0||SendOrder==null)return;
            bool changed=false;
            foreach(ZDO zdo in objects)
            {
                Vector3 at=zdo.GetPosition();
                if(!area.Through.TryGetValue(ZoneSystem.GetZone(at),out var through))continue;
                zdo.m_tempSortValue+=through.mirror+Vector3.Distance(at,through.there)-Vector3.Distance(at,refPos);
                changed=true;
            }
            if(changed)objects.Sort(SendOrder);
        }

        // A client that has just stepped through tells the host where it is now (the game tells it every two seconds).
        private static readonly System.Reflection.MethodInfo SendWhere=AccessTools.Method(typeof(ZNet),"SendServerSyncPlayerData");
        internal static void Announce()
        {
            if(ZNet.instance==null||ZNet.instance.IsServer()||SendWhere==null)return;
            ZNetPeer server=ZNet.instance.GetServerPeer();
            if(server==null||!server.IsReady())return;
            try{SendWhere.Invoke(ZNet.instance,new object[]{server});}catch(Exception e){Plugin.Log("Could not tell the host where we went: "+e.Message);}
        }

        // The game makes and removes the objects around the player each moment: the far zones' objects are added to its list.
        private static readonly AccessTools.FieldRef<ZNetScene,List<ZDO>> NearObjects=AccessTools.FieldRefAccess<ZNetScene,List<ZDO>>("m_tempCurrentObjects");
        private static readonly AccessTools.FieldRef<ZNetScene,List<ZDO>> DistantObjects=AccessTools.FieldRefAccess<ZNetScene,List<ZDO>>("m_tempCurrentDistantObjects");
        private static readonly Action<ZNetScene,List<ZDO>,List<ZDO>> Create=AccessTools.MethodDelegate<Action<ZNetScene,List<ZDO>,List<ZDO>>>(AccessTools.Method(typeof(ZNetScene),"CreateObjects"));
        private static readonly Action<ZNetScene,List<ZDO>,List<ZDO>> Remove=AccessTools.MethodDelegate<Action<ZNetScene,List<ZDO>,List<ZDO>>>(AccessTools.Method(typeof(ZNetScene),"RemoveObjects"));
        private static readonly Func<ZNetScene,ZDO,GameObject> CreateOne=AccessTools.MethodDelegate<Func<ZNetScene,ZDO,GameObject>>(AccessTools.Method(typeof(ZNetScene),"CreateObject",new[]{typeof(ZDO)}));
        private const int FarPerFrame=6;
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
            Create(scene,near,distant); // (the game's own, at its own pace)
            if(Extra.Count>0)
            {
                // The far side made a few a frame: in the game's list it raised how many the game makes a frame (1% of the list) and
                // made the far side all at once, up to 30 ms. Then counted as near, so the game doesn't take it away again.
                int made=0;
                foreach(ZDO z in Extra)
                {
                    if(made>=FarPerFrame)break;
                    if(z.Created||!ZoneSystem.instance.IsZoneReadyForType(z.GetSector(),z.Type))continue;
                    if(CreateOne(scene,z)!=null)made++;
                }
                Have.Clear();
                foreach(ZDO z in near)Have.Add(z);
                foreach(ZDO z in Extra)if(Have.Add(z))near.Add(z);
                Have.Clear();Extra.Clear();
            }
            Remove(scene,near,distant);
            return false;
        }

        // Every two seconds the game lets go of whatever it owns near the player but outside the area it keeps alive (which
        // takes in a far mirror's area at some distances): out there nothing would move. Taken back for the far zones kept loaded.
        private static readonly AccessTools.FieldRef<ZDOMan,float> ReleaseTimer=AccessTools.FieldRefAccess<ZDOMan,float>("m_releaseZDOTimer");
        private static readonly List<ZDO> Mine=new List<ZDO>();
        internal static void Reclaim(ZDOMan man)
        {
            if(Zones.Count==0&&Peers.Count==0||ReleaseTimer(man)!=0f||ZNet.instance==null||!ZNet.instance.IsServer())return; // (only right after the game's release)
            long me=ZDOMan.GetSessionID();
            Mine.Clear();
            foreach(Vector2s zone in Zones)man.FindSectorObjects(zone,new SimulationDistance(0,0),Mine);
            Reclaims++;
            foreach(ZDO zdo in Mine)if(zdo.Persistent&&!zdo.HasOwner()){zdo.SetOwner(me);Reclaimed++;}
            // a joined player's far side: theirs, unless someone is there (the host's own far side comes first); and taken from
            // whoever owned it and is not there any more (or nothing out there would move for them)
            foreach(var pair in Peers)
                foreach(Vector2s zone in pair.Value.Zones)
                {
                    if(Zones.Contains(zone))continue;
                    Mine.Clear();
                    man.FindSectorObjects(zone,new SimulationDistance(0,0),Mine);
                    foreach(ZDO zdo in Mine)
                    {
                        if(!zdo.Persistent||zdo.GetOwner()==pair.Key)continue;
                        if(zdo.HasOwner()&&LooksAfter(zdo.GetOwner(),zdo.GetPosition(),zone))continue;
                        zdo.SetOwner(pair.Key);Reclaimed++;
                    }
                }
            Mine.Clear();
        }
        // Not let go in the first place: a far zone in the ring the game releases (beyond the area it keeps alive, inside the one it
        // searches) was let go and taken straight back every two seconds, and each round trip resent every object out there to
        // every player. What a far side keeps owned is kept (Reclaim still takes back what nobody owns).
        private static long _releasing;
        internal static void Releasing(long uid)=>_releasing=Zones.Count==0&&Peers.Count==0?0:uid;
        internal static bool KeepOwner(ZDO zdo,long uid)
        {
            if(uid!=0||_releasing==0||zdo.GetOwner()!=_releasing||!zdo.Persistent)return false;
            Vector2s zone=ZoneSystem.GetZone(zdo.GetPosition());
            if(_releasing==ZDOMan.GetSessionID())return Zones.Contains(zone);
            return !Zones.Contains(zone)&&Peers.TryGetValue(_releasing,out PeerArea area)&&area.Zones.Contains(zone);
        }
        // Someone is there to move it: in their live area, or it is in their far side.
        private static bool LooksAfter(long owner,Vector3 at,Vector2s zone)
        {
            if(owner==ZDOMan.GetSessionID())return ZNetScene.InActiveArea(at,ZNet.instance.GetReferencePosition())||Zones.Contains(zone);
            ZNetPeer peer=ZNet.instance.GetPeer(owner);
            if(peer==null)return false;
            return ZNetScene.InActiveArea(at,peer.GetRefPos())||Peers.TryGetValue(owner,out PeerArea area)&&area.Zones.Contains(zone);
        }

        internal static void Stop(){Zones.Clear();Portals.Clear();Sweep.Clear();Swept.Clear();_sweepAt=0;_sweepWild=false;Peers.Clear();_syncing=_sorting=null;}
    }

    [HarmonyPatch(typeof(ZDOMan),"CreateSyncList")]
    internal static class SendFarToPeer
    {
        private static void Prefix(object peer)=>Far.SyncFor(peer);
        private static void Finalizer()=>Far.SyncDone();
    }
    [HarmonyPatch(typeof(ZDOMan),nameof(ZDOMan.FindSectorObjects))]
    internal static class AddFarForPeer
    {
        private static void Postfix(ZDOMan __instance,Vector2s sector,List<ZDO> sectorObjects,List<ZDO> distantSectorObjects)=>Far.AddPeerObjects(__instance,sector,sectorObjects,distantSectorObjects);
    }
    [HarmonyPatch(typeof(ZDOMan),"ServerSortSendZDOS")]
    internal static class SendFarInOrder
    {
        private static void Postfix(List<ZDO> objects,Vector3 refPos)=>Far.Reorder(objects,refPos);
    }
    [HarmonyPatch(typeof(ZDOMan),"ReleaseZDOS")]
    internal static class KeepFarOwned
    {
        private static void Postfix(ZDOMan __instance)=>Far.Reclaim(__instance);
    }
    [HarmonyPatch(typeof(ZDOMan),"ReleaseNearbyZDOS")]
    internal static class KeepFarUnreleased
    {
        private static void Prefix(long uid)=>Far.Releasing(uid);
        private static void Finalizer()=>Far.Releasing(0);
    }
    [HarmonyPatch(typeof(ZDO),nameof(ZDO.SetOwner))]
    internal static class KeepFarOwner
    {
        private static bool Prefix(ZDO __instance,long uid)=>!Far.KeepOwner(__instance,uid);
    }
    [HarmonyPatch(typeof(ZNetScene),"CreateDestroyObjects")]
    internal static class KeepFarLoaded
    {
        private static bool Prefix(ZNetScene __instance)=>Far.CreateDestroy(__instance);
    }
}
