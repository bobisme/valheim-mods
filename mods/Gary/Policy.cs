using System;

namespace Gary
{
    internal static class Policy
    {
        internal const double RetreatAt=0.20, ReturnAt=0.90, HealthFloor=0.05;
        internal const double RetreatDistance=40, ThreatSeconds=30, ThreatRange=35;
        internal static double ProtectedHealth(double requested,double maximum) =>
            double.IsNaN(requested)||double.IsInfinity(requested)?Math.Max(1,maximum*HealthFloor):Math.Max(Math.Max(1,maximum*HealthFloor),Math.Min(maximum,requested));
        internal static bool Retreat(bool current,double fraction) => current?fraction<ReturnAt:fraction<=RetreatAt;
        internal static bool FreshThreat(double age,double playerDistance,double companionDistance,bool hostile,bool player,bool dead) =>
            hostile&&!player&&!dead&&age>=0&&age<=ThreatSeconds&&playerDistance<=ThreatRange&&companionDistance<=ThreatRange+15;
        internal static bool CanGift(bool retreat,bool fighting,bool waiting,bool interior,double distance,double secondsUntilGift) =>
            !retreat&&!fighting&&!waiting&&!interior&&distance<=8&&secondsUntilGift<=0;
        internal static bool GuideWait(double playerDistance) => playerDistance>14;
    }
}
