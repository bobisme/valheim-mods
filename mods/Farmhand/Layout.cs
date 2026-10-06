using System;

namespace Farmhand
{
    // No Unity dependency: these limits apply to both the preview and real operations.
    internal static class Layout
    {
        internal static int RowCount(int requested) => Math.Max(1, Math.Min(9, requested));
        internal static float Spacing(float requested, float growRadius)
        {
            if (float.IsNaN(requested) || float.IsInfinity(requested)) requested = 1.8f;
            if (float.IsNaN(growRadius) || float.IsInfinity(growRadius) || growRadius < 0f) growRadius = 0f;
            return Math.Max(Math.Max(0.5f, Math.Min(5f, requested)), growRadius * 2f + 0.1f);
        }

        internal static float[] Offsets(int count, float spacing)
        {
            count = RowCount(count);
            var offsets = new float[count];
            for (int i = 0; i < count; i++) offsets[i] = (i - (count - 1) * 0.5f) * spacing;
            return offsets;
        }
    }

    internal enum HarvestOutcome { Waiting, Confirmed, TimedOut }

    internal static class HarvestConfirmation
    {
        // Disabling or unloading a view is not a harvest acknowledgement.
        internal static HarvestOutcome Observe(bool destroyed, bool picked, double elapsed)
        {
            if (destroyed || picked) return HarvestOutcome.Confirmed;
            return elapsed >= 2.0 ? HarvestOutcome.TimedOut : HarvestOutcome.Waiting;
        }
    }
}
