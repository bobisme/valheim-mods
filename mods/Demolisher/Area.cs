using System;
using System.Collections.Generic;
using System.Linq;

namespace Demolisher
{
    // The outline on the ground, in plain numbers (x and z in metres): no Unity, so the tests can run it.
    public static class Area
    {
        public const int MaximumCorners=32;
        public const double CloseDistance=1.5,MinimumEdge=0.5;

        // Whether a point is inside the outline (even-odd rule; the outline is closed back to its first corner).
        public static bool Inside(IReadOnlyList<(double x,double z)> corners,double x,double z)
        {
            bool inside=false;
            for(int i=0,j=corners.Count-1;i<corners.Count;j=i++)
            {
                var a=corners[i];var b=corners[j];
                if(a.z>z!=b.z>z && x<(b.x-a.x)*(z-a.z)/(b.z-a.z)+a.x)inside=!inside;
            }
            return corners.Count>=3 && inside;
        }

        // Square metres inside the outline.
        public static double Size(IReadOnlyList<(double x,double z)> corners)
        {
            double twice=0;
            for(int i=0,j=corners.Count-1;i<corners.Count;j=i++)twice+=corners[j].x*corners[i].z-corners[i].x*corners[j].z;
            return Math.Abs(twice)/2;
        }

        // A click this close to the first corner closes the outline.
        public static bool Closes(IReadOnlyList<(double x,double z)> corners,double x,double z)=>
            corners.Count>=3 && Distance(corners[0],(x,z))<=CloseDistance;

        // Why a corner can't go here (null when it can). closing: the edge from the last corner back to the first.
        public static string Problem(IReadOnlyList<(double x,double z)> corners,double x,double z,bool closing)
        {
            int n=corners.Count;
            if(closing)
            {
                if(n<3)return "An outline needs at least 3 corners.";
                if(Size(corners)<1)return "The outline has no room inside.";
                for(int i=1;i<n-2;i++)if(Cross(corners[n-1],corners[0],corners[i],corners[i+1]))return "The closing edge would cross another edge.";
                return null;
            }
            if(n>=MaximumCorners)return $"An outline has at most {MaximumCorners} corners.";
            if(n==0)return null;
            if(Distance(corners[n-1],(x,z))<MinimumEdge)return "Too close to the last corner.";
            for(int i=0;i<n-2;i++)if(Cross(corners[n-1],(x,z),corners[i],corners[i+1]))return "Edges can't cross.";
            return null;
        }

        // A circle around every corner, to find the loaded pieces worth testing.
        public static (double x,double z,double radius) Circle(IReadOnlyList<(double x,double z)> corners)
        {
            if(corners.Count==0)return (0,0,0);
            double cx=corners.Average(c=>c.x),cz=corners.Average(c=>c.z);
            return (cx,cz,corners.Max(c=>Distance(c,(cx,cz))));
        }

        // The confirmation: a title and the details, with skipped pieces counted by reason (largest first).
        public static (string title,string body) Question(int count,IReadOnlyDictionary<string,int> skipped)
        {
            string title=count==0?"Nothing to demolish":count==1?"Demolish 1 structure?":$"Demolish {count} structures?";
            string body=count==0?"No structure inside the outline can be removed.":"Everything highlighted red inside the outline is removed as the hammer would, top first, and its materials drop on the ground.";
            var reasons=skipped.Where(r=>r.Value>0).OrderByDescending(r=>r.Value).ThenBy(r=>r.Key,StringComparer.Ordinal).Select(r=>$"{r.Value} {r.Key}").ToList();
            if(reasons.Count>0)body+="\n\nLeft standing: "+string.Join(", ",reasons)+".";
            return (title,body);
        }

        private static double Distance((double x,double z) a,(double x,double z) b)=>Math.Sqrt((a.x-b.x)*(a.x-b.x)+(a.z-b.z)*(a.z-b.z));
        private static double Turn((double x,double z) a,(double x,double z) b,(double x,double z) c)=>(b.x-a.x)*(c.z-a.z)-(b.z-a.z)*(c.x-a.x);
        // Whether segments ab and cd cross or touch (shared corners are left out by the callers).
        private static bool Cross((double x,double z) a,(double x,double z) b,(double x,double z) c,(double x,double z) d)
        {
            double d1=Turn(c,d,a),d2=Turn(c,d,b),d3=Turn(a,b,c),d4=Turn(a,b,d);
            if((d1>0&&d2<0||d1<0&&d2>0)&&(d3>0&&d4<0||d3<0&&d4>0))return true;
            return d1==0&&On(c,d,a)||d2==0&&On(c,d,b)||d3==0&&On(a,b,c)||d4==0&&On(a,b,d);
        }
        private static bool On((double x,double z) a,(double x,double z) b,(double x,double z) p)=>
            Math.Min(a.x,b.x)<=p.x&&p.x<=Math.Max(a.x,b.x)&&Math.Min(a.z,b.z)<=p.z&&p.z<=Math.Max(a.z,b.z);
    }
}
