using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Shieldwall
{
    // Each war stave's own look (Models/stave/staves.py): Ember a charred staff round a smouldering coal, Frost a burst of ice, Thunder a
    // split limb round a storm-stone, Blast a club caged with fire-pots, Hearth a crook hung with a lantern. Made at the size it stands in its
    // socket (foot at 0, the socket swallowing the bottom 0.35 m, about 4.4 m tall); the item is the same, smaller.
    internal static class StaveModel
    {
        internal const float Sunk=0.35f,ItemScale=0.55f;
        // (as the model's materials are numbered)
        private const int Wood=0,Dark=1,Iron=2,Gold=3,Bone=4,Hide=5,Rope=6,Glow=7,Crystal=8,Shade=9,Paint=10;
        // Each stave's wood (multiplying the grain), its fire (glow, and how bright), the colour of its crystal or panes, its paint.
        private static readonly Dictionary<StaveKind,(Color wood,Color glow,float bright,Color crystal,Color paint)> Own=new Dictionary<StaveKind,(Color,Color,float,Color,Color)>
        {
            [StaveKind.Ember]=(new Color(0.6f,0.5f,0.45f),new Color(1f,0.24f,0.03f),0.9f,new Color(1f,0.5f,0.2f),new Color(0.4f,0.12f,0.06f)),
            [StaveKind.Frost]=(new Color(1.15f,1.4f,1.95f),new Color(0.3f,0.6f,1f),0.8f,new Color(0.55f,0.75f,0.95f),new Color(0.3f,0.4f,0.5f)),
            [StaveKind.Thunder]=(new Color(0.6f,0.72f,1.1f),new Color(0.42f,0.3f,1f),0.9f,new Color(0.7f,0.6f,1f),new Color(0.3f,0.2f,0.4f)),
            [StaveKind.Blast]=(new Color(0.62f,0.55f,0.52f),new Color(1f,0.38f,0.06f),0.9f,new Color(1f,0.6f,0.2f),new Color(0.46f,0.25f,0.15f)),
            [StaveKind.Hearth]=(new Color(0.9f,0.85f,0.8f),new Color(1f,0.6f,0.22f),0.8f,new Color(0.95f,0.68f,0.36f),new Color(0.5f,0.3f,0.15f)),
        };
        private static readonly Dictionary<StaveKind,Mesh> Meshes=new Dictionary<StaveKind,Mesh>();
        private static readonly Dictionary<StaveKind,Material[]> Sets=new Dictionary<StaveKind,Material[]>();
        private static readonly Dictionary<StaveKind,float> Heads=new Dictionary<StaveKind,float>();

        internal static Mesh Mesh(StaveKind kind)
        {
            if(Meshes.TryGetValue(kind,out Mesh made)&&made!=null)return made;
            try
            {
                using(var raw=typeof(StaveModel).Assembly.GetManifestResourceStream("Shieldwall.stave_"+kind.ToString().ToLowerInvariant()+".json.gz"))
                {
                    if(raw==null)return null;
                    using(var gz=new System.IO.Compression.GZipStream(raw,System.IO.Compression.CompressionMode.Decompress))
                    using(var text=new System.IO.StreamReader(gz))
                    {
                        JObject d=JObject.Parse(text.ReadToEnd());
                        made=Look.Build(d,"BobStave"+kind);
                        if(made!=null&&d["head"]!=null)Heads[kind]=(float)d["head"]; // (a baked model says where its fire is)
                    }
                }
            }
            catch(System.Exception e){Plugin.Log("Shieldwall: the "+kind+" stave's model: "+e.Message);made=null;}
            if(made!=null&&!Heads.ContainsKey(kind))
            {
                // Where its fire is: the middle of what glows in its head (the top third), for the stave's light.
                Vector3[] v=made.vertices;float top=made.bounds.max.y,sum=0;int n=0;
                foreach(int sub in new[]{Glow,Crystal})if(sub<made.subMeshCount)foreach(int i in made.GetTriangles(sub))if(v[i].y>top*0.66f){sum+=v[i].y;n++;}
                Heads[kind]=n>0?sum/n:top-0.5f;
            }
            Meshes[kind]=made;
            return made;
        }
        // How high its fire burns over its foot (in the model's own metres).
        internal static float Head(StaveKind kind)=>Mesh(kind)!=null&&Heads.TryGetValue(kind,out float h)?h:3.9f;
        internal static float Height(StaveKind kind)=>Mesh(kind)?.bounds.max.y??4.4f;

        private static Material[] Materials(StaveKind kind)
        {
            if(Sets.TryGetValue(kind,out Material[] set)&&set.All(m=>m!=null))return set;
            Material hewn=Assets.Find("wood_pole")?.GetComponentsInChildren<MeshRenderer>(true).Select(r=>r.sharedMaterial).FirstOrDefault(m=>m!=null&&m.shader!=null&&m.shader.name=="Custom/Piece");
            if(hewn==null)return null;
            Material baked=Baked(hewn,kind);
            if(baked!=null)return Sets[kind]=new[]{baked};
            var own=Own[kind];
            Material grain=new Material(hewn){name="BobStaveWood",hideFlags=HideFlags.HideAndDontSave};grain.mainTextureScale=Vector2.one*0.6f;grain.color=hewn.color*own.wood;Assets.Owned.Add(grain);
            Material Plain(Color c,float gloss=0,float metal=0)
            {
                var m=new Material(hewn){name="BobStavePlain",hideFlags=HideFlags.HideAndDontSave};
                if(m.HasProperty("_MainTex"))m.SetTexture("_MainTex",null);
                if(m.HasProperty("_BumpMap"))m.SetTexture("_BumpMap",null);
                m.color=c;
                if(m.HasProperty("_Glossiness"))m.SetFloat("_Glossiness",gloss);
                if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",gloss);
                if(m.HasProperty("_Metallic"))m.SetFloat("_Metallic",metal);
                Assets.Owned.Add(m);
                return m;
            }
            Material Lit(Color c,Color glow)
            {
                Material m=Plain(c,0.3f);
                if(m.HasProperty("_EmissionColor")){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",glow);}
                return m;
            }
            set=new[]
            {
                grain,Plain(new Color(0.07f,0.06f,0.055f)),Plain(new Color(0.2f,0.195f,0.19f),0.35f,0.3f),Plain(new Color(0.78f,0.5f,0.26f),0.4f,0.2f), // (copper: barely metallic, or it shows the sky black)
                Plain(new Color(0.7f,0.66f,0.56f)),Plain(new Color(0.36f,0.25f,0.16f)),Plain(new Color(0.48f,0.4f,0.27f)),
                Lit(own.glow,own.glow*own.bright),Lit(own.crystal,own.crystal*0.3f),Plain(new Color(0.12f,0.1f,0.09f)),Plain(own.paint,0.1f),
            };
            return Sets[kind]=set;
        }

        // A baked stave (tools/bake, docs/model-baking.md): one low mesh in the game's style, its colour, normal and glow maps embedded beside it,
        // drawn as the game draws its own pieces (the piece shader with its own small textures). Null if this stave has no maps.
        private static Material Baked(Material hewn,StaveKind kind)=>
            SharedBaked.Baked.Material(typeof(StaveModel).Assembly,"Shieldwall.stave_"+kind.ToString().ToLowerInvariant(),hewn,"BobStave"+kind+"Baked",Assets.Owned);

        // The stave's look under a parent: its foot at the parent's origin, standing up its y.
        internal static GameObject Dress(Transform parent,StaveKind kind,float scale)
        {
            Mesh mesh=Mesh(kind);Material[] set=mesh!=null?Materials(kind):null;
            if(set==null)return null;
            var go=new GameObject("BobStave"+kind);
            go.transform.SetParent(parent,false);go.transform.localScale=Vector3.one*scale;
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials=set.Take(mesh.subMeshCount).ToArray();
            return go;
        }

        // A stave item's look made its own: the game staff it was copied from (its meshes, effects and colliders) taken off, the stave put on
        // in their place, a capsule round it to lie on and be picked up by.
        internal static bool DressItem(GameObject item,StaveKind kind)
        {
            if(Mesh(kind)==null||Materials(kind)==null)return false;
            foreach(Transform child in item.transform.Cast<Transform>().ToList())Object.DestroyImmediate(child.gameObject);
            foreach(Collider c in item.GetComponents<Collider>())Object.DestroyImmediate(c);
            foreach(Renderer r in item.GetComponents<Renderer>())if(!(r is ParticleSystemRenderer))Object.DestroyImmediate(r); // (keep a dropped item's sparkle)
            foreach(MeshFilter f in item.GetComponents<MeshFilter>())Object.DestroyImmediate(f);
            foreach(LODGroup g in item.GetComponents<LODGroup>())Object.DestroyImmediate(g);
            var attach=new GameObject("attach");attach.transform.SetParent(item.transform,false);
            attach.layer=item.layer;
            GameObject look=Dress(attach.transform,kind,ItemScale);
            look.layer=item.layer;
            float height=Height(kind)*ItemScale;
            var capsule=item.AddComponent<CapsuleCollider>();
            capsule.direction=1;capsule.height=height;capsule.radius=0.16f;capsule.center=new Vector3(0,height/2,0);
            return true;
        }

        // Its inventory icon: the head and the top of the shaft, big, on the diagonal (the rest of the shaft runs out of the corner), turned a
        // little to show the head's depth; lit from the front and the side over a soft ambient, without the world's fog.
        internal const float IconFrom=0.5f; // (how far up the staff the picture starts)
        internal static Sprite Icon(GameObject item,StaveKind kind,string name)
        {
            Mesh mesh=Mesh(kind);
            if(mesh==null)return null;
            var stage=new GameObject("ShieldwallStaveIconStage");stage.transform.position=new Vector3(0,-3000,0);
            bool fog=RenderSettings.fog;var ambientMode=RenderSettings.ambientMode;Color ambient=RenderSettings.ambientLight;
            RenderTexture target=null,previous=RenderTexture.active;
            try
            {
                int layer=31;for(int l=31;l>8;l--)if(string.IsNullOrEmpty(LayerMask.LayerToName(l))){layer=l;break;}
                GameObject model=Dress(stage.transform,kind,1);
                if(model==null)return null;
                model.layer=layer;
                model.transform.localRotation=Quaternion.Euler(0,0,-42)*Quaternion.Euler(0,35,0);
                // Frame what's above IconFrom of its height, as the camera sees it (it looks along +z).
                Vector3[] v=mesh.vertices;float top=mesh.bounds.max.y;
                float x0=float.MaxValue,x1=float.MinValue,y0=float.MaxValue,y1=float.MinValue;
                for(int i=0;i<v.Length;i++)
                {
                    if(v[i].y<top*IconFrom)continue;
                    Vector3 w=model.transform.TransformPoint(v[i]);
                    x0=Mathf.Min(x0,w.x);x1=Mathf.Max(x1,w.x);y0=Mathf.Min(y0,w.y);y1=Mathf.Max(y1,w.y);
                }
                var centre=new Vector3((x0+x1)/2,(y0+y1)/2,stage.transform.position.z);
                var cameraObject=new GameObject("Camera");cameraObject.transform.SetParent(stage.transform,false);
                cameraObject.transform.position=centre+Vector3.back*10;cameraObject.transform.rotation=Quaternion.identity;
                Camera camera=cameraObject.AddComponent<Camera>();
                camera.enabled=false;camera.cullingMask=1<<layer;camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(0,0,0,0);
                camera.orthographic=true;camera.orthographicSize=Mathf.Max(x1-x0,y1-y0)/2*1.07f;camera.nearClipPlane=0.1f;camera.farClipPlane=30;camera.allowHDR=false;camera.allowMSAA=false;
                foreach(var (euler,intensity) in new[]{(new Vector3(35,-35,0),1.25f),(new Vector3(15,140,0),0.55f)})
                {
                    var lightObject=new GameObject("Light");lightObject.transform.SetParent(stage.transform,false);lightObject.transform.rotation=Quaternion.Euler(euler);
                    Light light=lightObject.AddComponent<Light>();light.type=LightType.Directional;light.intensity=intensity;light.shadows=LightShadows.None;light.cullingMask=1<<layer;
                }
                RenderSettings.fog=false;RenderSettings.ambientMode=UnityEngine.Rendering.AmbientMode.Flat;RenderSettings.ambientLight=new Color(0.5f,0.5f,0.54f);
                // Drawn on black and on white: the pieces' shader writes no alpha, so how much the background shows through is read from the two.
                const int size=256;
                target=RenderTexture.GetTemporary(size,size,24,RenderTextureFormat.ARGB32);
                Color[] Shot(Color background)
                {
                    camera.backgroundColor=background;camera.targetTexture=target;camera.Render();camera.targetTexture=null;
                    RenderTexture.active=target;
                    var read=new Texture2D(size,size,TextureFormat.RGBA32,false);
                    read.ReadPixels(new Rect(0,0,size,size),0,0);read.Apply();
                    Color[] pixels=read.GetPixels();Object.DestroyImmediate(read);
                    return pixels;
                }
                Color[] black=Shot(new Color(0,0,0,0)),white=Shot(new Color(1,1,1,0)),result=new Color[black.Length];
                for(int i=0;i<black.Length;i++)
                {
                    Color b=black[i],w=white[i];
                    float alpha=Mathf.Clamp01(1-((w.r-b.r)+(w.g-b.g)+(w.b-b.b))/3);
                    result[i]=alpha<0.004f?new Color(0,0,0,0):new Color(Mathf.Clamp01(b.r/alpha),Mathf.Clamp01(b.g/alpha),Mathf.Clamp01(b.b/alpha),alpha);
                }
                var texture=new Texture2D(size,size,TextureFormat.RGBA32,false);
                texture.SetPixels(result);texture.Apply();
                texture.hideFlags=HideFlags.HideAndDontSave;Assets.Owned.Add(texture);
                Sprite sprite=Sprite.Create(texture,new Rect(0,0,size,size),new Vector2(0.5f,0.5f),100f);
                sprite.name=name;sprite.hideFlags=HideFlags.HideAndDontSave;Assets.Owned.Add(sprite);
                Icons[kind]=texture;
                return sprite;
            }
            catch(System.Exception e){Debug.LogWarning("[Shieldwall] could not draw the "+kind+" stave's icon: "+e.Message);return null;}
            finally
            {
                RenderSettings.fog=fog;RenderSettings.ambientMode=ambientMode;RenderSettings.ambientLight=ambient;
                RenderTexture.active=previous;if(target!=null)RenderTexture.ReleaseTemporary(target);
                Object.DestroyImmediate(stage);
            }
        }
        internal static readonly Dictionary<StaveKind,Texture2D> Icons=new Dictionary<StaveKind,Texture2D>(); // (for siege icons: a look at them)
    }
}
