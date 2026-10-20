using Shipyard;

int checks=0;
void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}

// ---- Hulls ----
var f=Hulls.Of(Hulls.Faering)!;
Check(f.Stages.Select(s=>s.Name).SequenceEqual(new[]{"keel","ribs","strakes","fit"}),"A færing goes keel, ribs, strakes, fittings");
Check(Hulls.All.All(h=>h.Stages.All(s=>s.Cost.Length>0&&s.Cost.All(c=>c.n>0))),"Every stage costs something");
Check(Hulls.Total(f)["Wood"]==50,"A færing is 50 wood in all");
Check(Hulls.All.All(h=>PrefabNames.Shipped.Contains(h.Prefab)),"Every hull's boat is shipped");

// ---- A build, paid in full stage by stage ----
var b=new Build{Hull=f};
Check(b.Current!.Name=="keel"&&b.Needs().Count==2,"Laid down: the keel needs wood and resin");
var t=b.Take(new[]{("Wood",4),("Stone",9)});
Check(t.Count==1&&t[0]==("Wood",4)&&b.Stage==0&&b.Had("Wood")==4,"Part of it in: 4 wood taken, stone not wanted, still the keel");
t=b.Take(new[]{("Wood",50),("Resin",1)});
Check(t.Contains(("Wood",6))&&t.Contains(("Resin",1))&&b.Stage==0,"Only what it needs: 6 more wood; one resin short, still the keel");
t=b.Take(new[]{("Resin",5)});
Check(t.Single()==("Resin",1)&&b.Stage==1&&b.Paid.Count==0&&b.Current!.Name=="ribs","The last resin in: the keel's done, the ribs begun");
var r=b.Refund();
Check(r["Wood"]==10&&r["Resin"]==2,"Taken down now: the keel's makings back");
b.Take(new[]{("Wood",5)});
r=b.Refund();
Check(r["Wood"]==15,"And what's in towards the ribs");

// ---- Kept in the ZDO ----
var back=Build.Load(b.Save());
Check(back.Hull==f&&back.Stage==1&&back.Had("Wood")==5&&back.Save()==b.Save(),"Saved and loaded: "+b.Save());
Check(Build.Load("").Empty&&Build.Load("nosuch|2|Wood:3").Empty,"Nothing, or a hull no longer known: empty");
Check(Build.Load("faering|99|").Finished,"A stage past the end: finished");
Check(Build.Load("faering|1|Wood:3,Bad,:2,Resin:x").Had("Wood")==3,"Bad entries are skipped");

// ---- Finished ----
var all=new Build{Hull=f};
foreach(var s in f.Stages)all.Take(s.Cost.Select(c=>(c.item,c.n)));
Check(all.Finished&&all.Current==null&&all.Needs().Count==0&&all.Take(new[]{("Wood",5)}).Count==0,"Every stage paid: finished, taking nothing more");
Check(all.Refund()["Wood"]==50,"A finished one taken down gives it all back");

// ---- Launching ----
Check(Launching.Spot(f,d=>d<6?0:d-6)==7+2.7f,"The first stretch along with 0.6 m under the keel, all the hull's length: its middle");
Check(Launching.Spot(f,d=>d>=3&&d<4?2:d<10?0:d-10)==11+2.7f,"A deep hole too short for the hull is passed over");
Check(Launching.Spot(f,d=>d<17?0:5)==17+2.7f&&Launching.Spot(f,d=>d<18.5f?0:5)==null,"Deep water too near the end of reach (18 m past the stocks, and the hull's length) to take the hull: no launch");
Check(Launching.Spot(f,d=>0)==null,"No water within reach: no launch");

// ---- The knarr ----
var k=Hulls.Of(Hulls.Knarr)!;
Check(k.Stages.Select(s=>s.Name).SequenceEqual(new[]{"keel","ribs","strakes","deck","mast"}),"A knarr goes keel, ribs, strakes, deck, mast");
Check(k.CargoWidth*k.CargoHeight>6*3*2,"The knarr's hold is more than twice the longship's (6x3)");
Check(Hulls.Total(k)["BronzeNails"]==120&&!Hulls.Total(k).ContainsKey("IronNails"),"Bronze-nailed: after the karve, before the longship");
Check(k.Draught>f.Draught&&k.KeelDepth>f.KeelDepth&&k.Length>f.Length,"Deeper, longer: needs deeper water to launch");
Check(Launching.Spot(k,d=>d<4?0:1.0f)==null&&Launching.Spot(k,d=>d<4?0:2f)==4+8,"Water deep enough for a færing isn't for a knarr");
Check(f.Cradle==null&&k.Cradle!=null,"The knarr stands on a cradle of its own on the stocks");

// ---- Sailing ----
Check(Sailing.WindFactor(f,0.1f)==1f&&Sailing.WindFactor(null!,1f)==1f,"Other hulls sail as the game has them");
Check(Sailing.WindFactor(k,0f)<0.65f&&Sailing.WindFactor(k,0.2f)<0.65f,"The knarr is slow in light airs");
Check(Sailing.WindFactor(k,1f)>1.2f&&Sailing.WindFactor(k,0.9f)>1.2f,"And strong in a gale (more than the longship's sail)");
for(float w=0;w<1;w+=0.05f)Check(Sailing.WindFactor(k,w+0.05f)>=Sailing.WindFactor(k,w),"More wind never less sail: "+w);
// Measured against the longship (sail force = lerp(0.25,1,w) * factor): the knarr's speed under sail (force ~ v², so v ~ sqrt) is under
// the longship's below a fresh breeze, and over it in a strong one
Check(Sailing.WindFactor(k,0.3f)<0.8f&&Sailing.WindFactor(k,0.8f)>1.1f,"Below the longship at 0.3 wind, above it at 0.8");

// ---- Prefab names ----
{
    var golden=File.ReadAllLines(Path.Combine(AppContext.BaseDirectory,"prefabs.txt")).Select(l=>l.Trim()).Where(l=>l!="").ToList();
    Check(!golden.Except(PrefabNames.Shipped).Any(),"Every prefab name ever shipped is still in Core/PrefabNames.cs");
    Check(!PrefabNames.Shipped.Except(golden).Any(),"New names are in tests/Shipyard.Tests/prefabs.txt too");
    Check(PrefabNames.Shipped.All(n=>n.StartsWith("Shipyard_")),"Every prefab is named after the mod");
}
Console.WriteLine($"Passed {checks} shipyard checks.");
