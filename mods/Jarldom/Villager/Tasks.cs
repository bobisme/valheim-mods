using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jarldom
{
    // What a villager is doing now. A task starts, ticks a few times a second, and says when it's done.
    internal abstract class Task
    {
        public bool Done; public string Doing = "";
        public virtual bool Interruptible => true;
        public virtual void Start(Brain b) { }
        public abstract void Tick(Brain b);
        public virtual void Stop(Brain b) { b.Stand(); b.Move.Stop(); }
        public virtual string Debug(Brain b) => GetType().Name;
        protected float Started = Time.time;
    }

    internal sealed class WanderTask : Task
    {
        private readonly Vector3 _centre; private readonly float _radius; private float _until, _pauseUntil;
        public WanderTask(Vector3 centre, float radius, string doing) { _centre = centre; _radius = radius; Doing = doing; }
        public override void Start(Brain b) { _until = Time.time + UnityEngine.Random.Range(15f, 30f); }
        public override void Tick(Brain b)
        {
            if (Time.time > _until) { Done = true; return; }
            if (b.Move.Moving || Time.time < _pauseUntil) return;
            Vector3? to = Colony.Spot(_centre, _radius);
            if (to != null) b.Move.To(to.Value, 1f);
            _pauseUntil = Time.time + UnityEngine.Random.Range(4f, 10f);
        }
    }

    // A walk to somewhere, then a wait (used by work and errands).
    internal sealed class GoTask : Task
    {
        private readonly Vector3 _to; private readonly float _arrive; private readonly bool _run;
        public GoTask(Vector3 to, string doing, float arrive = 1.5f, bool run = false) { _to = to; Doing = doing; _arrive = arrive; _run = run; }
        public override void Start(Brain b) => b.Move.To(_to, _arrive, _run);
        public override void Tick(Brain b) { if (b.Move.Arrived || b.Move.Failed) Done = true; else if (!b.Move.Moving) b.Move.To(_to, _arrive, _run); }
    }

    // Sleep: to their own bed, else by the hall's fire, else by the pillars. Rest fills with the bed's comfort.
    internal sealed class SleepTask : Task
    {
        public bool JustRest;
        private Bed _bed; private Transform _spot; private GameObject _marker; private bool _down; private float _comfort; private double _since; private bool _own;
        public override bool Interruptible => false;
        public override void Start(Brain b)
        {
            Doing = JustRest ? "resting" : "going to bed";
            _bed = b.Colony.BedById(b.S.Bed);
            // The injured rest in the healer's house, if it has a bed nobody holds.
            if (JustRest && b.Colony.Best(RoomType.HealersHouse) != null)
            {
                Bed sick = b.Colony.AllBeds().FirstOrDefault(x => b.Colony.RoomAt(x.transform.position, RoomType.HealersHouse) != null && (Colony.BedOwner(x, out _) == "" || x == _bed));
                if (sick != null) { _spot = sick.m_spawnPoint != null ? sick.m_spawnPoint : sick.transform; Doing = "resting at the healer's"; b.Move.To(_spot.position, 1.2f); _own = true; return; }
            }
            if (_bed != null) { _own = true; _spot = _bed.m_spawnPoint != null ? _bed.m_spawnPoint : _bed.transform; b.Move.To(_spot.position, 1.2f); return; }
            Room hall = b.Colony.Best(RoomType.GreatHall);
            Vector3 at = hall != null ? b.Colony.Centre(hall) : b.Colony.Position + b.Colony.Seat.transform.forward * 3;
            Fireplace fire = Nearest<Fireplace>(at, 12f);
            if (fire != null) at = fire.transform.position + (b.Pos - fire.transform.position).normalized * 1.8f;
            Vector3 spot = Colony.Spot(at, 1.5f) ?? at;
            _marker = new GameObject("JarldomSleepSpot");
            _marker.transform.position = spot;
            _marker.transform.rotation = Quaternion.LookRotation(fire != null ? (fire.transform.position - spot).normalized : Vector3.forward) * Quaternion.Euler(0, 90, 0);
            _spot = _marker.transform;
            b.Move.To(spot, 1.2f);
        }
        public override void Tick(Brain b)
        {
            if (_spot == null) { Done = true; return; }
            if (!JustRest && b.Colony.Hour >= Schedule.WakeAt && b.Colony.Hour < Schedule.HomeAt) { Done = true; return; } // morning
            if (!_down)
            {
                if (b.Move.Failed) { Done = true; return; }
                if (!b.Move.Arrived && Vector3.Distance(b.Pos, _spot.position) > 1.6f) { if (!b.Move.Moving) b.Move.To(_spot.position, 1.2f); return; }
                _down = true; _since = b.Now;
                _comfort = Colony.ComfortAt(_spot.position);
                b.Sit(_spot, "attach_bed", Vector3.zero);
                Doing = JustRest ? "resting in bed" : _own ? "asleep" : "asleep by the fire";
                return;
            }
            double hours = (b.Now - _since) / Clock.Hour;
            Mood.Satisfy(b.S, Need.Rest, (float)(b.DecideDt / Clock.Hour * (4 + _comfort))); // a night in a good bed fills it
            if (JustRest && b.Colony.Hour >= 22) JustRest = false;
            if (JustRest && Activity_Over(b)) Done = true;
        }
        private static bool Activity_Over(Brain b) => b.Body.GetHealthPercentage() > 0.5f && b.S.InjuredUntil < b.Now;
        public override void Stop(Brain b)
        {
            if (_down)
            {
                double hours = (b.Now - _since) / Clock.Hour;
                if (hours >= 3)
                {
                    if (_own) Mood.Add(b.S, "ownbed", b.Now); else Mood.Add(b.S, "hallfire", b.Now);
                    if (EnvMan.IsWet() && Colony.ComfortAt(b.Pos) <= 1) Mood.Add(b.S, "wetcold", b.Now);
                    Mood.Satisfy(b.S, Need.Rest, 100);
                }
            }
            base.Stop(b);
            if (_marker != null) Object.Destroy(_marker);
        }
        public static T Nearest<T>(Vector3 at, float radius) where T : Component
        {
            var list = new List<Piece>(); Piece.GetAllPiecesInRadius(at, radius, list);
            return list.Select(p => p.GetComponent<T>()).Where(c => c != null).OrderBy(c => Vector3.Distance(c.transform.position, at)).FirstOrDefault();
        }
    }

    // Eat: from the bag, else fetch from the colony's food chests (the hall's first).
    internal sealed class EatTask : Task
    {
        private Container _chest; private ItemDrop.ItemData _item;
        public override void Start(Brain b)
        {
            Doing = "having a bite";
            if (Food.EatFromBag(b)) { Done = true; return; }
            var near = Stock.Food(b.Colony, b.Pos).Where(x => !b.Meals.Any(m => m.Prefab == x.item.m_dropPrefab?.name)).OrderByDescending(x => x.item.m_shared.m_food).FirstOrDefault();
            _chest = near.chest; _item = near.item;
            if (_chest == null) { Done = true; b.NoFoodUntil = Time.time + 60; return; }
            Doing = "fetching food";
            b.Move.To(_chest.transform.position, 1.8f);
        }
        public override void Tick(Brain b)
        {
            if (Done) return;
            if (_chest == null || b.Move.Failed) { Done = true; return; }
            if (!b.Move.Arrived && Vector3.Distance(b.Pos, _chest.transform.position) > 2.4f) { if (!b.Move.Moving) b.Move.To(_chest.transform.position, 1.8f); return; }
            int took = Stock.Take(_chest, b.Body.GetInventory(), _item, 2);
            if (took > 0) Food.EatFromBag(b); else b.NoFoodUntil = Time.time + 30;
            Done = true;
        }
    }

    // Evening in the great hall: a seat, food and company. With Shelter set: only out of the rain.
    internal sealed class HallTask : Task
    {
        public bool Shelter;
        private Chair _chair; private Vector3 _stand; private bool _there, _counted; private float _nextTalk;
        public override void Start(Brain b)
        {
            Room hall = b.Colony.Best(RoomType.GreatHall);
            if (hall == null && Shelter) hall = b.Colony.R.Rooms.FirstOrDefault(r => r.Ok);
            Doing = Shelter ? "out of the rain" : hall != null ? "off to the hall" : "by the pillars";
            Vector3 at = hall != null ? b.Colony.Centre(hall) : b.Colony.Position + b.Colony.Seat.transform.forward * 4;
            if (hall != null && !Shelter && b.S.Prof != Prof.Skald) _chair = Seats.Free(b, hall, b.Colony); // (a skald stands by the hearth to play)
            Fireplace hearth = hall != null && b.S.Prof == Prof.Skald ? SleepTask.Nearest<Fireplace>(at, 8f) : null;
            if (hearth != null) at = hearth.transform.position + (b.Pos - hearth.transform.position).normalized * 2f;
            _stand = _chair != null ? _chair.transform.position : Colony.Spot(at, hearth != null ? 1f : 3f) ?? at;
            b.Move.To(_stand, _chair != null ? 1.2f : 2f);
        }
        public override void Tick(Brain b)
        {
            if (!_there)
            {
                if (b.Move.Failed) { _there = true; return; }
                if (b.Move.Moving) return;
                if (!b.Move.Arrived && Utils.DistanceXZ(b.Pos, _stand) > 2.5f) { b.Move.To(_stand, _chair != null ? 1.2f : 2f); return; } // stopped by a fight
                _there = true;
                if (_chair != null && _chair.m_attachPoint != null) b.Sit(_chair.m_attachPoint, string.IsNullOrEmpty(_chair.m_attachAnimation) ? "attach_chair" : _chair.m_attachAnimation, Vector3.zero);
                Doing = Shelter ? "sheltering" : _chair != null ? "in the hall" : "keeping company";
            }
            if (Shelter) return;
            if (b.S.Prof == Prof.Skald && b.Held == null && Time.time > _nextSong) Sing(b);
            Mood.Satisfy(b.S, Need.Social, b.DecideDt / (float)Clock.Hour * 12f);
            if (!_counted && b.Colony.RoomAt(b.Pos, RoomType.GreatHall) != null) { _counted = true; Mood.Add(b.S, "hall", b.Now); b.S.Bump("halls"); }
            if (b.S.Needs[(int)Need.Food] < 70 && Food.CanEat(b)) Food.EatFromBag(b);
            if (Time.time > _nextTalk) { _nextTalk = Time.time + UnityEngine.Random.Range(25f, 60f); Talk.Gossip(b); }
        }
        public override void Stop(Brain b) { Seats.Free(b); base.Stop(b); }
        // The skald plays: a verse, a cheer, and everyone in the hall feels it.
        private float _nextSong;
        private static readonly string[] Verses = { "Cattle die, kinsmen die, the self must also die; but glory never dies for the one who earns it.", "Hear, all you hearers, of the hall of the slain!",
            "The wolf's food falls, the raven's feast is spread...", "Sing of the jarl who built this hall!", "Bring the horn, fill it high, and drink to those who fell!" };
        private void Sing(Brain b)
        {
            _nextSong = Time.time + UnityEngine.Random.Range(14f, 24f);
            string[] moves = { "emote_cheer", "emote_headbang", "emote_dance", "emote_toast" };
            Body_.Anim(b.Body)?.SetTrigger(moves[UnityEngine.Random.Range(0, moves.Length)]);
            b.Say(Verses[UnityEngine.Random.Range(0, Verses.Length)], 6);
            b.S.Bump("songs");
            if (b.S.Count("songs") >= 100 && string.IsNullOrEmpty(b.S.Byname)) { b.S.Byname = Names.Byname("skald"); b.Colony.Log("byname", b.S.Name, b.S.Byname); }
            foreach (Brain o in Villagers.All.Where(o => o != b && o.S.Colony == b.S.Colony && Vector3.Distance(o.Pos, b.Pos) < 20)) { if (!Mood.Has(o.S, "skald", o.Now)) Mood.Add(o.S, "skald", o.Now); Mood.Satisfy(o.S, Need.Social, 3); o.Dirty(); }
        }
    }

    // Worn gear to a station that can mend it, the game's way: the recipe's own station (or its repair station), at the recipe's level.
    internal sealed class RepairTask : Task
    {
        private CraftingStation _at;
        public static List<ItemDrop.ItemData> Worn(Brain b) => b.Body.GetInventory().GetAllItems()
            .Where(i => i.m_shared.m_useDurability && i.m_shared.m_canBeReparied && i.m_durability < i.GetMaxDurability() * 0.35f).ToList();
        public static bool Can(CraftingStation st, ItemDrop.ItemData item)
        {
            Recipe recipe = ObjectDB.instance?.GetRecipe(item);
            if (st == null || recipe == null || recipe.m_craftingStation == null && recipe.m_repairStation == null) return false;
            bool right = recipe.m_repairStation != null && recipe.m_repairStation.m_name == st.m_name || recipe.m_craftingStation != null && recipe.m_craftingStation.m_name == st.m_name;
            return right && Mathf.Min(st.GetLevel(), 4) >= recipe.m_minStationLevel;
        }
        public static CraftingStation Station(Brain b, List<ItemDrop.ItemData> worn) =>
            b.Colony.PiecesIn(b.Colony.Radius).Select(p => p.GetComponent<CraftingStation>()).Where(st => st != null && worn.Any(i => Can(st, i)))
                .OrderBy(st => Vector3.Distance(st.transform.position, b.Pos)).FirstOrDefault();
        public override void Start(Brain b)
        {
            Doing = "taking worn gear to be mended";
            _at = Station(b, Worn(b));
            if (_at == null) { Done = true; b.NoRepairUntil = Time.time + 120; return; }
            b.Move.To(_at.transform.position, 2f);
        }
        public override void Tick(Brain b)
        {
            if (_at == null || b.Move.Failed) { Done = true; b.NoRepairUntil = Time.time + 120; return; }
            if (Vector3.Distance(b.Pos, _at.transform.position) > 3f) { if (!b.Move.Moving) b.Move.To(_at.transform.position, 2f); return; }
            int n = 0;
            foreach (ItemDrop.ItemData item in Worn(b).Where(i => Can(_at, i)))
            {
                b.Body.RaiseSkill(Skills.SkillType.Crafting, 1f - item.m_durability / item.GetMaxDurability());
                item.m_durability = item.GetMaxDurability();
                n++;
            }
            if (n > 0) { _at.m_repairItemDoneEffects.Create(_at.transform.position, Quaternion.identity); b.Body.GetInventory().m_onChanged?.Invoke(); b.Say(n == 1 ? "Good as new." : $"All {n} mended.", 3); }
            Done = true;
        }
    }

    // Building a planned ghost (BuildOrders): the piece's exact materials from the stores, then the hammer at the spot. Paid in full or not
    // at all: nothing is taken from the bag until the piece stands.
    internal sealed class BuildTask : Task
    {
        private string _id, _prefab; private Vector3 _at; private Piece _piece; private int _phase; private float _next;
        private List<(string item, int amount)> _needs;
        private static readonly Dictionary<string, float> Held = new Dictionary<string, float>(); // plan id -> until (a builder that vanished lets go)
        private static class Taken
        {
            public static bool Contains(string id) => Held.TryGetValue(id, out float until) && until > Time.time;
            public static void Add(string id) => Held[id] = Time.time + 180;
        }
        public override void Stop(Brain b) { if (_id != null) Held.Remove(_id); base.Stop(b); }
        public override bool Interruptible => _phase == 0;
        public override void Start(Brain b)
        {
            // The first plan no other builder is on (each fetches the whole cost: two on one plan would waste a trip).
            string[] next = Compat.NextGhosts(b.Colony.Position, b.Colony.Radius, 8).Where(g => !Taken.Contains(g.Split('|')[0])).Take(1).ToArray();
            if (next.Length == 0) { Done = true; b.NoBuildUntil = Time.time + 30; string[] why = Compat.GhostBlockers(b.Colony.Position, b.Colony.Radius); if (why.Length > 0) b.Say("I can't build the plans: " + why[0].Replace("|", " ").Trim(), 6); return; }
            string[] f = next[0].Split('|');
            _id = f[0]; _prefab = f[1]; Taken.Add(_id);
            _at = new Vector3(float.Parse(f[2], System.Globalization.CultureInfo.InvariantCulture), float.Parse(f[3], System.Globalization.CultureInfo.InvariantCulture), float.Parse(f[4], System.Globalization.CultureInfo.InvariantCulture));
            _piece = Assets.Find(_prefab)?.GetComponent<Piece>();
            if (_piece == null) { Done = true; b.NoBuildUntil = Time.time + 30; return; }
            _needs = _piece.m_resources.Where(r => r.m_resItem != null && !r.m_upgraderResource && r.m_amount > 0).Select(r => (r.m_resItem.name, r.m_amount)).ToList();
            Doing = "building " + Localization.instance.Localize(_piece.m_name).ToLower();
        }
        private int Have(Brain b, string item) => b.Body.GetInventory().GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == item).Sum(i => i.m_stack);
        public override void Tick(Brain b)
        {
            if (Done) return;
            if (_phase == 0)
            {
                // Fetch what's missing from the stores; refuse the piece if the stores don't hold it all.
                var missing = _needs.Where(n => Have(b, n.item) < n.amount).ToList();
                if (missing.Count == 0) { _phase = 1; b.Move.To(_at, 2.5f); return; }
                var chests = Stock.Chests(b.Colony, b.Pos).Where(x => x.category != "treasury" && !ContainerAccess.InUse(x.chest)).ToList();
                foreach (var (item, amount) in missing)
                    if (chests.Sum(x => x.chest.GetInventory().GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == item).Sum(i => i.m_stack)) + Have(b, item) < amount)
                    { Done = true; b.NoBuildUntil = Time.time + 60; b.Say($"The stores have too little {Localization.instance.Localize(Assets.Find(item)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name ?? item).ToLower()} to build the plans.", 6); return; }
                var (want, count) = missing[0];
                Container from = chests.Select(x => x.chest).FirstOrDefault(ch => ch.GetInventory().GetAllItems().Any(i => i.m_dropPrefab != null && i.m_dropPrefab.name == want));
                if (from == null) { Done = true; b.NoBuildUntil = Time.time + 20; return; }
                if (Vector3.Distance(b.Pos, from.transform.position) > 2.4f) { if (!b.Move.Moving) b.Move.To(from.transform.position, 1.8f); if (b.Move.Failed) Done = true; return; }
                ItemDrop.ItemData stack = from.GetInventory().GetAllItems().First(i => i.m_dropPrefab != null && i.m_dropPrefab.name == want);
                if (Stock.Take(from, b.Body.GetInventory(), stack, count - Have(b, want)) == 0) { Done = true; b.NoBuildUntil = Time.time + 30; return; }
                return;
            }
            // (near enough across the ground, and within reach in height: a roof or upper wall is built from below)
            if (Utils.DistanceXZ(b.Pos, _at) > 3.5f || _at.y - b.Pos.y > 6f || b.Pos.y - _at.y > 3f) { if (!b.Move.Moving) b.Move.To(_at, 2.5f); if (b.Move.Failed) { Done = true; b.NoBuildUntil = Time.time + 20; } return; }
            b.Move.Stop();
            Vector3 d = _at - b.Pos; d.y = 0; if (d.sqrMagnitude > 0.01f) b.Ai.LookTowards(d.normalized);
            if (Time.time < _next) return;
            _next = Time.time + 1.2f / Mathf.Max(0.4f, b.Productivity());
            ItemDrop.ItemData hammer = Gear.Tool(b, "hammer");
            if (hammer == null) { Done = true; b.NoBuildUntil = Time.time + 30; return; }
            if (!b.Body.IsItemEquiped(hammer)) b.Body.EquipItem(hammer, false);
            Body_.Anim(b.Body)?.SetTrigger("swing_hammer");
            if (++_phase < 3) return; // a couple of blows, then it stands
            if (_needs.Any(n => Have(b, n.item) < n.amount)) { Done = true; b.NoBuildUntil = Time.time + 20; return; }
            if (!Compat.PlaceGhost(_id)) { Done = true; b.NoBuildUntil = Time.time + 10; return; }
            Inventory bag = b.Body.GetInventory();
            foreach (var (item, amount) in _needs)
                for (int left = amount; left > 0;)
                {
                    ItemDrop.ItemData st = bag.GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == item);
                    if (st == null) break;
                    int n = Math.Min(left, st.m_stack); bag.RemoveItem(st, n); left -= n;
                }
            b.Body.RaiseSkill(Skills.SkillType.Crafting, 0.5f);
            b.S.Bump("built");
            Done = true;
        }
    }

    // A trade run: the colony's surplus valuables to the traders, coins back at the traders' own prices (never better), a day later.
    internal sealed class TradeTask : Task
    {
        private Vector3 _edge; private bool _packed;
        public override bool Interruptible => false;
        public override void Start(Brain b)
        {
            Doing = "packing for a trade run";
            Inventory bag = b.Body.GetInventory();
            foreach (var x in Stock.Chests(b.Colony, b.Pos).Where(x => x.category != "treasury" && !ContainerAccess.InUse(x.chest)))
                foreach (ItemDrop.ItemData item in x.chest.GetInventory().GetAllItems().Where(Trade.Sellable).ToList())
                    if (bag.GetEmptySlots() > 1) Stock.Take(x.chest, bag, item, item.m_stack);
            if (!bag.GetAllItems().Any(Trade.Sellable)) { Done = true; b.NoTradeUntil = Time.time + 60; return; }
            Vector3 dir = (b.Pos - b.Colony.Position); dir.y = 0; if (dir.sqrMagnitude < 1) dir = Vector3.forward;
            _edge = b.Colony.Position + dir.normalized * (b.Colony.Radius + 10);
            b.Move.To(Colony.Spot(_edge, 4f) ?? _edge, 3f);
            _packed = true;
        }
        public override void Tick(Brain b)
        {
            if (!_packed) { Done = true; return; }
            Doing = "off on a trade run";
            // (a fight or a flee stops the walk: walk on unless really there, or the way is lost)
            if (!b.Move.Failed && !b.Move.Arrived && b.Move.Distance > 6f) { if (!b.Move.Moving) b.Move.To(b.Move.Goal, 3f); return; }
            Trade.Leave(b);
            Done = true;
        }
    }
    internal static class Trade
    {
        public static bool Sellable(ItemDrop.ItemData i) => i != null && i.m_shared.m_value > 0 && !i.m_equipped && !(i.m_dropPrefab != null && i.m_dropPrefab.name == "Coins") && !Items.IsRing(i);
        public static void Leave(Brain b)
        {
            Inventory bag = b.Body.GetInventory();
            var goods = bag.GetAllItems().Where(Sellable).ToList();
            int coins = goods.Sum(i => i.m_shared.m_value * i.m_stack);
            bool njord = b.Colony.R.Rooms.Any(r => r.Ok && r.Type == RoomType.Temple && r.God == "njord") && b.Colony.FavourOf("njord") > 0;
            if (njord) coins = coins * 110 / 100;
            bool lost = UnityEngine.Random.value < (b.Colony.FavourOf("njord") < -10 ? 0.15f : 0.05f);
            foreach (ItemDrop.ItemData i in goods) bag.RemoveItem(i);
            b.S.Bump("trades");
            b.Colony.Log("text", b.S.Name, "", $"{b.S.Name} set out to trade {goods.Count} lots worth {coins} coins.");
            Net.Act(b.Colony, "away", $"{b.S.Vid}|{(lost ? 0 : coins)}");
            b.Away(lost ? "trade:0" : "trade:" + coins);
        }
    }

    // A raid: fighters to their posts, everyone else into the great hall (door shut) or back to the pillars.
    internal sealed class DefendTask : Task
    {
        private Vector3 _to; private bool _shelter;
        public override void Start(Brain b)
        {
            bool fighter = Gear.CanFight(b) && (b.S.Prof == Prof.Guard || b.S.Prof == Prof.Hunter || b.S.Has("brave"));
            Room hall = b.Colony.Best(RoomType.GreatHall);
            if (fighter)
            {
                Zone post = b.Colony.R.Zones.Where(z => z.Type == ZoneType.GuardPost).OrderBy(z => Geometry.Distance(z.Xz, b.Pos.x, b.Pos.z)).FirstOrDefault();
                _to = post != null ? Zones.Centre(post) : b.Colony.RaidAt != Vector3.zero ? b.Colony.RaidAt : b.Colony.Position; // a guard post, else the stone under siege
                Doing = "to arms!";
            }
            else if (hall != null) { _to = b.Colony.Centre(hall); _shelter = true; Doing = "running for the hall"; }
            else { _to = b.Colony.Position; Doing = "falling back to the pillars"; }
            b.Move.To(Colony.Spot(_to, 2f) ?? _to, 2f, true);
        }
        public override void Tick(Brain b)
        {
            b.KeepDoorsOpen = false;
            if (!b.Move.Moving && Vector3.Distance(b.Pos, _to) > 5f) b.Move.To(Colony.Spot(_to, 2f) ?? _to, 2f, true);
            if (!b.Move.Moving) Doing = _shelter ? "hiding in the hall" : "holding";
        }
    }

    // Chairs in use by villagers (the game only sees players on them).
    internal static class Seats
    {
        private static readonly Dictionary<Chair, Brain> Taken = new Dictionary<Chair, Brain>();
        public static Chair Free(Brain b, Room hall, Colony c)
        {
            Free(b);
            var list = new List<Piece>(); Piece.GetAllPiecesInRadius(c.Centre(hall), 30, list);
            Chair best = list.Select(p => p.GetComponent<Chair>()).Where(ch => ch != null && Geometry.Inside(hall.Xz, ch.transform.position.x, ch.transform.position.z)
                && !(Taken.TryGetValue(ch, out Brain o) && o.Alive && o != b) && Player.GetClosestPlayer(ch.m_attachPoint != null ? ch.m_attachPoint.position : ch.transform.position, 0.3f) == null)
                .OrderBy(ch => Colony.Prefab(ch.gameObject).Contains("throne") ? 1 : 0).ThenBy(_ => UnityEngine.Random.value).FirstOrDefault();
            if (best != null) Taken[best] = b;
            return best;
        }
        public static void Free(Brain b) { foreach (Chair ch in Taken.Where(kv => kv.Value == b || kv.Key == null).Select(kv => kv.Key).ToList()) Taken.Remove(ch); }
        public static void Clear() => Taken.Clear();
    }

    // Food: three meals at most, burning as a player's do; health follows them.
    internal static class Food
    {
        private static readonly Dictionary<Brain, (float at, bool can)> Asked = new Dictionary<Brain, (float, bool)>();
        public static bool CanEat(Brain b)
        {
            if (b.Meals.Count(m => m.Left > m.Total * 0.5f) >= 3) return false;
            if (Bag(b).Any()) return true;
            if (Asked.TryGetValue(b, out var hit) && Time.time - hit.at < 5f) return hit.can;
            bool can = Stock.Food(b.Colony, b.Pos).Any(x => !b.Meals.Any(m => m.Prefab == x.item.m_dropPrefab?.name));
            if (Asked.Count > 200) Asked.Clear();
            Asked[b] = (Time.time, can);
            return can;
        }
        private static IEnumerable<ItemDrop.ItemData> Bag(Brain b) =>
            b.Body.GetInventory().GetAllItems().Where(i => Stock.Edible(i) && !b.Meals.Any(m => m.Prefab == i.m_dropPrefab?.name && m.Left > m.Total * 0.5f));
        public static bool EatFromBag(Brain b)
        {
            ItemDrop.ItemData item = Bag(b).OrderByDescending(i => i.m_shared.m_food).FirstOrDefault();
            if (item == null) return false;
            Eat(b, item);
            return true;
        }
        public static void Eat(Brain b, ItemDrop.ItemData item)
        {
            string prefab = item.m_dropPrefab != null ? item.m_dropPrefab.name : item.m_shared.m_name;
            b.Meals.RemoveAll(m => m.Prefab == prefab);
            if (b.Meals.Count >= 3) b.Meals.Remove(b.Meals.OrderBy(m => m.Left).First());
            b.Meals.Add(new Brain.Meal { Prefab = prefab, Health = item.m_shared.m_food, Stamina = item.m_shared.m_foodStamina, Left = item.m_shared.m_foodBurnTime, Total = item.m_shared.m_foodBurnTime });
            b.Body.GetInventory().RemoveOneItem(item);
            b.Body.m_consumeItemEffects.Create(b.Body.transform.position, Quaternion.identity);
            Body_.Anim(b.Body)?.SetTrigger("eat");
            Mood.Satisfy(b.S, Need.Food, 25 + item.m_shared.m_food);
            if (item.m_shared.m_food >= 30) Mood.Add(b.S, "finemeal", b.Now);
            b.S.Foods.Add(prefab); if (b.S.Foods.Count > 9) b.S.Foods.RemoveAt(0);
            b.Body.SetMaxHealth(b.MaxHealth());
            b.Dirty();
        }
        public static void Burn(Brain b, float seconds)
        {
            foreach (Brain.Meal m in b.Meals) m.Left -= seconds * Game.m_foodRate;
            b.Meals.RemoveAll(m => m.Left <= 0);
        }
    }
}
