using System;

namespace BuildShapes
{
    internal static class HallTerrainGeometry
    {
        // Inputs are already in the collider's local frame. Surface contact, not solid soil below it.
        internal static bool Contact(V3 a,V3 b,V3 c,V3 half)
        {
            bool Separated(V3 axis)
            {
                if(axis.Length<1e-5)return false;
                double pa=V3.Dot(axis,a),pb=V3.Dot(axis,b),pc=V3.Dot(axis,c);
                double radius=Math.Abs(axis.X)*half.X+Math.Abs(axis.Y)*half.Y+Math.Abs(axis.Z)*half.Z;
                return Math.Min(pa,Math.Min(pb,pc))>radius+0.001 || Math.Max(pa,Math.Max(pb,pc))<-radius-0.001;
            }
            V3[] axes={new V3(1,0,0),new V3(0,1,0),new V3(0,0,1)};
            foreach(var axis in axes)if(Separated(axis))return false;
            if(Separated(V3.Cross(b-a,c-a)))return false;
            foreach(var edge in new[]{b-a,c-b,a-c})foreach(var axis in axes)if(Separated(V3.Cross(edge,axis)))return false;
            return true;
        }
    }
}
