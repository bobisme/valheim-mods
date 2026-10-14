using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine.AI;
using HarmonyLib;
using UnityEngine;

namespace Jarldom
{
    // A point on the navmesh near a spot. The game's own Pathfinding.FindValidPoint queries with the agent type's enum value instead of the
    // navmesh agent's id, so for humanoids it nearly always says no; this asks the way GetPath does (its private SnapToNavMesh).
    internal static class Nav
    {
        private static readonly MethodInfo GetSettings = AccessTools.Method(typeof(Pathfinding), "GetSettings");
        private static readonly MethodInfo Snap = AccessTools.Method(typeof(Pathfinding), "SnapToNavMesh");
        private static readonly MethodInfo PokePoint = AccessTools.Method(typeof(Pathfinding), "PokePoint");
        public static bool Valid(out Vector3 point, Vector3 near, float range = 1.5f, Pathfinding.AgentType agent = Pathfinding.AgentType.Humanoid)
        {
            point = near;
            Pathfinding pf = Pathfinding.instance;
            if (pf == null || GetSettings == null || Snap == null) return false;
            try
            {
                PokePoint?.Invoke(pf, new object[] { near, agent });
                object settings = GetSettings.Invoke(pf, new object[] { agent });
                var args = new object[] { near, true, settings };
                if (!(bool)Snap.Invoke(pf, args)) return false;
                Vector3 p = (Vector3)args[0];
                if (Vector3.Distance(p, near) > range + 1f) return false; // (it searches a little wider than asked)
                point = p;
                return true;
            }
            catch { return false; }
        }
    }
}

namespace Jarldom
{
    // Stairs a villager can climb. The game's navmesh is built in 32 m tiles stitched along their edges, and where stairs cross an edge
    // the two halves never join (on open ground too): no one can path up them. So each stair in a colony gets a link of its own, from
    // a little before its foot to a little past its top, for the humanoid agent: the path goes up it whatever tiles it crosses.
    internal static class StairLinks
    {
        private static readonly Dictionary<int, (Piece stair, NavMeshLinkInstance link)> Links = new Dictionary<int, (Piece, NavMeshLinkInstance)>();
        private static readonly MethodInfo GetSettings = AccessTools.Method(typeof(Pathfinding), "GetSettings");
        private static float _next; private static Pathfinding _world;
        public static int Count => Links.Count;
        private const int NoAgent = int.MinValue;
        public static string Debug()
        {
            int agent = Agent();
            string tryLink = "";
            if (agent != NoAgent) { var l = NavMesh.AddLink(new NavMeshLinkData { startPosition = new Vector3(0, 0, 0), endPosition = new Vector3(1, 1, 0), width = 1f, bidirectional = true, agentTypeID = agent, costModifier = -1, area = 0 }); tryLink = l.valid ? "a test link is valid" : "a test link is INVALID"; if (l.valid) NavMesh.RemoveLink(l); }
            return $"agent {agent}, world same {_world == Pathfinding.instance}, next in {_next - Time.time:0.0}s, colonies {Colony.All.Count()}, floor link sets {FloorLinks.Count} ({FloorLinks.Values.Sum(x => x.Count)}), {tryLink}";
        }
        // (test: the stairs near a point, each with its link or why none)
        public static IEnumerable<string> Near(Vector3 at, float r)
        {
            var list = new List<Piece>(); Piece.GetAllPiecesInRadius(at, r, list);
            foreach (Piece p in list.Where(p => p != null && Utils.GetPrefabName(p.gameObject).Contains("stair")))
                yield return $"{Utils.GetPrefabName(p.gameObject)} at {p.transform.position.x:0.0},{p.transform.position.y:0.0},{p.transform.position.z:0.0}: " +
                    (Links.TryGetValue(p.GetInstanceID(), out var l) ? $"linked ({(l.link.valid ? "valid" : "invalid")})" : Ends(p, out Vector3 f, out Vector3 t) ? $"no link yet (ends {f:F1} -> {t:F1})" : "no ends found");
        }

        public static void Tick()
        {
            if (Time.time < _next || Pathfinding.instance == null) return;
            _next = Time.time + 5f;
            if (_world != Pathfinding.instance) { Clear(); _world = Pathfinding.instance; } // (a new world: the old one's links went with it)
            foreach (int gone in Links.Where(kv => kv.Value.stair == null).Select(kv => kv.Key).ToList()) { NavMesh.RemoveLink(Links[gone].link); Links.Remove(gone); }
            int agent = Agent();
            if (agent == NoAgent) return; // (an agent type's id is any int, negative too: it's made anew each session)
            foreach (string gone in FloorLinks.Keys.Where(k => !Colony.All.Any(c => k.StartsWith(c.Id + "/"))).ToList()) { foreach (var l in FloorLinks[gone]) NavMesh.RemoveLink(l); FloorLinks.Remove(gone); } // (a colony unloaded)
            foreach (Colony c in Colony.All)
            {
                Floors(c, agent);
                foreach (Piece p in c.PiecesIn(c.Radius + 10))
                {
                    if (p == null || Links.ContainsKey(p.GetInstanceID()) || !Utils.GetPrefabName(p.gameObject).Contains("stair")) continue;
                    if (!Ends(p, out Vector3 foot, out Vector3 top)) continue;
                    Vector3 up = top - foot; up.y = 0; up.Normalize();
                    var link = NavMesh.AddLink(new NavMeshLinkData { startPosition = foot - up * 0.4f, endPosition = top + up * 0.4f, width = 1.2f, bidirectional = true, agentTypeID = agent, costModifier = -1, area = 0 });
                    if (link.valid) Links[p.GetInstanceID()] = (p, link);
                }
            }
        }
        public static void Clear()
        {
            foreach (var kv in Links) NavMesh.RemoveLink(kv.Value.link); Links.Clear();
            foreach (var l in FloorLinks.Values.SelectMany(x => x)) NavMesh.RemoveLink(l); FloorLinks.Clear();
        }

        // Upper floors don't join across a tile's edge either (the ground does): in each room on an upper storey, links across every
        // tile edge that runs through it, a metre apart, on its floor.
        private static readonly Dictionary<string, List<NavMeshLinkInstance>> FloorLinks = new Dictionary<string, List<NavMeshLinkInstance>>();
        private const float Tile = 32f;
        private static void Floors(Colony c, int agent)
        {
            var want = new HashSet<string>();
            foreach (Room r in c.R.Rooms)
            {
                var (cx, cz) = Geometry.Centre(r.Xz);
                if (!ZoneSystem.instance.GetGroundHeight(new Vector3(cx, r.Floor, cz), out float ground) || r.Floor - ground < 1.5f) continue;
                string key = $"{c.Id}/{r.Id}@{r.Floor:0.0}/{r.Xz.Sum():0.0}"; // (a room moved or redrawn gets new links)
                want.Add(key);
                if (FloorLinks.ContainsKey(key)) continue;
                var list = new List<NavMeshLinkInstance>();
                float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
                for (int i = 0; i < r.Xz.Count; i += 2) { minX = Mathf.Min(minX, r.Xz[i]); maxX = Mathf.Max(maxX, r.Xz[i]); minZ = Mathf.Min(minZ, r.Xz[i + 1]); maxZ = Mathf.Max(maxZ, r.Xz[i + 1]); }
                for (float e = Mathf.Ceil((minX - Tile / 2) / Tile) * Tile + Tile / 2; e < maxX; e += Tile)
                    for (float z = minZ + 0.5f; z < maxZ; z += 1f) Across(list, r, new Vector3(e, 0, z), Vector3.right, agent);
                for (float e = Mathf.Ceil((minZ - Tile / 2) / Tile) * Tile + Tile / 2; e < maxZ; e += Tile)
                    for (float x = minX + 0.5f; x < maxX; x += 1f) Across(list, r, new Vector3(x, 0, e), Vector3.forward, agent);
                FloorLinks[key] = list;
            }
            foreach (string gone in FloorLinks.Keys.Where(k => k.StartsWith(c.Id + "/") && !want.Contains(k)).ToList())
            { foreach (var l in FloorLinks[gone]) NavMesh.RemoveLink(l); FloorLinks.Remove(gone); }
        }
        private static void Across(List<NavMeshLinkInstance> list, Room r, Vector3 at, Vector3 dir, int agent)
        {
            if (!Geometry.Inside(r.Xz, at.x, at.z)) return;
            // (on the floor there, if there is one: not over a stairwell)
            if (!Physics.Raycast(new Vector3(at.x, r.Floor + 1f, at.z), Vector3.down, out RaycastHit hit, 1.6f, LayerMask.GetMask("piece", "Default", "static_solid"), QueryTriggerInteraction.Ignore)) return;
            at.y = hit.point.y;
            var link = NavMesh.AddLink(new NavMeshLinkData { startPosition = at - dir * 0.6f, endPosition = at + dir * 0.6f, width = 1f, bidirectional = true, agentTypeID = agent, costModifier = -1, area = 0 });
            if (link.valid) list.Add(link);
        }

        // The foot and the top of a stair, from its snap points (the game's stairs all have them).
        private static bool Ends(Piece p, out Vector3 foot, out Vector3 top)
        {
            var bottom = new List<Vector3>(); var upper = new List<Vector3>();
            foreach (Transform t in p.transform)
            {
                if (!t.name.StartsWith("$hud_snappoint")) continue;
                if (t.name.Contains("bottom")) bottom.Add(t.position); else if (t.name.Contains("top")) upper.Add(t.position);
            }
            foot = bottom.Count > 0 ? bottom.Aggregate(Vector3.zero, (a, b) => a + b) / bottom.Count : Vector3.zero;
            top = upper.Count > 0 ? upper.Aggregate(Vector3.zero, (a, b) => a + b) / upper.Count : Vector3.zero;
            return bottom.Count > 0 && upper.Count > 0 && top.y - foot.y > 0.3f;
        }
        private static int Agent()
        {
            try
            {
                object settings = GetSettings?.Invoke(Pathfinding.instance, new object[] { Pathfinding.AgentType.Humanoid });
                object build = settings?.GetType().GetField("m_build").GetValue(settings);
                return build is NavMeshBuildSettings b ? b.agentTypeID : NoAgent;
            }
            catch { return NoAgent; }
        }
    }
}
