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
        private static bool Finite(double value)=>!double.IsNaN(value) && !double.IsInfinity(value);
        internal static bool Delta(double underlying,double baseline,double target,out float delta)
        {delta=(float)(target-underlying);return Finite(underlying) && Finite(baseline) && Finite(target) && Math.Abs(target-baseline)<=8 && Math.Abs(target-underlying)<=8;}
    }
}
