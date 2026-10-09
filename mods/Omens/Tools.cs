using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Omens
{
    // An "omen" command for Claude Tools, found while the game runs (no reference), so omens can be tested without waiting days.
    internal static class Tools
    {
        private const string ClaudeToolsGuid="com.dhack.claudetools";
        private static BaseUnityPlugin _tools;
        private static float _nextCheck;

        internal static void Tick()
        {
            if(Time.unscaledTime<_nextCheck)return;
            _nextCheck=Time.unscaledTime+5;
            BaseUnityPlugin found=Resources.FindObjectsOfTypeAll<BaseUnityPlugin>().Where(p=>p!=null&&p.gameObject.scene.IsValid())
                .FirstOrDefault(p=>MetadataHelper.GetMetadata(p)?.GUID==ClaudeToolsGuid);
            if(found==_tools)return;
            _tools=found;
            if(found==null)return;
            try
            {
                MethodInfo register=found.GetType().GetMethod("RegisterCommand",BindingFlags.Public|BindingFlags.Static);
                register?.Invoke(null,new object[]{Plugin.Name,"omen",
                    $"omen list | events | place <{string.Join("|",Policy.All.Select(o=>o.Test))}> [metres=12] | now <id|last> | avert <id> | clear <id> | fate [-5..5] | chains | soon: test omens (host only). place puts one ahead of the player; now marks it seen and brings its outcome at once, night or not (a hoard is taken by the host first); avert responds as a player would (burns the troll, takes the hoard); clear removes one without effect; fate shows or sets the gods' favour; chains brings returning omens due now; soon schedules the next natural omen in 5 s",
                    (Func<string[],Action<JObject>,Action<string>,IEnumerator>)Run});
                Plugin.Log("Claude Tools found: omen command added");
            }
            catch(Exception e){Plugin.Log("Could not add the omen command to Claude Tools: "+e.Message);}
        }
        internal static void Stop()
        {
            try{_tools?.GetType().GetMethod("UnregisterAll",BindingFlags.Public|BindingFlags.Static)?.Invoke(null,new object[]{Plugin.Name});}catch(Exception){}
            _tools=null;
        }
        private static IEnumerator Run(string[] args,Action<JObject> output,Action<string> error)
        {
            string sub=args.Length>1?args[1]:"list";
            switch(sub)
            {
                case "list":output(new JObject{["omens"]=new JArray(Director.TestList().ToArray())});break;
                case "events": // the raids this game knows, and whether each omen's raid is among them (works on any player's game)
                    var names=RandEventSystem.instance!=null?RandEventSystem.instance.m_events.Select(ev=>ev.m_name).ToArray():new string[0];
                    output(new JObject{["events"]=new JArray(names),
                        ["omenRaids"]=new JObject(Policy.All.Where(o=>o.Raid!=null).Select(o=>new JProperty(o.Raid,names.Contains(o.Raid))))});
                    break;
                case "fate":output(new JObject{["fate"]=Director.TestFate(args.Length>2?args[2]:"")});break;
                case "chains":output(new JObject{["chains"]=Director.TestChains()});break;
                case "soon":Director.TestSoon();output(new JObject{["soon"]="the next omen is placed within a few seconds near a player"});break;
                case "avert":output(new JObject{["avert"]=Director.TestAvert(args.Length>2?args[2]:"")});break;
                case "clear":output(new JObject{["clear"]=Director.TestClear(args.Length>2?args[2]:"")});break;
                case "now":output(new JObject{["now"]=Director.TestNow(args.Length>2?args[2]:"last")});break;
                case "place":
                    Player me=Player.m_localPlayer;
                    if(me==null){error("omen place: no player");break;}
                    Omen pick=args.Length>2?Policy.All.FirstOrDefault(o=>o.Test==args[2]):null;
                    if(pick==null){error("omen place: "+string.Join(", ",Policy.All.Select(o=>o.Test)));break;}
                    Kind? kind=pick.Kind;
                    float metres=args.Length>3&&float.TryParse(args[3],out float m)?m:12;
                    Vector3 ahead=me.transform.forward;ahead.y=0;
                    string id=Director.TestPlace(kind.Value,me.transform.position+ahead.normalized*metres);
                    if(id==null)error("omen place: only the host places omens");
                    else output(new JObject{["placed"]=Policy.Of(kind.Value).Name,["id"]=id});
                    break;
                default:error("omen: list, events, place, now, avert, clear, fate, chains or soon");break;
            }
            return null;
        }
    }
}
