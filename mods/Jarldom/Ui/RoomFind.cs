using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // A room found from one click on its floor: the floor spreads out on a half-metre grid until walls stop it (only what stands at a
    // knee's and a head's height both: a table or a bench is walked round, a wall isn't), doorways counting as wall open or shut; the
    // cells' outline, simplified, is the room. If it spreads too far, the walls don't close it: where it got out is shown.
    internal static class RoomFind
    {
        public const float Cell = 0.5f, MaxReach = 30f; public const int MaxCells = 6000;
        private static int _mask = -1;
        private static int Mask => _mask != -1 ? _mask : _mask = LayerMask.GetMask("piece", "piece_nonsolid", "Default", "static_solid", "terrain", "vehicle");

        public sealed class Result { public List<float> Xz; public float Floor; public int Doors; public Vector3? Leak; public string Why = ""; }

        public static Result Find(Vector3 at)
        {
            var r = new Result { Floor = at.y };
            // Doorways: wall whether the door stands open or shut (an open door leaves a gap the fill would pour through).
            var near = new List<Piece>(); Piece.GetAllPiecesInRadius(at, MaxReach + 5f, near);
            var doors = near.Where(p => p.GetComponent<Door>() != null && Mathf.Abs(p.transform.position.y - at.y) < 2.5f).Select(p => p.transform.position).ToList();
            var seen = new HashSet<(int, int)> { (0, 0) };
            var queue = new Queue<(int, int)>(); queue.Enqueue((0, 0));
            var doorsMet = new HashSet<int>();
            Vector3 Pos((int i, int j) c) => new Vector3(at.x + c.i * Cell, at.y, at.z + c.j * Cell);
            var steps = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
            while (queue.Count > 0)
            {
                var c = queue.Dequeue();
                Vector3 p = Pos(c);
                if (Utils.DistanceXZ(p, at) > MaxReach || seen.Count > MaxCells) { r.Leak = p; r.Why = "the walls don't close it"; return r; }
                foreach (var (di, dj) in steps)
                {
                    var n = (c.Item1 + di, c.Item2 + dj);
                    if (seen.Contains(n)) continue;
                    Vector3 q = Pos(n);
                    int door = doors.FindIndex(d => Utils.DistanceXZ(d, (p + q) / 2f) < 1.0f);
                    if (door >= 0) { doorsMet.Add(door); continue; }
                    if (Blocked(p, q)) continue;
                    if (!Floor(q, at.y)) continue; // (a hole, a stair down, the edge of a raised floor)
                    seen.Add(n); queue.Enqueue(n);
                }
            }
            r.Doors = doorsMet.Count;
            if (seen.Count < 8) { r.Why = "that's not a room's floor (walls right round it)"; return r; }
            r.Xz = Outline(seen, at);
            if (r.Xz == null || r.Xz.Count < 6) { r.Why = "its shape couldn't be traced"; r.Xz = null; }
            return r;
        }
        // A wall between two cells: something solid at a knee's height and at a head's (furniture is only the first).
        private static bool Blocked(Vector3 a, Vector3 b)
        {
            return Physics.Linecast(a + Vector3.up * 0.6f, b + Vector3.up * 0.6f, Mask, QueryTriggerInteraction.Ignore)
                && Physics.Linecast(a + Vector3.up * 1.6f, b + Vector3.up * 1.6f, Mask, QueryTriggerInteraction.Ignore);
        }
        private static bool Floor(Vector3 q, float y)
        {
            if (!Physics.Raycast(new Vector3(q.x, y + 1.0f, q.z), Vector3.down, out RaycastHit hit, 2.0f, Mask, QueryTriggerInteraction.Ignore)) return false;
            return Mathf.Abs(hit.point.y - y) < 0.6f;
        }

        // The outer edge of the cells, corner to corner, with straight runs and near-straight steps merged (at most 24 corners).
        private static List<float> Outline(HashSet<(int, int)> cells, Vector3 at)
        {
            // Each cell's sides that face out, as directed edges on the grid's corner lattice (corner (i,j) is the cell's low corner).
            var next = new Dictionary<(int, int), List<(int, int)>>();
            void Edge((int, int) a, (int, int) b) { if (!next.TryGetValue(a, out var l)) next[a] = l = new List<(int, int)>(); l.Add(b); }
            foreach (var (i, j) in cells)
            {
                if (!cells.Contains((i, j - 1))) Edge((i, j), (i + 1, j));         // bottom, left to right
                if (!cells.Contains((i + 1, j))) Edge((i + 1, j), (i + 1, j + 1)); // right, up
                if (!cells.Contains((i, j + 1))) Edge((i + 1, j + 1), (i, j + 1)); // top, right to left
                if (!cells.Contains((i - 1, j))) Edge((i, j + 1), (i, j));         // left, down
            }
            // Walk the loops, each edge once; keep the longest (the outside, not a pillar's hole).
            List<(int, int)> best = null;
            while (next.Count > 0)
            {
                var start = next.Keys.First(); var cur = start;
                var loop = new List<(int, int)>();
                for (int guard = 0; guard < 40000; guard++)
                {
                    if (!next.TryGetValue(cur, out var outs) || outs.Count == 0) { next.Remove(cur); break; }
                    var to = outs[outs.Count - 1]; outs.RemoveAt(outs.Count - 1); if (outs.Count == 0) next.Remove(cur);
                    loop.Add(cur); cur = to;
                    if (cur == start) break;
                }
                if (best == null || loop.Count > best.Count) best = loop;
            }
            if (best == null) return null;
            // Corners on the lattice to the world (a cell's low corner is half a cell below and left of its centre).
            var pts = best.Select(v => new Vector2(at.x + (v.Item1 - 0.5f) * Cell, at.z + (v.Item2 - 0.5f) * Cell)).ToList();
            pts = Simplify(pts, 0.3f);
            for (float tol = 0.6f; pts.Count > 24 && tol < 4f; tol += 0.3f) pts = Simplify(pts, tol);
            if (pts.Count < 3) return null;
            var xz = new List<float>(); foreach (Vector2 v in pts) { xz.Add((float)System.Math.Round(v.x, 2)); xz.Add((float)System.Math.Round(v.y, 2)); }
            return xz;
        }
        // Douglas-Peucker on a closed outline.
        private static List<Vector2> Simplify(List<Vector2> pts, float tol)
        {
            if (pts.Count < 4) return pts;
            // (split at the two points farthest apart, simplify each half)
            int far = 0; float best = 0;
            for (int k = 1; k < pts.Count; k++) { float d = (pts[k] - pts[0]).sqrMagnitude; if (d > best) { best = d; far = k; } }
            var a = Line(pts.GetRange(0, far + 1), tol);
            var b = Line(pts.GetRange(far, pts.Count - far).Concat(new[] { pts[0] }).ToList(), tol);
            return a.Take(a.Count - 1).Concat(b.Take(b.Count - 1)).ToList();
        }
        private static List<Vector2> Line(List<Vector2> pts, float tol)
        {
            if (pts.Count < 3) return pts;
            Vector2 s = pts[0], e = pts[pts.Count - 1];
            int idx = -1; float dmax = 0;
            for (int k = 1; k < pts.Count - 1; k++) { float d = Distance(pts[k], s, e); if (d > dmax) { dmax = d; idx = k; } }
            if (dmax <= tol) return new List<Vector2> { s, e };
            var left = Line(pts.GetRange(0, idx + 1), tol); var right = Line(pts.GetRange(idx, pts.Count - idx), tol);
            return left.Take(left.Count - 1).Concat(right).ToList();
        }
        private static float Distance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a; float len = ab.sqrMagnitude;
            if (len < 1e-6f) return (p - a).magnitude;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / len);
            return (p - (a + ab * t)).magnitude;
        }
    }
}
