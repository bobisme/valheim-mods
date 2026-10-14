using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace BobsAnimations
{
    // A clip: for each bone it drives, its local rotation every frame. A bone "set" takes the clip's rotation; a bone "added" (the spine,
    // the neck) turns from wherever the game's own animation has it, so breathing and the seat's posture still show through. Props are
    // items held in a hand for a stretch of the clip (a tankard while drinking).
    public sealed class Clip
    {
        public readonly string Name;
        public readonly float Fps,BlendIn,BlendOut;
        public readonly bool Loop,Seated,Still,Moving; // (a seated clip ends by itself when its character gets up; a still one when they walk off; a moving one
                                                       // when they've stood still a moment)
        public readonly string[] Bones;
        internal readonly bool[] Add;
        internal readonly Prop[] Props;
        internal readonly (int bone,Vector3[] shift)[] Moves;
        private readonly Quaternion[][] _rot;
        public float Length=>(_rot.Length-1)/Fps;

        internal sealed class Prop
        {
            public string Item,Bone;public float From,To;public Vector3 Position;public Quaternion Rotation;public float Scale=1;
            public bool Swap; // (the item given when the clip is played takes its place: what a hauler carries)
        }

        private Clip(JObject d)
        {
            Name=(string)d["name"];Fps=(float?)d["fps"]??30;Loop=(bool?)d["loop"]??false;Seated=(bool?)d["seated"]??false;Still=(bool?)d["still"]??false;Moving=(bool?)d["moving"]??false;
            BlendIn=(float?)d["blendIn"]??0.4f;BlendOut=(float?)d["blendOut"]??0.5f;
            Bones=d["bones"].ToObject<string[]>();
            Add=d["add"]?.ToObject<bool[]>()??new bool[Bones.Length];
            _rot=((JArray)d["rot"]).Select(f=>((JArray)f).Select(q=>new Quaternion((float)q[0],(float)q[1],(float)q[2],(float)q[3])).ToArray()).ToArray();
            Props=(d["props"] as JArray)?.Select(p=>new Prop{Item=(string)p["item"],Bone=(string)p["bone"],From=(float?)p["from"]??0,To=(float?)p["to"]??float.MaxValue,
                Position=V(p["p"]),Rotation=Quaternion.Euler(V(p["r"])),Scale=(float?)p["scale"]??1,Swap=(bool?)p["swap"]??false}).ToArray()??new Prop[0];
            // Bones whose place moves too (the hips, crouching): a shift from the game's own place each frame, in the parent's units.
            Moves=(d["pos"] as JObject)?.Properties().Select(q=>(Array.IndexOf(Bones,q.Name),((JArray)q.Value).Select(v=>V(v)).ToArray())).Where(m=>m.Item1>=0).ToArray()??new (int,Vector3[])[0];
            if(_rot.Length==0||_rot.Any(f=>f.Length!=Bones.Length)||Add.Length!=Bones.Length)throw new InvalidDataException("clip "+Name+": every frame needs a rotation for each bone");
        }
        private static Vector3 V(JToken t)=>t is JArray a&&a.Count==3?new Vector3((float)a[0],(float)a[1],(float)a[2]):Vector3.zero;

        internal Vector3 Shift(Vector3[] shift,float t)
        {
            float f=Mathf.Clamp(t*Fps,0,shift.Length-1);int a=(int)f,b=Math.Min(a+1,shift.Length-1);
            return Vector3.Lerp(shift[a],shift[b],f-a);
        }
        internal Quaternion Rotation(int bone,float t)
        {
            float f=Mathf.Clamp(t*Fps,0,_rot.Length-1);int a=(int)f,b=Math.Min(a+1,_rot.Length-1);
            return Quaternion.Slerp(_rot[a][bone],_rot[b][bone],f-a);
        }

        // ---- the clips there are: this mod's own (Clips/*.json.gz), and any another mod registers ----
        private static Dictionary<string,Clip> _all;
        private static Dictionary<string,Clip> All
        {
            get
            {
                if(_all!=null)return _all;
                _all=new Dictionary<string,Clip>(StringComparer.OrdinalIgnoreCase);
                var asm=typeof(Clip).Assembly;
                foreach(string res in asm.GetManifestResourceNames().Where(n=>n.StartsWith("BobsAnimations.clips.",StringComparison.Ordinal)))
                {
                    try
                    {
                        using(Stream raw=asm.GetManifestResourceStream(res))
                        using(Stream data=res.EndsWith(".gz",StringComparison.Ordinal)?(Stream)new GZipStream(raw,CompressionMode.Decompress):raw)
                        using(var text=new StreamReader(data)){var clip=new Clip(JObject.Parse(text.ReadToEnd()));_all[clip.Name]=clip;}
                    }
                    catch(Exception e){Plugin.Warn("Bob's Animations: clip "+res+": "+e.Message);}
                }
                return _all;
            }
        }
        public static Clip Get(string name)=>name!=null&&All.TryGetValue(name,out Clip c)?c:null;
        public static string[] Names()=>All.Keys.OrderBy(k=>k).ToArray();
        // Another mod's clip, in the same JSON shape (see Authoring/README.txt). Every game that shows it must register it too.
        public static bool Register(string json)
        {
            try{var clip=new Clip(JObject.Parse(json));All[clip.Name]=clip;return true;}
            catch(Exception e){Plugin.Warn("Bob's Animations: a registered clip: "+e.Message);return false;}
        }
    }
}
