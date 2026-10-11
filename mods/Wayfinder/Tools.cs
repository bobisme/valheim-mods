using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Wayfinder
{
    // A "wayfinder" command for Claude Tools, found while the game runs (no reference), to check the navigator's kit in a live game.
    internal static class Tools
    {
        private const string ClaudeToolsGuid="com.quad.claudetools";
        private static BaseUnityPlugin _tools;
        private static float _nextCheck;
        private const string TestKey="bob_wayfinder_test";

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
                found.GetType().GetMethod("RegisterCommand",BindingFlags.Public|BindingFlags.Static)?.Invoke(null,new object[]{Plugin.Name,"wayfinder",
                    "wayfinder info | course [add x z [name] | here [name] | clear | reverse | skip] | weather | give <compass|sunstone|line|stave|raven|all> | take | place <vane|beacon|mark> [ahead=6] | remove | map [large|small] [zoom] | raven | climb | ship: check Wayfinder. give/take put test items in the player's inventory and take them back; place/remove put test pieces ahead of the player and take them away.",
                    (Func<string[],Action<JObject>,Action<string>,IEnumerator>)Run});
                Plugin.Log("Claude Tools found: wayfinder command added");
            }
            catch(Exception e){Plugin.Log("Could not add the wayfinder command: "+e.Message);}
        }
        internal static void Stop()
        {
            try{_tools?.GetType().GetMethod("UnregisterAll",BindingFlags.Public|BindingFlags.Static)?.Invoke(null,new object[]{Plugin.Name});}catch(Exception){}
            _tools=null;
        }
        private static JArray V(Vector3 v)=>new JArray(Math.Round(v.x,1),Math.Round(v.y,1),Math.Round(v.z,1));
        private static float Arg(string[] a,int i,float d)=>a.Length>i&&float.TryParse(a[i],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float v)?v:d;

        private static IEnumerator Run(string[] args,Action<JObject> output,Action<string> error)
        {
            Player me=Player.m_localPlayer;
            if(me==null){error("wayfinder: no player");return null;}
            string sub=args.Length>1?args[1]:"info";
            switch(sub)
            {
                case "info":
                {
                    Ship ship=Ship.GetLocalShip();
                    output(new JObject{
                        ["course"]=new JArray(Course.Points.Select((p,i)=>new JObject{["i"]=i+1,["at"]=new JArray(Math.Round(p.x),Math.Round(p.z)),["name"]=p.name})),
                        ["next"]=Course.Next+1,["active"]=Course.Active,
                        ["items"]=new JArray(Items.All.Where(d=>d.Built!=null).Select(d=>d.Name+(Items.Has(me,d)?" (carried)":""))),
                        ["recipes"]=new JArray(Items.All.Where(d=>d.Recipe!=null&&ObjectDB.instance.m_recipes.Contains(d.Recipe)).Select(d=>d.Name)),
                        ["pieces"]=new JArray(new[]{Pieces.Vane,Pieces.Beacon,Pieces.Mark}.Where(p=>p!=null).Select(p=>p.GetComponent<Piece>().m_name)),
                        ["sky"]=Weather.SkyNow(me,Items.Has(me,Items.Sunstone)).ToString(),
                        ["env"]=EnvMan.instance?.GetCurrentEnvironment()?.m_name,
                        ["ship"]=ship!=null?Ships.NameOf(ship):null,
                        ["helmCourse"]=Ships.HelmCourse?.Count??0,
                        ["fleet"]=Ships.FleetCount,["landmarks"]=Landmarks.Count,
                        ["sounding"]=Sounding.Line,["aloft"]=Masthead.Aloft,["raven"]=Raven.Last,
                        ["at"]=V(me.transform.position)});
                    break;
                }
                case "course":
                {
                    string what=args.Length>2?args[2]:"list";
                    if(what=="add")Course.Add(new Vector3(Arg(args,3,0),0,Arg(args,4,0)),args.Length>5?string.Join(" ",args.Skip(5)):null);
                    else if(what=="here")Course.Add(me.transform.position,args.Length>3?string.Join(" ",args.Skip(3)):null);
                    else if(what=="clear")Course.Clear();
                    else if(what=="reverse")Course.Reverse();
                    else if(what=="skip")Course.Skip();
                    output(new JObject{["course"]=Policy.Save(Course.Points,Course.Next),["length"]=Policy.Distance(Policy.Length(Course.Flat,0))});
                    break;
                }
                case "weather":
                {
                    long p=Weather.Period(out double left);
                    var list=new JArray();
                    for(int i=0;i<4;i++)
                    {
                        EnvSetup e=Weather.Foretell(me.transform.position,p+i);
                        long t=(long)ZNet.instance.GetTimeSeconds()+(long)(i==0?0:left+(i-1)*EnvMan.instance.m_environmentDuration);
                        Weather.Wind(t,e,out Vector3 w,out float s);
                        list.Add(new JObject{["period"]=p+i,["weather"]=e?.m_name,["wind"]=Policy.WindFrom(Policy.Bearing(w.x,w.z)),["strength"]=Math.Round(s,2),["overcast"]=Weather.IsOvercast(e)});
                    }
                    Vector3 now=EnvMan.instance.GetWindDir();
                    output(new JObject{["now"]=EnvMan.instance.GetCurrentEnvironment()?.m_name,["windNow"]=Policy.WindFrom(Policy.Bearing(now.x,now.z)),["strengthNow"]=Math.Round(EnvMan.instance.GetWindIntensity(),2),
                        ["secondsLeft"]=Math.Round(left),["periods"]=list,["forecast"]=Weather.Forecast(me.transform.position)});
                    break;
                }
                case "give":
                {
                    string which=args.Length>2?args[2]:"all";
                    var given=new JArray();
                    foreach(ItemDef d in Items.All)
                    {
                        string k=d==Items.SunCompass?"compass":d==Items.Sunstone?"sunstone":d==Items.SoundingLine?"line":d==Items.Primstav?"stave":"raven";
                        if(which!="all"&&which!=k||d.Built==null||Items.Has(me,d))continue;
                        me.GetInventory().AddItem(d.Built,1);
                        var added=me.GetInventory().GetAllItems().LastOrDefault(i=>Items.Is(i,d));
                        if(added!=null){added.m_customData[TestKey]="1";given.Add(d.Name);}
                    }
                    output(new JObject{["given"]=given});
                    break;
                }
                case "take":
                {
                    var taken=new JArray();
                    foreach(var item in me.GetInventory().GetAllItems().Where(i=>i.m_customData.ContainsKey(TestKey)).ToList()){taken.Add(item.m_shared.m_name);me.GetInventory().RemoveItem(item);}
                    output(new JObject{["taken"]=taken});
                    break;
                }
                case "place":
                {
                    string what=args.Length>2?args[2]:"vane";
                    GameObject prefab=what=="beacon"?Pieces.Beacon:what=="mark"?Pieces.Mark:Pieces.Vane;
                    if(prefab==null){error("wayfinder place: no such piece");break;}
                    float ahead=Arg(args,3,6);
                    Vector3 f=me.transform.forward;f.y=0;f.Normalize();
                    Vector3 at=me.transform.position+f*ahead;
                    if(what=="mark")at.y=ZoneSystem.instance.m_waterLevel;
                    else if(ZoneSystem.instance.GetSolidHeight(at,out float h))at.y=h;
                    GameObject go=Object.Instantiate(prefab,at,Quaternion.LookRotation(-f));
                    go.GetComponent<ZNetView>()?.GetZDO()?.Set(TestKey,true);
                    output(new JObject{["placed"]=what,["at"]=V(at)});
                    break;
                }
                case "remove":
                {
                    int n=0;
                    foreach(ZNetView v in Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None))
                        if(v!=null&&v.IsValid()&&v.GetZDO().GetBool(TestKey)){v.ClaimOwnership();ZNetScene.instance.Destroy(v.gameObject);n++;}
                    output(new JObject{["removed"]=n});
                    break;
                }
                case "map":
                {
                    // wayfinder map [large|small] [zoom]: show the map as the player would (only the screen changes)
                    string mode=args.Length>2?args[2]:"large";
                    Minimap.instance.SetMapMode(mode=="small"?Minimap.MapMode.Small:Minimap.MapMode.Large);
                    if(args.Length>3)Minimap.instance.LargeZoom=Arg(args,3,0.1f);
                    output(new JObject{["mode"]=Minimap.instance.m_mode.ToString(),["zoom"]=Minimap.instance.LargeZoom});
                    break;
                }
                case "raven":
                    output(new JObject{["released"]=Raven.Release(me)});
                    break;
                case "climb":
                {
                    Ship ship=Ship.GetLocalShip();
                    Chair mast=ship!=null?ship.GetComponentsInChildren<Chair>().FirstOrDefault(c=>c.name=="mast"):null;
                    if(mast==null){error("wayfinder climb: not aboard a ship with a mast");break;}
                    Masthead.Climb(me,ship,mast);
                    output(new JObject{["climbed"]=true});
                    break;
                }
                case "ship":
                {
                    var ships=new JArray(Object.FindObjectsByType<Ship>(FindObjectsSortMode.None).Select(s=>new JObject{["name"]=Ships.NameOf(s),["prefab"]=s.name,["at"]=V(s.transform.position),
                        ["mastTop"]=s.m_mastObject!=null?V(Items.Bounds(s.m_mastObject.transform).max):null,["fittings"]=s.transform.Find(Fittings.RootName)!=null,
                        ["chairs"]=new JArray(s.GetComponentsInChildren<Chair>().Select(c=>c.name+":"+c.m_attachAnimation))}));
                    output(new JObject{["ships"]=ships});
                    break;
                }
                default:error("wayfinder: unknown "+sub);break;
            }
            return null;
        }
    }
}
