using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object=UnityEngine.Object;

namespace LocalPortals
{
    // An "lportal" command for Claude Tools, found while the game runs (no reference), to check the portals without building them by hand.
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
                found.GetType().GetMethod("RegisterCommand",BindingFlags.Public|BindingFlags.Static)?.Invoke(null,new object[]{Plugin.Name,"lportal",
                    "lportal place [ahead=6] [right=0] [turn=180] | info | probe | through <n> [steps=40] [film] | straddle <n> [depth=0.1] [turn=0] | name <n> [text] | perf [seconds=3] | camtest <n> [back=4] [rise=0.6] | shaders [filter] | back | remove: test local portals. place puts one ahead of the player (turn 180 faces the player); info lists nearby portals and their links; probe reports the cameras and shaders the views use; through walks the player into portal n (from info) and reports where they came out; back returns them to where through found them; remove takes away the portals this command placed",
                    (Func<string[],Action<JObject>,Action<string>,IEnumerator>)Run});
                Plugin.Log("Claude Tools found: lportal command added");
            }
            catch(Exception e){Plugin.Log("Could not add the lportal command to Claude Tools: "+e.Message);}
        }
        internal static void Stop()
        {
            try{_tools?.GetType().GetMethod("UnregisterAll",BindingFlags.Public|BindingFlags.Static)?.Invoke(null,new object[]{Plugin.Name});}catch(Exception){}
            _tools=null;
        }

        private const string TestKey="bob_lportal_test";
        private static Vector3 _before;
        private static Quaternion _beforeTurn;
        private static float _beforeAt=-1000;

        private static IEnumerator Run(string[] args,Action<JObject> output,Action<string> error)
        {
            Player me=Player.m_localPlayer;
            if(me==null){error("lportal: no player");return null;}
            string sub=args.Length>1?args[1]:"info";
            switch(sub)
            {
                case "place":
                {
                    float ahead=Arg(args,2,6),right=Arg(args,3,0),turn=Arg(args,4,180);
                    if(PortalPrefab.Prefab==null){error("lportal place: prefab missing");break;}
                    Vector3 forward=me.transform.forward;forward.y=0;forward.Normalize();
                    Vector3 at=me.transform.position+forward*ahead+Vector3.Cross(Vector3.up,forward)*right;
                    if(ZoneSystem.instance.GetSolidHeight(at,out float height))at.y=height;
                    GameObject go=Object.Instantiate(PortalPrefab.Prefab,at,Quaternion.LookRotation(forward)*Quaternion.Euler(0,turn,0));
                    go.GetComponent<ZNetView>()?.GetZDO()?.Set(TestKey,true);
                    output(new JObject{["placed"]=V(at),["facing"]=Math.Round(go.transform.eulerAngles.y,1)});
                    break;
                }
                case "info":output(new JObject{["portals"]=new JArray(Nearby(100).Select(Describe)),["drawnLastFrame"]=Views.Drawn,["trips"]=Crossing.Count,["cutting"]=Doubles.PartCount,["cutMs"]=Math.Round(Doubles.Ms,2),["wallsIgnored"]=Crossing.WallsIgnored});break;
                case "probe":output(Probe());break;
                case "camtest":
                {
                    // A player just out of portal n's partner, the camera behind them as the game would want it (distance, height):
                    // what the wall test through the glass gives, and where the camera ends up.
                    var list=Nearby(100).ToList();
                    int n=(int)Arg(args,2,0);
                    if(n<0||n>=list.Count||list[n].Partner==null){error("lportal camtest: no linked portal "+n);break;}
                    Transform b=list[n].Partner.transform,a=list[n].transform;
                    float back=Arg(args,3,4),rise=Arg(args,4,0.6f);
                    Vector3 from=b.TransformPoint(new Vector3(0,1.9f,0.3f)),end=b.TransformPoint(new Vector3(0,1.9f+rise,0.3f-back));
                    Vector3 wanted=end;
                    bool handled=Crossing.CollideCamera(GameCamera.instance,from,ref end);
                    Vector3 carried=Crossing.Point(b,a,end);
                    output(new JObject{["handled"]=handled,["wantedBehind"]=Math.Round(Vector3.Distance(from,wanted),2),["got"]=Math.Round(Vector3.Distance(from,end),2),
                        ["blockedBy"]=Crossing.CameraBlock,["carriedInFrontOfOther"]=V(a.InverseTransformPoint(carried))});
                    break;
                }
                case "shaders":
                {
                    string filter=args.Length>2?args[2].ToLower():"";
                    var names=new JArray(Resources.FindObjectsOfTypeAll<Shader>().Select(x=>x.name).Where(x=>x.ToLower().Contains(filter)).Distinct().OrderBy(x=>x));
                    output(new JObject{["shaders"]=names});
                    break;
                }
                case "fade":
                {
                    // Holds every mirror at this much faded (0 solid wood, 1 all glow); no number lets them go again.
                    LocalPortal.HoldFade=args.Length>2?Mathf.Clamp01(Arg(args,2,0)):-1;
                    if(args.Length>5)LocalPortal.FadeTint=new Color(Arg(args,3,0.25f),Arg(args,4,0.2f),Arg(args,5,0.13f),1);
                    output(new JObject{["hold"]=LocalPortal.HoldFade});
                    break;
                }
                case "materials":
                {
                    // The materials on the nearest portal's model: shader, keywords and properties.
                    var list=Nearby(100).ToList();
                    if(list.Count==0){error("lportal materials: no portal");break;}
                    var seen=new System.Collections.Generic.HashSet<Material>();var mats=new JArray();
                    foreach(Renderer r in list[0].GetComponentsInChildren<Renderer>(true))
                        foreach(Material m in r.sharedMaterials)
                        {
                            if(m==null||!seen.Add(m))continue;
                            var props=new JArray();
                            for(int i=0;i<m.shader.GetPropertyCount();i++)props.Add(m.shader.GetPropertyName(i)+":"+m.shader.GetPropertyType(i));
                            mats.Add(new JObject{["material"]=m.name,["on"]=r.name,["shader"]=m.shader.name,["queue"]=m.renderQueue,["keywords"]=new JArray(m.shaderKeywords),["passes"]=m.passCount,["props"]=props});
                        }
                    output(new JObject{["materials"]=mats});
                    break;
                }
                case "through":
                {
                    var list=Nearby(100).ToList();
                    int n=(int)Arg(args,2,0);
                    if(n<0||n>=list.Count||list[n].Partner==null){error("lportal through: no linked portal "+n);break;}
                    if(Time.time-_beforeAt>120){_before=me.transform.position;_beforeTurn=me.transform.rotation;}
                    _beforeAt=Time.time;
                    return Through(me,list[n],(int)Arg(args,3,40),output,args.Contains("film"));
                }
                case "straddle":
                {
                    // Stands the player partway into portal n's glass, facing in (depth: metres past the glass, negative: before it).
                    var list=Nearby(100).ToList();
                    int n=(int)Arg(args,2,0);
                    if(n<0||n>=list.Count){error("lportal straddle: no portal "+n);break;}
                    if(Time.time-_beforeAt>120){_before=me.transform.position;_beforeTurn=me.transform.rotation;}
                    _beforeAt=Time.time;
                    Transform t=list[n].transform;
                    float depth=Arg(args,3,0.1f),turn=Arg(args,4,0);
                    Vector3 at=t.TransformPoint(new Vector3(0,0.02f,-depth));
                    Quaternion facing=t.rotation*Quaternion.Euler(0,180+turn,0);
                    me.transform.SetPositionAndRotation(at,facing);
                    var rb=me.GetComponent<Rigidbody>();
                    if(rb!=null){rb.position=at;rb.linearVelocity=Vector3.zero;}
                    me.SetLookDir(facing*Vector3.forward);
                    Level(me);
                    output(new JObject{["at"]=V(at),["doubles"]=Doubles.Count,["parts"]=Doubles.PartCount});
                    break;
                }
                case "name":
                {
                    var list=Nearby(100).ToList();
                    int n=(int)Arg(args,2,0);
                    if(n<0||n>=list.Count){error("lportal name: no portal "+n);break;}
                    string text=args.Length>3?string.Join(" ",args.Skip(3)):"";
                    list[n].SetText(text);
                    output(new JObject{["named"]=n,["name"]=text});
                    break;
                }
                case "perf":
                {
                    float seconds=Arg(args,2,3);
                    return Perf(seconds,output);
                }
                case "walls":
                {
                    // What is let through behind the nearest portal, and everything the player's capsule would hit walking into it.
                    var list=Nearby(100).ToList();
                    if(list.Count==0){error("lportal walls: no portal");break;}
                    Transform t=list[0].transform;
                    var ignored=new JArray();
                    foreach(Collider c in Crossing.IgnoredWalls)if(c!=null)ignored.Add(c.name+" ("+c.transform.root.name+") layer "+LayerMask.LayerToName(c.gameObject.layer));
                    CapsuleCollider capsule=me.GetCollider();
                    var hits=new JArray();
                    Vector3 dir=-t.forward;
                    Vector3 p0=capsule.transform.TransformPoint(capsule.center+Vector3.up*(capsule.height/2-capsule.radius)),p1=capsule.transform.TransformPoint(capsule.center-Vector3.up*(capsule.height/2-capsule.radius));
                    foreach(RaycastHit h in Physics.CapsuleCastAll(p0,p1,capsule.radius*0.98f,dir,3f,~0,QueryTriggerInteraction.Ignore))
                    {
                        if(h.collider==capsule||h.collider.transform.IsChildOf(me.transform))continue;
                        Vector3 l=t.InverseTransformPoint(h.point);
                        hits.Add(h.collider.name+" ("+h.collider.transform.root.name+") layer "+LayerMask.LayerToName(h.collider.gameObject.layer)+
                            " at "+Math.Round(h.distance,2)+" m, portal x,y,z "+V(l)+(Physics.GetIgnoreCollision(capsule,h.collider)?" IGNORED":"")+(h.collider.enabled?"":" disabled"));
                    }
                    output(new JObject{["me"]=V(t.InverseTransformPoint(me.transform.position)),["radius"]=Math.Round(capsule.radius,2),["ignored"]=ignored,["hits"]=hits});
                    break;
                }
                case "back":
                    if(Time.time-_beforeAt>600){error("lportal back: nothing to go back to");break;}
                    me.transform.SetPositionAndRotation(_before,_beforeTurn);
                    var body=me.GetComponent<Rigidbody>();
                    if(body!=null){body.position=_before;body.linearVelocity=Vector3.zero;}
                    me.SetLookDir(_beforeTurn*Vector3.forward);
                    _beforeAt=-1000;
                    output(new JObject{["returned"]=V(_before)});
                    break;
                case "remove":
                {
                    int removed=0;
                    foreach(LocalPortal p in LocalPortal.Live.Where(p=>p!=null&&p.View.GetZDO().GetBool(TestKey)).ToList())
                    {
                        if(!p.View.IsOwner())p.View.ClaimOwnership();
                        ZNetScene.instance.Destroy(p.gameObject);removed++;
                    }
                    output(new JObject{["removed"]=removed});
                    break;
                }
                default:error("lportal: place, info, probe, through, back or remove");break;
            }
            return null;
        }
        private static float Arg(string[] args,int i,float fallback)=>args.Length>i&&float.TryParse(args[i],System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out float v)?v:fallback;

        // Looks a little down, as a player walking would (a pointer jump while the game was not focused can leave it at the floor).
        private static readonly HarmonyLib.AccessTools.FieldRef<Player,float> LookPitch=HarmonyLib.AccessTools.FieldRefAccess<Player,float>("m_lookPitch");
        private static void Level(Player me)=>LookPitch(me)=10;

        // Puts the player a step in front of the portal, facing it, and moves them in a little at a time through the physics.
        private static IEnumerator Through(Player me,LocalPortal p,int steps,Action<JObject> output,bool film)
        {
            Transform t=p.transform;
            Vector3 start=t.TransformPoint(new Vector3(0,0.05f,1.2f));
            Quaternion facing=t.rotation*Quaternion.Euler(0,180,0);
            var body=me.GetComponent<Rigidbody>();
            me.transform.SetPositionAndRotation(start,facing);
            if(body!=null){body.position=start;body.linearVelocity=Vector3.zero;}
            me.SetLookDir(facing*Vector3.forward);
            Level(me);
            int before=Crossing.Count;
            var frames=new JArray();
            int frame=0,after=0;
            bool turned=false;
            Vector3 walk=facing*Vector3.forward;
            yield return new WaitForFixedUpdate();
            // Walks in at about 2 m/s; with film, every rendered frame is saved (small) until a second after coming out.
            while(frame<400)
            {
                bool through=Crossing.Count!=before;
                if(through&&!turned){turned=true;walk=Crossing.Turn(t,p.Partner.transform)*walk;} // keep walking the way it went in
                if(through&&++after>(film?60:1))break;
                if(!through&&frame>steps*4)break;
                Vector3 step=walk*2f*Time.deltaTime;
                Vector3 next=me.transform.position+step;
                me.transform.position=next;
                if(body!=null){body.position=next;body.linearVelocity=step/Time.deltaTime;}
                yield return new WaitForEndOfFrame();
                if(film)
                {
                    LocalPortal other=p.Partner;
                    Transform cam=GameCamera.instance.transform;
                    frames.Add(new JObject{["f"]=SaveFrame(frame,through),
                        ["meInB"]=other!=null?V(other.transform.InverseTransformPoint(me.transform.position)):null,
                        ["meFacingB"]=other!=null?Math.Round(Vector3.SignedAngle(other.transform.forward,me.transform.forward,Vector3.up)):0,
                        ["camInA"]=V(t.InverseTransformPoint(cam.position)),["camInB"]=other!=null?V(other.transform.InverseTransformPoint(cam.position)):null,
                        ["camLookA"]=Math.Round(Vector3.SignedAngle(-t.forward,cam.forward,Vector3.up))});
                }
                frame++;
                yield return null;
            }
            yield return new WaitForSeconds(0.5f);
            LocalPortal q=p.Partner;
            Vector3 local=q!=null?q.transform.InverseTransformPoint(me.transform.position):Vector3.zero;
            output(new JObject{["went"]=Crossing.Count>before,["at"]=V(me.transform.position),["inFrontOfOther"]=V(local),
                ["facingOut"]=q!=null?Math.Round(Vector3.Angle(me.transform.forward,q.transform.forward),1):-1,
                ["lookOut"]=q!=null?Math.Round(Vector3.Angle(me.GetLookDir(),q.transform.forward),1):-1,
                ["camera"]=V(GameCamera.instance.transform.position),["frames"]=frames.Count>0?frames:null,
                ["cameraBlockedBy"]=Crossing.CameraBlock,["cameraClear"]=Math.Round(Crossing.CameraClear,2),["cameraWanted"]=Math.Round(Crossing.CameraWanted,2)});
        }

        // Average and worst frame times with the portal views as configured, then with none drawn, for the same span each.
        private static IEnumerator Perf(float seconds,Action<JObject> output)
        {
            var result=new JObject();
            int keep=Plugin.MaxViews.Value;
            foreach(int views in new[]{keep,0})
            {
                Plugin.MaxViews.Value=views;
                yield return new WaitForSecondsRealtime(0.5f);
                float total=0,worst=0;int frames=0,drawn=0;
                for(float t=0;t<seconds;t+=Time.unscaledDeltaTime)
                {
                    yield return null;
                    total+=Time.unscaledDeltaTime;worst=Mathf.Max(worst,Time.unscaledDeltaTime);frames++;drawn+=Views.Drawn;
                }
                result["views"+views]=new JObject{["avgMs"]=Math.Round(total/Mathf.Max(1,frames)*1000,1),["worstMs"]=Math.Round(worst*1000,1),["viewsPerFrame"]=Math.Round(drawn/(double)Mathf.Max(1,frames),2)};
            }
            Plugin.MaxViews.Value=keep;
            output(result);
        }

        // The frame just drawn, shrunk to 640 wide, as shots/film/NNN.jpg.
        private static string SaveFrame(int n,bool through)
        {
            Texture2D full=ScreenCapture.CaptureScreenshotAsTexture();
            int w=640,h=Mathf.Max(1,full.height*640/full.width);
            var rt=RenderTexture.GetTemporary(w,h,0);
            Graphics.Blit(full,rt);
            var small=new Texture2D(w,h,TextureFormat.RGB24,false);
            RenderTexture old=RenderTexture.active;RenderTexture.active=rt;
            small.ReadPixels(new Rect(0,0,w,h),0,0);small.Apply();
            RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);
            string dir=System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath,"claude","shots","film");
            System.IO.Directory.CreateDirectory(dir);
            string file=System.IO.Path.Combine(dir,n.ToString("000")+(through?"b":"a")+".jpg");
            System.IO.File.WriteAllBytes(file,small.EncodeToJPG(85));
            Object.Destroy(full);Object.Destroy(small);
            return System.IO.Path.GetFileName(file);
        }

        private static System.Collections.Generic.IEnumerable<LocalPortal> Nearby(float radius)
        {
            Vector3 at=Player.m_localPlayer.transform.position;
            return LocalPortal.Live.Where(p=>p!=null&&Vector3.Distance(p.transform.position,at)<radius).OrderBy(p=>Vector3.Distance(p.transform.position,at));
        }
        private static JObject Describe(LocalPortal p)
        {
            return new JObject{["at"]=V(p.transform.position),["facing"]=Math.Round(p.transform.eulerAngles.y,1),["colour"]=p.Colour,
                ["linked"]=!p.PartnerId().IsNone(),["partnerLoaded"]=p.Partner!=null,
                ["partnerAt"]=p.Partner!=null?V(p.Partner.transform.position):null,
                ["picture"]=p.Picture!=null?p.Picture.width+"x"+p.Picture.height:null,["drawnFramesAgo"]=p.PictureFrame<0?-1:Time.frameCount-p.PictureFrame,
                ["surface"]=p.Surface!=null?p.Surface.sharedMaterial?.name:null,["name"]=p.Name,["hover"]=p.GetHoverText(),["test"]=p.View.GetZDO().GetBool(TestKey),["hidden"]=p.Hidden,["faded"]=Math.Round(p.Faded,2)};
        }

        private static JObject Probe()
        {
            var cams=new JArray();
            foreach(Camera c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include,FindObjectsSortMode.None))
                cams.Add(new JObject{["name"]=c.name,["path"]=Path(c.transform),["enabled"]=c.isActiveAndEnabled,["depth"]=c.depth,["clear"]=c.clearFlags.ToString(),
                    ["mask"]=string.Join(",",Enumerable.Range(0,32).Where(i=>(c.cullingMask&(1<<i))!=0).Select(i=>LayerMask.LayerToName(i))),
                    ["path2"]=c.actualRenderingPath.ToString(),["hdr"]=c.allowHDR,["near"]=c.nearClipPlane,["far"]=c.farClipPlane,["fov"]=c.fieldOfView,
                    ["target"]=c.targetTexture!=null?c.targetTexture.name:null,["depthTex"]=c.depthTextureMode.ToString(),
                    ["components"]=new JArray(c.GetComponents<Component>().Select(x=>x.GetType().Name))});
            var shaders=new JObject();
            foreach(string s in new[]{"Unlit/Texture","Unlit/Color","Sprites/Default","UI/Default","Standard","Legacy Shaders/Diffuse","Skybox/Procedural"})shaders[s]=Shader.Find(s)!=null;
            return new JObject{["cameras"]=cams,["shaders"]=shaders,["viewShader"]=Views.ViewShaderName,["sky"]=RenderSettings.skybox!=null?RenderSettings.skybox.shader.name:null,
                ["fog"]=RenderSettings.fog,["screen"]=Screen.width+"x"+Screen.height,["drawnLastFrame"]=Views.Drawn};
        }
        private static string Path(Transform t)=>t.parent==null?t.name:Path(t.parent)+"/"+t.name;
        private static JArray V(Vector3 v)=>new JArray(Math.Round(v.x,2),Math.Round(v.y,2),Math.Round(v.z,2));
    }
}
