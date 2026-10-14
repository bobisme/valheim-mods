using System.Globalization;
using Hallwright;

int checks = 0;
void Check(bool ok, string label) { checks++; if (!ok) throw new Exception(label); }
void Reject(Action action, string label) { try { action(); } catch (ArgumentException) { checks++; return; } throw new Exception("Accepted: " + label); }
// Each face gets its own corners, as game meshes do (normals and uvs differ per face).
(float[] xyz, int[] tris) Split(float[][] corners, int[][] faces)
{
    var xyz = new List<float>(); var tris = new List<int>();
    foreach (var f in faces) { int at = xyz.Count / 3; foreach (int i in f) xyz.AddRange(corners[i]); for (int k = 1; k + 1 < f.Length; k++) { tris.Add(at); tris.Add(at + k); tris.Add(at + k + 1); } }
    return (xyz.ToArray(), tris.ToArray());
}
HashSet<(float,float,float,float,float,float)> Segments((float[] points, int[] lines) r)
{
    var s = new HashSet<(float,float,float,float,float,float)>();
    for (int i = 0; i < r.lines.Length; i += 2)
    {
        int a = r.lines[i], b = r.lines[i + 1];
        var p = (r.points[3*a], r.points[3*a+1], r.points[3*a+2]); var q = (r.points[3*b], r.points[3*b+1], r.points[3*b+2]);
        if (p.CompareTo(q) > 0) (p, q) = (q, p);
        s.Add((p.Item1, p.Item2, p.Item3, q.Item1, q.Item2, q.Item3));
    }
    return s;
}

if (args.Length >= 2) // dump <obj> <out.json> [crease]: the lines for a game mesh, to look at
{
    var xyz = new List<float>(); var tris = new List<int>();
    foreach (string line in File.ReadLines(args[0]))
    {
        var w = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (w.Length == 0) continue;
        if (w[0] == "v") xyz.AddRange(w.Skip(1).Take(3).Select(x => float.Parse(x, CultureInfo.InvariantCulture)));
        if (w[0] == "f") { var idx = w.Skip(1).Select(x => int.Parse(x.Split('/')[0]) - 1).ToArray(); for (int k = 1; k + 1 < idx.Length; k++) { tris.Add(idx[0]); tris.Add(idx[k]); tris.Add(idx[k + 1]); } }
    }
    var found = SketchEdges.Find(xyz.ToArray(), tris.ToArray(), args.Length > 2 ? double.Parse(args[2], CultureInfo.InvariantCulture) : SketchEdges.Crease);
    File.WriteAllText(args[1], System.Text.Json.JsonSerializer.Serialize(new { points = found.points, lines = found.lines, triangles = tris, xyz }));
    Console.WriteLine($"{args[0]}: {tris.Count / 3} triangles, {found.lines.Length / 2} lines");
    return;
}

float[][] cube = { new float[]{0,0,0}, new float[]{1,0,0}, new float[]{1,1,0}, new float[]{0,1,0}, new float[]{0,0,1}, new float[]{1,0,1}, new float[]{1,1,1}, new float[]{0,1,1} };
int[][] cubeFaces = { new[]{0,3,2,1}, new[]{4,5,6,7}, new[]{0,1,5,4}, new[]{2,3,7,6}, new[]{1,2,6,5}, new[]{0,4,7,3} };
var box = Split(cube, cubeFaces);
var boxLines = SketchEdges.Find(box.xyz, box.tris);
Check(boxLines.points.Length == 24, "A cube's 24 face corners weld to 8 points");
Check(boxLines.lines.Length == 24 && Segments(boxLines).Count == 12, "A cube is drawn with its 12 edges, not its face diagonals");

float[][] quad = { new float[]{0,0,0}, new float[]{2,0,0}, new float[]{2,0,2}, new float[]{0,0,2} };
var flat = Split(quad, new[] { new[]{0,1,2,3} });
Check(Segments(SketchEdges.Find(flat.xyz, flat.tris)).Count == 4, "A flat board shows its outline without the diagonal");
var both = Split(quad, new[] { new[]{0,1,2,3}, new[]{0,3,2,1} });
Check(Segments(SketchEdges.Find(both.xyz, both.tris)).Count == 4, "A double-sided board: the back faces neither hide its outline nor draw its diagonal");
var flipped = Split(quad, new[] { new[]{0,1,2}, new[]{0,3,2} });
Check(Segments(SketchEdges.Find(flipped.xyz, flipped.tris)).Count == 4, "A flat board with one triangle wound the other way still has no diagonal");

// A gentle fold stays smooth, a sharp one is inked.
float[][] folded(double degrees) { double r = degrees * Math.PI / 180; return new[] { new float[]{0,0,0}, new float[]{0,0,2}, new float[]{2,0,2}, new float[]{2,0,0}, new float[]{(float)(-2*Math.Cos(r)),(float)(2*Math.Sin(r)),2}, new float[]{(float)(-2*Math.Cos(r)),(float)(2*Math.Sin(r)),0} }; }
int[][] wings = { new[]{0,1,2,3}, new[]{0,5,4,1} };
var gentle = Split(folded(20), wings); var sharp = Split(folded(60), wings);
Check(Segments(SketchEdges.Find(gentle.xyz, gentle.tris)).Count == 6, "A 20° fold is not inked");
Check(Segments(SketchEdges.Find(sharp.xyz, sharp.tris)).Count == 7, "A 60° fold is inked");
Check(Segments(SketchEdges.Find(gentle.xyz, gentle.tris, 10)).Count == 7, "The crease angle is adjustable");

// Twelve-sided log: 30° facets stay smooth at 35° (only its ends' rims are inked), an eight-sided one shows its facets.
(float[] xyz, int[] tris) Log(int sides)
{
    var c = new List<float[]>(); for (int i = 0; i < sides; i++) { double a = 2 * Math.PI * i / sides; c.Add(new[]{(float)Math.Cos(a),(float)Math.Sin(a),0}); c.Add(new[]{(float)Math.Cos(a),(float)Math.Sin(a),4}); }
    var f = new List<int[]>(); for (int i = 0; i < sides; i++) { int j = (i + 1) % sides; f.Add(new[]{2*i, 2*j, 2*j+1, 2*i+1}); }
    return Split(c.ToArray(), f.ToArray());
}
var round = Log(12); Check(Segments(SketchEdges.Find(round.xyz, round.tris)).Count == 24, "A twelve-sided log shows only its two rims");
var octagon = Log(8); Check(Segments(SketchEdges.Find(octagon.xyz, octagon.tris)).Count == 24, "An eight-sided log shows its facets and rims");

var sliver = (xyz: new float[]{0,0,0, 1,0,0, 2,0,0, 0,0,0, 1,0,0, 1,0,1}, tris: new[]{0,1,2, 3,4,5});
Check(Segments(SketchEdges.Find(sliver.xyz, sliver.tris)).Count == 3, "Zero-area triangles are left out");
Check(SketchEdges.Find(new float[0], new int[0]).lines.Length == 0, "An empty mesh has no lines");
Reject(() => SketchEdges.Find(new float[]{0,0}, new int[0]), "Corners not in threes");
Reject(() => SketchEdges.Find(new float[]{0,0,0}, new[]{0,0,1}), "A triangle naming a missing corner");
// Strokes: ribbons along each line, past its ends, the same every time.
var one = SketchEdges.Strokes(new float[]{0,0,0, 3,0,0}, new[]{0,1});
var again = SketchEdges.Strokes(new float[]{3,0,0, 0,0,0}, new[]{1,0});
Check(one.xyz.SequenceEqual(SketchEdges.Strokes(new float[]{0,0,0, 3,0,0}, new[]{0,1}).xyz), "The same line is stroked the same way every time");
Check(one.xyz.Length == again.xyz.Length, "A line is stroked alike whichever end names it");
Check(one.triangles.Length > 0 && one.triangles.All(i => i >= 0 && i < one.xyz.Length / 3), "Stroke triangles name their own corners");
Check(one.xyz.All(float.IsFinite), "Strokes have finite corners");
float minX = Enumerable.Range(0, one.xyz.Length / 3).Min(i => one.xyz[3*i]), maxX = Enumerable.Range(0, one.xyz.Length / 3).Max(i => one.xyz[3*i]);
Check(minX < -0.02 && maxX > 3.02 && minX > -0.2 && maxX < 3.2, "Strokes run a little past both corners, as a drawn line does");
float spread = Enumerable.Range(0, one.xyz.Length / 3).Max(i => Math.Max(Math.Abs(one.xyz[3*i+1]), Math.Abs(one.xyz[3*i+2])));
Check(spread > 0.005 && spread < 0.08, "Strokes are pen-wide and stay close to their line");
var tall = SketchEdges.Strokes(new float[]{0,0,0, 0,2,0}, new[]{0,1});
Check(tall.triangles.Length > 0 && tall.xyz.All(float.IsFinite), "A vertical line gets a stroke too");
var scaled = SketchEdges.Strokes(new float[]{0,0,0, 1,0,0}, new[]{0,1}, 2, 2, 2);
Check(Enumerable.Range(0, scaled.xyz.Length / 3).Max(i => scaled.xyz[3*i]) > 2, "Strokes are made at the part's scale");
var cubeStrokes = SketchEdges.Strokes(boxLines.points, boxLines.lines);
Check(cubeStrokes.triangles.Length >= 12 * 2 * 2 * 3, "Every line of a cube gets at least one stroke");
Check(SketchEdges.Strokes(new float[]{1,1,1, 1,1,1}, new[]{0,1}).triangles.Length == 0, "A zero-length line gets no stroke");
Reject(() => SketchEdges.Strokes(new float[]{0,0,0}, new[]{0,1}), "A line naming a missing point");
Console.WriteLine($"Passed {checks} Hallwright sketch edge checks.");
