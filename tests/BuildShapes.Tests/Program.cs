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

// Two endpoints and a rise define the center without manual off-axis bend placement.
foreach (double shift in new[] { 0.0, -10000, 10000 })
foreach (V3 delta in new[] { new V3(12,0,0), new V3(0,4,12), new V3(7,-2,9) })
foreach (double rise in new[] { 0.0, 0.017, 3.0, 6.0 })
{
    V3 a = new V3(shift,7,shift), c = a+delta;
    V3 middle = Arch.Middle(a,c,rise);
    Check(Near(middle,new V3(a.X+delta.X/2,a.Y+delta.Y/2+rise,a.Z+delta.Z/2)), "Arch midpoint pins the requested rise above sloped endpoints");
    Check(Near(Arch.Middle(c,a,rise),middle), "Reversing endpoints preserves the arch center");
    foreach(double t in new[] { 0.0, 0.125, 0.25, 0.5, 0.75, 0.875, 1.0 })
    {
        V3 expected = a+delta*t+new V3(0,4*rise*t*(1-t),0);
        Check(Near(Curve.At(a,middle,c,t),expected), "Arch follows an independently computed parabola in a vertical plane");
        if(delta.Y==0)Check(Math.Abs(Curve.At(a,middle,c,t).Y-Curve.At(a,middle,c,1-t).Y)<1e-8, "Level endpoints give symmetric arch heights");
    }
    foreach(double beam in new[] { 1.0, 2.0 })
    {
        var plan = Curve.Plan(a,middle,c,beam);
        Check(Near(plan[0].Start,a)&&Near(plan[^1].End,c), "Arch ghosts meet both fixed endpoints at all rises");
        Check(plan.All(p=>Math.Abs((p.End-p.Start).Length-beam)<1e-7), "Arch slider changes preserve native beam lengths");
    }
}
Check(Arch.DefaultRise(new V3(),new V3(8,9,0))==4, "Initial rise depends on horizontal span, not endpoint elevation");
Check(Arch.DefaultRise(new V3(),new V3(128,0,0))==Arch.MaximumRise, "Initial rise stays within the slider range");
Check(Near(Arch.Middle(new V3(),new V3(8,0,0),Arch.MaximumRise),new V3(4,32,0)), "Maximum rise remains usable");
Reject(()=>Arch.Middle(new V3(),new V3(0,8,0),2), "Vertical-only endpoints are not an arch span");
Reject(()=>Arch.Middle(new V3(),new V3(0.09,8,0),2), "Near-vertical arch span rejected");
Reject(()=>Arch.Middle(new V3(),new V3(129,0,0),2), "Oversized arch span rejected");
Reject(()=>Arch.Middle(new V3(double.NaN,0,0),new V3(8,0,0),2), "Nonfinite arch endpoints rejected");
foreach(double invalid in new[] { double.NaN, double.PositiveInfinity, -0.01, 32.01 })
    Reject(()=>Arch.Middle(new V3(),new V3(8,0,0),invalid), "Nonfinite or out-of-range arch heights rejected");

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
 }
}
var offsetProfile=MirrorProfile.Choose(new[]{new V3(0,0,0),new V3(4,0,0)},new V3(2.1,0,0));
Check(!offsetProfile.FlipZ&&offsetProfile.Centre==2,"Snap-derived origin compensation avoids decorative mesh-bound drift");
var fallback=MirrorProfile.Choose(Array.Empty<V3>(),new V3(2.1,0,0));
Check(!fallback.FlipZ&&fallback.Centre==2.1,"Pieces without snaps retain bounded mesh-centre fallback");

// Line: a straight run of native-length beams, its outer ends on the markers, at any slope or upright.
foreach (var (from, to) in new[] { (new V3(0,1,0), new V3(7,1,0)), (new V3(2,0,3), new V3(2,6,3)), (new V3(-1,2,-1), new V3(4,5,6)) })
{
    var run = Curve.Plan(from, (from + to) * 0.5, to, 2);
    V3 axis = (to - from) * (1 / (to - from).Length);
    Check(Near(run[0].Start, from) && Near(run[run.Count - 1].End, to), "A line's outer beams meet both markers");
    Check(run.All(s => Math.Abs((s.End - s.Start).Length - 2) < 1e-6), "Line beams keep their native length");
    Check(run.All(s => (s.Start - from - axis * V3.Dot(s.Start - from, axis)).Length < 1e-6 && (s.End - from - axis * V3.Dot(s.End - from, axis)).Length < 1e-6), "Every beam lies on the line");
    Check(run.Count == (int)Math.Ceiling((to - from).Length / 2 - 1e-9), "A line uses the fewest beams that reach");
}
Reject(() => Curve.Plan(new V3(), new V3(0.5,0,0), new V3(1,0,0), 2), "A line shorter than one beam asks for a shorter beam");

// Arch: the circle through its ends and top, ends leaning as it has them.
V3 skyward = new V3(0, 1, 0);
foreach (var (from, to, rise) in new[] { (new V3(0,0,0), new V3(6,0,0), 3.0), (new V3(0,0,0), new V3(8,0,2), 1.5), (new V3(0,1,0), new V3(6,3,0), 4.0) })
{
    var arc = Arch.Circular(from, to, rise);
    Check(Near(arc(0), from) && Near(arc(1), to), "An arc meets both ends");
    // Equally far from one centre all the way round: fit the circle through three of its points and check the rest.
    V3 a = arc(0), b = arc(0.37), c = arc(0.81), ab = b - a, ac = c - a, n = V3.Cross(ab, ac);
    V3 centre = a + (V3.Cross(n, ab) * V3.Dot(ac, ac) + V3.Cross(ac, n) * V3.Dot(ab, ab)) * (1 / (2 * V3.Dot(n, n)));
    double radius = (a - centre).Length;
    Check(Enumerable.Range(0, 41).All(i => Math.Abs((arc(i / 40.0) - centre).Length - radius) < 1e-6), "An arc is part of one circle");
    V3 top = Arch.Middle(from, to, rise);
    Check(Enumerable.Range(0, 2001).Min(i => (arc(i / 2000.0) - top).Length) < 0.01, "An arc passes through its top");
}
var shallow = Curve.Plan(Arch.Circular(new V3(), new V3(8,0,0), 1.5), 1); V3 lean = shallow[0].End - shallow[0].Start;
Check(V3.Dot(lean * (1 / lean.Length), skyward) < 0.8, "A shallow arc's ends lean");
var half = Curve.Plan(Arch.Circular(new V3(), new V3(6,0,0), 3), 0.5); V3 stand = half[0].End - half[0].Start;
Check(V3.Dot(stand * (1 / stand.Length), skyward) > 0.95, "A semicircular arc stands skyward at its ends, as an arch does");
var flat = Arch.Circular(new V3(), new V3(4,0,0), 0); Check(Near(flat(0.5), new V3(2,0,0)), "An arc with no height is straight");
Reject(() => Arch.Circular(new V3(), new V3(0,3,0), 1), "An arch needs its ends apart horizontally");

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
// Curve: a path through any number of markers, straight to fully curved, open or closed.
{
    var two = new List<V3>{ new V3(0,0,0), new V3(6,0,0) };
    var line = Curve.Plan(Spline.Path(two,false,1),2,false,false,Spline.MaximumLength);
    Check(line.Count==3 && Near(line[0].Start,two[0]) && Near(line[^1].End,two[1]), "Two markers make a straight line, ends met");
    foreach (Segment g in line) Check(Math.Abs(g.Start.Y)<1e-9 && Math.Abs(g.Start.Z)<1e-9 && Math.Abs(g.End.Z)<1e-9, "Two markers stay on the straight line");
    var pts = new List<V3>{ new V3(0,0,0), new V3(6,0,4), new V3(12,0,0), new V3(18,0,5), new V3(24,1,0) };
    foreach (double c in new[]{0.0,0.3,1.0})
    foreach (bool closed in new[]{false,true})
    {
        var path = Spline.Path(pts,closed,c);
        Check(Near(path(0),pts[0]) && Near(path(1),closed?pts[0]:pts[^1]), "Path starts at the first marker and ends at the last (or back at the first)");
        for (int i=0;i<Spline.Sides(pts.Count,closed);i++)
        {
            var side = Spline.Side(pts,closed,c,i);
            Check(Near(side(0),pts[i]) && Near(side(1),pts[(i+1)%pts.Count]), "Each side runs marker to marker");
            if (c==0) Check(Near(side(0.5),(pts[i]+pts[(i+1)%pts.Count])*0.5), "No curve: straight sides");
        }
    }
    // Smooth through a marker (the same direction either side) when curved; a corner when straight.
    V3 Dir(Func<double,V3> f,double t,double h){ V3 d=f(t+h)-f(t-h); return d*(1/d.Length); }
    var s1=Spline.Side(pts,false,1,0); var s2=Spline.Side(pts,false,1,1);
    Check(V3.Dot(Dir(s1,1-1e-4,1e-5),Dir(s2,1e-4,1e-5))>0.999, "A full curve passes smoothly through each marker");
    var c1=Spline.Side(pts,false,0,0); var c2=Spline.Side(pts,false,0,1);
    Check(V3.Dot(Dir(c1,0.5,0.01),Dir(c2,0.5,0.01))<0.9, "No curve keeps the corner");
    // Three evenly spaced markers on a parabola give that parabola.
    var para=new List<V3>{ new V3(-4,0,16), new V3(0,0,0), new V3(4,0,16) };
    var pp=Spline.Path(para,false,1);
    for (double t=0.05;t<1;t+=0.1){ V3 q=pp(t); Check(Math.Abs(q.Z-q.X*q.X)<0.6, "Three markers fit a natural curve"); }
    // Sides laid one by one at 0%: every corner is a piece's end.
    var square=new List<V3>{ new V3(0,0,0), new V3(8,0,0), new V3(8,0,8), new V3(0,0,8) };
    var walls=new List<Segment>();
    for (int i=0;i<4;i++) walls.AddRange(Curve.Plan(Spline.Side(square,true,0,i),2,true,false,Spline.MaximumLength));
    Check(walls.Count==16, "A closed 8 m square takes 16 two-metre walls");
    foreach (V3 corner in square) Check(walls.Count(w=>Near(w.Start,corner))==1 && walls.Count(w=>Near(w.End,corner))==1, "Each corner is where two sides' walls meet");
    // A smooth closed loop: evenly round, every piece native length, joints close.
    var loop=Curve.Plan(Spline.Path(square,true,1),2,false,true,Spline.MaximumLength);
    Check(loop.Count>=16 && loop.Count<=20, "A loop takes about its length in pieces");
    foreach (Segment g in loop) Check(Math.Abs((g.End-g.Start).Length-2)<1e-7, "Loop pieces keep native length");
    for (int i=0;i<loop.Count;i++) Check((loop[(i+1)%loop.Count].Start-loop[i].End).Length<0.5, "Loop pieces join all the way round");
    // Upright (level) pieces: spaced across the ground, each at the lower of its ends' heights.
    var slope=new List<V3>{ new V3(0,0,0), new V3(8,4,0) };
    var steps=Curve.Plan(Spline.Path(slope,false,1),2,true,false,Spline.MaximumLength);
    Check(steps.Count==4, "Upright pieces are spaced by their run across the ground");
    foreach (Segment g in steps) Check(Math.Abs(g.Start.Y-g.End.Y)<1e-9 && Math.Abs((g.End-g.Start).Length-2)<1e-7, "Upright segments are level and native length");
    Check(Math.Abs(steps[0].Start.Y)<1e-9 && Math.Abs(steps[3].Start.Y-3)<1e-6, "Each upright piece stands at the lower of its ends");
    Reject(()=>Curve.Plan(Spline.Path(new List<V3>{new V3(0,0,0),new V3(0,6,0)},false,1),2,true,false,Spline.MaximumLength), "Upright pieces need a shape that runs sideways");
    Reject(()=>Spline.Path(new List<V3>{new V3(0,0,0),new V3(1,0,0)},true,1), "A loop needs three markers");
    Reject(()=>Spline.Path(new List<V3>{new V3(0,0,0),new V3(0.05,0,0),new V3(3,0,0)},false,1), "Markers on top of each other rejected");
    Reject(()=>Spline.Path(pts,false,1.5), "Curve above 100% rejected");
    // Circle: level round the centre at the edge point's height, from the edge point; pieces evenly round it.
    var ring2=Spline.Circle(new V3(5,0,5),new V3(9,2,5));
    Check(Near(ring2(0),new V3(9,2,5)) && Near(ring2(1),new V3(9,2,5)), "A circle starts and ends at the edge point");
    for (double t=0;t<1;t+=0.05) Check(Math.Abs((ring2(t)-new V3(5,2,5)).Length-4)<1e-9, "Every point of a circle is the radius from its centre, at the edge's height");
    var circlePieces=Curve.Plan(ring2,2,false,true,Spline.MaximumLength);
    Check(circlePieces.Count==13 && circlePieces.All(g=>Math.Abs((g.End-g.Start).Length-2)<1e-7), "A 4 m circle takes 13 two-metre pieces");
    Reject(()=>Spline.Circle(new V3(),new V3(0.1,3,0)), "A circle needs an edge away from its centre");
    Reject(()=>Spline.Circle(new V3(),new V3(50,0,0)), "A circle's size is bounded");
    // A circle's plane: level, through both points (tilted as little as they need), or standing upright through them.
    V3 c0=new V3(0,0,0),e0=new V3(3,4,0),sideways=new V3(0,0,1);
    V3 nLevel=Spline.CircleNormal(c0,e0,Spline.CircleLevel,sideways),nThrough=Spline.CircleNormal(c0,e0,Spline.CircleThrough,sideways),nStand=Spline.CircleNormal(c0,e0,Spline.CircleStanding,sideways);
    Check(Near(nLevel,new V3(0,1,0)) && Near(Spline.CircleCentre(c0,e0,nLevel),new V3(0,4,0)), "A level circle sits at the edge's height");
    Check(Math.Abs(V3.Dot(nThrough,e0-c0))<1e-9 && Math.Abs(nThrough.Z)<1e-9 && nThrough.Y>0 && Near(Spline.CircleCentre(c0,e0,nThrough),c0), "A circle through both points tilts only along its radius, centred on the click");
    Check(Math.Abs(nStand.Y)<1e-9 && Math.Abs(V3.Dot(nStand,e0-c0))<1e-9 && Near(Spline.CircleCentre(c0,e0,nStand),c0), "A standing circle's plane is upright, through both points");
    Check(Near(Spline.CircleNormal(c0,new V3(4,0,0),Spline.CircleThrough,sideways),new V3(0,1,0)), "Level points make a level circle through them");
    V3 nAbove=Spline.CircleNormal(c0,new V3(0,3,0),Spline.CircleThrough,new V3(1,0,0));
    Check(Math.Abs(nAbove.Y)<1e-9 && Math.Abs(nAbove.X)<1e-9 && Math.Abs(nAbove.Length-1)<1e-9, "An edge straight above stands the ring across the camera's view");
    // Gaps (what Repeat did): as few pieces as fit with no more than the gap between, the gaps even, the outer ends on the ends.
    var straight10=new List<V3>{ new V3(0,0,0), new V3(10,0,0) };
    var spaced=Curve.Plan(Spline.Path(straight10,false,1),1,false,false,Spline.MaximumLength,2);
    Check(spaced.Count==4 && Near(spaced[0].Start,new V3(0,0,0)) && Near(spaced[^1].End,new V3(10,0,0)), "Ten metres of one-metre pieces with gaps up to 2 m take four, ends met");
    for (int i=1;i<spaced.Count;i++) Check(Math.Abs((spaced[i].Start-spaced[i-1].End).Length-2)<1e-6, "The gaps come out even");
    foreach (double g in new[]{0.1,0.5,1.5,4.0})
    {
        var pieces=Curve.Plan(Spline.Path(pts,false,1),1,false,false,Spline.MaximumLength,g);
        foreach (Segment piece in pieces) Check(Math.Abs((piece.End-piece.Start).Length-1)<1e-7, "Spaced pieces keep native length");
        Check(Near(pieces[0].Start,pts[0]) && Near(pieces[^1].End,pts[^1]), "Spaced pieces meet both ends of a curve");
        var ring=Curve.Plan(Spline.Path(square,true,1),1,true,true,Spline.MaximumLength,g);
        for (int i=0;i<ring.Count;i++) Check((ring[(i+1)%ring.Count].Start-ring[i].End).Length<=g+0.05, "Round a loop, no gap is over the chosen one");
    }
    var tight=Curve.Plan(Spline.Path(square,true,1),2,false,true,Spline.MaximumLength,3);
    Check(tight.Count>=3, "With gaps a tight loop still lays (pieces needn't meet)");
    Reject(()=>Curve.Plan(Spline.Path(straight10,false,1),1,false,false,Spline.MaximumLength,-1), "Negative gaps rejected");
}
Console.WriteLine($"Passed {checks} curve geometry and planner reload/dependency checks.");

class OldPlanner : BepInEx.BaseUnityPlugin { }
class WrongSchemaPlanner : BepInEx.BaseUnityPlugin { public const int PlanningApiVersion = 1; }
class MockPlanner : BepInEx.BaseUnityPlugin
{
    public const int PlanningApiVersion = 1;
    public const string Version = "1.10.1";
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
