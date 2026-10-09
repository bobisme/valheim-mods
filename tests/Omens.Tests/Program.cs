using Omens;

int checks=0;
void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
var every=Enum.GetValues<Kind>();

// Every omen is complete, and its biomes, polarity and raid agree.
foreach(Omen o in Policy.All)
{
    Check(!string.IsNullOrWhiteSpace(o.Name)&&!string.IsNullOrWhiteSpace(o.Reading)&&!string.IsNullOrWhiteSpace(o.Outcome),"Every omen has a name, reading and outcome");
    Check(o.Biomes!=0,"Every omen can appear somewhere");
    Check((o.Result==Result.Raid)==!string.IsNullOrEmpty(o.Raid),"Raid omens, and only they, name a raid");
    Check(o.Bad==(o.Result is Result.Raid or Result.Stalkers or Result.BloodMoon or Result.Curse or Result.Storm),"Bad omens bring raids, hunters, a blood moon, a curse or a storm; good ones blessings, gifts, treasure, quarry or favour");
    Check(o.Respondable==!string.IsNullOrWhiteSpace(o.Averted)&&o.Respondable==!string.IsNullOrWhiteSpace(o.Action),"A respondable omen has an action and a response message; others have neither");
    Check(o.Cost==null?o.CostAmount==0:o.Respondable&&o.CostAmount>0&&!o.Cost.StartsWith("$")&&!o.Cost.Contains(' '),"A cost is an item prefab and an amount, and only on a respondable omen");
    Check(o.Cost!=null||!o.Respondable||o.Provokes||o.Result==Result.Curse,"A free response either provokes a fight or takes a hoard");
    Check(o.Result!=Result.Offering||o.Respondable&&!o.Bad&&o.Cost!=null,"An offering is a good omen that costs something");
    Check(Policy.ReadingOf(o.Kind,0)==o.Reading,"Every omen keeps its main reading");
    Check(!o.Provokes||o.Respondable&&o.Bad&&!o.Softens,"Only a bad omen's response can provoke its pack");
    Check(!string.IsNullOrWhiteSpace(o.Test)&&!o.Test.Contains(' ')&&!string.IsNullOrWhiteSpace(o.Config),"Every omen has a test name and a setting description");
    Check(o.SeenFrom>=10&&o.SeenFrom<=40,"Seen from a sensible distance");
    Check(!o.Linger||!o.Bad,"Only good omens linger after coming to pass");
    Check(Policy.Of(o.Kind)==o,"Each kind maps to its own omen");
}
Check(Policy.All.Select(o=>o.Kind).Distinct().Count()==Policy.All.Length&&Policy.All.Length==every.Length,"Every kind has exactly one omen");
Check(Policy.All.Select(o=>o.Test).Distinct().Count()==Policy.All.Length,"Test names are unique");
Check(Policy.All.Count(o=>o.Bad)==13&&Policy.All.Count(o=>!o.Bad)==8,"Thirteen bad and eight good omens");
Check((int)Kind.DeadTroll==0&&(int)Kind.BloodMoon==6&&(int)Kind.Scorched==16&&(int)Kind.GhostShip==19&&(int)Kind.ThorStorm==20,"Kinds keep their saved numbers");
foreach(int land in new[]{Policy.Meadows,Policy.BlackForest,Policy.Swamp,Policy.Mountain,Policy.Plains})
{
    Check(Policy.All.Count(o=>o.Bad&&(o.Biomes&land)!=0)>=3,"Every land has several bad omens");
    Check(Policy.All.Count(o=>!o.Bad&&o.Site==Site.Any&&(o.Biomes&land)!=0)>=2,"Every land has good omens that need no special spot");
}
Check(Policy.All.Where(o=>o.Softens).All(o=>o.Respondable&&o.Bad),"Only respondable bad omens can be softened");
Check(Policy.Of(Kind.BloodMoon).Softens&&!Policy.Of(Kind.DeadTroll).Softens,"An offering softens the blood moon; burning the troll averts its raid");

// Picking: polarity by chance, falling back when a biome has only the other kind.
int bad=0,trials=0;
for(double p=0;p<1;p+=0.001)for(double k=0;k<1;k+=0.25)
{
    Kind? pick=Policy.Pick(p,k,0.6,Policy.BlackForest,every);
    Check(pick!=null,"The Black Forest always has an omen");
    trials++;if(Policy.Of(pick.Value).Bad)bad++;
}
Check(Math.Abs(bad/(double)trials-0.6)<0.01,"60/40 bad to good where both are possible");
Check(Policy.Pick(0.99,0.0,0.6,Policy.Mountain,new[]{Kind.Ravens,Kind.BloodMoon})==Kind.Ravens,"Good roll in the mountains: ravens");
Check(Policy.Pick(0.0,0.5,0.6,Policy.Mountain,new[]{Kind.Ravens,Kind.BloodMoon})==Kind.BloodMoon,"Bad roll in the mountains: the blood moon");
Check(Policy.Pick(0.0,0.5,0.6,Policy.Mistlands,every) is Kind g0&&!Policy.Of(g0).Bad,"Bad roll where no bad omen fits falls back to a good one");
Check(Policy.Pick(0.0,0.0,0.6,Policy.Plains,new[]{Kind.WarBanner,Kind.DeadTroll})==Kind.WarBanner,"The war banner stands in the plains");
Check(Policy.Pick(0.0,0.0,0.6,Policy.Swamp,new[]{Kind.DeadTroll})==null,"Nothing fits: no omen");
Check(Policy.Pick(0.0,0.5,0.6,Policy.Meadows,every) is Kind m&&Policy.Of(m).Bad&&m!=Kind.DeadTroll,"Bad omen in the meadows is never the troll (Black Forest only)");
Check(Policy.Pick(0.99,0.0,0.6,Policy.Mountain,new[]{Kind.BloodMoon,Kind.Catch})==Kind.BloodMoon,"Mountains without open sky: a good roll falls back to the blood moon");
Check(Policy.Pick(0.99,0.0,0.6,Policy.Mountain,new[]{Kind.Catch,Kind.DeadTroll})==null,"Nothing that fits the mountains: no omen");
Check(Policy.Pick(0.99,0.5,0.6,Policy.Meadows,new[]{Kind.Catch,Kind.Cairn})==Kind.Catch,"A good roll on a shore picks the catch");
for(double k=0;k<1;k+=0.05)Check(Policy.Pick(0.0,k,0,Policy.BlackForest,every) is Kind g&&!Policy.Of(g).Bad,"With raids off only good omens appear");
foreach(double roll in new[]{-1.0,0,0.9999999,1,2,double.NaN})
    Check(Policy.Pick(0.0,roll,0.6,Policy.BlackForest,every) is Kind k&&Policy.Of(k).Bad,"Out-of-range rolls still pick a valid kind");
Check(Policy.Pick(0.0,0.0,0.6,Policy.BlackForest,new Kind[0])==null,"All omens disabled: none");

// Intervals.
foreach(double r in new[]{0.0,0.5,1.0,double.NaN})
{
    double d=Policy.NextDelay(1.5,1800,r);
    Check(d>=1.5*1800*0.75-1e-9&&d<=1.5*1800*1.25+1e-9,"Delay stays within 0.75–1.25 of the interval");
}
Check(Policy.NextDelay(0,1800,0.5)>=60,"A zero interval never spins");

// Timing of a bad omen.
Check(!Policy.RaidDue(1000,900,false,false),"Seen by day: nothing until night");
Check(Policy.RaidDue(1000,900,false,true),"Seen by day: the raid comes at nightfall");
Check(!Policy.RaidDue(1000,900,true,true),"Seen at night: a short grace first");
Check(Policy.RaidDue(1000+Policy.SeenAtNightDelay,1000,true,true),"Seen at night: the raid follows that night");
Check(!Policy.RaidDue(5000,1000,true,false),"Never by day");

// The state machine.
Check(Policy.Advance(State.Placed,10,0,100,false,false,false,false)==State.Placed,"Unseen omens wait");
Check(Policy.Advance(State.Placed,100,0,100,false,false,false,false)==State.Expired,"Unseen omens fade after their time");
Check(Policy.Advance(State.Placed,100,0,100,true,false,false,false)==State.Seen,"Seeing beats fading in the same tick");
Check(Policy.Advance(State.Placed,10,0,100,true,true,false,false)==State.Averted,"Responding before the reading still averts");
Check(Policy.Advance(State.Seen,10,0,100,false,false,true,false)==State.Fulfilled,"An outcome fulfills a seen omen");
Check(Policy.Advance(State.Seen,10,0,100,false,true,true,false)==State.Averted,"Averting wins over a simultaneous outcome");
Check(Policy.Advance(State.Seen,1e9,0,100,false,false,false,false)==State.Seen,"A seen omen never expires; it waits for its outcome");
Check(Policy.Advance(State.Seen,10,0,100,false,false,false,true)==State.Fizzled,"An impossible outcome fizzles");
foreach(State end in new[]{State.Fulfilled,State.Averted,State.Expired,State.Fizzled})
{
    Check(Policy.Finished(end),"End states are final");
    Check(Policy.Advance(end,1e9,0,1,true,true,true,true)==end,"End states never change");
}
Check(!Policy.Finished(State.Placed)&&!Policy.Finished(State.Seen),"Open states are open");
Check(!Policy.SignGone(State.Fulfilled,true,0)&&!Policy.SignGone(State.Fulfilled,true,Policy.Linger-1)&&Policy.SignGone(State.Fulfilled,true,Policy.Linger),"A lingering sign stays, then goes");
Check(Policy.SignGone(State.Fulfilled,false,0)&&Policy.SignGone(State.Expired,true,0)&&Policy.SignGone(State.Fizzled,true,0),"Other finished signs go at once");
Check(!Policy.SignGone(State.Averted,false,0)&&!Policy.SignGone(State.Averted,true,Policy.AvertLinger-1)&&Policy.SignGone(State.Averted,false,Policy.AvertLinger),"An averted sign stays just long enough to see the response");
Check(!Policy.SignGone(State.Seen,true,1e9)&&!Policy.SignGone(State.Placed,false,1e9),"Open omens keep their sign");

// Hunting packs: every base biome gets a small pack led by its strongest.
foreach(int biome in new[]{Policy.Meadows,Policy.BlackForest,Policy.Swamp,Policy.Mountain,Policy.Plains,Policy.Mistlands,32,64,256,0})
{
    foreach(Kind kind in new[]{Kind.DrainedDeer,Kind.Drowned,Kind.Hoard,Kind.WarBanner})
    {
        var pack=Policy.Pack(kind,biome);
        Check(pack.Length>=2&&pack.Length<=4,"A pack is a few creatures");
        Check(pack.All(c=>!string.IsNullOrWhiteSpace(c.prefab)&&c.level>=1&&c.level<=3),"Pack creatures have names and sane levels");
    }
    foreach(Kind kind in new[]{Kind.Catch,Kind.FallenStar,Kind.Hoard})
    {
        var gifts=Policy.Gifts(kind,biome);
        Check(gifts.Length>=1&&gifts.All(g=>!string.IsNullOrWhiteSpace(g.prefab)&&g.min>=1&&g.max>=g.min&&g.max<=100),"Gifts are real items in sane amounts");
    }
    Check(Policy.Chest(biome).StartsWith("TreasureChest_"),"The lights always lead to one of the game's chests");
}
Check(Policy.Pack(Kind.DrainedDeer,Policy.Swamp)[0].prefab=="Draugr_Elite"&&Policy.Pack(Kind.DrainedDeer,Policy.BlackForest)[0].prefab=="Greydwarf_Elite","The pack fits the base's biome");
Check(Policy.Pack(Kind.Drowned,Policy.Meadows).All(c=>c.prefab=="Draugr"&&c.level==1),"The drowned go easy on a meadows base");
Check(Policy.Pack(Kind.Drowned,Policy.Plains).Sum(c=>c.level)>Policy.Pack(Kind.Drowned,Policy.Swamp).Sum(c=>c.level),"The drowned grow stronger with the land");
Check(Policy.Pack(Kind.Hoard,Policy.Meadows).Any(c=>c.prefab=="Ghost")&&Policy.Pack(Kind.Hoard,Policy.Mountain).Any(c=>c.prefab=="Ghost"),"A ghost always leads the dead to their gold");
Check(Policy.Gifts(Kind.FallenStar,Policy.Swamp)[0].prefab=="IronScrap"&&Policy.Gifts(Kind.FallenStar,Policy.Plains)[0].prefab=="BlackMetalScrap","A star's ore fits the land");
Check(Policy.Gifts(Kind.Hoard,Policy.Plains).Sum(g=>g.max)>Policy.Gifts(Kind.Hoard,Policy.Meadows).Sum(g=>g.max),"Richer hoards in harder lands");
Check(Policy.Chest(Policy.Swamp)=="TreasureChest_swamp"&&Policy.Chest(Policy.Meadows)=="TreasureChest_meadows","The chest fits the land");
for(double r=0;r<=1;r+=0.01){int n=Policy.Roll(3,5,r);Check(n>=3&&n<=5,"Rolls stay in range");}
Check(Policy.Roll(3,5,0)==3&&Policy.Roll(3,5,0.999)==5&&Policy.Roll(4,4,0.5)==4&&Policy.Roll(3,5,double.NaN)==3,"Rolls reach both ends");
Check(Policy.StagLevel==3&&Policy.StagDrops.Any(d=>d.prefab=="HardAntler"),"The great stag is two-star and drops its antlers");

// Nearest base.
var bases=new List<(double x,double z)>{(100,0),(0,50),(-500,-500)};
Check(Policy.Nearest(0,0,bases,1000)==1,"Closest base wins");
Check(Policy.Nearest(0,0,bases,40)==-1,"No base within reach");
Check(Policy.Nearest(0,0,new List<(double x,double z)>(),1000)==-1,"No bases at all");
Check(Policy.Nearest(0,0,bases,50)==1,"Reach is inclusive");
// Blood moon nights.
foreach(bool soft in new[]{false,true})
{
    Check(Policy.SpawnChance(30,soft)==(soft?45:60)&&Policy.SpawnChance(80,soft)==100&&Policy.SpawnChance(-5,soft)==0,"Night spawns are likelier, never above certain");
    Check(Policy.MaxSpawned(2,soft)==(soft?3:3)&&Policy.MaxSpawned(4,soft)==(soft?5:6)&&Policy.MaxSpawned(0,soft)==0,"More at once, but an unlimited spawner stays unlimited");
    Check(Policy.LevelUpChance(10,soft)==(soft?15:20)&&Policy.LevelUpChance(60,soft)==70,"Stronger, within the game's own level-up ceiling");
}
Check(Policy.SpawnChance(30,true)<Policy.SpawnChance(30,false)&&Policy.LevelUpChance(10,true)<Policy.LevelUpChance(10,false),"An offering softens every part of it");
// Readings vary by sign, from a fixed set.
foreach(var (kind,more) in Policy.Readings)
{
    var seen=new HashSet<string>();
    for(long id=0;id<60;id++)seen.Add(Policy.ReadingOf(kind,id));
    Check(seen.Count==more.Length+1&&more.All(seen.Contains)&&seen.Contains(Policy.Of(kind).Reading),"Every reading of an omen turns up");
    Check(Policy.ReadingOf(kind,-7)!=null&&Policy.ReadingOf(kind,long.MinValue)!=null,"Any id picks a reading");
}

// The gods' favour.
Check(Policy.Fate(0,1)==1&&Policy.Fate(5,1)==5&&Policy.Fate(-5,-2)==-5&&Policy.Fate(4,2)==5,"Favour stays within its bounds");
Check(Policy.FateAverted>0&&Policy.FateOffering>Policy.FateAverted&&Policy.FateIgnored<0&&Policy.FateTaken<Policy.FateIgnored,"Answering omens pleases the gods; robbing the dead angers them most");
Check(Math.Abs(Policy.BadChance(0.6,0)-0.6)<1e-9&&Policy.BadChance(0.6,5)<0.6&&Policy.BadChance(0.6,-5)>0.6,"Favour shifts the odds of bad omens");
for(int f=Policy.FateMin;f<=Policy.FateMax;f++)
{
    Check(Policy.BadChance(0.6,f)>=0.25&&Policy.BadChance(0.6,f)<=0.85&&Policy.BadChance(double.NaN,f)>=0.25,"Never all bad or all good");
    Check(Policy.Wrath(3,f)<=3&&Policy.Wrath(1,f)>=1,"Wrath never passes three stars");
    Check(Policy.Generous(4,f)>=4&&Policy.IntervalFactor(f)>0&&Policy.IntervalFactor(f)<=1,"Favour never takes gifts away or slows omens");
    Check(new[]{"forsaken","displeased","watched","favoured","beloved"}.Contains(Policy.Standing(f)),"Every favour has a standing");
}
Check(Policy.Wrath(1,-3)==2&&Policy.Wrath(1,-2)==1&&Policy.Generous(4,3)==6&&Policy.Generous(4,2)==4&&Policy.IntervalFactor(-3)<1,"The forsaken face stronger packs more often; the beloved get more");
Check(Policy.StandingNews(0,0)==null&&Policy.StandingNews(1,2)==null&&Policy.StandingNews(2,3)!=null&&Policy.StandingNews(-2,-3)!=null&&Policy.StandingNews(0,1)!=null,"Only a change of standing is news");
Check(Policy.StandingNews(1,0)!=Policy.StandingNews(-1,0)||Policy.StandingNews(1,0)!=null,"Returning to neutral is told");

// The northern lights.
Check(Policy.AuroraSpawnChance(40)==20&&Policy.AuroraSpawnChance(-3)==0,"Under the lights, half as many night creatures");
Check(Policy.Pick(0.99,0.0,0.6,Policy.Mountain,new[]{Kind.Aurora,Kind.Wolves})==Kind.Aurora,"The rune stone stands anywhere");
Check(Policy.Pack(Kind.GhostShip,Policy.Swamp).SequenceEqual(Policy.Pack(Kind.Drowned,Policy.Swamp)),"The black ship's crew are the drowned");
// Chains: some omens return, worse.
foreach(Omen o in Policy.All)
{
    Kind? next=Policy.Chain(o.Kind);
    if(next==null)continue;
    Check(o.Bad&&Policy.Of(next.Value).Bad,"Only bad omens return, and they return as bad ones");
    Check(Policy.Chain(next.Value)==null||Policy.Chain(next.Value)!=o.Kind,"No two omens chain into each other");
    Check((Policy.Of(next.Value).Biomes&o.Biomes)!=0||Policy.Of(next.Value).Biomes==Policy.AnyLand,"A returning omen can stand where the first one did");
}
Check(Policy.Chain(Kind.Cairn)==Kind.GraveCandles&&Policy.Chain(Kind.Drowned)==Kind.GhostShip&&Policy.Chain(Kind.Ravens)==null,"The cairn's dead haunt you; the drowned man's ship comes looking");
Check(Policy.ChainChance(-5)==1&&Policy.ChainChance(5)==0&&Policy.ChainChance(0)>Policy.ChainChance(1)&&Policy.ChainChance(-1)>Policy.ChainChance(0),"Chains are likelier the less the gods like you");
for(int f=Policy.FateMin;f<=Policy.FateMax;f++)Check(Policy.ChainChance(f)>=0&&Policy.ChainChance(f)<=1,"Chain chances are chances");
Check(Policy.ChainedPrefix.EndsWith(" "),"The returning prefix joins the reading");

// Thor's storm.
Check(Policy.Of(Kind.ThorStorm).Softens&&Policy.Of(Kind.ThorStorm).Cost=="Coins","Coins for Thor soften his storm");
foreach(double r in new[]{0,0.5,1,double.NaN,-1,2}){float t=Policy.NextStrike(r);Check(t>=9&&t<=20,"Strikes come every 9 to 20 seconds");}
Check(Policy.CloseStrike(0.1)&&!Policy.CloseStrike(0.5),"Low rolls strike close");
int close=0;for(double r=0;r<1;r+=0.001)if(Policy.CloseStrike(r))close++;
Check(Math.Abs(close/1000.0-0.25)<0.01,"One strike in four comes close");
Check(Policy.StrikeDamage>0&&Policy.StrikeDamage<40&&Policy.StrikeWarning>=1,"A strike hurts but does not kill outright, and warns first");

// The rune bones.
Check(Policy.Compass(0,1)=="north"&&Policy.Compass(1,0)=="east"&&Policy.Compass(0,-1)=="south"&&Policy.Compass(-1,0)=="west"&&Policy.Compass(1,1)=="north-east"&&Policy.Compass(-1,-1)=="south-west","Compass points");
Check(Policy.Compass(0,0)=="north"&&Policy.Compass(-0.1,1)=="north","Compass edge cases");
Check(Policy.Roughly(3)==10&&Policy.Roughly(47)==50&&Policy.Roughly(130)==150&&Policy.Roughly(1234)==1250,"Distances are rough");
string reading=Policy.Cast(true,100,0,true,-4,"The moon bleeds tonight.");
Check(reading.Contains("east")&&reading.Contains("100 m")&&reading.Contains("cold")&&reading.Contains("forsaken")&&reading.EndsWith("tonight."),"The bones tell direction, distance, feel, standing and tonight");
Check(Policy.Cast(false,0,0,false,4,"").Contains("say nothing")&&Policy.Cast(false,0,0,false,4,"").Contains("beloved")&&Policy.Cast(true,0,50,false,0,"").Contains("warm"),"Quiet bones and warm signs");
Console.WriteLine($"Passed {checks} omen choice, timing, state and base checks.");
