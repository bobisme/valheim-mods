using System;
using System.Collections.Generic;
using HarmonyLib;

namespace Factorheim
{
    // Routed RPCs (one ZPackage each) that are (re)registered whenever ZRoutedRpc is new (a world load) and after a hot reload: an older
    // copy's handler under the same name is replaced, and ours are taken out again on unload.
    internal static class Net
    {
        private static readonly Dictionary<string,Action<long,ZPackage>> Handlers=new Dictionary<string,Action<long,ZPackage>>();
        private static ZRoutedRpc _on;

        // name: "FH_<Something>". Call from a feature's Start (any time is fine). skipOwn: our own game ignores what it sent (it has
        // already done it locally), for messages sent with Others.
        internal static void Register(string name,Action<long,ZPackage> handler,bool skipOwn=true)
        {
            if(skipOwn){var inner=handler;handler=(sender,pkg)=>{if(!Mine(sender))inner(sender,pkg);};}
            Handlers[name]=handler;
            if(ZRoutedRpc.instance!=null&&_on==ZRoutedRpc.instance)Add(ZRoutedRpc.instance,name,handler);
        }
        internal static void Tick()
        {
            ZRoutedRpc rpc=ZRoutedRpc.instance;
            if(rpc==null||rpc==_on)return;
            _on=rpc;
            foreach(var kv in Handlers)Add(rpc,kv.Key,kv.Value);
        }
        private static void Add(ZRoutedRpc rpc,string name,Action<long,ZPackage> handler)
        {
            var table=(System.Collections.IDictionary)AccessTools.Field(typeof(ZRoutedRpc),"m_functions").GetValue(rpc);
            table.Remove(name.GetStableHashCode());
            rpc.Register<ZPackage>(name,(sender,pkg)=>
            {
                try{handler(sender,pkg);}catch(Exception e){Plugin.Warn(name+": "+e.Message);}
            });
        }
        internal static void Stop()
        {
            if(ZRoutedRpc.instance!=null)
            {
                var table=(System.Collections.IDictionary)AccessTools.Field(typeof(ZRoutedRpc),"m_functions").GetValue(ZRoutedRpc.instance);
                foreach(string name in Handlers.Keys)table.Remove(name.GetStableHashCode());
            }
            Handlers.Clear();_on=null;
        }
        // To everyone else (our own game has already done it locally; registered with skipOwn). A client's only peer is the server,
        // so this goes to Everybody and the handler drops our own copy.
        internal static void Others(string name,ZPackage pkg)
        {
            if(ZRoutedRpc.instance==null||ZNet.instance==null||ZNet.instance.GetPeers().Count==0)return;
            ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody,name,pkg);
        }
        internal static void Everyone(string name,ZPackage pkg)=>ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.Everybody,name,pkg);
        internal static bool Mine(long sender)=>ZRoutedRpc.instance!=null&&sender==ZDOMan.GetSessionID();
    }
}
