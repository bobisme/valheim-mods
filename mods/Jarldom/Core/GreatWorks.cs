using System;
using System.Collections.Generic;
using System.Linq;

namespace Jarldom
{
    // Great Works: what the whole colony raises together once it's a village, chosen at the Thing; a jarl shows where it stands (the
    // hall tab), its plan is laid out for the builders (BuildOrders' ghosts), and when every piece stands it's done for good: a saga
    // entry, renown, a name on the jarls' maps and a lasting gift. Kept in the colony's flags ("gw:<work>" = its stage, and where).
    public static class GreatWorks
    {
        public const int Promised = 1, Laid = 2, Done = 3;
        public const string Lighthouse = "lighthouse";
        public static string Key(string work) => "gw:" + work;
        public static string AtKey(string work) => "gw:" + work + ":at";     // (packed x, z: TrueLegend.Pack)
        public static string YKey(string work) => "gw:" + work + ":y";       // (the ground's height, in tenths of a metre)
        public static string XKey(string work) => "gw:" + work + ":x";       // (where exactly, in tenths of a metre: the plan's parts are checked to half a metre)
        public static string ZKey(string work) => "gw:" + work + ":z";
        public static string YawKey(string work) => "gw:" + work + ":yaw";   // (degrees: its front, toward the sea)
        public static string BuiltKey(string work) => "gw:" + work + ":built";
        public static string DayKey(string work) => "gw:" + work + ":day";
        public static int Stage(IDictionary<string, int> flags, string work) => flags.TryGetValue(Key(work), out int s) ? s : 0;

        // (prefab, x, y, z from the foot of its middle, +z its front; yaw in degrees; Up: which way the piece's own up points, for a
        // leaning log (0, 0, 0: straight up))
        public struct Part
        {
            public string Prefab; public float X, Y, Z, Yaw, UpX, UpY, UpZ;
            public Part(string p, float x, float y, float z, float yaw) { Prefab = p; X = x; Y = y; Z = z; Yaw = yaw; UpX = 0; UpY = 0; UpZ = 0; }
            public bool Leans => UpX != 0 || UpZ != 0;
        }
        // A log from one point to another (its middle between them, leaning along the line).
        private static Part Log(string prefab, float x0, float y0, float z0, float x1, float y1, float z1) =>
            new Part(prefab, (x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2, 0) { UpX = x1 - x0, UpY = y1 - y0, UpZ = z1 - z0 };

        // The Lighthouse: a timber tower on a stone plinth. A keeper's hut of logs and planks on a course of stone, with a door; above it
        // two storeys of log trestle, narrowing a little as they rise, with a plank floor at each; ladders in switchbacks inside all the
        // way up (a landing at each turn); a railed gallery with torches at its corners and the beacon fire on a cairn of stones, 14 m
        // up. Its workbench and stonecutter stand on the plinth beside the hut, so the builders reach every piece.
        public static List<Part> LighthouseParts()
        {
            var parts = new List<Part>();
            void P(string prefab, float x, float y, float z, float yaw = 0) => parts.Add(new Part(prefab, x, y, z, yaw));
            const float deck = 0.25f, storey = 4f; // (the plinth's top; each floor above the last)
            for (int x = -2; x <= 2; x += 2) for (int z = -2; z <= 2; z += 2) P("stone_floor_2x2", x, deck - 0.5f, z);
            P("piece_workbench", 2.95f, deck, 0, 90); P("piece_stonecutter", -3.05f, deck, 0, 270);
            // the hut: corner logs; a course of stone, planks, a half wall (the door at the front right, planks over it)
            foreach (var (sx, sz) in Corners) P("wood_pole_log_4", sx * 2f, deck + 2.22f, sz * 2f);
            P("stone_wall_2x1", -1, deck + 0.5f, 2); P("woodwall", -1, deck + 2.01f, 2); P("wood_wall_half", -1, deck + 3.5f, 2);
            P("wood_door", 1, deck + 1.01f, 2); P("woodwall", 1, deck + 3.01f, 2);
            foreach (float a in new[] { -1f, 1f })
            {
                P("stone_wall_2x1", a, deck + 0.5f, -2); P("woodwall", a, deck + 2.01f, -2); P("wood_wall_half", a, deck + 3.5f, -2);
                foreach (float side in new[] { -2f, 2f }) { P("stone_wall_2x1", side, deck + 0.5f, a, 90); P("woodwall", side, deck + 2.01f, a, 90); P("wood_wall_half", side, deck + 3.5f, a, 90); }
            }
            Ring(parts, "wood_wall_log_4x0.5", 2f, deck + storey + 0.27f);
            // three flights of two ladders, one over another: up toward the back, a landing, back up toward the front; a floor at each
            // storey, open over the second ladder, the landing and the top of the first (head room all the way up)
            float[] half = { 2f, 1.75f, 1.5f };
            for (int s = 0; s < 3; s++)
            {
                float y0 = deck + storey * s;
                P("wood_stepladder", 0.5f, y0, 0); P("wood_stepladder", -0.5f, y0 + 2, 0, 180);
                P("wood_floor_1x1", -0.5f, y0 + 1.92f, -1.5f); P("wood_floor_1x1", 0.5f, y0 + 1.92f, -1.5f);
                for (float x = -1.5f; x <= 1.5f; x += 1f) for (float z = -1.5f; z <= 1.5f; z += 1f)
                    if (!Stairwell(x, z)) P("wood_floor_1x1", x, y0 + storey - 0.08f, z);
                if (s == 0) continue;
                // the trestle storey this flight climbs: leaning corner logs, a ring halfway and one on top
                float lo = half[s - 1], hi = half[s], yb = deck + storey * s;
                foreach (var (sx, sz) in Corners) parts.Add(Log("wood_pole_log_4", sx * lo, yb, sz * lo, sx * hi, yb + storey, sz * hi));
                Ring(parts, "wood_wall_log_4x0.5", (lo + hi) / 2, yb + storey / 2, open: true);
                Ring(parts, "wood_wall_log_4x0.5", hi, yb + storey + 0.27f, open: true); // (no log at the back, over the landing below)
            }
            // the gallery on top: posts, a rail, torches; over it, on four logs, the fire deck with the beacon's bonfire (3 m of head room
            // under it, over the stairhead)
            float onDeck = deck + storey * 3 + 0.08f;
            foreach (var (sx, sz) in Corners) { P("wood_pole", sx * 1.85f, onDeck + 0.5f, sz * 1.85f); P("piece_groundtorch_wood", sx * 1.85f, onDeck + 1.65f, sz * 1.85f); }
            Ring(parts, "wood_wall_log_4x0.5", 1.85f, onDeck + 0.8f);
            foreach (var (sx, sz) in Corners) { P("wood_pole_log", sx * 1.4f, onDeck + 1.11f, sz * 1.4f); P("wood_pole", sx * 1.4f, onDeck + 2.72f, sz * 1.4f); }
            float fireDeck = onDeck + 3.22f;
            Ring(parts, "wood_wall_log_4x0.5", 1.4f, fireDeck - 0.27f);
            for (int x = -2; x <= 2; x += 2) for (int z = -2; z <= 2; z += 2) if (x != 0 || z != 0) P("wood_floor", x, fireDeck - 0.08f, z); // (the bonfire sits in the middle)
            P("bonfire", 0, fireDeck + 0.5f, 0);
            return parts;
        }
        // Where each floor is open for the stair (cells of a metre, by their middles): over the second ladder, over the landing, and over the
        // top of the first ladder.
        public static bool Stairwell(float x, float z) => x < 0 && x > -1 && z > -1 && z < 1 || z < -1 && z > -2 && Math.Abs(x) < 1 || x > 0 && x < 1 && z > -1 && z < 0;
        private static readonly (int, int)[] Corners = { (1, 1), (1, -1), (-1, 1), (-1, -1) };
        // Four logs lying round a square (half its side out from the middle) at a height.
        private static void Ring(List<Part> parts, string log, float half, float y, bool open = false)
        {
            parts.Add(new Part(log, 0, y, half, 0)); if (!open) parts.Add(new Part(log, 0, y, -half, 0));
            parts.Add(new Part(log, half, y, 0, 90)); parts.Add(new Part(log, -half, y, 0, 90));
        }
        public const float Footing = 4f; // (half its footing, the gallery's overhang with it: the outline must hold this square)
        // Where a work stands in an outline drawn for it: the middle of the outline, if its footing fits inside ("" or why not).
        public static string Site(IList<float> xz, out float x, out float z)
        {
            x = z = 0;
            int n = xz.Count / 2;
            if (n < 3) return "draw its outline first";
            for (int i = 0; i < n; i++) { x += xz[i * 2]; z += xz[i * 2 + 1]; }
            x /= n; z /= n;
            foreach (var (sx, sz) in Corners) if (!Geometry.Inside(xz, x + sx * Footing, z + sz * Footing)) return $"the outline is too small: it needs {Footing * 2:0} × {Footing * 2:0} m clear in its middle";
            return "";
        }
        public static List<Part> Parts(string work) => work == Lighthouse ? LighthouseParts() : new List<Part>();

        // A part where it stands in the world: turned with the work about its foot, then moved there.
        public static (float x, float y, float z, float yaw) Place(Part p, float x, float y, float z, float yaw)
        {
            double a = yaw * Math.PI / 180.0, cos = Math.Cos(a), sin = Math.Sin(a);
            return ((float)(x + p.X * cos + p.Z * sin), y + p.Y, (float)(z - p.X * sin + p.Z * cos), (p.Yaw + yaw) % 360f);
        }
        // What it takes from the stores, by material (what each piece costs the game's way).
        private static readonly Dictionary<string, (string item, int n)[]> Cost = new Dictionary<string, (string, int)[]>
        {
            ["wood_floor_1x1"] = new[] { ("Wood", 1) }, ["wood_wall_half"] = new[] { ("Wood", 1) }, ["stone_pile"] = new[] { ("Stone", 50) }, ["fire_pit"] = new[] { ("Stone", 5), ("Wood", 2) }, ["wood_floor"] = new[] { ("Wood", 2) }, ["wood_pole"] = new[] { ("Wood", 1) }, ["wood_beam_45"] = new[] { ("Wood", 2) },
            ["wood_door"] = new[] { ("Wood", 4) }, ["wood_stepladder"] = new[] { ("Wood", 2) }, ["wood_wall_log_4x0.5"] = new[] { ("RoundLog", 2) },
            ["stone_wall_1x1"] = new[] { ("Stone", 3) }, ["stone_wall_2x1"] = new[] { ("Stone", 4) }, ["stone_wall_4x2"] = new[] { ("Stone", 6) }, ["stone_floor_2x2"] = new[] { ("Stone", 6) }, ["stone_pillar"] = new[] { ("Stone", 5) },
            ["piece_workbench"] = new[] { ("Wood", 10) }, ["piece_stonecutter"] = new[] { ("Wood", 10), ("Iron", 2), ("Stone", 4) },
            ["bonfire"] = new[] { ("SurtlingCore", 1), ("ElderBark", 5), ("FineWood", 5), ("RoundLog", 5) }, ["piece_groundtorch_wood"] = new[] { ("Wood", 2), ("Resin", 2) }, ["wood_pole_log"] = new[] { ("RoundLog", 1) },
        };
        public static Dictionary<string, int> Needs(string work)
        {
            var all = new Dictionary<string, int>();
            foreach (var p in Parts(work)) if (Cost.TryGetValue(p.Prefab, out var c)) foreach (var (item, n) in c) all[item] = (all.TryGetValue(item, out int had) ? had : 0) + n;
            return all;
        }
        public static string NeedsText(string work) => string.Join(", ", Needs(work).OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value} {Item(kv.Key)}"));
        private static string Item(string prefab) => prefab == "RoundLog" ? "core wood" : prefab == "FineWood" ? "fine wood" : prefab == "ElderBark" ? "ancient bark" : prefab == "SurtlingCore" ? "surtling core" : prefab.ToLower();
        public static string Name(string work) => work == Lighthouse ? "the Lighthouse" : work;
        public static string Title(string work) => work == Lighthouse ? "The Lighthouse" : work; // (to start a sentence)
        public static string Proper(string work, string colony) => (work == Lighthouse ? "Lighthouse" : work) + " of " + colony; // (its name on the map)
        public static string Gift(string work) => work == Lighthouse ? "Its fire is the colony's beacon, and ships on the colony's trade runs are lost half as often." : "";
        public const float LossAtSea = 0.5f; // (the Lighthouse: trade runs at sea lost this much as often)
        public const int Renown = 40;
    }
}
