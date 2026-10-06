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
Console.WriteLine($"Passed {checks} polygon, native height-limit, and conflict-safe undo checks.");
