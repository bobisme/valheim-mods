using BuildKit;

int checks = 0;
void Check(bool ok, string name) { checks++; if (!ok) throw new Exception(name); }
bool Near(float a, float b, float e = 1e-4f) => Math.Abs(a - b) < e;

// ---- The game's roof shapes, at their snap points ----
Check(Near(Roofs.Height(Shape.Straight, false, 1, 1), 0) && Near(Roofs.Height(Shape.Straight, false, -1, -1), 1), "26° straight: eave 0, top 1");
Check(Near(Roofs.Height(Shape.Straight, true, 1, 1), -1) && Near(Roofs.Height(Shape.Straight, true, -1, -1), 1), "45° straight: centred, -1 to 1");
Check(Near(Roofs.Height(Shape.Ridge, false, 1, 0), 0.5f) && Near(Roofs.Height(Shape.Ridge, false, 1, 1), 0) && Near(Roofs.Height(Shape.Ridge, false, -1, -1), 0), "26° ridge: 0.5 along the middle, 0 at both eaves");
Check(Near(Roofs.Height(Shape.Ridge, true, -1, 0), 1), "45° ridge: 1 along the middle");
foreach (bool steep in new[] { false, true })
{
    float k = steep ? 2 : 1;
    Check(Near(Roofs.Height(Shape.OuterCorner, steep, -1, -1), k) && Near(Roofs.Height(Shape.OuterCorner, steep, 1, -1), 0) && Near(Roofs.Height(Shape.OuterCorner, steep, -1, 1), 0) && Near(Roofs.Height(Shape.OuterCorner, steep, 1, 1), 0), "Outer corner: one high corner");
    Check(Near(Roofs.Height(Shape.InnerCorner, steep, 1, 1), 0) && Near(Roofs.Height(Shape.InnerCorner, steep, 1, -1), k) && Near(Roofs.Height(Shape.InnerCorner, steep, -1, 1), k) && Near(Roofs.Height(Shape.InnerCorner, steep, -1, -1), k), "Inner corner: one low corner");
}

// ---- The sod: even at the edges, lumpy inside, never thin ----
for (float t = -1; t <= 1; t += 0.05f)
    Check(Near(TurfMesh.Sod(t, 1, 7), TurfMesh.Thick) && Near(TurfMesh.Sod(-1, t, 7), TurfMesh.Thick), "Sod even along the edges at " + t);
float lo = 9, hi = -9;
for (float x = -1; x <= 1; x += 0.05f) for (float z = -1; z <= 1; z += 0.05f) { float s = TurfMesh.Sod(x, z, 7); lo = Math.Min(lo, s); hi = Math.Max(hi, s); }
Check(hi - lo > 0.02f && lo > TurfMesh.Thick - TurfMesh.Lumps - 1e-3f && hi < TurfMesh.Thick + TurfMesh.Lumps + 1e-3f, $"Lumpy inside, within bounds: {lo:0.000}..{hi:0.000}");

// ---- The torn edge: lumpy, never past its reach, nothing at the corners ----
for (int side = 0; side < 4; side++)
{
    float lo2 = 9, hi2 = 0;
    for (float t = 0; t <= 2.001f; t += 0.01f) { float o = TurfMesh.Out(side, t, 7); lo2 = Math.Min(lo2, o); hi2 = Math.Max(hi2, o); Check(TurfMesh.Drop(side, t, 7) >= 0 && TurfMesh.Drop(side, t, 7) <= 0.071f, "Droop in bounds"); }
    Check(lo2 >= 0 && hi2 <= 0.141f && hi2 - lo2 > 0.06f, $"Side {side}: lumps from {lo2:0.000} to {hi2:0.000}");
    Check(TurfMesh.Out(side, 0, 7) == 0 && TurfMesh.Out(side, 2, 7) == 0, "Nothing reaches out at the corners");
}

// ---- The meshes: every face turned the way it shows ----
foreach (Shape shape in Enum.GetValues<Shape>())
    foreach (bool steep in new[] { false, true })
    foreach (int rough in new[] { 15, 0, 5, 10 })
    {
        var m = TurfMesh.Build(shape, steep, 7, rough);
        string name = $"{shape} {(steep ? 45 : 26)}° open {rough}";
        Check(m.N.Count == m.V.Count && m.Uv.Count / 2 == m.Count, name + ": a normal and uv per corner");
        Check(m.T.All(t => t.Count % 3 == 0 && t.All(i => i >= 0 && i < m.Count)), name + ": whole triangles, in range");
        Check(m.T[TurfMesh.Top].Count > 0 && m.T[TurfMesh.Boards].Count > 0 && m.T[TurfMesh.Earth].Count > 0 && m.T[TurfMesh.Bark].Count > 0 && m.T[TurfMesh.Grass].Count > 0 && m.T[TurfMesh.Stick].Count > 0, name + ": every material used");
        foreach (int slot in new[] { TurfMesh.Top, TurfMesh.Boards, TurfMesh.Earth, TurfMesh.Bark, TurfMesh.Stick })
        {
            var t = m.T[slot]; int bad = 0;
            for (int i = 0; i < t.Count; i += 3)
            {
                (float x, float y, float z) P(int k) => (m.V[3 * k], m.V[3 * k + 1], m.V[3 * k + 2]);
                var (a, b, c) = (P(t[i]), P(t[i + 1]), P(t[i + 2]));
                float ux = b.x - a.x, uy = b.y - a.y, uz = b.z - a.z, vx = c.x - a.x, vy = c.y - a.y, vz = c.z - a.z;
                float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx; // (Unity: Cross(b - a, c - a) is the front)
                int k0 = t[i];
                float dot = nx * m.N[3 * k0] + ny * m.N[3 * k0 + 1] + nz * m.N[3 * k0 + 2];
                if (dot <= 0 && nx * nx + ny * ny + nz * nz > 1e-7) bad++; // (slivers where an edge pinches to nothing at a corner aside)
            }
            Check(bad == 0, $"{name}: slot {slot} faces outward ({bad} turned in)");
        }
        // (the sod stays inside the piece's square, and nowhere is it thin)
        Check(Enumerable.Range(0, m.Count).All(i => Math.Abs(m.V[3 * i]) <= 1.25f && Math.Abs(m.V[3 * i + 2]) <= 1.25f), name + ": inside the piece");
        // (the soil's top row meets the grass exactly along every side: no gap where the bulge starts)
        Check(m.T[TurfMesh.Earth].Count >= 4 * 32 * (TurfMesh.SoilRows - 1) * 6, name + ": the soil all round");
        // (a side against a neighbour stays inside the square at the top, where the neighbour's grass meets it; an open one reaches out)
        float maxOut = Enumerable.Range(0, m.Count).Where(i => i < m.Count).Max(i => Math.Max(Math.Abs(m.V[3 * i]), Math.Abs(m.V[3 * i + 2])));
        Check(rough != 15 || maxOut > 1.08f, name + ": the open edge reaches out: " + maxOut);
    }

// ---- The smoke hole: open through the middle, a hood standing clear over it ----
foreach (Shape vs in new[] { Shape.Straight, Shape.Ridge })
foreach (bool steep in new[] { false, true })
{
    var m = TurfMesh.Build(vs, steep, 7, 15, hole: true);
    var plain = TurfMesh.Build(vs, steep, 7, 15);
    float hx = TurfMesh.HoleAlong(vs);
    Check(m.T[TurfMesh.Top].Count < plain.T[TurfMesh.Top].Count, "The hole takes sod away");
    // (nothing of the sod or the boards over the hole's middle)
    bool covered = false;
    foreach (int slot in new[] { TurfMesh.Top, TurfMesh.Boards, TurfMesh.Earth })
        for (int i = 0; i < m.T[slot].Count; i += 3)
        {
            float cx = 0, cz = 0; for (int k = 0; k < 3; k++) { cx += m.V[3 * m.T[slot][i + k]] / 3; cz += m.V[3 * m.T[slot][i + k] + 2] / 3; }
            if (Math.Abs(cx) < hx - 0.05f && Math.Abs(cz) < TurfMesh.HoleHalf - 0.05f) covered = true;
        }
    Check(!covered, vs + ": nothing over the smoke hole but the hood");
    float clear = TurfMesh.HoodBase(vs, steep) - (Roofs.Height(vs, steep, 0, -TurfMesh.HoleHalf) + TurfMesh.Thick);
    Check(clear >= 0.84f, $"A smoke ball (0.84 m) rolls out under the cap: {clear:0.00} m clear");
    int bad = 0; var t = m.T[TurfMesh.Stick];
    for (int i = 0; i < t.Count; i += 3)
    {
        (float x, float y, float z) P(int k) => (m.V[3 * k], m.V[3 * k + 1], m.V[3 * k + 2]);
        var (a, b, c) = (P(t[i]), P(t[i + 1]), P(t[i + 2]));
        float ux = b.x - a.x, uy = b.y - a.y, uz = b.z - a.z, vx = c.x - a.x, vy = c.y - a.y, vz = c.z - a.z;
        float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
        if (nx * m.N[3 * t[i]] + ny * m.N[3 * t[i] + 1] + nz * m.N[3 * t[i] + 2] <= 0 && nx * nx + ny * ny + nz * nz > 1e-7) bad++;
    }
    Check(bad == 0, $"The hood's wood faces out ({bad} turned in)");
}

// ---- Wattle: the fence, and the daubed wall ----
foreach (var (name, m, w, h, d) in new[] { ("fence", Wattle.Fence(), 1.03f, 0.7f, 0.08f), ("daub", Wattle.Daub(), 1.01f, 1.01f, 0.16f) })
{
    Check(m.Count > 100 && m.T.All(t => t.All(i => i >= 0 && i < m.Count) && t.Count % 3 == 0), $"Wattle {name}: a mesh with real corners");
    float mx = 0, my = 0, mz = 0; for (int i = 0; i < m.Count; i++) { mx = Math.Max(mx, Math.Abs(m.V[3 * i])); my = Math.Max(my, Math.Abs(m.V[3 * i + 1])); mz = Math.Max(mz, Math.Abs(m.V[3 * i + 2])); }
    Check(mx <= w && my <= h && mz <= d, $"Wattle {name}: within its piece ({mx:0.00}, {my:0.00}, {mz:0.00})");
}
{
    // (the clay runs into the timbers round each panel, inside their depth: flush on their faces, the sky showed between)
    var daub = Wattle.Daub(); var clay = daub.T[PartMesh.Main].Distinct().ToList();
    foreach (int side in new[] { -1, 1 })
    {
        var panel = clay.Where(i => Math.Sign(daub.V[3 * i]) == side).ToList();
        float inner = panel.Min(i => Math.Abs(daub.V[3 * i])), outer = panel.Max(i => Math.Abs(daub.V[3 * i])), low = panel.Min(i => daub.V[3 * i + 1]), high = panel.Max(i => daub.V[3 * i + 1]), deep = panel.Max(i => Math.Abs(daub.V[3 * i + 2]));
        Check(inner < 0.08f - 0.02f && outer > 0.84f + 0.02f && low < -0.84f - 0.02f && high > 0.86f + 0.02f && deep < 0.13f, $"Daub panel {side}: tucked into the post, the end post, the sill and the plate ({inner:0.00}-{outer:0.00} across, {low:0.00}-{high:0.00} up, {deep:0.00} deep)");
    }
}

Console.WriteLine($"BuildKit: {checks} checks passed");
