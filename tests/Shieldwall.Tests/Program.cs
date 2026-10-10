using Shieldwall;

int checks=0;
void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}

// The stone's marks and ward.
Check(Policy.Marks(-3)==0&&Policy.Marks(4)==4&&Policy.Marks(99)==Policy.MaxMarks,"Marks stay between none and ten");
Check(Policy.Strength(5,true)==3&&Policy.Strength(1,true)==0&&Policy.Strength(5,false)==5,"A cracked stone works two marks lower, never below none");
Check(Policy.Numeral(1)=="I"&&Policy.Numeral(4)=="IV"&&Policy.Numeral(10)=="X"&&Policy.Numeral(0)=="0","Roman numerals");
Check(Policy.Title(0)=="Unblooded"&&Policy.Title(1)=="Blooded"&&Policy.Title(10)=="Legendary","Titles");
for(int t=0;t<=Policy.MaxMarks;t++)
{
    WardStats hearth=Policy.WardOf(t,true),open=Policy.WardOf(t,false);
    Check(hearth.Any&&open.Any,$"Even a new stone wards ({t})");
    Check(open.HealthRegen<hearth.HealthRegen&&open.Carry<=hearth.Carry&&open.Comfort==0,$"The open field is half the hearth ({t})");
    Check(Math.Abs((open.HealthRegen-1)*2-(hearth.HealthRegen-1))<1e-4,$"Exactly half the regeneration ({t})");
    if(t>0){WardStats before=Policy.WardOf(t-1,true);Check(hearth.HealthRegen>before.HealthRegen&&hearth.Carry>=before.Carry&&hearth.Comfort>=before.Comfort,$"Every mark helps ({t})");}
}
Check(Policy.WardOf(0,true).Carry==0&&Policy.WardOf(5,true).Carry==50&&Policy.WardOf(10,true).Armor==10&&Policy.WardOf(5,true).Armor==0,"Carry from the first mark, armor from the sixth");
Check(Policy.WardOf(10,true).HealthRegen<=1.36f,"Regeneration stays modest");
Check(Policy.Describe(Policy.WardOf(8,true)).Count()==6&&Policy.Describe(Policy.WardOf(0,false)).All(l=>l.Contains('%')),"The tooltip lists what the ward gives");

// Who comes.
Check(Policy.Stage(k=>false)==0,"A new world meets greydwarfs");
Check(Policy.Stage(k=>k=="defeated_eikthyr"||k=="defeated_gdking")==2,"Each boss beaten opens the next horde");
Check(Policy.Stage(k=>k=="defeated_queen")==6,"The furthest key decides");
foreach(Roster r in Policy.Rosters)Check(r.Grunts.Length>0&&r.Champions.Length>0&&r.Sappers.Length>0&&r.Chest.StartsWith("TreasureChest_")&&r.Spoils.Length>0,"Every horde is complete: "+r.Name);
Check(Policy.Rosters.Select(r=>r.Key).Distinct().Count()==Policy.Rosters.Length,"One horde per key");

// How big.
Check(Policy.Waves(0)==4&&Policy.Waves(10)==8,"Four waves at first, eight at most");
Check(Policy.Total(0,1)==30&&Policy.Total(0,3)>Policy.Total(0,1)&&Policy.Total(10,6)<=220,"Bigger with marks and friends, capped");
Check(Policy.StoneHealth(6,10)>Policy.StoneHealth(0,0)*5,"The stone is tougher where the horde is fiercer");
for(int stage=0;stage<Policy.Rosters.Length;stage++)
    for(int marks=0;marks<=10;marks+=3)
    {
        var plan=Policy.Plan(stage,marks,2,stage*31+marks);
        int total=plan.Sum(w=>w.Count);
        Check(plan.Count==Policy.Waves(marks),$"Wave count ({stage},{marks})");
        Check(Math.Abs(total-Policy.Total(marks,2))<=plan.Count+1+marks/4+2,$"About the planned size ({stage},{marks}): {total}");
        Check(plan.Last().Count>plan.First().Count,$"Waves grow ({stage},{marks})");
        Check(plan.Last().First().Role==Role.Champion&&plan.Last().First().Level==3,$"A warchief leads the last wave ({stage},{marks})");
        Check(plan.First().All(u=>u.Role==Role.Grunt),$"The first wave is plain fighters ({stage},{marks})");
        Check(plan.SelectMany(w=>w).All(u=>u.Level>=1&&u.Level<=3),$"Stars one to three ({stage},{marks})");
        var known=Policy.Rosters[stage].Grunts.Select(g=>g.prefab).Concat(Policy.Rosters[stage].Sappers).Concat(Policy.Rosters[stage].Flyers).Concat(Policy.Rosters[stage].Champions)
            .Concat(Policy.Rosters[Math.Max(0,stage-1)].Grunts.Select(g=>g.prefab)).ToHashSet();
        Check(plan.SelectMany(w=>w).All(u=>known.Contains(u.Prefab)),$"Only this horde and the last ({stage},{marks})");
        Check(Policy.Save(Policy.Load(Policy.Save(plan)))==Policy.Save(plan),$"A saved plan loads the same ({stage},{marks})");
    }
Check(Policy.Save(Policy.Plan(3,4,2,99))==Policy.Save(Policy.Plan(3,4,2,99)),"The same seed makes the same siege");
Check(Policy.Save(Policy.Plan(3,4,2,99))!=Policy.Save(Policy.Plan(3,4,2,100)),"Another seed another");
Check(Policy.Plan(0,0,1,5).SelectMany(w=>w).All(u=>u.Role!=Role.Flyer),"Greydwarfs have no flyers");
Check(Policy.Plan(4,9,4,5).SelectMany(w=>w).Count(u=>u.Role==Role.Champion)==1+9/4+1,"Seasoned stones draw more warchiefs");
Check(Policy.Load("Greydwarf/1/1,bad,Troll/x/4;;Neck/2/9").Sum(w=>w.Count)==1,"Damaged saves keep what they can");
Check(Policy.Level(0,0,0.99)==1&&Policy.Level(10,7,0.0)==3&&Policy.Level(0,0,0.05)==2,"Stars by roll");

// Pacing.
Check(Policy.NextWave(20,30,80,75),"After the longest gap, the next wave comes");
Check(!Policy.NextWave(20,30,30,75),"Not while most still stand");
Check(Policy.NextWave(5,30,20,75)&&!Policy.NextWave(5,30,5,75),"Sooner when the wave is mostly down, but not at once");
Check(Policy.Release(10,0,40)==3&&Policy.Release(10,39,40)==1&&Policy.Release(10,40,40)==0&&Policy.Release(0,0,40)==0&&Policy.Release(2,0,40)==2,"A few at a time, never past the cap");

// Rewards.
Check(Policy.Shards(0,0,1,0)==8&&Policy.Shards(0,0,0,0)==3,"An unscathed stone earns more shards");
Check(Policy.Shards(6,10,1,120)>Policy.Shards(0,0,1,0),"Fiercer, longer sieges earn more");
Check(Policy.Coins(20,40,0,0)==20&&Policy.Coins(20,40,0,1)==40&&Policy.Coins(20,40,10,1)==80,"Spoils grow with the marks");
Check(Policy.Carried(Role.Champion,0)>=3&&Policy.Carried(Role.Champion,0.99)<=6&&Policy.Carried(Role.Grunt,0.5)==0&&Policy.Carried(Role.Grunt,0.05)==1,"Warchiefs always carry shards; others now and then");

// Staves.
Check(Policy.Staves.Select(s=>s.Prefab).Distinct().Count()==Policy.Staves.Length&&Policy.Staves.All(s=>s.Prefab.StartsWith("BobStave")),"Five staves, distinct");
Check(Policy.Staves.All(s=>s.Cost.Any(c=>c.prefab==Policy.ShardPrefab)),"Every stave needs warshards");
Check(Policy.Staves.All(s=>s.StationLevel>=1&&s.StationLevel<=4),"Every stave is reachable");
Check(Policy.StaveOf(StaveKind.Hearth).Projectile==null&&Policy.Staves.Where(s=>s.Kind!=StaveKind.Hearth).All(s=>s.Projectile!=null),"Only the hearth stave throws nothing");
Check(Policy.Power(1)==1&&Policy.Power(4)>2&&Policy.Power(9)==Policy.Power(4),"Upgrades add power, up to four");
Check(Policy.RangeAt(Policy.StaveOf(StaveKind.Ember),4)==Policy.StaveOf(StaveKind.Ember).Range+6,"And reach");

// Aiming.
Check(Policy.Arc(20,0,30,0,out double flat)&&Math.Abs(flat)<1e-9,"No gravity: straight at it");
Check(Policy.Arc(20,0,30,9.81,out double low)&&low>0&&low<0.2,"Under gravity: a little up");
Check(!Policy.Arc(500,0,30,9.81,out _),"Out of reach");
{
    // The arc it gives lands where it should.
    double v=25,g=9.81,dx=30,dy=4;
    Policy.Arc(dx,dy,v,g,out double a);
    double t=dx/(v*Math.Cos(a)),y=v*Math.Sin(a)*t-0.5*g*t*t;
    Check(Math.Abs(y-dy)<0.01,"The arc lands on target");
}
var lead=Policy.Lead((0,0,0),(30,0,0),(0,0,5),30);
Check(Math.Abs(lead.z-5)<0.6&&lead.x==30,"Leads a moving target");
Check(Policy.Lead((0,0,0),(10,0,0),(0,0,0),30)==(10,0,0),"A still target is aimed at directly");

// The stone's level, power and stave upgrades.
Check(Policy.Level(0)==1&&Policy.Level(4)==5&&Policy.Level(9)==Policy.MaxLevel&&Policy.Level(-2)==1,"Level is 1 plus its upgrades, five at most");
Check(Policy.Upgrades.Length==Policy.MaxLevel-1,"One upgrade piece for each level above the first");
Check(Policy.Upgrades.Select(u=>u.prefab).Distinct().Count()==Policy.Upgrades.Length,"Each upgrade is its own piece (the game counts each kind once)");
Check(Policy.Upgrades.Zip(Policy.Upgrades.Skip(1),(a,b)=>b.shards>a.shards).All(x=>x),"Each upgrade costs more warshards than the last");
for(int l=1;l<Policy.MaxLevel;l++)Check(Policy.Capacity(l+1)>Policy.Capacity(l)&&Policy.PowerRadius(l+1)>Policy.PowerRadius(l),$"A higher level feeds more staves, farther ({l})");
Check(Policy.Capacity(1)==2&&Policy.Capacity(5)==10&&Policy.PowerRadius(1)==26&&Policy.PowerRadius(5)==50,"Two staves within 26 m at first; ten within 50 m at most");
Check(Policy.PowerRadius(Policy.MaxLevel)<=Policy.WardRadius+10,"Staves are never fed beyond where the stone can be seen to matter");
Check(Policy.SocketSpacing>=4,"Sockets keep apart");
Check(Policy.UpgradeShards(2)==4&&Policy.UpgradeShards(3)==7&&Policy.UpgradeShards(4)==10,"Strengthening costs more each step");
Check(Policy.UpgradeLevel(2)==2&&Policy.UpgradeLevel(4)==4&&Enumerable.Range(2,Policy.MaxQuality-1).All(q=>Policy.UpgradeLevel(q)<=Policy.MaxLevel),"Every step is reachable by raising the stone");
Check(Policy.EarlyBonus(3)==1&&Policy.EarlyBonus(25)==2&&Policy.EarlyBonus(300)==4,"Calling a wave early pays for the time saved, a little");

// Earthworks: a stone topples once the land under it sinks a metre below where it was set.
Check(!Policy.Toppled(30,new double[]{30,30.2,29.8,30,30})&&!Policy.Toppled(30,new double[]{29.5,29.4,29.2,29.6,29.3}),"A little digging round it is fine");
Check(Policy.Toppled(30,new double[]{28.5,28.9,28.7,29.1,28.8}),"Dug out from under it, it topples");
Check(!Policy.Toppled(30,new double[0])&&!Policy.Toppled(30,null),"No ground read, no topple");
Check(Policy.Plan(2,4,2,77).SelectMany(w=>w).Any(u=>u.Role==Role.Digger)&&Policy.Plan(2,4,2,77).First().All(u=>u.Role!=Role.Digger),"Diggers come from the second wave");

var rng=new Policy.Rng(7);var rolls=Enumerable.Range(0,1000).Select(_=>rng.Next()).ToList();
Check(rolls.All(r=>r>=0&&r<1)&&rolls.Average()>0.45&&rolls.Average()<0.55,"Rolls are even");
Console.WriteLine($"Passed {checks} Warstone, horde, pacing, reward, stave and aiming checks.");
