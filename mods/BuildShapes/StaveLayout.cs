using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildShapes
{
    // Tall sanctum, low aisles and nested pierced roofs on the native two-metre grid.
    internal static class StaveLayout
    {
        internal sealed class Options { internal int Height=6,Crowns=1;internal bool Gallery=true; }
        internal static IEnumerable<HallLayout.Cell> Tiles(HallLayout.Wing w)
        {for(int z=0;z<w.D;z++)for(int x=0;x<w.W;x++)yield return new HallLayout.Cell(w.X+x,w.Z+z);}
        private static V3[] Corners(HallLayout.Wing w)=>new[]{new V3(w.X*2,0,w.Z*2),new V3((w.X+w.W)*2,0,w.Z*2),new V3((w.X+w.W)*2,0,(w.Z+w.D)*2),new V3(w.X*2,0,(w.Z+w.D)*2)};
        internal static HallLayout.Wing SolveSanctum(IReadOnlyList<HallLayout.Cell> cells,int span,Options options,V3 entrance,Func<HallLayout.Wing,bool> accept=null)
        {
            var set=new HashSet<HallLayout.Cell>(cells);int maxX=cells.Max(c=>c.X)+1,maxZ=cells.Max(c=>c.Z)+1;
            HallLayout.Wing best=null;double score=double.MinValue;int margin=options.Gallery?1:0,min=options.Crowns*2+1;
            foreach(var start in cells.OrderBy(c=>c.Z).ThenBy(c=>c.X))
            for(int d=min;d<=Math.Min(maxZ-start.Z,cells.Count/min);d++)
            for(int w=min;w<=Math.Min(maxX-start.X,cells.Count/d);w++)
            {
                int width=Math.Min(w,d);if(width%2==0||width>span)continue;
                bool fit=true;
                for(int z=-margin;z<d+margin&&fit;z++)for(int x=-margin;x<w+margin;x++)
                    if(!set.Contains(new HallLayout.Cell(start.X+x,start.Z+z))){fit=false;break;}
                if(!fit)continue;
                var wing=new HallLayout.Wing{X=start.X,Z=start.Z,W=w,D=d,Across=w>d};
                if(accept!=null&&!accept(wing))continue;
                V3 center=wing.At(wing.Width,0,wing.Length);double cost=w*d*100+width*4-(center-entrance).Length;
                if(cost>score){best=wing;score=cost;}
            }
            if(best==null)throw new ArgumentException(options.Crowns==2?
                "Two crowns need a 10 m-wide chamber and core-wood framing. Draw at least 14 × 14 m with galleries, or choose one crown.":
                "A stave temple needs a 6 × 6 m chamber. Draw at least 10 × 10 m with galleries, or turn galleries off.");
            return best;
        }
        internal static HallLayout.Design Plan(IReadOnlyList<V3> corners,HallLayout.Kit kit,int sideHeight,int detail,int entrance,HallLayout.Details details,Options options)
        {
            if(options==null||options.Height<4||options.Height>8||options.Height%2!=0||options.Crowns<1||options.Crowns>2)throw new ArgumentException("Choose chamber height 4, 6 or 8 m and one or two tower crowns.");
            if(options.Height<sideHeight+2)throw new ArgumentException("Keep the chamber at least 2 m taller than the gallery / side wings.");
            if(details==null||details.Storeys!=1||details.Basement)throw new ArgumentException("Stave temples use one open chamber. Switch to Longhouse for storeys or a basement.");
            var plan=HallLayout.Plan(corners,kit,sideHeight,detail,entrance,false,new HallLayout.Details{SuppressRoof=true,OpenGallery=options.Gallery,Porch=details.Porch,DoorPoints=details.DoorPoints});
            HallLayout.Cell Beside(HallDoors.Entrance d,double x,double z)
            {V3 p=d.At+HallLayout.Turn(new V3(x,0,z),d.Yaw);return new HallLayout.Cell((int)Math.Floor(p.X/2),(int)Math.Floor(p.Z/2));}
            bool FitsDoors(HallLayout.Wing w)
            {
                var cells=new HashSet<HallLayout.Cell>(Tiles(w));
                // A chamber edge must not bisect an existing exterior opening.
                return plan.Entrances.All(d=>cells.Contains(Beside(d,-0.5,1))==cells.Contains(Beside(d,0.5,1)));
            }
            plan.StaveTemple=true;plan.Sanctum=SolveSanctum(plan.Cells,kit.SpanCells,options,plan.Door,FitsDoors);
            var core=plan.Sanctum;var coreCells=new HashSet<HallLayout.Cell>(Tiles(core));
            var fixedDoors=plan.Entrances.Where(d=>coreCells.Contains(Beside(d,-0.5,1))&&coreCells.Contains(Beside(d,0.5,1))).ToArray();
            bool Matches(HallDoors.Entrance a,HallDoors.Entrance b)=>(a.At-b.At).Length<0.01&&Math.Abs(a.Yaw-b.Yaw)<0.01;
            var unvisited=new HashSet<HallLayout.Cell>(plan.Cells.Where(c=>!coreCells.Contains(c)));var components=new List<HashSet<HallLayout.Cell>>();
            while(unvisited.Count>0)
            {
                var first=unvisited.OrderBy(c=>c.Z).ThenBy(c=>c.X).First();var component=new HashSet<HallLayout.Cell>{first};var todo=new Queue<HallLayout.Cell>();todo.Enqueue(first);unvisited.Remove(first);
                while(todo.Count>0){var c=todo.Dequeue();foreach(var next in new[]{new HallLayout.Cell(c.X-1,c.Z),new HallLayout.Cell(c.X+1,c.Z),new HallLayout.Cell(c.X,c.Z-1),new HallLayout.Cell(c.X,c.Z+1)})if(unvisited.Remove(next)){component.Add(next);todo.Enqueue(next);}}
                components.Add(component);
            }
            bool Connects(HallDoors.Entrance door,HashSet<HallLayout.Cell> component)=>component.Any(c=>
            {V3 local=HallLayout.Turn(new V3(c.X*2+1,0,c.Z*2+1)-door.At,-door.Yaw);return Math.Abs(local.Z+1)<0.01&&Math.Abs(local.X)<1.6;});
            bool Internal(HallDoors.Entrance d)=>new[]{Beside(d,-0.5,-1),Beside(d,0.5,-1)}.All(c=>plan.Cells.Contains(c)&&!coreCells.Contains(c));
            var interfaces=new List<HallDoors.Entrance>();
            for(double x=core.X*2+1;x<=(core.X+core.W)*2-1;x++)
            {interfaces.Add(new HallDoors.Entrance{At=new V3(x,0,core.Z*2),Yaw=0});interfaces.Add(new HallDoors.Entrance{At=new V3(x,0,(core.Z+core.D)*2),Yaw=180});}
            for(double z=core.Z*2+1;z<=(core.Z+core.D)*2-1;z++)
            {interfaces.Add(new HallDoors.Entrance{At=new V3(core.X*2,0,z),Yaw=90});interfaces.Add(new HallDoors.Entrance{At=new V3((core.X+core.W)*2,0,z),Yaw=270});}
            interfaces=interfaces.Where(Internal).ToList();
            var innerHints=fixedDoors.Select(d=>d.At).ToList();
            // All exterior doors in a connected aisle can share one passage into the chamber.
            foreach(var component in components)
            {
                var exterior=plan.Entrances.Where(d=>component.Contains(Beside(d,-0.5,1))||component.Contains(Beside(d,0.5,1))).ToArray();
                double Distance(HallDoors.Entrance d)=>exterior.Length==0?(d.At-plan.Door).Length:exterior.Min(e=>(d.At-e.At).Length);
                var target=interfaces.Where(d=>component.Contains(Beside(d,-0.5,-1))&&component.Contains(Beside(d,0.5,-1))).OrderBy(Distance).ThenBy(d=>d.Yaw).ThenBy(d=>d.At.Z).ThenBy(d=>d.At.X).FirstOrDefault();
                if(target==null||innerHints.Count>=HallDoors.Maximum)throw new ArgumentException("The side wings cannot all reach the temple chamber. Use fewer branches or open galleries.");
                innerHints.Add(target.At);
            }
            bool Access(IReadOnlyList<HallDoors.Entrance> doors)=>
                doors.All(d=>Internal(d)||fixedDoors.Any(f=>Matches(d,f)))&&
                (doors.Count<innerHints.Count||fixedDoors.All(f=>doors.Any(d=>Matches(d,f)))&&components.All(c=>doors.Any(d=>Connects(d,c))));
            var inner=HallLayout.Plan(Corners(core),kit,options.Height,detail,0,false,new HallLayout.Details{SuppressRoof=true,TallWalls=true,DoorPoints=innerHints,EntranceAcceptance=Access});
            plan.InnerEntrances.AddRange(inner.Entrances);
            var unique=new HashSet<string>();var initial=plan.Parts.ToArray();plan.Parts.Clear();
            string Key(HallLayout.Part p)=>p.Prefab+":"+p.Kind+":"+p.At.X+","+p.At.Y+","+p.At.Z+":"+p.Yaw+":"+p.End.X+","+p.End.Y+","+p.End.Z;
            void Part(HallLayout.Part p)
            {
                if(string.IsNullOrEmpty(p.Prefab)||!unique.Add(Key(p)))return;
                if(plan.Parts.Count>=HallLayout.MaximumParts)throw new ArgumentException("This temple exceeds 2,048 pieces. Reduce its area or intricacy.");
                plan.Parts.Add(p);
            }
            void Add(string name,V3 at,HallLayout.Anchor kind,double yaw=0,string role="temple frame",V3 end=default)=>Part(new HallLayout.Part{Prefab=name,At=at,End=end,Kind=kind,Yaw=yaw,Role=role});
            void Rod(V3 a,V3 b,string role="temple frame")
            {
                double length=(b-a).Length;if(length<0.2)return;string name=kit.Beam;double size=kit.BeamLength;
                if(length<size){name=kit.ShortBeam;size=kit.ShortLength;}
                int n=Math.Max(1,(int)Math.Ceiling(length/size-1e-9));V3 axis=(b-a)*(1/length);
                for(int i=0;i<n;i++){V3 at=a+axis*(n==1?0:(length-size)*i/(n-1));Add(name,at,HallLayout.Anchor.Segment,0,role,at+axis*size);}
            }
            foreach(var p in initial)Part(p);
            foreach(var p in inner.Parts)if(p.Role!="floor")
            {if(p.Role=="post")p.Prefab=kit.LoadPost??kit.Post;p.Role=p.Role=="entrance"?"sanctum entrance":"sanctum "+p.Role;Part(p);}
            void Column(V3 at,double top)
            {
                if(HallDoors.Blocks(plan,at))return;
                plan.StaveSupports.Add(new V3(at.X,top,at.Z));
                for(double h=0;h<top;h+=2)Add(kit.LoadPost??kit.Post,new V3(at.X,Math.Min(h,top-2),at.Z),HallLayout.Anchor.Bottom,0,"stave");
            }
            void Walls(HallLayout.Wing w,double from,double top)
            {
                foreach(var edge in new[]{(w.At(0,0,0),w.At(w.Width*2,0,0)),(w.At(w.Width*2,0,0),w.At(w.Width*2,0,w.Length*2)),(w.At(w.Width*2,0,w.Length*2),w.At(0,0,w.Length*2)),(w.At(0,0,w.Length*2),w.At(0,0,0))})
                {
                    V3 a=edge.Item1,b=edge.Item2,axis=(b-a)*(1/(b-a).Length);double yaw=Math.Atan2(-axis.Z,axis.X)*180/Math.PI;
                    for(double v=1;v<(b-a).Length;v+=2)
                    {
                        V3 at=a+axis*v;
                        for(double y=from;y<top-0.001;)
                        {double rise=Math.Min(2,top-y);Add(rise>=1.999?kit.Wall:kit.Half,new V3(at.X,y,at.Z),HallLayout.Anchor.Bottom,yaw,"tower wall");y+=rise;}
                    }
                    Rod(a+new V3(0,top,0),b+new V3(0,top,0),"tower belt");
                }
            }
            void Roof(HallLayout.Wing w,string stage,bool overhang)
            {
                w.Stage=stage;plan.Wings.Add(w);double width=w.Width*2,length=w.Length*2,yaw=w.Across?90:0,roofBase=w.RoofBase,peak=roofBase+w.Width*kit.Slope;
                plan.RoofHeight=Math.Max(plan.RoofHeight,peak);
                bool Cut(int col,int row)
                {V3 c=w.At(col*2+1,0,row*2+1);return w.Cutout.Contains(new HallLayout.Cell((int)Math.Floor(c.X/2),(int)Math.Floor(c.Z/2)));}
                for(int row=0;row<w.Length;row++)for(int col=0;col<w.Width;col++)
                {
                    if(Cut(col,row))continue;bool center=w.Width%2==1&&col==w.Width/2,left=col<w.Width/2;
                    double low=roofBase+Math.Min(col,w.Width-1-col)*2*kit.Slope;
                    Add(center?kit.Ridge:kit.Roof,w.At(center?col*2+1:left?col*2:(col+1)*2,low,row*2+1),center?HallLayout.Anchor.Floor:HallLayout.Anchor.RoofLow,yaw+(center?90:left?270:90),stage+" roof");
                    if(center)Rod(w.At(width/2,peak,row*2),w.At(width/2,peak,row*2+2),"temple ridge frame");
                }
                for(int end=0;end<2;end++)
                {
                    double v=end*length;
                    for(int col=0;col<w.Width;col++)
                    {
                        if(Cut(col,end==0?0:w.Length-1))continue;double low=Math.Min(col,w.Width-1-col)*2*kit.Slope;
                        for(double y=0;y<low-0.001;){double rise=Math.Min(2,low-y);Add(rise>=1.999?kit.Wall:kit.Half,w.At(col*2+1,roofBase+y,v),HallLayout.Anchor.Bottom,yaw,"temple gable");y+=rise;}
                        if(w.Width%2==0||col!=w.Width/2)Add(kit.Wedge,w.At(col*2+1,roofBase+low,v),HallLayout.Anchor.Bottom,yaw+(col<w.Width/2?0:180),"temple gable");
                    }
                    if(detail>0)
                    {Rod(w.At(0,roofBase,v),w.At(width/2,peak,v),"gable timberwork");Rod(w.At(width,roofBase,v),w.At(width/2,peak,v),"gable timberwork");if(detail>=2)Rod(w.At(width/2,roofBase,v),w.At(width/2,peak,v),"gable timberwork");}
                    if(details.Sweep&&stage!="gallery"&&width>=4)foreach(bool left in new[]{true,false})
                        foreach(var segment in Curve.Plan(w.At(left?0:width,roofBase,v+(end==0?-0.18:0.18)),w.At(left?width/4:width*0.75,roofBase+(peak-roofBase)*0.3,v+(end==0?-0.18:0.18)),w.At(width/2,peak+0.6,v+(end==0?-0.18:0.18)),kit.ShortLength))Add(kit.ShortBeam,segment.Start,HallLayout.Anchor.Segment,0,"temple carved trim",segment.End);
                    if(details.Finial!=null&&stage!="gallery")Add(details.Finial,w.At(width/2,peak,v),HallLayout.Anchor.Point,yaw+(end==0?180:0),"temple ridge carving");
                }
                // Skip trusses across the tower aperture, preserving an open chamber all the way up.
                for(int bay=0;bay<=w.Length;bay+=2)
                {
                    double v=Math.Min(bay*2,length);V3 ridge=w.At(width/2,0,v);
                    if(w.Cutout.Any(c=>ridge.X>c.X*2-0.01&&ridge.X<c.X*2+2.01&&ridge.Z>c.Z*2-0.01&&ridge.Z<c.Z*2+2.01))continue;
                    Rod(w.At(0,roofBase,v),w.At(width,roofBase,v));Rod(w.At(0,roofBase,v),w.At(width/2,peak,v));Rod(w.At(width,roofBase,v),w.At(width/2,peak,v));
                    if(detail>=2&&kit.Arch!=null&&stage!="gallery"&&width>=4){Add(kit.Arch,w.At(0,roofBase-2,v),HallLayout.Anchor.Bottom,yaw,"temple carved brace");Add(kit.Arch,w.At(width,roofBase-2,v),HallLayout.Anchor.Bottom,yaw+180,"temple carved brace");}
                }
                bool ClearEave(double col,double row,double low)
                {
                    var a=w.At(col*2,0,row*2);var b=w.At(col*2+2,0,row*2+2);double x0=Math.Min(a.X,b.X),x1=Math.Max(a.X,b.X),z0=Math.Min(a.Z,b.Z),z1=Math.Max(a.Z,b.Z);
                    V3 center=(a+b)*0.5;
                    foreach(var other in plan.Wings)
                    {
                        if(center.X<=other.X*2+0.001||center.X>=(other.X+other.W)*2-0.001||center.Z<=other.Z*2+0.001||center.Z>=(other.Z+other.D)*2-0.001||other.Cutout.Contains(new HallLayout.Cell((int)Math.Floor(center.X/2),(int)Math.Floor(center.Z/2))))continue;
                        double roof=double.MinValue;
                        foreach(double x in new[]{x0,(x0+x1)/2,x1})foreach(double z in new[]{z0,(z0+z1)/2,z1})
                        {double u=other.Across?(other.Z+other.D)*2-z:x-other.X*2;roof=Math.Max(roof,other.RoofBase+Math.Min(u,other.Width*2-u)*kit.Slope);}
                        if(low<roof-0.01)return false;
                    }
                    if(details.Porch && low<sideHeight+2*kit.Slope-0.01 && plan.PorchFloors.Any(f=>x1>f.X-1+0.001&&x0<f.X+1-0.001&&z1>f.Z-1+0.001&&z0<f.Z+1-0.001))return false;
                    return true;
                }
                foreach(bool left in new[]{true,false})
                {
                    double u=left?0:width;Rod(w.At(u,roofBase,0),w.At(u,roofBase,length));
                    if(stage!="gallery")for(double v=0;v<=length;v+=2)Column(w.At(u,0,v),roofBase);
                    if(overhang)for(int row=-1;row<=w.Length;row++)
                    {
                        double outer=left?-2:width+2,v=row*2+1;if(!ClearEave(left?-1:w.Width,row,roofBase-1))continue;Add("wood_roof",w.At(outer,roofBase-1,v),HallLayout.Anchor.RoofLow,yaw+(left?270:90),"temple eave roof");
                        if(row>=0&&row<w.Length)Rod(w.At(u,roofBase-2,v),w.At(outer,roofBase-1,v),"temple eave brace");
                    }
                }
                foreach(double v in new[]{0.0,length})
                {
                    if(stage!="gallery")foreach(double u in new[]{0.0,width})Column(w.At(u,0,v),roofBase);
                    if(!overhang)continue;int row=v==0?-1:w.Length;
                    for(int col=0;col<w.Width;col++)
                    {bool center=w.Width%2==1&&col==w.Width/2,left=col<w.Width/2;double low=roofBase+Math.Min(col,w.Width-1-col)*2*kit.Slope;if(!ClearEave(col,row,low))continue;
                        Add(center?kit.Ridge:kit.Roof,w.At(center?col*2+1:left?col*2:(col+1)*2,low,row*2+1),center?HallLayout.Anchor.Floor:HallLayout.Anchor.RoofLow,yaw+(center?90:left?270:90),"temple eave roof");
                        Rod(w.At(col*2+1,low+kit.Slope,v),w.At(col*2+1,low+kit.Slope,row<0?-2:length+2),"temple eave frame");}
                }
            }
            var lowCells=plan.Cells.Where(c=>!coreCells.Contains(c)).ToList();
            if(lowCells.Count>0)foreach(var w in HallLayout.SolveWings(lowCells,kit.SpanCells)){w.RoofBase=sideHeight;Roof(w,"gallery",false);}
            var stages=new List<HallLayout.Wing>{core};core.RoofBase=options.Height;
            for(int level=0;level<options.Crowns;level++)
            {
                var parent=stages.Last();int width=parent.Width-2,length=Math.Min(parent.Length-2,width);
                if(width<1||length<1)throw new ArgumentException("The chamber is too narrow for that many tower crowns.");
                var next=new HallLayout.Wing{X=parent.X+(parent.W-(parent.Across?length:width))/2,Z=parent.Z+(parent.D-(parent.Across?width:length))/2,W=parent.Across?length:width,D=parent.Across?width:length,Across=parent.Across,RoofBase=Math.Ceiling(parent.RoofBase+parent.Width*kit.Slope+1)};
                foreach(var c in Tiles(next))parent.Cutout.Add(c);Walls(next,parent.RoofBase,next.RoofBase);stages.Add(next);
            }
            for(int i=0;i<stages.Count;i++)Roof(stages[i],i==0?"sanctum":"crown "+i,details.Overhang);
            foreach(var w in plan.Wings.Where(w=>w.Stage=="gallery"))foreach(var p in Corners(w))Column(p,sideHeight);
            return plan;
        }
    }
}
