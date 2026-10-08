using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildShapes
{
    // Pure, bounded geometry. Coordinates are metres in the footprint's first-edge frame.
    internal static class HallLayout
    {
        internal const int MaximumCells = 64, MaximumParts = 1024, MaximumCorners = 24;
        internal enum Anchor { Floor, Bottom, RoofLow, Point, Segment }
        internal readonly struct Cell : IEquatable<Cell>
        {
            internal readonly int X, Z;
            internal Cell(int x, int z) { X=x; Z=z; }
            public bool Equals(Cell c) => X==c.X && Z==c.Z;
            public override bool Equals(object o) => o is Cell c && Equals(c);
            public override int GetHashCode() => X*397^Z;
        }
        internal sealed class Wing
        {
            internal int X,Z,W,D;
            internal bool Across;
            internal int Width => Across ? D : W;
            internal int Length => Across ? W : D;
            internal V3 At(double u,double y,double v) => Across ? new V3(X*2+v,y,Z*2+D*2-u) : new V3(X*2+u,y,Z*2+v);
        }
        internal sealed class Part
        {
            internal string Prefab, Role;
            internal V3 At, End;
            internal Anchor Kind;
            internal double Yaw;
        }
        internal sealed class Kit
        {
            internal string Floor="wood_floor", Wall="woodwall", Half="wood_wall_half", Quarter="wood_wall_quarter", Door="wood_door";
            internal string Post="wood_pole2", Beam="wood_beam", ShortBeam="wood_beam_1";
            internal string Roof="wood_roof_45", Ridge="wood_roof_top_45", Wedge="wood_wall_roof_45";
            internal string Arch=null, Raven=null;
            internal double Slope=1, BeamLength=2, ShortLength=1;
            internal int SpanCells=4;
        }
        internal sealed class Design
        {
            internal readonly List<Cell> Cells=new List<Cell>();
            internal readonly List<Wing> Wings=new List<Wing>();
            internal readonly List<Part> Parts=new List<Part>();
            internal readonly List<V3> Vertices=new List<V3>();
            internal V3 Door;
            internal double DoorYaw;
            internal double RoofHeight;
            internal int BoundaryPanels;
        }

        internal static List<Cell> Footprint(IReadOnlyList<V3> corners)
        {
            if(corners==null || corners.Count<3 || corners.Count>MaximumCorners)
                throw new ArgumentException("Mark 3–24 corners in boundary order.");
            for(int i=0;i<corners.Count;i++)
            {
                V3 a=corners[i],b=corners[(i+1)%corners.Count];
                if(!a.Finite || Math.Abs(a.X)>24 || Math.Abs(a.Z)>24 || Math.Abs(a.X/2-Math.Round(a.X/2))>0.001 || Math.Abs(a.Z/2-Math.Round(a.Z/2))>0.001)
                    throw new ArgumentException("Footprint corners must follow the 2 m grid, within 24 m of the first corner.");
                if((a-b).Length<1.9 || (Math.Abs(a.X-b.X)>0.001 && Math.Abs(a.Z-b.Z)>0.001))
                    throw new ArgumentException("Use square corners: each edge follows one of the two grid directions.");
                V3 previous=corners[(i+corners.Count-1)%corners.Count];
                if(Math.Abs((a.X-previous.X)*(b.Z-a.Z)-(a.Z-previous.Z)*(b.X-a.X))<1e-6 && V3.Dot(a-previous,b-a)<0)
                    throw new ArgumentException("A boundary edge doubles back over itself.");
                for(int j=i+1;j<corners.Count;j++)
                {
                    if(j==i+1 || (i==0 && j==corners.Count-1))continue;
                    V3 c=corners[j],d=corners[(j+1)%corners.Count];
                    if(Intersects(a,b,c,d))throw new ArgumentException("The footprint crosses or touches itself. Trace a simple boundary.");
                }
            }
            double area=0;
            for(int i=0;i<corners.Count;i++){V3 a=corners[i],b=corners[(i+1)%corners.Count];area+=a.X*b.Z-b.X*a.Z;}
            if(Math.Abs(area)<7.99)throw new ArgumentException("The footprint has no usable floor area.");
            int x0=(int)Math.Floor(corners.Min(p=>p.X)/2),x1=(int)Math.Ceiling(corners.Max(p=>p.X)/2);
            int z0=(int)Math.Floor(corners.Min(p=>p.Z)/2),z1=(int)Math.Ceiling(corners.Max(p=>p.Z)/2);
            var cells=new List<Cell>();
            for(int z=z0;z<z1;z++)for(int x=x0;x<x1;x++)
                if(Inside(corners,x*2+1,z*2+1))
                {if(cells.Count==MaximumCells)throw new ArgumentException("Keep the floor plan within 256 m² (64 floor tiles).");cells.Add(new Cell(x,z));}
            if(cells.Count==0)throw new ArgumentException("No floor tiles fit the footprint.");
            return cells;
        }
        private static bool Intersects(V3 a,V3 b,V3 c,V3 d)
        {
            double Cross(V3 p,V3 q,V3 r)=>(q.X-p.X)*(r.Z-p.Z)-(q.Z-p.Z)*(r.X-p.X);
            bool Between(V3 p,V3 q,V3 r)=>r.X>=Math.Min(p.X,q.X)-1e-6 && r.X<=Math.Max(p.X,q.X)+1e-6 && r.Z>=Math.Min(p.Z,q.Z)-1e-6 && r.Z<=Math.Max(p.Z,q.Z)+1e-6;
            double ac=Cross(a,b,c),ad=Cross(a,b,d),ca=Cross(c,d,a),cb=Cross(c,d,b);
            return ac*ad<0 && ca*cb<0 || Math.Abs(ac)<1e-6 && Between(a,b,c) || Math.Abs(ad)<1e-6 && Between(a,b,d) || Math.Abs(ca)<1e-6 && Between(c,d,a) || Math.Abs(cb)<1e-6 && Between(c,d,b);
        }
        private static bool Inside(IReadOnlyList<V3> polygon,double x,double z)
        {
            bool inside=false;
            for(int i=0,j=polygon.Count-1;i<polygon.Count;j=i++)
            {V3 a=polygon[i],b=polygon[j];if((a.Z>z)!=(b.Z>z) && x<(b.X-a.X)*(z-a.Z)/(b.Z-a.Z)+a.X)inside=!inside;}
            return inside;
        }

        // Bounded exact-cover search: score whole rectangular wings, then cover the remainder.
        // Keep the best valid cover when the search budget is reached; never emit clipped roof tiles.
        internal static List<Wing> SolveWings(List<Cell> cells,int span)
        {
            if(cells==null || cells.Count==0 || cells.Count>MaximumCells || span<1 || span>6)throw new ArgumentException("Invalid wing solve.");
            var remaining=new HashSet<Cell>(cells);
            List<Wing> best=null;
            double bestCost=double.MaxValue;
            int budget=2500;
            void Search(List<Wing> picked,double cost)
            {
                if(--budget<0 || cost>=bestCost)return;
                if(remaining.Count==0){best=picked.ToList();bestCost=cost;return;}
                if(picked.Count>=12)return;
                Cell first=remaining.OrderBy(p=>p.Z).ThenBy(p=>p.X).First();
                var candidates=new List<Wing>();
                for(int d=1;d<=12;d++)for(int w=1;w<=12;w++)
                {
                    if(Math.Min(w,d)>span || w*d>remaining.Count)continue;
                    bool all=true;
                    for(int z=0;z<d && all;z++)for(int x=0;x<w;x++)if(!remaining.Contains(new Cell(first.X+x,first.Z+z))){all=false;break;}
                    if(!all)continue;
                    bool across=w>d; // Ridge follows the longer dimension.
                    candidates.Add(new Wing{X=first.X,Z=first.Z,W=w,D=d,Across=across});
                }
                foreach(Wing wing in candidates.OrderByDescending(w=>w.W*w.D).ThenBy(w=>w.Width))
                {
                    for(int z=0;z<wing.D;z++)for(int x=0;x<wing.W;x++)remaining.Remove(new Cell(wing.X+x,wing.Z+z));
                    picked.Add(wing);
                    Search(picked,cost+100+2*(wing.W+wing.D)+ (wing.Width==1?30:0));
                    picked.RemoveAt(picked.Count-1);
                    for(int z=0;z<wing.D;z++)for(int x=0;x<wing.W;x++)remaining.Add(new Cell(wing.X+x,wing.Z+z));
                }
            }
            Search(new List<Wing>(),0);
            if(best==null)throw new ArgumentException("This outline needs too many roof wings. Simplify its narrow sections.");
            return best;
        }

        internal static Design Plan(IReadOnlyList<V3> corners,Kit kit,int height,int detail,int entrance)
        {
            if(kit==null || height<2 || height>4 || detail<0 || detail>3 || kit.Slope!=1 && kit.Slope!=0.5)
                throw new ArgumentException("Choose walls 2–4 m high, a native roof pitch, and intricacy 0–3.");
            var plan=new Design();plan.Cells.AddRange(Footprint(corners));plan.Wings.AddRange(SolveWings(plan.Cells,kit.SpanCells));
            var set=new HashSet<Cell>(plan.Cells);
            var unique=new HashSet<string>(StringComparer.Ordinal);
            void Add(string name,V3 at,Anchor kind,double yaw=0,string role="shell",V3 end=default)
            {
                if(string.IsNullOrEmpty(name))return;
                string key=name+":"+kind+":"+at.X+","+at.Y+","+at.Z+":"+yaw+":"+end.X+","+end.Y+","+end.Z;
                if(!unique.Add(key))return;
                if(plan.Parts.Count>=MaximumParts)throw new ArgumentException("This design exceeds 1,024 pieces. Reduce its area or intricacy.");
                plan.Parts.Add(new Part{Prefab=name,At=at,End=end,Kind=kind,Yaw=yaw,Role=role});
            }
            void Rod(string name,V3 a,V3 b,double nativeLength,string role="frame")
            {
                double length=(b-a).Length;
                if(length<0.2)return;
                int count=Math.Max(1,(int)Math.Ceiling(length/nativeLength-1e-9));
                V3 direction=(b-a)*(1/length);
                for(int i=0;i<count;i++)
                {
                    double offset=count==1?0:(length-nativeLength)*i/(count-1);
                    V3 start=a+direction*offset;
                    Add(name,start,Anchor.Segment,0,role,start+direction*nativeLength);
                }
            }
            var boundaries=new List<(V3 a,V3 b,double yaw)>();
            foreach(Cell c in plan.Cells)
            {
                Add(kit.Floor,new V3(c.X*2+1,0,c.Z*2+1),Anchor.Floor,0,"floor");
                if(!set.Contains(new Cell(c.X,c.Z-1)))boundaries.Add((new V3(c.X*2,0,c.Z*2),new V3(c.X*2+2,0,c.Z*2),0));
                if(!set.Contains(new Cell(c.X+1,c.Z)))boundaries.Add((new V3(c.X*2+2,0,c.Z*2),new V3(c.X*2+2,0,c.Z*2+2),270));
                if(!set.Contains(new Cell(c.X,c.Z+1)))boundaries.Add((new V3(c.X*2+2,0,c.Z*2+2),new V3(c.X*2,0,c.Z*2+2),180));
                if(!set.Contains(new Cell(c.X-1,c.Z)))boundaries.Add((new V3(c.X*2,0,c.Z*2+2),new V3(c.X*2,0,c.Z*2),90));
            }
            // Entrance numbers follow the user's edges, not arbitrary cell traversal.
            V3 ea=corners[((entrance%corners.Count)+corners.Count)%corners.Count],eb=corners[(entrance+1+corners.Count)%corners.Count];
            V3 desired=(ea+eb)*0.5;
            var doorway=boundaries.OrderBy(e=>(((e.a+e.b)*0.5)-desired).Length).First();
            plan.Door=desired;plan.DoorYaw=doorway.yaw;
            double doorAngle=doorway.yaw*Math.PI/180;
            V3 tangent=new V3(Math.Cos(doorAngle),0,-Math.Sin(doorAngle));
            Add(kit.Door,plan.Door,Anchor.Bottom,doorway.yaw,"entrance");plan.BoundaryPanels=boundaries.Count;
            foreach(var edge in boundaries)
            {
                V3 at=(edge.a+edge.b)*0.5;
                double along=V3.Dot(at-plan.Door,tangent);
                V3 normal=new V3(-tangent.Z,0,tangent.X);
                bool door=Math.Abs(V3.Dot(at-plan.Door,normal))<0.01 && Math.Abs(along)<1.999;
                for(int h=0;h<height;h+=2)
                {
                    if(door && h==0)
                    {
                        // Keep the entrance centred even when it splits two native 2 m wall panels.
                        if(Math.Abs(along)>0.1)
                            for(int q=0;q<2;q++)Add(kit.Quarter,at+tangent*(Math.Sign(along)*0.5)+new V3(0,q,0),Anchor.Bottom,edge.yaw,"wall");
                    }
                    else Add(h+2<=height?kit.Wall:kit.Half,at+new V3(0,h,0),Anchor.Bottom,edge.yaw,"wall");
                }
                Rod(kit.Beam,edge.a+new V3(0,height,0),edge.b+new V3(0,height,0),kit.BeamLength);
            }
            // Foundation contact points include all floor corners, also under interior tiles.
            var vertices=new HashSet<Cell>();
            foreach(Cell c in plan.Cells)for(int dz=0;dz<=1;dz++)for(int dx=0;dx<=1;dx++)vertices.Add(new Cell(c.X+dx,c.Z+dz));
            plan.Vertices.AddRange(vertices.OrderBy(v=>v.Z).ThenBy(v=>v.X).Select(v=>new V3(v.X*2,0,v.Z*2)));
            foreach(var edge in boundaries)
            {
                foreach(V3 v in new[]{edge.a,edge.b})
                    for(int h=0;h<height;h+=2)Add(kit.Post,v+new V3(0,Math.Min(h,height-2),0),Anchor.Bottom,0,"post");
            }
            foreach(Wing wing in plan.Wings)
            {
                double yaw=wing.Across?90:0,width=wing.Width*2,length=wing.Length*2;
                double peak=height+width*0.5*kit.Slope;
                plan.RoofHeight=Math.Max(plan.RoofHeight,peak);
                for(int row=0;row<wing.Length;row++)for(int col=0;col<wing.Width;col++)
                {
                    bool center=wing.Width%2==1 && col==wing.Width/2;
                    bool left=col<wing.Width/2;
                    double low=height+Math.Min(col,wing.Width-1-col)*2*kit.Slope;
                    if(center)Add(kit.Ridge,wing.At(col*2+1,low,row*2+1),Anchor.Floor,yaw+90,"roof");
                    else Add(kit.Roof,wing.At(left?col*2:(col+1)*2,low,row*2+1),Anchor.RoofLow,yaw+(left?270:90),"roof");
                }
                for(int end=0;end<2;end++)
                {
                    double v=end*length;
                    for(int col=0;col<wing.Width;col++)
                    {
                        double low=Math.Min(col,wing.Width-1-col)*2*kit.Slope;
                        for(int h=0;h<low-0.001;h+=2)
                            Add(h+2<=low?kit.Wall:kit.Half,wing.At(col*2+1,height+h,v),Anchor.Bottom,yaw,"gable");
                        bool center=wing.Width%2==1 && col==wing.Width/2;
                        if(!center)Add(kit.Wedge,wing.At(col*2+1,height+low,v),Anchor.Bottom,yaw+(col<wing.Width/2?0:180),"gable");
                        // Odd-width peak is an intentional small triangular gable vent.
                        else if(detail>0)
                        {
                            V3 tip=wing.At(col*2+1,height+low+kit.Slope,v);
                            Rod(kit.ShortBeam,wing.At(col*2,height+low,v),tip,kit.ShortLength,"ornament");
                            Rod(kit.ShortBeam,wing.At(col*2+2,height+low,v),tip,kit.ShortLength,"ornament");
                        }
                    }
                    if(detail>=1)
                    {
                        Rod(kit.Beam,wing.At(0,height,v),wing.At(width/2,peak,v),kit.BeamLength,"ornament");
                        Rod(kit.Beam,wing.At(width, height,v),wing.At(width/2,peak,v),kit.BeamLength,"ornament");
                    }
                    if(detail>=2)
                    {
                        V3 crest=wing.At(width/2,peak-0.2,v);
                        Rod(kit.Beam,wing.At(width/2,height,v),crest,kit.BeamLength,"ornament");
                        Rod(kit.Beam,wing.At(width*0.25,height,v),crest,kit.BeamLength,"ornament");
                        Rod(kit.Beam,wing.At(width*0.75,height,v),crest,kit.BeamLength,"ornament");
                    }
                    if(detail>=3 && kit.Raven!=null)Add(kit.Raven,wing.At(width/2,peak,v),Anchor.Point,yaw+(end==0?180:0),"ornament");
                }
                // Repeated structural trusses, independently of decorative intricacy.
                for(int bay=0;bay<=wing.Length;bay+=2)
                {
                    double v=Math.Min(bay*2,length);
                    Rod(kit.Beam,wing.At(0,height,v),wing.At(width,height,v),kit.BeamLength);
                    Rod(kit.Beam,wing.At(0,height,v),wing.At(width/2,peak,v),kit.BeamLength);
                    Rod(kit.Beam,wing.At(width,height,v),wing.At(width/2,peak,v),kit.BeamLength);
                    if(detail>=2 && kit.Arch!=null && width>=4)
                    {
                        Add(kit.Arch,wing.At(0,height-2,v),Anchor.Bottom,yaw,"ornament");
                        Add(kit.Arch,wing.At(width,height-2,v),Anchor.Bottom,yaw+180,"ornament");
                    }
                    if(detail>=1 && width>=4)
                    {
                        Rod(kit.ShortBeam,wing.At(0,height-1,v),wing.At(1,height,v),kit.ShortLength,"ornament");
                        Rod(kit.ShortBeam,wing.At(width,height-1,v),wing.At(width-1,height,v),kit.ShortLength,"ornament");
                    }
                }
                Rod(kit.Beam,wing.At(width/2,peak,0),wing.At(width/2,peak,length),kit.BeamLength);
                if(detail>=3)
                {
                    for(int row=0;row<wing.Length;row++)
                    {
                        Rod(kit.Beam,wing.At(-0.3,height+0.1,row*2),wing.At(-0.3,height+0.1,(row+1)*2),kit.BeamLength,"ornament");
                        Rod(kit.Beam,wing.At(width+0.3,height+0.1,row*2),wing.At(width+0.3,height+0.1,(row+1)*2),kit.BeamLength,"ornament");
                    }
                }
            }
            return plan;
        }
    }
}
