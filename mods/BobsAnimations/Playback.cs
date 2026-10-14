using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object=UnityEngine.Object;

namespace BobsAnimations
{
    // For other mods: play a clip on a character. The game that runs the character (its owner) starts and stops it; what plays and since
    // when is kept on the character's network object, so every game near it plays the same, in step.
    public static class Anim
    {
        internal const string ClipKey="bob_anim_clip",AtKey="bob_anim_at",EndKey="bob_anim_end",PropKey="bob_anim_prop";
        internal static long Now=>ZNet.instance!=null?ZNet.instance.GetTime().Ticks:DateTime.UtcNow.Ticks;

        // Plays a clip (by name) from its start, looping if the clip loops or loop is true. False if there's no such clip or another game
        // runs the character (ask that game, as with anything else about it).
        public static bool Play(Character c,string clip,bool? loop=null)=>Play(c,clip,null,loop);
        // As Play, with an item in the hands in place of the clip's own (a clip's swappable prop: the logs or stone a hauler carries).
        public static bool Play(Character c,string clip,string item,bool? loop=null)
        {
            ZDO z=Owned(c);
            if(z==null||Clip.Get(clip)==null)return false;
            z.Set(ClipKey,clip+(loop==true?"|loop":loop==false?"|once":""));z.Set(AtKey,Now);z.Set(EndKey,0L);z.Set(PropKey,item??"");
            Playback.Scan(true);
            return true;
        }
        // Lets the clip blend out (a looping one ends here).
        public static bool Stop(Character c)
        {
            ZDO z=Owned(c);
            if(z==null||string.IsNullOrEmpty(z.GetString(ClipKey)))return false;
            if(Clip.Get(z.GetString(ClipKey).Split('|')[0])==null){z.Set(ClipKey,"");return true;} // (a clip no game here knows: nothing to blend out)
            if(z.GetLong(EndKey,0)==0)z.Set(EndKey,Now);
            return true;
        }
        // The clip playing on a character (any game's view), or null.
        public static string Playing(Character c)
        {
            string s=c!=null?c.GetComponent<ZNetView>()?.GetZDO()?.GetString(ClipKey):null;
            return string.IsNullOrEmpty(s)?null:s.Split('|')[0];
        }
        private static ZDO Owned(Character c)
        {
            ZNetView v=c!=null?c.GetComponent<ZNetView>():null;
            return v!=null&&v.IsValid()&&v.IsOwner()?v.GetZDO():null;
        }
    }

    // Every game: the characters near it with a clip on, posed over their own animation each frame.
    internal static class Playback
    {
        private sealed class State
        {
            public Character Body;public ZNetView View;public Clip Clip;public string Key;public long At;public bool Loop;
            public Animator Animator;public string[] Seats;public float NextSeatCheck;public long Stood; // (seated or still clips: when it was seen getting up or walking off)
            public string Item;public Vector3[] LastPos,BasePos;public bool[] Moved;public float Weight;public float StillSince=-1; // (test: the weight last applied)
            public Dictionary<Transform,Quaternion> From;public Dictionary<Transform,Vector3> FromPos; // (the pose the clip before left: blended in from there)
            public Transform[] Bones;public Quaternion[] LastOut,LastBase;public bool[] Wrote;
            public readonly Dictionary<Clip.Prop,GameObject> Props=new Dictionary<Clip.Prop,GameObject>();
        }
        private static readonly Dictionary<Character,State> Playing=new Dictionary<Character,State>();
        // Bones the clip before drove and the new one doesn't: eased back to the game's pose over a moment.
        private static readonly List<(Transform bone,Quaternion from,float at)> Easing=new List<(Transform,Quaternion,float)>();
        private const float EaseTime=0.4f;
        private static readonly string[] SeatParams={"attach_chair","attach_throne","attach_bed","attach_dragon"};
        private static float _nextScan;

        // Twice a second (or at once, after Play here): who has a clip on, and whether it changed.
        internal static void Scan(bool now=false)
        {
            if(!now&&Time.time<_nextScan)return;
            _nextScan=Time.time+0.5f;
            foreach(Character c in Character.GetAllCharacters())
            {
                if(c==null)continue;
                ZNetView v=c.GetComponent<ZNetView>();ZDO z=v!=null&&v.IsValid()?v.GetZDO():null;
                string key=z?.GetString(Anim.ClipKey);
                Playing.TryGetValue(c,out State st);
                if(string.IsNullOrEmpty(key)){if(st!=null)Drop(st);continue;}
                long at=z.GetLong(Anim.AtKey,0);
                if(st!=null&&st.Key==key&&st.At==at)continue;
                Dictionary<Transform,Quaternion> from=null;Dictionary<Transform,Vector3> fromPos=null;
                if(st!=null)
                {
                    // One clip after another: the new one starts from where the last left the body, not from the game's pose.
                    from=new Dictionary<Transform,Quaternion>();fromPos=new Dictionary<Transform,Vector3>();
                    for(int i=0;i<st.Bones.Length;i++)if(st.Bones[i]!=null&&st.Wrote[i])from[st.Bones[i]]=st.LastOut[i];
                    for(int i=0;i<st.Bones.Length;i++)if(st.Bones[i]!=null&&st.Moved[i])fromPos[st.Bones[i]]=st.LastPos[i];
                    Drop(st);
                }
                string[] parts=key.Split('|');
                Clip clip=Clip.Get(parts[0]);
                if(clip==null)continue; // (a clip this game doesn't have: nothing to show)
                var bones=Bones(c);
                st=new State{Body=c,View=v,Clip=clip,Key=key,At=at,Loop=parts.Length>1?parts[1]=="loop":clip.Loop,
                    Bones=clip.Bones.Select(n=>bones.TryGetValue(n,out Transform t)?t:null).ToArray()};
                st.Animator=c.GetComponentInChildren<Animator>();st.Seats=st.Animator!=null?SeatParams.Where(n=>st.Animator.parameters.Any(q=>q.name==n&&q.type==AnimatorControllerParameterType.Bool)).ToArray():new string[0];
                st.Item=z.GetString(Anim.PropKey);st.LastPos=new Vector3[st.Bones.Length];st.BasePos=new Vector3[st.Bones.Length];st.Moved=new bool[st.Bones.Length];
                st.LastOut=new Quaternion[st.Bones.Length];st.LastBase=new Quaternion[st.Bones.Length];st.Wrote=new bool[st.Bones.Length];
                st.From=from;st.FromPos=fromPos;
                if(from!=null)foreach(var kv in from)if(!st.Bones.Contains(kv.Key))Easing.Add((kv.Key,kv.Value,Time.time));
                Playing[c]=st;
            }
            foreach(State gone in Playing.Values.Where(s=>s.Body==null).ToList())Drop(gone);
        }
        // Its bones by name: the body's own skeleton first (worn clothes and the like carry copies of it, and not every copy moves), then
        // anything else on the character.
        internal static Dictionary<string,Transform> Bones(Character c)
        {
            var d=new Dictionary<string,Transform>();
            SkinnedMeshRenderer body=c.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.rootBone!=null).OrderByDescending(r=>r.bones.Length).FirstOrDefault();
            Transform skeleton=body!=null?(body.rootBone.parent??body.rootBone):null;
            if(skeleton!=null)foreach(Transform t in skeleton.GetComponentsInChildren<Transform>(true))if(!d.ContainsKey(t.name))d[t.name]=t;
            foreach(Transform t in c.GetComponentsInChildren<Transform>(true))if(!d.ContainsKey(t.name))d[t.name]=t;
            return d;
        }

        // After the game's animation (and its head-turning, done in the animator) has posed the body this frame.
        internal static void LateUpdate()
        {
            for(int i=Easing.Count-1;i>=0;i--)
            {
                var (bone,from,at)=Easing[i];float k=(Time.time-at)/EaseTime;
                if(bone==null||k>=1){Easing.RemoveAt(i);continue;}
                bone.localRotation=Quaternion.Slerp(from,bone.localRotation,k);
            }
            if(Playing.Count==0)return;
            long now=Anim.Now;
            foreach(State st in Playing.Values.ToList())
            {
                if(st.Body==null||st.View==null||!st.View.IsValid()){Drop(st);continue;}
                Clip clip=st.Clip;float len=Mathf.Max(0.01f,clip.Length);
                float t=(float)((now-st.At)/(double)TimeSpan.TicksPerSecond);
                long end=st.View.GetZDO().GetLong(Anim.EndKey,0);
                // A seated clip on someone who got up: it blends out from then (each game sees it for itself; the owner ends it for all).
                // (and any other clip on someone who sits down: an armful carried into a chair would go through their lap)
                if(st.Stood==0&&Time.time>=st.NextSeatCheck)
                {
                    st.NextSeatCheck=Time.time+0.25f;
                    bool sitting=st.Animator!=null&&st.Seats.Any(n=>st.Animator.GetBool(n));
                    bool up=st.Clip.Seated?st.Seats.Length>0&&!sitting:sitting;
                    bool off=st.Clip.Still&&st.Body.GetVelocity().sqrMagnitude>0.6f*0.6f;
                    // (a clip for walking, stood still for over a second and a half: done with)
                    if(st.Clip.Moving){bool still=st.Body.GetVelocity().sqrMagnitude<0.2f*0.2f;st.StillSince=still?(st.StillSince<0?Time.time:st.StillSince):-1;if(still&&Time.time-st.StillSince>1.5f)off=true;}
                    if(up||off){st.Stood=now;if(st.View.IsOwner())Anim.Stop(st.Body);}
                }
                if(st.Stood!=0&&(end==0||st.Stood<end))end=st.Stood;
                float weight=Mathf.Clamp01(t/Mathf.Max(0.01f,clip.BlendIn));
                if(!st.Loop)weight*=Mathf.Clamp01((len-t)/Mathf.Max(0.01f,clip.BlendOut));
                if(end!=0)weight*=Mathf.Clamp01(1-(float)((now-end)/(double)TimeSpan.TicksPerSecond)/Mathf.Max(0.01f,clip.BlendOut));
                bool over=end!=0?weight<=0&&now>end:!st.Loop&&t>=len;
                if(over)
                {
                    Drop(st);
                    if(st.View.IsOwner()){ZDO z=st.View.GetZDO();if(z.GetString(Anim.ClipKey)==st.Key&&z.GetLong(Anim.AtKey,0)==st.At)z.Set(Anim.ClipKey,"");}
                    continue;
                }
                float at=st.Loop?Mathf.Repeat(t,len):Mathf.Clamp(t,0,len);
                if(t<0)weight=0; // (a start a moment ahead on this game's clock)
                for(int i=0;i<st.Bones.Length;i++)
                {
                    Transform b=st.Bones[i];if(b==null)continue;
                    // What the game's animation gave this bone: unchanged since we wrote it means it wasn't animated this frame (off screen):
                    // start from the same base again, not from our own pose, or an added turn would wind up.
                    Quaternion cur=b.localRotation;
                    Quaternion basis=st.Wrote[i]&&Quaternion.Dot(cur,st.LastOut[i])>0.999999f?st.LastBase[i]:cur;
                    Quaternion key=clip.Rotation(i,at);
                    Quaternion target=clip.Add[i]?basis*key:key;
                    Quaternion start=t<clip.BlendIn&&st.From!=null&&st.From.TryGetValue(b,out Quaternion was)?was:basis;
                    Quaternion pose=Quaternion.Slerp(start,target,weight);
                    b.localRotation=pose;st.LastOut[i]=pose;st.LastBase[i]=basis;st.Wrote[i]=true;
                }
                foreach(var (i,shift) in clip.Moves)
                {
                    Transform b=st.Bones[i];if(b==null)continue;
                    Vector3 cur=b.localPosition,basis=st.Moved[i]&&(cur-st.LastPos[i]).sqrMagnitude<1e-10f?st.BasePos[i]:cur;
                    Vector3 place=t<clip.BlendIn&&st.FromPos!=null&&st.FromPos.TryGetValue(b,out Vector3 was)?Vector3.Lerp(was,basis+clip.Shift(shift,at),weight):basis+clip.Shift(shift,at)*weight;
                    b.localPosition=place;st.LastPos[i]=place;st.BasePos[i]=basis;st.Moved[i]=true;
                }
                st.Weight=weight;
                Props(st,at,weight);
            }
        }
        // The clip's props: shown in the hand for their stretch of the clip (while it's blended in at all).
        private static void Props(State st,float at,float weight)
        {
            foreach(Clip.Prop p in st.Clip.Props)
            {
                bool show=weight>0.05f&&at>=p.From&&at<=p.To;
                st.Props.TryGetValue(p,out GameObject go);
                if(show&&go==null)
                {
                    Transform bone=Bones(st.Body).TryGetValue(p.Bone,out Transform found)?found:null;
                    // A hand's attach point is animated by the game (walking turns it in the hand): the prop goes on the hand itself, where
                    // the attach point is at rest, so it stays put in the hand whatever the legs are doing.
                    Matrix4x4 rest=Matrix4x4.identity;
                    if(bone!=null&&bone.parent!=null&&RestOf(p.Bone,out Matrix4x4 r)){rest=r;bone=bone.parent;}
                    go=bone!=null?Model(p.Swap&&!string.IsNullOrEmpty(st.Item)?st.Item:p.Item,bone):null;
                    if(go==null){st.Props[p]=null;continue;}
                    Matrix4x4 m=rest*Matrix4x4.TRS(p.Position,p.Rotation,Vector3.one*p.Scale);
                    go.transform.localPosition=m.GetColumn(3);go.transform.localRotation=m.rotation;go.transform.localScale=m.lossyScale;
                    st.Props[p]=go;
                }
                if(go!=null&&go.activeSelf!=show)go.SetActive(show);
            }
        }
        // An attach point's place in its hand at rest, from the game's own player (as built, before any animation turns it).
        private static readonly Dictionary<string,Matrix4x4> Rests=new Dictionary<string,Matrix4x4>();
        private static bool RestOf(string bone,out Matrix4x4 rest)
        {
            if(Rests.TryGetValue(bone,out rest))return rest!=Matrix4x4.zero;
            Transform t=ZNetScene.instance?.GetPrefab("Player")?.GetComponentsInChildren<Transform>(true).FirstOrDefault(x=>x.name==bone);
            rest=t!=null&&bone.EndsWith("_Attach",StringComparison.Ordinal)?Matrix4x4.TRS(t.localPosition,t.localRotation,t.localScale):Matrix4x4.zero;
            Rests[bone]=rest;
            return rest!=Matrix4x4.zero;
        }
        // An item's look as it is held (its "attach" part, as the game puts it in a hand), its meshes only.
        private static GameObject Model(string item,Transform bone)
        {
            GameObject prefab=ObjectDB.instance?.GetItemPrefab(item);
            if(prefab==null)return null;
            Transform source=prefab.transform.Find("attach")??prefab.transform;
            var root=new GameObject("BobAnimProp_"+item);root.transform.SetParent(bone,false);root.layer=bone.gameObject.layer;
            foreach(MeshRenderer r in source.GetComponentsInChildren<MeshRenderer>(true))
            {
                MeshFilter f=r.GetComponent<MeshFilter>();if(f==null||f.sharedMesh==null||!r.enabled)continue;
                var part=new GameObject(r.name){layer=root.layer};part.transform.SetParent(root.transform,false);
                Matrix4x4 rel=source.worldToLocalMatrix*r.transform.localToWorldMatrix;
                part.transform.localPosition=rel.GetColumn(3);part.transform.localRotation=rel.rotation;part.transform.localScale=rel.lossyScale;
                part.AddComponent<MeshFilter>().sharedMesh=f.sharedMesh;part.AddComponent<MeshRenderer>().sharedMaterials=r.sharedMaterials;
            }
            return root;
        }
        // Test: an item put on a character's hand point as a prop would be, measured in the hand bone's frame, then taken away again.
        internal static Newtonsoft.Json.Linq.JObject Hold(Character c,string item,string boneName)
        {
            Transform bone=Bones(c).TryGetValue(boneName,out Transform found)?found:null;
            Transform hand=bone;while(hand!=null&&hand.name!="RightHand"&&hand.name!="LeftHand")hand=hand.parent;
            if(bone==null||hand==null)return new Newtonsoft.Json.Linq.JObject{["error"]="no "+boneName+" on a hand"};
            GameObject go=Model(item,bone);
            if(go==null)return new Newtonsoft.Json.Linq.JObject{["error"]="no item "+item};
            try
            {
                Renderer[] rs=go.GetComponentsInChildren<Renderer>();
                if(rs.Length==0)return new Newtonsoft.Json.Linq.JObject{["error"]="no meshes"};
                Bounds b=rs[0].bounds;foreach(Renderer r in rs)b.Encapsulate(r.bounds);
                Transform mesh=rs[0].transform;
                Newtonsoft.Json.Linq.JArray J(Vector3 v)=>new Newtonsoft.Json.Linq.JArray(Math.Round(v.x,4),Math.Round(v.y,4),Math.Round(v.z,4));
                Bounds local=rs[0] is MeshRenderer mr?mr.GetComponent<MeshFilter>().sharedMesh.bounds:new Bounds();
                return new Newtonsoft.Json.Linq.JObject{["hand"]=hand.name,["centre"]=J(hand.InverseTransformPoint(b.center)),["worldSize"]=J(b.size),
                    ["meshCentre"]=J(hand.InverseTransformPoint(mesh.TransformPoint(local.center))),["meshSize"]=J(Vector3.Scale(local.size,mesh.lossyScale)),
                    ["meshUp"]=J(hand.InverseTransformDirection(mesh.up)),["meshForward"]=J(hand.InverseTransformDirection(mesh.forward)),["meshRight"]=J(hand.InverseTransformDirection(mesh.right)),
                    ["handScale"]=J(hand.lossyScale)};
            }
            finally{Object.DestroyImmediate(go);}
        }
        private static void Drop(State st)
        {
            foreach(GameObject go in st.Props.Values)if(go!=null)Object.Destroy(go);
            st.Props.Clear();
            if(st.Body!=null)Playing.Remove(st.Body);
            else foreach(var k in Playing.Where(p=>p.Value==st).Select(p=>p.Key).ToList())Playing.Remove(k);
        }
        internal static void Clear(){foreach(State st in Playing.Values.ToList())Drop(st);Playing.Clear();Easing.Clear();}
        internal static int Count=>Playing.Count;
        private static string Path(Transform t,Transform root){string path="";for(;t!=null&&t!=root;t=t.parent)path=t.name+(path==""?"":"/"+path);return path;}
        // Test: how many of the bones it drives are ones the character's skins really follow, and the first one's path.
        private static string Skinned(State st)
        {
            var used=new HashSet<Transform>(st.Body.GetComponentsInChildren<SkinnedMeshRenderer>().SelectMany(r=>r.bones));
            Transform first=st.Bones.FirstOrDefault(b=>b!=null);string path="";
            for(Transform t=first;t!=null&&t!=st.Body.transform;t=t.parent)path=t.name+(path==""?"":"/"+path);
            return $"{st.Bones.Count(b=>b!=null&&used.Contains(b))}/{st.Bones.Count(b=>b!=null)} ({path})";
        }
        // Test, at the end of a frame: for each playing clip its weight, and the bone whose pose drifted most from what was written this
        // frame (something after this mod moved it: another LateUpdate, an animator updated late).
        internal static Newtonsoft.Json.Linq.JArray Drift()
        {
            var list=new Newtonsoft.Json.Linq.JArray();
            foreach(State st in Playing.Values)
            {
                float worst=0;string bone="";
                for(int i=0;i<st.Bones.Length;i++)
                {
                    if(st.Bones[i]==null||!st.Wrote[i])continue;
                    float a=Quaternion.Angle(st.Bones[i].localRotation,st.LastOut[i]);
                    if(a>worst){worst=a;bone=st.Bones[i].name;}
                }
                Animator an=st.Body!=null?st.Body.GetComponentInChildren<Animator>():null;
                Transform hand=st.Bones.FirstOrDefault(x=>x!=null&&x.name=="RightHand");Transform root=st.Body.transform;
                string h=hand==null?"":$" right hand at {root.InverseTransformPoint(hand.position):F2}, its x {root.InverseTransformDirection(hand.right):F2}, z {root.InverseTransformDirection(hand.forward):F2};";
                Renderer prop=st.Props.Values.Where(g=>g!=null).Select(g=>g.GetComponentInChildren<Renderer>()).FirstOrDefault(r=>r!=null);
                if(prop!=null)h+=$" prop centre {root.InverseTransformPoint(prop.bounds.center):F2}, its x {root.InverseTransformDirection(prop.transform.right):F2}, its y {root.InverseTransformDirection(prop.transform.up):F2};";
                list.Add($"{st.Body?.m_name}: {st.Clip.Name} weight {st.Weight:0.00}, drift {worst:0.0} deg ({bone});{h} animator {(an==null?"none":$"enabled {an.enabled}, culling {an.cullingMode}, update {an.updateMode}")}");
            }
            return list;
        }
        // Test: what plays where, with each prop's place.
        internal static Newtonsoft.Json.Linq.JArray Describe()
        {
            var list=new Newtonsoft.Json.Linq.JArray();
            foreach(State st in Playing.Values)
                list.Add($"{st.Body?.m_name}: {st.Clip.Name}{(st.Loop?" (loop)":"")} {(Anim.Now-st.At)/(double)TimeSpan.TicksPerSecond:0.0}s, bones found {st.Bones.Count(b=>b!=null)}/{st.Bones.Length}, skinned {Skinned(st)}, props "+
                    string.Join("; ",st.Props.Select(p=>p.Key.Item+(p.Value==null?" missing":$" at {p.Value.transform.position} (on {Path(p.Value.transform.parent,st.Body.transform)} at {p.Value.transform.parent.position}; driven hand {Path(st.Bones.FirstOrDefault(x=>x!=null&&x.name.EndsWith("Hand")),st.Body.transform)} at {st.Bones.FirstOrDefault(x=>x!=null&&x.name.EndsWith("Hand"))?.position}) active {p.Value.activeInHierarchy} "+string.Join(",",p.Value.GetComponentsInChildren<Renderer>().Select(r=>$"{r.name} bounds {r.bounds.center} size {r.bounds.size} visible {r.isVisible} layer {r.gameObject.layer} mat {r.sharedMaterial?.name}/{r.sharedMaterial?.shader?.name}"))))));
            return list;
        }
    }
}
