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
        private const string Usage = "jarldom status | roster | villager <name|vid> | needs <name> | renown | rooms | room check <n> | board | log [n] | saga | compat | " +
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
        private static bool Cheats() => ZNet.instance != null && (ZNet.instance.IsServer() || Terminal.m_cheat);
        private static Colony Here()
        {
            Player p = Player.m_localPlayer;
            return p == null ? Colony.All.FirstOrDefault() : Colony.Nearest(p.transform.position, 400);
        }
        private static JArray V(Vector3 v) => new JArray(Math.Round(v.x, 1), Math.Round(v.y, 1), Math.Round(v.z, 1));
        private static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);
        private static Brain Who(string name) => Villagers.All.FirstOrDefault(b => b.S.Vid == name || b.S.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || b.S.FullName.Equals(name.Replace('_', ' '), StringComparison.OrdinalIgnoreCase))
            ?? Villagers.All.FirstOrDefault(b => b.S.Name.StartsWith(name, StringComparison.OrdinalIgnoreCase));

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
            string[] cheats = { "creature", "table", "soil", "cultivate", "grow", "nav", "roof", "act", "level", "put", "wall", "unput", "fill", "place", "spawn", "migrate", "accept", "room", "zone", "prof", "mood", "worth", "need", "give", "bed", "goto", "time", "pay", "favour", "feast", "thing", "ascend", "kill", "raid", "horn", "treasury", "stockpile", "tp", "remove" };
            if (cheats.Contains(cmd) && !(cmd == "room" && a.Length > 2 && a[2] == "check") && !Cheats()) { error("jarldom " + cmd + " changes the world: only in single player, for the host, or with devcommands"); return; }
            if (c == null && cmd != "place" && cmd != "compat" && cmd != "status") { error("no colony near (jarldom place pillars makes one)"); return; }
            Player me = Player.m_localPlayer;
            switch (cmd)
            {
                case "status":
                    output(new JObject
                    {
                        ["version"] = Plugin.Version, ["hostHasJarldom"] = Net.HostHasJarldom, ["colonies"] = new JArray(Colony.All.Select(x => x.Id)),
                        ["colony"] = c == null ? null : Describe(c),
                        ["compat"] = new JObject(Compat.Report().Select(r => new JProperty(r.name, r.present ? r.version : "absent"))),
                    });
                    break;
                case "compat": output(new JObject { ["compat"] = new JArray(Compat.Report().Select(r => new JObject { ["mod"] = r.name, ["present"] = r.present, ["version"] = r.version, ["use"] = r.use })) }); break;
                case "roster":
                    output(new JObject { ["roster"] = new JArray(c.R.Roster.Select(r => new JObject { ["vid"] = r.Vid, ["name"] = r.Name, ["prof"] = r.Prof.ToString(), ["state"] = r.State.ToString(), ["bed"] = r.Bed, ["worth"] = r.Worth, ["at"] = new JArray(r.X, r.Y, r.Z), ["loaded"] = Villagers.Body(r.Vid) != null })), ["fallen"] = c.R.Fallen.Count, ["coming"] = new JArray(c.R.Coming.Select(n => n.Name + " (" + n.Traits + ")")) });
                    break;
                case "villager": case "needs":
                    {
                        Brain b = a.Length > 2 ? Who(a[2]) : Villagers.All.FirstOrDefault();
                        if (b == null) { error("no such villager loaded here (owned by this game)"); return; }
                        output(Villager(b));
                    }
                    break;
                case "renown": c.SurveySoon(); output(new JObject { ["renown"] = c.Renown, ["wealth"] = c.WealthPart, ["comfort"] = c.ComfortPart, ["deeds"] = c.R.Deeds, ["pieceValue"] = c.PieceValue, ["beds"] = c.Beds, ["threshold"] = Renown.Threshold(c.R.Arrivals), ["migration"] = Migration.Why(c) }); break;
                case "rooms":
                    output(new JObject { ["rooms"] = new JArray(c.R.Rooms.Select((r, i) => new JObject { ["n"] = i, ["id"] = r.Id, ["type"] = r.Type.ToString(), ["god"] = r.God, ["ok"] = r.Ok, ["grade"] = r.Grade.ToString(), ["points"] = r.Points, ["failures"] = r.Failures, ["open"] = r.OpenEdges, ["area"] = Math.Round(Geometry.Area(r.Xz)) })), ["zones"] = new JArray(c.R.Zones.Select(z => new JObject { ["id"] = z.Id, ["type"] = z.Type.ToString(), ["area"] = Math.Round(Geometry.Area(z.Xz)) })) });
                    break;
                case "room":
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
                        float floor = Floor(new Vector3((x1 + x2) / 2, 0, (z1 + z2) / 2));
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
                        Vector3 at = me.transform.position + me.transform.forward * d;
                        if (ZoneSystem.instance.GetSolidHeight(at, out float h)) at.y = h;
                        Colony near = Colony.Nearest(at, Renown.MinApart);
                        if (near != null) { error($"colony {near.Id} is {Utils.DistanceXZ(near.Position, at):0} m away (colonies stand {Renown.MinApart} m apart)"); return; }
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
                        if (a.Length > 3) { var traits = a.Skip(3).Where(t => Tables.Trait(t) != null).Take(2).ToList(); if (traits.Count > 0) n.Traits = string.Join(",", traits.Concat(n.Traits.Split(',').Where(t => !traits.Contains(t) && traits.All(x => !Tables.Clash(x, t)))).Take(2)); }
                        c.R.Coming.Add(n); c.R.ComingLanded = true;
                        Migration.Accept(c, c.R.Coming.Count - 1, me?.GetPlayerName() ?? "");
                        Brain b = null;
                        string vid = c.R.Roster.Last().Vid;
                        if (a.Length > 2 && Enum.TryParse(a[2], true, out Prof p)) Villagers.Command(vid, "prof", p.ToString());
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
                case "worth": { Brain b = Who(a[2]); if (b == null) { error("who?"); return; } b.Command("worth", a[3]); output(new JObject { ["worth"] = b.S.Worth, ["worthy"] = b.S.Worthy }); } break;
                case "need": { Brain b = Who(a[2]); if (b == null) { error("who?"); return; } b.Command("need", a[3]); output(Villager(b)); } break;
                case "give":
                    {
                        Brain b = Who(a[2]); if (b == null) { error("who?"); return; }
                        GameObject prefab = Assets.Find(a[3]); if (prefab == null) { error("no prefab " + a[3]); return; }
                        int n = a.Length > 4 ? int.Parse(a[4]) : 1;
                        for (int i = 0; i < n;) { int stack = Math.Min(n - i, prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize); if (!b.Body.GetInventory().AddItem(prefab, stack)) break; i += stack; }
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
                case "thing": Thing.Call(c, c.Day); output(new JObject { ["petitions"] = new JArray(Thing.Shown(c).Select(p => p.Text)) }); break;
                case "ascend": { Brain b = Who(a[2]); if (b == null) { error("who?"); return; } Ascension.Begin(c, b.S.Vid, me?.GetPlayerName() ?? ""); output(new JObject { ["why"] = Ascension.Why(c, b.S) }); } break;
                case "kill": { Brain b = Who(a[2]); if (b == null) { error("who?"); return; } var hit = new HitData(); hit.m_damage.m_damage = 99999; hit.m_point = b.Body.GetCenterPoint(); b.Body.Damage(hit); output(new JObject { ["killed"] = b.S.Name }); } break;
                case "raid": RandEventSystem.instance.SetRandomEventByName(a.Length > 2 ? a[2] : "army_eikthyr", c.Position); output(new JObject { ["raid"] = a.Length > 2 ? a[2] : "army_eikthyr" }); break;
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
                case "unput":
                    {
                        int n = 0;
                        string only = a.Length > 2 ? a[2] : ""; float minY = a.Length > 3 ? F(a[3]) : float.MinValue;
                        foreach (Piece p in c.PiecesIn(c.Radius + 30).Where(p => p.GetComponent<ZNetView>()?.GetZDO()?.GetBool(TestKey) == true && (only == "" || Utils.GetPrefabName(p.gameObject) == only) && p.transform.position.y >= minY).ToList()) { p.GetComponent<ZNetView>().ClaimOwnership(); p.GetComponent<ZNetView>().Destroy(); n++; }
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
                case "creature":
                    {
                        GameObject prefab = ZNetScene.instance.GetPrefab(a[2]);
                        if (prefab == null || prefab.GetComponent<Character>() == null) { error("no creature " + a[2]); return; }
                        Vector3 at = new Vector3(F(a[3]), 0, F(a[4])); at.y = Ground(at) + 0.5f;
                        GameObject go = Object.Instantiate(prefab, at, Quaternion.identity);
                        if (a.Length > 5) go.GetComponent<Character>().SetLevel(int.Parse(a[5]));
                        output(new JObject { ["spawned"] = a[2], ["at"] = V(at) });
                    }
                    break;
                case "ground": { Vector3 at = new Vector3(F(a[2]), 0, F(a[3])); output(new JObject { ["ground"] = Ground(at) }); } break;
                case "remove":
                    {
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
        private static JObject Villager(Brain b) => new JObject
        {
            ["vid"] = b.S.Vid, ["name"] = b.S.FullName, ["prof"] = b.S.Prof.ToString(), ["traits"] = new JArray(b.S.Traits), ["doing"] = b.Doing, ["activity"] = b.Activity.ToString(), ["task"] = b.Task?.GetType().Name ?? "",
            ["at"] = V(b.Pos), ["health"] = Math.Round(b.Body.GetHealth()), ["maxHealth"] = Math.Round(b.Body.GetMaxHealth()), ["mood"] = Mood.Of(b.S, b.Now),
            ["needs"] = new JObject(Enum.GetValues(typeof(Need)).Cast<Need>().Select(n => new JProperty(n.ToString(), Math.Round(b.S.Needs[(int)n])))),
            ["reasons"] = new JArray(Mood.Reasons(b.S, b.Now).Select(r => $"{r.value:+0;-0} {r.text}")), ["meals"] = new JArray(b.Meals.Select(m => $"{m.Prefab} {m.Left:0}s")),
            ["bed"] = b.S.Bed, ["worth"] = b.S.Worth, ["skills"] = new JObject(b.S.Skills.Where(kv => !kv.Key.EndsWith("#")).Select(kv => new JProperty(kv.Key, Math.Round(kv.Value, 1)))),
            ["bag"] = new JArray(b.Body.GetInventory().GetAllItems().Select(i => $"{Prefab(i)} x{i.m_stack}{(i.m_equipped ? " (worn)" : "")}")), ["held"] = b.Held != null, ["fighting"] = b.Fighting, ["anim"] = AnimInfo(b.Body), ["combat"] = $"{b.CombatNote} swings {b.SwingsStarted}/{b.Swings} weapon {b.Body.GetCurrentWeapon()?.m_shared.m_name}",
            ["counts"] = new JObject(b.S.Counts.Select(kv => new JProperty(kv.Key, kv.Value))), ["moving"] = b.Move.Moving, ["goal"] = V(b.Move.Goal), ["taskDebug"] = b.Task?.Debug(b) ?? "", ["move"] = b.Move.Debug, ["velocity"] = Math.Round(b.Body.GetVelocity().magnitude, 2), ["moveDir"] = V(b.Body.GetMoveDir()),
        };
    }
}
