using Wayfinder;

int checks=0;
void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
bool Near(double a,double b,double tolerance=1e-6)=>Math.Abs(a-b)<=tolerance;

// Bearings: 0 north (+z), 90 east (+x)
Check(Near(Policy.Bearing(0,1),0)&&Near(Policy.Bearing(1,0),90)&&Near(Policy.Bearing(0,-1),180)&&Near(Policy.Bearing(-1,0),270),"Compass bearings run clockwise from north");
Check(Policy.Bearing(0,0)==0,"No direction reads north rather than NaN");
for(double a=0;a<720;a+=7.5){double b=Policy.Bearing(Math.Sin(a*Math.PI/180),Math.Cos(a*Math.PI/180));Check(b>=0&&b<360&&Near(Policy.Turn(b,a),0,1e-6),"Bearings stay in [0,360)");}
Check(Near(Policy.Turn(350,10),20)&&Near(Policy.Turn(10,350),-20)&&Near(Policy.Turn(0,180),180),"Turns take the short way round");
Check(Policy.Point(0)=="N"&&Policy.Point(359)=="N"&&Policy.Point(90)=="E"&&Policy.Point(292.5)=="WNW"&&Policy.Point(-45)=="NW","Sixteen compass points");
Check(Policy.WindFrom(180)=="north"&&Policy.WindFrom(45)=="south-west","Wind is named for where it blows from");

// Sail power: the game's own wind reckoning
double astern=Policy.SailPower(0,1,1,0,1),beam=Policy.SailPower(1,0,1,0,1),ahead=Policy.SailPower(0,-1,1,0,1);
Check(Near(astern,0.7)&&Near(beam,1)&&Near(ahead,0),"Wind astern 70%, on the beam full, dead ahead nothing (Ship.GetWindAngleFactor)");
Check(Near(Policy.SailPower(1,0,0,0,1),0.25),"Even a calm gives a quarter of the pull");
Check(Policy.SailPower(0,0,1,0,1)==0&&Policy.SailPower(1,0,1,0,0)==0,"No wind or no heading: no pull");
Check(Policy.OffStern(0,1,0,1)<1&&Near(Policy.OffStern(0,-1,0,1),180)&&Near(Policy.OffStern(1,0,0,1),90),"Wind angle off the stern");
Check(Policy.WindWord(10).Contains("fair")&&Policy.WindWord(170).Contains("headwind"),"Wind words");

// Clipping lines to the visible map
double ax=-1,ay=0.5,bx=2,by=0.5;
Check(Policy.Clip(ref ax,ref ay,ref bx,ref by,0,0,1,1)&&Near(ax,0)&&Near(bx,1)&&Near(ay,0.5),"A line across the map is cut at both edges");
ax=0.2;ay=0.2;bx=0.8;by=0.7;
Check(Policy.Clip(ref ax,ref ay,ref bx,ref by,0,0,1,1)&&Near(ax,0.2)&&Near(by,0.7),"A line inside stays whole");
ax=-1;ay=-1;bx=-0.5;by=2;
Check(!Policy.Clip(ref ax,ref ay,ref bx,ref by,0,0,1,1),"A line outside is dropped");
ax=0.5;ay=-1;bx=0.5;by=0.5;
Check(Policy.Clip(ref ax,ref ay,ref bx,ref by,0,0,1,1)&&Near(ay,0)&&Near(by,0.5),"A vertical line is cut at the bottom edge");

// Laying and following a course
var course=new List<(double x,double z)>{(0,0),(1000,0),(1000,1000)};
Check(Policy.InsertAt(course,500,20,60)==1,"A click on a leg goes into that leg");
Check(Policy.InsertAt(course,500,300,60)==3,"A click away from every leg goes at the end");
Check(Policy.InsertAt(course,1000,0,60)==3,"A click on a waypoint itself is not an insertion");
Check(Policy.Nearest(course,990,15,60)==1&&Policy.Nearest(course,500,500,60)==-1,"Nearest waypoint within reach, or none");
Check(Near(Policy.Length(course,0),2000)&&Near(Policy.Length(course,1),1000)&&Policy.Length(course,5)==0,"Course length from a waypoint on");
Check(Policy.Advance(course,0,10,10,40)==1,"Reaching the first waypoint makes the second the goal");
Check(Policy.Advance(course,1,500,0,40)==1,"Halfway along a leg nothing changes");
Check(Policy.Advance(course,1,1000,990,40)==3,"Cutting the corner to the last waypoint finishes the course");
Check(Policy.Advance(course,3,0,0,40)==3,"A finished course stays finished");

// The course as saved text
var named=new List<(double x,double z,string name)>{(1.25,-2.5,"Home"),(300.04,40,"Swamp; landing|x,y"),(-11000,11000,"")};
string saved=Policy.Save(named,1);
var back=new List<(double x,double z,string name)>();
Check(Policy.Load(saved,back,out int next)&&next==1&&back.Count==3,"A saved course loads back");
Check(Near(back[0].x,1.25,0.051)&&back[1].name=="Swamp landingxy"&&back[2].name=="","Names lose the separators; positions keep a decimetre");
Check(!Policy.Load("",back,out _)&&!Policy.Load("2|0|1,1,a",back,out _)&&!Policy.Load("junk",back,out _),"Empty, unknown versions and junk do not load");
Check(Policy.Load("1|99|1,1,a;bad;2,nan,b;3,3",back,out next)&&back.Count==2&&next==2,"Bad waypoints are skipped and next is clamped");
{
    var longCourse=new List<(double x,double z,string name)>();for(int i=0;i<40;i++)longCourse.Add((i*100,0,i==12?"Cape":""));
    Check(Policy.Load(Policy.Ahead(longCourse,10),back,out next)&&back.Count==Policy.AheadWaypoints&&next==1&&Near(back[1].x,1000,0.01),"Aboard: the leg being sailed and the next few");
    Check(back[0].name=="waypoint 10"&&back[3].name=="Cape","Unnamed waypoints keep their number in the whole course");
    Check(Policy.Ahead(longCourse,10).Length*2<Policy.Save(longCourse,10).Length,"Far less rides on the ship than the whole course");
    Check(Policy.Ahead(new List<(double x,double z,string name)>(),0)=="","No course, nothing aboard");
    Check(Policy.Load(Policy.Ahead(longCourse,0),back,out next)&&next==0&&back.Count==Policy.AheadWaypoints,"A course not yet begun starts at its first waypoint");
}
Check(Policy.Load("1|0|"+string.Join(";",Enumerable.Range(0,100).Select(i=>i+","+i+",w")),back,out _)&&back.Count==Policy.MaxWaypoints,"A course holds at most the maximum waypoints");
Check(Policy.Load("1|0|99999,0,far",back,out _)&&back.Count==0,"Waypoints off the edge of the world are dropped");
Check(Policy.Clean(new string('a',80)).Length==40,"Names are kept short");

// Distances and times
Check(Policy.Distance(234)=="230 m"&&Policy.Distance(1234)=="1.2 km"&&Policy.Distance(12345)=="12 km","Distances read naturally");
Check(Policy.Duration(45)=="45 s"&&Policy.Duration(125)=="2:05"&&Policy.Duration(3725)=="1 h 02"&&Policy.Duration(double.NaN)=="—","Times read naturally");

// Soundings
Check(Policy.Depth(30,30-1.83*3)=="3.0 fathoms"&&Policy.Depth(30,31)=="aground"&&Policy.Depth(30,-200).StartsWith("no bottom")&&Policy.Depth(30,30-1.83*15)=="15 fathoms","Depth in fathoms");
Check(Policy.Shoal(1.5)&&!Policy.Shoal(3),"Shoal water is under a keel and a little");
Check(Policy.Bottom("Swamp")=="black mud"&&Policy.Bottom("Ocean").Contains("clean")&&Policy.Bottom("unknown")=="sand","The tallow brings up the bottom");

// The sky for a sun-compass
Check(Policy.Read(true,false,false,false)==Policy.Sky.Sun,"Clear day: the sun");
Check(Policy.Read(true,true,true,false)==Policy.Sky.Sunstone&&Policy.Read(true,true,false,false)==Policy.Sky.Hidden,"Overcast day: the sunstone, or nothing");
Check(Policy.Read(false,false,true,false)==Policy.Sky.Star&&Policy.Read(false,true,true,false)==Policy.Sky.Dark,"Night: the lodestar if clear; a sunstone does not help");
Check(Policy.Read(true,false,true,true)==Policy.Sky.Dark,"Indoors: no sky");
Check(!Policy.Overcast("Clear",false,false,0.005)&&!Policy.Overcast("Twilight_Clear",false,false,0.005)&&!Policy.Overcast("Mistlands_clear",false,false,0.01),"Clear weathers show the sun");
Check(Policy.Overcast("Rain",true,false,0.01)&&Policy.Overcast("Misty",false,false,0.01)&&Policy.Overcast("Darklands_dark",false,true,0.01)&&Policy.Overcast("Clear",false,false,0.05),"Rain, mist, gloom and thick fog hide it");
Check(Policy.Weather("ThunderStorm")=="thunderstorm"&&Policy.Weather("LightRain")=="light rain"&&Policy.Weather("Twilight_SnowStorm")=="snowstorm"&&Policy.Weather("Heath clear")=="clear skies"&&Policy.Weather("Misty")=="mist","Weather names");
Check(Policy.WindStrength(0.1)=="calm"&&Policy.WindStrength(0.95)=="a storm wind","Wind strength words");

// Ship names
var names=new HashSet<string>();
for(long seed=-50;seed<50;seed++){string n=Policy.ShipName(seed);Check(!string.IsNullOrEmpty(n),"Every ship gets a name");names.Add(n);}
Check(names.Count>=20&&Policy.ShipName(7)==Policy.ShipName(7),"Names vary, and a ship keeps hers");

Check(Near(Policy.MastheadSight(3,16,16),3)&&Near(Policy.MastheadSight(3,40,16),3),"The tallest seat gets the whole bonus, no more");
Check(Near(Policy.MastheadSight(3,4,16),2)&&Near(Policy.MastheadSight(3,-1,16),1),"A quarter the height sees half as far beyond; none at the waterline");
Check(Policy.MastheadSight(3,7,16)<Policy.MastheadSight(3,10,16),"A taller mast sees farther");

Console.WriteLine($"Wayfinder: {checks} checks passed");
