using Spyglass;

int checks=0;
void Check(bool ok,string name){checks++;if(!ok)throw new Exception(name);}
bool Near(double a,double b,double tolerance=1e-6)=>Math.Abs(a-b)<=tolerance;

foreach(double baseFov in new[]{40.0,65,90,120})
{
    Check(Near(Policy.Fov(baseFov,1),baseFov),"1× keeps the game's view angle");
    double previous=baseFov;
    foreach(double magnification in new[]{1.5,2,4,8,12})
    {
        double fov=Policy.Fov(baseFov,magnification);
        Check(fov>0&&fov<previous,"More magnification always narrows the view");
        Check(Near(Math.Tan(baseFov*Math.PI/360)/Math.Tan(fov*Math.PI/360),magnification),"Magnification is optical (ratio of half-angle tangents)");
        double scale=Policy.LookScale(baseFov,fov);
        Check(Near(scale,Math.Max(0.02,1/magnification)),"Turning slows in proportion to magnification");
        previous=fov;
    }
}
foreach(double bad in new[]{double.NaN,0,-3,double.PositiveInfinity})
{
    double fov=Policy.Fov(65,bad);
    Check(double.IsFinite(fov)&&Near(fov,65),"Invalid magnification falls back to the unzoomed view");
    Check(double.IsFinite(Policy.Fov(bad,4))&&Policy.Fov(bad,4)>0,"An invalid game FOV never yields an invalid camera");
    Check(Policy.LookScale(65,bad)==1&&Policy.LookScale(bad,30)==1,"Invalid view angles leave turning speed unchanged");
}

double m=4;
foreach(int notch in Enumerable.Range(0,40)){m=Policy.Step(m,0.05,2,8);Check(m>=2&&m<=8,"Wheel zoom stays within limits");}
Check(m==8,"Zooming in reaches the maximum");
foreach(int notch in Enumerable.Range(0,40)){m=Policy.Step(m,-0.05,2,8);Check(m>=2&&m<=8,"Wheel zoom stays within limits");}
Check(m==2,"Zooming out reaches the minimum");
Check(Near(Policy.Step(4,0.05,2,8),5)&&Near(Policy.Step(4,-120,2,8),3.2),"One notch is one step regardless of raw scroll size");
Check(Policy.Step(4,0,2,8)==4&&Policy.Step(4,double.NaN,2,8)==4,"No or invalid scroll keeps magnification");
Check(Policy.Clamp(double.NaN,2,8)==2&&Policy.Clamp(50,2,8)==8&&Policy.Clamp(5,8,2)==8,"Clamping handles NaN and inverted limits");

// Flat land at 50 m, sea at 30 m.
Func<double,double,double> flat=(x,z)=>50;
double d=Policy.Gaze(0,60,0,1,-1,0,flat,30,8,600);
Check(Near(d,Math.Sqrt(200),1e-3),"A downward gaze meets flat ground where geometry says");
Check(double.IsNaN(Policy.Gaze(0,60,0,0,1,0,flat,30,8,600)),"Looking at the sky reveals nothing");
Check(double.IsNaN(Policy.Gaze(0,60,0,1,0,0,flat,30,8,600)),"A level gaze over level ground never lands");
Check(double.IsNaN(Policy.Gaze(0,40,0,1,-1,0,flat,30,8,600)),"A gaze starting under the ground is rejected");
Check(double.IsNaN(Policy.Gaze(0,60,0,1,-0.001,0,flat,30,8,600)),"Gaze points beyond the range are not revealed");
double sea=Policy.Gaze(0,40,0,0,-1,1,(x,z)=>-100,30,8,600);
Check(Near(sea,Math.Sqrt(200),1e-3),"Over deep water the gaze stops at sea level");
// A hill ahead stops the gaze on its near face.
Func<double,double,double> hill=(x,z)=>x>=300&&x<=340?120:0;
double h=Policy.Gaze(0,100,0,1,0,0,hill,30,8,600);
Check(h>=300-1e-3&&h<=304,"The near face of a hill stops the gaze");
foreach(var dir in new[]{(0.0,0.0,0.0),(double.NaN,1.0,0.0)})
    Check(double.IsNaN(Policy.Gaze(0,60,0,dir.Item1,dir.Item2,dir.Item3,flat,30,8,600)),"Degenerate directions are rejected");
Check(double.IsNaN(Policy.Gaze(0,60,0,1,-1,0,null,30,8,600))&&double.IsNaN(Policy.Gaze(0,60,0,1,-1,0,flat,30,0,600)),"Missing terrain or step is rejected");
Check(Near(Policy.Gaze(0,60,0,1,-1,0,(x,z)=>double.NaN,30,8,600),Math.Sqrt(1800),1e-3),"Unknown terrain height falls back to sea level");

const int Z=122,LeftShift=304,LeftControl=306;
Func<int,bool> shift=k=>k==LeftShift;
Func<int,bool> none=k=>false;
Check(Policy.Shadows(Z,new[]{LeftShift},Z,new int[0],shift),"Plain Z (GearSlots quick slot 1) yields while Shift+Z is held");
Check(!Policy.Shadows(Z,new[]{LeftShift},Z,new int[0],none),"Plain Z works normally without Shift");
Check(!Policy.Shadows(Z,new[]{LeftShift},Z,new[]{LeftShift},shift),"An identical shortcut is not hidden");
Check(!Policy.Shadows(Z,new[]{LeftShift},Z,new[]{LeftControl},k=>k==LeftShift||k==LeftControl),"A different modifier combination is not hidden");
Check(!Policy.Shadows(Z,new[]{LeftShift},122+1,new int[0],shift),"Other keys are never hidden");
Check(!Policy.Shadows(0,new int[0],0,new int[0],none),"An unbound spyglass key hides nothing");
Check(!Policy.Shadows(Z,new int[0],Z,new int[0],none),"A plain-Z spyglass key does not hide plain Z");
Check(Policy.ModName("GearSlots-639270216874136510")=="GearSlots"&&Policy.ModName("QualityOfLife-1")=="QualityOfLife","Reloaded copies share their mod's name");
Check(Policy.ModName("GearSlots")=="GearSlots"&&Policy.ModName("Mono.Cecil")=="Mono.Cecil"&&Policy.ModName("Some-Mod")=="Some-Mod"&&Policy.ModName(null)=="","Ordinary names are unchanged");
Console.WriteLine($"Passed {checks} spyglass zoom, gaze and shortcut checks.");
