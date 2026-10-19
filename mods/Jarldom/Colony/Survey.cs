using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // What the colony owner measures in the world: beds and their comfort, what the colony has built, and each room's walls, roof and contents.
    internal sealed partial class Colony
    {
        private static readonly List<Piece> PieceBuffer = new List<Piece>();
        private static readonly Collider[] Hits = new Collider[256];
        private static int _pieceMask = -1;
        public static int PieceMask => _pieceMask != -1 ? _pieceMask : _pieceMask = LayerMask.GetMask("piece", "piece_nonsolid", "Default", "static_solid");

        public static string Prefab(GameObject go) => Utils.GetPrefabName(go).ToLowerInvariant();
        // A piece's (or any component's) prefab name, kept by its instance: a scan of thousands of pieces asked it of every one, and
        // each asking made a new string. (instance ids aren't reused in a session; the cache is let go when it grows large)
        private static readonly Dictionary<int, string> _names = new Dictionary<int, string>();
        public static string NameOf(Component c)
        {
            int id = c.GetInstanceID();
            if (_names.TryGetValue(id, out string n)) return n;
            if (_names.Count > 100000) _names.Clear();
            return _names[id] = Utils.GetPrefabName(c.gameObject);
        }
        // (and which pieces have a component of a kind, the same way)
        private static readonly Dictionary<(Type, int), Component> Comps = new Dictionary<(Type, int), Component>();
        public static T CompOf<T>(Piece p) where T : Component
        {
            var key = (typeof(T), p.GetInstanceID());
            if (Comps.TryGetValue(key, out Component c)) return (T)c;
            if (Comps.Count > 200000) Comps.Clear();
            T t = p.GetComponent<T>(); Comps[key] = t; return t;
        }

        // Our own random id on a piece (never a ZDOID: those change every load).
        public static string IdOf(ZNetView v, bool create = true)
        {
            if (v == null || !v.IsValid()) return "";
            string id = v.GetZDO().GetString(IdKey, "");
            if (id.Length > 0 || !create) return id;
            if (!v.IsOwner()) v.ClaimOwnership();
            id = NewId();
            v.GetZDO().Set(IdKey, id);
            return id;
        }

        // (every piece in the loaded world is looked at to find these, and much asks: the answer is kept a few seconds; don't change the list)
        // Each radius asked, and each kind of piece (stations, beds, doors), is kept as long as the look itself: a work scan asks a hundred times.
        private float _piecesAt = -1, _piecesRadius; private List<Piece> _pieces; private readonly List<Vector3> _piecePos = new List<Vector3>(); // (where each was at the look: pieces stay put)
        private readonly Dictionary<float, List<Piece>> _within = new Dictionary<float, List<Piece>>();
        private readonly Dictionary<(System.Type, float), System.Collections.IList> _kinds = new Dictionary<(System.Type, float), System.Collections.IList>();
        private readonly Dictionary<float, int> _checkedFrame = new Dictionary<float, int>(); private readonly Dictionary<(System.Type, float), int> _kindChecked = new Dictionary<(System.Type, float), int>();
        public List<Piece> PiecesIn(float radius)
        {
            if (_pieces == null || Time.time - _piecesAt >= 4f || radius > _piecesRadius) // (pieces come and go seldom; gone ones are left out below)
            {
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                PieceBuffer.Clear();
                _piecesRadius = Mathf.Max(radius, Radius + 40f); // (one wide look serves every nearer ask)
                Piece.GetAllPiecesInRadius(Position, _piecesRadius, PieceBuffer);
                _pieces = PieceBuffer.ToList(); _piecesAt = Time.time; _within.Clear(); _kinds.Clear();
                _piecePos.Clear(); foreach (Piece p in _pieces) _piecePos.Add(p.transform.position);
                Split.Add($"p.all{_pieces.Count}", t0);
            }
            // (a kept list is checked for pieces gone since at most once a frame: the check of every piece was most of a scan's time)
            if (_within.TryGetValue(radius, out var kept)) { if (_checkedFrame.TryGetValue(radius, out int f) && f == Time.frameCount || kept.All(p => p != null)) { _checkedFrame[radius] = Time.frameCount; return kept; } _within.Remove(radius); }
            Vector3 at = Position; long t1 = System.Diagnostics.Stopwatch.GetTimestamp();
            var near = new List<Piece>(); float r2 = radius * radius;
            for (int i = 0; i < _pieces.Count; i++) if ((_piecePos[i] - at).sqrMagnitude < r2 && _pieces[i] != null) near.Add(_pieces[i]);
            _within[radius] = near;
            Split.Add($"p.r{radius:0}", t1);
            return _within[radius];
        }
        // The pieces within r of a point, from a colony's kept look where it covers the point (each piece's place read once a look,
        // not every piece in the loaded world asked where it is, as the game's own search does); else the game's search.
        public static List<Piece> PiecesNear(Vector3 at, float r)
        {
            var into = new List<Piece>();
            foreach (Colony c in ById.Values)
            {
                if (!c.Valid || Vector3.Distance(at, c.Position) + r >= c.Radius + 40f) continue;
                c.PiecesIn(c.Radius); // (a fresh look, if the kept one is old)
                float r2 = r * r;
                for (int i = 0; i < c._pieces.Count; i++) if ((c._piecePos[i] - at).sqrMagnitude < r2 && c._pieces[i] != null) into.Add(c._pieces[i]);
                return into;
            }
            Piece.GetAllPiecesInRadius(at, r, into);
            return into;
        }
        // The pieces of one kind (their component) within the radius.
        public List<T> PiecesIn<T>(float radius) where T : Component
        {
            List<Piece> all = PiecesIn(radius);
            if (_kinds.TryGetValue((typeof(T), radius), out var kept)) { var l = (List<T>)kept; if (_kindChecked.TryGetValue((typeof(T), radius), out int f) && f == Time.frameCount || l.All(x => x != null)) { _kindChecked[(typeof(T), radius)] = Time.frameCount; return l; } }
            long t2 = System.Diagnostics.Stopwatch.GetTimestamp();
            var found = new List<T>(); foreach (Piece p in all) { T t = CompOf<T>(p); if (t != null) found.Add(t); }
            _kinds[(typeof(T), radius)] = found; Split.Add($"p.{typeof(T).Name}", t2); return found;
        }

        // ---- beds ----
        // The colony's beds: in one of its rooms, or by the pillars. (A bed elsewhere in the radius is someone's own base: never taken.)
        public List<Bed> AllBeds() => PiecesIn<Bed>(Radius).Where(b => b != null && Ours(b.transform.position)).ToList();
        public const float NearPillars = 15f;
        public bool Ours(Vector3 p) => Utils.DistanceXZ(p, Position) <= NearPillars || RoomAt(p) != null;
        // Where the colony's people pick up what lies about: its rooms and zones, and by the pillars.
        // A fire or light anywhere in the colony circle is kept fed, along the paths too, whoever built it (fuel from the stores
        // takes nothing of theirs); only not inside a ward that keeps the jarl out (someone's own base).
        public bool Feeds(Piece p) => Tends(p.transform.position) || Utils.DistanceXZ(p.transform.position, Position) <= Radius && PrivateArea.CheckAccess(p.transform.position, 0f, false, false);
        public bool Tends(Vector3 p)
        {
            if (Utils.DistanceXZ(p, Position) <= NearPillars) return true;
            // (each spot's answer kept a few seconds, by the half metre: a work scan asks it of every fire, torch and dropped thing, and the
            // answer is every room's outline and every zone's edges; they change seldom)
            if (Time.time - _tendsAt > 10f) { _tends.Clear(); _tendsAt = Time.time; }
            var key = (Mathf.RoundToInt(p.x * 2), Mathf.RoundToInt(p.y * 2), Mathf.RoundToInt(p.z * 2));
            if (_tends.TryGetValue(key, out bool kept)) return kept;
            return _tends[key] = RoomAt(p) != null || R.Zones.Any(z => Geometry.Distance(z.Xz, p.x, p.z) <= 2f);
        }
        private readonly Dictionary<(int, int, int), bool> _tends = new Dictionary<(int, int, int), bool>(); private float _tendsAt;
        public static long Hash(string vid) => (long)(uint)vid.GetStableHashCode() + 1_000_000_000L * 7;
        public static string BedOwner(Bed bed, out string kind)
        {
            ZDO z = bed.GetComponent<ZNetView>()?.GetZDO();
            kind = "";
            if (z == null) return "";
            string ours = z.GetString(Villagers.ClaimKey, "");
            if (ours.Length > 0) { kind = "villager"; return ours; }
            if (z.GetLong(ZDOVars.s_owner, 0L) != 0) { kind = "player"; return z.GetString(ZDOVars.s_ownerName, "a player"); }
            if (z.GetLong("dhc_home", 0L) != 0) { kind = "companion"; return z.GetString("dhc_homename", "a companion"); }
            return "";
        }
        // Free: nobody's, or claimed by a soul who no longer lives here (gone to Valhalla, lost, sent away).
        // (or one whose claim is stale: the claimant's own record names another bed, or none; such claims were left by moves between beds,
        // and kept nine beds of a new longhouse empty while nine slept on the hall floor)
        public List<Bed> FreeBeds() => AllBeds().Where(b => { string who = BedOwner(b, out string kind); return who == "" || kind == "villager" && (!(R.Row(who) is RosterRow r) || r.State != SoulState.Active || StaleClaim(b, who)); }).ToList();
        public bool StaleClaim(Bed bed, string vid)
        {
            string mine = Villagers.ByVid(vid)?.S.Bed ?? R.Row(vid)?.Bed;
            return string.IsNullOrEmpty(mine) || mine != IdOf(bed.GetComponent<ZNetView>(), false);
        }
        private List<Bed> _bedsFrom; private readonly Dictionary<string, Bed> _bedIds = new Dictionary<string, Bed>();
        public Bed BedById(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            // Any bed in reach, ours or not: a bed already held (or given from the panel) stays held. Ours() only limits choosing new ones.
            // (the beds in reach by id, made again when the kept piece list is: asked by every villager every few seconds)
            List<Bed> beds = PiecesIn<Bed>(Radius + 20);
            if (!ReferenceEquals(beds, _bedsFrom)) { _bedsFrom = beds; _bedIds.Clear(); foreach (Bed b in beds) if (b != null) { string bid = IdOf(b.GetComponent<ZNetView>(), false); if (!string.IsNullOrEmpty(bid)) _bedIds[bid] = b; } }
            return _bedIds.TryGetValue(id, out Bed found) && found != null ? found : null;
        }
        private Vector3? Survey_BedSpot(string bedId)
        {
            Bed b = BedById(bedId);
            if (b == null) return null;
            return Spot(b.transform.position + b.transform.forward * 1.2f, 1.5f) ?? b.transform.position + Vector3.up * 0.5f;
        }
        public static int ComfortAt(Vector3 at)
        {
            Cover.GetCoverForPoint(at + Vector3.up, out float cover, out bool roof);
            return SE_Rested.CalculateComfortLevel(cover >= 0.8f && roof, at);
        }

        // ---- renown ----
        private void Survey()
        {
            var pieces = PiecesIn(Radius);
            double value = 0;
            foreach (Piece p in pieces)
            {
                if (p.m_resources == null) continue;
                foreach (Piece.Requirement req in p.m_resources)
                    if (req != null && req.m_resItem != null && !req.m_upgraderResource) value += req.m_amount * MaterialValue(req.m_resItem);
            }
            PieceValue = (int)value;
            int coins = 0; double valuables = 0, treasure = 0;
            foreach (Piece p in pieces)
            {
                Container c = p.GetComponent<Container>();
                if (c == null || !ContainerAccess.IsStorage(c)) continue;
                bool inTreasure = R.Rooms.Any(r => r.Ok && r.Type == RoomType.TreasureRoom && Geometry.Inside(r.Xz, p.transform.position.x, p.transform.position.z));
                foreach (ItemDrop.ItemData item in c.GetInventory().GetAllItems())
                {
                    if (item.m_dropPrefab != null && item.m_dropPrefab.name == "Coins") { coins += item.m_stack; if (inTreasure) treasure += item.m_stack * 10; }
                    else if (item.m_shared.m_value > 0) { valuables += item.m_shared.m_value * item.m_stack; if (inTreasure) treasure += item.m_shared.m_value * item.m_stack; }
                }
            }
            WealthPart = Jarldom.Renown.Wealth(value, coins, valuables, treasure > 0, treasure);
            var beds = pieces.Select(p => p.GetComponent<Bed>()).Where(b => b != null && Ours(b.transform.position)).ToList();
            Beds = beds.Count(b => { BedOwner(b, out string kind); return kind == "" || kind == "villager"; });
            var comforts = beds.Select(b => ComfortAt(b.transform.position)).ToList();
            ComfortPart = Jarldom.Renown.Comfort(comforts);
            int frigg = Mathf.RoundToInt(GreatWork.Blessing(this, "frigg")); // (2 with the Peak Temple raised to her)
            if (frigg > 0) ComfortPart += 3 * frigg;
            R.Flags["renown"] = Renown;
        }
        // What a material is worth to Odin's eye: its trader value, or a rough measure of how hard it is to come by.
        private static readonly Dictionary<string, double> Materials = new Dictionary<string, double>
        {
            ["Wood"] = 1, ["Stone"] = 1, ["Resin"] = 1, ["Flint"] = 1, ["LeatherScraps"] = 1, ["DeerHide"] = 2, ["FineWood"] = 2, ["RoundLog"] = 2, ["GreydwarfEye"] = 1,
            ["Copper"] = 4, ["Tin"] = 4, ["Bronze"] = 6, ["BronzeNails"] = 1, ["Iron"] = 10, ["IronNails"] = 2, ["Silver"] = 12, ["Obsidian"] = 4, ["Crystal"] = 6,
            ["BlackMetal"] = 15, ["BlackMarble"] = 4, ["YggdrasilWood"] = 4, ["Grausten"] = 3, ["Blackwood"] = 4, ["Flametal"] = 20, ["ElderBark"] = 3, ["Tar"] = 3,
            ["LinenCloth"] = 5, ["TrollHide"] = 4, ["WolfPelt"] = 4, ["LoxPelt"] = 6, ["Chain"] = 6, ["SurtlingCore"] = 5, ["Coins"] = 10,
        };
        public static double MaterialValue(ItemDrop item)
        {
            if (item == null) return 0;
            if (Materials.TryGetValue(item.name, out double v)) return v;
            return item.m_itemData.m_shared.m_value > 0 ? item.m_itemData.m_shared.m_value : 2;
        }

        // ---- rooms ----
        private readonly Queue<string> _roomQueue = new Queue<string>(); private readonly HashSet<string> _relooked = new HashSet<string>();
        public void CheckRoomSoon(string id) { if (!_roomQueue.Contains(id)) _roomQueue.Enqueue(id); RoomsSoon(); }
        private void RoomsDue()
        {
            // One room every check (so a big colony never stalls a frame); every room at least every five minutes.
            if (_roomQueue.Count == 0) foreach (Room r in R.Rooms.Where(r => Now - r.Checked > 300)) _roomQueue.Enqueue(r.Id);
            if (_roomQueue.Count == 0) return;
            string id = _roomQueue.Dequeue();
            Room room = R.Rooms.FirstOrDefault(r => r.Id == id);
            if (room == null) return;
            bool was = room.Ok; char grade = room.Grade; int points = room.Points; string failures = room.Failures;
            Check(room);
            if (room.Ok && !was) Log("room", "", Tables.Of(room.Type).Name.ToLower());
            // (a room that was fine and now fails: looked at again in a moment before it's believed, a piece may only be reloading)
            if (was && !room.Ok && !_relooked.Remove(room.Id)) { _relooked.Add(room.Id); room.Ok = true; room.Grade = grade; room.Points = points; room.Failures = failures; _roomQueue.Enqueue(room.Id); _nextRooms = Time.time + 15; return; }
            _relooked.Remove(room.Id);
            if (room.Ok != was || room.Grade != grade) Rerank();
            _dirty = true;
            if (_roomQueue.Count > 0) _nextRooms = Time.time + 1;
        }

        public const float ByreWall = 2f; // (metres of wall a longhouse shares with a byre for its warmth)
        public RoomVerdict Check(Room room)
        {
            room.Height = Ceiling(room);
            RoomFacts f = Measure(room);
            if (room.Type == RoomType.Longhouse && R.Rooms.Any(o => o.Type == RoomType.Byre && o.Ok && Geometry.SharedWall(room.Xz, o.Xz) >= ByreWall)) f.Comfort += 1; // (the beasts' warmth through the wall)
            RoomVerdict v = RoomRules.Judge(room.Type, f, room.God);
            room.Ok = v.Ok; room.Grade = v.Grade; room.Points = v.Points; room.Failures = v.Ok ? v.Capped : string.Join("; ", v.Failures); // (a working temple: what holds it at B, if anything)
            room.OpenEdges = string.Join(",", v.OpenEdges); room.Checked = Now;
            return v;
        }

        // How high the room goes: the lowest thing built over the floor (a roof, or the floor of the storey above), at the middle and
        // halfway to each corner. An open burial ground goes up 6 m.
        private static readonly RaycastHit[] Ups = new RaycastHit[16];
        public static float Ceiling(Room room)
        {
            if (Tables.Of(room.Type).Open) return 6f;
            var (cx, cz) = Geometry.Centre(room.Xz);
            var probes = new List<Vector2> { new Vector2(cx, cz) };
            for (int i = 0; i < room.Corners; i++) { var (x, z) = room.Corner(i); probes.Add(new Vector2((cx + x) / 2, (cz + z) / 2)); }
            var heights = new List<float>();
            foreach (Vector2 q in probes)
            {
                int n = Physics.RaycastNonAlloc(new Vector3(q.x, room.Floor + 1.2f, q.y), Vector3.up, Ups, 12f, PieceMask, QueryTriggerInteraction.Ignore);
                float best = float.MaxValue;
                for (int i = 0; i < n; i++) { Piece p = Ups[i].collider.GetComponentInParent<Piece>(); if (p != null && p.m_category != Piece.PieceCategory.Furniture && p.m_category != Piece.PieceCategory.Misc && Ups[i].distance < best) best = Ups[i].distance; }
                if (best < float.MaxValue) heights.Add(best + 1.2f);
            }
            if (heights.Count == 0) return 5f;
            heights.Sort();
            return Mathf.Clamp(heights[heights.Count / 2], 2.2f, 10f); // (the median: a beam or a hole doesn't decide it)
        }
        public static RoomFacts Measure(Room room)
        {
            var f = new RoomFacts();
            IList<float> xz = room.Xz;
            int n = room.Corners;
            f.Area = Geometry.Area(xz);
            float floor = room.Floor;
            RoomInfo info = Tables.Of(room.Type);
            // Walls: along each edge, something built low down and something higher up (at head height, or over a window), so a wall
            // with windows counts and a waist-high fence with nothing over it doesn't.
            var covered = new int[n]; var total = new int[n];
            foreach (var s in Geometry.EdgeSamples(xz, 0.5f))
            {
                total[s.edge]++;
                if (WallAt(xz, s.edge, s.x, s.z, floor)) covered[s.edge]++;
            }
            f.Walls = Enumerable.Range(0, n).Select(i => total[i] == 0 ? 0f : covered[i] / (float)total[i]).ToArray();
            if (info.Open) f.Walls = Enumerable.Repeat(1f, n).ToArray();
            // Roof: a 1 m grid inside, under cover.
            int under = 0, cells = 0;
            foreach (var (x, z) in Geometry.Grid(xz, 1f)) { cells++; if (Cover.IsUnderRoof(new Vector3(x, floor + 1.2f, z))) under++; }
            f.Roof = cells == 0 ? 0 : under / (float)cells;
            // What's inside.
            var (cx, cz) = Geometry.Centre(xz);
            float reach = 0; for (int i = 0; i < n; i++) reach = Mathf.Max(reach, Vector2.Distance(new Vector2(cx, cz), new Vector2(xz[i * 2], xz[i * 2 + 1])));
            var list = new List<Piece>();
            Piece.GetAllPiecesInRadius(new Vector3(cx, floor, cz), reach + 2f, list);
            var beds = new List<Vector3>(); var chests = new List<Vector3>();
            foreach (Piece p in list)
            {
                Vector3 at = p.transform.position;
                // Stairs or a ladder coming up into the room from the storey below: its way in, as a door is.
                string pn = Prefab(p.gameObject);
                if ((pn.Contains("stair") || pn.Contains("ladder")) && at.y < floor - 0.5f && at.y > floor - 4f && Geometry.Distance(xz, at.x, at.z) <= 1.5f) { f.Count["stairs"] = f.C("stairs") + 1; continue; }
                if (at.y < floor - 1f || at.y > floor + room.Height - 0.15f) continue; // (not the storey above)
                Door door = p.GetComponent<Door>();
                if (door != null)
                {
                    // A door in this room's walls: near its outline. Window shutters are doors to the game; not here.
                    if (!Prefab(p.gameObject).Contains("window") && Geometry.Distance(xz, at.x, at.z) <= 1.5f && EdgeDistance(xz, at.x, at.z) <= 1.5f)
                    { f.Doors++; if (door.m_keyItem != null) f.LockableDoors++; }
                    if (Kinds(p).Contains("gate") && Geometry.Distance(xz, at.x, at.z) <= 1.5f) f.Count["gate"] = f.C("gate") + 1; // (a byre's fence gate: in its walls or closing a stall)
                    continue;
                }
                if (!Geometry.Inside(xz, at.x, at.z)) continue;
                foreach (string kind in Kinds(p)) f.Count[kind] = f.C(kind) + 1;
                if (p.GetComponent<Bed>() != null) beds.Add(at);
                Container c = p.GetComponent<Container>();
                if (c != null && ContainerAccess.IsStorage(c))
                {
                    chests.Add(at);
                    foreach (ItemDrop.ItemData item in c.GetInventory().GetAllItems())
                        f.Value += item.m_dropPrefab != null && item.m_dropPrefab.name == "Coins" ? item.m_stack : item.m_shared.m_value * item.m_stack;
                }
            }
            f.Count["bedchest"] = beds.Count(b => chests.Any(c => Vector3.Distance(b, c) < 2.5f));
            f.Comfort = SE_Rested.CalculateComfortLevel(true, new Vector3(cx, floor + 0.5f, cz));
            f.FloorHeight = ZoneSystem.instance.GetGroundHeight(new Vector3(cx, floor, cz), out float ground) ? floor - ground : 0;
            return f;
        }
        // For tests: where along each edge the wall check finds no wall (what's hit low and high there).
        public static List<string> Gaps(Room room)
        {
            var list = new List<string>();
            foreach (var s in Geometry.EdgeSamples(room.Xz, 0.5f))
            {
                if (!WallAt(room.Xz, s.edge, s.x, s.z, room.Floor)) list.Add($"edge {s.edge} at {s.x:0.0},{s.z:0.0}");
            }
            return list;
        }
        // A wall at this point of an edge: something low and something higher, here or up to a metre outward (corners are marked a
        // little inside the walls).
        private static bool WallAt(IList<float> xz, int edge, float x, float z, float floor)
        {
            int n = xz.Count / 2, j = (edge + 1) % n;
            Vector2 a = new Vector2(xz[edge * 2], xz[edge * 2 + 1]), b = new Vector2(xz[j * 2], xz[j * 2 + 1]);
            Vector2 normal = new Vector2(b.y - a.y, a.x - b.x).normalized;
            var (cx, cz) = Geometry.Centre(xz);
            if (Vector2.Dot(normal, new Vector2(x - cx, z - cz)) < 0) normal = -normal; // (outward)
            foreach (float k in new[] { 0f, 0.5f, 1f })
            {
                float px = x + normal.x * k, pz = z + normal.y * k;
                if (Solid(new Vector3(px, floor + 0.7f, pz)) && (Solid(new Vector3(px, floor + 1.8f, pz)) || Solid(new Vector3(px, floor + 2.4f, pz)))) return true;
            }
            return false;
        }
        private static float EdgeDistance(IList<float> xz, float x, float z)
        {
            int n = xz.Count / 2; float best = float.MaxValue;
            for (int i = 0; i < n; i++) { int j = (i + 1) % n; best = Mathf.Min(best, Geometry.Segment(x, z, xz[i * 2], xz[i * 2 + 1], xz[j * 2], xz[j * 2 + 1])); }
            return best;
        }
        private static bool Solid(Vector3 at)
        {
            int count = Physics.OverlapSphereNonAlloc(at, 0.6f, Hits, PieceMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Piece p = Hits[i].GetComponentInParent<Piece>();
                if (p == null) { if (Hits[i].gameObject.layer == LayerMask.NameToLayer("static_solid")) return true; continue; }
                if (p.GetComponent<Door>() != null || p.GetComponent<WearNTear>() != null && p.m_category != Piece.PieceCategory.Furniture && p.m_category != Piece.PieceCategory.Misc) return true;
            }
            return false;
        }

        // What a piece counts as, for a room's requirements and grade.
        public static IEnumerable<string> Kinds(Piece p)
        {
            string n = Prefab(p.gameObject);
            if (p.GetComponent<Bed>() != null) yield return "bed";
            Container c = p.GetComponent<Container>();
            if (c != null && ContainerAccess.IsStorage(c) && c.m_privacy != Container.PrivacySetting.Private) yield return "chest"; // (one the villagers can open)
            foreach (Chair seat in Classes.SeatsOf(p)) yield return "seat"; // (a bench is three seats)
            if (Classes.IsHighSeat(p.gameObject)) yield return "highseat";
            if (Classes.IsTable(p)) yield return "table";
            // Fires that warm (any of them: campfire, hearth, bonfire, fire pit, brazier) are fires; torches, sconces and candles are lights.
            if (Classes.Warms(p)) { yield return "fire"; if (n.Contains("hearth")) yield return "hearth"; if (n.Contains("brazier")) yield return "brazier"; }
            else if (Classes.IsLight(p) || n.Contains("candle")) yield return "candle";
            if (n.Contains("banner")) yield return "banner";
            if (n.StartsWith("jarldomgilded")) yield return "gilded";
            if (n.Contains("raven")) { yield return "raven"; yield return "carving"; }
            else if (n.Contains("carving") || n.Contains("darkwood_wolf") || n.Contains("dragon") || n.Contains("statue")) yield return "carving";
            if (Classes.IsRug(n)) yield return "rug";
            // (a runestone, the colony's memorial stone; graves: a cairn - the game's stone pile - or any grave or tomb piece. Not runed
            // chairs and tables: they're furniture)
            bool memorial = n == Pieces.MemorialName.ToLowerInvariant();
            if (n.Contains("runestone") || memorial) yield return "runestone";
            if (n.Contains("grave") || n.Contains("tomb") || n == "stone_pile" || n.Contains("cairn") || memorial) yield return "grave";
            if (n.StartsWith(Pieces.AltarPrefix.ToLowerInvariant()))
            {
                string god = n.Substring(Pieces.AltarPrefix.Length);
                yield return "altar_" + god;
                if (p.GetComponent<Altar>() is Altar altar && altar.HoldsRelic) yield return "relic_" + god; // (its god's relic on it: the temple may grade past B)
            }
            if (n.Contains("forge") && p.GetComponent<CraftingStation>() != null) yield return "forge";
            if (n.Contains("workbench") && p.GetComponent<CraftingStation>() != null) yield return "workbench";
            // A station's extension counts where its own station's work is done (an anvil in the smithy, a spice rack in the brewhouse).
            StationExtension ext = p.GetComponent<StationExtension>();
            if (ext != null && ext.m_craftingStation != null) { string st = Prefab(ext.m_craftingStation.gameObject); yield return st.Contains("cauldron") ? "brewext" : st.Contains("forge") || st.Contains("workbench") ? "smithext" : "extension"; }
            if (n.Contains("cauldron") && p.GetComponent<CraftingStation>() != null) yield return "cauldron";
            if (p.GetComponent<Fermenter>() != null) yield return "fermenter";
            // (a bathhouse's: the Bathhouse mod's sauna stove, or the game's own hot tub, for its heat; its bucket, plunge tub and lockers)
            if (n == "bathhouse_stove" || n == "piece_bathtub") yield return "sauna";
            if (n == "bathhouse_bucket") yield return "bucket";
            if (n == "bathhouse_plungetub") yield return "plunge";
            if (n == "bathhouse_locker") yield return "locker";
            // (a byre's: Byre's trough and hay rack, its stall dividers, and the game's fence gates)
            if (n == "byretrough" || n == "byrehayrack") yield return "feeder";
            if (n == "byrestall") yield return "stall";
            if (n == "wood_fence_gate" || n == "wood_gate") yield return "gate";
            ItemStand stand = p.GetComponent<ItemStand>();
            if (stand != null)
            {
                yield return "itemstand";
                string held = p.GetComponent<ZNetView>()?.GetZDO()?.GetString(ZDOVars.s_item, "") ?? "";
                if (held.StartsWith("Trophy")) yield return "trophy";
            }
            if (n.StartsWith("trophy")) yield return "trophy";
        }

        // The room the point is in, of a kind (the great hall, a longhouse...).
        // In a house of storeys, the room whose floor is nearest below the point.
        public Room RoomAt(Vector3 p, RoomType? type = null) => RoomRules.At(R.Rooms, p.x, p.y, p.z, type);
        public Room Best(RoomType type) => R.Rooms.Where(r => r.Type == type && r.Ok).OrderByDescending(r => r.Points).FirstOrDefault() ?? R.Rooms.FirstOrDefault(r => r.Type == type);
        public Vector3 Centre(Room r) { var (x, z) = Geometry.Centre(r.Xz); return new Vector3(x, r.Floor, z); }
    }
}
