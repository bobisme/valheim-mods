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
