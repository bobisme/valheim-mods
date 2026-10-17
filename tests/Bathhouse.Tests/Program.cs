using Bathhouse;

int checks=0;
void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
RoomFacts Good()=>new RoomFacts{Stove=true,Lit=true,Benches=1,Cover=0.95f,UnderRoof=true,Walls=8,Rays=8,Across=2.5f};

Check(Sauna.Works(Good()),"A small roofed, walled room with a lit stove and a bench is a sauna");
Check(Sauna.WhyNot(null).Contains("stove")&&Sauna.WhyNot(new RoomFacts()).Contains("stove"),"No heat: no sauna");
var r=Good();r.UnderRoof=false;Check(Sauna.WhyNot(r).Contains("roof"),"No roof");
r=Good();r.Cover=0.5f;Check(Sauna.WhyNot(r).Contains("roof"),"Half a roof isn't one");
r=Good();r.Walls=7;Check(Sauna.Works(r),"A door's gap is fine");
r=Good();r.Walls=5;Check(Sauna.WhyNot(r).Contains("walls"),"Open on three sides: no sauna");
r=Good();r.Across=7.5f;Check(Sauna.WhyNot(r).Contains("too big"),"A hall is too big to heat");
r=Good();r.Benches=0;Check(Sauna.WhyNot(r).Contains("bench"),"No bench");
r=Good();r.Lit=false;Check(Sauna.WhyNot(r).Contains("cold"),"A cold stove");
r=new RoomFacts{HotTub=true,Lit=true,Cover=0.9f,UnderRoof=true,Walls=8,Rays=8,Across=3.5f};
Check(Sauna.Works(r),"The game's hot tub in a closed room counts, its own seats and all");
r.Lit=false;Check(Sauna.WhyNot(r).Contains("hot tub"),"The hot tub's fire out");
r=Good();r.UnderRoof=false;r.Benches=0;Check(Sauna.WhyNot(r).Contains("roof"),"The roof is told first: the room before what's in it");

float sat=0;for(int i=0;i<59;i++)sat=Sauna.Sit(sat,1,false);
Check(!Sauna.Done(sat)&&Sauna.Done(Sauna.Sit(sat,1,false)),"A minute in the heat");
sat=0;for(int i=0;i<30;i++)sat=Sauna.Sit(sat,1,true);
Check(Sauna.Done(sat),"Half a minute in steam does as much");
Check(Sauna.Steaming(100,80)&&!Sauna.Steaming(100,60)&&!Sauna.Steaming(100,0)&&!Sauna.Steaming(50,80),"Steam lingers half a minute; none thrown, none; a clock set back, none");

Check(Sauna.Lengthen(0)==1&&Sauna.Lengthen(1)==1.25f&&Sauna.Lengthen(4)==2&&Sauna.Lengthen(9)==2&&Sauna.Lengthen(-2)==1,"Company: a quarter longer each, to twice as long");
Check(Sauna.Plunge(200,100)&&Sauna.Plunge(280,100)&&!Sauna.Plunge(281,100)&&!Sauna.Plunge(200,0)&&!Sauna.Plunge(50,100),"The plunge counts within three minutes of the heat");
Check(Sauna.Rested(120)==720&&Sauna.Rested(-5)==600,"Bathed adds ten minutes to Rested");
Check(Sauna.Span(59.2f)=="1m 00s"&&Sauna.Span(65)=="1m 05s"&&Sauna.Span(9)=="9s","Spans");
Console.WriteLine($"Passed {checks} sauna checks.");
