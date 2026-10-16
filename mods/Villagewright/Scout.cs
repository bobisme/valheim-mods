using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Villagewright
{
    // Finding a site: Meadows ground as the world made it (loaded or not), scored for how dry, level and open it is, away from
    // anything anyone has built (every piece the game knows of, loaded or not).
    public sealed partial class Plugin
    {
        private static readonly System.Reflection.FieldInfo ObjectsById = AccessTools.Field(typeof(ZDOMan), "m_objectsByID");

        internal JObject Scout(Vector3 from, float range, float radius, bool coast)
        {
            var built = new List<Vector2>();
            var pieceHashes = new HashSet<int>();
            if (ObjectsById?.GetValue(ZDOMan.instance) is Dictionary<ZDOID, ZDO> all)
                foreach (var zdo in all.Values)
                {
                    Vector3 p = zdo.GetPosition();
                    if (Mathf.Abs(p.x - from.x) > range + radius + 40 || Mathf.Abs(p.z - from.z) > range + radius + 40) continue;
                    int hash = zdo.GetPrefab();
                    if (!pieceHashes.Contains(hash))
                    {
                        GameObject prefab = ZNetScene.instance.GetPrefab(hash);
                        if (prefab == null || prefab.GetComponent<Piece>() == null) continue;
                        pieceHashes.Add(hash);
                    }
                    built.Add(new Vector2(p.x, p.z));
                }
            var gen = WorldGenerator.instance; float water = ZoneSystem.instance.m_waterLevel;
            var results = new List<JObject>();
            for (float dz = -range; dz <= range; dz += 32)
                for (float dx = -range; dx <= range; dx += 32)
                {
                    float cx = from.x + dx, cz = from.z + dz;
                    if (built.Any(b => (b - new Vector2(cx, cz)).sqrMagnitude < (radius + 30) * (radius + 30))) continue;
                    int n = 0, dry = 0, meadow = 0; double sum = 0, sum2 = 0;
                    for (float r = 0; r <= radius; r += radius / 4)
                        for (int a = 0; a < (r == 0 ? 1 : 16); a++)
                        {
                            float x = cx + Mathf.Cos(a * Mathf.PI / 8) * r, z = cz + Mathf.Sin(a * Mathf.PI / 8) * r;
                            float h = gen.GetHeight(x, z); n++;
                            if (gen.GetBiome(x, z) == Heightmap.Biome.Meadows) meadow++;
                            if (h > water + 1) { dry++; sum += h; sum2 += h * h; }
                        }
                    if (meadow < n * 0.85 || dry < n * (coast ? 0.55 : 0.95) || (coast && dry > n * 0.9)) continue;
                    double mean = sum / dry, sd = Math.Sqrt(Math.Max(0, sum2 / dry - mean * mean));
                    results.Add(new JObject { ["x"] = Mathf.Round(cx), ["z"] = Mathf.Round(cz), ["sd"] = Math.Round(sd, 2), ["dry"] = Math.Round(dry / (double)n, 2), ["ground"] = Math.Round(mean, 1), ["distance"] = Mathf.Round(new Vector2(dx, dz).magnitude) });
                }
            return new JObject { ["builtNearby"] = built.Count, ["sites"] = new JArray(results.OrderBy(r => (double)r["sd"] + (double)r["distance"] / 400).Take(8)) };
        }
    }
}
