using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Bathhouse
{
    // A sauna in the world: a heat (a sauna stove, or the game's hot tub) and the room round it, read as Core's RoomFacts and judged by its
    // rules. Read again every few seconds at most for each heat (walls may be built or broken at any time).
    internal sealed class Heat
    {
        public Piece Piece; public bool Stove; public Vector3 At;
        public bool Lit => Piece != null && (Stove ? Piece.GetComponent<StoveSteam>()?.Lit == true : HotTubLit(Piece));
        public StoveSteam Steam => Stove && Piece != null ? Piece.GetComponent<StoveSteam>() : null;
        internal static bool HotTubLit(Piece tub) => tub.transform.Find("_Enabled") is Transform t && t.gameObject.activeSelf;
    }

    internal static class Room
    {
        private static readonly Dictionary<Piece, (float at, RoomFacts facts)> Cache = new Dictionary<Piece, (float, RoomFacts)>();
        private static readonly List<Piece> Near = new List<Piece>();
        private static int _mask;
        private static int Mask => _mask != 0 ? _mask : _mask = LayerMask.GetMask("piece", "Default", "static_solid", "terrain");

        // The heats within reach of a point (nearest first).
        internal static List<Heat> Heats(Vector3 at, float reach)
        {
            Near.Clear(); Piece.GetAllPiecesInRadius(at, reach, Near);
            return Near.Where(p => p != null).Select(p => (p, name: Utils.GetPrefabName(p.gameObject)))
                .Where(x => x.name == Kit.Stove || x.name == Kit.HotTub)
                .Select(x => new Heat { Piece = x.p, Stove = x.name == Kit.Stove, At = x.p.transform.position })
                .OrderBy(h => Vector3.Distance(h.At, at)).ToList();
        }
        // The heat a bather at this point is in the room of (or null): within reach, at about its height, under a roof.
        internal static Heat Around(Vector3 at)
        {
            foreach (Heat h in Heats(at, Sauna.Reach + 1))
                if (Mathf.Abs(at.y - h.At.y) < 3f && Utils.DistanceXZ(at, h.At) <= Sauna.Reach) return h;
            return null;
        }

        internal static RoomFacts Facts(Heat h, bool fresh = false)
        {
            if (h?.Piece == null) return null;
            if (!fresh && Cache.TryGetValue(h.Piece, out var had) && Time.time - had.at < 3f) { had.facts.Lit = h.Lit; return had.facts; }
            var f = new RoomFacts { Stove = h.Stove, HotTub = !h.Stove, Lit = h.Lit };
            Vector3 eye = h.At + Vector3.up * 1.3f;
            Cover.GetCoverForPoint(eye, out f.Cover, out f.UnderRoof);
            // (the walls: eight level rays from the heat, at a sitter's head; the stove's own body is behind the ray's start)
            float sum = 0; f.Rays = 8;
            for (int i = 0; i < f.Rays; i++)
            {
                Vector3 dir = Quaternion.Euler(0, i * 45f, 0) * Vector3.forward;
                Vector3 from = eye + dir * (h.Stove ? 0.5f : 1.8f); // (from just outside the heat: a hot tub is 3 m across)
                bool hit = Physics.Raycast(from, dir, out RaycastHit ray, Sauna.RayReach, Mask, QueryTriggerInteraction.Ignore);
                if (hit) f.Walls++;
                sum += (hit ? ray.distance : Sauna.RayReach) + (h.Stove ? 0.5f : 1.8f);
            }
            f.Across = sum / f.Rays;
            Near.Clear(); Piece.GetAllPiecesInRadius(h.At, Sauna.Reach, Near);
            f.Benches = Near.Count(p => p != null && Utils.GetPrefabName(p.gameObject) == Kit.Bench && Mathf.Abs(p.transform.position.y - h.At.y) < 2.5f);
            Cache[h.Piece] = (Time.time, f);
            if (Cache.Count > 64) foreach (Piece gone in Cache.Keys.Where(k => k == null).ToList()) Cache.Remove(gone);
            return f;
        }
        internal static string WhyNot(Heat h) => h == null ? "There's no sauna stove here." : Sauna.WhyNot(Facts(h));
        internal static bool Works(Heat h) => h != null && Sauna.Works(Facts(h));

        // Where bathers sit in a heat's room: the benches' seats and the hot tub's own.
        internal static List<Chair> Seats(Heat h)
        {
            Near.Clear(); Piece.GetAllPiecesInRadius(h.At, Sauna.Reach, Near);
            return Near.Where(p => p != null && Mathf.Abs(p.transform.position.y - h.At.y) < 2.5f && (Utils.GetPrefabName(p.gameObject) is string n && (n == Kit.Bench || n == Kit.HotTub)))
                .SelectMany(p => p.GetComponentsInChildren<Chair>()).ToList();
        }
        // Plunge tubs near a point.
        internal static List<Piece> Plunges(Vector3 at, float reach)
        {
            Near.Clear(); Piece.GetAllPiecesInRadius(at, reach, Near);
            return Near.Where(p => p != null && Utils.GetPrefabName(p.gameObject) == Kit.Plunge).OrderBy(p => Vector3.Distance(p.transform.position, at)).ToList();
        }
    }
}
