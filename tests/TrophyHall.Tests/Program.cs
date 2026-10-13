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
var seen=Policy.ParseSeen(null);
Check(Policy.Announce(seen,"10:20",5),"A new hall is read out");
seen=Policy.ParseSeen(Policy.Remember(seen,"10:20",5));
Check(!Policy.Announce(seen,"10:20",5)&&!Policy.Announce(seen,"10:20",3),"The same hall is not read out again");
Check(Policy.Announce(seen,"10:20",6),"A hall with more trophies is read out again");
Check(Policy.ParseSeen(Policy.Remember(seen,"10:20",3))["10:20"]==5,"Fewer trophies in reach don't lower the record");
Check(Policy.ParseSeen("-40:80=2;bad;=3;x=y")["-40:80"]==2&&Policy.ParseSeen("-40:80=2;bad;=3;x=y").Count==1,"Bad records are skipped");
string many="";for(int i=0;i<100;i++)many=Policy.Remember(Policy.ParseSeen(many),i+":0",1);
Check(Policy.ParseSeen(many).Count==Policy.MaxSeen&&Policy.ParseSeen(many).ContainsKey("99:0")&&!Policy.ParseSeen(many).ContainsKey("0:0"),"Only the latest halls are kept");
Console.WriteLine($"Passed {checks} trophy theme, cap, comfort and reading checks.");
