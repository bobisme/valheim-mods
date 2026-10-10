using MeadowGolf;
void Check(bool valid,string message){if(!valid)throw new Exception(message);}
Check(Rules.Parse("Meadow:1:3",out var first),"valid hole");
Check(Rules.Parse("meadow:1:5",out var same)&&first.Matches(same),"course case and cup par do not split a hole");
foreach(string s in new[]{"",":1:3","Meadow:0:3","Meadow:19:3","Meadow:1:1","Meadow:1:9","<b>bad:1:3","bad\nname:1:3","a:1:3:4",new string('a',49)+":1:3"})
    Check(!Rules.Parse(s,out _),"reject invalid marker: "+s);
Check(Rules.EditLabel("test2","Meadow:4:5",out var renamed)&&renamed.Label=="test2:4:5","plain course name keeps hole/par");
Check(Rules.EditLabel("  Bob's Course  ","Meadow:1:3",out renamed)&&renamed.Label=="Bob's Course:1:3","trimmed plain course name");
Check(Rules.EditLabel(" Bob : 2 : 4 ","Meadow:1:3",out renamed)&&renamed.Label=="Bob:2:4","full label edits hole/par and trims spaces");
Check(Rules.EditLabel(new string('a',48),"Meadow:18:8",out renamed)&&renamed.Number==18&&renamed.Par==8,"longest course name");
foreach(string text in new[]{"","  ","<b>test</b>","bad\nname",new string('a',49),"test:2","test:19:3","test:2:9","test:2:3:4"})
    Check(!Rules.EditLabel(text,"Meadow:4:5",out _),"invalid edit rejected: "+text);
Check(!Rules.EditLabel(null,"Meadow:1:3",out _)&&!Rules.EditLabel("test2","corrupt",out _),"invalid current label is not guessed");
for(int mode=0;mode<3;mode++)
{
    double previous=0;
    for(int n=0;n<=100;n++)
    {
        float power=n/100f;
        Check(Rules.ValidShot(mode,power,1,0),"valid shot");
        double speed=Rules.Speed(mode,power);Check(speed>=previous&&speed<=26,"monotonic bounded speed");previous=speed;
    }
}
Check(!Rules.ValidShot(0,float.NaN,1,0)&&!Rules.ValidShot(3,.5f,1,0)&&!Rules.ValidShot(0,.5f,2,0)&&!Rules.ValidShot(0,.5f,1,.3),"bad network shot rejected");
Check(Rules.Captures(.2,.1,1,1),"slow ball in cup");
Check(!Rules.Captures(.2,.1,8,1),"fast flyover not a score");
Check(!Rules.Captures(.1,2,1,1)&&!Rules.Captures(.1,-2,1,1),"above/below cup not a score");
Check(!Rules.Captures(.1,0,1,0),"no scoring unplayed balls");
Check(!Rules.Captures(double.NaN,0,1,1),"nonfinite position rejected");
string card="";for(int i=18;i>=1;i--)card=Rules.Record(card,i,3,i%4+1);
Check(Rules.ReadCard(card).Count==18&&card.Length<=256,"all 18 holes fit bounded card");
card=Rules.Record(card,7,4,2);var rows=Rules.ReadCard(card);Check(rows.Count==18&&rows[6].Hole==7&&rows[6].Strokes==2,"replay replaces prior score");
Check(Rules.ReadCard("1,3,2;1,3,4").Count==0&&Rules.ReadCard("1,3,100").Count==0,"invalid persisted card rejected");
Check(Rules.Outcome(1,3)=="Hole in one!"&&Rules.Outcome(3,3)=="Par"&&Rules.Outcome(4,3)=="Bogey","score names");
Check(Rules.Begin("Meadow:1:3","",4,false,first,false).Strokes==5,"same-hole return adds exactly one penalty");
Check(Rules.Begin("Meadow:1:3","1,3,2",2,true,first,false).Strokes==0,"replay finished hole starts at zero");
Rules.Parse("Meadow:2:3",out var next);
Check(Rules.Begin("Meadow:1:3","1,3,2",2,true,next,false).Card=="1,3,2","next hole keeps prior result");
Check(Rules.Begin("Elsewhere:1:3","1,3,2",2,true,next,false).Card=="","changing courses starts fresh");
Check(Rules.Begin("Meadow:1:3","1,3,2",2,true,next,true).Card=="","explicit new round clears card");
Console.WriteLine("Golf rules passed: marker matching, shot bounds, cup capture, penalties/card bounds and hole replay.");

// Complete both course lengths, including duplicate/missing endpoints and an extra back nine.
foreach(int holes in new[]{9,18})
{
    var markers=new List<Rules.CourseMarker>();
    for(int h=1;h<=18;h++)
    {markers.Add(new Rules.CourseMarker{Hole=h,X=h*1000,Z=0});markers.Add(new Rules.CourseMarker{Hole=h,Cup=true,X=h*1000+30,Z=0});}
    Check(Rules.CourseError(markers,holes)=="","course can span many loaded zones");
    string round="";
    for(int h=1;h<=holes;h++)
    {
        Check(Rules.NextHole(round,holes)==h,"sequential match hole "+h);
        round=Rules.Record(round,h,3,3);
    }
    Check(Rules.NextHole(round,holes)==0,"match completes at its declared length");
    markers.RemoveAt(3);Check(Rules.CourseError(markers,holes).Contains("Hole 2"),"missing cup named");
    markers.Add(new Rules.CourseMarker{Hole=2,Cup=true,X=2030});
    markers.Add(new Rules.CourseMarker{Hole=1});Check(Rules.CourseError(markers,holes).Contains("found 2"),"duplicate tee rejected");
}
Check(Rules.NextHole("1,3,2;3,3,4",9)==2,"unfinished hole cannot be skipped");
Check(Rules.NextHole("",0)==0&&Rules.CourseError(new(),10)!="","only 9 and 18 hole matches");
var far=new List<Rules.CourseMarker>{new(){Hole=1},new(){Hole=1,Cup=true,X=241}};
Check(Rules.CourseError(far,9).Contains("240"),"maximum hole distance checked");
Check(Rules.Resistance(Rules.Surface.Forest)>Rules.Resistance(Rules.Surface.Grass)&&Rules.Resistance(Rules.Surface.Marsh)>Rules.Resistance(Rules.Surface.Forest)&&Rules.Resistance(Rules.Surface.Snow)>Rules.Resistance(Rules.Surface.Firm),"rough, marsh and snow affect roll");
Check(!Rules.Submerged(30.02,30)&&Rules.Submerged(29.95,30),"hazards activate on actual water entry, not above the surface");
Check(!Rules.Submerged(-10,-10000)&&!Rules.Submerged(30,double.NaN),"absent liquid and invalid level stay dry");
Console.WriteLine("Golf matches passed: complete 9/18-hole rounds, missing/duplicate endpoints, progression and terrain hazards.");

var vertical=new List<Rules.CourseMarker>{new(){Hole=1,Y=0},new(){Hole=1,Cup=true,Y=241}};
Check(Rules.CourseError(vertical,9).Contains("240"),"extreme vertical hole separation rejected");

// Sequential pairs, reversed placement, multiple incomplete holes and course bounds.
foreach(bool cupFirst in new[]{false,true})
{
    var placed=new List<Rules.CourseMarker>();
    for(int h=1;h<=18;h++)foreach(bool cup in new[]{cupFirst,!cupFirst})
    {
        int n=Rules.NextMarker(placed,cup,h*4,0,cup?2:0,out int par);
        Check(n==h&&par==3,"automatic pair number "+h+" / cup="+cup);
        placed.Add(new Rules.CourseMarker{Hole=n,Par=par,Cup=cup,X=h*4,Z=cup?2:0});
    }
    Check(Rules.NextMarker(placed,false,0,0,0,out _)==0,"19th hole never silently duplicates an existing hole");
}
var incomplete=new List<Rules.CourseMarker>{new(){Hole=1,Par=4,X=0},new(){Hole=2,Par=5,X=100}};
Check(Rules.NextMarker(incomplete,true,99,0,0,out int copiedPar)==2&&copiedPar==5,"closest unpaired tee and its par");
Check(Rules.NextMarker(incomplete,true,1000,0,0,out _)==3,"faraway unmatched marker starts a new hole");
Check(Rules.NextMarker(incomplete,false,99,0,0,out _)==3,"two tees are never paired together");
incomplete.Add(new(){Hole=1,Cup=true});incomplete.Add(new(){Hole=2});
Check(Rules.NextMarker(incomplete,true,99,0,0,out _)==3,"ambiguous duplicates are never auto-paired");
Check(Rules.NextMarker(new(),false,double.NaN,0,0,out _)==0,"nonfinite auto-placement rejected");
Console.WriteLine("Golf automatic labels passed: 18 complete pairs in either order, nearest unmatched marker, inherited par, distance, duplicate and hole-limit checks.");
