using System;
using BepInEx.Bootstrap;
using UnityEngine;

namespace Omens
{
    // Respite (com.bobisme.respite), when it's there: one raid clock shared with the game and Bob's other mods, and raiders that
    // gather outside the base before they come. Reached by reflection; without it, raids come as before.
    internal static class RespiteLink
    {
        private static object _plugin;
        private static Type _api;
        private static Type Api()
        {
            if(!Chainloader.PluginInfos.TryGetValue("com.bobisme.respite",out var info)||info.Instance==null)return null;
            if(!ReferenceEquals(info.Instance,_plugin)){_plugin=info.Instance;_api=info.Instance.GetType().Assembly.GetType("Respite.Api");}
            return _api;
        }
        private static object Call(string name,params object[] args)
        {
            try{return Api()?.GetMethod(name)?.Invoke(null,args);}
            catch(Exception e){Plugin.Log("Respite: "+name+" failed: "+(e.InnerException??e).Message);return null;}
        }
        // Why no raid may come near a place now ("" if one may, or without Respite).
        internal static string Blocked(Vector3 at)=>Call("Blocked",at) as string??"";
        // A raid of ours that the game's raid system doesn't see came near a place.
        internal static void Raided(Vector3 at,string source)=>Call("Raided",at,source);
        // Raiders gather outside home, shown to the players near, and spawn(spot) when the warning is up. False without Respite.
        internal static bool Muster(Vector3 home,float clear,string text,Action<Vector3> spawn)=>Call("Muster",home,clear,text,spawn)!=null;
    }
}
