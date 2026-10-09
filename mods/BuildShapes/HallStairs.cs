using System;
using System.Collections.Generic;
using System.Linq;

namespace BuildShapes
{
    // The flight is kept wholly inside the cell union, with a 2 m landing at each end.
    // All levels align their top landing; an opening spans the full flight for headroom.
    internal static class HallStairs
    {
        private static List<(HallLayout.Cell start,bool across,int score)> Routes(HallLayout.Design plan,int rise)
        {
            var set=new HashSet<HallLayout.Cell>(plan.Cells);
            var routes=new List<(HallLayout.Cell start,bool across,int score)>();
            foreach(var start in plan.Cells)foreach(bool across in new[]{false,true})
            {
                HallLayout.Cell At(int along,int side=0)=>new HallLayout.Cell(start.X+(across?along:side),start.Z+(across?side:along));
                if(Enumerable.Range(0,rise+2).Any(i=>!set.Contains(At(i))))continue;
                int neighbours=Enumerable.Range(1,rise).Count(i=>set.Contains(At(i,1)))+Enumerable.Range(1,rise).Count(i=>set.Contains(At(i,-1)));
                if(neighbours<rise)continue;
                routes.Add((start,across,neighbours*10-Math.Abs(start.X)-Math.Abs(start.Z)));
            }
            return routes;
        }
        private static bool Clear((HallLayout.Cell start,bool across,int score) route,int rise,IReadOnlyList<HallDoors.Entrance> doors)
        {
            foreach(var door in doors)
            {
                V3 entry=door.At+HallLayout.Turn(new V3(0,0,1.5),door.Yaw);
                for(int i=1;i<=rise;i++)
                {
                    double x=(route.start.X+(route.across?i:0))*2+1,z=(route.start.Z+(route.across?0:i))*2+1;
                    if(Math.Abs(entry.X-x)<1.4 && Math.Abs(entry.Z-z)<1.4)return false;
                }
            }
            return true;
        }
        internal static Func<IReadOnlyList<HallDoors.Entrance>,bool> Acceptance(HallLayout.Design plan,int rise)
        {
            var routes=Routes(plan,rise);
            return doors=>routes.Any(route=>Clear(route,rise,doors));
        }
        internal static void Solve(HallLayout.Design plan,int rise)
        {
            var routes=Routes(plan,rise).Where(route=>Clear(route,rise,plan.Entrances)).ToArray();
            if(routes.Length==0)throw new ArgumentException($"No interior stair route fits. Leave a 2 × {2*(rise+2)} m run including landings, with space beside it; widen or lengthen a wing.");
            var route=routes.OrderByDescending(r=>r.score).ThenBy(r=>r.start.Z).ThenBy(r=>r.start.X).ThenBy(r=>r.across).First();
            plan.StairStart=route.start;plan.StairAcross=route.across;plan.StairRise=rise;
            for(int i=0;i<rise+2;i++)
            {
                var c=new HallLayout.Cell(route.start.X+(route.across?i:0),route.start.Z+(route.across?0:i));
                plan.StairZone.Add(c);if(i>0 && i<=rise)plan.StairHoles.Add(c);
            }
        }
        internal static IEnumerable<HallLayout.Part> Flight(HallLayout.Design plan,int rise)
        {
            for(int i=0;i<rise;i++)
            {
                double along=2*(plan.StairRise-rise+i+1),x=plan.StairStart.X*2,z=plan.StairStart.Z*2;
                yield return new HallLayout.Part{Prefab="wood_stair",Role="interior stair",At=new V3(x+(plan.StairAcross?along:1),i,z+(plan.StairAcross?1:along)),Kind=HallLayout.Anchor.RoofLow,Yaw=plan.StairAcross?270:180};
            }
        }
        internal static bool Blocks(HallLayout.Design plan,V3 point)
        {
            // Beam/pole radius plus the player's width; keep structural fixes out of the walking route.
            return plan.StairZone.Any(c=>point.X>c.X*2-0.3 && point.X<c.X*2+2.3 && point.Z>c.Z*2-0.3 && point.Z<c.Z*2+2.3);
        }
    }
}
