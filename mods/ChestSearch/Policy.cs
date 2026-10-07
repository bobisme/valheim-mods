using System;
namespace ChestSearch
{
    internal static class Policy
    {
        internal const int MaxChests=64,MaxResults=512,PageSize=24;
        internal static bool Matches(string name,string query)
        {
            if(name==null||query==null||query.Length>80)return false;
            foreach(string word in query.Split(new[]{' ','\t'},StringSplitOptions.RemoveEmptyEntries))
                if(name.IndexOf(word,StringComparison.OrdinalIgnoreCase)<0)return false;
            return true;
        }
        internal static bool InRange(double distance,double radius) => !double.IsNaN(distance)&&!double.IsInfinity(distance)&&
            !double.IsNaN(radius)&&!double.IsInfinity(radius)&&distance>=0&&radius>=5&&radius<=40&&distance<=radius;
        internal static int Capacity(int source,int desired,int maximum,int destination,bool sameType,bool empty)
        {
            if(source<=0||desired<=0||maximum<=0||destination<0||!empty&&!sameType)return 0;
            return Math.Max(0,Math.Min(Math.Min(source,desired),empty?maximum:maximum-destination));
        }
    }
    internal sealed class Lease
    {
        private readonly long _sender;
        private readonly double _expires;
        private bool _granted,_finished;
        internal Lease(long sender,double now){_sender=sender;_expires=now+8;}
        internal bool Expired(double now)=>double.IsNaN(now)||double.IsInfinity(now)||now>=_expires;
        internal bool Reply(long sender,bool granted,double now)
        {
            if(_finished||_granted||sender!=_sender||Expired(now))return false;
            if(granted)_granted=true;else _finished=true;
            return true;
        }
        internal bool Ready(double now,bool owner,bool active,bool valid) => !_finished&&_granted&&!Expired(now)&&owner&&active&&valid;
        internal void Finish()=>_finished=true;
    }
}
