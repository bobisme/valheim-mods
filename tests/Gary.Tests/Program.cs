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
Console.WriteLine($"Passed {checks} Gary injury, retaliation, gift, and guide policy checks.");
