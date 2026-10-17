using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Shieldwall
{
    // The Warstone upgrades' own looks (Models/upgrade/upgrades.py): a war banner on a tall guyed pole, a trophy totem crowned with an
    // aurochs' skull, a horned brazier, a rune obelisk in the Warstone's own rock. Foot at the origin, front toward forward.
    internal static class UpgradeModel
    {
        private static readonly Dictionary<string,string> Models=new Dictionary<string,string>
            {["BobWarBanner"]="banner",["BobWarTotem"]="totem",["BobWarBrazier"]="brazier",["BobWarObelisk"]="obelisk"};
        // Where a model's solid part is, if not all of it (the banner's guy ropes and pegs are walked through): centre, size.
        private static readonly Dictionary<string,(Vector3 centre,Vector3 size)> Solid=new Dictionary<string,(Vector3,Vector3)>
            {["banner"]=(new Vector3(0,3.1f,0),new Vector3(0.8f,6.2f,0.8f))};
        // How high the brazier's embers lie, for the fire.
        internal const float BrazierFire=1.08f;
        private static readonly Dictionary<string,Mesh> Meshes=new Dictionary<string,Mesh>();
        private static Material[] _set;
        private static readonly Dictionary<string,Material> BakedSet=new Dictionary<string,Material>();

        internal static bool Has(string prefab)=>Models.ContainsKey(prefab);
        internal static (Vector3 centre,Vector3 size)? SolidOf(string prefab)=>Models.TryGetValue(prefab,out string m)&&Solid.TryGetValue(m,out var s)?s:((Vector3,Vector3)?)null;

        private static Mesh Mesh(string model)
        {
            if(Meshes.TryGetValue(model,out Mesh made)&&made!=null)return made;
            try
            {
                using(var raw=typeof(UpgradeModel).Assembly.GetManifestResourceStream("Shieldwall.up_"+model+".json.gz"))
                {
                    if(raw==null)return null;
                    using(var gz=new System.IO.Compression.GZipStream(raw,System.IO.Compression.CompressionMode.Decompress))
                    using(var text=new System.IO.StreamReader(gz))
                        made=Look.Build(JObject.Parse(text.ReadToEnd()),"BobWarUpgrade_"+model);
                }
            }
            catch(System.Exception e){Plugin.Log("Shieldwall: the "+model+" model: "+e.Message);made=null;}
            return Meshes[model]=made;
        }

        // (as the model's materials are numbered: wood, dark, iron, gold, bone, hide, rope, glow, cloth, paint, shade, rock, rune)
        private static Material[] Materials()
        {
            if(_set!=null&&_set.All(m=>m!=null))return _set;
            Material hewn=Assets.Find("wood_pole")?.GetComponentsInChildren<MeshRenderer>(true).Select(r=>r.sharedMaterial).FirstOrDefault(m=>m!=null&&m.shader!=null&&m.shader.name=="Custom/Piece");
            if(hewn==null)return null;
            Material grain=new Material(hewn){name="BobWarUpgradeWood",hideFlags=HideFlags.HideAndDontSave};grain.mainTextureScale=Vector2.one*0.6f;grain.color=hewn.color*new Color(0.75f,0.68f,0.62f);Assets.Owned.Add(grain);
            Material Plain(Color c,float gloss=0,float metal=0)
            {
                var m=new Material(hewn){name="BobWarUpgradePlain",hideFlags=HideFlags.HideAndDontSave};
                if(m.HasProperty("_MainTex"))m.SetTexture("_MainTex",null);
                if(m.HasProperty("_BumpMap"))m.SetTexture("_BumpMap",null);
                m.color=c;
                if(m.HasProperty("_Glossiness"))m.SetFloat("_Glossiness",gloss);
                if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",gloss);
                if(m.HasProperty("_Metallic"))m.SetFloat("_Metallic",metal);
                Assets.Owned.Add(m);
                return m;
            }
            // The Warstone's rock, but in the pieces' shader: the rock's own lays a busy moss tile on every upward face.
            Material source=Assets.Find(Shieldwall.Look.RockFrom)?.GetComponentsInChildren<MeshRenderer>(true).Select(r=>r.sharedMaterial).FirstOrDefault(m=>m!=null&&!m.name.StartsWith("snow"));
            Material Rock(string n,Color c)
            {
                var m=new Material(hewn){name=n,hideFlags=HideFlags.HideAndDontSave,color=c};
                if(source!=null){m.mainTexture=source.mainTexture;m.mainTextureScale=Vector2.one*Shieldwall.Look.RockScale;
                    if(m.HasProperty("_BumpMap")&&source.HasProperty("_BumpMap")){m.SetTexture("_BumpMap",source.GetTexture("_BumpMap"));m.SetTextureScale("_BumpMap",Vector2.one*Shieldwall.Look.RockScale);}}
                foreach(string k in new[]{"_Glossiness","_Metallic","_Smoothness"})if(m.HasProperty(k))m.SetFloat(k,0.05f);
                Assets.Owned.Add(m);
                return m;
            }
            Material rock=Rock("BobWarUpgradeRock",new Color(0.52f,0.53f,0.57f)),rune=Rock("BobWarUpgradeRune",Shieldwall.Look.RuneDark);
            if(rune.HasProperty("_EmissionColor")){rune.EnableKeyword("_EMISSION");rune.SetColor("_EmissionColor",Shieldwall.Look.RuneIdle*1.8f);}
            Material embers=Plain(new Color(1f,0.4f,0.1f),0.2f);
            if(embers.HasProperty("_EmissionColor")){embers.EnableKeyword("_EMISSION");embers.SetColor("_EmissionColor",new Color(1f,0.3f,0.05f)*0.9f);}
            return _set=new[]
            {
                grain,Plain(new Color(0.07f,0.06f,0.055f)),Plain(new Color(0.2f,0.195f,0.19f),0.35f,0.3f),Plain(new Color(0.72f,0.5f,0.25f),0.4f,0.2f), // (bronze: barely metallic)
                Plain(new Color(0.72f,0.68f,0.58f)),Plain(new Color(0.36f,0.25f,0.16f)),Plain(new Color(0.48f,0.4f,0.27f)),embers,
                Plain(new Color(0.42f,0.06f,0.04f)),Plain(new Color(0.06f,0.055f,0.05f)),Plain(new Color(0.24f,0.21f,0.17f)),rock,rune,
            };
        }

        // A baked model (tools/bake, docs/model-baking.md): one material wearing its own maps, on a copy of the hewn wood's piece material.
        private static Material[] Baked(string model)
        {
            if(BakedSet.TryGetValue(model,out Material made)&&made!=null)return new[]{made};
            Material hewn=Assets.Find("wood_pole")?.GetComponentsInChildren<MeshRenderer>(true).Select(r=>r.sharedMaterial).FirstOrDefault(m=>m!=null&&m.shader!=null&&m.shader.name=="Custom/Piece");
            made=SharedBaked.Baked.Material(typeof(UpgradeModel).Assembly,"Shieldwall.up_"+model,hewn,"BobWarUpgrade_"+model+"Baked",Assets.Owned);
            BakedSet[model]=made;
            return made!=null?new[]{made}:null;
        }

        // A look to build the piece from (Pieces.Build copies its meshes): null if the model can't be had. Destroy it after.
        internal static GameObject Dress(string prefab)
        {
            if(!Models.TryGetValue(prefab,out string model))return null;
            Mesh mesh=Mesh(model);Material[] set=mesh!=null?Baked(model)??Materials():null;
            if(set==null)return null;
            var go=new GameObject("BobWarUpgradeLook_"+model);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials=set.Take(mesh.subMeshCount).ToArray();
            return go;
        }
    }
}
