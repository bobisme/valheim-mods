using System;
using System.Collections.Generic;
using UnityEngine;
namespace Gary
{
    internal static class GaryVisuals
    {
        private sealed class Bone
        {
            internal Transform Transform;internal Quaternion Before,Applied;internal Vector3 Position,AppliedPosition;internal bool Changed;
            internal void Restore()
            {
                if(!Changed||Transform==null)return;
                // Animator can already have supplied a fresh pose. Restore only our own previous override.
                if(Quaternion.Angle(Transform.localRotation,Applied)<0.01f)Transform.localRotation=Before;
                if((Transform.localPosition-AppliedPosition).sqrMagnitude<1e-10f)Transform.localPosition=Position;
                Changed=false;
            }
            internal void Rotate(Quaternion delta,Vector3 shift=default)
            {
                if(Transform==null)return;Before=Transform.localRotation;Position=Transform.localPosition;
                Applied=Before*delta;AppliedPosition=Position+shift;
                Transform.localRotation=Applied;Transform.localPosition=AppliedPosition;Changed=true;
            }
        }
        private sealed class View
        {
            internal Character Gary;internal GameObject Crown,Pouch,Feather,Nest,Find;
            internal ZDOID Home;internal int FindKind=-1;
            internal LODGroup Lod;internal readonly List<Renderer> LodRenderers=new List<Renderer>();
            internal readonly List<Mesh> Meshes=new List<Mesh>();
            internal readonly Dictionary<string,Bone> Bones=new Dictionary<string,Bone>();
        }
        private static readonly Dictionary<Character,View> Views=new Dictionary<Character,View>();
        private static readonly Dictionary<Color,Material> Materials=new Dictionary<Color,Material>();
        private static Material Paint(Color color)
        {
            if(Materials.TryGetValue(color,out Material m))return m;
            // A shader name can resolve to a stripped/unsupported variant and render magenta on Linux.
            // Clone a material the running game already renders, including its native shader settings.
            Material basis=null;
            foreach(string name in new[]{"piece_cauldron","wood_floor","wood_stack"})
            {
                GameObject prefab=ZNetScene.instance.GetPrefab(name);if(prefab==null)continue;
                foreach(Renderer renderer in prefab.GetComponentsInChildren<Renderer>(true))
                    foreach(Material candidate in renderer.sharedMaterials)
                        if(candidate!=null&&candidate.shader!=null&&candidate.shader.isSupported&&
                            (candidate.name=="cauldron_chain"||candidate.name=="woodwall"))basis=candidate;
                if(basis!=null)break;
            }
            if(basis==null)return null;
            m=new Material(basis){name="Gary matte forest accessory"};
            if(m.HasProperty("_Color"))m.SetColor("_Color",color);
            if(m.HasProperty("_MainTex"))m.SetTexture("_MainTex",Texture2D.whiteTexture);
            foreach(string name in new[]{"_EmissionColor","_Emissive","_NoiseGlowColor"})if(m.HasProperty(name))m.SetColor(name,Color.black);
            foreach(string name in new[]{"_Metallic","_MetallicAlphaGloss","_Glossiness","_GlossMapScale","_BumpScale","_NoiseGlowEnabled","_ValueNoiseVertex","_AddRain"})
                if(m.HasProperty(name))m.SetFloat(name,0);
            if(m.HasProperty("_EmissionMap"))m.SetTexture("_EmissionMap",Texture2D.blackTexture);
            if(m.HasProperty("_MetallicTex"))m.SetTexture("_MetallicTex",Texture2D.blackTexture);
            m.DisableKeyword("_EMISSION");Materials.Add(color,m);return m;
        }
        private static GameObject Shape(Transform parent,PrimitiveType type,Vector3 position,Vector3 scale,Color color,Vector3 angles=default)
        {
            GameObject go=GameObject.CreatePrimitive(type);go.name="Gary cosmetic";
            Collider collider=go.GetComponent<Collider>();if(collider!=null){collider.enabled=false;UnityEngine.Object.Destroy(collider);}
            go.transform.SetParent(parent,false);go.transform.localPosition=position;go.transform.localScale=scale;go.transform.localRotation=Quaternion.Euler(angles);
            Renderer renderer=go.GetComponent<Renderer>();Material material=Paint(color);if(material!=null)renderer.sharedMaterial=material;else renderer.enabled=false;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;go.layer=LayerMask.NameToLayer("Default");return go;
        }
        private static GameObject Anchor(Transform bone,Character c,string name,Vector3 offset)
        {
            var go=new GameObject(name);go.transform.SetPositionAndRotation(bone.position+c.transform.rotation*offset,c.transform.rotation);go.transform.SetParent(bone,true);
            Vector3 scale=bone.lossyScale;
            go.transform.localScale=new Vector3(1/Mathf.Max(0.001f,Mathf.Abs(scale.x)),1/Mathf.Max(0.001f,Mathf.Abs(scale.y)),1/Mathf.Max(0.001f,Mathf.Abs(scale.z)));
            return go;
        }
        private static View Get(Character c)
        {
            if(Views.TryGetValue(c,out View view))return view;
            view=new View{Gary=c};
            Transform armature=c.transform.Find("Visual/Armature.001/root");
            if(armature!=null)
                foreach(Transform bone in armature.GetComponentsInChildren<Transform>(true))
                    if(!view.Bones.ContainsKey(bone.name))view.Bones[bone.name]=new Bone{Transform=bone};
            Transform head=BoneOf(view,"head")??c.transform;
            view.Crown=Anchor(head,c,"Gary's flower crown",new Vector3(0,0.25f,0));
            Color leaf=new Color(0.20f,0.29f,0.13f),stem=new Color(0.24f,0.17f,0.10f);
            for(int i=0;i<20;i++)
            {
                float angle=i*Mathf.PI/10,next=(i+1)*Mathf.PI/10;
                Vector3 start=new Vector3(Mathf.Cos(angle)*0.23f,0,Mathf.Sin(angle)*0.20f),end=new Vector3(Mathf.Cos(next)*0.23f,0,Mathf.Sin(next)*0.20f);
                GameObject twig=Shape(view.Crown.transform,PrimitiveType.Cylinder,(start+end)*0.5f,new Vector3(0.018f,(end-start).magnitude*0.55f,0.018f),stem);
                twig.transform.localRotation=Quaternion.FromToRotation(Vector3.up,end-start);
            }
            // Small, tapered feather vanes around the back/sides, leaving Gary's face open.
            for(int i=0;i<7;i++)
            {
                float degrees=180+i*30,angle=degrees*Mathf.Deg2Rad;
                var holder=new GameObject("crown feather");holder.transform.SetParent(view.Crown.transform,false);
                holder.transform.localPosition=new Vector3(Mathf.Cos(angle)*0.22f,0.01f,Mathf.Sin(angle)*0.19f);
                holder.transform.localRotation=Quaternion.Euler(0,90-degrees,0)*Quaternion.Euler(-18,0,0);
                Feather(view,holder.transform,0.18f+(i%3)*0.035f,0.025f,i%2==0?new Color(0.61f,0.58f,0.49f):new Color(0.42f,0.43f,0.39f));
            }
            Transform spine=BoneOf(view,"spine2")??c.transform;
            view.Pouch=Anchor(spine,c,"Gary's tiny forest pouch",new Vector3(0.42f,-0.10f,-0.03f));
            Shape(view.Pouch.transform,PrimitiveType.Sphere,Vector3.zero,new Vector3(0.24f,0.30f,0.16f),new Color(0.37f,0.23f,0.12f));
            Shape(view.Pouch.transform,PrimitiveType.Cube,new Vector3(0,0.10f,0.02f),new Vector3(0.25f,0.07f,0.17f),stem);
            Shape(view.Pouch.transform,PrimitiveType.Cube,new Vector3(0,0.22f,-0.04f),new Vector3(0.035f,0.23f,0.035f),stem,new Vector3(0,0,20));
            view.Feather=Anchor(head,c,"Gary's tucked feather",new Vector3(-0.28f,0.12f,-0.05f));
            view.Feather.transform.localRotation*=Quaternion.Euler(0,0,-25);
            Feather(view,view.Feather.transform,0.27f,0.035f,new Color(0.62f,0.60f,0.51f));
            Combine(view,view.Crown);Combine(view,view.Pouch);Combine(view,view.Feather);
            view.Lod=c.GetComponentInChildren<LODGroup>();
            if(view.Lod!=null)
            {
                foreach(GameObject prop in new[]{view.Crown,view.Pouch,view.Feather})view.LodRenderers.AddRange(prop.GetComponentsInChildren<Renderer>());
                LOD[] levels=view.Lod.GetLODs();
                for(int i=0;i<levels.Length;i++)
                {var renderers=new List<Renderer>(levels[i].renderers);renderers.AddRange(view.LodRenderers);levels[i].renderers=renderers.ToArray();}
                view.Lod.SetLODs(levels);
            }
            Views.Add(c,view);return view;
        }
        private static void Feather(View view,Transform parent,float length,float width,Color color)
        {
            const int rows=8,side=rows*3;
            var vertices=new Vector3[side*2];var uv=new Vector2[vertices.Length];var triangles=new List<int>();
            for(int face=0;face<2;face++)
            for(int row=0;row<rows;row++)
            {
                float t=row/(float)(rows-1),w=width*Mathf.Sin(t*Mathf.PI)*(row%2==0?1:0.82f);
                int i=face*side+row*3;float z=face==0?0.002f:-0.002f;
                vertices[i]=new Vector3(-w,length*t,z);vertices[i+1]=new Vector3(0,length*t,z*2);vertices[i+2]=new Vector3(w,length*t,z);
                uv[i]=new Vector2(0,t);uv[i+1]=new Vector2(0.5f,t);uv[i+2]=new Vector2(1,t);
                if(row==rows-1)continue;
                for(int col=0;col<2;col++)
                {
                    int a=i+col,b=a+1,c=a+3,d=b+3;
                    if(face==0)triangles.AddRange(new[]{a,b,c,c,b,d});else triangles.AddRange(new[]{a,c,b,c,d,b});
                }
            }
            Mesh mesh=new Mesh{name="Gary tapered feather"};mesh.vertices=vertices;mesh.uv=uv;mesh.triangles=triangles.ToArray();mesh.RecalculateNormals();mesh.RecalculateBounds();view.Meshes.Add(mesh);
            var go=new GameObject("matte feather vane");go.transform.SetParent(parent,false);go.AddComponent<MeshFilter>().sharedMesh=mesh;
            MeshRenderer renderer=go.AddComponent<MeshRenderer>();Material material=Paint(color);if(material!=null)renderer.sharedMaterial=material;else renderer.enabled=false;
            renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            Shape(parent,PrimitiveType.Cylinder,new Vector3(0,length*0.45f,0),new Vector3(0.006f,length*0.48f,0.006f),new Color(0.35f,0.29f,0.20f));
        }
        private static Transform BoneOf(View v,string name)=>v.Bones.TryGetValue(name,out Bone bone)?bone.Transform:null;
        private static Vector3 RootShift(View v,float metres)
        {return v.Bones.TryGetValue("root",out Bone root)&&root.Transform!=null&&root.Transform.parent!=null?
            root.Transform.parent.InverseTransformVector(v.Gary.transform.up*metres):Vector3.zero;}
        private static void Bend(View v,string name,Vector3 angles,Vector3 shift=default)
        {if(v.Bones.TryGetValue(name,out Bone bone)&&bone.Transform!=null)
            {
                Quaternion frame=v.Gary.transform.rotation,worldDelta=frame*Quaternion.Euler(angles)*Quaternion.Inverse(frame);
                Quaternion rotation=bone.Transform.rotation;
                bone.Rotate(Quaternion.Inverse(rotation)*worldDelta*rotation,shift);
            }}
        internal static Vector3 Hand(Character c)
        {
            if(c==null)return Vector3.zero;
            // Server fetch physics needs the bone, not render objects or materials.
            Transform hand=Views.TryGetValue(c,out View view)?BoneOf(view,"r_hand"):
                c.transform.Find("Visual/Armature.001/root/spine1/spine2/spine3/r_shoulder/r_arm1/r_arm2/r_hand");return hand!=null?hand.position:c.GetCenterPoint()+c.transform.forward*0.45f;
        }
        private static void Combine(View v,GameObject root)
        {
            var groups=new Dictionary<Material,List<CombineInstance>>();var originals=new List<GameObject>();
            foreach(MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
            {
                MeshRenderer renderer=filter.GetComponent<MeshRenderer>();Material material=renderer!=null?renderer.sharedMaterial:null;
                if(renderer==null||!renderer.enabled||material==null||filter.sharedMesh==null)continue;
                if(!groups.TryGetValue(material,out List<CombineInstance> group)){group=new List<CombineInstance>();groups.Add(material,group);}
                group.Add(new CombineInstance{mesh=filter.sharedMesh,transform=root.transform.worldToLocalMatrix*filter.transform.localToWorldMatrix});originals.Add(filter.gameObject);
            }
            foreach(GameObject go in originals){go.SetActive(false);UnityEngine.Object.Destroy(go);}
            foreach(var pair in groups)
            {
                Mesh mesh=new Mesh{name="Gary combined cosmetic"};mesh.CombineMeshes(pair.Value.ToArray(),true,true);v.Meshes.Add(mesh);
                var go=new GameObject("forest cosmetic mesh");go.transform.SetParent(root.transform,false);
                go.AddComponent<MeshFilter>().sharedMesh=mesh;MeshRenderer renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=pair.Key;
                renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
                if(pair.Key.shader==null||!pair.Key.shader.isSupported)renderer.enabled=false;
            }
        }
        private static void Nest(View view,ZDO z)
        {
            ZDOID id=z.GetZDOID(Activities.Home);GameObject pile=!id.IsNone()?ZNetScene.instance.FindInstance(id):null;
            Vector3 spot=z.GetVec3(Activities.HomeSpot,Vector3.zero);
            bool valid=Plugin.Instance.Nests.Value&&pile!=null&&Companion.Data(pile.transform)?.GetPrefab()=="wood_stack".GetStableHashCode()&&Vector3.Distance(pile.transform.position,spot)<=5;
            if(!valid){if(view.Nest!=null)UnityEngine.Object.Destroy(view.Nest);view.Nest=null;view.Home=ZDOID.None;return;}
            if(view.Nest!=null&&view.Home==id)return;
            if(view.Nest!=null)UnityEngine.Object.Destroy(view.Nest);
            view.Nest=new GameObject("Gary's branch and leaf nest");view.Nest.transform.position=spot;view.Home=id;
            for(int i=0;i<20;i++)
            {
                float angle=i*Mathf.PI/10;Vector3 point=new Vector3(Mathf.Cos(angle),0.05f+(i%3)*0.025f,Mathf.Sin(angle));
                Shape(view.Nest.transform,PrimitiveType.Cube,point,new Vector3(0.55f,0.06f,0.06f),new Color(0.23f,0.17f,0.10f),new Vector3(0,-i*18,8));
                Shape(view.Nest.transform,PrimitiveType.Sphere,point*0.70f,new Vector3(0.42f,0.035f,0.19f),new Color(0.18f+(i%3)*0.02f,0.27f,0.12f),new Vector3(0,i*31,0));
            }
        }
        private static void Find(View v,ZDO z,bool showing)
        {
            int kind=z.GetInt(Activities.ShowKind,0);
            if(!showing||kind<0||kind>=Nature.Gifts.Length){if(v.Find!=null)UnityEngine.Object.Destroy(v.Find);v.Find=null;v.FindKind=-1;return;}
            if(v.Find==null||v.FindKind!=kind)
            {
                if(v.Find!=null)UnityEngine.Object.Destroy(v.Find);v.Find=new GameObject("Gary's show-and-tell preview");v.FindKind=kind;
                // Copy only native mesh renderers. No ItemDrop, ZNetView, script, collider, or real item is cloned.
                GameObject prefab=ZNetScene.instance.GetPrefab(Nature.Gifts[kind]);
                if(prefab!=null)
                    foreach(MeshFilter mesh in prefab.GetComponentsInChildren<MeshFilter>(true))
                    {
                        MeshRenderer renderer=mesh.GetComponent<MeshRenderer>();if(renderer==null||mesh.sharedMesh==null)continue;
                        var copy=new GameObject("forest find mesh");copy.transform.SetParent(v.Find.transform,false);
                        copy.transform.localPosition=prefab.transform.InverseTransformPoint(mesh.transform.position);
                        copy.transform.localRotation=Quaternion.Inverse(prefab.transform.rotation)*mesh.transform.rotation;
                        copy.transform.localScale=mesh.transform.lossyScale;
                        copy.AddComponent<MeshFilter>().sharedMesh=mesh.sharedMesh;copy.AddComponent<MeshRenderer>().sharedMaterials=renderer.sharedMaterials;
                    }
            }
            v.Find.transform.SetPositionAndRotation(Hand(v.Gary),v.Gary.transform.rotation);
        }
        internal static void LateUpdate()
        {
            if(Plugin.Instance==null||ZNet.instance==null||ZNet.instance.IsDedicated())return;
            foreach(Character gone in new List<Character>(Views.Keys))if(gone==null||!Companion.Is(gone))Release(gone);
            foreach(Companion.State st in Companion.States.Values)
            {
                Character c=st.Body;if(c==null)continue;ZDO z=Companion.Data(c);if(z==null)continue;View v=Get(c);
                foreach(Bone b in v.Bones.Values)b.Restore();
                v.Crown.SetActive(Plugin.Instance.FlowerCrown.Value);v.Pouch.SetActive(Plugin.Instance.ForestPouch.Value);v.Feather.SetActive(Plugin.Instance.EarFeather.Value);
                Nest(v,z);
                int kind=z.GetLong(Activities.PoseUntil,0)>Companion.Now?z.GetInt(Activities.PoseKey,0):0;
                if(z.GetBool(Companion.Retreating,false)||c.InAttack()||(!BoatRide.IsRiding(c)&&c.GetVelocity().sqrMagnitude>1))kind=0;
                float t=(float)((Companion.Now-z.GetLong(Activities.PoseAt,0))/(double)TimeSpan.TicksPerSecond),wiggle=Mathf.Sin(t*9);
                switch(kind)
                {
                    case Activities.Wave:Bend(v,"r_arm1",new Vector3(0,0,130));Bend(v,"r_arm2",new Vector3(0,0,-35+wiggle*18));break;
                    case Activities.Cheer:Bend(v,"r_arm1",new Vector3(0,0,145));Bend(v,"l_arm1",new Vector3(0,0,-145));Bend(v,"head",new Vector3(-8,wiggle*8,0));break;
                    case Activities.Dance:Bend(v,"spine1",new Vector3(0,wiggle*15,wiggle*7));Bend(v,"r_arm1",new Vector3(0,0,55+wiggle*20));Bend(v,"l_arm1",new Vector3(0,0,-55+wiggle*20));break;
                    case Activities.Curl:
                        Bend(v,"root",Vector3.zero,RootShift(v,-0.5f));Bend(v,"spine1",new Vector3(35,0,8));Bend(v,"spine2",new Vector3(25,0,0));Bend(v,"head",new Vector3(25,0,0));
                        Bend(v,"r_arm1",new Vector3(0,25,25));Bend(v,"l_arm1",new Vector3(0,-25,-25));
                        Bend(v,"l_leg1",new Vector3(-55,0,0));Bend(v,"r_leg1",new Vector3(-55,0,0));Bend(v,"l_leg2",new Vector3(70,0,0));Bend(v,"r_leg2",new Vector3(70,0,0));break;
                    case Activities.Shake:Bend(v,"spine1",new Vector3(0,Mathf.Sin(t*24)*18,0));Bend(v,"head",new Vector3(0,Mathf.Sin(t*24)*22,0));break;
                    case Activities.Crouch:Bend(v,"root",Vector3.zero,RootShift(v,-0.3f));Bend(v,"spine1",new Vector3(22,0,0));Bend(v,"head",new Vector3(20,0,0));Bend(v,"l_leg1",new Vector3(-25,0,0));Bend(v,"r_leg1",new Vector3(-25,0,0));break;
                    case Activities.Show:Bend(v,"r_arm1",new Vector3(0,-50,55));Bend(v,"r_arm2",new Vector3(0,0,-35));Bend(v,"head",new Vector3(0,12,0));break;
                    case Activities.Proud:Bend(v,"head",new Vector3(-12,wiggle*5,0));break;
                    case Activities.Work:Bend(v,"r_arm1",new Vector3(wiggle*35,0,40));Bend(v,"r_arm2",new Vector3(0,0,-20));Bend(v,"head",new Vector3(8,0,0));break;
                }
                Find(v,z,kind==Activities.Show);
            }
        }
        private static void Release(Character c)
        {
            if(!Views.TryGetValue(c,out View v))return;
            foreach(Bone bone in v.Bones.Values)bone.Restore();
            if(v.Lod!=null)
            {
                LOD[] levels=v.Lod.GetLODs();
                for(int i=0;i<levels.Length;i++)
                {var renderers=new List<Renderer>(levels[i].renderers);renderers.RemoveAll(renderer=>v.LodRenderers.Contains(renderer));levels[i].renderers=renderers.ToArray();}
                v.Lod.SetLODs(levels);
            }
            foreach(GameObject go in new[]{v.Crown,v.Pouch,v.Feather,v.Nest,v.Find})if(go!=null)UnityEngine.Object.Destroy(go);
            foreach(Mesh mesh in v.Meshes)if(mesh!=null)UnityEngine.Object.Destroy(mesh);Views.Remove(c);
        }
        internal static void Clear()
        {
            foreach(Character c in new List<Character>(Views.Keys))Release(c);
            foreach(Material material in Materials.Values)if(material!=null)UnityEngine.Object.Destroy(material);Materials.Clear();
        }
    }
}
