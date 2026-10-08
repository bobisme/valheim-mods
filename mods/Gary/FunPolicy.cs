using System;
namespace Gary
{
    internal static class FunPolicy
    {
        internal static bool Recent(double seconds,double limit)=>!double.IsNaN(seconds)&&!double.IsInfinity(seconds)&&seconds>=0&&seconds<=limit;
        internal static bool FetchReach(double distance)=>Recent(distance,16);
        internal static int Emote(string name)
        {switch(name){case "wave":return 1;case "cheer":return 2;case "dance":return 3;case "sit":case "relax":return 4;default:return 0;}}
        internal static bool HomeReady(bool following,bool resting,double stationary,double distance)=>following&&resting&&Recent(stationary,double.MaxValue)&&stationary>=8&&Recent(distance,12);
    }
}
