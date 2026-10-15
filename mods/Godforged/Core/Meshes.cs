using System;
using System.Collections.Generic;
using UnityEngine;
using Object=UnityEngine.Object;

namespace Godforged
{
    // Models made in Blender (Models/<name>.py → Models/<name>.json.gz, built into the DLL): each file holds named parts (a body, a
    // rotor, a trigger...), each part a mesh with one triangle list per material slot. Slots are named from Look's palette ("white",
    // "gunmetal", "glow_cyan", "c_ff8800_m40_g60"...), so a model needs no material code. Coordinates are the game's: +z forward,
    // +y up, metres; Models/wulib.py converts from Blender.
    internal static class Meshes
    {
        internal sealed class Part{internal Mesh Mesh;internal string[] Slots;internal Vector3 Pivot;}
        private static readonly Dictionary<string,Dictionary<string,Part>> Files=new Dictionary<string,Dictionary<string,Part>>();

        internal static Dictionary<string,Part> Load(string file)
        {
            if(Files.TryGetValue(file,out var parts))return parts;
            parts=new Dictionary<string,Part>();
            Files[file]=parts;   // (a missing or broken file is tried once)
            try
            {
                using(var raw=typeof(Plugin).Assembly.GetManifestResourceStream("Godforged."+file+".json.gz"))
                {
                    if(raw==null){Plugin.Warn("No model "+file+" in the mod");return parts;}
                    using(var gz=new System.IO.Compression.GZipStream(raw,System.IO.Compression.CompressionMode.Decompress))
                    using(var text=new System.IO.StreamReader(gz))
                    {
                        var d=Newtonsoft.Json.Linq.JObject.Parse(text.ReadToEnd());
                        foreach(var kv in (Newtonsoft.Json.Linq.JObject)d["parts"])
                        {
                            var p=(Newtonsoft.Json.Linq.JObject)kv.Value;
                            float[] v=p["v"].ToObject<float[]>(),n=p["n"].ToObject<float[]>(),uv=p["uv"].ToObject<float[]>();
                            int[][] t=p["t"].ToObject<int[][]>();
                            var verts=new Vector3[v.Length/3];var norms=new Vector3[verts.Length];var uvs=new Vector2[verts.Length];var cols=new Color[verts.Length];
                            for(int i=0;i<verts.Length;i++){verts[i]=new Vector3(v[3*i],v[3*i+1],v[3*i+2]);norms[i]=new Vector3(n[3*i],n[3*i+1],n[3*i+2]);uvs[i]=new Vector2(uv[2*i],uv[2*i+1]);cols[i]=Color.white;}
                            var mesh=new Mesh{name="Godforged "+file+"/"+kv.Key};
                            if(verts.Length>65000)mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;
                            mesh.vertices=verts;mesh.normals=norms;mesh.uv=uvs;mesh.colors=cols;   // (the game's shaders read the vertex colour)
                            mesh.subMeshCount=t.Length;
                            for(int k=0;k<t.Length;k++)mesh.SetTriangles(t[k],k);
                            mesh.RecalculateBounds();mesh.RecalculateTangents();
                            float[] pv=p["pivot"]?.ToObject<float[]>();
                            parts[kv.Key]=new Part{Mesh=Look.Own(mesh),Slots=p["slots"].ToObject<string[]>(),Pivot=pv!=null?new Vector3(pv[0],pv[1],pv[2]):Vector3.zero};
                        }
                    }
                }
            }
            catch(Exception e){Plugin.Warn("Model "+file+": "+e.Message);}
            return parts;
        }
        internal static void Clear()=>Files.Clear();   // (the meshes themselves are Look's: kept while in a world, as the materials are)

        // The part as a child of parent, at its pivot (Blender's origin of the part), painted by its slot names. Null if there is no such part.
        internal static GameObject Add(Transform parent,string file,string part,string name=null)
        {
            if(!Load(file).TryGetValue(part,out Part p))return null;
            var go=new GameObject(name??part){layer=parent.gameObject.layer};
            go.transform.SetParent(parent,false);
            go.transform.localPosition=p.Pivot;
            go.AddComponent<MeshFilter>().sharedMesh=p.Mesh;
            MeshRenderer r=go.AddComponent<MeshRenderer>();
            var mats=new Material[p.Slots.Length];bool glowOnly=true;
            for(int i=0;i<mats.Length;i++){mats[i]=Look.Slot(p.Slots[i]);glowOnly&=p.Slots[i].StartsWith("glow_");}
            r.sharedMaterials=mats;
            if(glowOnly)r.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }
        // Every part of the file under parent (the ones listed in skip left out, for a feature that builds those itself).
        internal static Dictionary<string,GameObject> AddAll(Transform parent,string file,params string[] skip)
        {
            var made=new Dictionary<string,GameObject>();
            foreach(string part in Load(file).Keys)
                if(Array.IndexOf(skip,part)<0)made[part]=Add(parent,file,part);
            return made;
        }
        internal static bool Has(string file)=>Load(file).Count>0;
    }
}
