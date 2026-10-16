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
        public const string Lighthouse = "lighthouse", PeakTemple = "peaktemple";
        public static readonly string[] All = { Lighthouse, PeakTemple };
        public static string Key(string work) => "gw:" + work;
        public static string AtKey(string work) => "gw:" + work + ":at";     // (packed x, z: TrueLegend.Pack)
        public static string YKey(string work) => "gw:" + work + ":y";       // (the ground's height, in tenths of a metre)
        public static string XKey(string work) => "gw:" + work + ":x";       // (where exactly, in tenths of a metre: the plan's parts are checked to half a metre)
        public static string ZKey(string work) => "gw:" + work + ":z";
        public static string YawKey(string work) => "gw:" + work + ":yaw";   // (degrees: its front, toward the sea)
        public static string BuiltKey(string work) => "gw:" + work + ":built";
        public static string DayKey(string work) => "gw:" + work + ":day";
        public static string AgainKey(string work) => "gw:" + work + ":again"; // (raised again after it was taken down: no second reward)
        public static string GodKey(string work) => "gw:" + work + ":god";   // (the Peak Temple's god: its index in Tables.Gods)
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

        // The Lighthouse: a tall timber tower on a stone plinth. The plinth 8 m square, a stone pillar with a torch at each corner; the
        // keeper's hut on it, two courses of stone under log-framed planks, a door and a banner beside it; four storeys of log trestle
        // above, narrowing as they rise, a plank floor at each; ladders in switchbacks inside all the way up (a landing at each turn, the
        // floors open over the stair); a railed gallery 20 m up with torches at its corners and red banners hung from its sides; over
        // it on four logs the fire deck, dragon heads at its corners, and the beacon's bonfire 24 m up. Its workbench (with a chopping
        // block and a tanning rack, for the reach) and stonecutter stand on the plinth.
        public const int Storeys = 4; // (of trestle, over the hut)
        public static List<Part> LighthouseParts()
        {
            var parts = new List<Part>();
            void P(string prefab, float x, float y, float z, float yaw = 0) => parts.Add(new Part(prefab, x, y, z, yaw));
            const float deck = 0.25f, storey = 4f; // (the plinth's top; each floor above the last)
            for (int x = -3; x <= 3; x += 2) for (int z = -3; z <= 3; z += 2) P("stone_floor_2x2", x, deck - 0.5f, z);
            foreach (var (sx, sz) in Corners) { P("stone_pillar", sx * 3.5f, deck + 1f, sz * 3.5f); P("piece_groundtorch_wood", sx * 3.5f, deck + 2.65f, sz * 3.5f); }
            P("piece_workbench", 3.1f, deck, 0, 90); P("piece_workbench_ext1", 3.2f, deck, 2.4f, 90); P("piece_workbench_ext2", 3.2f, deck, -2.4f, 90);
            P("piece_stonecutter", -3.05f, deck, 0, 270); P("forge", 0, deck, -3.3f, 180);
            // the hut: corner logs; two courses of stone, then planks (the door at the front right, planks over it); a banner by the door
            // (inside each corner log, all the way up, an iron-banded pole: timber alone loses its strength 20 m up, and these carry it)
            foreach (var (sx, sz) in Corners) { P("wood_pole_log_4", sx * 2f, deck + 2.22f, sz * 2f); P("woodiron_pole", sx * 2f, deck + 1f, sz * 2f); P("woodiron_pole", sx * 2f, deck + 3f, sz * 2f); }
            P("stone_wall_2x1", -1, deck + 0.5f, 2); P("stone_wall_2x1", -1, deck + 1.5f, 2); P("woodwall", -1, deck + 3.01f, 2);
            P("wood_door", 1, deck + 1.01f, 2); P("woodwall", 1, deck + 3.01f, 2);
            P("piece_banner03", -1, deck + 3.9f, 2.3f, 90);
            foreach (float a in new[] { -1f, 1f })
            {
                P("stone_wall_2x1", a, deck + 0.5f, -2); P("stone_wall_2x1", a, deck + 1.5f, -2); P("woodwall", a, deck + 3.01f, -2);
                foreach (float side in new[] { -2f, 2f }) { P("stone_wall_2x1", side, deck + 0.5f, a, 90); P("stone_wall_2x1", side, deck + 1.5f, a, 90); P("woodwall", side, deck + 3.01f, a, 90); }
            }
            Ring(parts, "wood_wall_log_4x0.5", 2f, deck + storey + 0.27f);
            // a flight of two ladders a storey, one over another: up toward the back, a landing, back up toward the front; a floor at
            // each storey, open over the second ladder, the landing and the top of the first (head room all the way up)
            for (int s = 0; s <= Storeys; s++)
            {
                float y0 = deck + storey * s;
                P("wood_stepladder", 0.5f, y0, 0); P("wood_stepladder", -0.5f, y0 + 2, 0, 180);
                P("wood_floor_1x1", -0.5f, y0 + 1.92f, -1.5f); P("wood_floor_1x1", 0.5f, y0 + 1.92f, -1.5f);
                for (float x = -1.5f; x <= 1.5f; x += 1f) for (float z = -1.5f; z <= 1.5f; z += 1f)
                    if (!Stairwell(x, z)) P("wood_floor_1x1", x, y0 + storey - 0.08f, z);
                if (s == 0) continue;
                // the trestle storey this flight climbs: leaning corner logs, a ring halfway and one on top (open at the back, over the landing)
                float lo = 2f - 0.125f * (s - 1), hi = lo - 0.125f, yb = deck + storey * s;
                float mid = (lo + hi) / 2;
                foreach (var (sx, sz) in Corners)
                {
                    parts.Add(Log("wood_pole_log_4", sx * lo, yb, sz * lo, sx * hi, yb + storey, sz * hi));
                    parts.Add(Log("woodiron_pole", sx * lo, yb, sz * lo, sx * mid, yb + storey / 2, sz * mid)); parts.Add(Log("woodiron_pole", sx * mid, yb + storey / 2, sz * mid, sx * hi, yb + storey, sz * hi));
                }
                Ring(parts, "wood_wall_log_4x0.5", (lo + hi) / 2, yb + storey / 2, open: true);
                Ring(parts, "wood_wall_log_4x0.5", hi, yb + storey + 0.27f, open: true);
            }
            // the gallery on top: posts, a rail, torches, banners hung over its sides; over it, on four logs, the fire deck with dragon
            // heads at its corners and the beacon's bonfire (3 m of head room under it, over the stairhead)
            float onDeck = deck + storey * (Storeys + 1) + 0.08f;
            foreach (var (sx, sz) in Corners) { P("wood_pole", sx * 1.85f, onDeck + 0.5f, sz * 1.85f); P("piece_groundtorch_wood", sx * 1.85f, onDeck + 1.65f, sz * 1.85f); }
            Ring(parts, "wood_wall_log_4x0.5", 1.85f, onDeck + 0.8f);
            P("piece_banner03", 2.15f, onDeck + 1.05f, 0); P("piece_banner03", -2.15f, onDeck + 1.05f, 0); P("piece_banner03", 0, onDeck + 1.05f, -2.15f, 90); P("piece_banner03", 0, onDeck + 1.05f, 2.15f, 90); // (hung over the rail)
            foreach (var (sx, sz) in Corners) { P("wood_pole_log", sx * 1.5f, onDeck + 1.11f, sz * 1.5f); P("wood_pole", sx * 1.5f, onDeck + 2.72f, sz * 1.5f); } // (on the iron poles' tops)
            float fireDeck = onDeck + 3.22f;
            Ring(parts, "wood_wall_log_4x0.5", 1.5f, fireDeck - 0.27f);
            for (int x = -2; x <= 2; x += 2) for (int z = -2; z <= 2; z += 2) if (x != 0 || z != 0) P("wood_floor", x, fireDeck - 0.08f, z); // (the bonfire sits in the middle)
            foreach (var (sx, sz) in Corners) P("wood_dragon1", sx * 2.6f, fireDeck, sz * 2.6f, (float)(Math.Atan2(sx, sz) * 180 / Math.PI));
            P("bonfire", 0, fireDeck + 0.5f, 0);
            return parts;
        }
        public static float FireHeight(string work) => Parts(work).Where(p => p.Prefab == "bonfire").Select(p => p.Y).DefaultIfEmpty(15f).First();
        // Where each floor is open for the stair (cells of a metre, by their middles): over the second ladder, over the landing, and over the
        // top of the first ladder.
        public static bool Stairwell(float x, float z) => x < 0 && x > -1 && z > -1 && z < 1 || z < -1 && z > -2 && Math.Abs(x) < 1 || x > 0 && x < 1 && z > -1 && z < 0;
        private static readonly (int, int)[] Corners = { (1, 1), (1, -1), (-1, 1), (-1, -1) };
        // Four logs lying round a square (half its side out from the middle) at a height.
        private static void Ring(List<Part> parts, string log, float half, float y, bool open = false)
        {
            parts.Add(new Part(log, 0, y, half, 0));
            parts.Add(new Part(log, 0, y, open ? -(half + 0.53f) : -half, 0)); // (open: the back log outside the legs, clear of the landing inside)
            parts.Add(new Part(log, half, y, 0, 90)); parts.Add(new Part(log, -half, y, 0, 90));
        }
        public const float Footing = 4.5f; // (half its footing, the gallery's overhang with it: the outline must hold this square)
        public static float FootingOf(string work) => work == PeakTemple ? 6f : Footing; // (the temple's plinth: 10 × 12 m)
        // Where a work stands in an outline drawn for it: the middle of the outline, if its footing fits inside ("" or why not).
        public static string Site(IList<float> xz, out float x, out float z) => Site(Lighthouse, xz, out x, out z);
        public static string Site(string work, IList<float> xz, out float x, out float z)
        {
            float foot = FootingOf(work);
            x = z = 0;
            int n = xz.Count / 2;
            if (n < 3) return "draw its outline first";
            for (int i = 0; i < n; i++) { x += xz[i * 2]; z += xz[i * 2 + 1]; }
            x /= n; z /= n;
            foreach (var (sx, sz) in Corners) if (!Geometry.Inside(xz, x + sx * foot, z + sz * foot)) return $"the outline is too small: it needs {foot * 2:0} × {foot * 2:0} m clear in its middle";
            return "";
        }
        public static List<Part> Parts(string work, string god = null) => work == Lighthouse ? LighthouseParts() : work == PeakTemple ? TempleParts(god) : new List<Part>();

        // The Peak Temple: an A-frame hof for the snow line. A stone plinth 10 × 12 m, a stone pillar with a torch at each corner and either
        // side of the way in; on it the hall, all roof: the steepest the game has (67°) from the plinth to a ridge 8 m up, two rows a side,
        // 10 m long, so the snow slides off it; its gables closed with planks, the door in the front one between stone jambs, a banner
        // either side; a dragon head at each end of the ridge; inside, the god's altar at the back between two torches. Behind it the
        // workbench and stonecutter it was raised with.
        public const float TempleRidge = 8f, TempleHalf = 4f, TempleEnd = 5f; // (the ridge's height; half its width at the plinth; half its length)
        public static List<Part> TempleParts(string god = null)
        {
            var parts = new List<Part>();
            void P(string prefab, float x, float y, float z, float yaw = 0) => parts.Add(new Part(prefab, x, y, z, yaw));
            const float deck = 0.25f; // (the plinth's top)
            for (int x = -4; x <= 4; x += 2) for (int z = -5; z <= 5; z += 2) P("stone_floor_2x2", x, deck - 0.5f, z);
            foreach (var (px, pz) in new[] { (4.5f, 5.5f), (-4.5f, 5.5f), (4.5f, -5.5f), (-4.5f, -5.5f), (3.0f, 5.7f), (-3.0f, 5.7f) })
            { P("stone_pillar", px, deck + 1f, pz); P("piece_groundtorch_wood", px, deck + 2.65f, pz); }
            // the roof, from the plinth up (a 67° piece rises 4 m over 2: its pivot 1.02 m over its foot and 1.06 m in from it)
            for (float z = -4; z <= 4; z += 2)
            {
                P("wood_roof_67", 2.94f, deck + 1.02f, z, 90); P("wood_roof_67", 0.94f, deck + 5.02f, z, 90);
                P("wood_roof_67", -2.94f, deck + 1.02f, z, 270); P("wood_roof_67", -0.94f, deck + 5.02f, z, 270);
                P("wood_roof_top_67", 0, deck + 5.85f, z, 90);
            }
            // the gables: a triangle under each slope, planks between; at the front the door between stone jambs, planks over it
            foreach (float gz in new[] { TempleEnd, -TempleEnd })
            {
                P("wood_wall_roof_67_a", -3, deck, gz, 0); P("wood_wall_roof_67_a", 3, deck, gz, 180);
                P("wood_wall_roof_67_a", -1, deck + 4f, gz, 0); P("wood_wall_roof_67_a", 1, deck + 4f, gz, 180);
                P("woodwall", -1, deck + 3f, gz); P("woodwall", 1, deck + 3f, gz);
                if (gz < 0) { P("woodwall", -1, deck + 1f, gz); P("woodwall", 1, deck + 1f, gz); continue; }
                P("stone_wall_1x1", -1.5f, deck + 0.5f, gz); P("stone_wall_1x1", -1.5f, deck + 1.5f, gz);
                P("stone_wall_1x1", 1.5f, deck + 0.5f, gz); P("stone_wall_1x1", 1.5f, deck + 1.5f, gz);
                P("stone_wall_1x1", -1.5f, deck + 2.5f, gz); P("stone_wall_1x1", 1.5f, deck + 2.5f, gz); // (a third stone each side: what the banners hang from)
                P("wood_door", 0, deck + 1.01f, gz);
            }
            P("piece_banner03", -1.42f, deck + 2.9f, TempleEnd + 0.6f, 90); P("piece_banner03", 1.42f, deck + 2.9f, TempleEnd + 0.6f, 90); // (their bars at the jambs' tops: where Bob hung one) // (hung on the door's jambs, in front of the stone; the pillars clear of them)
            P("wood_dragon1", 0, deck + TempleRidge + 0.2f, TempleEnd + 0.2f, 0); P("wood_dragon1", 0, deck + TempleRidge + 0.2f, -TempleEnd - 0.2f, 180);
            // inside: the altar at the back, a torch either side, wolf pelts from the door up to it (the rest is the jarl's to hang)
            P("JarldomAltar_" + (string.IsNullOrEmpty(god) ? "odin" : god), 0, deck, -3.6f);
            P("rug_wolf", 0, deck + 0.03f, 3.2f); P("rug_wolf", 0, deck + 0.03f, 0.2f);
            P("piece_groundtorch_wood", 1.6f, deck + 0.7f, -3.8f); P("piece_groundtorch_wood", -1.6f, deck + 0.7f, -3.8f);
            // behind it, what it was raised with (the builders' reach)
            P("piece_workbench", 2.5f, deck, -5.6f, 180); P("piece_stonecutter", -2.5f, deck, -5.6f, 180);
            return parts;
        }

        // Its facing, kept to the steps the game turns pieces by (22.5°): what a player builds on or beside it lines up with it.
        public const float YawStep = 22.5f;
        public static float SnapYaw(float yaw) { float y = (float)Math.Round(yaw / YawStep) * YawStep % 360f; return y < 0 ? y + 360f : y; }
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
            ["JarldomAltar"] = new[] { ("Stone", 12), ("Coins", 10), ("Resin", 2) },
            ["stone_wall_1x1"] = new[] { ("Stone", 3) }, ["stone_wall_2x1"] = new[] { ("Stone", 4) }, ["stone_wall_4x2"] = new[] { ("Stone", 6) }, ["stone_floor_2x2"] = new[] { ("Stone", 6) }, ["stone_pillar"] = new[] { ("Stone", 5) },
            ["piece_workbench"] = new[] { ("Wood", 10) }, ["woodwall"] = new[] { ("Wood", 2) }, ["wood_roof_45"] = new[] { ("Wood", 2) }, ["wood_roof_top_45"] = new[] { ("Wood", 2) }, ["wood_wall_roof_45"] = new[] { ("Wood", 2) }, ["wood_roof_67"] = new[] { ("Wood", 2) }, ["wood_roof_top_67"] = new[] { ("Wood", 2) }, ["wood_wall_roof_67_a"] = new[] { ("Wood", 2) },
            ["wood_pole_log_4"] = new[] { ("RoundLog", 2) }, ["rug_wolf"] = new[] { ("WolfPelt", 4) }, ["piece_stonecutter"] = new[] { ("Wood", 10), ("Iron", 2), ("Stone", 4) },
            ["bonfire"] = new[] { ("SurtlingCore", 1), ("ElderBark", 5), ("FineWood", 5), ("RoundLog", 5) }, ["piece_groundtorch_wood"] = new[] { ("Wood", 2), ("Resin", 2) }, ["wood_pole_log"] = new[] { ("RoundLog", 1) }, ["woodiron_pole"] = new[] { ("Wood", 2), ("Iron", 1) }, ["forge"] = new[] { ("Stone", 4), ("Coal", 4), ("Wood", 10), ("Copper", 6) }, ["stone_pillar"] = new[] { ("Stone", 5) }, ["piece_banner03"] = new[] { ("LeatherScraps", 6), ("FineWood", 2), ("Raspberry", 4) }, ["wood_dragon1"] = new[] { ("FineWood", 10) }, ["piece_workbench_ext1"] = new[] { ("Wood", 10), ("Flint", 10) }, ["piece_workbench_ext2"] = new[] { ("Wood", 10), ("Flint", 15), ("LeatherScraps", 20), ("DeerHide", 5) },
        };
        public static Dictionary<string, int> Needs(string work)
        {
            var all = new Dictionary<string, int>();
            foreach (var p in Parts(work)) if (Cost.TryGetValue(p.Prefab.StartsWith("JarldomAltar_") ? "JarldomAltar" : p.Prefab, out var c)) foreach (var (item, n) in c) all[item] = (all.TryGetValue(item, out int had) ? had : 0) + n;
            return all;
        }
        public static string NeedsText(string work) => string.Join(", ", Needs(work).OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Value} {Item(kv.Key)}"));
        private static string Item(string prefab) => prefab == "RoundLog" ? "core wood" : prefab == "FineWood" ? "fine wood" : prefab == "ElderBark" ? "ancient bark" : prefab == "SurtlingCore" ? "surtling core"
            : prefab == "LeatherScraps" ? "leather scraps" : prefab == "WolfPelt" ? "wolf pelts" : prefab == "DeerHide" ? "deer hides" : prefab == "Raspberry" ? "raspberries" : prefab.ToLower();
        public static string Name(string work) => work == Lighthouse ? "the Lighthouse" : work == PeakTemple ? "the Peak Temple" : work;
        public static string Title(string work) => work == Lighthouse ? "The Lighthouse" : work == PeakTemple ? "The Peak Temple" : work; // (to start a sentence)
        public static string Proper(string work, string colony) => (work == Lighthouse ? "Lighthouse" : work == PeakTemple ? "Peak Temple" : work) + " of " + colony; // (its name on the map)
        public static string Gift(string work) => work == Lighthouse ? "Its fire is the colony's beacon, and ships on the colony's trade runs are lost half as often."
            : work == PeakTemple ? "Its god's blessing on the colony is twice as strong, and offerings to them count double." : "";
        // What it looks like, for the hall tab before it's placed.
        public static string Look(string work) => work == Lighthouse ? "a tall timber tower on a stone plinth, ladders inside up to a railed gallery hung with banners, and the beacon's bonfire on a dragon-headed fire deck 24 m up"
            : work == PeakTemple ? "an A-frame hof for the snow line: on a stone plinth ringed with torch-topped pillars, the steepest roof there is from the ground to a dragon-headed ridge 8 m up, and the god's altar inside" : "";
        // Where it must stand, for the hall tab and the Thing's word.
        public static string Where(string work) => work == Lighthouse ? "by the shore" : work == PeakTemple ? "on the highest ground in the colony" : "";
        public const float LossAtSea = 0.5f; // (the Lighthouse: trade runs at sea lost this much as often)
        public const int Renown = 40;
    }
}
