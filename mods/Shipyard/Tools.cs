using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using BepInEx;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Shipyard
{
    // A "yard" command for Claude Tools, found while the game runs (no reference), to check the slipway and the boats in a live game.
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
                found.GetType().GetMethod("RegisterCommand", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[]{Plugin.Name, "yard",
                    "yard status | lay [hull] | pay [stages] | launch | use | down | ships | row [forward|backward|leave] [n] | sink: check Shipyard. status shows the slipways within 150 m (the build, the stage, what it needs) and the boats; lay lays a keel on the nearest slipway; pay puts in the stage under way (or that many stages) from nothing (cheats only); launch launches the finished boat on the nearest slipway as E does; use is E on it, as the player (cheats only); down takes the nearest slipway down as the hammer does and lists what lies around it after (cheats only); ships lists the boats loaded (where, how high the hull rides over the sea, speed, owner); row works the nearest færing's oars as its helm does, taking the helm first, and leave lets go of it (cheats only; the game rows only with someone aboard); sink removes the boats loaded within 150 m (test boats; cheats only).",
                    (Func<string[], Action<JObject>, Action<string>, IEnumerator>)Run});
                Plugin.Log("Claude Tools found: yard command added");
            }
            catch (Exception e) { Plugin.Log("Could not add the yard command: " + e.Message); }
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

        private static Slipway Nearest(Vector3 at) => UnityEngine.Object.FindObjectsByType<Slipway>(FindObjectsSortMode.None).Where(s => Vector3.Distance(s.transform.position, at) < 150).OrderBy(s => Vector3.Distance(s.transform.position, at)).FirstOrDefault();
        private static JObject Describe(Slipway s, Vector3 me)
        {
            Build b = s.Now();
            return new JObject { ["at"] = V(s.transform.position), ["yaw"] = Math.Round(s.transform.eulerAngles.y), ["distance"] = Math.Round(Vector3.Distance(s.transform.position, me), 1), ["build"] = b.Save(),
                ["stage"] = b.Empty ? "" : b.Finished ? "finished" : $"{b.Stage + 1}/{b.Hull.Stages.Length} {b.Current.Name}", ["needs"] = string.Join(", ", b.Needs().Select(n => $"{n.n} {n.item}")),
                ["launchAt"] = b.Hull != null && s.Where(b.Hull) is Vector3 w ? V(w) : null, ["owner"] = s.GetComponent<ZNetView>().IsOwner() };
        }
        private static JArray Boats(Vector3 me) => new JArray(UnityEngine.Object.FindObjectsByType<Ship>(FindObjectsSortMode.None).Where(Yard.IsFaering).Select(s => new JObject
        {
            ["at"] = V(s.transform.position), ["distance"] = Math.Round(Vector3.Distance(s.transform.position, me), 1), ["overSea"] = Math.Round(s.transform.position.y - ZoneSystem.instance.m_waterLevel, 2),
            ["tilt"] = Math.Round(Vector3.Angle(s.transform.up, Vector3.up), 1), ["speed"] = Math.Round(s.GetComponent<Rigidbody>().linearVelocity.magnitude, 2), ["setting"] = s.GetSpeedSetting().ToString(),
            ["owner"] = s.GetComponent<ZNetView>().IsOwner(), ["depth"] = Math.Round(ZoneSystem.instance.m_waterLevel - ZoneSystem.instance.GetGroundHeight(s.transform.position), 2),
        }));

        private static IEnumerator Run(string[] a, Action<JObject> output, Action<string> error)
        {
            Player me = Player.m_localPlayer;
            if (me == null || ZNet.instance == null) { error("needs the player in a world"); yield break; }
            string verb = a.Length > 1 ? a[1] : "status";
            Vector3 at = me.transform.position;
            Slipway s = Nearest(at);
            ZNetView view = s != null ? s.GetComponent<ZNetView>() : null;
            switch (verb)
            {
                case "status":
                    output(new JObject { ["slipways"] = new JArray(UnityEngine.Object.FindObjectsByType<Slipway>(FindObjectsSortMode.None).Where(x => Vector3.Distance(x.transform.position, at) < 150).Select(x => Describe(x, at))),
                        ["boats"] = Boats(at), ["made"] = new JArray(new[] { Names.Slipway, Names.Faering }.Where(n => Yard.Get(n) != null)) });
                    break;
                case "ships": output(new JObject { ["boats"] = Boats(at) }); break;
                case "lay":
                case "pay":
                case "launch":
                {
                    if (s == null || view == null || !view.IsValid()) { error("no slipway within 150 m"); yield break; }
                    if (verb != "launch" && !Cheats()) { error("yard " + verb + " changes the world: only in single player, for the host, or for a server admin with devcommands on"); yield break; }
                    if (verb == "lay") view.InvokeRPC("Shipyard_Lay", a.Length > 2 ? a[2] : Hulls.Faering);
                    else if (verb == "launch") view.InvokeRPC("Shipyard_Launch");
                    else
                    {
                        if (!view.IsOwner()) view.ClaimOwnership();
                        int stages = (int)F(a, 2, 1);
                        Build b = s.Now();
                        for (int i = 0; i < stages && b.Current != null; i++) b.Take(b.Needs().Select(n => (n.item, n.n)).ToList());
                        view.GetZDO().Set(Slipway.Key, b.Save());
                    }
                    yield return new WaitForSeconds(1f);
                    output(Describe(s, at));
                    break;
                }
                case "use":
                {
                    // E on the nearest slipway, as the player (cheats only: it reaches 150 m)
                    if (s == null) { error("no slipway within 150 m"); yield break; }
                    if (!Cheats()) { error("yard use reaches a far slipway: only where cheats are allowed"); yield break; }
                    output(new JObject { ["hover"] = s.GetHoverText(), ["used"] = s.Interact(me, false, false) });
                    yield return new WaitForSeconds(1f);
                    output(Describe(s, at));
                    break;
                }
                case "down":
                {
                    // the nearest slipway taken down as the hammer does; what it gave back is what lies around it after
                    if (s == null) { error("no slipway within 150 m"); yield break; }
                    if (!Cheats()) { error("yard down takes a slipway down: only where cheats are allowed"); yield break; }
                    Vector3 where = s.transform.position; string had = s.Now().Save();
                    s.GetComponent<WearNTear>().Remove();
                    yield return new WaitForSeconds(1.5f);
                    var drops = new JObject();
                    foreach (ItemDrop d in UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None).Where(x => Vector3.Distance(x.transform.position, where) < 4))
                        drops[d.m_itemData.m_dropPrefab?.name ?? d.name] = (int)(drops[d.m_itemData.m_dropPrefab?.name ?? d.name] ?? 0) + d.m_itemData.m_stack;
                    output(new JObject { ["build"] = had, ["dropped"] = drops });
                    break;
                }
                case "row":
                {
                    // the nearest færing's oars, as its helm sets them: forward (stop, slow) or backward (back, stop); the game rows only
                    // with someone aboard
                    if (!Cheats()) { error("yard row steers a boat: only where cheats are allowed"); yield break; }
                    Ship ship = UnityEngine.Object.FindObjectsByType<Ship>(FindObjectsSortMode.None).Where(Yard.IsFaering).OrderBy(x => Vector3.Distance(x.transform.position, at)).FirstOrDefault();
                    if (ship == null) { error("no færing loaded"); yield break; }
                    if (a.Length > 2 && a[2] == "leave") { me.AttachStop(); yield return new WaitForSeconds(0.5f); output(new JObject { ["helm"] = me.IsAttached() }); break; }
                    // (and only with a hand on the tiller: the player takes it, if near enough)
                    if (!me.IsAttached() && ship.GetComponentInChildren<ShipControlls>() is ShipControlls helm) { helm.Interact(me, false, false); yield return new WaitForSeconds(1f); }
                    for (int i = 0; i < (int)F(a, 3, 1); i++) { if (a.Length > 2 && a[2] == "backward") ship.Backward(); else ship.Forward(); }
                    yield return new WaitForSeconds(0.5f);
                    output(new JObject { ["helm"] = me.IsAttached(), ["boats"] = Boats(at) });
                    break;
                }
                case "sink":
                {
                    if (!Cheats()) { error("yard sink removes boats: only where cheats are allowed"); yield break; }
                    int n = 0;
                    foreach (Ship ship in UnityEngine.Object.FindObjectsByType<Ship>(FindObjectsSortMode.None).Where(x => Yard.IsFaering(x) && Vector3.Distance(x.transform.position, at) < 150).ToList())
                    {
                        ZNetView v = ship.GetComponent<ZNetView>(); if (!v.IsOwner()) v.ClaimOwnership(); v.Destroy(); n++;
                    }
                    output(new JObject { ["removed"] = n });
                    break;
                }
                default: error("yard status | lay [hull] | pay [stages] | launch | use | down | ships | row [forward|backward|leave] [n] | sink"); break;
            }
        }
    }
}
