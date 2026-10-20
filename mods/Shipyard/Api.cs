using System;
using System.Linq;
using UnityEngine;

namespace Shipyard
{
    // For other mods (Jarldom's shipwright), found by reflection: the type "Shipyard.Api" in this assembly. A slipway is named by where it
    // stands. What's put in obeys the slipway's rule as a player's E does (a stage is built only when paid in full, nothing taken past what it
    // needs); the calls that change a build take the slipway over on this game first, so a villager's colony can work one with nobody near.
    public static class Api
    {
        public const int Version = 1;
        // The hulls that can be built ("faering", "knarr"), their titles, and how many slots a hull's hold has (0: none).
        public static string[] Hulls => Shipyard.Hulls.All.Select(h => h.Name).ToArray();
        public static string Title(string hull) => Shipyard.Hulls.Of(hull)?.Title;
        public static int CargoSlots(string hull) => Shipyard.Hulls.Of(hull) is Hull h ? h.CargoWidth * h.CargoHeight : 0;
        // The boat prefab a hull launches as, and every stage's cost ("Wood:10,Resin:2" per stage).
        public static string Prefab(string hull) => Shipyard.Hulls.Of(hull)?.Prefab;
        public static string[] StageCosts(string hull) => Shipyard.Hulls.Of(hull)?.Stages.Select(s => string.Join(",", Sailcloth.CostOf(s).Select(c => $"{c.item}:{c.n}"))).ToArray() ?? new string[0];
        // The slipways loaded within r of a point, nearest first.
        public static Vector3[] SlipwaysNear(Vector3 at, float r) =>
            Slipway.All.Where(s => s != null && Vector3.Distance(s.transform.position, at) <= r).OrderBy(s => Vector3.Distance(s.transform.position, at)).Select(s => s.transform.position).ToArray();
        // A slipway's build: the hull on it (null: none laid), the stage under way (0-based; the hull's stage count once finished; -1: none
        // laid or no slipway there), and what the stage under way still needs (prefab, how many).
        public static string Hull(Vector3 slipway) => At(slipway)?.Now().Hull?.Name;
        public static int Stage(Vector3 slipway) => At(slipway) is Slipway s && !s.Now().Empty ? s.Now().Stage : -1;
        public static (string item, int n)[] Needs(Vector3 slipway) => At(slipway)?.Now().Needs().ToArray() ?? new (string, int)[0];
        public static bool Finished(Vector3 slipway) => At(slipway)?.Now().Finished ?? false;
        // Lay a keel for a hull on an empty slipway. False: no slipway there, it's not empty, or no such hull.
        public static bool StartBuild(Vector3 slipway, string hull) => At(slipway)?.Lay(hull) ?? false;
        // Put in up to n of an item towards the stage under way: how many it took (the rest is the caller's to keep). The stage is built once
        // the last of it is in.
        public static int Contribute(Vector3 slipway, string item, int n) => At(slipway)?.Put(item, n) ?? 0;
        // Launch the finished boat, as E does. False: not finished, or no water deep enough ahead.
        public static bool Launch(Vector3 slipway) => At(slipway)?.LaunchNow() ?? false;

        private static Slipway At(Vector3 at) => Slipway.All.Where(s => s != null && Vector3.Distance(s.transform.position, at) < 1f).OrderBy(s => Vector3.Distance(s.transform.position, at)).FirstOrDefault();
    }
}
