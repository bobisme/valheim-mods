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

        // The Lighthouse, in timber: a keeper's hut of logs and planks with a door, two storeys of log trestle above it narrowing as they
        // rise (rings of crossed logs at each floor and between), a plank gallery with a log rail, torches at its corners, and the
        // beacon fire on a cairn of stones in its middle, about 14 m up.
        public static List<Part> LighthouseParts()
        {
            var parts = new List<Part>();
            const float floor = 0.15f, deck = 0.23f, storey = 4.4f;
            for (int x = -1; x <= 1; x += 2) for (int z = -1; z <= 1; z += 2) parts.Add(new Part("wood_floor", x, floor, z, 0));
            // the hut: corner logs, plank walls (the door at the front right), a ring of logs on top
            foreach (var (sx, sz) in Corners) parts.Add(new Part("wood_pole_log_4", sx * 2f, deck + 2.22f, sz * 2f, 0));
            for (int row = 0; row < 2; row++)
                foreach (float a in new[] { -1f, 1f })
                {
                    float y = deck + 1.01f + 2.02f * row;
                    parts.Add(row == 0 && a > 0 ? new Part("wood_door", a, y, 2f, 0) : new Part("woodwall", a, y, 2f, 0));
                    parts.Add(new Part("woodwall", a, y, -2f, 0)); parts.Add(new Part("woodwall", 2f, y, a, 90)); parts.Add(new Part("woodwall", -2f, y, a, 90));
                }
            float top0 = deck + storey;
            Ring(parts, "wood_wall_log_4x0.5", 2f, top0 + 0.27f);
            // two storeys of trestle, each narrower than the one below
            float[] half = { 2f, 1.5f, 1f };
            for (int s = 0; s < 2; s++)
            {
                float y0 = top0 + storey * s, y1 = y0 + storey;
                foreach (var (sx, sz) in Corners) parts.Add(Log("wood_pole_log_4", sx * half[s], y0, sz * half[s], sx * half[s + 1], y1, sz * half[s + 1]));
                float mid = (half[s] + half[s + 1]) / 2;
                Ring(parts, mid > 1.6f ? "wood_wall_log_4x0.5" : "wood_wall_log", mid, (y0 + y1) / 2);
                Ring(parts, half[s + 1] > 1.2f ? "wood_wall_log_4x0.5" : "wood_wall_log", half[s + 1], y1 + 0.27f);
            }
            float galley = top0 + storey * 2 + 0.6f;
            for (int x = -1; x <= 1; x += 2) for (int z = -1; z <= 1; z += 2) parts.Add(new Part("wood_floor", x, galley, z, 0));
            float onDeck = galley + 0.08f;
            foreach (var (sx, sz) in Corners) { parts.Add(new Part("wood_pole", sx * 1.85f, onDeck + 0.5f, sz * 1.85f, 0)); parts.Add(new Part("piece_groundtorch_wood", sx * 1.85f, onDeck + 1.65f, sz * 1.85f, 0)); }
            Ring(parts, "wood_wall_log_4x0.5", 1.85f, onDeck + 0.8f);
            parts.Add(new Part("stone_pile", 0, onDeck + 0.09f, 0, 0));
            parts.Add(new Part("fire_pit", 0, onDeck + 0.9f, 0, 0));
            return parts;
        }
        private static readonly (int, int)[] Corners = { (1, 1), (1, -1), (-1, 1), (-1, -1) };
        // Four logs lying round a square (half its side out from the middle) at a height.
        private static void Ring(List<Part> parts, string log, float half, float y)
        {
            parts.Add(new Part(log, 0, y, half, 0)); parts.Add(new Part(log, 0, y, -half, 0));
            parts.Add(new Part(log, half, y, 0, 90)); parts.Add(new Part(log, -half, y, 0, 90));
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
            ["wood_floor"] = new[] { ("Wood", 2) }, ["woodwall"] = new[] { ("Wood", 2) }, ["wood_door"] = new[] { ("Wood", 4) }, ["wood_pole"] = new[] { ("Wood", 1) },
            ["wood_pole_log_4"] = new[] { ("RoundLog", 2) }, ["wood_wall_log_4x0.5"] = new[] { ("RoundLog", 2) }, ["wood_wall_log"] = new[] { ("RoundLog", 1) },
            ["piece_groundtorch_wood"] = new[] { ("Wood", 2), ("Resin", 2) }, ["stone_pile"] = new[] { ("Stone", 50) }, ["fire_pit"] = new[] { ("Stone", 5), ("Wood", 2) },
        };
        public static Dictionary<string, int> Needs(string work)
        {
            var all = new Dictionary<string, int>();
            foreach (var p in Parts(work)) if (Cost.TryGetValue(p.Prefab, out var c)) foreach (var (item, n) in c) all[item] = (all.TryGetValue(item, out int had) ? had : 0) + n;
            return all;
        }
        public static string NeedsText(string work) => string.Join(", ", Needs(work).OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value} {(kv.Key == "RoundLog" ? "core wood" : kv.Key.ToLower())}"));
        public static string Name(string work) => work == Lighthouse ? "the Lighthouse" : work;
        public static string Title(string work) => work == Lighthouse ? "The Lighthouse" : work; // (to start a sentence)
        public static string Proper(string work, string colony) => (work == Lighthouse ? "Lighthouse" : work) + " of " + colony; // (its name on the map)
        public static string Gift(string work) => work == Lighthouse ? "Its hearth is the colony's beacon, and ships on the colony's trade runs are lost half as often." : "";
        public const float LossAtSea = 0.5f; // (the Lighthouse: trade runs at sea lost this much as often)
        public const int Renown = 40;
    }
}
