using System;
using System.Linq;

namespace BuildShapes
{
    internal static class HallExcavation
    {
        // Clearance for 1 m-thick stone retaining walls. The union retains concave courtyards.
        internal static bool Pit(HallLayout.Design plan,double x,double z,double margin=0.75)=>plan.Cells.Any(c=>x>=c.X*2-margin && x<=c.X*2+2+margin && z>=c.Z*2-margin && z<=c.Z*2+2+margin);
        internal static double? Target(HallLayout.Design plan,double x,double z)
        {
            if(Pit(plan,x,z))return -4;
            foreach(var door in plan.Entrances)
            {
                V3 local=HallLayout.Turn(new V3(x,0,z)-door.At,-door.Yaw);
                // A small apron at the stair-free entrance; outside this strip the site stays untouched.
                if(Math.Abs(local.X)<=2 && local.Z>=-4 && local.Z<=-0.75)return -0.15;
            }
            return null;
        }
        // A Norse longhouse's ground, as (height above the floor, mode): mode -1 only digs, +1 only heaps, 0 levels.
        // The floor is dug in; the terrain grid is 1 m, so a ditch 1.5 m wide keeps the soil below the floor at the walls,
        // and an earth bank rises beyond it to just under the eaves. Each door gets a ramp down from the old ground.
        internal static (double y,int mode)? Norse(HallLayout.Design plan,double x,double z,double sink,bool bank,double eaves)
        {
            foreach(var door in plan.Entrances)
            {
                V3 local=HallLayout.Turn(new V3(x,0,z)-door.At,-door.Yaw);
                if(sink>0 && Math.Abs(local.X)<=1.5 && local.Z<=0.5 && local.Z>=-10)return (-0.15+Math.Max(0,-local.Z-1.5)*0.5,-1);
            }
            double outside=Outside(plan,x,z);
            if(outside<1.5)return sink>0?(-0.15,-1):((double y,int mode)?)null;
            if(!bank || outside>6)return null;
            foreach(var door in plan.Entrances)
            {
                V3 local=HallLayout.Turn(new V3(x,0,z)-door.At,-door.Yaw);
                if(Math.Abs(local.X)<=3 && local.Z>=-11)return null;
            }
            return (eaves-0.3-Math.Max(0,outside-2.5)*0.6,1);
        }
        // Distance from the floor plan, 0 inside it.
        internal static double Outside(HallLayout.Design plan,double x,double z)
        {
            double best=double.MaxValue;
            foreach(var c in plan.Cells)
            {
                double dx=Math.Max(0,Math.Max(c.X*2-x,x-c.X*2-2)),dz=Math.Max(0,Math.Max(c.Z*2-z,z-c.Z*2-2));
                best=Math.Min(best,dx*dx+dz*dz);
            }
            return Math.Sqrt(best);
        }
        internal static double Settle(double ground,double target,int mode)=>mode<0?Math.Min(ground,target):mode>0?Math.Max(ground,target):target;
        private static bool Finite(double value)=>!double.IsNaN(value) && !double.IsInfinity(value);
        internal static bool Delta(double underlying,double baseline,double target,out float delta)
        {delta=(float)(target-underlying);return Finite(underlying) && Finite(baseline) && Finite(target) && Math.Abs(target-baseline)<=8 && Math.Abs(target-underlying)<=8;}
    }
}
