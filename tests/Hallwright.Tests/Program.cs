using Hallwright;
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
Reject(()=>HallLayout.Footprint(Rect(34,32)),"area cap");
Reject(()=>HallLayout.Footprint(Rect(66,2)),"extent cap");
Reject(()=>HallLayout.Footprint(new[]{new V3(0,0,0),new V3(8,0,0),new V3(0,0,4)}),"diagonal closing edge");
Reject(()=>HallLayout.Footprint(new[]{new V3(0,0,0),new V3(4,0,0),new V3(4,0,4),new V3(2,0,4),new V3(2,0,-2),new V3(0,0,-2)}),"crossing boundary");
Reject(()=>HallLayout.Plan(Rect(8,8),new HallLayout.Kit(),5,1,0),"height bound");
Reject(()=>HallLayout.Plan(Rect(8,8),new HallLayout.Kit(),3,5,0),"intricacy bound");
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
Check(simple.Parts.Where(p=>p.Role is "floor" or "roof" or "entrance").Select(Key).ToHashSet().SetEquals(ornate.Parts.Where(p=>p.Role is "floor" or "roof" or "entrance").Select(Key)),"Intricacy preserves floors, roof and entrance; wall panels gain windows");
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
 Check(plan.Parts.Count<=HallLayout.MaximumParts && plan.Parts.All(p=>p.At.Finite&&p.End.Finite),"Detail output is bounded and finite");
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

// Stacked floors must match the same concave cell union minus the intentional stair opening.
var largeL=new[]{new V3(0,0,0),new V3(12,0,0),new V3(12,0,6),new V3(6,0,6),new V3(6,0,12),new V3(0,0,12)};
var largeU=new[]{new V3(0,0,0),new V3(12,0,0),new V3(12,0,12),new V3(8,0,12),new V3(8,0,4),new V3(4,0,4),new V3(4,0,12),new V3(0,0,12)};
foreach(var outline in new[]{Rect(8,12),largeL,largeU,largeL.Reverse().ToArray(),largeU.Reverse().ToArray()})
foreach(int h in new[]{2,3,4})foreach(int storeys in new[]{1,2,3})foreach(bool basement in new[]{false,true})
{
 var plan=HallLayout.Plan(outline,new HallLayout.Kit(),h,1,0,true,new HallLayout.Details{Storeys=storeys,Basement=basement});
 var set=plan.Cells.ToHashSet();
 Check(plan.Storeys==storeys&&plan.StoreyHeight==h&&plan.Basement==basement,"Storey options retained");
 Check(plan.Parts.Count(p=>p.Role=="roof")==set.Count,"Multi-storey roofs exactly cover concave footprint");
 for(int level=0;level<storeys;level++)
 {
  string role=level==0?"floor":"upper floor";
  var floors=plan.Parts.Where(p=>p.Role==role&&Math.Abs(p.At.Y-level*h)<0.001).ToArray();
  var expected=(level>0||basement)?set.Except(plan.StairHoles).ToHashSet():set;
  Check(floors.Length==expected.Count,"Every storey has exact floor coverage with stair hole");
  Check(floors.Select(p=>new HallLayout.Cell((int)Math.Floor(p.At.X/2),(int)Math.Floor(p.At.Z/2))).ToHashSet().SetEquals(expected),"Upper floors preserve courtyard");
 }
 Check(plan.Parts.Count(p=>p.Role=="interior stair")==h*(storeys-1)+(basement?3:0),"Native stair rise covers all levels");
 foreach(var step in plan.Parts.Where(p=>p.Role=="interior stair"))
 {
  var snaps=fixture.GetProperty("wood_stair").GetProperty("snaps").EnumerateObject().Select(v=>Vector(v.Value)).ToArray();
  double y=snaps.Min(v=>v.Y);V3 anchor=snaps.Where(v=>Math.Abs(v.Y-y)<0.001).Aggregate(new V3(0,0,0),(a,b)=>a+b)*0.5;
  var world=snaps.Select(v=>step.At+Turn(v-anchor,step.Yaw)).ToArray();
  Check(Math.Abs(world.Max(v=>v.Y)-world.Min(v=>v.Y)-1)<0.001,"Each native stair rises exactly 1m");
  V3 centre=(world[0]+world[1]+world[2]+world[3])*0.25;
  Check(plan.StairHoles.Contains(new HallLayout.Cell((int)Math.Floor(centre.X/2),(int)Math.Floor(centre.Z/2))),"Stair flight runs under intentional headroom opening");
 }
 Check(plan.Parts.All(p=>p.At.Finite&&p.End.Finite)&&plan.Parts.Count<=HallLayout.MaximumParts,"Stacked outputs finite and bounded");
 Check(plan.Parts.Where(p=>p.Role=="storey post"||p.Role=="tier post").All(p=>!HallStairs.Blocks(plan,p.At)),"Columns leave stairs and landings clear");
 Check(plan.Parts.Count(p=>p.Role=="cellar floor")== (basement?set.Count:0),"Basement floor covers cell union");
 if(basement)
 {
  Check(plan.Parts.Count(p=>p.Role=="retaining wall")==plan.BoundaryPanels*3,"Full 3m retaining perimeter");
  Check(plan.Parts.Where(p=>p.Role=="cellar floor").All(p=>p.At.Y==-4),"Cellar stone slab sits 3m below main floor");
  foreach(var cell in plan.Cells)Check(HallExcavation.Target(plan,cell.X*2+1,cell.Z*2+1)==-4,"Excavation reaches every cellar cell");
 }
}
var courtyard=HallLayout.Plan(largeU,new HallLayout.Kit(),3,1,0,true,new HallLayout.Details{Basement=true});
Check(HallExcavation.Target(courtyard,6,10)==null,"Excavation preserves U courtyard centre");
Check(HallExcavation.Delta(100,100,92,out var digging)&&digging==-8,"Native 8m dig bound inclusive");
Check(!HallExcavation.Delta(100,100,91.99,out _),"Dig bound rejects excessive lowering");
Check(!HallExcavation.Delta(92,100,91,out _),"Previously lowered terrain still obeys original base bound");
Check(!HallExcavation.Delta(108,100,99,out _),"Hidden underlying clamp bound retained");
Reject(()=>HallLayout.Plan(Rect(4,4),new HallLayout.Kit(),3,1,0,false,new HallLayout.Details{Storeys=2}),"Small footprint cannot fit accessible stairs");
Reject(()=>HallLayout.Plan(Rect(8,12),new HallLayout.Kit(),3,1,0,false,new HallLayout.Details{Storeys=4}),"Storey cap");
var royal=HallLayout.Plan(Rect(8,12),new HallLayout.Kit{Lattice="darkwood_decowall"},3,4,0);
Check(royal.Parts.Any(p=>p.Role=="king's knotwork")&&royal.Parts.Any(p=>p.Role=="carved panel")&&royal.Parts.Any(p=>p.Role=="window frame"),"Royal style contains real native decorative work and daylight");
Console.WriteLine($"Hallwright extended: {checks:N0} checks passed");

var narrow=new V3(0.1,0.1,0.1);
Check(HallTerrainGeometry.Contact(new V3(-2,0,-2),new V3(2,0,-2),new V3(0,0,2),narrow),"Narrow post between terrain vertices contacts triangle surface");
Check(!HallTerrainGeometry.Contact(new V3(-2,1,-2),new V3(2,1,-2),new V3(0,1,2),narrow),"Buried post gains no imaginary soil support");
Check(!HallTerrainGeometry.Contact(new V3(-2,-1,-2),new V3(2,-1,-2),new V3(0,-1,2),narrow),"Floating post does not contact lower surface");
Check(!HallTerrainGeometry.Contact(new V3(1,0,1),new V3(2,0,1),new V3(1,0,2),narrow),"Nearby triangle outside narrow collider rejected");
Check(HallTerrainGeometry.Contact(new V3(-2,-1,-2),new V3(2,1,-2),new V3(0,0,2),narrow),"Sloped pit edge contact uses actual triangle plane");
Console.WriteLine($"Terrain geometry: {checks:N0} checks passed");

var tee=new[]{new V3(0,0,0),new V3(12,0,0),new V3(12,0,4),new V3(8,0,4),new V3(8,0,12),new V3(4,0,12),new V3(4,0,4),new V3(0,0,4)};
var aitch=new[]{new V3(0,0,0),new V3(4,0,0),new V3(4,0,4),new V3(8,0,4),new V3(8,0,0),new V3(12,0,0),new V3(12,0,12),new V3(8,0,12),new V3(8,0,8),new V3(4,0,8),new V3(4,0,12),new V3(0,0,12)};
var comb=new[]{new V3(0,0,0),new V3(20,0,0),new V3(20,0,8),new V3(18,0,8),new V3(18,0,2),new V3(16,0,2),new V3(16,0,8),new V3(14,0,8),new V3(14,0,2),new V3(12,0,2),new V3(12,0,8),new V3(10,0,8),new V3(10,0,2),new V3(8,0,2),new V3(8,0,8),new V3(6,0,8),new V3(6,0,2),new V3(4,0,2),new V3(4,0,8),new V3(2,0,8),new V3(2,0,2),new V3(0,0,2)};
foreach(var outline in new[]{tee,aitch,comb})foreach(var boundary in new[]{outline,outline.Reverse().ToArray()})foreach(bool tiered in new[]{false,true})
{
 var plan=HallLayout.Plan(boundary,new HallLayout.Kit(),3,2,0,tiered);
 var cover=new HashSet<HallLayout.Cell>();
 foreach(var wing in plan.Wings)for(int z=0;z<wing.D;z++)for(int x=0;x<wing.W;x++)Check(cover.Add(new HallLayout.Cell(wing.X+x,wing.Z+z)),"Complex roof cover never overlaps");
 Check(cover.SetEquals(plan.Cells),"T/H/22-corner comb roofs exactly cover complex footprint");
 Check(plan.Parts.Count(p=>p.Role=="floor")==plan.Cells.Count&&plan.Parts.Count(p=>p.Role=="roof")==plan.Cells.Count,"Complex footprint keeps exact floor and roof cells");
 Check(plan.Wings.Count<=12&&plan.Parts.Count<=HallLayout.MaximumParts,"Complex search remains bounded");
}
foreach(var outline in new[]{tee,aitch})foreach(int edge in Enumerable.Range(0,outline.Length))
{
 var plan=HallLayout.Plan(outline,new HallLayout.Kit(),3,1,edge,true,new HallLayout.Details{Storeys=2,Basement=true});
 V3 entry=plan.Door+Turn(new V3(0,0,1.5),plan.DoorYaw);
 Check(!plan.StairHoles.Any(c=>Math.Abs(entry.X-(c.X*2+1))<1.4&&Math.Abs(entry.Z-(c.Z*2+1))<1.4),"Interior stair solver preserves every selected entrance");
}
Console.WriteLine($"Complex footprints: {checks:N0} checks passed");

// Player marks are hints; native openings and retained wall spans are the geometric oracle.
foreach(bool reverse in new[]{false,true})foreach(int height in new[]{2,3,4})foreach(int storeys in new[]{1,2})foreach(bool basement in new[]{false,true})
{
 var outline=Rect(8,12);if(reverse)Array.Reverse(outline);
 var hints=new[]{new V3(2.2,0,-0.2),new V3(8.2,0,8.1),new V3(5.7,0,12.2)};
 var kit=new HallLayout.Kit{Door=height>=3?"wood_gate":"wood_door",DoorHeight=height>=3?3:2};
 var plan=HallLayout.Plan(outline,kit,height,4,0,true,new HallLayout.Details{Storeys=storeys,Basement=basement,DoorPoints=hints});
 Check(plan.Entrances.Count==3&&plan.Parts.Count(p=>p.Role=="entrance")==3,"Each marked entrance creates exactly one native opening");
 for(int i=0;i<hints.Length;i++)Check((plan.Entrances[i].At-hints[i]).Length<0.6,"Approximate hints snap to nearby metre wall positions");
 foreach(var door in plan.Entrances)
 {
  V3 inside=door.At+Turn(new V3(0,0,0.25),door.Yaw),outside=door.At+Turn(new V3(0,0,-0.25),door.Yaw);
  Check(HallLayout.Inside(outline,inside.X,inside.Z)&&!HallLayout.Inside(outline,outside.X,outside.Z),"Door orientation faces outward for either winding");
  V3 entry=door.At+Turn(new V3(0,0,1.5),door.Yaw);
  Check(!plan.StairHoles.Any(c=>Math.Abs(entry.X-c.X*2-1)<1.4&&Math.Abs(entry.Z-c.Z*2-1)<1.4),"Every entrance keeps clear interior stair access");
  foreach(var wall in plan.Parts.Where(p=>p.Role=="wall"))
  {
   var snaps=fixture.GetProperty(wall.Prefab).GetProperty("snaps").EnumerateObject().Select(x=>Vector(x.Value)).ToArray();
   double low=snaps.Min(v=>v.Y);var bottom=snaps.Where(v=>Math.Abs(v.Y-low)<1e-6).ToArray();
   V3 anchor=bottom.Aggregate(new V3(0,0,0),(a,b)=>a+b)*(1.0/bottom.Length);
   var actual=snaps.Select(v=>Turn(wall.At+Turn(v-anchor,wall.Yaw)-door.At,-door.Yaw)).ToArray();
   if(actual.All(v=>Math.Abs(v.Z)<0.001))Check(!(actual.Min(v=>v.Y)<kit.DoorHeight-0.001&&actual.Max(v=>v.X)>-1+0.001&&actual.Min(v=>v.X)<1-0.001),"Wall infill leaves each full native door opening clear");
  }
  Check(plan.Parts.Where(p=>p.Role is "post" or "storey post" or "tier post").All(p=>!HallDoors.Blocks(plan,p.At)||p.At.Y+2<=0||p.At.Y>=kit.DoorHeight),"Load posts leave doorway height clear including cellar overlaps");
  if(basement)
  {
   V3 apron=door.At+Turn(new V3(0,0,-3),door.Yaw);
   Check(HallExcavation.Target(plan,apron.X,apron.Z)==-0.15,"Every cellar entrance has a level ground apron");
   Check((door.Landing-door.At).Length>1.99,"Every cellar entrance has an external landing");
  }
 }
}
var concaveHints=new[]{new V3(2.1,0,-0.2),new V3(4.1,0,7.0)};
foreach(var boundary in new[]{l,l.Reverse().ToArray()})
{
 var plan=HallLayout.Plan(boundary,new HallLayout.Kit(),3,1,0,false,new HallLayout.Details{DoorPoints=concaveHints});
 Check(plan.Entrances.Count==2&&plan.Entrances[1].Yaw==270,"Concave courtyard wall accepts a second outward-facing entrance");
}
var sharing=HallDoors.Solve(Rect(8,12),new[]{new V3(3,0,0),new V3(3.5,0,0)},0,false);
Check((sharing[0].At-sharing[1].At).Length>=3,"Joint fit separates competing hints instead of overlapping openings");
var porchFit=HallDoors.Solve(Rect(8,12),new[]{new V3(0.5,0,0),new V3(8,0,8)},0,true);
Check(porchFit[0].At.X>=2&&porchFit[0].At.X<=6,"Primary entrance solve reserves the full porch span");
Reject(()=>HallDoors.Solve(Rect(8,12),new[]{new V3(30,0,30)},0,false),"Distant entrance hint");
Reject(()=>HallDoors.Solve(Rect(8,12),Enumerable.Repeat(new V3(2,0,0),9).ToArray(),0,false),"Entrance cap");
Reject(()=>HallDoors.Solve(Rect(8,12),new[]{new V3(double.NaN,0,0)},0,false),"Nonfinite entrance hint");
Console.WriteLine($"Marked entrances: {checks:N0} checks passed");

var tightHints=new[]{new V3(0,0,5),new V3(4,0,5)};
var naiveDoors=HallDoors.Solve(Rect(4,10),tightHints,0,false);
var narrowHall=new HallLayout.Design();narrowHall.Cells.AddRange(HallLayout.Footprint(Rect(4,10)));narrowHall.Entrances.AddRange(naiveDoors);
Reject(()=>HallStairs.Solve(narrowHall,3),"Nearest independent openings consume both narrow-wing stair routes");
var fitted=HallLayout.Plan(Rect(4,10),new HallLayout.Kit(),3,1,0,false,new HallLayout.Details{Storeys=2,DoorPoints=tightHints});
Check(fitted.Entrances.Count==2&&fitted.StairHoles.Count==3,"Joint door fit finds a stair route where nearest openings cannot");
Check(fitted.Entrances.Any(d=>d.At.Z<=1||d.At.Z>=9),"Door hint can move to a landing bay to preserve interior stairs");
Check(fitted.Entrances.Zip(tightHints,(door,hint)=>(door.At-hint).Length).All(distance=>distance<=4),"Stair-aware fit stays within the hint tolerance");
Console.WriteLine($"Joint entrances and stairs: {checks:N0} checks passed");

// Use captured native snap endpoints, not the helper's yaw, as the exterior-stair oracle.
var exteriorSnaps=fixture.GetProperty("wood_stair").GetProperty("snaps").EnumerateObject().Select(s=>Vector(s.Value)).ToArray();
double exteriorLowY=exteriorSnaps.Min(v=>v.Y),exteriorHighY=exteriorSnaps.Max(v=>v.Y);
V3 SnapMean(IEnumerable<V3> points){var all=points.ToArray();return all.Aggregate(new V3(0,0,0),(a,b)=>a+b)*(1.0/all.Length);}
var nativeBottom=SnapMean(exteriorSnaps.Where(v=>Math.Abs(v.Y-exteriorLowY)<1e-6));
var nativeTop=SnapMean(exteriorSnaps.Where(v=>Math.Abs(v.Y-exteriorHighY)<1e-6));
foreach(double yaw in new[]{0.0,90,180,270})foreach(double hallYaw in new[]{0.0,37})
{
 var entrance=new HallDoors.Entrance{At=new V3(4,0,0),Landing=new V3(4,0,-2),Yaw=yaw};
 V3 previousLow=Turn(entrance.Landing,hallYaw);
 for(int i=0;i<6;i++)
 {
  var stair=HallStairs.Exterior(entrance,i);
  V3 origin=Turn(stair.At,hallYaw)-Turn(nativeBottom,stair.Yaw+hallYaw);
  V3 actualTop=origin+Turn(nativeTop,stair.Yaw+hallYaw),actualBottom=origin+Turn(nativeBottom,stair.Yaw+hallYaw);
  Check((actualTop-previousLow).Length<1e-6,"Exterior stair top joins the porch/door landing or the preceding stair bottom");
  Check(Math.Abs(actualTop.Y-actualBottom.Y-1)<1e-6,"Native exterior stair rises exactly one metre toward the door");
  V3 expectedOutward=Turn(new V3(0,0,-2),yaw+hallYaw);
  Check(((actualBottom-actualTop)-expectedOutward-new V3(0,-1,0)).Length<1e-6,"Exterior stair descends outward for every wall and rotated hall");
  previousLow=actualBottom;
 }
}
Console.WriteLine($"Exterior native stair endpoints: {checks:N0} checks passed");

var expandedRoyal=HallLayout.Plan(Rect(8,16),new HallLayout.Kit{Lattice="darkwood_decowall",LoadPost="woodiron_pole"},3,4,0,true,new HallLayout.Details{Storeys=3,Basement=true,Sweep=true,Finial="wood_dragon1"});
Check(expandedRoyal.Parts.Count>1024&&expandedRoyal.Parts.Count<=2048,"Large three-storey royal cellar shell exceeds the old cap and fits the new bound");
Console.WriteLine($"Expanded royal shell: {expandedRoyal.Parts.Count} pieces; {checks:N0} checks passed");
Reject(()=>HallLayout.Plan(Rect(16,16),new HallLayout.Kit{Lattice="darkwood_decowall",LoadPost="woodiron_pole"},3,4,0,true,new HallLayout.Details{Storeys=3,Basement=true,Sweep=true,Finial="wood_dragon1"}),"Very large royal shell still respects the 2048 bound");


// Exercise the expanded boundary and actual roofs, rather than accepting only larger markers.
Check(HallLayout.Footprint(Rect(32,32)).Count==256,"A 1024 square-metre floor plan reaches the new area boundary");
Check(HallLayout.Footprint(Rect(8,64)).Count==128,"A 64 metre longhouse reaches the new extent boundary");
foreach(var (outline,intricacy,tiered) in new[]{(Rect(16,24),2,true),(Rect(32,32),0,false),(Rect(8,64),2,true)})
{
 var largeWatch=Stopwatch.StartNew();
 Verify(outline,3,intricacy,true,tiered,true);
 var wings=HallLayout.SolveWings(HallLayout.Footprint(outline),4);
 Check(wings.All(w=>w.Width<=4),"Larger roofs retain the timber support-span limit");
 Console.WriteLine($"Grand footprint {outline[2].X} x {outline[2].Z} m: native geometry validated in {largeWatch.ElapsedMilliseconds} ms.");
}
var longWings=HallLayout.SolveWings(HallLayout.Footprint(Rect(8,64)),4);
Check(longWings.Count==1&&longWings[0].Length==32,"A longhouse receives one continuous 64 metre ridge instead of artificial 24 metre roof sections");
var grandU=new[]{new V3(0,0,0),new V3(24,0,0),new V3(24,0,32),new V3(16,0,32),new V3(16,0,8),new V3(8,0,8),new V3(8,0,32),new V3(0,0,32)};
Verify(grandU,3,2,true,true,true);
Verify(grandU.Reverse().ToArray(),3,2,true,true,true);
Check(HallLayout.Footprint(grandU.Reverse().ToArray()).ToHashSet().SetEquals(HallLayout.Footprint(grandU)),"Grand concave courtyard accepts both boundary directions");
var grandRoyal=HallLayout.Plan(Rect(12,32),new HallLayout.Kit{Lattice="darkwood_decowall",LoadPost="woodiron_pole"},3,4,0,true,new HallLayout.Details{Sweep=true,Finial="wood_dragon1"});
Check(grandRoyal.Cells.Count==96&&grandRoyal.Parts.Count<=2048,"384 square-metre royal hall generates within the independent piece budget");
Console.WriteLine($"Grand royal hall: {grandRoyal.Parts.Count} pieces; {checks:N0} checks passed.");

var signedLong=new[]{new V3(0,0,0),new V3(64,0,0),new V3(64,0,4),new V3(-64,0,4),new V3(-64,0,0)};
Verify(signedLong,3,0,true,false,true);
var signedWings=HallLayout.SolveWings(HallLayout.Footprint(signedLong),4);
Check(signedWings.Count==1&&signedWings[0].Length==64,"Negative and positive extent bounds support a continuous ridge across the full signed span");
Console.WriteLine($"Signed extent and grand courtyard roofs: {checks:N0} checks passed.");

// Shuttered windows: native shutter pairs fill each 2 × 1 m opening exactly, at every intricacy, and only when unlocked.
var shutterKit=new HallLayout.Kit{Window="wood_window"};
var shutterSnaps=fixture.GetProperty("wood_window").GetProperty("snaps").EnumerateObject().Select(s=>Vector(s.Value)).ToArray();
V3 shutterAnchor=shutterSnaps.Where(v=>Math.Abs(v.Y-shutterSnaps.Min(w=>w.Y))<1e-6).Aggregate(new V3(0,0,0),(a,b)=>a+b)*0.5;
foreach(int intricacy in new[]{0,2,4})foreach(int wallHeight in new[]{2,3,4})
{
 var shuttered=HallLayout.Plan(Rect(8,12),shutterKit,wallHeight,intricacy,0,false,new HallLayout.Details{Shutters=true});
 var shutters=shuttered.Parts.Where(p=>p.Role=="window shutter").ToList();
 Check(shutters.Count>0 && shutters.Count%2==0,"Shuttered windows come in pairs at every intricacy and wall height");
 Check(!shuttered.Parts.Any(p=>p.Role=="window frame"),"Shutters replace the open daylight frames");
 foreach(var pair in shutters.GroupBy(p=>Math.Round(p.At.Y,3)+":"+Math.Round(p.At.X+p.At.Z-(p.At.X+p.At.Z)%2,3)))
 {
  var spans=shutters.Where(p=>Math.Abs(p.At.Y-pair.First().At.Y)<1e-6).Select(p=>shutterSnaps.Select(s=>p.At+Turn(s-shutterAnchor,p.Yaw)).ToArray()).ToList();
  Check(spans.All(s=>Math.Abs(s.Min(v=>v.Y)-pair.First().At.Y)<1e-6 && Math.Abs(s.Max(v=>v.Y)-pair.First().At.Y-1)<1e-6),"Each shutter fills one metre of height above the sill");
 }
 foreach(var p in shutters)
 {
  var s=shutterSnaps.Select(v=>p.At+Turn(v-shutterAnchor,p.Yaw)).ToArray();
  double span=Math.Max(s.Max(v=>v.X)-s.Min(v=>v.X),s.Max(v=>v.Z)-s.Min(v=>v.Z));
  Check(Math.Abs(span-1)<1e-6,"A native shutter spans one metre of its opening");
  Check(shutters.Count(q=>q!=p && (q.At-p.At).Length<1.0001 && Math.Abs(q.At.Y-p.At.Y)<1e-6 && Math.Abs(Math.Abs(q.Yaw-p.Yaw)-180)<1e-6)==1,"Each shutter has a mirrored partner across the opening");
 }
}
Check(!HallLayout.Plan(Rect(8,12),new HallLayout.Kit(),3,2,0,false,new HallLayout.Details{Shutters=true}).Parts.Any(p=>p.Role=="window shutter"),"Locked shutters leave the walls as before");
Check(!HallLayout.Plan(Rect(8,12),shutterKit,3,2,0,false,new HallLayout.Details{Shutters=false}).Parts.Any(p=>p.Role=="window shutter"),"Shutters can be switched off");

// Chimneys: the roof tile above the marked floor tile opens, a timber flue rises past the ridge, and nothing crosses the flue.
void VerifyChimney(V3[] outline,V3 at,bool steep,bool tiered,int storeys,string label)
{
 var kit=new HallLayout.Kit{Slope=steep?1:0.5,Roof=steep?"wood_roof_45":"wood_roof",Ridge=steep?"wood_roof_top_45":"wood_roof_top",Wedge=steep?"wood_wall_roof_45":"wood_wall_roof_a"};
 var plain=HallLayout.Plan(outline,kit,3,2,0,tiered,new HallLayout.Details{Storeys=storeys});
 var plan=HallLayout.Plan(outline,kit,3,2,0,tiered,new HallLayout.Details{Storeys=storeys,ChimneyPoints=new[]{at}});
 var cell=new HallLayout.Cell((int)Math.Floor(at.X/2),(int)Math.Floor(at.Z/2));
 Check(plan.Chimneys.Count==1 && plan.Chimneys.Contains(cell),label+": chimney sits on its marked tile");
 Check(plan.Parts.Count(p=>p.Role=="roof")==plain.Parts.Count(p=>p.Role=="roof")-1,label+": exactly one roof tile opens");
 bool InTile(V3 p,double margin)=>p.X>cell.X*2+margin && p.X<cell.X*2+2-margin && p.Z>cell.Z*2+margin && p.Z<cell.Z*2+2-margin;
 var kept=plan.Parts.Where(p=>p.Role=="roof").Select(Key).ToHashSet();var removed=plain.Parts.Where(p=>p.Role=="roof" && !kept.Contains(Key(p))).ToList();
 Check(removed.Count==1 && removed[0].At.X>=cell.X*2-1e-6 && removed[0].At.X<=cell.X*2+2+1e-6 && removed[0].At.Z>=cell.Z*2-1e-6 && removed[0].At.Z<=cell.Z*2+2+1e-6,label+": the opened roof tile is the one above the chimney");
 Check(!plan.Parts.Any(p=>p.Role=="upper floor" && InTile(p.At,0.1)),label+": no upper floor covers the flue");
 var flue=plan.Parts.Where(p=>p.Role=="chimney").ToList();
 Check(flue.Max(p=>p.At.Y)>=plan.RoofHeight+0.8,label+": the flue rises past the ridge");
 Check(flue.All(p=>fixture.TryGetProperty(p.Prefab,out _)),label+": flue uses native pieces");
 foreach(var p in plan.Parts.Where(p=>p.Kind==HallLayout.Anchor.Segment && p.Role!="chimney"))
  for(int i=0;i<=10;i++){V3 q=p.At+(p.End-p.At)*(i/10.0);Check(!(InTile(q,0.25) && q.Y>=3-0.001),label+": no beam crosses the open flue");}
 var walls=flue.Where(p=>p.Prefab=="wood_wall_half").ToList();
 foreach(var side in new[]{0,1,2,3})Check(walls.Any(w=>side switch{0=>Math.Abs(w.At.X-cell.X*2)<1e-6,1=>Math.Abs(w.At.X-cell.X*2-2)<1e-6,2=>Math.Abs(w.At.Z-cell.Z*2)<1e-6,_=>Math.Abs(w.At.Z-cell.Z*2-2)<1e-6}),label+": all four flue sides are walled");
 if(storeys>1)Check(flue.Any(p=>p.At.Y<3*storeys-0.001),label+": a shaft carries the flue through the upper floors");
 Check(plan.Parts.Count<=HallLayout.MaximumParts,label+": part budget");
}
VerifyChimney(Rect(8,12),new V3(3,0,5),true,false,1,"Even wing beside the ridge");
VerifyChimney(Rect(8,12),new V3(1,0,5),true,false,1,"Even wing at the eave");
VerifyChimney(Rect(10,12),new V3(5,0,5),true,false,1,"Odd wing on the ridge");
VerifyChimney(Rect(10,12),new V3(5,0,5),false,false,1,"Low pitch on the ridge");
VerifyChimney(Rect(8,12),new V3(3,0,1),true,false,1,"Gable end");
VerifyChimney(Rect(10,12),new V3(3,0,7),true,true,1,"Tiered wing");
VerifyChimney(Rect(12,8),new V3(5,0,3),true,false,1,"Across wing");
VerifyChimney(l,new V3(1,0,7),true,false,1,"Concave wing");
var storeyPlan=HallLayout.Plan(Rect(8,16),new HallLayout.Kit(),3,2,0,false,new HallLayout.Details{Storeys=2});
var freeCell=storeyPlan.Cells.First(c=>!storeyPlan.StairZone.Contains(c) && !storeyPlan.StairHoles.Contains(c) && c.X>0 && c.Z>0);
VerifyChimney(Rect(8,16),new V3(freeCell.X*2+1,0,freeCell.Z*2+1),true,false,2,"Two storeys");
var stairCell=storeyPlan.StairHoles.First();
Reject(()=>HallLayout.Plan(Rect(8,16),new HallLayout.Kit(),3,2,0,false,new HallLayout.Details{Storeys=2,ChimneyPoints=new[]{new V3(stairCell.X*2+1,0,stairCell.Z*2+1)}}),"chimney over the stairs");
Reject(()=>HallLayout.Plan(Rect(8,12),new HallLayout.Kit(),3,2,0,false,new HallLayout.Details{ChimneyPoints=new[]{new V3(9,0,5)}}),"chimney outside the floor plan");
Reject(()=>HallLayout.Plan(Rect(16,16),new HallLayout.Kit(),3,2,0,false,new HallLayout.Details{ChimneyPoints=Enumerable.Range(0,5).Select(i=>new V3(i*2+1,0,5)).ToArray()}),"five chimneys");
var chimneyPlan=HallLayout.Plan(Rect(8,12),new HallLayout.Kit(),3,2,0,false,new HallLayout.Details{ChimneyPoints=new[]{new V3(3,0,5)}});
Check(HallLayout.ChimneyBlocks(chimneyPlan,new V3(3,0,5)) && HallLayout.ChimneyBlocks(chimneyPlan,new V3(4,0,5)) && !HallLayout.ChimneyBlocks(chimneyPlan,new V3(2,0,4)) && !HallLayout.ChimneyBlocks(chimneyPlan,new V3(7,0,5)),"Support columns avoid the flue but may share its corners");
Console.WriteLine($"Shutters and chimneys: {checks:N0} checks passed.");

// Norse longhouse: hearths down the middle aisle with ridge vents beside them, platforms along the long walls, aisle posts, no low ties.
var norseKit=new HallLayout.Kit{SmallFloor="wood_floor_1x1",SpanCells=5};
HallLayout.Details Norse(bool benches=true,bool hearth=true)=>new HallLayout.Details{Norse=true,Benches=benches,Hearth=hearth?"hearth":null};
var hearthSnaps=fixture.GetProperty("hearth").GetProperty("snaps").EnumerateObject().Select(s=>Vector(s.Value)).Where(v=>v.Y==0).ToArray();
(double x0,double x1,double z0,double z1) Bed(HallLayout.Part p){var c=hearthSnaps.Select(s=>p.At+Turn(s,p.Yaw)).ToArray();return (c.Min(v=>v.X),c.Max(v=>v.X),c.Min(v=>v.Z),c.Max(v=>v.Z));}
bool Hits((double x0,double x1,double z0,double z1) a,(double x0,double x1,double z0,double z1) b)=>a.x1>b.x0+0.001 && b.x1>a.x0+0.001 && a.z1>b.z0+0.001 && b.z1>a.z0+0.001;
void VerifyNorse(V3[] outline,int entrance,int depth,int hearths,string label)
{
 var plan=HallLayout.Plan(outline,norseKit,2,2,entrance,false,Norse());
 var wing=plan.Wings.Single();double width=wing.Width*2,length=wing.Length*2;
 // Positions across (u) and along (v) the wing.
 double U(V3 p)=>wing.Across?wing.Z*2+wing.D*2-p.Z:p.X-wing.X*2;
 double Along(V3 p)=>wing.Across?p.X-wing.X*2:p.Z-wing.Z*2;
 Check(plan.Norse && plan.Hearths==hearths && plan.Parts.Count(p=>p.Role=="hearth")==hearths,label+": hearth count");
 foreach(var h in plan.Parts.Where(p=>p.Role=="hearth"))
 {
  var bed=Bed(h);
  Check(Math.Abs((wing.Across?bed.x1-bed.x0:bed.z1-bed.z0)-4)<1e-6,label+": the hearth's 4 m runs along the hall");
  Check(Math.Abs(U(h.At)-width/2)<1e-6 && Math.Abs(h.At.Y)<1e-9,label+": hearth on the floor in the middle aisle");
  Check(!plan.Chimneys.Any(c=>Hits(bed,(c.X*2,c.X*2+2,c.Z*2,c.Z*2+2))),label+": no vent opens above a hearth");
  Check(plan.Chimneys.Count(c=>Math.Abs(Along(new V3(c.X*2+1,0,c.Z*2+1))-Along(h.At))<3.01)==2,label+": a vent beside each end of the hearth");
  Check(Math.Min(U(new V3(bed.x0,0,bed.z0)),U(new V3(bed.x1,0,bed.z1)))-depth>=1.5-1e-6,label+": 1.5 m to walk between hearth and platform");
 }
 var floors=plan.Parts.Where(p=>p.Role=="platform").ToList();
 if(depth==0){Check(floors.Count==0,label+": no platforms");}
 else
 {
  Check(floors.All(p=>Math.Abs(p.At.Y-0.5)<1e-9 && (U(p.At)<depth || U(p.At)>width-depth)),label+": platforms 0.5 m up along the long walls");
  Check(floors.All(p=>p.Prefab==(depth==2?"wood_floor":"wood_floor_1x1")),label+": platform boards fit the depth");
  foreach(var d in plan.Entrances)Check(!floors.Any(p=>(new V3(p.At.X,0,p.At.Z)-d.At).Length<1.5),label+": the door opens onto the aisle, not a platform");
  Check(plan.Parts.Where(p=>p.Role=="platform front").All(p=>Math.Abs(p.At.Y+0.5)<1e-9),label+": platform fronts reach below the floor");
  double perSide=floors.Count/(depth==2?1.0:2.0);
  Check(perSide>=2*wing.Length-plan.Entrances.Count-plan.Chimneys.Count*2,label+": platforms run the length of both walls");
 }
 Check(plan.Entrances.Count==1 && Math.Abs(Along(plan.Entrances[0].At)-(wing.Across?length-3:3))<1e-6 || Math.Abs(Along(plan.Entrances[0].At)-3)<1e-6,label+": the door is near one end of the long wall");
 // Headroom: no level beam crosses the aisle below 3 m (no tie beams at the wall plate); rafters rise from the walls.
 foreach(var p in plan.Parts.Where(p=>p.Kind==HallLayout.Anchor.Segment && Math.Abs(p.End.Y-p.At.Y)<0.01))
  for(int i=0;i<=8;i++){V3 q=p.At+(p.End-p.At)*(i/8.0);double u=U(q);Check(!(u>depth+0.3 && u<width-depth-0.3 && q.Y<3 && Along(q)>0.3 && Along(q)<length-0.3),label+": open headroom over the aisle");}
 if(wing.Width>=4)Check(plan.Parts.Count(p=>p.Role=="aisle post")>=4 && plan.Parts.Where(p=>p.Role=="aisle post").All(p=>Math.Abs(U(p.At)-(depth>0?depth:2))<1e-6 || Math.Abs(U(p.At)-width+(depth>0?depth:2))<1e-6),label+": two rows of aisle posts");
 Check(!plan.Parts.Any(p=>p.Role=="window frame"),label+": no open daylight windows");
 Check(plan.Parts.All(p=>fixture.TryGetProperty(p.Prefab,out _)),label+": native pieces");
 Check(plan.Parts.Count<=HallLayout.MaximumParts && plan.Parts.All(p=>p.At.Finite && p.End.Finite),label+": bounded and finite");
}
VerifyNorse(Rect(10,20),1,2,2,"10 m longhouse");
VerifyNorse(Rect(8,16),1,1,1,"8 m longhouse");
VerifyNorse(Rect(8,24),1,1,3,"Long 8 m longhouse");
VerifyNorse(Rect(6,16),1,0,1,"6 m longhouse");
VerifyNorse(Rect(20,10),0,2,2,"Across longhouse");
// Plain wood spans 8 m in a hall, but a longhouse keeps one roof over its full 10 m, with the hearths down the middle.
foreach(var outline in new[]{Rect(10,20),Rect(20,10),Rect(10,30)})
{
 var plainPlan=HallLayout.Plan(outline,new HallLayout.Kit{SmallFloor="wood_floor_1x1"},2,2,1,false,Norse());
 var plainWing=plainPlan.Wings.Single();
 Check(plainWing.Width==5 && plainPlan.Hearths>=2,"Plain-wood 10 m longhouse: one roof wing");
 foreach(var h in plainPlan.Parts.Where(p=>p.Role=="hearth"))Check(Math.Abs((plainWing.Across?h.At.Z-plainWing.Z*2:h.At.X-plainWing.X*2)-5)<1e-6,"Plain-wood 10 m longhouse: hearths on the centre line");
}
Check(HallLayout.Plan(Rect(10,20),new HallLayout.Kit(),3,2,1).Wings.Count==2,"A plain-wood 10 m hall still splits its roof at 8 m");
Check(HallLayout.Plan(Rect(6,16),norseKit,2,2,1,false,Norse(hearth:false)).Parts.Count(p=>p.Role=="platform")>0,"Without a hearth a 6 m hall still gets platforms");
Check(!HallLayout.Plan(Rect(8,16),new HallLayout.Kit(),2,2,1,false,Norse()).Parts.Any(p=>p.Role=="platform"),"1 m platforms wait for the 1×1 floor");
Check(!HallLayout.Plan(Rect(10,20),norseKit,2,2,1,false,Norse(benches:false)).Parts.Any(p=>p.Role.StartsWith("platform")),"Platforms can be switched off");
Check(HallLayout.Plan(Rect(10,20),norseKit,2,2,1,false,Norse(hearth:false)).Chimneys.Count==0,"No hearth, no vents");
Check(HallLayout.Plan(Rect(10,20),norseKit,2,4,1,false,new HallLayout.Details{Norse=true,Hearth="hearth",ChimneyPoints=new[]{new V3(5,0,5)}}).Hearths==1,"A hearth never sits under a marked chimney");
Check(HallLayout.Plan(Rect(10,20),norseKit,3,4,1,false,Norse()).Parts.Count(p=>p.Role=="window frame")==0,"Ornate longhouses stay dark inside");
Check(HallLayout.Plan(Rect(10,20),norseKit,2,2,1,true,Norse()).TieredWings==1,"A longhouse can still have a tiered roof");
Reject(()=>HallLayout.Plan(Rect(10,20),norseKit,2,2,1,false,new HallLayout.Details{Norse=true,Storeys=2}),"two-storey longhouse");
Reject(()=>HallLayout.Plan(Rect(10,20),norseKit,2,2,1,false,new HallLayout.Details{Norse=true,Porch=true}),"longhouse porch");
Reject(()=>HallLayout.Plan(Rect(10,20),norseKit,2,2,1,false,new HallLayout.Details{Norse=true,Basement=true}),"longhouse basement");
// Its ground: dug floor and the strip just outside the walls, ramps to each door, only ever dug, nothing beyond.
var dug=HallLayout.Plan(Rect(10,20),norseKit,2,2,1,false,Norse());
var dugDoor=dug.Entrances[0];V3 Out(double along,double out_)=>dugDoor.At+Turn(new V3(along,0,-out_),dugDoor.Yaw);
Check(HallExcavation.Norse(dug,5,10,1)==(-0.15,-1),"The floor is dug just below its boards");
Check(HallExcavation.Norse(dug,-1,10,1)==(-0.15,-1) && HallExcavation.Norse(dug,5,21.4,1)==(-0.15,-1),"The soil stays below the floor just outside the walls");
Check(HallExcavation.Norse(dug,-2,10,1)==null && HallExcavation.Norse(dug,-7,10,1)==null,"Ground beyond the strip is untouched");
Check(HallExcavation.Norse(dug,5,10,0)==null && HallExcavation.Norse(dug,-2,10,0)==null,"An undug longhouse leaves the ground alone");
for(double x=-8;x<=18;x+=0.5)for(double z=-8;z<=28;z+=0.5){var g=HallExcavation.Norse(dug,x,z,1);Check(g==null || g.Value.mode<0,"A longhouse only ever digs, never heaps earth up");}
var r1=HallExcavation.Norse(dug,Out(0,1).X,Out(0,1).Z,1);var r2=HallExcavation.Norse(dug,Out(0,4).X,Out(0,4).Z,1);var r3=HallExcavation.Norse(dug,Out(0,8).X,Out(0,8).Z,1);
Check(r1==(-0.15,-1) && r2.HasValue && r3.HasValue && r2.Value.mode<0 && r2.Value.y>r1.Value.y && r3.Value.y>r2.Value.y,"A ramp climbs from each door to the old ground");
Check(HallExcavation.Settle(3,1,-1)==1 && HallExcavation.Settle(0,1,-1)==0 && HallExcavation.Settle(0,1,1)==1 && HallExcavation.Settle(3,1,1)==3 && HallExcavation.Settle(3,1,0)==1,"Digging only digs, heaping only heaps");
// Doorways stay clear to the top of the opening plus a beam's half-thickness, for walls as tall as the door.
bool Blocked(HallLayout.Design plan,double door)=>plan.Parts.Where(p=>p.Kind==HallLayout.Anchor.Segment).Any(p=>Enumerable.Range(0,9).Select(i=>p.At+(p.End-p.At)*(i/8.0)).Any(q=>plan.Entrances.Any(d=>{V3 l=Turn(q-d.At,-d.Yaw);return Math.Abs(l.X)<0.99 && Math.Abs(l.Z)<0.35 && q.Y>-0.01 && q.Y<door+0.35;})));
foreach(int doorEdge in new[]{0,1})foreach(int intricacy in new[]{0,2,4})
{
 Check(!Blocked(HallLayout.Plan(Rect(8,12),new HallLayout.Kit(),2,intricacy,doorEdge),2),"No beam crosses the top of a door in a 2 m wall");
 Check(!Blocked(HallLayout.Plan(Rect(8,12),new HallLayout.Kit{Door="wood_gate",DoorHeight=3},3,intricacy,doorEdge),3),"No beam crosses the top of a gate in a 3 m wall");
 Check(!Blocked(HallLayout.Plan(Rect(10,20),norseKit,2,intricacy,doorEdge,false,Norse()),2),"No beam crosses a longhouse door");
 Check(!Blocked(HallLayout.Plan(Rect(8,16),new HallLayout.Kit(),2,intricacy,doorEdge,false,new HallLayout.Details{Storeys=2}),2),"No joist crosses a door under an upper floor");
}
// Smoke holes are left open under any roof: a lowered roof piece over one kept the smoke in.
foreach(var width in new[]{10,8})
{
 var vented=HallLayout.Plan(Rect(width,20),new HallLayout.Kit{SmallFloor="wood_floor_1x1",SpanCells=5},2,2,1,false,Norse());
 Check(vented.Chimneys.Count>0 && !vented.Parts.Any(p=>p.Role=="smoke hole roof"),"Every smoke hole is open");
}
// Smoke holes, not chimneys: the roof tile is simply open.
foreach(var plan in new[]{HallLayout.Plan(Rect(10,20),norseKit,2,2,1,false,Norse()),HallLayout.Plan(Rect(10,20),norseKit,2,2,1,false,new HallLayout.Details{Norse=true,Hearth="hearth",ChimneyPoints=new[]{new V3(1,0,13)}})})
{
 Check(plan.Chimneys.Count>0 && !plan.Parts.Any(p=>p.Role=="chimney"),"A longhouse lets its smoke out through open holes, with no flue");
 Check(plan.Parts.Count(p=>p.Role=="roof")==plan.Cells.Count-plan.Chimneys.Count,"Each smoke hole is one open roof tile");
}
// A stone footing: two courses under every wall panel but the doors', then timber from 1 m.
var footingKit=new HallLayout.Kit{SmallFloor="wood_floor_1x1",SpanCells=5,StoneFence="stone_fence"};
foreach(int wallHeight in new[]{2,3})
{
 var footed=HallLayout.Plan(Rect(10,20),footingKit,wallHeight,2,1,false,new HallLayout.Details{Norse=true,Benches=true,Hearth="hearth",StoneFooting=true});
 var stones=footed.Parts.Where(p=>p.Role=="stone footing").ToList();
 Check(stones.Count==2*(footed.BoundaryPanels-1) && stones.All(p=>p.Prefab=="stone_fence"),"Two stone fence courses under every wall panel except the door's");
 // Each fence is centred on the wall line it carries.
 foreach(var s in stones)Check(footed.Parts.Any(w=>w.Role=="wall" && Math.Abs(w.At.X-s.At.X)<1e-9 && Math.Abs(w.At.Z-s.At.Z)<1e-9 && Math.Abs(w.Yaw-s.Yaw)<1e-9),"The fence is centred under its timber wall");
 Check(stones.Count(p=>p.At.Y==-1)==stones.Count(p=>p.At.Y==0),"One course in the ground, one above the floor");
 Check(footed.Parts.Where(p=>p.Role=="wall").All(p=>p.At.Y>=1-1e-9 || footed.Entrances.Any(d=>(new V3(p.At.X,0,p.At.Z)-d.At).Length<1.01)),"Timber walls start on the stone, except beside the door");
 Check(footed.Parts.Count(p=>p.Role=="wall" && p.At.Y>=1-1e-9)>0,"Half-height timber walls above the footing");
 Check(footed.Parts.All(p=>fixture.TryGetProperty(p.Prefab,out _)),"Footing uses native pieces");
}
Check(!HallLayout.Plan(Rect(10,20),norseKit,2,2,1,false,new HallLayout.Details{Norse=true,StoneFooting=true}).Parts.Any(p=>p.Role=="stone footing"),"No footing until stone walls are unlocked");
Console.WriteLine($"Norse longhouse: {checks:N0} checks passed.");
// Mountain cabin: stacked log walls on a stone footing, no corner posts, a 67° roof, a fire pit under a chimney.
var cabinKit=new HallLayout.Kit{SmallFloor="wood_floor_1x1",Log="wood_wall_log",Post="wood_pole_log",Beam="wood_wall_log",StoneFence="stone_fence",StoneBlock="stone_wall_1x1",Window="wood_window",Slope=2,Roof="wood_roof_67",Ridge="wood_roof_top_67",Wedge="wood_wall_roof_67_a"};
HallLayout.Details CabinDetails()=>new HallLayout.Details{Cabin=true,Hearth="fire_pit",StoneFooting=true,Shutters=true};
foreach(var (w,d) in new[]{(8,10),(10,8),(6,6)})
{
 var cabin=HallLayout.Plan(Rect(w,d),cabinKit,3,2,0,false,CabinDetails());
 var logs=cabin.Parts.Where(p=>p.Role=="log wall").ToList();
 Check(logs.Count>0 && logs.All(p=>Math.Abs(p.At.Y*2-Math.Round(p.At.Y*2))<1e-9 && p.At.Y<3),"Logs stack in half-metre courses below the eaves");
 Check(logs.All(p=>p.At.Y>=1-1e-9 || cabin.Entrances.Any(e=>(new V3(p.At.X,0,p.At.Z)-e.At).Length<1.01)),"Logs start on the stone fence");
 Check(cabin.Parts.Where(p=>p.Role=="stone footing").All(p=>p.Prefab=="stone_fence" || p.Prefab=="stone_wall_1x1"),"A fieldstone fence footing");
 var cornersOut=new[]{new V3(0,0,0),new V3(w,0,0),new V3(0,0,d),new V3(w,0,d)};
 foreach(var c in cornersOut)
 {
  Check(cabin.Parts.Count(p=>p.Prefab=="stone_wall_1x1" && (p.At-new V3(c.X,p.At.Y,c.Z)).Length<1e-9)==2,"A dressed stone fills each outer corner of both fence courses");
  // Every course of both walls runs on past the corner by half a metre.
  var ends=cabin.Parts.Where(p=>p.Role=="log end" && (new V3(p.At.X,0,p.At.Z)-c).Length<0.6).ToList();
  int courses=logs.Count(p=>(new V3(p.At.X,0,p.At.Z)-c).Length>0.99 && (new V3(p.At.X,0,p.At.Z)-c).Length<1.01);
  Check(ends.Count==courses && courses>0,"Each log at a corner has its end crossing past it");
  Check(ends.All(p=>Math.Abs(Math.Abs(p.At.X-c.X)+Math.Abs(p.At.Z-c.Z)-0.515)<1e-9),"Log ends reach half a metre past the corner");
 }
 // Gable logs: halfway up the gable, each course reaching the roof on both sides; boards only above them.
 var wing=cabin.Wings.Single();double span=wing.Width*2,rise=span/2*2;
 double Across(V3 p)=>wing.Across?p.Z-wing.Z*2:p.X-wing.X*2;
 var gableLogs=cabin.Parts.Where(p=>p.Role=="gable log").ToList();
 Check(gableLogs.Count>0,"Log gables");
 double logTop=gableLogs.Max(g=>g.At.Y)+0.5-3;
 Check(logTop<=rise/2+0.25+1e-9 && logTop>=rise/2-0.5,"Logs reach halfway up the gable");
 foreach(var course in gableLogs.GroupBy(g=>(Math.Round(g.At.Y,3),Math.Round(wing.Across?g.At.X:g.At.Z))))
 {
  double y=course.Key.Item1-3,reach=span-2*(y+0.25)/2;
  Check(Math.Abs(course.Min(g=>Across(g.At))-1-(span-reach)/2)<1e-6 && Math.Abs(course.Max(g=>Across(g.At))+1-(span+reach)/2)<1e-6,"A gable course reaches the roof at both ends");
 }
 Check(!cabin.Parts.Any(p=>p.Role=="gable" && p.Prefab=="woodwall" && p.At.Y+2<=3+logTop+1e-9),"No boards hidden behind the logs");
 Check(cabin.Parts.Where(p=>p.Role=="chimney").All(p=>p.Prefab=="wood_wall_log"),"A log chimney");
 Check(!cabin.Parts.Any(p=>p.Role=="post"),"Log walls carry their own corners");
 Check(cabin.Parts.Count(p=>p.Role=="stone footing")>0,"Cabin stands on a stone footing");
 Check(cabin.Parts.Where(p=>p.Role=="roof").All(p=>p.Prefab=="wood_roof_67" || p.Prefab=="wood_roof_top_67"),"Steep 67° roof");
 Check(cabin.Chimneys.Count==1 && cabin.Parts.Count(p=>p.Role=="hearth" && p.Prefab=="fire_pit")==1,"One fire pit under one chimney");
 var pit=cabin.Parts.Single(p=>p.Role=="hearth");var flue=cabin.Chimneys.Single();
 Check(Math.Abs(pit.At.X-(flue.X*2+1))<1e-9 && Math.Abs(pit.At.Z-(flue.Z*2+1))<1e-9 && Math.Abs(pit.At.Y)<1e-9,"The fire pit sits on the floor under the flue");
 Check((pit.At-cabin.Door).Length>=3,"The fire is away from the door");
 Check(cabin.Parts.Count(p=>p.Role=="roof")==cabin.Cells.Count-1,"Every tile roofed but the flue's");
}
Reject(()=>HallLayout.Plan(Rect(8,10),new HallLayout.Kit(),3,2,0,false,CabinDetails()),"cabin without logs");
// Under a floor: stone piers at the corners and every 4 m, beams on every tile edge between them.
foreach(var outline in new[]{Rect(8,10),Rect(10,10),l,u})
{
 var plan=HallLayout.Plan(outline,new HallLayout.Kit(),3,1,0);
 var piers=HallLayout.Piers(plan);var sills=HallLayout.Sills(plan);
 var cells=plan.Cells.ToHashSet();
 int Around(V3 v)=>new[]{(0,0),(-1,0),(0,-1),(-1,-1)}.Count(o=>cells.Contains(new HallLayout.Cell((int)Math.Round(v.X/2)+o.Item1,(int)Math.Round(v.Z/2)+o.Item2)));
 foreach(var c in outline)Check(piers.Any(p=>(p-c).Length<1e-9),"A pier under every outline corner");
 Check(piers.Count*2.5<plan.Cells.Count*6*0.5,"Piers take under half the stone of a slab under every tile");
  foreach(var c in cells)foreach(var (a,b) in new[]{(new V3(c.X*2,0,c.Z*2),new V3(c.X*2+2,0,c.Z*2)),(new V3(c.X*2,0,c.Z*2),new V3(c.X*2,0,c.Z*2+2))})
  Check(sills.Any(s=>(s.a-a).Length<1e-9 && (s.b-b).Length<1e-9),"Every tile edge has a beam under it");
 // No beam is more than one beam from a pier.
 foreach(var s in sills)Check(piers.Any(p=>(p-s.a).Length<2.01 || (p-s.b).Length<2.01),"Beams reach a pier within 2 m");
 // Along the walls a pier every 4 m.
 var edge=sills.Where(s=>Around(s.a)<4 && Around(s.b)<4).ToList();
 foreach(var s in edge)Check(piers.Any(p=>(p-s.a).Length<1e-9 || (p-s.b).Length<1e-9),"Every wall beam has a pier at one end");
}
// Stilt cabin: a deck along the entrance side, railed except in front of the door, its landing off the deck.
var stilts=HallLayout.Plan(Rect(8,10),new HallLayout.Kit{SmallFloor="wood_floor_1x1"},3,2,0,false,new HallLayout.Details{Stilts=true,Deck=true});
var deckFloors=stilts.Parts.Where(p=>p.Role=="deck").ToList();
Check(deckFloors.Count==4 && deckFloors.All(p=>p.At.Z<0 && p.At.Z>-2),"Four deck tiles outside the entrance wall");
Check(stilts.PorchFloors.Count==4,"Deck tiles count as porch floor for supports");
var rails=stilts.Parts.Where(p=>p.Role=="deck rail").ToList();
Check(rails.Count>0 && rails.All(r=>Math.Abs(r.At.X-stilts.Door.X)>1.5),"No rail across the way in from the door");
Check((stilts.EntryLanding-stilts.Door).Length>1.9 && stilts.EntryLanding.Z<-1.9,"The way in lands off the deck's edge");
Check(!HallLayout.Plan(Rect(8,10),new HallLayout.Kit{SmallFloor="wood_floor_1x1"},3,2,0,false,new HallLayout.Details{Stilts=true}).Parts.Any(p=>p.Role=="deck"),"No deck when it is off");
// Stone keep: stone courses in running bond, arrow loops above the ground floor, a double gate under a stone arch, a walkable roof
// behind merlons, stairs all the way up.
var keepKit=new HallLayout.Kit{SmallFloor="wood_floor_1x1",StoneWall="stone_wall_2x1",StoneBlock="stone_wall_1x1",Door="wood_gate",DoorHeight=3,Leaves=2};
foreach(int storeys in new[]{2,3,4})
{
 const int h=4;
 var keep=HallLayout.Plan(Rect(12,14),keepKit,h,2,0,false,new HallLayout.Details{Keep=true,Storeys=storeys});
 double roof=h*(storeys-1);
 Check(keep.Wings.Count==0 && !keep.Parts.Any(p=>p.Role=="roof") && keep.RoofHeight>=roof,"A keep has a flat walkable roof, not a pitched one");
 var stones=keep.Parts.Where(p=>p.Role=="stone wall").ToList();
 Check(stones.All(p=>p.Prefab.StartsWith("stone_wall")) && keep.Parts.Where(p=>p.Role=="merlon").All(p=>p.Prefab=="stone_wall_1x1"),"Stone walls");
 Check(!keep.Parts.Any(p=>p.Prefab.StartsWith("wood") && (p.Role=="wall" || p.Role=="gable")),"No timber in the outer wall");
 // Running bond: a 2 m stone's joints fall a metre from those of the course below.
 foreach(var s2 in stones.Where(p=>p.Prefab=="stone_wall_2x1"))
 {
  bool alongX=Math.Abs(Math.Sin(s2.Yaw*Math.PI/180))<1e-6;double c=alongX?s2.At.X:s2.At.Z;
  Check(((int)Math.Round(c-1+s2.At.Y)%2+2)%2==0,"Stones in running bond");
 }
 // Length laid in each course: the whole perimeter (and its corners), less the gate, the arch and the loops.
 double Laid(int y)=>stones.Where(p=>Math.Abs(p.At.Y-y)<1e-9).Sum(p=>p.Prefab=="stone_wall_2x1"?2.0:1.0);
 int loops=keep.BoundaryPanels/2;
 for(int y=0;y<=roof;y++)
 {
  double expect=52+4-(y<3?4:0);
  if(y>=h && y<roof && y%h==1)Check(Laid(y)<expect && Laid(y)>=expect-loops-2,"Arrow loops leave gaps in their course");
  else Check(Math.Abs(Laid(y)-expect)<1e-9,$"Course {y} is closed but for the gate");
 }
 // Below the floor the walls go on down, bonded the same, closed under the gate too, for the builder to cut at the ground.
 var footing=keep.Parts.Where(p=>p.Role=="keep footing").ToList();
 for(int y=-HallLayout.KeepFooting;y<0;y++)Check(Math.Abs(footing.Where(p=>Math.Abs(p.At.Y-y)<1e-9).Sum(p=>p.Prefab=="stone_wall_2x1"?2.0:1.0)-56)<1e-9,"A footing course under the whole wall line");
 Check(keep.Parts.Count(p=>p.Role=="entrance" && p.Prefab=="wood_gate")==2,"A double gate");
 Check(!keep.Parts.Any(p=>p.Role=="gate arch"),"No arch over the gate");
 Check(!stones.Any(p=>p.At.Y<3-1e-9 && Math.Abs(p.At.Z)<1e-9 && Math.Abs(p.At.X-6)<2+(p.Prefab=="stone_wall_2x1"?1:0.5)-1e-9),"Nothing in the gateway");
 Check(stones.Count(p=>Math.Abs(p.At.Y-3)<1e-9 && Math.Abs(p.At.Z)<1e-9 && Math.Abs(p.At.X-6)<2.5)>0,"Stone over the gate");
 // No beam crosses the 4 m opening below a metre over the gate.
 Check(!keep.Parts.Any(p=>p.Kind==HallLayout.Anchor.Segment && Enumerable.Range(0,9).Select(i=>p.At+(p.End-p.At)*(i/8.0)).Any(q=>Math.Abs(q.X-6)<1.99 && Math.Abs(q.Z)<0.35 && q.Y<3.35 && q.Y>-0.01)),"No beam across the double gate");
 var merlons=keep.Parts.Where(p=>p.Role=="merlon").ToList();
 Check(merlons.Count==keep.BoundaryPanels && merlons.All(p=>Math.Abs(p.At.Y-(roof+1))<1e-9),"A merlon on every parapet panel");
 Check(stones.Max(p=>p.At.Y)<=roof+1e-9,"The parapet is one course above the roof deck");
 Check(keep.Parts.Count(p=>p.Role=="upper floor")>0 && keep.Parts.Where(p=>p.Role=="upper floor").Max(p=>p.At.Y)>=roof-1e-9,"Floors up to the roof deck");
 Check(keep.Parts.Count(p=>p.Role=="interior stair")>=3*(storeys-1),"Stairs climb to the roof");
 Check(keep.Parts.Where(p=>p.Role=="storey post").All(p=>p.At.Y+2<=roof+1e-9),"Posts stop under the roof deck");
 foreach(var c in new[]{new V3(0,0,0),new V3(12,0,0),new V3(0,0,14),new V3(12,0,14)})
  Check(keep.Parts.Count(p=>p.Prefab=="stone_wall_1x1" && (p.Role=="stone wall" || p.Role=="keep footing") && Math.Abs(p.At.X-c.X)<1e-9 && Math.Abs(p.At.Z-c.Z)<1e-9)==(int)roof+1+HallLayout.KeepFooting,"A corner stone in every course, up the parapet");
}
// A plain door in a keep: jambs of stone, bonded the same.
var doorKeep=HallLayout.Plan(Rect(8,10),new HallLayout.Kit{SmallFloor="wood_floor_1x1",StoneWall="stone_wall_2x1",StoneBlock="stone_wall_1x1"},3,2,0,false,new HallLayout.Details{Keep=true,Storeys=2});
Check(doorKeep.Parts.Count(p=>p.Role=="entrance")==1 && !doorKeep.Parts.Any(p=>p.Role=="gate arch"),"A keep with a door has no arch");
Check(!doorKeep.Parts.Any(p=>p.Role=="stone wall" && p.At.Y<2-1e-9 && Math.Abs(p.At.Z)<1e-9 && Math.Abs(p.At.X-4)<1+(p.Prefab=="stone_wall_2x1"?1:0.5)-1e-9),"Nothing in the doorway");
Reject(()=>HallLayout.Plan(Rect(4,10),keepKit,4,2,0,false,new HallLayout.Details{Keep=true,Storeys=2}),"double gate against a corner");
Reject(()=>HallLayout.Plan(Rect(8,10),keepKit,3,2,0,false,new HallLayout.Details{Keep=true,Storeys=1}),"one-storey keep");
Reject(()=>HallLayout.Plan(Rect(8,10),new HallLayout.Kit(),3,2,0,false,new HallLayout.Details{Keep=true,Storeys=3}),"keep without stone");
Reject(()=>HallLayout.Plan(Rect(8,10),keepKit,3,2,0,false,new HallLayout.Details{Keep=true,Storeys=3,Porch=true}),"keep with a porch");
Console.WriteLine($"Cabin, stilts and keep: {checks:N0} checks passed.");
