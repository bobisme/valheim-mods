using Respite;

int checks=0;
void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
const double Day=1800,Week=7*Day,Gap=1.5*Day;
string Blocked(List<Raid> raids,double now,float x,float z)=>Policy.Blocked(raids,now,x,z,500,Gap,3,Week,Day);

var raids=new List<Raid>();
Check(Blocked(raids,10*Day,0,0)=="","No raids yet: one may come");
Policy.Add(raids,new Raid{T=10*Day,X=0,Z=0,Src="army_eikthyr"},8*Day);
Check(Blocked(raids,10*Day+600,100,0).Contains("army_eikthyr"),"A raid nearby rests the land");
Check(Blocked(raids,10*Day+600,800,0)=="","A base farther than the area has its own clock");
Check(Blocked(raids,11.4*Day,0,0)!=""&&Blocked(raids,11.6*Day,0,0)=="","The rest lasts the cooldown, then ends");
Check(Blocked(raids,9*Day,0,0)=="","A raid in the future (a clock set back) doesn't block");
Check(!Policy.Add(raids,new Raid{T=10*Day+60,X=40,Z=0,Src="omens"},8*Day)&&raids.Count==1,"The same raid told twice is one raid");
Check(Policy.Add(raids,new Raid{T=12*Day,X=0,Z=0},8*Day)&&Policy.Add(raids,new Raid{T=14*Day,X=0,Z=0},8*Day),"Raids apart in time are kept");
Check(Blocked(raids,16*Day,0,0).StartsWith("3 raids"),"Three in a week: no fourth");
Check(Blocked(raids,17.1*Day,0,0)=="","The oldest drops out of the week");
Check(Policy.Blocked(raids,16*Day,0,0,500,Gap,0,Week,Day)=="","Cap 0: no cap");
Policy.Add(raids,new Raid{T=30*Day,X=0,Z=0},8*Day);
Check(raids.Count==1,"Raids older than kept are forgotten");

Check(Policy.Span(Day*2,Day)=="2 days"&&Policy.Span(Day/4,Day)=="6 hours"&&Policy.Span(10,Day)=="under an hour","Spans read as the game's days and hours");

Check(Policy.Edge(new float[0],0)==0&&Policy.Edge(new float[0],70)==70,"No buildings: the edge is the given border");
var d=Enumerable.Range(1,40).Select(i=>(float)i).ToList();d.Add(140);
Check(Policy.Edge(d,0)>=38&&Policy.Edge(d,0)<=40,"One stray far piece doesn't move the edge");
Check(Policy.Edge(new[]{10f,20f,30f},0)==30,"A few pieces: the farthest");
Check(Policy.Edge(new[]{10f,20f},90)==90,"A colony's border counts when it's farther");
Check(Policy.Distance(10,40,80)==80&&Policy.Distance(60,40,80)==100&&Policy.Distance(400,40,80)==300,"Gather past the edge, not too near, not lost far off");
var a=Policy.Angles(Policy.Seed(120.4f,-33.2f),16).ToList();
Check(a.Count==16&&a.Distinct().Count()==16&&a.All(x=>x>=0&&x<360),"Sixteen distinct directions");
Check(a.SequenceEqual(Policy.Angles(Policy.Seed(120.4f,-33.2f),16)),"The same place gives every game the same directions");
Check(Policy.Compass(0,1)=="north"&&Policy.Compass(1,1)=="north-east"&&Policy.Compass(1,0)=="east"&&Policy.Compass(0,-1)=="south"&&Policy.Compass(-1,0.1f)=="west","Compass");
Console.WriteLine($"Passed {checks} raid clock and muster checks.");
