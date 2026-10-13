using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Shieldwall
{
    // The Warstone's own look (Models/warstone): a split, iron-bound menhir on a plinth, war runes cut into it. It shows how the stone stands:
    // a slanting cut for each mark, climbing its front from the foot (an unwon mark's groove is filled with the very rock cut from it);
    // a cracked stone shows its cracks and its runes gutter; in a siege the runes flare, and fade and flicker as the stone weakens.
    // One mesh: the game's rock, its iron and a glowing rune paint (the runes and the marks' cuts); uvs are in metres, each material scaled to its texture.
    internal static class Look
    {
        internal const int Rock=0,Iron=1,Rune=2,Marks=Policy.MaxMarks;
        // Where each material comes from: "<prefab>[:<material index>]", and its texture repeats per metre.
        internal static string RockFrom="rock4_forest",IronFrom="iron_wall_1x1";
        internal static float RockScale=0.35f,IronScale=0.25f;
        internal static Color RockTint=new Color(0.45f,0.46f,0.5f);
        internal static readonly Color RuneDark=new Color(0.35f,0.06f,0.02f),RuneEmber=new Color(1.2f,0.32f,0.08f),RuneIdle=new Color(0.5f,0.11f,0.03f),
            CrackDark=new Color(0.03f,0.025f,0.025f),CrackGlow=new Color(0.08f,0.01f,0f);
        private static Mesh _mesh,_crack;private static Mesh[] _caps;private static Material[] _materials;private static Material _crackMaterial;
        private static readonly List<Object> Owned=new List<Object>();

        internal static Mesh Mesh{get{Load();return _mesh;}}
        internal static Mesh CrackMesh{get{Load();return _crack;}}
        private static void Load()
        {
            if(_mesh!=null)return;
            try
            {
                using(var raw=typeof(Look).Assembly.GetManifestResourceStream("Shieldwall.warstone.json.gz"))
                {
                    if(raw==null)return;
                    using(var gz=new System.IO.Compression.GZipStream(raw,System.IO.Compression.CompressionMode.Decompress))
                    using(var text=new System.IO.StreamReader(gz))
                    {
                        var d=JObject.Parse(text.ReadToEnd());
                        _crack=Build((JObject)d["crack"],"BobWarstoneCracks");
                        _caps=(d["caps"] as JArray)?.Select((c,i)=>Build((JObject)c,"BobWarstoneMark"+i)).ToArray();
                        _mesh=Build(d,"BobWarstone");
                    }
                }
            }
            catch(System.Exception e){Plugin.Log("Shieldwall: the Warstone's model: "+e.Message);_mesh=null;}
        }
        private static Mesh Build(JObject d,string name)
        {
            if(d==null)return null;
            float[] v=d["v"].ToObject<float[]>(),n=d["n"].ToObject<float[]>(),uv=d["uv"].ToObject<float[]>();
            int[][] t=d["t"].ToObject<int[][]>();
            var mesh=new Mesh{name=name};
            var verts=new Vector3[v.Length/3];var norms=new Vector3[verts.Length];var uvs=new Vector2[verts.Length];
            for(int i=0;i<verts.Length;i++){verts[i]=new Vector3(v[3*i],v[3*i+1],v[3*i+2]);norms[i]=new Vector3(n[3*i],n[3*i+1],n[3*i+2]);uvs[i]=new Vector2(uv[2*i],uv[2*i+1]);}
            mesh.vertices=verts;mesh.normals=norms;mesh.uv=uvs;mesh.colors=Enumerable.Repeat(Color.white,verts.Length).ToArray(); // (the piece shaders read the vertex colour)
            mesh.subMeshCount=t.Length;
            for(int k=0;k<t.Length;k++)mesh.SetTriangles(t[k],k);
            mesh.RecalculateBounds();mesh.RecalculateTangents();
            Owned.Add(mesh);
            return mesh;
        }

        internal static Material[] Materials
        {
            get
            {
                if(_materials!=null&&_materials.All(m=>m!=null))return _materials;
                Material rock=Copy(From(RockFrom),"BobWarstoneRock",RockScale,RockTint),iron=Copy(From(IronFrom),"BobWarstoneIron",IronScale);
                if(rock==null||iron==null)return null;
                // The runes: the rock's own material darkened to old blood, glowing like embers (brighter in a siege, see Glow).
                Material rune=Paint("BobWarstoneRune",RuneDark,RuneIdle);
                // the cracks: the iron's plain material near black (the rock's shader lightens anything painted with it)
                _crackMaterial=Copy(From(IronFrom),"BobWarstoneCrack",1);
                if(_crackMaterial!=null){_crackMaterial.color=CrackDark;if(_crackMaterial.HasProperty("_MainTex"))_crackMaterial.SetTexture("_MainTex",null);if(_crackMaterial.HasProperty("_EmissionColor")){_crackMaterial.EnableKeyword("_EMISSION");_crackMaterial.SetColor("_EmissionColor",CrackGlow);}}
                return _materials=new[]{rock,iron,rune};
            }
        }
        // How a stone looks for how it stands: its marks gilded, its cracks, its runes (dim and guttering when cracked; flaring in a siege,
        // fading and flickering as it weakens). Called every frame by the stone; the materials change only when the marks or the crack do.
        private static MaterialPropertyBlock _block;
        internal static int TestMarks=-1,TestCracked=-1,TestHealth=-1; // (test: shown instead of the stone's own, see siege look)
        internal static void Status(Warstone stone,ref int shownMarks,ref int shownCracked)
        {
            Transform model=stone.transform.Find("Model");
            Renderer r=model!=null?model.GetComponent<MeshRenderer>():null;
            if(r==null)return;
            int marks=Policy.Marks(TestMarks>=0?TestMarks:stone.Marks);bool cracked=TestCracked>=0?TestCracked==1:stone.Cracked;
            if(marks!=shownMarks)
            {
                shownMarks=marks;
                for(int i=0;i<Marks;i++){Transform cap=model.Find("Mark"+i);if(cap!=null)cap.gameObject.SetActive(i>=marks);} // a won mark's cut is open
            }
            if((cracked?1:0)!=shownCracked)
            {
                shownCracked=cracked?1:0;
                Transform crack=model.Find("Crack");
                if(crack!=null)crack.gameObject.SetActive(cracked);
            }
            Color glow=RuneIdle;
            float t=Time.time+stone.transform.position.x;
            if(stone.Phase==Phase.Battle||stone.Phase==Phase.Gathering||TestHealth>=0)
            {
                ZDO z=stone.Z;
                float health=z==null?1:Mathf.Clamp01(z.GetFloat(Stone.HealthKey,1)/Mathf.Max(1,z.GetFloat(Stone.MaxHealthKey,1)));
                if(stone.Phase==Phase.Gathering)health=1;
                if(TestHealth>=0)health=TestHealth/100f;
                glow=Color.Lerp(RuneIdle*0.3f,RuneEmber,health);
                if(health<0.3f)glow*=0.55f+0.45f*Mathf.PerlinNoise(t*7,0); // about to break: the fire in it stutters
            }
            else if(cracked)glow=RuneIdle*(0.12f+0.2f*Mathf.PerlinNoise(t*0.6f,0)); // its fire all but out
            if(_block==null)_block=new MaterialPropertyBlock();
            _block.Clear();_block.SetColor("_EmissionColor",glow);
            r.SetPropertyBlock(_block,Rune);
        }

        // The rock's own material in a paint colour, glowing.
        private static Material Paint(string name,Color colour,Color glow)
        {
            Material m=Copy(From(RockFrom),name,RockScale);
            if(m==null)return null;
            m.color=colour;
            if(m.HasProperty("_EmissionColor")){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",glow);}
            if(m.HasProperty("_EmissiveTex"))m.SetTexture("_EmissiveTex",Texture2D.whiteTexture);
            return m;
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
            for(int i=0;_caps!=null&&i<_caps.Length;i++)
            {
                Transform cap=model.Find("Mark"+i);
                if(cap==null){cap=new GameObject("Mark"+i){layer=model.gameObject.layer}.transform;cap.SetParent(model,false);}
                (cap.GetComponent<MeshFilter>()??cap.gameObject.AddComponent<MeshFilter>()).sharedMesh=_caps[i];
                (cap.GetComponent<MeshRenderer>()??cap.gameObject.AddComponent<MeshRenderer>()).sharedMaterials=new[]{materials[Rock]};
            }
            if(CrackMesh!=null&&_crackMaterial!=null)
            {
                Transform crack=model.Find("Crack");
                if(crack==null){crack=new GameObject("Crack"){layer=model.gameObject.layer}.transform;crack.SetParent(model,false);crack.gameObject.SetActive(false);}
                (crack.GetComponent<MeshFilter>()??crack.gameObject.AddComponent<MeshFilter>()).sharedMesh=CrackMesh;
                var cr=crack.GetComponent<MeshRenderer>()??crack.gameObject.AddComponent<MeshRenderer>();cr.sharedMaterials=new[]{_crackMaterial};cr.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            if(model.GetComponent<Collider>()==null&&model.Find("plinth")==null)
            {
                // Boxes, not the mesh: the game sets a ghost down by its nearest convex or primitive collider (with none it goes far off in the sky).
                Box(model,"plinth",new Vector3(0,0.02f,0.04f),new Vector3(2.5f,0.5f,2.1f));
                Box(model,"menhir",new Vector3(-0.08f,1.5f,0.06f),new Vector3(1.35f,2.6f,0.85f));
                Box(model,"head",new Vector3(-0.15f,3.0f,0.08f),new Vector3(0.7f,0.6f,0.5f));
            }
            return true;
        }
        private static void Box(Transform model,string name,Vector3 centre,Vector3 size)
        {
            var part=new GameObject(name){layer=model.gameObject.layer};part.transform.SetParent(model,false);
            var box=part.AddComponent<BoxCollider>();box.center=centre;box.size=size;
        }
        // Test: change where the look's materials come from, then redress the prefab and every standing stone.
        internal static void Restyle()
        {
            _materials=null;_mesh=null;_crack=null;_caps=null;
            if(Stone.Prefab!=null)Dress(Stone.Prefab);
            foreach(Warstone w in Warstone.Loaded)if(w!=null){Transform kit=w.transform.Find("Model/Kit");if(kit!=null)Object.Destroy(kit.gameObject);Dress(w.gameObject);w.ShownMarks=w.ShownCracked=-1;} // (0.4.14 stones still carry their kit until a restart)
        }
        internal static void Forget(){foreach(Object o in Owned)if(o!=null)Object.Destroy(o);Owned.Clear();_mesh=null;_crack=null;_caps=null;_materials=null;_crackMaterial=null;}
    }
}
