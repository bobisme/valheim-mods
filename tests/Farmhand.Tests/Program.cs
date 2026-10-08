using Farmhand;

int checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new Exception(message);
}
for (int count = -10; count <= 30; count++)
{
    float[] row = Layout.Offsets(count, 2f);
    Check(row.Length >= 1 && row.Length <= 9, "Unbounded row");
    Check(Math.Abs(row.Sum()) < 0.0001f, "Row isn't centered on aim point");
    Check(row.Distinct().Count() == row.Length, "Duplicate planting targets");
    for (int i = 1; i < row.Length; i++) Check(Math.Abs(row[i] - row[i - 1] - 2f) < 0.0001f, "Uneven row spacing");
}
foreach (float radius in new[] { 0f, 0.5f, 1f, 2.5f, 5f })
foreach (float requested in new[] { -1f, 0.5f, 1.8f, 100f, float.NaN, float.PositiveInfinity })
{
    float spacing = Layout.Spacing(requested, radius);
    Check(!float.IsNaN(spacing) && !float.IsInfinity(spacing), "Invalid spacing");
    Check(spacing > radius * 2 && spacing >= 0.5f, "Growth spaces overlap");
}
Check(Layout.Offsets(1, 2f).Single() == 0f, "Single crop must use aim point");
Check(HarvestConfirmation.Observe(false, false, 0) == HarvestOutcome.Waiting, "Unacknowledged harvest replants early");
Check(HarvestConfirmation.Observe(false, false, 1.999) == HarvestOutcome.Waiting, "Premature RPC timeout");
Check(HarvestConfirmation.Observe(false, false, 2) == HarvestOutcome.TimedOut, "Unbounded wait");
Check(HarvestConfirmation.Observe(false, false, 100) == HarvestOutcome.TimedOut, "Late unacknowledged crop replanted");
Check(HarvestConfirmation.Observe(true, false, 0.1) == HarvestOutcome.Confirmed, "Destroyed harvested crop not acknowledged");
Check(HarvestConfirmation.Observe(false, true, 0.1) == HarvestOutcome.Confirmed, "Picked crop not acknowledged");
Check(HarvestConfirmation.Observe(false, true, 2.1) == HarvestOutcome.Confirmed, "Confirmed response at deadline discarded");
PlotPoint[] Points(params double[] xy)=>Enumerable.Range(0,xy.Length/2).Select(i=>new PlotPoint(xy[i*2],xy[i*2+1])).ToArray();
void Reject(PlotPoint[] boundary,double spacing=2,double area=1000)
{
    bool caught=false;try{PlotLayout.Plan(boundary,spacing,area);}catch(ArgumentException){caught=true;}
    Check(caught,"Invalid plot accepted");
}
var rectangle=Points(0,0,6,0,6,4,0,4);
var rectangularSites=PlotLayout.Plan(rectangle,2,100);
Check(rectangularSites.Count==6,"Rectangle should give two rows of three with half-cell borders");
Check(rectangularSites.Any(p=>p.X==1&&p.Z==1)&&rectangularSites.Any(p=>p.X==5&&p.Z==3),"Half-cell border phase");
var concave=Points(0,0,8,0,8,2,2,2,2,8,0,8);
Check(PlotLayout.Validate(concave,100,out _)&&PlotLayout.Area(concave)==28,"L-shaped boundary must retain its notch");
Check(PlotLayout.Contains(concave,new PlotPoint(1,7))&&!PlotLayout.Contains(concave,new PlotPoint(5,5)),"Concave point inclusion");
Check(PlotLayout.Plan(concave,2,100).Count==7,"Do not fill the convex hull of an L shape");
var uShape=Points(0,0,8,0,8,8,6,8,6,2,2,2,2,8,0,8);
Check(PlotLayout.Plan(uShape,2,100).Count==10,"U-shape must preserve its central courtyard");
foreach(var polygon in new[]{rectangle,concave,uShape})
{
    foreach(var point in new[]{new PlotPoint(0,0),new PlotPoint(1,1),new PlotPoint(3,5),new PlotPoint(8,8),new PlotPoint(-1,3)})
        Check(PlotLayout.Contains(polygon,point)==PlotLayout.Contains(polygon.Reverse().ToArray(),point),"Containment must work for either winding");
    var sites=PlotLayout.Plan(polygon,2,100);
    Check(sites.Count==PlotLayout.Plan(polygon.Reverse().ToArray(),2,100).Count,"Reversed grid preserves rectangular arm coverage");
    for(int i=0;i<sites.Count;i++)
    {
        Check(PlotLayout.Contains(polygon,sites[i]),"A crop leaked out of the polygon");
        for(int edge=0;edge<polygon.Length;edge++)Check(PlotLayout.EdgeDistanceSquared(polygon[edge],polygon[(edge+1)%polygon.Length],sites[i])+1e-8>=1,"Crop center violates border clearance");
        for(int j=0;j<i;j++)Check(PlotLayout.DistanceSquared(sites[i],sites[j])+1e-8>=4,"Crop spacing overlaps");
    }
    foreach(double angle in new[]{0.1,0.7,1.3,2.9})
    {
        double c=Math.Cos(angle),sn=Math.Sin(angle);
        PlotPoint Rotate(PlotPoint p)=>new PlotPoint(9000+p.X*c-p.Z*sn,-7000+p.X*sn+p.Z*c);
        var rotated=PlotLayout.Plan(polygon.Select(Rotate).ToArray(),2,100);
        Check(rotated.Count==sites.Count,"World offsets and rotations change crop count");
        foreach(var point in sites){var expected=Rotate(point);Check(rotated.Any(p=>PlotLayout.DistanceSquared(p,expected)<1e-12),"Row grid must rotate with the first edge");}
    }
}
Check(PlotLayout.Contains(rectangle,new PlotPoint(6,2)),"Boundary inclusion");
Check(!PlotLayout.Contains(rectangle,new PlotPoint(double.NaN,0)),"Nonfinite query");
Reject(Points(0,0,1,0));
Reject(Points(0,0,2,2,0,2,2,0)); // bow tie
Reject(Points(0,0,8,0,0,6,8,6,0,2)); // crossing with nonzero signed area
Reject(Points(0,0,4,0,2,0,2,4,0,4)); // adjacent edge backtracking
Reject(Points(0,0,4,0,4,4,0,4,4,0)); // repeated nonadjacent vertex
Reject(Points(0,0,4,0,4,4,2,0,0,4)); // boundary touches its own edge
Reject(Points(0,0,4,0,8,0)); // degenerate
Reject(Points(0,0,0.1,0,4,4,0,4));
Reject(Points(0,0,6,0,6,4,double.NaN,4));
Reject(Points(0,0,6,0,6,4,double.PositiveInfinity,4));
Reject(rectangle,2,23.9);
Reject(rectangle,double.NaN);
Reject(rectangle,0.49);
Reject(Points(0,0,30,0,30,30,0,30),0.5,1000); // sites over cap, not a truncated layout
Reject(Points(0,0,20000,0,20000,0.3,0,0.3),0.5,7000); // bound work before scanning empty skinny plots
Reject(Enumerable.Range(0,33).Select(i=>new PlotPoint(10*Math.Cos(i*2*Math.PI/33),10*Math.Sin(i*2*Math.PI/33))).ToArray());
var collinearEdge=Points(0,0,3,0,6,0,6,4,0,4);
Check(PlotLayout.Plan(collinearEdge,2,100).Count==6,"Straight redundant corner is allowed");
Check(PlotLayout.Plan(Points(0,0,0.6,0,0.6,2,0,2),2,100).Count==0,"Narrow plot should have no fitting crop centers");
var random=new Random(173);
for(int sample=0;sample<100;sample++)
{
    int count=random.Next(5,20);
    var boundary=Enumerable.Range(0,count).Select(i=>{double radius=3+random.NextDouble()*8,angle=i*2*Math.PI/count;return new PlotPoint(radius*Math.Cos(angle),radius*Math.Sin(angle));}).ToArray();
    Check(PlotLayout.Validate(boundary,1000,out _),"Radial simple polygon incorrectly rejected");
    double spacing=1.5+random.NextDouble();var sites=PlotLayout.Plan(boundary,spacing,1000);
    Check(sites.Count<=PlotLayout.MaxSites,"Unbounded layout");
    foreach(var site in sites)
    {
        Check(PlotLayout.Contains(boundary,site)&&PlotLayout.Contains(boundary.Reverse().ToArray(),site),"Random concave layout crossed the outline");
        for(int edge=0;edge<boundary.Length;edge++)Check(PlotLayout.EdgeDistanceSquared(boundary[edge],boundary[(edge+1)%boundary.Length],site)+1e-8>=spacing*spacing/4,"Random plot border clearance");
    }
    for(int i=0;i<sites.Count;i++)for(int j=0;j<i;j++)Check(PlotLayout.DistanceSquared(sites[i],sites[j])+1e-8>=spacing*spacing,"Random layout overlap");
}
Console.WriteLine($"Passed {checks} farming row, concave plot and harvest checks.");
