using BuildShapes;
using System.Diagnostics;
using System.Text.Json;

int checks=0;
void Check(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
void Reject(Action action,string label){try{action();}catch(ArgumentException){checks++;return;}throw new Exception("Accepted: "+label);}
V3[] Rect(int w,int d)=>new[]{new V3(0,0,0),new V3(w,0,0),new V3(w,0,d),new V3(0,0,d)};
var l=new[]{new V3(0,0,0),new V3(8,0,0),new V3(8,0,4),new V3(4,0,4),new V3(4,0,10),new V3(0,0,10)};
var u=new[]{new V3(0,0,0),new V3(8,0,0),new V3(8,0,8),new V3(6,0,8),new V3(6,0,4),new V3(2,0,4),new V3(2,0,8),new V3(0,0,8)};
Check(HallLayout.Footprint(Rect(8,12)).Count==24,"Rectangle floor area");
Check(HallLayout.Footprint(l).Count==14,"Concave L keeps its missing corner");
Check(HallLayout.Footprint(u).Count==12,"U outline keeps its courtyard indentation");
Check(HallLayout.Footprint(l.Reverse().ToArray()).ToHashSet().SetEquals(HallLayout.Footprint(l)),"Clockwise and counterclockwise cover identical tiles");
Reject(()=>HallLayout.Footprint(Rect(3,8)),"off-grid");
Reject(()=>HallLayout.Footprint(Rect(18,18)),"area cap");
Reject(()=>HallLayout.Footprint(Rect(26,2)),"extent cap");
Reject(()=>HallLayout.Footprint(new[]{new V3(0,0,0),new V3(8,0,0),new V3(0,0,4)}),"diagonal closing edge");
Reject(()=>HallLayout.Footprint(new[]{new V3(0,0,0),new V3(4,0,0),new V3(4,0,4),new V3(2,0,4),new V3(2,0,-2),new V3(0,0,-2)}),"crossing boundary");
Reject(()=>HallLayout.Plan(Rect(8,8),new HallLayout.Kit(),5,1,0),"height bound");
Reject(()=>HallLayout.Plan(Rect(8,8),new HallLayout.Kit(),3,4,0),"intricacy bound");
var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/native-pieces.json"))).RootElement;
V3 Vector(JsonElement v)=>new(v[0].GetDouble(),v[1].GetDouble(),v[2].GetDouble());
V3 Turn(V3 v,double yaw){double a=yaw*Math.PI/180;return new V3(v.X*Math.Cos(a)+v.Z*Math.Sin(a),v.Y,-v.X*Math.Sin(a)+v.Z*Math.Cos(a));}
void Verify(V3[] outline,int height,int detail,bool steep)
{
 var kit=new HallLayout.Kit{Slope=steep?1:0.5,Roof=steep?"wood_roof_45":"wood_roof",Ridge=steep?"wood_roof_top_45":"wood_roof_top",Wedge=steep?"wood_wall_roof_45":"wood_wall_roof_a"};
 var plan=HallLayout.Plan(outline,kit,height,detail,0);
 var set=plan.Cells.ToHashSet();var cover=new HashSet<HallLayout.Cell>();
 foreach(var wing in plan.Wings)for(int z=0;z<wing.D;z++)for(int x=0;x<wing.W;x++)Check(cover.Add(new HallLayout.Cell(wing.X+x,wing.Z+z)),"Roof wings do not overlap floor tiles");
 Check(cover.SetEquals(set),"Roof wings cover exactly the concave footprint");
 Check(plan.Parts.Count(p=>p.Role=="floor")==set.Count,"Exactly one floor per tile");
 Check(plan.Parts.Count(p=>p.Role=="roof")==set.Count,"Exactly one roof tile per floor tile");
 Check(plan.Parts.Count(p=>p.Role=="entrance")==1,"One accessible entrance in the external boundary");
 Check((plan.Door-(outline[0]+outline[1])*0.5).Length<1e-8,"Entrance is centred on the chosen input edge");
 Check(plan.Parts.Where(p=>p.Role=="post").All(p=>p.At.Y+2<=height),"Native posts never protrude above odd-height walls");
 Check(plan.Parts.Count<=HallLayout.MaximumParts,"Part budget");
 Check(plan.Parts.All(p=>p.At.Finite && p.End.Finite && double.IsFinite(p.Yaw)),"Finite poses");
 foreach(var p in plan.Parts)
 {
  Check(fixture.TryGetProperty(p.Prefab,out var piece),"Native prefab exists: "+p.Prefab);
  if(p.Kind==HallLayout.Anchor.Segment)Check(Math.Abs((p.End-p.At).Length-(p.Prefab==kit.ShortBeam?kit.ShortLength:kit.BeamLength))<1e-8,"Beams retain native lengths");
  if(p.Role!="roof")continue;
  var snaps=piece.GetProperty("snaps").EnumerateObject().Select(s=>Vector(s.Value)).ToArray();
  double low=snaps.Min(v=>v.Y);var bottom=snaps.Where(v=>Math.Abs(v.Y-low)<1e-6).ToArray();
  V3 anchor=bottom.Aggregate(new V3(0,0,0),(a,b)=>a+b)*(1.0/bottom.Length);
  var transformed=snaps.Select(s=>p.At+Turn(s-anchor,p.Yaw)).ToArray();
  double minX=transformed.Min(v=>v.X),maxX=transformed.Max(v=>v.X),minZ=transformed.Min(v=>v.Z),maxZ=transformed.Max(v=>v.Z);
  Check(Math.Abs(maxX-minX-2)<0.002 && Math.Abs(maxZ-minZ-2)<0.002,"Roof native snaps cover a 2×2 tile after rotation");
  var cell=new HallLayout.Cell((int)Math.Round(minX/2),(int)Math.Round(minZ/2));
  Check(set.Contains(cell),"Roof stays inside footprint, including rotated wings");
  Check(Math.Abs(transformed.Min(v=>v.Y)-p.At.Y)<0.001,"Roof bottom edge is pinned without pivot drift");
 }
}
var watch=Stopwatch.StartNew();
foreach(var outline in new[]{Rect(8,12),Rect(6,10),Rect(12,4),l,u})
 foreach(int height in new[]{2,3,4})foreach(int detail in new[]{0,1,2,3})foreach(bool steep in new[]{false,true})Verify(outline,height,detail,steep);
var simple=HallLayout.Plan(Rect(8,12),new HallLayout.Kit(),3,0,0);
var ornate=HallLayout.Plan(Rect(8,12),new HallLayout.Kit(),3,3,0);
Check(ornate.Parts.Count>simple.Parts.Count,"Intricacy adds real pieces");
string Key(HallLayout.Part p)=>p.Prefab+":"+p.At.X+","+p.At.Y+","+p.At.Z+":"+p.Yaw;
Check(simple.Parts.Where(p=>p.Role is "floor" or "roof" or "wall" or "entrance").Select(Key).ToHashSet().SetEquals(ornate.Parts.Where(p=>p.Role is "floor" or "roof" or "wall" or "entrance").Select(Key)),"Intricacy preserves the shell");
for(int edge=0;edge<l.Length;edge++)Check(HallLayout.Plan(l,new HallLayout.Kit(),3,1,edge).Parts.Count(p=>p.Role=="entrance")==1,"Entrance can move to each input edge");
Console.WriteLine($"Passed {checks} Hallwright geometry/native-snap checks in {watch.ElapsedMilliseconds} ms.");
