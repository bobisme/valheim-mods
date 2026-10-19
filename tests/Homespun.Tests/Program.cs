using Homespun;

int checks=0;
void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
bool Near(double a,double b)=>Math.Abs(a-b)<1e-6;

// ---- The chain closes: everything a step takes is gathered, or made by another step ----
var gathered=new HashSet<string>{Names.Lichen,Names.Flax,Names.Nettle,Names.LoxPelt,Names.Fleece,"Blueberries","Raspberry","Dandelion","Thistle","Wood","Amber","LeatherScraps","DeerHide","WolfPelt"};
var made=new HashSet<string>(Chain.Conversions.Select(c=>c.To).Concat(Chain.AllMakes().Select(m=>m.Item)).Concat(Dye.Variants)); // (the vat makes the dyed)
foreach(var c in Chain.Conversions)Check(gathered.Contains(c.From)||made.Contains(c.From),$"{c.Station} takes {c.From}, which nothing makes");
foreach(var m in Chain.AllMakes())foreach(var (res,_) in m.Needs)Check(gathered.Contains(res)||made.Contains(res),$"{m.Item} needs {res}, which nothing makes");
foreach(string item in new[]{Names.RettedFlax,Names.LineFlax,Names.RettedNettle,Names.LineNettle,Names.Wool,Names.Yarn,Names.Thread,Names.LinenBolt,Names.WoolBolt,Names.Band})
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

// ---- Nettle: flax's way, at flax's pace, for half the thread ----
Check(Chain.Of(Names.Pool,Names.Nettle)?.To==Names.RettedNettle&&Chain.Of(Names.Brake,Names.RettedNettle)?.To==Names.LineNettle,"Nettle is retted in the pool and broken at the brake");
Check(Chain.Of(Names.Pool,Names.Nettle).Seconds==Chain.Of(Names.Pool,Names.Flax).Seconds&&Chain.Of(Names.Brake,Names.RettedNettle).Seconds==Chain.Of(Names.Brake,Names.RettedFlax).Seconds,"As long in the pool and at the brake as flax");
Check(Chain.Of(Names.Wheel,Names.LineNettle)?.To==Names.Thread&&Chain.Of(Names.Spindle,Names.LineNettle)?.To==Names.Thread,"Line nettle spins into linen thread, on the wheel and the spindle");
Check(Chain.Yield(Names.Wheel,Names.LineNettle)*2==Chain.Yield(Names.Wheel,Names.LineFlax)&&Chain.Yield(Names.Spindle,Names.LineNettle)*2==Chain.Yield(Names.Spindle,Names.LineFlax),"A nettle gives half a flax's thread");
Check(Chain.Of(Names.Spindle,Names.LineNettle).Seconds==Chain.Of(Names.Spindle,Names.LineFlax).Seconds,"A strick of either takes a minute by hand");
Check(Chain.At(Names.Wheel).Any(c=>c.From==Names.LineNettle),"The game's wheel takes line nettle (Wheel.Ensure adds whatever Chain.At gives it)");

// ---- What a garment comes to, in what's gathered ----
var tunic=Chain.Raw(Names.Tunic);
Check(Near(tunic[Names.Fleece],17)&&Near(tunic[Names.Flax],0.5)&&tunic.Count==2,"A wool tunic: two bolts (16 yarn) and a band (half of 2 thread, 2 yarn): 17 fleece, half a flax ("+Chain.RawText(tunic)+")");
var lox=Chain.Raw(Names.Tunic,fleece:false);
Check(Near(lox[Names.LoxPelt],17/4.0)&&!lox.ContainsKey(Names.Fleece),"Without Byre, the yarn's wool is combed from lox pelts ("+Chain.RawText(lox)+")");
var shirt=Chain.Raw(Names.Undertunic);
Check(Near(shirt[Names.Flax],5)&&shirt.Count==1,"A linen undertunic: a bolt (8 thread) and 2 thread, two threads a flax: 5 flax, never nettle, which gives less ("+Chain.RawText(shirt)+")");
var blue=Chain.Raw("ArmorTunic1");
Check(Near(blue[Names.Flax],4)&&Near(blue["Blueberries"],5),"Hildir's blue tunic: a bolt of linen and blueberries ("+Chain.RawText(blue)+")");
Check(Chain.Hildir.Select(h=>h.item).Distinct().Count()==Chain.Hildir.Length,"Each of Hildir's clothes once");

// ---- The spindle ----
var have=new Dictionary<string,int>{[Names.Wool]=3,[Names.LineFlax]=1};
Check(Chain.SpindleNext(n=>have.TryGetValue(n,out int k)?k:0).From==Names.LineFlax,"The spindle spins flax before wool");
have.Remove(Names.LineFlax);
have[Names.LineNettle]=2;
Check(Chain.SpindleNext(n=>have.TryGetValue(n,out int k)?k:0).From==Names.LineNettle,"Then line nettle");
have.Remove(Names.LineNettle);
Check(Chain.SpindleNext(n=>have.TryGetValue(n,out int k)?k:0).From==Names.Wool,"Then wool");
Check(Chain.SpindleNext(n=>0)==null,"Nothing to spin");
var flax=Chain.Of(Names.Spindle,Names.LineFlax);
float spun=0;bool done=false;int frames=0;
while(!done&&frames<10000){(spun,done)=Chain.Spin(spun,0.5f,flax);frames++;}
Check(done&&frames==120,"A line flax spins in a minute by hand");
Check(Chain.Spin(5,-1,flax)==(5,false)&&Chain.Spin(5,1,null)==(5,false),"No time, or nothing to spin: no change");
Check(Chain.Span(65)=="1m 05s"&&Chain.Span(9.2f)=="10s","Spans");

// ---- Dyeing: a dyed thing is its own item; the vat dyes what its one dyestuff runs to ----
{
    Check(Dye.Read(Names.WoolBolt)==(Names.WoolBolt,null),"An undyed bolt reads as undyed");
    var blueD=Dye.Of("Blue")!;var redD=Dye.Of("Red")!;
    Check(Dye.Read("Homespun_WoolTunic_Blue")==(Names.Tunic,blueD),"A blue tunic reads as a tunic, blue");
    Check(Dye.Read("Homespun_WoolTunic_Green")==null&&Dye.Read("Flax")==null&&Dye.Read("ArmorTunic1")==null,"An unknown colour, flax and Hildir's clothes aren't ours to dye");
    Check(Dye.Into(Names.WoolBolt,blueD)=="Homespun_WoolBolt_Blue","A wool bolt dyed blue");
    Check(Dye.Into("Homespun_WoolTunic_Blue",redD)=="Homespun_WoolTunic_Red","A blue tunic dyed red again");
    Check(Dye.Into("Homespun_WoolTunic_Blue",blueD)==null,"Blue into blue: nothing to do");
    Check(Dye.Measures("Homespun_HoodedCloak_Red")==3&&Dye.Measures(Names.LinenBolt)==1,"A cloak takes three measures, a bolt one");
    foreach(var m in Chain.Makes.Where(m=>m.Station==Names.Table))
    {
        int bolts=m.Needs.Where(n=>n.item==Names.LinenBolt||n.item==Names.WoolBolt).Sum(n=>n.n);
        Check(bolts==0||Dye.Measures(m.Item)==bolts,$"A garment takes a measure of dye for each bolt in it ({m.Item})");
    }
    Check(Dye.Variants.Count()==28&&Dye.Variants.All(PrefabNames.Shipped.Contains),"Every dyed thing is shipped (7 things, 4 colours)");
    Check(new[]{Names.Vat,Names.Lichen,Names.WildLichen}.All(PrefabNames.Shipped.Contains),"The vat and the lichen are shipped");
    Check(Dye.Title("Bolt of wool",blueD)=="Bolt of blue wool"&&Dye.Title("Wool tunic",redD)=="Red wool tunic"&&Dye.Title("Hooded cloak",Dye.Of("Purple")!)=="Purple hooded cloak","Names");
    Check(!Dye.Description("Loose trousers of grey-brown wool, gathered below the knee.",blueD).Contains("grey-brown"),"A dyed thing isn't said to be undyed");

    var b=Dye.Batch(new List<(string,int)>{(Names.WoolBolt,3),("Blueberries",12),(Names.Tunic,1)});
    Check(b.dye==blueD&&b.dyed.Count==1&&b.dyed[0]==(0,2,"Homespun_WoolBolt_Blue")&&b.stuffUsed==10,"12 blueberries dye two of three bolts, and not the tunic (two measures) after");
    b=Dye.Batch(new List<(string,int)>{(Names.Tunic,1),("Blueberries",15),(Names.LinenBolt,1)});
    Check(b.dyed.Count==2&&b.dyed[0]==(0,1,"Homespun_WoolTunic_Blue")&&b.dyed[1]==(2,1,"Homespun_LinenBolt_Blue")&&b.stuffUsed==15,"15: the tunic, then the bolt");
    Check(Dye.Batch(new List<(string,int)>{(Names.WoolBolt,1),("Blueberries",5),("Raspberry",5)}).dyed.Count==0,"Two dyestuffs: nothing");
    Check(Dye.Batch(new List<(string,int)>{("Homespun_WoolBolt_Blue",2),("Blueberries",20)}).dyed.Count==0,"Blue already: nothing");
    b=Dye.Batch(new List<(string,int)>{("Homespun_WoolBolt_Blue",2),(Names.Lichen,6)});
    Check(b.dyed.Count==1&&b.dyed[0].n==2&&b.dyed[0].into=="Homespun_WoolBolt_Purple"&&b.stuffUsed==6,"Lichen: 3 a measure, purple over blue");
    Check(Dye.Wants(new List<(string,int)>{(Names.WoolBolt,1),("Blueberries",5)})==null,"A bolt and five blueberries: ready");
    Check(Dye.Wants(new List<(string,int)>{(Names.WoolBolt,1),("Blueberries",4)})!.StartsWith("Not dye enough"),"Four: not enough");
    Check(Dye.Wants(new List<(string,int)>())!.StartsWith("Put in cloth"),"Empty: what it wants");
    Check(Dye.Wants(new List<(string,int)>{(Names.WoolBolt,1),("Blueberries",5),("Dandelion",5)})!.StartsWith("Two dyes"),"Two dyes");

    var dyedTunic=Chain.AllMakes().Single(m=>m.Item=="Homespun_WoolTunic_Red");
    Check(dyedTunic.Station==Names.Table&&dyedTunic.Needs.Contains(("Homespun_WoolBolt_Red",2))&&dyedTunic.Needs.Contains((Names.Band,1)),"A red tunic is sewn from red bolts and an undyed band");
    Check(Chain.AllMakes().Count(m=>Dye.Read(m.Item)?.dye!=null)==20,"Each of the five garments in each of the four colours at the sewing table");
    var redTunic=Chain.Raw("Homespun_WoolTunic_Red");
    Check(Near(redTunic["Raspberry"],10)&&Near(redTunic[Names.Fleece],17),"A red tunic: two bolts' fleece, and 10 raspberries ("+Chain.RawText(redTunic)+")");
}

// ---- Tapestries: hung pieces woven from dyed bolts ----
{
    Check(new[]{Names.Frieze,Names.Hanging}.All(PrefabNames.Shipped.Contains),"The tapestries are shipped");
    Check(Dye.Variants.Contains("Homespun_WoolBolt_Yellow")&&Dye.Variants.Contains("Homespun_WoolBolt_Red")&&Dye.Variants.Contains("Homespun_WoolBolt_Blue"),"The dyed bolts the tapestries take are made");
}
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
    Check(new[]{Names.WildNettle,Names.NettleCrop,Names.NettleSapling}.All(shipped.Contains),"The nettle plants are shipped (Plants.cs)");
}
Console.WriteLine($"Passed {checks} homespun checks.");
