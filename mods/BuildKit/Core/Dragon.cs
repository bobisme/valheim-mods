using System;
using System.Collections.Generic;

namespace BuildKit
{
    // A carved dragon's head for a ridge's gable end, as on a stave church: a neck rising out of the ridge's end and curving up and out,
    // the head reaching out beyond it with its jaws open, a crest of curled fins down its neck, a ridge over each eye. Added to a ridge
    // piece's mesh; the end at +x.
    public static class Dragon
    {
        // The neck's line, from inside the ridge's end out to behind the head: (out past the end, up over the ridge's top), and its
        // thickness there (across, up and down).
        private static readonly (float x, float y, float w, float h)[] Neck =
        {
            (-0.35f, -0.02f, 0.075f, 0.17f), (0.0f, 0.02f, 0.07f, 0.17f), (0.3f, 0.09f, 0.066f, 0.16f), (0.58f, 0.2f, 0.062f, 0.15f),
            (0.82f, 0.36f, 0.058f, 0.14f), (0.98f, 0.54f, 0.056f, 0.13f),
        };
        public static void Add(ShingleMesh m, float ridgeTop, int slot)
        {
            float x0 = 1f; // (the ridge's end)
            var line = new List<(float x, float y, float w, float h)>();
            foreach (var p in Neck) line.Add((x0 + p.x, ridgeTop + p.y, p.w, p.h));
            // The head: from the top of the neck curling forward, out over the gable; the jaws below it.
            var head = new List<(float x, float y, float w, float h)>
            {
                line[line.Count - 1],
                (x0 + 1.06f, ridgeTop + 0.66f, 0.07f, 0.13f), (x0 + 1.24f, ridgeTop + 0.74f, 0.064f, 0.1f), (x0 + 1.42f, ridgeTop + 0.78f, 0.05f, 0.07f), (x0 + 1.58f, ridgeTop + 0.82f, 0.036f, 0.045f),
            };
            Loft(m, slot, line, true, false);
            Loft(m, slot, head, false, true);
            // (the lower jaw: from under the head's back, dropping open toward the snout)
            var jaw = new List<(float x, float y, float w, float h)> { (x0 + 1.1f, ridgeTop + 0.56f, 0.05f, 0.06f), (x0 + 1.28f, ridgeTop + 0.6f, 0.044f, 0.04f), (x0 + 1.46f, ridgeTop + 0.58f, 0.034f, 0.03f), (x0 + 1.56f, ridgeTop + 0.62f, 0.024f, 0.02f) };
            Loft(m, slot, jaw, true, true);
            // (a tongue curling out of the open mouth)
            var tongue = new List<(float x, float y, float w, float h)> { (x0 + 1.24f, ridgeTop + 0.67f, 0.018f, 0.01f), (x0 + 1.5f, ridgeTop + 0.68f, 0.016f, 0.009f), (x0 + 1.66f, ridgeTop + 0.72f, 0.012f, 0.007f), (x0 + 1.72f, ridgeTop + 0.8f, 0.008f, 0.005f) };
            Loft(m, slot, tongue, true, true);
            // A crest down the back of the neck: fins leaning back, curled at their tips.
            for (int i = 1; i < line.Count; i++)
            {
                var p = line[i]; var q = line[i - 1];
                float dx = p.x - q.x, dy = p.y - q.y, l = (float)Math.Sqrt(dx * dx + dy * dy); dx /= l; dy /= l;
                float bx = -dy, by = dx; // (the neck's back: to the left of its way up, which is outward-and-back)
                if (by < 0) { bx = -bx; by = -by; }
                float bx2 = -bx, by2 = by; // (leaning back toward the ridge)
                float tall = 0.1f + 0.03f * (float)Math.Sin(i * 1.7f);
                Fin(m, slot, (p.x - bx * 0.0f + (-dx) * 0.04f, p.y + p.h * 0.85f), (p.x + bx2 * 0.5f * tall - dx * 0.1f, p.y + p.h + tall), (p.x + dx * 0.05f, p.y + p.h * 0.85f), 0.012f);
            }
            // (and over the head, two horns sweeping back, and a brow over each eye)
            var top = head[1];
            Fin(m, slot, (top.x - 0.04f, top.y + top.h * 0.8f), (top.x - 0.34f, top.y + top.h + 0.26f), (top.x + 0.08f, top.y + top.h * 0.8f), 0.016f, 0.04f);
            Fin(m, slot, (top.x - 0.04f, top.y + top.h * 0.8f), (top.x - 0.34f, top.y + top.h + 0.26f), (top.x + 0.08f, top.y + top.h * 0.8f), 0.016f, -0.04f);
            var eye = head[2];
            foreach (int side in new[] { -1, 1 })
                Lump(m, slot, eye.x - 0.04f, eye.y + eye.h * 0.45f, side * eye.w * 0.9f, 0.03f);
        }

        // A tube along a line, its cross-section an eight-sided ellipse (w across, h up and down) square to the line; capped where asked.
        private static void Loft(ShingleMesh m, int slot, List<(float x, float y, float w, float h)> line, bool capStart, bool capEnd)
        {
            const int sides = 8;
            var rings = new List<int[]>();
            for (int i = 0; i < line.Count; i++)
            {
                var p = line[i];
                var a = line[Math.Max(0, i - 1)]; var b = line[Math.Min(line.Count - 1, i + 1)];
                float tx = b.x - a.x, ty = b.y - a.y, tl = (float)Math.Sqrt(tx * tx + ty * ty); tx /= tl; ty /= tl;
                float ux = -ty, uy = tx; // (up, square to the line, in the x-y plane)
                var ring = new int[sides];
                for (int k = 0; k < sides; k++)
                {
                    double th = 2 * Math.PI * k / sides;
                    float c = (float)Math.Cos(th), s = (float)Math.Sin(th);
                    float x = p.x + ux * p.h * s, y = p.y + uy * p.h * s, z = p.w * c;
                    float nx = ux * s / p.h, ny = uy * s / p.h, nz = c / p.w; float nl = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    ring[k] = m.AddPoint(x, y, z, nx / nl, ny / nl, nz / nl, (float)k / sides, i * 0.3f, 1, 1, 1);
                }
                rings.Add(ring);
            }
            for (int i = 0; i + 1 < rings.Count; i++)
                for (int k = 0; k < sides; k++)
                {
                    int k2 = (k + 1) % sides;
                    float mx = m.Nx(rings[i][k]) + m.Nx(rings[i + 1][k2]), my = m.Ny(rings[i][k]) + m.Ny(rings[i + 1][k2]), mz = m.Nz(rings[i][k]) + m.Nz(rings[i + 1][k2]);
                    m.QuadFacing(slot, rings[i][k], rings[i][k2], rings[i + 1][k2], rings[i + 1][k], mx, my, mz);
                }
            if (capStart) Cap(m, slot, rings[0], line[0], line[1], -1);
            if (capEnd) Cap(m, slot, rings[rings.Count - 1], line[line.Count - 1], line[line.Count - 2], -1);
        }
        private static void Cap(ShingleMesh m, int slot, int[] ring, (float x, float y, float w, float h) at, (float x, float y, float w, float h) toward, int _)
        {
            float nx = at.x - toward.x, ny = at.y - toward.y, nl = (float)Math.Sqrt(nx * nx + ny * ny); nx /= nl; ny /= nl;
            int c = m.AddPoint(at.x + nx * 0.01f, at.y + ny * 0.01f, 0, nx, ny, 0, 0.5f, 0.5f, 1, 1, 1);
            for (int k = 0; k < ring.Length; k++) m.TriFacing(slot, c, ring[k], ring[(k + 1) % ring.Length], nx, ny, 0);
        }
        // A thin fin: a triangle from base point a, up to tip t, back down to base point b, given thickness, at z (0: on the middle).
        private static void Fin(ShingleMesh m, int slot, (float x, float y) a, (float x, float y) t, (float x, float y) b, float thick, float z = 0)
        {
            foreach (int side in new[] { -1, 1 })
            {
                float zz = z + side * thick / 2;
                int p = m.AddPoint(a.x, a.y, zz, 0, 0, side, 0, 0, 1, 1, 1), q = m.AddPoint(t.x, t.y, z + side * thick / 6, 0, 0, side, 0.5f, 1, 1, 1, 1), r = m.AddPoint(b.x, b.y, zz, 0, 0, side, 1, 0, 1, 1, 1);
                m.TriFacing(slot, p, q, r, 0, 0, side);
            }
        }
        // A small rounded lump (an eye's brow): an eight-sided cone, its base on the head.
        private static void Lump(ShingleMesh m, int slot, float x, float y, float z, float r)
        {
            float out_ = Math.Sign(z);
            int tip = m.AddPoint(x, y, z + out_ * r * 0.7f, 0, 0, out_, 0.5f, 0.5f, 0.8f, 0.8f, 0.8f);
            var ring = new int[8];
            for (int k = 0; k < 8; k++) { double th = 2 * Math.PI * k / 8; ring[k] = m.AddPoint(x + r * (float)Math.Cos(th), y + r * (float)Math.Sin(th), z, (float)Math.Cos(th), (float)Math.Sin(th), out_ * 0.5f, 0, 0, 0.8f, 0.8f, 0.8f); }
            for (int k = 0; k < 8; k++) m.TriFacing(slot, tip, ring[k], ring[(k + 1) % 8], 0, 0, out_);
        }
    }
}
