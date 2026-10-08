using System;
using System.Collections.Generic;

namespace Farmhand
{
    internal readonly struct PlotPoint
    {
        internal readonly double X, Z;
        internal PlotPoint(double x,double z){X=x;Z=z;}
    }
    // An ordered simple polygon, not a convex hull. Row direction comes from its first edge.
    internal static class PlotLayout
    {
        internal const int MaxMarkers=32, MaxSites=512, MaxCandidates=32768;
        private const double Epsilon=1e-8;
        private static bool Finite(double n)=>!double.IsNaN(n)&&!double.IsInfinity(n);
        private static double Cross(PlotPoint a,PlotPoint b,PlotPoint c)=>(b.X-a.X)*(c.Z-a.Z)-(b.Z-a.Z)*(c.X-a.X);
        internal static double Area(IReadOnlyList<PlotPoint> polygon)
        {
            if(polygon.Count<3)return 0;
            double sum=0;for(int i=1;i<polygon.Count-1;i++)sum+=Cross(polygon[0],polygon[i],polygon[i+1]);
            return Math.Abs(sum)*0.5;
        }
        private static bool OnEdge(PlotPoint a,PlotPoint b,PlotPoint p)=>Math.Abs(Cross(a,b,p))<=Epsilon&&
            p.X>=Math.Min(a.X,b.X)-Epsilon&&p.X<=Math.Max(a.X,b.X)+Epsilon&&p.Z>=Math.Min(a.Z,b.Z)-Epsilon&&p.Z<=Math.Max(a.Z,b.Z)+Epsilon;
        private static bool Intersects(PlotPoint a,PlotPoint b,PlotPoint c,PlotPoint d)
        {
            double abC=Cross(a,b,c),abD=Cross(a,b,d),cdA=Cross(c,d,a),cdB=Cross(c,d,b);
            return ((abC>Epsilon&&abD< -Epsilon||abC< -Epsilon&&abD>Epsilon)&&
                    (cdA>Epsilon&&cdB< -Epsilon||cdA< -Epsilon&&cdB>Epsilon))||
                OnEdge(a,b,c)||OnEdge(a,b,d)||OnEdge(c,d,a)||OnEdge(c,d,b);
        }
        internal static bool Validate(IReadOnlyList<PlotPoint> p,double maximumArea,out string error)
        {
            error=null;
            if(p==null||p.Count<3){error="Place at least three corners, in boundary order.";return false;}
            if(p.Count>MaxMarkers){error="At most 32 corners.";return false;}
            foreach(PlotPoint v in p)if(!Finite(v.X)||!Finite(v.Z)||Math.Abs(v.X)>100000||Math.Abs(v.Z)>100000)
            {error="Invalid plot coordinates.";return false;}
            for(int i=0;i<p.Count;i++)
            {
                PlotPoint a=p[i],b=p[(i+1)%p.Count],c=p[(i+2)%p.Count];
                if(DistanceSquared(a,b)<0.04){error="Corners are too close together.";return false;}
                if(Math.Abs(Cross(a,b,c))<Epsilon&&(b.X-a.X)*(c.X-b.X)+(b.Z-a.Z)*(c.Z-b.Z)<0)
                {error="An edge doubles back. Remove the overlapping corner.";return false;}
                for(int j=i+1;j<p.Count;j++)
                {
                    if(j==i+1||i==0&&j==p.Count-1)continue;
                    if(Intersects(a,b,p[j],p[(j+1)%p.Count])){error="The outline crosses itself. Remove or move a corner.";return false;}
                }
            }
            double area=Area(p);
            if(area<0.5){error="The plot is too narrow or has no area.";return false;}
            if(!Finite(maximumArea)||maximumArea<0.5||area>maximumArea){error="The plot exceeds the maximum area.";return false;}
            return true;
        }
        internal static bool Contains(IReadOnlyList<PlotPoint> p,PlotPoint v)
        {
            if(p==null||p.Count<3||!Finite(v.X)||!Finite(v.Z))return false;
            bool inside=false;
            for(int i=0,j=p.Count-1;i<p.Count;j=i++)
            {
                PlotPoint a=p[j],b=p[i];if(OnEdge(a,b,v))return true;
                if((a.Z>v.Z)!=(b.Z>v.Z)&&v.X<(b.X-a.X)*(v.Z-a.Z)/(b.Z-a.Z)+a.X)inside=!inside;
            }
            return inside;
        }
        internal static double DistanceSquared(PlotPoint a,PlotPoint b)=>(a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z);
        internal static double EdgeDistanceSquared(PlotPoint a,PlotPoint b,PlotPoint p)
        {
            double dx=b.X-a.X,dz=b.Z-a.Z,length=dx*dx+dz*dz;
            double t=length>0?Math.Max(0,Math.Min(1,((p.X-a.X)*dx+(p.Z-a.Z)*dz)/length)):0;
            return DistanceSquared(p,new PlotPoint(a.X+t*dx,a.Z+t*dz));
        }
        internal static List<PlotPoint> Plan(IReadOnlyList<PlotPoint> boundary,double spacing,double maximumArea)
        {
            if(!Validate(boundary,maximumArea,out string error))throw new ArgumentException(error);
            if(!Finite(spacing)||spacing<0.5||spacing>100)throw new ArgumentException("Invalid crop spacing.");
            PlotPoint origin=boundary[0];double length=Math.Sqrt(DistanceSquared(origin,boundary[1]));
            double ux=(boundary[1].X-origin.X)/length,uz=(boundary[1].Z-origin.Z)/length;
            var local=new List<PlotPoint>();double minX=double.MaxValue,maxX=double.MinValue,minZ=double.MaxValue,maxZ=double.MinValue;
            foreach(PlotPoint p in boundary)
            {
                double dx=p.X-origin.X,dz=p.Z-origin.Z;var q=new PlotPoint(dx*ux+dz*uz,-dx*uz+dz*ux);local.Add(q);
                minX=Math.Min(minX,q.X);maxX=Math.Max(maxX,q.X);minZ=Math.Min(minZ,q.Z);maxZ=Math.Max(maxZ,q.Z);
            }
            // Fix the grid phase relative to the first corner. A rectangular plot gets half-cell borders.
            double firstX=(Math.Ceiling(minX/spacing-0.5)+0.5)*spacing;
            double firstZ=(Math.Ceiling(minZ/spacing-0.5)+0.5)*spacing;
            int columns=(int)Math.Max(0,Math.Floor((maxX-firstX)/spacing+Epsilon)+1);
            int rows=(int)Math.Max(0,Math.Floor((maxZ-firstZ)/spacing+Epsilon)+1);
            if((long)rows*columns>MaxCandidates)throw new ArgumentException("The plot is too spread out for this spacing. Use a smaller plot.");
            var result=new List<PlotPoint>();double marginSquared=spacing*spacing*0.25;
            for(int row=0;row<rows;row++)for(int column=0;column<columns;column++)
            {
                var p=new PlotPoint(firstX+column*spacing,firstZ+row*spacing);
                if(!Contains(local,p))continue;
                bool clear=true;for(int i=0;i<local.Count;i++)
                    if(EdgeDistanceSquared(local[i],local[(i+1)%local.Count],p)+Epsilon<marginSquared){clear=false;break;}
                if(!clear)continue;
                if(result.Count>=MaxSites)throw new ArgumentException("More than 512 crop spots. Use a smaller plot or wider spacing.");
                result.Add(new PlotPoint(origin.X+p.X*ux-p.Z*uz,origin.Z+p.X*uz+p.Z*ux));
            }
            return result;
        }
    }
}
