using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Shieldwall
{
    // Messages between games. A siege runs on whichever game owns its Warstone (the player nearest it), so this works on a
    // dedicated server that does not have the mod: players send the horn and the horde's blows to that game, and it tells everyone.
    internal static class Net
    {
        private const string Call_="bob_sw_call_v1",Damage_="bob_sw_damage_v1",Say_="bob_sw_say_v1",Hello_="bob_sw_hello_v1",
            Horn_="bob_sw_horn_v1",Choose_="bob_sw_choose_v1",Tally_="bob_sw_tally_v1",Saga_="bob_sw_saga_v1";
        private static ZRoutedRpc _rpc;
        private static readonly Dictionary<string,object> Handlers=new Dictionary<string,object>();
        private static bool _hostAnswered;
        private static float _nextHello;

        internal static void Tick()
        {
            if(_rpc!=ZRoutedRpc.instance){Unregister();Register();_hostAnswered=false;_nextHello=0;}
            // Ask the host whether it has the mod (it answers in kind); until it does, our pieces are not built where it would delete them.
            if(_rpc!=null&&!_hostAnswered&&ZNet.instance!=null&&!ZNet.instance.IsServer()&&ZNet.instance.GetServerPeer()!=null&&Time.time>=_nextHello)
            {
                _nextHello=Time.time+5;
                _rpc.InvokeRoutedRPC(Hello_,Plugin.Version);
            }
        }
        // Whether the host keeps a piece of ours built here. A host's game deletes any object whose prefab it does not know once it
        // loads it: a player hosting without Shieldwall deletes a Warstone as soon as they come near, and a dedicated server
        // without it does the same around the middle of the world (the land it keeps loaded for itself).
        internal static bool HostKeeps(Vector3 at)
        {
            if(ZNet.instance==null||ZNet.instance.IsServer()||_hostAnswered)return true;
            ZNetPeer server=ZNet.instance.GetServerPeer();
            if(server==null)return true;
            bool playerHost=ZNet.instance.GetPlayerList().Any(p=>p.m_characterID.UserID==server.m_uid);
            return !playerHost&&Mathf.Max(Mathf.Abs(at.x),Mathf.Abs(at.z))>DedicatedArea;
        }
        internal const float DedicatedArea=400;

        internal static void Call(Warstone stone,Cause cause)
        {
            ZDO z=stone.Z;if(z==null||_rpc==null)return;
            if(z.GetOwner()==0)stone.View.ClaimOwnership(); // nobody near enough to own it yet: the caller is
            if(stone.View.IsOwner())Director.OnCall(stone,cause);
            else _rpc.InvokeRoutedRPC(z.GetOwner(),Call_,z.m_uid,(int)cause);
        }
        // The host turning a raid into a siege at a stone it may not have loaded: ask whoever owns the stone.
        internal static bool CallSaved(ZDO stone,Cause cause)
        {
            if(_rpc==null||stone==null||stone.GetOwner()==0)return false;
            Warstone loaded=Find(stone.m_uid);
            if(loaded!=null&&loaded.View.IsOwner()){Director.OnCall(loaded,cause);return true;}
            _rpc.InvokeRoutedRPC(stone.GetOwner(),Call_,stone.m_uid,(int)cause);
            return true;
        }
        // The horn sounded at the war council, with the boasts sworn.
        internal static void Horn(Warstone stone,int boasts)
        {
            ZDO z=stone.Z;if(z==null||_rpc==null)return;
            if(z.GetOwner()==0)stone.View.ClaimOwnership();
            if(stone.View.IsOwner())Director.OnCall(stone,Cause.Horn,boasts);
            else _rpc.InvokeRoutedRPC(z.GetOwner(),Horn_,z.m_uid,boasts);
        }
        // A boon chosen at the war council.
        internal static void Choose(Warstone stone,string boon,string who)
        {
            ZDO z=stone.Z;if(z==null||_rpc==null)return;
            if(z.GetOwner()==0)stone.View.ClaimOwnership();
            if(stone.View.IsOwner())Director.Choose(stone,boon,who);
            else _rpc.InvokeRoutedRPC(z.GetOwner(),Choose_,z.m_uid,boon,who);
        }
        // A raider slain (on the game that owned it): counted in the saga by the stone's owner.
        internal static void Tally(Vector3 stoneAt,long siege,string who)
        {
            Warstone stone=Warstone.Loaded.FirstOrDefault(w=>w!=null&&w.Z!=null&&Vector3.Distance(w.transform.position,stoneAt)<3);
            if(stone==null||_rpc==null)return;
            if(stone.View.IsOwner())Shieldwall.Saga.Count(stone,siege,who);
            else _rpc.InvokeRoutedRPC(stone.Z.GetOwner(),Tally_,stone.Z.m_uid,siege,who);
        }
        // The saga, for everyone near the stone.
        internal static void Saga(string topic,string text,Vector3 at,float radius)
        {
            if(_rpc==null)return;
            _rpc.InvokeRoutedRPC(ZRoutedRpc.Everybody,Saga_,topic,text,at,radius);
        }
        // Positive: the horde's blows. Negative: a hearth stave mending it.
        internal static void Damage(Warstone stone,float amount,Vector3 at)
        {
            ZDO z=stone.Z;if(z==null||_rpc==null)return;
            if(stone.View.IsOwner())Director.OnDamage(stone,amount,at);
            else _rpc.InvokeRoutedRPC(z.GetOwner(),Damage_,z.m_uid,amount,at);
        }
        // A line for everyone near a spot (radius 0: everyone), in the middle of the screen and in chat.
        internal static void Say(string text,Vector3 at,float radius)
        {
            if(_rpc==null)return;
            _rpc.InvokeRoutedRPC(ZRoutedRpc.Everybody,Say_,text,at,radius);
        }

        private static Warstone Find(ZDOID id)=>Warstone.Loaded.FirstOrDefault(w=>w!=null&&w.Z!=null&&w.Z.m_uid==id);
        private static void OnCall(long sender,ZDOID id,int cause)
        {
            Warstone stone=Find(id);
            if(stone!=null&&stone.View.IsOwner())Director.OnCall(stone,(Cause)cause);
        }
        private static void OnDamage(long sender,ZDOID id,float amount,Vector3 at)
        {
            Warstone stone=Find(id);
            if(stone!=null&&stone.View.IsOwner())Director.OnDamage(stone,amount,at);
        }
        private static void OnHorn(long sender,ZDOID id,int boasts)
        {
            Warstone stone=Find(id);
            if(stone!=null&&stone.View.IsOwner())Director.OnCall(stone,Cause.Horn,boasts);
        }
        private static void OnChoose(long sender,ZDOID id,string boon,string who)
        {
            Warstone stone=Find(id);
            if(stone!=null&&stone.View.IsOwner())Director.Choose(stone,boon,who);
        }
        private static void OnTally(long sender,ZDOID id,long siege,string who)
        {
            Warstone stone=Find(id);
            if(stone!=null&&stone.View.IsOwner())Shieldwall.Saga.Count(stone,siege,who);
        }
        private static void OnSaga(long sender,string topic,string text,Vector3 at,float radius)
        {
            Player me=Player.m_localPlayer;
            if(me==null||radius>0&&Vector3.Distance(me.transform.position,at)>radius)return;
            Shieldwall.Saga.Show(topic,text);
        }
        private static void OnHello(long sender,string version)
        {
            if(ZNet.instance!=null&&ZNet.instance.IsServer())_rpc?.InvokeRoutedRPC(sender,Hello_,Plugin.Version);
            else _hostAnswered=true;
        }
        private static void OnSay(long sender,string text,Vector3 at,float radius)
        {
            Player me=Player.m_localPlayer;
            if(me==null||radius>0&&Vector3.Distance(me.transform.position,at)>radius)return;
            me.Message(MessageHud.MessageType.Center,"<color=#FF9060>"+text+"</color>");
            if(Chat.instance!=null)Chat.instance.AddString("<color=#FF9060>Shieldwall</color>",text,Talker.Type.Normal);
        }

        private static void Register()
        {
            _rpc=ZRoutedRpc.instance;if(_rpc==null)return;
            _rpc.Register<ZDOID,int>(Call_,OnCall);
            _rpc.Register<ZDOID,float,Vector3>(Damage_,OnDamage);
            _rpc.Register<string,Vector3,float>(Say_,OnSay);
            _rpc.Register<string>(Hello_,OnHello);
            _rpc.Register<ZDOID,int>(Horn_,OnHorn);
            _rpc.Register<ZDOID,string,string>(Choose_,OnChoose);
            _rpc.Register<ZDOID,long,string>(Tally_,OnTally);
            _rpc.Register<string,string,Vector3,float>(Saga_,OnSaga);
            var table=AccessTools.Field(typeof(ZRoutedRpc),"m_functions").GetValue(_rpc) as IDictionary;
            foreach(string name in new[]{Call_,Damage_,Say_,Hello_,Horn_,Choose_,Tally_,Saga_})Handlers[name]=table?[name.GetStableHashCode()];
        }
        internal static void Unregister()
        {
            if(_rpc!=null&&AccessTools.Field(typeof(ZRoutedRpc),"m_functions").GetValue(_rpc) is IDictionary table)
                foreach(var entry in Handlers)
                    if(ReferenceEquals(table[entry.Key.GetStableHashCode()],entry.Value))table.Remove(entry.Key.GetStableHashCode()); // only our own handlers
            Handlers.Clear();_rpc=null;
        }
    }

    // The Warstone, sockets and upgrades refuse to be built where the host would delete them, before anything is spent.
    [HarmonyPatch(typeof(Player),nameof(Player.TryPlacePiece))]
    internal static class HostCheck
    {
        private static bool Prefix(Player __instance,Piece piece,GameObject ___m_placementGhost,ref bool __result)
        {
            if(piece==null||!Ours(piece.gameObject.name))return true;
            Vector3 at=___m_placementGhost!=null?___m_placementGhost.transform.position:__instance.transform.position;
            if(Net.HostKeeps(at))return true;
            __instance.Message(MessageHud.MessageType.Center,"The host needs Shieldwall (0.3.2 or newer), or its game deletes this");
            __result=false;
            return false;
        }
        private static bool Ours(string name)=>name==Stone.PrefabName||Pieces.All.Any(p=>p.name==name);
    }
}
