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
Console.WriteLine($"Passed {checks} polygon rasterization and native height-limit checks.");
