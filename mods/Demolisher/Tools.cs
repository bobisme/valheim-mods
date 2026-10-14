using System;
using System.Collections;
using System.Reflection;
using BepInEx;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Demolisher
{
    // A "demolisher" command for Claude Tools, found while the game runs (no reference), to see the lightning in a live game.
    internal static class Tools
    {
        private const string ClaudeToolsGuid="com.quad.claudetools";
        private static BaseUnityPlugin _tools;
        private static float _nextCheck;

        internal static void Tick()
        {
            if(Time.unscaledTime<_nextCheck)return;
            _nextCheck=Time.unscaledTime+5;
            BaseUnityPlugin found=BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(ClaudeToolsGuid,out PluginInfo info)?info.Instance:null;
            if(found==_tools)return;
            _tools=found;
            if(found==null)return;
            try
            {
                found.GetType().GetMethod("RegisterCommand",BindingFlags.Public|BindingFlags.Static)?.Invoke(null,new object[]{Plugin.Name,"demolisher",
                    "demolisher strike [metres]: Thor's lightning as a demolition calls it (the buildup, then the bolt), on the ground that many metres (default 12) in front of the player, removing nothing; reports the player's health (it should do no harm: check again with status). It returns as the bolt strikes, so a shot can follow.",
                    (Func<string[],Action<JObject>,Action<string>,IEnumerator>)Run});
                Plugin.Log("Claude Tools found: demolisher command added");
            }
            catch(Exception e){Plugin.Log("Could not add the demolisher command: "+e.Message);}
        }
        internal static void Stop()
        {
            try{_tools?.GetType().GetMethod("UnregisterAll",BindingFlags.Public|BindingFlags.Static)?.Invoke(null,new object[]{Plugin.Name});}catch(Exception){}
            _tools=null;
        }

        private static IEnumerator Run(string[] a,Action<JObject> output,Action<string> error)
        {
            Player me=Player.m_localPlayer;
            if(me==null||ZNetScene.instance==null){error("needs the player in a world");yield break;}
            if(a.Length<2||a[1]!="strike"){error("demolisher strike [metres]");yield break;}
            float metres=a.Length>2&&float.TryParse(a[2],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float m)?m:12;
            Vector3 forward=me.transform.forward;forward.y=0;forward.Normalize();
            Vector3 at=me.transform.position+forward*metres;
            at.y=ZoneSystem.instance!=null?ZoneSystem.instance.GetGroundHeight(at):at.y;
            if(!Plugin.Buildup(at)){error("no vfx_lever prefab");yield break;}
            yield return new WaitForSeconds(Plugin.Instance!=null?Plugin.Instance.LightningDelay:3);
            if(!Plugin.Bolt(at)){error("no lightningAOE prefab");yield break;}
            output(new JObject{["at"]=new JArray(Math.Round(at.x,1),Math.Round(at.y,1),Math.Round(at.z,1)),["health"]=Math.Round(me.GetHealth(),1)});
        }
    }
}
