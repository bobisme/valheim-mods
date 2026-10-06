using Farmhand;

int checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new Exception(message);
}
for (int count = -10; count <= 30; count++)
{
    float[] row = Layout.Offsets(count, 2f);
    Check(row.Length >= 1 && row.Length <= 9, "Unbounded row");
    Check(Math.Abs(row.Sum()) < 0.0001f, "Row isn't centered on aim point");
    Check(row.Distinct().Count() == row.Length, "Duplicate planting targets");
    for (int i = 1; i < row.Length; i++) Check(Math.Abs(row[i] - row[i - 1] - 2f) < 0.0001f, "Uneven row spacing");
}
foreach (float radius in new[] { 0f, 0.5f, 1f, 2.5f, 5f })
foreach (float requested in new[] { -1f, 0.5f, 1.8f, 100f, float.NaN, float.PositiveInfinity })
{
    float spacing = Layout.Spacing(requested, radius);
    Check(!float.IsNaN(spacing) && !float.IsInfinity(spacing), "Invalid spacing");
    Check(spacing > radius * 2 && spacing >= 0.5f, "Growth spaces overlap");
}
Check(Layout.Offsets(1, 2f).Single() == 0f, "Single crop must use aim point");
Check(HarvestConfirmation.Observe(false, false, 0) == HarvestOutcome.Waiting, "Unacknowledged harvest replants early");
Check(HarvestConfirmation.Observe(false, false, 1.999) == HarvestOutcome.Waiting, "Premature RPC timeout");
Check(HarvestConfirmation.Observe(false, false, 2) == HarvestOutcome.TimedOut, "Unbounded wait");
Check(HarvestConfirmation.Observe(false, false, 100) == HarvestOutcome.TimedOut, "Late unacknowledged crop replanted");
Check(HarvestConfirmation.Observe(true, false, 0.1) == HarvestOutcome.Confirmed, "Destroyed harvested crop not acknowledged");
Check(HarvestConfirmation.Observe(false, true, 0.1) == HarvestOutcome.Confirmed, "Picked crop not acknowledged");
Check(HarvestConfirmation.Observe(false, true, 2.1) == HarvestOutcome.Confirmed, "Confirmed response at deadline discarded");
Console.WriteLine($"Passed {checks} geometry and harvest acknowledgement checks.");
