using MeadowGolf;
void Check(bool valid,string message){if(!valid)throw new Exception(message);}
Check(Rules.Parse("Meadow:1:3",out var first),"valid hole");
Check(Rules.Parse("meadow:1:5",out var same)&&first.Matches(same),"course case and cup par do not split a hole");
foreach(string s in new[]{"",":1:3","Meadow:0:3","Meadow:19:3","Meadow:1:1","Meadow:1:9","<b>bad:1:3","bad\nname:1:3","a:1:3:4",new string('a',49)+":1:3"})
    Check(!Rules.Parse(s,out _),"reject invalid marker: "+s);
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
