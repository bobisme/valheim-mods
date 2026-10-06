using System;
using System.Collections.Generic;

namespace BuildShapes
{
    internal readonly struct V3
    {
        internal readonly double X, Y, Z;
        internal V3(double x, double y, double z) { X = x; Y = y; Z = z; }
        internal double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        internal bool Finite => Valid(X) && Valid(Y) && Valid(Z);
        private static bool Valid(double v) => !double.IsNaN(v) && !double.IsInfinity(v) && Math.Abs(v) <= 1000000;
        public static V3 operator +(V3 a, V3 b) => new V3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static V3 operator -(V3 a, V3 b) => new V3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static V3 operator *(V3 a, double k) => new V3(a.X * k, a.Y * k, a.Z * k);
        internal static double Dot(V3 a, V3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    }
    internal readonly struct Segment
    {
        internal readonly V3 Start, End;
        internal Segment(V3 start, V3 end) { Start = start; End = end; }
    }
    internal static class Curve
    {
        internal const int MaximumPieces = 256;
        private const int Samples = 1024;
        // The middle marker is ON the curve, rather than an invisible Bezier control handle.
        internal static V3 At(V3 start, V3 middle, V3 end, double t)
        {
            V3 control = middle * 2 - (start + end) * 0.5;
            return start * ((1 - t) * (1 - t)) + control * (2 * t * (1 - t)) + end * (t * t);
        }
        internal static List<Segment> Plan(V3 start, V3 middle, V3 end, double length)
        {
            if (!start.Finite || !middle.Finite || !end.Finite || double.IsNaN(length) || double.IsInfinity(length) || length < 0.25 || length > 8.1)
                throw new ArgumentException("Invalid curve or beam length.");
            if ((start - end).Length < 0.1 || (start - middle).Length > 128 || (middle - end).Length > 128)
                throw new ArgumentException("Place distinct ends within 128 metres of the bend.");
            var points = new V3[Samples + 1]; var distance = new double[Samples + 1];
            points[0] = start;
            for (int i = 1; i <= Samples; i++)
            {
                points[i] = At(start, middle, end, (double)i / Samples);
                distance[i] = distance[i - 1] + (points[i] - points[i - 1]).Length;
            }
            double total = distance[Samples];
            if (total < length - 0.0001) throw new ArgumentException("Choose a shorter beam or mark a longer curve.");
            if (total > 128 || total / length > MaximumPieces) throw new ArgumentException("Curve exceeds 128 metres or 256 pieces.");
            int count = Math.Max(1, (int)Math.Ceiling(total / length - 1e-9));
            V3 Sample(double d)
            {
                int hi = Array.BinarySearch(distance, d);
                if (hi >= 0) return points[hi];
                hi = ~hi;
                if (hi >= distance.Length) return end;
                int lo = hi - 1;
                double part = (d - distance[lo]) / (distance[hi] - distance[lo]);
                return points[lo] + (points[hi] - points[lo]) * part;
            }
            var result = new List<Segment>(count);
            V3 previousDirection = default;
            for (int i = 0; i < count; i++)
            {
                V3 a = Sample(total * i / count), b = Sample(total * (i + 1) / count);
                double chord = (b - a).Length;
                if (chord < 0.01 || (count == 1 && Math.Abs(chord - length) > 0.001))
                    throw new ArgumentException("Choose a shorter beam or a gentler bend.");
                V3 direction = (b - a) * (1 / chord);
                if (i > 0 && V3.Dot(previousDirection, direction) < 0.25)
                    throw new ArgumentException("Bend is too tight for this beam. Choose a shorter beam.");
                V3 centre = (a + b) * 0.5;
                // Real pieces keep their native length; small overlaps avoid gaps. Preserve the outer ends.
                if (i == 0) centre = start + direction * (length * 0.5);
                if (i == count - 1) centre = end - direction * (length * 0.5);
                result.Add(new Segment(centre - direction * (length * 0.5), centre + direction * (length * 0.5)));
                previousDirection = direction;
            }
            return result;
        }
    }
}
