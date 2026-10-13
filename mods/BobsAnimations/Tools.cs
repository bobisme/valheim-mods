using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BobsAnimations
{
    // An "anim" command for Claude Tools, found while the game runs (no reference): who's near, a character's skeleton for Blender, and
    // playing clips on them for testing.
    internal static class Tools
    {
        private const string ClaudeToolsGuid="com.quad.claudetools";
        private static BaseUnityPlugin _tools;
        private static float _nextCheck;
        private static readonly string[] Seats={"attach_chair","attach_throne","attach_bed","attach_dragon"};

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
                found.GetType().GetMethod("RegisterCommand",BindingFlags.Public|BindingFlags.Static)?.Invoke(null,new object[]{Plugin.Name,"anim",
                    "anim chars [radius=100] | clips | rig [who] | hold [who] [item=Tankard] [bone=RightHand_Attach] | play <clip> [who] [loop|once] [item=<prefab>] | stop [who] | near [who] | params [who] | drift | spin [who] | shot [who] [yaw=0] [pitch=10] [dist=1.6] [up=0]: "+
                    "Bob's Animations. who: me, seated (the nearest sitting character), still (the nearest one standing still), nearest (the nearest other character, the default) a number from chars (its default radius) or a name. "+
                    "rig saves the skeleton and skin, posed as now, to claude/shots/anim-rig.json for Authoring/rig.py; play and stop need the game that runs the character; "+
                    "shot pictures it alone (nothing else drawn), yaw 0 at its face",
                    (Func<string[],Action<JObject>,Action<string>,IEnumerator>)Run});
            }
            catch(Exception e){Plugin.Instance?.Error(e);}
        }
        internal static void Stop()
        {
            try{_tools?.GetType().GetMethod("UnregisterAll",BindingFlags.Public|BindingFlags.Static)?.Invoke(null,new object[]{Plugin.Name});}catch(Exception){}
            _tools=null;
        }

        private static List<Character> Near(float radius)
        {
            Player me=Player.m_localPlayer;
            if(me==null)return new List<Character>();
            return Character.GetAllCharacters().Where(c=>c!=null&&c!=me&&Vector3.Distance(c.transform.position,me.transform.position)<=radius)
                .OrderBy(c=>Vector3.Distance(c.transform.position,me.transform.position)).ToList();
        }
        private static string Seat(Character c)
        {
            Animator a=c.GetComponentInChildren<Animator>();
            if(a==null)return null;
            foreach(string s in Seats)if(a.parameters.Any(p=>p.name==s&&p.type==AnimatorControllerParameterType.Bool)&&a.GetBool(s))return s;
            return null;
        }
        private static Character Who(string who)
        {
            if(who=="me")return Player.m_localPlayer;
            List<Character> near=Near(100);
            if(who=="still")return near.FirstOrDefault(c=>Seat(c)==null&&c.GetVelocity().sqrMagnitude<0.01f&&c.GetComponent<VisEquipment>()!=null);
            if(who=="seated")return near.FirstOrDefault(c=>Seat(c)!=null)??(Player.m_localPlayer!=null&&Seat(Player.m_localPlayer)!=null?Player.m_localPlayer:null);
            if(int.TryParse(who,out int i))return i>=0&&i<near.Count?near[i]:null;
            if(who!="nearest")return near.FirstOrDefault(c=>c.m_name.StartsWith(who,StringComparison.OrdinalIgnoreCase));
            return near.FirstOrDefault();
        }

        private static IEnumerator Run(string[] args,Action<JObject> output,Action<string> error)
        {
            string sub=args.Length>1?args[1]:"chars";
            if(Player.m_localPlayer==null){error("anim: needs the player in a world");return null;}
            if(sub=="clips"){output(new JObject{["clips"]=new JArray(Clip.Names().Select(n=>{Clip c=Clip.Get(n);return $"{n}: {c.Length:0.0}s, {c.Bones.Length} bones{(c.Loop?", loops":"")}{(c.Props.Length>0?", props "+string.Join(",",c.Props.Select(p=>p.Item)):"")}";})),["playing"]=Playback.Describe()});return null;}
            if(sub=="chars")
            {
                float radius=args.Length>2&&float.TryParse(args[2],NumberStyles.Float,CultureInfo.InvariantCulture,out float r)?r:100;
                Player me=Player.m_localPlayer;
                output(new JObject{["chars"]=new JArray(Near(radius).Select((c,i)=>$"{i}: {c.m_name} ({Utils.GetPrefabName(c.gameObject)}) {Vector3.Distance(c.transform.position,me.transform.position):0.0} m"+
                    $"{(Seat(c) is string s?", "+s:c.GetVelocity().sqrMagnitude<0.01f?", still":", moving")}{(c.GetComponent<ZNetView>()?.IsOwner()==true?", ours":", another game's")}{(Anim.Playing(c) is string p?", playing "+p:"")}")),
                    ["me"]=Seat(me)!=null?"seated: "+Seat(me):"standing"});
                return null;
            }
            Character who=Who(sub=="play"?(args.Length>3&&!args[3].StartsWith("item=",StringComparison.Ordinal)?args[3]:"nearest"):(args.Length>2?args[2]:"nearest"));
            if(who==null){error("anim: nobody there (anim chars lists who's near)");return null;}
            switch(sub)
            {
                case "rig":output(Rig(who));break;
                case "play":
                {
                    if(args.Length<3){error("anim play <clip> [who] [loop|once]");break;}
                    bool? loop=args.Skip(4).Contains("loop")?true:args.Skip(4).Contains("once")?false:(bool?)null;
                    string item=args.Skip(3).FirstOrDefault(a=>a.StartsWith("item=",StringComparison.Ordinal))?.Substring(5);
                    if(Clip.Get(args[2])==null){error("anim play: no clip "+args[2]+" (anim clips)");break;}
                    if(!Anim.Play(who,args[2],item,loop)){error("anim play: another game runs "+who.m_name);break;}
                    output(new JObject{["playing"]=args[2],["on"]=who.m_name,["seat"]=Seat(who),["length"]=Math.Round(Clip.Get(args[2]).Length,2)});
                    break;
                }
                case "stop":output(new JObject{["stopped"]=Anim.Stop(who),["on"]=who.m_name});break;
                case "drift":return DriftAtEnd(output);
                case "spin":return SpinWatch(who,output);
                case "params":
                {
                    // The animator's switches that are on (bools, and ints and floats that aren't 0), for finding how a character is posed.
                    Animator an=who.GetComponentInChildren<Animator>();
                    if(an==null){error("anim params: no animator");break;}
                    output(new JObject{["who"]=who.m_name,["on"]=new JArray(an.parameters.Where(q=>q.type==AnimatorControllerParameterType.Bool&&an.GetBool(q.name)||q.type==AnimatorControllerParameterType.Int&&an.GetInteger(q.name)!=0||q.type==AnimatorControllerParameterType.Float&&Math.Abs(an.GetFloat(q.name))>0.01f)
                        .Select(q=>q.type==AnimatorControllerParameterType.Bool?q.name:q.type==AnimatorControllerParameterType.Int?$"{q.name}={an.GetInteger(q.name)}":$"{q.name}={an.GetFloat(q.name):0.00}"))});
                    break;
                }
                case "near":
                {
                    // The pieces within 2 m, measured in the character's own frame (x its right, y up, z its front): for fitting a clip to
                    // the furniture round a seat (a table's top and how far in front it is).
                    var list=new List<Piece>();Piece.GetAllPiecesInRadius(who.transform.position,2f,list);
                    output(new JObject{["who"]=who.m_name,["pieces"]=new JArray(list.Select(p=>
                    {
                        Renderer[] rs=p.GetComponentsInChildren<Renderer>().Where(r=>r.enabled).ToArray();
                        if(rs.Length==0)return null;
                        Bounds b=rs[0].bounds;foreach(Renderer r in rs)b.Encapsulate(r.bounds);
                        Vector3 c=who.transform.InverseTransformPoint(b.center),top=who.transform.InverseTransformPoint(new Vector3(b.center.x,b.max.y,b.center.z));
                        return $"{Utils.GetPrefabName(p.gameObject)}: centre ({c.x:0.00}, {c.y:0.00}, {c.z:0.00}), top at {top.y:0.00}, size {b.size.x:0.00} x {b.size.y:0.00} x {b.size.z:0.00} (world)";
                    }).Where(t=>t!=null))});
                    break;
                }
                case "hold":
                {
                    // Where an item would be held (as a clip's prop on <bone>, default RightHand_Attach), measured in the hand bone's own frame
                    // (RightHand or LeftHand): its meshes' centre and their up and forward axes, for placing a stand-in in Blender.
                    string item=args.Length>3?args[3]:"Tankard",bone=args.Length>4?args[4]:"RightHand_Attach";
                    output(Playback.Hold(who,item,bone));break;
                }
                case "shot":
                {
                    float F(int n,float d)=>args.Length>n&&float.TryParse(args[n],NumberStyles.Float,CultureInfo.InvariantCulture,out float f)?f:d;
                    return ShotAtEnd(who,F(3,0),F(4,10),F(5,1.6f),F(6,0),output);
                }
                default:error("anim: chars | clips | rig | play | stop | shot");break;
            }
            return null;
        }

        // (A character out of the player's view keeps the skin it last had in view: Unity skins only what a camera sees. `anim drift` gives
        // the real pose in numbers.)
        // At the end of the frame, as the game draws it: after the clips have posed the bones (a shot during Update has props where the
        // game's own animation left them, while the skin shows the last frame's pose).
        // Test: the right hand's biggest turn from one frame to the next over the next few seconds (a flip shows as a big one).
        private static IEnumerator SpinWatch(Character c,Action<JObject> output)
        {
            Transform hand=Playback.Bones(c).TryGetValue("RightHand",out Transform h)?h:null;
            if(hand==null){output(new JObject{["error"]="no hand"});yield break;}
            float worst=0,at=0,start=Time.time;Quaternion? last=null;int frames=0;var jumps=new JArray();
            while(Time.time-start<4.5f&&c!=null)
            {
                yield return new WaitForEndOfFrame();
                Quaternion q=Quaternion.Inverse(c.transform.rotation)*hand.rotation;
                if(last.HasValue){float a=Quaternion.Angle(last.Value,q);if(a>worst){worst=a;at=Time.time-start;}if(a>25)jumps.Add($"{Time.time-start:0.00}s {a:0}deg during {Anim.Playing(c)??"nothing"}");}
                last=q;frames++;
            }
            output(new JObject{["who"]=c?.m_name,["playing"]=c!=null?Anim.Playing(c):null,["frames"]=frames,["worstTurn"]=Math.Round(worst,1),["at"]=Math.Round(at,2),["jumps"]=jumps});
        }
        private static IEnumerator DriftAtEnd(Action<JObject> output)
        {
            yield return new WaitForEndOfFrame();
            output(new JObject{["drift"]=Playback.Drift()});
        }
        private static IEnumerator ShotAtEnd(Character c,float yaw,float pitch,float dist,float up,Action<JObject> output)
        {
            yield return new WaitForEndOfFrame();
            if(c!=null)output(Shot(c,yaw,pitch,dist,up));
        }
        // The character alone (nothing else drawn), from a camera round its chest: yaw 0 is its face.
        private static JObject Shot(Character c,float yaw,float pitch,float dist,float up)
        {
            const int layer=31;
            var moved=new List<(GameObject go,int layer)>();
            foreach(Renderer r in c.GetComponentsInChildren<Renderer>(true)){moved.Add((r.gameObject,r.gameObject.layer));r.gameObject.layer=layer;}
            var holder=new GameObject("anim test camera");var texture=new RenderTexture(900,900,24);
            try
            {
                Transform chest=Playback.Bones(c).TryGetValue("Spine2",out Transform spine)?spine:c.transform;
                Vector3 focus=chest.position+Vector3.up*up;
                Quaternion turn=c.transform.rotation*Quaternion.Euler(pitch,180+yaw,0);
                Camera cam=holder.AddComponent<Camera>();cam.cullingMask=1<<layer;cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=new Color(0.55f,0.62f,0.7f);
                cam.fieldOfView=35;cam.nearClipPlane=0.05f;cam.targetTexture=texture;
                holder.transform.SetPositionAndRotation(focus-turn*Vector3.forward*dist,turn);
                var light=holder.AddComponent<Light>();light.type=LightType.Directional;light.cullingMask=1<<layer;light.intensity=0.9f;
                cam.Render();
                RenderTexture before=RenderTexture.active;RenderTexture.active=texture;
                var image=new Texture2D(900,900,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,900,900),0,0);image.Apply();RenderTexture.active=before;
                string dir=System.IO.Path.Combine(Paths.BepInExRootPath,"claude","shots");System.IO.Directory.CreateDirectory(dir);
                string file=System.IO.Path.Combine(dir,$"anim-{DateTime.Now:HHmmss-fff}-{yaw:0}-{pitch:0}.png");
                System.IO.File.WriteAllBytes(file,image.EncodeToPNG());UnityEngine.Object.Destroy(image);
                return new JObject{["shot"]=file,["who"]=c.m_name,["playing"]=Anim.Playing(c)};
            }
            finally
            {
                foreach(var (go,old) in moved)if(go!=null)go.layer=old;
                UnityEngine.Object.Destroy(holder);texture.Release();UnityEngine.Object.Destroy(texture);
            }
        }

        // Its skeleton and skin, for building it in Blender (Authoring/rig.py): each bone's parent and pose now (local), and each skin's
        // bind poses, vertices, triangles and weights.
        private static JObject Rig(Character c)
        {
            var skins=new JArray();var bones=new Dictionary<Transform,int>();var boneList=new JArray();
            Transform root=c.transform;
            int Index(Transform t)
            {
                if(t==null||t==root)return -1;
                if(bones.TryGetValue(t,out int i))return i;
                int parent=Index(t.parent);
                i=bones.Count;bones[t]=i;
                boneList.Add(new JObject{["name"]=t.name,["parent"]=parent,["local"]=new JObject{["p"]=V(t.localPosition,6),["r"]=Q(t.localRotation),["s"]=V(t.localScale,6)}});
                return i;
            }
            foreach(Transform t in c.GetComponentsInChildren<Transform>(true))Index(t); // (every bone, worn or not: hand attach points too)
            foreach(SkinnedMeshRenderer skin in c.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Mesh m=skin.sharedMesh;if(m==null||!skin.gameObject.activeInHierarchy)continue;
                if(!m.isReadable){skins.Add(new JObject{["name"]=skin.name,["error"]="not readable"});continue;}
                var used=skin.bones.Select(Index).ToArray();
                var bind=new JArray();foreach(Matrix4x4 b in m.bindposes){var w=b.inverse;bind.Add(new JArray(Enumerable.Range(0,16).Select(k=>Math.Round(w[k%4,k/4],6))));}
                var weights=new JArray();foreach(BoneWeight w in m.boneWeights)weights.Add(new JArray(w.boneIndex0,Math.Round(w.weight0,4),w.boneIndex1,Math.Round(w.weight1,4),w.boneIndex2,Math.Round(w.weight2,4),w.boneIndex3,Math.Round(w.weight3,4)));
                skins.Add(new JObject{["name"]=skin.name,["mesh"]=m.name,["transform"]=Index(skin.transform),["bones"]=new JArray(used),["bind"]=bind,
                    ["vertices"]=new JArray(m.vertices.Select(v=>V(v,5))),["triangles"]=new JArray(m.triangles),["weights"]=weights});
            }
            var rig=new JObject{["who"]=c.m_name,["seat"]=Seat(c),["scale"]=V(c.transform.localScale,4),["bones"]=boneList,["skins"]=skins};
            string file=System.IO.Path.Combine(Paths.BepInExRootPath,"claude","shots","anim-rig.json");
            System.IO.File.WriteAllText(file,rig.ToString(Newtonsoft.Json.Formatting.None));
            return new JObject{["rig"]=file,["who"]=c.m_name,["seat"]=Seat(c),["bones"]=boneList.Count,["skins"]=new JArray(skins.Select(k=>k["name"]+": "+(k["error"]??((JArray)k["vertices"]).Count+" vertices")))};
        }
        private static JArray Q(Quaternion q)=>new JArray(Math.Round(q.x,6),Math.Round(q.y,6),Math.Round(q.z,6),Math.Round(q.w,6));
        private static JArray V(Vector3 v,int digits)=>new JArray(Math.Round(v.x,digits),Math.Round(v.y,digits),Math.Round(v.z,digits));
    }
}
