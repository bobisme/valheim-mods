using System;
using System.Linq;
using UnityEngine;

namespace Bathhouse
{
    // For other mods (Jarldom: a bathhouse room, villagers bathing on washing day), found by reflection: the type "Bathhouse.Api" in this
    // assembly.
    public static class Api
    {
        public const int Version = 1;
        // A working sauna's heat (a sauna stove or the game's hot tub, lit, in a room that's a sauna) within reach of a point.
        public static bool IsSauna(Vector3 at) => Plugin.Instance != null && Room.Heats(at, Sauna.Reach + 1).Any(h => Utils.DistanceXZ(h.At, at) <= Sauna.Reach && Mathf.Abs(h.At.y - at.y) < 3f && Room.Works(h));
        // The same, whether or not its fire is lit (a sauna built, if not in use).
        public static bool IsSaunaBuilt(Vector3 at) => Plugin.Instance != null && Room.Heats(at, Sauna.Reach + 1).Any(h => Utils.DistanceXZ(h.At, at) <= Sauna.Reach && Mathf.Abs(h.At.y - at.y) < 3f && Sauna.WhyNot(Unlit(Room.Facts(h))) == "");
        // Why the nearest heat's room isn't a sauna ("" when it is).
        public static string WhyNot(Vector3 at) => Plugin.Instance == null ? "Bathhouse isn't running." : Room.WhyNot(Room.Heats(at, Sauna.Reach + 1).FirstOrDefault());
        // The heats of built saunas (lit or not) within radius of a point.
        public static Vector3[] Saunas(Vector3 at, float radius) => Plugin.Instance == null ? new Vector3[0]
            : Room.Heats(at, radius).Where(h => Sauna.WhyNot(Unlit(Room.Facts(h))) == "").Select(h => h.At).ToArray();
        // Is that sauna's heat lit (its stove's fire, the hot tub's)?
        public static bool Lit(Vector3 sauna) => Room.Heats(sauna, 0.5f).FirstOrDefault()?.Lit == true;
        // Where bathers sit in that sauna: each seat's attach point (where the body goes, and which way it faces) and whether someone's in it.
        public static (Transform at, bool taken)[] Seats(Vector3 sauna) => Room.Heats(sauna, 0.5f).FirstOrDefault() is Heat h
            ? Room.Seats(h).Where(c => c.m_attachPoint != null).Select(c => (c.m_attachPoint, c.IsInUse())).ToArray() : new (Transform, bool)[0];
        // Throw water on that sauna stove's stones (steam and the hiss on every game near). False if it has no stove, or it's cold.
        public static bool Steam(Vector3 sauna)
        {
            StoveSteam s = StoveSteam.Nearest(sauna, 0.5f, true);
            if (s == null) return false;
            s.Throw(); return true;
        }
        // Plunge tubs within radius of a point.
        public static Vector3[] PlungeTubs(Vector3 at, float radius) => Room.Plunges(at, radius).Select(p => p.transform.position).ToArray();
        // Other bathers in a sauna, as another mod counts them (its villagers sitting in the heat): they make this game's player's Bathed
        // and Invigorated last longer, as other players do. Called with the sauna's heat; return how many.
        public static void AddCompany(Func<Vector3, int> count) { if (count != null && !Bathing.CompanyFrom.Contains(count)) Bathing.CompanyFrom.Add(count); }
        public static void RemoveCompany(Func<Vector3, int> count) => Bathing.CompanyFrom.Remove(count);
        // This game's player: Bathed, Invigorated (seconds left, 0 if not).
        public static float Bathed(Player p) => Effects.Left(p, Effects.Bathed);
        public static float Invigorated(Player p) => Effects.Left(p, Effects.Invigorated);

        private static RoomFacts Unlit(RoomFacts f) => f == null ? null : new RoomFacts { Stove = f.Stove, HotTub = f.HotTub, Lit = true, Benches = f.Benches, Cover = f.Cover, UnderRoof = f.UnderRoof, Walls = f.Walls, Rays = f.Rays, Across = f.Across };
    }
}
