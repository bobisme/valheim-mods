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

        // A prefab's item as one in the world would be (its own data has no drop prefab set, so it wouldn't know what it is).
        public static ItemDrop.ItemData Of(GameObject prefab)
        {
            ItemDrop.ItemData i = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
            i.m_dropPrefab = prefab;
            return i;
        }
        public static string Category(ItemDrop.ItemData item)
        {
            if (item == null) return "any";
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
            if (n.StartsWith("Raw") || n.EndsWith("Meat") || n.Contains("Fish") || n == "Mushroom" || n == "Raspberry" || n == "Blueberries" || n == "Honey" || n == "Carrot" || n == "Turnip" || n == "Onion" || n == "Thistle" || n == "Dandelion") return "rawfood";
            if (t == ItemDrop.ItemData.ItemType.OneHandedWeapon || t == ItemDrop.ItemData.ItemType.TwoHandedWeapon || t == ItemDrop.ItemData.ItemType.Bow || t == ItemDrop.ItemData.ItemType.Shield
                || t == ItemDrop.ItemData.ItemType.Helmet || t == ItemDrop.ItemData.ItemType.Chest || t == ItemDrop.ItemData.ItemType.Legs || t == ItemDrop.ItemData.ItemType.Shoulder || t == ItemDrop.ItemData.ItemType.Tool || t == ItemDrop.ItemData.ItemType.Ammo) return "gear";
            return "any";
        }

        // How much the stores may hold of what woodcutters and miners bring in, before they stop (the jarl sets it in the Work tab;
        // 0 is no limit). Without one, a woodlot fills every chest in the colony with logs.
        public static readonly Dictionary<string, int> KeepDefault = new Dictionary<string, int> { ["wood"] = 1000, ["stone"] = 1000 };
        public static int Keep(Colony c, string cat) => c.R.Flags.TryGetValue("keep:" + cat, out int v) ? v : KeepDefault.TryGetValue(cat, out int d) ? d : 0;
        public static int Held(Colony c, string cat) =>
            Chests(c).Where(x => x.category != "treasury").Sum(x => x.chest.GetInventory().GetAllItems().Where(i => Category(i) == cat).Sum(i => i.m_stack));
        public static bool Enough(Colony c, string cat) { int keep = Keep(c, cat); return keep > 0 && Held(c, cat) >= keep; }

        // The colony's storage chests, loaded here, nearest first; with their category (from the record) and our id.
        // The list of chests is kept a few seconds (finding them walks every loaded piece); categories are read fresh.
        private static readonly Dictionary<string, (float at, List<Container> chests)> Cache = new Dictionary<string, (float, List<Container>)>();
        public static void Forget() => Cache.Clear();
        private static List<Container> Boxes(Colony c)
        {
            if (Cache.TryGetValue(c.Id, out var hit) && Time.time - hit.at < 3f) return hit.chests;
            var boxes = new List<Container>();
            foreach (Piece p in c.PiecesIn(c.Radius))
            {
                Container box = p.GetComponent<Container>();
                if (box == null || !ContainerAccess.IsStorage(box)) continue;
                if (box.m_privacy == Container.PrivacySetting.Private) continue;
                if (Utils.GetPrefabName(p.gameObject) == Pieces.PyreName) continue; // grave goods aren't the colony's
                boxes.Add(box);
            }
            Cache[c.Id] = (Time.time, boxes);
            return boxes;
        }
        public static List<(Container chest, string id, string category)> Chests(Colony c, Vector3? near = null)
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
            Vector3 from = near ?? c.Position;
            return list.OrderBy(x => Utils.DistanceXZ(x.Item1.transform.position, from) + Mathf.Abs(x.Item1.transform.position.y - from.y) * (near.HasValue && near.Value.y == 0 ? 0 : 1)).ToList();
        }
        private static bool InStockpileRoom(Colony c, Vector3 p) => c.R.Rooms.Any(r => r.Ok && r.Type == RoomType.Stockpile && Geometry.Inside(r.Xz, p.x, p.z));

        // Where an item should go: a stockpile of its category, then an "any" stockpile; never the treasury unless it's coins.
        public static Container Destination(Colony c, ItemDrop.ItemData item, Vector3 near)
        {
            string cat = Category(item);
            bool coins = item.m_dropPrefab != null && item.m_dropPrefab.name == "Coins";
            var chests = Chests(c, near).Where(x => !ContainerAccess.InUse(x.chest) && x.chest.GetInventory().CanAddItem(item, 1)).ToList();
            if (coins) { var t = chests.FirstOrDefault(x => x.category == "treasury"); if (t.chest != null) return t.chest; }
            var best = chests.FirstOrDefault(x => x.category == cat);
            if (best.chest == null) best = chests.FirstOrDefault(x => x.category == "any");
            if (best.chest == null && c.R.Stockpiles.Count == 0) best = chests.FirstOrDefault(x => x.category == "" && x.chest.GetInventory().HaveItem(item.m_shared.m_name));
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
        public static IEnumerable<(Container chest, ItemDrop.ItemData item)> Food(Colony c, Vector3 near)
        {
            foreach (var x in Chests(c, near))
            {
                if (x.category == "treasury" || ContainerAccess.InUse(x.chest)) continue;
                if (c.RoomAt(x.chest.transform.position, RoomType.Brewhouse) != null) continue; // the brewer's honey and berries aren't for eating
                foreach (ItemDrop.ItemData item in x.chest.GetInventory().GetAllItems())
                    if (Edible(item)) yield return (x.chest, item);
            }
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
