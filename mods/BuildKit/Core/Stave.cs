using System;
using System.Collections.Generic;

namespace BuildKit
{
    // A stave church's walls, posts and portal, in the shingles' materials (slots: Shingles = tarred wood, shaded per piece by vertex
    // colour; Boards = the game's own wood; Trim = the lighter wood of carvings). Each in its game piece's frame: a wall 2 × 2 m, its
    // faces at z = ±0.15; a post 2 m tall about its middle; a door's frame round a 1.4 × 1.9 m opening.
    public static class Stave
    {
        // ---- a stave wall: upright planks between a sill and a wall plate ----
        public const int Planks = 8;
        public static ShingleMesh Wall(int seed = 5)
        {
            var m = new ShingleMesh();
            var rng = new Random(seed);
            // (a dark backing, so the gaps between staves show wood, not daylight)
            Box(m, ShingleMesh.Shingles, -1, -0.84f, -0.05f, 1, 0.86f, 0.05f, 0.45f);
            float w = 2f / Planks;
            for (int i = 0; i < Planks; i++)
            {
                float x0 = -1 + i * w + 0.007f, x1 = x0 + w - 0.014f, tone = 0.85f + 0.25f * (float)rng.NextDouble();
                Plank(m, x0, x1, -0.84f, 0.86f, 0.115f, tone);
            }
            Box(m, ShingleMesh.Shingles, -1, -1, -0.15f, 1, -0.84f, 0.15f, 0.75f);   // the sill, the staves standing in it
            Box(m, ShingleMesh.Shingles, -1, 0.86f, -0.14f, 1, 1, 0.14f, 0.8f);      // the wall plate on top
            return m;
        }
        // One stave: a plank whose faces swell a little in the middle (an adzed board), both sides.
        private static void Plank(ShingleMesh m, float x0, float x1, float y0, float y1, float half, float tone)
        {
            float xm = (x0 + x1) / 2, bulge = 0.022f;
            foreach (int side in new[] { -1, 1 })
            {
                float z = side * half, zm = side * (half + bulge);
                // (two faces meeting in a ridge down the middle)
                int a = m.AddPoint(x0, y0, z, -0.3f, 0, side, 0, 0, tone, tone, tone), b = m.AddPoint(xm, y0, zm, 0, 0, side, 0.5f, 0, tone, tone, tone);
                int c = m.AddPoint(xm, y1, zm, 0, 0, side, 0.5f, 3, tone, tone, tone), d = m.AddPoint(x0, y1, z, -0.3f, 0, side, 0, 3, tone, tone, tone);
                m.QuadFacing(ShingleMesh.Shingles, a, b, c, d, -0.2f, 0, side);
                a = m.AddPoint(xm, y0, zm, 0, 0, side, 0.5f, 0, tone, tone, tone); b = m.AddPoint(x1, y0, z, 0.3f, 0, side, 1, 0, tone, tone, tone);
                c = m.AddPoint(x1, y1, z, 0.3f, 0, side, 1, 3, tone, tone, tone); d = m.AddPoint(xm, y1, zm, 0, 0, side, 0.5f, 3, tone, tone, tone);
                m.QuadFacing(ShingleMesh.Shingles, a, b, c, d, 0.2f, 0, side);
            }
            // (its edges, seen in the gaps)
            Box(m, ShingleMesh.Shingles, x0, y0, -half, x0 + 0.004f, y1, half, tone * 0.7f);
            Box(m, ShingleMesh.Shingles, x1 - 0.004f, y0, -half, x1, y1, half, tone * 0.7f);
        }

        // ---- a stave post: a round column, a base ring, a carved band and a cushion capital ----
        public static ShingleMesh Post()
        {
            var m = new ShingleMesh();
            Column(m, ShingleMesh.Shingles, 0, 0, -0.92f, 0.82f, 0.15f, 0.14f, 14, 0.9f);
            Column(m, ShingleMesh.Trim, 0, 0, -1f, -0.9f, 0.19f, 0.18f, 14, 1f);     // base
            Column(m, ShingleMesh.Trim, 0, 0, -0.9f, -0.86f, 0.175f, 0.16f, 14, 1f);
            Column(m, ShingleMesh.Trim, 0, 0, 0.5f, 0.56f, 0.165f, 0.165f, 14, 1f);  // a carved band
            Column(m, ShingleMesh.Trim, 0, 0, 0.6f, 0.64f, 0.16f, 0.16f, 14, 1f);
            Column(m, ShingleMesh.Trim, 0, 0, 0.82f, 0.9f, 0.15f, 0.2f, 14, 1f);     // the capital, swelling out
            Column(m, ShingleMesh.Trim, 0, 0, 0.9f, 1f, 0.2f, 0.2f, 14, 1f);
            return m;
        }

        // ---- a carved portal: the jambs and an arch over them, round the game's own door ----
        public const float JambIn = 0.7f, JambOut = 1f, JambDeep = 0.22f;
        public static ShingleMesh Portal()
        {
            var m = new ShingleMesh();
            foreach (int side in new[] { -1, 1 })
            {
                float x0 = side < 0 ? -JambOut : JambIn, x1 = side < 0 ? -JambIn : JambOut, xc = (x0 + x1) / 2;
                Box(m, ShingleMesh.Shingles, x0, -1, -JambDeep, x1, 1, JambDeep, 0.85f);
                // On each face, two vines winding up round each other, over and under (the "Urnes" style's tendrils, much simplified).
                foreach (int face in new[] { -1, 1 })
                    for (int strand = 0; strand < 2; strand++)
                    {
                        var path = new List<(float x, float y, float z)>();
                        for (int k = 0; k <= 40; k++)
                        {
                            float y = -0.92f + k * (1.78f / 40), ph = (float)(y * 9.5 + strand * Math.PI);
                            path.Add((xc + 0.095f * (float)Math.Sin(ph), y, face * (JambDeep + 0.018f + 0.012f * (float)Math.Cos(ph))));
                        }
                        Tube(m, ShingleMesh.Trim, path, 0.02f, 6, face);
                    }
                // (a capital at the top of each jamb, and a beast's head on it looking in at whoever comes through)
                Box(m, ShingleMesh.Trim, side < 0 ? x0 - 0.03f : x0, 0.86f, -JambDeep - 0.03f, side < 0 ? x1 : x1 + 0.03f, 1f, JambDeep + 0.03f, 1f); // (flush on the doorway's side: the door swings there)
                foreach (int face in new[] { -1, 1 })
                {
                    var head = new List<(float x, float y, float z)> { (xc, 0.72f, face * (JambDeep + 0.02f)), (xc - side * 0.03f, 0.76f, face * (JambDeep + 0.07f)), (xc - side * 0.07f, 0.78f, face * (JambDeep + 0.11f)) };
                    Tube(m, ShingleMesh.Trim, head, 0.045f, 8, face, 0.4f);
                }
            }
            // The arch over the door, standing proud of the wall above it, carved with a vine along it.
            const int steps = 24; float rise = 0.42f, inner = 0.7f, outer = 1f;
            var arc = new List<(float x, float y, float z)>[2];
            for (int f = 0; f < 2; f++) arc[f] = new List<(float x, float y, float z)>();
            for (int k = 0; k <= steps; k++)
            {
                double a = Math.PI * k / steps; float cx = -(float)Math.Cos(a), cy = (float)Math.Sin(a);
                float ix = cx * inner, iy = 1f + cy * rise * inner / outer, ox = cx * outer, oy = 1f + cy * rise;
                if (k < steps)
                {
                    double b = Math.PI * (k + 1) / steps; float dx = -(float)Math.Cos(b), dy = (float)Math.Sin(b);
                    float jx = dx * inner, jy = 1f + dy * rise * inner / outer, px = dx * outer, py = 1f + dy * rise;
                    foreach (int face in new[] { -1, 1 })
                    {
                        float z = face * JambDeep;
                        int p0 = m.AddPoint(ix, iy, z, 0, 0, face, 0, 0, 0.85f, 0.85f, 0.85f), p1 = m.AddPoint(ox, oy, z, 0, 0, face, 0, 1, 0.85f, 0.85f, 0.85f);
                        int p2 = m.AddPoint(px, py, z, 0, 0, face, 1, 1, 0.85f, 0.85f, 0.85f), p3 = m.AddPoint(jx, jy, z, 0, 0, face, 1, 0, 0.85f, 0.85f, 0.85f);
                        m.QuadFacing(ShingleMesh.Shingles, p0, p1, p2, p3, 0, 0, face);
                    }
                    // (its outer and inner edges)
                    float onx = ox + px, ony = oy + py - 2, inx = -(ix + jx), iny = -(iy + jy - 2);
                    int q0 = m.AddPoint(ox, oy, -JambDeep, onx, ony, 0, 0, 0, 0.7f, 0.7f, 0.7f), q1 = m.AddPoint(px, py, -JambDeep, onx, ony, 0, 1, 0, 0.7f, 0.7f, 0.7f);
                    int q2 = m.AddPoint(px, py, JambDeep, onx, ony, 0, 1, 1, 0.7f, 0.7f, 0.7f), q3 = m.AddPoint(ox, oy, JambDeep, onx, ony, 0, 0, 1, 0.7f, 0.7f, 0.7f);
                    m.QuadFacing(ShingleMesh.Shingles, q0, q1, q2, q3, onx, ony, 0);
                    q0 = m.AddPoint(ix, iy, -JambDeep, inx, iny, 0, 0, 0, 0.7f, 0.7f, 0.7f); q1 = m.AddPoint(jx, jy, -JambDeep, inx, iny, 0, 1, 0, 0.7f, 0.7f, 0.7f);
                    q2 = m.AddPoint(jx, jy, JambDeep, inx, iny, 0, 1, 1, 0.7f, 0.7f, 0.7f); q3 = m.AddPoint(ix, iy, JambDeep, inx, iny, 0, 0, 1, 0.7f, 0.7f, 0.7f);
                    m.QuadFacing(ShingleMesh.Shingles, q0, q1, q2, q3, inx, iny, 0);
                }
                float mx = (ix + ox) / 2, my = (iy + oy) / 2;
                for (int f = 0; f < 2; f++) { int face = f == 0 ? -1 : 1; float wob = 0.04f * (float)Math.Sin(k * 1.3 + f); arc[f].Add((mx + cx * wob, my + cy * wob * rise, face * (JambDeep + 0.018f))); }
            }
            // (the space under the arch filled: a panel set back in it, carved with a knot of two loops)
            foreach (int face in new[] { -1, 1 })
            {
                float z = face * (JambDeep - 0.05f);
                int c = m.AddPoint(0, 1f, z, 0, 0, face, 0.5f, 0, 0.6f, 0.6f, 0.6f);
                for (int k = 0; k < steps; k++)
                {
                    double a0 = Math.PI * k / steps, a1 = Math.PI * (k + 1) / steps;
                    int p0 = m.AddPoint(-(float)Math.Cos(a0) * inner, 1f + (float)Math.Sin(a0) * rise * inner / outer, z, 0, 0, face, 0, 1, 0.6f, 0.6f, 0.6f);
                    int p1 = m.AddPoint(-(float)Math.Cos(a1) * inner, 1f + (float)Math.Sin(a1) * rise * inner / outer, z, 0, 0, face, 1, 1, 0.6f, 0.6f, 0.6f);
                    m.TriFacing(ShingleMesh.Shingles, c, p0, p1, 0, 0, face);
                }
                foreach (int loop in new[] { -1, 1 })
                {
                    var knot = new List<(float x, float y, float z)>();
                    for (int k = 0; k <= 24; k++) { double t = 2 * Math.PI * k / 24; knot.Add((loop * 0.2f + 0.16f * (float)Math.Cos(t), 1.12f + 0.09f * (float)Math.Sin(t), z + face * (0.012f + 0.01f * (float)Math.Sin(2 * t + loop)))); }
                    Tube(m, ShingleMesh.Trim, knot, 0.016f, 6, face);
                }
            }
            Tube(m, ShingleMesh.Trim, arc[0], 0.022f, 6, -1);
            Tube(m, ShingleMesh.Trim, arc[1], 0.022f, 6, 1);
            return m;
        }

        // ---- helpers ----
        public static void Box(ShingleMesh m, int slot, float x0, float y0, float z0, float x1, float y1, float z1, float tone)
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
        public static void Column(ShingleMesh m, int slot, float cx, float cz, float y0, float y1, float r0, float r1, int sides, float tone)
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
        public static void Tube(ShingleMesh m, int slot, List<(float x, float y, float z)> path, float r, int sides, int face, float taper = 1f)
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
