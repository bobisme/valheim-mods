using System;
using System.Collections;
using System.Collections.Generic;
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
                    "yard [at x z] status | lay [hull] | pay [stages] [colour] | launch | clear | use | api [hull] | down | bump | mend | ships | row [forward|backward|leave] [n] | sink: check Shipyard (at x z first: as if standing there, for the slipway and boats nearest that spot). status shows the slipways within 150 m (the build, the stage, what it needs) and the boats; lay lays a keel on the nearest slipway; pay puts in the stage under way (or that many stages) from nothing, a sail in it in the colour given (cheats only); launch launches the finished boat on the nearest slipway as E does; clear forgets its build, giving nothing back (cheats only); use is E on it, as the player (cheats only); api builds a whole boat on it through Shipyard.Api and launches it (cheats only); mend is the hammer's repair on the nearest boat, as the player (a slipway near her stands for a workbench); bump knocks the nearest boat as the shore does and says if a naust kept it off (cheats only); down takes the nearest slipway down as the hammer does and lists what lies around it after (cheats only); ships lists the boats loaded (where, how high the hull rides over the sea, speed, owner); row works the helm of the boat the player stands on (any kind, to compare) or else the nearest of ours, taking the helm first, a step forward or backward per n (forward 0: just how she goes), and leave lets go of it (cheats only; the game rows and sails only with someone at the helm); sink removes the boats loaded within 150 m (test boats; cheats only).",
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
                ["launchAt"] = b.Hull != null && s.Where(b.Hull) is Vector3 w ? V(w) : null, ["owner"] = s.GetComponent<ZNetView>().IsOwner(), ["depths"] = Depths(s) };
        }
        // The water's depth ahead of the bow end, every 2 m to the furthest launch reach (negative: dry), and any boat in the way.
        private static string Depths(Slipway s)
        {
            Vector3 fwd = s.transform.forward; fwd.y = 0; fwd.Normalize();
            float sea = ZoneSystem.instance.m_waterLevel, reach = Hulls.All.Max(Launching.Reach);
            var d = new List<string>();
            for (float m = 0; m <= reach; m += 2)
            {
                Vector3 p = s.transform.position + fwd * m;
                // (a boat in the way, by name: water a launch counts as dry)
                Collider hit = Physics.OverlapCapsule(new Vector3(p.x, sea - 1f, p.z), new Vector3(p.x, sea + 2f, p.z), 2.6f, LayerMask.GetMask("vehicle"), QueryTriggerInteraction.Ignore).FirstOrDefault();
                d.Add((sea - ZoneSystem.instance.GetGroundHeight(p)).ToString("0.0") + (hit ? $"[{hit.transform.root.name}]" : ""));
            }
            return string.Join(" ", d);
        }
        private static JArray Boats(Vector3 me) => new JArray(UnityEngine.Object.FindObjectsByType<Ship>(FindObjectsSortMode.None).Where(Yard.IsOurs).Select(s => new JObject
        {
            ["hull"] = Yard.HullOf(s)?.Name, ["at"] = V(s.transform.position), ["wind"] = EnvMan.instance != null ? Math.Round(EnvMan.instance.GetWindIntensity(), 2) : 0,
            ["hold"] = s.GetComponentInChildren<Container>() is Container c ? $"{c.GetInventory()?.NrOfItems()} items, {c.m_width}x{c.m_height}" : "", ["distance"] = Math.Round(Vector3.Distance(s.transform.position, me), 1), ["overSea"] = Math.Round(s.transform.position.y - ZoneSystem.instance.m_waterLevel, 2),
            ["tilt"] = Math.Round(Vector3.Angle(s.transform.up, Vector3.up), 1), ["speed"] = Math.Round(s.GetComponent<Rigidbody>().linearVelocity.magnitude, 2), ["setting"] = s.GetSpeedSetting().ToString(),
            ["owner"] = s.GetComponent<ZNetView>().IsOwner(), ["depth"] = Math.Round(ZoneSystem.instance.m_waterLevel - ZoneSystem.instance.GetGroundHeight(s.transform.position), 2),
        }));

        private static IEnumerator Run(string[] a, Action<JObject> output, Action<string> error)
        {
            Player me = Player.m_localPlayer;
            if (me == null || ZNet.instance == null) { error("needs the player in a world"); yield break; }
            Vector3 at = me.transform.position;
            // ("yard at x z <verb>": as if standing there, for the slipway and boats nearest that spot rather than the player)
            if (a.Length > 4 && a[1] == "at" && float.TryParse(a[2], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float ax)
                && float.TryParse(a[3], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float az))
            { at = new Vector3(ax, at.y, az); a = new[] { a[0] }.Concat(a.Skip(4)).ToArray(); }
            string verb = a.Length > 1 ? a[1] : "status";
            Slipway s = Nearest(at);
            ZNetView view = s != null ? s.GetComponent<ZNetView>() : null;
            switch (verb)
            {
                case "status":
                    output(new JObject { ["slipways"] = new JArray(UnityEngine.Object.FindObjectsByType<Slipway>(FindObjectsSortMode.None).Where(x => Vector3.Distance(x.transform.position, at) < 150).Select(x => Describe(x, at))),
                        ["boats"] = Boats(at), ["made"] = new JArray(new[] { Names.Slipway, Names.Faering, Names.Knarr, Names.Naust }.Where(n => Yard.Get(n) != null)) });
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
                        string colour = a.Length > 3 ? (a[3] == "undyed" ? "" : a[3]) : null; // (pay [stages] [colour]: the sail in that colour)
                        for (int i = 0; i < stages && b.Current != null; i++) b.Take(b.Needs().Select(n => (n.item == Sailcloth.Sail && colour != null ? Sailcloth.Of(colour) : n.item, n.n)).ToList());
                        view.GetZDO().Set(Slipway.Key, b.Save());
                    }
                    yield return new WaitForSeconds(1f);
                    output(Describe(s, at));
                    break;
                }
                case "clear":
                {
                    // the nearest slipway's build forgotten, nothing given back (cheats only: undoes a pay)
                    if (s == null || view == null || !view.IsValid()) { error("no slipway within 150 m"); yield break; }
                    if (!Cheats()) { error("yard clear changes the world: only where cheats are allowed"); yield break; }
                    if (!view.IsOwner()) view.ClaimOwnership();
                    view.GetZDO().Set(Slipway.Key, "");
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
                case "api":
                {
                    // a whole build through Shipyard.Api, as Jarldom's shipwright works it: lay, put in stage by stage (a little over and a little short
                    // each time, to see it take only what's needed), launch (cheats only: it makes things from nothing)
                    if (s == null) { error("no slipway within 150 m"); yield break; }
                    if (!Cheats()) { error("yard api builds a boat from nothing: only where cheats are allowed"); yield break; }
                    Vector3 sw = s.transform.position; string hull = a.Length > 2 ? a[2] : Hulls.Faering;
                    var log = new JArray();
                    log.Add($"near: {Api.SlipwaysNear(at, 150).Length}, hulls: {string.Join(",", Api.Hulls)}, {hull} hold {Api.CargoSlots(hull)}");
                    log.Add($"start: {Api.StartBuild(sw, hull)}, again: {Api.StartBuild(sw, hull)}, stage {Api.Stage(sw)}");
                    for (int guard = 0; guard < 20 && !Api.Finished(sw); guard++)
                    {
                        int stage = Api.Stage(sw);
                        var needs = Api.Needs(sw);
                        var (first, n0) = needs[0];
                        int part = Api.Contribute(sw, first, Math.Max(1, n0 - 1)); // (one short)
                        var took = needs.Select(x => $"{x.item} {(x.item == first ? part + Api.Contribute(sw, x.item, 5) : Api.Contribute(sw, x.item, x.n + 7))}/{x.n}");
                        log.Add($"stage {stage}: took {string.Join(", ", took)} -> stage {Api.Stage(sw)}");
                    }
                    yield return new WaitForSeconds(1f);
                    log.Add($"finished: {Api.Finished(sw)}, launch: {Api.Launch(sw)}, stage after: {Api.Stage(sw)}");
                    output(new JObject { ["api"] = log });
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
                case "bump":
                {
                    // the nearest of our boats knocked as by the shore (blunt 10, nobody behind it): her health before and after, and whether a naust
                    // shelters her (cheats only)
                    if (!Cheats()) { error("yard bump harms a boat: only where cheats are allowed"); yield break; }
                    Ship ship = UnityEngine.Object.FindObjectsByType<Ship>(FindObjectsSortMode.None).Where(Yard.IsOurs).OrderBy(x => Vector3.Distance(x.transform.position, at)).FirstOrDefault();
                    if (ship == null) { error("no boat of ours loaded"); yield break; }
                    WearNTear wear = ship.GetComponent<WearNTear>(); float before = wear.GetHealthPercentage();
                    var hit = new HitData(); hit.m_damage.m_blunt = 10; hit.m_point = ship.transform.position; hit.m_dir = Vector3.up;
                    wear.Damage(hit);
                    yield return new WaitForSeconds(1f);
                    output(new JObject { ["hull"] = Yard.HullOf(ship)?.Name, ["sheltered"] = Naust.Sheltering(ship.transform.position) != null, ["before"] = Math.Round(before, 3), ["after"] = Math.Round(wear.GetHealthPercentage(), 3) });
                    break;
                }
                case "mend":
                {
                    // the hammer's repair on the nearest boat, as the player: the game's station check (a slipway near her counts), then the mend
                    Ship ship = UnityEngine.Object.FindObjectsByType<Ship>(FindObjectsSortMode.None).OrderBy(x => Vector3.Distance(x.transform.position, at)).FirstOrDefault();
                    if (ship == null) { error("no boat loaded"); yield break; }
                    var check = HarmonyLib.AccessTools.Method(typeof(Player), "CheckCanRemovePiece");
                    bool may = (bool)check.Invoke(me, new object[] { ship.GetComponent<Piece>() });
                    WearNTear wear = ship.GetComponent<WearNTear>(); float before = wear.GetHealthPercentage();
                    bool mended = may && wear.Repair();
                    yield return new WaitForSeconds(1f);
                    output(new JObject { ["boat"] = Utils.GetPrefabName(ship.gameObject), ["slipwayNear"] = Slipway.Near(ship.transform.position, Slipway.RepairReach), ["mayMend"] = may, ["mended"] = mended, ["before"] = Math.Round(before, 3), ["after"] = Math.Round(wear.GetHealthPercentage(), 3) });
                    break;
                }
                case "row":
                {
                    // the nearest færing's oars, as its helm sets them: forward (stop, slow) or backward (back, stop); the game rows only
                    // with someone aboard
                    if (!Cheats()) { error("yard row steers a boat: only where cheats are allowed"); yield break; }
                    // (the ship the player stands on, any kind, for comparing; else the nearest of ours)
                    Ship ship = UnityEngine.Object.FindObjectsByType<Ship>(FindObjectsSortMode.None).Where(x => Vector3.Distance(x.transform.position, me.transform.position) < 10).OrderBy(x => Vector3.Distance(x.transform.position, me.transform.position)).FirstOrDefault()
                        ?? UnityEngine.Object.FindObjectsByType<Ship>(FindObjectsSortMode.None).Where(Yard.IsOurs).OrderBy(x => Vector3.Distance(x.transform.position, at)).FirstOrDefault();
                    if (ship == null) { error("no boat loaded"); yield break; }
                    if (a.Length > 2 && a[2] == "leave") { me.AttachStop(); yield return new WaitForSeconds(0.5f); output(new JObject { ["helm"] = me.IsAttached() }); break; }
                    // (and only with a hand on the tiller: the player takes it, if near enough)
                    if (!me.IsAttached() && ship.GetComponentInChildren<ShipControlls>() is ShipControlls helm) { helm.Interact(me, false, false); yield return new WaitForSeconds(1f); }
                    for (int i = 0; i < (int)F(a, 3, 1); i++) { if (a.Length > 2 && a[2] == "backward") ship.Backward(); else ship.Forward(); }
                    yield return new WaitForSeconds(0.5f);
                    Rigidbody body = ship.GetComponent<Rigidbody>();
                    output(new JObject { ["helm"] = me.IsAttached(), ["ship"] = Utils.GetPrefabName(ship.gameObject), ["setting"] = ship.GetSpeedSetting().ToString(), ["at"] = V(ship.transform.position),
                        ["speed"] = Math.Round(Vector3.Dot(body.velocity, ship.transform.forward), 2), ["wind"] = Math.Round(EnvMan.instance.GetWindIntensity(), 2), ["windAngle"] = Math.Round(Vector3.Angle(EnvMan.instance.GetWindDir(), ship.transform.forward)) });
                    break;
                }
                case "sink":
                {
                    if (!Cheats()) { error("yard sink removes boats: only where cheats are allowed"); yield break; }
                    int n = 0;
                    foreach (Ship ship in UnityEngine.Object.FindObjectsByType<Ship>(FindObjectsSortMode.None).Where(x => Yard.IsOurs(x) && Vector3.Distance(x.transform.position, at) < 150).ToList())
                    {
                        ZNetView v = ship.GetComponent<ZNetView>(); if (!v.IsOwner()) v.ClaimOwnership(); v.Destroy(); n++;
                    }
                    output(new JObject { ["removed"] = n });
                    break;
                }
                default: error("yard status | lay [hull] | pay [stages] [colour] | launch | clear | use | api [hull] | down | bump | mend | ships | row [forward|backward|leave] [n] | sink"); break;
            }
        }
    }
}
