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

        // The Lighthouse: a stone base 8 m square and 8 m high (buttressed corners rising as turrets, battlements, a door at the front
        // right), its workbench and stonecutter on the ground floor (so every piece up to the fire is in their reach); inside, ladders in
        // switchbacks by a plank floor to its roof; on the roof a stone shaft 4 m square and 8 m high, a wooden stair winding up its
        // outside to a braced, railed gallery with torches at its corners; and the bonfire on top, 17 m up.
        public static List<Part> LighthouseParts()
        {
            var parts = new List<Part>();
            void P(string prefab, float x, float y, float z, float yaw = 0) => parts.Add(new Part(prefab, x, y, z, yaw));
            // the base: its stone floor, four storeys of wall (the door's corner below), buttresses, battlements
            for (int x = -2; x <= 2; x += 2) for (int z = -2; z <= 2; z += 2) P("stone_floor_2x2", x, -0.5f, z);
            for (int row = 0; row < 4; row++)
            {
                float y = 1f + 2f * row;
                P("stone_wall_4x2", -2, y, 3.5f); if (row > 0) P("stone_wall_4x2", 2, y, 3.5f);
                P("stone_wall_4x2", -2, y, -3.5f); P("stone_wall_4x2", 2, y, -3.5f);
                foreach (float side in new[] { -3.5f, 3.5f }) { P("stone_wall_4x2", side, y, -2, 90); P("stone_wall_4x2", side, y, 2, 90); }
            }
            P("wood_door", 1, 1, 3.5f); P("stone_wall_2x1", 3, 0.5f, 3.5f); P("stone_wall_2x1", 3, 1.5f, 3.5f);
            foreach (var (sx, sz) in Corners) for (int k = 0; k < 5; k++) P("stone_pillar", sx * 4f, 1f + 2f * k, sz * 4f);
            foreach (float a in new[] { -3f, -1f, 1f, 3f }) { P("stone_wall_1x1", a, 8.5f, 3.5f); P("stone_wall_1x1", a, 8.5f, -3.5f); P("stone_wall_1x1", 3.5f, 8.5f, a, 90); P("stone_wall_1x1", -3.5f, 8.5f, a, 90); }
            // the keeper's workshop: what the builders need within reach of every piece
            P("piece_workbench", -0.5f, 0, -2.3f); P("piece_stonecutter", 2.2f, 0, 0.2f, 90);
            // inside: ladders up the left wall to a plank floor, then along the back wall to the roof
            P("wood_stepladder", -2.5f, 0, 1); P("wood_stepladder", -2.5f, 2, -1);
            for (int x = 0; x <= 2; x += 2) for (int z = -2; z <= 2; z += 2) P("wood_floor", x, 3.92f, z);
            for (float z = -2.5f; z <= 2.5f; z += 1f) P("wood_floor_1x1", -1.5f, 3.92f, z);
            P("wood_floor_1x1", -2.5f, 3.92f, -2.5f);
            P("wood_stepladder", -1, 4, -2.5f, 270); P("wood_stepladder", 1, 6, -2.5f, 270);
            // the roof: stone under the shaft, planks round it (open where the ladders come up)
            foreach (var (sx, sz) in Corners) P("stone_floor_2x2", sx, 7.5f, sz);
            for (float x = -2.5f; x <= 2.5f; x += 1f) for (float z = -2.5f; z <= 2.5f; z += 1f)
            {
                bool ring = Math.Abs(x) > 2 || Math.Abs(z) > 2, well = z < -2 && x > -2 && x < 2;
                if (ring && !well) P("wood_floor_1x1", x, 7.92f, z);
            }
            // the shaft, and the stair round its outside: up the right side, a landing, along the front
            for (int row = 0; row < 4; row++) { float y = 9f + 2f * row; P("stone_wall_4x2", 0, y, 1.5f); P("stone_wall_4x2", 0, y, -1.5f); P("stone_wall_4x2", 1.5f, y, 0, 90); P("stone_wall_4x2", -1.5f, y, 0, 90); }
            P("stone_floor_2x2", 0, 15.5f, 0);
            P("wood_stepladder", 2.5f, 8, -1, 180); P("wood_stepladder", 2.5f, 10, 1, 180);
            P("wood_floor_1x1", 2.5f, 11.92f, 2.5f);
            P("wood_stepladder", 1, 12, 2.5f, 90); P("wood_stepladder", -1, 14, 2.5f, 90);
            // the gallery round the top (open where the stair comes up), braced from the shaft, railed, a torch at each corner
            for (float x = -3.5f; x <= 3.5f; x += 1f) for (float z = -3.5f; z <= 3.5f; z += 1f)
            {
                bool ring = Math.Abs(x) > 2 || Math.Abs(z) > 2, hole = z > 2 && z < 3 && x > -2 && x < 0;
                if (ring && !hole) P("wood_floor_1x1", x, 15.92f, z);
            }
            foreach (float a in new[] { -1f, 1f }) { P("wood_beam_45", 3, 15, a); P("wood_beam_45", -3, 15, a, 180); P("wood_beam_45", a, 15, -3, 90); }
            foreach (var (sx, sz) in Corners) { P("wood_pole", sx * 3.75f, 16.5f, sz * 3.75f); P("piece_groundtorch_wood", sx * 3.75f, 17.65f, sz * 3.75f); }
            foreach (float a in new[] { -3.75f, 3.75f }) { P("wood_pole", 0, 16.5f, a); P("wood_pole", a, 16.5f, 0); }
            foreach (float a in new[] { -1.9f, 1.9f }) { P("wood_wall_log_4x0.5", a, 16.8f, 3.75f); P("wood_wall_log_4x0.5", a, 16.8f, -3.75f); P("wood_wall_log_4x0.5", 3.75f, 16.8f, a, 90); P("wood_wall_log_4x0.5", -3.75f, 16.8f, a, 90); }
            // the beacon
            P("bonfire", 0, 16.45f, 0);
            return parts;
        }
        private static readonly (int, int)[] Corners = { (1, 1), (1, -1), (-1, 1), (-1, -1) };
        // Four logs lying round a square (half its side out from the middle) at a height.
        private static void Ring(List<Part> parts, string log, float half, float y)
        {
            parts.Add(new Part(log, 0, y, half, 0)); parts.Add(new Part(log, 0, y, -half, 0));
            parts.Add(new Part(log, half, y, 0, 90)); parts.Add(new Part(log, -half, y, 0, 90));
        }
        public const float Footing = 4.5f; // (half its footing, the gallery's overhang with it: the outline must hold this square)
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
            ["wood_floor_1x1"] = new[] { ("Wood", 1) }, ["wood_floor"] = new[] { ("Wood", 2) }, ["wood_pole"] = new[] { ("Wood", 1) }, ["wood_beam_45"] = new[] { ("Wood", 2) },
            ["wood_door"] = new[] { ("Wood", 4) }, ["wood_stepladder"] = new[] { ("Wood", 2) }, ["wood_wall_log_4x0.5"] = new[] { ("RoundLog", 2) },
            ["stone_wall_1x1"] = new[] { ("Stone", 3) }, ["stone_wall_2x1"] = new[] { ("Stone", 4) }, ["stone_wall_4x2"] = new[] { ("Stone", 6) }, ["stone_floor_2x2"] = new[] { ("Stone", 6) }, ["stone_pillar"] = new[] { ("Stone", 5) },
            ["piece_workbench"] = new[] { ("Wood", 10) }, ["piece_stonecutter"] = new[] { ("Wood", 10), ("Iron", 2), ("Stone", 4) },
            ["bonfire"] = new[] { ("SurtlingCore", 1), ("ElderBark", 5), ("FineWood", 5), ("RoundLog", 5) }, ["piece_groundtorch_wood"] = new[] { ("Wood", 2), ("Resin", 2) },
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
        public static string Gift(string work) => work == Lighthouse ? "Its bonfire is the colony's beacon, and ships on the colony's trade runs are lost half as often." : "";
        public const float LossAtSea = 0.5f; // (the Lighthouse: trade runs at sea lost this much as often)
        public const int Renown = 40;
    }
}
