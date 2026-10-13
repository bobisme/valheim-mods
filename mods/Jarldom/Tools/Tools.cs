using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using BepInEx;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jarldom
{
    // The "jarldom" command for Claude Tools: look at the colony and drive it without playing. Commands that change the world follow the
    // game's cheat rule (single player, the host, or devcommands); the rest only read.
    internal static class Tools
    {
        private static BaseUnityPlugin _tools; private static float _next;
        private const string Usage = "jarldom status | roster | smite <prefab|stag> | feed <name> | greetings | dogs | dog adopt|release | corner <name> [stand] | dim <name> <days> | villager <name|vid> | needs <name> | renown | rooms | room check <n> | board | log [n] | saga | compat | " +
            "[cheats:] place pillars [metres] | spawn [profession] [traits..] | migrate [now|dusk] | accept [i|all] | room <type> <x1> <z1> <x2> <z2> [god] | zone <type> <x1> <z1> <x2> <z2> | " +
            "prof <name> <profession> | mood <name> <moodlet> | worth <name> <n> | need <name> <need>=<n> | give <name> <prefab> [n] | bed <name> | goto <name> <x> <z> | time <hours> | pay | " +
            "favour <god> <n> | feast | thing now | ascend <name> | kill <name> | raid <event> | horn | treasury [x z] | stockpile <category> [x z] | tp <name> | remove | " +
            "act <action> <arg> (as the colony window does) | put <prefab> <x> <z> [yaw] [y] | wall <prefab> <x1> <z1> <x2> <z2> [levels] [step] | unput [prefab] [minY] (test pieces) | fill <x> <z> <prefab> [n] (into the chest there) | ground <x> <z>";

        public static void Tick()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 5;
            BaseUnityPlugin found = Compat.Find("com.quad.claudetools", "com.dhack.claudetools");
            if (found == _tools) return;
            _tools = found;
            if (found == null) return;
            try
            {
                found.GetType().GetMethod("RegisterCommand", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[] { Plugin.Name, "jarldom", Usage, (Func<string[], Action<JObject>, Action<string>, IEnumerator>)Run });
                Plugin.Log("Claude Tools found: jarldom command added");
            }
            catch (Exception e) { Plugin.Log("Could not add the jarldom command to Claude Tools: " + e.Message); }
        }
        public static void Stop()
        {
            try { _tools?.GetType().GetMethod("UnregisterAll", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, new object[] { Plugin.Name }); } catch { }
            _tools = null;
        }
        private static bool Cheats() => ZNet.instance != null && (ZNet.instance.IsServer() || global::Console.instance != null && global::Console.instance.IsCheatsEnabled()); // (the game's own test: devcommands on and a server admin, not just devcommands typed on a client)
        private static Colony Here()
        {
            Player p = Player.m_localPlayer;
            return p == null ? (ZNet.instance != null ? Colony.Nearest(ZNet.instance.GetReferencePosition(), 400) : null) ?? Colony.All.FirstOrDefault() : Colony.Nearest(p.transform.position, 400); // (a dedicated server: where it's looking)
        }
        private static JArray V(Vector3 v) => new JArray(Math.Round(v.x, 1), Math.Round(v.y, 1), Math.Round(v.z, 1));
        private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
        private static Brain Who(string name) => Villagers.All.FirstOrDefault(b => b.S.Vid == name || b.S.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || b.S.FullName.Equals(name.Replace('_', ' '), StringComparison.OrdinalIgnoreCase))
            ?? Villagers.All.FirstOrDefault(b => b.S.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase));

        private static string Box(IList<float> xz) { var xs = xz.Where((v, i) => i % 2 == 0).ToList(); var zs = xz.Where((v, i) => i % 2 == 1).ToList(); return $"{xs.Min():0.#},{zs.Min():0.#} to {xs.Max():0.#},{zs.Max():0.#}"; }
        private static Vector3? _pathMark; private static Dictionary<ZDOID, uint> NetMark = new Dictionary<ZDOID, uint>(); private static float NetMarkAt;
        public static void RunNow(string[] args, Action<JObject> output, Action<string> error) => Run(args, output, error); // (the server's own mailbox)
        private static IEnumerator Run(string[] args, Action<JObject> output, Action<string> error)
        {
            try { Do(args, output, error); }
            catch (Exception e) { error("jarldom: " + e.Message + "\n" + e.StackTrace); }
            return null;
        }
        private static void Do(string[] a, Action<JObject> output, Action<string> error)
        {
            string cmd = a.Length > 1 ? a[1].ToLowerInvariant() : "status";
            Colony c = Here();
            string[] cheats = { "arrivals", "despawn", "drop", "creature", "table", "soil", "cultivate", "grow", "nav", "roof", "act", "level", "put", "wall", "unput", "fill", "place", "spawn", "migrate", "accept", "room", "zone", "prof", "mood", "worth", "need", "give", "bed", "goto", "time", "pay", "favour", "feast", "thing", "ascend", "kill", "raid", "horn", "treasury", "stockpile", "tp", "remove", "tribute", "craft", "clear", "site", "hurt", "takedown", "relic", "pickrelic", "touch", "mischief", "follow", "purge", "leisure", "makeown", "deeds", "wreck", "slay", "unlose", "forgetrow", "unlog", "relog", "kneel", "setfuel", "traderun", "away", "master", "envoy", "capture", "barrow", "claims", "ask", "smite", "feed", "dog", "landing", "corner", "dim" };
            if (cheats.Contains(cmd) && !(cmd == "room" && a.Length > 2 && a[2] == "check") && !Cheats()) { error("jarldom " + cmd + " changes the world: only in single player, for the host, or for a server admin with devcommands on"); return; }
            if (cmd == "seatview") { Ui.JarlView.Test(); output(new JObject { ["active"] = Ui.JarlView.Active }); return; }
            if (cmd == "pintest")
            {
                // A pin of ours by its key (local only): add, then look; remove, then look; the player's own pins are counted each time.
                Vector3 here = Player.m_localPlayer.transform.position + Vector3.forward * 5;
                var pins = HarmonyLib.AccessTools.Field(typeof(Minimap), "m_pins").GetValue(Minimap.instance) as List<Minimap.PinData>;
                int Theirs() => pins.Count(pp => pp.m_save);
                bool Ours() => pins.Any(pp => pp.m_name == "Jarldom test pin");
                string step = a.Length > 2 ? a[2] : "add";
                int before = Theirs();
                if (step == "add") Net.PinHere("jarldom-test", here, "Jarldom test pin", true); else Net.PinHere("jarldom-test", here, "", false);
                output(new JObject { ["step"] = step, ["ourPin"] = Ours(), ["playerPinsBefore"] = before, ["playerPinsAfter"] = Theirs() });
                return;
            }
            if (cmd == "netsize")
            {
                // What the game sends for Jarldom's objects (read-only): each one's whole record, as sent when it changes or moves.
                int Size(ZDO z) { if (z == null) return 0; var pkg = new ZPackage(); z.Serialize(pkg); return pkg.Size(); }
                var o = new JObject();
                foreach (Colony col in Colony.All) o[col.R.Name] = new JObject { ["pillars"] = Size(col.Z), ["board"] = Size(col.BoardZ), ["record"] = col.Z.GetByteArray(Colony.RecordKey)?.Length ?? 0, ["saga"] = Size(col.SagaZ), ["parts"] = $"log {col.R.Log.Count}, flags {col.R.Flags.Count}, rooms {col.R.Rooms.Count}, zones {col.R.Zones.Count}, roster {col.R.Roster.Count}, fallen {col.R.Fallen.Count}" };
                o["villagers"] = new JArray(Villagers.Bodies().Select(h => { ZDO z = h.GetComponent<ZNetView>().GetZDO(); return $"{z.GetString(Villagers.NameKey)}: body {Size(z)} B (bag {z.GetByteArray(ZDOVars.s_items)?.Length ?? 0}), soul {Size(SoulStore.Of(z))} B"; }));
                output(o);
                return;
            }
            if (cmd == "netrate")
            {
                // Test: how much Jarldom's objects send. "netrate start", wait, "netrate": each object's changes (data revisions) since,
                // times its whole size (a changed object goes out whole to every peer near it).
                int Size(ZDO z) { if (z == null) return 0; var pkg = new ZPackage(); z.Serialize(pkg); return pkg.Size(); }
                var all = new List<(string what, ZDO z)>();
                foreach (Colony col in Colony.All) { all.Add(("pillars", col.Z)); all.Add(("board", col.BoardZ)); all.Add(("saga", col.SagaZ)); }
                foreach (Humanoid h in Villagers.Bodies()) { ZDO z = h.GetComponent<ZNetView>().GetZDO(); string n = z.GetString(Villagers.NameKey).Split(' ')[0]; all.Add((n + " body", z)); all.Add((n + " soul", SoulStore.Of(z))); }
                if (a.Length > 2 && a[2] == "start") { NetMark = all.Where(x => x.z != null).ToDictionary(x => x.z.m_uid, x => x.z.DataRevision); NetMarkAt = Time.time; output(new JObject { ["marked"] = NetMark.Count }); return; }
                float secs = Mathf.Max(0.1f, Time.time - NetMarkAt);
                var rows = all.Where(x => x.z != null && NetMark.ContainsKey(x.z.m_uid)).Select(x => (x.what, changes: (long)(x.z.DataRevision - NetMark[x.z.m_uid]), size: Size(x.z))).ToList();
                double total = rows.Sum(r => r.changes * r.size) / secs;
                output(new JObject { ["seconds"] = Math.Round(secs, 1), ["bytesPerSecond"] = Math.Round(total), ["bodiesPerSecond"] = Math.Round(rows.Where(r => r.what.EndsWith("body")).Sum(r => r.changes * r.size) / secs), ["soulsPerSecond"] = Math.Round(rows.Where(r => r.what.EndsWith("soul")).Sum(r => r.changes * r.size) / secs),
                    ["top"] = new JArray(rows.OrderByDescending(r => r.changes * r.size).Take(10).Select(r => $"{r.what}: {r.changes} changes x {r.size} B = {r.changes * r.size / secs:0} B/s")) });
                return;
            }
            if (cmd == "zdokeys")
            {
                // Test: what a villager's body object carries, by kind (strings and byte arrays with their sizes).
                Brain b = Who(a[2]); if (b == null) { error("who?"); return; }
                ZDO z = b.Body.GetComponent<ZNetView>().GetZDO();
                ZDOExtraData.GetData(z.m_uid, out var fl, out var v3, out var qu, out var it, out var lo, out var st, out var ba, out var con);
                var names = typeof(ZDOVars).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static).Where(f => f.FieldType == typeof(int)).ToDictionary(f => (int)f.GetValue(null), f => f.Name, EqualityComparer<int>.Default);
                foreach (string k in new[] { Villagers.VidKey, Villagers.ColonyKey, Villagers.NameKey, Villagers.DoingKey, Villagers.MoodKey, Villagers.StateKey, "jarldom_soul", "dhc_home", "dhc_homename", Leisure.IdleKey, Follow.Key }) names[k.GetStableHashCode()] = k;
                string N(int h) => names.TryGetValue(h, out string n) ? n : h.ToString();
                output(new JObject { ["floats"] = fl.Count, ["vec3"] = v3.Count, ["quats"] = qu.Count, ["ints"] = it.Count, ["longs"] = lo.Count,
                    ["intKeys"] = string.Join(", ", it.Select(kv => N(kv.Key))), ["floatKeys"] = string.Join(", ", fl.Select(kv => N(kv.Key))), ["longKeys"] = string.Join(", ", lo.Select(kv => N(kv.Key))), ["vecKeys"] = string.Join(", ", v3.Select(kv => N(kv.Key))),
                    ["strings"] = new JArray(st.Select(kv => $"{N(kv.Key)}: {kv.Value.Length} \"{(kv.Value.Length > 40 ? kv.Value.Substring(0, 40) + "…" : kv.Value)}\"")),
                    ["bytes"] = new JArray(ba.Select(kv => $"{N(kv.Key)}: {kv.Value.Length} B")) });
                return;
            }
            if (cmd == "pathtest")
            {
                // A villager's road (read-only): "pathtest mark" remembers where the player stands; "pathtest" asks the navmesh for the way
                // from the mark to where they stand now, as a villager asks (full path first; else the partial one, and the door it ends at).
                Vector3 here = Player.m_localPlayer.transform.position;
                if (a.Length > 2 && a[2] == "mark") { _pathMark = here; output(new JObject { ["marked"] = V(here) }); return; }
                if (_pathMark == null) { error("pathtest mark first"); return; }
                Vector3 from = _pathMark.Value; var path = new List<Vector3>();
                bool fromOk = Nav.Valid(out Vector3 f2, from, 2f), toOk = Nav.Valid(out Vector3 t2, here, 2f);
                bool full = Pathfinding.instance.GetPath(fromOk ? f2 : from, toOk ? t2 : here, path, Pathfinding.AgentType.Humanoid, requireFullPath: true);
                var o = new JObject { ["from"] = V(from), ["to"] = V(here), ["startOnNavmesh"] = fromOk, ["endOnNavmesh"] = toOk, ["full"] = full };
                if (full) { o["waypoints"] = path.Count; o["length"] = Math.Round(path.Zip(path.Skip(1), Vector3.Distance).Sum(), 1); o["climb"] = Math.Round(path.Max(p => p.y) - path.Min(p => p.y), 1); }
                else
                {
                    bool partial = Pathfinding.instance.GetPath(fromOk ? f2 : from, toOk ? t2 : here, path, Pathfinding.AgentType.Humanoid);
                    Vector3 end = path.Count > 0 ? path[path.Count - 1] : from;
                    var near = new List<Piece>(); Piece.GetAllPiecesInRadius(end, 4f, near);
                    Door door = near.Select(p => p.GetComponent<Door>()).Where(d => d != null && !Utils.GetPrefabName(d.gameObject).Contains("window")).OrderBy(d => Vector3.Distance(d.transform.position, end)).FirstOrDefault();
                    o["partial"] = partial; o["partialEnd"] = V(end); o["endShort"] = Math.Round(Vector3.Distance(end, here), 1);
                    o["doorAtEnd"] = door == null ? "none" : $"{Utils.GetPrefabName(door.gameObject)} {Vector3.Distance(door.transform.position, end):0.0} m from the end, {(door.GetComponent<ZNetView>()?.GetZDO()?.GetInt(ZDOVars.s_state) == 0 ? "shut" : "open")}";
                }
                output(o);
                return;
            }
            if (cmd == "selftest")
            {
                // Read-only checks of what the mod reads from live game objects near the player (anywhere, no colony needed).
                Vector3 here = Player.m_localPlayer.transform.position; float r = a.Length > 2 ? F(a[2]) : 30f;
                var near = new List<Piece>(); Piece.GetAllPiecesInRadius(here, r, near);
                var stations = near.Select(p => p.GetComponent<CraftingStation>()).Where(st => st != null)
                    .Select(st => $"{Utils.GetPrefabName(st.gameObject)} lvl {st.GetLevel()}: {Crafts.Unusable(st) ?? "usable"}");
                var ferms = near.Select(p => p.GetComponent<Fermenter>()).Where(f => f != null)
                    .Select(f => $"fermenter at {f.transform.position.x:0},{f.transform.position.z:0}: {Work.FermentStatus(f)}, roofed {Work.Roofed(f.transform.position)}");
                var cooks = near.Select(p => p.GetComponent<CookingStation>()).Where(cs => cs != null)
                    .Select(cs => $"{Utils.GetPrefabName(cs.gameObject)}: fire {Work.Cook(cs, "IsFireLit")}, free slot {Work.CookFree(cs)}, done {Work.Cook(cs, "HaveDoneItem")}");
                var smelters = near.Select(p => p.GetComponent<Smelter>()).Where(sm => sm != null)
                    .Select(sm => $"{Utils.GetPrefabName(sm.gameObject)}: queue {Work.QueueSize(sm)} of {sm.m_maxOre}, fuel {sm.GetComponent<ZNetView>()?.GetZDO()?.GetFloat(ZDOVars.s_fuel):0.0}");
                Work.LearnCrops();
                var beasts = Character.GetAllCharacters().Where(ch => ch != null && ch.IsTamed() && Vector3.Distance(ch.transform.position, here) < r)
                    .Select(ch => $"{Utils.GetPrefabName(ch.gameObject)} '{ch.GetComponent<Tameable>()?.GetText()}': livestock {ch.GetComponent<Procreation>() != null}, young {ch.GetComponent<Growup>() != null}, saddled {ch.GetComponentInChildren<Sadle>() != null}, hungry {ch.GetComponent<Tameable>()?.IsHungry()}, eats {string.Join("/", ch.GetComponent<MonsterAI>()?.m_consumeItems?.Where(i => i != null).Select(i => i.name).Take(4) ?? new string[0])}");
                var known = ObjectDB.instance.m_recipes.Count(rc => rc != null && rc.m_enabled && rc.m_item != null && rc.m_craftingStation != null && Player.m_localPlayer.IsRecipeKnown(rc.m_item.m_itemData.m_shared.m_name)
                    && near.Any(p => p.GetComponent<CraftingStation>() is CraftingStation st && st.m_name == rc.m_craftingStation.m_name && st.GetLevel() >= rc.m_minStationLevel));
                output(new JObject { ["stations"] = new JArray(stations), ["fermenters"] = new JArray(ferms), ["cooking"] = new JArray(cooks), ["smelters"] = new JArray(smelters),
                    ["saplings"] = new JArray(Work.Saplings.Select(x => $"{x.sapling.name} from {x.amount} {x.seed}, biomes {x.plant.m_biome}")), ["crops"] = Work.Crops.Count,
                    ["tamed"] = new JArray(beasts), ["commissionable"] = known, ["costMsPerFrame"] = Plugin.CostFrames > 0 ? Math.Round(Plugin.CostMs / Plugin.CostFrames, 4) : -1 });
                return;
            }
            if (cmd == "stitch")
            {
                // What the navmesh's tile stitching sees at a point (read-only): the raw hits of a ray down, in the order the engine gives.
                Vector3 p = new Vector3(F(a[2]), 6000f, F(a[3]));
                var buf = new RaycastHit[64];
                int n = Physics.RaycastNonAlloc(p, Vector3.down, buf, 10000f, Pathfinding.instance.m_layers.value);
                output(new JObject { ["raw"] = new JArray(buf.Take(n).Select(h => $"{h.point.y:0.00} {Utils.GetPrefabName(h.collider.transform.root.gameObject)}")), ["stairLinks"] = StairLinks.Count });
                return;
            }
            if (cmd == "heights")
            {
                // Along a line (read-only): what a ray down from the top first hits, and how high ("heights x1 z1 x2 z2 top [step]").
                float x1 = F(a[2]), z1 = F(a[3]), x2 = F(a[4]), z2 = F(a[5]), top = F(a[6]), step = a.Length > 7 ? F(a[7]) : 0.5f;
                int mask = LayerMask.GetMask("piece", "Default", "static_solid", "terrain");
                int n = Mathf.Max(1, Mathf.RoundToInt(Vector2.Distance(new Vector2(x1, z1), new Vector2(x2, z2)) / step));
                var line = new JArray();
                for (int i = 0; i <= n; i++)
                {
                    Vector3 p = new Vector3(Mathf.Lerp(x1, x2, i / (float)n), top, Mathf.Lerp(z1, z2, i / (float)n));
                    line.Add(Physics.Raycast(p, Vector3.down, out RaycastHit h, 30f, mask, QueryTriggerInteraction.Ignore) ? $"{p.x:0.0},{p.z:0.0}: {h.point.y:0.00} {Utils.GetPrefabName(h.collider.transform.root.gameObject)}" : $"{p.x:0.0},{p.z:0.0}: none");
                }
                output(new JObject { ["line"] = line });
                return;
            }
            if (cmd == "probe")
            {
                // From the player (read-only): the floor under them, the ceiling over them, and the walls each way at chest height.
                Vector3 here = Player.m_localPlayer.transform.position;
                int mask = LayerMask.GetMask("piece", "Default", "static_solid", "terrain");
                float Hit(Vector3 from, Vector3 dir, float max) => Physics.Raycast(from, dir, out RaycastHit h, max, mask, QueryTriggerInteraction.Ignore) ? h.distance : -1;
                float down = Hit(here + Vector3.up * 0.5f, Vector3.down, 4f), up = Hit(here + Vector3.up * 1.2f, Vector3.up, 12f);
                Vector3 chest = here + Vector3.up * 1.2f;
                output(new JObject { ["at"] = V(here), ["floor"] = down < 0 ? (JToken)null : Math.Round(here.y + 0.5f - down, 2), ["ceiling"] = up < 0 ? (JToken)null : Math.Round(here.y + 1.2f + up, 2),
                    ["east"] = Hit(chest, Vector3.right, 30), ["west"] = Hit(chest, Vector3.left, 30), ["north"] = Hit(chest, Vector3.forward, 30), ["south"] = Hit(chest, Vector3.back, 30) });
                return;
            }
            if (cmd == "storeys")
            {
                // Floors near the player by height (read-only): each level's extent, for finding rooms in a house of storeys.
                Vector3 here = Player.m_localPlayer.transform.position; float r = a.Length > 2 ? F(a[2]) : 15f;
                var list = new List<Piece>(); Piece.GetAllPiecesInRadius(here, r, list);
                var floors = list.Where(p => { string n = Utils.GetPrefabName(p.gameObject); return n.Contains("floor") || n.StartsWith("stone_floor"); })
                    .GroupBy(p => Mathf.Round(p.transform.position.y * 2) / 2).OrderBy(g => g.Key)
                    .Select(g => new JObject { ["y"] = g.Key, ["pieces"] = g.Count(), ["x"] = $"{g.Min(p => p.transform.position.x):0.0}..{g.Max(p => p.transform.position.x):0.0}", ["z"] = $"{g.Min(p => p.transform.position.z):0.0}..{g.Max(p => p.transform.position.z):0.0}" });
                output(new JObject { ["at"] = V(here), ["levels"] = new JArray(floors) });
                return;
            }
            if (cmd == "roomtest")
            {
                // Measure a room without marking it (read-only, anywhere): roomtest <type> <x1> <z1> <x2> <z2> [floor y]
                if (a.Length < 7 || !Enum.TryParse(a[2], true, out RoomType type)) { error("roomtest <type> <x1> <z1> <x2> <z2> [floor]"); return; }
                float x1 = Math.Min(F(a[3]), F(a[5])), x2 = Math.Max(F(a[3]), F(a[5])), z1 = Math.Min(F(a[4]), F(a[6])), z2 = Math.Max(F(a[4]), F(a[6]));
                float floor = a.Length > 7 ? F(a[7]) : Floor(new Vector3((x1 + x2) / 2, 0, (z1 + z2) / 2));
                var room = new Room { Id = "test", Type = type, Floor = floor, Xz = new List<float> { x1, z1, x2, z1, x2, z2, x1, z2 } };
                room.Height = Colony.Ceiling(room);
                RoomFacts f = Colony.Measure(room);
                RoomVerdict v = RoomRules.Judge(type, f, "");
                var near = new List<Piece>(); Piece.GetAllPiecesInRadius(new Vector3((x1 + x2) / 2, floor, (z1 + z2) / 2), Mathf.Max(x2 - x1, z2 - z1), near);
                var doors = near.Where(p => p.GetComponent<Door>() != null && Math.Abs(p.transform.position.y - floor) < 3).Select(p => $"{Utils.GetPrefabName(p.gameObject)} at {p.transform.position.x:0.0},{p.transform.position.y:0.0},{p.transform.position.z:0.0} edge {Geometry.Distance(room.Xz, p.transform.position.x, p.transform.position.z):0.00}").ToList();
                output(new JObject { ["gaps"] = new JArray(Colony.Gaps(room)), ["doorsNear"] = new JArray(doors), ["floor"] = Math.Round(floor, 2), ["ceiling"] = Math.Round(room.Height, 2), ["ok"] = v.Ok, ["grade"] = v.Grade.ToString(), ["failures"] = new JArray(v.Failures),
                    ["walls"] = new JArray(f.Walls.Select(w => Math.Round(w, 2))), ["roof"] = Math.Round(f.Roof, 2), ["doors"] = f.Doors, ["contents"] = new JObject(f.Count.Select(kv => new JProperty(kv.Key, kv.Value))) });
                return;
            }
            if (cmd == "finditems") { string q = a[2].ToLowerInvariant(); output(new JObject { ["items"] = new JArray(ObjectDB.instance.m_items.Where(i => i != null && i.name.ToLowerInvariant().Contains(q)).Select(i => { var sh = i.GetComponent<ItemDrop>().m_itemData.m_shared; return $"{i.name} ({sh.m_itemType}, {Localization.instance.Localize(sh.m_name)}, armor {sh.m_armor}, value {sh.m_value})"; })) }); return; }
            if (cmd == "attackof") { var sh = ObjectDB.instance.GetItemPrefab(a[2]).GetComponent<ItemDrop>().m_itemData.m_shared; output(new JObject { ["anim"] = sh.m_attack?.m_attackAnimation, ["type"] = sh.m_attack?.m_attackType.ToString(), ["skill"] = sh.m_skillType.ToString(), ["damage"] = sh.m_damages.GetTotalDamage(), ["equipDur"] = sh.m_equipDuration, ["status"] = sh.m_attackStatusEffect?.name }); return; }
            if (cmd == "traders") { output(new JObject { ["traders"] = new JArray(ZNetScene.instance.m_prefabs.Where(pf => pf != null && pf.GetComponent<Trader>() != null).Select(pf => pf.name + ": " + string.Join(", ", pf.GetComponent<Trader>().m_items.Select(t => $"{t.m_prefab?.name} x{t.m_stack} {t.m_price}c{(string.IsNullOrEmpty(t.m_requiredGlobalKey) ? "" : " [" + t.m_requiredGlobalKey + "]")}")))) }); return; }
            if (cmd == "items")
            {
                // Jarldom's items as the game has them: in the item list, and their recipes (read-only, anywhere).
                var db = ObjectDB.instance;
                output(new JObject { ["items"] = new JArray(Items.All.Select(i => i.name + (db != null && db.GetItemPrefab(i.name) == i ? "" : " (not in ObjectDB)"))),
                    ["recipes"] = new JArray((db?.m_recipes ?? new List<Recipe>()).Where(r => r != null && r.m_item != null && r.m_item.name.StartsWith("Jarldom"))
                        .Select(r => $"{r.m_item.name}: {r.m_craftingStation?.name ?? "by hand"} lvl {r.m_minStationLevel}, " + string.Join(" + ", r.m_resources.Select(q => $"{q.m_amount} {q.m_resItem?.name}")))) });
                return;
            }
            if (cmd == "pickrelic")
            {
                // The nearest dropped heart of the high seat into the local player's bag, wherever it lies (for tests).
                ItemDrop drop = Object.FindObjectsOfType<ItemDrop>().Where(d => Utils.GetPrefabName(d.gameObject) == Items.RelicName).OrderBy(d => Vector3.Distance(d.transform.position, Player.m_localPlayer.transform.position)).FirstOrDefault();
                if (drop == null) { error("no heart lying about"); return; }
                bool got = Player.m_localPlayer.Pickup(drop.gameObject, false, false);
                output(new JObject { ["picked"] = got, ["colony"] = Relic.ColonyName(drop.m_itemData) });
                return;
            }
            if (cmd == "relic")
            {
                // A heart of the high seat from a jarldom backup file, into the local player's bag.
                if (a.Length < 3 || !System.IO.File.Exists(a[2])) { error("relic <backup file>"); return; }
                string[] lines = System.IO.File.ReadAllLines(a[2]);
                ItemDrop.ItemData item = Player.m_localPlayer?.GetInventory().AddItem(Items.Relic, 1) == true ? Player.m_localPlayer.GetInventory().GetAllItems().Last(Items.IsRelic) : null;
                if (item == null) { error("no room in the bag"); return; }
                item.m_customData[Relic.IdKey] = lines[0]; item.m_customData[Relic.NameKey] = lines[1]; item.m_customData[Relic.RecordKey] = lines[2];
                if (lines.Length > 3 && lines[3].Length > 0) item.m_customData[Relic.BoardKey] = lines[3];
                output(new JObject { ["relic"] = lines[1] });
                return;
            }
            if (c == null && cmd != "place" && cmd != "put" && cmd != "claims" && cmd != "find" && cmd != "profile" && cmd != "versions" && cmd != "env" && cmd != "overlay" && cmd != "icon" && cmd != "layers" && cmd != "compat" && cmd != "status") { error("no colony near (jarldom place pillars makes one)"); return; }
            Player me = Player.m_localPlayer;
            switch (cmd)
            {
                case "status":
                    output(new JObject
                    {
                        ["version"] = Plugin.Version, ["hostHasJarldom"] = Net.HostHasJarldom, ["colonies"] = new JArray(Colony.All.Select(x => x.Id)),
                        ["colony"] = c == null ? null : Describe(c),
                        ["needsYou"] = c == null ? null : new JArray(Ui.ColonyWindow.NeedsYou(c)),
                        ["compat"] = new JObject(Compat.Report().Select(r => new JProperty(r.name, r.present ? r.version : "absent"))),
                    });
                    break;
                case "graves":
                    // Villagers' tombstones (read-only): whose, where, what's in them.
                    output(new JObject { ["graves"] = new JArray(UnityEngine.Object.FindObjectsByType<TombStone>(FindObjectsSortMode.None).Where(t => t.GetComponent<ZNetView>()?.GetZDO()?.GetString(Villagers.TombKey, "") != "").Select(t =>
                        $"{t.GetComponent<ZNetView>().GetZDO().GetString(Villagers.TombKey)} at {t.transform.position.x:0},{t.transform.position.z:0}: " + string.Join(", ", t.GetComponent<Container>().GetInventory().GetAllItems().Select(i => Classes.Prefab(i) + " x" + i.m_stack)))) });
                    break;
                case "ui":
                    {
                        // A window opened as a player would open it, for a look (shot) or a click (screen): "ui colony [tab]", "ui villager <name>",
                        // "ui room <n>", "ui zone <n>", "ui seat" (the jarl's view, the colony's own), "ui close".
                        string what = a.Length > 2 ? a[2] : "colony";
                        switch (what)
                        {
                            case "colony": Ui.ColonyWindow.Open(c, a.Length > 3 ? a[3] : null); break;
                            case "villager": { Brain b = Who(a[3]); if (b == null) { error("who?"); return; } Ui.VillagerPanel.Open(b.Body, true); break; }
                            case "room": Ui.RoomEditor.Open(c, c.R.Rooms[int.Parse(a[3])], null); break;
                            case "zone": Ui.RoomEditor.Open(c, null, c.R.Zones[int.Parse(a[3])]); break;
                            case "seat": if (!Ui.JarlView.Active) Ui.JarlView.Test(); break;
                            case "close": Ui.Windows.CloseAll(); Ui.JarlView.End(); break;
                            default: error("ui colony|villager|room|zone|seat|close"); return;
                        }
                        output(new JObject { ["opened"] = what });
                        break;
                    }
                case "sagatest":
                    {
                        // The saga's safety (owner, cheats): the log in memory cut to its last two entries and the saga unread, then a save.
                        if (!Cheats()) { error("cheats only"); return; }
                        int before = ColonyRecord.LoadLog(c.SagaZ?.GetByteArray(Colony.SagaLogKey)).Count;
                        c.ForgetSagaForTest(); int cut = c.R.Log.Count;
                        c.Log("text", "", "", "A test of the saga's keeping.");
                        c.MarkDirty(); c.Flush();
                        int after = ColonyRecord.LoadLog(c.SagaZ?.GetByteArray(Colony.SagaLogKey)).Count;
                        output(new JObject { ["sagaBefore"] = before, ["memoryCut"] = cut, ["sagaAfter"] = after, ["memoryAfter"] = c.R.Log.Count });
                        break;
                    }
                case "flags":
                    {
                        // The colony's flags (read-only), those starting with the prefix if one is given.
                        string pre = a.Length > 2 ? a[2] : "";
                        if (a.Length > 3 && !Cheats()) { error("jarldom flags <key> <value> changes the colony: only in single player, for the host, or for a server admin with devcommands on"); return; }
                        if (a.Length > 3) { if (a[3] == "-") c.R.Flags.Remove(pre); else c.R.Flags[pre] = (int)F(a[3]); c.MarkDirty(); } // ("flags key value" sets one, "flags key -" clears it)
                        output(new JObject { ["flags"] = new JObject(c.R.Flags.Where(kv => kv.Key.StartsWith(pre)).OrderBy(kv => kv.Key).Select(kv => new JProperty(kv.Key, kv.Value))) });
                        break;
                    }
                case "compat": output(new JObject { ["compat"] = new JArray(Compat.Report().Select(r => new JObject { ["mod"] = r.name, ["present"] = r.present, ["version"] = r.version, ["use"] = r.use })) }); break;
                case "roster":
                    output(new JObject { ["roster"] = new JArray(c.R.Roster.Select(r => new JObject { ["vid"] = r.Vid, ["name"] = r.Name, ["prof"] = r.Prof.ToString(), ["state"] = r.State.ToString(), ["bed"] = r.Bed, ["worth"] = r.Worth, ["at"] = new JArray(r.X, r.Y, r.Z), ["loaded"] = Villagers.Body(r.Vid) != null })), ["fallen"] = c.R.Fallen.Count, ["coming"] = new JArray(c.R.Coming.Select(n => n.Name + " (" + n.Traits + ")")) });
                    break;
                case "villager": case "needs":
                    {
                        Brain b = a.Length > 2 ? Who(a[2]) : Villagers.All.FirstOrDefault();
                        if (b == null)
                        {
                            // Why not (a body here without a brain): who owns it, is it dead, can its state be read.
                            Character body = a.Length > 2 ? Villagers.Bodies().FirstOrDefault(h => h.GetComponent<ZNetView>().GetZDO().GetString(Villagers.NameKey, "").StartsWith(a[2], StringComparison.OrdinalIgnoreCase)) : null;
                            if (body == null) { error("no such villager loaded here"); return; }
                            ZDO bz = body.GetComponent<ZNetView>().GetZDO();
                            byte[] st = SoulStore.StateOf(bz);
                            string read; try { read = VillagerState.Load(st) == null ? "null" : "ok"; } catch (Exception e) { read = e.Message; }
                            error($"{bz.GetString(Villagers.NameKey)} has no brain: owner {(bz.IsOwner() ? "this game" : bz.GetOwner().ToString())}, dead {body.IsDead()}, at {V(body.transform.position)}, soul {(SoulStore.Of(bz) != null ? "found" : "missing")}, state {st?.Length ?? 0} B ({read}), ai {(body.GetComponent<MonsterAI>()?.enabled.ToString() ?? "none")}");
                            return;
                        }
                        output(Villager(b));
                    }
                    break;
                case "renown": c.SurveySoon(); output(new JObject { ["renown"] = c.Renown, ["wealth"] = c.WealthPart, ["comfort"] = c.ComfortPart, ["deeds"] = c.R.Deeds, ["pieceValue"] = c.PieceValue, ["beds"] = c.Beds, ["threshold"] = Renown.Threshold(c.R.Arrivals), ["migration"] = Migration.Why(c) }); break;
                case "rooms":
                    output(new JObject { ["rooms"] = new JArray(c.R.Rooms.Select((r, i) => new JObject { ["n"] = i, ["id"] = r.Id, ["type"] = r.Type.ToString(), ["god"] = r.God, ["ok"] = r.Ok, ["grade"] = r.Grade.ToString(), ["points"] = r.Points, ["failures"] = r.Failures, ["open"] = r.OpenEdges, ["area"] = Math.Round(Geometry.Area(r.Xz)), ["floor"] = Math.Round(r.Floor, 2), ["height"] = Math.Round(r.Height, 2), ["box"] = Box(r.Xz) })), ["zones"] = new JArray(c.R.Zones.Select(z => new JObject { ["id"] = z.Id, ["type"] = z.Type.ToString(), ["area"] = Math.Round(Geometry.Area(z.Xz)), ["box"] = Box(z.Xz) })) });
                    break;
                case "room":
                    if (a.Length > 3 && a[2] == "roof")
                    {
                        // Which cells of a room the game doesn't see as under a roof (read-only).
                        Room r = c.R.Rooms[int.Parse(a[3])];
                        var open = Geometry.Grid(r.Xz, 1f).Where(p => !Cover.IsUnderRoof(new Vector3(p.Item1, r.Floor + 1.2f, p.Item2))).Select(p => $"{p.Item1:0.#},{p.Item2:0.#}");
                        output(new JObject { ["open"] = new JArray(open) });
                        return;
                    }
                    if (a.Length > 3 && a[2] == "check")
                    {
                        int n = int.Parse(a[3]);
                        Room r = n >= 0 && n < c.R.Rooms.Count ? c.R.Rooms[n] : c.R.Rooms.FirstOrDefault(x => x.Id == a[3]);
                        if (r == null) { error("no such room"); return; }
                        RoomFacts f = Colony.Measure(r);
                        RoomVerdict v = c.IsOwner ? c.Check(r) : RoomRules.Judge(r.Type, f, r.God);
                        output(new JObject { ["type"] = r.Type.ToString(), ["ok"] = v.Ok, ["grade"] = v.Grade.ToString(), ["points"] = v.Points, ["failures"] = new JArray(v.Failures), ["walls"] = new JArray(f.Walls.Select(w => Math.Round(w, 2))), ["roof"] = Math.Round(f.Roof, 2), ["doors"] = f.Doors, ["area"] = Math.Round(f.Area, 1), ["comfort"] = f.Comfort, ["floorHeight"] = Math.Round(f.FloorHeight, 1), ["contents"] = new JObject(f.Count.Select(kv => new JProperty(kv.Key, kv.Value))) });
                        c.MarkDirty();
                    }
                    else
                    {
                        if (a.Length < 7) { error("room <type> <x1> <z1> <x2> <z2> [god]"); return; }
                        float x1 = F(a[3]), z1 = F(a[4]), x2 = F(a[5]), z2 = F(a[6]);
                        string at = a.FirstOrDefault(x => x.StartsWith("y=")); // ("y=<height>": a room on an upper floor, as the marker's corners would be)
                        if (at != null) a = a.Where(x => x != at).ToArray();
                        float floor = at != null ? F(at.Substring(2)) : Floor(new Vector3((x1 + x2) / 2, 0, (z1 + z2) / 2));
                        string god = a.Length > 7 && Tables.God(a[7]) != null ? a[7] : a[2].Equals("OdinShrine", StringComparison.OrdinalIgnoreCase) ? "odin" : "";
                        Net.Act(c, "room", $"{Parse<RoomType>(a[2])}|{god}|{floor.ToString(CultureInfo.InvariantCulture)}|{Rect(x1, z1, x2, z2)}");
                        output(new JObject { ["added"] = a[2], ["floor"] = floor });
                    }
                    break;
                case "zone":
                    {
                        if (a.Length < 7) { error("zone <type> <x1> <z1> <x2> <z2>"); return; }
                        float x1 = F(a[3]), z1 = F(a[4]), x2 = F(a[5]), z2 = F(a[6]);
                        float floor = ZoneSystem.instance.GetGroundHeight(new Vector3((x1 + x2) / 2, 0, (z1 + z2) / 2));
                        Net.Act(c, "zone", $"{Parse<ZoneType>(a[2])}|{floor.ToString(CultureInfo.InvariantCulture)}|{Rect(x1, z1, x2, z2)}");
                        output(new JObject { ["added"] = a[2] });
                    }
                    break;
                case "board":
                    output(new JObject { ["completed"] = c.Board.CompletedCount, ["orders"] = new JArray(c.Board.All.Where(o => o.State != OrderState.Done && o.State != OrderState.Cancelled).OrderByDescending(o => o.Priority).Take(60).Select(o => new JObject { ["id"] = o.Id, ["kind"] = o.Kind, ["prof"] = o.Prof.ToString(), ["state"] = o.State.ToString(), ["by"] = o.By, ["who"] = o.Vid, ["blocked"] = o.Blocked, ["prio"] = o.Priority, ["at"] = new JArray(Math.Round(o.X), Math.Round(o.Y), Math.Round(o.Z)) })) });
                    break;
                case "log": { int n = a.Length > 2 ? int.Parse(a[2]) : 20; output(new JObject { ["log"] = new JArray(c.R.Log.Skip(Math.Max(0, c.R.Log.Count - n)).Select(e => $"d{e.Day} {e.Kind} {e.A} {e.B} {e.Text}".Trim())) }); } break;
                case "saga": output(new JObject { ["saga"] = new JArray(Stories.Chronicle(c.R.Log)) }); break;

                // ---- cheats ----
                case "place":
                    {
                        if (me == null) { error("no player"); return; }
                        float d = a.Length > 3 ? F(a[3]) : 6;
                        Vector3 at = a.Length > 4 ? new Vector3(F(a[3]), 0, F(a[4])) : me.transform.position + me.transform.forward * d; // (place pillars <x> <z>, or <metres ahead>)
                        if (ZoneSystem.instance.GetSolidHeight(at, out float h)) at.y = h;
                        Colony near = Colony.Nearest(at, Renown.MinApart);
                        if (near != null) { error($"colony {near.Id} is {Utils.DistanceXZ(near.Position, at):0} m away (colonies stand {Renown.MinApart} m apart)"); return; }
                        Relic.Placing(me, at); // (carrying a heart: these pillars take its colony)
                        GameObject go = Object.Instantiate(Pieces.Get(Pieces.PillarsName), at, Quaternion.LookRotation(-me.transform.forward));
                        go.GetComponent<Piece>().SetCreator(me.GetPlayerID(), Splatform.PlatformManager.DistributionPlatform.LocalUser.PlatformUserID);
                        go.GetComponent<WearNTear>()?.OnPlaced();
                        output(new JObject { ["placed"] = V(at) });
                    }
                    break;
                case "spawn":
                    {
                        int seed = UnityEngine.Random.Range(0, int.MaxValue);
                        Newcomer n = Souls.Make(seed, "test" + seed.ToString("x"), null);
                        string named = a.FirstOrDefault(x => x.StartsWith("name=")); if (named != null) { bool fem = a.Contains("female"); for (int s2 = seed; n.Female != fem; s2++) n = Souls.Make(s2, n.Group, null); n.Name = named.Substring(5).Replace('_', ' '); } // (a particular soul: name=Odd, female)
                        if (a.Length > 3) { var traits = a.Skip(3).Where(t => Tables.Trait(t) != null).Take(2).ToList(); if (traits.Count > 0) n.Traits = string.Join(",", traits.Concat(n.Traits.Split(',').Where(t => !traits.Contains(t) && traits.All(x => !Tables.Clash(x, t)))).Take(2)); }
                        c.R.Coming.Add(n); c.R.ComingLanded = true;
                        Migration.Accept(c, c.R.Coming.Count - 1, me?.GetPlayerName() ?? "");
                        Brain b = null;
                        string vid = c.R.Roster.Last().Vid;
                        if (a.Length > 2 && Enum.TryParse(a[2], true, out Prof p)) Plugin.Instance.StartCoroutine(WhenUp(vid, "prof", p.ToString())); // (their body isn't up yet)
                        if (a.Contains("test")) Villagers.Command(vid, "mood", "arrived"); // (marks below)
                        if (a.Contains("test")) { Humanoid hb = Villagers.Body(vid); Brain tb = hb != null ? Villagers.BrainFor(hb) : null; if (tb != null) { tb.S.Bump("testspawn"); tb.Dirty(); } }
                        output(new JObject { ["spawned"] = n.Name, ["vid"] = vid, ["traits"] = n.Traits, ["story"] = n.Story });
                    }
                    break;
                case "migrate":
                    {
                        string when = a.Length > 2 ? a[2] : "now";
                        if (when == "dusk") Migration.Dusk(c, c.Day, true);
                        else { c.R.Coming.Clear(); Migration.Dawn(c, c.Day, true); Migration.Dusk(c, c.Day, true); }
                        output(new JObject { ["coming"] = new JArray(c.R.Coming.Select(Souls.Describe)), ["landed"] = c.R.ComingLanded });
                    }
                    break;
                case "accept":
                    {
                        int count = c.R.Coming.Count;
                        if (a.Length > 2 && a[2] == "all") for (int i = 0; i < count; i++) Migration.Accept(c, 0, me?.GetPlayerName() ?? "");
                        else Migration.Accept(c, a.Length > 2 ? int.Parse(a[2]) : 0, me?.GetPlayerName() ?? "");
                        output(new JObject { ["population"] = c.Population });
                    }
                    break;
                case "prof": { Brain b = Who(a[2]); if (b == null) { error("who?"); return; } b.Command("prof", Parse<Prof>(a[3]).ToString()); output(new JObject { ["prof"] = b.S.Prof.ToString() }); } break;
                case "mood": { Brain b = Who(a[2]); if (b == null) { error("who?"); return; } b.Command("mood", a[3]); output(new JObject { ["mood"] = Mood.Of(b.S, b.Now) }); } break;
                // Test: asks (read-only): every ask here, what this player took on, what the world offers. ask <name> make [Kind] [deed] |
                // take | done | answer | clear: make one (a kind forced: tried until it comes up), take it on as this player, mark it done
                // (a visit seen, a kill made), answer it as the panel does (hand-in from the bag), or drop it.
                case "asks":
                    {
                                                WorldFacts wf = Villagers.All.FirstOrDefault(b => b.S.Colony == c.Id) is Brain any ? Asks.Facts(c, any.S) : null;
                        output(new JObject
                        {
                            ["asks"] = new JArray(Villagers.All.Where(b => b.S.Colony == c.Id && b.S.Asked != null).Select(b => $"{b.S.Name}: {Requests.Describe(b.S.Asked)} [{b.S.Asked.Kind} {b.S.Asked.Target} id {b.S.Asked.Id}, day {b.S.Asked.Day}, {Requests.DaysLeft(b.S.Asked, c.Day)} left, coins {b.S.Asked.Coins}{(b.S.Asked.Deed ? ", DEED" : "")}] “{b.S.Asked.Why}”")),
                            ["shown"] = new JArray(c.R.Living.Select(r => (r.Name, ask: Asks.Shown(Villagers.Body(r.Vid)?.GetComponent<ZNetView>()?.GetZDO()))).Where(x => x.ask != null).Select(x => $"{x.Name}: {x.ask.Value.what} ({x.ask.Value.daysLeft} left{(x.ask.Value.deed ? ", deed" : "")})")),
                            ["taken"] = new JArray(Asks.All(Player.m_localPlayer).Select(t => $"{t.Name}: {t.Kind} {t.Target} ({t.Label}) id {t.Id}{(t.Done ? " DONE" : "")} at {t.X:0},{t.Z:0}")),
                            ["stage"] = wf?.Stage ?? -1,
                            ["places"] = new JArray((wf?.Places ?? new List<Place>()).Select(p => $"{p.Label} ({p.Key}, stage {p.Stage}) {Mathf.RoundToInt(Utils.DistanceXZ(new Vector3(p.X, 0, p.Z), c.Position))} m at {p.X:0},{p.Z:0}")),
                            ["avenge"] = new JArray((wf?.Avenge ?? new List<(string, string)>()).Select(x => $"{x.Item1} by {x.Item2}")),
                        });
                    }
                    break;
                case "dogs":
                    {
                        JArray Row(IEnumerable<Character> l) => new JArray(l.Select(d => { MonsterAI ai = d.GetComponent<MonsterAI>(); GameObject f = ai?.GetFollowTarget(); ZDO z = d.GetComponent<ZNetView>().GetZDO();
                            return $"{d.GetComponent<Tameable>()?.GetText()} ({Utils.GetPrefabName(d.gameObject)}) at {d.transform.position.x:0},{d.transform.position.z:0} follow {(f != null ? f.name + (Villagers.Brain(f.GetComponent<Character>()) is Brain fb ? " " + fb.S.Name : "") : "-")} patrol {(z.GetBool(ZDOVars.s_patrol) ? z.GetVec3(ZDOVars.s_patrolPoint, Vector3.zero).ToString("F0") : "-")} owner {(d.GetComponent<ZNetView>().IsOwner() ? "this game" : "other")}"; }));
                        Character first = Dogs.Of(c).FirstOrDefault();
                        var around = first == null ? new JArray() : new JArray(Character.GetAllCharacters().Where(ch => ch != null && ch != first && !ch.IsPlayer() && Vector3.Distance(ch.transform.position, first.transform.position) < 40)
                            .Select(ch => $"{Utils.GetPrefabName(ch.gameObject)} {Vector3.Distance(ch.transform.position, first.transform.position):0} m enemy {BaseAI.IsEnemy(first, ch)} tamed {ch.IsTamed()} alerted {(ch.GetBaseAI() as MonsterAI)?.IsAlerted()} event {(ch.GetBaseAI() as MonsterAI)?.IsEventCreature()}"));
                        output(new JObject { ["dogs"] = Row(Dogs.Of(c)), ["strays"] = Row(Dogs.Strays(c)), ["around"] = around });
                    }
                    break;
                case "dog":
                    {
                        string op = a.Length > 2 ? a[2] : "";
                        Character near(IEnumerable<Character> l) => l.OrderBy(d => Vector3.Distance(d.transform.position, me.transform.position)).FirstOrDefault();
                        if (op == "adopt") { Character w = near(Dogs.Strays(c)); if (w == null) { error("no tame wolf in the colony"); return; } string why = Dogs.Adopt(c, w); if (why != "") { error(why); return; } output(new JObject { ["adopted"] = w.GetComponent<Tameable>()?.GetText() }); }
                        else if (op == "release") { Character d = near(Dogs.Of(c)); if (d == null) { error("no village dog"); return; } Dogs.Release(d); output(new JObject { ["released"] = d.GetComponent<Tameable>()?.GetText() }); }
                        else { error("dog adopt | release"); return; }
                    }
                    break;
                case "landing":
                    {
                        // "reset": as if every player had been away a while (the next landing at a dock is met); "meet": the welcome at once.
                        Zone dock = c.R.Zones.FirstOrDefault(z => z.Type == ZoneType.FishingDock);
                        if (a.Length > 2 && a[2] == "meet") { if (dock == null) { error("no fishing dock zone"); return; } Landing.Meet(c, Zones.Centre(dock), me.GetPlayerName()); }
                        else Landing.Forget();
                        output(new JObject { ["dock"] = dock != null ? V(Zones.Centre(dock)) : null, ["onShip"] = Ship.GetLocalShip() != null });
                    }
                    break;
                case "corner":
                    {
                        // A villager's corner: their bed, the item stands within 4 m of it (and what hangs there, and whose), their keepsakes.
                        // "stand": a test item stand put 2 m from their bed.
                        Brain b = a.Length > 2 ? Who(a[2]) : null; if (b == null) { error("corner <name> [stand]"); return; }
                        Bed bed = c.BedById(b.S.Bed); if (bed == null) { error("they have no bed"); return; }
                        if (a.Length > 3 && a[3] == "stand") { Vector3 at = bed.transform.position + bed.transform.right * 2f; Put(Assets.Find("itemstandh"), new Vector3(at.x, bed.transform.position.y, at.z), bed.transform.eulerAngles.y); } // (a standing one: a wall one needs a wall)
                        var stands = c.PiecesIn(c.Radius).Where(p => p != null && p.GetComponent<ItemStand>() != null && Vector3.Distance(p.transform.position, bed.transform.position) < 4f).Select(p => p.GetComponent<ItemStand>());
                        output(new JObject { ["bed"] = V(bed.transform.position), ["stands"] = new JArray(stands.Select(s => $"{Utils.GetPrefabName(s.gameObject)} at {V(s.transform.position)} holds {(s.HaveAttachment() ? s.GetAttachedItem() : "-")} by {s.GetComponent<ZNetView>().GetZDO().GetString(CornerTask.StandKey, "-")}")),
                            ["keepsakes"] = new JArray(b.Body.GetInventory().GetAllItems().Where(CornerTask.Keepsake).Select(Prefab)), ["doing"] = b.Doing });
                    }
                    break;
                case "dim":
                    {
                        // As if their worth last rose that many days ago (0: today).
                        Brain b = a.Length > 2 ? Who(a[2]) : null; if (b == null) { error("dim <name> [days]"); return; }
                        int today = Clock.Day(b.Now); if (a.Length > 3) { b.S.Counts[Dimming.Key] = today - int.Parse(a[3]); b.Dirty(); }
                        ZDO bz = b.View.GetZDO();
                        output(new JObject { ["name"] = b.S.Name, ["stage"] = Dimming.Of(b.S, today), ["days"] = Dimming.Days(b.S, today), ["worthy"] = b.S.Worthy, ["skin"] = V(bz.GetVec3(ZDOVars.s_skinColor, Vector3.zero)), ["ownSkin"] = V(bz.GetVec3("jarldom_skin0", Vector3.zero)) });
                    }
                    break;
                case "greetings": output(new JObject { ["recent"] = new JArray(Greet.Recent) }); break;
                case "feed":
                    {
                        // One food or potion from the player's bag into a villager's, as a hotbar key on them does.
                        Brain b = a.Length > 2 ? Who(a[2]) : null; if (b == null) { error("feed <name>"); return; }
                        ItemDrop.ItemData food = me.GetInventory().GetAllItems().FirstOrDefault(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable && !i.m_equipped);
                        if (food == null) { error("nothing to eat in the bag"); return; }
                        string what = food.m_shared.m_name;
                        Villagers.Feed(b.Body, me, food);
                        output(new JObject { ["fed"] = b.S.Name, ["item"] = what });
                    }
                    break;
                case "smite":
                    {
                        // A killing blow from the player to the nearest creature of that prefab (or "stag": a great stag), as their own hit
                        // would land: the kill hooks see the player as the killer.
                        Player p = Player.m_localPlayer; string want = a.Length > 2 ? a[2] : "";
                        Character ch = Character.GetAllCharacters().Where(x => x != null && !x.IsDead() && !x.IsPlayer() && Villagers.Brain(x) == null && Vector3.Distance(x.transform.position, p.transform.position) < 80
                            && (want == "stag" ? Compat.IsGreatStag(x) : Utils.GetPrefabName(x.gameObject) == want)).OrderBy(x => Vector3.Distance(x.transform.position, p.transform.position)).FirstOrDefault();
                        if (ch == null) { error("smite <prefab|stag>: none within 80 m"); return; }
                        var hit = new HitData { m_point = ch.GetCenterPoint(), m_dir = (ch.transform.position - p.transform.position).normalized };
                        hit.m_damage.m_pierce = ch.GetMaxHealth() * 10f; hit.SetAttacker(p);
                        string name = ch.GetHoverName(); ch.Damage(hit);
                        output(new JObject { ["smote"] = name, ["prefab"] = Utils.GetPrefabName(ch.gameObject), ["level"] = ch.GetLevel() });
                    }
                    break;
                case "ask":
                    {
                        Brain b = a.Length > 2 ? Who(a[2]) : null; if (b == null) { error("ask <name> make [Kind] [deed] | take | done | answer | clear | disgrace"); return; }
                        string op = a.Length > 3 ? a[3] : "make";
                                                if (op == "make")
                        {
                            Ask? kind = a.Length > 4 && Enum.TryParse(a[4], true, out Ask k) ? k : (Ask?)null;
                            bool deed = a.Contains("deed");
                            Request made = null; var facts = Asks.Facts(c, b.S); var rng = new System.Random();
                            for (int i = 0; i < 400 && made == null; i++) { Request q = Requests.Generate(b.S, facts, c.Day, rng, deed); if (q != null && (kind == null || q.Kind == kind)) made = q; }
                            if (made == null) { error($"no {(kind?.ToString() ?? "")} ask fits (stage {facts.Stage}, {facts.Places.Count} places)"); return; }
                            b.S.Asked = made; Asks.Tag(b); b.Dirty();
                            c.Log("ask", b.S.Name, "", Requests.Short(made));
                        }
                        else if (op == "take") { if (b.S.Asked == null) { error("they ask nothing"); return; } Asks.Take(c, b.S); }
                        else if (op == "done")
                        {
                            var list = Asks.All(Player.m_localPlayer); foreach (var t in list.Where(t => t.Vid == b.S.Vid)) t.Done = true;
                            Player.m_localPlayer.m_customData[Asks.TakenKey] = string.Join("\u001e", list.Select(t => t.Encode()));
                        }
                        else if (op == "answer") Asks.Answer(c, b.S);
                        else if (op == "disgrace") { b.S.Disgraced = true; b.S.DisgraceSince = b.Now; b.Dirty(); Asks.Daily(b); }
                        else if (op == "clear") { b.S.Asked = null; Asks.Tag(b); Asks.Drop(Player.m_localPlayer, b.S.Vid); b.Dirty(); }
                        else { error("ask <name> make [Kind] [deed] | take | done | answer | clear | disgrace"); return; }
                        output(new JObject { ["name"] = b.S.Name, ["ask"] = b.S.Asked == null ? "" : $"{Requests.Describe(b.S.Asked)} [{b.S.Asked.Kind} {b.S.Asked.Target} id {b.S.Asked.Id}{(b.S.Asked.Deed ? ", DEED" : "")}]", ["taken"] = new JArray(Asks.All(Player.m_localPlayer).Select(t => $"{t.Name}: {t.Kind} {t.Target}{(t.Done ? " DONE" : "")}")), ["disgraced"] = b.S.Disgraced, ["hoard"] = b.S.Hoard, ["worth"] = b.S.Worth });
                    }
                    break;
                case "worth": { Brain b = Who(a[2]); if (b == null) { error("who?"); return; } b.Command("worth", a[3]); output(new JObject { ["worth"] = b.S.Worth, ["worthy"] = b.S.Worthy }); } break;
                case "need": { Brain b = Who(a[2]); if (b == null) { error("who?"); return; } b.Command("need", a[3]); output(Villager(b)); } break;
                case "give":
                    {
                        Brain b = Who(a[2]); if (b == null) { error("who?"); return; }
                        GameObject prefab = Assets.Find(a[3]); if (prefab == null) { error("no prefab " + a[3]); return; }
                        int n = a.Length > 4 ? int.Parse(a[4]) : 1;
                        for (int i = 0; i < n;) { int stack = Math.Min(n - i, prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize); if (!b.Body.GetInventory().AddItem(prefab, stack)) break; i += stack; }
                        if (a.Contains("keep")) foreach (ItemDrop.ItemData k in b.Body.GetInventory().GetAllItems().Where(x => Prefab(x) == a[3])) CornerTask.Mark(k, "test"); // (a keepsake, for their corner)
                        output(new JObject { ["bag"] = new JArray(b.Body.GetInventory().GetAllItems().Select(i => $"{Prefab(i)} x{i.m_stack}{(i.m_equipped ? " (worn)" : "")}")) });
                    }
                    break;
                case "bed": { Brain b = Who(a[2]); if (b == null) { error("who?"); return; } Bed free = c.FreeBeds().OrderBy(x => Vector3.Distance(x.transform.position, b.Pos)).FirstOrDefault(); if (free == null) { error("no free bed"); return; } b.Command("bed", Colony.IdOf(free.GetComponent<ZNetView>())); output(new JObject { ["bed"] = b.S.Bed, ["at"] = V(free.transform.position) }); } break;
                case "goto": { Brain b = Who(a[2]); if (b == null) { error("who?"); return; } Vector3 to = new Vector3(F(a[3]), 0, F(a[4])); ZoneSystem.instance.GetSolidHeight(to, out float h); to.y = h; b.Task?.Stop(b); b.Task = new GoTask(to, "going where I was told", 1.2f); b.Task.Start(b); output(new JObject { ["to"] = V(to) }); } break;
                case "tp": { Brain b = Who(a[2]); if (b == null) { error("who?"); return; } Vector3 to = me.transform.position + me.transform.forward * 2; b.Place(to); output(new JObject { ["at"] = V(to) }); } break;
                case "time":
                    {
                        double to;
                        if (a.Length > 2 && (a[2] == "morning" || a[2] == "day" || a[2] == "dusk" || a[2] == "night"))
                        {
                            double hour = a[2] == "morning" ? 7 : a[2] == "day" ? 10 : a[2] == "dusk" ? 18.2 : 23;
                            to = Clock.At(c.Day, hour);
                            if (to <= ZNet.instance.GetTimeSeconds()) to = Clock.At(c.Day + 1, hour);
                        }
                        else to = ZNet.instance.GetTimeSeconds() + Clock.Hours(a.Length > 2 ? double.Parse(a[2], CultureInfo.InvariantCulture) : 1);
                        ZNet.instance.SetNetTime(to);
                        output(new JObject { ["day"] = c.Day, ["hour"] = Math.Round(c.Hour, 2) });
                    }
                    break;
                case "pay": c.Payday(c.Day); output(new JObject { ["log"] = c.R.Log.Last().Text, ["missed"] = c.R.MissedPaydays }); break;
                case "favour": Faith.Add(c, a[2], int.Parse(a[3]), "test"); output(new JObject { ["favour"] = new JObject(c.R.Favour.Select(kv => new JProperty(kv.Key, kv.Value))), ["odin"] = Faith.Odin(c) }); break;
                case "feast": Feast.Call(c, me?.GetPlayerName() ?? "test"); output(new JObject { ["ready"] = Feast.Ready(c, out _, out _), ["lastFeast"] = c.R.LastFeast }); break;
                case "thing":
                    {
                        // Test: thing (raise today's matters now) | call | end | rule <yes|no> | speak (as if the teller spoke).
                        string sub = a.Length > 2 ? a[2] : "raise";
                        if (sub == "raise") Thing.Dawn(c, c.Day + Thing.Pending(c).Count, force: true);
                        else if (sub == "test") { Brain t = Villagers.All.Where(x => x.S.Colony == c.Id && Thing.Comes(x)).OrderBy(_ => UnityEngine.Random.value).FirstOrDefault(); Thing.Pending(c).Add(new Petition { Id = "test", About = "", Text = "Let us drink to the first copper from the camp!", Yes = "Drink to it", No = "Not now", YesMood = 2, NoMood = -1, By = t?.S.Vid ?? "", ByName = t?.S.Name ?? "", Day = c.Day }); c.MarkDirty(); }
                        else if (sub == "call") Thing.Call(c, Player.m_localPlayer?.GetPlayerName() ?? "the jarl");
                        else if (sub == "end") Thing.Close(c, "adjourned");
                        else if (sub == "speak") Thing.Speak(c);
                        else if (sub == "rule") Thing.Rule(c, c.R.Flag(Thing.Cur) - 1, a.Length > 3 && a[3] == "yes", Player.m_localPlayer?.GetPlayerName() ?? "the jarl");
                        int here = Thing.Gathered(c, out int expected);
                        output(new JObject { ["held"] = Thing.Held(c), ["current"] = c.R.Flag(Thing.Cur), ["spoken"] = c.R.Flag(Thing.Spoken), ["gathered"] = $"{here}/{expected}", ["canCall"] = Thing.CanCall(c),
                            ["petitions"] = new JArray(Thing.Pending(c).Select(p => $"day {p.Day} by {p.ByName}: {p.Text}")) });
                        break;
                    }
                case "ascend": { Brain b = Who(a[2]); if (b == null) { error("who?"); return; } Ascension.Begin(c, b.S.Vid, me?.GetPlayerName() ?? ""); output(new JObject { ["why"] = Ascension.Why(c, b.S) }); } break;
                case "hurt": { Brain b = Who(a[2]); if (b == null) { error("who?"); return; } float pct = a.Length > 3 ? F(a[3]) : 80; b.Body.SetHealth(b.Body.GetMaxHealth() * Mathf.Clamp01(1 - pct / 100f)); if (a.Contains("injure")) { b.S.InjuredUntil = b.Now + Clock.DayLength; Mood.Add(b.S, "injured", b.Now); b.Dirty(); } output(new JObject { ["health"] = b.Body.GetHealth(), ["injured"] = b.S.InjuredUntil > b.Now }); } break;
                case "kill": { Brain b = Who(a[2]); if (b == null) { error("who?"); return; } var hit = new HitData(); hit.m_damage.m_damage = 99999; hit.m_point = b.Body.GetCenterPoint(); b.Body.Damage(hit); output(new JObject { ["killed"] = b.S.Name }); } break;
                case "raid": RandEventSystem.instance.SetRandomEventByName(a.Length > 2 ? a[2] : "army_eikthyr", c.Position); output(new JObject { ["raid"] = a.Length > 2 ? a[2] : "army_eikthyr" }); break;
                case "tribute":
                    if (a.Length > 2 && a[2] == "pay") Tribute.Pay(c, me?.GetPlayerName() ?? "test");
                    else if (a.Length > 2 && a[2] == "dusk") Tribute.Dusk(c, c.Day);
                    else if (a.Length > 2 && a[2] == "clear") { foreach (string k in c.R.Flags.Keys.Where(k => k.StartsWith("tribute:")).ToList()) c.R.Flags.Remove(k); c.MarkDirty(); Tribute.Leave(c); }
                    else if (a.Length > 2 && a[2] == "demand") Tribute.Demand(c, c.Day);
                    output(new JObject { ["due"] = Tribute.Due(c), ["raiding"] = Tribute.Raiding(c), ["level"] = c.R.Flag("tribute:level"), ["next"] = c.R.Flag("tribute:next"), ["day"] = c.Day,
                        ["heralds"] = new JArray(Character.GetAllCharacters().Where(Tribute.IsHerald).Select(h => $"{h.transform.position.x:0},{h.transform.position.y:0},{h.transform.position.z:0}")),
                        ["event"] = RandEventSystem.instance?.GetCurrentRandomEvent()?.m_name ?? "" });
                    break;
                case "follow": { Brain b = Who(a[2]); if (b == null) { error("who?"); return; } bool stop = a.Length > 3 && a[3] == "stop"; b.Command("follow", stop ? "0" : me.GetPlayerID() + "|" + me.GetPlayerName()); output(new JObject { ["following"] = Follow.LeaderId(b) != 0, ["doing"] = b.Doing }); } break;
                case "purge":
                    {
                        // Test only: the stores and every villager's bag keep at most N of an item (the rest is gone).
                        string what = a[2]; int keep = a.Length > 3 ? int.Parse(a[3]) : 0, gone = 0;
                        var invs = Stock.Chests(c).Select(x => x.chest.GetInventory()).Concat(Villagers.All.Where(x => x.S.Colony == c.Id).Select(x => x.Body.GetInventory())).ToList();
                        int seen = 0;
                        foreach (Inventory inv in invs)
                            foreach (ItemDrop.ItemData i in inv.GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == what && !i.m_equipped).ToList())
                            {
                                int take = Math.Min(i.m_stack, Math.Max(0, seen + i.m_stack - keep));
                                seen += i.m_stack - take;
                                if (take > 0) { inv.RemoveItem(i, take); gone += take; }
                            }
                        output(new JObject { ["removed"] = gone, ["kept"] = seen });
                    }
                    break;
                case "doors":
                    if (a.Length > 2 && a[2] == "shut") foreach (Door d in c.PiecesIn(c.Radius).Select(pc => pc.GetComponent<Door>()).Where(d => d != null && d.m_keyItem == null && d.GetComponent<ZNetView>().GetZDO().GetInt(ZDOVars.s_state) != 0)) d.GetComponent<ZNetView>().InvokeRPC("UseDoor", true);
                    output(new JObject { ["doors"] = new JArray(c.PiecesIn(c.Radius).Select(pc => pc.GetComponent<Door>()).Where(d => d != null).Select(d => $"{d.name.Replace("(Clone)", "")} at {d.transform.position.x:0},{d.transform.position.z:0}: {(d.GetComponent<ZNetView>().GetZDO().GetInt(ZDOVars.s_state) == 0 ? "shut" : "open")}")), ["waiting"] = Doors.Waiting }); break;
                case "recipes": output(new JObject { ["recipes"] = new JArray(ObjectDB.instance.m_recipes.Where(r => r?.m_item != null && r.m_item.name == a[2]).Select(r => $"{r.name} enabled={r.m_enabled} station={r.m_craftingStation?.name} lvl={r.m_minStationLevel} one={r.m_requireOnlyOneIngredient} upgradeOnly={r.m_noCraftOnlyUpgrade}: " + string.Join(", ", r.m_resources.Select(q => $"{q.m_resItem?.name} {q.m_amount}+{q.m_amountPerLevel}")))), ["chosen"] = Crafts.RecipeFor(a[2])?.name }); break;
                case "craft":
                    if (a.Length > 2) Net.Act(c, "craft", a[2] + "|" + (a.Length > 3 ? a[3] : "1"));
                    output(new JObject { ["commissions"] = new JArray(Crafts.Commissions(c).Select(k => $"{k.Key} x{k.Value}")),
                        ["offered"] = new JArray(Crafts.Offered(c, Player.m_localPlayer).Select(r => r.m_item.name)), ["brewing"] = new JArray(Crafts.BrewWhy(c)) });
                    break;
                case "horn": Net.Act(c, "horn", c.HornUntil > Time.time ? "off" : "on"); output(new JObject { ["horn"] = c.HornUntil > Time.time }); break;
                case "treasury": case "stockpile":
                    {
                        int xi = cmd == "treasury" ? 2 : 3;
                        Vector3 from = a.Length > xi + 1 ? new Vector3(F(a[xi]), 0, F(a[xi + 1])) : me.transform.position;
                        Container ch = Stock.Chests(c, from).Select(x => x.chest).FirstOrDefault();
                        if (ch == null) { error("no chest"); return; }
                        string id = Colony.IdOf(ch.GetComponent<ZNetView>());
                        Net.Act(c, cmd, cmd == "treasury" ? id : id + "|" + (a.Length > 2 ? a[2] : "any"));
                        output(new JObject { ["chest"] = V(ch.transform.position), ["id"] = id });
                    }
                    break;
                case "put":
                    {
                        GameObject prefab = Assets.Find(a[2]);
                        if (prefab == null || prefab.GetComponent<Piece>() == null) { error("no piece " + a[2]); return; }
                        Vector3 at = new Vector3(F(a[3]), 0, F(a[4]));
                        at.y = a.Length > 6 ? F(a[6]) : Ground(at);
                        output(new JObject { ["put"] = V(Put(prefab, at, a.Length > 5 ? F(a[5]) : 0).transform.position) });
                    }
                    break;
                case "wall":
                    {
                        GameObject prefab = Assets.Find(a[2]);
                        if (prefab == null) { error("no piece " + a[2]); return; }
                        Vector3 p1 = new Vector3(F(a[3]), 0, F(a[4])), p2 = new Vector3(F(a[5]), 0, F(a[6]));
                        int levels = a.Length > 7 ? int.Parse(a[7]) : 1; float step = a.Length > 8 ? F(a[8]) : 2f;
                        float floor = a.Length > 9 ? F(a[9]) : Ground((p1 + p2) / 2);
                        float len = Vector3.Distance(p1, p2); int n = Mathf.Max(1, Mathf.RoundToInt(len / step));
                        float yaw = Mathf.Atan2(p2.x - p1.x, p2.z - p1.z) * Mathf.Rad2Deg + 90;
                        int made = 0;
                        for (int lv = 0; lv < levels; lv++)
                            for (int i = 0; i < n; i++) { Vector3 at = Vector3.Lerp(p1, p2, (i + 0.5f) / n); at.y = floor + lv * 2f; Put(prefab, at, yaw); made++; }
                        output(new JObject { ["pieces"] = made, ["yaw"] = yaw, ["floor"] = floor });
                    }
                    break;
                case "alonewhere": output(new JObject { ["alone"] = new JArray(c.PiecesIn(c.Radius + 20).Select(p => p.GetComponent<ZNetView>()).Where(v => v != null && v.IsValid() && v.GetComponent<Fireplace>() != null && c.LeftAloneAt(v.transform.position)).Select(v => $"{Utils.GetPrefabName(v.gameObject)} at {V(v.transform.position)}")) }); break; // (test: where the fires left alone are)
                case "tagged": { var counts = c.PiecesIn(c.Radius + 30).Where(p => p.GetComponent<ZNetView>()?.GetZDO()?.GetBool(TestKey) == true && (a.Length < 3 || Utils.GetPrefabName(p.gameObject) == a[2])).GroupBy(p => Utils.GetPrefabName(p.gameObject)).OrderByDescending(g => g.Count()); output(new JObject { ["tagged"] = new JObject(counts.Select(g => new JProperty(g.Key, g.Count()))) }); } break; // (count only: what unput would take)
                case "unput":
                    {
                        int n = 0;
                        string only = a.Length > 2 ? a[2] : ""; float minY = a.Length > 3 ? F(a[3]) : float.MinValue;
                        bool boxed = a.Length > 7; float bx1 = boxed ? F(a[4]) : 0, bz1 = boxed ? F(a[5]) : 0, bx2 = boxed ? F(a[6]) : 0, bz2 = boxed ? F(a[7]) : 0; // ("unput <prefab> <minY> x1 z1 x2 z2": only there)
                        bool In(Vector3 q) => !boxed || q.x >= Math.Min(bx1, bx2) && q.x <= Math.Max(bx1, bx2) && q.z >= Math.Min(bz1, bz2) && q.z <= Math.Max(bz1, bz2);
                        foreach (Piece p in c.PiecesIn(c.Radius + 30).Where(p => p.GetComponent<ZNetView>()?.GetZDO()?.GetBool(TestKey) == true && (only == "" || Utils.GetPrefabName(p.gameObject) == only) && p.transform.position.y >= minY && In(p.transform.position)).ToList()) { p.GetComponent<ZNetView>().ClaimOwnership(); p.GetComponent<ZNetView>().Destroy(); n++; }
                        output(new JObject { ["removed"] = n });
                    }
                    break;
                case "fill":
                    {
                        Vector3 at = new Vector3(F(a[2]), 0, F(a[3]));
                        Container ch = Stock.Chests(c, at).Select(x => x.chest).FirstOrDefault();
                        GameObject prefab = Assets.Find(a[4]);
                        if (ch == null || prefab == null) { error("no chest or prefab"); return; }
                        ContainerAccess.TakeOver(ch);
                        int n = a.Length > 5 ? int.Parse(a[5]) : 1, put = 0;
                        while (put < n) { int stack = Math.Min(n - put, prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize); if (!ch.GetInventory().AddItem(prefab, stack)) break; put += stack; }
                        output(new JObject { ["chest"] = V(ch.transform.position), ["put"] = put, ["holds"] = new JArray(ch.GetInventory().GetAllItems().Select(i => $"{Prefab(i)} x{i.m_stack}")) });
                    }
                    break;
                case "act": Net.Act(c, a[2], a.Length > 3 ? string.Join(" ", a.Skip(3)) : ""); output(new JObject { ["act"] = a[2] }); break;
                case "level":
                    {
                        // The game's own levelling, straight on the land's terrain records: a flat square to exactly y (the hoe's piece adds its
                        // own offset and smoothing, and stacks).
                        float x1 = Math.Min(F(a[2]), F(a[4])), x2 = Math.Max(F(a[2]), F(a[4])), z1 = Math.Min(F(a[3]), F(a[5])), z2 = Math.Max(F(a[3]), F(a[5])), y = F(a[6]);
                        Vector3 centre = new Vector3((x1 + x2) / 2, y, (z1 + z2) / 2);
                        float radius = Math.Max(x2 - x1, z2 - z1) / 2;
                        var settings = new TerrainOp.Settings { m_level = true, m_levelRadius = radius, m_square = true, m_smooth = false, m_paintCleared = false, m_raise = false };
                        var doOp = HarmonyLib.AccessTools.Method(typeof(TerrainComp), "DoOperation");
                        var comps = new HashSet<TerrainComp>();
                        foreach (Vector3 off in new[] { Vector3.zero, new Vector3(radius, 0, radius), new Vector3(-radius, 0, radius), new Vector3(radius, 0, -radius), new Vector3(-radius, 0, -radius) })
                            foreach (TerrainComp tc in TerrainComp.FindAllTerrainCompilers(centre + off)) comps.Add(tc);
                        if (comps.Count == 0) { Heightmap hm = Heightmap.FindHeightmap(centre); TerrainComp made = hm?.GetAndCreateTerrainCompiler(); if (made != null) comps.Add(made); }
                        int n = 0;
                        foreach (TerrainComp tc in comps)
                        {
                            ZNetView tv = tc.GetComponent<ZNetView>();
                            if (tv == null || !tv.IsValid()) continue;
                            if (!tv.IsOwner()) tv.ClaimOwnership();
                            doOp.Invoke(tc, new object[] { centre, Vector3.zero, settings }); n++;
                        }
                        output(new JObject { ["terrainRecords"] = n, ["radius"] = radius });
                    }
                    break;
                case "cultivate":
                    {
                        GameObject op = ToolPiece("Cultivator", "cultivate_v2");
                        if (op == null) { error("no cultivate_v2"); return; }
                        float x1 = Math.Min(F(a[2]), F(a[4])), x2 = Math.Max(F(a[2]), F(a[4])), z1 = Math.Min(F(a[3]), F(a[5])), z2 = Math.Max(F(a[3]), F(a[5]));
                        int n = 0;
                        for (float x = x1; x <= x2 + 0.01f; x += 1.5f) for (float z = z1; z <= z2 + 0.01f; z += 1.5f) { Object.Instantiate(op, new Vector3(x, ZoneSystem.instance.GetGroundHeight(new Vector3(x, 0, z)), z), Quaternion.identity); n++; }
                        output(new JObject { ["ops"] = n });
                    }
                    break;
                case "grow":
                    {
                        Vector3 at = new Vector3(F(a[2]), 0, F(a[3])); float r = a.Length > 4 ? F(a[4]) : 6;
                        int n = 0;
                        foreach (GameObject go in Work.Things(new Vector3(at.x, ZoneSystem.instance.GetGroundHeight(at), at.z), r).ToList()) { Plant p = go.GetComponent<Plant>(); if (p != null && p.GetComponent<ZNetView>().IsOwner()) { if (p.Grow() != null) n++; } }
                        output(new JObject { ["grown"] = n });
                    }
                    break;
                case "chests":
                    output(new JObject { ["treasury"] = c.R.Treasury, ["stockpiles"] = new JObject(c.R.Stockpiles.Select(kv => new JProperty(kv.Key, kv.Value))), ["chests"] = new JArray(Stock.Chests(c).Select(x => new JObject { ["at"] = V(x.chest.transform.position), ["id"] = x.id, ["category"] = x.category, ["items"] = new JArray(x.chest.GetInventory().GetAllItems().Select(i => $"{Prefab(i)} x{i.m_stack}")) })) });
                    break;
                case "nav":
                    {
                        Vector3 at = new Vector3(F(a[2]), F(a[3]), F(a[4]));
                        float r = a.Length > 5 ? F(a[5]) : 1.5f;
                        bool ok = Nav.Valid(out Vector3 v, at, r);
                        var path = new List<Vector3>();
                        bool p2 = a.Length > 8 && Pathfinding.instance.GetPath(at, new Vector3(F(a[6]), F(a[7]), F(a[8])), path, Pathfinding.AgentType.Humanoid);
                        output(new JObject { ["valid"] = ok, ["point"] = ok ? V(v) : null, ["path"] = p2, ["corners"] = new JArray(path.Select(V)) });
                    }
                    break;
                case "table":
                    {
                        PieceTable t = ObjectDB.instance.GetItemPrefab(a[2])?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
                        output(new JObject { ["pieces"] = new JArray((t?.m_pieces ?? new List<GameObject>()).Where(p => p != null).Select(p => p.name + " [" + string.Join(",", p.GetComponents<Component>().Select(x => x.GetType().Name)) + "]")) });
                    }
                    break;
                case "clear":
                    {
                        // Ground cleared for building: trees, stumps, bushes, rocks and plants in the rectangle (not pieces, creatures or items).
                        float x1 = Math.Min(F(a[2]), F(a[4])), x2 = Math.Max(F(a[2]), F(a[4])), z1 = Math.Min(F(a[3]), F(a[5])), z2 = Math.Max(F(a[3]), F(a[5]));
                        Vector3 mid = new Vector3((x1 + x2) / 2, me.transform.position.y, (z1 + z2) / 2);
                        var gone = Physics.OverlapBox(mid, new Vector3((x2 - x1) / 2, 40, (z2 - z1) / 2), Quaternion.identity, ~0, QueryTriggerInteraction.Collide).Select(col => col.GetComponentInParent<ZNetView>())
                            .Where(v => v != null && v.IsValid() && v.GetComponent<Piece>() == null && v.GetComponent<Character>() == null && v.GetComponent<ItemDrop>() == null
                                && (v.GetComponent<TreeBase>() != null || v.GetComponent<TreeLog>() != null || v.GetComponent<Destructible>() != null || v.GetComponent<MineRock>() != null || v.GetComponent<MineRock5>() != null || v.GetComponent<Pickable>() != null || v.GetComponent<Plant>() != null))
                            .Where(v => { Vector3 q = v.transform.position; return q.x >= x1 && q.x <= x2 && q.z >= z1 && q.z <= z2; }).Distinct().ToList();
                        foreach (ZNetView v in gone) { v.ClaimOwnership(); ZNetScene.instance.Destroy(v.gameObject); }
                        output(new JObject { ["cleared"] = gone.Count });
                    }
                    break;
                case "spot":
                    {
                        // Flat dry meadow min..max metres from the pillars, from the world's generator (anywhere, loaded or not; read-only).
                        float lo = F(a[2]), hi = F(a[3]); var found = new JArray(); var rng = new System.Random(7);
                        float H(float x, float z) => WorldGenerator.instance.GetHeight(x, z);
                        for (int i = 0; i < 4000 && found.Count < 6; i++)
                        {
                            double ang = rng.NextDouble() * Math.PI * 2, d = lo + rng.NextDouble() * (hi - lo);
                            float x = c.Position.x + (float)(Math.Sin(ang) * d), z = c.Position.z + (float)(Math.Cos(ang) * d);
                            if (WorldGenerator.instance.GetBiome(x, z) != Heightmap.Biome.Meadows) continue;
                            float y = H(x, z), spread = 0;
                            for (int k = 0; k < 8; k++) { double t = k * Math.PI / 4; spread = Math.Max(spread, Math.Abs(H(x + (float)Math.Sin(t) * 10, z + (float)Math.Cos(t) * 10) - y)); }
                            if (y < ZoneSystem.instance.m_waterLevel + 2 || spread > (a.Length > 4 ? F(a[4]) : 3f)) continue;
                            found.Add($"{x:0},{y:0.0},{z:0} ({d:0} m, spread {spread:0.0})");
                        }
                        output(new JObject { ["spots"] = found });
                    }
                    break;
                case "site":
                    {
                        // Somewhere to build: the ground's lowest and highest over a rectangle, and what stands on it.
                        float x1 = Math.Min(F(a[2]), F(a[4])), x2 = Math.Max(F(a[2]), F(a[4])), z1 = Math.Min(F(a[3]), F(a[5])), z2 = Math.Max(F(a[3]), F(a[5]));
                        float lo = float.MaxValue, hi = float.MinValue;
                        for (float x = x1; x <= x2; x += 1f) for (float z = z1; z <= z2; z += 1f) { float y = ZoneSystem.instance.GetGroundHeight(new Vector3(x, 0, z)); lo = Math.Min(lo, y); hi = Math.Max(hi, y); }
                        Vector3 mid = new Vector3((x1 + x2) / 2, (lo + hi) / 2, (z1 + z2) / 2); Vector3 half = new Vector3((x2 - x1) / 2, 6, (z2 - z1) / 2);
                        var things = Physics.OverlapBox(mid, half, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore).Select(col => col.GetComponentInParent<ZNetView>()).Where(v => v != null && v.GetComponent<Character>() == null).Distinct()
                            .Where(v => { Vector3 q = v.transform.position; return q.x >= x1 && q.x <= x2 && q.z >= z1 && q.z <= z2; })
                            .GroupBy(v => Utils.GetPrefabName(v.gameObject)).ToDictionary(g => g.Key, g => g.Count());
                        output(new JObject { ["low"] = Math.Round(lo, 2), ["high"] = Math.Round(hi, 2), ["things"] = new JObject(things.Select(kv => new JProperty(kv.Key, kv.Value))), ["fromPillars"] = Math.Round(Utils.DistanceXZ(mid, c.Position), 1) });
                    }
                    break;
                case "soil":
                    {
                        Vector3 at = new Vector3(F(a[2]), 0, F(a[3])); at.y = ZoneSystem.instance.GetGroundHeight(at);
                        Heightmap hm = Heightmap.FindHeightmap(at);
                        output(new JObject { ["cultivated"] = hm != null && hm.IsCultivated(at), ["heightmap"] = hm != null ? hm.name : "none", ["y"] = at.y });
                    }
                    break;
                case "roof":
                    {
                        Vector3 at = new Vector3(F(a[2]), F(a[3]), F(a[4]));
                        Cover.GetCoverForPoint(at, out float cover, out bool roof);
                        bool hit = Physics.Raycast(at, Vector3.up, out RaycastHit rh, 30f, LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle"));
                        Room r = c.RoomAt(at);
                        output(new JObject { ["underRoof"] = roof, ["cover"] = cover, ["ray"] = hit ? rh.collider.name + " @" + rh.point.y.ToString("F1") + " tag " + rh.collider.tag : "nothing", ["room"] = r?.Type.ToString(), ["roomFloor"] = r?.Floor });
                    }
                    break;
                case "despawn":
                    {
                        // Test villagers gone without a trace (their rags with them): by name, or "tagged" for every one spawned with tag.
                        int n = 0;
                        foreach (Brain b in Villagers.All.Where(b => a.Length > 2 && (b.S.Name == a[2] || a[2] == "tagged" && b.S.Count("testspawn") > 0)).ToList())
                        {
                            b.Release(); c.ReleaseBed(b.S.Vid);
                            c.R.Roster.RemoveAll(r => r.Vid == b.S.Vid);
                            b.View.Destroy(); n++;
                        }
                        c.R.Arrivals = Math.Max(0, c.R.Arrivals - n); // (test souls don't count toward Odin's next threshold)
                        c.MarkDirty();
                        output(new JObject { ["despawned"] = n });
                    }
                    break;
                case "arrivals": c.R.Arrivals = int.Parse(a[2]); c.MarkDirty(); output(new JObject { ["arrivals"] = c.R.Arrivals, ["threshold"] = Renown.Threshold(c.R.Arrivals) }); break;
                case "stations":
                    output(new JObject { ["stations"] = new JArray(c.PiecesIn(c.Radius).Where(p => p.GetComponent<Smelter>() != null || p.GetComponent<CookingStation>() != null || p.GetComponent<Fireplace>() != null).Select(p =>
                    {
                        ZDO z = p.GetComponent<ZNetView>().GetZDO(); Smelter sm = p.GetComponent<Smelter>();
                        return $"{Utils.GetPrefabName(p.gameObject)} at {p.transform.position:F0}: fuel {z.GetFloat(ZDOVars.s_fuel):0.#}" + (sm != null ? $", ore queued {Work.QueueSize(sm)}, processed {z.GetInt(ZDOVars.s_spawnAmount)}" : "");
                    })) });
                    break;
                case "perf":
                    {
                        Plugin.CostMs = 0; Plugin.ThinkMs = 0; Plugin.CostFrames = 0;
                        output(new JObject { ["note"] = "counters reset: run jarldom perf2 after a few seconds" });
                    }
                    break;
                case "perf2":
                    output(new JObject { ["frames"] = Plugin.CostFrames, ["updateMsPerFrame"] = Math.Round(Plugin.CostMs / Math.Max(1, Plugin.CostFrames), 3), ["thinkMsPerFrame"] = Math.Round(Plugin.ThinkMs / Math.Max(1, Plugin.CostFrames), 3), ["villagers"] = Villagers.All.Count(), ["fps"] = Math.Round(1f / Time.smoothDeltaTime) });
                    break;
                case "drop":
                    {
                        // Put things from a villager's bag back on the ground where they stand (prefab names, or "all-but-own").
                        Brain b = Who(a[2]); if (b == null) { error("who?"); return; }
                        var names = new HashSet<string>(a.Skip(3));
                        Inventory bag = b.Body.GetInventory(); int n = 0;
                        foreach (ItemDrop.ItemData item in bag.GetAllItems().Where(i => !i.m_equipped && names.Contains(Prefab(i))).ToList())
                        { ItemDrop.DropItem(item, item.m_stack, b.Pos + Vector3.up + UnityEngine.Random.insideUnitSphere * 0.4f, Quaternion.identity); bag.RemoveItem(item); n++; }
                        output(new JObject { ["dropped"] = n, ["at"] = V(b.Pos) });
                    }
                    break;
                case "unlose":
                    {
                        // Test mishaps only: one lost to Hel brought back with their id, name, trade and place on the roster (their state
                        // was lost with them, so traits and story are new). Their draugr goes; their tombstone is still theirs.
                        RosterRow row = c.R.Roster.FirstOrDefault(r => r.Vid == a[2] || r.Name.Equals(a[2], StringComparison.OrdinalIgnoreCase));
                        if (row == null || row.State != SoulState.Lost) { error("no one lost by that name or id"); return; }
                        Newcomer n = Souls.Make(row.Vid.GetStableHashCode(), "restored", null); n.Name = row.Name; n.Female = row.Female; n.Prof = row.Prof;
                        VillagerState st = Souls.Become(n, row.Vid, c.Id, c.Now); st.Prof = row.Prof; st.Worth = row.Worth;
                        foreach (Character d in Character.GetAllCharacters().Where(x => x != null && !x.IsDead() && x.GetComponent<ZNetView>()?.GetZDO()?.GetString(Draugr.NameKey, "") == "Draugr " + row.Name.Split(' ')[0]).ToList()) d.GetComponent<ZNetView>().Destroy();
                        Vector3 at = Colony.Spot(c.Position + c.Seat.transform.forward * 3f, 2.5f) ?? c.Position + Vector3.up;
                        Humanoid h = Villagers.Spawn(c, st, at, null, dressNew: true);
                        if (h == null) { error("couldn't make them"); return; }
                        row.State = SoulState.Active; c.R.Deeds -= Renown.Deed("lost"); c.Log("text", row.Name, "", $"Hel let {row.Name} go."); c.MarkDirty(); // (the loss's deed undone too)
                        output(new JObject { ["back"] = row.Name, ["traits"] = string.Join(",", st.Traits), ["at"] = V(at) });
                    }
                    break;
                case "traderun": { Brain b = Who(a[2]); if (b == null) { error("who?"); return; } TradeWay? way = a.Length > 3 ? Parse<TradeWay>(a[3]) : (TradeWay?)null; b.Task?.Stop(b); b.Task = new TradeTask(way); b.Task.Start(b); output(new JObject { ["doing"] = b.Task.Doing, ["done"] = b.Task.Done, ["karve"] = Trade.Ship(c, false) != null ? V(Trade.Ship(c, false).transform.position) : null, ["longship"] = Trade.Ship(c, true) != null ? V(Trade.Ship(c, true).transform.position) : null, ["plan"] = new JArray(Trade.Plan(c)) }); } break; // (test: traderun <who> [Foot|Cart|Karve|Longship])
                case "fx": { Vector3 at = me.transform.position + me.transform.forward * 4; Assets.Effect(a[2], at + Vector3.up * (a.Length > 3 ? F(a[3]) : 0)); output(new JObject { ["fx"] = a[2], ["found"] = Assets.Find(a[2]) != null }); } break; // (test: an effect 4 m ahead)
                case "dumpmesh":
                    {
                        // Test: a prefab's meshes (most detailed LOD), in its own frame, to a JSON file (for carving in Blender): dumpmesh <prefab> <file>
                        GameObject src = Assets.Find(a[2]); if (src == null) { error("no prefab " + a[2]); return; }
                        var lod0 = new HashSet<Renderer>(src.GetComponentsInChildren<LODGroup>(true).SelectMany(g => g.GetLODs().Take(1)).SelectMany(l => l.renderers).Where(r => r != null));
                        var lodded = new HashSet<Renderer>(src.GetComponentsInChildren<LODGroup>(true).SelectMany(g => g.GetLODs()).SelectMany(l => l.renderers).Where(r => r != null));
                        var parts = new JArray(); var info = new JArray();
                        foreach (MeshRenderer r in src.GetComponentsInChildren<MeshRenderer>(true))
                        {
                            MeshFilter f = r.GetComponent<MeshFilter>(); Mesh m = f?.sharedMesh;
                            if (m == null || lodded.Contains(r) && !lod0.Contains(r)) continue;
                            info.Add($"{r.name} mesh {m.name} readable {m.isReadable} verts {m.vertexCount} subs {m.subMeshCount} mats {string.Join(",", r.sharedMaterials.Select(x => x?.name + "/" + x?.shader?.name))} active {r.gameObject.activeInHierarchy}");
                            if (!m.isReadable) continue;
                            Matrix4x4 toRoot = src.transform.worldToLocalMatrix * r.transform.localToWorldMatrix;
                            var v = new JArray(); foreach (Vector3 p3 in m.vertices) { Vector3 q = toRoot.MultiplyPoint3x4(p3); v.Add(q.x); v.Add(q.y); v.Add(q.z); }
                            var n = new JArray(); foreach (Vector3 p3 in m.normals) { Vector3 q = toRoot.MultiplyVector(p3).normalized; n.Add(q.x); n.Add(q.y); n.Add(q.z); }
                            var uv = new JArray(); foreach (Vector2 p2 in m.uv) { uv.Add(p2.x); uv.Add(p2.y); }
                            var subs = new JArray(); for (int k = 0; k < m.subMeshCount; k++) subs.Add(new JArray(m.GetTriangles(k)));
                            parts.Add(new JObject { ["name"] = r.name, ["v"] = v, ["n"] = n, ["uv"] = uv, ["subs"] = subs, ["mats"] = new JArray(r.sharedMaterials.Select(x => x?.name)) });
                        }
                        if (a.Length > 3) System.IO.File.WriteAllText(a[3], new JObject { ["prefab"] = a[2], ["parts"] = parts }.ToString(Newtonsoft.Json.Formatting.None));
                        output(new JObject { ["parts"] = info, ["written"] = parts.Count });
                    }
                    break;
                case "carvedebug": { var res = new JArray(); foreach (MeshFilter f in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(f => f.name.EndsWith("_odin") || f.name.EndsWith("_thor")).Where(f => f.gameObject.activeInHierarchy).Take(6)) { var r = f.GetComponent<MeshRenderer>(); res.Add($"{f.name} idx {(f.sharedMesh != null ? string.Join("/", Enumerable.Range(0, f.sharedMesh.subMeshCount).Select(k => f.sharedMesh.GetIndexCount(k))) : "")} enabled {r?.enabled} visible {r?.isVisible} readable {f.sharedMesh?.isReadable} active {f.gameObject.activeInHierarchy} pos {V(f.transform.position)} scale {f.transform.lossyScale} verts {f.sharedMesh?.vertexCount} subs {f.sharedMesh?.subMeshCount} bounds {f.sharedMesh?.bounds} rb {r?.bounds} mats {string.Join(",", r?.sharedMaterials.Select(m => m?.name + "/" + m?.shader?.name))} layer {f.gameObject.layer}"); } output(new JObject { ["carved"] = res }); } break;
                case "setfuel": { Vector3 at = new Vector3(F(a[2]), 0, F(a[3])); Fireplace f = c.PiecesIn<Fireplace>(c.Radius + 20).OrderBy(x => Utils.DistanceXZ(x.transform.position, at)).FirstOrDefault(); ZNetView v = f?.GetComponent<ZNetView>(); if (v == null) { error("no fire"); return; } v.ClaimOwnership(); v.GetZDO().Set(ZDOVars.s_fuel, F(a[4])); output(new JObject { ["fire"] = V(f.transform.position), ["fuel"] = F(a[4]), ["max"] = f.m_maxFuel }); } break; // (test: setfuel <x> <z> <n>)
                case "animq": { Brain b = Who(a[2]); RosterRow row = b == null ? c.R.Roster.FirstOrDefault(r => r.Name.Equals(a[2], StringComparison.OrdinalIgnoreCase)) : null; Humanoid body = b?.Body ?? (row != null ? Villagers.Body(row.Vid) : null); if (body == null) { error("who?"); return; } Animator an = body.GetComponentInChildren<Animator>(); var st = an.GetCurrentAnimatorStateInfo(0); output(new JObject { ["speed"] = an.speed, ["kneelMark"] = body.GetComponent<ZNetView>().GetZDO().GetLong(Kneels.Key, 0L) > 0, ["emoteTag"] = st.IsTag("emote"), ["state"] = st.shortNameHash, ["inEmote"] = body.InEmote(), ["doing"] = b?.Doing ?? "(another game runs them)", ["at"] = V(body.transform.position) }); } break; // (test: is the animator in an emote)
                case "kneel": { Brain b = Who(a[2]); if (b == null) { error("who?"); return; } b.Task?.Stop(b); b.Task = new KneelTask(Time.time + (a.Length > 3 ? F(a[3]) : 15f), b.Pos + b.Body.transform.forward); b.Task.Start(b); output(new JObject { ["at"] = V(b.Pos), ["facing"] = V(b.Body.transform.forward) }); } break; // (test: kneel <name> [seconds])
                case "tier":
                    {
                        HighSeat.TestTier = int.Parse(a[2]);
                        GameObject pf = Pieces.Get(Pieces.PillarsName); Transform lk = pf?.transform.Find("new");
                        var sizes = new JArray(); for (int i = 0; lk != null && i < Pieces.Tiers; i++) { Transform tr = lk.Find("tier" + i); if (tr == null) continue; Bounds b = Assets.MeshBounds(pf.transform, tr.gameObject); sizes.Add($"tier{i}: x {b.min.x:0.00}..{b.max.x:0.00} y {b.min.y:0.00}..{b.max.y:0.00} z {b.min.z:0.00}..{b.max.z:0.00}"); }
                        foreach (BoxCollider bc in pf?.GetComponentsInChildren<BoxCollider>() ?? new BoxCollider[0]) sizes.Add($"{bc.name}: centre {bc.center} size {bc.size} trigger {bc.isTrigger}");
                        output(new JObject { ["tier"] = HighSeat.TestTier, ["bounds"] = sizes });
                    }
                    break; // (test: the pillars' look of a rank; -1 back to the colony's)
                case "unlog": { int day = a.Length > 2 && System.Text.RegularExpressions.Regex.IsMatch(a[2], "^d\\d+$") ? int.Parse(a[2].Substring(1)) : -1; string start = string.Join(" ", a.Skip(day >= 0 ? 3 : 2)); int n = c.R.Log.RemoveAll(e => (day < 0 || e.Day == day) && e.Text.StartsWith(start) && start.Length > 8); if (n > 0) { c.R.LogChanges++; c.MarkDirty(); c.Flush(); } output(new JObject { ["removed"] = n }); } break; // (test entries out of the saga: unlog [dN] <text start>)
                case "relog": { int day = int.Parse(a[2].Substring(1)); LogEntry after = c.R.Log.LastOrDefault(e => e.Day == day && e.Kind == a[3]); if (after == null) { error("no " + a[3] + " on day " + day); return; } c.R.Log.Insert(c.R.Log.IndexOf(after) + 1, new LogEntry(after.Time + 0.001, day, "text", "", "", string.Join(" ", a.Skip(4)))); c.R.LogChanges++; c.MarkDirty(); c.Flush(); output(new JObject { ["put"] = true }); } break; // (an entry put back: relog dN <after kind> <text>)
                case "forgetrow": { int n = c.R.Roster.RemoveAll(r => a.Skip(2).Contains(r.Vid)); c.MarkDirty(); output(new JObject { ["forgot"] = n }); } break; // (test souls off the roster)
                case "raidwhy":
                    {
                        RandomEvent ev = RandEventSystem.instance?.GetCurrentRandomEvent();
                        output(new JObject { ["raid"] = c.RaidOn, ["event"] = ev == null ? "" : ev.m_name + " at " + V(ev.m_pos), ["siege"] = Compat.Siege(c.Position, c.Radius, out Vector3 st), ["band"] = Warband.Raiding(c),
                            ["restless"] = new JArray(Character.GetAllCharacters().Where(ch => ch != null && !ch.IsDead() && Utils.DistanceXZ(ch.transform.position, c.Position) < c.Radius + 10 && (ch.GetComponent<ZNetView>()?.GetZDO()?.GetString(Draugr.NameKey, "") ?? "").StartsWith("Draugr ")).Select(ch => ch.GetHoverName() + " at " + V(ch.transform.position) + " hp " + ch.GetHealth())) });
                    }
                    break;
                case "plants": output(new JObject { ["plants"] = new JArray(c.PiecesIn(c.Radius).Select(pc => pc.GetComponent<Plant>()).Where(pl => pl != null && Utils.GetPrefabName(pl.gameObject) == a[2]).Select(pl => $"{V(pl.transform.position)} {pl.GetStatus()}")) }); break;
                case "sowwhy":
                    {
                        Work.LearnCrops();
                        var seeds = Work.Replant.Select(kv => $"{kv.Key} <- {kv.Value.sapling.name} ({kv.Value.seed} x{kv.Value.amount}) stock {Crafts.InStock(c, kv.Value.seed)} grows here {Weaving.GrowsHere(kv.Value.sapling, c.Position)} biome {kv.Value.sapling.GetComponent<Plant>()?.m_biome}");
                        output(new JObject { ["replant"] = new JArray(seeds), ["sowOrders"] = c.Board.All.Count(o => o.Kind == "sow" && (o.State == OrderState.Open || o.State == OrderState.Claimed)) });
                    }
                    break;
                case "leisure": { Brain b = Who(a[2]); if (b == null) { error("who?"); return; } b.Task?.Stop(b); b.Task = new LeisureTask(a[3], "test"); b.Task.Start(b); output(new JObject { ["doing"] = b.Task.Doing }); } break;
                case "deeds": c.R.Deeds += int.Parse(a[2]); c.MarkDirty(); output(new JObject { ["deeds"] = c.R.Deeds }); break; // (test mishaps undone)
                case "hildir": if (a.Length > 2) Weaving.TestHildir = a[2] == "on"; output(new JObject { ["known"] = Weaving.HildirKnown(), ["keys"] = new JArray(new[] { "Hildir1", "Hildir2", "Hildir3" }.Where(k => ZoneSystem.instance.GetGlobalKey(k))) }); break;
                case "claims":
                    {
                        // Test: claims (this colony's) | claims survey (the nearest stake, now) | claims send <name> (that miner sets out).
                        string sub = a.Length > 2 ? a[2] : "";
                        if (sub == "elder") { Claims.TestElder = !Claims.TestElder; output(new JObject { ["testElder"] = Claims.TestElder }); }
                        if (sub == "survey") { OreClaim st = UnityEngine.Object.FindObjectsByType<OreClaim>(FindObjectsSortMode.None).OrderBy(x => Vector3.Distance(x.transform.position, Player.m_localPlayer.transform.position)).FirstOrDefault(); if (st == null) { error("no stake loaded"); return; } st.GetComponent<ZNetView>().ClaimOwnership(); Claim got = st.Survey(); Vector3 sp = st.transform.position;
                            output(new JObject { ["stake"] = $"{sp.x:0.0},{sp.z:0.0}", ["id"] = st.Id, ["survey"] = got == null ? "nothing" : Claims.Ores(got), ["ready"] = new JArray(new[] { Vector3.zero, Vector3.forward, Vector3.back, Vector3.left, Vector3.right }.Select(d => ZNetScene.instance.IsAreaReady(sp + d * Mining.CryptReach))),
                                ["near"] = new JArray(Claims.Near(sp, Mining.BareReach, false).Select(d => $"{Utils.GetPrefabName(d.go)} {d.ore} {d.n} t{d.tier}")), ["home"] = OreClaim.HomeOf(sp) }); }
                        if (sub == "send") { Brain b = Who(a[3]); if (b == null) { error("who?"); return; } b.NoClaimUntil = 0; Task t = a.Length > 4 && Claims.Get(a[4]) is Claim pick ? new ClaimTrip(pick) : Claims.Next(b); if (t == null) { var pk = Gear.Tool(b, "pickaxe"); error($"no claim for them: {Gear.Problem} (owner {c?.IsOwner}, colony {b.Colony?.Id}, claims {Claims.Of(b.Colony).Count()}, with ore {Claims.Of(b.Colony).Count(x => x.Ore > 0)}, pickaxe {pk?.m_shared.m_name} tier {pk?.m_shared.m_toolTier}, tiers {string.Join(",", Claims.Of(b.Colony).Select(x => x.Tier))}, out {string.Join(",", Claims.Of(b.Colony).Select(x => Claims.Out(b.Colony, x.Id)))})"); return; } b.Task?.Stop(b); b.Task = t; t.Start(b); }
                        if (c != null) output(new JObject { ["stores"] = new JObject(new[] { "copperore", "tinore", "ironscrap", "silverore" }.Select(o => new JProperty(o, $"{Claims.Held(c, o)} held, need {Claims.Need(c, o):0.00}"))), ["keep"] = Stock.Keep(c, "ore") });
                        output(new JObject { ["claims"] = new JArray(Claims.All().Select(x => $"{x.Id} for {x.Colony}: {Claims.Title(x)} at {x.X:0},{x.Z:0} {x.Biome} beds {x.Beds} crypt {x.Crypt} left [{Claims.Ores(x)}] taken [{string.Join(";", x.Taken.Select(kv => kv.Key + ":" + kv.Value))}] out {(c != null ? Claims.Out(c, x.Id) : 0)}")) });
                        break;
                    }
                case "fires": output(new JObject { ["fires"] = new JArray(c.PiecesIn(c.Radius).Select(pc => pc.GetComponent<Fireplace>()).Where(f => f != null).Select(f => $"{Utils.GetPrefabName(f.gameObject)} at {f.transform.position.x:0},{f.transform.position.z:0}: warms {Classes.Warms(f)}, lit {Classes.Lit(f)}, areas {string.Join("/", f.GetComponentsInChildren<EffectArea>(true).Select(e => e.m_type + (e.gameObject.activeInHierarchy ? "" : "(off)")))}, fuel {f.GetComponent<ZNetView>().GetZDO().GetFloat(ZDOVars.s_fuel):0.0}/{f.m_maxFuel} {f.m_fuelItem?.name}, by {f.GetComponent<Piece>()?.GetCreator()}, fed {c.Feeds(f.GetComponent<Piece>())}")), ["founder"] = c.R.Founder, ["me"] = Player.m_localPlayer?.GetPlayerID() ?? 0, ["players"] = new JArray(Player.GetAllPlayers().Select(x => x.GetPlayerName() + " " + x.GetPlayerID())), ["allowed"] = new JArray(c.R.Allowed) }); break;
                case "upkeep": output(new JObject { ["ruins"] = new JArray(Upkeep.Ruins(c).Select(r => $"{r.Prefab} at {r.At.x:0.0},{r.At.y:0.0},{r.At.z:0.0}")), ["hurt"] = new JArray(Upkeep.Hurt(c).Select(w => $"{Utils.GetPrefabName(w.gameObject)} at {w.transform.position.x:0},{w.transform.position.z:0}")), ["wornPaths"] = Upkeep.Worn(c), ["busiest"] = Upkeep.Busiest(c) }); break;
                case "wreck":
                    {
                        // Test only: the nearest piece to x z hurt to pct% (or broken, at 0), as a monster's blow would (no player's hand).
                        Vector3 at = new Vector3(F(a[2]), 0, F(a[3])); float pct = a.Length > 4 ? F(a[4]) : 0;
                        WearNTear w = c.PiecesIn(c.Radius + 30).Select(pc => pc.GetComponent<WearNTear>()).Where(x => x != null).OrderBy(x => Utils.DistanceXZ(x.transform.position, at)).FirstOrDefault();
                        if (w == null) { error("no piece there"); return; }
                        ZNetView wv = w.GetComponent<ZNetView>(); wv.ClaimOwnership();
                        string what = Utils.GetPrefabName(w.gameObject);
                        if (pct <= 0) { var hit = new HitData(); hit.m_damage.m_blunt = 99999; hit.m_point = w.transform.position; hit.m_toolTier = 99; w.Damage(hit); }
                        else wv.GetZDO().Set(ZDOVars.s_health, w.m_health * pct / 100f);
                        output(new JObject { ["piece"] = what, ["at"] = V(w.transform.position), ["health"] = pct });
                    }
                    break;
                case "seats":
                    {
                        Room hall = c.Best(RoomType.GreatHall);
                        var list = new List<Piece>(); Piece.GetAllPiecesInRadius(c.Centre(hall), 14, list);
                        output(new JObject { ["hall"] = Box(hall.Xz), ["pieces"] = new JArray(list.Where(pc => Utils.GetPrefabName(pc.gameObject).Contains("bench") || Utils.GetPrefabName(pc.gameObject).Contains("chair") || Utils.GetPrefabName(pc.gameObject).Contains("throne") || Utils.GetPrefabName(pc.gameObject).Contains("stool") || pc.GetComponent<Chair>() != null)
                            .Select(pc => $"{Utils.GetPrefabName(pc.gameObject)} at {pc.transform.position.x:0.0},{pc.transform.position.y:0.0},{pc.transform.position.z:0.0}: chairs {pc.GetComponentsInChildren<Chair>(true).Length}, in hall {Geometry.Inside(hall.Xz, pc.transform.position.x, pc.transform.position.z)}, free {pc.GetComponentsInChildren<Chair>(true).Count(Seats.IsFree)}")) });
                    }
                    break;
                case "skaltest":
                    {
                        BaseUnityPlugin skal = Compat.Find(Compat.Known.First(k => k.name == "SkalTavern").guids);
                        MethodInfo raise = skal == null ? null : HarmonyLib.AccessTools.Method(skal.GetType().Assembly.GetType("SkalTavern.Toast"), "Raise");
                        if (raise == null) { error("no SkalTavern toast"); return; }
                        bool hooked = HarmonyLib.Harmony.GetPatchInfo(raise)?.Postfixes.Any(px => px.owner == "com.bobisme.jarldom.skal") == true;
                        if (a.Length > 2 && a[2] == "hall") { Skal.Answer(c.Centre(c.Best(RoomType.GreatHall)), me.GetPlayerName()); output(new JObject { ["hooked"] = hooked, ["answered"] = "at the hall" }); break; }
                        raise.Invoke(null, new object[] { me });
                        output(new JObject { ["hooked"] = hooked, ["toasted"] = true, ["near"] = Villagers.All.Count(v => Vector3.Distance(v.Pos, me.transform.position) < 12f) });
                    }
                    break;
                case "makeown": { Brain b = Who(a[2]); if (b == null) { error("who?"); return; } if (!MakeOwn.Can(c, a[3], b.Pos)) { error("can't make " + a[3] + " here (station, stores)"); return; } b.Task?.Stop(b); b.Task = new MakeOwn(a[3]); b.Task.Start(b); output(new JObject { ["doing"] = b.Task.Doing }); } break;
                case "slay":
                    {
                        // Test only: every creature of a prefab in the colony (and 30 m round) dies.
                        int n = 0;
                        foreach (Character ch in Character.GetAllCharacters().Where(x => x != null && !x.IsDead() && !x.IsPlayer() && Utils.GetPrefabName(x.gameObject) == a[2] && Utils.DistanceXZ(x.transform.position, c.Position) < c.Radius + 30).ToList())
                        { ZNetView v = ch.GetComponent<ZNetView>(); v.ClaimOwnership(); var hit = new HitData(); hit.m_damage.m_damage = 99999; hit.m_point = ch.GetCenterPoint(); ch.Damage(hit); if (!ch.IsDead() && ch.GetHealth() > 0) v.Destroy(); n++; }
                        output(new JObject { ["slain"] = n });
                    }
                    break;
                case "creature":
                    {
                        GameObject prefab = ZNetScene.instance.GetPrefab(a[2]);
                        if (prefab == null || prefab.GetComponent<Character>() == null) { error("no creature " + a[2]); return; }
                        Vector3 at = new Vector3(F(a[3]), 0, F(a[4])); at.y = Ground(at) + 0.5f;
                        GameObject go = Object.Instantiate(prefab, at, Quaternion.identity);
                        if (a.Length > 5 && int.TryParse(a[5], out int lvl)) go.GetComponent<Character>().SetLevel(lvl);
                        if (a.Contains("tamed")) go.GetComponent<Character>().SetTamed(true);
                        output(new JObject { ["spawned"] = a[2], ["at"] = V(at), ["tamed"] = go.GetComponent<Character>().IsTamed() });
                    }
                    break;
                case "ground": { Vector3 at = new Vector3(F(a[2]), 0, F(a[3])); output(new JObject { ["ground"] = Ground(at) }); } break;
                case "touch": { Brain b = Who(a[2]); if (b == null) { error("who?"); return; } if (a.Length > 3 && a[3] == "-") b.S.Counts.Remove(Saboteur.Mark); else b.S.Bump(Saboteur.Mark); b.Dirty(); output(new JObject { ["touched"] = b.S.Name, ["is"] = Saboteur.Touched(b.S) }); } break; // (test: touch <name> [-])
                case "touchedwho": output(new JObject { ["count"] = Villagers.All.Count(x => x.S.Colony == c.Id && Saboteur.Touched(x.S)), ["vids"] = new JArray(Villagers.All.Where(x => x.S.Colony == c.Id && Saboteur.Touched(x.S)).Select(x => x.S.Vid)) }); break; // (test: vids only, not names)
                case "mischief": { Brain b = Who(a[2]); if (b == null) { error("who?"); return; } b.NextMischief = 1; Saboteur.Tick(b); output(new JObject { ["mischief"] = b.S.Count("mischief"), ["seen"] = new JObject(Villagers.All.Where(x => x.S.Count("seen") > 0).Select(x => new JProperty(x.S.Name, x.S.Count("seen")))) }); } break;
                case "away": { double h = a.Length > 2 ? F(a[2]) : 24; CatchUp.Run(c, h * Clock.Hour); output(new JObject { ["log"] = c.R.Log.Last().Text }); } break;
                case "find":
                    {
                        // Test: find location <name part> | find biome <Biome>: the nearest few to the player (or the colony).
                        Vector3 from = Player.m_localPlayer?.transform.position ?? c?.Position ?? Vector3.zero;
                        if (a.Length > 3 && a[2] == "location")
                            output(new JObject { ["found"] = new JArray(ZoneSystem.instance.m_locationInstances.Values.Where(l => l.m_location?.m_prefabName != null && l.m_location.m_prefabName.IndexOf(a[3], StringComparison.OrdinalIgnoreCase) >= 0)
                                .OrderBy(l => Utils.DistanceXZ(l.m_position, from)).Take(5).Select(l => $"{l.m_location.m_prefabName} at {l.m_position.x:0},{l.m_position.y:0},{l.m_position.z:0} ({Utils.DistanceXZ(l.m_position, from):0} m, {(l.m_placed ? "generated" : "not yet generated")})")) });
                        else if (a.Length > 3 && a[2] == "biome" && Enum.TryParse(a[3], true, out Heightmap.Biome want))
                        {
                            var hits = new JArray();
                            for (float r = 64; r < 4000 && hits.Count < 3; r += 64)
                                for (int k = 0; k < 24 && hits.Count < 3; k++)
                                {
                                    float ang = k * Mathf.PI * 2 / 24; Vector3 p = from + new Vector3(Mathf.Cos(ang) * r, 0, Mathf.Sin(ang) * r);
                                    if (WorldGenerator.instance.GetBiome(p) == want) hits.Add($"{p.x:0},{p.z:0} ({r:0} m, height {WorldGenerator.instance.GetHeight(p.x, p.z):0})");
                                }
                            output(new JObject { ["found"] = hits });
                        }
                        else error("find location <name> | find biome <Biome>");
                        break;
                    }
                case "profile":
                    {
                        // Test: Jarldom's entries in the local profile's known world keys (and prune them).
                        PlayerProfile pr = Game.instance?.GetPlayerProfile();
                        int had = pr?.m_playerStats?.Sum(st => st.m_knownWorldKeys.Keys.Count(k => k.StartsWith(ProfileKeys.Prefix))) ?? -1;
                        int total = pr?.m_playerStats?.Sum(st => st.m_knownWorldKeys.Count) ?? -1;
                        int gone = a.Length > 2 && a[2] == "prune" ? ProfileKeys.Prune(pr) : 0;
                        output(new JObject { ["jarldomEntries"] = had, ["allEntries"] = total, ["pruned"] = gone });
                        break;
                    }
                case "versions":
                    {
                        // Test: versions heard; "versions fake <data>" pretends a player on that data version spoke (0: forget).
                        if (a.Length > 3 && a[2] == "fake") { if (a[3] == "0") Versions.Forget(); else Versions.Heard(424242, $"9.9.9|{a[3]}|Tester"); }
                        output(new JObject { ["mine"] = $"{Plugin.Version} data {Plugin.DataVersion}", ["mismatch"] = Versions.Mismatch, ["who"] = Versions.Who, ["recordRevision"] = c?.Z?.DataRevision ?? 0, ["heard"] = new JArray(Versions.All.Select(v => $"{v.sender}: {v.name} {v.version} data {v.data}, {v.ago:0}s ago")), ["peers"] = new JArray((ZNet.instance?.GetPeers() ?? new List<ZNetPeer>()).Select(q => $"{q.m_uid}: {q.m_playerName}{(q.m_server ? " (server)" : "")}")), ["me"] = ZDOMan.GetSessionID(), ["colonyOwner"] = c?.Z?.GetOwner() ?? 0 });
                        break;
                    }
                case "roomfind":
                    {
                        // Test: the room found from a spot on its floor (as Ctrl+Shift+click with the marker), without marking it.
                        var f = RoomFind.Find(new Vector3(F(a[2]), F(a[3]), F(a[4])));
                        output(new JObject { ["found"] = f.Xz != null, ["why"] = f.Why, ["doors"] = f.Doors, ["area"] = f.Xz != null ? Math.Round(Geometry.Area(f.Xz), 1) : 0, ["corners"] = f.Xz != null ? f.Xz.Count / 2 : 0,
                            ["outline"] = f.Xz != null ? string.Join(" ", Enumerable.Range(0, f.Xz.Count / 2).Select(k => $"{f.Xz[2 * k]:0.#},{f.Xz[2 * k + 1]:0.#}")) : "", ["leak"] = f.Leak != null ? V(f.Leak.Value) : null });
                        break;
                    }
                case "icon":
                    {
                        // Test: draw a prefab's icon as the mod does, with the scene's light kept out (1) or not (0), to a PNG.
                        GameObject src = ZNetScene.instance.GetPrefab(a.Length > 2 ? a[2] : Pieces.PillarsName);
                        if (src == null) { error("no such prefab"); break; }
                        if (a.Length > 3 && a[3] == "current")
                        {
                            // (what the hammer shows now)
                            Sprite cur = src.GetComponent<Piece>()?.m_icon ?? src.GetComponent<ItemDrop>()?.m_itemData.GetIcon();
                            if (cur == null || !cur.texture.isReadable) { error("no readable icon"); break; }
                            string f = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "claude", "shots", $"icon-{src.name}-current.png");
                            System.IO.File.WriteAllBytes(f, ImageConversion.EncodeToPNG(cur.texture)); output(new JObject { ["icon"] = f }); break;
                        }
                        Assets.IconLightKept = a.Length <= 3 || a[3] != "0";
                        Sprite sp = Assets.Icon(src, "test", Quaternion.Euler(0, -30, 0));
                        Assets.IconLightKept = true;
                        if (sp == null) { error("no icon"); break; }
                        string file = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "claude", "shots", $"icon-{src.name}-{(a.Length > 3 ? a[3] : "1")}.png");
                        System.IO.File.WriteAllBytes(file, ImageConversion.EncodeToPNG(sp.texture));
                        output(new JObject { ["icon"] = file, ["fog"] = RenderSettings.fog, ["ambient"] = RenderSettings.ambientLight.ToString(), ["mode"] = RenderSettings.ambientMode.ToString() });
                        break;
                    }
                case "zonescan":
                    {
                        // Test: what each work zone's scan sees (as Work.Scan looks), and why each thing is or isn't work.
                        var res = new JArray();
                        foreach (Zone z in c.R.Zones)
                        {
                            Vector3 centre = Zones.Centre(z); float reach = Zones.Reach(z) + 2, scan = reach + Zones.FuzzReach(z);
                            var things = Work.Things(centre, scan, beasts: z.Type == ZoneType.Hunting).ToList();
                            var beasts = things.Select(go => go.GetComponent<Character>()).Where(ch => ch != null).Select(ch => $"{Utils.GetPrefabName(ch.gameObject)} {ch.GetFaction()} works={Zones.Works(z, ch.transform.position, false, Mathf.FloorToInt(ch.transform.position.x * 4) * 73856093 ^ Mathf.FloorToInt(ch.transform.position.z * 4) * 19349663)} dead={ch.IsDead()} tame={ch.IsTamed()} at {ch.transform.position.x:0},{ch.transform.position.z:0}").ToList();
                            res.Add(new JObject { ["zone"] = z.Type.ToString(), ["centre"] = V(centre), ["reach"] = reach, ["scan"] = scan, ["things"] = things.Count, ["beasts"] = new JArray(beasts) });
                        }
                        output(new JObject { ["zones"] = res }); break;
                    }
                case "layers":
                    {
                        // Test: the physics layers of prefabs' colliders (what a zone's scan sees).
                        var res = new JObject();
                        foreach (string name in a.Skip(2))
                        {
                            GameObject pf = ZNetScene.instance.GetPrefab(name);
                            res[name] = pf == null ? "no such prefab" : string.Join(",", pf.GetComponentsInChildren<Collider>(true).Select(col => LayerMask.LayerToName(col.gameObject.layer) + (col.isTrigger ? "(trigger)" : "")).Distinct());
                        }
                        output(res); break;
                    }
                case "overlay":
                    {
                        // Test: what's drawn for the hammer, and leftover overlays of other copies.
                        output(new JObject { ["overlay"] = Overlay_.State() });
                        break;
                    }
                case "env":
                    {
                        // Test: hold the weather (a name like ThunderStorm, Rain, Clear) or let it go (off).
                        string e = a.Length > 2 ? a[2] : "off";
                        EnvMan.instance.m_debugEnv = e == "off" ? "" : e;
                        output(new JObject { ["env"] = EnvMan.instance.m_debugEnv, ["now"] = EnvMan.instance.GetCurrentEnvironment()?.m_name });
                        break;
                    }
                case "fuel":
                    {
                        // Test: the fires within r of x z set to n fuel (0: out).
                        Vector3 at = new Vector3(F(a[2]), 0, F(a[3])); float r = F(a[4]); float n = F(a[5]); int done = 0;
                        foreach (Fireplace f in c.PiecesIn(c.Radius + 20).Select(pc => pc.GetComponent<Fireplace>()).Where(f => f != null && Utils.DistanceXZ(f.transform.position, at) <= r))
                        { ZNetView fv = f.GetComponent<ZNetView>(); if (!fv.IsOwner()) fv.ClaimOwnership(); fv.GetZDO().Set(ZDOVars.s_fuel, n); done++; }
                        output(new JObject { ["fires"] = done });
                        break;
                    }
                case "fallen": output(new JObject { ["now"] = c.Now, ["respawn"] = Plugin.Instance.RespawnSeconds.Value, ["fallen"] = new JArray(c.R.Fallen.Select(f => $"{c.R.Row(f.Vid)?.Name ?? f.Vid}: {f.Killer}, back in {f.At + Plugin.Instance.RespawnSeconds.Value - c.Now:0} s")) }); break;
                case "rates":
                    output(new JObject(Enum.GetValues(typeof(Prof)).Cast<Prof>().Select(p => new JProperty(p.ToString(), new JObject(new JProperty("hours", c.R.Flag("worked:" + p) / 10f), new JProperty("perHour", new JObject(CatchUp.Rates(c, p).Select(kv => new JProperty(kv.Key, Math.Round(kv.Value, 2))))))))));
                    break;
                case "master": { Skills.SkillType sk = a.Length > 2 && Enum.TryParse(a[2], true, out Skills.SkillType s2) ? s2 : Skills.SkillType.Swords; c.R.Flags["master:skill"] = (int)sk; c.R.Flags["master:until"] = c.Day; c.MarkDirty(); output(new JObject { ["master"] = sk.ToString() }); } break;
                case "envoy": c.R.Flags["merc:offer"] = c.Day; c.MarkDirty(); output(new JObject { ["envoy"] = c.Day }); break;
                case "capture": { Brain b = Who(a[2]); if (b == null) { error("who?"); return; } bool took = Captives.Take(b, true); output(new JObject { ["captured"] = took, ["held"] = new JArray(Captives.Held(c).Select(h => $"{h.name} at {h.at.x:0},{h.at.z:0}, {h.daysLeft} days")) }); } break;
                case "barrow":
                    if (a.Length > 2 && a[2] == "raise") Warband.Raise(c, c.Day);
                    else if (a.Length > 2 && a[2] == "band") { c.R.Flags["barrow:next"] = c.Day; Warband.Dusk(c, c.Day); }
                    else if (a.Length > 2 && a[2] == "clear") { foreach (string k in c.R.Flags.Keys.Where(k => k.StartsWith("barrow:")).ToList()) c.R.Flags.Remove(k); c.MarkDirty(); }
                    else if (a.Length > 2 && a[2] == "retreat") Warband.Retreat(c);
                    output(new JObject { ["stands"] = Warband.Stands(c), ["at"] = V(Warband.Barrow(c)), ["jarl"] = Warband.JarlName(c), ["next"] = c.R.Flag("barrow:next"), ["raiding"] = Warband.Raiding(c) });
                    break;
                case "backup":
                    {
                        // The colony's record and board to a file (BepInEx/claude/jarldom-<id>-<time>.txt), for jarldom relic to bring back.
                        c.MarkDirty(); c.Flush();
                        string file = System.IO.Path.Combine(BepInEx.Paths.BepInExRootPath, "claude", $"jarldom-{c.Id}-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
                        System.IO.File.WriteAllLines(file, new[] { c.Id, c.R.Name, Convert.ToBase64String(c.R.Save(withLog: true)), Convert.ToBase64String(c.BoardBytes() ?? new byte[0]) });
                        output(new JObject { ["file"] = file, ["bytes"] = c.Z.GetByteArray(Colony.RecordKey).Length });
                    }
                    break;
                case "takedown":
                    {
                        // As if taken down with the hammer: the heart drops where they stood.
                        WearNTear w = c.Seat.GetComponent<WearNTear>();
                        w.Remove();
                        output(new JObject { ["takendown"] = c.R.Name });
                    }
                    break;
                case "remove":
                    {
                        if (a.Length < 3 || a[2] != "everything") { error("jarldom remove everything: every villager and every high seat in the world, gone (for a clean test world only)"); return; }
                        int n = 0;
                        foreach (Humanoid h in Villagers.Bodies().ToList()) { Villagers.Brain(h)?.Release(); h.GetComponent<ZNetView>().ClaimOwnership(); h.GetComponent<ZNetView>().Destroy(); n++; }
                        foreach (ZNetView v in Assets.Instances(Pieces.PillarsHash).ToList()) { v.ClaimOwnership(); v.Destroy(); n++; }
                        output(new JObject { ["removed"] = n });
                    }
                    break;
                default: error(Usage); break;
            }
        }
        private static string Prefab(ItemDrop.ItemData i) => i.m_dropPrefab != null ? i.m_dropPrefab.name : i.m_shared.m_name; // (Unity's null: a prefab from an unloaded copy)
        // A tool's own terrain piece (the v2 ones live only in the tools' tables, not in the scene's prefabs).
        private static GameObject ToolPiece(string tool, string name) => ObjectDB.instance.GetItemPrefab(tool)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces?.m_pieces.FirstOrDefault(p => p != null && p.name == name);
        private static string AnimInfo(Humanoid h)
        {
            Animator an = h.GetComponentInChildren<Animator>();
            if (an == null) return "";
            var bools = an.parameters.Where(p => p.type == AnimatorControllerParameterType.Bool && an.GetBool(p.nameHash)).Select(p => p.name);
            return "on: " + string.Join(",", bools) + " state " + an.GetCurrentAnimatorStateInfo(0).shortNameHash;
        }
        private const string TestKey = "jarldom_test";
        // The floor a room stands on: the first surface down from just above the ground (not the roof over it).
        public static float Floor(Vector3 at)
        {
            float ground = ZoneSystem.instance.GetGroundHeight(at);
            return Physics.Raycast(new Vector3(at.x, ground + 1.5f, at.z), Vector3.down, out RaycastHit hit, 4f, LayerMask.GetMask("piece", "terrain", "Default", "static_solid")) ? hit.point.y : ground;
        }
        private static float Ground(Vector3 at) => ZoneSystem.instance.GetSolidHeight(at, out float h) ? h : ZoneSystem.instance.GetGroundHeight(at);
        // A piece put by a test command: built by the player, marked for unput.
        private static GameObject Put(GameObject prefab, Vector3 at, float yaw)
        {
            GameObject go = Object.Instantiate(prefab, at, Quaternion.Euler(0, yaw, 0));
            Player me = Player.m_localPlayer;
            if (me != null) go.GetComponent<Piece>()?.SetCreator(me.GetPlayerID(), Splatform.PlatformManager.DistributionPlatform.LocalUser.PlatformUserID);
            go.GetComponent<WearNTear>()?.OnPlaced();
            go.GetComponent<ZNetView>()?.GetZDO()?.Set(TestKey, true);
            return go;
        }
        private static T Parse<T>(string s) where T : struct => Enum.TryParse(s, true, out T v) ? v : throw new ArgumentException($"{s} is not a {typeof(T).Name}: {string.Join(", ", Enum.GetNames(typeof(T)))}");
        private static string Rect(float x1, float z1, float x2, float z2)
        {
            float a = Math.Min(x1, x2), b = Math.Max(x1, x2), c = Math.Min(z1, z2), d = Math.Max(z1, z2);
            return string.Join(",", new[] { a, c, b, c, b, d, a, d }.Select(v => v.ToString(CultureInfo.InvariantCulture)));
        }
        private static JObject Describe(Colony c) => new JObject
        {
            ["id"] = c.Id, ["name"] = c.R.Name, ["at"] = V(c.Position), ["owner"] = c.IsOwner ? "this game" : c.Z.GetOwner().ToString(), ["rank"] = c.R.Rank.ToString(), ["population"] = c.Population, ["cap"] = c.Cap,
            ["radius"] = c.Radius, ["renown"] = c.Renown, ["day"] = c.Day, ["hour"] = Math.Round(c.Hour, 2), ["arrivals"] = c.R.Arrivals, ["coming"] = c.R.Coming.Count, ["landed"] = c.R.ComingLanded,
            ["raid"] = c.RaidOn, ["horn"] = c.HornUntil > Time.time, ["rooms"] = c.R.Rooms.Count, ["zones"] = c.R.Zones.Count, ["orders"] = c.Board.All.Count(o => o.State == OrderState.Open || o.State == OrderState.Claimed),
            ["treasury"] = c.R.Treasury, ["deeds"] = c.R.Deeds, ["fallen"] = c.R.Fallen.Count, ["recordBytes"] = c.Z.GetByteArray(Colony.RecordKey)?.Length ?? 0,
        };
        // A command for a villager who has only just been made: sent once their body (and brain) is up, within ten seconds.
        private static System.Collections.IEnumerator WhenUp(string vid, string action, string arg)
        {
            for (float until = Time.time + 10f; Time.time < until; )
            {
                Humanoid h = Villagers.Body(vid);
                if (h != null && Villagers.BrainFor(h) != null) { Villagers.Command(vid, action, arg); yield break; }
                yield return new WaitForSeconds(0.5f);
            }
        }
        private static JObject Villager(Brain b) => new JObject
        {
            ["vid"] = b.S.Vid, ["name"] = b.S.FullName, ["prof"] = b.S.Prof.ToString(), ["traits"] = new JArray(b.S.Traits), ["doing"] = b.Doing, ["activity"] = b.Activity.ToString(), ["task"] = b.Task?.GetType().Name ?? "",
            ["said"] = Villagers.Tags(b.View.GetZDO()).GetString(Talk.SayKey, "").Split('\u001f')[0],
            ["at"] = V(b.Pos), ["health"] = Math.Round(b.Body.GetHealth()), ["maxHealth"] = Math.Round(b.Body.GetMaxHealth()), ["mood"] = Mood.Of(b.S, b.Now),
            ["needs"] = new JObject(Enum.GetValues(typeof(Need)).Cast<Need>().Select(n => new JProperty(n.ToString(), Math.Round(b.S.Needs[(int)n])))),
            ["reasons"] = new JArray(Mood.Reasons(b.S, b.Now).Select(r => $"{r.value:+0;-0} {r.text}")), ["meals"] = new JArray(b.Meals.Select(m => $"{m.Prefab} {m.Left:0}s")),
            ["bed"] = b.S.Bed, ["worth"] = b.S.Worth, ["skills"] = new JObject(b.S.Skills.Where(kv => !kv.Key.EndsWith("#")).Select(kv => new JProperty(kv.Key, Math.Round(kv.Value, 1)))),
            ["bag"] = new JArray(b.Body.GetInventory().GetAllItems().Select(i => $"{Prefab(i)} x{i.m_stack}{(i.m_equipped ? " (worn)" : "")}")), ["held"] = b.Held != null, ["fighting"] = b.Fighting, ["anim"] = AnimInfo(b.Body), ["lastAttacker"] = b.LastAttacker, ["foe"] = b.Foe != null ? b.Foe.m_name : "", ["combat"] = $"{b.CombatNote} swings {b.SwingsStarted}/{b.Swings} weapon {b.Body.GetCurrentWeapon()?.m_shared.m_name}",
            ["counts"] = new JObject(b.S.Counts.Select(kv => new JProperty(kv.Key, kv.Value))), ["moving"] = b.Move.Moving, ["goal"] = V(b.Move.Goal), ["taskDebug"] = b.Task?.Debug(b) ?? "", ["move"] = b.Move.Debug, ["velocity"] = Math.Round(b.Body.GetVelocity().magnitude, 2), ["moveDir"] = V(b.Body.GetMoveDir()),
        };
    }
}
