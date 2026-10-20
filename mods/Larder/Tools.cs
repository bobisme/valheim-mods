using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Larder
{
    // A "larder" command for Claude Tools, found while the game runs (no reference), to check the dairy in a live game.
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
                found.GetType().GetMethod("RegisterCommand", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[]{Plugin.Name, "larder",
                    "larder status | give <milk|foods|meat|flour|all|prefab> [n] | finish [radius] | use <add|fuel|empty> [times] | spoil [hours=24] [radius=4] | place <churn|press|smokehouse|saltpan|barrel|rack|oven|piece> | remove [radius]: check Larder. status says whether Byre's milk is there, which stations are in the hammer's menu, the churn's recipes in the game, and Larder's queued stations within 30 m (the press, smokehouse, salt pan, salting barrel, drying rack: what's queued, done, fuel, how far on, and whether what it needs where it stands is met), and the bake ovens' slots and fuel; give puts milk, the foods (dairy, preserved, the bread, and salt and dough), meat and fish to preserve, barley flour, or any item in the bag (cheats only); finish runs those stations within radius (20 m) an hour on, as if you'd been away (cheats only); use presses E on the nearest station's switch within 6 m (put in, fuel, take out; the oven's bread comes out by its put-in switch), times over, and says what its hover said (cheats only); spoil (with spoilage on; cheats only) ages the food in the bag and the chests within radius as if that many game hours had passed (a game hour is 75 s), and lists each food's freshness; place builds a station or any piece (a chest for a full bag's things) on the ground 2.5 m ahead, facing you, tagged as a test piece (cheats only); remove takes down the tagged ones within radius (30 m), nothing else, and never a chest with anything in it.",
                    (Func<string[], Action<JObject>, Action<string>, IEnumerator>)Run});
                Plugin.Log("Claude Tools found: larder command added");
            }
            catch (Exception e) { Plugin.Log("Could not add the larder command: " + e.Message); }
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
        private const string TestKey = "larder_test";
        private static Vector2i? FreeCell(Inventory bag)
        {
            for (int y = 0; y < Mathf.Min(4, bag.GetHeight()); y++)
                for (int x = 0; x < bag.GetWidth(); x++) if (bag.GetItemAt(x, y) == null) return new Vector2i(x, y);
            return null;
        }
        // (Larder's queued stations: the cheese press and the preserving ones)
        private static System.Collections.Generic.IEnumerable<Smelter> Presses(Vector3 at, float radius) => UnityEngine.Object.FindObjectsByType<Smelter>(FindObjectsSortMode.None)
            .Where(s => s != null && (Utils.GetPrefabName(s.gameObject) == Names.Press || Kit.WorksOf(Utils.GetPrefabName(s.gameObject)) != null) && Vector3.Distance(s.transform.position, at) < radius).OrderBy(s => Vector3.Distance(s.transform.position, at));
        // (the bake ovens: the game's cooking station on our model)
        private static System.Collections.Generic.IEnumerable<CookingStation> Ovens(Vector3 at, float radius) => UnityEngine.Object.FindObjectsByType<CookingStation>(FindObjectsSortMode.None)
            .Where(c => c != null && Utils.GetPrefabName(c.gameObject) == Baked.Oven && Vector3.Distance(c.transform.position, at) < radius).OrderBy(c => Vector3.Distance(c.transform.position, at));
        private static readonly FieldInfo HaveRoof = typeof(Smelter).GetField("m_haveRoof", BindingFlags.Instance | BindingFlags.NonPublic);

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
                    PieceTable table = Kit.Hammer(ZNetScene.instance);
                    var presses = new JArray(Presses(at, 30).Select(s =>
                    {
                        ZDO z = s.GetComponent<ZNetView>()?.GetZDO();
                        Works w = Kit.WorksOf(Utils.GetPrefabName(s.gameObject));
                        var o = new JObject { ["station"] = Utils.GetPrefabName(s.gameObject), ["at"] = V(s.transform.position), ["distance"] = Math.Round(Vector3.Distance(s.transform.position, at), 1), ["takes"] = new JArray(s.m_conversion.Select(c => c.m_from?.name)) };
                        if (s.m_fuelItem != null) o["burns"] = s.m_fuelItem.name;
                        if (w != null && w.Needs != Needs.Nothing) o["needs"] = $"{w.Needs}: {((bool)HaveRoof.GetValue(s) ? "met" : "NOT met")}";
                        if (z == null) return o;
                        int queued = z.GetInt(ZDOVars.s_queued);
                        var q = new JArray(); for (int i = 0; i < queued && i < 40; i++) q.Add(z.GetString("item" + i));
                        o["queued"] = q; o["bake"] = Math.Round(z.GetFloat(ZDOVars.s_bakeTimer), 1); o["perProduct"] = s.m_secPerProduct;
                        o["done"] = z.GetInt(ZDOVars.s_spawnAmount);
                        if (s.m_fuelItem != null) o["fuel"] = Math.Round(z.GetFloat(ZDOVars.s_fuel), 2);
                        return o;
                    }));
                    output(new JObject
                    {
                        ["milk"] = new JArray(Names.Milks.Where(m => Things.Milk(m) != null)),
                        ["inMenu"] = new JArray(Kit.Made.Values.Where(p => p != null && table != null && table.m_pieces.Contains(p)).Select(p => p.name)),
                        ["registered"] = new JArray(Dairy.Pieces.Concat(Dairy.Items).Concat(Preserves.Pieces).Concat(Preserves.Items).Concat(Baking.Pieces).Concat(Baking.ItemNames).Where(n => ZNetScene.instance.GetPrefab(n) != null)),
                        ["recipes"] = new JArray(ObjectDB.instance.m_recipes.Where(r => r != null && r.name.StartsWith("Recipe_Larder_"))
                            .Select(r => $"{r.name}: {r.m_amount} from {string.Join(", ", r.m_resources.Select(q => q.m_amount + " " + q.m_resItem?.name))} at {r.m_craftingStation?.m_name}")),
                        ["presses"] = presses,
                        // (each slot as the game keeps it on the ZDO: what's in it, how long it's been in, and 0 baking, 1 done, 2 burnt)
                        ["ovens"] = new JArray(Ovens(at, 30).Select(c =>
                        {
                            ZDO z = c.GetComponent<ZNetView>()?.GetZDO();
                            return new JObject
                            {
                                ["at"] = V(c.transform.position), ["distance"] = Math.Round(Vector3.Distance(c.transform.position, at), 1), ["fuel"] = Math.Round(z?.GetFloat(ZDOVars.s_fuel) ?? 0, 2),
                                ["slots"] = new JArray(Enumerable.Range(0, c.m_slots.Length).Select(i => z == null ? "" : $"{z.GetString("slot" + i)} {z.GetFloat("slot" + i):0}s {z.GetInt("slotstatus" + i)}")),
                            };
                        })),
                    });
                    break;
                }
                case "give":
                {
                    if (!Cheats()) { error("larder give makes items: only in single player, for the host, or for a server admin with devcommands on"); yield break; }
                    string what = a.Length > 2 ? a[2] : "all"; int n = (int)F(a, 3, 10);
                    string[] meat = Preserved.Meats.Append(Preserved.Fish).ToArray(), foods = Dairy.Items.Concat(Preserves.Items).Concat(Baking.ItemNames).ToArray();
                    string[] list = what == "milk" ? Names.Milks : what == "foods" ? foods : what == "meat" ? meat : what == "flour" ? new[] { Baked.Flour } : what == "all" ? Names.Milks.Concat(foods).Concat(meat).ToArray() : new[] { what };
                    var given = new JObject();
                    foreach (string item in list)
                    {
                        GameObject p = Things.Get(item) ?? ZNetScene.instance.GetPrefab(item);
                        if (p?.GetComponent<ItemDrop>() == null) { given[item] = "no such item"; continue; }
                        // (a stack into a free cell of the bag's own rows: gear-slot mods add rows below that make the bag's AddItem say full)
                        int count = Math.Min(n, p.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize);
                        given[item] = FreeCell(me.GetInventory()) is Vector2i cell && me.GetInventory().AddItem(item, count, 1, 0, 0L, "", cell, false) != null ? count : 0;
                    }
                    output(new JObject { ["given"] = given });
                    break;
                }
                case "finish":
                {
                    if (!Cheats()) { error("larder finish moves the presses' clocks: only in single player, for the host, or for a server admin with devcommands on"); yield break; }
                    var ran = new JArray();
                    foreach (Smelter s in Presses(at, F(a, 2, 20)))
                    {
                        ZNetView view = s.GetComponent<ZNetView>();
                        if (view == null || !view.IsValid()) continue;
                        if (!view.IsOwner()) view.ClaimOwnership();
                        // (an hour since it last looked: its next tick works that through, as for a station come back into a loaded area)
                        view.GetZDO().Set(ZDOVars.s_startTime, ZNet.instance.GetTime().AddSeconds(-3600).Ticks);
                        ran.Add(V(s.transform.position));
                    }
                    yield return new WaitForSeconds(2.5f);
                    output(new JObject { ["ran"] = ran });
                    break;
                }
                case "use":
                {
                    // (E on one of the nearest station's switches, as the player pressing it: the game takes what it wants from the bag)
                    if (!Cheats()) { error("larder use presses E for the player: only in single player, for the host, or for a server admin with devcommands on"); yield break; }
                    Smelter s = Presses(at, 6).FirstOrDefault(); CookingStation oven = Ovens(at, 6).FirstOrDefault();
                    if (s == null && oven == null) { error("no Larder station within 6 m"); yield break; }
                    string which = a.Length > 2 ? a[2] : "add"; int times = (int)F(a, 3, 1);
                    // (the oven's bread comes out by the switch it goes in by)
                    bool baking = oven != null && (s == null || Vector3.Distance(oven.transform.position, at) < Vector3.Distance(s.transform.position, at));
                    Switch sw = baking ? (which == "fuel" ? oven.m_addFuelSwitch : oven.m_addFoodSwitch) : which == "fuel" ? s.m_addWoodSwitch : which == "empty" ? s.m_emptyOreSwitch : s.m_addOreSwitch;
                    GameObject station = baking ? oven.gameObject : s.gameObject;
                    if (sw == null) { error($"the {Utils.GetPrefabName(station)} has no {which} switch"); yield break; }
                    var hovers = new JArray(); int done = 0;
                    for (int i = 0; i < times; i++)
                    {
                        hovers.Add(sw.GetHoverText());
                        if (sw.Interact(me, false, false)) done++;
                        yield return new WaitForSeconds(0.3f);
                    }
                    hovers.Add(sw.GetHoverText());
                    output(new JObject { ["station"] = Utils.GetPrefabName(station), ["switch"] = which, ["took"] = done, ["hover"] = new JArray(hovers.Select(h => h.ToString()).Distinct()) });
                    break;
                }
                case "spoil":
                {
                    // (as if hours of game time had passed: each food's last look in the bag and the chests within radius moved back, then aged
                    // where it lies, as opening them would; a game hour is 75 s)
                    if (!Cheats()) { error("larder spoil moves food's clocks: only in single player, for the host, or for a server admin with devcommands on"); yield break; }
                    if (!Spoiling.On) { error("spoilage is off (the Spoilage setting, Larder's config): nothing spoils"); yield break; }
                    float hours = F(a, 2, 24), radius = F(a, 3, 4); double back = hours * Spoilage.Day / 24;
                    var where = new JArray();
                    void Run1(Inventory inv, Kept kept, bool cold, string name)
                    {
                        foreach (ItemDrop.ItemData item in inv.GetAllItems()) if (Spoiling.KindOf(item) != Kind.Never) Spoiling.BackDate(item, back);
                        int spoiled = Spoiling.Age(inv, kept, cold);
                        where.Add(new JObject { ["in"] = name, ["kept"] = kept.ToString(), ["cold"] = cold, ["spoiled"] = spoiled,
                            ["foods"] = new JArray(inv.GetAllItems().Where(i => Spoiling.KindOf(i) != Kind.Never || i.m_dropPrefab?.name == Spoilage.Spoiled)
                                .Select(i => $"{i.m_dropPrefab?.name} x{i.m_stack}: {Spoiling.Freshness(i) ?? "spoiled"}")) });
                    }
                    Run1(me.GetInventory(), Kept.Bag, false, "bag");
                    foreach (Container c in UnityEngine.Object.FindObjectsByType<Container>(FindObjectsSortMode.None).Where(c => c != null && c.GetInventory() != null && c.GetComponentInParent<Character>() == null && Vector3.Distance(c.transform.position, at) < radius)) // (chests; not a villager's bag)
                    {
                        ZNetView v = c.GetComponent<ZNetView>();
                        if (v == null || !v.IsValid()) continue;
                        if (!v.IsOwner()) v.ClaimOwnership();
                        Vector3 p = c.transform.position;
                        Run1(c.GetInventory(), Spoiling.Where(p), Spoiling.Cold(p), $"{Utils.GetPrefabName(c.gameObject)} at {p.x:0.#},{p.y:0.#},{p.z:0.#}");
                    }
                    output(new JObject { ["hours"] = hours, ["where"] = where });
                    break;
                }
                case "place":
                {
                    // (on the ground 2.5 m ahead, its front to the player, tagged as a test piece; console spawn leaves a piece a metre up, where it breaks)
                    if (!Cheats()) { error("larder place builds for free: only in single player, for the host, or for a server admin with devcommands on"); yield break; }
                    string which = a.Length < 3 ? "churn" : a[2];
                    string prefab = which == "churn" ? Names.Churn : which == "press" ? Names.Press : which == "smokehouse" ? Preserved.Smokehouse : which == "saltpan" ? Preserved.SaltPan
                        : which == "barrel" ? Preserved.SaltBarrel : which == "rack" ? Preserved.DryingRack : which == "oven" ? Baked.Oven : which; // (or any piece: a chest to stow a full bag's things in)
                    GameObject p = ZNetScene.instance.GetPrefab(prefab);
                    if (p == null) { error("no " + prefab); yield break; }
                    Vector3 fwd = Vector3.ProjectOnPlane(me.transform.forward, Vector3.up).normalized;
                    if (!Physics.Raycast(at + fwd * 2.5f + Vector3.up * 3, Vector3.down, out RaycastHit hit, 8, LayerMask.GetMask("Default", "static_solid", "terrain", "piece"))) { error("no ground ahead"); yield break; }
                    GameObject go = UnityEngine.Object.Instantiate(p, hit.point, Quaternion.LookRotation(-fwd));
                    go.GetComponent<Piece>()?.SetCreator(me.GetPlayerID(), Splatform.PlatformManager.DistributionPlatform.LocalUser.PlatformUserID);
                    go.GetComponent<ZNetView>()?.GetZDO()?.Set(TestKey, true);
                    output(new JObject { ["placed"] = prefab, ["at"] = V(hit.point) });
                    break;
                }
                case "remove":
                {
                    // (only the pieces larder place made: tagged, within radius (30 m))
                    if (!Cheats()) { error("larder remove takes down pieces: only in single player, for the host, or for a server admin with devcommands on"); yield break; }
                    var gone = new JArray();
                    foreach (Piece piece in UnityEngine.Object.FindObjectsByType<Piece>(FindObjectsSortMode.None).Where(q => q != null && Vector3.Distance(q.transform.position, at) < F(a, 2, 30)).ToList())
                    {
                        ZNetView view = piece.GetComponent<ZNetView>();
                        if (view == null || !view.IsValid() || !view.GetZDO().GetBool(TestKey)) continue;
                        if (piece.GetComponent<Container>() is Container box && box.GetInventory().NrOfItems() > 0) { gone.Add(Utils.GetPrefabName(piece.gameObject) + " not empty: kept"); continue; }
                        if (!view.IsOwner()) view.ClaimOwnership();
                        gone.Add(Utils.GetPrefabName(piece.gameObject) + " " + V(piece.transform.position));
                        ZNetScene.instance.Destroy(piece.gameObject);
                    }
                    output(new JObject { ["removed"] = gone });
                    break;
                }
                default: error("larder status | give <milk|foods|meat|flour|all|prefab> [n] | finish [radius] | use <add|fuel|empty> [times] | spoil [hours=24] [radius=4] | place <churn|press|smokehouse|saltpan|barrel|rack|oven|piece> | remove [radius]"); yield break;
            }
        }
    }
}
