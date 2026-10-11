using System;
using System.Collections.Generic;
using System.Linq;

namespace Jarldom
{
    // What the world showed when a room was checked: measured by the game side, judged here.
    public sealed class RoomFacts
    {
        public float[] Walls = new float[0];    // each edge's share covered by wall, door or gate pieces (0..1)
        public float Roof;                      // share of the floor under cover (0..1)
        public int Doors, LockableDoors;
        public float Area, FloorHeight;         // m²; how high the floor stands over the ground outside (watchtowers)
        public Dictionary<string, int> Count = new Dictionary<string, int>(); // pieces inside by kind (bed, chest, seat, table, fire, hearth...)
        public int Comfort;                     // the game's comfort level in the middle of the room
        public int Value;                       // coins and valuables inside (treasure room)
        public bool Altar(string god) => C("altar_" + god) > 0;
        public int C(string kind) => Count.TryGetValue(kind, out int v) ? v : 0;
    }

    public sealed class RoomVerdict
    {
        public bool Ok; public int Points; public char Grade = 'D'; public List<string> Failures = new List<string>(); public List<int> OpenEdges = new List<int>();
    }

    public static class RoomRules
    {
        // The room a point is in: inside its outline and within its storey; in a house of storeys, the one whose floor is nearest below.
        public static Room At(IEnumerable<Room> rooms, float x, float y, float z, RoomType? type = null) =>
            rooms.Where(r => (type == null || r.Type == type) && Geometry.Inside(r.Xz, x, z) && y > r.Floor - 1.5f && y < r.Floor + r.Height + 0.5f)
                .OrderBy(r => y >= r.Floor - 0.25f ? y - r.Floor : 100 + r.Floor - y).FirstOrDefault(); // (a little under a floor is still on it: feet sink in)
        public const float WallShare = 0.8f, RoofShare = 0.9f;

        public static RoomVerdict Judge(RoomType type, RoomFacts f, string god = "")
        {
            RoomInfo info = Tables.Of(type);
            var v = new RoomVerdict();
            if (info.Enclosed)
            {
                for (int i = 0; i < f.Walls.Length; i++) if (f.Walls[i] < WallShare) v.OpenEdges.Add(i);
                if (v.OpenEdges.Count > 0) v.Failures.Add(v.OpenEdges.Count == 1 ? "a wall has a gap" : $"{v.OpenEdges.Count} walls have gaps");
            }
            if (info.Roofed && f.Roof < RoofShare) v.Failures.Add($"only {Math.Round(f.Roof * 100)}% of it is roofed (needs {RoofShare * 100}%)");
            if (info.Door && f.Doors == 0 && f.C("stairs") == 0) v.Failures.Add("no door on its walls (or stairs up into it)");
            if (info.Lockable && f.LockableDoors == 0 && f.Doors == 0) v.Failures.Add("no door to lock");
            if (info.Fire && f.C("fire") == 0) v.Failures.Add("no fire");
            if (info.Hearth && f.C("hearth") == 0 && f.C("fire") == 0) v.Failures.Add("no hearth");
            if (info.Beds > 0 && f.C("bed") < info.Beds) v.Failures.Add(info.Beds == 1 ? "no bed" : $"needs {info.Beds} beds");
            if (info.Seats > 0 && f.C("seat") < info.Seats) v.Failures.Add($"{f.C("seat")} of {info.Seats} seats");
            if (info.Tables > 0 && f.C("table") < info.Tables) v.Failures.Add($"{f.C("table")} of {info.Tables} tables");
            if (info.Chests > 0 && f.C("chest") < info.Chests) v.Failures.Add($"{f.C("chest")} of {info.Chests} chests");
            if (info.Graves > 0 && f.C("grave") < info.Graves) v.Failures.Add($"{f.C("grave")} of {info.Graves} grave markers");
            if (info.MinArea > 0 && f.Area < info.MinArea) v.Failures.Add($"{Math.Round(f.Area)} of {info.MinArea} m²");
            if (info.MinFloor > 0 && f.FloorHeight < info.MinFloor) v.Failures.Add($"its floor is {Math.Round(f.FloorHeight, 1)} m up (needs {info.MinFloor})");
            if (info.Brew && f.C("cauldron") == 0 && f.C("fermenter") == 0) v.Failures.Add("no cauldron or fermenter");
            if (info.Forge && f.C("forge") == 0 && f.C("workbench") == 0) v.Failures.Add("no forge or workbench");
            if (info.Altar)
            {
                string want = type == RoomType.OdinShrine ? "odin" : god;
                if (string.IsNullOrEmpty(want) ? f.Count.Keys.All(k => !k.StartsWith("altar_")) : !f.Altar(want))
                    v.Failures.Add(type == RoomType.OdinShrine ? "no altar of Odin" : "no altar to its god");
            }
            v.Points = Points(type, f);
            v.Grade = Tables.Grade(v.Points);
            v.Ok = v.Failures.Count == 0;
            return v;
        }

        // Each room counts what suits it. Gilded pieces and the high seat count for every hall-like room.
        public static int Points(RoomType type, RoomFacts f)
        {
            int p = 0;
            switch (type)
            {
                case RoomType.Longhouse:
                    p = f.Comfort * 2 + Math.Min(f.C("bed"), 8) + Math.Min(f.C("bedchest"), 8) * 2 + Math.Min(f.C("rug"), 6) * 2 + Math.Min(f.C("banner"), 4);
                    break;
                case RoomType.GreatHall:
                    p = Math.Min(f.C("seat"), 24) + Math.Min(f.C("table"), 8) * 2 + Math.Min(f.C("banner"), 8) * 2 + Math.Min(f.C("carving"), 8) * 3
                        + Math.Min(f.C("trophy"), 12) * 2 + f.C("gilded") * 4 + (f.C("highseat") > 0 ? 8 : 0) + Math.Min(f.C("rug"), 6) + f.Comfort;
                    break;
                case RoomType.Stockpile:
                    p = Math.Min(f.C("chest"), 20) * 2 + Math.Min(f.C("itemstand"), 10) * 2;
                    break;
                case RoomType.Smithy:
                    p = f.C("forge") * 8 + f.C("workbench") * 4 + f.C("extension") * 4;
                    break;
                case RoomType.OdinShrine:
                case RoomType.Temple:
                    p = Math.Min(f.C("raven"), 6) * 4 + Math.Min(f.C("trophy"), 10) * 2 + Math.Min(f.C("candle"), 8) * 2 + Math.Min(f.C("brazier"), 6) * 3
                        + Math.Min(f.C("carving"), 6) * 3 + f.C("gilded") * 4 + Math.Min(f.C("banner"), 4) * 2 + 6;
                    break;
                case RoomType.HealersHouse:
                    p = f.Comfort * 3 + Math.Min(f.C("bed"), 6) * 2;
                    break;
                case RoomType.TreasureRoom:
                    p = (int)Math.Min(80, Math.Sqrt(Math.Max(0, f.Value)) / 2);
                    break;
                case RoomType.BurialGround:
                    p = Math.Min(f.C("grave"), 12) * 3 + Math.Min(f.C("runestone"), 6) * 4 + Math.Min(f.C("candle"), 8) * 2;
                    break;
                case RoomType.Brewhouse:
                    p = Math.Min(f.C("fermenter"), 6) * 4 + f.C("cauldron") * 6 + f.C("extension") * 2 + Math.Min(f.C("chest"), 4);
                    break;
                case RoomType.Watchtower:
                    p = (int)Math.Min(40, f.FloorHeight * 4) + Math.Min(f.C("brazier"), 4) * 4;
                    break;
            }
            return Math.Max(0, p);
        }
    }
}
