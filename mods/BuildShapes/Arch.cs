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
    }
}
