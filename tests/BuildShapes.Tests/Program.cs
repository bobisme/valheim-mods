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
