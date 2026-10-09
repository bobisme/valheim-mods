using BuildShapes;
int checks=0;
void Check(bool ok,string label){checks++;if(!ok)throw new Exception(label);}
void Reject(Action action,string label){try{action();}catch(ArgumentException){checks++;return;}throw new Exception("Accepted: "+label);}
var owned=new TerrainComp{SavedHeight=12};owned.View.Owner=true;
Plugin.Own(owned,true);Check(owned.View.Claims==0&&owned.Loads==1&&owned.Height==12,"Owned terrain is refreshed without unnecessary transfer");
var remote=new TerrainComp{SavedHeight=7};remote.View.OnClaim=()=>{remote.SavedHeight=9;remote.DataRevision++;};
Plugin.Own(remote,true);Check(remote.View.Owner&&remote.View.Claims==1,"Another peer's terrain is acquired using the native view");
Check(remote.View.Events.SequenceEqual(new[]{"load","claim","load"})&&remote.Height==9,"Load before transfer and refresh newer synchronized data before capture");
remote.View.Owner=false;int calls=remote.Loads,claims=remote.View.Claims;
Reject(()=>Plugin.Own(remote),"Mid-operation owner loss rejects the save");
Check(remote.Loads==calls&&remote.View.Claims==claims,"Save guard never reacquires and overwrites data behind an active operation");
var denied=new TerrainComp();denied.View.AllowClaim=false;
Reject(()=>Plugin.Own(denied,true),"Failed acquisition is reported");Check(denied.View.Claims==1&&denied.Loads==1,"Failed transfer stops before writable preparation");
var invalid=new TerrainComp();invalid.View.Valid=false;
Reject(()=>Plugin.Own(invalid,true),"Invalid view rejected before claim");Check(invalid.View.Claims==0&&invalid.Loads==0,"Invalid network terrain stays untouched");
Reject(()=>Plugin.Own(null,true),"Missing terrain compiler rejected");
var read=new TerrainComp{SavedHeight=3};Plugin.Read(read);
Check(!read.View.Owner&&read.View.Claims==0&&read.Height==3,"Restoration can inspect synchronized heights before taking ownership");
Console.WriteLine($"Passed {checks} production terrain ownership checks (network view is stubbed).");
