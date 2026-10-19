using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Homespun
{
    // A "spun" command for Claude Tools, found while the game runs (no reference), to check the cloth-making chain in a live game.
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
                found.GetType().GetMethod("RegisterCommand", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[]{Plugin.Name, "spun",
                    "spun status | give <item|fibre|cloth|garments|hildir|dyes|dyed|all> [n] | finish | steep [radius] | feed <item> [n] | drops [rolls] | fresh [meadows|forest|mountain] [n] [edge] | chain [item] | recipes: check Homespun. status shows the retting pools, flax brakes and spinning wheels within 30 m (what's queued, done, and how far on), the dye vats (what's in them and what they're doing) and the spindle in hand; steep finishes the batch in the dye vats within 60 m now (cheats only); give puts items of the chain in the bag (an item's prefab or one of the groups; cheats only); finish runs the stations near you an hour on, as if you'd been away (cheats only), and finishes the thread on the spindle; feed puts an item straight into the nearest station within 20 m that takes it, as if from a bag (cheats only); drops rolls a greydwarf's drops many times and counts the nettle; fresh lists the nearest ground of a biome the world hasn't made yet (where wild nettle and lichen can still grow), for a trip; chain shows what an item (or every garment) comes to in what's gathered, with and without Byre's fleece; recipes lists Homespun's recipes and where each is made.",
                    (Func<string[], Action<JObject>, Action<string>, IEnumerator>)Run});
                Plugin.Log("Claude Tools found: spun command added");
            }
            catch (Exception e) { Plugin.Log("Could not add the spun command: " + e.Message); }
        }
        internal static void Stop()
        {
            try { _tools?.GetType().GetMethod("UnregisterAll", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[] { Plugin.Name }); } catch (Exception) { }
            _tools = null;
        }
        // The game's own test: single player or the host, or devcommands on and a server admin.
        private static bool Cheats() => ZNet.instance != null && (ZNet.instance.IsServer() || global::Console.instance != null && global::Console.instance.IsCheatsEnabled());
        private static JArray V(Vector3 v) => new JArray(Math.Round(v.x, 1), Math.Round(v.y, 1), Math.Round(v.z, 1));
        private static float F(string[] a, int i, float or) => a.Length > i && float.TryParse(a[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : or;

        private static JObject Station(Smelter s, Vector3 me)
        {
            ZDO z = s.GetComponent<ZNetView>()?.GetZDO();
            var o = new JObject { ["station"] = Utils.GetPrefabName(s.gameObject), ["at"] = V(s.transform.position), ["distance"] = Math.Round(Vector3.Distance(s.transform.position, me), 1) };
            if (z == null) return o;
            int queued = z.GetInt(ZDOVars.s_queued);
            var q = new JArray(); for (int i = 0; i < queued && i < 40; i++) q.Add(z.GetString("item" + i));
            o["queued"] = q; o["bake"] = Math.Round(z.GetFloat(ZDOVars.s_bakeTimer), 1); o["perProduct"] = s.m_secPerProduct;
            o["done"] = z.GetInt(ZDOVars.s_spawnAmount) > 0 ? $"{z.GetInt(ZDOVars.s_spawnAmount)} {z.GetString(ZDOVars.s_spawnOre)}" : "";
            o["takes"] = new JArray(s.m_conversion.Where(c => c?.m_from != null && c.m_to != null).Select(c => $"{c.m_from.name} -> {c.m_to.name}"));
            o["owner"] = z.IsOwner();
            return o;
        }

        private static IEnumerator Run(string[] a, Action<JObject> output, Action<string> error)
        {
            Player me = Player.m_localPlayer;
            if (me == null || ZNet.instance == null) { error("needs the player in a world"); yield break; }
            string verb = a.Length > 1 ? a[1] : "status";
            Vector3 at = me.transform.position;
            switch (verb)
            {
                case "status":
                {
                    var stations = new JArray(UnityEngine.Object.FindObjectsByType<Smelter>(FindObjectsSortMode.None)
                        .Where(s => s != null && Vector3.Distance(s.transform.position, at) < 30 && (Kit.Ours(Utils.GetPrefabName(s.gameObject)) || Utils.GetPrefabName(s.gameObject) == Names.Wheel))
                        .OrderBy(s => Vector3.Distance(s.transform.position, at)).Select(s => Station(s, at)));
                    var bag = new JObject();
                    foreach (string n in Things.AllItems.Concat(new[] { Names.Flax, Names.Thread, Names.LoxPelt, Names.Fleece }))
                    {
                        GameObject p = Things.Get(n) ?? ZNetScene.instance.GetPrefab(n);
                        int k = p == null ? 0 : me.GetInventory().CountItems(p.GetComponent<ItemDrop>().m_itemData.m_shared.m_name);
                        if (k > 0) bag[n] = k;
                    }
                    output(new JObject { ["stations"] = stations, ["spindle"] = new JObject { ["holding"] = Spindle.Holding(me), ["spinning"] = Spindle.On?.From ?? "", ["spun"] = Math.Round(Spindle.Spun, 1), ["of"] = Spindle.On?.Seconds ?? 0 },
                        ["bag"] = bag, ["items"] = Things.AllItems.Count(n => Things.Get(n) != null), ["pieces"] = Kit.Made.Count, ["byre"] = ZNetScene.instance.GetPrefab(Names.Fleece) != null,
                        ["nettle"] = new JObject { ["made"] = Plants.All.Count(x => Plants.Get(x) != null), ["sown"] = Plants.Cultivator(ZNetScene.instance)?.m_pieces.Contains(Plants.Get(Plants.Sapling)) ?? false,
                            ["wild"] = ZoneSystem.instance?.m_vegetation.Count(v => v.m_prefab != null && v.m_prefab == Plants.Get(Plants.Wild)) ?? 0,
                            ["lichen"] = ZoneSystem.instance?.m_vegetation.Count(v => v.m_prefab != null && v.m_prefab == Plants.Get(Plants.WildLichen)) ?? 0 },
                        ["vats"] = new JArray(UnityEngine.Object.FindObjectsByType<DyeVat>(FindObjectsSortMode.None).Where(v => Vector3.Distance(v.transform.position, at) < 30)
                            .Select(v => new JObject { ["at"] = V(v.transform.position), ["status"] = v.Status(), ["holds"] = string.Join(", ", DyeVat.Contents(v.GetComponent<Container>().GetInventory()).Select(c => $"{c.stack} {c.prefab}")) })) });
                    break;
                }
                case "give":
                {
                    if (!Cheats()) { error("spun give makes items: only in single player, for the host, or for a server admin with devcommands on"); yield break; }
                    string what = a.Length > 2 ? a[2] : "all"; int n = (int)F(a, 3, 10);
                    string[] fibre = { Names.Flax, Names.RettedFlax, Names.LineFlax, Names.Nettle, Names.RettedNettle, Names.LineNettle, Names.LoxPelt, Names.Wool, Names.Yarn, Names.Thread, Names.Fleece };
                    string[] cloth = { Names.LinenBolt, Names.WoolBolt, Names.Band, Names.Spindle };
                    string[] garments = Chain.Garments.Select(g => g.Prefab).ToArray();
                    string[] dyes = Dye.Dyes.Select(d => d.Stuff).ToArray();
                    string[] list = what == "fibre" ? fibre : what == "cloth" ? cloth : what == "garments" ? garments : what == "hildir" ? Chain.Hildir.Select(h => h.item).ToArray()
                        : what == "dyes" ? dyes : what == "dyed" ? Dye.Variants.ToArray()
                        : what == "all" ? fibre.Concat(cloth).Concat(garments).ToArray() : new[] { what };
                    var given = new JObject();
                    foreach (string item in list)
                    {
                        GameObject p = Things.Get(item) ?? ZNetScene.instance.GetPrefab(item);
                        if (p?.GetComponent<ItemDrop>() == null) { given[item] = "no such item"; continue; }
                        int count = Math.Min(n, p.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize * 2);
                        given[item] = me.GetInventory().AddItem(p, count) ? count : 0;
                    }
                    output(new JObject { ["given"] = given });
                    break;
                }
                case "finish":
                {
                    if (!Cheats()) { error("spun finish moves station clocks: only in single player, for the host, or for a server admin with devcommands on"); yield break; }
                    float radius = F(a, 2, 20);
                    var ran = new JArray();
                    foreach (Smelter s in UnityEngine.Object.FindObjectsByType<Smelter>(FindObjectsSortMode.None))
                    {
                        string name = Utils.GetPrefabName(s.gameObject);
                        if (Vector3.Distance(s.transform.position, at) > radius || !(Kit.Ours(name) || name == Names.Wheel)) continue;
                        ZNetView view = s.GetComponent<ZNetView>();
                        if (view == null || !view.IsValid()) continue;
                        if (!view.IsOwner()) view.ClaimOwnership();
                        // (an hour since it last looked: its next tick works that through, as for a station come back into a loaded area)
                        view.GetZDO().Set(ZDOVars.s_startTime, ZNet.instance.GetTime().AddSeconds(-3600).Ticks);
                        ran.Add(name + " " + V(s.transform.position));
                    }
                    if (Spindle.On != null) Spindle.Spun = Spindle.On.Seconds;
                    yield return new WaitForSeconds(2.5f);
                    output(new JObject { ["ran"] = ran, ["spindle"] = Spindle.On != null });
                    break;
                }
                case "feed":
                {
                    if (!Cheats()) { error("spun feed makes items: only in single player, for the host, or for a server admin with devcommands on"); yield break; }
                    string item = a.Length > 2 ? a[2] : Names.Nettle; int n = (int)F(a, 3, 1);
                    Smelter s = UnityEngine.Object.FindObjectsByType<Smelter>(FindObjectsSortMode.None)
                        .Where(x => x != null && Vector3.Distance(x.transform.position, at) < 20 && x.m_conversion.Any(c => c?.m_from != null && c.m_from.name == item))
                        .OrderBy(x => Vector3.Distance(x.transform.position, at)).FirstOrDefault();
                    ZNetView view = s != null ? s.GetComponent<ZNetView>() : null;
                    if (view == null || !view.IsValid()) { error($"no station within 20 m takes {item}"); yield break; }
                    for (int i = 0; i < n; i++) view.InvokeRPC("RPC_AddOre", item, false); // (name, cheated)
                    yield return new WaitForSeconds(0.5f);
                    output(Station(s, at));
                    break;
                }
                case "steep":
                {
                    if (!Cheats()) { error("spun steep moves vat clocks: only in single player, for the host, or for a server admin with devcommands on"); yield break; }
                    var ran = new JArray();
                    foreach (DyeVat v in UnityEngine.Object.FindObjectsByType<DyeVat>(FindObjectsSortMode.None).Where(v => Vector3.Distance(v.transform.position, at) < 60))
                    {
                        ZNetView view = v.GetComponent<ZNetView>();
                        if (view == null || !view.IsValid()) continue;
                        if (!view.IsOwner()) view.ClaimOwnership();
                        view.GetZDO().Set(DyeVat.Since, ZNet.instance.GetTime().AddSeconds(-Dye.SteepSeconds - 1).Ticks);
                        ran.Add(V(v.transform.position));
                    }
                    yield return new WaitForSeconds(1.5f);
                    output(new JObject { ["steeped"] = ran });
                    break;
                }
                case "vat":
                {
                    // (test: "vat put <item> [n]" makes items straight into the nearest vat within 60 m; "vat clear" empties it)
                    if (!Cheats()) { error("spun vat makes items: only in single player, for the host, or for a server admin with devcommands on"); yield break; }
                    DyeVat v = UnityEngine.Object.FindObjectsByType<DyeVat>(FindObjectsSortMode.None).Where(x => Vector3.Distance(x.transform.position, at) < 60).OrderBy(x => Vector3.Distance(x.transform.position, at)).FirstOrDefault();
                    ZNetView view = v != null ? v.GetComponent<ZNetView>() : null;
                    if (view == null || !view.IsValid()) { error("no dye vat within 60 m"); yield break; }
                    if (!view.IsOwner()) view.ClaimOwnership();
                    Inventory inv = v.GetComponent<Container>().GetInventory();
                    if (a.Length > 2 && a[2] == "clear") inv.RemoveAll();
                    else if (a.Length > 3 && a[2] == "put")
                    {
                        GameObject p = Things.Get(a[3]) ?? ZNetScene.instance.GetPrefab(a[3]);
                        if (p?.GetComponent<ItemDrop>() == null) { error("no item " + a[3]); yield break; }
                        inv.AddItem(p, (int)F(a, 4, 1));
                    }
                    yield return new WaitForSeconds(0.2f);
                    output(new JObject { ["at"] = V(v.transform.position), ["status"] = v.Status(), ["holds"] = string.Join(", ", DyeVat.Contents(inv).Select(c => $"{c.stack} {c.prefab}")) });
                    break;
                }
                case "drops":
                {
                    CharacterDrop drops = ZNetScene.instance.GetPrefab("Greydwarf")?.GetComponent<CharacterDrop>();
                    if (drops == null) { error("no greydwarf drops"); yield break; }
                    int rolls = (int)F(a, 2, 1000);
                    var times = new System.Collections.Generic.Dictionary<string, int>(); var counts = new System.Collections.Generic.Dictionary<string, int>();
                    for (int i = 0; i < rolls; i++)
                        foreach (var d in drops.GenerateDropList().Where(d => d.Key != null))
                        {
                            times[d.Key.name] = (times.TryGetValue(d.Key.name, out int t) ? t : 0) + 1;
                            counts[d.Key.name] = (counts.TryGetValue(d.Key.name, out int c) ? c : 0) + d.Value;
                        }
                    // (each drop: how often in the rolls, and how many on average when it drops; the world's loot modifiers scale every one)
                    output(new JObject { ["rolls"] = rolls, ["drops"] = new JObject(times.Keys.Select(k => new JProperty(k, $"{100f * times[k] / rolls:0.#}% x{(float)counts[k] / times[k]:0.##}"))) });
                    break;
                }
                case "fresh":
                {
                    bool forest = a.Length > 2 && a[2].StartsWith("f"), mountain = a.Length > 2 && a[2].StartsWith("mo");
                    Heightmap.Biome want = forest ? Heightmap.Biome.BlackForest : mountain ? Heightmap.Biome.Mountain : Heightmap.Biome.Meadows;
                    int n = (int)F(a, 3, 5);
                    MethodInfo made = typeof(ZoneSystem).GetMethod("IsZoneGenerated", BindingFlags.NonPublic | BindingFlags.Instance);
                    var found = new JArray();
                    // (rings out from here a zone (64 m) apart, the zone's centre: the biome there, and the world not yet made there)
                    for (int r = 1; r < 80 && found.Count < n; r++)
                        for (int i = 0; i < r * 8 && found.Count < n; i++)
                        {
                            float ang = i * Mathf.PI * 2 / (r * 8);
                            Vector3 p = at + new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang)) * r * 64f;
                            var zone = ZoneSystem.GetZone(p); Vector3 c = ZoneSystem.GetZonePos(zone);
                            if (WorldGenerator.instance.GetBiome(c) != want || (bool)made.Invoke(ZoneSystem.instance, new object[] { zone })) continue;
                            if (a.Contains("edge") && WorldGenerator.instance.GetBiomeArea(c) != Heightmap.BiomeArea.Edge) continue; // (as the game decides a zone's area for its vegetation)
                            float h = WorldGenerator.instance.GetHeight(c.x, c.z);
                            if (h < ZoneSystem.instance.m_waterLevel + 1) continue;
                            string spot = $"{c.x:0} {h + 1:0.0} {c.z:0}";
                            if (!found.Any(t => (string)t == spot)) found.Add(spot);
                        }
                    output(new JObject { ["biome"] = want.ToString(), ["fresh"] = found });
                    break;
                }
                case "chain":
                {
                    var o = new JObject();
                    foreach (string item in a.Length > 2 ? new[] { a[2] } : Chain.Garments.Select(g => g.Prefab).ToArray())
                        o[item] = new JObject { ["withFleece"] = Chain.RawText(Chain.Raw(item)), ["withoutByre"] = Chain.RawText(Chain.Raw(item, fleece: false)) };
                    o["steps"] = new JArray(Chain.Conversions.Select(c => $"{c.Station}: {c.From} -> {c.Yield} {c.To}" + (c.Seconds > 0 ? $" ({c.Seconds}s)" : "")));
                    output(o);
                    break;
                }
                case "recipes":
                {
                    var o = new JArray(ObjectDB.instance.m_recipes.Where(r => r != null && r.name.StartsWith("Recipe_Homespun_"))
                        .Select(r => $"{r.name}: {r.m_amount} {r.m_item?.name} at {r.m_craftingStation?.m_name ?? "hand"} <- {string.Join(", ", r.m_resources.Select(q => $"{q.m_amount} {q.m_resItem?.name}"))}"));
                    output(new JObject { ["recipes"] = o, ["count"] = o.Count });
                    break;
                }
                default: error("spun status | give <item|fibre|cloth|garments|hildir|all> [n] | finish [radius] | chain [item] | recipes"); break;
            }
        }
    }
}
