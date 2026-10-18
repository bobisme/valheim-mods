using Homespun;

int checks=0;
void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
bool Near(double a,double b)=>Math.Abs(a-b)<1e-6;

// ---- The chain closes: everything a step takes is gathered, or made by another step ----
var gathered=new HashSet<string>{Names.Flax,Names.LoxPelt,Names.Fleece,"Blueberries","Raspberry","Dandelion","Thistle","Wood","Amber","LeatherScraps","DeerHide","WolfPelt"};
var made=new HashSet<string>(Chain.Conversions.Select(c=>c.To).Concat(Chain.AllMakes().Select(m=>m.Item)));
foreach(var c in Chain.Conversions)Check(gathered.Contains(c.From)||made.Contains(c.From),$"{c.Station} takes {c.From}, which nothing makes");
foreach(var m in Chain.AllMakes())foreach(var (res,_) in m.Needs)Check(gathered.Contains(res)||made.Contains(res),$"{m.Item} needs {res}, which nothing makes");
foreach(string item in new[]{Names.RettedFlax,Names.LineFlax,Names.Wool,Names.Yarn,Names.Thread,Names.LinenBolt,Names.WoolBolt,Names.Band})
    Check(made.Contains(item),$"Something makes {item}");
Check(Chain.Garments.All(g=>Chain.Makes.Any(m=>m.Item==g.Prefab&&m.Station==Names.Table)),"Every garment is sewn at the sewing table");
Check(Chain.AllMakes().GroupBy(m=>m.Station+"|"+m.Item).All(g=>g.Count()==1),"No recipe twice at one station");
Check(Chain.Conversions.GroupBy(c=>c.Station+"|"+c.From).All(g=>g.Count()==1),"One way to put each thing in at each station");

// ---- Yields and times ----
Check(Chain.Yield(Names.Wheel,Names.LineFlax)==2&&Chain.Yield(Names.Wheel,Names.Flax)==1,"Retted and combed, a flax spins into two threads; raw, into the game's one");
Check(Chain.Yield(Names.Brake,Names.LoxPelt)==4,"A lox pelt combs out into four wool");
Check(Near(Chain.PoolHolds*Chain.Of(Names.Pool,Names.Flax).Seconds,1800),"A full retting pool soaks a day (30 minutes)");
Check(Chain.Conversions.Where(c=>c.Station!=Names.Wheel).All(c=>c.Seconds>0),"Every step takes time");
foreach(var s in Chain.At(Names.Spindle))
{
    var w=Chain.Of(Names.Wheel,s.From);
    Check(w!=null&&w.To==s.To&&w.Yield==s.Yield&&w.Optional==s.Optional,$"The spindle spins {s.From} as the wheel does, only slower");
}
Check(Chain.Conversions.Where(c=>c.Optional).All(c=>c.From==Names.Fleece),"Only Byre's fleece is optional");

// ---- What a garment comes to, in what's gathered ----
var tunic=Chain.Raw(Names.Tunic);
Check(Near(tunic[Names.Fleece],17)&&Near(tunic[Names.Flax],0.5)&&tunic.Count==2,"A wool tunic: two bolts (16 yarn) and a band (half of 2 thread, 2 yarn): 17 fleece, half a flax ("+Chain.RawText(tunic)+")");
var lox=Chain.Raw(Names.Tunic,fleece:false);
Check(Near(lox[Names.LoxPelt],17/4.0)&&!lox.ContainsKey(Names.Fleece),"Without Byre, the yarn's wool is combed from lox pelts ("+Chain.RawText(lox)+")");
var shirt=Chain.Raw(Names.Undertunic);
Check(Near(shirt[Names.Flax],5)&&shirt.Count==1,"A linen undertunic: a bolt (8 thread) and 2 thread, two threads a flax: 5 flax ("+Chain.RawText(shirt)+")");
var blue=Chain.Raw("ArmorTunic1");
Check(Near(blue[Names.Flax],4)&&Near(blue["Blueberries"],5),"Hildir's blue tunic: a bolt of linen and blueberries ("+Chain.RawText(blue)+")");
Check(Chain.Hildir.Select(h=>h.item).Distinct().Count()==Chain.Hildir.Length,"Each of Hildir's clothes once");

// ---- The spindle ----
var have=new Dictionary<string,int>{[Names.Wool]=3,[Names.LineFlax]=1};
Check(Chain.SpindleNext(n=>have.TryGetValue(n,out int k)?k:0).From==Names.LineFlax,"The spindle spins flax before wool");
have.Remove(Names.LineFlax);
Check(Chain.SpindleNext(n=>have.TryGetValue(n,out int k)?k:0).From==Names.Wool,"Then wool");
Check(Chain.SpindleNext(n=>0)==null,"Nothing to spin");
var flax=Chain.Of(Names.Spindle,Names.LineFlax);
float spun=0;bool done=false;int frames=0;
while(!done&&frames<10000){(spun,done)=Chain.Spin(spun,0.5f,flax);frames++;}
Check(done&&frames==120,"A line flax spins in a minute by hand");
Check(Chain.Spin(5,-1,flax)==(5,false)&&Chain.Spin(5,1,null)==(5,false),"No time, or nothing to spin: no change");
Check(Chain.Span(65)=="1m 05s"&&Chain.Span(9.2f)=="10s","Spans");

// ---- Prefab names: a save finds a thing again by its prefab's name; none shipped may go ----
{
    var golden=File.ReadAllLines(Path.Combine(AppContext.BaseDirectory,"prefabs.txt")).Select(l=>l.Trim()).Where(l=>l!="").ToList();
    var shipped=PrefabNames.Shipped.ToList();
    Check(!golden.Except(shipped).Any(),"Every prefab name ever shipped is still in Core/PrefabNames.cs (gone: "+string.Join(", ",golden.Except(shipped))+")");
    Check(shipped.Distinct().Count()==shipped.Count,"No prefab name twice");
    Check(!shipped.Except(golden).Any(),"New prefab names are added to tests/Homespun.Tests/prefabs.txt too");
    Check(shipped.All(n=>n.StartsWith("Homespun_")),"Every prefab is named after the mod");
    var ours=typeof(Names).GetFields().Select(f=>(string)f.GetValue(null)!).Where(n=>n.StartsWith("Homespun_"));
    Check(ours.All(shipped.Contains),"Every Homespun_ name in Names is in the shipped list ("+string.Join(", ",ours.Except(shipped))+")");
    Check(Chain.Garments.All(g=>shipped.Contains(g.Prefab)),"Every garment is shipped");
}
Console.WriteLine($"Passed {checks} homespun checks.");
