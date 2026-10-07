namespace Gary
{
    internal static class RidePolicy
    {
        internal static bool CanBoard(bool following,bool retreat,bool fighting,bool boat,double distance,double height)=>
            following&&!retreat&&!fighting&&boat&&distance>=0&&distance<=12&&height>=0&&height<=5;
        internal static bool Grace(double seconds)=>seconds>=0&&seconds<=1.5;
        internal static bool CanDisembark(bool alive,bool dryGround,double distance)=>alive&&dryGround&&distance>=0&&distance<=20;
    }
}
