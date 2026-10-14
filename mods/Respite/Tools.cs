using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Respite
{
    // A "respite" command for Claude Tools, found while the game runs (no reference), to check the raid clock and musters in a live game.
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
                found.GetType().GetMethod("RegisterCommand",BindingFlags.Public|BindingFlags.Static)?.Invoke(null,new object[]{Plugin.Name,"respite",
                    "respite status | raid <event> [east north] | roll | stop | muster [east north] | clear: check Respite. status shows the raid clock near the player (and why a raid may not come), the raid under way and where it gathers; raid starts one of the game's raids at the player (or metres east and north of them), as an omen would, without asking the clock; roll lets the game's own raid timer pick a raid now (the clock may refuse it); stop ends the raid under way; muster sends three greylings through a test muster at the player (or an offset); clear forgets the raids near the player (host only).",
                    (Func<string[],Action<JObject>,Action<string>,IEnumerator>)Run});
                Plugin.Log("Claude Tools found: respite command added");
            }
            catch(Exception e){Plugin.Log("Could not add the respite command: "+e.Message);}
        }
        internal static void Stop()
        {
            try{_tools?.GetType().GetMethod("UnregisterAll",BindingFlags.Public|BindingFlags.Static)?.Invoke(null,new object[]{Plugin.Name});}catch(Exception){}
            _tools=null;
        }
        private static float F(string[] a,int i)=>a.Length>i&&float.TryParse(a[i],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float v)?v:0;
        private static JArray V(Vector3 v)=>new JArray(Math.Round(v.x,1),Math.Round(v.y,1),Math.Round(v.z,1));

        private static IEnumerator Run(string[] a,Action<JObject> output,Action<string> error)
        {
            Player me=Player.m_localPlayer;
            if(me==null||ZNet.instance==null||RandEventSystem.instance==null){error("needs the player in a world");yield break;}
            Vector3 here=me.transform.position;
            string verb=a.Length>1?a[1]:"status";
            bool host=ZNet.instance.IsServer();
            switch(verb)
            {
                case "status":
                {
                    RandomEvent ev=RandEventSystem.instance.GetCurrentRandomEvent()??RandEventSystem.instance.GetActiveEvent();
                    var o=new JObject{["host"]=host,["here"]=V(here),["blocked"]=Ledger.Blocked(here),
                        ["raids"]=new JArray(Ledger.Raids.OrderByDescending(r=>r.T).Select(r=>new JObject{["src"]=r.Src,["daysAgo"]=Math.Round((Ledger.Now-r.T)/Ledger.Day,2),
                            ["metres"]=Math.Round(Utils.DistanceXZ(new Vector3(r.X,0,r.Z),here)),["at"]=new JArray(Math.Round(r.X),Math.Round(r.Z))}))};
                    if(ev!=null)o["event"]=new JObject{["name"]=ev.m_name,["pos"]=V(ev.m_pos),["time"]=Math.Round(ev.m_time,1),["duration"]=ev.m_duration,["raidlike"]=GameRaids.Raidlike(ev),
                        ["active"]=RandEventSystem.instance.GetActiveEvent()==ev,["spawning"]=RandEventSystem.instance.GetCurrentSpawners()!=null};
                    if(Muster.GameSpot!=null)o["gathers"]=new JObject{["at"]=V(Muster.GameSpot.Value),["metres"]=Math.Round(Utils.DistanceXZ(Muster.GameSpot.Value,ev!=null?ev.m_pos:here))};
                    output(o);break;
                }
                case "raid":
                {
                    if(!host){error("only the host starts raids");yield break;}
                    string name=a.Length>2?a[2]:"army_eikthyr";
                    if(!RandEventSystem.instance.HaveEvent(name)){error("the game has no raid "+name);yield break;}
                    Vector3 at=here+new Vector3(F(a,3),0,F(a,4));
                    RandEventSystem.instance.SetRandomEventByName(name,at);
                    output(new JObject{["started"]=name,["at"]=V(at),["now"]=RandEventSystem.instance.GetCurrentRandomEvent()?.m_name??"(none: drawn to a Warstone?)"});break;
                }
                case "roll":
                {
                    if(!host){error("only the host rolls raids");yield break;}
                    string before=RandEventSystem.instance.GetCurrentRandomEvent()?.m_name;
                    RandEventSystem.instance.StartRandomEvent();
                    RandomEvent ev=RandEventSystem.instance.GetCurrentRandomEvent();
                    output(new JObject{["started"]=ev!=null&&ev.m_name!=before?ev.m_name:"(nothing)",["blockedHere"]=Ledger.Blocked(here)});break;
                }
                case "stop": RandEventSystem.instance.ResetRandomEvent();output(new JObject{["stopped"]=true});break;
                case "muster":
                {
                    Vector3 home=here+new Vector3(F(a,2),0,F(a,3));
                    Vector3 spot=Muster.Begin(home,0,null,"A test band comes!",at=>
                    {
                        GameObject prefab=ZNetScene.instance.GetPrefab("Greyling");
                        for(int i=0;i<3&&prefab!=null;i++)
                        {
                            GameObject go=UnityEngine.Object.Instantiate(prefab,at+new Vector3(UnityEngine.Random.Range(-3f,3f),1,UnityEngine.Random.Range(-3f,3f)),Quaternion.identity);
                            go.GetComponent<MonsterAI>()?.SetHuntPlayer(true);
                        }
                        Plugin.Log($"Test muster came at {at:F0}");
                    });
                    output(new JObject{["home"]=V(home),["gathers"]=V(spot),["metres"]=Math.Round(Utils.DistanceXZ(spot,home)),["seconds"]=Plugin.Instance.WarningSeconds.Value});break;
                }
                case "clear":
                {
                    if(!host){error("only the host clears the clock");yield break;}
                    output(new JObject{["forgot"]=Ledger.Forget(here,Plugin.Instance.AreaRadius.Value)});break;
                }
                default: error("respite status | raid <event> [east north] | roll | stop | muster [east north] | clear");break;
            }
        }
    }
}
