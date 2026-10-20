using Byre;

int checks=0;
void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
const float Day=1800;
Species sheep=Species.Sheep,goat=Species.Goat,cow=Species.Cow,pig=Species.Pig,goose=Species.Goose;

// ---- the species table ----
foreach(Species s in Species.All)
{
    // (a product may be the game's own, the goose's Feathers; no tool: by hand; no body: the hen's puff of feathers)
    bool Ours(string n)=>n==null||n.StartsWith("Byre")&&PrefabNames.Shipped.Contains(n);
    Check(s.Prefab.StartsWith("Byre")&&s.Young.StartsWith("Byre")&&Ours(s.Ragdoll)&&Ours(s.Tool)&&Ours(s.Egg),s.Key+": every name is a Byre one, and shipped");
    Check(PrefabNames.Shipped.Contains(s.Prefab)&&PrefabNames.Shipped.Contains(s.Young),s.Key+": its prefabs are in the shipped list");
    Check((s.Ragdoll==null)==(s.RagdollFrom==null),s.Key+": a body made from a body");
    Check(s.Product==null?s.Tool==null&&s.Verb==null:s.Verb!=null&&s.Ready!=null&&s.NotReady!=null,s.Key+": something to harvest, and the words for it, or neither");
    Check(s.Product==null||s.Product.StartsWith("Byre")?Ours(s.Product):s.Tool==null,s.Key+": a game item as a product only by hand");
    Check(s.Scale>0&&s.YoungScale>0&&s.MaxAltitude>s.MinAltitude&&s.Drops.Length>0&&s.Drops.All(d=>d.Max>=d.Min&&d.Min>=1&&d.Chance>0&&d.Chance<=1),s.Key+": size, ground and drops make sense");
    Check(s.HerdMin>=1&&s.HerdMax>=s.HerdMin&&s.MaxTilt>s.MinTilt,s.Key+": a herd and its ground make sense");
    Check(s.Product==null||s.YieldMax>=s.YieldMin&&s.YieldMin>=1&&s.YieldHungry>=1&&s.YieldHungry<=s.YieldMin,s.Key+": fed gives at least as much as hungry");
    Check(s.Foods.Length>0&&s.Foods.Distinct().Count()==s.Foods.Length,s.Key+": eats something, each named once");
    Check(s.FedSeconds>0&&s.TamingSeconds>0&&s.GrowSeconds>0&&(s.Product==null||s.PeriodDays>0)&&s.FleeRange>0&&s.MaxNear>=2,s.Key+": timers and ranges are set");
    Check(Species.Of(s.Prefab)==s&&Species.Of(s.Young)==s&&s.IsYoung(s.Young)&&!s.IsYoung(s.Prefab)&&Species.ByKey(s.Key.ToUpper())==s,s.Key+": found by its prefabs and key");
}
Check(Species.Of("Boar")==null&&Species.Of(null)==null,"The game's boar isn't livestock of ours");
Check(goat.MinTilt>=sheep.MaxTilt/2&&goat.MaxTilt>sheep.MaxTilt,"Goats spawn on steeper ground than sheep");
Check(goat.Biome=="Mountain"&&sheep.Biome=="Meadows","Sheep in the Meadows, goats in the Mountains");
Check(sheep.Product=="ByreWool"&&sheep.Tool=="ByreShears"&&goat.Product=="ByreGoatMilk"&&goat.Tool=="ByreMilkPail","Shears for wool, the pail for milk");
Check(cow.Product=="ByreCowMilk"&&cow.Tool=="ByreMilkPail"&&cow.Commandable&&cow.Scale>1.5f,"A cow: milked with the same pail, big, led by the halter");
Check(goose.Product=="Feathers"&&goose.Tool==null&&goose.Egg=="ByreGooseEgg"&&goose.Guards&&goose.From=="Hen"&&goose.YoungScale<1,"A goose: plucked by hand, lays eggs, guards; a hen made over, its gosling small");
Check(pig.Product==null&&!pig.Wild&&pig.Roots&&pig.Commandable&&pig.From=="Boar","A pig: nothing to harvest, never wild, roots for truffles when led");
Check(Species.All.Count(s=>s.Wild)==4&&Species.All.Select(s=>s.Key).Distinct().Count()==Species.All.Length,"Four kinds come wild; keys are each once");
Check(sheep.ModelOf(false)=="sheep"&&sheep.ModelOf(true)=="sheep"&&goose.ModelOf(false)=="goose"&&goose.ModelOf(true)=="gosling","Each wears its model (a gosling its own)");

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

// ---- the goose's guard ----
Check(Husbandry.Stranger(true,false,false,true)&&!Husbandry.Stranger(true,true,false,true),"A player is a stranger until they've petted it");
Check(Husbandry.Stranger(false,false,false,false)&&!Husbandry.Stranger(false,false,true,false)&&!Husbandry.Stranger(false,false,false,true),"A wild creature is a stranger; a tame one, or one on the players' side, isn't");
Check(Husbandry.Chases(goose,true,false,true,5,false),"A tame goose goes for a stranger near");
Check(!Husbandry.Chases(goose,false,false,true,5,false)&&!Husbandry.Chases(goose,true,true,true,5,false)&&!Husbandry.Chases(sheep,true,false,true,5,false),"Not wild, not a gosling, not a sheep");
Check(!Husbandry.Chases(goose,true,false,true,Husbandry.GuardRange+0.1f,false)&&!Husbandry.Chases(goose,true,false,true,1,false)&&!Husbandry.Chases(goose,true,false,true,5,true)&&!Husbandry.Chases(goose,true,false,false,5,false),"Not too far, not already close, not resting, not a friend");
Check(Husbandry.KeepsChasing(4,1)&&!Husbandry.KeepsChasing(Husbandry.ChaseStop,1)&&!Husbandry.KeepsChasing(Husbandry.ChaseGiveUp,1)&&!Husbandry.KeepsChasing(4,Husbandry.ChaseSeconds),"It stops short, lets one go that got away, and gives up after a few seconds");

// ---- the pig's truffles ----
Check(Husbandry.Roots(pig,true,false,true,true,1e9,0,10),"A tame grown pig, led, in the forest: a lucky roll finds one");
Check(!Husbandry.Roots(pig,true,false,false,true,1e9,0,10)&&!Husbandry.Roots(pig,true,false,true,false,1e9,0,10)&&!Husbandry.Roots(pig,false,false,true,true,1e9,0,10)&&!Husbandry.Roots(pig,true,true,true,true,1e9,0,10)&&!Husbandry.Roots(sheep,true,false,true,true,1e9,0,10),"Only led, only in the forest, only tame grown pigs");
Check(!Husbandry.Roots(pig,true,false,true,true,Husbandry.RootGapSeconds-1,0,10),"Never two too close together");
Check(Husbandry.Roots(pig,true,false,true,true,1e9,0.099,10)&&!Husbandry.Roots(pig,true,false,true,true,1e9,0.1,10),"Once a minute, one in ten: about one each ten minutes");
{int found=0;for(int i=0;i<1000;i++)if(Husbandry.Roots(pig,true,false,true,true,1e9,i/1000.0,10))found++;Check(found==100,"The roll's share is one in the minutes");}

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
