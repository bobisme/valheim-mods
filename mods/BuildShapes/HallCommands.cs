using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BuildShapes
{
    public sealed partial class Plugin
    {
        private BaseUnityPlugin _hallClaude;
        private float _hallNextClaude;
        private void UpdateHallCommands()
        {
            if(Time.unscaledTime<_hallNextClaude)return;_hallNextClaude=Time.unscaledTime+2;
            var found=Resources.FindObjectsOfTypeAll<BaseUnityPlugin>().FirstOrDefault(p=>p!=null && p.gameObject.scene.IsValid() && MetadataHelper.GetMetadata(p)?.GUID=="com.dhack.claudetools");
            if(found==_hallClaude)return;_hallClaude=found;if(found==null)return;
            try
            {
                found.GetType().GetMethod("RegisterCommand",BindingFlags.Static|BindingFlags.Public)?.Invoke(null,new object[]{Name,"hall",
                    "hall catalog | rectangle <width> <length> [distance=10] | outline <x,z>... | options height=2|3|4 detail=0..4 storeys=1|2|3 basement=on|off pitch=26|45 material=auto|timber|core|stone|dark raise=0..2 roof=gabled|tiered entrance=auto|door|gate overhang=on|off porch=on|off trim=on|off crest=auto|none|dragon|raven | doors clear | doors <x,z>... | status | stairs | entrances | pieces | ui | clear | confirm | undo | restoreground | keepground | reload: local Hallwright previews; only confirm creates shared ghosts; undo removes the last shape's unbuilt ghosts",
                    new Func<string[],Action<JObject>,Action<string>,IEnumerator>(HallCommand)});
                found.GetType().GetMethod("RegisterFrame",BindingFlags.Static|BindingFlags.Public)?.Invoke(null,new object[]{Name,new Func<string,float[]>(HallFrame)});
            }
            catch(Exception ex){Logger.LogWarning("Hallwright ClaudeTools link: "+ex.GetBaseException().Message);}
        }
        private void UnregisterHallCommands()
        {if(_hallClaude!=null)try{_hallClaude.GetType().GetMethod("UnregisterAll",BindingFlags.Public|BindingFlags.Static)?.Invoke(null,new object[]{Name});}catch(Exception){} _hallClaude=null;}
        private float[] HallFrame(string name)
        {
            if(name!="hallpreview" || _tool!=Tool.Hall || _hallDesign==null || _hallDesign.Cells.Count==0)return null;
            double x=_hallDesign.Cells.Average(c=>c.X*2+1),z=_hallDesign.Cells.Average(c=>c.Z*2+1);
            Vector3 center=HallWorld(new V3(x,0,z));return new[]{center.x,center.y,center.z,_hallFrame.eulerAngles.y};
        }
        private JObject HallReadout()=>new JObject
        {
            ["mode"]=_tool.ToString(),["corners"]=_markers.Count,["pieces"]=_output.Count,["floorY"]=_hallFloorY,
            ["entrances"]=new JArray((_hallDesign?.Entrances??new System.Collections.Generic.List<HallDoors.Entrance>()).Select(d=>new JObject{["x"]=d.At.X,["z"]=d.At.Z,["yaw"]=d.Yaw,["edge"]=d.Edge})),["entranceMarkers"]=_hallDoorPoints.Count,
            ["area"]=_hallDesign?.Cells.Count*4,["porchArea"]=_hallDesign?.PorchFloors.Count*4,["wings"]=_hallDesign?.Wings.Count,["height"]=_hallHeight,["storeys"]=_hallStoreys,["basement"]=_hallBasement,["groundVertices"]=_hallGroundJob.Count,["stairHoles"]=_hallDesign?.StairHoles.Count,["pitch"]=_hallRoof45?45:26,
            ["overhang"]=_hallOverhang,["porch"]=_hallPorch,["sweep"]=_hallSweep,["crest"]=new[]{"Auto","None","Dragon","Raven"}[_hallCrestMode],["detail"]=_hallDetail,["roof"]=_hallTiered?"Tiered":"Gabled",["tieredWings"]=_hallDesign?.TieredWings,["entrance"]=_hallEntranceMode==0?"Auto":_hallEntranceMode==1?"Door":"Gate",["opening"]=_hallKit?.Door,["materials"]=HallMaterials[_hallMaterialMode],["addedSupports"]=_hallAddedPosts,["wouldFall"]=_hallFalls,
            ["checked"]=_hallSupport.Count,["solveMs"]=_hallSolveMs,["problem"]=_hallProblem,["note"]=_hallNote,
            ["bill"]=JObject.FromObject(_hallBill),["stations"]=new JArray(_hallStations),
            ["weakest"]=new JArray(_hallSupport.OrderBy(k=>k.Value.Support/k.Value.Max).Take(8).Select(k=>new JObject
                {["piece"]=_output[int.Parse(k.Key)].Prefab,["role"]=_hallRoles[int.Parse(k.Key)],["support"]=k.Value.Support,["minimum"]=k.Value.Min,["maximum"]=k.Value.Max})),
        };
        private static bool HallBool(string text)
        {if(text!="on" && text!="off")throw new ArgumentException("Use on or off for detail switches.");return text=="on";}
        private static float HallFloat(string text)
        {if(!float.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out float value) || float.IsNaN(value) || float.IsInfinity(value))throw new ArgumentException("Use finite numbers.");return value;}
        private IEnumerator HallCommand(string[] args,Action<JObject> output,Action<string> error)
        {
            try
            {
                string action=args.Length>1?args[1].ToLowerInvariant():"status";
                Player player=Player.m_localPlayer;
                if(action=="catalog")
                {
                    RefreshHallCatalog(player);
                    string[] candidates={"wood_floor","woodwall","wood_wall_half","wood_wall_quarter","wood_door","wood_gate","wood_dragon1","wood_pole2","wood_beam","wood_beam_1","wood_roof","wood_roof_top","wood_wall_roof_a","wood_roof_45","wood_roof_top_45","wood_wall_roof_45","wood_pole_log","wood_wall_log","woodiron_pole","stone_floor_2x2","darkwood_beam","darkwood_raven","darkwood_arch"};
                    output(new JObject{["unlocked"]=new JArray(candidates.Select(n=>new JObject{["prefab"]=n,["known"]=HallKnown(n),["material"]=_hallCatalog.TryGetValue(n,out var p)?p.GetComponent<WearNTear>()?.m_materialType.ToString():null}))});return null;
                }
                if(action=="status"){output(HallReadout());return null;}
                if(action=="pieces")
                {
                    var pieces=new System.Collections.Generic.List<Piece>();Piece.GetAllPiecesInRadius(player.transform.position,40,pieces);
                    output(new JObject{["world"]=ZNet.instance.GetWorldUID(),["pieces"]=new JArray(pieces.Where(p=>p!=null && p.GetComponent<ZNetView>()?.IsValid()==true).Take(2048).Select(p=>new JObject{
                        ["prefab"]=Utils.GetPrefabName(p.gameObject),["creator"]=p.GetCreator(),["position"]=new JArray(p.transform.position.x,p.transform.position.y,p.transform.position.z),["rotation"]=new JArray(p.transform.rotation.x,p.transform.rotation.y,p.transform.rotation.z,p.transform.rotation.w)}))});return null;
                }
                if(action=="entrances")
                {
                    var pieces=new System.Collections.Generic.List<Piece>();Piece.GetAllPiecesInRadius(player.transform.position,40,pieces);
                    var checks=new JArray();
                    foreach(var piece in pieces.Where(p=>p!=null && (Utils.GetPrefabName(p.gameObject)=="wood_door" || Utils.GetPrefabName(p.gameObject)=="wood_gate") && p.GetComponent<ZNetView>()?.IsValid()==true).Take(32))
                    {
                        var snaps=new System.Collections.Generic.List<Transform>();piece.GetSnapPoints(snaps);float low=snaps.Min(t=>t.position.y);
                        Vector3 bottom=snaps.Where(t=>Math.Abs(t.position.y-low)<0.01).Aggregate(Vector3.zero,(a,t)=>a+t.position)/snaps.Count(t=>Math.Abs(t.position.y-low)<0.01);
                        var blockers=new System.Collections.Generic.HashSet<string>();
                        for(int i=0;i<=6;i++)
                        {
                            Vector3 foot=bottom+piece.transform.forward*(i/3f-1)+Vector3.up*0.16f;
                            foreach(Collider hit in Physics.OverlapCapsule(foot+Vector3.up*0.3f,foot+Vector3.up*1.55f,0.25f,LayerMask.GetMask("piece","Default","static_solid"),QueryTriggerInteraction.Ignore))
                            {Piece other=hit.GetComponentInParent<Piece>();if(other==piece || other!=null && other.GetComponent<ZNetView>()?.IsValid()!=true)continue;blockers.Add(other!=null?Utils.GetPrefabName(other.gameObject):hit.name);}
                        }
                        checks.Add(new JObject{["position"]=new JArray(bottom.x,bottom.y,bottom.z),["clear"]=blockers.Count==0,["blockers"]=new JArray(blockers)});
                    }
                    output(new JObject{["entrances"]=checks,["checked"]=checks.Count,["blocked"]=checks.Count(c=>(bool)c["clear"]==false)});return null;
                }
                if(action=="stairs")
                {
                    var pieces=new System.Collections.Generic.List<Piece>();Piece.GetAllPiecesInRadius(player.transform.position,40,pieces);
                    var checks=new JArray();
                    foreach(var piece in pieces.Where(p=>p!=null && Utils.GetPrefabName(p.gameObject)=="wood_stair" && p.GetComponent<ZNetView>()?.IsValid()==true).Take(64))
                    {
                        Vector3[] snaps=HallSnaps(piece.gameObject);float bottom=snaps.Min(v=>v.y),top=snaps.Max(v=>v.y);
                        Vector3 low=Vector3.zero,high=Vector3.zero;int ln=0,hn=0;
                        foreach(var snap in snaps){if(Mathf.Abs(snap.y-bottom)<0.001){low+=snap;ln++;}if(Mathf.Abs(snap.y-top)<0.001){high+=snap;hn++;}}
                        low=piece.transform.TransformPoint(low/ln);high=piece.transform.TransformPoint(high/hn);
                        var blockers=new System.Collections.Generic.HashSet<string>();
                        for(int sample=1;sample<8;sample++)
                        {
                            Vector3 foot=Vector3.Lerp(low,high,sample/8f)+Vector3.up*0.16f;
                            foreach(Collider hit in Physics.OverlapCapsule(foot+Vector3.up*0.3f,foot+Vector3.up*1.55f,0.25f,LayerMask.GetMask("piece","Default","static_solid"),QueryTriggerInteraction.Ignore))
                            {Piece other=hit.GetComponentInParent<Piece>();if(other==piece || other!=null && other.GetComponent<ZNetView>()?.IsValid()!=true)continue;blockers.Add(other!=null?Utils.GetPrefabName(other.gameObject):hit.name);}
                        }
                        checks.Add(new JObject{["position"]=new JArray(piece.transform.position.x,piece.transform.position.y,piece.transform.position.z),["clear"]=blockers.Count==0,["blockers"]=new JArray(blockers)});
                    }
                    output(new JObject{["stairs"]=checks,["checked"]=checks.Count,["blocked"]=checks.Count(c=>(bool)c["clear"]==false)});return null;
                }
                if(action=="clear"){if(_tool==Tool.Hall)Stop();output(new JObject{["cleared"]=true});return null;}
                if(action=="reload")return ReloadHallScripts(output,error);
                if(action=="restoreground" || action=="keepground")
                {RestoreHallGround(action=="keepground");output(new JObject{["ground"]=action=="keepground"?"kept":"restored"});return null;}
                if(!Ready(player) || !_planner.Available(player))throw new ArgumentException("Equip the hammer, close other menus, and finish any bridge/blueprint placement first.");
                if(action=="undo")
                {
                    if(_lastPlan==null)throw new ArgumentException("No shape to undo in this session.");
                    if(!_planner.Remove(player,_lastPlan,out int removed,out string problem))throw new ArgumentException(problem);
                    _lastPlan=null;output(new JObject{["removed"]=removed});return null;
                }
                if(action=="rectangle" || action=="outline")
                {
                    V3[] local;
                    if(action=="rectangle")
                    {
                        if(args.Length<4)throw new ArgumentException("hall rectangle <width> <length> [distance=10]");
                        float w=HallFloat(args[2]),d=HallFloat(args[3]);
                        local=new[]{new V3(0,0,0),new V3(w,0,0),new V3(w,0,d),new V3(0,0,d)};
                    }
                    else
                    {
                        if(args.Length<5 || args.Length>HallLayout.MaximumCorners+2)throw new ArgumentException("hall outline <x,z>... (3–24 local corners)");
                        local=args.Skip(2).Select(t=>{string[] xy=t.Split(',');if(xy.Length!=2)throw new ArgumentException("Each corner is x,z.");return new V3(HallFloat(xy[0]),0,HallFloat(xy[1]));}).ToArray();
                    }
                    HallLayout.Footprint(local); // Validate before replacing the player's existing local preview.
                    float distance=action=="rectangle" && args.Length>4?HallFloat(args[4]):10;
                    if(distance<2 || distance>24)throw new ArgumentException("Preview distance must be 2–24 metres.");
                    Stop();_tool=Tool.Hall;_hallFrame=Quaternion.Euler(0,player.transform.eulerAngles.y,0);
                    _hallOrigin=player.transform.position+_hallFrame*new Vector3(-4,0,distance);
                    foreach(V3 v in local)_markers.Add(_hallOrigin+_hallFrame*V(v));
                    BuildHallPreview();output(HallReadout());return null;
                }
                if(_tool!=Tool.Hall)throw new ArgumentException("Start Hallwright from F4 or use hall rectangle first.");
                if(action=="doors")
                {
                    var points=args.Skip(2).ToArray();
                    if(points.Length==1 && points[0]=="clear")_hallDoorPoints.Clear();
                    else
                    {
                        if(points.Length<1 || points.Length>HallDoors.Maximum)throw new ArgumentException("hall doors <x,z>...: one to eight local entrance points, or clear");
                        var parsed=points.Select(t=>{string[] xy=t.Split(',');if(xy.Length!=2)throw new ArgumentException("Each entrance is x,z.");return new V3(HallFloat(xy[0]),0,HallFloat(xy[1]));}).ToArray();
                        var corners=_markers.Select(p=>V(Quaternion.Inverse(_hallFrame)*(p-_hallOrigin))).ToArray();HallLayout.Footprint(corners);HallDoors.Solve(corners,parsed,_hallEntrance,_hallPorch);
                        _hallDoorPoints.Clear();_hallDoorPoints.AddRange(parsed);
                    }
                    BuildHallPreview();output(HallReadout());return null;
                }
                if(action=="options")
                {
                    int height=_hallHeight,detail=_hallDetail,material=_hallMaterialMode,entrance=_hallEntranceMode,crest=_hallCrestMode,storeys=_hallStoreys;bool basement=_hallBasement,steep=_hallRoof45,tiered=_hallTiered,overhang=_hallOverhang,porch=_hallPorch,sweep=_hallSweep;float raise=_hallRaise;
                    foreach(string option in args.Skip(2))
                    {
                        string[] kv=option.Split('=');if(kv.Length!=2)throw new ArgumentException("Options use key=value.");
                        switch(kv[0])
                        {
                            case "height": float h=HallFloat(kv[1]);if(h!=Math.Round(h) || h<2 || h>4)throw new ArgumentException("Height must be 2, 3, or 4.");height=(int)h;break;
                            case "detail": float d=HallFloat(kv[1]);if(d!=Math.Round(d) || d<0 || d>4)throw new ArgumentException("Detail must be 0–4.");detail=(int)d;break;
                            case "storeys": float n=HallFloat(kv[1]);if(n!=Math.Round(n) || n<1 || n>3)throw new ArgumentException("Storeys must be 1–3.");storeys=(int)n;break;
                            case "basement": basement=HallBool(kv[1]);break;
                            case "pitch": if(kv[1]!="26" && kv[1]!="45")throw new ArgumentException("Pitch is 26 or 45.");steep=kv[1]=="45";break;
                            case "raise": raise=HallFloat(kv[1]);if(raise<0 || raise>2)throw new ArgumentException("Raise is 0–2 m.");break;
                            case "roof": if(kv[1]!="gabled" && kv[1]!="tiered")throw new ArgumentException("Roof is gabled or tiered.");tiered=kv[1]=="tiered";break;
                            case "entrance": entrance=Array.IndexOf(new[]{"auto","door","gate"},kv[1]);if(entrance<0)throw new ArgumentException("Entrance is auto, door, or gate.");break;
                            case "overhang": overhang=HallBool(kv[1]);break;
                            case "porch": porch=HallBool(kv[1]);break;
                            case "trim": sweep=HallBool(kv[1]);break;
                            case "crest": crest=Array.IndexOf(new[]{"auto","none","dragon","raven"},kv[1]);if(crest<0)throw new ArgumentException("Ridge ends are auto, none, dragon, or raven.");break;
                            case "material": material=Array.IndexOf(new[]{"auto","timber","core","stone","dark"},kv[1]);if(material<0)throw new ArgumentException("Unknown material choice.");break;
                            default:throw new ArgumentException("Unknown hall option: "+kv[0]);
                        }
                    }
                    _hallStoreys=storeys;_hallBasement=basement;_hallHeight=height;_hallDetail=detail;_hallMaterialMode=material;_hallRoof45=steep;_hallRaise=raise;_hallTiered=tiered;_hallEntranceMode=entrance;_hallCrestMode=crest;_hallOverhang=overhang;_hallPorch=porch;_hallSweep=sweep;BuildHallPreview();output(HallReadout());return null;
                }
                if(action=="ui"){if(_markers.Count<3 && !System.IO.File.Exists(HallGroundFile))throw new ArgumentException("Draw a footprint first.");OpenHallMenu();output(HallReadout());return null;}
                if(action=="confirm")
                {
                    BuildHallPreview();if(_hallProblem!=null)throw new ArgumentException(_hallProblem);
                    string key=SubmitHall(player);_lastPlan=key;output(new JObject{["plan"]=key,["pieces"]=_output.Count});Stop();return null;
                }
                throw new ArgumentException("Unknown hall action.");
            }
            catch(Exception ex){error(ex.GetBaseException().Message);return null;}
        }
        private IEnumerator ReloadHallScripts(Action<JObject> output,Action<string> error)
        {
            var engine=Resources.FindObjectsOfTypeAll<BaseUnityPlugin>().FirstOrDefault(p=>p!=null && MetadataHelper.GetMetadata(p)?.GUID=="com.bepis.bepinex.scriptengine");
            MethodInfo reload=engine?.GetType().GetMethod("ReloadPlugins",BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public);
            if(reload==null){error("ScriptEngine's reload interface is unavailable; press F6.");return null;}
            engine.StartCoroutine(DeferredHallReload(engine,reload));
            output(new JObject{["reloadScheduled"]=true});return null;
        }
        private static IEnumerator DeferredHallReload(BaseUnityPlugin engine,MethodInfo reload)
        {yield return new WaitForSeconds(1);reload.Invoke(engine,null);}
    }
}
