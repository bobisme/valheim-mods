using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BuildKit
{
    // A "kit" command for Claude Tools, found while the game runs (no reference): to see and try the kit's pieces in a live game.
    internal static class Tools
    {
        private const string ClaudeToolsGuid = "com.quad.claudetools";
        private static BaseUnityPlugin _tools;
        private static float _nextCheck;

        internal static void Tick()
        {
            if (Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + 5;
            BaseUnityPlugin found = BepInEx.Bootstrap.Chainloader.PluginInfos.TryGetValue(ClaudeToolsGuid, out PluginInfo info) ? info.Instance : null;
            if (found == _tools) return;
            _tools = found;
            if (found == null) return;
            try
            {
                found.GetType().GetMethod("RegisterCommand", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[]{Plugin.Name, "kit",
                    "kit list: the kit's pieces. kit mats <filter>: game materials whose name or texture has the filter (prefab, material, shader, texture). " +
                    "kit look <sod|earth|bark> <prefab/material prefix> [scale]: try another texture (pieces made again). " +
                    "kit place <piece> <x> <y> <z> [yaw]: build one there (as if by the player). kit clear [radius]: take down the kit's pieces near the player.",
                    (Func<string[], Action<JObject>, Action<string>, IEnumerator>)Run});
            }
            catch (Exception e) { Plugin.Warn("Could not add the kit command: " + e.Message); }
        }
        internal static void Stop()
        {
            try { _tools?.GetType().GetMethod("UnregisterAll", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[] { Plugin.Name }); } catch (Exception) { }
            _tools = null;
        }
        private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

        private static IEnumerator Run(string[] a, Action<JObject> output, Action<string> error)
        {
            ZNetScene scene = ZNetScene.instance;
            if (scene == null) { error("needs a world"); yield break; }
            string sub = a.Length > 1 ? a[1] : "list";
            switch (sub)
            {
                case "mem": // (what reloads leave: the kit's materials, meshes, icons and prefab holders alive, of every copy)
                    output(new JObject
                    {
                        ["materials"] = Resources.FindObjectsOfTypeAll<Material>().Count(m => m.name.StartsWith("BuildKit ", StringComparison.Ordinal)),
                        ["meshes"] = Resources.FindObjectsOfTypeAll<Mesh>().Count(m => Kit.All.Any(d => m.name.StartsWith(d.Name, StringComparison.Ordinal))),
                        ["holders"] = Resources.FindObjectsOfTypeAll<GameObject>().Count(g => g.name == "BuildKit prefabs"),
                        ["unityMB"] = Math.Round(UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576.0),
                    });
                    break;
                case "list":
                    output(new JObject { ["pieces"] = new JArray(Kit.All.Select(d => Kit.Made.TryGetValue(d.Name, out GameObject p) && p != null
                        ? $"{d.Name} ({d.Title}): {(Kit.Hammer()?.m_pieces.Contains(p) == true ? "in the hammer" : "NOT in the hammer")}, from {d.From}" : $"{d.Name}: not made")),
                        ["gameTurfInHammer"] = Kit.Hammer()?.m_pieces.Any(x => x != null && x.name == "turf_roof") == true,
                        // (the turf standing near the player: which sides are open, bits -z +x +z -x)
                        ["standing"] = new JArray(TurfLook.All.Where(t => t != null && Player.m_localPlayer != null && Vector3.Distance(t.transform.position, Player.m_localPlayer.transform.position) < 150)
                            .Select(t => $"{Utils.GetPrefabName(t.gameObject)} at {t.transform.position.x:0.0},{t.transform.position.y:0.0},{t.transform.position.z:0.0} yaw {t.transform.eulerAngles.y:0}: open {t.Mask}")) });
                    break;
                case "mats":
                    {
                        string f = a.Length > 2 ? a[2].ToLowerInvariant() : "";
                        var hits = new JArray(); var seen = new System.Collections.Generic.HashSet<string>();
                        foreach (GameObject go in scene.m_prefabs)
                        {
                            if (go == null) continue;
                            foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
                                foreach (Material m in r.sharedMaterials)
                                {
                                    if (m == null) continue;
                                    Texture t = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                                    if (!(m.name.ToLowerInvariant().Contains(f) || (t != null && t.name.ToLowerInvariant().Contains(f)) || (m.shader != null && m.shader.name.ToLowerInvariant().Contains(f)))) continue;
                                    if (!seen.Add(m.name)) continue;
                                    hits.Add($"{go.name}/{m.name} [{m.shader?.name}] tex {t?.name} {t?.width}x{t?.height}");
                                    if (hits.Count >= 60) break;
                                }
                            if (hits.Count >= 60) break;
                        }
                        output(new JObject { ["materials"] = hits });
                    }
                    break;
                case "look":
                    {
                        if (a.Length < 4) { error("kit look <sod|earth|bark> <prefab/material> [scale]"); yield break; }
                        if (a[2] == "sod") { Look.SodTexture = a[3]; if (a.Length > 4) Look.SodScale = F(a[4]); }
                        else if (a[2] == "earth") Look.EarthTexture = a[3];
                        else if (a[2] == "bark") Look.BarkTexture = a[3];
                        else { error("sod, earth or bark"); yield break; }
                        Kit.Remake();
                        output(new JObject { ["sod"] = Look.SodTexture, ["earth"] = Look.EarthTexture, ["bark"] = Look.BarkTexture, ["scale"] = Look.SodScale });
                    }
                    break;
                case "stage": // (test: "stage <yaw> <pitch> <dist> <piece@x,y,z,yaw> ...": those pieces set out as ghosts far below the world, drawn to a PNG)
                    {
                        var parts = new System.Collections.Generic.List<(GameObject, Vector3, float)>();
                        foreach (string spec in a.Skip(5))
                        {
                            string[] q = spec.Split('@'); string[] n = q[1].Split(',');
                            GameObject prefab = scene.GetPrefab(q[0]); if (prefab == null) { error("no piece " + q[0]); yield break; }
                            parts.Add((prefab, new Vector3(F(n[0]), F(n[1]), F(n[2])), n.Length > 3 ? F(n[3]) : 0));
                        }
                        string path = System.IO.Path.Combine(Paths.BepInExRootPath, "claude", "shots", $"stage-{DateTime.Now:HHmmss-fff}.png");
                        output(new JObject { ["stage"] = Icons.Stage(parts, F(a[2]), F(a[3]), F(a[4]), path) });
                    }
                    break;
                case "ground": // (test: "ground x0 z0 x1 z1 [step]": the ground's height on a grid, to find level ground for samples)
                    {
                        float x0 = F(a[2]), z0 = F(a[3]), x1 = F(a[4]), z1 = F(a[5]), st = a.Length > 6 ? F(a[6]) : 2;
                        var rows = new JArray();
                        for (float z = z0; z <= z1 + 0.01f; z += st) { var row = new System.Collections.Generic.List<string>(); for (float x = x0; x <= x1 + 0.01f; x += st) row.Add(ZoneSystem.instance.GetGroundHeight(new Vector3(x, 0, z)).ToString("0.0")); rows.Add($"z {z:0}: " + string.Join(" ", row)); }
                        output(new JObject { ["x"] = $"{x0:0}..{x1:0} by {st}", ["rows"] = rows });
                    }
                    break;
                case "place":
                    {
                        if (a.Length < 6) { error("kit place <piece> <x> <y> <z> [yaw]"); yield break; }
                        GameObject prefab = scene.GetPrefab(a[2]);
                        if (prefab == null) { error("no piece " + a[2]); yield break; }
                        var at = new Vector3(F(a[3]), 0, F(a[5]));
                        at.y = a[4].StartsWith("g") ? ZoneSystem.instance.GetGroundHeight(at) + (a[4].Length > 1 ? F(a[4].Substring(1)) : 0) : F(a[4]); // ("g", "g+1.5": on the ground, or that far above it)
                        GameObject go = Object.Instantiate(prefab, at, Quaternion.Euler(0, a.Length > 6 ? F(a[6]) : 0, 0));
                        Piece piece = go.GetComponent<Piece>();
                        if (Player.m_localPlayer != null) piece?.SetCreator(Player.m_localPlayer.GetPlayerID(), Splatform.PlatformManager.DistributionPlatform.LocalUser.PlatformUserID);
                        go.GetComponent<WearNTear>()?.OnPlaced();
                        output(new JObject { ["placed"] = a[2], ["at"] = new JArray(at.x, at.y, at.z) });
                    }
                    break;
                case "clear":
                    {
                        Player me = Player.m_localPlayer; if (me == null) { error("no player"); yield break; }
                        float radius = a.Length > 2 ? F(a[2]) : 30;
                        var hashes = Kit.All.Select(d => d.Name.GetStableHashCode()).ToList();
                        int n = 0;
                        var near = new System.Collections.Generic.List<Piece>(); Piece.GetAllPiecesInRadius(me.transform.position, radius, near);
                        foreach (Piece p in near)
                        {
                            if (p == null || Vector3.Distance(p.transform.position, me.transform.position) > radius) continue;
                            ZNetView v = p.GetComponent<ZNetView>();
                            if (v == null || !v.IsValid() || !hashes.Contains(v.GetZDO().GetPrefab())) continue;
                            v.ClaimOwnership(); v.Destroy(); n++;
                        }
                        output(new JObject { ["removed"] = n });
                    }
                    break;
                case "icons": // (each piece's build-menu icon, side by side, as a picture: kit icons)
                    {
                        var icons = Kit.All.Select(d => Kit.Made.TryGetValue(d.Name, out GameObject p) && p != null ? p.GetComponent<Piece>()?.m_icon : null).ToList();
                        var sheet = new Texture2D(128 * icons.Count, 128, TextureFormat.RGBA32, false);
                        sheet.SetPixels(Enumerable.Repeat(new Color(0.25f, 0.22f, 0.2f, 1), sheet.width * sheet.height).ToArray());
                        for (int i = 0; i < icons.Count; i++)
                        {
                            if (icons[i] == null) continue;
                            Color[] px = icons[i].texture.GetPixels();
                            for (int k = 0; k < px.Length; k++) { int x = k % 128, y = k / 128; Color b = sheet.GetPixel(i * 128 + x, y); sheet.SetPixel(i * 128 + x, y, Color.Lerp(b, px[k], px[k].a)); }
                        }
                        sheet.Apply();
                        string path = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "claude", "shots", "kit-icons.png");
                        System.IO.File.WriteAllBytes(path, ImageConversion.EncodeToPNG(sheet));
                        Object.Destroy(sheet);
                        output(new JObject { ["icons"] = path });
                    }
                    break;
                case "smoke": // (how the game's smoke is made: the hearth's spawner, its smoke's collider and layer)
                    {
                        SmokeSpawner sp = scene.GetPrefab(a.Length > 2 ? a[2] : "hearth")?.GetComponentInChildren<SmokeSpawner>(true);
                        GameObject smoke = sp?.m_smokePrefab;
                        var cols = smoke != null ? smoke.GetComponentsInChildren<Collider>(true) : new Collider[0];
                        string Layers(int mask) => string.Join(",", Enumerable.Range(0, 32).Where(i => (mask & (1 << i)) != 0).Select(LayerMask.LayerToName));
                        output(new JObject { ["prefab"] = smoke?.name, ["interval"] = sp?.m_interval, ["testRadius"] = sp?.m_testRadius, ["testMask"] = sp != null ? Layers(sp.m_testMask.value) : "",
                            ["layer"] = smoke != null ? LayerMask.LayerToName(smoke.layer) : "", ["colliders"] = new JArray(cols.Select(c => c is SphereCollider sc ? $"sphere r {sc.radius} scale {smoke.transform.localScale}" : c.GetType().Name)),
                            ["collidesWith"] = smoke != null ? string.Join(",", Enumerable.Range(0, 32).Where(i => !Physics.GetIgnoreLayerCollision(smoke.layer, i) && LayerMask.LayerToName(i) != "").Select(LayerMask.LayerToName)) : "",
                            ["vel"] = smoke?.GetComponent<Smoke>()?.m_vel.ToString(), ["force"] = smoke?.GetComponent<Smoke>()?.m_force, ["ttl"] = smoke?.GetComponent<Smoke>()?.m_ttl });
                    }
                    break;
                case "smoketest": // (kit smoketest <piece> x z: the piece on four poles 2 m up (3 m at its high side), a lit fire pit under its middle)
                    {
                        string name = a[2]; var at = new Vector3(F(a[3]), 0, F(a[4])); at.y = ZoneSystem.instance.GetGroundHeight(at);
                        Kit.Def d = Kit.Defs.FirstOrDefault(x => x.Name == name);
                        if (d == null) { error("no kit piece " + name); yield break; }
                        var made = new JArray();
                        GameObject Put(string prefab, Vector3 p, float yaw)
                        {
                            GameObject go = Object.Instantiate(scene.GetPrefab(prefab), p, Quaternion.Euler(0, yaw, 0));
                            go.GetComponent<Piece>()?.SetCreator(Player.m_localPlayer.GetPlayerID(), Splatform.PlatformManager.DistributionPlatform.LocalUser.PlatformUserID);
                            go.GetComponent<WearNTear>()?.OnPlaced();
                            made.Add($"{prefab} {p.x:0.00} {p.y:0.00} {p.z:0.00}");
                            return go;
                        }
                        float baseY = at.y + 2f;
                        foreach (float x in new[] { -0.9f, 0.9f })
                            foreach (float z in new[] { -0.9f, 0.9f })
                            {
                                float top = baseY + Roofs.Height(d.Shape, d.Steep, x, z); // (the roof's underside over this pole)
                                // (poles stand on their middle: a 2 m one from the ground, a 1 m one on it where the roof is higher)
                                Put("wood_pole2", new Vector3(at.x + x, at.y + 1f, at.z + z), 0);
                                if (top - at.y > 2.5f) Put("wood_pole", new Vector3(at.x + x, at.y + 2.5f, at.z + z), 0);
                            }
                        Put(name, new Vector3(at.x, baseY, at.z), 0);
                        GameObject fire = Put("fire_pit", at, 0);
                        var fv = fire.GetComponent<ZNetView>(); if (fv != null && fv.IsValid()) fv.GetZDO().Set(ZDOVars.s_fuel, fire.GetComponent<Fireplace>().m_maxFuel);
                        output(new JObject { ["built"] = made, ["capUnderside"] = baseY + BuildKit.TurfMesh.HoodBase(d.Shape, d.Steep) });
                    }
                    break;
                case "smokecount": // (kit smokecount x z capY: smoke balls within 3 m, those above capY (out through the vent), those under the roof)
                    {
                        var at = new Vector3(F(a[2]), 0, F(a[3])); float cap = F(a[4]);
                        var near = Object.FindObjectsOfType<Smoke>().Where(sm => sm != null && Vector2.Distance(new Vector2(sm.transform.position.x, sm.transform.position.z), new Vector2(at.x, at.z)) < 3f).ToList();
                        output(new JObject { ["near"] = near.Count, ["aboveCap"] = near.Count(sm => sm.transform.position.y > cap), ["underRoof"] = near.Count(sm => sm.transform.position.y < cap - 1.2f),
                            ["balls"] = string.Join(" ", near.OrderBy(sm => sm.transform.position.y).Select(sm => $"({sm.transform.position.x - at.x:0.0},{sm.transform.position.y:0.0},{sm.transform.position.z - at.z:0.0})")) });
                    }
                    break;
                case "probe": // (what's built at a spot: kit probe x y z [radius])
                    {
                        var at = new Vector3(F(a[2]), F(a[3]), F(a[4])); float r = a.Length > 5 ? F(a[5]) : 0.3f;
                        var hits = Physics.OverlapSphere(at, r, LayerMask.GetMask("piece", "piece_nonsolid"), QueryTriggerInteraction.Ignore);
                        output(new JObject { ["hits"] = new JArray(hits.Select(h => $"{h.name} on {(h.GetComponentInParent<Piece>() is Piece pc ? Utils.GetPrefabName(pc.gameObject) + " at " + pc.transform.position : "?")} layer {LayerMask.LayerToName(h.gameObject.layer)}")) });
                    }
                    break;
                default: error("kit list|mats|look|place|clear|probe"); break;
            }
        }
    }
}
