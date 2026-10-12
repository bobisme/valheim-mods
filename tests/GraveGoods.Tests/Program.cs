using GraveGoods;

int checks=0;
void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
var rng=new Random(7);

// Chests: tier from the biome, mostly kept, sometimes one off
Check(Policy.BiomeTier("Meadows")==1&&Policy.BiomeTier("Swamp")==3&&Policy.BiomeTier("AshLands")==7&&Policy.BiomeTier("Unknown")==1,"Biomes give tiers");
int same=0;for(int i=0;i<5000;i++){int t=Policy.ChestTier(4,rng);Check(t>=3&&t<=5,"A chest's tier stays within one");if(t==4)same++;}
Check(same>3200&&same<3800,"About 70% of chests keep their biome's tier");
for(int i=0;i<500;i++){Check(Policy.ChestTier(1,rng)>=1&&Policy.ChestTier(7,rng)<=7,"Tiers stay 1 to 7");}

// Rarity by weight: rare things rare
double[] w={40,33,18,7,2};
var counts=new int[5];for(int i=0;i<100000;i++)counts[(int)Policy.RollRarity(w,rng)]++;
Check(counts[0]>counts[1]&&counts[1]>counts[2]&&counts[2]>counts[3]&&counts[3]>counts[4],"Each rarity is rarer than the last");
Check(counts[4]>1500&&counts[4]<2500,"About 2% are saga finds");
Check(Policy.RollRarity(new double[]{1,0,0,0,0},rng,bump:2)==Rarity.Runed&&Policy.RollRarity(new double[]{0,0,0,0,1},rng,bump:3)==Rarity.Saga,"A bump raises the rarity, up to saga");

// Quality: rarer finds better kept, never past the item's own top
for(int i=0;i<500;i++){int q=Policy.RollQuality(Rarity.Saga,4,rng);Check(q==4,"A saga find is at full quality");}
for(int i=0;i<500;i++){int q=Policy.RollQuality(Rarity.Heirloom,1,rng);Check(q==1,"Never above the item's own top quality");}
for(int i=0;i<500;i++){int q=Policy.RollQuality(Rarity.Plain,4,rng);Check(q>=1&&q<=2,"Plain finds are low quality");}

// Rolls: counts, fitting slots, no doubles, one element at most
for(int i=0;i<3000;i++)
{
    var r=(Rarity)(i%5);var slot=new[]{Slot.Weapon,Slot.Armor,Slot.Shield}[i%3];int tier=1+i%7;
    var g=Policy.Roll(r,tier,slot,"Swords",new[]{"Helmet","Chest","Legs","Shoulder"}[i%4],rng);
    if(r!=Rarity.Saga)Check(g.Affixes.Count==Policy.AffixCount[(int)r],"Each rarity has its number of powers");
    Check(g.Affixes.Select(a=>a.Id).Distinct().Count()==g.Affixes.Count,"No power twice");
    Check(g.Affixes.Count(a=>Policy.Elements.Contains(a.Id))<=1,"One element at most");
    foreach(var a in g.Affixes.Where(a=>g.Name==""||!Policy.Sagas.Any(s=>s.Name==g.Name)))Check((Policy.Def(a.Id).Slots&slot)!=0,"Powers fit the kind of item");
    Check(g.Affixes.All(a=>Policy.Def(a.Id).MinTier<=tier||Policy.Sagas.Any(s=>s.Name==g.Name)),"Deep powers only from deep chests");
    if(r==Rarity.Heirloom)Check(g.Name!="","Heirlooms have names");
    if(r<=Rarity.Runed)Check(g.Name=="","Lesser finds are named from their powers");
    foreach(var a in g.Affixes.Where(a=>a.Id=="wskill"))Check(a.Arg=="Swords","A weapon's skill power is for its own skill");
}
var saga=Policy.Roll(Rarity.Saga,5,Slot.Weapon,"Swords","",rng);
Check(Policy.Sagas.Any(s=>s.Name==saga.Name&&s.Class=="Swords")&&saga.Saga!=""&&saga.Affixes.Count>=4,"A saga sword is one of the sagas' swords, with its lore and its own powers and two more");
var none=Policy.Roll(Rarity.Saga,5,Slot.Weapon,"Pickaxes","",rng);
Check(none.Rarity==Rarity.Heirloom&&none.Name!=""&&none.Affixes.Count==3,"A kind the sagas never sang of comes as the best heirloom");
var deep=Policy.Roll(Rarity.Saga,7,Slot.Weapon,"Swords","",new Random(1));var shallow=Policy.Roll(Rarity.Saga,1,Slot.Weapon,"Swords","",new Random(1));
Check(deep.Name==shallow.Name,"Same luck, same saga");

// Each saga once per world, until all of its kind are found
var found=new HashSet<string>();
for(int i=0;i<5;i++){var s5=Policy.Roll(Rarity.Saga,4,Slot.Weapon,"Swords","",rng,n=>found.Contains(n));Check(found.Add(s5.Name),"A saga not yet found comes first");}
Check(found.Count==Policy.Sagas.Count(s=>s.Class=="Swords"),"Every saga sword found once");
var again=Policy.Roll(Rarity.Saga,4,Slot.Weapon,"Swords","",rng,n=>found.Contains(n));Check(found.Contains(again.Name),"Once all are found, they can come again");
Check(Policy.SagaKey("Gunnar's Atgeir")=="bob_gravegoods_saga_gunnarsatgeir"&&Policy.SagaKey("Ægishjálmur")=="bob_gravegoods_saga_gishjalmur"&&Policy.SagaKey("Hermóðr's Leggings")=="bob_gravegoods_saga_hermorsleggings","Saga keys are plain letters");
Check(Policy.Sagas.Select(s=>Policy.SagaKey(s.Name)).Distinct().Count()==Policy.Sagas.Length,"Every saga has its own key");

// Flat powers grow with depth; percentages don't
var hp1=Enumerable.Range(0,400).Select(i=>Policy.Roll(Rarity.Heirloom,1,Slot.Armor,"","Chest",new Random(i))).SelectMany(g=>g.Affixes).Where(a=>a.Id=="hp").Select(a=>a.Value).DefaultIfEmpty(0).Max();
var hp7=Enumerable.Range(0,400).Select(i=>Policy.Roll(Rarity.Heirloom,7,Slot.Armor,"","Chest",new Random(i))).SelectMany(g=>g.Affixes).Where(a=>a.Id=="hp").Select(a=>a.Value).DefaultIfEmpty(0).Max();
Check(hp1>=4&&hp1<=9&&hp7>hp1*3,"Health grows with the tier");

// Names and lines
var fine=new Goods{Rarity=Rarity.Fine};fine.Affixes.Add(new Affix("fire",0.2));
Check(Policy.Title(fine,"Iron sword")=="Burning Iron sword","A fine find takes its power's prefix");
var runed=new Goods{Rarity=Rarity.Runed};runed.Affixes.Add(new Affix("dmg",0.1));runed.Affixes.Add(new Affix("leech",0.03));
Check(Policy.Title(runed,"Iron sword")=="Keen Iron sword of the Raven","A runed find takes a prefix and a suffix");
Check(Policy.Title(new Goods(),"Club")=="Club","A plain find is its kind");
Check(Policy.Line(new Affix("dmg",0.147))=="+15% damage"&&Policy.Line(new Affix("hp",12))=="+12 health"&&Policy.Line(new Affix("hp",-15))=="-15 health","Lines read well, curses too");
Check(Policy.Line(new Affix("wskill",8,"BloodMagic"))=="+8 Blood Magic"&&Policy.Line(new Affix("stam",0.2))=="-20% stamina to swing","Skill and reduction lines");
for(int i=0;i<200;i++){string k=Policy.Kenning((Slot)(1<<(i%3)),rng);Check(k.Length>5&&!k.Contains("--"),"Kennings read as names");}

// Save and load
for(int i=0;i<500;i++)
{
    var g=Policy.Roll((Rarity)(i%5),1+i%7,new[]{Slot.Weapon,Slot.Armor,Slot.Shield}[i%3],"Axes","Helmet",rng);
    var back=Policy.Load(Policy.Save(g));
    Check(back!=null&&back.Rarity==g.Rarity&&back.Tier==g.Tier&&back.Name==g.Name&&back.Saga==g.Saga&&back.Affixes.Count==g.Affixes.Count,"Saved and loaded alike");
    for(int j=0;j<g.Affixes.Count;j++)Check(back.Affixes[j].Id==g.Affixes[j].Id&&back.Affixes[j].Value==g.Affixes[j].Value&&back.Affixes[j].Arg==g.Affixes[j].Arg,"Powers kept exactly");
}
Check(Policy.Load("")==null&&Policy.Load("2|0|1|||")==null&&Policy.Load("junk")==null,"Bad data is no grave goods");
var odd=new Goods{Name="A|b,c:d"};Check(Policy.Load(Policy.Save(odd)).Name=="A|b,c:d","Any name survives the record");
Check(Policy.Load("1|1|3|||dmg:0.1:,nosuch:3:,hp:x:").Affixes.Count==1,"Unknown or broken powers are skipped");
Check(Math.Abs(Policy.Load("1|2|3|||dmg:0.1:,dmg:0.05:").Sum("dmg")-0.15)<1e-9,"Sums add up");

// Recipes
Check(Policy.RecipeTier(new[]{"Wood","Bronze","LeatherScraps"})==2&&Policy.RecipeTier(new[]{"BlackMetal","LinenThread"})==5&&Policy.RecipeTier(new[]{"Mystery"})==0,"A recipe's tier is its deepest ingredient");

Console.WriteLine($"GraveGoods: {checks} checks passed");
