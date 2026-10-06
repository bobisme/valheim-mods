using System;
using System.Collections.Generic;
using System.Linq;

namespace PolygonLeveler
{
    internal readonly struct GroundSample
    {
        internal readonly Point Position;
        internal readonly double Height;
        internal GroundSample(double x, double z, double height) { Position = new Point(x, z); Height = height; }
    }

    internal readonly struct GroundPlane
    {
        internal readonly double X, Z, Height, SlopeX, SlopeZ;
        internal GroundPlane(double x, double z, double height, double slopeX, double slopeZ)
        { X = x; Z = z; Height = height; SlopeX = slopeX; SlopeZ = slopeZ; }
        internal double At(double x, double z) => Height + SlopeX * (x - X) + SlopeZ * (z - Z);

        internal static GroundPlane Fit(IEnumerable<GroundSample> input)
        {
            var raw = input.Take(Geometry.MaxVertices + 1).ToList();
            if (raw.Count > Geometry.MaxVertices || raw.Any(p => !Geometry.Finite(p.Position.X) ||
                !Geometry.Finite(p.Position.Z) || !Geometry.Finite(p.Height)))
                throw new ArgumentException("Invalid or too many ground samples.");
            // Tile seams have two stored copies of the same vertex; give that location one vote.
            var samples = raw.GroupBy(p => p.Position).Select(g => new GroundSample(g.Key.X, g.Key.Z, g.Average(p => p.Height))).ToList();
            if (samples.Count < 3) throw new ArgumentException("More terrain vertices are needed to fit a plane. Mark a wider polygon.");
            double x = samples.Average(p => p.Position.X), z = samples.Average(p => p.Position.Z), height = samples.Average(p => p.Height);
            double xx = 0, zz = 0, xz = 0, xy = 0, zy = 0;
            foreach (GroundSample p in samples)
            {
                double dx = p.Position.X - x, dz = p.Position.Z - z, dy = p.Height - height;
                xx += dx * dx; zz += dz * dz; xz += dx * dz; xy += dx * dy; zy += dz * dy;
            }
            // Centered least squares minimizes vertical earth movement without world-origin cancellation.
            double determinant = xx * zz - xz * xz, trace = xx + zz;
            if (!(determinant > trace * trace * 1e-10) || !Geometry.Finite(determinant))
                throw new ArgumentException("Terrain samples are nearly collinear. Mark a wider polygon.");
            double slopeX = (xy * zz - zy * xz) / determinant, slopeZ = (zy * xx - xy * xz) / determinant;
            if (!Geometry.Finite(slopeX) || !Geometry.Finite(slopeZ) || !Geometry.Finite(height))
                throw new ArgumentException("Could not fit a finite ground plane.");
            return new GroundPlane(x, z, height, slopeX, slopeZ);
        }
    }
}
