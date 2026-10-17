// Villagewright's layout engine without the game: checks on synthetic land, and an SVG of each village to look at.
// dotnet run -- [out dir]   (pictures go to the out dir when given)
using System.Globalization;
using System.Text;
using Village;

CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
string outDir = args.Length > 0 ? args[0] : null;
int failures = 0;
void Check(bool ok, string what) { if (!ok) { failures++; Console.WriteLine("FAIL " + what); } }

var lands = new (string name, IGround ground)[] { ("hills", new Hills()), ("coast", new Coast()), ("steep", new Steep()) };
foreach (var (landName, land) in lands)
foreach (Kind kind in Enum.GetValues(typeof(Kind)))
{
    var site = Site.Circle(0, 0, 80);
    var brief = Brief.For(kind, site.Radius, 7);
    Plan plan;
    try { plan = Planner.Make(site, brief, land); }
    catch (ArgumentException ex) { Console.WriteLine($"{landName}/{kind}: refused: {ex.Message}"); if (landName != "steep") failures++; continue; }
    var again = Planner.Make(site, brief, land);
    Check(Describe(plan) == Describe(again), $"{landName}/{kind}: the same seed makes the same village");
    // Nothing overlaps, every lot is inside the site and on dry ground.
    for (int i = 0; i < plan.Lots.Count; i++)
    {
        var a = plan.Lots[i];
        foreach (var c in a.Corners(0)) Check(site.Contains(c), $"{landName}/{kind}: {a.Name} inside the site");
        for (int j = i + 1; j < plan.Lots.Count; j++) Check(!Geometry.Overlap(a.Corners(1).ToList(), plan.Lots[j].Corners(1).ToList()), $"{landName}/{kind}: {a.Name} clear of {plan.Lots[j].Name}");
        Check(a.Pad > land.WaterLevel, $"{landName}/{kind}: {a.Name} above water");
        Check(a.Width % 2 == 0 && a.Depth % 2 == 0, $"{landName}/{kind}: {a.Name} on the 2 m grid");
        foreach (var s in plan.Streets) Check(Geometry.PolylineDistance(a.Centre, s.Points, out _, out _) > s.Width / 2 + Math.Min(a.Width, a.Depth) / 2.0 - 0.5, $"{landName}/{kind}: {a.Name} off the street");
    }
    foreach (var pr in plan.Props.Where(o => o.Role == "field fence"))
        foreach (var s in plan.Streets) Check(Geometry.PolylineDistance(new P2(pr.X, pr.Z), s.Points, out _, out _) > s.Width / 2, $"{landName}/{kind}: a fence at ({pr.X:0},{pr.Z:0}) stands in a street");
    Check(plan.Palisade.Count == 0 || plan.Gates.Count > 0, $"{landName}/{kind}: the palisade has a gate");
    var other = Planner.Make(site, Brief.For(kind, site.Radius, 8), land);
    Check(Describe(plan) != Describe(other), $"{landName}/{kind}: another seed makes another village");
    Console.WriteLine($"{landName}/{kind}: {plan.Lots.Count} lots ({plan.Lots.Count(l => l.Use == Use.House)}/{brief.Houses} houses), {plan.Streets.Count} streets, {plan.Fields.Count} fields, {plan.Props.Count} props, palisade {plan.Palisade.Count}, gates {plan.Gates.Count}, {plan.Millis} ms. {string.Join(" ", plan.Notes)}");
    if (outDir != null) File.WriteAllText(Path.Combine(outDir, $"{landName}-{kind}.svg"), Svg(plan, land));
}
// A drawn plot.
{
    var plot = Site.Polygon(new List<P2> { new(-60, -30), new(50, -40), new(70, 30), new(0, 50), new(-50, 30) });
    var plan = Planner.Make(plot, Brief.For(Kind.Farming, plot.Radius, 3), new Hills());
    foreach (var l in plan.Lots) foreach (var c in l.Corners(0)) Check(plot.Contains(c), "plot: lots inside the drawn plot");
    if (outDir != null) File.WriteAllText(Path.Combine(outDir, "plot-Farming.svg"), Svg(plan, new Hills()));
    Console.WriteLine($"plot: {plan.Lots.Count} lots, {plan.Fields.Count} fields. {string.Join(" ", plan.Notes)}");
}
Console.WriteLine(failures == 0 ? "All village checks passed." : $"{failures} failures.");
return failures == 0 ? 0 : 1;

static string Describe(Plan p) => string.Join(";", p.Lots.Select(l => $"{l.Use}{l.Origin}{l.Yaw:0.0}{l.Width}x{l.Depth}")) + "|" + p.Props.Count + "|" + p.Square;

static string Svg(Plan plan, IGround g)
{
    double x0 = plan.Site.MinX - 10, z0 = plan.Site.MinZ - 10, x1 = plan.Site.MaxX + 10, z1 = plan.Site.MaxZ + 10, s = 4;
    string X(double x) => ((x - x0) * s).ToString("0.#");
    string Z(double z) => ((z1 - z) * s).ToString("0.#");
    string Pt(P2 p) => X(p.X) + "," + Z(p.Z);
    var b = new StringBuilder();
    b.Append($"<svg xmlns='http://www.w3.org/2000/svg' width='{(x1 - x0) * s:0}' height='{(z1 - z0) * s:0}'>");
    for (double z = z0; z < z1; z += 2) for (double x = x0; x < x1; x += 2)
    {
        double h = g.Height(x + 1, z + 1);
        string c = h < g.WaterLevel ? "#5b8db8" : $"hsl(95,30%,{Math.Clamp(25 + (h - 30) * 1.5, 20, 85):0}%)";
        b.Append($"<rect x='{X(x)}' y='{Z(z + 2)}' width='{2 * s}' height='{2 * s}' fill='{c}'/>");
    }
    if (plan.Site.Plot != null) b.Append($"<polygon points='{string.Join(" ", plan.Site.Plot.Select(Pt))}' fill='none' stroke='white' stroke-dasharray='8,6' stroke-width='2'/>");
    else b.Append($"<circle cx='{X(plan.Site.Centre.X)}' cy='{Z(plan.Site.Centre.Z)}' r='{plan.Site.Radius * s}' fill='none' stroke='white' stroke-dasharray='8,6' stroke-width='2'/>");
    foreach (var f in plan.Fields) b.Append($"<polygon points='{Pt(f.World(0, 0))} {Pt(f.World(f.Width, 0))} {Pt(f.World(f.Width, f.Depth))} {Pt(f.World(0, f.Depth))}' fill='#a08a3c' stroke='#5a4a1c'/>");
    b.Append($"<circle cx='{X(plan.Square.X)}' cy='{Z(plan.Square.Z)}' r='{plan.SquareRadius * s}' fill='#c9b99a'/>");
    foreach (var st in plan.Streets) b.Append($"<polyline points='{string.Join(" ", st.Points.Select(Pt))}' fill='none' stroke='{(st.Main ? "#d8c8a8" : "#b8a888")}' stroke-width='{st.Width * s}' stroke-linecap='round' stroke-linejoin='round'/>");
    if (plan.Palisade.Count > 2) b.Append($"<polygon points='{string.Join(" ", plan.Palisade.Select(Pt))}' fill='none' stroke='#4a3010' stroke-width='3'/>");
    foreach (var gt in plan.Gates) b.Append($"<circle cx='{X(gt.X)}' cy='{Z(gt.Z)}' r='10' fill='none' stroke='red' stroke-width='3'/>");
    foreach (var l in plan.Lots)
    {
        string fill = l.Use switch { Use.Hall => "#8b2e1a", Use.Temple => "#3a3a7a", Use.Smithy => "#333", Use.Storehouse => "#7a6040", Use.Boathouse => "#305060", _ => l.Style == Style.Longhouse ? "#6b4a2a" : "#9a7048" };
        b.Append($"<polygon points='{string.Join(" ", l.Corners(0).Select(Pt))}' fill='{fill}' stroke='black'/>");
        b.Append($"<circle cx='{X(l.Door.X)}' cy='{Z(l.Door.Z)}' r='4' fill='yellow'/>");
    }
    foreach (var p in plan.Props.Where(p => p.Role != "crop" && p.Role != "field fence"))
        b.Append($"<circle cx='{X(p.X)}' cy='{Z(p.Z)}' r='{(p.Role.Contains("torch") ? 2.5 : 2)}' fill='{(p.Role.Contains("torch") ? "orange" : p.Role == "palisade" ? "#3a2008" : "white")}'/>");
    b.Append("</svg>");
    return b.ToString();
}

sealed class Hills : IGround
{
    public double WaterLevel => 30;
    public double Height(double x, double z) => 38 + 3 * Math.Sin(x / 23) * Math.Cos(z / 31) + 2 * Math.Sin((x + z) / 17) + x * 0.04;
}
sealed class Coast : IGround
{
    public double WaterLevel => 30;
    // Land rising from a sea in the south-east.
    public double Height(double x, double z) => 31 + (60 - (x - z) * 0.7) * 0.12 + 1.5 * Math.Sin(x / 13) * Math.Cos(z / 19);
}
sealed class Steep : IGround
{
    public double WaterLevel => 30;
    public double Height(double x, double z) => 50 + 12 * Math.Sin(x / 9) * Math.Cos(z / 11);
}
