using System.Collections.Generic;
using HarmonyLib;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // An expedition: a crew the jarl picks in the colony window, each with a role, packing for it from the stores and following them
    // out. The role is kept on the body beside the follow (whichever game owns them next knows it), and ends with the follow.
    //   guard: fights (needs a weapon); cook: packs food and hands the jarl some when they're running low; scout: the map uncovered far
    //   round the jarl as they go; carrier: packs a camp's makings (wood and stone).
    internal static class Expedition
    {
        public const string RoleKey = "jarldom_role";
        public static readonly string[] Roles = { "guard", "cook", "scout", "carrier" };
        public const int MaxCrew = 6;
        public static readonly Dictionary<string, string> RoleText = new Dictionary<string, string>
        {
            ["guard"] = "Fights whatever comes at the crew or the jarl. Needs a weapon.",
            ["cook"] = "Packs food from the stores and hands the jarl some when they're running low.",
            ["scout"] = "Uncovers the map far round the jarl as the crew goes.",
            ["carrier"] = "Packs a camp's makings from the stores: 25 wood and 5 stone.",
        };
        // (with the Camping mod: the sleepers' bedrolls, and the carrier's lean-to and the jarl's bedroll, or a tent for the cold)
        public static string Describe(string role)
        {
            string text = RoleText.TryGetValue(role, out string t) ? t : "";
            if (!CampingKit) return text;
            if (role == "carrier") return "Packs a camp's makings from the stores: 35 wood and 5 stone, hides for a lean-to and the jarl's bedroll, and a tent's makings if there are wolf pelts (the jarl sleeps in it in the mountains).";
            if (role != "guard") return text + " Takes a bedroll's hides from the stores to sleep on at camp.";
            return text + " Keeps watch at night: no bedroll.";
        }
        // What each role packs: (prefab, amount); the cook's food is any food, counted in items.
        private static readonly Dictionary<string, (string item, int n)[]> Kit = new Dictionary<string, (string, int)[]>
        {
            ["carrier"] = new[] { ("Wood", 25), ("Stone", 5) },
        };
        // ---- the Camping mod's pieces, when it's installed: bedrolls round the fire, a lean-to over the jarl's, a tent in the cold ----
        public const string BedrollName = "Camping_Bedroll", LeanToName = "Camping_LeanTo", TentName = "Camping_Tent";
        public const string SlotKey = "jarldom_campslot"; // (on a camp bedroll or tent: 0 the jarl's, 1.. the sleepers' in turn)
        public static bool CampingKit => ZNetScene.instance != null && ZNetScene.instance.GetPrefab(BedrollName) != null;
        public static List<(ItemDrop item, int n)> CostOf(string prefab)
        {
            var cost = new Dictionary<ItemDrop, int>();
            foreach (Piece.Requirement r in ZNetScene.instance?.GetPrefab(prefab)?.GetComponent<Piece>()?.m_resources ?? new Piece.Requirement[0])
                if (r.m_resItem != null) cost[r.m_resItem] = (cost.TryGetValue(r.m_resItem, out int n) ? n : 0) + r.m_amount;
            return cost.Select(kv => (kv.Key, kv.Value)).ToList();
        }
        private static (string item, int n)[] Makings(string prefab, int times = 1) => CostOf(prefab).Select(x => (x.item.gameObject.name, x.n * times)).ToArray();
        // What a role packs with the Camping mod, on top of the kit above.
        private static List<(string item, int n)> CampingPack(Brain b, string role, List<Container> chests)
        {
            var want = new List<(string, int)>();
            if (!CampingKit || role == "guard") return want;
            want.AddRange(Makings(BedrollName));
            if (role != "carrier") return want;
            want.AddRange(Makings(BedrollName)); want.AddRange(Makings(LeanToName));
            // (a tent only from stores with the wolf pelts for it)
            var tent = Makings(TentName);
            if (tent.All(x => chests.Sum(ch => ch.GetInventory().GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == x.item).Sum(i => i.m_stack)) >= x.n)) want.AddRange(tent);
            return want;
        }
        public const int CookFood = 10;

        public static string RoleOf(Brain b) => b?.View != null && b.View.IsValid() ? b.View.GetZDO().GetString(RoleKey, "") : "";
        public static bool Out(Brain b) => RoleOf(b) != "" && Follow.LeaderId(b) != 0;
        // Why this one can't take that role now ("" if they can).
        public static string CantTake(Brain b, string role)
        {
            if (b == null) return "not here";
            if (Follow.LeaderId(b) != 0) return "already out with someone";
            if (role == "guard" && !Gear.CanFight(b)) return "no weapon";
            return "";
        }

        // Out with the jarl in that role: the follow, the role, and the role's packing.
        public static void Join(Brain b, long leader, string name, string role)
        {
            if (!Roles.Contains(role)) return;
            b.View.GetZDO().Set(RoleKey, role);
            if (!Crews.TryGetValue(leader, out var ids)) Crews[leader] = ids = new List<ZDOID>();
            if (!ids.Contains(b.View.GetZDO().m_uid)) ids.Add(b.View.GetZDO().m_uid);
            Follow.Start(b, leader, name);
            Pack(b, role);
        }
        // ---- left behind: a crew member in ground that isn't loaded (two jumps too quick, a portal's far end not loaded yet) runs no
        // thinking there, so can't catch up: the jarl's game brings them to the jarl ----
        private static readonly Dictionary<long, List<ZDOID>> Crews = new Dictionary<long, List<ZDOID>>(); // (leader -> crew bodies)
        private static float _nextGather; private static bool _scanned;
        public static void Forget() { Crews.Clear(); _scanned = false; }
        public static void Tick()
        {
            Player me = Player.m_localPlayer;
            if (me == null || ZNetScene.instance == null || Time.time < _nextGather) return;
            _nextGather = Time.time + 5f;
            // (crews this game sets out with, and any crew already loaded here following this jarl: after a reload the list is empty)
            long id = me.GetPlayerID();
            foreach (Brain b in Villagers.All) if (b.View != null && b.View.IsValid() && Follow.LeaderId(b) == id && RoleOf(b) != "")
                { if (!Crews.TryGetValue(id, out var l)) Crews[id] = l = new List<ZDOID>(); if (!l.Contains(b.View.GetZDO().m_uid)) l.Add(b.View.GetZDO().m_uid); }
            // (once a world, the saved world's followers of this jarl: a crew stranded before a restart or a reload is found too)
            if (!_scanned)
            {
                _scanned = true;
                int hash = VillagerPrefab.Name.GetStableHashCode();
                var all = AccessTools.FieldRefAccess<ZDOMan, Dictionary<ZDOID, ZDO>>("m_objectsByID")(ZDOMan.instance);
                foreach (ZDO z in all.Values)
                    if (z.GetPrefab() == hash && z.GetLong(Follow.Key, 0L) == id && z.GetString(RoleKey, "") != "")
                    { if (!Crews.TryGetValue(id, out var l)) Crews[id] = l = new List<ZDOID>(); if (!l.Contains(z.m_uid)) l.Add(z.m_uid); }
            }
            if (!Crews.TryGetValue(id, out var ids) || ids.Count == 0) return;
            int k = 0;
            foreach (ZDOID zid in ids.ToList())
            {
                ZDO z = ZDOMan.instance.GetZDO(zid);
                if (z == null || z.GetLong(Follow.Key, 0L) != id) { ids.Remove(zid); continue; }
                if (ZNetScene.instance.FindInstance(z) != null || Utils.DistanceXZ(z.GetPosition(), me.transform.position) < 60f) continue;
                if (me.IsTeleporting() || me.IsAttachedToShip() || me.GetStandingOnShip() != null) continue; // (not while the jarl's between places or at sea)
                float a = k++ * 2.1f;
                Vector3 to = me.transform.position - me.transform.forward * 3f + new Vector3(Mathf.Sin(a) * 2.5f, 0.3f, Mathf.Cos(a) * 2.5f);
                if (!z.IsOwner()) z.SetOwner(ZDOMan.GetSessionID());
                z.SetPosition(to);
            }
        }
        // Home: the role goes with the follow (what's left of the packing goes back to the stores as any carrying does).
        public static void Ended(Brain b) { if (b?.View != null && b.View.IsValid()) b.View.GetZDO().Set(RoleKey, ""); }

        private static void Pack(Brain b, string role)
        {
            Colony c = b.Colony;
            if (c == null || !c.Valid) return;
            Inventory bag = b.Body.GetInventory();
            var chests = Stock.ChestsFor(b).Where(x => x.category != "treasury" && !ContainerAccess.InUse(x.chest)).Select(x => x.chest).ToList();
            int packed = 0; string said = null;
            if (role == "cook")
            {
                // (the most filling first: cooked meat and stews before raw carrots; three kinds at most, so the jarl gets a mix)
                var all = chests.SelectMany(ch => ch.GetInventory().GetAllItems().Where(i => Stock.Edible(i) && !Stock.Kept(c, ch, i)).Select(i => (ch, i))).ToList();
                var kinds = all.Select(x => x.i.m_shared.m_name).Distinct().OrderByDescending(n => all.First(x => x.i.m_shared.m_name == n).i.m_shared.m_food).Take(3).ToList();
                foreach (string kind in kinds)
                    foreach (var (ch, food) in all.Where(x => x.i.m_shared.m_name == kind))
                    {
                        int want = Mathf.CeilToInt(CookFood / (float)kinds.Count);
                        if (packed >= CookFood || bag.GetEmptySlots() <= 1 || bag.CountItems(kind) >= want) break;
                        packed += Stock.Take(ch, bag, food, Mathf.Min(food.m_stack, Mathf.Min(CookFood - packed, want - bag.CountItems(kind))));
                    }
                said = packed > 0 ? $"I've packed {packed} things to eat." : "There's nothing in the stores to pack: we'll eat what we find.";
            }
            if (Kit.ContainsKey(role) || CampingKit && role != "guard")
            {
                var got = new List<string>();
                var want = (Kit.TryGetValue(role, out var kit) ? kit : new (string, int)[0]).Concat(CampingPack(b, role, chests))
                    .GroupBy(x => x.Item1).Select(g => (item: g.Key, n: g.Sum(x => x.Item2))).ToList();
                foreach (var (item, n) in want)
                {
                    int left = n;
                    foreach (Container ch in chests)
                        foreach (ItemDrop.ItemData i in ch.GetInventory().GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == item).ToList())
                        {
                            if (left <= 0 || bag.GetEmptySlots() <= 1) break;
                            left -= Stock.Take(ch, bag, i, Mathf.Min(i.m_stack, left));
                        }
                    if (left < n) got.Add($"{n - left} {Localization.instance.Localize(ObjectDB.instance.GetItemPrefab(item)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name ?? item).ToLowerInvariant()}");
                }
                said = (said != null ? said + " " : "") + (got.Count > 0 ? $"Packed: {string.Join(", ", got)}." : said != null ? "No hides for a bedroll." : "The stores are bare: I've nothing to carry.");
            }
            if (said != null) b.Say(said, 6);
        }

        // ---- a camp: a fire, a chest and a workbench where the jarl stands, paid from what the crew carries ----
        public const string CampKey = "jarldom_camp"; // (on the camp's pieces: the colony's id)
        public static readonly string[] CampPieces = { "fire_pit", "piece_chest_wood", "piece_workbench" };
        public const float NoCampNearHome = 60f;
        internal static bool TestAnywhere; // (the jarldom expedition camp test, by the colony)
        public static List<Brain> CrewOf(long leader) => Villagers.All.Where(b => b.Body != null && Follow.LeaderId(b) == leader && RoleOf(b) != "").ToList();
        // What the camp costs, all three pieces (the game's own prices).
        public static List<(ItemDrop item, int n)> CampCost()
        {
            var cost = new Dictionary<ItemDrop, int>();
            foreach (string name in CampPieces)
                foreach (Piece.Requirement r in ZNetScene.instance?.GetPrefab(name)?.GetComponent<Piece>()?.m_resources ?? new Piece.Requirement[0])
                    if (r.m_resItem != null) cost[r.m_resItem] = (cost.TryGetValue(r.m_resItem, out int n) ? n : 0) + r.m_amount;
            return cost.Select(kv => (kv.Key, kv.Value)).ToList();
        }
        // Under water there, or within a few metres (a camp's chest and bench stand out to each side).
        public static bool Wet(Vector3 at, float around)
        {
            float sea = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;
            foreach (Vector3 o in new[] { Vector3.zero, Vector3.right * around, Vector3.left * around, Vector3.forward * around, Vector3.back * around })
                if (ZoneSystem.instance.GetSolidHeight(at + o, out float h) && h < sea + 0.3f) return true;
            return false;
        }
        // Why the crew can't make camp here ("" if they can).
        public static string CantCamp(List<Brain> crew, Vector3 at)
        {
            if (crew.Count == 0) return "no crew is out with you";
            Colony home = crew[0].Colony;
            if (!TestAnywhere && Colony.All.Any(c => c.Valid && Utils.DistanceXZ(at, c.Position) < c.Radius + NoCampNearHome)) return "you're near a colony: no need for a camp here";
            if (Wet(at, 3.5f)) return "the ground's under water here: find dry ground";
            var missing = CampCost().Where(x => crew.Sum(b => b.Body.GetInventory().CountItems(x.item.m_itemData.m_shared.m_name)) < x.n)
                .Select(x => $"{x.n - crew.Sum(b => b.Body.GetInventory().CountItems(x.item.m_itemData.m_shared.m_name))} {Localization.instance.Localize(x.item.m_itemData.m_shared.m_name).ToLowerInvariant()}").ToList();
            if (missing.Count > 0) return "the crew carries too little: short " + string.Join(", ", missing) + " (a carrier packs a camp's makings)";
            return "";
        }

        // ---- an outpost raised at a camp: high-seat pillars beside it, paid from what the crew carries, linked to the crew's home ----
        public static List<(ItemDrop item, int n)> PillarsCost()
        {
            var cost = new Dictionary<ItemDrop, int>();
            foreach (Piece.Requirement r in Pieces.Get(Pieces.PillarsName)?.GetComponent<Piece>()?.m_resources ?? new Piece.Requirement[0])
                if (r.m_resItem != null) cost[r.m_resItem] = (cost.TryGetValue(r.m_resItem, out int n) ? n : 0) + r.m_amount;
            return cost.Select(kv => (kv.Key, kv.Value)).ToList();
        }
        // (what the crew carries, and what's in the camp chest if there's one)
        private static int Have(List<Brain> crew, Container chest, string name) => crew.Sum(b => b.Body.GetInventory().CountItems(name)) + (chest != null ? chest.GetInventory().CountItems(name) : 0);
        private static List<string> Short(List<Brain> crew, Container chest, List<(ItemDrop item, int n)> cost) => cost
            .Where(x => Have(crew, chest, x.item.m_itemData.m_shared.m_name) < x.n)
            .Select(x => $"{x.n - Have(crew, chest, x.item.m_itemData.m_shared.m_name)} {Localization.instance.Localize(x.item.m_itemData.m_shared.m_name).ToLowerInvariant()}").ToList();
        private static Container CampChest(Player jarl) { var st = new CampState(); return jarl != null && AtCamp(jarl, st) ? st.Chest : null; }
        public static string CantRaise(List<Brain> crew, Vector3 at)
        {
            Container chest = crew.Count > 0 ? CampChest(Follow.Leader(crew[0])) : null;
            if (crew.Count == 0) return "no crew is out with you";
            var near = Outposts.All().Where(x => Utils.DistanceXZ(x.at, at) < Renown.MinApart).OrderBy(x => Utils.DistanceXZ(x.at, at)).ToList();
            if (near.Count > 0) return $"a colony stands {Mathf.RoundToInt(Utils.DistanceXZ(near[0].at, at))} m away: an outpost needs {Renown.MinApart:0} m from any other";
            if (Wet(at, 2f)) return "the ground's under water here: find dry ground";
            var missing = Short(crew, chest, PillarsCost());
            if (missing.Count > 0) return "the crew carries too little for the high-seat pillars: short " + string.Join(", ", missing);
            return "";
        }
        private static readonly List<(Vector3 at, string home, string who, float until)> Pending = new List<(Vector3, string, string, float)>();
        // The crew member raising it: paid, pillars up (the new colony founds itself on this game), the link made once it has.
        public static void Raise(Brain b, Vector3 at, float yaw)
        {
            var crew = CrewOf(Follow.LeaderId(b));
            // (clear of the camp: six metres out from its fire, on the jarl's side)
            Player lead = Follow.Leader(b); var cs = new CampState();
            if (lead != null && AtCamp(lead, cs) && Utils.DistanceXZ(at, cs.Fire.transform.position) < 6f)
            {
                Vector3 away = lead.transform.position - cs.Fire.transform.position; away.y = 0;
                if (away.sqrMagnitude < 1f) away = -lead.transform.forward;
                at = cs.Fire.transform.position + away.normalized * 6f;
            }
            string why = CantRaise(crew, at);
            if (why != "") { b.Say("We can't raise an outpost: " + why + ".", 6); return; }
            Container chest = CampChest(Follow.Leader(b));
            foreach (var (item, n) in PillarsCost())
            {
                int left = n; string name = item.m_itemData.m_shared.m_name;
                foreach (Brain m in crew.OrderBy(m => RoleOf(m) == "carrier" ? 0 : 1)) { int take = Mathf.Min(left, m.Body.GetInventory().CountItems(name)); if (take > 0) { m.Body.GetInventory().RemoveItem(name, take); left -= take; } if (left <= 0) break; }
                if (left > 0 && chest != null) { ContainerAccess.TakeOver(chest); int take = Mathf.Min(left, chest.GetInventory().CountItems(name)); chest.GetInventory().RemoveItem(name, take); left -= take; }
            }
            if (ZoneSystem.instance.GetSolidHeight(at, out float h)) at.y = h;
            Player jarl = Follow.Leader(b);
            GameObject go = Object.Instantiate(Pieces.Get(Pieces.PillarsName), at, Quaternion.Euler(0, yaw, 0));
            Piece piece = go.GetComponent<Piece>();
            piece?.SetCreator(jarl != null ? jarl.GetPlayerID() : 0L, Splatform.PlatformManager.DistributionPlatform.LocalUser.PlatformUserID);
            piece?.m_placeEffect.Create(at, go.transform.rotation, go.transform);
            go.GetComponent<WearNTear>()?.OnPlaced();
            Pending.Add((at, b.S.Colony, jarl?.GetPlayerName() ?? "", Time.time + 300f)); // (a new colony settles in 30 s or so before it ticks)
            b.Say(new[] { "The pillars stand. This is ours now, jarl.", "There: the high seat's pillars. Odin will see this place.", "An outpost, here. Let them come." }[Random.Range(0, 3)], 7);
        }
        // From every colony's tick on its owner: a colony just founded where an expedition raised pillars, linked to the crew's home.
        public static void LinkPending(Colony c)
        {
            if (Pending.Count == 0) return;
            for (int i = Pending.Count - 1; i >= 0; i--)
            {
                var p = Pending[i];
                if (Time.time > p.until) { Pending.RemoveAt(i); continue; }
                if (!c.IsOwner || c.R == null || Utils.DistanceXZ(c.Position, p.at) > 3f) continue;
                Outposts.LinkFromExpedition(c, p.home, p.who);
                Pending.RemoveAt(i);
            }
        }

        // ---- at camp: the jarl near one of the camps the crew made, each to their role there ----
        public const float CampReach = 30f;
        internal sealed class CampState
        {
            public Fireplace Fire; public Container Chest; public float NextLook, Next, Until; public int Phase; public Vector3 Goal; public ItemDrop Item;
            public GameObject Bed; // (a spot by the fire to sleep on at night)
            public readonly Dictionary<int, Bed> Rolls = new Dictionary<int, Bed>(); // (the camp's bedrolls and tent by slot, with the Camping mod)
            public Bed OnRoll; public int Slot = -1; public float Since; // (tonight's bedroll, lain on)
            public void Wake(Brain b)
            {
                Slot = -1; Since = 0;
                if (Bed == null && OnRoll == null) return;
                b.Stand(); if (Bed != null) Object.Destroy(Bed); Bed = null; OnRoll = null;
            }
        }
        private static readonly List<Piece> Found = new List<Piece>();
        // Is the jarl at a camp (looked for every two seconds): its fire, and its chest.
        public static bool AtCamp(Player jarl, CampState st)
        {
            if (Time.time >= st.NextLook)
            {
                st.NextLook = Time.time + 2f; st.Fire = null; st.Chest = null; st.Rolls.Clear();
                Found.Clear(); Piece.GetAllPiecesInRadius(jarl.transform.position, CampReach, Found);
                foreach (Piece p in Found)
                {
                    ZDO z = p.GetComponent<ZNetView>()?.GetZDO();
                    if (z == null || z.GetString(CampKey, null) == null) continue;
                    if (p.GetComponent<Bed>() is Bed roll && z.GetInt(SlotKey, -1) is int slot && slot >= 0 && !st.Rolls.ContainsKey(slot)) st.Rolls[slot] = roll;
                    if (st.Fire == null && p.GetComponent<Fireplace>() is Fireplace f) st.Fire = f;
                    if (st.Chest == null && p.GetComponent<Container>() is Container ch) st.Chest = ch;
                }
            }
            return st.Fire != null;
        }
        // What lies about near the camp (not coins: those are someone's).
        private static List<ItemDrop> Lying(Vector3 fire) => Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None)
            .Where(i => i != null && Utils.DistanceXZ(i.transform.position, fire) < 25f && i.m_itemData != null && !(i.m_itemData.m_dropPrefab != null && i.m_itemData.m_dropPrefab.name == "Coins")).ToList();
        // One turn of the role's camp work (a few times a second); what they're doing.
        public static string CampWork(Brain b, Player jarl, CampState st)
        {
            Vector3 fire = st.Fire.transform.position;
            int k = (b.S.Vid.GetStableHashCode() & 0x7fffffff) % 6;
            // (night at camp: all but the guards sleep round the fire, the guards keep watch)
            double hour = Clock.HourOfDay(ZNet.instance.GetTimeSeconds());
            bool night = hour >= Schedule.SleepAt || hour < Schedule.WakeAt;
            if (night && RoleOf(b) != "guard")
            {
                // (their own bedroll if the camp has one for them: slot 1 for the first sleeper of the crew, and on)
                if (st.Slot < 0)
                {
                    var sleepers = CrewOf(Follow.LeaderId(b)).Where(m => RoleOf(m) != "guard").OrderBy(m => m.S.Vid, System.StringComparer.Ordinal).ToList();
                    st.Slot = sleepers.IndexOf(b) + 1; st.Since = Time.time;
                }
                if ((object)st.OnRoll != null && (!st.Rolls.TryGetValue(st.Slot, out Bed was) || was != st.OnRoll || st.OnRoll == null)) { b.Stand(); st.OnRoll = null; } // (packed up under them)
                if (st.Rolls.TryGetValue(st.Slot, out Bed roll) && roll != null && st.Bed == null)
                {
                    Transform lie = roll.m_spawnPoint != null ? roll.m_spawnPoint : roll.transform;
                    if (st.OnRoll != roll)
                    {
                        // (beside it is near enough: under a lean-to the way in is low; walked a while and not there, lain on anyway)
                        if (Utils.DistanceXZ(b.Pos, lie.position) > 1.6f && !b.Move.Failed && Time.time - st.Since < 40f)
                        { if (!b.Move.Moving) b.Move.To(Colony.Spot(lie.position, 1.2f) ?? lie.position, 1f); return "going to my bedroll"; }
                        b.Sit(lie, "attach_bed", Vector3.zero); st.OnRoll = roll;
                    }
                    Mood.Satisfy(b.S, Need.Rest, (float)(b.GameDt / Clock.Hour * 13)); // (furs under them: near a bed's rest)
                    return "asleep on a bedroll at camp";
                }
                float a = k * 60f * Mathf.Deg2Rad;
                Vector3 spot = fire + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * 2.2f;
                if (st.Bed == null)
                {
                    if (Utils.DistanceXZ(b.Pos, spot) > 1.2f) { if (!b.Move.Moving) b.Move.To(Colony.Spot(spot, 1f) ?? spot, 0.8f); return "going to sleep by the camp fire"; }
                    st.Bed = new GameObject("JarldomCampSleep");
                    st.Bed.transform.position = b.Pos;
                    Vector3 toFire = fire - b.Pos; toFire.y = 0;
                    st.Bed.transform.rotation = Quaternion.LookRotation(toFire.sqrMagnitude > 0.01f ? toFire.normalized : Vector3.forward) * Quaternion.Euler(0, 90, 0);
                    b.Sit(st.Bed.transform, "attach_bed", Vector3.zero);
                }
                Mood.Satisfy(b.S, Need.Rest, (float)(b.GameDt / Clock.Hour * 10)); // (a night by the fire: most of a night's rest, if not a bed's)
                return "asleep by the camp fire";
            }
            st.Wake(b);
            switch (RoleOf(b))
            {
                case "guard":
                {
                    // (a post a little out from the fire, a different side every minute or so, looking out)
                    float a = (k * 60f + Mathf.Floor(Time.time / 70f) * 97f) * Mathf.Deg2Rad;
                    Vector3 post = fire + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * 8f;
                    if (Utils.DistanceXZ(b.Pos, post) > 2f) { if (!b.Move.Moving || Vector3.Distance(b.Move.Goal, post) > 2f) b.Move.To(Colony.Spot(post, 2f) ?? post, 1.2f); }
                    else { Vector3 outward = b.Pos - fire; outward.y = 0; if (outward.sqrMagnitude > 0.01f) b.Ai.LookTowards(outward.normalized); }
                    return "keeping watch at the camp";
                }
                case "cook":
                {
                    if (Utils.DistanceXZ(b.Pos, fire) > 2.5f) { if (!b.Move.Moving) b.Move.To(fire, 2f); return "going to the camp fire"; }
                    if (Time.time >= st.Next)
                    {
                        st.Next = Time.time + 15f;
                        ZNetView v = st.Fire.GetComponent<ZNetView>();
                        string wood = st.Fire.m_fuelItem != null ? st.Fire.m_fuelItem.m_itemData.m_shared.m_name : "$item_wood";
                        if (v != null && v.IsValid() && v.GetZDO().GetFloat(ZDOVars.s_fuel) < st.Fire.m_maxFuel - 1f)
                        {
                            // (from their own pack, else the camp chest)
                            if (b.Body.GetInventory().CountItems(wood) > 0) { b.Body.GetInventory().RemoveItem(wood, 1); v.InvokeRPC("RPC_AddFuel"); }
                            else if (st.Chest != null && st.Chest.GetInventory().CountItems(wood) > 0) { ContainerAccess.TakeOver(st.Chest); st.Chest.GetInventory().RemoveItem(wood, 1); v.InvokeRPC("RPC_AddFuel"); }
                        }
                    }
                    Vector3 to = fire - b.Pos; to.y = 0; if (to.sqrMagnitude > 0.01f) b.Ai.LookTowards(to.normalized);
                    return "tending the camp fire";
                }
                case "scout":
                {
                    // (out to a spot 60-90 m off, a look round from there, back to the fire, a rest; again)
                    if (st.Phase == 0)
                    {
                        float a = Random.Range(0f, Mathf.PI * 2), d = Random.Range(60f, 90f);
                        Vector3 far = fire + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * d;
                        st.Goal = Nav.Valid(out Vector3 v, far, 6f) ? v : far; st.Phase = 1; st.Until = Time.time + 45f;
                        b.Move.To(st.Goal, 3f, true);
                    }
                    if (st.Phase == 1)
                    {
                        if (b.Move.Arrived || b.Move.Failed || Time.time > st.Until || !b.Move.Moving && Utils.DistanceXZ(b.Pos, st.Goal) < 8f)
                        { Net.Explore(b.Pos, 90f); st.Phase = 2; st.Until = Time.time + 45f; b.Move.To(fire, 3f, true); }
                        else if (!b.Move.Moving) b.Move.To(st.Goal, 3f, true);
                        return "scouting round the camp";
                    }
                    if (st.Phase == 2)
                    {
                        if (Utils.DistanceXZ(b.Pos, fire) < 4f || Time.time > st.Until) { st.Phase = 3; st.Until = Time.time + 30f; }
                        else if (!b.Move.Moving) b.Move.To(fire, 3f, true);
                        return "coming back to camp";
                    }
                    if (Time.time > st.Until) st.Phase = 0;
                    return "resting at the camp fire";
                }
                case "carrier":
                {
                    // (a load of five kinds, or nothing more lying about: into the camp chest; else what lies about near the camp, picked up)
                    var loot = b.Body.GetInventory().GetAllItems().Where(i => !i.m_equipped && Work.Loot(i) && !(i.m_dropPrefab != null && (i.m_dropPrefab.name == "Wood" || i.m_dropPrefab.name == "Stone"))).ToList();
                    if (st.Chest != null && loot.Count > 0 && (st.Phase == 1 || Time.time >= st.Next) && (loot.Count >= 5 || st.Phase == 1 || st.Item == null && Time.time >= st.Next && Lying(fire).Count == 0))
                    {
                        st.Phase = 1; st.Item = null;
                        Stock.Walk w = Stock.WalkTo(b, st.Chest, 3.5f);
                        if (w == Stock.Walk.Walking) return "taking things to the camp chest";
                        if (w == Stock.Walk.Unreachable) { st.Phase = 0; st.Next = Time.time + 60f; return "can't get to the camp chest"; } // (kept in the pack a while)
                        if (w == Stock.Walk.Reached)
                        {
                            ContainerAccess.TakeOver(st.Chest);
                            foreach (var i in loot) if (st.Chest.GetInventory().CanAddItem(i)) { st.Chest.GetInventory().AddItem(i.Clone()); b.Body.GetInventory().RemoveItem(i); }
                        }
                        st.Phase = 0; st.Next = Time.time + 1f;
                        return "taking things to the camp chest";
                    }
                    if (st.Item == null && Time.time >= st.Next) { st.Next = Time.time + 3f; st.Item = Lying(fire).OrderBy(i => Utils.DistanceXZ(i.transform.position, b.Pos)).FirstOrDefault(); }
                    if (st.Item != null)
                    {
                        if (!st.Item) { st.Item = null; return "gathering for the camp"; }
                        if (!WorkTask.InReach(b, st.Item.transform.position)) { if (!b.Move.Moving) b.Move.To(st.Item.transform.position, 1.2f); return "gathering for the camp"; }
                        if (WorkTask.PickUpNow(b, st.Item) != WorkTask.Got.Waiting) st.Item = null;
                        return "gathering for the camp";
                    }
                    if (Utils.DistanceXZ(b.Pos, fire) > 5f && !b.Move.Moving) b.Move.To(fire, 3.5f);
                    return "resting at the camp fire";
                }
                default:
                    if (Utils.DistanceXZ(b.Pos, fire) > 5f && !b.Move.Moving) b.Move.To(fire, 3.5f);
                    return "resting at the camp fire";
            }
        }

        // While following: what the role does on the road (from FollowTask, a few times a second).
        public static void OnTheRoad(Brain b, Player jarl, ref float next)
        {
            if (Time.time < next || jarl == null) return;
            switch (RoleOf(b))
            {
                case "scout":
                    next = Time.time + 8f;
                    Net.Explore(jarl.transform.position, 140f); // (the jarl's own reveal is about 100 m: the scout's eyes go further)
                    break;
                case "cook":
                    next = Time.time + 20f;
                    // (the jarl on this game, running low: fewer than two foods going, or one nearly gone)
                    if (jarl != Player.m_localPlayer) break;
                    var foods = jarl.GetFoods();
                    if (foods.Count >= 2 && foods.All(f => f.m_time > 120f)) break;
                    ItemDrop.ItemData give = b.Body.GetInventory().GetAllItems().Where(Stock.Edible).Where(i => !foods.Any(f => f.m_item?.m_shared.m_name == i.m_shared.m_name))
                        .OrderByDescending(i => i.m_shared.m_food + i.m_shared.m_foodStamina).FirstOrDefault();
                    if (give == null) break;
                    var one = give.Clone(); one.m_stack = 1;
                    if (!jarl.GetInventory().AddItem(one)) break;
                    b.Body.GetInventory().RemoveItem(give, 1);
                    b.Say(new[] { $"Here, jarl: eat something.", $"You look hungry, jarl. Here.", $"Eat, jarl. You'll need it." }[Random.Range(0, 3)], 5);
                    jarl.Message(MessageHud.MessageType.TopLeft, $"{b.S.Name} gives you {Localization.instance.Localize(give.m_shared.m_name)}.");
                    next = Time.time + 90f;
                    break;
                default: next = Time.time + 30f; break;
            }
        }
    }

    // The camp made: one of the crew (a carrier, if there is one) goes to the spot and puts the pieces up, the cost taken from the crew.
    internal sealed class CampTask : Task
    {
        private readonly Vector3 _at; private readonly float _yaw; private float _until; private bool _made;
        public CampTask(Vector3 at, float yaw) { _at = at; _yaw = yaw; Doing = "making camp"; }
        public override bool Interruptible => false;
        public override void Start(Brain b) { _until = Time.time + 40f; b.Move.To(_at, 2.5f); }
        public override void Tick(Brain b)
        {
            if (_made || Time.time > _until) { if (!_made) b.Say("I couldn't get there to make camp.", 5); Done = true; return; }
            if (Utils.DistanceXZ(b.Pos, _at) > 3.5f && !b.Move.Failed) { if (!b.Move.Moving) b.Move.To(_at, 2.5f); return; }
            long leader = Follow.LeaderId(b);
            var crew = Expedition.CrewOf(leader);
            string why = Expedition.CantCamp(crew, _at);
            if (why != "") { b.Say("We can't make camp: " + why + ".", 6); Done = true; return; }
            // (paid in full first, carriers' packs before the rest)
            foreach (var (item, n) in Expedition.CampCost())
            {
                int left = n; string name = item.m_itemData.m_shared.m_name;
                foreach (Brain m in crew.OrderBy(m => Expedition.RoleOf(m) == "carrier" ? 0 : 1))
                {
                    int take = Mathf.Min(left, m.Body.GetInventory().CountItems(name));
                    if (take > 0) { m.Body.GetInventory().RemoveItem(name, take); left -= take; }
                    if (left <= 0) break;
                }
            }
            Quaternion rot = Quaternion.Euler(0, _yaw, 0);
            Vector3 right = rot * Vector3.right;
            string colony = b.S.Colony ?? "";
            // (the fire on walkable ground near the spot; the chest and the bench set out from where the fire landed, on the ground there)
            Vector3 fire = Colony.Spot(_at, 1.5f) ?? _at;
            GameObject Put(string prefab, Vector3 spot, Quaternion r)
            {
                GameObject p = ZNetScene.instance.GetPrefab(prefab);
                if (p == null) return null;
                Vector3 at = spot;
                if (ZoneSystem.instance.GetSolidHeight(at, out float h)) at.y = h;
                GameObject made = Object.Instantiate(p, at, r);
                Piece piece = made.GetComponent<Piece>();
                Player jarl = Follow.Leader(b);
                if (piece != null) { piece.SetCreator(jarl != null ? jarl.GetPlayerID() : 0L, Splatform.PlatformManager.DistributionPlatform.LocalUser.PlatformUserID); piece.m_placeEffect.Create(at, r, made.transform); }
                made.GetComponent<WearNTear>()?.OnPlaced();
                made.GetComponent<ZNetView>()?.GetZDO()?.Set(Expedition.CampKey, colony);
                return made;
            }
            Put("fire_pit", fire, rot);
            // (the chest and the bench to either side, turned a little round the fire if a tree or a rock is in the way)
            if (!Clear(fire, right, 2.8f, new Vector3(0.8f, 0.45f, 0.45f), out Vector3 chestAt, out Vector3 toChest, 0, 25, -25, 50, -50)) { chestAt = fire + right * 2.8f; toChest = right; }
            Put("piece_chest_wood", chestAt, Quaternion.LookRotation(toChest) * Quaternion.Euler(0, 180, 0)); // (facing the fire)
            if (!Clear(fire, -right, 3.4f, new Vector3(1f, 0.6f, 0.6f), out Vector3 benchAt, out Vector3 toBench, 0, -25, 25, -50, 50)) { benchAt = fire - right * 3.4f; toBench = -right; }
            Put("piece_workbench", benchAt, Quaternion.LookRotation(toBench) * Quaternion.Euler(0, 180, 0));
            Physics.SyncTransforms();
            int rolls = Expedition.CampingKit ? Furnish(b, crew, fire, rot, Put) : 0;
            _made = true;
            b.Say(rolls > 0 ? new[] { "Camp's made, jarl: furs down round the fire.", "There: a fire, a chest, a bench, and somewhere to sleep.", "Bedrolls out. We'll sleep warm tonight." }[Random.Range(0, 3)]
                : new[] { "Camp's made, jarl.", "There: a fire, a chest and a bench. Camp.", "We'll sleep warm tonight. Camp's up." }[Random.Range(0, 3)], 6);
            b.Colony?.Log("text", b.S.Name, "", $"{b.S.Name} made camp out in the wilds with {Follow.Leader(b)?.GetPlayerName() ?? "the jarl"}.");
            b.S.Bump("camps"); b.Dirty();
        }

        // The Camping mod's pieces, as far as what the crew carries pays: behind the fire, the jarl's place (a tent in the cold, else
        // a lean-to over the jarl's bedroll and the first sleeper's), the other sleepers' bedrolls round the fire's front, feet to it.
        // Each bedroll or tent is marked with its slot: 0 the jarl's, 1.. the sleepers' in turn. How many bedrolls were laid.
        private static int Furnish(Brain b, List<Brain> crew, Vector3 fire, Quaternion rot, System.Func<string, Vector3, Quaternion, GameObject> put)
        {
            int sleepers = crew.Count(m => Expedition.RoleOf(m) != "guard"), laid = 0;
            Vector3 forward = rot * Vector3.forward, right = rot * Vector3.right;
            Quaternion away = rot * Quaternion.Euler(0, 180, 0); // (the back pieces' open fronts to the fire)
            Heightmap.Biome biome = WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(fire) : Heightmap.Biome.Meadows;
            bool cold = biome == Heightmap.Biome.Mountain || biome == Heightmap.Biome.DeepNorth;
            void Mark(GameObject go, int slot) => go?.GetComponent<ZNetView>()?.GetZDO()?.Set(Expedition.SlotKey, slot);
            var spots = new List<(Vector3 at, Quaternion r)>(); // (the sleepers' bedrolls, in slot order)
            Vector3? roofAt = null; Quaternion roofTurn = away;
            // (the jarl's place behind the fire, or turned round it some way if the trees stand close)
            float[] behind = { 180, 150, 210, 120, 240 };
            if (cold && Clear(fire, forward, 3.9f, new Vector3(1.6f, 1.2f, 1.7f), out Vector3 tentAt, out Vector3 toTent, behind) && Pay(crew, Expedition.CostOf(Expedition.TentName)))
            { Mark(put(Expedition.TentName, tentAt, Quaternion.LookRotation(toTent)), 0); laid++; }
            else if (Clear(fire, forward, 3.4f, new Vector3(1.5f, 0.9f, 1.7f), out Vector3 backAt, out Vector3 toBack, behind))
            {
                Quaternion turn = Quaternion.LookRotation(toBack); Vector3 across = turn * Vector3.right;
                bool roof = Pay(crew, Expedition.CostOf(Expedition.LeanToName));
                // (bedrolls first: the lean-to's slab would be the ground under them)
                if (Pay(crew, Expedition.CostOf(Expedition.BedrollName))) { Mark(put(Expedition.BedrollName, backAt - across * 0.7f - toBack * 0.2f, turn), 0); laid++; }
                spots.Add((backAt + across * 0.7f - toBack * 0.2f, turn));
                if (roof) { roofAt = backAt; roofTurn = turn; }
            }
            Physics.SyncTransforms();
            // (the rest round the front of the fire, heads out; a spot with a tree in it turned aside, or left out)
            foreach (var (deg, far) in new[] { 0f, -32f, 32f, -64f, 64f }.Select(d => (d, 3.1f)).Concat(new[] { 0f, -24f, 24f, -48f, 48f, -72f, 72f }.Select(d => (d, 4.4f))))
            {
                if (spots.Count >= sleepers) break;
                if (!Clear(fire, Quaternion.AngleAxis(deg, Vector3.up) * forward, far, new Vector3(0.5f, 0.3f, 1.05f), out Vector3 at, out Vector3 dir, 0, 8, -8)) continue;
                if (spots.Any(x => Overlap(x.at, x.r * Vector3.forward, at, dir)) || roofAt.HasValue && Utils.DistanceXZ(roofAt.Value, at) < 2.7f) continue;
                spots.Add((at, Quaternion.LookRotation(dir)));
            }
            for (int i = 0; i < sleepers && i < spots.Count; i++)
            {
                if (!Pay(crew, Expedition.CostOf(Expedition.BedrollName))) break;
                Mark(put(Expedition.BedrollName, spots[i].at, spots[i].r), i + 1); laid++;
            }
            if (roofAt.HasValue) put(Expedition.LeanToName, roofAt.Value, roofTurn);
            return laid;
        }
        // Room for a piece of that half-size (x across, z along the way out) at that far out from the fire, the way given or turned
        // by one of the offsets (degrees, tried in turn): no tree, rock or piece in it. Where it'll stand, and the way out to it.
        internal static bool Clear(Vector3 fire, Vector3 way, float far, Vector3 half, out Vector3 at, out Vector3 dir, params float[] offsets)
        {
            if (_solid == -1) _solid = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "vehicle");
            way.y = 0; way.Normalize();
            foreach (float deg in offsets)
            {
                dir = Quaternion.AngleAxis(deg, Vector3.up) * way;
                at = fire + dir * far;
                at.y = ZoneSystem.instance.GetGroundHeight(at);
                if (!Physics.CheckBox(at + Vector3.up * (half.y + 0.15f), half, Quaternion.LookRotation(dir), _solid, QueryTriggerInteraction.Ignore)) return true;
            }
            at = fire; dir = way; return false;
        }
        private static int _solid = -1;
        // Two bedrolls (a metre wide, two long) laid there those ways would lie on each other.
        private static bool Overlap(Vector3 a, Vector3 aWay, Vector3 b, Vector3 bWay)
        {
            for (int i = -2; i <= 2; i++) for (int j = -2; j <= 2; j++)
                if (Utils.DistanceXZ(a + aWay * (i * 0.45f), b + bWay * (j * 0.45f)) < 1.05f) return true;
            return false;
        }
        // Paid from the crew's packs (carriers' first), only if all of it's there.
        private static bool Pay(List<Brain> crew, List<(ItemDrop item, int n)> cost)
        {
            if (cost.Count == 0 || cost.Any(x => crew.Sum(m => m.Body.GetInventory().CountItems(x.item.m_itemData.m_shared.m_name)) < x.n)) return false;
            foreach (var (item, n) in cost)
            {
                int left = n; string name = item.m_itemData.m_shared.m_name;
                foreach (Brain m in crew.OrderBy(m => Expedition.RoleOf(m) == "carrier" ? 0 : 1))
                {
                    int take = Mathf.Min(left, m.Body.GetInventory().CountItems(name));
                    if (take > 0) { m.Body.GetInventory().RemoveItem(name, take); left -= take; }
                    if (left <= 0) break;
                }
            }
            return true;
        }
    }
}
