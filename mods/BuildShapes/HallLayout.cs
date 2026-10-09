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
            internal double TierLift;
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
            internal double Slope=1, BeamLength=2, ShortLength=1, DoorHeight=2;
            internal int SpanCells=4;
        }
        internal sealed class Design
        {
            internal readonly List<Cell> Cells=new List<Cell>();
            internal readonly List<Wing> Wings=new List<Wing>();
            internal readonly List<Part> Parts=new List<Part>();
            internal readonly List<V3> Vertices=new List<V3>();
            internal readonly List<V3> PorchFloors=new List<V3>();
            internal V3 Door;
            internal V3 EntryLanding;
            internal double DoorYaw;
            internal double RoofHeight;
            internal int BoundaryPanels;
            internal int TieredWings;
        }
        internal sealed class Details
        {
            internal bool Overhang, Porch, Sweep;
            internal string Finial;
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

        internal static Design Plan(IReadOnlyList<V3> corners,Kit kit,int height,int detail,int entrance,bool tiered=false,Details details=null)
        {
            if(kit==null || kit.DoorHeight<2 || kit.DoorHeight>height || kit.DoorHeight!=Math.Round(kit.DoorHeight) || height<2 || height>4 || detail<0 || detail>3 || kit.Slope!=1 && kit.Slope!=0.5)
                throw new ArgumentException("Choose walls 2–4 m high, a native roof pitch, and intricacy 0–3.");
            var plan=new Design();plan.Cells.AddRange(Footprint(corners));plan.Wings.AddRange(SolveWings(plan.Cells,kit.SpanCells));
            var set=new HashSet<Cell>(plan.Cells);
            details=details??new Details{Finial=detail>=3?kit.Raven:null};
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
                if(name==kit.Beam && length<nativeLength && length>=kit.ShortLength)
                {name=kit.ShortBeam;nativeLength=kit.ShortLength;}
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
            V3 outward=new V3(-Math.Sin(doorAngle),0,-Math.Cos(doorAngle));
            V3 PorchAt(double u,double y,double v)=>plan.Door-tangent*u+outward*v+new V3(0,y,0);
            plan.EntryLanding=plan.Door;
            // All footprint edges and roof tiles are orthogonal in this frame, including rotated wings.
            (double x0,double x1,double z0,double z1) Rect(V3 a,V3 b)=>(Math.Min(a.X,b.X),Math.Max(a.X,b.X),Math.Min(a.Z,b.Z),Math.Max(a.Z,b.Z));
            bool Overlap((double x0,double x1,double z0,double z1) a,(double x0,double x1,double z0,double z1) b)=>
                a.x1>b.x0+0.001 && b.x1>a.x0+0.001 && a.z1>b.z0+0.001 && b.z1>a.z0+0.001;
            bool Outside((double x0,double x1,double z0,double z1) r)=>!plan.Cells.Any(c=>Overlap(r,(c.X*2,c.X*2+2,c.Z*2,c.Z*2+2)));
            var extensions=new List<(double x0,double x1,double z0,double z1)>();
            if(details.Porch)
            {
                if((eb-ea).Length<4)throw new ArgumentException("A covered porch needs a straight entrance edge at least 4 m long.");
                var deck=Rect(PorchAt(-2,0,0),PorchAt(2,0,2));
                if(!Outside(deck))throw new ArgumentException("The porch reaches another wing. Choose an entrance with 2 m of clear space outside.");
                extensions.Add(deck);plan.EntryLanding=PorchAt(0,0,2);
            }
            Add(kit.Door,plan.Door,Anchor.Bottom,doorway.yaw,"entrance");plan.BoundaryPanels=boundaries.Count;
            foreach(var edge in boundaries)
            {
                V3 at=(edge.a+edge.b)*0.5;
                double along=V3.Dot(at-plan.Door,tangent);
                V3 normal=new V3(-tangent.Z,0,tangent.X);
                bool door=Math.Abs(V3.Dot(at-plan.Door,normal))<0.01 && Math.Abs(along)<1.999;
                if(door && Math.Abs(along)>0.1)
                    for(int q=0;q<kit.DoorHeight;q++)Add(kit.Quarter,at+tangent*(Math.Sign(along)*0.5)+new V3(0,q,0),Anchor.Bottom,edge.yaw,"wall");
                // Header starts above the actual opening: a 3 m gate must not have a wall across its upper metre.
                for(int h=door?(int)kit.DoorHeight:0;h<height;h+=2)
                    Add(h+2<=height?kit.Wall:kit.Half,at+new V3(0,h,0),Anchor.Bottom,edge.yaw,"wall");
                Rod(kit.Beam,edge.a+new V3(0,height,0),edge.b+new V3(0,height,0),kit.BeamLength);
            }
            // Foundation contact points include all floor corners, also under interior tiles.
            var vertices=new HashSet<Cell>();
            foreach(Cell c in plan.Cells)for(int dz=0;dz<=1;dz++)for(int dx=0;dx<=1;dx++)vertices.Add(new Cell(c.X+dx,c.Z+dz));
            plan.Vertices.AddRange(vertices.OrderBy(v=>v.Z).ThenBy(v=>v.X).Select(v=>new V3(v.X*2,0,v.Z*2)));
            if(details.Porch)
            {
                double yaw=doorway.yaw+180,peak=height+2*kit.Slope;
                for(int side=0;side<2;side++)
                {
                    double u=side==0?-1:1;
                    V3 floor=PorchAt(u,0,1);plan.PorchFloors.Add(floor);
                    Add(kit.Floor,floor,Anchor.Floor,yaw,"porch floor");
                    Add(kit.Roof,PorchAt(side==0?-2:2,height,1),Anchor.RoofLow,yaw+(side==0?270:90),"porch roof");
                    V3 inside=PorchAt(u,0,-0.1);
                    var behind=plan.Wings.FirstOrDefault(w=>inside.X>w.X*2 && inside.X<(w.X+w.W)*2 && inside.Z>w.Z*2 && inside.Z<(w.Z+w.D)*2);
                    bool gableBack=behind!=null && (behind.Across
                        ?Math.Abs(plan.Door.X-behind.X*2)<0.001 || Math.Abs(plan.Door.X-(behind.X+behind.W)*2)<0.001
                        :Math.Abs(plan.Door.Z-behind.Z*2)<0.001 || Math.Abs(plan.Door.Z-(behind.Z+behind.D)*2)<0.001);
                    if(!gableBack)Add(kit.Wedge,PorchAt(u,height,0),Anchor.Bottom,yaw+(side==0?0:180),"porch gable");
                }
                foreach(double u in new[]{-2.0,0,2.0})foreach(double v in new[]{0.0,2})
                {
                    V3 point=PorchAt(u,0,v);
                    if(!plan.Vertices.Any(p=>(p-point).Length<0.001))plan.Vertices.Add(point);
                    if(u==0)continue;
                    for(int h=0;h<height;h+=2)Add(kit.Post,PorchAt(u,Math.Min(h,height-2),v),Anchor.Bottom,0,"porch post");
                }
                foreach(double v in new[]{0.0,2})
                {
                    Rod(kit.Beam,PorchAt(-2,height,v),PorchAt(2,height,v),kit.BeamLength,"porch frame");
                    Rod(kit.Beam,PorchAt(-2,height,v),PorchAt(0,peak,v),kit.BeamLength,"porch frame");
                    Rod(kit.Beam,PorchAt(2,height,v),PorchAt(0,peak,v),kit.BeamLength,"porch frame");
                    foreach(double u in new[]{-2.0,2})Rod(kit.ShortBeam,PorchAt(u,height-1,v),PorchAt(u+(u<0?1:-1),height,v),kit.ShortLength,"porch brace");
                }
                foreach(double u in new[]{-2.0,2})Rod(kit.Beam,PorchAt(u,height,0),PorchAt(u,height,2),kit.BeamLength,"porch frame");
                Rod(kit.Beam,PorchAt(0,peak,0),PorchAt(0,peak,2),kit.BeamLength,"porch frame");
            }
            foreach(var edge in boundaries)
            {
                foreach(V3 v in new[]{edge.a,edge.b})
                    for(int h=0;h<height;h+=2)Add(kit.Post,v+new V3(0,Math.Min(h,height-2),0),Anchor.Bottom,0,"post");
            }
            foreach(Wing wing in plan.Wings)
            {
                double yaw=wing.Across?90:0,width=wing.Width*2,length=wing.Length*2;
                wing.TierLift=tiered && wing.Width>=3?1:0;
                if(wing.TierLift>0)plan.TieredWings++;
                double peak=height+width*0.5*kit.Slope+wing.TierLift;
                double Lift(int col)=>col>0 && col<wing.Width-1?wing.TierLift:0;
                void Rafter(double v,string role)
                {
                    if(wing.TierLift==0)
                    {
                        Rod(kit.Beam,wing.At(0,height,v),wing.At(width/2,peak,v),kit.BeamLength,role);
                        Rod(kit.Beam,wing.At(width,height,v),wing.At(width/2,peak,v),kit.BeamLength,role);
                    }
                    else foreach(bool left in new[]{true,false})
                    {
                        double edge=left?0:width,shoulder=left?2:width-2,y=height+2*kit.Slope;
                        Rod(kit.Beam,wing.At(edge,height,v),wing.At(shoulder,y,v),kit.BeamLength,role);
                        Rod(kit.ShortBeam,wing.At(shoulder,y,v),wing.At(shoulder,y+wing.TierLift,v),kit.ShortLength,role);
                        Rod(kit.Beam,wing.At(shoulder,y+wing.TierLift,v),wing.At(width/2,peak,v),kit.BeamLength,role);
                    }
                }
                plan.RoofHeight=Math.Max(plan.RoofHeight,peak);
                for(int row=0;row<wing.Length;row++)for(int col=0;col<wing.Width;col++)
                {
                    bool center=wing.Width%2==1 && col==wing.Width/2;
                    bool left=col<wing.Width/2;
                    double low=height+Math.Min(col,wing.Width-1-col)*2*kit.Slope+Lift(col);
                    if(center)Add(kit.Ridge,wing.At(col*2+1,low,row*2+1),Anchor.Floor,yaw+90,"roof");
                    else Add(kit.Roof,wing.At(left?col*2:(col+1)*2,low,row*2+1),Anchor.RoofLow,yaw+(left?270:90),"roof");
                }
                if(wing.TierLift>0)
                {
                    double y=height+2*kit.Slope;
                    foreach(double shoulder in new[]{2.0,width-2})
                    {
                        for(int row=0;row<wing.Length;row++)
                            Add(kit.Half,wing.At(shoulder,y,row*2+1),Anchor.Bottom,yaw+(shoulder==2?90:270),"tier wall");
                        Rod(kit.Beam,wing.At(shoulder,y,0),wing.At(shoulder,y,length),kit.BeamLength);
                        Rod(kit.Beam,wing.At(shoulder,y+wing.TierLift,0),wing.At(shoulder,y+wing.TierLift,length),kit.BeamLength);
                    }
                }
                for(int end=0;end<2;end++)
                {
                    double v=end*length;
                    for(int col=0;col<wing.Width;col++)
                    {
                        double low=Math.Min(col,wing.Width-1-col)*2*kit.Slope+Lift(col);
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
                        Rafter(v,"ornament");
                    }
                    if(detail>=2)
                    {
                        V3 crest=wing.At(width/2,peak-0.2,v);
                        Rod(kit.Beam,wing.At(width/2,height,v),crest,kit.BeamLength,"ornament");
                        Rod(kit.Beam,wing.At(width*0.25,height,v),crest,kit.BeamLength,"ornament");
                        Rod(kit.Beam,wing.At(width*0.75,height,v),crest,kit.BeamLength,"ornament");
                    }
                    double sign=end==0?-1:1;
                    bool exposed=Outside(Rect(wing.At(0,0,v),wing.At(width,0,v+sign*0.5)));
                    if(details.Sweep && exposed && width>=4)
                    {
                        foreach(bool left in new[]{true,false})
                        {
                            double edge=left?0:width,mid=left?width*0.25:width*0.75;
                            foreach(var segment in Curve.Plan(wing.At(edge,height,v+sign*0.15),
                                wing.At(mid,height+(peak-height)*0.3,v+sign*0.15),wing.At(width/2,peak+0.6,v+sign*0.15),kit.ShortLength))
                                Add(kit.ShortBeam,segment.Start,Anchor.Segment,0,"gable trim",segment.End);
                        }
                    }
                    string finial=details.Finial;
                    if(finial!=null && Outside(Rect(wing.At(width/2-0.3,0,v),wing.At(width/2+0.3,0,v+sign*3.4))))
                        Add(finial,wing.At(width/2,peak,v),Anchor.Point,yaw+(end==0?180:0),"ridge carving");
                }
                if(details.Overhang)
                {
                    bool Extend(V3 a,V3 b)
                    {
                        var rectangle=Rect(a,b);
                        if(!Outside(rectangle) || extensions.Any(r=>Overlap(r,rectangle)))return false;
                        extensions.Add(rectangle);return true;
                    }
                    // A native 26° apron drops only 1 m over its 2 m depth, preserving useful headroom.
                    for(int row=-1;row<=wing.Length;row++)foreach(bool left in new[]{true,false})
                    {
                        double outer=left?-2:width+2,inner=left?0:width,v=row*2+1;
                        if(!Extend(wing.At(outer,0,row*2),wing.At(inner,0,row*2+2)))continue;
                        Add("wood_roof",wing.At(outer,height-1,v),Anchor.RoofLow,yaw+(left?270:90),"overhang roof");
                        // Brackets under side aisles; end corners connect through the extended end roof.
                        if(row>=0 && row<wing.Length)
                            Rod(kit.Beam,wing.At(inner,height-2,v),wing.At(outer,height-1,v),kit.BeamLength,"eave bracket");
                    }
                    foreach(int row in new[]{-1,wing.Length})for(int col=0;col<wing.Width;col++)
                    {
                        if(!Extend(wing.At(col*2,0,row*2),wing.At(col*2+2,0,row*2+2)))continue;
                        bool center=wing.Width%2==1 && col==wing.Width/2,left=col<wing.Width/2;
                        double low=height+Math.Min(col,wing.Width-1-col)*2*kit.Slope+Lift(col),v=row*2+1;
                        if(center)Add(kit.Ridge,wing.At(col*2+1,low,v),Anchor.Floor,yaw+90,"overhang roof");
                        else Add(kit.Roof,wing.At(left?col*2:(col+1)*2,low,v),Anchor.RoofLow,yaw+(left?270:90),"overhang roof");
                        double boundary=row<0?0:length,tip=row<0?-2:length+2;
                        Rod(kit.Beam,wing.At(col*2+1,low+kit.Slope,boundary),wing.At(col*2+1,low+kit.Slope,tip),kit.BeamLength,"eave outrigger");
                    }
                }
                // Repeated structural trusses, independently of decorative intricacy.
                for(int bay=0;bay<=wing.Length;bay+=2)
                {
                    double v=Math.Min(bay*2,length);
                    Rod(kit.Beam,wing.At(0,height,v),wing.At(width,height,v),kit.BeamLength);
                    Rafter(v,"frame");
                    if(wing.TierLift>0)foreach(double shoulder in new[]{2.0,width-2})
                    {
                        double top=height+2*kit.Slope+wing.TierLift;
                        for(int h=0;h<top;h+=2)
                            Add(kit.Post,wing.At(shoulder,Math.Min(h,top-2),v),Anchor.Bottom,0,"tier post");
                    }
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
