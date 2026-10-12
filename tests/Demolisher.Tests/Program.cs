using Demolisher;
int checks=0;
void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
var square=new List<(double x,double z)>{(0,0),(10,0),(10,10),(0,10)};
Check(Area.Inside(square,5,5)&&Area.Inside(square,0.1,9.9),"Points inside a square are inside");
Check(!Area.Inside(square,-0.1,5)&&!Area.Inside(square,5,10.1)&&!Area.Inside(square,20,5),"Points outside a square are outside");
Check(!Area.Inside(square.Take(2).ToList(),5,0),"Two corners enclose nothing");
var ell=new List<(double x,double z)>{(0,0),(10,0),(10,4),(4,4),(4,10),(0,10)};
Check(Area.Inside(ell,2,8)&&Area.Inside(ell,8,2)&&!Area.Inside(ell,8,8),"The notch of an L is outside it");
Check(Math.Abs(Area.Size(square)-100)<1e-9&&Math.Abs(Area.Size(ell)-64)<1e-9,"Areas by the shoelace rule, either winding");
Check(Math.Abs(Area.Size(square.AsEnumerable().Reverse().ToList())-100)<1e-9,"Clockwise outlines have positive size");
Check(Area.Closes(square,1,1)&&!Area.Closes(square,2,2)&&!Area.Closes(square.Take(2).ToList(),0,0),"Clicking near the first corner closes three or more corners");
Check(Area.Problem(square,0,0,true)==null,"A square closes");
Check(Area.Problem(square.Take(2).ToList(),0,0,true)!=null,"Two corners can't close");
Check(Area.Problem(new List<(double,double)>{(0,0),(10,0),(20,0)},0,0,true)!=null,"A flat outline has no room inside");
var bow=new List<(double x,double z)>{(0,0),(10,0),(0,10),(10,10)};
Check(Area.Problem(bow,0,0,true)!=null,"A bow tie's closing edge crosses another edge");
Check(Area.Problem(new List<(double,double)>(),3,3,false)==null,"The first corner goes anywhere");
Check(Area.Problem(square.Take(1).ToList(),0.2,0.2,false)!=null,"A corner on top of the last one is refused");
Check(Area.Problem(new List<(double,double)>{(0,0),(10,0),(10,10)},5,-5,false)!=null,"An edge crossing an earlier edge is refused");
Check(Area.Problem(new List<(double,double)>{(0,0),(10,0),(10,10)},0,10,false)==null,"An edge clear of the others is fine");
var many=Enumerable.Range(0,Area.MaximumCorners).Select(i=>(Math.Cos(i*0.1)*20,Math.Sin(i*0.1)*20)).ToList();
Check(Area.Problem(many,0,0,false)!=null,"No more than the maximum corners");
var circle=Area.Circle(square);
Check(circle.x==5&&circle.z==5&&Math.Abs(circle.radius-Math.Sqrt(50))<1e-9,"The search circle covers every corner");
var (title,body)=Area.Question(3,new Dictionary<string,int>{["protected by a ward"]=2,["chests with items"]=5,["ships or carts"]=0});
Check(title=="Demolish 3 structures?","Title counts structures");
Check(body.Contains("Left standing: 5 chests with items, 2 protected by a ward.")&&!body.Contains("ships"),"Skipped reasons largest first, zero counts left out");
Check(Area.Question(1,new Dictionary<string,int>()).title=="Demolish 1 structure?"&&!Area.Question(1,new Dictionary<string,int>()).body.Contains("Left standing"),"Singular, and no skipped line when nothing is skipped");
Check(Area.Question(0,new Dictionary<string,int>()).title=="Nothing to demolish","Nothing inside says so");
var random=new Random(20261012);
for(int n=0;n<2000;n++)
{
    double x=random.NextDouble()*30-10,z=random.NextDouble()*30-10;
    bool rect=x>0&&x<10&&z>0&&z<10;
    if(Math.Abs(x)>1e-6&&Math.Abs(x-10)>1e-6&&Math.Abs(z)>1e-6&&Math.Abs(z-10)>1e-6)Check(Area.Inside(square,x,z)==rect,"Random points agree with the square");
}
Console.WriteLine($"Demolisher: {checks} checks passed");
