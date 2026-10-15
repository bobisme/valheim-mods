using System;
using System.Collections.Generic;

namespace BuildKit
{
    // A shingled roof's look, made from its shape: rows of tarred wooden shingles with rounded tongues (a stave church's dragon scales),
    // each row half a shingle along from the one below and lapping over its heads, on boards; a ridge capped with two boards, a hip
    // with a board along it, a valley lined with one. Plain lists, turned into a mesh in the game.
    public sealed class ShingleMesh
    {
        public const int Shingles = 0, Boards = 1, Trim = 2, Slots = 3; // (material slots)
        public const float Wide = 0.2f, Expose = 0.17f, Length = 0.42f, Gap = 0.012f, Base = 0.03f, Lift = 0.028f, Thick = 0.018f, Under = 0.03f;
        public readonly List<float> V = new List<float>(), N = new List<float>(), Uv = new List<float>(), C = new List<float>();
        public readonly List<int>[] T = new List<int>[Slots];
        public ShingleMesh() { for (int i = 0; i < Slots; i++) T[i] = new List<int>(); }
        public int Count => V.Count / 3;

        // A face of the roof: where it is on the piece, and which way is downhill on it.
        private sealed class Facet { public Func<float, float, bool> In; public float Dx, Dz; public Func<float, float, float> Inside = (x, z) => 9; } // (Inside: how far in from a hip or valley line)
        private static List<Facet> Facets(Shape s)
        {
            switch (s)
            {
                case Shape.Straight: return new List<Facet> { new Facet { In = (x, z) => true, Dx = 0, Dz = 1 } };
                case Shape.Ridge: return new List<Facet> { new Facet { In = (x, z) => z >= 0, Dx = 0, Dz = 1 }, new Facet { In = (x, z) => z < 0, Dx = 0, Dz = -1 } };
                case Shape.OuterCorner: return new List<Facet> { new Facet { In = (x, z) => x >= z, Dx = 1, Dz = 0, Inside = (x, z) => (x - z) * 0.7071f }, new Facet { In = (x, z) => z > x, Dx = 0, Dz = 1, Inside = (x, z) => (z - x) * 0.7071f } };
                default: return new List<Facet> { new Facet { In = (x, z) => x <= z, Dx = 1, Dz = 0, Inside = (x, z) => (z - x) * 0.7071f }, new Facet { In = (x, z) => z < x, Dx = 0, Dz = 1, Inside = (x, z) => (x - z) * 0.7071f } };
            }
        }

        // dragon: a carved dragon's head rising from the +x end (a ridge's gable end).
        public static ShingleMesh Build(Shape s, bool steep, int seed = 3, bool dragon = false)
        {
            var m = new ShingleMesh();
            m.Underside(s, steep);
            var rng = new Random(seed * 131 + (int)s * 17 + (steep ? 7 : 0));
            foreach (Facet f in Facets(s)) m.Rows(s, steep, f, rng);
            m.Caps(s, steep);
            if (dragon) Dragon.Add(m, Roofs.Height(s, steep, 0, 0) + Base + Lift + Thick + CapThick, Shingles);
            return m;
        }
        // (for the carvings: a point, its normal's parts, and faces shown toward a way)
        public int AddPoint(float x, float y, float z, float nx, float ny, float nz, float u, float v, float r, float g, float b) => Add((x, y, z), nx, ny, nz, u, v, r, g, b);
        public float Nx(int i) => N[3 * i]; public float Ny(int i) => N[3 * i + 1]; public float Nz(int i) => N[3 * i + 2];
        public float X(int i) => V[3 * i]; public float Y(int i) => V[3 * i + 1]; public float Z(int i) => V[3 * i + 2];
        public void QuadFacing(int slot, int a, int b, int c, int d, float nx, float ny, float nz) => QuadOut(slot, a, b, c, d, nx, ny, nz);
        public void TriFacing(int slot, int a, int b, int c, float nx, float ny, float nz) => TriOut(slot, a, b, c, nx, ny, nz);

        // ---- the shingles ----
        // Rows across the facet, from the eave up: each shingle a tongue, its butt (the rounded end, downhill) lifted off the boards by
        // the row under it, its head tucked under the row above.
        private void Rows(Shape s, bool steep, Facet f, Random rng)
        {
            float ax = -f.Dz, az = f.Dx; // (across the slope)
            // (on the facet's own grid: downhill distance from the piece's far side, across from its middle)
            for (int row = 0; row * Expose < 2.4f; row++)
            {
                float down = 1.02f - row * Expose; // (the butt's line, eave first: just past the piece's edge, lapping onto the next piece's)
                float shift = (row % 2 == 1 ? Wide / 2 : 0) + (float)(rng.NextDouble() - 0.5) * 0.02f;
                for (int col = -7; col <= 7; col++)
                {
                    float across = col * Wide + shift;
                    float cx = f.Dx * (down - Length / 2) + ax * across, cz = f.Dz * (down - Length / 2) + az * across; // (the shingle's middle)
                    if (Math.Abs(cx) > 1.02f || Math.Abs(cz) > 1.02f || !f.In(cx, cz)) continue;
                    // (a shingle sticking out past the piece's sides is trimmed off there: the next piece lays its own)
                    float bx = f.Dx * down + ax * across, bz = f.Dz * down + az * across;
                    if (Math.Abs(bx) > 1.06f || Math.Abs(bz) > 1.06f) continue;
                    // (none reaching more than a little over a hip or valley line: they'd stand up through its boards, which cover the cut)
                    bool over = false;
                    foreach (float sa in new[] { -Wide / 2, Wide / 2 }) foreach (float sl in new[] { 0f, Length })
                    {
                        float px = f.Dx * (down - Length + sl) + ax * (across + sa), pz = f.Dz * (down - Length + sl) + az * (across + sa);
                        if (f.Inside(px, pz) < -0.03f) over = true;
                    }
                    if (over) continue;
                    Tongue(s, steep, f, down, across, rng);
                }
            }
        }
        // One shingle: its outline (a strip, the butt end rounded in five steps) laid on the roof, lifted toward the butt, with a
        // butt edge and side edges; its colour a little different from its neighbours'.
        private void Tongue(Shape s, bool steep, Facet f, float down, float across, Random rng)
        {
            float ax = -f.Dz, az = f.Dx, w = (Wide - Gap) / 2;
            float tone = 0.86f + 0.22f * (float)rng.NextDouble(), warm = (float)(rng.NextDouble() - 0.5) * 0.08f;
            float r = tone + warm, g = tone, b = tone - warm;
            float lift = Lift * (0.85f + 0.3f * (float)rng.NextDouble());
            // The outline, from the head's left corner round the butt to the head's right: (across, along) with along = 0 at the head.
            var outline = new List<(float a, float l)> { (-w, 0) };
            float straight = Length - w;
            outline.Add((-w, straight));
            for (int k = 1; k < 6; k++) { double th = Math.PI * k / 6; outline.Add((-w * (float)Math.Cos(th), straight + w * 0.9f * (float)Math.Sin(th))); }
            outline.Add((w, straight)); outline.Add((w, 0));
            (float x, float y, float z) At(float a, float l, float up)
            {
                float d = Math.Max(-1.25f, down - Length + l); // (downhill distance: the butt at `down`; a head runs on a little past the piece's top, under the next piece's first row, as shingles lap)
                float x = f.Dx * d + ax * (across + a), z = f.Dz * d + az * (across + a);
                float h = Roofs.Height(s, steep, x, z); // (past the piece's edge too: the slope goes on there, under the next piece)
                return (x, h + Base + lift * (l / Length) + up, z);
            }
            var (nx, ny, nz) = Up(s, steep, f);
            // Top: a fan from the middle of the head.
            int centre = Add(At(0, Length * 0.45f, Thick), nx, ny, nz, 0.5f, 0.45f, r, g, b);
            var ring = new int[outline.Count];
            for (int i = 0; i < outline.Count; i++) ring[i] = Add(At(outline[i].a, outline[i].l, Thick), nx, ny, nz, outline[i].a / Wide + 0.5f, outline[i].l / Length, r, g, b);
            for (int i = 0; i + 1 < outline.Count; i++) TriOut(Shingles, centre, ring[i], ring[i + 1], nx, ny, nz);
            TriOut(Shingles, centre, ring[outline.Count - 1], ring[0], nx, ny, nz);
            // Edges: down from the top outline to the underside, all round but the head (under the next row).
            for (int i = 0; i + 1 < outline.Count; i++)
            {
                var p = outline[i]; var q = outline[i + 1];
                var pt = At(p.a, p.l, Thick); var qt = At(q.a, q.l, Thick); var pb = At(p.a, p.l, 0); var qb = At(q.a, q.l, 0);
                float ex = (pt.x + qt.x) / 2 - (f.Dx * (down - Length / 2) + ax * across), ez = (pt.z + qt.z) / 2 - (f.Dz * (down - Length / 2) + az * across); // (outward from the shingle's middle)
                float el = (float)Math.Sqrt(ex * ex + ez * ez); if (el > 0) { ex /= el; ez /= el; }
                float dr = r * 0.8f, dg = g * 0.8f, db = b * 0.8f; // (end grain, a shade darker)
                int a0 = Add(pb, ex, 0, ez, 0, 0, dr, dg, db), a1 = Add(qb, ex, 0, ez, 1, 0, dr, dg, db), a2 = Add(qt, ex, 0, ez, 1, 0.1f, dr, dg, db), a3 = Add(pt, ex, 0, ez, 0, 0.1f, dr, dg, db);
                QuadOut(Shingles, a0, a1, a2, a3, ex, 0.2f, ez);
            }
        }
        private static (float, float, float) Up(Shape s, bool steep, Facet f)
        {
            float k = steep ? 1f : 0.5f, l = (float)Math.Sqrt(k * k + 1); // (each facet falls k per metre downhill)
            return (f.Dx * k / l, 1 / l, f.Dz * k / l);
        }

        // ---- the boards under them, seen from inside, and round the edge ----
        public const float Step = 0.125f;
        private void Underside(Shape s, bool steep)
        {
            int cells = (int)Math.Round(2 / Step);
            var grid = new int[cells + 1, cells + 1];
            for (int i = 0; i <= cells; i++)
                for (int j = 0; j <= cells; j++)
                {
                    float x = -1 + i * Step, z = -1 + j * Step;
                    var (nx, ny, nz) = Normal(s, steep, x, z);
                    grid[i, j] = Add((x, Roofs.Height(s, steep, x, z) - Under, z), -nx, -ny, -nz, x * 0.5f, z * 0.5f, 1, 1, 1);
                }
            for (int i = 0; i < cells; i++)
                for (int j = 0; j < cells; j++)
                    T[Boards].AddRange(new[] { grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], grid[i, j], grid[i + 1, j + 1], grid[i, j + 1] }); // (seen from below)
            // (and their top, dark under the shingles: wherever a shingle's left out, at a hip or valley, boards show, not the sky)
            var over = new int[cells + 1, cells + 1];
            for (int i = 0; i <= cells; i++)
                for (int j = 0; j <= cells; j++)
                {
                    float x = -1 + i * Step, z = -1 + j * Step;
                    var (nx, ny, nz) = Normal(s, steep, x, z);
                    over[i, j] = Add((x, Roofs.Height(s, steep, x, z) + Base * 0.5f, z), nx, ny, nz, x * 0.5f, z * 0.5f, 0.45f, 0.4f, 0.36f);
                }
            for (int i = 0; i < cells; i++)
                for (int j = 0; j < cells; j++)
                {
                    var (nx, ny, nz) = Normal(s, steep, -1 + (i + 0.5f) * Step, -1 + (j + 0.5f) * Step);
                    QuadOut(Boards, over[i, j], over[i + 1, j], over[i + 1, j + 1], over[i, j + 1], nx, ny, nz);
                }
            // (and a barge board's thickness up the gable ends of a straight piece or a ridge, up to the shingles: the edges up and
            // down the slope meet the next piece's, where a strip would show as a line across the roof)
            for (int side = 0; side < 4; side++)
            {
                if (side == 0 || side == 2 || s == Shape.OuterCorner || s == Shape.InnerCorner) continue;
                float ox = side == 1 ? 1 : side == 3 ? -1 : 0, oz = side == 0 ? -1 : side == 2 ? 1 : 0;
                for (int k = 0; k < cells; k++)
                {
                    float t0 = -1 + k * Step, t1 = t0 + Step;
                    (float x, float z) p = side == 0 || side == 2 ? (t0, oz) : (ox, t0), q = side == 0 || side == 2 ? (t1, oz) : (ox, t1);
                    float hp = Roofs.Height(s, steep, p.x, p.z), hq = Roofs.Height(s, steep, q.x, q.z);
                    int a = Add((p.x, hp - Under, p.z), ox, 0, oz, t0, 0, 0.8f, 0.8f, 0.8f), b = Add((q.x, hq - Under, q.z), ox, 0, oz, t1, 0, 0.8f, 0.8f, 0.8f);
                    int c = Add((q.x, hq + Base, q.z), ox, 0, oz, t1, 0.06f, 0.8f, 0.8f, 0.8f), d = Add((p.x, hp + Base, p.z), ox, 0, oz, t0, 0.06f, 0.8f, 0.8f, 0.8f);
                    QuadOut(Trim, a, b, c, d, ox, 0, oz);
                }
            }
        }
        private static (float, float, float) Normal(Shape s, bool steep, float x, float z)
        {
            const float e = 0.02f;
            float dx = (Roofs.Height(s, steep, x + e, z) - Roofs.Height(s, steep, x - e, z)) / (2 * e), dz = (Roofs.Height(s, steep, x, z + e) - Roofs.Height(s, steep, x, z - e)) / (2 * e);
            float l = (float)Math.Sqrt(dx * dx + 1 + dz * dz);
            return (-dx / l, 1 / l, -dz / l);
        }

        // ---- ridge, hip and valley ----
        public const float CapWide = 0.15f, CapThick = 0.035f;
        private void Caps(Shape s, bool steep)
        {
            float k = steep ? 1f : 0.5f, top = Base + Lift + Thick + 0.005f;
            switch (s)
            {
                case Shape.Ridge:
                    // (two boards along the ridge, one down each side, meeting over it)
                    foreach (int side in new[] { -1, 1 }) Board((-1.02f, Roofs.Height(s, steep, 0, 0) + top + CapThick, 0), (1.02f, Roofs.Height(s, steep, 0, 0) + top + CapThick, 0), 0, side * CapWide, -k * CapWide, top, s, steep, true);
                    break;
                case Shape.OuterCorner:
                    // (two boards down the hip, from the top corner to the eave corner, one along each face, meeting over it)
                    foreach (int side in new[] { -1, 1 }) Board((-1, Roofs.Height(s, steep, -1, -1) + top + CapThick, -1), (1.02f, Roofs.Height(s, steep, 1, 1) + top + CapThick, 1.02f), side * CapWide, -side * CapWide, -k * CapWide, top, s, steep, true);
                    break;
                case Shape.InnerCorner:
                    // (one board down the valley over where the two faces' shingles meet, wide enough to cover their cut ends)
                    foreach (int side in new[] { -1, 1 }) Board((-1, Roofs.Height(s, steep, -1, -1) + top + 0.01f, -1), (1.02f, Roofs.Height(s, steep, 1, 1) + top + 0.01f, 1.02f), side * CapWide, -side * CapWide, k * CapWide, top, s, steep, true); // (one up each face, from the valley line)
                    break;
            }
        }
        // A trim board from a to b, wide toward (wx, wy, wz): all of it that way (one of a pair meeting along a ridge, hip or valley), or half each way.
        private void Board((float x, float y, float z) a, (float x, float y, float z) b, float wx, float wz, float wy, float lift, Shape s, bool steep, bool oneWay)
        {
            float hx = oneWay ? wx : wx / 2, hz = oneWay ? wz : wz / 2, hy = oneWay ? wy : wy / 2;
            var c0 = oneWay ? a : (a.x - hx, a.y - hy, a.z - hz); var c1 = oneWay ? b : (b.x - hx, b.y - hy, b.z - hz);
            var c2 = (b.x + hx, b.y + hy, b.z + hz); var c3 = (a.x + hx, a.y + hy, a.z + hz);
            // (its upward face, and its thickness down to the shingles)
            float ux = c1.Item1 - c0.Item1, uy = c1.Item2 - c0.Item2, uz = c1.Item3 - c0.Item3, vx = c3.Item1 - c0.Item1, vy = c3.Item2 - c0.Item2, vz = c3.Item3 - c0.Item3;
            float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx; float nl = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
            nx /= nl; ny /= nl; nz /= nl; if (ny < 0) { nx = -nx; ny = -ny; nz = -nz; }
            var corners = new[] { c0, c1, c2, c3 };
            var topI = new int[4]; var botI = new int[4];
            for (int i = 0; i < 4; i++)
            {
                var c = corners[i];
                topI[i] = Add(c, nx, ny, nz, i == 1 || i == 2 ? 2 : 0, i >= 2 ? 0.3f : 0, 0.9f, 0.9f, 0.9f);
                botI[i] = Add((c.Item1 - nx * CapThick, c.Item2 - ny * CapThick, c.Item3 - nz * CapThick), -nx, -ny, -nz, i == 1 || i == 2 ? 2 : 0, i >= 2 ? 0.3f : 0, 0.9f, 0.9f, 0.9f);
            }
            QuadOut(Trim, topI[0], topI[1], topI[2], topI[3], nx, ny, nz);
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                float mx = (corners[i].Item1 + corners[j].Item1) / 2 - (c0.Item1 + c2.Item1) / 2, my = (corners[i].Item2 + corners[j].Item2) / 2 - (c0.Item2 + c2.Item2) / 2, mz = (corners[i].Item3 + corners[j].Item3) / 2 - (c0.Item3 + c2.Item3) / 2;
                QuadOut(Trim, topI[i], topI[j], botI[j], botI[i], mx, my, mz);
            }
        }

        private int Add((float x, float y, float z) p, float nx, float ny, float nz, float u, float v, float r, float g, float b)
        {
            V.Add(p.x); V.Add(p.y); V.Add(p.z); N.Add(nx); N.Add(ny); N.Add(nz); Uv.Add(u); Uv.Add(v); C.Add(r); C.Add(g); C.Add(b);
            return Count - 1;
        }
        private void QuadOut(int slot, int a, int b, int c, int d, float nx, float ny, float nz) { TriOut(slot, a, b, c, nx, ny, nz); TriOut(slot, a, c, d, nx, ny, nz); }
        private void TriOut(int slot, int a, int b, int c, float nx, float ny, float nz)
        {
            float ux = V[3 * b] - V[3 * a], uy = V[3 * b + 1] - V[3 * a + 1], uz = V[3 * b + 2] - V[3 * a + 2];
            float vx = V[3 * c] - V[3 * a], vy = V[3 * c + 1] - V[3 * a + 1], vz = V[3 * c + 2] - V[3 * a + 2];
            float cx = uy * vz - uz * vy, cy = uz * vx - ux * vz, cz = ux * vy - uy * vx;
            if (cx * nx + cy * ny + cz * nz >= 0) T[slot].AddRange(new[] { a, b, c }); else T[slot].AddRange(new[] { a, c, b }); // (as the turf's, which shows)
        }
    }
}
