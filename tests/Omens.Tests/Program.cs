using Omens;

int checks=0;
void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
var every=new[]{Kind.DeadTroll,Kind.Ravens,Kind.AbandonedCamp};

// Every omen is complete, and its biomes, polarity and raid agree.
foreach(Omen o in Policy.All)
{
    Check(!string.IsNullOrWhiteSpace(o.Name)&&!string.IsNullOrWhiteSpace(o.Reading)&&!string.IsNullOrWhiteSpace(o.Outcome),"Every omen has a name, reading and outcome");
    Check(o.Biomes!=0,"Every omen can appear somewhere");
    Check(o.Bad==!string.IsNullOrEmpty(o.Raid),"Bad omens, and only bad ones, bring a raid");
    Check(!o.Respondable||!string.IsNullOrWhiteSpace(o.Averted),"A respondable omen says what averting it did");
    Check(Policy.Of(o.Kind)==o,"Each kind maps to its own omen");
}
Check(Policy.All.Select(o=>o.Kind).Distinct().Count()==Policy.All.Length,"Kinds are unique");

// Picking: polarity by chance, falling back when a biome has only the other kind.
int bad=0,trials=0;
for(double p=0;p<1;p+=0.001)for(double k=0;k<1;k+=0.25)
{
    Kind? pick=Policy.Pick(p,k,0.6,Policy.BlackForest,every);
    Check(pick!=null,"The Black Forest always has an omen");
    trials++;if(Policy.Of(pick.Value).Bad)bad++;
}
Check(Math.Abs(bad/(double)trials-0.6)<0.01,"60/40 bad to good where both are possible");
Check(Policy.Pick(0.99,0.5,0.6,Policy.Mountain,every)==Kind.Ravens,"Good roll in the mountains: ravens");
Check(Policy.Pick(0.0,0.5,0.6,Policy.Mountain,every)==Kind.Ravens,"Bad roll where no bad omen fits falls back to a good one");
Check(Policy.Pick(0.0,0.0,0.6,Policy.Swamp,new[]{Kind.DeadTroll})==null,"Nothing fits: no omen");
Check(Policy.Pick(0.0,0.5,0.6,Policy.Meadows,every)==Kind.AbandonedCamp,"Bad omen in the meadows: the camp (trolls are Black Forest only)");
Check(Policy.Pick(0.5,0.5,0,Policy.BlackForest,every)==Kind.Ravens,"With raids off only good omens appear");
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

// Nearest base.
var bases=new List<(double x,double z)>{(100,0),(0,50),(-500,-500)};
Check(Policy.Nearest(0,0,bases,1000)==1,"Closest base wins");
Check(Policy.Nearest(0,0,bases,40)==-1,"No base within reach");
Check(Policy.Nearest(0,0,new List<(double x,double z)>(),1000)==-1,"No bases at all");
Check(Policy.Nearest(0,0,bases,50)==1,"Reach is inclusive");
Console.WriteLine($"Passed {checks} omen choice, timing, state and base checks.");
