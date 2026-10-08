using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Spyglass
{
    // Pure rules shared with the standalone tests: no Unity or game types.
    internal static class Policy
    {
        internal const double StepFactor=1.25;

        // Optical zoom: magnification divides the tangent of the half-angle, not the angle itself.
        internal static double Fov(double baseFov,double magnification)
        {
            if(!(baseFov>0&&baseFov<180))baseFov=65;
            if(!(magnification>=1)||double.IsInfinity(magnification))magnification=1;
            return 2*Math.Atan(Math.Tan(baseFov*Math.PI/360)/magnification)*180/Math.PI;
        }
        internal static double Clamp(double magnification,double min,double max)
        {
            if(!(min>=1))min=1;if(!(max>=min))max=min;
            return double.IsNaN(magnification)?min:Math.Min(max,Math.Max(min,magnification));
        }
        // One wheel notch is one multiplicative step, however large the raw scroll value is.
        internal static double Step(double magnification,double scroll,double min,double max)
        {
            if(double.IsNaN(scroll)||scroll==0)return Clamp(magnification,min,max);
            return Clamp(scroll>0?magnification*StepFactor:magnification/StepFactor,min,max);
        }
        // Turning speed scales with the view angle so the aim moves across the screen at the usual rate.
        internal static double LookScale(double baseFov,double zoomedFov)
        {
            if(!(baseFov>0&&baseFov<180)||!(zoomedFov>0&&zoomedFov<180))return 1;
            return Math.Min(1,Math.Max(0.02,Math.Tan(zoomedFov*Math.PI/360)/Math.Tan(baseFov*Math.PI/360)));
        }

        // Distance along a normalized ray to the first point below land or sea, from the generated terrain shape.
        // Marches in steps, then bisects the crossing. NaN: the ray starts under the surface or never meets it.
        internal static double Gaze(double ox,double oy,double oz,double dx,double dy,double dz,Func<double,double,double> height,
            double water,double step,double maxRange)
        {
            double length=Math.Sqrt(dx*dx+dy*dy+dz*dz);
            if(!(length>1e-9)||!(step>0)||!(maxRange>0)||height==null)return double.NaN;
            dx/=length;dy/=length;dz/=length;
            double Above(double t)
            {
                double ground=height(ox+dx*t,oz+dz*t);
                if(double.IsNaN(ground))ground=double.NegativeInfinity;
                return oy+dy*t-Math.Max(ground,water);
            }
            if(!(Above(0)>0))return double.NaN;
            double previous=0;
            for(double t=Math.Min(step,maxRange);;t=Math.Min(t+step,maxRange))
            {
                if(Above(t)<=0)
                {
                    double low=previous,high=t;
                    for(int i=0;i<24;i++){double mid=(low+high)/2;if(Above(mid)>0)low=mid;else high=mid;}
                    return high;
                }
                if(t>=maxRange)return double.NaN;
                previous=t;
            }
        }

        // ScriptEngine loads each copy of a mod as "<name>-<ticks>"; old copies stay loaded after a hot reload.
        internal static string ModName(string assembly)=>Regex.Replace(assembly??"",@"-\d+$","");

        // A shorter shortcut on the same key (Z, while ours is Shift+Z) would also fire. It loses only while all our keys are held.
        internal static bool Shadows(int ourMain,IEnumerable<int> ourModifiers,int theirMain,IEnumerable<int> theirModifiers,Func<int,bool> held)
        {
            if(ourMain==0||ourMain!=theirMain||held==null)return false;
            var ours=new HashSet<int>(ourModifiers??Enumerable.Empty<int>());
            var theirs=new HashSet<int>(theirModifiers??Enumerable.Empty<int>());
            return theirs.IsProperSubsetOf(ours)&&ours.All(held);
        }
    }
}
