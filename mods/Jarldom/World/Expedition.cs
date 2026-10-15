using System.Collections.Generic;
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
        // What each role packs: (prefab, amount); the cook's food is any food, counted in items.
        private static readonly Dictionary<string, (string item, int n)[]> Kit = new Dictionary<string, (string, int)[]>
        {
            ["carrier"] = new[] { ("Wood", 25), ("Stone", 5) },
        };
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
            Follow.Start(b, leader, name);
            Pack(b, role);
        }
        // Home: the role goes with the follow (what's left of the packing goes back to the stores as any carrying does).
        public static void Ended(Brain b) { if (b?.View != null && b.View.IsValid()) b.View.GetZDO().Set(RoleKey, ""); }

        private static void Pack(Brain b, string role)
        {
            Colony c = b.Colony;
            if (c == null || !c.Valid) return;
            Inventory bag = b.Body.GetInventory();
            var chests = Stock.ChestsFor(b).Where(x => x.category != "treasury" && !ContainerAccess.InUse(x.chest)).Select(x => x.chest).ToList();
            int packed = 0;
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
                b.Say(packed > 0 ? $"I've packed {packed} things to eat." : "There's nothing in the stores to pack: we'll eat what we find.", 5);
            }
            else if (Kit.TryGetValue(role, out var kit))
            {
                var got = new List<string>();
                foreach (var (item, n) in kit)
                {
                    int left = n;
                    foreach (Container ch in chests)
                        foreach (ItemDrop.ItemData i in ch.GetInventory().GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == item).ToList())
                        {
                            if (left <= 0 || bag.GetEmptySlots() <= 1) break;
                            left -= Stock.Take(ch, bag, i, Mathf.Min(i.m_stack, left));
                        }
                    if (left < n) got.Add($"{n - left} {item.ToLowerInvariant()}");
                }
                b.Say(got.Count > 0 ? $"Packed: {string.Join(", ", got)}." : "The stores are bare: I've nothing to carry.", 5);
            }
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
        // Why the crew can't make camp here ("" if they can).
        public static string CantCamp(List<Brain> crew, Vector3 at)
        {
            if (crew.Count == 0) return "no crew is out with you";
            Colony home = crew[0].Colony;
            if (!TestAnywhere && Colony.All.Any(c => c.Valid && Utils.DistanceXZ(at, c.Position) < c.Radius + NoCampNearHome)) return "you're near a colony: no need for a camp here";
            var missing = CampCost().Where(x => crew.Sum(b => b.Body.GetInventory().CountItems(x.item.m_itemData.m_shared.m_name)) < x.n)
                .Select(x => $"{x.n - crew.Sum(b => b.Body.GetInventory().CountItems(x.item.m_itemData.m_shared.m_name))} {Localization.instance.Localize(x.item.m_itemData.m_shared.m_name).ToLowerInvariant()}").ToList();
            if (missing.Count > 0) return "the crew carries too little: short " + string.Join(", ", missing) + " (a carrier packs a camp's makings)";
            return "";
        }

        // ---- at camp: the jarl near one of the camps the crew made, each to their role there ----
        public const float CampReach = 30f;
        internal sealed class CampState
        {
            public Fireplace Fire; public Container Chest; public float NextLook, Next, Until; public int Phase; public Vector3 Goal; public ItemDrop Item;
        }
        private static readonly List<Piece> Found = new List<Piece>();
        // Is the jarl at a camp (looked for every two seconds): its fire, and its chest.
        public static bool AtCamp(Player jarl, CampState st)
        {
            if (Time.time >= st.NextLook)
            {
                st.NextLook = Time.time + 2f; st.Fire = null; st.Chest = null;
                Found.Clear(); Piece.GetAllPiecesInRadius(jarl.transform.position, CampReach, Found);
                foreach (Piece p in Found)
                {
                    ZDO z = p.GetComponent<ZNetView>()?.GetZDO();
                    if (z == null || z.GetString(CampKey, null) == null) continue;
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
                    if (st.Chest != null && loot.Count > 0 && (loot.Count >= 5 || st.Phase == 1 || st.Item == null && Time.time >= st.Next && Lying(fire).Count == 0))
                    {
                        st.Phase = 1; st.Item = null;
                        Stock.Walk w = Stock.WalkTo(b, st.Chest);
                        if (w == Stock.Walk.Walking) return "taking things to the camp chest";
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
            void Put(string prefab, Vector3 spot, Quaternion r)
            {
                GameObject p = ZNetScene.instance.GetPrefab(prefab);
                if (p == null) return;
                Vector3 at = spot;
                if (ZoneSystem.instance.GetSolidHeight(at, out float h)) at.y = h;
                GameObject made = Object.Instantiate(p, at, r);
                Piece piece = made.GetComponent<Piece>();
                Player jarl = Follow.Leader(b);
                if (piece != null) { piece.SetCreator(jarl != null ? jarl.GetPlayerID() : 0L, Splatform.PlatformManager.DistributionPlatform.LocalUser.PlatformUserID); piece.m_placeEffect.Create(at, r, made.transform); }
                made.GetComponent<WearNTear>()?.OnPlaced();
                made.GetComponent<ZNetView>()?.GetZDO()?.Set(Expedition.CampKey, colony);
            }
            Put("fire_pit", fire, rot);
            Put("piece_chest_wood", fire + right * 2.8f, rot * Quaternion.Euler(0, -90, 0)); // (facing the fire)
            Put("piece_workbench", fire - right * 3.4f, rot * Quaternion.Euler(0, 90, 0));
            _made = true;
            b.Say(new[] { "Camp's made, jarl.", "There: a fire, a chest and a bench. Camp.", "We'll sleep warm tonight. Camp's up." }[Random.Range(0, 3)], 6);
            b.Colony?.Log("text", b.S.Name, "", $"{b.S.Name} made camp out in the wilds with {Follow.Leader(b)?.GetPlayerName() ?? "the jarl"}.");
            b.S.Bump("camps"); b.Dirty();
        }
    }
}
