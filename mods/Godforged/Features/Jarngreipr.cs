using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Godforged
{
    // Járngreipr, Thor's iron gauntlets: worn in the trinket slot (the game has no gloves), a copy of the Bracelets of the Brave (their
    // adrenaline and its reward come with them), and seen as iron bracers on both forearms. Without them Mjölnir can't be thrown.
    internal sealed class Jarngreipr:Feature
    {
        internal override string Name=>"Járngreipr";
        internal static readonly ItemDef Gauntlets=new ItemDef{Prefab="GF_Jarngreipr",Name="Járngreipr",Base="TrinketBlackDamageHealth",
            Description="Thor's iron gauntlets. Mjölnir comes back to the hand that threw it, and only a hand in these can catch it.",
            Cost="Iron:20, BlackMetal:10, WolfPelt:4, Thunderstone:1",Station="forge",Level=2,Weight=3f,
            Model=t=>{Meshes.Add(t,"jarngreipr","pair");Meshes.Add(t,"jarngreipr","pairglow");},
            Worn=WornParts()};
        internal override IEnumerable<ItemDef> Items=>new[]{Gauntlets};

        // What's worn: an iron cuff on each forearm, and an iron plate over the back of every finger and thumb joint, each on its own bone
        // so the fingers bend in them (the pinky's a size smaller; the left thumb's turned about the bone, the thumbs curling the other way
        // on that hand). The back of the hand is the game's own: the Protector's plate (Register).
        private const bool RigidPlates=false;   // (0.2.10's plates, one rigid per finger joint: kept for trying)
        private static WornPart[] WornParts()
        {
            var parts=new List<WornPart>{new WornPart{Attach="attach_LeftForeArm",Model=Cuff},new WornPart{Attach="attach_RightForeArm",Model=Cuff}};
            // The gloves are made from the wearer's hands (Glove), but where the skin is too coarse to make iron from, a plate of its own:
            // the back of the hand, and the fingers' first joints (the middle's skin there is too coarse, and the rest match it).
            foreach(string side in new[]{"Left","Right"})
            {
                parts.Add(new WornPart{Attach=$"attach_{side}Hand",Model=t=>Plate(t,"backhand",1f,0f)});
                foreach(string finger in new[]{"Index","Middle","Ring","Pinky"})   // (every first joint alike: 0.2.40)
                {
                    float size=finger=="Pinky"?0.85f:1f;
                    parts.Add(new WornPart{Attach=$"attach_{side}Hand{finger}1",Model=t=>Plate(t,"mid1",size,0f)});
                }
            }
            if(!RigidPlates)return parts.ToArray();
            foreach(string side in new[]{"Left","Right"})
            {
                foreach(string finger in new[]{"Index","Middle","Ring","Pinky"})
                    for(int k=1;k<=3;k++)
                    {
                        string part=k==1?"seg1":k==2?"seg2":"tip";float size=finger=="Pinky"?0.85f:1f;
                        parts.Add(new WornPart{Attach=$"attach_{side}Hand{finger}{k}",Model=t=>Plate(t,part,size,0f)});
                    }
                for(int k=1;k<=3;k++)
                {
                    string part=k==1?"thumb1":k==2?"thumb2":"thumbtip";float turn=side=="Left"?180f:0f;
                    parts.Add(new WornPart{Attach=$"attach_{side}HandThumb{k}",Model=t=>Plate(t,part,1f,turn)});
                }
            }
            return parts.ToArray();
        }
        private static void Plate(Transform attach,string part,float size,float turn)
        {
            GameObject g=Meshes.Add(attach,"jarngreipr",part,"gf_plate");
            if(g==null)return;
            g.transform.localRotation=Quaternion.Euler(0,turn,0);g.transform.localScale=Vector3.one*size;
        }

        // The back of the hand: the Protector's plate (Breastplate of the Protector), borrowed whole and cut down when worn (WornSkin keeps
        // only the hand's bones; with ForearmLook Protector, its forearm armour too, in place of the iron cuffs).
        private static ConfigEntry<string> _hand,_forearm;
        internal override void Bind(ConfigFile c)
        {
            const string s="2 - Járngreipr";
            _hand=c.Bind(s,"HandFrom","","Another game armour to wear on the hands as well, cut down to them (its prefab name, as ArmorDeepNorthHeavyChest; empty: none, the default). Needs a restart.");
            _forearm=c.Bind(s,"ForearmLook","Cuffs",new ConfigDescription("Cuffs: Járngreipr's iron cuffs on the forearms. Protector: the hand armour's own forearm plates instead. Needs a restart.",
                new AcceptableValueList<string>("Cuffs","Protector")));
        }
        internal override void Stop()=>Glove.Clear();
        internal override void Register(ZNetScene scene)
        {
            if(Gauntlets.Built==null||Gauntlets.Built.transform.Find("attach_skin")!=null||string.IsNullOrEmpty(_hand?.Value))return;
            GameObject src=scene.GetPrefab(_hand.Value);
            if(src==null){Plugin.Warn($"Járngreipr: the game has no {_hand.Value} to take the hand plate from");return;}
            Borrow(src,_forearm.Value=="Protector"?"arm":"hand",_forearm.Value=="Protector");
        }
        internal static bool Worn(Humanoid h)=>Things.Wears(h,Gauntlets);

        // How each bracer sits in its forearm bone's frame (the model runs along y from the elbow): set live with `gf cuff`.
        private static Vector3 _euler=Vector3.zero,_position=Vector3.zero,_scale=Vector3.one;
        private static void Cuff(Transform attach)
        {
            var g=new GameObject("gf_cuff"){layer=attach.gameObject.layer};
            g.transform.SetParent(attach,false);
            g.transform.localPosition=_position;g.transform.localRotation=Quaternion.Euler(_euler);g.transform.localScale=_scale;
            Meshes.Add(g.transform,"jarngreipr","cuff");
            Meshes.Add(g.transform,"jarngreipr","glow");
        }

        // The real forearms, from the player's body mesh as posed now: for each side, the arm's radius every 2 cm from the elbow (vertices
        // weighted mostly to the forearm bone, measured round the line from elbow to wrist), and where the worn cuffs are and how big.
        private static JObject Measure()
        {
            var o=new JObject();
            Player me=Player.m_localPlayer;
            SkinnedMeshRenderer body=me!=null?me.GetComponent<VisEquipment>()?.m_bodyModel:null;
            if(body==null)return new JObject{["error"]="no body"};
            var baked=new Mesh();body.BakeMesh(baked);
            Vector3[] v=baked.vertices;
            BoneWeight[] w=body.sharedMesh.boneWeights;
            Transform[] bones=body.bones;
            Matrix4x4 toWorld=Matrix4x4.TRS(body.transform.position,body.transform.rotation,Vector3.one);
            o["bodyScale"]=Tools.V(body.transform.lossyScale);o["weights"]=w.Length;o["verts"]=v.Length;
            // Every bone the body is skinned to, with how many vertices each carries most of.
            var count=new int[bones.Length];
            foreach(BoneWeight b in w){int t=b.weight0>=b.weight1?b.boneIndex0:b.boneIndex1;if(t>=0&&t<count.Length)count[t]++;}
            var bl=new JArray();for(int i=0;i<bones.Length;i++)bl.Add($"{i} {(bones[i]!=null?bones[i].name:"-")} {count[i]}");
            o["bones"]=bl;
            foreach(string side in new[]{"Left","Right"})
            {
                Transform arm=Utils.FindChild(me.transform,side+"ForeArm"),hand=Utils.FindChild(me.transform,side+"Hand");
                int bi=System.Array.IndexOf(bones,arm);
                if(arm==null||hand==null){o[side]="no bones";continue;}
                Vector3 axis=(hand.position-arm.position).normalized;float len=Vector3.Distance(arm.position,hand.position);
                var rmax=new float[16];var rmin=new float[16];var n=new int[16];
                for(int i=0;i<16;i++)rmin[i]=9f;
                for(int i=0;i<v.Length&&i<w.Length;i++)
                {
                    BoneWeight b=w[i];
                    float wt=b.boneIndex0==bi?b.weight0:b.boneIndex1==bi?b.weight1:b.boneIndex2==bi?b.weight2:b.boneIndex3==bi?b.weight3:0f;
                    if(wt<0.5f)continue;
                    Vector3 p=toWorld.MultiplyPoint3x4(v[i])-arm.position;
                    float t=Vector3.Dot(p,axis),r=(p-axis*t).magnitude;
                    int k=Mathf.FloorToInt(t/0.02f);
                    if(k<0||k>=16)continue;
                    rmax[k]=Mathf.Max(rmax[k],r);rmin[k]=Mathf.Min(rmin[k],r);n[k]++;
                }
                var prof=new JArray();
                for(int k=0;k<16;k++)if(n[k]>0)prof.Add($"{k*2}-{k*2+2}cm: r {rmin[k]:F3}..{rmax[k]:F3} ({n[k]})");
                var cuffs=new JArray();
                foreach(Transform t in arm.GetComponentsInChildren<Transform>(true))
                {
                    if(t.name!="gf_cuff")continue;
                    Bounds bb=new Bounds(t.position,Vector3.zero);
                    foreach(Renderer r in t.GetComponentsInChildren<Renderer>())bb.Encapsulate(r.bounds);
                    Vector3 c=bb.center-arm.position;
                    cuffs.Add(new JObject{["lossyScale"]=Tools.V(t.lossyScale),["boundsSize"]=Tools.V(bb.size),
                        ["centreAlong"]=Math.Round(Vector3.Dot(c,axis),3),["centreOff"]=Math.Round((c-axis*Vector3.Dot(c,axis)).magnitude,3),
                        ["upAxisVsArm"]=Math.Round(Vector3.Angle(t.up,axis),1)});
                }
                o[side]=new JObject{["boneIndex"]=bi,["length"]=Math.Round(len,3),["profile"]=prof,["cuffs"]=cuffs};
            }
            Object.Destroy(baked);
            // Which prefab the game draws worn gauntlets from (ObjectDB, by name hash), against this copy's.
            GameObject inDb=ObjectDB.instance!=null?ObjectDB.instance.GetItemPrefab(Gauntlets.Prefab.GetStableHashCode()):null;
            Transform Sc(GameObject g)=>g!=null?g.transform.Find("attach_RightForeArm/gf_cuff"):null;
            ItemDrop.ItemData worn=me.GetInventory().GetEquippedItems().Find(Gauntlets.Is);
            o["prefabs"]=new JObject{["dbIsOurs"]=inDb==Gauntlets.Built,["dbCuffScale"]=Sc(inDb)!=null?Tools.V(Sc(inDb).localScale):null,
                ["ourCuffScale"]=Sc(Gauntlets.Built)!=null?Tools.V(Sc(Gauntlets.Built).localScale):null,
                ["wornDropIsOurs"]=worn!=null&&worn.m_dropPrefab==Gauntlets.Built,["dbCount"]=ObjectDB.instance!=null?ObjectDB.instance.m_items.FindAll(i=>i!=null&&i.name==Gauntlets.Prefab).Count:0};
            return o;
        }

        // The hand's bones (the hand and every finger joint under it), each in its own frame (turned with the bone, unscaled): which way
        // its first child lies and how far, and the box round the skin it carries (vertices weighted mostly to it), as the glove is fitted to.
        private static JObject Hand(string side)
        {
            Player me=Player.m_localPlayer;
            SkinnedMeshRenderer body=me!=null?me.GetComponent<VisEquipment>()?.m_bodyModel:null;
            Transform hand=me!=null?Utils.FindChild(me.transform,side+"Hand"):null;
            if(body==null||hand==null)return new JObject{["error"]="no body or hand"};
            var baked=new Mesh();body.BakeMesh(baked);
            Vector3[] v=baked.vertices;BoneWeight[] w=body.sharedMesh.boneWeights;Transform[] bones=body.bones;
            Matrix4x4 toWorld=Matrix4x4.TRS(body.transform.position,body.transform.rotation,Vector3.one);
            var o=new JObject();
            foreach(Transform b in hand.GetComponentsInChildren<Transform>(true))
            {
                if(b.name.StartsWith("attach")||b.name.StartsWith("gf_")||b.GetComponent<Renderer>()!=null)continue;
                int bi=Array.IndexOf(bones,b);
                Quaternion inv=Quaternion.Inverse(b.rotation);
                bool any=false;Vector3 lo=Vector3.zero,hi=Vector3.zero;int n=0;
                if(bi>=0)for(int i=0;i<v.Length&&i<w.Length;i++)
                {
                    BoneWeight bw=w[i];
                    float wt=bw.boneIndex0==bi?bw.weight0:bw.boneIndex1==bi?bw.weight1:bw.boneIndex2==bi?bw.weight2:bw.boneIndex3==bi?bw.weight3:0f;
                    if(wt<0.5f)continue;
                    Vector3 p=inv*(toWorld.MultiplyPoint3x4(v[i])-b.position);
                    if(!any){lo=hi=p;any=true;}else{lo=Vector3.Min(lo,p);hi=Vector3.Max(hi,p);}
                    n++;
                }
                Transform child=null;
                foreach(Transform c in b)if(!c.name.StartsWith("attach")&&!c.name.StartsWith("gf_")){child=c;break;}
                o[b.name]=new JObject{["parent"]=b.parent!=null?b.parent.name:"",["inSkin"]=bi>=0,["verts"]=n,
                    ["skinMin"]=any?Tools.V(lo):null,["skinMax"]=any?Tools.V(hi):null,
                    ["child"]=child!=null?child.name:"",["toChild"]=child!=null?Tools.V(inv*(child.position-b.position)):null,
                    ["childUp"]=child!=null?Tools.V(inv*child.up):null};   // (curled: the next joint's axis leans towards the palm)
            }
            Object.Destroy(baked);
            return o;
        }

        // Every game item worn on the body with a skinned mesh (attach_skin, bound to the body's bones when worn), and how much of it the
        // hands carry: to borrow a real glove instead of making one.
        private static JObject Scan()
        {
            Player me=Player.m_localPlayer;
            SkinnedMeshRenderer body=me!=null?me.GetComponent<VisEquipment>()?.m_bodyModel:null;
            if(body==null||ObjectDB.instance==null)return new JObject{["error"]="no body"};
            string[] names=Array.ConvertAll(body.bones,b=>b!=null?b.name:"");
            var rows=new List<(int hand,JObject row)>();
            foreach(GameObject item in ObjectDB.instance.m_items)
            {
                if(item==null)continue;
                var kids=new JArray();int handVerts=0,total=0,fingerVerts=0;
                foreach(Transform c in item.transform)
                {
                    if(!c.name.StartsWith("attach"))continue;
                    kids.Add(c.name);
                    if(c.name!="attach_skin")continue;
                    foreach(SkinnedMeshRenderer r in c.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                    {
                        Mesh m=r.sharedMesh;if(m==null)continue;
                        BoneWeight[] w;try{w=m.boneWeights;}catch{continue;}
                        total+=w.Length;
                        foreach(BoneWeight b in w)
                        {
                            int i=b.weight0>=b.weight1?b.boneIndex0:b.boneIndex1;
                            string n=i>=0&&i<names.Length?names[i]:"";
                            if(n.EndsWith("Hand"))handVerts++;
                            else if(n.Contains("Hand"))fingerVerts++;
                        }
                    }
                }
                if(handVerts+fingerVerts<20)continue;
                rows.Add((handVerts+fingerVerts,new JObject{["item"]=item.name,["name"]=item.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name,
                    ["handVerts"]=handVerts,["fingerVerts"]=fingerVerts,["totalVerts"]=total,["attach"]=kids}));
            }
            rows.Sort((a,b)=>b.hand.CompareTo(a.hand));
            var o=new JObject{["bodyBones"]=names.Length};
            var list=new JArray();foreach(var r in rows)list.Add(r.row);
            o["items"]=list;
            return o;
        }

        // Tries another item's skinned mesh as the gauntlets: every attach_skin of that item copied onto Járngreipr. The game's meshes
        // can't be read or cut, so when it's worn (WornSkin) every bone but those kept ("hand": the hands and fingers; "arm": the forearms
        // too; "all") is swapped for a point of no size at the hand on its side: the rest of the mesh shrinks into the hand and only the
        // glove shows, bending with the fingers as the game's own armour does. Re-equip to see it. nocuff hides the iron cuffs meanwhile.
        internal static string Keep="hand";
        private static JObject Borrow(string from,string keep,bool noCuff)=>Borrow(ObjectDB.instance!=null?ObjectDB.instance.GetItemPrefab(from):null,keep,noCuff);
        private static JObject Borrow(GameObject src,string keep,bool noCuff)
        {
            string from=src!=null?src.name:"";
            var skins=new List<Transform>();
            if(src!=null)foreach(Transform c in src.transform)if(c.name=="attach_skin")skins.Add(c);   // (armour has several)
            if(skins.Count==0||Gauntlets.Built==null)return new JObject{["error"]="no such item with attach_skin"};
            for(int i=Gauntlets.Built.transform.childCount-1;i>=0;i--)
                if(Gauntlets.Built.transform.GetChild(i).name=="attach_skin")Object.DestroyImmediate(Gauntlets.Built.transform.GetChild(i).gameObject);
            Keep=keep;
            int n=0;
            if(keep!="none")foreach(Transform skin in skins)
            {
                GameObject copy=Object.Instantiate(skin.gameObject,Gauntlets.Built.transform,false);
                copy.name="attach_skin";copy.SetActive(false);n++;
            }
            foreach(Transform c in Gauntlets.Built.transform)
            {
                if(c.name.StartsWith("attach_LeftForeArm"))c.name=noCuff?"attach_LeftForeArm_off":"attach_LeftForeArm";
                if(c.name.StartsWith("attach_RightForeArm"))c.name=noCuff?"attach_RightForeArm_off":"attach_RightForeArm";
            }
            return new JObject{["borrowed"]=from,["skins"]=n,["keep"]=keep,["cuffs"]=!noCuff};
        }
        internal static bool Kept(string bone)=>Keep=="all"||bone.Contains("Hand")||Keep=="arm"&&bone.Contains("ForeArm");

        // Where the worn glove's vertices are, as posed now, along the right forearm (elbow 0 to wrist 0.26 and past), 2 cm at a time, and
        // the body's for comparison: to see which skin it covers.
        // The back of the hand's real surface, in the hand bone's frame as posed now: every skin vertex of the hand and the fingers' first
        // joints, binned by how far along the hand (y) and across it (x), with the lowest and highest z in each bin (the back is -z).
        private static JObject BackOfHand(string side)
        {
            Player me=Player.m_localPlayer;
            SkinnedMeshRenderer body=me!=null?me.GetComponent<VisEquipment>()?.m_bodyModel:null;
            Transform hand=me!=null?Utils.FindChild(me.transform,side+"Hand"):null;
            if(body==null||hand==null)return new JObject{["error"]="no body"};
            var m=new Mesh();body.BakeMesh(m);
            Matrix4x4 w=Matrix4x4.TRS(body.transform.position,body.transform.rotation,Vector3.one);
            BoneWeight[] bw=body.sharedMesh.boneWeights;Vector3[] v=m.vertices;Transform[] bones=body.bones;
            Quaternion inv=Quaternion.Inverse(hand.rotation);
            var rows=new SortedDictionary<string,float[]>();
            for(int i=0;i<v.Length&&i<bw.Length;i++)
            {
                int t=bw[i].weight0>=bw[i].weight1?bw[i].boneIndex0:bw[i].boneIndex1;
                string nm=t>=0&&t<bones.Length&&bones[t]!=null?bones[t].name:"";
                if(!(nm==side+"Hand"||nm.StartsWith(side+"Hand")&&nm.EndsWith("1")&&!nm.Contains("Thumb")))continue;
                Vector3 p=inv*(w.MultiplyPoint3x4(v[i])-hand.position);
                string key=$"y{Mathf.FloorToInt(p.y/0.02f)*2,3} x{Mathf.FloorToInt(p.x/0.02f)*2,3}";
                if(!rows.TryGetValue(key,out float[] r))rows[key]=r=new[]{9f,-9f,0f};
                r[0]=Mathf.Min(r[0],p.z);r[1]=Mathf.Max(r[1],p.z);r[2]++;
            }
            Object.Destroy(m);
            var o=new JObject();foreach(var kv in rows)o[kv.Key]=$"z {kv.Value[0]:F3}..{kv.Value[1]:F3} ({kv.Value[2]})";
            return o;
        }

        private static string Path(Transform t){var p=t.name;while(t.parent!=null&&p.Length<300){t=t.parent;p=t.name+"/"+p;}return p;}
        private static JObject GloveAlong()
        {
            Player me=Player.m_localPlayer;
            SkinnedMeshRenderer body=me!=null?me.GetComponent<VisEquipment>()?.m_bodyModel:null;
            SkinnedMeshRenderer glove=null;
            if(body!=null)foreach(SkinnedMeshRenderer r in body.transform.parent.GetComponentsInChildren<SkinnedMeshRenderer>(true))if(r.name=="gf_glove")glove=r;
            Transform arm=me!=null?Utils.FindChild(me.transform,"RightForeArm"):null,hand=me!=null?Utils.FindChild(me.transform,"RightHand"):null;
            if(body==null||glove==null||arm==null)return new JObject{["error"]="no glove worn"};
            Vector3 axis=(hand.position-arm.position).normalized;
            JArray Along(SkinnedMeshRenderer r)
            {
                var m=new Mesh();r.BakeMesh(m);
                Matrix4x4 w=Matrix4x4.TRS(r.transform.position,r.transform.rotation,Vector3.one);
                var n=new int[24];var rr=new float[24];
                foreach(Vector3 v in m.vertices)
                {
                    Vector3 p=w.MultiplyPoint3x4(v)-arm.position;float t=Vector3.Dot(p,axis),rad=(p-axis*t).magnitude;
                    int k=Mathf.FloorToInt((t+0.06f)/0.02f);
                    if(k<0||k>=24||rad>0.15f)continue;
                    n[k]++;rr[k]=Mathf.Max(rr[k],rad);
                }
                Object.Destroy(m);
                var a=new JArray();for(int k=0;k<24;k++)if(n[k]>0)a.Add($"{k*2-6}cm: {n[k]} r<={rr[k]:F3}");
                return a;
            }
            // The glove's own forearm vertices (most weight on RightForeArm), where they are now.
            var fa=new JArray();
            {
                var m=new Mesh();glove.BakeMesh(m);
                Matrix4x4 wm=Matrix4x4.TRS(glove.transform.position,glove.transform.rotation,Vector3.one);
                BoneWeight[] bw=glove.sharedMesh.boneWeights;Vector3[] bv=m.vertices;
                int fi=Array.FindIndex(glove.bones,b=>b!=null&&b.name=="RightForeArm");
                Transform gb=fi>=0?glove.bones[fi]:null;
                fa.Add($"glove's RightForeArm is the found one: {gb==arm}; its path {(gb!=null?Path(gb):"-")}; found {Path(arm)}; body's same as glove's: {fi>=0&&body.bones[fi]==gb}");
                for(int i=0;i<bv.Length&&i<bw.Length&&fa.Count<12;i++)
                {
                    if(bw[i].boneIndex0!=fi)continue;
                    Vector3 p=wm.MultiplyPoint3x4(bv[i])-arm.position;float t=Vector3.Dot(p,axis);
                    fa.Add($"t {t:F3} r {(p-axis*t).magnitude:F3} w0 {bw[i].weight0:F2} b1 {bw[i].boneIndex1}:{bw[i].weight1:F2}");
                }
                Object.Destroy(m);
                fa.Add($"forearm index {fi}, glove bones {glove.bones.Length}, mesh bindposes {glove.sharedMesh.bindposes.Length}, blendshapes body {body.sharedMesh.blendShapeCount}");
            }
            return new JObject{["forearmVerts"]=fa,["glove"]=Along(glove),["body"]=Along(body),["gloveVerts"]=glove.sharedMesh!=null?glove.sharedMesh.vertexCount:0,
                ["gloveScale"]=Tools.V(glove.transform.lossyScale),["bodyScale"]=Tools.V(body.transform.lossyScale),["gloveParent"]=glove.transform.parent.name};
        }

        internal override string Command=>"cuff";
        internal override string Help=>"[ex ey ez px py pz [sx sy sz]]: turn, move and size the worn gauntlets on the forearms, live (no numbers: where they are)";
        internal override IEnumerator Test(string[] args,System.Action<JObject> output,System.Action<string> error)
        {
            if(args.Length>2&&args[2]=="measure"){output(Measure());return null;}
            if(args.Length>2&&args[2]=="hand"){output(Hand(args.Length>3?args[3]:"Right"));return null;}
            if(args.Length>2&&args[2]=="scan"){output(Scan());return null;}
            if(args.Length>2&&args[2]=="glove"){output(GloveAlong());return null;}
            if(args.Length>2&&args[2]=="backhand"){output(BackOfHand(args.Length>3?args[3]:"Right"));return null;}
            if(args.Length>3&&args[2]=="borrow"){output(Borrow(args[3],args.Length>4?args[4]:"hand",args.Length>5&&args[5]=="nocuff"));return null;}
            if(args.Length>=5)
            {
                float A(int i)=>args.Length>i?float.Parse(args[i],CultureInfo.InvariantCulture):0f;
                _euler=new Vector3(A(2),A(3),A(4));_position=new Vector3(A(5),A(6),A(7));
                _scale=args.Length>10?new Vector3(A(8),A(9),A(10)):Vector3.one;
            }
            int moved=0;
            var roots=new List<Transform>();
            if(Gauntlets.Built!=null)roots.Add(Gauntlets.Built.transform);
            if(Player.m_localPlayer!=null)roots.Add(Player.m_localPlayer.transform);
            foreach(Transform root in roots)
                foreach(Transform t in root.GetComponentsInChildren<Transform>(true))
                    if(t.name=="gf_cuff"){t.localPosition=_position;t.localRotation=Quaternion.Euler(_euler);t.localScale=_scale;moved++;}
            // Each forearm bone's frame: its scale, and where the elbow's next joint (the hand) lies in it (the way along the bone, and its length).
            var bones=new JObject();
            if(Player.m_localPlayer!=null)foreach(string side in new[]{"Left","Right"})
            {
                Transform arm=Utils.FindChild(Player.m_localPlayer.transform,side+"ForeArm"),hand=Utils.FindChild(Player.m_localPlayer.transform,side+"Hand"),
                    upper=Utils.FindChild(Player.m_localPlayer.transform,side+"Arm");
                if(arm==null)continue;
                bones[side]=new JObject{["scale"]=Tools.V(arm.lossyScale),["handDir"]=hand!=null?Tools.V((Quaternion.Inverse(arm.rotation)*(hand.position-arm.position)).normalized):null,
                    ["elbowToHandWorld"]=hand!=null?Math.Round(Vector3.Distance(arm.position,hand.position),3):0,
                    ["upperArmLengthWorld"]=upper!=null?Math.Round(Vector3.Distance(upper.position,arm.position),3):0};
            }
            output(new JObject{["euler"]=Tools.V(_euler),["position"]=Tools.V(_position),["scale"]=Tools.V(_scale),["moved"]=moved,["bones"]=bones});
            return null;
        }
    }

    // Járngreipr's borrowed skin, as it's put on: bones not kept go to a point of no size at the hand on their side (see Borrow).
    [HarmonyPatch(typeof(VisEquipment),"AttachArmor")]
    internal static class WornSkin
    {
        private static void Postfix(VisEquipment __instance,int itemHash,List<GameObject> __result)
        {
            if(__result==null||itemHash!=Jarngreipr.Gauntlets.Prefab.GetStableHashCode()||__instance.m_bodyModel==null)return;
            Transform root=__instance.m_bodyModel.rootBone;
            Transform Nowhere(string side)
            {
                Transform at=Utils.FindChild(__instance.transform,side+"Hand")??root;
                Transform p=at!=null?at.Find("gf_nowhere"):null;
                if(p==null&&at!=null){p=new GameObject("gf_nowhere").transform;p.SetParent(at,false);p.localScale=Vector3.zero;}
                return p;
            }
            Transform left=Nowhere("Left"),right=Nowhere("Right");
            foreach(GameObject go in __result)
            {
                if(go==null)continue;
                foreach(SkinnedMeshRenderer r in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    Transform[] b=(Transform[])r.bones.Clone();
                    for(int i=0;i<b.Length;i++)
                        if(b[i]!=null&&!Jarngreipr.Kept(b[i].name))b[i]=b[i].name.StartsWith("Left")?left:right;
                    r.bones=b;
                }
            }
            // (after: the glove is bound to the body's own bones, the forearms' too)
            Plugin.Safe("Járngreipr gloves",()=>{GameObject g=Glove.Wear(__instance.m_bodyModel);if(g!=null)__result.Add(g);});
        }
    }
}
