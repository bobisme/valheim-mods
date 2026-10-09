using Gary;

int checks=0;
void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
foreach(double maximum in new[]{40.0,150,500})
foreach(double request in new[]{-1e20,-1.0,0,1,7.5,30,135,150,1e20,double.NaN,double.PositiveInfinity,double.NegativeInfinity})
{
    double actual=Policy.ProtectedHealth(request,maximum);
    Check(double.IsFinite(actual)&&actual>0&&actual<=maximum,"Every hit, including lethal and invalid damage, retains live bounded HP");
    if(double.IsFinite(request)&&request>=maximum*Policy.HealthFloor&&request<=maximum)
        Check(actual==request,"Ordinary nonlethal health changes are preserved");
}
bool retreat=false;
foreach(var step in new[]{(0.50,false),(0.20,true),(0.05,true),(0.21,true),(0.89,true),(0.90,false),(0.89,false),(0.19,true)})
{retreat=Policy.Retreat(retreat,step.Item1);Check(retreat==step.Item2,"Retreat hysteresis holds until healed and prevents oscillation");}
Check(Policy.FreshThreat(0,10,12,true,false,false),"A verified creature attacker can trigger defense");
Check(Policy.FreshThreat(30,35,50,true,false,false),"Threat boundary is inclusive");
foreach(double age in new[]{-1.0,30.01,double.NaN,double.PositiveInfinity})
    Check(!Policy.FreshThreat(age,10,12,true,false,false),"Stale/future/nonfinite threats are rejected");
Check(!Policy.FreshThreat(1,35.01,10,true,false,false),"Never chase beyond player's defense radius");
Check(!Policy.FreshThreat(1,10,50.01,true,false,false),"Never engage an attacker far from Gary");
Check(!Policy.FreshThreat(1,10,10,false,false,false),"Friendly creatures cannot become defensive targets");
Check(!Policy.FreshThreat(1,10,10,true,true,false),"PvP and tamed friends cannot trigger defense");
Check(!Policy.FreshThreat(1,10,10,true,false,true),"Dead attackers are discarded");
Check(Policy.FreshSelfThreat(0,2,2,true,false,false),"Gary can retaliate against his own attacker without a player threat");
Check(Policy.FreshSelfThreat(30,35,35,true,false,false),"Self-defense time and range boundaries are inclusive");
foreach(double age in new[]{-1.0,30.01,double.NaN,double.PositiveInfinity})
    Check(!Policy.FreshSelfThreat(age,2,2,true,false,false),"Self-defense rejects stale/future/nonfinite threats");
Check(!Policy.FreshSelfThreat(1,35.01,2,true,false,false),"Self-defense does not chase distant attackers");
Check(!Policy.FreshSelfThreat(1,2,35.01,true,false,false),"Self-defense stays near the place Gary was attacked");
Check(!Policy.FreshSelfThreat(1,2,2,false,false,false),"Self-defense rejects friendly creatures");
Check(!Policy.FreshSelfThreat(1,2,2,true,true,false),"Self-defense rejects players and tamed friends");
Check(!Policy.FreshSelfThreat(1,2,2,true,false,true),"Self-defense stops when the attacker dies");
foreach(double distance in new[]{double.NaN,double.PositiveInfinity})
{
    Check(!Policy.FreshSelfThreat(1,distance,2,true,false,false),"Invalid companion distance cannot trigger self-defense");
    Check(!Policy.FreshSelfThreat(1,2,distance,true,false,false),"Invalid origin distance cannot trigger self-defense");
}
Check(Policy.CanGift(false,false,false,false,8,0),"A nearby calm companion can give a due gift");
foreach(var blocked in new[]{(true,false,false,false),(false,true,false,false),(false,false,true,false),(false,false,false,true)})
    Check(!Policy.CanGift(blocked.Item1,blocked.Item2,blocked.Item3,blocked.Item4,3,-1),"Retreat/combat/waiting/interiors suppress gifts");
Check(!Policy.CanGift(false,false,false,false,8.01,0),"No food thrown from afar");
Check(!Policy.CanGift(false,false,false,false,3,0.01),"No gift before saved cooldown expires");
Check(Policy.GuideWait(14.01)&&!Policy.GuideWait(14),"Guiding waits for the player to catch up");
// Forest-item conservation across repeated harvest/gift cycles, including a serialized F6 handoff.
var random=new Random(20261007);
ForestStash stash=new ForestStash(0,0,0);int harvested=0,given=0;
for(int step=0;step<1000;step++)
{
    int kind=random.Next(-1,5),amount=random.Next(-1,9),before=stash.Count;
    if(random.Next(2)==0)
    {
        bool accepted=stash.TryAdd(kind,amount,out ForestStash next);
        if(accepted){harvested+=amount;Check(next.At(kind)==stash.At(kind)+amount,"Harvest credits exactly its actual yield");}
        else Check(next.Count==before,"Rejected harvest cannot create or discard food");
        stash=next;
    }
    else
    {
        bool accepted=stash.TryTake(kind,out ForestStash next);
        if(accepted){given++;Check(next.Count==before-1,"Each snack consumes one harvested item");}
        else Check(next.Count==before,"An empty or invalid pocket produces no snack");
        stash=next;
    }
    stash=new ForestStash(stash.Berries,stash.Blueberries,stash.Mushrooms,stash.Feathers);
    Check(stash.Count==harvested-given&&stash.Count>=0&&stash.Count<=ForestStash.Capacity,"Food stays conserved and bounded across reloads");
}
Check(new ForestStash(int.MaxValue,int.MaxValue,int.MaxValue,int.MaxValue).Count==6,"Malformed saved pockets cannot exceed six items");
Check(new ForestStash(-1,-1,-1,-1).Count==0,"Negative saved pockets cannot create food");
Check(new ForestStash(1,2,3).Feathers==0,"Existing three-pocket saves load without changing their food");
Check(new ForestStash(0,0,0,int.MaxValue).Feathers==2,"Feathers occupy at most two pockets");
Check(!new ForestStash(0,0,0,2).TryAdd(3,1,out _),"Full feather pockets refuse more gathering");
Check(!new ForestStash(2,2,2).TryAdd(3,1,out _),"Feathers cannot overflow a full food stash");
Check(new ForestStash(1,1,1).TryAdd(3,1,out var mixed)&&mixed.Count==4&&mixed.Feathers==1,"One real feather can join existing food");
Check(mixed.TryTake(3,out var afterFeather)&&afterFeather.Count==3&&afterFeather.Feathers==0&&afterFeather.Berries==1,"A feather gift spends only one feather");
int featherGifts=0;
for(int roll=0;roll<100;roll++)
{
    int kind=mixed.GiftKind(roll);if(kind==3)featherGifts++;
    Check(kind>=0&&mixed.At(kind)>0,"Every gift selection uses an occupied pocket");
    Check(new ForestStash(0,0,0,1).GiftKind(roll)==3,"A feather-only stash can still give a gift");
    Check(new ForestStash(0,1,0).GiftKind(roll)==1,"Food-only stashes never select a missing feather");
    Check(new ForestStash(0,0,0).GiftKind(roll)==-1,"Empty pockets never create gifts");
}
Check(featherGifts==20,"Feathers are occasional: 20 percent of selections when food is available");
Check(mixed.GiftKind(-1)==-1&&mixed.GiftKind(100)==-1,"Invalid random draws cannot select a gift");
Check(Policy.CanPet(true,5,true,false),"A nearby living master can pet Gary");
foreach(double distance in new[]{-1.0,5.01,double.NaN,double.PositiveInfinity})
    Check(!Policy.CanPet(true,distance,true,false),"Invalid/out-of-reach pet requests are rejected");
Check(!Policy.CanPet(false,1,true,false)&&!Policy.CanPet(true,1,false,false)&&!Policy.CanPet(true,1,true,true),"Other players, dead players and healing retreats cannot pet");
Check(Policy.MoodDue(2)&&!Policy.MoodDue(1.99),"Chirps and reactions have a minimum cooldown");
Check(Policy.PlayRecentMood(0)&&Policy.PlayRecentMood(4)&&!Policy.PlayRecentMood(4.01)&&!Policy.PlayRecentMood(-1),"Observers play only recent reactions");
Check(Policy.SafeRest(40,18)&&!Policy.SafeRest(39.99,18)&&!Policy.SafeRest(40,17.99),"Rest requires distance from both friend and danger");
Check(Policy.RestSpotScore(40,30,true)>Policy.RestSpotScore(40,30,false),"Reachable cover is preferred for a refuge");
Check(Policy.RestSpotScore(40,50,false)>Policy.RestSpotScore(40,20,false),"Retreat routes prefer more separation from danger");
Check(double.IsNegativeInfinity(Policy.RestSpotScore(40,17.99,true)),"Cover cannot make an unsafe enemy distance acceptable");
foreach(double invalid in new[]{double.NaN,double.PositiveInfinity,double.NegativeInfinity})
{
    Check(!Policy.MoodDue(invalid)&&!Policy.PlayRecentMood(invalid),"Invalid reaction clocks cannot replay effects");
    Check(!Policy.SafeRest(invalid,20)&&!Policy.SafeRest(45,invalid),"Invalid positions cannot enable rapid healing");
    Check(double.IsNegativeInfinity(Policy.RestSpotScore(invalid,20,true))&&double.IsNegativeInfinity(Policy.RestSpotScore(45,invalid,true)),"Invalid refuge scores are rejected");
}
// Read-only inventory header and dungeon-layout/ledger safety.
byte[] Header(int version,int count,bool compact,bool body=false)
{
    using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
    writer.Write(version);if(compact)writer.Write((ushort)count);else writer.Write(count);
    if(body)writer.Write((byte)1);return stream.ToArray();
}
foreach(int version in Enumerable.Range(101,9))
{
    bool compact=version>=108;
    Check(LootPolicy.Chest(Header(version,0,compact),true)==LootState.Empty,"Exact native empty chest header is empty");
    Check(LootPolicy.Chest(Header(version,1,compact,true),true)==LootState.Remaining,"Saved positive count retains even unavailable or modded items");
    Check(LootPolicy.Chest(Header(version,1,compact),true)==LootState.Unknown,"Truncated nonempty chest cannot become empty");
    Check(LootPolicy.Chest(Header(version,0,compact,true),true)==LootState.Unknown,"Unexpected trailing data is not empty");
    Check(LootPolicy.Chest(Header(version,0,compact),false)==LootState.Unknown,"Ungenerated defaults are unknown");
}
foreach(byte[] data in new byte[][]{null,Array.Empty<byte>(),new byte[5],Header(110,0,true),Header(100,0,false),Header(106,-1,false),new byte[1024*1024+1]})
    Check(LootPolicy.Chest(data!,true)==LootState.Unknown,"Missing, future, malformed, and oversized inventories stay unknown");
foreach(LootState state in Enum.GetValues<LootState>())
{
    Check(LootPolicy.Merge(state,LootState.Remaining)==LootState.Remaining,"Positive loot evidence takes precedence");
    Check(LootPolicy.Merge(state,LootState.Unknown)!=LootState.Empty,"Partial scans never become empty");
}
foreach(int count in new[]{0,1,50,512,513})
{
    byte[] data=new byte[4+count*32];BitConverter.GetBytes(count).CopyTo(data,0);
    Check(LootPolicy.RoomCount(data)==(count>0&&count<=512?count:0),"Room layouts are bounded and exact");
    Check(LootPolicy.RoomCount(data.Take(data.Length-1).ToArray())==0,"Truncated layout cannot certify all rooms");
}
var ledger=LootPolicy.Ledger("1,2;1,2;bad;3,-4");
Check(ledger.SequenceEqual(new[]{"1,2","3,-4"}),"Ledger rejects duplicates and malformed IDs");
Check(!LootPolicy.Update(ledger,"1,2",LootState.Unknown)&&ledger.Contains("1,2"),"Unknown scan preserves previous cleared knowledge");
Check(LootPolicy.Update(ledger,"1,2",LootState.Remaining)&&!ledger.Contains("1,2"),"Restocked or unfinished interiors remove the cleared mark");
Check(LootPolicy.Update(ledger,"5,6",LootState.Empty)&&ledger.Contains("5,6"),"Only confirmed empty interiors gain cleared marks");
for(int n=0;n<1000;n++)LootPolicy.Update(ledger,n+",0",LootState.Empty);
Check(ledger.Count==512&&ledger.Last()=="999,0","Saved knowledge keeps a bounded recent history");
Check(LootPolicy.Ledger(new string('x',16385)).Count==0,"Oversized saved ledger does not allocate unbounded entries");
Check(RidePolicy.CanBoard(true,false,false,true,12,5),"Nearby following Gary can board at inclusive reach limits");
foreach(var state in new[]{(false,false,false,true),(true,true,false,true),(true,false,true,true),(true,false,false,false)})
    Check(!RidePolicy.CanBoard(state.Item1,state.Item2,state.Item3,state.Item4,2,1),"Waiting, retreating, fighting or no boat prevents new boarding");
foreach(double invalid in new[]{-1.0,12.01,double.NaN,double.PositiveInfinity,double.NegativeInfinity})
    Check(!RidePolicy.CanBoard(true,false,false,true,invalid,1),"Invalid or distant boarding is rejected");
foreach(double invalid in new[]{-1.0,5.01,double.NaN,double.PositiveInfinity,double.NegativeInfinity})
    Check(!RidePolicy.CanBoard(true,false,false,true,2,invalid),"Invalid boarding height is rejected");
Check(RidePolicy.Grace(0)&&RidePolicy.Grace(1.5)&&!RidePolicy.Grace(1.51)&&!RidePolicy.Grace(double.NaN),"Brief loss of boat contact has a bounded grace period");
Check(RidePolicy.CanDisembark(true,true,20),"A nearby living player on dry ground can bring Gary ashore");
Check(!RidePolicy.CanDisembark(false,true,2)&&!RidePolicy.CanDisembark(true,false,2),"Death and swimming cannot pull Gary off the boat");
foreach(double invalid in new[]{-1.0,20.01,double.NaN,double.PositiveInfinity,double.NegativeInfinity})
    Check(!RidePolicy.CanDisembark(true,true,invalid),"Disembarking cannot teleport Gary to a distant or invalid shore");
// Saved activity deadlines reject rollback/nonfinite input; fetch and home have independent reach limits.
foreach(double invalid in new[]{-1.0,double.NaN,double.PositiveInfinity,double.NegativeInfinity})
{
    Check(!FunPolicy.Recent(invalid,45),"Invalid elapsed clocks cannot keep a saved activity alive");
    Check(!FunPolicy.FetchReach(invalid),"Invalid item distances cannot permit fetch");
    Check(!FunPolicy.HomeReady(true,true,invalid,3),"Invalid stationary timer cannot activate a nest");
    Check(!FunPolicy.HomeReady(true,true,10,invalid),"Invalid home distances cannot activate a nest");
}
Check(FunPolicy.FetchReach(16)&&!FunPolicy.FetchReach(16.001),"Fetch cannot chase a thrown stick beyond its player reach");
Check(FunPolicy.Recent(0,45)&&FunPolicy.Recent(45,45)&&!FunPolicy.Recent(45.001,45),"Saved fetch clocks are bounded including both endpoints");
Check(FunPolicy.HomeReady(true,true,8,12),"An eight-second pause within twelve metres enables a nest");
Check(FunPolicy.HomeReady(true,true,7200,3),"A long peaceful pause keeps Gary at home");
Check(!FunPolicy.HomeReady(false,true,10,3)&&!FunPolicy.HomeReady(true,false,10,3),"Waiting and activity conditions do not summon Gary home");
Check(!FunPolicy.HomeReady(true,true,7.99,3)&&!FunPolicy.HomeReady(true,true,10,12.01),"A moving or distant player cannot keep Gary nesting");
foreach(var pair in new[]{("wave",1),("cheer",2),("dance",3),("sit",4),("relax",4)})
    Check(FunPolicy.Emote(pair.Item1)==pair.Item2,"Only known native emote names map to Gary poses");
foreach(string name in new string[]{null!,"","attack","throw","Wave","unknown"})
    Check(FunPolicy.Emote(name)==0,"Unknown names cannot trigger attack animations or a pose");
// A simulated one-second throw never queries the airborne item's navigation destination.
for(int tick=0;tick<=15;tick++)Check(FunPolicy.FetchInFlight(tick/10.0),"Flight phase chases the prevalidated ground landing instead of cancelling on an airborne path");
Check(!FunPolicy.FetchInFlight(1.501),"Landed fetch switches to the actual dropped Wood's ground position");
for(int tick=0;tick<=50;tick++)Check(FunPolicy.FetchNavigationGrace(tick/10.0),"Transient navigation failure retains the same fetch until its retry grace ends");
Check(!FunPolicy.FetchNavigationGrace(5.001),"An unreachable throw releases normal Wood after bounded retries");
foreach(double bad in new[]{-1.0,double.NaN,double.PositiveInfinity,double.NegativeInfinity})
    Check(!FunPolicy.FetchInFlight(bad)&&!FunPolicy.FetchNavigationGrace(bad),"Invalid fetch clocks cannot keep flight or retry alive");
// Reforestation: when he may look, which spots are fair, and which tree.
Check(ReforestPolicy.Ready(true,20,60,600,480,0,6),"Near the bed, long after his last tree: he looks for a spot");
Check(!ReforestPolicy.Ready(false,20,60,600,480,0,6)&&!ReforestPolicy.Ready(true,61,60,600,480,0,6)&&!ReforestPolicy.Ready(true,20,60,100,480,0,6)&&!ReforestPolicy.Ready(true,20,60,600,480,6,6),
    "Not when turned off, away from home, too soon, or with enough saplings growing");
Check(!ReforestPolicy.Ready(true,double.NaN,60,600,480,0,6)&&ReforestPolicy.Ready(true,20,60,double.PositiveInfinity,480,0,6),"No bed distance, no planting; never planted counts as long ago");
Check(ReforestPolicy.Spot(30,60,20,false,false,false,false,false,false),"Open wild ground near home is fair");
Check(!ReforestPolicy.Spot(5,60,20,false,false,false,false,false,false)&&!ReforestPolicy.Spot(70,60,20,false,false,false,false,false,false),"Not right by the bed, not far from home");
Check(!ReforestPolicy.Spot(30,60,3,false,false,false,false,false,false)&&ReforestPolicy.Spot(30,60,double.MaxValue,false,false,false,false,false,false),"Clear of buildings");
Check(!ReforestPolicy.Spot(30,60,20,true,false,false,false,false,false)&&!ReforestPolicy.Spot(30,60,20,false,true,false,false,false,false)&&!ReforestPolicy.Spot(30,60,20,false,false,true,false,false,false),
    "Never in the base, on farmland or on paths");
Check(!ReforestPolicy.Spot(30,60,20,false,false,false,true,false,false)&&!ReforestPolicy.Spot(30,60,20,false,false,false,false,true,false)&&!ReforestPolicy.Spot(30,60,20,false,false,false,false,false,true),
    "Never under a roof or canopy, crowded, or by the water");
Check(ReforestPolicy.StumpKind("Beech_Stub(Clone)")==0&&ReforestPolicy.StumpKind("BirchStub")==1&&ReforestPolicy.StumpKind("OakStub")==2&&ReforestPolicy.StumpKind("Pinetree_01_Stub")==3&&ReforestPolicy.StumpKind("FirTree_Stub")==4,
    "Stumps tell which tree stood there");
Check(ReforestPolicy.StumpKind("Beech1")==-1&&ReforestPolicy.StumpKind("stubbe")==-1&&ReforestPolicy.StumpKind(null)==-1,"Trees and odd stumps are not stumps he replants");
var meadows=new[]{true,false,true,false,false};
Check(ReforestPolicy.Choose(2,meadows,0.9)==2&&ReforestPolicy.Choose(4,meadows,0)==0&&ReforestPolicy.Choose(-1,meadows,0.9)==2,"The stump's own tree if it grows here, else one that does");
Check(ReforestPolicy.Choose(-1,new bool[5],0.5)==-1&&ReforestPolicy.Choose(-1,null,0.5)==-1&&ReforestPolicy.Choose(-1,meadows,double.NaN)==0,"Nothing grows here: nothing planted");
for(double r=0;r<1;r+=0.01){int k=ReforestPolicy.Choose(-1,meadows,r);Check(k==0||k==2,"Only trees that grow here");}
Check(ReforestPolicy.Saplings.Length==ReforestPolicy.Names.Length,"Every sapling has a name");
Console.WriteLine($"Passed {checks} Gary injury, defense, food conservation, personality, petting, retreat, dungeon loot, guide and boat boarding checks.");
