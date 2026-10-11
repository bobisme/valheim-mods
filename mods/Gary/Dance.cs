using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Gary
{
    // A keyframed clip made in Blender (anim/: rig.py builds Gary from `gary rig`, dance.py keys it, export.py writes it): each bone's
    // local rotation per frame, and the root's local position. Played over the animator's pose, so no asset bundle or controller is needed.
    internal sealed class Clip
    {
        internal readonly string Name;internal readonly float Fps;internal readonly string[] Bones;
        private readonly Quaternion[][] _rot;private readonly Vector3[] _root;
        internal float Length=>(_rot.Length-1)/Fps;
        private Clip(string name,float fps,string[] bones,Quaternion[][] rot,Vector3[] root){Name=name;Fps=fps;Bones=bones;_rot=rot;_root=root;}

        private static readonly Dictionary<string,Clip> Loaded=new Dictionary<string,Clip>();
        internal static Clip Get(string name)
        {
            if(Loaded.TryGetValue(name,out Clip clip))return clip;
            try
            {
                using(Stream stream=typeof(Clip).Assembly.GetManifestResourceStream("Gary."+name+".json"))
                using(var reader=new StreamReader(stream))
                {
                    JObject d=JObject.Parse(reader.ReadToEnd());
                    var bones=d["bones"].ToObject<string[]>();
                    var rot=new List<Quaternion[]>();
                    foreach(JArray frame in d["rot"])
                    {
                        var row=new Quaternion[bones.Length];
                        for(int i=0;i<row.Length;i++){var q=(JArray)frame[i];row[i]=new Quaternion((float)q[0],(float)q[1],(float)q[2],(float)q[3]);}
                        rot.Add(row);
                    }
                    var root=new List<Vector3>();foreach(JArray p in d["root"])root.Add(new Vector3((float)p[0],(float)p[1],(float)p[2]));
                    clip=new Clip(name,(float)d["fps"],bones,rot.ToArray(),root.ToArray());
                }
            }
            catch(Exception e){Plugin.Instance?.Error(e);clip=null;}
            Loaded[name]=clip;return clip;
        }
        internal Quaternion Rotation(int bone,float t)
        {
            float f=Mathf.Clamp(t*Fps,0,_rot.Length-1);int a=(int)f,b=Math.Min(a+1,_rot.Length-1);
            return Quaternion.Slerp(_rot[a][bone],_rot[b][bone],f-a);
        }
        internal Vector3 Root(float t)
        {
            float f=Mathf.Clamp(t*Fps,0,_root.Length-1);int a=(int)f,b=Math.Min(a+1,_root.Length-1);
            return Vector3.Lerp(_root[a],_root[b],f-a);
        }
    }
}
