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
}
