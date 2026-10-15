using System;

namespace BuildShapes
{
    internal static class Arch
    {
        internal const float MaximumRise = 32f;
        internal static double HorizontalSpan(V3 start, V3 end)
        {
            V3 delta = end - start;
            return Math.Sqrt(delta.X * delta.X + delta.Z * delta.Z);
        }
        internal static double DefaultRise(V3 start, V3 end) =>
            Math.Min(MaximumRise, Math.Max(0.25, HorizontalSpan(start, end) * 0.5));

        // Height is the rise above the midpoint of the endpoint line, not world altitude.
        // Feeding this ON-curve midpoint into Curve preserves the existing native-length planner.
        internal static V3 Middle(V3 start, V3 end, double rise)
        {
            if (!start.Finite || !end.Finite || double.IsNaN(rise) || double.IsInfinity(rise) ||
                rise < 0 || rise > MaximumRise)
                throw new ArgumentException("Choose a center rise from 0 to 32 metres.");
            double span = HorizontalSpan(start, end);
            if (span < 0.1 || span > 128)
                throw new ArgumentException("Place arch ends at least 0.1 metres apart horizontally, within 128 metres.");
            return (start + end) * 0.5 + new V3(0, rise, 0);
        }
        // Arch: the circle through both ends and the point the given height above their middle; the ends lean as the circle has them.
        internal static Func<double, V3> Circular(V3 start, V3 end, double rise)
        {
            V3 top = Middle(start, end, rise), chord = end - start;
            double length = chord.Length;
            // In the upright plane through the ends: u along the chord, v square to it and upwards.
            V3 u = chord * (1 / length), v = new V3(0, 1, 0) - u * u.Y;
            if (v.Length < 1e-9) throw new ArgumentException("Place arch ends at least 0.1 metres apart horizontally.");
            v = v * (1 / v.Length);
            double cu = V3.Dot(top - start, u), cv = V3.Dot(top - start, v);
            if (cv < 1e-6) return t => start + chord * t; // (no height: straight)
            // Centre on the chord's perpendicular bisector (u = length/2), equally far from the start and the top.
            double centre = (cu * cu + cv * cv - length * cu) / (2 * cv);
            double a0 = Math.Atan2(-centre, -length * 0.5), a1 = Math.Atan2(-centre, length * 0.5), radius = Math.Sqrt(length * length * 0.25 + centre * centre);
            // Sweep from the start to the end the way that passes over the top (anticlockwise in u, v when the top is above).
            double sweep = a1 - a0; if (sweep > 0) sweep -= 2 * Math.PI;
            return t => { double a = a0 + sweep * t; return start + u * (length * 0.5 + radius * Math.Cos(a)) + v * (centre + radius * Math.Sin(a)); };
        }
    }
}
