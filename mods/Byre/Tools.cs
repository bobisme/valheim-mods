using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Byre
{
    // A "byre" command for Claude Tools, found while the game runs (no reference), to check herds in a live game without waiting days.
    // Test animals are tagged ("byre_test" in their ZDO) and clear removes only tagged ones, only within its radius.
    internal static class Tools
    {
        private const string ClaudeToolsGuid = "com.quad.claudetools";
        private const string Help = "byre herds [r] | spawn <sheep|goat|lamb|kid> [tame] [n] | ready [r] | age <hours> [r] | feed [r] | bait [item] [n] | use | troughs [r] | troughs fill <item> [n] | clear [r] | grip <ByreShears|ByreMilkPail> ex ey ez: check Byre. herds lists the animals within r m (40) with their kind, tameness, hunger and wool or milk; spawn puts n (1) tagged test animals 3 m ahead (tame: already tamed); ready makes the wool and milk of tame ones within r (10) ready now; age moves their clocks (last shorn or milked, and a lamb's or kid's birth) back that many game hours; feed fills them as a meal would; bait drops n (4) of an item (Raspberry) at the feet of the nearest wild one within 80 m; use is E with the tool on the nearest tame one in 5 m; troughs lists the troughs within r (30) and what's in them, troughs fill puts n (10) of an item in the nearest one; clear removes the tagged test animals within r (30), nothing else; grip turns how the hand holds the shears or the pail (the equipoffset's euler angles, the grip point kept in the fist) for the next equip and the one in hand now: put the numbers found in Items.cs's Hand.";
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
                found.GetType().GetMethod("RegisterCommand", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[] { Plugin.Name, "byre", Help,
                    (Func<string[], Action<JObject>, Action<string>, IEnumerator>)Run });
                Plugin.Log("Claude Tools found: byre command added");
            }
            catch (Exception e) { Plugin.Log("Could not add the byre command: " + e.Message); }
        }
        internal static void Stop()
        {
            try { _tools?.GetType().GetMethod("UnregisterAll", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[] { Plugin.Name }); } catch (Exception) { }
            _tools = null;
        }
        private static float F(string[] a, int i, float otherwise) => a.Length > i && float.TryParse(a[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : otherwise;
        private static JArray V(Vector3 v) => new JArray(Math.Round(v.x, 1), Math.Round(v.y, 1), Math.Round(v.z, 1));
        private static Livestock[] Near(Vector3 at, float r) => Live<Livestock>.All.Where(l => l != null && Vector3.Distance(l.transform.position, at) <= r).OrderBy(l => Vector3.Distance(l.transform.position, at)).ToArray();
        private static JObject Facts(Livestock l, Vector3 from) => new JObject
        {
            ["kind"] = l.Kind?.Key, ["young"] = l.Young, ["name"] = l.Body?.m_name, ["at"] = V(l.transform.position), ["dist"] = Math.Round(Vector3.Distance(l.transform.position, from), 1),
            ["tame"] = l.Tamed, ["hungry"] = !l.Fed, ["alerted"] = l.AI != null && l.AI.IsAlerted(), ["ready"] = l.Ready(), ["waitHours"] = Math.Round(l.Remaining() / (Livestock.DayLength / 24), 1),
            ["fleece"] = l.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name == Animals.FleeceName)?.enabled, ["test"] = l.Test, ["owner"] = l.View != null && l.View.IsOwner(),
            ["level"] = l.Body?.GetLevel(), ["health"] = l.Body != null ? Math.Round(l.Body.GetHealth(), 1) : 0,
            ["tamingLeft"] = !l.Tamed && l.Tame != null && l.View != null && l.View.IsValid() ? Math.Round(l.View.GetZDO().GetFloat(ZDOVars.s_tameTimeLeft, l.Tame.m_tamingTime)) : (double?)null,
        };

        private static IEnumerator Run(string[] a, Action<JObject> output, Action<string> error)
        {
            Player me = Player.m_localPlayer;
            if (me == null || ZNet.instance == null) { error("needs the player in a world"); yield break; }
            Vector3 here = me.transform.position;
            string verb = a.Length > 1 ? a[1] : "herds";
            switch (verb)
            {
                case "help": output(new JObject { ["help"] = Help }); break;
                case "herds":
                {
                    Livestock[] all = Near(here, F(a, 2, 40));
                    output(new JObject { ["count"] = all.Length, ["made"] = Kit.Made.Count, ["animals"] = new JArray(all.Select(l => Facts(l, here))) }); break;
                }
                case "spawn":
                {
                    string kind = a.Length > 2 ? a[2].ToLowerInvariant() : "sheep";
                    Species s = Species.ByKey(kind) ?? Species.All.FirstOrDefault(x => x.YoungTitle.Equals(kind, StringComparison.OrdinalIgnoreCase));
                    if (s == null) { error("spawn <sheep|goat|lamb|kid> [tame] [n]"); yield break; }
                    bool young = !s.Key.Equals(kind, StringComparison.OrdinalIgnoreCase), tame = a.Skip(3).Any(x => x == "tame");
                    int n = Math.Max(1, Math.Min(10, (int)F(a, a.Length > 3 && a[3] == "tame" ? 4 : 3, 1)));
                    GameObject prefab = ZNetScene.instance.GetPrefab(young ? s.Young : s.Prefab);
                    if (prefab == null) { error("not made: " + (young ? s.Young : s.Prefab)); yield break; }
                    var made = new JArray();
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 at = here + me.transform.forward * 3f + me.transform.right * (i - (n - 1) / 2f) * 1.5f;
                        if (ZoneSystem.instance != null) at.y = ZoneSystem.instance.GetSolidHeight(at) + 0.2f;
                        GameObject go = UnityEngine.Object.Instantiate(prefab, at, Quaternion.LookRotation(-me.transform.forward));
                        ZNetView view = go.GetComponent<ZNetView>();
                        view.GetZDO().Set(Livestock.TestKey, true);
                        if (tame) go.GetComponent<Character>().SetTamed(true);
                        made.Add(V(at));
                    }
                    yield return null;
                    output(new JObject { ["spawned"] = made, ["prefab"] = prefab.name, ["tame"] = tame }); break;
                }
                case "ready":
                case "age":
                case "feed":
                {
                    float hours = verb == "age" ? F(a, 2, 0) : 0, r = F(a, verb == "age" ? 3 : 2, 10);
                    double back = hours * Livestock.DayLength / 24;
                    var done = new JArray();
                    foreach (Livestock l in Near(here, r).Where(l => l.Tamed || verb == "age"))
                    {
                        if (l.View == null || !l.View.IsValid()) continue;
                        l.View.ClaimOwnership(); ZDO zdo = l.View.GetZDO();
                        if (verb == "ready") l.SetLast(1); // (long ago: ready whatever the period; 0 would read as never shorn, which is ready too)
                        else if (verb == "feed") zdo.Set(ZDOVars.s_tameLastFeeding, ZNet.instance.GetTime().Ticks);
                        else
                        {
                            if (l.LastAt > 0) l.SetLast(Math.Max(1, l.LastAt - back));
                            long born = zdo.GetLong(ZDOVars.s_spawnTime, 0L);
                            if (born > 0) zdo.Set(ZDOVars.s_spawnTime, born - Husbandry.Ticks(back));
                        }
                        done.Add(Facts(l, here));
                    }
                    yield return new WaitForSeconds(2.1f); // (the fleece looks again every 2 s)
                    output(new JObject { ["verb"] = verb, ["animals"] = done }); break;
                }
                case "bait":
                {
                    // (test: food at the feet of the nearest wild one within 80 m, without walking up and scaring it off)
                    Livestock l = Near(here, 80).FirstOrDefault(x => !x.Tamed);
                    GameObject food = ZNetScene.instance.GetPrefab(a.Length > 2 ? a[2] : "Raspberry");
                    if (l == null || food == null || food.GetComponent<ItemDrop>() == null) { error("bait [item prefab] [n]: no wild one within 80 m, or no such item"); yield break; }
                    int n = (int)F(a, 3, 4);
                    for (int i = 0; i < n; i++) UnityEngine.Object.Instantiate(food, l.transform.position + l.transform.forward * 1.2f + Vector3.up * 0.5f, Quaternion.identity);
                    output(new JObject { ["baited"] = Facts(l, here), ["item"] = food.name, ["n"] = n }); break;
                }
                case "use":
                {
                    Livestock l = Near(here, 5).FirstOrDefault(x => x.Tamed && !x.Young);
                    if (l == null) { error("no tame grown animal within 5 m"); yield break; }
                    int before = me.GetInventory().NrOfItems();
                    bool used = l.Use(me);
                    yield return new WaitForSeconds(1f);
                    output(new JObject { ["used"] = used, ["hasTool"] = Livestock.HasTool(me, l.Kind), ["after"] = Facts(l, here), ["dropped"] = new JArray(UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None)
                        .Where(d => d.m_itemData.m_dropPrefab != null && d.m_itemData.m_dropPrefab.name == l.Kind.Product && Vector3.Distance(d.transform.position, l.transform.position) < 4).Select(d => d.m_itemData.m_stack)) });
                    break;
                }
                case "troughs":
                {
                    if (a.Length > 3 && a[2] == "fill")
                    {
                        Trough t = Live<Trough>.All.Where(x => x != null && x.Box != null && Vector3.Distance(x.transform.position, here) <= 30).OrderBy(x => Vector3.Distance(x.transform.position, here)).FirstOrDefault();
                        GameObject food = ZNetScene.instance.GetPrefab(a[3]);
                        if (t == null || food == null || food.GetComponent<ItemDrop>() == null) { error("troughs fill <item prefab> [n]: no trough within 30 m, or no such item"); yield break; }
                        ContainerAccess.TakeOver(t.Box);
                        int want = (int)F(a, 4, 10), put = 0;
                        while (put < want && t.Box.GetInventory().AddItem(food, 1)) put++;
                        output(new JObject { ["at"] = V(t.transform.position), ["put"] = put, ["holds"] = new JArray(t.Box.GetInventory().GetAllItems().Select(i => $"{i.m_dropPrefab?.name ?? i.m_shared.m_name} x{i.m_stack}")) });
                        break;
                    }
                    float r = F(a, 2, 30);
                    output(new JObject { ["troughs"] = new JArray(Live<Trough>.All.Where(t => Vector3.Distance(t.transform.position, here) <= r).Select(t => new JObject
                    {
                        ["at"] = V(t.transform.position), ["holds"] = new JArray(t.Box?.GetInventory()?.GetAllItems().Select(i => $"{i.m_dropPrefab?.name ?? i.m_shared.m_name} x{i.m_stack}") ?? Enumerable.Empty<string>()),
                    })) });
                    break;
                }
                case "clear":
                {
                    float r = F(a, 2, 30); int n = 0;
                    foreach (Livestock l in Near(here, r).Where(l => l.Test).ToList())
                    {
                        l.View.ClaimOwnership(); l.View.Destroy(); n++;
                    }
                    output(new JObject { ["removed"] = n, ["radius"] = r }); break;
                }
                case "grip":
                {
                    Items.Def d = a.Length > 2 ? Items.All.FirstOrDefault(x => x.Name.Equals(a[2], StringComparison.OrdinalIgnoreCase) && x.Hand.HasValue) : null;
                    GameObject prefab = d != null ? Kit.ItemPrefabs.FirstOrDefault(g => g.name == d.Name) : null;
                    if (prefab == null) { error("byre grip <ByreShears|ByreMilkPail> ex ey ez"); yield break; }
                    var e = new Vector3(F(a, 3, 0), F(a, 4, 0), F(a, 5, 0));
                    d.Hand = e; Items.Hold(prefab, e, d);
                    Transform grip = prefab.transform.Find("equipoffset");
                    int turned = 0;
                    VisEquipment vis = me.GetComponent<VisEquipment>();
                    foreach (string field in new[] { "m_rightItemInstance", "m_leftItemInstance" })
                    {
                        var inst = HarmonyLib.Traverse.Create(vis).Field(field).GetValue<GameObject>();
                        if (inst == null || !inst.name.StartsWith("attach") || me.GetCurrentWeapon()?.m_dropPrefab?.name != d.Name) continue;
                        inst.transform.localPosition = grip.localPosition; inst.transform.localRotation = grip.localRotation; turned++;
                    }
                    output(new JObject { ["euler"] = new JArray(e.x, e.y, e.z), ["position"] = new JArray(Math.Round(grip.localPosition.x, 3), Math.Round(grip.localPosition.y, 3), Math.Round(grip.localPosition.z, 3)), ["turnedInHand"] = turned });
                    break;
                }
                default: error(Help); break;
            }
        }
    }
}
