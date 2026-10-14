using Hallwright;
using BepInEx.Bootstrap;
using UnityEngine;

int checks = 0;
void Check(bool ok, string name) { checks++; if (!ok) throw new Exception(name); }

// The real reflection bridge through reloads, missing/old planners, and whole-building shells.
var link = new Planner(); var player = new Player();
Check(!link.Ready(), "Missing planner is harmless");
Chainloader.PluginInfos[Planner.Guid] = new BepInEx.PluginInfo { Instance = new OldPlanner() };
Check(!link.Ready(), "Old planner refuses activation");
var legacy = new MockPlanner(); Chainloader.PluginInfos[Planner.Guid].Instance = legacy;
Check(link.Ready() && link.Available(player), "Compatible planner discovered after Hallwright's Awake");
Check(link.Remove(player,"first",out int removed,out _) && removed == 2 && legacy.Removes == 1, "Undo uses the supported interface");
Chainloader.PluginInfos[Planner.Guid].Instance = null;
Check(!link.Ready(), "Transient null during F6 tolerated");
Chainloader.PluginInfos[Planner.Guid].Instance = new WrongSchemaPlanner();
Check(!link.Ready(), "Changed method signature rejected");
Chainloader.PluginInfos[Planner.Guid].Instance = legacy;
Check(link.CreateShell(player,"Hall",new string[256],new Vector3[256],new Quaternion[256],out _,out _)&&legacy.Creates==1,"Small shell uses original API in one call");
Check(!link.CreateShell(player,"Hall",new string[257],new Vector3[257],new Quaternion[257],out _,out string error)&&legacy.Creates==1&&error.Contains("whole-building"),"Large shell cannot be partially submitted to old planner");
Check(!link.CanCreateShell(1772,out error)&&error.Contains("1.10.1")&&error.Contains("draft stays open")&&legacy.Creates==1,"Preflight reports the loaded version and missing capability without creating partial ghosts");
var shells=new ShellPlanner();Chainloader.PluginInfos[Planner.Guid].Instance=shells;
Check(link.CanCreateShell(1772,out _)&&shells.Shells==0,"Planner-only replacement rebinds the capability without submitting the existing draft");
Check(link.WholeShell,"Whole-building API reported for status");
Check(link.CreateShell(player,"Hall",new string[600],new Vector3[600],new Quaternion[600],out string key,out _)&&key=="shell"&&shells.Shells==1&&shells.Creates==0,"Large shell uses optional whole-building method once");
var limited=new LimitedShellPlanner();Chainloader.PluginInfos[Planner.Guid].Instance=limited;
Check(!link.CanCreateShell(1772,out error)&&error.Contains("1024")&&limited.Shells==0,"Older whole-building capacity is rejected before excavation or native submission");
Chainloader.PluginInfos[Planner.Guid].Instance=legacy;
Check(!link.CreateShell(player,"Hall",new string[600],new Vector3[600],new Quaternion[600],out _,out _)&&shells.Shells==1,"Independent downgrade clears stale shell method");
Console.WriteLine($"Passed {checks} Hallwright planner reload/shell checks.");

class OldPlanner : BepInEx.BaseUnityPlugin { }
class WrongSchemaPlanner : BepInEx.BaseUnityPlugin { public const int PlanningApiVersion = 1; }
class MockPlanner : BepInEx.BaseUnityPlugin
{
    public const int PlanningApiVersion = 1;
    public const string Version = "1.10.1";
    public int Creates, Removes;
    public bool TryCreateGhostPlan(Player p,string title,string[] names,Vector3[] poses,Quaternion[] rotations,out string key,out string error)
    { Creates++; key="first"; error=null; return true; }
    public bool TryRemoveGhostPlan(Player p,string key,out int removed,out string error) { Removes++; removed=2; error=null; return true; }
}
class ShellPlanner : MockPlanner
{
 public new const int PlanningApiVersion=1;
 public const int MaximumShellPieces=2048;
 public int Shells;
 public bool TryCreateBuildingShell(Player p,string title,string[] names,Vector3[] poses,Quaternion[] rotations,out string key,out string error)
 {Shells++;key="shell";error=null;return true;}
}
class LimitedShellPlanner : ShellPlanner { public new const int PlanningApiVersion=1; public new const int MaximumShellPieces=1024; }
