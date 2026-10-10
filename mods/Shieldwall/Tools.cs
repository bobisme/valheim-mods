using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Shieldwall
{
    // A "siege" command for Claude Tools, found while the game runs (no reference), so sieges can be checked and tried quickly.
    internal static class Tools
    {
        private const string ClaudeToolsGuid="com.dhack.claudetools";
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
                found.GetType().GetMethod("RegisterCommand",BindingFlags.Public|BindingFlags.Static)?.Invoke(null,new object[]{Plugin.Name,"siege",
                    "siege status | plan [stage] [marks] [players] | route | start [stage 0-6] | end | marks <n> | place [metres | x z] | remove | horn | tryplant | craftui [close] | raid [event] | plant <stave prefab> [metres] [angle] | unplant | raiders | wall [radius] [gap degrees] | unwall | hover: Warstones near the player and their sieges; a preview of a siege's waves; "+
                    "the road the horde would take; start a short-warning test siege at the nearest stone (no mark for holding it); end the siege now; set a stone's marks; place a Warstone ahead of the player (test); plant a staff from nothing beside the nearest stone (test); remove every planted staff (test); what each raider is doing; ring the stone with test stake walls (open toward the rift by gap degrees) and remove them; the hover text of the stone and staves",
                    (Func<string[],Action<JObject>,Action<string>,IEnumerator>)Run});
                Plugin.Log("Claude Tools found: siege command added");
            }
            catch(Exception e){Plugin.Log("Could not add the siege command to Claude Tools: "+e.Message);}
        }
        internal static void Stop()
        {
            try{_tools?.GetType().GetMethod("UnregisterAll",BindingFlags.Public|BindingFlags.Static)?.Invoke(null,new object[]{Plugin.Name});}catch(Exception){}
            _tools=null;
        }
        private static Warstone Nearest()
        {
            Player me=Player.m_localPlayer;
            return me==null?null:Warstone.Loaded.Where(w=>w!=null&&w.Z!=null).OrderBy(w=>Vector3.Distance(w.transform.position,me.transform.position)).FirstOrDefault();
        }
        private static JObject Describe(Warstone w)
        {
            ZDO z=w.Z;Player me=Player.m_localPlayer;
            long siege=w.Siege;
            return new JObject
            {
                ["position"]=new JArray(Math.Round(w.transform.position.x,1),Math.Round(w.transform.position.y,1),Math.Round(w.transform.position.z,1)),
                ["distance"]=me!=null?Math.Round(Vector3.Distance(me.transform.position,w.transform.position),1):-1,
                ["owner"]=w.View.IsOwner()?"this game":z.GetOwner().ToString(),
                ["marks"]=w.Marks,["cracked"]=w.Cracked,["strength"]=w.Strength,["hearth"]=Ward.AtHearth(w),
                ["phase"]=w.Phase.ToString(),["wave"]=z.GetInt(Stone.WaveKey,0),["queue"]=z.GetInt(Stone.QueueKey,0),["spawned"]=z.GetInt(Stone.SpawnedKey,0),
                ["kills"]=z.GetInt(Stone.KillsKey,0),["health"]=Math.Round(z.GetFloat(Stone.HealthKey,0)),["maxHealth"]=Math.Round(z.GetFloat(Stone.MaxHealthKey,0)),
                ["rift"]=w.Phase==Phase.Idle?null:new JArray(Math.Round(w.Rift.x,1),Math.Round(w.Rift.y,1),Math.Round(w.Rift.z,1)),
                ["raidersAlive"]=Raider.Loaded.Count(r=>r!=null&&r.Siege==siege&&r.Body!=null&&!r.Body.IsDead()),
                ["held"]=z.GetInt(Stone.HeldKey,0),["fallen"]=z.GetInt(Stone.FallenKey,0),
                ["staves"]=new JArray(Planted.Loaded.Where(p=>p!=null&&Vector3.Distance(p.transform.position,w.transform.position)<=Policy.WardRadius+10).Select(p=>p.GetHoverName()).ToArray()),
            };
        }
        private static IEnumerator Run(string[] args,Action<JObject> output,Action<string> error)
        {
            string sub=args.Length>1?args[1]:"status";
            Warstone stone=Nearest();
            switch(sub)
            {
                case "status":
                    output(new JObject{["fps"]=Math.Round(1/Mathf.Max(0.001f,Time.smoothDeltaTime)),["stage"]=Director.StageNow(),["roster"]=Policy.Rosters[Director.StageNow()].Name,
                        ["keys"]=new JArray(ZoneSystem.instance!=null?ZoneSystem.instance.GetGlobalKeys().ToArray():new string[0]),
                        ["stones"]=new JArray(Warstone.Loaded.Where(w=>w!=null&&w.Z!=null).Select(Describe).ToArray())});
                    break;
                case "plan":
                    int stage=args.Length>2&&int.TryParse(args[2],out int s)?s:Director.StageNow(),marks=args.Length>3&&int.TryParse(args[3],out int m)?m:stone?.Strength??0,
                        players=args.Length>4&&int.TryParse(args[4],out int p)?p:1;
                    var plan=Policy.Plan(stage,marks,players,12345);
                    output(new JObject{["stage"]=stage,["marks"]=marks,["players"]=players,["health"]=Policy.StoneHealth(stage,marks),
                        ["waves"]=new JArray(plan.Select(w=>new JObject{["count"]=w.Count,["units"]=new JObject(w.GroupBy(u=>$"{u.Prefab}{(u.Level>1?"*"+u.Level:"")}{(u.Role!=Role.Grunt?" "+u.Role:"")}")
                            .Select(g=>new JProperty(g.Key,g.Count())))}).ToArray())});
                    break;
                case "route":
                    if(stone==null){error("siege route: no Warstone loaded");break;}
                    Vector3 from=stone.Phase!=Phase.Idle?stone.Rift:(Player.m_localPlayer!=null?Player.m_localPlayer.transform.position:stone.transform.position);
                    var path=new System.Collections.Generic.List<Vector3>();
                    bool found=Pathfinding.instance.GetPath(from,stone.transform.position,path,Pathfinding.AgentType.Humanoid,false,true);
                    float length=0;for(int i=1;i<path.Count;i++)length+=Vector3.Distance(path[i-1],path[i]);
                    output(new JObject{["found"]=found,["corners"]=path.Count,["length"]=Math.Round(length,1),
                        ["reachesStone"]=path.Count>0&&Utils.DistanceXZ(path[path.Count-1],stone.transform.position)<=Policy.Reach+1,
                        ["end"]=path.Count>0?new JArray(Math.Round(path[path.Count-1].x,1),Math.Round(path[path.Count-1].y,1),Math.Round(path[path.Count-1].z,1)):null});
                    break;
                case "start":
                    if(stone==null){error("siege start: no Warstone loaded");break;}
                    Director.TestStage=args.Length>2&&int.TryParse(args[2],out int st)?Mathf.Clamp(st,0,Policy.Rosters.Length-1):-1;
                    Net.Call(stone,Cause.Test);
                    output(new JObject{["start"]="a test siege gathers (10 s warning); holding it gives spoils but no mark"});
                    break;
                case "end":
                    if(stone==null||!stone.View.IsOwner()){error("siege end: only the game that owns the stone");break;}
                    stone.Z.Set(Stone.PhaseKey,(int)Phase.Idle);
                    output(new JObject{["end"]="the siege is called off; the horde melts away"});
                    break;
                case "marks":
                    if(stone==null||!stone.View.IsOwner()||args.Length<3||!int.TryParse(args[2],out int n)){error("siege marks <n>: on a stone this game owns");break;}
                    stone.Z.Set(Stone.MarksKey,Policy.Marks(n));stone.Z.Set(Stone.CrackedKey,false);
                    output(new JObject{["marks"]=Policy.Marks(n)});
                    break;
                case "raiders":
                {
                    var list=Raider.Loaded.Where(r=>r!=null&&r.Body!=null&&!r.Body.IsDead()).Select(r=>new JObject{
                        ["name"]=r.Body.m_name,["role"]=r.Role.ToString(),["level"]=r.Body.GetLevel(),["health"]=Math.Round(r.Body.GetHealth()),
                        ["toStone"]=stone!=null?Math.Round(Vector3.Distance(r.transform.position,stone.transform.position),1):-1,
                        ["doing"]=r.Doing(),["pos"]=new JArray(Math.Round(r.transform.position.x,1),Math.Round(r.transform.position.y,1),Math.Round(r.transform.position.z,1))}).ToArray();
                    output(new JObject{["raiders"]=new JArray(list)});
                    break;
                }
                case "unplant":
                {
                    int removed=0;
                    foreach(Planted staff in Planted.Loaded.ToList())if(staff!=null&&staff.View.IsValid()){staff.View.ClaimOwnership();ZNetScene.instance.Destroy(staff.gameObject);removed++;}
                    output(new JObject{["removed"]=removed});
                    break;
                }
                case "wall":
                {
                    // A ring of stake walls round the stone (test), with an opening of so many degrees facing the rift (0: closed).
                    Player me=Player.m_localPlayer;GameObject stake=ZNetScene.instance.GetPrefab("stake_wall");
                    if(stone==null||me==null||stake==null){error("siege wall: needs a loaded Warstone");break;}
                    float radius=args.Length>2&&float.TryParse(args[2],out float r)?r:7,gap=args.Length>3&&float.TryParse(args[3],out float g)?g:0;
                    Vector3 c=stone.transform.position;
                    Vector3 toward=(stone.Phase!=Phase.Idle?stone.Rift:me.transform.position)-c;toward.y=0;
                    float facing=Mathf.Atan2(toward.x,toward.z)*Mathf.Rad2Deg;
                    int count=Mathf.CeilToInt(2*Mathf.PI*radius/1.8f),made=0;
                    for(int i=0;i<count;i++)
                    {
                        float angle=360f*i/count;
                        if(gap>0&&Mathf.Abs(Mathf.DeltaAngle(angle,facing))<gap/2)continue;
                        Vector3 dir=Quaternion.Euler(0,angle,0)*Vector3.forward;
                        Vector3 at=c+dir*radius;
                        if(ZoneSystem.instance.GetGroundHeight(at,out float h))at.y=h;
                        GameObject go=UnityEngine.Object.Instantiate(stake,at,Quaternion.LookRotation(dir));
                        go.GetComponent<Piece>()?.SetCreator(me.GetPlayerID(),Splatform.PlatformManager.DistributionPlatform.LocalUser.PlatformUserID);
                        go.GetComponent<ZNetView>()?.GetZDO()?.Set("bob_sw_testwall",true);
                        made++;
                    }
                    output(new JObject{["walls"]=made,["radius"]=radius,["gap"]=gap});
                    break;
                }
                case "unwall":
                {
                    int removed=0;
                    foreach(ZNetView v in Assets.Instances("stake_wall".GetStableHashCode()).ToList())
                        if(v.GetZDO().GetBool("bob_sw_testwall",false)){v.ClaimOwnership();ZNetScene.instance.Destroy(v.gameObject);removed++;}
                    output(new JObject{["removed"]=removed});
                    break;
                }
                case "hover":
                {
                    if(stone==null){error("siege hover: no Warstone loaded");break;}
                    var lines=new JArray(stone.Hover());
                    foreach(Planted staff in Planted.Loaded.Where(p=>p!=null))lines.Add(staff.GetHoverText());
                    output(new JObject{["hover"]=lines,["station"]=stone.GetComponent<CraftingStation>().GetLevel()});
                    break;
                }
                case "recipes":
                {
                    Player me=Player.m_localPlayer;
                    var list=ObjectDB.instance.m_recipes.Where(r=>r!=null&&r.m_craftingStation!=null&&r.m_craftingStation.m_name==Stone.StationName).Select(r=>new JObject{
                        ["item"]=r.m_item?.m_itemData.m_shared.m_name,["level"]=r.m_minStationLevel,["known"]=me!=null&&me.IsRecipeKnown(r.m_item.m_itemData.m_shared.m_name),
                        ["needs"]=string.Join(", ",r.m_resources.Select(q=>$"{q.m_amount} {q.m_resItem?.m_itemData.m_shared.m_name} (+{q.m_amountPerLevel})"))}).ToArray();
                    PieceTable hammer=ObjectDB.instance.GetItemPrefab("Hammer")?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
                    output(new JObject{["recipes"]=new JArray(list),["inHammer"]=hammer!=null&&hammer.m_pieces.Any(p=>p!=null&&p.name==Stone.PrefabName),
                        ["pieceKnown"]=me!=null&&me.IsRecipeKnown(Stone.StationName)});
                    break;
                }
                case "horn":
                {
                    // The Shift+E path, pressed twice as a player would (the first asks, the second calls).
                    Player me=Player.m_localPlayer;
                    if(stone==null||me==null){error("siege horn: no Warstone loaded");break;}
                    bool first=stone.Horn(me),second=stone.Horn(me);
                    output(new JObject{["first"]=first,["second"]=second,["phase"]=stone.Phase.ToString()});
                    break;
                }
                case "tryplant":
                {
                    // The Use path: plant whatever the player holds where they look.
                    Player me=Player.m_localPlayer;
                    if(me==null){error("siege tryplant: no player");break;}
                    int before=Planted.Loaded.Count;
                    bool handled=Planted.TryPlant(me);
                    output(new JObject{["handled"]=handled,["held"]=me.RightItem?.m_shared.m_name,["planted"]=Planted.Loaded.Count-before});
                    break;
                }
                case "craftui":
                {
                    // Open (or with "close", shut) the stone's crafting menu, as E does.
                    Player me=Player.m_localPlayer;
                    if(args.Length>2&&args[2]=="close"){InventoryGui.instance.Hide();output(new JObject{["closed"]=true});break;}
                    if(stone==null||me==null){error("siege craftui: no Warstone loaded");break;}
                    me.SetCraftingStation(stone.GetComponent<CraftingStation>());InventoryGui.instance.Show(null,3);
                    output(new JObject{["open"]=InventoryGui.IsVisible()});
                    break;
                }
                case "raid":
                {
                    // The host starting a base raid at the stone, as the game would: Shieldwall should turn it into a siege.
                    if(stone==null||RandEventSystem.instance==null){error("siege raid: needs a loaded Warstone");break;}
                    string name=args.Length>2?args[2]:RandEventSystem.instance.m_events.Where(e=>e!=null&&e.m_enabled&&e.m_random&&e.m_nearBaseOnly&&e.m_spawn.Count>0).Select(e=>e.m_name).FirstOrDefault();
                    if(name==null||!RandEventSystem.instance.HaveEvent(name)){error("siege raid: no such raid "+name);break;}
                    RandEventSystem.instance.SetRandomEventByName(name,stone.transform.position+Vector3.forward*20);
                    RandomEvent running=RandEventSystem.instance.GetCurrentRandomEvent();
                    output(new JObject{["raid"]=name,["gameRaidRunning"]=running!=null?running.m_name:null,["stonePhase"]=stone.Phase.ToString()});
                    break;
                }
                case "remove":
                {
                    if(stone==null||!stone.View.IsValid()){error("siege remove: no Warstone loaded");break;}
                    if(stone.Phase!=Phase.Idle){error("siege remove: end its siege first");break;}
                    stone.View.ClaimOwnership();ZNetScene.instance.Destroy(stone.gameObject);
                    output(new JObject{["removed"]="the nearest Warstone"});
                    break;
                }
                case "place":
                {
                    Player me=Player.m_localPlayer;
                    if(me==null||Stone.Prefab==null){error("siege place: no player or no Warstone prefab");break;}
                    float metres=args.Length>2&&float.TryParse(args[2],out float f)?f:6;
                    Vector3 ahead=me.transform.forward;ahead.y=0;ahead.Normalize();
                    Vector3 at=me.transform.position+ahead*metres;
                    if(args.Length>3&&float.TryParse(args[2],out float px)&&float.TryParse(args[3],out float pz)){at=new Vector3(px,0,pz);ahead=(me.transform.position-at);ahead.y=0;ahead=-ahead.normalized;}
                    if(ZoneSystem.instance.GetSolidHeight(at,out float h))at.y=h;
                    GameObject go=UnityEngine.Object.Instantiate(Stone.Prefab,at,Quaternion.LookRotation(-ahead));
                    go.GetComponent<Piece>()?.SetCreator(me.GetPlayerID(),Splatform.PlatformManager.DistributionPlatform.LocalUser.PlatformUserID);
                    output(new JObject{["placed"]=new JArray(Math.Round(at.x,1),Math.Round(at.y,1),Math.Round(at.z,1))});
                    break;
                }
                case "plant":
                {
                    if(stone==null||args.Length<3){error("siege plant <prefab> [metres] [angle]: needs a loaded Warstone");break;}
                    GameObject item=Items.Get(args[2])??Assets.Find(args[2]);
                    ItemDrop drop=item!=null?item.GetComponent<ItemDrop>():null;
                    if(drop==null){error("siege plant: no item "+args[2]);break;}
                    float metres=args.Length>3&&float.TryParse(args[3],out float f)?f:5,angle=args.Length>4&&float.TryParse(args[4],out float a)?a:0;
                    Vector3 at=stone.transform.position+Quaternion.Euler(0,angle,0)*Vector3.forward*metres;
                    if(ZoneSystem.instance.GetGroundHeight(at,out float h))at.y=h;
                    ItemDrop.ItemData data=drop.m_itemData.Clone();data.m_dropPrefab=item;
                    Planted.Make(data,at,Quaternion.identity);
                    output(new JObject{["planted"]=args[2],["at"]=new JArray(Math.Round(at.x,1),Math.Round(at.y,1),Math.Round(at.z,1))});
                    break;
                }
                default:error("siege: status, plan, route, start, end, marks, place or plant");break;
            }
            return null;
        }
    }
}
