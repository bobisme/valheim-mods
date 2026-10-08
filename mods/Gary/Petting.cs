using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Gary
{
    internal static class Petting
    {
        private const string RequestName="bob_gary_pet_v1",AcceptName="bob_gary_pet_accept_v1";
        private static ZRoutedRpc Rpc;
        private static object RequestHandler,AcceptHandler;
        private static float NextRequest;
        private static readonly Dictionary<long,float> Requests=new Dictionary<long,float>();
        internal static void Register(ZRoutedRpc rpc)
        {
            Unregister();Rpc=rpc;if(rpc==null)return;
            rpc.Register<ZDOID>(RequestName,RequestOnServer);rpc.Register<ZDOID,ZDOID>(AcceptName,AcceptOnOwner);
            IDictionary table=(IDictionary)AccessTools.Field(typeof(ZRoutedRpc),"m_functions").GetValue(rpc);
            RequestHandler=table[RequestName.GetStableHashCode()];AcceptHandler=table[AcceptName.GetStableHashCode()];
        }
        internal static void Unregister()
        {
            if(Rpc!=null)
            {
                IDictionary table=(IDictionary)AccessTools.Field(typeof(ZRoutedRpc),"m_functions").GetValue(Rpc);
                foreach(var pair in new[]{new KeyValuePair<string,object>(RequestName,RequestHandler),new KeyValuePair<string,object>(AcceptName,AcceptHandler)})
                    if(ReferenceEquals(table[pair.Key.GetStableHashCode()],pair.Value))table.Remove(pair.Key.GetStableHashCode());
            }
            Rpc=null;RequestHandler=null;AcceptHandler=null;Requests.Clear();NextRequest=0;
        }
        internal static void Request(Player player,Character gary)
        {
            if(Rpc==null||Time.unscaledTime<NextRequest||player!=Player.m_localPlayer||Companion.Owner(gary)!=player)return;
            if(player.IsDead()||player.InAttack()||player.InDodge()||Vector3.Distance(player.transform.position,gary.transform.position)>5)return;
            NextRequest=Time.unscaledTime+2;
            Rpc.InvokeRoutedRPC(RequestName,gary.GetZDOID());Personality.Pat(player,gary.gameObject);
        }
        private static void RequestOnServer(long sender,ZDOID id)
        {
            if(ZNet.instance==null||!ZNet.instance.IsServer()||ZDOMan.instance==null)return;
            if(Requests.TryGetValue(sender,out float last)&&Time.unscaledTime-last<2)return;
            Requests[sender]=Time.unscaledTime;if(Requests.Count>64)Requests.Clear();
            ZDOID playerID=sender==ZNet.GetUID()?ZNet.instance.LocalPlayerCharacterID:ZNet.instance.GetPeer(sender)?.m_characterID??ZDOID.None;
            ZDO player=ZDOMan.instance.GetZDO(playerID),gary=ZDOMan.instance.GetZDO(id);
            if(player==null||gary==null||gary.GetPrefab()!="Greydwarf".GetStableHashCode()||gary.GetOwner()==0)return;
            if(!Policy.CanPet(player.GetLong(ZDOVars.s_playerID,0)!=0&&gary.GetLong(Companion.Master,0)==player.GetLong(ZDOVars.s_playerID,0),
                Vector3.Distance(player.GetPosition(),gary.GetPosition()),player.GetFloat(ZDOVars.s_health,0)>0&&!player.GetBool(ZDOVars.s_dead,false),
                gary.GetBool(Companion.Retreating,false)))return;
            Rpc.InvokeRoutedRPC(gary.GetOwner(),AcceptName,id,playerID);
        }
        private static void AcceptOnOwner(long sender,ZDOID id,ZDOID playerID)
        {
            if(ZNet.instance==null||ZNetScene.instance==null)return;
            bool server=ZNet.instance.IsServer()?sender==ZNet.GetUID():ZNet.instance.GetPeer(sender)?.m_server==true;
            if(!server)return;
            GameObject body=ZNetScene.instance.FindInstance(id),friend=ZNetScene.instance.FindInstance(playerID);
            Character c=body!=null?body.GetComponent<Character>():null;Player p=friend!=null?friend.GetComponent<Player>():null;
            if(c==null||p==null||!Companion.Is(c)||!c.GetComponent<ZNetView>().IsOwner())return;
            ZDO z=Companion.Data(c);
            if(!Policy.CanPet(Companion.Owner(c)==p,Vector3.Distance(c.transform.position,p.transform.position),!p.IsDead()&&p.GetHealth()>0,z.GetBool(Companion.Retreating,false))||
                (Companion.Now-z.GetLong("bob_gary_last_pet",0))/(double)TimeSpan.TicksPerSecond<3||Brain.ThreatFor(c,p)!=null)return;
            Companion.State st=Companion.Get(c);
            Activities.Cancel(st);
            if(Personality.Mood(st,Personality.Pet,"*happy forest noises*"))z.Set("bob_gary_last_pet",Companion.Now);
        }
    }
}
