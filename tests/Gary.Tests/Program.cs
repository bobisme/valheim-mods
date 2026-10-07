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
// Food conservation across repeated harvest/gift cycles, including a serialized F6 handoff.
var random=new Random(20261007);
ForestStash stash=new ForestStash(0,0,0);int harvested=0,given=0;
for(int step=0;step<1000;step++)
{
    int kind=random.Next(-1,4),amount=random.Next(-1,9),before=stash.Count;
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
    stash=new ForestStash(stash.Berries,stash.Blueberries,stash.Mushrooms);
    Check(stash.Count==harvested-given&&stash.Count>=0&&stash.Count<=ForestStash.Capacity,"Food stays conserved and bounded across reloads");
}
Check(new ForestStash(int.MaxValue,int.MaxValue,int.MaxValue).Count==6,"Malformed saved pockets cannot exceed six items");
Check(new ForestStash(-1,-1,-1).Count==0,"Negative saved pockets cannot create food");
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
Console.WriteLine($"Passed {checks} Gary injury, defense, food conservation, personality, petting, retreat, dungeon loot and guide checks.");
