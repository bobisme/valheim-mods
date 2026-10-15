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
        internal static V3 Cross(V3 a, V3 b) => new V3(a.Y*b.Z-a.Z*b.Y, a.Z*b.X-a.X*b.Z, a.X*b.Y-a.Y*b.X);
    }
    internal readonly struct Segment
    {
        internal readonly V3 Start, End;
        internal Segment(V3 start, V3 end) { Start = start; End = end; }
    }
    internal static class Curve
    {
        internal const int MaximumPieces = 256;
        private const int Samples = 4096;
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
            return Plan(t => At(start, middle, end, t), length);
        }
        // Native-length beams along any path from path(0) to path(1) (Arch's and Circle's shapes as well as Curve's). The outer pieces
        // meet both ends and the overlaps between are spread evenly. loop: the path comes back to its start (path(1) == path(0)), so
        // the pieces go evenly all the way round, ends unpinned. level: spaced by their run across the ground, as upright pieces
        // are (each segment at the lower of its ends' heights). gap: the most room between pieces, shrunk evenly so they still
        // meet both ends (0: end to end, overlapping as they must).
        internal static List<Segment> Plan(Func<double, V3> path, double length, bool level = false, bool loop = false, double maximumLength = 128, double gap = 0)
        {
            if (double.IsNaN(length) || double.IsInfinity(length) || length < 0.1 || length > 8.1) throw new ArgumentException("Invalid curve or beam length.");
            V3 start = path(0), end = path(1);
            if (!start.Finite || !end.Finite) throw new ArgumentException("Invalid curve.");
            if (!loop && (start - end).Length < 0.1) throw new ArgumentException("Place distinct ends.");
            var arc = new Arc(path, level);
            double total = arc.Total;
            if (level && total < 0.1) throw new ArgumentException("Upright pieces need a shape that runs sideways (Home lets them follow it).");
            if (total < (loop ? 2 : 1) * length - 0.0001) throw new ArgumentException("Choose a shorter piece or mark a longer shape.");
            if (total > maximumLength || total / length > MaximumPieces) throw new ArgumentException($"The shape is over {maximumLength:0} metres or 256 pieces.");
            if (double.IsNaN(gap) || gap < 0 || gap > 16) throw new ArgumentException("Choose a gap from 0 to 16 metres.");
            if (gap > 0) return Spaced(arc, path, length, level, loop, gap);
            int count = Math.Max(loop ? 3 : 1, (int)Math.Ceiling(total / length - 1e-9));
            var result = new List<Segment>(count);
            V3 first = default, previousDirection = default;
            for (int i = 0; i < count; i++)
            {
                V3 a = arc.Sample(total * i / count), b = arc.Sample(total * (i + 1) / count), fa = Flat(a, level), fb = Flat(b, level);
                double chord = (fb - fa).Length;
                if (chord < 0.01 || (count == 1 && Math.Abs(chord - length) > 0.001))
                    throw new ArgumentException("Choose a shorter piece or a gentler bend.");
                V3 direction = (fb - fa) * (1 / chord);
                if (i > 0 && V3.Dot(previousDirection, direction) < 0.25)
                    throw new ArgumentException("Bend is too tight for this piece. Choose a shorter one, or less curve (Ctrl+scroll) for a corner.");
                if (i == 0) first = direction;
                if (loop && i == count - 1 && V3.Dot(direction, first) < 0.25)
                    throw new ArgumentException("Bend is too tight for this piece. Choose a shorter one, or less curve (Ctrl+scroll) for a corner.");
                V3 centre = (fa + fb) * 0.5;
                // Real pieces keep their native length; overlaps avoid gaps. Preserve the outer ends.
                if (!loop && i == 0) centre = Flat(start, level) + direction * (length * 0.5);
                if (!loop && i == count - 1) centre = Flat(end, level) - direction * (length * 0.5);
                V3 from = centre - direction * (length * 0.5), to = centre + direction * (length * 0.5);
                if (level) { double y = Math.Min(a.Y, b.Y); from = new V3(from.X, y, from.Z); to = new V3(to.X, y, to.Z); }
                result.Add(new Segment(from, to));
                previousDirection = direction;
            }
            return result;
        }
        private static V3 Flat(V3 p, bool level) => level ? new V3(p.X, 0, p.Z) : p;
        // With gaps: as few pieces as fit with no more than the gap between them, the gaps even; each piece square across the curve
        // where it stands (its ends on the curve); the outer pieces' ends on the curve's ends. Pieces needn't meet, so no bend is too tight.
        private static List<Segment> Spaced(Arc arc, Func<double, V3> path, double length, bool level, bool loop, double gap)
        {
            double total = arc.Total;
            int count = loop ? Math.Max(3, (int)Math.Ceiling(total / (length + gap) - 1e-9)) : Math.Max(1, (int)Math.Ceiling((total + gap) / (length + gap) - 1e-9));
            if (count > MaximumPieces) throw new ArgumentException("The shape is over 256 pieces.");
            if (!loop && count == 1 && Math.Abs(total - length) > 0.001) count = 2;
            var result = new List<Segment>(count);
            V3 At(double d) => arc.Sample(loop ? ((d % total) + total) % total : Math.Max(0, Math.Min(total, d)));
            for (int i = 0; i < count; i++)
            {
                double middle = loop ? total * i / count : length * 0.5 + (total - length) * i / Math.Max(1, count - 1);
                V3 a = At(middle - length * 0.5), b = At(middle + length * 0.5), fa = Flat(a, level), fb = Flat(b, level);
                double chord = (fb - fa).Length;
                if (chord < 0.01) throw new ArgumentException("Choose a shorter piece or a gentler bend.");
                V3 direction = (fb - fa) * (1 / chord), centre = (fa + fb) * 0.5;
                if (!loop && i == 0) centre = Flat(path(0), level) + direction * (length * 0.5);
                if (!loop && i == count - 1) centre = Flat(path(1), level) - direction * (length * 0.5);
                V3 from = centre - direction * (length * 0.5), to = centre + direction * (length * 0.5);
                if (level) { double y = Math.Min(a.Y, b.Y); from = new V3(from.X, y, from.Z); to = new V3(to.X, y, to.Z); }
                result.Add(new Segment(from, to));
            }
            return result;
        }

        private sealed class Arc
        {
            private readonly V3[] _points = new V3[Samples+1];
            private readonly double[] _distance = new double[Samples+1];
            internal double Total => _distance[Samples];
            // level: distance measured across the ground only.
            internal Arc(Func<double, V3> path, bool level = false)
            {
                _points[0] = path(0);
                for (int i=1; i<=Samples; i++)
                {
                    _points[i]=path((double)i/Samples);
                    if (!_points[i].Finite) throw new ArgumentException("Invalid curve.");
                    _distance[i]=_distance[i-1]+(Flat(_points[i],level)-Flat(_points[i-1],level)).Length;
                }
            }
            internal double Parameter(double d)
            {
                if (d <= 0) return 0;
                if (d >= Total) return 1;
                int hi=Array.BinarySearch(_distance,d);
                if (hi >= 0) return (double)hi/Samples;
                hi=~hi; int lo=hi-1;
                return (lo+(d-_distance[lo])/(_distance[hi]-_distance[lo]))/Samples;
            }
            internal V3 Sample(double d)
            {
                double sample=Parameter(d)*Samples;
                int lo=(int)sample;
                if (lo>=Samples) return _points[Samples];
                return _points[lo]+(_points[lo+1]-_points[lo])*(sample-lo);
            }
        }
    }

    // Curve's path: through every marker in order, and back to the first when closed. curve (0 to 1) is how round it runs through
    // the markers: 0 straight sides meeting in sharp corners, 1 a full curve. Two markers make a straight line. The full curve is a
    // Catmull-Rom spline spaced by the distances between markers (so uneven spacing doesn't loop), its open ends parabolic.
    internal static class Spline
    {
        internal const int MaximumPoints = 64;
        internal const double MaximumLength = 256;
        private static void Check(IReadOnlyList<V3> points, bool closed)
        {
            if (points == null || points.Count < 2 || points.Count > MaximumPoints || closed && points.Count < 3)
                throw new ArgumentException("Place 2 to 64 markers (3 to close a loop).");
            for (int i = 0; i < points.Count; i++)
            {
                if (!points[i].Finite) throw new ArgumentException("Invalid curve.");
                if ((closed || i + 1 < points.Count) && (points[(i + 1) % points.Count] - points[i]).Length < 0.1) throw new ArgumentException("Place distinct markers.");
            }
        }
        internal static int Sides(int points, bool closed) => closed ? points : points - 1;
        // Circle: round the centre, level at the edge point's height, starting and ending at the edge point.
        internal static Func<double, V3> Circle(V3 centre, V3 edge)
        {
            if (!centre.Finite || !edge.Finite) throw new ArgumentException("Invalid circle.");
            V3 outward = new V3(edge.X - centre.X, 0, edge.Z - centre.Z);
            double radius = outward.Length;
            if (radius < 0.25) throw new ArgumentException("Click a point on the edge away from the centre.");
            if (radius * 2 * Math.PI > MaximumLength) throw new ArgumentException($"A circle can be {MaximumLength / Math.PI:0} metres across at most.");
            V3 u = outward * (1 / radius), w = new V3(-u.Z, 0, u.X), c = new V3(centre.X, edge.Y, centre.Z);
            return t => c + u * (radius * Math.Cos(2 * Math.PI * t)) + w * (radius * Math.Sin(2 * Math.PI * t));
        }
        // Side i: from marker i to the next.
        internal static Func<double, V3> Side(IReadOnlyList<V3> points, bool closed, double curve, int i)
        {
            Check(points, closed);
            int n = points.Count;
            if (double.IsNaN(curve) || curve < 0 || curve > 1) throw new ArgumentException("Choose a curve from 0 to 100%.");
            V3 p0 = points[i], p1 = points[(i + 1) % n];
            if (curve == 0 || n == 2) return t => p0 + (p1 - p0) * t;
            double d = (p1 - p0).Length;
            V3 m0 = Tangent(points, closed, i) * (d * curve), m1 = Tangent(points, closed, (i + 1) % n) * (d * curve);
            return t =>
            {
                double t2 = t * t, t3 = t2 * t;
                return p0 * (2 * t3 - 3 * t2 + 1) + m0 * (t3 - 2 * t2 + t) + p1 * (-2 * t3 + 3 * t2) + m1 * (t3 - t2);
            };
        }
        // The whole path, path(0) at the first marker and path(1) at the last (back at the first when closed), each side's share of
        // the parameter as long as its chord.
        internal static Func<double, V3> Path(IReadOnlyList<V3> points, bool closed, double curve)
        {
            Check(points, closed);
            int sides = Sides(points.Count, closed);
            var parts = new Func<double, V3>[sides]; var until = new double[sides + 1];
            for (int i = 0; i < sides; i++) { parts[i] = Side(points, closed, curve, i); until[i + 1] = until[i] + (points[(i + 1) % points.Count] - points[i]).Length; }
            return t =>
            {
                double at = Math.Max(0, Math.Min(1, t)) * until[sides];
                int i = 0; while (i < sides - 1 && at > until[i + 1]) i++;
                return parts[i]((at - until[i]) / (until[i + 1] - until[i]));
            };
        }
        // The direction through marker i, per metre: square across the neighbours' chord; at an open end, the parabola's (Bessel).
        private static V3 Tangent(IReadOnlyList<V3> p, bool closed, int i)
        {
            int n = p.Count;
            V3 Through(int k)
            {
                V3 before = p[(k - 1 + n) % n], after = p[(k + 1) % n];
                return (after - before) * (1 / ((p[k] - before).Length + (after - p[k]).Length));
            }
            if (closed || (i > 0 && i < n - 1)) return Through(i);
            if (i == 0) return (p[1] - p[0]) * (2 / (p[1] - p[0]).Length) - Through(1);
            return (p[n - 1] - p[n - 2]) * (2 / (p[n - 1] - p[n - 2]).Length) - Through(n - 2);
        }
    }
}
