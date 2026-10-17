using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Village;

namespace Villagewright
{
    // Fimbulwinter's roads (FimbulwinterRoads.Api), when that mod is in the game: egress points snap onto a road, and a village
    // between two points of one road follows it. By reflection, so Villagewright runs without it.
    internal static class WorldRoads
    {
        private static bool _looked;
        private static MethodInfo _nearest, _points;
        private static PropertyInfo _ready;

        private static bool Find()
        {
            if (!_looked)
            {
                _looked = true;
                Type api = AccessTools.TypeByName("FimbulwinterRoads.Api");
                if (api != null)
                {
                    _nearest = api.GetMethod("NearestPoint", new[] { typeof(Vector3), typeof(Vector3).MakeByRefType(), typeof(string).MakeByRefType(), typeof(float).MakeByRefType() });
                    _points = api.GetMethod("RoadPoints", new[] { typeof(string) });
                    _ready = api.GetProperty("Ready");
                }
            }
            return _nearest != null && _points != null && _ready != null && (bool)_ready.GetValue(null);
        }

        // The nearest point of a road within reach, and the road's id.
        internal static bool Nearest(Vector3 p, float reach, out Vector3 point, out string road)
        {
            point = p; road = null;
            try
            {
                if (!Find()) return false;
                var args = new object[] { p, null, null, 0f };
                if (!(bool)_nearest.Invoke(null, args)) return false;
                point = (Vector3)args[1]; road = (string)args[2];
                return new Vector2(point.x - p.x, point.z - p.z).magnitude <= reach;
            }
            catch (Exception) { return false; }
        }

        internal static List<P2> Points(string road)
        {
            try { return Find() && road != null ? ((Vector3[])_points.Invoke(null, new object[] { road }))?.Select(v => new P2(v.x, v.z)).ToList() : null; }
            catch (Exception) { return null; }
        }
    }
}
