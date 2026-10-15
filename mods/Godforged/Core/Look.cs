using System;
using System.Collections.Generic;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Godforged
{
    // What everything is made of: the game's own lit material (from the Arbalest; a new Material on Unity's default shader is pink in
    // Valheim) painted in flat colours, and the Arbalest bolt's additive trail material for things that glow (beams, blades, jets, lenses).
    // The Builder puts models together from plain shapes; it is Quad's Guns Model.Builder (HardHeadHackerHead/valheim-mods), trimmed.
    internal static class Look
    {
        internal static readonly Color Gunmetal=new Color(0.16f,0.17f,0.19f),Black=new Color(0.05f,0.05f,0.06f),White=new Color(0.86f,0.87f,0.88f),
            Chrome=new Color(0.72f,0.74f,0.78f),Hazard=new Color(0.95f,0.62f,0.08f),Red=new Color(1f,0.12f,0.08f),Blue=new Color(0.2f,0.55f,1f),
            Green=new Color(0.25f,1f,0.3f),Cyan=new Color(0.2f,0.95f,1f),Violet=new Color(0.7f,0.25f,1f),Lens=new Color(0.12f,0.28f,0.42f);

        internal static Material Base,GlowBase;
        private static readonly Dictionary<string,Material> Cache=new Dictionary<string,Material>();
        private static readonly List<Object> Made=new List<Object>();
        internal static bool Ready=>Base!=null;

        internal static void Init(ZNetScene scene)
        {
            if(Base!=null&&GlowBase!=null)return;
            GameObject arbalest=scene.GetPrefab("CrossbowArbalest");
            Renderer r=arbalest!=null?arbalest.GetComponentInChildren<MeshRenderer>(true):null;
            if(r!=null)Base=r.sharedMaterial;
            GameObject bolt=scene.GetPrefab("arbalest_projectile_iron");
            if(bolt!=null)foreach(ParticleSystemRenderer pr in bolt.GetComponentsInChildren<ParticleSystemRenderer>(true))
                if(pr.sharedMaterial!=null){GlowBase=pr.sharedMaterial;break;}
            if(Base==null)Plugin.Warn("No Arbalest material: models will look plain");
            if(GlowBase==null)Plugin.Warn("No Arbalest bolt trail material: glows will be plain");
        }
        // Unloading. Inside a world the materials stay: worn and held items, placed speeders and items on the ground still draw with
        // them until they're remade (destroying them turns those magenta). At the main menu nothing uses them any more.
        internal static void Clear()
        {
            if(ZNetScene.instance==null)foreach(Object o in Made)if(o!=null)Object.Destroy(o);
            Made.Clear();Cache.Clear();Base=null;GlowBase=null;
        }
        internal static T Own<T>(T o)where T:Object{Made.Add(o);return o;}

        // A painted surface. emit > 0 makes it glow by itself (in the dark too), lit as the rest.
        internal static Material Paint(Color color,float metal=0.4f,float gloss=0.45f,float emit=0f)
        {
            string key=$"p{color.r:F3}/{color.g:F3}/{color.b:F3}/{metal:F2}/{gloss:F2}/{emit:F2}";
            if(Cache.TryGetValue(key,out Material m)&&m!=null)return m;
            m=Own(Base!=null?new Material(Base):new Material(Shader.Find("Standard")));
            foreach(string tex in m.GetTexturePropertyNames())m.SetTexture(tex,null);   // (the Arbalest's normal and detail maps stamp its wood grain and plates on every face)
            if(m.HasProperty("_Color"))m.SetColor("_Color",color);
            if(m.HasProperty("_Metallic"))m.SetFloat("_Metallic",metal);
            if(m.HasProperty("_Glossiness"))m.SetFloat("_Glossiness",gloss);
            // The game's shader (Custom/Creature) takes metal and gloss from _MetallicGlossMap (r: metal, a: gloss), and with none Unity
            // gives it white: everything mirror-bright. So each painted material gets a one-pixel map of its own values.
            if(m.HasProperty("_MetallicGlossMap"))
            {
                var map=Own(new Texture2D(1,1,TextureFormat.RGBA32,false));
                map.SetPixel(0,0,new Color(metal,metal,metal,gloss));map.Apply();
                m.SetTexture("_MetallicGlossMap",map);
            }
            if(emit>0f&&m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",color*emit);
                if(m.HasProperty("_EmissionMap"))m.SetTexture("_EmissionMap",Texture2D.whiteTexture);   // (the game's shader multiplies emission by this map: none = no glow)
            }
            Cache[key]=m;
            return m;
        }
        // Light itself, added on top of what is behind (a beam, a blade's core, a jet's flame). alpha: how strong.
        internal static Material Glow(Color color,float alpha=1f)
        {
            string key=$"g{color.r:F3}/{color.g:F3}/{color.b:F3}/{alpha:F2}";
            if(Cache.TryGetValue(key,out Material m)&&m!=null)return m;
            if(GlowBase==null)return Paint(color,0,0,3f);
            m=Own(new Material(GlowBase));
            m.mainTexture=null;
            var c=new Color(color.r,color.g,color.b,alpha);
            if(m.HasProperty("_TintColor"))m.SetColor("_TintColor",c);
            if(m.HasProperty("_Color"))m.SetColor("_Color",c);
            if(m.HasProperty("_EmissionColor"))m.SetColor("_EmissionColor",c);
            Cache[key]=m;
            return m;
        }

        // A material by palette name, as Blender models name their slots (Models/wulib.py has the same table, for its previews):
        // "white" "offwhite" "gunmetal" "black" "rubber" "carbon" "chrome" "steel" "brass" "copper" "hazard" "red" "blue" "olive" "lens"
        // "glass", "glow_<cyan|red|blue|green|orange|violet|white|yellow>" (light, added on top), "emit_<same>" (a lit surface that glows:
        // indicator lights, screens), or "c_RRGGBB[_mNN][_gNN][_eNN]" for any colour (metal, gloss, emission in hundredths).
        internal static Material Slot(string name)
        {
            switch(name)
            {
                case "white":return Paint(new Color(0.86f,0.87f,0.88f),0.1f,0.55f);
                case "offwhite":return Paint(new Color(0.74f,0.73f,0.70f),0.1f,0.45f);
                case "gunmetal":return Paint(new Color(0.16f,0.17f,0.19f),0.6f,0.5f);
                case "black":return Paint(new Color(0.05f,0.05f,0.06f),0.2f,0.35f);
                case "rubber":return Paint(new Color(0.035f,0.035f,0.04f),0f,0.12f);
                case "carbon":return Paint(new Color(0.09f,0.09f,0.10f),0.3f,0.65f);
                case "chrome":return Paint(new Color(0.72f,0.74f,0.78f),0.9f,0.85f);
                case "steel":return Paint(new Color(0.45f,0.47f,0.5f),0.75f,0.55f);
                case "brass":return Paint(new Color(0.66f,0.50f,0.20f),0.8f,0.6f);
                case "copper":return Paint(new Color(0.72f,0.42f,0.22f),0.8f,0.6f);
                case "hazard":return Paint(new Color(0.95f,0.62f,0.08f),0.1f,0.5f);
                case "red":return Paint(new Color(0.62f,0.08f,0.06f),0.15f,0.5f);
                case "blue":return Paint(new Color(0.10f,0.22f,0.55f),0.2f,0.55f);
                case "olive":return Paint(new Color(0.22f,0.26f,0.17f),0.1f,0.35f);
                case "lens":return Paint(Lens,0.3f,0.95f);
                case "glass":return Paint(new Color(0.08f,0.12f,0.16f),0.5f,0.95f);
            }
            // On a model, light-giving parts are drawn unlit in their full colour (the game's lit shader dims them, additive glow over a
            // dark part reads grey): glow_ brighter and tinted towards white, emit_ (indicators, screens) as the plain colour.
            if(name.StartsWith("glow_"))return Unlit(Color.Lerp(Named(name.Substring(5)),Color.white,0.35f));
            if(name.StartsWith("emit_"))return Unlit(Named(name.Substring(5)));
            if(name.StartsWith("c_")&&name.Length>=8&&ColorUtility.TryParseHtmlString("#"+name.Substring(2,6),out Color hex))
            {
                float metal=0.3f,gloss=0.5f,emit=0f;
                if(name.EndsWith("_f")||name.EndsWith("_l"))   // (forged iron or leather: a grain of its own, not a flat colour)
                {
                    foreach(string bit in name.Substring(8).Split(new[]{'_'},System.StringSplitOptions.RemoveEmptyEntries))
                    {
                        if(bit.Length<2||!int.TryParse(bit.Substring(1),out int q))continue;
                        if(bit[0]=='m')metal=q/100f;else if(bit[0]=='g')gloss=q/100f;
                    }
                    return Grained(hex,metal,gloss,name.EndsWith("_f"),8f);
                }
                foreach(string bit in name.Substring(8).Split(new[]{'_'},System.StringSplitOptions.RemoveEmptyEntries))
                {
                    if(bit.Length<2||!int.TryParse(bit.Substring(1),out int k))continue;
                    if(bit[0]=='m')metal=k/100f;else if(bit[0]=='g')gloss=k/100f;else if(bit[0]=='e')emit=k/100f;
                }
                return Paint(hex,metal,gloss,emit);
            }
            Plugin.Warn("Unknown material slot "+name);
            return Paint(Color.magenta,0,0.5f);
        }
        // A surface with a grain made here: forged iron (mottled, pitted, scratched bright, its shine varying) or leather (a fine grain, a
        // little worn). tiling: how many times the texture repeats over the model's UVs (Blender models' UVs are in metres).
        internal static Material Grained(Color color,float metal,float gloss,bool iron,float tiling)
        {
            string key=$"gr{color.r:F3}/{color.g:F3}/{color.b:F3}/{metal:F2}/{gloss:F2}/{iron}/{tiling:F1}";
            if(Cache.TryGetValue(key,out Material m)&&m!=null)return m;
            m=Paint(color,metal,gloss);
            m=Own(new Material(m));
            const int N=256;
            var albedo=Own(new Texture2D(N,N,TextureFormat.RGBA32,true));var mg=Own(new Texture2D(N,N,TextureFormat.RGBA32,true));
            var rng=new System.Random(iron?7:11);
            float[] scratch=new float[N*N];
            if(iron)for(int s=0;s<90;s++)   // bright scratches: short straight strokes
            {
                float x=(float)rng.NextDouble()*N,y=(float)rng.NextDouble()*N,a=(float)(rng.NextDouble()*Math.PI),len=6+(float)rng.NextDouble()*30;
                for(float t=0;t<len;t+=0.5f){int px=((int)(x+Mathf.Cos(a)*t)%N+N)%N,py=((int)(y+Mathf.Sin(a)*t)%N+N)%N;scratch[py*N+px]=1f;}
            }
            for(int y=0;y<N;y++)for(int x=0;x<N;x++)
            {
                float u=x/(float)N,v=y/(float)N;
                float f=Fbm(u,v,iron?5f:24f,4),fine=Fbm(u,v,iron?40f:90f,2);
                float shade=iron?0.68f+0.5f*f+0.12f*fine:0.78f+0.3f*f+0.18f*fine;
                if(iron&&fine<0.25f)shade*=0.8f;   // (pits)
                float sc=scratch[y*N+x];
                Color c=color*shade;if(sc>0)c=Color.Lerp(c,color*1.9f,0.6f);
                c.a=1f;albedo.SetPixel(x,y,c);
                float mt=iron?Mathf.Clamp01(metal+(f-0.5f)*0.2f+sc*0.2f):metal;
                float gl=Mathf.Clamp01(gloss*(iron?0.55f+0.9f*f:0.6f+0.8f*fine)+sc*0.35f);
                mg.SetPixel(x,y,new Color(mt,mt,mt,gl));
            }
            albedo.Apply(true);mg.Apply(true);
            albedo.wrapMode=TextureWrapMode.Repeat;mg.wrapMode=TextureWrapMode.Repeat;
            if(m.HasProperty("_Color"))m.SetColor("_Color",Color.white);
            m.mainTexture=albedo;m.mainTextureScale=new Vector2(tiling,tiling);
            if(m.HasProperty("_MetallicGlossMap")){m.SetTexture("_MetallicGlossMap",mg);m.SetTextureScale("_MetallicGlossMap",new Vector2(tiling,tiling));}
            Cache[key]=m;
            return m;
        }
        private static float Hash(int x,int y){unchecked{int h=x*374761393+y*668265263;h=(h^(h>>13))*1274126177;return (h&0xffff)/65535f;}}
        // Smooth noise that wraps at the texture's edge (period: the scale), several octaves.
        private static float Fbm(float u,float v,float scale,int octaves)
        {
            float sum=0,amp=0.5f,norm=0;
            for(int o=0;o<octaves;o++)
            {
                int p=Mathf.Max(1,Mathf.RoundToInt(scale));
                float x=u*p,y=v*p;int x0=Mathf.FloorToInt(x),y0=Mathf.FloorToInt(y);float fx=x-x0,fy=y-y0;
                fx=fx*fx*(3-2*fx);fy=fy*fy*(3-2*fy);
                float a=Hash(x0%p,y0%p+o*97),b=Hash((x0+1)%p,y0%p+o*97),c=Hash(x0%p,(y0+1)%p+o*97),d=Hash((x0+1)%p,(y0+1)%p+o*97);
                sum+=amp*Mathf.Lerp(Mathf.Lerp(a,b,fx),Mathf.Lerp(c,d,fx),fy);norm+=amp;amp*=0.5f;scale*=2f;
            }
            return sum/norm;
        }

        internal static Color Named(string glow)
        {
            switch(glow)
            {
                case "cyan":return Cyan;case "red":return Red;case "blue":return Blue;case "green":return Green;case "violet":return Violet;
                case "orange":return new Color(1f,0.45f,0.08f);case "yellow":return new Color(1f,0.85f,0.2f);case "white":return Color.white;
            }
            return Color.white;
        }

        // A flat colour that ignores light (Unity's sprite shader is in every build). Opaque by colour, drawn just after the opaque pass.
        internal static Material Unlit(Color color)
        {
            string key=$"u{color.r:F3}/{color.g:F3}/{color.b:F3}";
            if(Cache.TryGetValue(key,out Material m)&&m!=null)return m;
            Shader sh=Shader.Find("Sprites/Default");
            if(sh==null)return Paint(color,0f,0.6f,2.5f);
            m=Own(new Material(sh));
            m.color=new Color(color.r,color.g,color.b,1f);m.renderQueue=2460;
            Cache[key]=m;
            return m;
        }

        // The box around every renderer under root, in space's own coordinates.
        internal static Bounds Bounds(Transform root,Transform space)
        {
            bool any=false;var b=new Bounds();
            foreach(Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                if(r is ParticleSystemRenderer)continue;
                MeshFilter f=r.GetComponent<MeshFilter>();
                if(f==null||f.sharedMesh==null)continue;
                Bounds mb=f.sharedMesh.bounds;
                for(int i=0;i<8;i++)
                {
                    Vector3 corner=mb.center+Vector3.Scale(mb.extents,new Vector3((i&1)==0?-1:1,(i&2)==0?-1:1,(i&4)==0?-1:1));
                    Vector3 p=space.InverseTransformPoint(r.transform.TransformPoint(corner));
                    if(!any){b=new Bounds(p,Vector3.zero);any=true;}else b.Encapsulate(p);
                }
            }
            return any?b:new Bounds(Vector3.zero,Vector3.one*0.2f);
        }

        // A shape with no collider, under parent: a primitive at a place, size and turn, painted.
        internal static GameObject Shape(PrimitiveType type,Transform parent,string name,Vector3 at,Vector3 size,Material material,Vector3 euler=default)
        {
            GameObject p=GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(p.GetComponent<Collider>());
            p.name=name;p.layer=parent.gameObject.layer;
            p.transform.SetParent(parent,false);
            p.transform.localPosition=at;p.transform.localRotation=Quaternion.Euler(euler);p.transform.localScale=size;
            Renderer r=p.GetComponent<Renderer>();
            r.sharedMaterial=material;
            if(material!=null&&material==GlowBase||IsGlow(material))r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            return p;
        }
        private static bool IsGlow(Material m)=>m!=null&&GlowBase!=null&&m.shader==GlowBase.shader;

        // Puts a model together: parts by position (in the model's space), size and colour. Cylinders lie along the axis given.
        internal sealed class Builder
        {
            internal readonly Transform Root;
            internal Builder(Transform root){Root=root;}
            internal Builder Group(string name,Vector3 at,Vector3 euler=default)
            {
                var go=new GameObject(name){layer=Root.gameObject.layer};
                go.transform.SetParent(Root,false);
                go.transform.localPosition=at;go.transform.localRotation=Quaternion.Euler(euler);
                return new Builder(go.transform);
            }
            internal Transform Point(string name,Vector3 at,Vector3 euler=default)=>Group(name,at,euler).Root;
            internal GameObject Box(string name,Vector3 at,Vector3 size,Material m,Vector3 euler=default)=>Shape(PrimitiveType.Cube,Root,name,at,size,m,euler);
            internal GameObject Ball(string name,Vector3 at,Vector3 size,Material m)=>Shape(PrimitiveType.Sphere,Root,name,at,size,m);
            internal GameObject Tube(string name,Vector3 at,float diameter,float length,Material m,char axis='z',Vector3 extraEuler=default)
            {
                Vector3 euler=axis=='z'?new Vector3(90,0,0):axis=='x'?new Vector3(0,0,90):Vector3.zero;
                return Shape(PrimitiveType.Cylinder,Root,name,at,new Vector3(diameter,length*0.5f,diameter),m,euler+extraEuler);
            }
            internal GameObject Capsule(string name,Vector3 at,float diameter,float length,Material m,char axis='z')
            {
                Vector3 euler=axis=='z'?new Vector3(90,0,0):axis=='x'?new Vector3(0,0,90):Vector3.zero;
                return Shape(PrimitiveType.Capsule,Root,name,at,new Vector3(diameter,length*0.5f,diameter),m,euler);
            }
        }

        // A light that comes with a part (a blade, a muzzle flash, a jet): no shadows, cheap.
        internal static Light AddLight(Transform parent,Color color,float range,float intensity,Vector3 at=default)
        {
            var go=new GameObject("light"){layer=parent.gameObject.layer};
            go.transform.SetParent(parent,false);go.transform.localPosition=at;
            Light l=go.AddComponent<Light>();
            l.type=LightType.Point;l.color=color;l.range=range;l.intensity=intensity;l.shadows=LightShadows.None;
            return l;
        }
    }
}
