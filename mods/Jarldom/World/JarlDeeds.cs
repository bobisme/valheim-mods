using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Jarldom
{
    // The jarls' deeds (Core/Jarls.cs): counted on the colony's owner as they happen at home, and a boss slain anywhere carried home on
    // the player's own character until they next come to a colony of theirs. A byname won is told in the saga.
    // The tales read from runestones, learned by the colony (flag "lore:<text key>": 1 learned, 2 told by the skald).
    internal static class Legends
    {
        public const string Prefix = "lore:"; public const int Learned = 1, Told = 2;
        public static string Untold(Colony c) => c.R.Flags.Where(kv => kv.Key.StartsWith(Prefix) && kv.Value == Learned).Select(kv => kv.Key.Substring(Prefix.Length)).OrderBy(k => k, System.StringComparer.Ordinal).FirstOrDefault();
        // The tale as the skald tells it: a framing line, then the runestone's words a sentence or two a line.
        public static List<string> Lines(string key)
        {
            string text = Localization.instance.Localize(key).Replace("\n", " ").Replace("\r", " ");
            if (string.IsNullOrWhiteSpace(text) || text == key) return new List<string>();
            var lines = new List<string> { new[] { "As our grandmothers told it:", "Hear a tale of this land, as it was told to us:", "The old ones of this hall tell it so:" }[UnityEngine.Random.Range(0, 3)] };
            var cur = "";
            foreach (string s in System.Text.RegularExpressions.Regex.Split(text.Trim(), @"(?<=[.!?])\s+"))
            {
                if (cur.Length > 0 && cur.Length + s.Length > 140) { lines.Add(cur); cur = ""; }
                cur = cur.Length == 0 ? s : cur + " " + s;
            }
            if (cur.Length > 0) lines.Add(cur);
            return lines.Take(6).ToList();
        }
    }

    internal static class JarlDeeds
    {
        public const string PendingKey = "jarldom_deeds"; // (player custom data: "boss:Bonemass;boss:Eikthyr", deeds done away from home)
        private static float _next, _nextHome;

        // ---- on the colony's owner ----
        public static void Add(Colony c, string who, string what, int by = 1)
        {
            if (c == null || !c.IsOwner || string.IsNullOrEmpty(who)) return;
            Tally.Add(c.R.Flags, who, what, by);
            Title(c, who); c.MarkDirty();
        }
        // (the "tally" action: deeds a player's game carried here)
        public static void Delivered(Colony c, string who, string arg)
        {
            if (!c.AllowedName(who)) return;
            foreach (string d in arg.Split(';').Where(x => x != ""))
            {
                if (d.StartsWith(Wagers.Prefix)) { ArenaWatch.Heard(c, who, d); continue; } // (a fight in Quad's Arena)
                string[] f = d.Split(':');
                if (f[0] == "boss" && f.Length > 1) { Tally.Boss(c.R.Flags, who, f[1]); c.Log("slew", who, Boss(f[1])); }
                if (f[0] == "first" && f.Length > 2) { string what = string.Join(":", f.Skip(1)); if (!Wanderer.Known(c.R.Flags, what) && Wanderer.Points(what) > 0) { c.R.Flags[Wanderer.Prefix + what] = c.Day; c.Log("first", who, "", Wanderer.Tell(what, who)); } }
                if (f[0] == "lore" && f.Length > 1 && c.R.Flag(Legends.Prefix + f[1]) == 0) { c.R.Flags[Legends.Prefix + f[1]] = Legends.Learned; c.Log("lore", who, ""); }
            }
            Title(c, who); c.MarkDirty();
        }
        // A new byname: in the saga, and told.
        public static void Title(Colony c, string who)
        {
            var (id, title, _) = Bynames.For(Tally.Of(c.R.Flags, who));
            if (id == Tally.Get(c.R.Flags, who, Tally.Title)) return;
            Tally.Set(c.R.Flags, who, Tally.Title, id);
            if (id == 0) return;
            c.Log("byname", who, title);
            c.Tell($"The colony has a name for you now, {who}: {who} {title}.", true);
        }
        // Every half minute: the colony's people at home are seen (the days they were away counted), the feast caller credited.
        public static void Tick(Colony c)
        {
            if (!c.IsOwner || Time.time < _nextHome) return;
            _nextHome = Time.time + 30f;
            foreach (Player p in Player.GetAllPlayers().Where(p => p != null && !p.IsDead() && Utils.DistanceXZ(p.transform.position, c.Position) < c.Radius))
            {
                string who = p.GetPlayerName();
                if (!c.AllowedName(who)) continue;
                int away = Tally.Home(c.R.Flags, who, c.Day);
                if (away > 0) { c.Log("home", who, away.ToString()); Title(c, who); }
                c.MarkDirty();
            }
        }
        // A feast called by a player is theirs when it's held.
        public static void FeastCalled(Colony c, string who) { if (c.IsOwner && !string.IsNullOrEmpty(who)) { Tally.Set(c.R.Flags, who, "feastday", c.Day); c.MarkDirty(); } }
        public static void FeastHeld(Colony c, int day)
        {
            foreach (string who in Tally.Players(c.R.Flags).Where(p => Tally.Get(c.R.Flags, p, "feastday") == day).ToList()) { Tally.Set(c.R.Flags, who, "feastday", 0); Add(c, who, Tally.Feasts); }
        }
        // Raids held: those home at the end of it fought for it.
        public static void RaidHeld(Colony c)
        {
            foreach (Player p in Player.GetAllPlayers().Where(p => p != null && Utils.DistanceXZ(p.transform.position, c.Position) < c.Radius + 30f))
                if (c.AllowedName(p.GetPlayerName())) Add(c, p.GetPlayerName(), Tally.Raids);
        }

        public static string Boss(string prefab) { int t = Bynames.Tier(prefab); return t > 0 ? Bynames.Bosses[t - 1].name : prefab; }
        // How a player is known in this colony: "Bob Bonemass-bane", or "Bob".
        public static string Named(Colony c, string player) => c == null || string.IsNullOrEmpty(player) ? player : Bynames.Named(player, Tally.Of(c.R.Flags, player));

        // ---- on each player's own game ----
        // A boss falls with this player near: remembered on their character, for the colony.
        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        private static class Slain
        {
            private static void Prefix(Character __instance)
            {
                if (__instance == null || !__instance.IsBoss()) return;
                Player me = Player.m_localPlayer;
                if (me == null || me.IsDead() || Vector3.Distance(me.transform.position, __instance.transform.position) > 60f) return;
                string prefab = Utils.GetPrefabName(__instance.gameObject);
                if (Bynames.Tier(prefab) == 0) return;
                Plugin.Guard(() => Carry(me, "boss:" + prefab));
            }
        }
        // A runestone read: its tale is carried home for the colony (the skald tells it as theirs).
        [HarmonyPatch(typeof(RuneStone), nameof(RuneStone.Interact))]
        private static class Read
        {
            private static readonly System.Reflection.MethodInfo Random = AccessTools.Method(typeof(RuneStone), "GetRandomText");
            private static void Postfix(RuneStone __instance, Humanoid character, bool hold)
            {
                if (hold || !(character is Player p) || p != Player.m_localPlayer) return;
                Plugin.Guard(() =>
                {
                    var r = Random?.Invoke(__instance, null) as RuneStone.RandomRuneText;
                    string text = r != null ? r.m_text : __instance.m_text;
                    if (string.IsNullOrEmpty(text) || text.IndexOfAny(new[] { ';', ':', '|' }) >= 0 || !text.StartsWith("$")) return; // (a key: told in each player's own language)
                    if (p.m_customData.TryGetValue(PendingKey, out string had) && had.Split(';').Contains("lore:" + text)) return; // (read before)
                    Carry(p, "lore:" + text);
                });
            }
        }
        // ---- Odin the Wanderer (Core/Wanderer.cs): firsts out in the world, noticed on the player's game and carried home ----
        public const string FirstsKey = "jarldom_firsts"; // (player custom data: the firsts this character has carried, so each goes once)
        private static float _nextFirst;
        public static void Firsts()
        {
            Player me = Player.m_localPlayer;
            if (me == null || me.IsDead() || Time.time < _nextFirst || ZoneSystem.instance == null) return;
            _nextFirst = Time.time + 3f;
            Vector3 at = me.transform.position;
            var found = new List<string>();
            Heightmap.Biome biome = me.GetCurrentBiome();
            if (biome != Heightmap.Biome.Meadows && biome != Heightmap.Biome.None && !me.InInterior()) found.Add("biome:" + biome);
            Location loc = Location.GetLocation(at, true);
            var lm = loc != null ? Requests.Landmark(Utils.GetPrefabName(loc.gameObject)) : null;
            if (lm != null) found.Add((me.InInterior() ? "dungeon:" : "place:") + lm.Value.prefix);
            float home = float.MaxValue;
            foreach (string line in ZoneSystem.instance.GetGlobalKeys().Where(l => l.StartsWith("jarldom_colony_")))
            {
                string[] v = line.Substring(line.IndexOf(' ') + 1).Split(',');
                if (v.Length >= 2 && float.TryParse(v[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) && float.TryParse(v[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float z))
                    home = Mathf.Min(home, Utils.DistanceXZ(at, new Vector3(x, 0, z)));
            }
            int far = home < float.MaxValue && !me.InInterior() ? Wanderer.Far(home) : 0;
            foreach (int km in Wanderer.FarKm.Where(k => k <= far)) found.Add("far:" + km); // (every milestone on the way)
            Isles(me, at, found);
            if (found.Count == 0) return;
            me.m_customData.TryGetValue(FirstsKey, out string had);
            var carried = new HashSet<string>((had ?? "").Split(';').Where(s => s != ""));
            foreach (string f in found.Where(f => !carried.Contains(f) && Wanderer.Points(f) > 0))
            {
                carried.Add(f);
                Carry(me, "first:" + f);
            }
            me.m_customData[FirstsKey] = string.Join(";", carried);
        }
        // Sailing round an island (Core's Island.Mass and Circuit): the island beside the ship found once, the bearing round it kept while
        // the ship stays near; round, and it's one more of this character's islands ("jarldom_isles": their middles, so each counts once).
        public const string IslesKey = "jarldom_isles";
        private static Circuit _circuit; private static float _nextIsle;
        public static bool TestAboard; // (test: as if on a ship)
        public static void CarryFirst(Player me, string f)
        {
            me.m_customData.TryGetValue(FirstsKey, out string had);
            var carried = new HashSet<string>((had ?? "").Split(';').Where(x => x != ""));
            if (!carried.Add(f) || Wanderer.Points(f) <= 0) return;
            Carry(me, "first:" + f);
            me.m_customData[FirstsKey] = string.Join(";", carried);
        }
        public static void Isles(Player me, Vector3 at, List<string> found, bool now = false)
        {
            if (Ship.GetLocalShip() == null && !TestAboard || me.InInterior() || WorldGenerator.instance == null) { _circuit = null; return; }
            if (now) _nextIsle = 0;
            if (_circuit != null && !_circuit.Step(at.x, at.z)) _circuit = null; // (sailed off and left it)
            if (_circuit == null)
            {
                if (Time.time < _nextIsle) return;
                _nextIsle = Time.time + 20f;
                var mass = Island.Mass((x, z) => WorldGenerator.instance.GetHeight(x, z), ZoneSystem.instance.m_waterLevel, at.x, at.z);
                if (mass == null) return;
                _circuit = new Circuit(mass.Value.x, mass.Value.z, mass.Value.reach);
                _circuit.Step(at.x, at.z);
                return;
            }
            if (!_circuit.Done) return;
            string key = $"{Mathf.RoundToInt(_circuit.X / 32f)}_{Mathf.RoundToInt(_circuit.Z / 32f)}";
            _circuit = null;
            me.m_customData.TryGetValue(IslesKey, out string had);
            var isles = new HashSet<string>((had ?? "").Split(';').Where(x => x != ""));
            if (!isles.Add(key)) return;
            me.m_customData[IslesKey] = string.Join(";", isles);
            me.Message(MessageHud.MessageType.Center, isles.Count == 1 ? "You've sailed all the way round an island." : $"Round another island: {isles.Count} now.");
            foreach (int n in Wanderer.Isles.Where(n => n <= isles.Count)) found.Add("isle:" + n);
        }
        // ---- Wayfinder's primstav: the next festival burnt into it, once a minute ----
        private static float _nextFeast;
        public static void Primstav()
        {
            if (Time.time < _nextFeast || EnvMan.instance == null || Player.m_localPlayer == null) return;
            _nextFeast = Time.time + 60f;
            Date today = Year.Today();
            var (f, until) = Calendar.Next(today);
            int day = EnvMan.instance.GetDay();
            // (and the next washing day, Laugardagr: the week's mark)
            Compat.WayfinderFeast((day + until, Calendar.Name(f)), (day + Calendar.DaysToWashing(today.WorldDay), "Laugardagr, washing day"));
        }
        // ---- GraveGoods: an heirloom or saga weapon brought into the colony is marvelled at, once each ----
        private static float _nextMarvel; private static readonly HashSet<string> Shown = new HashSet<string>();
        public static string MarvelKey(string name) => "gg:" + name.GetStableHashCode();
        public static void Marvels()
        {
            Player me = Player.m_localPlayer;
            if (me == null || me.IsDead() || Time.time < _nextMarvel) return;
            _nextMarvel = Time.time + 5f;
            Colony c = Colony.At(me.transform.position);
            if (c == null || !c.Valid || !c.AllowedName(me.GetPlayerName())) return;
            if (!Villagers.Bodies().Any(h => h != null && !h.IsDead() && Vector3.Distance(h.transform.position, me.transform.position) < 12f)) return; // (someone there to see it)
            foreach (ItemDrop.ItemData item in me.GetInventory().GetAllItems())
            {
                int r = Compat.GoodsRarity(item);
                if (r < 3) continue;
                string name = Compat.GoodsName(item);
                if (name == "") name = Localization.instance.Localize(item.m_shared.m_name);
                if (c.R.Flag(MarvelKey(name)) != 0 || !Shown.Add(c.Id + "|" + name)) continue;
                Net.Act(c, "marvel", name + "|" + r);
                return; // (one at a time)
            }
        }
        // On the colony's owner: the nearest villager says so, and those near are lifted.
        public static void Marvel(Colony c, string who, string arg)
        {
            string[] f = arg.Split('|');
            if (f.Length < 2 || c.R.Flag(MarvelKey(f[0])) != 0) return;
            c.R.Flags[MarvelKey(f[0])] = c.Day; c.MarkDirty();
            bool saga = f[1] == "4";
            c.Log("marvel", who, f[0]);
            Player p = Player.GetAllPlayers().FirstOrDefault(x => x != null && x.GetPlayerName() == who);
            if (p == null) return;
            var near = c.R.Living.Select(r => (r, body: Villagers.Body(r.Vid))).Where(x => x.body != null && Vector3.Distance(x.body.transform.position, p.transform.position) < 15f)
                .OrderBy(x => Vector3.Distance(x.body.transform.position, p.transform.position)).ToList();
            if (near.Count == 0) return;
            string line = saga
                ? new[] { $"By the gods... is that {f[0]}? The one from the old songs?", $"{f[0]}! My grandmother sang of that blade.", $"That's {f[0]}. I never thought to see it with my own eyes." }[UnityEngine.Random.Range(0, 3)]
                : new[] { $"That's no common thing, jarl. {f[0]}, isn't it?", $"{f[0]}... an heirloom of some great house.", $"Look at the work on that. They'll remember {f[0]} here." }[UnityEngine.Random.Range(0, 3)];
            Villagers.Command(near[0].r.Vid, "say", line);
            foreach (var x in near) Villagers.Command(x.r.Vid, "mood", "marvel");
        }
        public static void Carry(Player me, string deed)
        {
            me.m_customData.TryGetValue(PendingKey, out string had);
            me.m_customData[PendingKey] = string.IsNullOrEmpty(had) ? deed : had + ";" + deed;
            _next = 0;
        }
        // At a colony of theirs: what they carry is given to it (every colony they belong to hears of it as they come).
        public static void PlayerTick()
        {
            Player me = Player.m_localPlayer;
            if (me == null || Time.time < _next) return;
            _next = Time.time + 5f;
            if (!me.m_customData.TryGetValue(PendingKey, out string deeds) || string.IsNullOrEmpty(deeds)) return;
            Colony c = Colony.At(me.transform.position);
            if (c == null || !c.Valid || !c.AllowedName(me.GetPlayerName())) return;
            // (each colony of theirs hears each deed once: how many of the list it has heard is kept beside it, by colony)
            string[] all = deeds.Split(';');
            string doneKey = PendingKey + ":" + c.Id;
            int heard = me.m_customData.TryGetValue(doneKey, out string h) && int.TryParse(h, out int n) ? n : 0;
            if (heard >= all.Length) return;
            string news = string.Join(";", all.Skip(heard));
            Net.Act(c, "tally", news);
            me.m_customData[doneKey] = all.Length.ToString();
            Plugin.Log($"Jarldom: {me.GetPlayerName()} brought word to {c.R.Name}: {news}");
        }
    }
}
