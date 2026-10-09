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
void Verify(V3[] outline,int height,int detail,bool steep,bool tiered=false,bool gate=false,int entrance=0)
{
 var kit=new HallLayout.Kit{Slope=steep?1:0.5,Roof=steep?"wood_roof_45":"wood_roof",Ridge=steep?"wood_roof_top_45":"wood_roof_top",Wedge=steep?"wood_wall_roof_45":"wood_wall_roof_a"};
 if(gate){kit.Door="wood_gate";kit.DoorHeight=3;}
 var plan=HallLayout.Plan(outline,kit,height,detail,entrance,tiered);
 var set=plan.Cells.ToHashSet();var cover=new HashSet<HallLayout.Cell>();
 foreach(var wing in plan.Wings)for(int z=0;z<wing.D;z++)for(int x=0;x<wing.W;x++)Check(cover.Add(new HallLayout.Cell(wing.X+x,wing.Z+z)),"Roof wings do not overlap floor tiles");
 Check(cover.SetEquals(set),"Roof wings cover exactly the concave footprint");
 Check(plan.Parts.Count(p=>p.Role=="floor")==set.Count,"Exactly one floor per tile");
 Check(plan.Parts.Count(p=>p.Role=="roof")==set.Count,"Exactly one roof tile per floor tile");
 Check(plan.Parts.Count(p=>p.Role=="entrance")==1,"One accessible entrance in the external boundary");
 Check((plan.Door-(outline[entrance]+outline[(entrance+1)%outline.Length])*0.5).Length<1e-8,"Entrance is centred on the chosen input edge");
 Check(plan.Parts.Where(p=>p.Role=="post").All(p=>p.At.Y+2<=height),"Native posts never protrude above odd-height walls");
 Check(plan.Parts.Count<=HallLayout.MaximumParts,"Part budget");
 Check(plan.Parts.All(p=>p.At.Finite && p.End.Finite && double.IsFinite(p.Yaw)),"Finite poses");
 foreach(var p in plan.Parts)
 {
  Check(fixture.TryGetProperty(p.Prefab,out var piece),"Native prefab exists: "+p.Prefab);
  if(p.Kind==HallLayout.Anchor.Segment)Check(Math.Abs((p.End-p.At).Length-(p.Prefab==kit.ShortBeam?kit.ShortLength:kit.BeamLength))<1e-8,"Beams retain native lengths");
  if(p.Role!="roof" && p.Role!="wall" && p.Role!="entrance" && p.Role!="tier wall")continue;
  var snaps=piece.GetProperty("snaps").EnumerateObject().Select(s=>Vector(s.Value)).ToArray();
  double low=snaps.Min(v=>v.Y);var bottom=snaps.Where(v=>Math.Abs(v.Y-low)<1e-6).ToArray();
  V3 anchor=bottom.Aggregate(new V3(0,0,0),(a,b)=>a+b)*(1.0/bottom.Length);
  var transformed=snaps.Select(s=>p.At+Turn(s-anchor,p.Yaw)).ToArray();
  double minX=transformed.Min(v=>v.X),maxX=transformed.Max(v=>v.X),minZ=transformed.Min(v=>v.Z),maxZ=transformed.Max(v=>v.Z);
  if(p.Role=="entrance")Check(Math.Abs(transformed.Max(v=>v.Y)-p.At.Y-kit.DoorHeight)<0.001,"Opening preserves native 2 m door / 3 m gate height");
  if(p.Role=="wall")
  {
   V3 tangent=Turn(new V3(1,0,0),plan.DoorYaw),normal=Turn(new V3(0,0,1),plan.DoorYaw);
   if(transformed.All(v=>Math.Abs(V3.Dot(v-plan.Door,normal))<0.001))
    Check(!(transformed.Max(v=>V3.Dot(v-plan.Door,tangent))>-1+0.001 && transformed.Min(v=>V3.Dot(v-plan.Door,tangent))<1-0.001 && transformed.Min(v=>v.Y)<kit.DoorHeight-0.001),"No wall obstructs any part of entrance opening");
  }
  if(p.Role=="tier wall")Check(Math.Abs(transformed.Min(v=>v.Y)-height-2*kit.Slope)<0.001 && Math.Abs(transformed.Max(v=>v.Y)-height-2*kit.Slope-1)<0.001,"Tier wall closes the exact one-metre roof step");
  if(p.Role!="roof")continue;
  Check(Math.Abs(maxX-minX-2)<0.002 && Math.Abs(maxZ-minZ-2)<0.002,"Roof native snaps cover a 2×2 tile after rotation");
  var cell=new HallLayout.Cell((int)Math.Round(minX/2),(int)Math.Round(minZ/2));
  Check(set.Contains(cell),"Roof stays inside footprint, including rotated wings");
  var wing=plan.Wings.Single(w=>cell.X>=w.X && cell.X<w.X+w.W && cell.Z>=w.Z && cell.Z<w.Z+w.D);
  int column=wing.Across?wing.D-1-(cell.Z-wing.Z):cell.X-wing.X;
  double lift=tiered && wing.Width>=3 && column>0 && column<wing.Width-1?1:0;
  Check(Math.Abs(p.At.Y-(height+Math.Min(column,wing.Width-1-column)*2*kit.Slope+lift))<0.001,"Roof follows the gabled or tiered profile exactly");
  Check(Math.Abs(transformed.Min(v=>v.Y)-p.At.Y)<0.001,"Roof bottom edge is pinned without pivot drift");
 }
}
var watch=Stopwatch.StartNew();
foreach(var outline in new[]{Rect(8,12),Rect(6,10),Rect(12,4),l,u})
 foreach(int height in new[]{2,3,4})foreach(int detail in new[]{0,1,2,3})foreach(bool steep in new[]{false,true})foreach(bool tiered in new[]{false,true})foreach(bool gate in height>=3?new[]{false,true}:new[]{false})Verify(outline,height,detail,steep,tiered,gate);
foreach(var outline in new[]{Rect(6,10),l,l.Reverse().ToArray()})
 for(int edge=0;edge<outline.Length;edge++)foreach(int height in new[]{3,4})Verify(outline,height,2,true,true,true,edge);
Reject(()=>HallLayout.Plan(Rect(8,12),new HallLayout.Kit{Door="wood_gate",DoorHeight=3},2,1,0),"gate above wall height");
var tieredPlan=HallLayout.Plan(Rect(8,12),new HallLayout.Kit(),3,2,0,true);
Check(tieredPlan.TieredWings==1 && tieredPlan.Parts.Any(p=>p.Role=="tier post"),"Wide wing receives raised tier and structural colonnade");
Check(tieredPlan.Parts.Count(p=>p.Role=="tier wall")==12,"Both raised roof steps closed along all six bays");
Check(HallLayout.Plan(Rect(4,12),new HallLayout.Kit(),3,2,0,true).TieredWings==0,"Narrow wings remain gabled without clipped upper roof");
var simple=HallLayout.Plan(Rect(8,12),new HallLayout.Kit(),3,0,0);
var ornate=HallLayout.Plan(Rect(8,12),new HallLayout.Kit(),3,3,0);
Check(ornate.Parts.Count>simple.Parts.Count,"Intricacy adds real pieces");
string Key(HallLayout.Part p)=>p.Prefab+":"+p.At.X+","+p.At.Y+","+p.At.Z+":"+p.Yaw;
Check(simple.Parts.Where(p=>p.Role is "floor" or "roof" or "wall" or "entrance").Select(Key).ToHashSet().SetEquals(ornate.Parts.Where(p=>p.Role is "floor" or "roof" or "wall" or "entrance").Select(Key)),"Intricacy preserves the shell");
for(int edge=0;edge<l.Length;edge++)Check(HallLayout.Plan(l,new HallLayout.Kit(),3,1,edge).Parts.Count(p=>p.Role=="entrance")==1,"Entrance can move to each input edge");
// Native snap geometry is the oracle: added roofs must fit outside the original floor plan,
// preserve every core shell pose, and keep openings and stairs connected to the porch landing.
void VerifyDetails(V3[] outline,int height,bool tiered,bool steep,int edge,HallLayout.Details details)
{
 var kit=new HallLayout.Kit{Door="wood_gate",DoorHeight=3,Slope=steep?1:0.5,Roof=steep?"wood_roof_45":"wood_roof",Ridge=steep?"wood_roof_top_45":"wood_roof_top",Wedge=steep?"wood_wall_roof_45":"wood_wall_roof_a"};
 var plain=HallLayout.Plan(outline,kit,height,2,edge,tiered);
 var plan=HallLayout.Plan(outline,kit,height,2,edge,tiered,details);
 string CoreKey(HallLayout.Part p)=>Key(p)+":"+p.End.X+","+p.End.Y+","+p.End.Z;
 Check(plain.Parts.Where(p=>p.Role is "floor" or "roof" or "wall" or "entrance").Select(CoreKey).ToHashSet().SetEquals(plan.Parts.Where(p=>p.Role is "floor" or "roof" or "wall" or "entrance").Select(CoreKey)),"Details preserve the original shell and full gate opening");
 Check(plan.Cells.ToHashSet().SetEquals(plain.Cells),"Detail options do not enlarge the drawn floor plan");
 Check(plan.Parts.Count<=1024 && plan.Parts.All(p=>p.At.Finite&&p.End.Finite),"Detail output is bounded and finite");
 var extras=new List<(double x0,double x1,double z0,double z1)>();
 bool Hit((double x0,double x1,double z0,double z1) a,(double x0,double x1,double z0,double z1) b)=>a.x1>b.x0+0.001&&b.x1>a.x0+0.001&&a.z1>b.z0+0.001&&b.z1>a.z0+0.001;
 foreach(var p in plan.Parts)
 {
  Check(fixture.TryGetProperty(p.Prefab,out var piece),"Detail uses a real native prefab");
  if(p.Kind==HallLayout.Anchor.Segment)Check(Math.Abs((p.End-p.At).Length-(p.Prefab==kit.ShortBeam?kit.ShortLength:kit.BeamLength))<0.001,"Curved trim, brackets and porch beams keep native lengths");
  if(p.Role=="ridge carving")
  {
   V3 ahead=p.At+Turn(new V3(0,0,1),p.Yaw);
   Check(!plan.Cells.Contains(new HallLayout.Cell((int)Math.Floor(ahead.X/2),(int)Math.Floor(ahead.Z/2))),"Dragon carving projects outside the hall");
  }
  if(p.Role is not ("overhang roof" or "porch roof"))continue;
  var snaps=piece.GetProperty("snaps").EnumerateObject().Select(s=>Vector(s.Value)).ToArray();
  double y=snaps.Min(v=>v.Y);var low=snaps.Where(v=>Math.Abs(v.Y-y)<0.001).ToArray();
  V3 anchor=low.Aggregate(new V3(),(a,b)=>a+b)*(1.0/low.Length);
  var world=snaps.Select(s=>p.At+Turn(s-anchor,p.Yaw)).ToArray();
  var rect=(world.Min(v=>v.X),world.Max(v=>v.X),world.Min(v=>v.Z),world.Max(v=>v.Z));
  Check(Math.Abs(rect.Item2-rect.Item1-2)<0.001&&Math.Abs(rect.Item4-rect.Item3-2)<0.001,"Added roofs have exact native 2×2 coverage");
  Check(!plan.Cells.Any(c=>Hit(rect,(c.X*2,c.X*2+2,c.Z*2,c.Z*2+2))),"No added roof overlaps another wing's footprint");
  Check(!extras.Any(r=>Hit(rect,r)),"Extensions and canopy tiles never overlap each other in plan view");extras.Add(rect);
 }
 Check(plan.PorchFloors.Count==(details.Porch?2:0),"Porch has exactly two deck tiles");
 if(details.Porch)
 {
  Check(Math.Abs((plan.EntryLanding-plan.Door).Length-2)<0.001,"Stairs move to the outer porch edge");
  V3 center=plan.PorchFloors.Aggregate(new V3(),(a,b)=>a+b)*0.5;
  Check((center-(plan.Door+plan.EntryLanding)*0.5).Length<0.001,"Porch deck centres between doorway and new stair landing");
  foreach(var f in plan.PorchFloors)
   Check(plan.Vertices.Any(v=>(v-(f+Turn(new V3(1,0,1),plan.DoorYaw))).Length<0.001),"Porch corners participate in terrain and foundation sampling");
 }
 else Check((plan.EntryLanding-plan.Door).Length<0.001,"No porch leaves the original stair landing unchanged");
 Check(plan.Parts.Any(p=>p.Role=="gable trim")==details.Sweep,"Swept exposed gables appear only when selected");
 Check(plan.Parts.Any(p=>p.Role=="ridge carving")== (details.Finial!=null),"Carvings follow explicit selection");
}
foreach(var outline in new[]{Rect(8,12),Rect(6,10),l,l.Reverse().ToArray(),u})
 for(int edge=0;edge<outline.Length;edge++)
 {
  bool porch=(outline[edge]-outline[(edge+1)%outline.Length]).Length>=4;
  foreach(int height in new[]{3,4})foreach(bool steep in new[]{true,false})foreach(bool tiered in new[]{true,false})
   foreach(int mask in Enumerable.Range(0,8))
    VerifyDetails(outline,height,tiered,steep,edge,new HallLayout.Details{Porch=porch&&(mask&1)!=0,Overhang=(mask&2)!=0,Sweep=(mask&4)!=0,Finial="wood_dragon1"});
 }
Reject(()=>HallLayout.Plan(Rect(2,10),new HallLayout.Kit(),3,2,0,false,new HallLayout.Details{Porch=true}),"porch on short entrance edge");
Check(!HallLayout.Plan(Rect(8,12),new HallLayout.Kit{Raven="wood_dragon1"},3,3,0,false,new HallLayout.Details()).Parts.Any(p=>p.Role=="ridge carving"),"Explicit no carving does not fall back to legacy grand ornament");

Console.WriteLine($"Passed {checks} Hallwright geometry/native-snap checks in {watch.ElapsedMilliseconds} ms.");
