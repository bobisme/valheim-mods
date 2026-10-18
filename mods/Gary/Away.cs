using System.Collections.Generic;
using UnityEngine;

namespace Gary
{
    // Server only: a Gary whose player is offline leaves the world with them and comes back where he was when they return.
    // He is parked far outside the map instead of deleted, so his saved stash, nest, recovery and loot memory stay on his object.
    internal static class Away
    {
        internal const string Gone="bob_gary_away",Spot="bob_gary_away_pos",Facing="bob_gary_away_rot";
        // No zone out here is ever loaded, so the parked object is never created or simulated.
        private static readonly Vector3 Parking=new Vector3(-90000,0,-90000);
        private const float Grace=10;
        private static float _next;
        private static readonly Dictionary<long,float> Missing=new Dictionary<long,float>();
        private static readonly Dictionary<long,long> PeerPlayer=new Dictionary<long,long>();

        internal static void Tick()
        {
            if(Time.unscaledTime<_next)return;
            _next=Time.unscaledTime+2;
            if(ZNet.instance==null||!ZNet.instance.IsServer()||ZDOMan.instance==null||ZNetScene.instance==null)return;
            var online=Online();
            foreach(ZDOID id in ZDOExtraData.GetAllZDOIDsWithHash(ZDOExtraData.Type.Long,Companion.Master.GetStableHashCode()))
            {
                ZDO gary=ZDOMan.instance.GetZDO(id);
                if(gary==null||gary.GetPrefab()!="Greydwarf".GetStableHashCode())continue;
                long master=gary.GetLong(Companion.Master,0);if(master==0)continue;
                bool away=gary.GetBool(Gone,false);
                if(online.Contains(master)){Missing.Remove(master);if(away)Return(gary);continue;}
                if(away||!Plugin.Instance.LeaveWithOwner.Value)continue;
                // Brief gaps (joining, a slow handshake) should not send him away.
                if(!Missing.TryGetValue(master,out float since)){Missing[master]=Time.unscaledTime;continue;}
                if(Time.unscaledTime-since>=Grace){Missing.Remove(master);Leave(gary);}
            }
        }
        private static HashSet<long> Online()
        {
            var ids=new HashSet<long>();
            if(Player.m_localPlayer!=null)ids.Add(Player.m_localPlayer.GetPlayerID());
            var connected=new HashSet<long>();
            foreach(ZNetPeer peer in ZNet.instance.GetPeers())
            {
                if(peer==null)continue;connected.Add(peer.m_uid);
                // A player who died has no character until respawn; remember who the connection belongs to.
                long player=ZDOMan.instance.GetZDO(peer.m_characterID)?.GetLong(ZDOVars.s_playerID,0)??0;
                if(player!=0)PeerPlayer[peer.m_uid]=player;
            }
            foreach(long uid in new List<long>(PeerPlayer.Keys))if(!connected.Contains(uid))PeerPlayer.Remove(uid);
            foreach(long player in PeerPlayer.Values)ids.Add(player);
            return ids;
        }
        private static void Leave(ZDO gary)
        {
            GameObject go=ZNetScene.instance.FindInstance(gary.m_uid);
            Vector3 at=go!=null?go.transform.position:gary.GetPosition();
            Quaternion facing=go!=null?go.transform.rotation:gary.GetRotation();
            gary.SetOwner(ZNet.GetUID());
            // A deck link would keep pulling him back to the ship; he reboards on his own when he returns beside it.
            BoatRide.Forget(gary);
            gary.Set(Spot,at);gary.Set(Facing,facing);gary.Set(Gone,true);
            Move(gary,go,Parking,facing);
            ZLog.Log($"Gary: player {gary.GetLong(Companion.Master,0)} is offline; their Gary left the world from {at}.");
        }
        private static void Return(ZDO gary)
        {
            gary.SetOwner(ZNet.GetUID());
            Vector3 at=gary.GetVec3(Spot,Parking);
            gary.Set(Gone,false);
            if(at==Parking)return;
            Move(gary,ZNetScene.instance.FindInstance(gary.m_uid),at,gary.GetQuaternion(Facing,Quaternion.identity));
        }
        // Recall puts him beside his player itself; it only has to say he is back.
        internal static void Recalled(ZDO gary) => gary.Set(Gone,false);
        private static void Move(ZDO gary,GameObject go,Vector3 at,Quaternion facing)
        {
            gary.SetPosition(at);gary.SetRotation(facing);
            if(go==null)return;
            go.transform.SetPositionAndRotation(at,facing);
            Rigidbody body=go.GetComponent<Rigidbody>();if(body!=null){body.position=at;body.linearVelocity=Vector3.zero;}
        }
        internal static void Clear(){Missing.Clear();PeerPlayer.Clear();}
    }
}
