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
                    "spun status | give <item|fibre|cloth|garments|hildir|all> [n] | finish [radius] | chain [item] | recipes: check Homespun. status shows the retting pools, flax brakes and spinning wheels within 30 m (what's queued, done, and how far on) and the spindle in hand; give puts items of the chain in the bag (an item's prefab or one of the groups; cheats only); finish runs the stations near you an hour on, as if you'd been away (cheats only), and finishes the thread on the spindle; chain shows what an item (or every garment) comes to in what's gathered, with and without Byre's fleece; recipes lists Homespun's recipes and where each is made.",
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
                            ["wild"] = ZoneSystem.instance?.m_vegetation.Count(v => v.m_prefab != null && v.m_prefab == Plants.Get(Plants.Wild)) ?? 0 } });
                    break;
                }
                case "give":
                {
                    if (!Cheats()) { error("spun give makes items: only in single player, for the host, or for a server admin with devcommands on"); yield break; }
                    string what = a.Length > 2 ? a[2] : "all"; int n = (int)F(a, 3, 10);
                    string[] fibre = { Names.Flax, Names.RettedFlax, Names.LineFlax, Names.Nettle, Names.RettedNettle, Names.LineNettle, Names.LoxPelt, Names.Wool, Names.Yarn, Names.Thread, Names.Fleece };
                    string[] cloth = { Names.LinenBolt, Names.WoolBolt, Names.Band, Names.Spindle };
                    string[] garments = Chain.Garments.Select(g => g.Prefab).ToArray();
                    string[] list = what == "fibre" ? fibre : what == "cloth" ? cloth : what == "garments" ? garments : what == "hildir" ? Chain.Hildir.Select(h => h.item).ToArray()
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
