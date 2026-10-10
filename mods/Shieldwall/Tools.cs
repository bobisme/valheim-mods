using System;
using System.Collections;
using System.Collections.Generic;
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
        private const string ClaudeToolsGuid="com.quad.claudetools";
        private const string OldClaudeToolsGuid="com.dhack.claudetools"; // (Claude Tools before 1.2.2)
        private static BaseUnityPlugin _tools;
        private static float _nextCheck;

        internal static void Tick()
        {
            if(Time.unscaledTime<_nextCheck)return;
            _nextCheck=Time.unscaledTime+5;
            BaseUnityPlugin found=BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(ClaudeToolsGuid,out PluginInfo info)||BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(OldClaudeToolsGuid,out info)?info.Instance:null;
            if(found==_tools)return;
            _tools=found;
            if(found==null)return;
            try
            {
                found.GetType().GetMethod("RegisterCommand",BindingFlags.Public|BindingFlags.Static)?.Invoke(null,new object[]{Plugin.Name,"siege",
                    "siege status | plan [stage] [marks] [players] [boasts] | route | start [stage 0-6] [boasts] | end | council [close] | horn [boasts] | offer | boon <id> | saga | boasts | marks <n> | place [metres | x z] | remove | horn | craftui [close] | raid [event] | plant <stave prefab|none> [metres] [angle] | tower <stave> [metres] [angle] [storeys] | line x1 z1 x2 z2 [piece] | unbuild | put <prefab> <metres> <angle> | goto x z | moat [radius] [passes] | focus [x z] | unplant | raiders | wall [radius] [gap degrees] | unwall | hover: Warstones near the player and their sieges; a preview of a siege's waves; "+
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
        private const string TestKey="bob_sw_testwall";
        // A piece made by a test command: player-built (the horde treats it as yours), marked for unbuild.
        private static void Built(GameObject go)
        {
            if(go==null)return;
            Player me=Player.m_localPlayer;
            if(me!=null)go.GetComponent<Piece>()?.SetCreator(me.GetPlayerID(),Splatform.PlatformManager.DistributionPlatform.LocalUser.PlatformUserID);
            go.GetComponent<ZNetView>()?.GetZDO()?.Set(TestKey,true);
        }
        private static Vector3? _focus; // "siege focus x z": the stone the commands act on is the one nearest there, not the player
        private static Warstone Nearest()
        {
            Player me=Player.m_localPlayer;
            if(me==null)return null;
            Vector3 from=_focus??me.transform.position;
            return Warstone.Loaded.Where(w=>w!=null&&w.Z!=null).OrderBy(w=>Utils.DistanceXZ(w.transform.position,from)).FirstOrDefault();
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
                ["staves"]=new JArray(Planted.Loaded.Where(p=>p!=null&&Vector3.Distance(p.transform.position,w.transform.position)<=Policy.WardRadius+10).Select(p=>p.Name).ToArray()),
            };
        }
        // Boasts by name or number, comma-separated: "BloodMoon,Fog" or "0,1".
        private static int Boasts(string text)
        {
            int mask=0;
            foreach(string part in text.Split(','))
                if(Enum.TryParse(part.Trim(),true,out Boast b)&&Enum.IsDefined(typeof(Boast),b))mask|=1<<(int)b;
            return mask;
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
                    int stage=args.Length>2&&int.TryParse(args[2],out int s)?s:-1,marks=args.Length>3&&int.TryParse(args[3],out int m)?m:stone?.Strength??0,
                        players=args.Length>4&&int.TryParse(args[4],out int p)?p:1;
                    if(stage<0)stage=Policy.SiegeStage(Director.StageNow(),marks);
                    int planBoasts=args.Length>5?Boasts(args[5]):0;
                    var plan=Policy.Plan(stage,marks,players,12345,planBoasts);
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
                    Director.TestBoasts=args.Length>3?Boasts(args[3]):0;
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
                    foreach(Planted staff in Planted.Loaded.Where(p=>p!=null))lines.Add(staff.Hover());
                    output(new JObject{["hover"]=lines,["station"]=stone.GetComponent<CraftingStation>().GetLevel()});
                    break;
                }
                case "sight":
                {
                    // For each socket: every foe in reach and everything on the line from the stave's head to it.
                    var list=new JArray();
                    foreach(Planted staff in Planted.Loaded.Where(p=>p!=null))
                        list.Add(new JObject{["at"]=new JArray(staff.transform.position.x,staff.transform.position.y,staff.transform.position.z),["sight"]=new JArray(staff.Sightlines().ToArray())});
                    output(new JObject{["sockets"]=list});
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
                    // The war council's horn, with boasts (names or numbers, comma-separated; only those the stone offers count).
                    if(stone==null){error("siege horn: no Warstone loaded");break;}
                    Net.Horn(stone,args.Length>2?Boasts(args[2]):0);
                    output(new JObject{["phase"]=stone.Phase.ToString(),["boasts"]=stone.Z.GetInt(Stone.BoastsKey,0)});
                    break;
                }
                case "council":
                {
                    // Open (or close) the war council, as Shift+E on an idle stone does.
                    if(args.Length>2&&args[2]=="close"){Council.Close();output(new JObject{["open"]=false});break;}
                    if(stone==null||Player.m_localPlayer==null){error("siege council: no Warstone loaded");break;}
                    output(new JObject{["horn"]=stone.Horn(Player.m_localPlayer),["open"]=Council.IsOpen});
                    break;
                }
                case "boasts":
                    if(stone==null){error("siege boasts: no Warstone loaded");break;}
                    output(new JObject{["offer"]=new JArray(stone.BoastOffer.Select(b=>b.ToString()).ToArray()),["sworn"]=stone.Z.GetInt(Stone.BoastsKey,0),
                        ["boons"]=new JArray(stone.Boons.ToArray()),["boonOffer"]=new JArray(stone.BoonOffer),["capacity"]=stone.Capacity,["reach"]=stone.Reach});
                    break;
                case "offer":
                    // Offer the stone a boon now, as a held siege does (test).
                    if(stone==null||!stone.View.IsOwner()){error("siege offer: on a stone this game owns");break;}
                    Director.Offer(stone);
                    output(new JObject{["offer"]=new JArray(stone.BoonOffer)});
                    break;
                case "boon":
                    // boon <id>: choose one of the offered boons; boon clear: forget every boon (test).
                    if(stone==null||args.Length<3||Player.m_localPlayer==null){error("siege boon <id|clear>");break;}
                    if(args[2]=="clear"){if(!stone.View.IsOwner()){error("siege boon clear: on a stone this game owns");break;}stone.Z.Set(Stone.BoonsKey,"");stone.Z.Set(Stone.OfferKey,"");}
                    else Net.Choose(stone,args[2],Player.m_localPlayer.GetPlayerName());
                    output(new JObject{["boons"]=new JArray(stone.Boons.ToArray()),["boonOffer"]=new JArray(stone.BoonOffer)});
                    break;
                case "saga":
                    if(stone==null){error("siege saga: no Warstone loaded");break;}
                    Saga.Show(stone.Z.GetString(Stone.SagaTopicKey,""),stone.Z.GetString(Stone.SagaKey,""));
                    output(new JObject{["topic"]=stone.Z.GetString(Stone.SagaTopicKey,""),["saga"]=stone.Z.GetString(Stone.SagaKey,""),["tally"]=stone.Z.GetString(Stone.TallyKey,"")});
                    break;
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
                case "goto":
                {
                    // goto <x> <z>: move the player there (test worlds: so this game owns and runs what is built there).
                    Player me=Player.m_localPlayer;
                    if(me==null||args.Length<4||!float.TryParse(args[2],out float gx)||!float.TryParse(args[3],out float gz)){error("siege goto <x> <z>");break;}
                    Vector3 to=new Vector3(gx,0,gz);
                    to.y=ZoneSystem.instance.GetGroundHeight(to,out float gh)?gh+0.5f:me.transform.position.y;
                    me.TeleportTo(to,me.transform.rotation,false);
                    output(new JObject{["goto"]=new JArray(Math.Round(to.x,1),Math.Round(to.y,1),Math.Round(to.z,1))});
                    break;
                }
                case "put":
                {
                    // put <prefab> <metres> <angle>: any piece beside the nearest stone, facing it (test).
                    if(stone==null||args.Length<5||!float.TryParse(args[3],out float pm)||!float.TryParse(args[4],out float pa)){error("siege put <prefab> <metres> <angle>");break;}
                    GameObject prefab=ZNetScene.instance.GetPrefab(args[2]);
                    if(prefab==null){error("siege put: no prefab "+args[2]);break;}
                    Vector3 at=stone.transform.position+Quaternion.Euler(0,pa,0)*Vector3.forward*pm;
                    if(ZoneSystem.instance.GetGroundHeight(at,out float h))at.y=h;
                    Vector3 face=stone.transform.position-at;face.y=0;
                    Built(UnityEngine.Object.Instantiate(prefab,at,Quaternion.LookRotation(face.sqrMagnitude>0.01f?face:Vector3.forward)));
                    output(new JObject{["put"]=args[2],["level"]=stone.Level});
                    break;
                }
                case "moat":
                {
                    // moat [radius=12] [depth passes=4]: dig a ring of pits round the stone with the game's own digging (test).
                    if(stone==null){error("siege moat: needs a loaded Warstone");break;}
                    float radius=args.Length>2&&float.TryParse(args[2],out float mr)?mr:12;int passes=args.Length>3&&int.TryParse(args[3],out int mp)?mp:4;
                    int count=Mathf.CeilToInt(2*Mathf.PI*radius/1.5f);
                    for(int pass=0;pass<passes;pass++)
                        for(int i=0;i<count;i++)
                        {
                            Vector3 at=stone.transform.position+Quaternion.Euler(0,360f*i/count,0)*Vector3.forward*radius;
                            if(ZoneSystem.instance.GetGroundHeight(at,out float h))at.y=h;
                            Assets.Dig(at);
                        }
                    output(new JObject{["moat"]=radius,["digs"]=count*passes});
                    break;
                }
                case "focus":
                {
                    if(args.Length>3&&float.TryParse(args[2],out float fx)&&float.TryParse(args[3],out float fz))_focus=new Vector3(fx,0,fz);else _focus=null;
                    output(new JObject{["focus"]=_focus.HasValue?new JArray(_focus.Value.x,_focus.Value.z):null});
                    break;
                }
                case "strays":
                {
                    // Every Warstone this game knows of, loaded or not (a host knows the whole world; a client only what is near it).
                    var zdos=new List<ZDO>();int index=0;
                    while(!ZDOMan.instance.GetAllZDOsWithPrefabIterative(Stone.PrefabName,zdos,ref index)){}
                    Vector3 me=Player.m_localPlayer!=null?Player.m_localPlayer.transform.position:Vector3.zero;
                    output(new JObject{["host"]=ZNet.instance.IsServer(),["stones"]=new JArray(zdos.Select(z=>new JObject{
                        ["position"]=new JArray(Math.Round(z.GetPosition().x,1),Math.Round(z.GetPosition().y,1),Math.Round(z.GetPosition().z,1)),
                        ["distance"]=Math.Round(Vector3.Distance(z.GetPosition(),me)),["owner"]=z.GetOwner(),["marks"]=z.GetInt(Stone.MarksKey)}).ToArray())});
                    break;
                }
                case "purge":
                {
                    // purge <x> <z>: delete the Warstone standing there, loaded or not (the strays of a build gone wrong).
                    if(args.Length<4||!float.TryParse(args[2],out float px)||!float.TryParse(args[3],out float pz)){error("siege purge <x> <z>");break;}
                    var zdos=new List<ZDO>();int index=0;
                    while(!ZDOMan.instance.GetAllZDOsWithPrefabIterative(Stone.PrefabName,zdos,ref index)){}
                    ZDO z=zdos.Where(o=>Utils.DistanceXZ(o.GetPosition(),new Vector3(px,0,pz))<3).OrderBy(o=>Utils.DistanceXZ(o.GetPosition(),new Vector3(px,0,pz))).FirstOrDefault();
                    if(z==null){error("siege purge: no Warstone within 3 m of that spot");break;}
                    if(z.GetInt(Stone.PhaseKey)!=0){error("siege purge: its siege is on");break;}
                    ZNetView loaded=ZNetScene.instance.FindInstance(z);
                    z.SetOwner(ZDOMan.GetSessionID());
                    if(loaded!=null)ZNetScene.instance.Destroy(loaded.gameObject);else ZDOMan.instance.DestroyZDO(z);
                    output(new JObject{["purged"]=new JArray(Math.Round(z.GetPosition().x,1),Math.Round(z.GetPosition().y,1),Math.Round(z.GetPosition().z,1))});
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
                case "tower":
                {
                    // plant <stave> [metres] [angle]: a socket with that stave on the ground beside the stone.
                    // tower <stave> [metres] [angle] [storeys]: stone pillars (2 m each) with the socket on top.
                    if(stone==null||args.Length<3){error($"siege {args[1]} <stave prefab|none> [metres] [angle] [storeys]: needs a loaded Warstone");break;}
                    ItemDrop.ItemData data=null;
                    if(args[2]!="none")
                    {
                        GameObject item=Items.Get(args[2])??Assets.Find(args[2]);
                        ItemDrop drop=item!=null?item.GetComponent<ItemDrop>():null;
                        if(drop==null){error("siege plant: no item "+args[2]);break;}
                        data=drop.m_itemData.Clone();data.m_dropPrefab=item;
                    }
                    float metres=args.Length>3&&float.TryParse(args[3],out float f)?f:6,angle=args.Length>4&&float.TryParse(args[4],out float a2)?a2:0;
                    int storeys=args[1]=="tower"?(args.Length>5&&int.TryParse(args[5],out int st2)?st2:2):0;
                    Vector3 at=stone.transform.position+Quaternion.Euler(0,angle,0)*Vector3.forward*metres;
                    if(ZoneSystem.instance.GetGroundHeight(at,out float h))at.y=h;
                    GameObject pillar=ZNetScene.instance.GetPrefab("stone_pillar");
                    for(int i=0;i<storeys&&pillar!=null;i++)Built(UnityEngine.Object.Instantiate(pillar,at+Vector3.up*(1+2*i),Quaternion.identity));
                    at.y+=2*storeys-(storeys>0?0.05f:0); // seated a little into the pillar, as the hammer would
                    Built(Planted.Make(data,at,Quaternion.LookRotation(stone.transform.position-at,Vector3.up)*Quaternion.identity));
                    output(new JObject{["socket"]=args[2],["at"]=new JArray(Math.Round(at.x,1),Math.Round(at.y,1),Math.Round(at.z,1)),["storeys"]=storeys});
                    break;
                }
                case "line":
                {
                    // line <x1> <z1> <x2> <z2> [piece=stake_wall]: a wall of pieces between two points (test).
                    if(args.Length<6||!float.TryParse(args[2],out float x1)||!float.TryParse(args[3],out float z1)||!float.TryParse(args[4],out float x2)||!float.TryParse(args[5],out float z2))
                    {error("siege line <x1> <z1> <x2> <z2> [piece]");break;}
                    GameObject wall=ZNetScene.instance.GetPrefab(args.Length>6?args[6]:"stake_wall");
                    if(wall==null){error("siege line: no piece "+(args.Length>6?args[6]:""));break;}
                    float width=wall.GetComponentsInChildren<BoxCollider>().Select(c=>c.size.x*c.transform.lossyScale.x).DefaultIfEmpty(2).Max();
                    Vector3 start=new Vector3(x1,0,z1),end=new Vector3(x2,0,z2);
                    int count=Mathf.Max(1,Mathf.CeilToInt(Vector3.Distance(start,end)/(width*0.95f)));
                    Quaternion facing=Quaternion.LookRotation(Vector3.Cross(Vector3.up,(end-start).normalized));
                    for(int i=0;i<count;i++)
                    {
                        Vector3 at=Vector3.Lerp(start,end,(i+0.5f)/count);
                        if(ZoneSystem.instance.GetGroundHeight(at,out float h))at.y=h;
                        Built(UnityEngine.Object.Instantiate(wall,at,facing));
                    }
                    output(new JObject{["walls"]=count,["piece"]=wall.name});
                    break;
                }
                case "unbuild":
                {
                    // Remove every test piece (walls, pillars, sockets) placed by these commands.
                    int removed=0;
                    foreach(ZNetView v in Assets.AllInstances().Where(v=>v.GetZDO().GetBool(TestKey,false)).ToList()){v.ClaimOwnership();ZNetScene.instance.Destroy(v.gameObject);removed++;}
                    output(new JObject{["removed"]=removed});
                    break;
                }
                default:error("siege: status, plan, route, start, end, marks, place or plant");break;
            }
            return null;
        }
    }
}
