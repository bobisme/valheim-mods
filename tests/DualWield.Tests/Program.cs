using DualWield;

int checks=0;
void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
bool Near(double a,double b)=>Math.Abs(a-b)<1e-9;

Check(Policy.FamilyOf(true,true,false)==Family.Axes&&Policy.FamilyOf(true,false,true)==Family.Knives,"One-handed axes and knives have dual move sets");
Check(Policy.FamilyOf(false,true,false)==Family.None,"Two-handed axes are not paired");
Check(Policy.FamilyOf(true,false,false)==Family.None,"Swords, clubs and others have no dual move set yet");

Check(Policy.Equip(Family.Axes,Family.Axes,false,20,20,false)==Verdict.Dual,"A second axe goes to the off hand at skill 20");
Check(Policy.Equip(Family.Axes,Family.Axes,false,19.9,20,false)==Verdict.NeedsSkill,"Below the skill it is refused with a reason");
Check(Policy.Equip(Family.Axes,Family.Knives,false,99,20,false)==Verdict.Single,"Mixed pairs are not allowed");
Check(Policy.Equip(Family.None,Family.Axes,false,99,20,false)==Verdict.Single,"An empty or non-weapon main hand equips normally");
Check(Policy.Equip(Family.Knives,Family.Knives,true,99,20,false)==Verdict.Single,"The same item cannot be in both hands");
Check(Policy.Equip(Family.Axes,Family.Axes,false,99,20,true)==Verdict.Single,"Holding the swap key replaces the main weapon");
Check(Policy.Equip(Family.Axes,Family.None,false,99,20,false)==Verdict.Single,"A non-matching item equips normally");

Check(Near(Policy.ChainAverage(1,3,2),4.0/3),"A three-hit chain with a doubled last hit averages 4/3");
Check(Near(Policy.ChainAverage(0.5,1,3),0.5)&&Near(Policy.ChainAverage(0.5,0,3),0.5),"A single attack averages its own multiplier");
Check(Policy.ChainAverage(0,3,2)==0&&Policy.ChainAverage(double.NaN,3,2)==0,"No damage multiplier, no damage");
Check(Near(Policy.ChainAverage(1,4,double.NaN),1),"A missing last-hit multiplier counts as 1");

// Equal weapons: each hit is worth 62% of both, i.e. 1.24× one weapon, when the chains average the same.
Check(Near(Policy.DamageFactor(50,50,0.62,1,1),1.24),"Equal weapons, equal chains: 1.24× per hit");
Check(Near(Policy.DamageFactor(50,50,0.62,4.0/3,2),1.24*(4.0/3)/2),"A heavier dual chain is scaled down to the same total");
Check(Near(Policy.DamageFactor(60,30,0.62,1,1),0.62*90/60),"A weaker off-hand adds less");
Check(Policy.DamageFactor(60,500,0.62,1,1)<=0.62*560/60+1e-9,"The factor is a share of the real weapons, never more");
Check(Policy.DamageFactor(0,50,0.62,1,1)==1&&Policy.DamageFactor(50,50,0.62,0,1)==1&&Policy.DamageFactor(50,50,0.62,1,double.NaN)==1,"Invalid inputs leave damage unchanged");
Check(Near(Policy.DamageFactor(50,-5,0.62,1,1),0.62)&&Near(Policy.DamageFactor(50,50,0,1,1),1.24),"Bad off-hand damage or share fall back safely");

Check(Near(Policy.Stamina(20,1.3),26)&&Policy.Stamina(double.NaN,1.3)==0&&Near(Policy.Stamina(20,0),20)&&Policy.Stamina(-5,1.3)==0,"Stamina is the main weapon's, 1.3× per swing");
Check(Near(Policy.Effective(12,20,0.5),22)&&Policy.Equip(Family.Axes,Family.Axes,false,Policy.Effective(12,20,0.5),20,false)==Verdict.Dual,"Woodcutting gives half credit toward axes");
Check(Near(Policy.Effective(12,100,0.5),62)&&Near(Policy.Effective(12,40,0),12)&&Near(Policy.Effective(12,40,5),52),"Credit is clamped to between none and full");
Check(Near(Policy.Effective(double.NaN,double.NaN,0.5),0)&&Near(Policy.Effective(-3,10,0.5),5),"Invalid skills count as nothing");
Console.WriteLine($"Passed {checks} dual wield pairing, skill, damage and stamina checks.");
