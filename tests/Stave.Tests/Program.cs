using BuildShapes;
using System.Text.Json;
using System.Diagnostics;
int checks=0;
void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
void Reject(Action action,string message){try{action();}catch(ArgumentException){checks++;return;}throw new Exception("Accepted "+message);}
V3[] Rect(int w,int d)=>new[]{new V3(0,0,0),new V3(w,0,0),new V3(w,0,d),new V3(0,0,d)};
var fixture=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/native-pieces.json"))).RootElement;
V3 Vec(JsonElement x)=>new(x[0].GetDouble(),x[1].GetDouble(),x[2].GetDouble());
var watch=Stopwatch.StartNew();
void Verify(V3[] outline,int chamber,int crowns,bool gallery,bool steep,int detail,bool eaves,int entrance=0,IReadOnlyList<V3> doors=null)
{
 var kit=new HallLayout.Kit{SpanCells=5,Door=chamber==4?"wood_door":"wood_gate",DoorHeight=chamber==4?2:3,Slope=steep?1:0.5,Roof=steep?"wood_roof_45":"wood_roof",Ridge=steep?"wood_roof_top_45":"wood_roof_top",Wedge=steep?"wood_wall_roof_45":"wood_wall_roof_a"};
 int side=chamber==4?2:3;
 var details=new HallLayout.Details{DoorPoints=doors,Overhang=eaves,Sweep=detail>=2,Finial=detail>=2?"wood_dragon1":null};
 var options=new StaveLayout.Options{Height=chamber,Crowns=crowns,Gallery=gallery};
 HallLayout.Design plan;try{plan=StaveLayout.Plan(outline,kit,side,detail,entrance,details,options);}catch(Exception ex){throw new Exception($"gallery={gallery}, crowns={crowns}, outline="+string.Join(";",outline.Select(v=>v.X+","+v.Z)),ex);}
 var set=HallLayout.Footprint(outline).ToHashSet();Check(plan.Cells.ToHashSet().SetEquals(set),"Temple preserves concave footprint");
 Check(plan.Parts.Count(p=>p.Role=="floor")==set.Count,"One floor per drawn cell and no intermediate ceilings");
 Check(plan.Parts.Count<=2048&&plan.Parts.All(p=>p.At.Finite&&p.End.Finite&&double.IsFinite(p.Yaw)),"Finite bounded output");
 var stages=plan.Wings.Where(w=>w.Stage!="gallery").ToArray();Check(stages.Length==crowns+1,"Requested crown count");
 for(int i=1;i<stages.Length;i++){var parent=stages[i-1];var next=stages[i];Check(parent.Cutout.SetEquals(StaveLayout.Tiles(next)),"Parent roof aperture exactly matches child walls");Check(next.RoofBase>=parent.RoofBase+parent.Width*kit.Slope+1-0.001,"Child eaves clear the lower ridge");Check(next.Width<parent.Width&&next.Length<parent.Length,"Crown narrows in both dimensions");}
 var cover=new HashSet<HallLayout.Cell>();
 foreach(var p in plan.Parts)
 {
  Check(fixture.TryGetProperty(p.Prefab,out var native),"Native prefab exists: "+p.Prefab);
  if(p.Kind==HallLayout.Anchor.Segment){Check(Math.Abs((p.End-p.At).Length-(p.Prefab==kit.ShortBeam?kit.ShortLength:kit.BeamLength))<0.001,"Every beam keeps native snap length");continue;}
  if(!p.Role.EndsWith("roof")||p.Role=="temple eave roof"||p.Role=="porch roof")continue;
  var snaps=native.GetProperty("snaps").EnumerateObject().Select(x=>Vec(x.Value)).ToArray();double low=snaps.Min(x=>x.Y);var bottom=snaps.Where(x=>Math.Abs(x.Y-low)<0.001).ToArray();var anchor=bottom.Aggregate(new V3(),(a,b)=>a+b)*(1.0/bottom.Length);
  var world=snaps.Select(x=>p.At+HallLayout.Turn(x-anchor,p.Yaw)).ToArray();double x0=world.Min(x=>x.X),z0=world.Min(x=>x.Z);
  Check(Math.Abs(world.Max(x=>x.X)-x0-2)<0.001&&Math.Abs(world.Max(x=>x.Z)-z0-2)<0.001,"Native roof covers exactly one two-metre tile");
  var cell=new HallLayout.Cell((int)Math.Round(x0/2),(int)Math.Round(z0/2));Check(set.Contains(cell)&&cover.Add(cell),"Roof cells stay in the drawn footprint and never duplicate even across stages");
  var wing=plan.Wings.Single(w=>StaveLayout.Tiles(w).Contains(cell)&&!w.Cutout.Contains(cell));int col=wing.Across?wing.D-1-(cell.Z-wing.Z):cell.X-wing.X;
  Check(Math.Abs(p.At.Y-(wing.RoofBase+Math.Min(col,wing.Width-1-col)*2*kit.Slope))<0.001,"Native roof edge follows explicit stage elevation");
 }
 Check(cover.SetEquals(set),"Pierced roof stages and low wings cover every drawn cell exactly once");
 var roofs=plan.Parts.Where(p=>p.Role.EndsWith("roof")).Select(p=>
 {
  var snaps=fixture.GetProperty(p.Prefab).GetProperty("snaps").EnumerateObject().Select(x=>Vec(x.Value)).ToArray();double y=snaps.Min(x=>x.Y);var bottom=snaps.Where(x=>Math.Abs(x.Y-y)<0.001).ToArray();var anchor=bottom.Aggregate(new V3(),(a,b)=>a+b)*(1.0/bottom.Length);var transformed=snaps.Select(x=>p.At+HallLayout.Turn(x-anchor,p.Yaw)).ToArray();
  double Height(double x,double z){var local=HallLayout.Turn(new V3(x,0,z)-new V3(p.At.X,0,p.At.Z),-p.Yaw)+anchor;double rise=snaps.Max(v=>v.Y)-y;double h=p.Prefab.Contains("_top")?rise*(1-Math.Abs(local.Z)):rise*(1-local.Z)/2;return p.At.Y+y+h-anchor.Y;}
  return (part:p,x0:transformed.Min(v=>v.X),x1:transformed.Max(v=>v.X),z0:transformed.Min(v=>v.Z),z1:transformed.Max(v=>v.Z),height:(Func<double,double,double>)Height);
 }).ToArray();
 foreach(var a in roofs.Where(r=>r.part.Role=="temple eave roof"))foreach(var b in roofs.Where(r=>r.part!=a.part))
 {
  double x0=Math.Max(a.x0,b.x0),x1=Math.Min(a.x1,b.x1),z0=Math.Max(a.z0,b.z0),z1=Math.Min(a.z1,b.z1);if(x1-x0<0.01||z1-z0<0.01)continue;
  double min=double.MaxValue,max=double.MinValue;
  foreach(double x in new[]{x0,(x0+x1)/2,x1})foreach(double z in new[]{z0,(z0+z1)/2,z1}){double delta=a.height(x,z)-b.height(x,z);min=Math.Min(min,delta);max=Math.Max(max,delta);}
  Check(min>=-0.01||max<=0.01,"Native roof surfaces never cross under layered eaves");
 }
 var remaining=new HashSet<HallLayout.Cell>(plan.Cells.Where(c=>!StaveLayout.Tiles(plan.Sanctum).Contains(c)));
 while(remaining.Count>0)
 {
  var component=new HashSet<HallLayout.Cell>();var todo=new Queue<HallLayout.Cell>();var first=remaining.First();remaining.Remove(first);todo.Enqueue(first);
  while(todo.Count>0){var c=todo.Dequeue();component.Add(c);foreach(var n in new[]{new HallLayout.Cell(c.X-1,c.Z),new HallLayout.Cell(c.X+1,c.Z),new HallLayout.Cell(c.X,c.Z-1),new HallLayout.Cell(c.X,c.Z+1)})if(remaining.Remove(n))todo.Enqueue(n);}
  Check(plan.InnerEntrances.Any(d=>component.Any(c=>{var v=HallLayout.Turn(new V3(c.X*2+1,0,c.Z*2+1)-d.At,-d.Yaw);return Math.Abs(v.Z+1)<0.01&&Math.Abs(v.X)<1.6;})),"Every low wing component reaches a fitted inner doorway");
 }
 foreach(var d in plan.Entrances.Concat(plan.InnerEntrances))foreach(var wall in plan.Parts.Where(p=>p.Role=="wall"||p.Role=="sanctum wall"||p.Role=="gallery railing"))
 {
  var snaps=fixture.GetProperty(wall.Prefab).GetProperty("snaps").EnumerateObject().Select(x=>Vec(x.Value)).ToArray();double low=snaps.Min(v=>v.Y);var bottom=snaps.Where(v=>Math.Abs(v.Y-low)<0.001).ToArray();var anchor=bottom.Aggregate(new V3(),(a,b)=>a+b)*(1.0/bottom.Length);var actual=snaps.Select(v=>HallLayout.Turn(wall.At+HallLayout.Turn(v-anchor,wall.Yaw)-d.At,-d.Yaw)).ToArray();
  if(actual.All(v=>Math.Abs(v.Z)<0.001))Check(!(actual.Min(v=>v.Y)<kit.DoorHeight-0.001&&actual.Max(v=>v.X)>-1+0.001&&actual.Min(v=>v.X)<1-0.001),$"Door {d.At.X},{d.At.Z} yaw {d.Yaw} blocked by {wall.Role} {wall.At.X},{wall.At.Y},{wall.At.Z} yaw {wall.Yaw}; gallery={gallery} outline="+string.Join(";",outline.Select(v=>v.X+","+v.Z)));
 }
 var center=plan.Sanctum.At(plan.Sanctum.Width,0,plan.Sanctum.Length);
 foreach(var stave in plan.StaveSupports){Check(!HallDoors.Blocks(plan,stave),"Staves do not block outer or inner entrances");Check(plan.Vertices.Any(v=>Math.Abs(v.X-stave.X)<0.001&&Math.Abs(v.Z-stave.Z)<0.001),"Every stave has a terrain/foundation vertex");}
 Check(plan.Parts.Where(p=>p.Role=="stave").All(p=>Math.Abs((plan.Sanctum.Across?p.At.Z-center.Z:p.At.X-center.X))>=0.99),"At least two-metre nave aisle between native staves");
 var again=StaveLayout.Plan(outline,kit,side,detail,entrance,details,options);Check(plan.Sanctum.X==again.Sanctum.X&&plan.Sanctum.Z==again.Sanctum.Z&&plan.Parts.Select(p=>p.Prefab+":"+p.At.X+","+p.At.Y+","+p.At.Z+":"+p.Yaw).SequenceEqual(again.Parts.Select(p=>p.Prefab+":"+p.At.X+","+p.At.Y+","+p.At.Z+":"+p.Yaw)),"Deterministic chamber and pieces");
}
var concave=new[]{new V3(0,0,0),new V3(18,0,0),new V3(18,0,10),new V3(14,0,10),new V3(14,0,18),new V3(0,0,18)};
foreach(var outline in new[]{Rect(10,14),Rect(14,18),Rect(18,14),concave,concave.Reverse().ToArray()})
 foreach(bool steep in new[]{true,false})foreach(int chamber in new[]{4,6,8})foreach(int detail in new[]{0,2})foreach(bool eaves in new[]{false,true})Verify(outline,chamber,1,true,steep,detail,eaves);
foreach(var outline in new[]{Rect(14,18),Rect(18,14),concave})foreach(bool steep in new[]{true,false})Verify(outline,6,2,true,steep,2,true);
Verify(Rect(6,10),4,1,false,true,1,false);foreach(var outline in new[]{Rect(12,10),Rect(18,14),concave})Verify(outline,6,1,false,true,2,true);for(int e=0;e<4;e++)Verify(Rect(14,18),6,1,true,true,2,true,e);
var cross=new[]{new V3(0,0,6),new V3(6,0,6),new V3(6,0,0),new V3(16,0,0),new V3(16,0,6),new V3(22,0,6),new V3(22,0,16),new V3(16,0,16),new V3(16,0,22),new V3(6,0,22),new V3(6,0,16),new V3(0,0,16)};
var courtyard=new[]{new V3(0,0,0),new V3(24,0,0),new V3(24,0,24),new V3(14,0,24),new V3(14,0,10),new V3(10,0,10),new V3(10,0,24),new V3(0,0,24)};
foreach(var outline in new[]{cross,cross.Reverse().ToArray(),courtyard})foreach(bool gallery in new[]{true,false})foreach(bool steep in new[]{true,false})Verify(outline,6,1,gallery,steep,2,true);
foreach(bool gallery in new[]{true,false})Verify(Rect(14,18),6,1,gallery,true,2,true,0,new[]{new V3(7,0,0),new V3(14,0,9),new V3(7,0,18),new V3(0,0,9)});
Verify(Rect(6,10),6,1,false,true,2,true,0,new[]{new V3(1,0,0),new V3(0,0,2)});
Reject(()=>StaveLayout.Plan(Rect(8,8),new HallLayout.Kit(),3,1,0,new HallLayout.Details(),new StaveLayout.Options()),"gallery too small");
Reject(()=>StaveLayout.Plan(Rect(10,10),new HallLayout.Kit(),3,1,0,new HallLayout.Details(),new StaveLayout.Options{Crowns=2}),"two crowns in a narrow chamber");
Reject(()=>StaveLayout.Plan(Rect(14,18),new HallLayout.Kit(),3,1,0,new HallLayout.Details(),new StaveLayout.Options{Height=4}),"low chamber over tall gallery");
Reject(()=>StaveLayout.Plan(Rect(14,18),new HallLayout.Kit(),3,1,0,new HallLayout.Details{Storeys=2},new StaveLayout.Options()),"ceilings dividing open temple");
Reject(()=>StaveLayout.Plan(Rect(14,18),new HallLayout.Kit(),3,1,0,new HallLayout.Details{Basement=true},new StaveLayout.Options()),"temple basement");
Console.WriteLine($"Passed {checks} stave temple native-snap/circulation checks in {watch.ElapsedMilliseconds} ms.");
