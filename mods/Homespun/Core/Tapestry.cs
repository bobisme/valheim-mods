using System.Linq;
using UnityEngine;

namespace Homespun
{
    // Hall tapestries: what dyeing is for, at the top end. Hung on a wall as the game's banners are (Furniture), in the banners' comfort
    // group but one better, and woven from dyed bolts: a long procession band (Oseberg) and a wall hanging (Överhogdal). Jarldom's halls
    // count them (its Colony/Survey.cs "tapestry"). Bounds from Models/README (origin at the rod's top centre, hanging down, front +x).
    public static class Tapestry
    {
        public const int Comfort = 2;
        public static readonly (string item, int n)[] FriezeCost = { (Names.LinenBolt, 1), ("Homespun_WoolBolt_Red", 1), ("Homespun_WoolBolt_Blue", 1), ("Homespun_WoolBolt_Yellow", 1), ("Wood", 1) };
        public static readonly (string item, int n)[] HangingCost = { (Names.LinenBolt, 2), ("Homespun_WoolBolt_Red", 1), ("Homespun_WoolBolt_Blue", 1), ("Wood", 1) };
        public static readonly Vector3 FriezeCentre = new Vector3(0, -0.28f, 0), FriezeSize = new Vector3(0.1f, 0.6f, 3.2f);
        public static readonly Vector3 HangingCentre = new Vector3(0, -0.9f, 0), HangingSize = new Vector3(0.1f, 1.86f, 1.6f);
        public static bool Is(string prefab) => prefab == Names.Frieze || prefab == Names.Hanging;
    }
}
