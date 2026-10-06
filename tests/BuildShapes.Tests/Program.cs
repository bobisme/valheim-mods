using BuildShapes;
using BepInEx.Bootstrap;
using UnityEngine;

int checks = 0;
void Check(bool ok, string name) { checks++; if (!ok) throw new Exception(name); }
bool Near(V3 a, V3 b) => (a - b).Length < 1e-6;
void Reject(Action action, string reason) { bool rejected = false; try { action(); } catch (ArgumentException) { rejected = true; } Check(rejected, reason); }
foreach (double shift in new[] { 0, -10000.0, 10000.0 })
{
    V3 a = new V3(shift, 1, shift), b = new V3(shift + 6, 5, shift + 3), c = new V3(shift + 12, 1, shift);
    Check(Near(Curve.At(a,b,c,0), a) && Near(Curve.At(a,b,c,0.5),b) && Near(Curve.At(a,b,c,1),c), "Curve passes through all three markers");
    foreach (double length in new[] { 1.0, 2.0, 4.0 })
    {
        var segments = Curve.Plan(a,b,c,length);
        Check(segments.Count > 1 && segments.Count <= 256, "Bounded beam count");
        Check(Near(segments[0].Start,a) && Near(segments[^1].End,c), "Native-length pieces preserve the outer endpoints");
        foreach (Segment segment in segments)
            Check(Math.Abs((segment.End - segment.Start).Length - length) < 1e-7, "Every piece retains native length");
        for (int i = 1; i < segments.Count; i++)
            Check((segments[i].Start - segments[i-1].End).Length < length, "Adjacent curve joints remain local");
    }
}
var straight = Curve.Plan(new V3(0,0,0),new V3(4,0,0),new V3(8,0,0),2);
Check(straight.Count == 4, "Straight chain uses exact piece count");
for (int i = 0; i < straight.Count; i++)
    Check(Near(straight[i].Start,new V3(i*2,0,0)) && Near(straight[i].End,new V3(i*2+2,0,0)), "Straight pieces meet exactly");
var vertical = Curve.Plan(new V3(0,0,0),new V3(1,4,0),new V3(0,8,0),1);
Check(Near(vertical[0].Start,new V3(0,0,0)) && Near(vertical[^1].End,new V3(0,8,0)), "Vertical arches supported");
Reject(() => Curve.Plan(new V3(0,0,0),new V3(1,1,0),new V3(0,0,0),1), "Coincident endpoints rejected");
Reject(() => Curve.Plan(new V3(double.NaN,0,0),new V3(1,1,0),new V3(2,0,0),1), "Nonfinite markers rejected");
Reject(() => Curve.Plan(new V3(0,0,0),new V3(1,1,0),new V3(2,0,0),double.PositiveInfinity), "Invalid beam length rejected");
Reject(() => Curve.Plan(new V3(0,0,0),new V3(0.2,0,0),new V3(0.4,0,0),2), "Too-short curve rejected");
Reject(() => Curve.Plan(new V3(0,0,0),new V3(40,0,0),new V3(80,0,0),0.25), "Piece budget enforced before placement");
Reject(() => Curve.Plan(new V3(0,0,0),new V3(100,50,0),new V3(150,0,0),1), "Curve length budget");
Reject(() => Curve.Plan(new V3(0,0,0),new V3(1000,0,0),new V3(8,0,0),1), "Oversized control polygon rejected");

// Mirror positions and orthonormal frames at translated, oblique planes and tilted source orientations.
V3 Unit(V3 a) => a*(1/a.Length);
foreach(double shift in new[]{0.0,-10000,10000})
foreach(V3 line in new[]{new V3(0,0,8),new V3(8,0,0),new V3(6,3,8)})
{
 V3 origin=new V3(shift,3,shift), end=origin+line;
 var mirror=new Mirror(origin,end);
 V3 right=Unit(new V3(1,2,3)), up=Unit(V3.Cross(new V3(-2,1,4),right)), forward=V3.Cross(right,up);
 V3 point=origin+new V3(5,4,-7), reflected=mirror.Point(point);
 Check(Near(mirror.Point(reflected),point),"Mirror point involution at world coordinates");
 Check(Math.Abs(reflected.Y-point.Y)<1e-8,"Vertical mirror preserves height");
 Check(Near(mirror.Point(origin),origin)&&Near(mirror.Point(end),end),"Mirror line points fixed regardless of marker heights");
 V3 r=mirror.Right(right),u=mirror.Up(up),f=mirror.Forward(forward);
 Check(Near(V3.Cross(r,u),f)&&Math.Abs(r.Length-1)<1e-8&&Math.Abs(u.Length-1)<1e-8,"Mirrored frame has positive handedness and unit axes");
 Check(Near(mirror.Right(r),right)&&Near(mirror.Up(u),up)&&Near(mirror.Forward(f),forward),"Tilted orientation mirror involution");
 V3 local=new V3(2,5,-3);
 Check(Near(mirror.Point(point+right*local.X+up*local.Y+forward*local.Z),reflected+r*(-local.X)+u*local.Y+f*local.Z),"Offset-pivot transform agrees with reflection plus local-X flip");
 // A beam spanning local x=0..4 has its symmetry centre at x=2, not at its root.
 V3 offsetRoot=mirror.Origin(point,right,2);
 Check(Near(offsetRoot+r*4,mirror.Point(point))&&Near(offsetRoot,mirror.Point(point+right*4)),"Offset-pivot beam endpoints mirror exactly with root compensation");
 Check(Near(mirror.Origin(offsetRoot,r,2),point),"Offset-pivot mirror root compensation is an involution");
 Check(Math.Abs((reflected-mirror.Point(origin+new V3(2,1,0))).Length-(point-(origin+new V3(2,1,0))).Length)<1e-7,"Reflection preserves distances");
}
Reject(()=>new Mirror(new V3(),new V3(0,5,0)),"Vertical-only mirror line rejected");
Reject(()=>new Mirror(new V3(),new V3(double.NaN,0,1)),"Nonfinite mirror line rejected");
Reject(()=>new Mirror(new V3(),new V3(129,0,0)),"Oversized mirror line rejected");

// Recorded native snaps for roof wedges/roofs/beams, captured from the installed game's piece catalog.
using(var fixtures=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/structural-snaps.json"))))
foreach(var fixture in fixtures.RootElement.EnumerateArray())
{
 string fixtureName=fixture.GetProperty("prefab").GetString()!;
 var snaps=fixture.GetProperty("snaps").EnumerateArray().Select(p=>new V3(p[0].GetDouble(),p[1].GetDouble(),p[2].GetDouble())).ToArray();
 var profile=MirrorProfile.Choose(snaps,new V3(0.03,0.2,0.002));
 Check(profile.FlipZ==fixture.GetProperty("flipZ").GetBoolean(),fixtureName+": native structural symmetry axis");
 // An oblique world plane and tilted source frame make wrong axis/sign/pivot changes visible.
 var mirror=new Mirror(new V3(3,4,1),new V3(8,5,7));
 V3 source=new V3(12,9,6),r=Unit(new V3(1,2,3)),u=Unit(V3.Cross(new V3(3,1,-2),r)),f=V3.Cross(r,u);
 V3 root=mirror.Origin(source,profile.FlipZ?f:r,profile.Centre);
 V3 mr=mirror.Right(r,profile.FlipZ),mu=mirror.Up(u),mf=mirror.Forward(f,profile.FlipZ);
 Check(Near(V3.Cross(mr,mu),mf),fixtureName+": proper-handed mirrored frame");
 foreach(V3 point in snaps)
 {
  V3 expected=mirror.Point(source+r*point.X+u*point.Y+f*point.Z);
  Check(snaps.Any(q=>Near(root+mr*q.X+mu*q.Y+mf*q.Z,expected)),fixtureName+": every reflected native snap lands correctly");
  // Use a tilted final frame: pinning must include the selected snap's full XYZ offset,
  // not just height, and must remain correct away from the world origin.
  foreach(V3 station in new[]{new V3(5,7,-3),new V3(10000,21,-10000)})
  {
   V3 placed=Curve.AnchoredOrigin(station,point,r,u,f);
   Check(Near(placed+r*point.X+u*point.Y+f*point.Z,station),fixtureName+": selected Repeat snap stays on the path after final tilt/yaw");
  }
 }
}
V3 anchorStation=new V3(10,20,30);
Check(Near(Curve.AnchoredOrigin(anchorStation,new V3(0,-1,0),new V3(1,0,0),new V3(0,0,1),new V3(0,-1,0)),new V3(10,20,31)),"Bottom of a pitched post pins to the path, not its root");
Check(Near(Curve.AnchoredOrigin(anchorStation,new V3(2,0,0),new V3(0,0,-1),new V3(0,1,0),new V3(1,0,0)),new V3(10,20,32)),"End of a yawed beam pins to the path");
Check(Near(Curve.AnchoredOrigin(anchorStation,new V3(),new V3(1,0,0),new V3(0,0,1),new V3(0,-1,0)),anchorStation),"Piece origin retains legacy Repeat placement");
var offsetProfile=MirrorProfile.Choose(new[]{new V3(0,0,0),new V3(4,0,0)},new V3(2.1,0,0));
Check(!offsetProfile.FlipZ&&offsetProfile.Centre==2,"Snap-derived origin compensation avoids decorative mesh-bound drift");
var fallback=MirrorProfile.Choose(Array.Empty<V3>(),new V3(2.1,0,0));
Check(!fallback.FlipZ&&fallback.Centre==2.1,"Pieces without snaps retain bounded mesh-centre fallback");

var row=Curve.Repeat(new V3(),new V3(5,0,0),new V3(10,0,0),3);
Check(row.Count==5,"Repeat fits both ends with bounded maximum spacing");
for(int i=0;i<row.Count;i++)
 Check(Near(row[i].Position,new V3(i*2.5,0,0))&&Math.Abs(row[i].Yaw-90)<1e-8,"Straight repeat evenly spaced with tangent yaw");
// Integrate the analytic derivative independently; X=12t lets us recover each station's parameter.
double ArcBetween(double lo,double hi)
{
 double total=0;const int slices=1000;
 double Speed(double t)=>Math.Sqrt(144+Math.Pow(16-32*t,2)+Math.Pow(12-24*t,2));
 for(int i=0;i<slices;i++){double a=lo+(hi-lo)*i/slices,b=lo+(hi-lo)*(i+1)/slices;total+=(b-a)/6*(Speed(a)+4*Speed((a+b)/2)+Speed(b));}
 return total;
}
foreach(double spacing in new[]{0.25,1.0,2.0,16.0})
{
 var stations=Curve.Repeat(new V3(),new V3(6,4,3),new V3(12,0,0),spacing);
 Check(Near(stations[0].Position,new V3())&&Near(stations[^1].Position,new V3(12,0,0)),"Curved repeat preserves both native-root anchors");
 double desired=ArcBetween(0,1)/(stations.Count-1);
 for(int i=1;i<stations.Count;i++)
 {
  double previous=stations[i-1].Position.X/12,t=stations[i].Position.X/12;
  Check(Math.Abs(ArcBetween(previous,t)-desired)<0.0001,"Repeat uniform in arc distance, checked with independent integration");
  Check((stations[i].Position-stations[i-1].Position).Length<=spacing+1e-7,"Repeat chord never exceeds chosen maximum spacing");
  Check(Math.Abs(stations[i].Yaw-Math.Atan2(12,12-24*t)*180/Math.PI)<0.0001,"Repeat uses analytic projected tangent yaw");
 }
}
var posts=Curve.Repeat(new V3(),new V3(0,4,0),new V3(0,8,0),2);
Check(posts.Count==5&&posts.All(s=>s.Yaw==0),"Vertical path has stable yaw fallback without tipping posts");
var cusp=Curve.Repeat(new V3(),new V3(0,5,2),new V3(0,10,0),1);
Check(cusp.All(s=>!double.IsNaN(s.Yaw)&&!double.IsInfinity(s.Yaw)),"Vanishing horizontal tangent retains finite yaw");
Reject(()=>Curve.Repeat(new V3(),new V3(64,0,0),new V3(128,0,0),0.25),"Repeat rejects station budget before output");
Reject(()=>Curve.Repeat(new V3(),new V3(100,0,0),new V3(200,0,0),2),"Repeat path bound");
Reject(()=>Curve.Repeat(new V3(),new V3(1,0,0),new V3(2,0,0),double.NaN),"Repeat nonfinite spacing");
Reject(()=>Curve.Repeat(new V3(),new V3(1,0,0),new V3(),2),"Repeat coincident ends");

// Exercise the real reflection bridge through reloads, missing/old dependencies, and failure replies.
var link = new Planner(); var player = new Player();
Check(!link.Ready(), "Missing planner is harmless");
Chainloader.PluginInfos[Planner.Guid] = new BepInEx.PluginInfo { Instance = new OldPlanner() };
Check(!link.Ready(), "Old planner refuses activation");
var first = new MockPlanner(); Chainloader.PluginInfos[Planner.Guid].Instance = first;
Check(link.Ready(), "Compatible planner discovered after add-on Awake");
Check(link.Create(player,"wood_beam",new Vector3[2],new Quaternion[2],out string key,out _) && key == "first" && first.Creates == 1, "Batch submitted to live instance");
Check(link.Remove(player,key,out int removed,out _) && removed == 2 && first.Removes == 1, "Undo uses same supported interface");
Chainloader.PluginInfos[Planner.Guid].Instance = null;
Check(!link.Ready(), "Transient null during F6 tolerated");
var second = new MockPlanner { Key = "second", Reject = true }; Chainloader.PluginInfos[Planner.Guid].Instance = second;
Check(link.Ready() && !link.Create(player,"wood_beam",new Vector3[1],new Quaternion[1],out _,out string error) && error == "protected", "New planner errors propagated after reload");
Check(first.Creates == 1 && second.Creates == 1, "No stale-instance call after independent planner reload");
Chainloader.PluginInfos[Planner.Guid].Instance = new WrongSchemaPlanner();
Check(!link.Ready(), "Changed method signature rejected");
Chainloader.PluginInfos[Planner.Guid].Instance = new ThrowingPlanner();
Check(link.Ready() && !link.Create(player,"wood_beam",new Vector3[1],new Quaternion[1],out _,out error) && error == "world changed", "Reflection invocation exceptions become useful failures");
var extended=new ExtendedPlanner();Chainloader.PluginInfos[Planner.Guid].Instance=extended;
Check(link.Extended&&link.Available(player),"Optional extended API discovered on independently reloaded planner");
Check(link.AtRay(player,new Vector3(),new Vector3(),out string ghostId,out string prefab,out _,out _,out float hit)&&ghostId=="ghost"&&prefab=="wood_beam"&&hit==3,"Ghost selection uses live public ray API");
Check(link.Create(player,"Mirror",new[]{"wood_beam","wood_pole"},new Vector3[2],new Quaternion[2],out _,out _)&&extended.Title=="Mirror"&&extended.Names[1]=="wood_pole","Mixed-piece shape title and prefabs preserved");
extended.InputAvailable=false;Check(!link.Available(player),"Concurrent planner placement blocks shape input");
extended.InputAvailable=true;Check(link.Available(player),"Input resumes after planner placement ends");
Chainloader.PluginInfos[Planner.Guid].Instance=new MockPlanner();
Check(link.Ready()&&!link.Extended&&link.Available(player)&&!link.AtRay(player,new Vector3(),new Vector3(),out _,out _,out _,out _,out _),"Original v1 planner retains Curve support without stale extended calls");
Console.WriteLine($"Passed {checks} curve geometry and planner reload/dependency checks.");

class OldPlanner : BepInEx.BaseUnityPlugin { }
class WrongSchemaPlanner : BepInEx.BaseUnityPlugin { public const int PlanningApiVersion = 1; }
class MockPlanner : BepInEx.BaseUnityPlugin
{
    public const int PlanningApiVersion = 1;
    public int Creates, Removes; public string Key = "first"; public bool Reject;
    public virtual bool TryCreateGhostPlan(Player p,string title,string[] names,Vector3[] poses,Quaternion[] rotations,out string key,out string error)
    { Creates++; key=Reject ? null : Key; error=Reject ? "protected" : null; return !Reject; }
    public bool TryRemoveGhostPlan(Player p,string key,out int removed,out string error) { Removes++; removed=2; error=null; return true; }
}
class ThrowingPlanner : MockPlanner
{
    public new const int PlanningApiVersion = 1;
    public override bool TryCreateGhostPlan(Player p,string title,string[] names,Vector3[] poses,Quaternion[] rotations,out string key,out string error)
    { key=null; error=null; throw new InvalidOperationException("world changed"); }
}

class ExtendedPlanner : MockPlanner
{
 public new const int PlanningApiVersion=1;
 public bool InputAvailable=true;public string Title;public string[] Names;
 public bool IsPlanningInputAvailable(Player p)=>InputAvailable;
 public bool TryGetGhostAtRay(Player p,Vector3 o,Vector3 d,out string id,out string prefab,out Vector3 pos,out Quaternion rot,out float distance)
 {id="ghost";prefab="wood_beam";pos=default;rot=default;distance=3;return InputAvailable;}
 public override bool TryCreateGhostPlan(Player p,string title,string[] names,Vector3[] poses,Quaternion[] rotations,out string key,out string error)
 {Title=title;Names=names;return base.TryCreateGhostPlan(p,title,names,poses,rotations,out key,out error);}
}
