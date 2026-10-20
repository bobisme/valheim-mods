using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;

namespace Jarldom
{
    // The other mods Jarldom works with when they're there (none is needed). Found by plugin id in BepInEx's list (a dictionary lookup;
    // ScriptEngine and the dev loader keep it current), reached by reflection or a published API.
    internal static class Compat
    {
        public static readonly (string name, string[] guids, string use)[] Known =
        {
            ("AICompanion", new[] { "com.quad.aicompanion", "com.dhack.aicompanion" }, "beds and chests a companion claimed are left alone (and ours are marked for it)"),
            ("Gary", new[] { "com.bobisme.gary" }, "villagers pet Gary the Greydwarf"),
            ("QualityOfLife", new[] { "com.quad.qualityoflife", "com.dhack.qualityoflife" }, "chest rules as stockpile categories"),
            ("BuildOrders", new[] { "com.quad.buildorders", "com.dhack.buildorders" }, "builders build its planned ghosts from the stores"),
            ("Hallwright", new[] { "cx.bob.hallwright" }, "Hallwright outlines as rooms (not yet)"),
            ("FeedFromChests", new[] { "com.quad.feedfromchests", "com.dhack.feedfromchests" }, "smelters fed from chests"),
            ("Farmhand", new[] { "com.bobisme.farmhand" }, "rows and batches (not yet)"),
            ("Omens", new[] { "com.bobisme.omens" }, "Odin's favour is the gods' Fate"),
            ("Shieldwall", new[] { "com.bobisme.shieldwall" }, "a siege at the colony's Warstone is a raid: villagers defend it, guards man the stave towers and a gothi chants for the ward"),
            ("Respite", new[] { "com.bobisme.respite" }, "one raid clock for the world: warbands and Fuling bands wait their turn, and gather outside the colony first"),
            ("TrophyHall", new[] { "com.bobisme.trophyhall" }, "trophies grade the great hall"),
            ("SkalTavern", new[] { "com.quad.skaltavern", "com.dhack.skaltavern" }, "a Skål! at feasts; its ale, mead and wine drunk in the hall from its chests"),
            ("PartyHud", new[] { "com.quad.partyhud", "com.dhack.partyhud" }, "(not yet)"),
            ("LedgerChest", new[] { "com.quad.ledgerchest", "com.dhack.ledgerchest" }, "(not yet)"),
            ("ClaudeTools", new[] { "com.quad.claudetools", "com.dhack.claudetools" }, "the jarldom command"),
            ("Seasons", new[] { "shudnal.Seasons" }, "the colony's year follows its seasons (festivals mid-season)"),
        };
        public static BaseUnityPlugin Find(params string[] guids)
        {
            foreach (string g in guids) if (Chainloader.PluginInfos.TryGetValue(g, out PluginInfo info) && info.Instance != null) return info.Instance;
            return null;
        }
        public static bool Has(string name) => Known.Where(k => k.name == name).Any(k => Find(k.guids) != null);
        public static IEnumerable<(string name, bool present, string version, string use)> Report() =>
            Known.Select(k => { BaseUnityPlugin p = Find(k.guids); return (k.name, p != null, p?.Info?.Metadata?.Version?.ToString() ?? "", k.use); });

        // ---- Seasons (shudnal): the season, its day and each season's length, from its static state (reflection: no published API) ----
        private static BaseUnityPlugin _seasonsPlugin; private static FieldInfo _seasonState; private static MethodInfo _season, _seasonDay, _seasonLength;
        public static bool Seasons(out int season, out int day, out int[] lengths)
        {
            season = 0; day = 1; lengths = null;
            BaseUnityPlugin p = Find("shudnal.Seasons");
            if (p == null) return false;
            try
            {
                if (p != _seasonsPlugin)
                {
                    _seasonsPlugin = p;
                    Type t = p.GetType().Assembly.GetType("Seasons.Seasons");
                    _seasonState = t?.GetField("seasonState", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    Type st = _seasonState?.FieldType;
                    _season = st?.GetMethod("GetCurrentSeason", Type.EmptyTypes); _seasonDay = st?.GetMethod("GetCurrentDay", Type.EmptyTypes);
                    _seasonLength = st?.GetMethods().FirstOrDefault(m => m.Name == "GetDaysInSeason" && m.GetParameters().Length == 1);
                }
                object state = _seasonState?.GetValue(null);
                if (state == null || _season == null || _seasonDay == null || _seasonLength == null) return false;
                object now = _season.Invoke(state, null);
                season = Convert.ToInt32(now); day = (int)_seasonDay.Invoke(state, null);
                Type e = now.GetType();
                lengths = Enumerable.Range(0, 4).Select(i => (int)_seasonLength.Invoke(state, new[] { Enum.ToObject(e, i) })).ToArray();
                return true;
            }
            catch (Exception ex) { Plugin.Trace("Jarldom: Seasons not read: " + ex.Message); return false; }
        }

        // ---- Omens: one gods' favour per world; the great stag ----
        private static readonly ApiRef Omens = new ApiRef("Omens.Api", "com.bobisme.omens");
        public static bool OmensFate(out int fate)
        {
            fate = 0;
            Func<int> get = Omens.Bind<Func<int>>("GetFate");
            if (get == null) return false;
            try { fate = get(); } catch { return false; }
            return fate != int.MinValue; // (not the host: Omens' ledger lives there)
        }
        // Points of our favour (-100..100) become Omens' fate steps (-5..5) as they add up.
        public static bool OmensAddFate(Colony c, int points, string why)
        {
            Func<int, string, bool> add = Omens.Bind<Func<int, string, bool>>("AddFate");
            if (add == null) return false;
            try
            {
                int pool = c.R.Flag("omenspool") + points;
                int steps = pool / 20;
                c.R.Flags["omenspool"] = pool - steps * 20;
                if (steps != 0 && !add(steps, why)) { c.R.Flags["omenspool"] = pool; return false; }
                return true;
            }
            catch { return false; }
        }
        // The great stag an omen set loose (Omens 0.6.3 and later, Api version 1): is this it, and where do they run now.
        public static bool IsGreatStag(Character ch)
        {
            Func<Character, bool> f = ch == null ? null : Omens.Bind<Func<Character, bool>>("IsGreatStag", 1);
            return f != null && Safe(() => f(ch));
        }
        // The sky tonight (Omens 0.6.4 and later, Api version 2).
        public static bool Aurora() { Func<bool> f = Omens.Bind<Func<bool>>("IsAurora", 2); return f != null && Safe(f); }
        public static bool BloodMoon() { Func<bool> f = Omens.Bind<Func<bool>>("IsBloodMoon", 2); return f != null && Safe(f); }
        public static Vector3[] GreatStags()
        {
            Func<Vector3[]> f = Omens.Bind<Func<Vector3[]>>("GreatStags", 1);
            return f == null ? new Vector3[0] : Safe(f) ?? new Vector3[0];
        }

        // ---- Respite: one raid clock for the world, and raiders that gather outside the colony before they come ----
        private static readonly ApiRef Respite = new ApiRef("Respite.Api", "com.bobisme.respite");
        // Why no raid may come near a place now ("" if one may, or without Respite).
        public static string RaidBlocked(Vector3 at)
        {
            Func<Vector3, string> blocked = Respite.Bind<Func<Vector3, string>>("Blocked");
            try { return blocked?.Invoke(at) ?? ""; } catch { return ""; }
        }
        public static void Raided(Vector3 at, string source)
        {
            Action<Vector3, string> raided = Respite.Bind<Action<Vector3, string>>("Raided");
            try { raided?.Invoke(at, source); } catch { }
        }
        // Raiders gather outside home on from's side, told (text, then where and when) to the players near; spawn(spot) when the warning
        // is up. False without Respite.
        public static bool Muster(Vector3 home, float clear, Vector3 from, string text, Action<Vector3> spawn)
        {
            var muster = Respite.Bind<Func<Vector3, float, Vector3, string, Action<Vector3>, Vector3>>("MusterFrom");
            if (muster == null) return false;
            try { muster(home, clear, from, text, spawn); return true; } catch (Exception e) { Plugin.Warn("Respite muster failed: " + e.Message); return false; }
        }

        // ---- GraveGoods: heirlooms and saga weapons, marvelled at and worth more on a pyre ----
        private static readonly ApiRef GraveGoods = new ApiRef("GraveGoods.Api", "com.bobisme.gravegoods");
        public static int GoodsRarity(ItemDrop.ItemData item) { var f = GraveGoods.Bind<Func<ItemDrop.ItemData, int>>("Rarity", 1); try { return f != null ? f(item) : -1; } catch { return -1; } }
        public static string GoodsName(ItemDrop.ItemData item) { var f = GraveGoods.Bind<Func<ItemDrop.ItemData, string>>("Name", 1); try { return f?.Invoke(item) ?? ""; } catch { return ""; } }
        // (a heirloom counts three times its worth as grave goods, a saga weapon ten)
        public static float GoodsWorth(ItemDrop.ItemData item) { int r = GoodsRarity(item); return r == 4 ? 10f : r == 3 ? 3f : 1f; }

        // ---- Bathhouse: its saunas for washing day (steam on the stones), and villagers in the heat as its players' company ----
        private static readonly ApiRef Bathhouse = new ApiRef("Bathhouse.Api", "cx.bob.bathhouse");
        public static bool BathSteam(Vector3 near)
        {
            var saunas = Bathhouse.Bind<Func<Vector3, float, Vector3[]>>("Saunas", 1); var steam = Bathhouse.Bind<Func<Vector3, bool>>("Steam", 1);
            try { return saunas != null && steam != null && saunas(near, 6f).OrderBy(x => Vector3.Distance(x, near)).Take(1).Any(steam); } catch { return false; }
        }
        private static Action<Func<Vector3, int>> _companyTo; private static readonly Func<Vector3, int> CompanyCount = at => Bath.Company(at);
        private static float _nextCompany;
        public static void BathCompany()
        {
            if (Time.time < _nextCompany) return;
            _nextCompany = Time.time + 5f;
            // (once a copy of the Bathhouse mod is found: again if it's reloaded, a fresh copy knowing nobody)
            var add = Bathhouse.Bind<Action<Func<Vector3, int>>>("AddCompany", 1);
            if (add == null || add == _companyTo) return;
            _companyTo = add;
            try { add(CompanyCount); } catch { }
        }

        // ---- Homespun: cloth from field to back (Villager/Weaving.cs): the weaver tends its retting pools, flax brake and spinning wheel,
        // weaves at its loom and sews at its table, and the colony wears its garments ----
        private static readonly ApiRef Homespun = new ApiRef("Homespun.Api", "cx.bob.homespun");
        public static bool HomespunOn => Homespun.Present && HomespunStations.Length > 0;
        private static string[] _hsStations = new string[0], _hsGarments = new string[0], _hsDyes = new string[0]; private static float _hsNext;
        private static void HomespunNames()
        {
            if (Time.time < _hsNext) return;
            _hsNext = Time.time + 10f;
            var st = Homespun.Bind<Func<string[]>>("get_Stations", 1); var ga = Homespun.Bind<Func<string[]>>("get_Garments", 1);
            var dy = Homespun.Bind<Func<string[]>>("get_Dyes", 2);
            try { _hsStations = st?.Invoke() ?? new string[0]; _hsGarments = ga?.Invoke() ?? new string[0]; _hsDyes = dy?.Invoke() ?? new string[0]; } catch { _hsStations = _hsGarments = _hsDyes = new string[0]; }
        }
        public static string[] HomespunStations { get { HomespunNames(); return _hsStations; } }
        public static string[] HomespunGarments { get { HomespunNames(); return _hsGarments; } }
        // What one of an item put in at a Homespun station (or the spinning wheel) comes to, or null.
        public static (string to, int n)? HomespunGives(string station, string from) { var f = Homespun.Bind<Func<string, string, (string, int)?>>("Gives", 1); try { return f?.Invoke(station, from); } catch { return null; } }
        // Dyeing (Homespun 0.2.0 and later, Api version 2; Villager/Dyeing.cs): its colours, a thing's colour, what it becomes dyed, the
        // dyestuff for a colour and how many a measure, the measures a thing takes. Without it: no colours, nothing dyed.
        public static string[] HomespunDyes { get { HomespunNames(); return _hsDyes; } }
        public static bool HomespunDyeing => HomespunOn && HomespunDyes.Length > 0;
        private static readonly Dictionary<string, string> _hsDyeOf = new Dictionary<string, string>(); private static Delegate _hsDyedFn;
        // A thing's colour ("Blue"), or null: undyed, or not a thing that's dyed. (asked of every garment a villager sorts: kept per prefab)
        public static string HomespunDye(string prefab)
        {
            if (string.IsNullOrEmpty(prefab) || !prefab.StartsWith("Homespun_")) return null;
            var f = Homespun.Bind<Func<string, (string, string)?>>("Dyed", 2);
            if (f == null) return null;
            if (f != _hsDyedFn) { _hsDyeOf.Clear(); _hsDyedFn = f; } // (a reloaded Homespun: asked afresh)
            if (_hsDyeOf.TryGetValue(prefab, out string kept)) return kept;
            string dye; try { dye = f(prefab)?.Item2; } catch { dye = null; }
            return _hsDyeOf[prefab] = dye;
        }
        // Its wool sails (Api version 3): the undyed one's name, and whether a thing is one of them in any colour. Shipyard's knarr takes one.
        public static string HomespunSail { get { var f = Homespun.Bind<Func<string>>("get_Sail", 3); try { return f?.Invoke(); } catch { return null; } } }
        public static bool HomespunIsSail(string prefab) { var f = Homespun.Bind<Func<string, bool>>("IsSail", 3); try { return f != null && f(prefab); } catch { return false; } }
        public static bool HomespunDyeable(string prefab) { var f = Homespun.Bind<Func<string, (string, string)?>>("Dyed", 2); try { return f?.Invoke(prefab) != null; } catch { return false; } }
        public static string HomespunDyedAs(string prefab, string dye) { var f = Homespun.Bind<Func<string, string, string>>("DyedAs", 2); try { return f?.Invoke(prefab, dye); } catch { return null; } }
        public static (string stuff, int perMeasure)? HomespunDyestuff(string dye) { var f = Homespun.Bind<Func<string, (string, int)?>>("Dyestuff", 2); try { return f?.Invoke(dye); } catch { return null; } }
        public static int HomespunMeasures(string prefab) { var f = Homespun.Bind<Func<string, int>>("Measures", 2); try { return f?.Invoke(prefab) ?? 0; } catch { return 0; } }

        // ---- Shipyard: its slipways, where the shipwright builds a færing or a knarr in stages (World/Shipyard.cs) ----
        private static readonly ApiRef ShipyardMod = new ApiRef("Shipyard.Api", "cx.bob.shipyard");
        public static bool ShipyardOn => ShipyardMod.Present;
        public static string[] ShipyardHulls { get { var f = ShipyardMod.Bind<Func<string[]>>("get_Hulls", 1); try { return f?.Invoke() ?? new string[0]; } catch { return new string[0]; } } }
        public static string ShipyardTitle(string hull) { var f = ShipyardMod.Bind<Func<string, string>>("Title", 1); try { return f?.Invoke(hull); } catch { return null; } }
        public static string ShipyardPrefab(string hull) { var f = ShipyardMod.Bind<Func<string, string>>("Prefab", 1); try { return f?.Invoke(hull); } catch { return null; } }
        public static string[] ShipyardStageCosts(string hull) { var f = ShipyardMod.Bind<Func<string, string[]>>("StageCosts", 1); try { return f?.Invoke(hull) ?? new string[0]; } catch { return new string[0]; } }
        public static Vector3[] Slipways(Vector3 at, float r) { var f = ShipyardMod.Bind<Func<Vector3, float, Vector3[]>>("SlipwaysNear", 1); try { return f?.Invoke(at, r) ?? new Vector3[0]; } catch { return new Vector3[0]; } }
        public static string SlipwayHull(Vector3 slipway) { var f = ShipyardMod.Bind<Func<Vector3, string>>("Hull", 1); try { return f?.Invoke(slipway); } catch { return null; } }
        public static int SlipwayStage(Vector3 slipway) { var f = ShipyardMod.Bind<Func<Vector3, int>>("Stage", 1); try { return f?.Invoke(slipway) ?? -1; } catch { return -1; } }
        public static bool SlipwayFinished(Vector3 slipway) { var f = ShipyardMod.Bind<Func<Vector3, bool>>("Finished", 1); try { return f != null && f(slipway); } catch { return false; } }
        public static (string item, int n)[] SlipwayNeeds(Vector3 slipway) { var f = ShipyardMod.Bind<Func<Vector3, (string, int)[]>>("Needs", 1); try { return f?.Invoke(slipway) ?? new (string, int)[0]; } catch { return new (string, int)[0]; } }
        public static bool SlipwayStart(Vector3 slipway, string hull) { var f = ShipyardMod.Bind<Func<Vector3, string, bool>>("StartBuild", 1); try { return f != null && f(slipway, hull); } catch { return false; } }
        public static int SlipwayPut(Vector3 slipway, string item, int n) { var f = ShipyardMod.Bind<Func<Vector3, string, int, int>>("Contribute", 1); try { return f?.Invoke(slipway, item, n) ?? 0; } catch { return 0; } }
        public static bool SlipwayLaunch(Vector3 slipway) { var f = ShipyardMod.Bind<Func<Vector3, bool>>("Launch", 1); try { return f != null && f(slipway); } catch { return false; } }

        // ---- Wayfinder: its Beacon Fire is the colony's beacon ----
        private static readonly ApiRef Wayfinder = new ApiRef("Wayfinder.Api", "com.bobisme.wayfinder");
        public static Vector3? WayfinderBeacon(Vector3 at, float radius) { var f = Wayfinder.Bind<Func<Vector3, float, Vector3?>>("NearestBeacon", 1); try { return f?.Invoke(at, radius); } catch { return null; } }
        public static bool WayfinderLight(Vector3 at, float radius) { var f = Wayfinder.Bind<Func<Vector3, float, bool>>("LightBeacon", 1); try { return f != null && f(at, radius); } catch { return false; } }
        // (v2: the next festival on Wayfinder's primstav)
        public static void WayfinderFeast(params (int day, string name)[] days)
        {
            var clear = Wayfinder.Bind<Action<string>>("ClearFeastDays", 2); var add = Wayfinder.Bind<Action<string, int, string>>("AddFeastDay", 2);
            try { clear?.Invoke("Jarldom"); if (add != null) foreach (var d in days) add("Jarldom", d.day, d.name); } catch { }
        }
        public static bool WayfinderLit(Vector3 at, float radius) { var f = Wayfinder.Bind<Func<Vector3, float, bool>>("BeaconLit", 1); try { return f != null && f(at, radius); } catch { return false; } }

        // ---- Arena (Quad's): a jarl's fights in the ring, watched from the stands and wagered on (Api v1, Arena 0.4.4 and later) ----
        private static readonly ApiRef Arena = new ApiRef("Arena.Api", "com.quad.arena", "com.dhack.arena");
        private delegate bool ContestFn(out string fighter, out string kind);
        private delegate bool CentreFn(out Vector3 centre);
        private delegate bool ResultFn(out string fighter, out string kind, out string outcome, out bool won, out bool crowned, out int coins, out double when);
        public static bool ArenaContest(out string fighter, out string kind)
        {
            fighter = kind = "";
            var f = Arena.Bind<ContestFn>("IsContestRunning", 1);
            try { return f != null && f(out fighter, out kind); } catch { return false; }
        }
        public static bool ArenaCentre(out Vector3 centre)
        {
            centre = Vector3.zero;
            var f = Arena.Bind<CentreFn>("ArenaAt", 1);
            try { return f != null && f(out centre); } catch { return false; }
        }
        public static Vector3[] ArenaStands(int max) { var f = Arena.Bind<Func<int, Vector3[]>>("StandSpots", 1); try { return f?.Invoke(max) ?? new Vector3[0]; } catch { return new Vector3[0]; } }
        public static int ArenaResults() { var f = Arena.Bind<Func<int>>("get_ResultCount", 1); try { return f != null ? f() : 0; } catch { return 0; } }
        public static bool ArenaLast(out string fighter, out string kind, out string outcome, out bool won, out bool crowned, out int coins)
        {
            fighter = kind = outcome = ""; won = crowned = false; coins = 0;
            var f = Arena.Bind<ResultFn>("LastResult", 1);
            try { return f != null && f(out fighter, out kind, out outcome, out won, out crowned, out coins, out _); } catch { return false; }
        }
        public static bool ArenaPresent => Arena.Present && Arena.Version >= 1;

        // ---- Meadow Golf (Bob's): a caddie on the course (Api 1: polled, read-only) ----
        private static readonly ApiRef Golf = new ApiRef("MeadowGolf.Api", "com.bobisme.golf");
        private delegate bool BallFn(long player, out Vector3 at, out int strokes, out bool still, out bool done);
        public static bool GolfPresent => Golf.Present && Golf.Version >= 1;
        public static bool GolfBall(long player, out Vector3 at, out int strokes, out bool still, out bool done)
        {
            at = Vector3.zero; strokes = 0; still = done = false;
            var f = Golf.Bind<BallFn>("BallOf", 1);
            try { return f != null && f(player, out at, out strokes, out still, out done); } catch { return false; }
        }
        public static Vector3 GolfCup(long player)
        {
            var f = Golf.Bind<Func<long, Vector3>>("CupOf", 1);
            return f == null ? Vector3.zero : Safe(() => f(player));
        }
        public static bool HoldsClub(Player p)
        {
            var f = Golf.Bind<Func<Player, bool>>("HoldsClub", 1);
            return f != null && Safe(() => f(p));
        }

        // ---- Shieldwall: a siege at the colony's Warstone is a raid ----
        private static readonly ApiRef Shieldwall = new ApiRef("Shieldwall.Api", "com.bobisme.shieldwall");
        public static bool Siege(Vector3 at, float radius, out Vector3 stone)
        {
            stone = Vector3.zero;
            Func<Vector3, float, bool> siegeAt = Shieldwall.Bind<Func<Vector3, float, bool>>("IsSiegeAt");
            if (siegeAt == null) return false;
            try
            {
                if (!siegeAt(at, radius)) return false;
                Func<Vector3, float, Vector3> stoneAt = Shieldwall.Bind<Func<Vector3, float, Vector3>>("StoneAt");
                if (stoneAt != null) stone = stoneAt(at, radius);
                return true;
            }
            catch { return false; }
        }

        // The stave sockets near the stone (Shieldwall 0.4.27: Api 2), the raised ones first: where the guards stand in a siege.
        public static IList<Vector3> TowerPosts(Vector3 at, float radius)
        {
            Func<Vector3, float, IList<Vector3>> posts = Shieldwall.Bind<Func<Vector3, float, IList<Vector3>>>("TowerPosts", 2);
            return posts == null ? new Vector3[0] : Safe(() => posts(at, radius)) ?? new Vector3[0];
        }
        // The gothi's chant: the stone's ward a little stronger for a while (Shieldwall Api 2). Whether a stone took it.
        public static bool WardBonus(Vector3 at, float radius, string source, float mult, float seconds)
        {
            Func<Vector3, float, string, float, float, int> set = Shieldwall.Bind<Func<Vector3, float, string, float, float, int>>("SetWardBonus", 2);
            return set != null && Safe(() => set(at, radius, source, mult, seconds)) > 0;
        }
        // Paid off: the siege ends (on the stone's owner's game; Shieldwall 0.4.12 and later). How many ended.
        public static int CallOffSiege(Vector3 at, float radius)
        {
            Func<Vector3, float, int> callOff = Shieldwall.Bind<Func<Vector3, float, int>>("CallOff");
            return callOff == null ? 0 : Safe(() => callOff(at, radius));
        }

        // ---- BuildOrders: the helper hooks it publishes (AppDomain data, no types shared) ----
        // (BuildOrders counts its plans' pieces afresh each time, 5 to 15 ms in a big base: the answer kept a few seconds; plans change seldom)
        private static readonly Dictionary<(int, int, int), (float at, int n)> GhostsKept = new Dictionary<(int, int, int), (float, int)>();
        public static int GhostCount(Vector3 at, float radius)
        {
            var key = (Mathf.RoundToInt(at.x), Mathf.RoundToInt(at.z), Mathf.RoundToInt(radius));
            if (GhostsKept.TryGetValue(key, out var kept) && Time.time - kept.at < 10f) return kept.n;
            int n = AppDomain.CurrentDomain.GetData("DHack.BuildOrders.Count") is Func<Vector3, float, int> f ? Safe(() => f(at, radius)) : 0;
            GhostsKept[key] = (Time.time, n);
            return n;
        }
        public static string[] NextGhosts(Vector3 at, float radius, int max) => AppDomain.CurrentDomain.GetData("DHack.BuildOrders.Next") is Func<Vector3, float, int, string[]> f ? Safe(() => f(at, radius, max)) ?? new string[0] : new string[0];
        public static bool PlaceGhost(string id) => AppDomain.CurrentDomain.GetData("DHack.BuildOrders.Place") is Func<string, bool> f && Safe(() => f(id));
        public static string[] GhostBlockers(Vector3 at, float radius) => AppDomain.CurrentDomain.GetData("DHack.BuildOrders.Blockers") is Func<Vector3, float, string[]> f ? Safe(() => f(at, radius)) ?? new string[0] : new string[0];
        private static T Safe<T>(Func<T> f) { try { return f(); } catch { return default; } }

        // ---- Gary: a greydwarf to pet ----
        public static Character Gary(Vector3 near, float radius) =>
            Character.GetAllCharacters().FirstOrDefault(c => c != null && c.m_group == "bob_gary" && Vector3.Distance(c.transform.position, near) < radius);

        // ---- SkalTavern: a toast at the feast ----
        public static void SkalToast(Colony c)
        {
            if (!Has("SkalTavern")) return;
            foreach (Brain b in Villagers.All.Where(b => b.S.Colony == c.Id)) b.Say(new[] { "Skål!", "To the feast!", "Skål to the hall!", "Drink up, all of you!", "To the jarl and the hall!" }[UnityEngine.Random.Range(0, 5)], 4);
        }
    }
}
