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
        // Arch: rises straight up from both ends. An upright leg as long as one beam (three quarters of the height on a low arch), so the end beams
        // stand plumb, then a half-ellipse over the line between the legs' tops (a semicircle when it's half as high as it's wide),
        // its top the given height above the ends' middle. Leans with the line when the ends are at different heights.
        internal static Func<double, V3> Upright(V3 start, V3 end, double rise, double leg)
        {
            Middle(start, end, rise); // (the same checks)
            leg = Math.Max(0, Math.Min(leg, rise * 0.75));
            V3 lift = new V3(0, leg, 0), crown = new V3(0, rise - leg, 0), a = start + lift, chord = end - start;
            // The parameter runs about evenly along the length (the planner measures the real length anyway).
            double semi = 0.5 * chord.Length, top = rise - leg, oval = Math.PI * (3 * (semi + top) - Math.Sqrt((3 * semi + top) * (semi + 3 * top))) * 0.5;
            double total = 2 * leg + oval, l = total > 0 ? leg / total : 0;
            return t =>
            {
                if (t <= l) return start + lift * (l > 0 ? t / l : 1);
                if (t >= 1 - l) return end + lift * (l > 0 ? (1 - t) / l : 1);
                double angle = Math.PI * (t - l) / (1 - 2 * l);
                return a + chord * ((1 - Math.Cos(angle)) * 0.5) + crown * Math.Sin(angle);
            };
        }
        // Arc: the circle through both ends and the point the given height above their middle; the ends lean as the circle has them.
        internal static Func<double, V3> Circular(V3 start, V3 end, double rise)
        {
            V3 top = Middle(start, end, rise), chord = end - start;
            double length = chord.Length;
            // In the upright plane through the ends: u along the chord, v square to it and upwards.
            V3 u = chord * (1 / length), v = new V3(0, 1, 0) - u * u.Y;
            if (v.Length < 1e-9) throw new ArgumentException("Place arc ends at least 0.1 metres apart horizontally.");
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
