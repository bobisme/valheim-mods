using Byre;

int checks=0;
void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
const float Day=1800;
Species sheep=Species.Sheep,goat=Species.Goat;

// ---- the species table ----
foreach(Species s in Species.All)
{
    Check(s.Prefab.StartsWith("Byre")&&s.Young.StartsWith("Byre")&&s.Ragdoll.StartsWith("Byre")&&s.Product.StartsWith("Byre")&&s.Tool.StartsWith("Byre"),s.Key+": every name is a Byre one");
    Check(PrefabNames.Shipped.Contains(s.Prefab)&&PrefabNames.Shipped.Contains(s.Young)&&PrefabNames.Shipped.Contains(s.Ragdoll)&&PrefabNames.Shipped.Contains(s.Product)&&PrefabNames.Shipped.Contains(s.Tool),s.Key+": its prefabs are in the shipped list");
    Check(s.HerdMin>=1&&s.HerdMax>=s.HerdMin&&s.MaxTilt>s.MinTilt,s.Key+": a herd and its ground make sense");
    Check(s.YieldMax>=s.YieldMin&&s.YieldMin>=1&&s.YieldHungry>=1&&s.YieldHungry<=s.YieldMin,s.Key+": fed gives at least as much as hungry");
    Check(s.Foods.Length>0&&s.Foods.Distinct().Count()==s.Foods.Length,s.Key+": eats something, each named once");
    Check(s.FedSeconds>0&&s.TamingSeconds>0&&s.GrowSeconds>0&&s.PeriodDays>0&&s.FleeRange>0&&s.MaxNear>=2,s.Key+": timers and ranges are set");
    Check(Species.Of(s.Prefab)==s&&Species.Of(s.Young)==s&&s.IsYoung(s.Young)&&!s.IsYoung(s.Prefab)&&Species.ByKey(s.Key.ToUpper())==s,s.Key+": found by its prefabs and key");
}
Check(Species.Of("Boar")==null&&Species.Of(null)==null,"The game's boar isn't livestock of ours");
Check(goat.MinTilt>=sheep.MaxTilt/2&&goat.MaxTilt>sheep.MaxTilt,"Goats spawn on steeper ground than sheep");
Check(goat.Biome=="Mountain"&&sheep.Biome=="Meadows","Sheep in the Meadows, goats in the Mountains");
Check(sheep.Product=="ByreWool"&&sheep.Tool=="ByreShears"&&goat.Product=="ByreGoatMilk"&&goat.Tool=="ByreMilkPail","Shears for wool, the pail for milk");

// ---- regrowth ----
Check(Husbandry.Ready(sheep,0,100,Day,true)&&Husbandry.Ready(sheep,0,100,Day,false),"Never shorn: in full fleece");
double cut=10000;
Check(!Husbandry.Ready(sheep,cut,cut+Day,Day,true),"A day after shearing: not yet");
Check(Husbandry.Ready(sheep,cut,cut+2*Day,Day,true),"Two days fed: wool back");
Check(!Husbandry.Ready(sheep,cut,cut+2*Day,Day,false)&&Husbandry.Ready(sheep,cut,cut+4*Day,Day,false),"Hungry: twice as long");
Check(Husbandry.Ready(goat,cut,cut+Day,Day,true)&&!Husbandry.Ready(goat,cut,cut+Day-1,Day,true),"A fed goat milks once a day");
Check(Husbandry.Ready(sheep,cut,cut+0.5*Day+3*Day,Day,true),"A night slept through counts (the clock jumps, the wool grows)");
Check(Husbandry.Ready(sheep,cut,cut-500,Day,true),"A clock set back: never locked out");
Check(Math.Abs(Husbandry.Remaining(sheep,cut,cut+Day,Day,true)-Day)<1e-6&&Husbandry.Remaining(sheep,cut,cut+5*Day,Day,true)==0,"Remaining counts down to 0");
Check(Math.Abs(Husbandry.Remaining(sheep,cut,cut+Day,Day,false)-3*Day)<1e-6,"Remaining while hungry: the longer period");
Check(Husbandry.Period(sheep,3600,true)==2*3600,"A longer day (a mod's) stretches the period");

// ---- yields ----
Check(Husbandry.Yield(sheep,true,0)==2&&Husbandry.Yield(sheep,true,0.99)==3&&Husbandry.Yield(sheep,true,1)==3&&Husbandry.Yield(sheep,true,-1)==2,"Fed: two or three fleeces' worth");
Check(Husbandry.Yield(sheep,false,0.99)==1&&Husbandry.Yield(goat,false,0)==1,"Hungry: one");
int[] seen=new int[4];for(int i=0;i<100;i++)seen[Husbandry.Yield(sheep,true,i/100.0)]++;
Check(seen[2]==50&&seen[3]==50,"The roll splits evenly");
Check(Husbandry.CanHarvest(true,false)&&!Husbandry.CanHarvest(false,false)&&!Husbandry.CanHarvest(true,true),"Only grown tame animals");

// ---- running away, the trough ----
Check(Husbandry.Flees(sheep,false,7,false)&&!Husbandry.Flees(sheep,false,9,false)&&!Husbandry.Flees(sheep,true,1,false),"Wild sheep run from 8 m, tame ones never");
Check(!Husbandry.Flees(sheep,false,5,true)&&Husbandry.Flees(sheep,false,3,true),"Crouching halves it");
Check(Husbandry.SeeksTrough(true,true,10)&&!Husbandry.SeeksTrough(true,false,1)&&!Husbandry.SeeksTrough(false,true,1)&&!Husbandry.SeeksTrough(true,true,13),"Hungry tame ones near a trough walk to it");

// ---- the hover's hours ----
Check(Husbandry.Hours(75,Day)=="1 h"&&Husbandry.Hours(76,Day)=="2 h"&&Husbandry.Hours(Day,Day)=="1 d"&&Husbandry.Hours(Day+75*4,Day)=="1 d 4 h"&&Husbandry.Hours(-5,Day)=="0 h","Hours");
Check(Husbandry.Seconds(Husbandry.Ticks(1234.5))==1234.5,"Ticks and seconds");

// ---- Prefab names: a save finds a thing again by its prefab's name; none shipped may go ----
{
    var golden=File.ReadAllLines(Path.Combine(AppContext.BaseDirectory,"prefabs.txt")).Select(l=>l.Trim()).Where(l=>l!="").ToList();
    var shipped=PrefabNames.Shipped.ToList();
    Check(!golden.Except(shipped).Any(),"Every prefab name ever shipped is still in Core/PrefabNames.cs (gone: "+string.Join(", ",golden.Except(shipped))+")");
    Check(shipped.Distinct().Count()==shipped.Count,"No prefab name twice");
    Check(!shipped.Except(golden).Any(),"New prefab names are added to tests/Byre.Tests/prefabs.txt too");
    Check(shipped.All(n=>n.StartsWith("Byre")),"Every prefab is a Byre one");
}
Console.WriteLine($"Passed {checks} herd checks.");
