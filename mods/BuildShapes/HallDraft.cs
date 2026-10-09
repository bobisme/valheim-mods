using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace BuildShapes
{
    internal sealed class HallDraft
    {
        public int Version=1;
        public long World,Player;
        public float[] Origin;
        public float Yaw,Raise;
        public float[][] Corners,Doors;
        public int Height,Detail,Entrance,Material,Opening,Crest,Storeys;
        public bool Roof45,Solid,ShowRoof,Tiered,Overhang,Porch,Sweep,Basement,GuideOnly;
        private static bool Finite(float x)=>!float.IsNaN(x) && !float.IsInfinity(x);
        private static bool Point(float[] p)=>p!=null && p.Length==3 && p.All(x=>Finite(x) && Math.Abs(x)<1000000);
        internal bool Valid(long world,long player)=>Version==1 && World==world && Player==player &&
            Point(Origin) && Finite(Yaw) && Math.Abs(Yaw)<=360 && Finite(Raise) && Raise>=0 && Raise<=2 &&
            Corners!=null && Corners.Length>=1 && Corners.Length<=24 && Corners.All(Point) &&
            Doors!=null && Doors.Length<=8 && Doors.All(Point) &&
            Height>=2 && Height<=4 && Detail>=0 && Detail<=4 && Entrance>=0 && Entrance<24 &&
            Material>=0 && Material<=4 && Opening>=0 && Opening<=2 && Crest>=0 && Crest<=3 && Storeys>=1 && Storeys<=3;
    }
    internal static class HallDraftStore
    {
        internal static void Write(string path,HallDraft draft)
        {
            if(draft==null || !draft.Valid(draft.World,draft.Player))throw new ArgumentException("Invalid Hallwright draft.");
            string data=JsonConvert.SerializeObject(draft),temp=path+".tmp";
            if(data.Length>65536)throw new ArgumentException("Hallwright draft is too large.");
            File.WriteAllText(temp,data);
            if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
        }
        internal static HallDraft Read(string path,long world,long player)
        {
            if(new FileInfo(path).Length>65536)throw new ArgumentException("Hallwright draft is too large.");
            var draft=JsonConvert.DeserializeObject<HallDraft>(File.ReadAllText(path));
            if(draft==null || !draft.Valid(world,player))throw new ArgumentException("Saved draft is invalid or belongs to another character/world.");
            return draft;
        }
    }
}
