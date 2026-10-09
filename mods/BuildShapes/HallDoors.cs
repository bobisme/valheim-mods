using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildShapes
{
    // Fit the requested entrances together, rather than letting an early snap consume a later opening.
    internal static class HallDoors
    {
        internal const int Maximum=8;
        internal sealed class Entrance
        {
            internal V3 At,Landing;
            internal double Yaw;
            internal int Edge;
        }
        internal static List<Entrance> Solve(IReadOnlyList<V3> corners,IReadOnlyList<V3> requests,int fallback,bool porch,Func<IReadOnlyList<Entrance>,bool> accept=null)
        {
            int edge=((fallback%corners.Count)+corners.Count)%corners.Count;
            Entrance Make(int e,double along)
            {
                V3 a=corners[e],b=corners[(e+1)%corners.Count],direction=(b-a)*(1/(b-a).Length);
                // Winding determines which side of the boundary faces out.
                double area=0;for(int i=0;i<corners.Count;i++){V3 x=corners[i],y=corners[(i+1)%corners.Count];area+=x.X*y.Z-y.X*x.Z;}
                V3 inward=area>0?new V3(-direction.Z,0,direction.X):new V3(direction.Z,0,-direction.X);
                double yaw=Math.Atan2(inward.X,inward.Z)*180/Math.PI;if(yaw<0)yaw+=360;
                V3 at=a+direction*along;return new Entrance{At=at,Landing=at,Yaw=yaw,Edge=e};
            }
            if(requests==null || requests.Count==0)return new List<Entrance>{Make(edge,(corners[(edge+1)%corners.Count]-corners[edge]).Length/2)};
            if(requests.Count>Maximum || requests.Any(p=>!p.Finite || Math.Abs(p.X)>HallLayout.MaximumExtent || Math.Abs(p.Z)>HallLayout.MaximumExtent))throw new ArgumentException("Mark at most eight entrances near exterior walls.");
            var choices=new List<Entrance[]>();
            for(int i=0;i<requests.Count;i++)
            {
                var options=new List<Entrance>();
                for(int e=0;e<corners.Count;e++)
                {
                    double length=(corners[(e+1)%corners.Count]-corners[e]).Length,clearance=i==0 && porch?2:1;
                    for(double along=clearance;along<=length-clearance;along++)
                    {
                        var door=Make(e,along);
                        if((door.At-requests[i]).Length<=4)options.Add(door);
                    }
                }
                var nearest=options.OrderBy(d=>(d.At-requests[i]).Length).ThenBy(d=>d.Edge).ThenBy(d=>d.At.X).ThenBy(d=>d.At.Z).Take(12).ToArray();
                if(nearest.Length==0)throw new ArgumentException($"Entrance {i+1} needs a clear exterior wall within 4 m"+(i==0 && porch?" and room for its 4 m porch.":"."));
                choices.Add(nearest);
            }
            List<Entrance> best=null;double score=double.MaxValue;int budget=20000;
            var picked=new List<Entrance>();
            void Search(int i,double cost)
            {
                if(--budget<0 || cost>=score)return;
                if(i==requests.Count){best=picked.ToList();score=cost;return;}
                foreach(var door in choices[i])
                {
                    if(picked.Any(d=>!Compatible(d,door)))continue;
                    picked.Add(door);if(accept==null || accept(picked))Search(i+1,cost+(door.At-requests[i]).Length);picked.RemoveAt(picked.Count-1);
                }
            }
            Search(0,0);
            if(best==null)throw new ArgumentException("The entrance markers cannot fit separate openings with clear stairs and decks. Spread them out or remove one.");
            return best;
        }
        private static bool Compatible(Entrance a,Entrance b)
        {
            V3 difference=a.At-b.At,local=HallLayout.Turn(difference,-a.Yaw);
            if(Math.Abs(a.Yaw-b.Yaw)<0.01 && Math.Abs(local.Z)<0.01)return Math.Abs(local.X)>=3-0.001;
            return difference.Length>=1.8;
        }
        internal static bool Blocks(HallLayout.Design plan,V3 point)=>plan.Entrances.Concat(plan.InnerEntrances).Any(d=>
        {V3 p=HallLayout.Turn(point-d.At,-d.Yaw);return Math.Abs(p.X)<1.3 && Math.Abs(p.Z)<0.6;});
    }
}
