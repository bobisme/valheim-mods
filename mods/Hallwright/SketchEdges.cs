using System;
using System.Collections.Generic;

namespace Hallwright
{
    // The lines an architect would ink on a piece: where its surface folds (more than Crease degrees) and where it ends. Corners that
    // differ only in normals or texture coordinates are welded first, so a hard edge isn't drawn twice and a flat face isn't cut up.
    // A face behind another (a double-sided board) doesn't count as a fold: only faces on opposite sides of an edge make it smooth.
    internal static class SketchEdges
    {
        internal const double Crease = 35;
        private const double Weld = 0.001; // (metres)
        private const double Area = 1e-10;

        // xyz: corner positions (three floats each); triangles: corner indices in threes. Returns the welded points and the line pairs.
        internal static (float[] points, int[] lines) Find(float[] xyz, int[] triangles, double crease = Crease)
        {
            if (xyz == null || triangles == null || xyz.Length % 3 != 0 || triangles.Length % 3 != 0) throw new ArgumentException("Corners come in threes.");
            int corners = xyz.Length / 3;
            var welded = new int[corners];
            var ids = new Dictionary<(long, long, long), int>();
            var points = new List<float>();
            for (int i = 0; i < corners; i++)
            {
                var key = ((long)Math.Round(xyz[3 * i] / Weld), (long)Math.Round(xyz[3 * i + 1] / Weld), (long)Math.Round(xyz[3 * i + 2] / Weld));
                if (!ids.TryGetValue(key, out int id)) { id = ids.Count; ids[key] = id; points.Add(xyz[3 * i]); points.Add(xyz[3 * i + 1]); points.Add(xyz[3 * i + 2]); }
                welded[i] = id;
            }
            double cos = Math.Cos(crease * Math.PI / 180);
            var edges = new Dictionary<long, List<(double nx, double ny, double nz, double dx, double dy, double dz)>>();
            var order = new List<long>();
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                int[] c = { triangles[t], triangles[t + 1], triangles[t + 2] };
                if (c[0] < 0 || c[1] < 0 || c[2] < 0 || c[0] >= corners || c[1] >= corners || c[2] >= corners) throw new ArgumentException("A triangle names a missing corner.");
                int a = welded[c[0]], b = welded[c[1]], d = welded[c[2]];
                if (a == b || b == d || a == d) continue;
                double[] pa = P(points, a), pb = P(points, b), pd = P(points, d);
                double[] n = Cross(Sub(pb, pa), Sub(pd, pa));
                double length = Math.Sqrt(Dot(n, n));
                if (length < Area) continue;
                n = Scale(n, 1 / length);
                foreach (var (u, v, w) in new[] { (a, b, d), (b, d, a), (d, a, b) })
                {
                    // Which way the face lies from this edge: towards its third corner, within its own plane.
                    double[] pu = P(points, u), along = Sub(P(points, v), pu), toward = Sub(P(points, w), pu);
                    double[] side = Sub(toward, Scale(along, Dot(toward, along) / Dot(along, along)));
                    double sideLength = Math.Sqrt(Dot(side, side));
                    if (sideLength < 1e-9) continue;
                    side = Scale(side, 1 / sideLength);
                    long key = (long)Math.Min(u, v) << 32 | (uint)Math.Max(u, v);
                    if (!edges.TryGetValue(key, out var faces)) { edges[key] = faces = new List<(double, double, double, double, double, double)>(2); order.Add(key); }
                    faces.Add((n[0], n[1], n[2], side[0], side[1], side[2]));
                }
            }
            var lines = new List<int>();
            foreach (long key in order)
            {
                var faces = edges[key];
                bool smooth = false;
                for (int i = 0; i < faces.Count && !smooth; i++)
                    for (int j = i + 1; j < faces.Count && !smooth; j++)
                    {
                        var f = faces[i]; var g = faces[j];
                        // Opposite sides of the edge, and (allowing for a flipped winding) nearly the same plane.
                        bool opposite = f.dx * g.dx + f.dy * g.dy + f.dz * g.dz < 0;
                        smooth = opposite && Math.Abs(f.nx * g.nx + f.ny * g.ny + f.nz * g.nz) >= cos;
                    }
                if (!smooth) { lines.Add((int)(key >> 32)); lines.Add((int)(key & 0xffffffff)); }
            }
            return (points.ToArray(), lines.ToArray());
        }
        // Pen strokes along the lines, for ink thicker than the one pixel Unity draws lines with: each stroke is a ribbon of two crossed
        // strips (seen edge-on from no side), Width wide in metres. Drawn by hand: strokes run past the corners (Overshoot), long ones
        // bend a little, widths vary, and some lines get a second, fainter pass beside the first. All of it follows from the line's own
        // ends, so the same piece is always drawn the same way and nothing shimmers. scale: the mesh's scale in its piece (the strokes
        // are made at that size, so a scaled part's ink stays as wide as everyone else's).
        internal const double Width = 0.034, Overshoot = 0.09;
        internal static (float[] xyz, int[] triangles) Strokes(float[] points, int[] lines, float sx = 1, float sy = 1, float sz = 1)
        {
            if (points == null || lines == null || points.Length % 3 != 0 || lines.Length % 2 != 0) throw new ArgumentException("Points come in threes and lines in pairs.");
            var xyz = new List<float>(); var triangles = new List<int>();
            for (int i = 0; i < lines.Length; i += 2)
            {
                int a = lines[i], b = lines[i + 1];
                if (a < 0 || b < 0 || 3 * a + 2 >= points.Length || 3 * b + 2 >= points.Length) throw new ArgumentException("A line names a missing point.");
                double[] p = { points[3 * a] * sx, points[3 * a + 1] * sy, points[3 * a + 2] * sz }, q = { points[3 * b] * sx, points[3 * b + 1] * sy, points[3 * b + 2] * sz };
                double length = Math.Sqrt(Dot(Sub(q, p), Sub(q, p)));
                if (length < 1e-4) continue;
                var random = new Random(Seed(p, q));
                Stroke(xyz, triangles, p, q, length, random, Width * (0.8 + 0.4 * random.NextDouble()), 0);
                // A second pass on about a third of the longer lines: a little off the first, finer, its own overshoot.
                if (length > 0.3 && random.NextDouble() < 0.35) Stroke(xyz, triangles, p, q, length, random, Width * 0.55, Width * (0.6 + 0.5 * random.NextDouble()));
            }
            return (xyz.ToArray(), triangles.ToArray());
        }
        private static void Stroke(List<float> xyz, List<int> triangles, double[] p, double[] q, double length, Random random, double width, double offset)
        {
            double[] along = Scale(Sub(q, p), 1 / length);
            // Two directions across the stroke: one as level as the stroke allows, one square to both.
            double[] up = Math.Abs(along[1]) < 0.9 ? new double[] { 0, 1, 0 } : new double[] { 1, 0, 0 };
            double[] across = Cross(along, up); across = Scale(across, 1 / Math.Sqrt(Dot(across, across)));
            double[] over = Cross(across, along);
            double[] shift = Add(Scale(across, offset * (random.NextDouble() < 0.5 ? -1 : 1)), Scale(over, offset * (random.NextDouble() - 0.5)));
            double start = -Overshoot * (0.4 + random.NextDouble()) * Math.Min(1, length * 2), end = length + Overshoot * (0.4 + random.NextDouble()) * Math.Min(1, length * 2);
            int segments = length < 0.6 ? 1 : length < 2 ? 2 : 3;
            double bend = Math.Min(0.03, length * 0.01) * (random.NextDouble() * 2 - 1), bend2 = Math.Min(0.02, length * 0.006) * (random.NextDouble() * 2 - 1);
            var spine = new List<double[]>();
            for (int k = 0; k <= segments; k++)
            {
                double t = (double)k / segments, sag = Math.Sin(Math.PI * t);
                double[] at = Add(Add(p, Scale(along, start + (end - start) * t)), shift);
                spine.Add(Add(at, Add(Scale(across, bend * sag), Scale(over, bend2 * sag))));
            }
            foreach (double[] side in new[] { across, over })
            {
                int first = xyz.Count / 3;
                for (int k = 0; k <= segments; k++)
                {
                    // Tapered: full width in the middle, a third of it at the tips.
                    double t = (double)k / segments, half = width * 0.5 * (k == 0 || k == segments ? 0.35 : 1);
                    foreach (double sign in new[] { -1.0, 1.0 }) { double[] v = Add(spine[k], Scale(side, sign * half)); xyz.Add((float)v[0]); xyz.Add((float)v[1]); xyz.Add((float)v[2]); }
                }
                for (int k = 0; k < segments; k++)
                {
                    int v0 = first + 2 * k;
                    triangles.Add(v0); triangles.Add(v0 + 1); triangles.Add(v0 + 3);
                    triangles.Add(v0); triangles.Add(v0 + 3); triangles.Add(v0 + 2);
                }
            }
        }
        // The same line always gets the same hand, whichever end it's named from.
        private static int Seed(double[] p, double[] q)
        {
            long h = 17;
            foreach (double v in p[0] + p[1] + p[2] <= q[0] + q[1] + q[2] ? new[] { p[0], p[1], p[2], q[0], q[1], q[2] } : new[] { q[0], q[1], q[2], p[0], p[1], p[2] })
                h = h * 31 + (long)Math.Round(v * 1000);
            return (int)(h ^ (h >> 32));
        }
        private static double[] Add(double[] a, double[] b) => new[] { a[0] + b[0], a[1] + b[1], a[2] + b[2] };
        private static double[] P(List<float> p, int i) => new double[] { p[3 * i], p[3 * i + 1], p[3 * i + 2] };
        private static double[] Sub(double[] a, double[] b) => new[] { a[0] - b[0], a[1] - b[1], a[2] - b[2] };
        private static double[] Scale(double[] a, double k) => new[] { a[0] * k, a[1] * k, a[2] * k };
        private static double Dot(double[] a, double[] b) => a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
        private static double[] Cross(double[] a, double[] b) => new[] { a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2], a[0] * b[1] - a[1] * b[0] };
    }
}
