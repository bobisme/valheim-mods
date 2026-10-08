using TrophyHall;

int checks=0;
void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}

Check(Policy.Themes.Select(t=>t.Perk).Distinct().Count()==Policy.Themes.Length,"Each perk belongs to one theme");
Check(Policy.Themes.SelectMany(t=>t.Trophies).Distinct().Count()==Policy.Themes.Sum(t=>t.Trophies.Length),"A trophy belongs to at most one theme");
Check(Policy.Themes.All(t=>t.Trophies.All(n=>n.StartsWith("Trophy")&&!Policy.Bosses.Contains(n))),"Themes use creature trophies, never bosses");
Check(!Policy.Counts("TrophyEikthyr",true)&&!Policy.Counts("TrophyFader",true),"Boss trophies stay on their altars");
Check(Policy.Counts("TrophySkeleton",true)&&!Policy.Counts("Wood",false)&&!Policy.Counts("",true),"Any creature trophy counts; other items do not");

Check(Policy.Perks(new[]{"TrophyBoar","TrophyBoar","TrophyBoar"}).Count==1,"Ten boar heads are one perk");
Check(Policy.Perks(new[]{"TrophyForestTroll","TrophyFrostTroll"}).Single().Perk==Perk.Carry,"Variants of a creature share its perk");
Check(Policy.Perks(new[]{"TrophySkeleton","TrophyGoblin"}).Count==0,"Trophies without a theme give no perk (they still add comfort)");
var all=Policy.Themes.SelectMany(t=>t.Trophies).ToList();
var full=Policy.Perks(all);
Check(full.Count==Policy.MaxThemes&&full.All(p=>p.Perk!=Perk.Speed),"At most six perks; the commonest (deer) drops out first");
Check(Policy.Perks(null).Count==0,"An empty hall gives nothing");

Check(Policy.Comfort(0)==0&&Policy.Comfort(3)==0&&Policy.Comfort(4)==1&&Policy.Comfort(7)==1&&Policy.Comfort(8)==2&&Policy.Comfort(40)==2,"Comfort: +1 at four kinds, +2 at eight, never more");

Check(Policy.Fallen(new List<Theme>())=="","No themed trophies, no line");
Check(Policy.Fallen(new[]{Policy.Themes[2]})=="Wolves have fallen here.","One kind");
Check(Policy.Fallen(new[]{Policy.Themes[0],Policy.Themes[2]})=="Trolls and wolves have fallen here.","Two kinds");
Check(Policy.Fallen(new[]{Policy.Themes[0],Policy.Themes[2],Policy.Themes[1]})=="Trolls, wolves and draugr have fallen here.","Three kinds");
Console.WriteLine($"Passed {checks} trophy theme, cap, comfort and reading checks.");
