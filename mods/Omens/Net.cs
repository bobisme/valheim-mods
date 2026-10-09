using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Omens
{
    // Players report what they saw or did; the host decides and tells everyone.
    internal static class Net
    {
        private const string Seen="bob_omens_seen_v1",Respond_="bob_omens_respond_v1",Show="bob_omens_show_v1",Resolved="bob_omens_resolved_v1",Bless="bob_omens_bless_v1",Burn="bob_omens_burn_v1",Moon="bob_omens_bloodmoon_v1",
            Favour_="bob_omens_favour_v1",Lead_="bob_omens_lead_v1",Mark_="bob_omens_mark_v1",Aurora_="bob_omens_aurora_v1";
        private static ZRoutedRpc _rpc;
        private static readonly Dictionary<string,object> Handlers=new Dictionary<string,object>();
        private static readonly HashSet<long> Reported=new HashSet<long>();
        private static readonly Dictionary<long,Minimap.PinData> Pins=new Dictionary<long,Minimap.PinData>();
        private static readonly List<Minimap.PinData> Marks=new List<Minimap.PinData>();
        private static readonly System.Reflection.MethodInfo Explore=AccessTools.Method(typeof(Minimap),"Explore",new[]{typeof(Vector3),typeof(float)});
        private static float _nextLook;

        internal static void Tick()
        {
            if(_rpc!=ZRoutedRpc.instance){Unregister();Register();}
            Player me=Player.m_localPlayer;
            if(_rpc==null||me==null||me.IsDead()||Time.time<_nextLook)return;
            _nextLook=Time.time+0.5f;
            foreach(OmenSign sign in OmenSign.Loaded)
            {
                if(sign==null||sign.Id==0||Reported.Contains(sign.Id))continue;
                if(Vector3.Distance(me.transform.position,sign.transform.position)>sign.Omen.SeenFrom)continue; // ravens are seen from afar, overhead
                Reported.Add(sign.Id);
                _rpc.InvokeRoutedRPC(Seen,sign.Id);
            }
        }
        internal static void Respond(long id){_rpc?.InvokeRoutedRPC(Respond_,id);}

        // ---- host → everyone ----
        internal static void Tell(string text,Vector3 pos,float centerRadius,long pinId)=>_rpc?.InvokeRoutedRPC(ZRoutedRpc.Everybody,Show,text,pos,centerRadius,pinId);
        internal static void Resolve(long id)=>_rpc?.InvokeRoutedRPC(ZRoutedRpc.Everybody,Resolved,id);
        internal static void Blessing(Vector3 pos,float radius)=>_rpc?.InvokeRoutedRPC(ZRoutedRpc.Everybody,Bless,pos,radius);
        internal static void BloodMoon(bool active,bool softened)=>_rpc?.InvokeRoutedRPC(ZRoutedRpc.Everybody,Moon,active,softened);
        internal static void Responded(long id)=>_rpc?.InvokeRoutedRPC(ZRoutedRpc.Everybody,Burn,id);
        internal static void Favour(Vector3 pos,float radius)=>_rpc?.InvokeRoutedRPC(ZRoutedRpc.Everybody,Favour_,pos,radius);
        internal static void Lead(Vector3 from,Vector3 to)=>_rpc?.InvokeRoutedRPC(ZRoutedRpc.Everybody,Lead_,from,to);
        internal static void Aurora(bool active)=>_rpc?.InvokeRoutedRPC(ZRoutedRpc.Everybody,Aurora_,active);
        internal static void Mark(Vector3 pos,string label)=>_rpc?.InvokeRoutedRPC(ZRoutedRpc.Everybody,Mark_,pos,label);

        private static void OnShow(long sender,string text,Vector3 pos,float centerRadius,long pinId)
        {
            if(!FromHost(sender))return;
            Player me=Player.m_localPlayer;
            if(Chat.instance!=null)Chat.instance.AddString("<color=#B48CFF>Omen</color>",text,Talker.Type.Normal);
            // Radius 0: everyone sees it on screen; below 0: chat only.
            if(me!=null&&(centerRadius==0||(centerRadius>0&&Vector3.Distance(me.transform.position,pos)<=centerRadius)))
                me.Message(MessageHud.MessageType.Center,"<color=#B48CFF>"+text+"</color>");
            if(pinId!=0&&Minimap.instance!=null&&!Pins.ContainsKey(pinId))
                Pins[pinId]=Minimap.instance.AddPin(pos,Minimap.PinType.Icon3,"Omen",false,false);
        }
        private static void OnResolved(long sender,long id)
        {
            if(!FromHost(sender))return;
            if(Pins.TryGetValue(id,out Minimap.PinData pin)&&Minimap.instance!=null)Minimap.instance.RemovePin(pin);
            Pins.Remove(id);
        }
        private static void OnBless(long sender,Vector3 pos,float radius)
        {
            Player me=Player.m_localPlayer;
            if(!FromHost(sender)||me==null||Vector3.Distance(me.transform.position,pos)>radius)return;
            me.GetSEMan().AddStatusEffect(SEMan.s_statusEffectRested,true);
            if(Minimap.instance!=null&&Explore!=null)Explore.Invoke(Minimap.instance,new object[]{pos,250f});
        }
        // Each game shows it on its own copy of the sign: ragdolls settle a little differently on every machine.
        private static void OnResponded(long sender,long id)
        {
            if(!FromHost(sender))return;
            OmenSign sign=OmenSign.Loaded.FirstOrDefault(s=>s!=null&&s.Id==id);
            if(sign!=null)sign.Responded();
        }
        private static void OnFavour(long sender,Vector3 pos,float radius)
        {
            Player me=Player.m_localPlayer;
            if(FromHost(sender)&&me!=null&&Vector3.Distance(me.transform.position,pos)<=radius)Omens.Favour.Give(me);
        }
        // A light drifts from the sign to what it leads to, for players near enough to follow it.
        private static void OnLead(long sender,Vector3 from,Vector3 to)
        {
            Player me=Player.m_localPlayer;
            if(FromHost(sender)&&me!=null&&Vector3.Distance(me.transform.position,from)<=120)Looks.Guide(from,to);
        }
        private static void OnMark(long sender,Vector3 pos,string label)
        {
            if(FromHost(sender)&&Minimap.instance!=null)Marks.Add(Minimap.instance.AddPin(pos,Minimap.PinType.Icon3,label,false,false));
        }
        private static void OnAurora(long sender,bool active){if(FromHost(sender))Omens.Aurora.Heard(active);}
        private static void OnMoon(long sender,bool active,bool softened){if(FromHost(sender))Omens.BloodMoon.Heard(active,softened);}
        private static bool FromHost(long sender)=>ZNet.instance!=null&&(ZNet.instance.IsServer()?sender==ZNet.GetUID():sender==ZNet.instance.GetServerPeer()?.m_uid);

        private static void Register()
        {
            _rpc=ZRoutedRpc.instance;if(_rpc==null)return;
            _rpc.Register<long>(Seen,(s,id)=>Director.OnSeen(s,id));
            _rpc.Register<long>(Respond_,(s,id)=>Director.OnRespond(s,id));
            _rpc.Register<string,Vector3,float,long>(Show,OnShow);
            _rpc.Register<long>(Resolved,OnResolved);
            _rpc.Register<Vector3,float>(Bless,OnBless);
            _rpc.Register<long>(Burn,OnResponded);
            _rpc.Register<bool,bool>(Moon,OnMoon);
            _rpc.Register<Vector3,float>(Favour_,OnFavour);
            _rpc.Register<Vector3,Vector3>(Lead_,OnLead);
            _rpc.Register<Vector3,string>(Mark_,OnMark);
            _rpc.Register<bool>(Aurora_,OnAurora);
            var table=AccessTools.Field(typeof(ZRoutedRpc),"m_functions").GetValue(_rpc) as IDictionary;
            foreach(string name in new[]{Seen,Respond_,Show,Resolved,Bless,Burn,Moon,Favour_,Lead_,Mark_,Aurora_})Handlers[name]=table?[name.GetStableHashCode()];
        }
        internal static void Unregister()
        {
            if(_rpc!=null&&AccessTools.Field(typeof(ZRoutedRpc),"m_functions").GetValue(_rpc) is IDictionary table)
                foreach(var entry in Handlers)
                    if(ReferenceEquals(table[entry.Key.GetStableHashCode()],entry.Value))table.Remove(entry.Key.GetStableHashCode()); // only our own handlers
            Handlers.Clear();_rpc=null;Reported.Clear();
            if(Minimap.instance!=null)foreach(Minimap.PinData pin in Pins.Values)Minimap.instance.RemovePin(pin);
            Pins.Clear();
            if(Minimap.instance!=null)foreach(Minimap.PinData pin in Marks)Minimap.instance.RemovePin(pin);
            Marks.Clear();
        }
    }
}
