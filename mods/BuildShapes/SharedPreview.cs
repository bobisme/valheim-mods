using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace BuildShapes
{
    internal readonly struct SharedPoint
    {
        internal readonly float X,Y,Z;
        internal SharedPoint(float x,float y,float z){X=x;Y=y;Z=z;}
        internal bool Valid=>SharedPreviewCodec.Finite(X)&&SharedPreviewCodec.Finite(Y)&&SharedPreviewCodec.Finite(Z)&&Math.Abs(X)<1000000&&Math.Abs(Y)<1000000&&Math.Abs(Z)<1000000;
    }
    internal sealed class SharedStroke
    {
        internal float Width;
        internal bool ThroughGround;
        internal SharedPoint[] Points;
    }
    internal sealed class SharedGhost
    {
        internal string Prefab;
        internal SharedPoint At;
        internal float X,Y,Z,W;
    }
    internal sealed class SharedPreview
    {
        internal long World,Revision;
        internal byte Tool;
        internal SharedPoint Center;
        internal readonly List<SharedStroke> Lines=new List<SharedStroke>();
        internal readonly List<SharedGhost> Pieces=new List<SharedGhost>();
    }
    internal static class SharedPreviewCodec
    {
        internal const int MaxBytes=256*1024,MaxLines=640,MaxPoints=12288,MaxPieces=2048,MaxPrefabs=256,MaxPeers=8;
        private const int Magic=0x42535031;
        private static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
        internal static bool Finite(float x)=>!float.IsNaN(x)&&!float.IsInfinity(x);
        private static bool Name(string s)=>!string.IsNullOrEmpty(s)&&s.Length<=128&&Utf8.GetByteCount(s)<=128&&!s.Any(char.IsControl);
        private static void Point(BinaryWriter w,SharedPoint p){if(!p.Valid)throw new ArgumentException("Invalid shared point.");w.Write(p.X);w.Write(p.Y);w.Write(p.Z);}
        private static SharedPoint Point(BinaryReader r){var p=new SharedPoint(r.ReadSingle(),r.ReadSingle(),r.ReadSingle());if(!p.Valid)throw new ArgumentException("Invalid shared point.");return p;}
        private static void Quaternion(SharedGhost g)
        {float norm=g.X*g.X+g.Y*g.Y+g.Z*g.Z+g.W*g.W;if(!Finite(norm)||norm<0.25f||norm>4)throw new ArgumentException("Invalid preview rotation.");}
        internal static byte[] Encode(SharedPreview p)
        {
            if(p==null||p.Revision<1||p.Tool>5||p.Lines.Count>MaxLines||p.Pieces.Count>MaxPieces||p.Pieces.Any(g=>g==null)||p.Tool==0&&(p.Lines.Count!=0||p.Pieces.Count!=0))throw new ArgumentException("Invalid preview bounds.");
            var names=p.Pieces.Select(g=>g.Prefab).Distinct().ToArray();if(names.Length>MaxPrefabs||names.Any(s=>!Name(s)))throw new ArgumentException("Invalid preview prefabs.");
            using(var stream=new MemoryStream())using(var w=new BinaryWriter(stream,Utf8,true))
            {
                w.Write(Magic);w.Write(p.World);w.Write(p.Revision);w.Write(p.Tool);Point(w,p.Center);
                w.Write((ushort)names.Length);foreach(string name in names){byte[] text=Utf8.GetBytes(name);w.Write((ushort)text.Length);w.Write(text);}
                w.Write((ushort)p.Lines.Count);int points=0;
                foreach(var line in p.Lines)
                {
                    if(line==null||line.Points==null||line.Points.Length<2||line.Points.Length>128||!Finite(line.Width)||line.Width<0.001f||line.Width>0.25f||(points+=line.Points.Length)>MaxPoints)throw new ArgumentException("Invalid preview lines.");
                    w.Write(line.Width);w.Write(line.ThroughGround);w.Write((ushort)line.Points.Length);foreach(var point in line.Points)Point(w,point);
                }
                w.Write((ushort)p.Pieces.Count);
                foreach(var g in p.Pieces){Quaternion(g);w.Write((ushort)Array.IndexOf(names,g.Prefab));Point(w,g.At);w.Write(g.X);w.Write(g.Y);w.Write(g.Z);w.Write(g.W);}
                if(stream.Length>MaxBytes)throw new ArgumentException("Preview packet is too large.");return stream.ToArray();
            }
        }
        internal static SharedPreview Decode(byte[] bytes,long world)
        {
            if(bytes==null||bytes.Length<39||bytes.Length>MaxBytes)throw new ArgumentException("Invalid preview packet size.");
            using(var stream=new MemoryStream(bytes,false))using(var r=new BinaryReader(stream,Utf8,true))
            {
                if(r.ReadInt32()!=Magic)throw new ArgumentException("Unsupported preview protocol.");
                var p=new SharedPreview{World=r.ReadInt64(),Revision=r.ReadInt64(),Tool=r.ReadByte(),Center=Point(r)};
                if(p.World!=world||p.Revision<1||p.Tool>5)throw new ArgumentException("Invalid preview header.");
                int count=r.ReadUInt16();if(count>MaxPrefabs)throw new ArgumentException("Too many preview prefabs.");
                var names=new string[count];
                for(int i=0;i<count;i++){int length=r.ReadUInt16();if(length<1||length>128||length>stream.Length-stream.Position)throw new ArgumentException("Invalid prefab length.");names[i]=Utf8.GetString(r.ReadBytes(length));if(!Name(names[i]))throw new ArgumentException("Invalid prefab name.");}
                count=r.ReadUInt16();if(count>MaxLines)throw new ArgumentException("Too many preview lines.");int points=0;
                for(int i=0;i<count;i++)
                {
                    float width=r.ReadSingle();byte through=r.ReadByte();int length=r.ReadUInt16();
                    if(!Finite(width)||width<0.001f||width>0.25f||through>1||length<2||length>128||(points+=length)>MaxPoints)throw new ArgumentException("Invalid line bounds.");
                    var line=new SharedStroke{Width=width,ThroughGround=through==1,Points=new SharedPoint[length]};for(int j=0;j<length;j++)line.Points[j]=Point(r);p.Lines.Add(line);
                }
                count=r.ReadUInt16();if(count>MaxPieces)throw new ArgumentException("Too many preview pieces.");
                for(int i=0;i<count;i++)
                {
                    int index=r.ReadUInt16();if(index>=names.Length)throw new ArgumentException("Invalid prefab index.");
                    var g=new SharedGhost{Prefab=names[index],At=Point(r),X=r.ReadSingle(),Y=r.ReadSingle(),Z=r.ReadSingle(),W=r.ReadSingle()};Quaternion(g);p.Pieces.Add(g);
                }
                if(stream.Position!=stream.Length||p.Tool==0&&(p.Lines.Count!=0||p.Pieces.Count!=0||names.Length!=0))throw new ArgumentException("Invalid preview tail.");return p;
            }
        }
    }
    internal sealed class SharedPreviewCadence
    {
        private float _nextChange,_nextHeartbeat;
        internal bool Due(float now,bool dirty)=>now>=_nextChange&&(dirty||now>=_nextHeartbeat);
        internal void Sent(float now){_nextChange=now+0.5f;_nextHeartbeat=now+4;}
    }
    internal sealed class SharedPreviewSet
    {
        internal const float Lifetime=12;
        internal sealed class Entry { internal SharedPreview Preview;internal float Seen; }
        internal readonly Dictionary<long,Entry> Entries=new Dictionary<long,Entry>();
        private readonly Dictionary<long,long> _latest=new Dictionary<long,long>();
        private readonly HashSet<long> _cleared=new HashSet<long>();
        internal bool Accept(long sender,SharedPreview preview,float now)
        {
            if(_latest.TryGetValue(sender,out long revision))
            {
                if(preview.Revision<revision)return false;
                if(preview.Revision==revision)
                {
                    if(Entries.TryGetValue(sender,out var current)){current.Seen=now;return false;}
                    // An unchanged heartbeat can revive an expired live draft, never an explicit clear.
                    if(preview.Tool==0||_cleared.Contains(sender))return false;
                }
            }
            else if(_latest.Count>=16)return false;
            if(!Entries.ContainsKey(sender)&&preview.Tool!=0&&Entries.Count>=SharedPreviewCodec.MaxPeers)return false;
            _latest[sender]=preview.Revision;
            if(preview.Tool==0){_cleared.Add(sender);return Entries.Remove(sender);}
            _cleared.Remove(sender);
            Entries[sender]=new Entry{Preview=preview,Seen=now};return true;
        }
        internal long[] Expire(float now,ISet<long> online)
        {var gone=Entries.Where(p=>now-p.Value.Seen>Lifetime||!online.Contains(p.Key)).Select(p=>p.Key).ToArray();foreach(long id in gone)Entries.Remove(id);foreach(long id in _latest.Keys.Where(id=>!online.Contains(id)).ToArray()){_latest.Remove(id);_cleared.Remove(id);}return gone;}
    }
}
