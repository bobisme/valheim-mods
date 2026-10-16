using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // The colony's chests: stockpiles by category, the treasury, and the safe way to take from and put into them.
    internal static class Stock
    {
        public static readonly string[] Categories = { "any", "wood", "stone", "ore", "metal", "food", "rawfood", "hides", "seeds", "gear", "valuables" };
        // A trade chest: what the jarl puts in it, a trader sells. Not one of the stores: no one takes from it, fills it or eats from it.
        public const string TradeCategory = "trade";

        // A prefab's item as one in the world would be (its own data has no drop prefab set, so it wouldn't know what it is).
        public static ItemDrop.ItemData Of(GameObject prefab)
        {
            ItemDrop.ItemData i = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
            i.m_dropPrefab = prefab;
            return i;
        }
        // (by prefab: what decides it is the same for every one of a kind; asked of every item in every chest, often)
        private static readonly Dictionary<string, string> CategoryOf = new Dictionary<string, string>();
        public static string Category(ItemDrop.ItemData item)
        {
            if (item == null) return "any";
            if (item.m_dropPrefab == null) return CategoryNow(item);
            string name = item.m_dropPrefab.name;
            if (!CategoryOf.TryGetValue(name, out string cat)) CategoryOf[name] = cat = CategoryNow(item);
            return cat;
        }
        private static string CategoryNow(ItemDrop.ItemData item)
        {
            string n = item.m_dropPrefab != null ? item.m_dropPrefab.name : "";
            var t = item.m_shared.m_itemType;
            if (n == "Coins" || item.m_shared.m_value > 0) return "valuables";
            // (gear by what it is first: a wooden shield or a fishing rod is gear, not wood or fish; every cape, belt and crossbow alike)
            if (Classes.IsEquipment(item)) return "gear";
            if (Work.Saplings.Any(s => s.seed == n)) return "seeds"; // (pine cones, fir cones, acorns: tree seeds as beech seeds are)
            if (n.Contains("Wood") || n == "RoundLog" || n == "Resin" || n == "ElderBark" || n == "YggdrasilWood" || n == "Blackwood") return "wood";
            if (n == "Stone" || n == "Flint" || n == "BlackMarble" || n == "Grausten" || n == "Obsidian" || n == "Crystal") return "stone";
            if (n.EndsWith("Ore") || n.EndsWith("Scrap") || n == "BlackMetalScrap" || n == "FlametalOreNew") return "ore";
            if (n == "Copper" || n == "Tin" || n == "Bronze" || n == "Iron" || n == "Silver" || n == "BlackMetal" || n.StartsWith("Flametal") || n.EndsWith("Nails")) return "metal";
            if (n.Contains("Hide") || n.Contains("Pelt") || n == "LeatherScraps" || n == "Feathers" || n == "Chitin") return "hides";
            if (n.Contains("Seed") || n == "Barley" || n == "Flax") return "seeds";
            if (t == ItemDrop.ItemData.ItemType.Consumable) return item.m_shared.m_food > 0 ? "food" : "food";
            if (n.StartsWith("Raw") || n.EndsWith("Meat") || n == "NeckTail" || n.Contains("Fish") || n == "Mushroom" || n == "Raspberry" || n == "Blueberries" || n == "Honey" || n == "Carrot" || n == "Turnip" || n == "Onion" || n == "Thistle" || n == "Dandelion") return "rawfood";
            if (t == ItemDrop.ItemData.ItemType.OneHandedWeapon || t == ItemDrop.ItemData.ItemType.TwoHandedWeapon || t == ItemDrop.ItemData.ItemType.Bow || t == ItemDrop.ItemData.ItemType.Shield
                || t == ItemDrop.ItemData.ItemType.Helmet || t == ItemDrop.ItemData.ItemType.Chest || t == ItemDrop.ItemData.ItemType.Legs || t == ItemDrop.ItemData.ItemType.Shoulder || t == ItemDrop.ItemData.ItemType.Tool || t == ItemDrop.ItemData.ItemType.Ammo) return "gear";
            return "any";
        }

        // How much the stores may hold of what woodcutters and miners bring in, before they stop (the jarl sets it in the Work tab;
        // 0 is no limit). Without one, a woodlot fills every chest in the colony with logs.
        public static readonly Dictionary<string, int> KeepDefault = new Dictionary<string, int> { ["wood"] = 1000, ["stone"] = 1000, ["ore"] = 300, ["coal"] = 100 }; // (ore: of each kind)
        public static int Keep(Colony c, string cat) => c.R.Flags.TryGetValue("keep:" + cat, out int v) ? v : KeepDefault.TryGetValue(cat, out int d) ? d : 0;
        public static int Held(Colony c, string cat) =>
            Chests(c).Where(x => x.category != "treasury").Sum(x => x.chest.GetInventory().GetAllItems().Where(i => Category(i) == cat && !(cat == "wood" && i.m_dropPrefab != null && i.m_dropPrefab.name == "Resin")).Sum(i => i.m_stack)); // (resin is kept with the wood, but isn't wood a woodcutter brings: not counted toward the limit)
        public static bool Enough(Colony c, string cat) { int keep = Keep(c, cat); return keep > 0 && Held(c, cat) >= keep; }

        // The colony's storage chests, loaded here, nearest first; with their category (from the record) and our id.
        // The list of chests is kept a few seconds (finding them walks every loaded piece); categories are read fresh.
        private static readonly Dictionary<string, (float at, List<Container> chests)> Cache = new Dictionary<string, (float, List<Container>)>();
        // Near enough to reach into a chest: across the floor, and up to a stack's height above (chests stacked three high stand 3 m up;
        // counted straight, its top one was never near, and whoever went to it stood under it for good).
        public static bool OutOfReach(Vector3 me, Vector3 at, float reach) => Utils.DistanceXZ(me, at) > reach || at.y - me.y > 3.5f || me.y - at.y > 1.5f;
        // A chest walked to and still out of reach once there (the walk ends on a slope above it, under a balcony) is passed over by that
        // villager a while, and another with the same goods taken instead: never stood beside for ever.
        public enum Walk { Reached, Walking, Unreachable }
        private static readonly Dictionary<(string vid, int chest), float> Unreachable = new Dictionary<(string, int), float>();
        public static Walk WalkTo(Brain b, Container chest, float reach = 2.4f)
        {
            Vector3 at = chest.transform.position;
            if (!OutOfReach(b.Pos, at, reach)) return Walk.Reached;
            bool there = !b.Move.Moving && b.Move.Arrived && Vector3.Distance(b.Move.Goal, at) < 3f;
            if (b.Move.Failed && Vector3.Distance(b.Move.Goal, at) < 3f || there)
            {
                Unreachable[(b.S.Vid, chest.GetInstanceID())] = Time.time + 600f;
                if (Unreachable.Count > 500) foreach (var k in Unreachable.Where(kv => kv.Value < Time.time).Select(kv => kv.Key).ToList()) Unreachable.Remove(k);
                b.Move.Stop();
                return Walk.Unreachable;
            }
            if (!b.Move.Moving || Vector3.Distance(b.Move.Goal, at) > 3f) b.Move.To(at, 1.8f);
            return Walk.Walking;
        }
        // The same for any place to stand by (a station, an altar, a grave, a cart, a dock): there once within reach, or once the walk has
        // ended within reach across the ground (it may stand a little up or down: on a plinth, a dock over the water); out of reach once
        // the walk has ended farther off or failed.
        public static Walk WalkNear(Brain b, Vector3 at, float reach, float arrive)
        {
            if (Vector3.Distance(b.Pos, at) <= reach) return Walk.Reached;
            bool ended = !b.Move.Moving && b.Move.Arrived && Vector3.Distance(b.Move.Goal, at) < reach + 1f;
            if (ended) return Utils.DistanceXZ(b.Pos, at) <= reach ? Walk.Reached : Walk.Unreachable;
            if (b.Move.Failed && Vector3.Distance(b.Move.Goal, at) < reach + 1f) return Walk.Unreachable;
            if (!b.Move.Moving) b.Move.To(at, arrive);
            return Walk.Walking;
        }
        public static bool Reachable(Brain b, Container chest) => !Unreachable.TryGetValue((b.S.Vid, chest.GetInstanceID()), out float until) || Time.time > until;
        // The colony's chests near a villager, less those they couldn't reach lately.
        public static List<(Container chest, string id, string category)> ChestsFor(Brain b) => Chests(b.Colony, b.Pos).Where(x => Reachable(b, x.chest)).ToList();
        public static void Forget() { Cache.Clear(); Listed.Clear(); Counted.Clear(); CategoryOf.Clear(); }
        private static List<Container> Boxes(Colony c)
        {
            if (Cache.TryGetValue(c.Id, out var hit) && Time.time - hit.at < 3f) return hit.chests;
            var boxes = new List<Container>();
            foreach (Container box in c.PiecesIn<Container>(c.Radius)) // (the colony's kept list of them: not every piece looked at again)
            {
                if (box == null || !ContainerAccess.IsStorage(box)) continue;
                Piece p = box.GetComponent<Piece>();
                if (box.m_privacy == Container.PrivacySetting.Private) continue;
                if (Colony.NameOf(p) == Pieces.PyreName) continue; // grave goods aren't the colony's
                boxes.Add(box);
            }
            Cache[c.Id] = (Time.time, boxes);
            return boxes;
        }
        // The chests with their categories, worked out once a second (a work scan asks a hundred times over; a chest's category changes only
        // when the jarl marks it); each caller gets its own list.
        private static readonly Dictionary<string, (float at, List<(Container, string, string)> list)> Listed = new Dictionary<string, (float, List<(Container, string, string)>)>();
        public static List<(Container chest, string id, string category)> Chests(Colony c, Vector3? near = null, bool withTrade = false)
        {
            if (!Listed.TryGetValue(c.Id, out var hit) || Time.time - hit.at > 1f) Listed[c.Id] = hit = (Time.time, Categorised(c));
            var list = new List<(Container chest, string id, string category)>(hit.list.Count);
            foreach (var x in hit.list) if (x.Item1 != null && (withTrade || x.Item3 != TradeCategory)) list.Add(x);
            Vector3 from = near ?? c.Position;
            return list.OrderBy(x => Utils.DistanceXZ(x.Item1.transform.position, from) + Mathf.Abs(x.Item1.transform.position.y - from.y) * (near.HasValue && near.Value.y == 0 ? 0 : 1)).ToList();
        }
        // The trade chests, loaded here, nearest first.
        public static List<Container> TradeChests(Colony c, Vector3? near = null) => Chests(c, near, true).Where(x => x.category == TradeCategory).Select(x => x.chest).ToList();
        // How many of an item the stores hold (not the treasury; with free, only chests no one has open), from a count made once a frame.
        private static readonly Dictionary<string, (int frame, Dictionary<string, int> all, Dictionary<string, int> free)> Counted = new Dictionary<string, (int, Dictionary<string, int>, Dictionary<string, int>)>();
        public static int Count(Colony c, string prefab, bool free = false)
        {
            if (!Counted.TryGetValue(c.Id, out var hit) || hit.frame != Time.frameCount)
            {
                var all = new Dictionary<string, int>(); var open = new Dictionary<string, int>();
                foreach (var x in Chests(c))
                {
                    if (x.category == "treasury") continue;
                    bool inUse = ContainerAccess.InUse(x.chest);
                    foreach (ItemDrop.ItemData i in x.chest.GetInventory().GetAllItems())
                    {
                        if (i.m_dropPrefab == null) continue;
                        string n = i.m_dropPrefab.name;
                        all[n] = (all.TryGetValue(n, out int a) ? a : 0) + i.m_stack;
                        if (!inUse) open[n] = (open.TryGetValue(n, out int b) ? b : 0) + i.m_stack;
                    }
                }
                Counted[c.Id] = hit = (Time.frameCount, all, open);
            }
            return (free ? hit.free : hit.all).TryGetValue(prefab, out int k) ? k : 0;
        }
        private static List<(Container, string, string)> Categorised(Colony c)
        {
            var list = new List<(Container, string, string)>();
            foreach (Container box in Boxes(c))
            {
                if (box == null) continue;
                Piece p = box.GetComponent<Piece>();
                if (p == null) continue;
                string id = Colony.IdOf(p.GetComponent<ZNetView>(), false);
                string cat = id.Length > 0 && c.R.Stockpiles.TryGetValue(id, out string s) ? s : "";
                if (id.Length > 0 && id == c.R.Treasury) cat = "treasury";
                if (cat.Length == 0 && InStockpileRoom(c, p.transform.position)) cat = "any";
                list.Add((box, id, cat));
            }
            return list;
        }
        private static bool InStockpileRoom(Colony c, Vector3 p) => c.R.Rooms.Any(r => r.Ok && r.Type == RoomType.Stockpile && Geometry.Inside(r.Xz, p.x, p.z));

        // Where an item should go: a stockpile of its category, then an "any" stockpile; never the treasury unless it's coins.
        public static Container Destination(Colony c, ItemDrop.ItemData item, Vector3 near)
        {
            string cat = Category(item);
            bool coins = item.m_dropPrefab != null && item.m_dropPrefab.name == "Coins";
            // (room is looked for only in the chests of the kind wanted, nearest first, until one has it: asked of every chest, it was a
            // hitch for every load and every miner's look at the claims)
            var chests = Chests(c, near);
            bool Fits((Container chest, string id, string category) x) => !ContainerAccess.InUse(x.chest) && x.chest.GetInventory().CanAddItem(item, 1);
            if (coins) { var t = chests.FirstOrDefault(x => x.category == "treasury" && Fits(x)); if (t.chest != null) return t.chest; }
            var best = chests.FirstOrDefault(x => x.category == cat && Fits(x));
            if (best.chest == null) best = chests.FirstOrDefault(x => x.category == "any" && Fits(x));
            if (best.chest == null && c.R.Stockpiles.Count == 0) best = chests.FirstOrDefault(x => x.category == "" && x.chest.GetInventory().HaveItem(item.m_shared.m_name) && Fits(x));
            return best.chest;
        }

        public static Container Treasury(Colony c)
        {
            if (string.IsNullOrEmpty(c.R.Treasury)) return null;
            return Chests(c).FirstOrDefault(x => x.id == c.R.Treasury).chest;
        }
        public static int Coins(Container box) => box.GetInventory().GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == "Coins").Sum(i => i.m_stack);
        // All or nothing.
        public static bool TakeCoins(Container box, int amount)
        {
            if (amount <= 0) return true;
            if (ContainerAccess.InUse(box) || Coins(box) < amount) return false;
            ContainerAccess.TakeOver(box);
            if (Coins(box) < amount) return false;
            Inventory inv = box.GetInventory();
            int left = amount;
            foreach (ItemDrop.ItemData coin in inv.GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == "Coins").ToList())
            {
                int take = Math.Min(left, coin.m_stack);
                inv.RemoveItem(coin, take);
                left -= take;
                if (left == 0) break;
            }
            return left == 0;
        }

        // Food the colony has for its villagers: the great hall's chests first, then any food stockpile.
        // Kept in the great hall for a festival coming (FestivalDays.SetAside): not to be taken for a fire, a spit or a craft.
        public static bool Kept(Colony c, Container chest, ItemDrop.ItemData item) => c.RoomAt(chest.transform.position, RoomType.GreatHall) != null && FestivalDays.SetAside(c, item);
        public static IEnumerable<(Container chest, ItemDrop.ItemData item)> Food(Colony c, Vector3 near)
        {
            foreach (var x in Chests(c, near))
            {
                if (x.category == "treasury" || ContainerAccess.InUse(x.chest)) continue;
                if (c.RoomAt(x.chest.transform.position, RoomType.Brewhouse) != null) continue; // the brewer's honey and berries aren't for eating
                bool hall = c.RoomAt(x.chest.transform.position, RoomType.GreatHall) != null;
                foreach (ItemDrop.ItemData item in x.chest.GetInventory().GetAllItems())
                    if (Edible(item) && !(hall && FestivalDays.SetAside(c, item)) && !ForBrew(c, item)) yield return (x.chest, item);
            }
        }
        // With a fermenter under a roof to keep going, what the cook brews with isn't eaten: honey not at all, berries, yellow
        // mushrooms and thistle not below a brew's worth. (honey and berries kept vanishing into the villagers before a mead base
        // could be brewed; asked a few seconds at a time, not counted at every bite)
        public static readonly Dictionary<string, int> BrewKeep = new Dictionary<string, int> { ["Honey"] = int.MaxValue, ["Raspberry"] = 20, ["Blueberries"] = 10, ["MushroomYellow"] = 10, ["Thistle"] = 10 };
        private static readonly Dictionary<string, (float at, Dictionary<string, int> held)> _brew = new Dictionary<string, (float, Dictionary<string, int>)>();
        public static bool ForBrew(Colony c, ItemDrop.ItemData item)
        {
            string n = item?.m_dropPrefab?.name;
            if (n == null || !BrewKeep.TryGetValue(n, out int keep)) return false;
            if (!_brew.TryGetValue(c.Id, out var b) || Time.time - b.at > 10f)
            {
                bool brewing = c.PiecesIn<Fermenter>(c.Radius).Any(f => Work.Roofed(f.transform.position));
                b = (Time.time, brewing ? BrewKeep.Keys.ToDictionary(k => k, k => Count(c, k)) : null);
                _brew[c.Id] = b;
            }
            return b.held != null && b.held.TryGetValue(n, out int have) && have <= keep;
        }
        public static bool Edible(ItemDrop.ItemData item) => item != null && item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Consumable && item.m_shared.m_food > 0;

        // Move one item (or part of a stack) from a chest into a bag, safely (taken over and reloaded first). Returns what moved.
        public static int Take(Container from, Inventory to, ItemDrop.ItemData item, int amount)
        {
            if (from == null || to == null || item == null || amount <= 0 || ContainerAccess.InUse(from)) return 0;
            ContainerAccess.TakeOver(from);
            Inventory inv = from.GetInventory();
            ItemDrop.ItemData have = inv.GetAllItems().FirstOrDefault(i => i == item) ?? inv.GetAllItems().FirstOrDefault(i => i.m_shared.m_name == item.m_shared.m_name && i.m_quality == item.m_quality);
            if (have == null) return 0;
            int n = Math.Min(amount, have.m_stack);
            ItemDrop.ItemData copy = have.Clone(); copy.m_stack = n;
            if (!to.CanAddItem(copy, n)) return 0;
            if (!to.AddItem(copy)) return 0;
            inv.RemoveItem(have, n);
            return n;
        }
        // Put a stack from a bag into a chest; what doesn't fit stays in the bag.
        public static int Put(Inventory from, Container to, ItemDrop.ItemData item)
        {
            if (to == null || item == null || ContainerAccess.InUse(to)) return 0;
            ContainerAccess.TakeOver(to);
            Inventory inv = to.GetInventory();
            int n = item.m_stack, moved = 0;
            while (n > 0)
            {
                ItemDrop.ItemData copy = item.Clone(); copy.m_stack = Math.Min(n, item.m_shared.m_maxStackSize);
                if (!inv.CanAddItem(copy, copy.m_stack)) { copy.m_stack = 1; if (!inv.CanAddItem(copy, 1)) break; }
                int want = copy.m_stack; // (AddItem tops up stacks and changes copy.m_stack to what went into a new slot)
                if (!inv.AddItem(copy)) break;
                n -= want; moved += want;
            }
            if (moved > 0) from.RemoveItem(item, moved);
            return moved;
        }
    }
}
