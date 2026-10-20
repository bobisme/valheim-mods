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
        private const string Help = "byre herds [r] | spawn <sheep|goat|cow|pig|goose|lamb|kid|calf|piglet|gosling|boar> [tame] [n] | ready [r] | age <hours> [r] | feed [r] | bait [item] [n] | use [kind] | breed [r] | hatch [r] | roots | guard [r] | troughs [r] | troughs fill <item> [n] | tag <prefab> [r] | clear [r] [items] | grip <ByreShears|ByreMilkPail> ex ey ez: check Byre. herds lists the animals within r m (40) with their kind, tameness, hunger and wool or milk; spawn puts n (1) tagged test animals 3 m ahead (tame: already tamed; boar: the game's, for breeding pigs); ready makes the wool and milk of tame ones within r (10) ready now; age moves their clocks (last shorn or milked, and a lamb's or kid's birth) back that many game hours; feed fills them as a meal would; bait drops n (4) of an item (Raspberry) at the feet of the nearest wild one within 80 m; use is E with the tool on the nearest tame one (of that kind) in 5 m (a goose: plucked, or petted, by hand); breed makes every tame breeder within r (10, the game's boars too) due and has it give birth now (or lay); hatch has the eggs within r (10) that are growing hatch now (they grow only under a roof by a fire); roots has the nearest tame pig within 10 m nose about now, the roll and the gap skipped (it must still be following someone in the Black Forest); guard lists the tame geese within r (20), who they know and whom they're chasing; troughs lists the troughs within r (30) and what's in them, troughs fill puts n (10) of an item in the nearest one; tag marks every creature of a prefab within r (10) as a test animal; clear removes the tagged test animals within r (30) (and tagged boars), nothing else; with items, goose eggs, cow's milk and truffles on the ground there too; grip turns how the hand holds the shears or the pail (the equipoffset's euler angles, the grip point kept in the fist) for the next equip and the one in hand now: put the numbers found in Items.cs's Hand.";
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

        private static readonly string[] TestGoods = { "ByreGooseEgg", "ByreCowMilk", "ByreTruffle" };
        private static readonly int Floors = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain");
        private static readonly MethodInfo Procreate = HarmonyLib.AccessTools.Method(typeof(Procreation), "Procreate"), CanGrow = HarmonyLib.AccessTools.Method(typeof(EggGrow), "CanGrow");
        // What's near by prefab: creatures and items on the ground (to see what a birth or a hatching made).
        private static System.Collections.Generic.Dictionary<string, int> Counts(Vector3 at, float r) =>
            UnityEngine.Object.FindObjectsByType<ZNetView>(FindObjectsSortMode.None).Where(v => v != null && v.IsValid() && (v.GetComponent<Character>() != null || v.GetComponent<ItemDrop>() != null) && Vector3.Distance(v.transform.position, at) <= r)
                .GroupBy(v => Utils.GetPrefabName(v.gameObject)).ToDictionary(g => g.Key, g => g.Count());

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
                    if (s == null && kind != "boar") { error("spawn <sheep|goat|cow|pig|goose|lamb|kid|calf|piglet|gosling|boar> [tame] [n]"); yield break; }
                    bool young = s != null && !s.Key.Equals(kind, StringComparison.OrdinalIgnoreCase), tame = a.Skip(3).Any(x => x == "tame");
                    int n = Math.Max(1, Math.Min(10, (int)F(a, a.Length > 3 && a[3] == "tame" ? 4 : 3, 1)));
                    string name = s == null ? "Boar" : young ? s.Young : s.Prefab;
                    GameObject prefab = ZNetScene.instance.GetPrefab(name);
                    if (prefab == null) { error("not made: " + name); yield break; }
                    var made = new JArray();
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 at = here + me.transform.forward * 3f + me.transform.right * (i - (n - 1) / 2f) * 1.5f;
                        // (the floor under the spot, from about the player's height: indoors, not the roof over it)
                        if (Physics.Raycast(new Vector3(at.x, here.y + 1.5f, at.z), Vector3.down, out RaycastHit hit, 8f, Floors)) at.y = hit.point.y + 0.2f;
                        else if (ZoneSystem.instance != null) at.y = ZoneSystem.instance.GetSolidHeight(at) + 0.2f;
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
                case "breed":
                {
                    float r = F(a, 2, 10);
                    var before = Counts(here, r + 3);
                    var had = new System.Collections.Generic.HashSet<ZDOID>(Character.GetAllCharacters().Where(c => c != null && c.GetComponent<ZNetView>()?.GetZDO() != null).Select(c => c.GetComponent<ZNetView>().GetZDO().m_uid));
                    var done = new JArray();
                    foreach (Procreation p in UnityEngine.Object.FindObjectsByType<Procreation>(FindObjectsSortMode.None).Where(p => Vector3.Distance(p.transform.position, here) <= r))
                    {
                        Character ch = p.GetComponent<Character>(); ZNetView view = p.GetComponent<ZNetView>();
                        if (ch == null || !ch.IsTamed() || view == null || !view.IsValid()) continue;
                        view.ClaimOwnership();
                        view.GetZDO().Set(ZDOVars.s_pregnant, ZNet.instance.GetTime().Ticks - Husbandry.Ticks(p.m_pregnancyDuration + 5));
                        Procreate.Invoke(p, null);
                        done.Add(Utils.GetPrefabName(p.gameObject) + " at " + V(p.transform.position).ToString(Newtonsoft.Json.Formatting.None));
                    }
                    yield return new WaitForSeconds(1f);
                    // (the young born here tagged as test animals, as their parents: clear removes them)
                    foreach (Character c in Character.GetAllCharacters().Where(c => c != null && Vector3.Distance(c.transform.position, here) <= r + 3))
                        if (c.GetComponent<ZNetView>() is ZNetView v && v.GetZDO() != null && !had.Contains(v.GetZDO().m_uid)) { v.ClaimOwnership(); v.GetZDO().Set(Livestock.TestKey, true); }
                    var after = Counts(here, r + 3);
                    output(new JObject { ["due"] = done, ["born"] = new JObject(after.Where(kv => kv.Value > (before.TryGetValue(kv.Key, out int b) ? b : 0)).Select(kv => new JProperty(kv.Key, kv.Value - (before.TryGetValue(kv.Key, out int b2) ? b2 : 0)))) });
                    break;
                }
                case "hatch":
                {
                    float r = F(a, 2, 10);
                    var eggs = new JArray();
                    foreach (EggGrow egg in UnityEngine.Object.FindObjectsByType<EggGrow>(FindObjectsSortMode.None).Where(e => Vector3.Distance(e.transform.position, here) <= r))
                    {
                        ZNetView view = egg.GetComponent<ZNetView>();
                        if (view == null || !view.IsValid()) continue;
                        view.ClaimOwnership();
                        float start = view.GetZDO().GetFloat(ZDOVars.s_growStart, 0f);
                        bool grows = (bool)CanGrow.Invoke(egg, null);
                        if (start > 0f) view.GetZDO().Set(ZDOVars.s_growStart, (float)(ZNet.instance.GetTimeSeconds() - egg.m_growTime - 1));
                        eggs.Add(new JObject { ["egg"] = Utils.GetPrefabName(egg.gameObject), ["at"] = V(egg.transform.position), ["growing"] = start > 0f, ["canGrow"] = grows, ["hatches"] = egg.m_grownPrefab != null ? egg.m_grownPrefab.name : null });
                    }
                    yield return new WaitForSeconds(6f); // (an egg looks every 5 s)
                    output(new JObject { ["eggs"] = eggs, ["near"] = new JObject(Counts(here, r + 2).Select(kv => new JProperty(kv.Key, kv.Value))) });
                    break;
                }
                case "roots":
                {
                    Livestock l = Near(here, 10).FirstOrDefault(x => x.Kind != null && x.Kind.Roots && x.Tamed && !x.Young);
                    if (l == null) { error("no tame grown pig within 10 m"); yield break; }
                    l.View.ClaimOwnership();
                    string rolled = l.Root(false), forced = l.Root(true);
                    yield return new WaitForSeconds(1f);
                    output(new JObject { ["pig"] = Facts(l, here), ["rolled"] = rolled, ["forced"] = forced, ["biome"] = Heightmap.FindBiome(l.transform.position).ToString(),
                        ["following"] = l.AI?.GetFollowTarget()?.name, ["truffles"] = UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None).Count(d => d.m_itemData.m_dropPrefab?.name == "ByreTruffle" && Vector3.Distance(d.transform.position, l.transform.position) < 4) });
                    break;
                }
                case "guard":
                {
                    float r = F(a, 2, 20);
                    output(new JObject { ["geese"] = new JArray(Near(here, r).Where(l => l.Kind != null && l.Kind.Guards).Select(l =>
                    {
                        JObject f = Facts(l, here);
                        f["knowsMe"] = l.Knows(me.GetPlayerID()); f["known"] = l.View.GetZDO().GetString(Livestock.KnownKey, "");
                        f["chasing"] = l.Chasing != null ? l.Chasing.GetHoverName() + " at " + Math.Round(Vector3.Distance(l.Chasing.transform.position, l.transform.position), 1) + " m" : null;
                        return f;
                    })) });
                    break;
                }
                case "tag":
                {
                    // (test animals born untagged: tag every one of that prefab within r (10), so clear removes them)
                    string name = a.Length > 2 ? a[2] : ""; float r = F(a, 3, 10); int n = 0;
                    foreach (Character c in Character.GetAllCharacters().Where(c => c != null && Utils.GetPrefabName(c.gameObject) == name && Vector3.Distance(c.transform.position, here) <= r))
                        if (c.GetComponent<ZNetView>() is ZNetView v && v.IsValid()) { v.ClaimOwnership(); v.GetZDO().Set(Livestock.TestKey, true); n++; }
                    output(new JObject { ["tagged"] = n, ["prefab"] = name, ["radius"] = r }); break;
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
                    string only = a.Length > 2 ? a[2] : null;
                    Livestock l = Near(here, 5).FirstOrDefault(x => x.Tamed && !x.Young && (only == null || string.Equals(x.Kind?.Key, only, StringComparison.OrdinalIgnoreCase)));
                    if (l == null) { error("no tame grown " + (only ?? "animal") + " within 5 m"); yield break; }
                    int before = me.GetInventory().NrOfItems();
                    bool used = l.Use(me); l.Know(me); // (as E does: petted or plucked, a goose knows you)
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
                    // (the game's boars spawned for breeding pigs: tagged the same)
                    foreach (Character ch in Character.GetAllCharacters().Where(c => c != null && c.GetComponent<Livestock>() == null && Vector3.Distance(c.transform.position, here) <= r).ToList())
                    {
                        ZNetView v = ch.GetComponent<ZNetView>();
                        if (v == null || !v.IsValid() || !v.GetZDO().GetBool(Livestock.TestKey, false)) continue;
                        v.ClaimOwnership(); v.Destroy(); n++;
                    }
                    // (items: Byre 0.3.0's new goods lying within r too, laid, milked or rooted up in a test: only those, nothing older)
                    int items = 0;
                    if (a.Skip(2).Contains("items"))
                        foreach (ItemDrop d in UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None).Where(d => d != null && TestGoods.Contains(Utils.GetPrefabName(d.gameObject)) && Vector3.Distance(d.transform.position, here) <= r).ToList())
                            if (d.GetComponent<ZNetView>() is ZNetView v && v.IsValid()) { v.ClaimOwnership(); v.Destroy(); items++; }
                    output(new JObject { ["removed"] = n, ["items"] = items, ["radius"] = r }); break;
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
