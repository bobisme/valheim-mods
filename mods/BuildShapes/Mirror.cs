using System;

namespace BuildShapes
{
    internal readonly struct Mirror
    {
        private readonly V3 _origin, _normal;
        internal Mirror(V3 first, V3 second)
        {
            V3 direction = new V3(second.X-first.X, 0, second.Z-first.Z);
            if (!first.Finite || !second.Finite || direction.Length < 0.1 || direction.Length > 128)
                throw new ArgumentException("Mark a mirror line at least 0.1 metre long, up to 128 metres across.");
            _origin = first;
            _normal = new V3(-direction.Z, 0, direction.X)*(1/direction.Length);
        }
        internal V3 Vector(V3 vector) => vector-_normal*(2*V3.Dot(vector,_normal));
        internal V3 Point(V3 point) => _origin+Vector(point-_origin);
        // A world reflection has negative determinant. Flipping local X as well restores a proper
        // rotation; only local-X-symmetric geometry is exactly mirrored without a handed prefab.
        internal V3 Forward(V3 forward, bool flipZ = false) => Vector(forward)*(flipZ ? -1 : 1);
        internal V3 Up(V3 up) => Vector(up);
        internal V3 Right(V3 right, bool flipZ = false) => Vector(right)*(flipZ ? 1 : -1);
        // Some pieces have their origin at one end, away from their local symmetry plane.
        // Reflect about the native geometry's local-X centre, then compensate the root position.
        internal V3 Origin(V3 root, V3 right, double centreX) => Point(root+right*(2*centreX));
    }
    internal readonly struct MirrorProfile
    {
        internal readonly bool FlipZ;
        internal readonly double Centre;
        private MirrorProfile(bool flipZ, double centre) { FlipZ=flipZ; Centre=centre; }
        // The native snap layout reveals a gable's triangular profile, a sloped beam, or an
        // offset origin. Choose the reflection that preserves that layout, without changing scale.
        internal static MirrorProfile Choose(System.Collections.Generic.IReadOnlyList<V3> snaps, V3 fallbackCentre)
        {
            if (snaps.Count < 2 || snaps.Count > 64) return new MirrorProfile(false, fallbackCentre.X);
            double minX=double.PositiveInfinity,maxX=double.NegativeInfinity,minZ=minX,maxZ=maxX;
            foreach(V3 p in snaps)
            {
                if(!p.Finite) return new MirrorProfile(false,fallbackCentre.X);
                minX=Math.Min(minX,p.X);maxX=Math.Max(maxX,p.X);minZ=Math.Min(minZ,p.Z);maxZ=Math.Max(maxZ,p.Z);
            }
            double cx=(minX+maxX)/2,cz=(minZ+maxZ)/2;
            double Score(bool z)
            {
                double score=0;
                foreach(V3 p in snaps)
                {
                    V3 reflected=z?new V3(p.X,p.Y,2*cz-p.Z):new V3(2*cx-p.X,p.Y,p.Z);
                    double nearest=double.PositiveInfinity;
                    foreach(V3 q in snaps) nearest=Math.Min(nearest,(reflected-q).Length);
                    score+=nearest;
                }
                return score/snaps.Count;
            }
            bool flipZ=Score(true)+0.001<Score(false);
            return new MirrorProfile(flipZ,flipZ?cz:cx);
        }
    }
}
