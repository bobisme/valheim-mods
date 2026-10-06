using System;
using System.Collections.Generic;
using System.Linq;

namespace PolygonLeveler
{
    internal readonly struct Point
    {
        internal readonly double X, Z;
        internal Point(double x, double z) { X = x; Z = z; }
    }

    internal static class Geometry
    {
        internal const int MaxMarkers = 16;
        internal const int MaxVertices = 2048;
        internal static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static double Cross(Point a, Point b, Point c) => (b.X - a.X) * (c.Z - a.Z) - (b.Z - a.Z) * (c.X - a.X);

        internal static List<Point> Hull(IEnumerable<Point> input)
        {
            var points = input.Take(MaxMarkers + 1).ToList();
            if (points.Count > MaxMarkers || points.Any(p => !Finite(p.X) || !Finite(p.Z)))
                throw new ArgumentException("Invalid or too many markers");
            points = points.OrderBy(p => p.X).ThenBy(p => p.Z).ToList();
            for (int i = points.Count - 1; i > 0; i--)
                if (points[i].X == points[i - 1].X && points[i].Z == points[i - 1].Z) points.RemoveAt(i);
            if (points.Count < 3) return points;
            var hull = new List<Point>();
            foreach (Point p in points)
            {
                while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            int lower = hull.Count;
            for (int i = points.Count - 2; i >= 0; i--)
            {
                Point p = points[i];
                while (hull.Count > lower && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0) hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            hull.RemoveAt(hull.Count - 1);
            return hull;
        }

        internal static double Area(IReadOnlyList<Point> hull)
        {
            if (hull.Count < 3) return 0;
            // Work relative to the first point so small polygons far from world origin remain precise.
            double sum = 0;
            for (int i = 1; i < hull.Count - 1; i++) sum += Cross(hull[0], hull[i], hull[i + 1]);
            return Math.Abs(sum) * 0.5;
        }

        internal static bool Contains(IReadOnlyList<Point> hull, Point point)
        {
            if (hull.Count < 3 || Area(hull) < 0.01 || !Finite(point.X) || !Finite(point.Z)) return false;
            for (int i = 0; i < hull.Count; i++)
                if (Cross(hull[i], hull[(i + 1) % hull.Count], point) < -0.00001) return false;
            return true;
        }

        internal static bool LevelDelta(double underlying, double limitBase, double target, out double delta)
        {
            delta = target - underlying;
            return Finite(underlying) && Finite(limitBase) && Finite(target) &&
                Math.Abs(target - limitBase) <= 8.0 && Math.Abs(delta) <= 8.0;
        }
    }
}
