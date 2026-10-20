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
                    "larder status | give <milk|foods|all|prefab> [n] | finish [radius] | place <churn|press|piece> | remove [radius]: check Larder. status says whether Byre's milk is there, whether the churn and the press are in the hammer's menu, the churn's recipes in the game, and the cheese presses within 30 m (what's queued, done, and how far on); give puts milk or the foods in the bag (cheats only); finish runs the presses within radius (20 m) an hour on, as if you'd been away (cheats only); place builds a churn, a press or any piece (a chest for a full bag's things) on the ground 2.5 m ahead, facing you, tagged as a test piece (cheats only); remove takes down the tagged ones within radius (30 m), nothing else, and never a chest with anything in it.",
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
        private static System.Collections.Generic.IEnumerable<Smelter> Presses(Vector3 at, float radius) => UnityEngine.Object.FindObjectsByType<Smelter>(FindObjectsSortMode.None)
            .Where(s => s != null && Utils.GetPrefabName(s.gameObject) == Names.Press && Vector3.Distance(s.transform.position, at) < radius).OrderBy(s => Vector3.Distance(s.transform.position, at));

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
                        var o = new JObject { ["at"] = V(s.transform.position), ["distance"] = Math.Round(Vector3.Distance(s.transform.position, at), 1), ["takes"] = new JArray(s.m_conversion.Select(c => c.m_from?.name)) };
                        if (z == null) return o;
                        int queued = z.GetInt(ZDOVars.s_queued);
                        var q = new JArray(); for (int i = 0; i < queued && i < 40; i++) q.Add(z.GetString("item" + i));
                        o["queued"] = q; o["bake"] = Math.Round(z.GetFloat(ZDOVars.s_bakeTimer), 1); o["perProduct"] = s.m_secPerProduct;
                        o["done"] = z.GetInt(ZDOVars.s_spawnAmount);
                        return o;
                    }));
                    output(new JObject
                    {
                        ["milk"] = new JArray(Names.Milks.Where(m => Things.Milk(m) != null)),
                        ["inMenu"] = new JArray(Kit.Made.Values.Where(p => p != null && table != null && table.m_pieces.Contains(p)).Select(p => p.name)),
                        ["registered"] = new JArray(Dairy.Pieces.Concat(Dairy.Items).Where(n => ZNetScene.instance.GetPrefab(n) != null)),
                        ["recipes"] = new JArray(ObjectDB.instance.m_recipes.Where(r => r != null && r.name.StartsWith("Recipe_Larder_"))
                            .Select(r => $"{r.name}: {r.m_amount} from {string.Join(", ", r.m_resources.Select(q => q.m_amount + " " + q.m_resItem?.name))} at {r.m_craftingStation?.m_name}")),
                        ["presses"] = presses,
                    });
                    break;
                }
                case "give":
                {
                    if (!Cheats()) { error("larder give makes items: only in single player, for the host, or for a server admin with devcommands on"); yield break; }
                    string what = a.Length > 2 ? a[2] : "all"; int n = (int)F(a, 3, 10);
                    string[] list = what == "milk" ? Names.Milks : what == "foods" ? Dairy.Items.ToArray() : what == "all" ? Names.Milks.Concat(Dairy.Items).ToArray() : new[] { what };
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
                case "place":
                {
                    // (on the ground 2.5 m ahead, its front to the player, tagged as a test piece; console spawn leaves a piece a metre up, where it breaks)
                    if (!Cheats()) { error("larder place builds for free: only in single player, for the host, or for a server admin with devcommands on"); yield break; }
                    string prefab = a.Length < 3 || a[2] == "churn" ? Names.Churn : a[2] == "press" ? Names.Press : a[2]; // (or any piece: a chest to stow a full bag's things in)
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
                default: error("larder status | give <milk|foods|all|prefab> [n] | finish [radius] | place <churn|press|piece> | remove [radius]"); yield break;
            }
        }
    }
}
