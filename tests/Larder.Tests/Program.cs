using Larder;

int checks=0;
void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}

// ---- the foods: against the game's (cooked meat 30/10, carrot soup 15/45, deer stew 45/15) and Byre's milk (12/30, 10/38) ----
foreach(Food f in Dairy.Foods)
{
    Check(f.Prefab.StartsWith("Larder_")&&PrefabNames.Shipped.Contains(f.Prefab),f.Title+": a Larder prefab, shipped");
    Check(f.Health>0&&f.Stamina>0&&f.Regen>0&&f.Seconds>=1200&&f.Weight>0&&f.Stack>=10&&f.Model!=null,f.Title+": eats as a food, stacks, has a look");
    Check(f.Health+f.Stamina<=70,f.Title+": no better than a Black Forest food all told");
    Check(f.Health+f.Stamina>12+30,f.Title+": better than the milk it's made from");
}
Food butter=Dairy.Foods.Single(f=>f.Prefab==Names.Butter),cheese=Dairy.Foods.Single(f=>f.Prefab==Names.Cheese),skyr=Dairy.Foods.Single(f=>f.Prefab==Names.Skyr);
Check(butter.Stamina>butter.Health&&cheese.Health>cheese.Stamina&&skyr.Health==skyr.Stamina,"Butter for stamina, cheese for health, skyr even");
Check(cheese.Seconds>=Dairy.Foods.Max(f=>f.Seconds),"Cheese, the keeping food, lasts longest");

// ---- the churn: butter from the cow's milk, skyr from the goat's, each listed once; the press takes either ----
var churned=Dairy.Churned().ToList();
Check(churned.Single(m=>m.Item==Names.Butter).Needs.Any(n=>n.item==Names.CowMilk)&&churned.Single(m=>m.Item==Names.Skyr).Needs.Any(n=>n.item==Names.GoatMilk),"Butter from the cow's milk, skyr from the goat's");
foreach(string milk in Names.Milks) Check(Dairy.Pressed().Any(p=>p.from==milk&&p.to==Names.Cheese),milk+": pressed into cheese");
Check(churned.All(m=>m.Station==Names.Churn&&m.Amount>=1&&m.Needs.All(n=>n.n>=1)),"Churned at the churn, something made from something");
Check(churned.Select(m=>m.Item).Distinct().Count()==churned.Count,"One recipe per food");
Check(Dairy.PressSeconds>=60&&Dairy.PressHolds>=10,"The press sets a cheese in a minute or more, and holds a pailful");
Check(Dairy.ChurnCost.Length>0&&Dairy.PressCost.Length>0,"The stations cost something");
Check(Dairy.Pieces.Concat(Dairy.Items).All(PrefabNames.Shipped.Contains),"Every piece and food is in the shipped list");

// ---- preserved foods (L1): against the game's (cooked meat 30/10 and deer 35/12 for 20 minutes, boar jerky 23/23 and wolf jerky 33/33 for 30,
// cooked fish 45/15, deer stew 45/15); the game's own smoked fish and moose (Deep North: 30/16 with 95 eitr, 105/35) are other foods ----
foreach (Food f in Preserves.Foods)
{
    Check(f.Prefab.StartsWith("Larder_")&&PrefabNames.Shipped.Contains(f.Prefab)&&Preserves.IsPreserved(f.Prefab),f.Title+": a Larder prefab, shipped, preserved");
    Check(f.Health>0&&f.Stamina>0&&f.Regen>0&&f.Weight>0&&f.Stack>=10&&f.Model!=null,f.Title+": eats as a food, stacks, has a look");
    Check(f.Seconds>=2400,f.Title+": lasts longer than any meat cooked fresh or jerky (a voyage's food)");
    Check(f.Health+f.Stamina>=40&&f.Health+f.Stamina<=60,f.Title+": about the cooked meat it's made from, all told, and short of a stew");
    Check(f.Health+f.Stamina<80,f.Title+": below the Swamp's serpent stew (80/26) and the game's own smoked moose (105/35)");
}
Check(Dairy.Foods.All(f=>!Preserves.IsPreserved(f.Prefab))&&!Preserves.IsPreserved(Preserved.Salt)&&!Preserves.IsPreserved("CookedMeat"),"Only the preserved foods are preserved");
Check(Preserves.Salt.Health==0&&Preserves.Salt.Stamina==0&&Preserves.Salt.Stack>=20,"Salt isn't eaten, and stacks deep");

// ---- the preserving stations ----
foreach (Works w in Preserves.Stations)
{
    Check(w.Name.StartsWith("Larder_")&&PrefabNames.Shipped.Contains(w.Name),w.Title+": a Larder piece, shipped");
    Check(w.From==Preserved.Kiln,w.Title+": made over from the charcoal kiln (the game's queue, its small fire)");
    Check(w.Holds>=8&&w.Seconds>=60&&w.Makes.Length>0&&w.Cost.Length>0,w.Title+": holds a batch, takes time, makes something, costs something");
    Check((w.Fuel==null)==(w.MaxFuel==0)&&(w.Fuel==null)==(w.FuelPer==0),w.Title+": a fuel and its numbers together, or neither");
    Check(w.AddText!=null&&w.EmptyText!=null&&w.Description!=null,w.Title+": says what to do with it");
}
Works smoke=Preserves.Stations.Single(w=>w.Name==Preserved.Smokehouse),pan=Preserves.Stations.Single(w=>w.Name==Preserved.SaltPan),
    barrel=Preserves.Stations.Single(w=>w.Name==Preserved.SaltBarrel),rack=Preserves.Stations.Single(w=>w.Name==Preserved.DryingRack);
Check(smoke.Fuel=="Wood"&&smoke.Smoke&&smoke.Makes.All(m=>Preserved.Meats.Contains(m.from)&&m.to==Preserved.SmokedMeat),"The smokehouse burns wood and smokes meat");
Check(pan.Needs==Needs.Sea&&pan.Makes.Single()==("Wood",Preserved.Salt),"The salt pan boils salt out of the sea with firewood");
Check(barrel.Fuel==Preserved.Salt&&barrel.FuelPer==1&&barrel.Makes.All(m=>Preserved.Meats.Contains(m.from)&&m.to==Preserved.SaltMeat),"The barrel cures meat with a salt each");
Check(rack.Needs==Needs.OpenSky&&rack.Fuel==null&&rack.Makes.Single()==(Preserved.Fish,Preserved.Stockfish),"The rack dries fish under the open sky, burning nothing");
Check(rack.Holds*rack.Seconds>=2*1800,"A full rack dries over days (a game day is 30 minutes)");
Check(Preserves.Stations.SelectMany(w=>w.Makes).Select(m=>m.to).Distinct().OrderBy(x=>x).SequenceEqual(Preserves.Items.OrderBy(x=>x)),"Every preserved food and the salt is made somewhere, and nothing else");
Check(Preserves.Pieces.Concat(Preserves.Items).All(PrefabNames.Shipped.Contains),"Every preserving piece and item is in the shipped list");

// ---- the bake oven (L2): flatbread, the keeping bread, against the game's oven bread (23/70, 25 minutes, 0.5 kg; a dvergr oven) ----
{
    Food bread=Baking.Flatbread,dough=Baking.Dough;
    Check(bread.Health>0&&bread.Stamina>0&&bread.Regen>0&&bread.Model!=null&&bread.Stack>=20,"Flatbread: eats as a food, stacks, has a look");
    Check(bread.Health<23&&bread.Stamina<70,"Flatbread: weaker than the game's oven bread");
    Check(bread.Seconds>1500&&bread.Weight<0.5f,"Flatbread: lasts longer and weighs less than it (what you take on a voyage)");
    Check(Preserves.IsPreserved(bread.Prefab)&&Baking.Keeps(bread.Prefab),"Flatbread keeps");
    Check(!Preserves.IsPreserved(dough.Prefab)&&dough.Health==0&&dough.Stamina==0,"Dough isn't eaten, and doesn't keep");
    Check(Baking.Mixed.Item==dough.Prefab&&Baking.Mixed.Station==Baked.Cauldron&&Baking.Mixed.Needs.Single()==(Baked.Flour,4)&&Baking.Mixed.Amount==2,"Dough is mixed at the cauldron, two flour a loaf");
    Check(Baking.Bakes().Single()==(dough.Prefab,bread.Prefab)&&Baking.BakeSeconds>=30&&Baking.Slots>=1,"The oven bakes the dough into flatbread");
    Check(Baking.OvenCost.All(c=>c.item=="Stone"||c.item=="Wood"),"The oven is only stone and wood (no iron, no surtling cores)");
    Check(Baking.Pieces.Concat(Baking.ItemNames).All(PrefabNames.Shipped.Contains),"The oven, the bread and the dough are in the shipped list");
}

// ---- spoilage (L3, a server setting, off by default): how long food keeps, and where it keeps longer ----
{
    Check(Spoilage.KindOf("RawMeat",false)==Kind.RawMeat&&Spoilage.KindOf("DeerMeat",false)==Kind.RawMeat&&Spoilage.KindOf("NeckTail",false)==Kind.RawMeat&&Spoilage.KindOf("FishRaw",false)==Kind.RawFish,"Raw meat and fish spoil, though the game's aren't eaten (materials)");
    Check(Spoilage.KindOf("CookedMeat",false)==Kind.Never&&Spoilage.KindOf("Wood",false)==Kind.Never,"Other things that aren't eaten never spoil");
    Check(Spoilage.KindOf("CookedMeat",true)==Kind.Cooked&&Spoilage.KindOf("NeckTailGrilled",true)==Kind.Cooked&&Spoilage.KindOf("FishCooked",true)==Kind.Cooked,"Cooked is cooked");
    Check(Spoilage.KindOf("DeerStew",true)==Kind.Dish&&Spoilage.KindOf("CarrotSoup",true)==Kind.Dish&&Spoilage.KindOf("LoxPie",true)==Kind.Dish&&Spoilage.KindOf("BloodPudding",true)==Kind.Dish,"Stews, soups, pies and puddings are dishes");
    Check(Spoilage.KindOf("LoxPieUncooked",true)!=Kind.Cooked,"An uncooked pie isn't cooked");
    Check(Spoilage.KindOf("Carrot",true)==Kind.Vegetable&&Spoilage.KindOf("Raspberry",true)==Kind.Berries&&Spoilage.KindOf("Bread",true)==Kind.Baked,"Vegetables, berries, bread");
    Check(Spoilage.KindOf(Names.GoatMilk,false)==Kind.Milk&&Spoilage.KindOf(Names.GoatMilk,true)==Kind.Milk&&Spoilage.KindOf(Names.Cheese,true)==Kind.Dairy&&Spoilage.KindOf(Names.Butter,true)==Kind.Dairy,"Byre's milk and Larder's dairy");
    foreach (string keeps in Preserves.Foods.Select(f=>f.Prefab).Append(Baked.Flatbread).Concat(new[]{"BoarJerky","WolfJerky","Honey","QueensJam","ChickenEgg",Spoilage.Spoiled,Preserved.Salt}))
        Check(Spoilage.KindOf(keeps,true)==Kind.Never&&!Spoilage.IsSpoiled(Spoilage.KindOf(keeps,true),1e12f),keeps+": never spoils");
    Check(Spoilage.KindOf("Wood",false)==Kind.Never&&Spoilage.KindOf("MeadHealthMinor",false)==Kind.Never,"What isn't food (or is drunk for what it does) never spoils");
    Check(Spoilage.KindOf("SomeModdedFood",true)==Kind.Other&&Spoilage.Days(Kind.Other)>0&&!float.IsInfinity(Spoilage.Days(Kind.Other)),"Another mod's food spoils at a middling rate");
    Check(Spoilage.Days(Kind.RawFish)<Spoilage.Days(Kind.RawMeat)&&Spoilage.Days(Kind.RawMeat)<Spoilage.Days(Kind.Cooked)&&Spoilage.Days(Kind.Cooked)<Spoilage.Days(Kind.Vegetable),"Fish goes first, then meat; cooked keeps longer; roots longest");
    Check(Spoilage.Days(Kind.Milk)<Spoilage.Days(Kind.Dairy),"Butter and cheese keep longer than the milk they're made from");
    foreach (Kind k in Enum.GetValues<Kind>().Where(k=>k!=Kind.Never)) Check(Spoilage.Days(k)>=1,k+": keeps at least a day (no food lost overnight)");
    Check(Spoilage.Rate(Kept.Open,false)==1&&Spoilage.Rate(Kept.Bag,false)==1,"Warm in the open or carried: the full rate");
    Check(Spoilage.Rate(Kept.Roofed,false)<1&&Spoilage.Rate(Kept.Cellar,false)<Spoilage.Rate(Kept.Roofed,false),"A roof slows it, a cellar more");
    foreach (Kept w in Enum.GetValues<Kept>()) Check(Spoilage.Rate(w,true)<Spoilage.Rate(w,false)&&Spoilage.Rate(w,true)>0,w+": cold slows it, never stops it");
    float meat=Spoilage.Life(Kind.RawMeat);
    Check(!Spoilage.IsSpoiled(Kind.RawMeat,Spoilage.Age(0,meat-1,1))&&Spoilage.IsSpoiled(Kind.RawMeat,Spoilage.Age(0,meat,1)),"Raw meat in a warm chest spoils after two days, not before");
    Check(!Spoilage.IsSpoiled(Kind.RawMeat,Spoilage.Age(0,meat*2,Spoilage.Rate(Kept.Cellar,false))),"In a cellar it lasts more than twice as long");
    Check(Spoilage.Age(100,-500,1)==100,"Time going back (a world's clock set back) counts as none");
    Check(Spoilage.Age(Spoilage.Age(0,600,1),600,0.5f)==900,"Its life adds up where it lay, a stretch at a time");
    Check(Spoilage.DaysLeft(Kind.RawMeat,0)==2&&Spoilage.DaysLeft(Kind.RawMeat,meat*2)==0,"Days left, down to none");
    float[] dug={2,2,2,2,2,2,0.2f,0.1f}, open={0,0,0,0.5f,0,0,0,0}, half={2,2,2,2,0,0,0,0};
    Check(Spoilage.IsCellar(0,dug,true),"A roofed room dug two metres into the ground, open on the stair side, is a cellar");
    Check(!Spoilage.IsCellar(0,dug,false),"Not without a roof");
    Check(!Spoilage.IsCellar(0,open,true)&&!Spoilage.IsCellar(0,half,true),"Not a house on the ground, nor a room dug into a slope on one side");
    Check(Spoilage.Freshness(Kind.RawMeat,0).StartsWith("Fresh")&&Spoilage.Freshness(Kind.RawMeat,meat*0.9f).StartsWith("Going off")&&Spoilage.Freshness(Kind.Never,0)=="Keeps","The tooltip says how fresh it is");
}

// ---- Prefab names: a save finds a thing again by its prefab's name; none shipped may go ----
{
    var golden=File.ReadAllLines(Path.Combine(AppContext.BaseDirectory,"prefabs.txt")).Select(l=>l.Trim()).Where(l=>l!="").ToList();
    var shipped=PrefabNames.Shipped.ToList();
    Check(!golden.Except(shipped).Any(),"Every prefab name ever shipped is still in Core/PrefabNames.cs (gone: "+string.Join(", ",golden.Except(shipped))+")");
    Check(shipped.Distinct().Count()==shipped.Count,"No prefab name twice");
    Check(!shipped.Except(golden).Any(),"New prefab names are added to tests/Larder.Tests/prefabs.txt too");
    Check(shipped.All(n=>n.StartsWith("Larder_")),"Every prefab is a Larder one");
}
Console.WriteLine($"Passed {checks} larder checks.");
