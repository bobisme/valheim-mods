using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Shieldwall
{
    // The Warstone's own look (Models/warstone): a split, iron-bound menhir on a plinth ringed by stone shields, war runes cut into it.
    // One mesh, three materials: the game's rock, its iron and a glowing rune paint; uvs are in metres, each material scaled to its texture.
    internal static class Look
    {
        internal const int Rock=0,Iron=1,Rune=2;
        // Where each material comes from: "<prefab>[:<material index>]", and its texture repeats per metre.
        internal static string RockFrom="rock4_forest",IronFrom="iron_wall_1x1";
        internal static float RockScale=0.35f,IronScale=0.25f;
        internal static Color RockTint=new Color(0.45f,0.46f,0.5f);
        internal static readonly Color RuneDark=new Color(0.35f,0.06f,0.02f),RuneEmber=new Color(1.2f,0.32f,0.08f),RuneIdle=new Color(0.5f,0.11f,0.03f);
        private static Mesh _mesh;private static Material[] _materials;
        private static readonly List<Object> Owned=new List<Object>();

        internal static Mesh Mesh
        {
            get
            {
                if(_mesh!=null)return _mesh;
                try
                {
                    using(var raw=typeof(Look).Assembly.GetManifestResourceStream("Shieldwall.warstone.json.gz"))
                    {
                        if(raw==null)return null;
                        using(var gz=new System.IO.Compression.GZipStream(raw,System.IO.Compression.CompressionMode.Decompress))
                        using(var text=new System.IO.StreamReader(gz))
                        {
                            var d=JObject.Parse(text.ReadToEnd());
                            float[] v=d["v"].ToObject<float[]>(),n=d["n"].ToObject<float[]>(),uv=d["uv"].ToObject<float[]>();
                            int[][] t=d["t"].ToObject<int[][]>();
                            var mesh=new Mesh{name="BobWarstone"};
                            var verts=new Vector3[v.Length/3];var norms=new Vector3[verts.Length];var uvs=new Vector2[verts.Length];
                            for(int i=0;i<verts.Length;i++){verts[i]=new Vector3(v[3*i],v[3*i+1],v[3*i+2]);norms[i]=new Vector3(n[3*i],n[3*i+1],n[3*i+2]);uvs[i]=new Vector2(uv[2*i],uv[2*i+1]);}
                            mesh.vertices=verts;mesh.normals=norms;mesh.uv=uvs;mesh.colors=Enumerable.Repeat(Color.white,verts.Length).ToArray(); // (the piece shaders read the vertex colour)
                            mesh.subMeshCount=t.Length;
                            for(int k=0;k<t.Length;k++)mesh.SetTriangles(t[k],k);
                            mesh.RecalculateBounds();mesh.RecalculateTangents();
                            Owned.Add(mesh);
                            return _mesh=mesh;
                        }
                    }
                }
                catch(System.Exception e){Plugin.Log("Shieldwall: the Warstone's model: "+e.Message);return null;}
            }
        }

        internal static Material[] Materials
        {
            get
            {
                if(_materials!=null&&_materials.All(m=>m!=null))return _materials;
                Material rock=Copy(From(RockFrom),"BobWarstoneRock",RockScale,RockTint),iron=Copy(From(IronFrom),"BobWarstoneIron",IronScale);
                if(rock==null||iron==null)return null;
                // The runes: the rock's own material darkened to old blood, glowing like embers (brighter in a siege, see Glow).
                Material rune=Copy(From(RockFrom),"BobWarstoneRune",RockScale);
                rune.color=RuneDark;
                if(rune.HasProperty("_EmissionColor")){rune.EnableKeyword("_EMISSION");rune.SetColor("_EmissionColor",RuneIdle);}
                if(rune.HasProperty("_EmissiveTex"))rune.SetTexture("_EmissiveTex",Texture2D.whiteTexture);
                return _materials=new[]{rock,iron,rune};
            }
        }
        // A stone's runes burn bright while its siege is on.
        private static MaterialPropertyBlock _block;
        internal static void Glow(GameObject stone,bool siege)
        {
            Renderer r=stone.transform.Find("Model")?.GetComponent<MeshRenderer>();
            if(r==null||r.sharedMaterials.Length<=Rune)return;
            if(_block==null)_block=new MaterialPropertyBlock();
            _block.Clear();
            if(siege)_block.SetColor("_EmissionColor",RuneEmber);
            r.SetPropertyBlock(_block,Rune);
        }
        private static Material From(string spec)
        {
            string[] parts=spec.Split(':');
            GameObject prefab=Assets.Find(parts[0]);
            if(prefab==null)return null;
            int index=parts.Length>1&&int.TryParse(parts[1],out int i)?i:0;
            Renderer r=prefab.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(x=>x.sharedMaterials.Length>index&&x.sharedMaterials[index]!=null&&!x.sharedMaterials[index].name.StartsWith("snow"));
            return r!=null?r.sharedMaterials[index]:null;
        }
        private static Material Copy(Material from,string name,float scale,Color? tint=null)
        {
            if(from==null)return null;
            var m=new Material(from){name=name,hideFlags=HideFlags.HideAndDontSave};
            if(tint.HasValue&&m.HasProperty("_Color"))m.color=m.color*tint.Value;
            if(m.HasProperty("_MainTex"))m.mainTextureScale=Vector2.one*scale;
            Owned.Add(m);
            return m;
        }

        // Put the model (and its colliders) on a new Warstone prefab, or on stones already standing after a change of look.
        internal static bool Dress(GameObject go)
        {
            Mesh mesh=Mesh;Material[] materials=Materials;
            if(mesh==null||materials==null)return false;
            Transform model=go.transform.Find("Model");
            if(model==null){model=new GameObject("Model"){layer=LayerMask.NameToLayer("piece")}.transform;model.SetParent(go.transform,false);}
            (model.GetComponent<MeshFilter>()??model.gameObject.AddComponent<MeshFilter>()).sharedMesh=mesh;
            (model.GetComponent<MeshRenderer>()??model.gameObject.AddComponent<MeshRenderer>()).sharedMaterials=materials;
            if(model.Find("Kit")==null)Kit(model);
            if(model.GetComponent<Collider>()==null&&model.Find("plinth")==null)
            {
                // Boxes, not the mesh: the game sets a ghost down by its nearest convex or primitive collider (with none it goes far off in the sky).
                Box(model,"plinth",new Vector3(0,0.02f,0.04f),new Vector3(2.5f,0.5f,2.1f));
                Box(model,"menhir",new Vector3(-0.08f,1.5f,0.06f),new Vector3(1.35f,2.6f,0.85f));
                Box(model,"head",new Vector3(-0.15f,3.0f,0.08f),new Vector3(0.7f,0.6f,0.5f));
            }
            return true;
        }
        // The game's own arms about it: shields stood rim to rim round the plinth (its front left open), blades driven into the ground.
        private static readonly (string prefab,float angle,float lean,float roll)[] Shields=
        {
            ("ShieldBanded",-38,16,20),("ShieldBanded",-10,12,200),("ShieldIronBuckler",18,18,0),("ShieldBanded",44,10,95),("ShieldWood",68,15,300),
            ("ShieldBanded",92,13,160),("ShieldIronBuckler",116,17,40),("ShieldBanded",140,11,250),("ShieldBronzeBuckler",164,15,0),("ShieldBanded",192,13,130),("ShieldBanded",218,18,330),
        };
        // (point first: blades go in point down, the atgeir butt down)
        private static readonly (string prefab,Vector3 at,Vector3 dir,float depth,float roll,bool butt)[] Blades=
        {
            ("SwordIron",new Vector3(-1.05f,0.05f,-1.0f),new Vector3(0.25f,-1,0.3f),0.28f,30,false),
            ("AxeIron",new Vector3(0.82f,0.33f,-0.62f),new Vector3(-0.35f,-1,0.25f),0.12f,-60,false),
            ("AtgeirIron",new Vector3(1.05f,0.0f,0.5f),new Vector3(-0.12f,-1,-0.05f),0.4f,0,true),
            ("SwordIron",new Vector3(-1.3f,0.0f,0.45f),new Vector3(0.45f,-1,-0.15f),0.3f,110,false),
        };
        // A skull of the fallen set on the plinth, looking out.
        private static readonly (string prefab,Vector3 at,float yaw)[] Skulls=
        {
            ("TrophySkeleton",new Vector3(0.55f,0.3f,-0.68f),200),
        };
        private static void Kit(Transform model)
        {
            var kit=new GameObject("Kit"){layer=model.gameObject.layer}.transform;kit.SetParent(model,false);
            foreach(var (name,at,yaw) in Skulls)
            {
                Transform look=Part(kit,name,out Bounds b);if(look==null)continue;
                look.localRotation=Quaternion.Euler(0,yaw,0);look.localPosition=at-look.localRotation*new Vector3(b.center.x,b.min.y,b.center.z);
            }
            foreach(var (name,angle,lean,roll) in Shields)
            {
                Transform look=Part(kit,name,out Bounds b);if(look==null)continue;
                // the thinnest axis is the shield's face; it faces out and a little up, leaning back on the plinth with its rim on the ground
                Vector3 size=b.size;int thin=size.x<size.y?(size.x<size.z?0:2):(size.y<size.z?1:2);
                Vector3 face=Vector3.zero;face[thin]=1;
                float a=angle*Mathf.Deg2Rad,l=lean*Mathf.Deg2Rad,radius=Mathf.Max(size[(thin+1)%3],size[(thin+2)%3])/2;
                Vector3 outward=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a)),normal=outward*Mathf.Cos(l)+Vector3.up*Mathf.Sin(l);
                Quaternion turn=Quaternion.AngleAxis(roll,normal)*Quaternion.FromToRotation(face,normal);
                Vector3 centre=new Vector3(outward.x*1.4f,radius*Mathf.Cos(l)-0.05f,outward.z*1.14f);
                look.localRotation=turn;look.localPosition=centre-turn*b.center;
            }
            foreach(var (name,at,dir,depth,roll,butt) in Blades)
            {
                Transform look=Part(kit,name,out Bounds b);if(look==null)continue;
                // the longest axis is the blade; the end nearer the grip (the item's origin) goes into the ground for a pole, the far one for a blade
                Vector3 size=b.size;int along=size.x>size.y?(size.x>size.z?0:2):(size.y>size.z?1:2);
                Vector3 axis=Vector3.zero;axis[along]=1;
                if(Vector3.Dot(b.center,axis)>0!=butt)axis=-axis;
                Vector3 tip=b.center+axis*size[along]/2,down=dir.normalized;
                Quaternion turn=Quaternion.AngleAxis(roll,down)*Quaternion.FromToRotation(axis,down);
                look.localRotation=turn;look.localPosition=at+down*depth-turn*tip;
            }
        }
        // A copy of a game item's look under the kit, and its bounds in its own frame.
        private static Transform Part(Transform kit,string prefab,out Bounds bounds)
        {
            bounds=default;
            GameObject source=Assets.Find(prefab);
            GameObject look=source!=null?Assets.Model(source,kit,Vector3.zero,Quaternion.identity):null;
            if(look==null)return null;
            bool any=false;
            foreach(MeshFilter f in look.GetComponentsInChildren<MeshFilter>())
            {
                Bounds m=f.sharedMesh.bounds;Matrix4x4 to=look.transform.worldToLocalMatrix*f.transform.localToWorldMatrix;
                for(int i=0;i<8;i++)
                {
                    Vector3 c=to.MultiplyPoint3x4(m.center+Vector3.Scale(m.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1)));
                    if(!any){bounds=new Bounds(c,Vector3.zero);any=true;}else bounds.Encapsulate(c);
                }
            }
            foreach(Transform t in look.GetComponentsInChildren<Transform>(true))t.gameObject.layer=kit.gameObject.layer;
            return look.transform;
        }
        private static void Box(Transform model,string name,Vector3 centre,Vector3 size)
        {
            var part=new GameObject(name){layer=model.gameObject.layer};part.transform.SetParent(model,false);
            var box=part.AddComponent<BoxCollider>();box.center=centre;box.size=size;
        }
        // Test: change where the look's materials come from, then redress the prefab and every standing stone.
        internal static void Restyle()
        {
            _materials=null;_mesh=null;
            foreach(Transform kit in new[]{Stone.Prefab}.Concat(Warstone.Loaded.Where(w=>w!=null).Select(w=>w.gameObject)).Where(g=>g!=null).Select(g=>g.transform.Find("Model/Kit")).Where(k=>k!=null).ToList())Object.DestroyImmediate(kit.gameObject);
            if(Stone.Prefab!=null)Dress(Stone.Prefab);
            foreach(Warstone w in Warstone.Loaded)if(w!=null)Dress(w.gameObject);
        }
        internal static void Forget(){foreach(Object o in Owned)if(o!=null)Object.Destroy(o);Owned.Clear();_mesh=null;_materials=null;}
    }
}
