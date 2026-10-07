using ChestSearch;
int checks=0;
void Check(bool ok,string message){checks++;if(!ok)throw new Exception(message);}
Check(Policy.Matches("Iron Scrap","IRON scr"),"All words match case-insensitively");
Check(Policy.Matches("Bronze","  \t ")&&Policy.Matches("Bronze",""),"Empty query shows all stacks");
Check(!Policy.Matches("Iron Scrap","iron wood")&&!Policy.Matches("Wood",new string('x',81)),"Missing words and oversized queries cannot match");
Check(Policy.Matches("Écorce ancienne","écorce"),"Localized item names are searchable");
foreach(double d in new[]{0.0,5,20,40})Check(Policy.InRange(d,40),"Valid distances within radius are allowed");
foreach(double d in new[]{-1.0,40.01,double.NaN,double.PositiveInfinity})Check(!Policy.InRange(d,40),"Invalid and outside distances are rejected");
Check(!Policy.InRange(1,4.99)&&!Policy.InRange(1,40.01),"Search range is bounded");
var random=new Random(20261007);
for(int n=0;n<500;n++)
{
    int source=random.Next(1,100),wanted=random.Next(1,100),maximum=random.Next(1,100),destination=random.Next(0,maximum+1);
    int moved=Policy.Capacity(source,wanted,maximum,destination,true,false);
    Check(moved>=0&&moved<=source&&moved<=wanted&&moved+destination<=maximum,"Partial moves conserve items and fit the destination");
    Check(Policy.Capacity(source,wanted,maximum,destination,false,false)==0,"Different destination types never swap back into a chest");
}
Check(Policy.Capacity(50,50,50,47,true,false)==3,"Nearly-full matching stack takes exactly three");
Check(Policy.Capacity(10,10,50,0,false,true)==10,"Empty slot accepts original full stack");
foreach(double at in new[]{0.0,0.1,7.99})
{
    var lease=new Lease(42,0);
    Check(!lease.Ready(at,true,true,true),"Owning chest without grant is not enough");
    Check(!lease.Reply(99,true,at)&&!lease.Ready(at,true,true,true),"Only captured chest owner can grant");
    Check(lease.Reply(42,true,at),"Current owner grant accepted");
    Check(!lease.Ready(at,false,true,true),"Reply before ownership data arrives cannot transfer");
    Check(!lease.Ready(at,true,false,true)&&!lease.Ready(at,true,true,false),"Closing/access failure prevents transfer");
    Check(lease.Ready(at,true,true,true),"Granted owned active valid chest can transfer");
    Check(!lease.Reply(42,true,at),"Duplicate grants do not restart a transfer");
    lease.Finish();Check(!lease.Ready(at,true,true,true)&&!lease.Reply(42,true,at),"Completed or cancelled lease cannot move a second time");
}
foreach(double at in new[]{8.0,9,double.NaN,double.PositiveInfinity})
{
    var lease=new Lease(42,0);
    Check(lease.Expired(at)&&!lease.Reply(42,true,at)&&!lease.Ready(at,true,true,true),"Late or invalid grants never authorize transfers");
}
var denied=new Lease(42,0);Check(denied.Reply(42,false,1)&&!denied.Reply(42,true,2)&&!denied.Ready(2,true,true,true),"Denied requests cannot later be reused");
Console.WriteLine($"Passed {checks} chest search, capacity, conservation, asynchronous permission and cancellation checks.");
