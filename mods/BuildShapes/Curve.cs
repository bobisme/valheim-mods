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
        private const int Samples = 1024;
        // Pin the chosen prefab-local point to a path station using the FINAL oriented frame.
        // This keeps the anchor fixed when curve-following or yaw/pitch/roll changes.
        internal static V3 AnchoredOrigin(V3 station,V3 anchor,V3 right,V3 up,V3 forward) =>
            station-right*anchor.X-up*anchor.Y-forward*anchor.Z;
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
            var arc = new Arc(start, middle, end);
            double total = arc.Total;
            if (total < length - 0.0001) throw new ArgumentException("Choose a shorter beam or mark a longer curve.");
            if (total > 128 || total / length > MaximumPieces) throw new ArgumentException("Curve exceeds 128 metres or 256 pieces.");
            int count = Math.Max(1, (int)Math.Ceiling(total / length - 1e-9));
            var result = new List<Segment>(count);
            V3 previousDirection = default;
            for (int i = 0; i < count; i++)
            {
                V3 a = arc.Sample(total * i / count), b = arc.Sample(total * (i + 1) / count);
                double chord = (b - a).Length;
                if (chord < 0.01 || (count == 1 && Math.Abs(chord - length) > 0.001))
                    throw new ArgumentException("Choose a shorter beam or a gentler bend.");
                V3 direction = (b - a) * (1 / chord);
                if (i > 0 && V3.Dot(previousDirection, direction) < 0.25)
                    throw new ArgumentException("Bend is too tight for this beam. Choose a shorter beam.");
                V3 centre = (a + b) * 0.5;
                // Real pieces keep their native length; overlaps avoid gaps. Preserve the outer ends.
                if (i == 0) centre = start + direction * (length * 0.5);
                if (i == count - 1) centre = end - direction * (length * 0.5);
                result.Add(new Segment(centre - direction * (length * 0.5), centre + direction * (length * 0.5)));
                previousDirection = direction;
            }
            return result;
        }

        internal readonly struct Station
        {
            internal readonly V3 Position;
            internal readonly double Yaw;
            internal Station(V3 position, double yaw) { Position = position; Yaw = yaw; }
        }
        // Maximum desired spacing is adjusted evenly to include both ends. Only yaw follows the path;
        // a vertical tangent retains the previous yaw, so upright posts never tip over.
        internal static List<Station> Repeat(V3 start, V3 middle, V3 end, double spacing)
        {
            if (!start.Finite || !middle.Finite || !end.Finite || double.IsNaN(spacing) || double.IsInfinity(spacing) || spacing < 0.25 || spacing > 16)
                throw new ArgumentException("Choose spacing from 0.25 to 16 metres.");
            if ((start-end).Length < 0.1 || (start-middle).Length > 128 || (middle-end).Length > 128)
                throw new ArgumentException("Place distinct ends within 128 metres of the bend.");
            var arc = new Arc(start, middle, end);
            if (arc.Total > 128 || arc.Total < 0.1 || Math.Ceiling(arc.Total / spacing) + 1 > MaximumPieces)
                throw new ArgumentException("Repeat exceeds 128 metres or 256 pieces. Increase spacing.");
            int intervals = Math.Max(1, (int)Math.Ceiling(arc.Total / spacing));
            var result = new List<Station>(intervals + 1);
            V3 control = middle * 2 - (start+end)*0.5;
            double yaw = 0;
            for (int i = 0; i <= intervals; i++)
            {
                double d = arc.Total*i/intervals, t = arc.Parameter(d);
                V3 tangent = (control-start)*(2*(1-t)) + (end-control)*(2*t);
                if (Math.Sqrt(tangent.X*tangent.X+tangent.Z*tangent.Z) > 0.00001)
                    yaw = Math.Atan2(tangent.X,tangent.Z)*180/Math.PI;
                result.Add(new Station(arc.Sample(d), yaw));
            }
            return result;
        }

        private sealed class Arc
        {
            private readonly V3[] _points = new V3[Samples+1];
            private readonly double[] _distance = new double[Samples+1];
            internal double Total => _distance[Samples];
            internal Arc(V3 start, V3 middle, V3 end)
            {
                _points[0] = start;
                for (int i=1; i<=Samples; i++)
                { _points[i]=At(start,middle,end,(double)i/Samples); _distance[i]=_distance[i-1]+(_points[i]-_points[i-1]).Length; }
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
}
