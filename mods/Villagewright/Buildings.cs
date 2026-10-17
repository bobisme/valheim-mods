using System;
using System.Collections.Generic;
using System.Linq;
using Hallwright;
using UnityEngine;
using Village;

namespace Villagewright
{
    // Each lot's building, designed by Hallwright's geometry (HallLayout / StaveLayout) and turned into game pieces in the world.
    internal sealed class PiecePose
    {
        internal string Prefab, Role;
        internal Vector3 Position;
        internal Quaternion Rotation;
    }

    internal sealed class Building
    {
        internal Lot Lot;
        internal HallLayout.Design Design;
        internal readonly List<PiecePose> Pieces = new List<PiecePose>();
        internal string Problem;
    }

    internal static class Buildings
    {
        private static readonly Dictionary<string, Vector3[]> Snaps = new Dictionary<string, Vector3[]>();
        internal static GameObject Prefab(string name) => ZNetScene.instance != null && name != null ? ZNetScene.instance.GetPrefab(name) : null;
        internal static bool Has(string name) => Prefab(name) != null;

        private static Vector3[] SnapsOf(string name)
        {
            if (Snaps.TryGetValue(name, out var s)) return s;
            GameObject prefab = Prefab(name) ?? throw new ArgumentException("This game has no " + name + ".");
            var list = new List<Transform>(); prefab.GetComponent<Piece>().GetSnapPoints(list);
            s = list.Where(t => t != null).Select(t => prefab.transform.InverseTransformPoint(t.position)).ToArray();
            Snaps[name] = s; return s;
        }
        private static Vector3[] Ends(string name)
        {
            Vector3[] s = SnapsOf(name);
            if (s.Length < 2) throw new ArgumentException("No usable beam snap points on " + name + ".");
            float[] ranges = { s.Max(v => v.x) - s.Min(v => v.x), s.Max(v => v.y) - s.Min(v => v.y), s.Max(v => v.z) - s.Min(v => v.z) };
            int axis = ranges[1] > ranges[0] ? 1 : 0; if (ranges[2] > ranges[axis]) axis = 2;
            float lo = s.Min(v => v[axis]), hi = s.Max(v => v[axis]);
            Vector3 Mean(IEnumerable<Vector3> values) { Vector3 p = Vector3.zero; int n = 0; foreach (var v in values) { p += v; n++; } return p / n; }
            return new[] { Mean(s.Where(v => Mathf.Abs(v[axis] - lo) < 0.001f)), Mean(s.Where(v => Mathf.Abs(v[axis] - hi) < 0.001f)) };
        }
        private static Vector3 Anchor(string name, HallLayout.Anchor kind)
        {
            Vector3[] snaps = SnapsOf(name);
            if (snaps.Length == 0) throw new ArgumentException("Missing snap anchor on " + name + ".");
            if (kind == HallLayout.Anchor.Point) return snaps[0];
            float y = snaps.Min(v => v.y);
            var low = snaps.Where(v => Mathf.Abs(v.y - y) < 0.001f).ToArray();
            Vector3 anchor = Vector3.zero; foreach (var v in low) anchor += v; return anchor / low.Length;
        }
        // The top of a beam's shape above its snap line (Hallwright's joist drop).
        private static float TopOf(string name)
        {
            GameObject prefab = Prefab(name); float top = 0.15f;
            foreach (var f in prefab.GetComponentsInChildren<MeshFilter>(true))
            {
                if (f.sharedMesh == null) continue;
                Bounds b = f.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 c = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                    top = Mathf.Max(top, prefab.transform.InverseTransformPoint(f.transform.TransformPoint(c)).y);
                }
            }
            return Mathf.Min(top, 0.4f);
        }

        private static HallLayout.Kit Kit(Lot lot)
        {
            var kit = new HallLayout.Kit();
            bool grand = lot.Use == Use.Hall || lot.Use == Use.Temple;
            if (lot.Wall >= 3 && (lot.Use == Use.Hall || lot.Use == Use.Boathouse || lot.Use == Use.Storehouse) && Has("wood_gate")) { kit.Door = "wood_gate"; kit.DoorHeight = 3; }
            if ((grand || lot.Detail >= 1) && Has("wood_pole_log") && Has("wood_wall_log")) { kit.Post = "wood_pole_log"; kit.Beam = "wood_wall_log"; kit.SpanCells = 5; }
            if (lot.Detail >= 2)
            {
                if (Has("darkwood_beam")) kit.Beam = "darkwood_beam";
                if (Has("darkwood_raven")) kit.Raven = "darkwood_raven";
                if (Has("darkwood_arch")) kit.Arch = "darkwood_arch";
                if (Has("darkwood_decowall")) kit.Lattice = "darkwood_decowall";
            }
            if (lot.Style == Style.Stave && Has("woodiron_pole")) kit.LoadPost = "woodiron_pole";
            if (Has("wood_window")) kit.Window = "wood_window";
            if (Has("wood_floor_1x1")) kit.SmallFloor = "wood_floor_1x1";
            if (Has("stone_fence")) kit.StoneFence = "stone_fence";
            bool steep = lot.Steep;
            kit.Roof = steep ? "wood_roof_45" : "wood_roof"; kit.Ridge = steep ? "wood_roof_top_45" : "wood_roof_top"; kit.Wedge = steep ? "wood_wall_roof_45" : "wood_wall_roof_a";
            kit.Slope = steep ? 1 : 0.5;
            kit.JoistDrop = TopOf(kit.Beam) + 0.1;
            Vector3[] ends = Ends(kit.Beam); kit.BeamLength = (ends[1] - ends[0]).magnitude;
            ends = Ends(kit.ShortBeam); kit.ShortLength = (ends[1] - ends[0]).magnitude;
            return kit;
        }

        internal static Building Design(Lot lot)
        {
            var b = new Building { Lot = lot };
            try
            {
                var kit = Kit(lot);
                var corners = new[] { new V3(0, 0, 0), new V3(lot.Width, 0, 0), new V3(lot.Width, 0, lot.Depth), new V3(0, 0, lot.Depth) };
                bool norse = lot.Style == Style.Longhouse;
                string finial = lot.Detail >= 2 ? (Has("wood_dragon1") ? "wood_dragon1" : kit.Raven) : null;
                var details = new HallLayout.Details
                {
                    Norse = norse, Benches = norse, StoneFooting = norse && kit.StoneFence != null, Hearth = norse && Has("hearth") ? "hearth" : null,
                    Storeys = 1, Shutters = !norse && kit.Window != null, Sweep = lot.Use == Use.Hall, Finial = finial,
                    DoorPoints = new[] { new V3(lot.DoorAlong, 0, 0) },
                };
                b.Design = lot.Style == Style.Stave
                    ? StaveLayout.Plan(corners, kit, 3, lot.Detail, 0, details, new StaveLayout.Options { Height = lot.Width >= 16 ? 8 : 6, Crowns = 1, Gallery = true })
                    : HallLayout.Plan(corners, kit, lot.Wall, lot.Detail, 0, false, details);
                Quaternion frame = Quaternion.Euler(0, (float)lot.Yaw, 0);
                Vector3 World(V3 p) => new Vector3((float)lot.Origin.X, (float)lot.Floor, (float)lot.Origin.Z) + frame * new Vector3((float)p.X, (float)p.Y, (float)p.Z);
                void Add(HallLayout.Part part)
                {
                    Vector3 target = World(part.At), position; Quaternion rotation;
                    if (part.Kind == HallLayout.Anchor.Segment)
                    {
                        Vector3[] ends = Ends(part.Prefab); Vector3 end = World(part.End);
                        if (Mathf.Abs((end - target).magnitude - (ends[1] - ends[0]).magnitude) > 0.01f) throw new ArgumentException("A beam would need stretching.");
                        rotation = Quaternion.FromToRotation(ends[1] - ends[0], end - target);
                        position = (target + end) * 0.5f - rotation * ((ends[0] + ends[1]) * 0.5f);
                    }
                    else if (Furniture.Contains(part.Role)) { rotation = frame * Quaternion.Euler(0, (float)part.Yaw, 0); position = target; }
                    else { rotation = frame * Quaternion.Euler(0, (float)part.Yaw, 0); position = target - rotation * Anchor(part.Prefab, part.Kind); }
                    if (b.Pieces.Any(p => p.Prefab == part.Prefab && (p.Position - position).sqrMagnitude < 0.01f && Quaternion.Angle(p.Rotation, rotation) < 5)) return;
                    b.Pieces.Add(new PiecePose { Prefab = part.Prefab, Position = position, Rotation = rotation, Role = part.Role });
                }
                // Foundations: the lot is levelled to its pad, so a course of stone (timber halls and the temple) or a post at each corner
                // (longhouses, whose floor is dug in) reaches into the ground.
                if (!norse && Has("stone_floor_2x2"))
                {
                    double depth = lot.Floor - lot.Pad;
                    int count = Math.Max(1, (int)Math.Ceiling(depth + 0.1));
                    foreach (var c in b.Design.Cells) for (int n = 0; n < count; n++)
                        Add(new HallLayout.Part { Prefab = "stone_floor_2x2", At = new V3(c.X * 2 + 1, -n - 1.06, c.Z * 2 + 1), Kind = HallLayout.Anchor.Bottom, Role = "foundation" });
                }
                else foreach (V3 v in b.Design.Vertices)
                    if (!HallStairs.Blocks(b.Design, v)) Add(new HallLayout.Part { Prefab = kit.Post, At = new V3(v.X, -2, v.Z), Kind = HallLayout.Anchor.Bottom, Role = "foundation" });
                foreach (var part in b.Design.Parts) Add(part);
                // Ridge posts under wide roofs (Hallwright adds these when its support estimate says the roof would fall; we have no
                // estimate here, so every wing wider than 6 m gets them, where they don't block a door, the stairs or a chimney).
                if (!norse && lot.Style != Style.Stave)
                    foreach (var wing in b.Design.Wings.Where(w => w.Width >= 4))
                    {
                        double length = wing.Length * 2, peak = wing.RoofBase + wing.Width * kit.Slope + wing.TierLift;
                        for (double v = length <= 2 ? 1 : 2; v < length; v += 4)
                        {
                            V3 at = wing.At(wing.Width, 0, v);
                            if (HallStairs.Blocks(b.Design, at) || HallDoors.Blocks(b.Design, at) || HallLayout.ChimneyBlocks(b.Design, at)) continue;
                            int count = Math.Max(1, (int)Math.Ceiling(peak / 2));
                            for (int n = 0; n < count; n++) Add(new HallLayout.Part { Prefab = kit.Post, At = new V3(at.X, peak - 2 * (n + 1), at.Z), Kind = HallLayout.Anchor.Bottom, Role = "support" });
                        }
                    }
                Furnish(b, Add);
            }
            catch (ArgumentException ex) { b.Problem = ex.Message; b.Pieces.Clear(); }
            return b;
        }

        // Furniture on the floor where nothing of the building stands: a bed and a chest against the walls at the back, a table with
        // benches, a rug. Cells are kept clear by the door, the hearth, platforms, posts and stairs.
        private static readonly HashSet<string> Furniture = new HashSet<string> { "bed", "chest", "table", "bench", "barrel", "rug" };
        private static readonly HashSet<string> Blocking = new HashSet<string> { "platform", "platform front", "hearth", "post", "support", "stave", "storey post", "tier post", "interior stair", "aisle post", "chimney", "foundation" };
        private static void Furnish(Building b, Action<HallLayout.Part> add)
        {
            var lot = b.Lot; var d = b.Design;
            if (lot.Use != Use.House && lot.Use != Use.Hall && lot.Use != Use.Smithy && lot.Use != Use.Storehouse) return;
            var cells = new HashSet<HallLayout.Cell>(d.Cells);
            var busy = new HashSet<HallLayout.Cell>();
            var platforms = new HashSet<HallLayout.Cell>();
            foreach (var part in d.Parts)
            {
                if (!Blocking.Contains(part.Role) || part.At.Y > 2.5) continue;
                if (part.Role == "hearth")
                {
                    // The hearth is 4 x 3 m (turned with it), centred on a corner or a cell: every cell it reaches, with room to pass.
                    bool turned = Math.Abs(Math.Round(part.Yaw / 90)) % 2 == 1;
                    double hx = (turned ? 1.5 : 2) + 0.5, hz = (turned ? 2 : 1.5) + 0.5;
                    for (int x = (int)Math.Floor((part.At.X - hx) / 2); x <= (int)Math.Floor((part.At.X + hx - 1e-6) / 2); x++)
                        for (int z = (int)Math.Floor((part.At.Z - hz) / 2); z <= (int)Math.Floor((part.At.Z + hz - 1e-6) / 2); z++)
                            busy.Add(new HallLayout.Cell(x, z));
                    continue;
                }
                double fx = part.At.X / 2, fz = part.At.Z / 2;
                int cx = (int)Math.Floor(fx), cz = (int)Math.Floor(fz);
                bool onX = Math.Abs(fx - Math.Round(fx)) < 0.2, onZ = Math.Abs(fz - Math.Round(fz)) < 0.2;
                if (part.Role == "platform" && part.Prefab == "wood_floor") platforms.Add(new HallLayout.Cell(cx, cz));
                if (onX && onZ)
                {
                    // A post on a grid corner: it stands in the room only where all four cells round it are floor (else it's in a wall).
                    int x = (int)Math.Round(fx), z = (int)Math.Round(fz);
                    var round = new[] { new HallLayout.Cell(x, z), new HallLayout.Cell(x - 1, z), new HallLayout.Cell(x, z - 1), new HallLayout.Cell(x - 1, z - 1) };
                    if (round.All(cells.Contains)) foreach (var c in round) busy.Add(c);
                }
                else if (onX || onZ) { if (cells.Contains(new HallLayout.Cell(cx, cz)) && !(onX ? !cells.Contains(new HallLayout.Cell(cx - 1, cz)) : !cells.Contains(new HallLayout.Cell(cx, cz - 1)))) busy.Add(new HallLayout.Cell(cx, cz)); }
                else busy.Add(new HallLayout.Cell(cx, cz));
            }
            foreach (var e in d.Entrances)
                foreach (var c in d.Cells) { double dx = c.X * 2 + 1 - e.At.X, dz = c.Z * 2 + 1 - e.At.Z; if (dx * dx + dz * dz < 2.6 * 2.6) busy.Add(c); }
            bool Free(int x, int z) { var c = new HallLayout.Cell(x, z); return cells.Contains(c) && !busy.Contains(c); }
            void Take(int x, int z) => busy.Add(new HallLayout.Cell(x, z));
            int maxZ = d.Cells.Max(c => c.Z), maxX = d.Cells.Max(c => c.X);
            var rng = new Village.Rng((int)(Village.Rng.Hash(lot.Name + lot.Origin) & 0x7fffffff), "furnish");

            // Beds: on the longhouse's raised platforms, as the Norse slept; else two cells along the back wall (or a gable wall).
            bool bed = lot.Use == Use.House || lot.Use == Use.Hall;
            int beds = lot.Use == Use.Hall ? 6 : lot.Width * lot.Depth >= 80 ? 2 : 1;
            var usedPlatform = new HashSet<HallLayout.Cell>();
            var pairs = new List<(HallLayout.Cell a, HallLayout.Cell b)>();
            foreach (var c in platforms.OrderBy(c => c.X).ThenBy(c => c.Z).ToList())
                foreach (var n in new[] { new HallLayout.Cell(c.X + 1, c.Z), new HallLayout.Cell(c.X, c.Z + 1) })
                {
                    if (usedPlatform.Contains(c) || !platforms.Contains(n) || usedPlatform.Contains(n)) continue;
                    pairs.Add((c, n)); usedPlatform.Add(c); usedPlatform.Add(n); break;
                }
            // Spread along the platforms, not bunched at one end.
            int wanted = bed ? Math.Min(beds, pairs.Count) : 0;
            for (int i = 0; i < wanted; i++)
            {
                var (c, n) = pairs[(int)((i + 0.5) * pairs.Count / wanted)];
                bool alongX = n.X != c.X;
                add(new HallLayout.Part { Prefab = "bed", At = new V3(alongX ? c.X * 2 + 2 : c.X * 2 + 1, 0.5, alongX ? c.Z * 2 + 1 : c.Z * 2 + 2), Kind = HallLayout.Anchor.Point, Yaw = alongX ? 90 : 0, Role = "bed" });
                beds--;
            }
            // On the floor: two free cells side by side against an outside wall, the bed's length along it, a chest beside it.
            foreach (var c in d.Cells.OrderByDescending(c => c.Z).ThenBy(c => c.X).ToList())
            {
                if (!bed || beds == 0) break;
                foreach (bool alongX in new[] { true, false })
                {
                    var n = alongX ? new HallLayout.Cell(c.X + 1, c.Z) : new HallLayout.Cell(c.X, c.Z + 1);
                    if (!Free(c.X, c.Z) || !Free(n.X, n.Z)) continue;
                    // Which side is the wall: both cells must have it.
                    int wx = 0, wz = 0;
                    if (alongX) { if (!cells.Contains(new HallLayout.Cell(c.X, c.Z + 1)) && !cells.Contains(new HallLayout.Cell(n.X, n.Z + 1))) wz = 1; else if (!cells.Contains(new HallLayout.Cell(c.X, c.Z - 1)) && !cells.Contains(new HallLayout.Cell(n.X, n.Z - 1))) wz = -1; else continue; }
                    else { if (!cells.Contains(new HallLayout.Cell(c.X + 1, c.Z)) && !cells.Contains(new HallLayout.Cell(n.X + 1, n.Z))) wx = 1; else if (!cells.Contains(new HallLayout.Cell(c.X - 1, c.Z)) && !cells.Contains(new HallLayout.Cell(n.X - 1, n.Z))) wx = -1; else continue; }
                    double mx = alongX ? c.X * 2 + 2 : c.X * 2 + 1 + wx * 0.05, mz = alongX ? c.Z * 2 + 1 + wz * 0.05 : c.Z * 2 + 2;
                    add(new HallLayout.Part { Prefab = "bed", At = new V3(mx, 0, mz), Kind = HallLayout.Anchor.Point, Yaw = alongX ? 90 : 0, Role = "bed" });
                    Take(c.X, c.Z); Take(n.X, n.Z); beds--;
                    // A chest at its foot, back to the wall.
                    var foot = alongX ? new HallLayout.Cell(c.X + 2, c.Z) : new HallLayout.Cell(c.X, c.Z + 2);
                    if (Free(foot.X, foot.Z))
                    {
                        add(new HallLayout.Part { Prefab = "piece_chest_wood", At = new V3(foot.X * 2 + 1 + wx * 0.5, 0, foot.Z * 2 + 1 + wz * 0.5), Kind = HallLayout.Anchor.Point, Yaw = wz == 1 ? 180 : wz == -1 ? 0 : wx == 1 ? 270 : 90, Role = "chest" });
                        Take(foot.X, foot.Z);
                    }
                    break;
                }
            }
            // A table with a bench each side, in a free cell with free cells either side of it along z.
            int tables = lot.Use == Use.Hall ? 4 : lot.Use == Use.Storehouse ? 0 : 1;
            foreach (var c in d.Cells.OrderBy(c => rng.Unit()).ToList())
            {
                if (tables == 0) break;
                if (!Free(c.X, c.Z)) continue;
                add(new HallLayout.Part { Prefab = "piece_table", At = new V3(c.X * 2 + 1, 0, c.Z * 2 + 1), Kind = HallLayout.Anchor.Point, Role = "table" });
                add(new HallLayout.Part { Prefab = "piece_bench01", At = new V3(c.X * 2 + 1, 0, c.Z * 2 + 1 - 0.95), Kind = HallLayout.Anchor.Point, Role = "bench" });
                add(new HallLayout.Part { Prefab = "piece_bench01", At = new V3(c.X * 2 + 1, 0, c.Z * 2 + 1 + 0.95), Kind = HallLayout.Anchor.Point, Yaw = 180, Role = "bench" });
                Take(c.X, c.Z); tables--;
            }
            // Storehouses: barrels and chests along the walls.
            if (lot.Use == Use.Storehouse || lot.Use == Use.Smithy)
                foreach (var c in d.Cells.Where(c => Free(c.X, c.Z) && (c.X == 0 || c.X == maxX)).Take(4))
                {
                    add(new HallLayout.Part { Prefab = "piece_chest_barrel", At = new V3(c.X * 2 + 1, 0, c.Z * 2 + 0.6), Kind = HallLayout.Anchor.Point, Role = "barrel" });
                    add(new HallLayout.Part { Prefab = "piece_chest_barrel", At = new V3(c.X * 2 + 1, 0, c.Z * 2 + 1.5), Kind = HallLayout.Anchor.Point, Role = "barrel" });
                    Take(c.X, c.Z);
                }
            // A fur on the floor.
            var rugAt = d.Cells.FirstOrDefault(c => Free(c.X, c.Z));
            if (lot.Use == Use.House && cells.Contains(rugAt) && Free(rugAt.X, rugAt.Z))
                add(new HallLayout.Part { Prefab = rng.Chance(0.5) ? "rug_fur" : "rug_deer", At = new V3(rugAt.X * 2 + 1, 0, rugAt.Z * 2 + 1), Kind = HallLayout.Anchor.Point, Role = "rug" });
        }

        // A longhouse's dug ground at a world point: its height, or null where it leaves the ground as it is.
        internal static double? Dig(Building b, double x, double z, double ground)
        {
            if (b.Design == null || b.Lot.Style != Style.Longhouse || b.Lot.Sink <= 0) return null;
            P2 l = b.Lot.Local(new P2(x, z));
            if (l.X < -12 || l.Z < -12 || l.X > b.Lot.Width + 12 || l.Z > b.Lot.Depth + 12) return null;
            var t = HallExcavation.Norse(b.Design, l.X, l.Z, b.Lot.Sink);
            if (!t.HasValue) return null;
            return HallExcavation.Settle(ground, b.Lot.Floor + t.Value.y, t.Value.mode);
        }
    }
}
