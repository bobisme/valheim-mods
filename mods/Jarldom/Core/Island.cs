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
    }
}
