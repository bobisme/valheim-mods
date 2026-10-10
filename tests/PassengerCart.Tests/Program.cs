using PassengerCart;

int checks=0;
void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}

var seats=Policy.Seats();
Check(seats.Length==4,"Four seats");
foreach(var s in seats)
{
    Check(Math.Abs(s.X)+Policy.SeatWidth/2<=Policy.WallX+1e-9,"Every seat fits between the side walls");
    Check(s.Z+Policy.SeatDepth/2<Policy.FrontZ&&s.Z-Policy.SeatDepth/2>Policy.BackZ,"Every seat fits between the front and back walls");
    Check(Math.Abs(s.DetachX)>Policy.WallX+0.5&&Math.Sign(s.DetachX)==Math.Sign(s.X),"A passenger steps off on their own side, clear of the cart");
}
for(int i=0;i<seats.Length;i++)for(int j=i+1;j<seats.Length;j++)
{
    bool apart=Math.Abs(seats[i].X-seats[j].X)>=Policy.SeatWidth-1e-9||Math.Abs(seats[i].Z-seats[j].Z)>=Policy.SeatDepth;
    Check(apart,"Seats never overlap");
}
Check(Policy.SeatWidth*2<=Policy.BenchWidth+1e-9&&Policy.BenchWidth<=Policy.WallX*2,"Two seats fit on a bench and the bench fits in the bed");
// Legroom: a sitter's feet reach about 0.55 m ahead of the seat's centre.
double front=Policy.Rows.Max(),back=Policy.Rows.Min();
Check(front+0.55<=Policy.FrontZ+0.05,"The front row's feet fit before the front wall");
Check(back+0.55<=front-Policy.SeatDepth/2-0.02+0.15,"The back row's feet reach under the front bench, not through its backrest");
Check(Policy.CargoZ-Policy.CargoDepth/2>=Policy.BackZ-0.01&&Policy.CargoZ+Policy.CargoDepth/2<=back-Policy.SeatDepth/2,"The cargo crate sits behind the back bench, inside the back wall");
Check(Policy.SeatTop>Policy.Floor&&Policy.SeatTop<Policy.WallTop,"Seats sit above the floor and below the wall top");
Check(Math.Abs(Policy.SeatTop-Policy.SitDrop-Policy.Floor)<0.05,"The sitting pose puts a passenger's feet on the floor");
Check(Policy.CenterOfMassHeight<0.52*Policy.Scale,"The centre of mass sits below the axle");

Check(Math.Abs(Policy.Mass(50)-60)<1e-9&&Policy.Mass(double.NaN)>0&&Policy.Mass(0)>0,"A little heavier than the vanilla cart, never weightless");
Check(Policy.Health(1000)==2000&&Policy.Health(500)==1000,"Twice the vanilla health");
Check(Policy.Health(double.NaN)>0&&Policy.Health(0)>0&&Policy.Health(-5)>0,"A missing vanilla health still gives a sturdy cart");
Check(Policy.Taken(0.1)&&!Policy.Taken(0.5),"A seat is taken only by someone sitting in it");
Check(!Policy.Tipped(1)&&!Policy.Tipped(0.5)&&Policy.Tipped(0.1)&&Policy.Tipped(-1)&&Policy.Tipped(double.NaN),"Passengers get off a cart on its side or upside down");

Console.WriteLine($"PassengerCart: {checks} checks passed");
