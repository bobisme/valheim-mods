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
