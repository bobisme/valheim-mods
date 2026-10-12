using Delivery;

int checks=0;
void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}

// Every meal its own call, the same every time, never the end call or the courier's answers
var meals=Enumerable.Range(0,60).Select(i=>"Meal"+i).Concat(new[]{"CookedMeat","FishCooked","SerpentStew","Bread","LoxPie","QueensJam"}).ToList();
var tunes=Policy.Tunes(meals);
Check(tunes.Count==meals.Count,"Every meal has a call");
Check(tunes.Values.Select(Policy.Key).Distinct().Count()==meals.Count,"No two meals share a call");
var reserved=new[]{Policy.Key(Policy.EndCall),Policy.Key(Policy.Answer),Policy.Key(Policy.Arrived)};
Check(tunes.Values.All(t=>!reserved.Contains(Policy.Key(t))),"No meal's call is the end call or an answer");
var again=Policy.Tunes(Enumerable.Reverse(meals));
Check(meals.All(m=>Policy.Key(again[m])==Policy.Key(tunes[m])),"The same calls whatever order the meals come in");
foreach(var t in tunes.Values)
{
    Check(t.Length>=3&&t.Length<=5,"Three to five notes");
    Check(t.All(n=>n.Partial>=Policy.LowPartial&&n.Partial<=Policy.HighPartial),"Only notes a horn plays");
    Check(t.Select(n=>n.Partial).Distinct().Count()>1,"A call is more than one note");
    for(int i=1;i<t.Length;i++)Check(Math.Abs(t[i].Partial-t[i-1].Partial)<=2,"Leaps of two overtones at most");
    Check(Policy.Length(t)<2.2,"A call is short");
    Check(Policy.Key(Policy.Parse(Policy.Key(t)))==Policy.Key(t),"A call survives being sent");
}
Check(Policy.Parse("9L 3S x 4").Length==1,"Nonsense in a sent call is dropped");

// Prices and fees
Check(Policy.Price(30,10,0)==7&&Policy.Price(0,0,0)==2&&Policy.Price(80,26,0)==18,"A coin for every six it fills, two at least");
Check(Policy.Fee("Meadows",false)==5&&Policy.Fee("Meadows",true)==8,"Five in the meadows, half again by night");
Check(Policy.Fee("Plains",false)>Policy.Fee("Swamp",false)&&Policy.Fee("AshLands",false)>Policy.Fee("Mistlands",false),"Danger costs more");
Check(Policy.Fee("Unknown",false)==10,"Somewhere new costs ten");

// Waiting and regard
Check(Math.Abs(Policy.Wait(60,0.5f)-60)<0.01&&Math.Abs(Policy.Wait(60,1)-30)<0.01&&Math.Abs(Policy.Wait(60,0)-90)<0.01,"Middling: the base; devoted: half; sullen: half again");
Check(Policy.RunSpeed(1)>Policy.RunSpeed(0),"He runs faster for those he likes");
Check(Policy.AfterTip(0.5f,0,20)<0.5f,"No tip costs a little");
Check(Policy.AfterTip(0.5f,10,20)>Policy.AfterTip(0.5f,2,20)&&Policy.AfterTip(0.5f,2,20)>0.5f,"Bigger tips, more regard");
Check(Policy.AfterTip(0.95f,1000,20)==1f&&Policy.AfterTip(0.01f,0,20)==0f,"Regard stays 0 to 1");
Check(Policy.AfterTip(0.5f,1000,20)<=0.75f,"One tip can't buy everything");
Check(Policy.AfterStiffed(0.5f)<Policy.AfterTip(0.5f,0,20),"Not paying is worse than not tipping");
float r=0.5f;int runs=0;while(r<0.8f&&runs<50){r=Policy.AfterTip(r,4,20);runs++;}
Check(runs>=2&&runs<=6,"A few good tips make him devoted");
Check(Policy.RegardName(0.1f)=="Sullen"&&Policy.RegardName(0.5f)=="Steady"&&Policy.RegardName(0.9f)=="Devoted","Regard has names");
foreach(int total in new[]{1,5,13,20,40,100,300})
{
    int[] tips=Policy.TipChoices(total);
    Check(tips.Length==3&&tips.Distinct().Count()==3&&tips[0]<tips[1]&&tips[1]<tips[2]&&tips[0]>=1,"Three different tips, smallest first");
}
Check(Policy.TipChoices(40).SequenceEqual(new[]{4,8,20}),"A tenth, a fifth and half");
Check(!Policy.Extra(0.5f,0.0)&&Policy.Extra(0.9f,0.1)&&!Policy.Extra(0.9f,0.5),"Only a devoted courier throws in extra, now and then");

// Saving
var c=new Courier{Regard=0.73f,Seed=12345,Runs=4,Tipped=31};
var back=Policy.Load(Policy.Save(c),99);
Check(Math.Abs(back.Regard-0.73f)<0.001&&back.Seed==12345&&back.Runs==4&&back.Tipped==31&&back.Name==c.Name,"A courier survives saving");
Check(Policy.Load(null,99).Seed==99&&Policy.Load("garbage",7).Regard==0.5f,"A new courier when there's none saved");
Check(Policy.Load("1|7|1|-3|x",5).Regard==1f&&Policy.Load("1|7|1|-3|x",5).Runs==0,"Bad numbers are mended");
Check(new Courier{Seed=-5}.Name.Length>0,"Any seed gives a name");

// Words
Check(Policy.Compass(0,1)=="north"&&Policy.Compass(1,0)=="east"&&Policy.Compass(-1,-1)=="south-west","Directions");
Check(Policy.Order(new[]{new KeyValuePair<string,int>("Bread",2),new KeyValuePair<string,int>("Jam",1)})=="2× Bread, 1× Jam","An order in words");
Check(string.Format(Policy.Thanks(0,20,3),"Bob").Length>0&&string.Format(Policy.Thanks(10,20,3),"Bob").Length>0,"He always has something to say");

Console.WriteLine($"Delivery: {checks} checks passed");
