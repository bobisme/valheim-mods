using System;
using System.Collections.Generic;

namespace Jarldom
{
    // Island outposts (spec #45): is there a way over land (or water shallow enough to wade) from one place to another? A flood fill
    // over the land, a cell every step metres, within the box round both places and a margin; null if the box is too big to tell.
    public static class Island
    {
        public const float Wade = 0.6f; // (water this shallow a cart or a walker crosses)
        public static bool? Connected(Func<float, float, float> height, float sea, float x0, float z0, float x1, float z1, float step = 16f, float margin = 400f, int maxCells = 60000)
        {
            float minX = Math.Min(x0, x1) - margin, minZ = Math.Min(z0, z1) - margin, maxX = Math.Max(x0, x1) + margin, maxZ = Math.Max(z0, z1) + margin;
            int w = (int)Math.Ceiling((maxX - minX) / step) + 1, h = (int)Math.Ceiling((maxZ - minZ) / step) + 1;
            if ((long)w * h > maxCells) return null;
            bool Land(int i, int j) => height(minX + i * step, minZ + j * step) > sea - Wade;
            int si = (int)Math.Round((x0 - minX) / step), sj = (int)Math.Round((z0 - minZ) / step), ti = (int)Math.Round((x1 - minX) / step), tj = (int)Math.Round((z1 - minZ) / step);
            var seen = new bool[w, h]; var queue = new Queue<(int, int)>();
            // (each end from the nearest land cell to it: pillars stand on land, but a cell's middle may be just off the shore)
            (int, int)? Start(int ci, int cj) { for (int r = 0; r <= 2; r++) for (int di = -r; di <= r; di++) for (int dj = -r; dj <= r; dj++) { int i = ci + di, j = cj + dj; if (i >= 0 && j >= 0 && i < w && j < h && Land(i, j)) return (i, j); } return null; }
            var s = Start(si, sj); var t = Start(ti, tj);
            if (s == null || t == null) return false;
            queue.Enqueue(s.Value); seen[s.Value.Item1, s.Value.Item2] = true;
            while (queue.Count > 0)
            {
                var (i, j) = queue.Dequeue();
                if (i == t.Value.Item1 && j == t.Value.Item2) return true;
                foreach (var (ni, nj) in new[] { (i + 1, j), (i - 1, j), (i, j + 1), (i, j - 1) })
                {
                    if (ni < 0 || nj < 0 || ni >= w || nj >= h || seen[ni, nj]) continue;
                    seen[ni, nj] = true;
                    if (Land(ni, nj)) queue.Enqueue((ni, nj));
                }
            }
            return false;
        }
        // The land mass a point stands on or beside (the nearest land within a few cells), if it's an island: its middle, its reach
        // from the middle, and its size in cells; null if there's no land near, or it runs on past maxCells (a continent, not an isle).
        public static (float x, float z, float reach, int cells)? Mass(Func<float, float, float> height, float sea, float x, float z, float step = 16f, int maxCells = 12000)
        {
            bool Land(int i, int j) => height(x + i * step, z + j * step) > sea - Wade;
            (int, int)? start = null;
            for (int r = 0; r <= 12 && start == null; r++) for (int di = -r; di <= r && start == null; di++) for (int dj = -r; dj <= r; dj++) if (Math.Max(Math.Abs(di), Math.Abs(dj)) == r && Land(di, dj)) { start = (di, dj); break; }
            if (start == null) return null;
            var seen = new HashSet<(int, int)> { start.Value }; var queue = new Queue<(int, int)>(); queue.Enqueue(start.Value);
            double sx = 0, sz = 0; int n = 0;
            while (queue.Count > 0)
            {
                var (i, j) = queue.Dequeue(); n++; sx += i; sz += j;
                if (n > maxCells) return null;
                foreach (var (ni, nj) in new[] { (i + 1, j), (i - 1, j), (i, j + 1), (i, j - 1) })
                    if (seen.Add((ni, nj)) && Land(ni, nj)) queue.Enqueue((ni, nj));
            }
            if (n < 8) return null; // (a rock or a sandbar, not an isle)
            float cx = (float)(sx / n), cz = (float)(sz / n), reach = 0;
            foreach (var (i, j) in seen) if (Land(i, j)) reach = Math.Max(reach, (float)Math.Sqrt((i - cx) * (i - cx) + (j - cz) * (j - cz)));
            return (x + cx * step, z + cz * step, (reach + 1) * step, n);
        }
    }

    // Sailing round an island: the bearing swept round its middle, kept while the ship stays near it. Round is 330 degrees one way.
    public sealed class Circuit
    {
        public const float Round = 330f, Near = 300f;
        public float X, Z, Reach; public float Swept; private float? _last;
        public Circuit(float x, float z, float reach) { X = x; Z = z; Reach = reach; }
        // A new place on the water: false if it's left the island behind (too far from it).
        public bool Step(float px, float pz)
        {
            float dx = px - X, dz = pz - Z;
            if (Math.Sqrt(dx * dx + dz * dz) > Reach + Near) return false;
            float a = (float)(Math.Atan2(dz, dx) * 180 / Math.PI);
            if (_last.HasValue) { float d = a - _last.Value; while (d > 180) d -= 360; while (d < -180) d += 360; Swept += d; }
            _last = a;
            return true;
        }
        public bool Done => Math.Abs(Swept) >= Round;
    }
}
