using PolygonLeveler;

int checks = 0;
void Check(bool condition, string name) { checks++; if (!condition) throw new Exception(name); }
var square = Geometry.Hull(new[] { new Point(10,10), new Point(0,0), new Point(0,10), new Point(10,0), new Point(5,5), new Point(0,0) });
Check(square.Count == 4 && Geometry.Area(square) == 100, "Convex hull ignores interior/duplicate markers");
Check(Geometry.Contains(square, new Point(0,0)) && Geometry.Contains(square, new Point(5,0)), "Polygon includes its boundary");
Check(!Geometry.Contains(square, new Point(-0.01,5)) && !Geometry.Contains(square, new Point(10.01,5)), "Outside terrain is excluded");
var triangle = Geometry.Hull(new[] { new Point(0,0), new Point(4,0), new Point(0,4) });
int vertices = 0;
for (int x = -2; x <= 6; x++) for (int z = -2; z <= 6; z++)
{
    bool inside = Geometry.Contains(triangle, new Point(x,z));
    Check(inside == (x >= 0 && z >= 0 && x + z <= 4), "Triangle rasterization changed outside vertex");
    if (inside) vertices++;
}
Check(vertices == 15, "Triangle grid count");
var line = Geometry.Hull(new[] { new Point(0,0), new Point(1,1), new Point(2,2) });
Check(Geometry.Area(line) == 0 && !Geometry.Contains(line,new Point(1,1)), "Degenerate polygon rejected");
foreach (double shift in new[] { 0.0, -10000.0, 10000.0 })
{
    var translated = Geometry.Hull(square.Select(p => new Point(p.X+shift,p.Z+shift)));
    Check(Geometry.Area(translated) == 100, "World-coordinate area precision");
    Check(Geometry.Contains(translated,new Point(shift+5,shift+5)), "Translated containment");
}
bool rejected = false;
try { Geometry.Hull(Enumerable.Range(0,17).Select(i => new Point(i,i))); } catch (ArgumentException) { rejected = true; }
Check(rejected, "Marker budget");
rejected = false;
try { Geometry.Hull(new[] { new Point(double.NaN,0) }); } catch (ArgumentException) { rejected = true; }
Check(rejected, "Nonfinite markers");
Check(Geometry.LevelDelta(100,100,108,out double high) && high == 8, "Native upper terrain limit inclusive");
Check(!Geometry.LevelDelta(100,100,108.01,out _), "Original terrain limit");
Check(!Geometry.LevelDelta(95,100,104,out _), "Compiler delta limit independent of original height");
Check(Geometry.LevelDelta(104,100,105,out double legacy) && legacy == 1, "Legacy modifier contributes underlying height");
Check(Geometry.LevelDelta(0,0,0,out double saturated) && saturated == 0, "Hidden saturated level/smooth deltas do not leave uneven ground");
Check(!Geometry.LevelDelta(100,100,double.PositiveInfinity,out _), "Invalid target height");
// Mixed native height state: untouched vertices, old smoothing, and legacy leveling.
float[] levels = { 0, 2, -4, 8 }, smooth = { 0, -0.75f, 1.25f, 0 };
bool[] modified = { false, true, true, true };
var original = new TerrainSnapshot(new[] { 0, 1, 2 }, levels, smooth, modified);
levels[0] = levels[1] = levels[2] = 3; smooth[0] = smooth[1] = smooth[2] = 0;
modified[0] = modified[1] = modified[2] = true;
var leveled = new TerrainSnapshot(new[] { 0, 1, 2 }, levels, smooth, modified);
Check(leveled.Matches(levels, smooth, modified), "Leveled state can be undone");
levels[3] = -2; smooth[3] = 0.5f;
Check(leveled.Matches(levels, smooth, modified), "Neighboring edits do not block undo");
foreach (string changed in new[] { "level", "smooth", "modified" })
{
    if (changed == "level") levels[2] += 0.001f;
    if (changed == "smooth") smooth[2] = 0.001f;
    if (changed == "modified") modified[2] = false;
    Check(!leveled.Matches(levels, smooth, modified), "Later " + changed + " edit blocks undo");
    // Production checks all selected cells before writing any of them.
    if (leveled.Matches(levels, smooth, modified)) original.Restore(levels, smooth, modified);
    Check(levels[0] == 3 && modified[0], "Conflict leaves other polygon vertices unchanged");
    leveled.Restore(levels, smooth, modified);
}
original.Restore(levels, smooth, modified);
Check(levels.Take(3).SequenceEqual(new float[] { 0, 2, -4 }) && smooth.Take(3).SequenceEqual(new float[] { 0, -0.75f, 1.25f }) &&
    modified.Take(3).SequenceEqual(new[] { false, true, true }), "Undo restores preexisting level, smoothing, and untouched flags exactly");
Check(levels[3] == -2 && smooth[3] == 0.5f && modified[3], "Undo preserves unrelated edits");
Check(!leveled.Matches(new float[1], new float[1], new bool[1]), "Changed grid rejected before undo");
foreach (int[] invalid in new[] { new[] { -1 }, new[] { 4 }, new[] { 0, 0 }, Array.Empty<int>(), Enumerable.Range(0, Geometry.MaxVertices + 1).ToArray() })
{
    rejected = false;
    try { new TerrainSnapshot(invalid, levels, smooth, modified); } catch (ArgumentException) { rejected = true; }
    Check(rejected, "Invalid or oversized snapshot rejected");
}
foreach (double shift in new[] { 0.0, -10000.0, 10000.0 })
{
    var ground = new List<GroundSample>();
    for (int x = -3; x <= 3; x++) for (int z = -2; z <= 2; z++)
        ground.Add(new GroundSample(x + shift, z - shift, 25 + 0.5 * x - 0.25 * z));
    GroundPlane fitted = GroundPlane.Fit(ground.AsEnumerable().Reverse());
    Check(Math.Abs(fitted.SlopeX - 0.5) < 1e-10 && Math.Abs(fitted.SlopeZ + 0.25) < 1e-10, "Existing slope survives flattening far from origin");
    foreach (GroundSample s in ground) Check(Math.Abs(fitted.At(s.Position.X, s.Position.Z) - s.Height) < 1e-9, "Exact sloped plane is unchanged");
}
var flat = GroundPlane.Fit(new[] { new GroundSample(0, 0, 12), new GroundSample(4, 0, 12), new GroundSample(0, 7, 12) });
Check(flat.At(-100, 100) == 12 && flat.SlopeX == 0 && flat.SlopeZ == 0, "Horizontal ground remains horizontal");
var noisy = new List<GroundSample>();
for (int x = -3; x <= 3; x++) for (int z = -2; z <= 2; z++)
    noisy.Add(new GroundSample(x, z, 10 + 0.2 * x - 0.3 * z + 0.05 * (x * x + z * z)));
GroundPlane closest = GroundPlane.Fit(noisy);
Check(Math.Abs(closest.SlopeX - 0.2) < 1e-10 && Math.Abs(closest.SlopeZ + 0.3) < 1e-10, "Bumps are removed while average slope is retained");
Check(Math.Abs(noisy.Sum(s => s.Height - closest.At(s.Position.X, s.Position.Z))) < 1e-9, "Fitted plane balances vertical cuts and fills");
Check(Math.Abs(noisy.Sum(s => s.Position.X * (s.Height - closest.At(s.Position.X, s.Position.Z)))) < 1e-9 &&
    Math.Abs(noisy.Sum(s => s.Position.Z * (s.Height - closest.At(s.Position.X, s.Position.Z)))) < 1e-9, "Residuals are orthogonal to both slope directions");
double Error(GroundPlane plane) => noisy.Sum(s => Math.Pow(s.Height - plane.At(s.Position.X, s.Position.Z), 2));
foreach (var perturbation in new[] { (0.1, 0.0, 0.0), (-0.1, 0.0, 0.0), (0.0, 0.1, 0.0), (0.0, 0.0, -0.1), (0.1, 0.1, 0.1) })
    Check(Error(closest) < Error(new GroundPlane(closest.X, closest.Z, closest.Height + perturbation.Item1,
        closest.SlopeX + perturbation.Item2, closest.SlopeZ + perturbation.Item3)), "Nearby alternative plane requires more squared vertical movement");
GroundPlane seam = GroundPlane.Fit(noisy.Concat(Enumerable.Repeat(noisy[0], 30)));
Check(Math.Abs(seam.SlopeX - closest.SlopeX) < 1e-10 && Math.Abs(seam.SlopeZ - closest.SlopeZ) < 1e-10 &&
    Math.Abs(seam.At(0, 0) - closest.At(0, 0)) < 1e-10, "Duplicate tile-seam vertices do not bias the plane");
foreach (var invalid in new[] {
    Array.Empty<GroundSample>(), new[] { new GroundSample(0,0,0), new GroundSample(1,0,1) },
    new[] { new GroundSample(0,0,0), new GroundSample(1,1,2), new GroundSample(2,2,4) },
    new[] { new GroundSample(0,0,0), new GroundSample(0,0,1), new GroundSample(0,0,2) },
    new[] { new GroundSample(0,0,double.NaN), new GroundSample(1,0,1), new GroundSample(0,1,2) },
    new[] { new GroundSample(double.PositiveInfinity,0,0), new GroundSample(1,0,1), new GroundSample(0,1,2) },
    new[] { new GroundSample(0,0,0), new GroundSample(1,1,0), new GroundSample(2,2 + 1e-8,0) },
    Enumerable.Range(0, Geometry.MaxVertices + 1).Select(i => new GroundSample(i, i % 5, 0)).ToArray() })
{
    rejected = false;
    try { GroundPlane.Fit(invalid); } catch (ArgumentException) { rejected = true; }
    Check(rejected, "Invalid, degenerate, or oversized plane fit rejected");
}
Check(!Geometry.LevelDelta(0, 0, closest.At(1000, 1000), out _), "Fitted slope still obeys native height limits");
Console.WriteLine($"Passed {checks} polygon, plane-fit, native height-limit, and conflict-safe undo checks.");
