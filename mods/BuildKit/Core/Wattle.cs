using System;
using System.Collections.Generic;

namespace BuildKit
{
    // Wattle: withies woven between stakes, and wattle daubed with clay in a timber frame. Each in its game piece's frame (slots: Main =
    // stakes or clay, shaded per part by vertex colour; Boards = the game's own wood; Trim = withies or the frame's timbers).
    public static class Wattle
    {
        // ---- wattle: withies woven between stakes (a fence, 2 × 1 m), and wattle daubed with clay in a timber frame (a wall, 2 × 2 m) ----
        public static PartMesh Fence(int seed = 9)
        {
            var m = new PartMesh();
            var rng = new Random(seed);
            const int stakes = 7; float x0 = -0.9f, gap = 0.3f;
            for (int i = 0; i < stakes; i++)
            {
                float x = x0 + i * gap + (float)(rng.NextDouble() - 0.5) * 0.03f, top = 0.55f + 0.1f * (float)rng.NextDouble();
                Column(m, PartMesh.Main, x, 0, -0.5f, top, 0.035f, 0.03f, 7, 0.8f + 0.3f * (float)rng.NextDouble());
            }
            // (each withy in front of one stake and behind the next, the row above the other way round)
            for (int row = 0; row < 20; row++)
            {
                float y = -0.45f + row * 0.0475f + (float)(rng.NextDouble() - 0.5) * 0.006f, phase = row % 2 == 0 ? 0 : (float)Math.PI;
                var path = new List<(float x, float y, float z)>();
                for (int k = 0; k <= 40; k++)
                {
                    float x = -1f + k * 0.05f, t = (x - x0) / gap * (float)Math.PI + phase;
                    path.Add((x, y + 0.006f * (float)Math.Sin(k * 0.7 + row), 0.045f * (float)Math.Cos(t)));
                }
                float tone = 0.85f + 0.3f * (float)rng.NextDouble();
                TubeTone(m, PartMesh.Trim, path, 0.024f, 6, tone);
            }
            return m;
        }
        public static PartMesh Daub(int seed = 11)
        {
            var m = new PartMesh();
            var rng = new Random(seed);
            // The frame: sill, wall plate, the posts at each end and in the middle, and a brace.
            Box(m, PartMesh.Trim, -1, -1, -0.15f, 1, -0.84f, 0.15f, 0.8f);
            Box(m, PartMesh.Trim, -1, 0.86f, -0.14f, 1, 1, 0.14f, 0.85f);
            foreach (float x in new[] { -0.92f, 0f, 0.92f }) Box(m, PartMesh.Trim, x - 0.08f, -0.84f, -0.13f, x + 0.08f, 0.86f, 0.13f, 0.75f + 0.2f * (float)rng.NextDouble());
            // The daub between: clay, lumpy, a little proud of the timbers' middle and well short of their faces. Each panel runs 4 cm into
            // the timbers round it (hidden inside them): ending flush on their faces, the least shift in drawing showed the sky between.
            const float into = 0.04f;
            foreach (float cx in new[] { -0.46f, 0.46f })
                foreach (int face in new[] { -1, 1 })
                {
                    const int nx = 8, ny = 16; var grid = new int[nx + 1, ny + 1];
                    for (int i = 0; i <= nx; i++)
                        for (int j = 0; j <= ny; j++)
                        {
                            float x = cx - 0.38f - into + i * ((0.76f + 2 * into) / nx), y = -0.84f - into + j * ((1.7f + 2 * into) / ny);
                            bool edge = i == 0 || j == 0 || i == nx || j == ny;
                            float bump = edge ? 0 : 0.012f * (float)(Math.Sin(x * 23 + y * 7 + seed) + Math.Cos(y * 19 - x * 5)) / 2;
                            float tone = 0.9f + 0.12f * (float)Math.Sin(x * 11 + y * 13);
                            grid[i, j] = m.AddPoint(x, y, face * (0.09f + bump), 0, 0, face, x * 0.7f, y * 0.7f, tone, tone, tone);
                        }
                    for (int i = 0; i < nx; i++) for (int j = 0; j < ny; j++) m.QuadFacing(PartMesh.Main, grid[i, j], grid[i + 1, j], grid[i + 1, j + 1], grid[i, j + 1], 0, 0, face);
                }
            return m;
        }

        // ---- helpers ----
        private static void TubeTone(PartMesh m, int slot, List<(float x, float y, float z)> path, float r, int sides, float tone)
        {
            int first = m.Count;
            Tube(m, slot, path, r, sides, 1);
            for (int i = first; i < m.Count; i++) { m.C[3 * i] = tone; m.C[3 * i + 1] = tone * 0.97f; m.C[3 * i + 2] = tone * 0.9f; }
        }
        public static void Box(PartMesh m, int slot, float x0, float y0, float z0, float x1, float y1, float z1, float tone)
        {
            var faces = new (float nx, float ny, float nz)[] { (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1) };
            foreach (var f in faces)
            {
                var c = new (float x, float y, float z)[4];
                if (f.nx != 0) { float x = f.nx > 0 ? x1 : x0; c[0] = (x, y0, z0); c[1] = (x, y0, z1); c[2] = (x, y1, z1); c[3] = (x, y1, z0); }
                else if (f.ny != 0) { float y = f.ny > 0 ? y1 : y0; c[0] = (x0, y, z0); c[1] = (x1, y, z0); c[2] = (x1, y, z1); c[3] = (x0, y, z1); }
                else { float z = f.nz > 0 ? z1 : z0; c[0] = (x0, y0, z); c[1] = (x1, y0, z); c[2] = (x1, y1, z); c[3] = (x0, y1, z); }
                var k = new int[4];
                for (int i = 0; i < 4; i++) k[i] = m.AddPoint(c[i].x, c[i].y, c[i].z, f.nx, f.ny, f.nz, f.nx != 0 ? c[i].z * 2 : c[i].x * 2, c[i].y * 2, tone, tone, tone);
                m.QuadFacing(slot, k[0], k[1], k[2], k[3], f.nx, f.ny, f.nz);
            }
        }
        // An upright round column from y0 (radius r0) to y1 (radius r1), capped at the top.
        public static void Column(PartMesh m, int slot, float cx, float cz, float y0, float y1, float r0, float r1, int sides, float tone)
        {
            var lo = new int[sides]; var hi = new int[sides];
            for (int k = 0; k < sides; k++)
            {
                double th = 2 * Math.PI * k / sides; float c = (float)Math.Cos(th), s = (float)Math.Sin(th), slope = (r0 - r1) / Math.Max(0.01f, y1 - y0);
                lo[k] = m.AddPoint(cx + c * r0, y0, cz + s * r0, c, slope, s, (float)k / sides, 0, tone, tone, tone);
                hi[k] = m.AddPoint(cx + c * r1, y1, cz + s * r1, c, slope, s, (float)k / sides, 1, tone, tone, tone);
            }
            for (int k = 0; k < sides; k++) { int j = (k + 1) % sides; double th = 2 * Math.PI * (k + 0.5) / sides; m.QuadFacing(slot, lo[k], lo[j], hi[j], hi[k], (float)Math.Cos(th), 0, (float)Math.Sin(th)); }
            int top = m.AddPoint(cx, y1, cz, 0, 1, 0, 0.5f, 0.5f, tone, tone, tone);
            for (int k = 0; k < sides; k++) { int a = m.AddPoint(m.X(hi[k]), y1, m.Z(hi[k]), 0, 1, 0, 0, 0, tone, tone, tone), b = m.AddPoint(m.X(hi[(k + 1) % sides]), y1, m.Z(hi[(k + 1) % sides]), 0, 1, 0, 1, 0, tone, tone, tone); m.TriFacing(slot, top, a, b, 0, 1, 0); }
        }
        // A round tube along a path (its ends capped), seen from the given side (+1/-1 in z) at least; taper narrows it toward the end.
        public static void Tube(PartMesh m, int slot, List<(float x, float y, float z)> path, float r, int sides, int face, float taper = 1f)
        {
            var rings = new List<int[]>();
            for (int i = 0; i < path.Count; i++)
            {
                var a = path[Math.Max(0, i - 1)]; var b = path[Math.Min(path.Count - 1, i + 1)];
                float tx = b.x - a.x, ty = b.y - a.y, tz = b.z - a.z, tl = (float)Math.Sqrt(tx * tx + ty * ty + tz * tz); tx /= tl; ty /= tl; tz /= tl;
                // (two directions across it: one in the face's plane, one out of it)
                float ux = -ty, uy = tx, uz = 0; float ul = (float)Math.Sqrt(ux * ux + uy * uy); if (ul < 1e-4f) { ux = 1; uy = 0; ul = 1; } ux /= ul; uy /= ul;
                float wx = ty * uz - tz * uy, wy = tz * ux - tx * uz, wz = tx * uy - ty * ux;
                float rr = r * (1 - (1 - taper) * i / Math.Max(1, path.Count - 1));
                var ring = new int[sides];
                for (int k = 0; k < sides; k++)
                {
                    double th = 2 * Math.PI * k / sides; float c = (float)Math.Cos(th), s = (float)Math.Sin(th);
                    float nx = ux * c + wx * s, ny = uy * c + wy * s, nz = uz * c + wz * s;
                    ring[k] = m.AddPoint(path[i].x + nx * rr, path[i].y + ny * rr, path[i].z + nz * rr, nx, ny, nz, (float)k / sides, i * 0.2f, 1, 1, 1);
                }
                rings.Add(ring);
            }
            for (int i = 0; i + 1 < rings.Count; i++)
                for (int k = 0; k < sides; k++)
                {
                    int j = (k + 1) % sides;
                    m.QuadFacing(slot, rings[i][k], rings[i][j], rings[i + 1][j], rings[i + 1][k], m.Nx(rings[i][k]) + m.Nx(rings[i + 1][j]), m.Ny(rings[i][k]) + m.Ny(rings[i + 1][j]), m.Nz(rings[i][k]) + m.Nz(rings[i + 1][j]));
                }
            foreach (int end in new[] { 0, rings.Count - 1 })
            {
                var p = path[end]; var q = path[end == 0 ? 1 : end - 1];
                float nx = p.x - q.x, ny = p.y - q.y, nz = p.z - q.z;
                int c = m.AddPoint(p.x, p.y, p.z, nx, ny, nz, 0.5f, 0.5f, 1, 1, 1);
                for (int k = 0; k < sides; k++) m.TriFacing(slot, c, rings[end][k], rings[end][(k + 1) % sides], nx, ny, nz);
            }
        }
    }
}
