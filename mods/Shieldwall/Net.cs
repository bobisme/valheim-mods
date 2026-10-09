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
        private const string Call_="bob_sw_call_v1",Damage_="bob_sw_damage_v1",Say_="bob_sw_say_v1",StaveHit_="bob_sw_stavehit_v1";
        private static ZRoutedRpc _rpc;
        private static readonly Dictionary<string,object> Handlers=new Dictionary<string,object>();

        internal static void Tick(){if(_rpc!=ZRoutedRpc.instance){Unregister();Register();}}

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

        internal static void StaveHit(Planted stave,float damage)
        {
            ZDO z=stave.View.GetZDO();if(z==null||_rpc==null)return;
            if(stave.View.IsOwner())stave.Struck(damage);
            else _rpc.InvokeRoutedRPC(z.GetOwner(),StaveHit_,z.m_uid,damage);
        }
        private static void OnStaveHit(long sender,ZDOID id,float damage)
        {
            Planted stave=Planted.Loaded.FirstOrDefault(p=>p!=null&&p.View.IsValid()&&p.View.GetZDO().m_uid==id);
            if(stave!=null)stave.Struck(damage);
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
            _rpc.Register<ZDOID,float>(StaveHit_,OnStaveHit);
            var table=AccessTools.Field(typeof(ZRoutedRpc),"m_functions").GetValue(_rpc) as IDictionary;
            foreach(string name in new[]{Call_,Damage_,Say_,StaveHit_})Handlers[name]=table?[name.GetStableHashCode()];
        }
        internal static void Unregister()
        {
            if(_rpc!=null&&AccessTools.Field(typeof(ZRoutedRpc),"m_functions").GetValue(_rpc) is IDictionary table)
                foreach(var entry in Handlers)
                    if(ReferenceEquals(table[entry.Key.GetStableHashCode()],entry.Value))table.Remove(entry.Key.GetStableHashCode()); // only our own handlers
            Handlers.Clear();_rpc=null;
        }
    }
}
