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
        internal V3 Forward(V3 forward) => Vector(forward);
        internal V3 Up(V3 up) => Vector(up);
        internal V3 Right(V3 right) => Vector(right)*-1;
        // Some pieces have their origin at one end, away from their local symmetry plane.
        // Reflect about the native geometry's local-X centre, then compensate the root position.
        internal V3 Origin(V3 root, V3 right, double centreX) => Point(root+right*(2*centreX));
    }
}
