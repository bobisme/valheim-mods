using System;
using System.Collections.Generic;

namespace BuildKit
{
    // The game's roof shapes, as the turf laid on them follows them: each is a 2 × 2 m piece (x and z from -1 to 1, the low eave toward
    // +z), its height over the piece's origin where its rafters lie (where its snap points are).
    public enum Shape { Straight, Ridge, OuterCorner, InnerCorner }

    public static class Roofs
    {
        // Rise over the piece's two metres: 26° roofs climb a metre, 45° ones two.
        public static float Height(Shape s, bool steep, float x, float z)
        {
            float k = steep ? 1f : 0.5f;
            switch (s)
            {
                case Shape.Straight: return steep ? -z : k * (1 - z);           // (a 45° piece is centred on its origin: -1 at the eave, 1 at the top)
                case Shape.Ridge: return k * (1 - Math.Abs(z));
                case Shape.OuterCorner: return k * Math.Min(1 - x, 1 - z);       // a hip: the lower of the two slopes
                default: return k * Math.Max(1 - x, 1 - z);                      // a valley: the higher
            }
        }
    }

    // A turf roof's look, made from its shape: sod on a layer of birch bark over boards. Plain lists, turned into a mesh in the game.
    public sealed class TurfMesh
    {
        public const int Top = 0, Earth = 1, Bark = 2, Boards = 3, Grass = 4, Stick = 5, Slots = 6; // (material slots)
        public const float Step = 0.125f, Under = 0.03f, BarkTop = 0.06f, Thick = 0.22f, Lumps = 0.07f;
        public readonly List<float> V = new List<float>(), N = new List<float>(), Uv = new List<float>();
        public readonly List<int>[] T = new List<int>[Slots];
        public TurfMesh() { for (int i = 0; i < Slots; i++) T[i] = new List<int>(); }
        public int Count => V.Count / 3;

        // How thick the sod lies at a point: a little lumpy inside, even at the piece's edges (so neighbours meet without a step).
        public static float Sod(float x, float z, int seed)
        {
            float edge = Math.Min(1 - Math.Abs(x), 1 - Math.Abs(z));
            float taper = Math.Max(0f, Math.Min(1f, edge / 0.35f));
            taper = taper * taper * (3 - 2 * taper);
            double n = Math.Sin(x * 3.1 + seed * 1.7) * Math.Cos(z * 2.9 - seed * 0.9) + 0.5 * Math.Sin((x + z) * 5.3 + seed) + 0.35 * Math.Cos((x - z) * 9.1 - seed * 2.3);
            return Thick + Lumps * (float)n / 1.85f * taper;
        }

        // rough: the sides left open (bit 0: -z, 1: +x, 2: +z, 3: -x), whose edges are torn and lumpy; the others meet a neighbour, and
        // stay where the neighbour's sod meets them.
        public static TurfMesh Build(Shape s, bool steep, int seed = 7, int rough = 15)
        {
            var m = new TurfMesh();
            int cells = (int)Math.Round(2 / Step);
            // The sod's top, and the boards under it (seen from inside the house), on the same grid.
            int[,] top = new int[cells + 1, cells + 1], bottom = new int[cells + 1, cells + 1];
            for (int i = 0; i <= cells; i++)
                for (int j = 0; j <= cells; j++)
                {
                    float x = -1 + i * Step, z = -1 + j * Step, h = Roofs.Height(s, steep, x, z);
                    var (nx, ny, nz) = Normal(p => Roofs.Height(s, steep, p.x, p.z) + Sod(p.x, p.z, seed), x, z);
                    top[i, j] = m.Add(x, h + Sod(x, z, seed), z, nx, ny, nz, x * 0.9f, z * 0.9f);
                    var (bx, by, bz) = Normal(p => Roofs.Height(s, steep, p.x, p.z), x, z);
                    bottom[i, j] = m.Add(x, h - Under, z, -bx, -by, -bz, x * 0.5f, z * 0.5f);
                }
            // (each cell split along its x = z diagonal, the line hips and valleys run on, so they stay sharp)
            for (int i = 0; i < cells; i++)
                for (int j = 0; j < cells; j++)
                {
                    int a = top[i, j], b = top[i + 1, j], c = top[i + 1, j + 1], d = top[i, j + 1];
                    m.Quad(Top, a, b, c, d);
                    a = bottom[i, j]; b = bottom[i + 1, j]; c = bottom[i + 1, j + 1]; d = bottom[i, j + 1];
                    m.Quad(Boards, a, d, c, b); // (seen from below)
                }
            // Round the edge: a strip of bark over the boards, then the cut sod, crumbly and bulging (not cut clean).
            for (int side = 0; side < 4; side++) m.RoughEdge(s, steep, side, seed, (rough >> side & 1) != 0);
            m.Tufts(s, steep, seed);
            m.Sticks(s, steep, seed, rough);
            return m;
        }
        // Along each side, anticlockwise from above: -z, +x, +z, -x.
        private static (float x, float z) Edge(int side, float t) =>
            side == 0 ? (-1 + t, -1) : side == 1 ? (1, -1 + t) : side == 2 ? (1 - t, 1) : (-1, 1 - t);
        private static (float, float, float) Normal(Func<(float x, float z), float> h, float x, float z)
        {
            const float e = 0.02f;
            float dx = (h((x + e, z)) - h((x - e, z))) / (2 * e), dz = (h((x, z + e)) - h((x, z - e))) / (2 * e);
            float l = (float)Math.Sqrt(dx * dx + 1 + dz * dz);
            return (-dx / l, 1 / l, -dz / l);
        }
        private int Add(float x, float y, float z, float nx, float ny, float nz, float u, float v)
        {
            V.Add(x); V.Add(y); V.Add(z); N.Add(nx); N.Add(ny); N.Add(nz); Uv.Add(u); Uv.Add(v);
            return Count - 1;
        }
        // a b c d anticlockwise seen from the side that shows (Unity: clockwise is the front, so they're added reversed).
        private void Quad(int slot, int a, int b, int c, int d) => T[slot].AddRange(new[] { a, c, b, a, d, c });
        // A smooth wobble, the same for the same arguments: about -1 to 1.
        private static float Wobble(float t, float k, int seed) => (float)((Math.Sin(t * 9.7 + k * 2.1 + seed) + 0.6 * Math.Sin(t * 23.3 - k * 3.7 + seed * 1.3) + 0.35 * Math.Sin(t * 51.1 + k * 5.9 - seed * 0.7)) / 1.95);

        // An open side's torn edge: how far the sod reaches out past the piece's square (lumps of every size, 2 to 14 cm) and how far
        // its grass rounds down over the edge before the soil shows (2 to 7 cm); none at the corners, where the next side starts.
        public const float Reach = 0.12f, Droop = 0.045f;
        public static float Out(int side, float t, int seed)
        {
            float corner = Math.Min(1f, Math.Min(t, 2 - t) / 0.15f);
            float lump = Math.Max(0f, Wobble(t * 0.55f, 31 + side * 7, seed)) * 0.75f + 0.25f * (0.5f + 0.5f * Wobble(t * 1.7f, 37 + side, seed));
            return corner * (0.02f + Reach * lump);
        }
        public static float Drop(int side, float t, int seed) => Math.Min(1f, Math.Min(t, 2 - t) / 0.15f) * (0.025f + Droop * (0.5f + 0.5f * Wobble(t * 0.8f, 41 + side, seed)));

        // One side's edge, as a grid along it (every 6 cm) and up it: the bark strip, then rows of soil, then (on an open side) the
        // grass rounding over the lip. An open side's lip reaches out unevenly and droops; a side against a neighbour stays at the
        // piece's square (a gentle bulge, buried in the neighbour's sod).
        public const int SoilRows = 5; public const float Bulge = 0.12f, EdgeStep = 0.0625f;
        private void RoughEdge(Shape s, bool steep, int side, int seed, bool open)
        {
            float ox = side == 0 ? 0 : side == 1 ? 1 : side == 2 ? 0 : -1, oz = side == 0 ? -1 : side == 1 ? 0 : side == 2 ? 1 : 0; // (outward)
            int cols = (int)Math.Round(2 / EdgeStep), rows = SoilRows + 2 + (open ? 2 : 0); // (row 0: under the boards; 1: top of the bark; then soil; then the lip)
            var grid = new int[cols + 1, rows];
            var slot = new int[rows];
            for (int c = 0; c <= cols; c++)
            {
                float t = c * EdgeStep; var (x, z) = Edge(side, t);
                float h = Roofs.Height(s, steep, x, z), corner = Math.Min(1f, Math.Min(t, 2 - t) / 0.12f);
                float reach = open ? Out(side, t, seed) : 0, drop = open ? Drop(side, t, seed) : 0;
                for (int r = 0; r < rows; r++)
                {
                    float y, out_, nx = ox, ny = 0, nz = oz;
                    if (r == 0) { y = h - Under; out_ = 0; slot[r] = Bark; } // (on the boards' own edge: any further out and the sky shows through between them)
                    else if (r == 1) { y = h + BarkTop; out_ = 0.012f * (1 + Wobble(t, 1, seed)) * corner + 0.2f * reach; slot[r] = Earth; }
                    else if (r < SoilRows + 2)
                    {
                        int k = r - 1; float f = (float)k / SoilRows; // (0 at the bark, 1 at the top of the soil)
                        float soilTop = Thick - drop;
                        y = h + BarkTop + (soilTop - BarkTop) * f + (k < SoilRows ? 0.012f * Wobble(t, 10 + k, seed) : 0); // (less than half a row: rows never fold over each other)
                        if (open) out_ = reach * (0.35f + 0.65f * f) + 0.03f * (float)Math.Sin(Math.PI * f) * (0.5f + 0.5f * Wobble(t, 20 + k, seed)) * corner;
                        else out_ = (k == SoilRows ? 0 : Bulge * (float)Math.Sin(Math.PI * (0.1 + 0.9 * f)) * (0.45f + 0.55f * Wobble(t * 0.7f, 20 + k * 0.3f, seed))) * corner;
                        slot[r] = k == SoilRows && open ? Top : Earth;
                    }
                    else if (r == SoilRows + 2) { y = h + Thick - 0.55f * drop; out_ = 0.85f * reach; nx = ox * 0.8f; ny = 0.6f; nz = oz * 0.8f; slot[r] = Top; } // (the grass rounding over)
                    else { y = h + Thick; out_ = 0; nx = ox * 0.3f; ny = 0.95f; nz = oz * 0.3f; slot[r] = Top; } // (where it meets the top)
                    float u = t * 1.2f, v = y * 1.2f;
                    grid[c, r] = Add(x + ox * out_, y, z + oz * out_, nx, ny, nz, u, v);
                }
            }
            for (int c = 0; c < cols; c++)
                for (int r = 0; r < rows - 1; r++)
                {
                    // (facing out and, on the lip, up)
                    float up = r >= SoilRows + 1 && open ? 0.8f : 0;
                    QuadOut(r == 0 ? Bark : r >= SoilRows + 1 && open ? Top : Earth, grid[c, r], grid[c + 1, r], grid[c + 1, r + 1], grid[c, r + 1], ox, up, oz);
                }
        }
        // A quad facing the given way, whichever way round its corners were given (each half on its own: where an edge pinches to
        // nothing at a corner, a quad can be twisted).
        private void QuadOut(int slot, int a, int b, int c, int d, float nx, float ny, float nz) { TriOut(slot, a, b, c, nx, ny, nz); TriOut(slot, a, c, d, nx, ny, nz); }
        private void TriOut(int slot, int a, int b, int c, float nx, float ny, float nz)
        {
            float ux = V[3 * b] - V[3 * a], uy = V[3 * b + 1] - V[3 * a + 1], uz = V[3 * b + 2] - V[3 * a + 2];
            float vx = V[3 * c] - V[3 * a], vy = V[3 * c + 1] - V[3 * a + 1], vz = V[3 * c + 2] - V[3 * a + 2];
            float cx = uy * vz - uz * vy, cy = uz * vx - ux * vz, cz = ux * vy - uy * vx; // (Cross(b - a, c - a): the front of a, b, c)
            if (cx * nx + cy * ny + cz * nz >= 0) T[slot].AddRange(new[] { a, b, c }); else T[slot].AddRange(new[] { a, c, b });
        }

        // Sticks in the sod: birch branches laid in under it, here and there showing through the grass (one end buried, the other
        // lifting out), and broken ends poking out of the cut edges.
        public const int StickCount = 9, EdgeSticks = 3;
        private void Sticks(Shape s, bool steep, int seed, int rough)
        {
            var rng = new Random(seed * 7727 + (int)s * 13 + (steep ? 5 : 0));
            float R(float a, float b) => a + (b - a) * (float)rng.NextDouble();
            float TopAt(float x, float z) => Roofs.Height(s, steep, x, z) + Sod(x, z, seed);
            for (int n = 0; n < StickCount; n++)
            {
                float cx = R(-0.8f, 0.8f), cz = R(-0.8f, 0.8f), len = R(0.25f, 0.6f), ang = R(0, (float)Math.PI * 2), rad = R(0.011f, 0.022f);
                float dx = (float)Math.Cos(ang) * len / 2, dz = (float)Math.Sin(ang) * len / 2;
                float ax = Clamp(cx - dx), az = Clamp(cz - dz), bx = Clamp(cx + dx), bz = Clamp(cz + dz);
                Prism((ax, TopAt(ax, az) - R(0.03f, 0.06f), az), (bx, TopAt(bx, bz) + R(-0.01f, Math.Min(0.04f, len * 0.1f)), bz), rad, rng); // (lifting out low, not standing up)
            }
            for (int side = 0; side < 4; side++)
                for (int n = 0; n < EdgeSticks; n++)
                {
                    if ((rough >> side & 1) == 0) { R(0, 1); R(0, 1); R(0, 1); R(0, 1); R(0, 1); R(0, 1); R(0, 1); R(0, 1); continue; } // (against a neighbour: none, the same draws used)
                    float ox = side == 0 ? 0 : side == 1 ? 1 : side == 2 ? 0 : -1, oz = side == 0 ? -1 : side == 1 ? 0 : side == 2 ? 1 : 0;
                    float at = R(0.2f, 1.8f); var (x, z) = Edge(side, at);
                    float h = Roofs.Height(s, steep, x, z), y = h + R(BarkTop + 0.03f, Thick - Drop(side, at, seed) - 0.03f), poke = Out(side, at, seed) + R(0.04f, 0.1f), slant = R(-0.04f, 0.04f);
                    // (along the edge a little too, so they don't all point straight out)
                    float ex = oz * R(-0.08f, 0.08f), ez = -ox * R(-0.08f, 0.08f);
                    Prism((x - ox * 0.2f, y - slant, z - oz * 0.2f), (x + ox * poke + ex, y + slant, z + oz * poke + ez), R(0.01f, 0.02f), rng);
                }
        }
        private static float Clamp(float v) => Math.Max(-0.92f, Math.Min(0.92f, v));
        // A five-sided stick from a to b, its far end capped (the near one is buried).
        private void Prism((float x, float y, float z) a, (float x, float y, float z) b, float r, Random rng)
        {
            float ax = b.x - a.x, ay = b.y - a.y, az = b.z - a.z, al = (float)Math.Sqrt(ax * ax + ay * ay + az * az);
            ax /= al; ay /= al; az /= al;
            // (two directions across the stick)
            float px = Math.Abs(ay) < 0.9f ? 0 : 1, py = Math.Abs(ay) < 0.9f ? 1 : 0, pz = 0;
            float ux = ay * pz - az * py, uy = az * px - ax * pz, uz = ax * py - ay * px; float ul = (float)Math.Sqrt(ux * ux + uy * uy + uz * uz); ux /= ul; uy /= ul; uz /= ul;
            float wx = ay * uz - az * uy, wy = az * ux - ax * uz, wz = ax * uy - ay * ux;
            const int sides = 5; float turn = (float)(rng.NextDouble() * Math.PI);
            var ring0 = new int[sides]; var ring1 = new int[sides];
            for (int i = 0; i < sides; i++)
            {
                double th = turn + i * 2 * Math.PI / sides; float c = (float)Math.Cos(th), sn = (float)Math.Sin(th);
                float nx = ux * c + wx * sn, ny = uy * c + wy * sn, nz = uz * c + wz * sn;
                float taper = 0.75f; // (thinner toward the far end)
                ring0[i] = Add(a.x + nx * r, a.y + ny * r, a.z + nz * r, nx, ny, nz, i / (float)sides, 0);
                ring1[i] = Add(b.x + nx * r * taper, b.y + ny * r * taper, b.z + nz * r * taper, nx, ny, nz, i / (float)sides, al * 2);
            }
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides;
                float mx = (V[3 * ring0[i]] + V[3 * ring0[j]]) / 2 - a.x, my = (V[3 * ring0[i] + 1] + V[3 * ring0[j] + 1]) / 2 - a.y, mz = (V[3 * ring0[i] + 2] + V[3 * ring0[j] + 2]) / 2 - a.z;
                QuadOut(Stick, ring0[i], ring0[j], ring1[j], ring1[i], mx, my, mz);
            }
            int cap = Add(b.x, b.y, b.z, ax, ay, az, 0.5f, 0.5f);
            for (int i = 0; i < sides; i++)
            {
                int j = (i + 1) % sides, p = Add(V[3 * ring1[i]], V[3 * ring1[i] + 1], V[3 * ring1[i] + 2], ax, ay, az, 0, 0), q = Add(V[3 * ring1[j]], V[3 * ring1[j] + 1], V[3 * ring1[j] + 2], ax, ay, az, 1, 0);
                float cx2 = 0, cy2 = 0, cz2 = 0;
                { float ux2 = V[3 * p] - V[3 * cap], uy2 = V[3 * p + 1] - V[3 * cap + 1], uz2 = V[3 * p + 2] - V[3 * cap + 2], vx2 = V[3 * q] - V[3 * cap], vy2 = V[3 * q + 1] - V[3 * cap + 1], vz2 = V[3 * q + 2] - V[3 * cap + 2];
                  cx2 = uy2 * vz2 - uz2 * vy2; cy2 = uz2 * vx2 - ux2 * vz2; cz2 = ux2 * vy2 - uy2 * vx2; }
                if (cx2 * ax + cy2 * ay + cz2 * az >= 0) T[Stick].AddRange(new[] { cap, p, q }); else T[Stick].AddRange(new[] { cap, q, p });
            }
        }

        // Tufts of grass: two crossed cards each, upright, rooted a little into the sod, kept off the edges.
        public const int TuftCount = 34;
        public const float TuftWide = 0.4f, TuftTall = 0.32f;
        private void Tufts(Shape s, bool steep, int seed)
        {
            var rng = new Random(seed * 7919 + (int)s * 31 + (steep ? 1 : 0));
            for (int n = 0; n < TuftCount; n++)
            {
                float x = (float)(rng.NextDouble() * 1.6 - 0.8), z = (float)(rng.NextDouble() * 1.6 - 0.8);
                float y = Roofs.Height(s, steep, x, z) + Sod(x, z, seed) - 0.04f;
                float tall = TuftTall * (0.7f + 0.6f * (float)rng.NextDouble()), wide = TuftWide * (0.8f + 0.4f * (float)rng.NextDouble());
                double turn = rng.NextDouble() * Math.PI;
                for (int card = 0; card < 2; card++)
                {
                    double a = turn + card * Math.PI / 2;
                    float dx = (float)Math.Cos(a) * wide / 2, dz = (float)Math.Sin(a) * wide / 2;
                    // (both sides drawn: one card faces each way, normals up so they light like the sod)
                    int p = Add(x - dx, y, z - dz, 0, 1, 0, 0, 0), q = Add(x + dx, y, z + dz, 0, 1, 0, 1, 0);
                    int r = Add(x + dx, y + tall, z + dz, 0, 1, 0, 1, 1), t = Add(x - dx, y + tall, z - dz, 0, 1, 0, 0, 1);
                    T[Grass].AddRange(new[] { p, r, q, p, t, r, p, q, r, p, r, t });
                }
            }
        }
    }
}
