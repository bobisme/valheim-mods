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
                        made=Look.Build(JObject.Parse(text.ReadToEnd()),"BobStave"+kind);
                }
            }
            catch(System.Exception e){Plugin.Log("Shieldwall: the "+kind+" stave's model: "+e.Message);made=null;}
            if(made!=null)
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
            foreach(Renderer r in item.GetComponents<Renderer>())Object.DestroyImmediate(r);
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
    }
}
