using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace MeadowGolf
{
    public sealed partial class Plugin
    {
        private BaseUnityPlugin _tools;private float _nextTools;
        private void CommandsTick()
        {
            if(Time.unscaledTime<_nextTools)return;_nextTools=Time.unscaledTime+2;
            BaseUnityPlugin tools=Chainloader.PluginInfos.TryGetValue("com.dhack.claudetools",out var info)?info.Instance:null;
            if(tools==_tools)return;_tools=tools;if(tools==null)return;
            try
            {
                tools.GetType().GetMethod("RegisterCommand",BindingFlags.Static|BindingFlags.Public)?.Invoke(null,new object[]{Name,"golf",
                    "golf status | match 9|18|join|end|stop|scores | roundtest 9|18 | card open|close | shot drive|chip|putt <power=0..1> <dx> <dz> | predict <mode> <power> <dx> <dz> | playtest <mode> <power> <dx> <dz> | fixture floor|basement|ramp | animation | listen | reload | testcourse <length=2..30> | starttest | testclear: tests/reload only in local Creative; ordinary shots require the club and a nearby own ball",
                    new Func<string[],Action<JObject>,Action<string>,IEnumerator>(GolfCommand)});
            }
            catch(Exception e){Logger.LogWarning("Golf ClaudeTools link: "+e.GetBaseException().Message);}
        }
        private void CommandsStop()
        {if(_tools!=null)try{_tools.GetType().GetMethod("UnregisterAll",BindingFlags.Static|BindingFlags.Public)?.Invoke(null,new object[]{Name});}catch(Exception){} _tools=null;}
        private static JArray Vec(Vector3 v)=>new JArray(v.x,v.y,v.z);
        private static float Number(string s)
        {if(!float.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out float n)||!Rules.Finite(n))throw new ArgumentException("Use finite numbers.");return n;}
        private static bool Creative()=>Player.m_localPlayer!=null&&ZNet.instance!=null&&ZNet.instance.IsServer()&&ZNet.instance.GetWorldName()=="Creative";
        private IEnumerator GolfCommand(string[] args,Action<JObject> output,Action<string> error)
        {
            try
            {
                string command=args.Length>1?args[1]:"status";
                if(command=="roundtest"&&args.Length==3)
                {
                    if(!Creative()||(args[2]!="9"&&args[2]!="18"))throw new InvalidOperationException("golf roundtest 9|18 requires local Creative.");
                    return RoundTest(int.Parse(args[2],CultureInfo.InvariantCulture),output);
                }
                if(command=="animation")
                {
                    var animator=Player.m_localPlayer?.GetComponentInChildren<Animator>();
                    output(new JObject{["clips"]=new JArray((animator?.runtimeAnimatorController?.animationClips??new AnimationClip[0]).Where(c=>c.events.Any(e=>e.functionName.Contains("Attack"))).Take(16).Select(c=>new JObject{
                        ["name"]=c.name,["length"]=c.length,["events"]=new JArray(c.events.Select(e=>new JObject{["time"]=e.time,["function"]=e.functionName}))}))});return null;
                }
                if(command=="reload")
                {
                    if(!Creative())throw new InvalidOperationException("Developer reload is restricted to locally hosted Creative.");
                    GolfReload.Schedule();output(new JObject{["golfReloadScheduled"]=true});return null;
                }
                if(command=="match"&&args.Length==3)
                {
                    if(args[2]=="9"||args[2]=="18")GolfMatches.RequestMatch(int.Parse(args[2],CultureInfo.InvariantCulture));
                    else if(args[2]=="join")GolfMatches.RequestMatch(0,true);
                    else if(args[2]=="end")GolfMatches.EndMatch();
                    else if(args[2]=="stop")GolfMatches.StopRound();
                    else if(args[2]=="scores")GolfMatches.RefreshBoard();
                    else throw new ArgumentException("golf match 9|18|join|end|stop|scores");
                    output(new JObject{["matchRequest"]=args[2]});return null;
                }
                if(command=="status")
                {
                    var club=Prefabs.ClubPrefab?.GetComponent<ItemDrop>();
                    output(new JObject{["version"]=Version,["cardOpen"]=_card,["nativeUI"]=_nativeUI!=null&&_nativeUI.activeInHierarchy,["uiFont"]=_nativeFont?.name,["headingFont"]=_titleFont?.name,["previewScenes"]=Enumerable.Range(0,UnityEngine.SceneManagement.SceneManager.sceneCount).Count(i=>UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).name.StartsWith("MeadowGolfAim-")),["assembly"]=typeof(Plugin).Assembly.ManifestModule.ModuleVersionId.ToString(),["instance"]=GetInstanceID(),["sounds"]=new JObject{["created"]=ShotPhysics.SoundsCreated,["last"]=ShotPhysics.LastSoundName,["playing"]=ShotPhysics.LastSound!=null&&ShotPhysics.LastSound.IsPlaying(),["swingClips"]=ZNetScene.instance?.GetPrefab("sfx_club_swing")?.GetComponent<ZSFX>()?.m_audioClips.Length??0,["hitClips"]=ZNetScene.instance?.GetPrefab("sfx_wood_hit")?.GetComponent<ZSFX>()?.m_audioClips.Length??0},["active"]=Active,["preview"]=new JObject{["complete"]=_preview.Complete,["hazard"]=_preview.Hazard,["reason"]=_preview.Reason,["distance"]=_preview.Distance,["end"]=Vec(_preview.End),["surfaces"]=_preview.SurfaceCount,["maximumSlice"]=_preview.MaximumSlice,["milliseconds"]=_preview.Milliseconds},["clubRegistered"]=club!=null,["clubAnimation"]=club?.m_itemData.m_shared.m_attack.m_attackAnimation,
                        ["markers"]=new JArray(GolfMarker.Loaded.Where(m=>m!=null&&m.View.IsValid()).Select(m=>new JObject{["kind"]=m.Cup?"cup":"tee",["label"]=m.GetText(),["position"]=Vec(m.transform.position)})),
                        ["balls"]=new JArray(GolfBall.Loaded.Where(b=>b!=null&&b.Data!=null).Select(b=>new JObject{["id"]=b.Data.m_uid.ToString(),["sequence"]=b.Data.GetLong("bob_golf_sequence",0),["pending"]=b.Pending,["impactDelay"]=b.LastImpactDelay,["impactSource"]=b.LastImpactSource,["shotRejection"]=b.LastRejectedShot,["spin"]=Vec(b.Body.angularVelocity),["player"]=b.Data.GetString(GolfWorld.NameKey,""),["mine"]=b.Mine,
                            ["owner"]=b.View.IsOwner(),["position"]=Vec(b.transform.position),["velocity"]=Vec(b.Body.linearVelocity),["kinematic"]=b.Body.isKinematic,["still"]=b.Still,["done"]=b.Done,
                            ["strokes"]=b.Strokes,["label"]=b.Data.GetString(GolfWorld.LabelKey,""),["card"]=b.Data.GetString(GolfWorld.CardKey,""),["match"]=b.Data.GetString(GolfMatches.Id,""),["holes"]=b.Data.GetInt(GolfMatches.Length,0),["matchState"]=GolfMatches.State(b.Data),["closed"]=b.Closed}))});return null;
                }
                if(command=="ui")
                {output(new JObject{["images"]=new JArray((InventoryGui.instance?.m_player?.GetComponentsInChildren<UnityEngine.UI.Image>(true)??new UnityEngine.UI.Image[0]).Where(i=>i.sprite!=null).Take(40).Select(i=>new JObject{["name"]=i.name,["sprite"]=i.sprite.name,["type"]=i.type.ToString(),["size"]=new JArray(i.rectTransform.rect.width,i.rectTransform.rect.height),["color"]=new JArray(i.color.r,i.color.g,i.color.b,i.color.a),["border"]=new JArray(i.sprite.border.x,i.sprite.border.y,i.sprite.border.z,i.sprite.border.w)}))});return null;}
                if(command=="scores")
                {output(new JObject{["match"]=GolfMatches.BoardId,["rows"]=new JArray(GolfMatches.Board.Select(row=>new JObject{["name"]=row.Name,["holes"]=row.Holes,["card"]=row.Card,["state"]=row.State}))});return null;}
                if(command=="listen")return SoundCheck(output);
                if(command=="card"&&args.Length==3)
                {
                    if(!Active)throw new InvalidOperationException("Equip the club with menus closed.");
                    if(args[2]!="open"&&args[2]!="close")throw new ArgumentException("golf card open|close");
                    _card=args[2]=="open";if(_card)GolfMatches.RefreshBoard();_charging=false;output(new JObject{["cardOpen"]=_card});return null;
                }
                if(command=="fixture"&&args.Length==3)
                {
                    if(!Creative())throw new InvalidOperationException("Disposable fixtures are restricted to local Creative.");
                    GolfBall ball=MyBall();if(ball==null)throw new InvalidOperationException("Start a hole first.");
                    if(args[2]!="floor"&&args[2]!="basement"&&args[2]!="ramp"&&args[2]!="forest"&&args[2]!="water")throw new ArgumentException("golf fixture floor|basement|ramp|forest|water");
                    return FixtureTest(ball,args[2],output);
                }
                if(command=="predict"&&args.Length==6)
                {
                    int mode=Array.IndexOf(Modes,CultureInfo.InvariantCulture.TextInfo.ToTitleCase(args[2]));float power=Number(args[3]);
                    Vector3 direction=new Vector3(Number(args[4]),0,Number(args[5]));GolfBall ball=MyBall();
                    if(ball==null||mode<0||power<0||power>1||direction.sqrMagnitude<.001f)throw new ArgumentException("Supply mode, power 0..1 and a nonzero direction; start a hole first.");
                    _preview.Predict(ball,mode,power,direction.normalized,true);
                    output(new JObject{["complete"]=_preview.Complete,["hazard"]=_preview.Hazard,["reason"]=_preview.Reason,["distance"]=_preview.Distance,["end"]=Vec(_preview.End),["maximumSlice"]=_preview.MaximumSlice,["milliseconds"]=_preview.Milliseconds,["surfaces"]=_preview.SurfaceCount});return null;
                }
                if(command=="playtest"&&args.Length==6)
                {
                    if(!Creative())throw new InvalidOperationException("Physics playtests are restricted to locally hosted Creative.");
                    int mode=Array.IndexOf(Modes,CultureInfo.InvariantCulture.TextInfo.ToTitleCase(args[2]));float power=Number(args[3]);
                    Vector3 direction=new Vector3(Number(args[4]),0,Number(args[5]));GolfBall ball=MyBall();
                    if(ball==null||mode<0||power<0||power>1||direction.sqrMagnitude<.001f)throw new ArgumentException("Supply mode, power 0..1 and a nonzero direction; start a hole first.");
                    return PhysicsTest(ball,mode,power,direction.normalized,output);
                }
                if(command=="starttest")
                {
                    if(!Creative())throw new InvalidOperationException("Test starts are restricted to local Creative.");
                    var tee=GolfMarker.Loaded.FirstOrDefault(m=>m!=null&&!m.Cup&&m.View.IsValid()&&m.View.GetZDO().GetLong("bob_golf_test",0)==Player.m_localPlayer.GetPlayerID()&&Vector3.Distance(m.transform.position,Player.m_localPlayer.transform.position)<=5);
                    var cup=GolfMarker.Loaded.FirstOrDefault(m=>m!=null&&m.Cup&&m.View.IsValid()&&m.View.GetZDO().GetLong("bob_golf_test",0)==Player.m_localPlayer.GetPlayerID());
                    if(tee==null||cup==null||!Active)throw new InvalidOperationException("Equip the club and stand within 5 m of a tagged test tee.");
                    GolfWorld.Start(tee,cup,false);output(new JObject{["requestedStart"]=true});return null;
                }
                if(command=="shot"&&args.Length==6)
                {
                    int mode=Array.IndexOf(Modes,CultureInfo.InvariantCulture.TextInfo.ToTitleCase(args[2]));float power=Number(args[3]);
                    Vector3 direction=new Vector3(Number(args[4]),0,Number(args[5]));GolfBall b=MyBall();
                    if(!Active||b==null||!b.Still||b.Done||Vector3.Distance(Player.m_localPlayer.transform.position,b.transform.position)>3||direction.sqrMagnitude<.001f||mode<0||power<0||power>1)
                        throw new InvalidOperationException("Equip the club and stand within 3 m of your stationary own ball. Supply mode, power 0..1 and a nonzero direction.");
                    Swing(Player.m_localPlayer,b,mode,power,direction.normalized);output(new JObject{["requested"]=true});return null;
                }
                if(command=="testcourse"&&args.Length==3)
                {
                    if(!Creative())throw new InvalidOperationException("Test courses are only allowed in the locally hosted Creative world.");
                    float length=Number(args[2]);if(length<2||length>30)throw new ArgumentException("Test length must be 2–30 m.");
                    Player p=Player.m_localPlayer;Vector3 forward=p.transform.forward;forward.y=0;forward.Normalize();
                    Vector3 start=Floor(p.transform.position+forward*1.5f),end=Floor(p.transform.position+forward*(1.5f+length));
                    var tee=UnityEngine.Object.Instantiate(Prefabs.TeePrefab,start,Quaternion.LookRotation(forward));
                    var cup=UnityEngine.Object.Instantiate(Prefabs.CupPrefab,end,Quaternion.identity);
                    foreach(var go in new[]{tee,cup})
                    {
                        go.GetComponent<Piece>().SetCreator(p.GetPlayerID(),Splatform.PlatformManager.DistributionPlatform.LocalUser.PlatformUserID);var z=go.GetComponent<ZNetView>().GetZDO();z.Set(GolfWorld.LabelKey,"Creative golf:1:3");z.Set("bob_golf_test",p.GetPlayerID());
                    }
                    output(new JObject{["tee"]=Vec(start),["cup"]=Vec(end),["label"]="Creative golf:1:3"});return null;
                }
                if(command=="testclear")
                {
                    if(!Creative())throw new InvalidOperationException("Test cleanup is only allowed in the locally hosted Creative world.");
                    int removed=0;long player=Player.m_localPlayer.GetPlayerID();
                    foreach(var marker in GolfMarker.Loaded.ToArray())if(marker!=null&&marker.View.IsValid()&&marker.View.GetZDO().GetLong("bob_golf_test",0)==player)
                    {marker.View.ClaimOwnership();ZNetScene.instance.Destroy(marker.gameObject);removed++;}
                    output(new JObject{["removedTestMarkers"]=removed});return null;
                }
                throw new ArgumentException("golf status | reload (local Creative, Golf only) | shot drive|chip|putt <power> <dx> <dz> | testcourse <length> | testclear");
            }
            catch(Exception e){error(e.GetBaseException().Message);return null;}
        }
        private IEnumerator SoundCheck(Action<JObject> output)
        {
            for(int i=0;i<8;i++)
            {
                yield return new WaitForSecondsRealtime(.08f);
                output(new JObject{["sound"]=ShotPhysics.LastSoundName,["created"]=ShotPhysics.SoundsCreated,["playing"]=ShotPhysics.LastSound!=null&&ShotPhysics.LastSound.IsPlaying()});
            }
        }
        private IEnumerator PhysicsTest(GolfBall source,int mode,float power,Vector3 direction,Action<JObject> output,Vector3? start=null)
        {
            using(var preview=new ShotPreview())
            {
                preview.Predict(source,mode,power,direction,true,start);
                var result=new JObject{["mode"]=Modes[mode],["power"]=power,["predictedComplete"]=preview.Complete,["predictedHazard"]=preview.Hazard,["predictedDistance"]=preview.Distance,
                    ["predictedEnd"]=Vec(preview.End),["predictionMilliseconds"]=preview.Milliseconds,["surfaces"]=preview.SurfaceCount};
                Vector3 origin=start??source.Body.position;GolfProbe probe=GolfProbe.Create(source,start);
                try
                {
                    ShotPhysics.Launch(probe.Body,mode,power,direction);
                    ZDO cup=ZDOMan.instance?.GetZDO(source.Data.GetZDOID(GolfWorld.CupKey));float started=Time.time;
                    while(Time.time-started<25&&!probe.Body.IsSleeping())
                    {
                        yield return new WaitForFixedUpdate();
                        if(cup!=null){Vector3 d=probe.Body.position-(cup.GetPosition()+Vector3.up*ShotPhysics.Radius);
                            if(Rules.Captures(new Vector2(d.x,d.z).magnitude,d.y,probe.Body.linearVelocity.magnitude,1))break;}
                    }
                    Vector3 delta=probe.Body.position-origin;
                    result["actualEnd"]=Vec(probe.Body.position);result["actualDistance"]=new Vector2(delta.x,delta.z).magnitude;
                    result["endpointError"]=preview.Complete?Vector3.Distance(preview.End,probe.Body.position):-1;
                    result["actualHazard"]=probe.Hazard;result["seconds"]=Time.time-started;result["settled"]=probe.Body.IsSleeping();output(result);
                }
                finally {if(probe!=null)Destroy(probe.gameObject);}
            }
        }
        private IEnumerator FixtureTest(GolfBall source,string kind,Action<JObject> output)
        {
            Vector3 direction=Vector3.forward,origin=source.Body.position+Vector3.up*(kind=="basement"?-100:100);
            var fixtures=new System.Collections.Generic.List<GameObject>();
            PhysicsMaterial material=ZNetScene.instance.GetPrefab("wood_floor")?.GetComponentInChildren<Collider>()?.sharedMaterial;
            try
            {
                var floor=new GameObject("Disposable Golf floor"){layer=LayerMask.NameToLayer("piece")};fixtures.Add(floor);floor.AddComponent<GolfTestLifetime>();
                floor.transform.position=origin+Vector3.forward*8-Vector3.up*(ShotPhysics.Radius+.1f);
                var box=floor.AddComponent<BoxCollider>();box.size=new Vector3(6,.2f,30);box.sharedMaterial=material;
                if(kind=="forest")
                {
                    floor.AddComponent<GolfSurface>().Override=Rules.Surface.Forest;
                    var tree=new GameObject("Disposable Golf tree collider"){layer=LayerMask.NameToLayer("static_solid")};fixtures.Add(tree);tree.AddComponent<GolfTestLifetime>();
                    tree.transform.position=origin+Vector3.forward*3+Vector3.right*.38f+Vector3.up*1.5f;
                    var trunk=tree.AddComponent<CapsuleCollider>();trunk.radius=.35f;trunk.height=3.5f;trunk.sharedMaterial=material;
                }
                if(kind=="water")
                {
                    var pool=new GameObject("Disposable Golf water volume"){layer=LayerMask.NameToLayer("WaterVolume")};fixtures.Add(pool);pool.AddComponent<GolfTestLifetime>();
                    pool.transform.position=origin+Vector3.forward*5+Vector3.up*.15f;
                    var trigger=pool.AddComponent<BoxCollider>();trigger.isTrigger=true;trigger.size=new Vector3(6,1,6);
                    var water=pool.AddComponent<WaterVolume>();water.enabled=false;water.m_forceDepth=0;
                }
                if(kind=="ramp")
                {
                    var ramp=new GameObject("Disposable Golf ramp"){layer=LayerMask.NameToLayer("piece")};fixtures.Add(ramp);ramp.AddComponent<GolfTestLifetime>();
                    Quaternion rotation=Quaternion.Euler(-8,0,0);Vector3 normal=rotation*Vector3.up;
                    ramp.transform.SetPositionAndRotation(origin+Vector3.forward*4+Vector3.up*(Mathf.Tan(8*Mathf.Deg2Rad)*3)-Vector3.up*ShotPhysics.Radius-normal*.1f,rotation);
                    var incline=ramp.AddComponent<BoxCollider>();incline.size=new Vector3(4,.2f,6);incline.sharedMaterial=material;
                }
                if(kind=="basement")
                {
                    var ceiling=new GameObject("Disposable Golf basement ceiling"){layer=LayerMask.NameToLayer("piece")};fixtures.Add(ceiling);ceiling.AddComponent<GolfTestLifetime>();
                    ceiling.transform.position=origin+Vector3.forward*8+Vector3.up*1.5f;
                    var roof=ceiling.AddComponent<BoxCollider>();roof.size=new Vector3(6,.2f,30);roof.sharedMaterial=material;
                }
                foreach(var go in fixtures)foreach(Character c in Character.GetAllCharacters())if(c!=null)foreach(var collider in c.GetComponentsInChildren<Collider>())Physics.IgnoreCollision(go.GetComponent<Collider>(),collider);
                yield return new WaitForFixedUpdate();
                yield return PhysicsTest(source,kind=="basement"?1:2,kind=="floor"||kind=="forest"||kind=="water"?.6f:kind=="basement"?.5f:.25f,direction,r=>{r["fixture"]=kind;output(r);},origin);
                if(kind=="water")yield return HazardTest(origin,output);
            }
            finally {foreach(var go in fixtures)if(go!=null)Destroy(go);}
        }
        private static Vector3 Floor(Vector3 near)
        {
            if(!Physics.Raycast(near+Vector3.up*.5f,Vector3.down,out RaycastHit hit,2,LayerMask.GetMask("terrain","piece","static_solid","Default"),QueryTriggerInteraction.Ignore))
                throw new InvalidOperationException("Find a clear floor within 1.5 m of your feet for both ends of the test hole.");
            return hit.point;
        }
    }
}
