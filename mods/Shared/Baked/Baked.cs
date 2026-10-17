using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace SharedBaked
{
    // (Shared by every mod with baked models: mods/Shared/Baked, compiled into each mod. docs/model-baking.md)
    // A baked model is a low-poly mesh (its json.gz from tools/bake/bake.py: v, n, tan, uv, t) and its own small maps embedded beside it
    // (<name>_albedo.png, _normal.png, _glow.png), drawn as the game draws its own pieces: a game Custom/Piece material with those maps.
    internal static class Baked
    {
        // (ImageConversion.LoadImage lives in a module the build can't reference directly (its netstandard is newer), so it's called by name.)
        private static readonly System.Reflection.MethodInfo LoadImage=System.Type.GetType("UnityEngine.ImageConversion, UnityEngine.ImageConversionModule")
            ?.GetMethod("LoadImage",new[]{typeof(Texture2D),typeof(byte[]),typeof(bool)});

        // Is this json a baked model's (it brings its own tangents)?
        internal static bool Is(JObject d)=>d?["tan"] is JArray;

        // Set a baked mesh's tangents (the ones its normal map was baked against). False if the json has none.
        internal static bool Tangents(Mesh mesh,JObject d)
        {
            if(!(d?["tan"] is JArray tan))return false;
            float[] a=tan.ToObject<float[]>();
            var t=new Vector4[a.Length/4];
            for(int i=0;i<t.Length;i++)t[i]=new Vector4(a[4*i],a[4*i+1],a[4*i+2],a[4*i+3]);
            mesh.tangents=t;
            return true;
        }

        // A texture embedded in the assembly, or null. linear: data, not colour (a normal map). Added to owned, if given, for cleaning up.
        internal static Texture2D Texture(System.Reflection.Assembly asm,string resource,bool linear,List<Object> owned=null)
        {
            using(var raw=asm.GetManifestResourceStream(resource))
            {
                if(raw==null)return null;
                var bytes=new byte[raw.Length];int got=0;while(got<bytes.Length)got+=raw.Read(bytes,got,bytes.Length-got);
                var tex=new Texture2D(2,2,TextureFormat.RGBA32,true,linear){name=resource,hideFlags=HideFlags.HideAndDontSave};
                if(LoadImage==null||!(bool)LoadImage.Invoke(null,new object[]{tex,bytes,true})){Object.DestroyImmediate(tex);return null;}
                tex.wrapMode=TextureWrapMode.Clamp;tex.filterMode=FilterMode.Bilinear;tex.anisoLevel=4;
                owned?.Add(tex);
                return tex;
            }
        }

        // The material for a baked model whose maps are embedded as <resource>_albedo.png etc.: a copy of a game Custom/Piece material (from)
        // wearing them. Null if there's no albedo map (not baked). glow: how bright its glow map burns (1: as baked).
        internal static Material Material(System.Reflection.Assembly asm,string resource,Material from,string name,List<Object> owned=null,float glow=1f)
        {
            if(from==null)return null;
            Texture2D albedo=Texture(asm,resource+"_albedo.png",false,owned);
            if(albedo==null)return null;
            var m=new Material(from){name=name,hideFlags=HideFlags.HideAndDontSave,color=Color.white};
            m.mainTexture=albedo;m.mainTextureScale=Vector2.one;m.mainTextureOffset=Vector2.zero;
            Texture2D normal=Texture(asm,resource+"_normal.png",true,owned);
            if(normal!=null&&m.HasProperty("_BumpMap")){m.SetTexture("_BumpMap",normal);m.SetTextureScale("_BumpMap",Vector2.one);m.SetTextureOffset("_BumpMap",Vector2.zero);m.EnableKeyword("_NORMALMAP");}
            Texture2D lit=Texture(asm,resource+"_glow.png",false,owned);
            if(m.HasProperty("_EmissionColor"))
            {
                if(lit!=null){m.SetTexture("_EmissionMap",lit);m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",Color.white*glow);}
                else{m.SetColor("_EmissionColor",Color.black);m.DisableKeyword("_EMISSION");}
            }
            foreach(string k in new[]{"_MetallicGlossMap","_MetallicTex","_EmissiveTex","_MossTex"})if(m.HasProperty(k))m.SetTexture(k,null);
            foreach(var (k,v) in new[]{("_Glossiness",0.12f),("_Smoothness",0.12f),("_Metallic",0f),("_MetallicAlphaGloss",0f)})if(m.HasProperty(k))m.SetFloat(k,v);
            owned?.Add(m);
            return m;
        }
    }
}
