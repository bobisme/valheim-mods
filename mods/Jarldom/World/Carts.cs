using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // Ore routes: an outpost's carter loads its ore and metal (up to six stacks) from its stores onto a cart and drives it to the main
    // colony (ore can't go through portals). The trip is abstract: the load becomes a global key, "jarldom_cargo_<main>_<n> item:count|...",
    // which the main colony unloads into its stores when it next runs; the carter is away for the round trip (as a trader is) and comes
    // back to the outpost. A long road has its dangers: a chance the load is lost to an ambush.
    internal static class Carts
    {
        public const string Prefix = "cart:";
        public const float Speed = 3f; // metres a second, with a laden cart
        public static float AmbushChance(float distance) => Mathf.Clamp((distance - 300f) / 3000f, 0f, 0.3f);
        public static bool IsCargo(ItemDrop.ItemData i) => i?.m_dropPrefab != null && !i.m_equipped && (Stock.Category(i) == "ore" || Stock.Category(i) == "metal");
        public static Vagon CartNear(Colony c) => c.PiecesIn(c.Radius).Select(p => p.GetComponent<Vagon>()).FirstOrDefault(v => v != null); // (a cart is a piece)

        // The carter's day: a load when there is one, a cart to carry it, and a main colony to take it to.
        public static Task Next(Brain b)
        {
            Colony c = b.Colony;
            if (c == null || !c.IsOwner || Time.time < b.NoCartUntil) return null;
            string main = Outposts.Parent(c);
            if (main == "") { b.Idle("a carter needs an outpost to carry from"); b.NoCartUntil = Time.time + 60; return null; }
            if (!Stock.Chests(c, b.Pos).Any(x => x.category != "treasury" && x.chest.GetInventory().GetAllItems().Count(IsCargo) > 0)) { b.NoCartUntil = Time.time + 60; return null; }
            Vagon cart = CartNear(c);
            if (cart == null) { b.Idle("there's no cart here"); b.NoCartUntil = Time.time + 60; return null; }
            return new CartTask(main, cart);
        }

        // On the main colony's owner, every few seconds: loads that arrived while it was away.
        private static Dictionary<string, GameObject> _byLower;
        private static GameObject Item(string lower)
        {
            if (_byLower == null && ObjectDB.instance != null) _byLower = ObjectDB.instance.m_items.Where(i => i != null).GroupBy(i => i.name.ToLowerInvariant()).ToDictionary(g => g.Key, g => g.First());
            return _byLower != null && _byLower.TryGetValue(lower, out GameObject go) ? go : null;
        }
        public static void Unload(Colony c)
        {
            if (ZoneSystem.instance == null) return;
            string head = "jarldom_cargo_" + c.Id.ToLowerInvariant() + "_";
            foreach (string line in ZoneSystem.instance.GetGlobalKeys().Where(l => l.StartsWith(head)).ToList())
            {
                int space = line.IndexOf(' '); if (space < 0) continue;
                string key = line.Substring(0, space);
                var left = new List<string>(); var got = new List<string>();
                foreach (string part in line.Substring(space + 1).Split('|'))
                {
                    string[] kv = part.Split(':');
                    if (kv.Length != 2 || !int.TryParse(kv[1], out int n) || n <= 0) continue;
                    GameObject prefab = Item(kv[0]);
                    if (prefab == null) continue;
                    int put = Put(c, prefab, n);
                    if (put > 0) got.Add($"{put} {Localization.instance.Localize(prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name).ToLower()}");
                    if (put < n) left.Add($"{kv[0]}:{n - put}");
                }
                if (left.Count == 0) ZoneSystem.instance.RemoveGlobalKey(key);
                else ZoneSystem.instance.SetGlobalKey(key + " " + string.Join("|", left)); // (the stores are full: the rest waits on the cart)
                if (got.Count > 0) c.Log("text", "", "", "A cart came in from the outpost with " + string.Join(", ", got) + ".");
            }
        }
        internal static int Put(Colony c, GameObject prefab, int n)
        {
            ItemDrop drop = prefab.GetComponent<ItemDrop>();
            int stack = Math.Max(1, drop.m_itemData.m_shared.m_maxStackSize), put = 0;
            var chests = new List<Container>();
            ItemDrop.ItemData item = Stock.Of(prefab);
            Container best = Stock.Destination(c, item, c.Position);
            if (best != null) chests.Add(best);
            chests.AddRange(Stock.Chests(c, c.Position).Where(x => x.category == "any" || x.category == Stock.Category(item)).Select(x => x.chest).Where(ch => ch != best));
            foreach (Container chest in chests.Where(ch => !ContainerAccess.InUse(ch)))
            {
                ContainerAccess.TakeOver(chest);
                while (put < n)
                {
                    int before = chest.GetInventory().CountItems(drop.m_itemData.m_shared.m_name);
                    chest.GetInventory().AddItem(prefab, Math.Min(stack, n - put));
                    int added = chest.GetInventory().CountItems(drop.m_itemData.m_shared.m_name) - before;
                    if (added <= 0) break;
                    put += added;
                }
                if (put >= n) break;
            }
            return put;
        }
    }

    // Loading the cart: the outpost's ore and metal from its stores, then off down the road (away until the round trip is done).
    internal sealed class CartTask : Task
    {
        private readonly string _main; private readonly Vagon _cart; private int _phase; private Container _from;
        public CartTask(string main, Vagon cart) { _main = main; _cart = cart; }
        public override bool Interruptible => _phase == 0;
        public override void Start(Brain b) { Doing = "loading the cart"; }
        private int Loaded(Brain b) => b.Body.GetInventory().GetAllItems().Count(Carts.IsCargo);
        public override void Tick(Brain b)
        {
            if (_cart == null) { Done = true; return; }
            Inventory bag = b.Body.GetInventory();
            if (_phase == 0)
            {
                if (Loaded(b) >= 6 || bag.GetEmptySlots() <= 1) { _phase = 1; return; }
                if (_from == null)
                {
                    _from = Stock.ChestsFor(b).Where(x => x.category != "treasury" && !ContainerAccess.InUse(x.chest)).Select(x => x.chest).FirstOrDefault(ch => ch.GetInventory().GetAllItems().Any(Carts.IsCargo));
                    if (_from == null) { _phase = Loaded(b) > 0 ? 1 : 9; if (_phase == 9) Done = true; return; }
                }
                if (Stock.OutOfReach(b.Pos, _from.transform.position, 2.4f)) { if (!b.Move.Moving) b.Move.To(_from.transform.position, 1.8f); if (b.Move.Failed) { Done = true; b.NoCartUntil = Time.time + 120; } return; }
                ItemDrop.ItemData item = _from.GetInventory().GetAllItems().FirstOrDefault(Carts.IsCargo);
                if (item == null || Stock.Take(_from, bag, item, item.m_stack) == 0) _from = null;
                return;
            }
            // To the cart, and away.
            Doing = "taking the cart to the main colony";
            if (Vector3.Distance(b.Pos, _cart.transform.position) > 3f) { if (!b.Move.Moving) b.Move.To(_cart.transform.position, 2f); if (b.Move.Failed) { Done = true; b.NoCartUntil = Time.time + 120; } return; }
            Colony c = b.Colony;
            Vector3 main = Outposts.Where(_main) ?? c.Position;
            float distance = Utils.DistanceXZ(main, c.Position);
            var load = bag.GetAllItems().Where(Carts.IsCargo).GroupBy(i => i.m_dropPrefab.name).Select(g => (name: g.Key, n: g.Sum(i => i.m_stack))).ToList();
            foreach (ItemDrop.ItemData i in bag.GetAllItems().Where(Carts.IsCargo).ToList()) bag.RemoveItem(i);
            string what = string.Join(", ", load.Select(l => $"{l.n} {Localization.instance.Localize(ObjectDB.instance.GetItemPrefab(l.name)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name ?? l.name).ToLower()}"));
            if (UnityEngine.Random.value < Carts.AmbushChance(distance))
                c.Log("text", b.S.Name, "", $"{b.S.Name}'s cart was ambushed on the road, {Mathf.RoundToInt(distance)} m long: the load ({what}) was lost.");
            else
            {
                ZoneSystem.instance.SetGlobalKey($"jarldom_cargo_{_main}_{Colony.NewId()} " + string.Join("|", load.Select(l => $"{l.name.ToLowerInvariant()}:{l.n}")));
                c.Log("text", b.S.Name, "", $"{b.S.Name} took a cart to the main colony with {what}.");
            }
            b.S.Bump("carts"); b.Dirty();
            float trip = distance * 2 / Carts.Speed; // there and back
            b.Away(Carts.Prefix + Mathf.RoundToInt(trip).ToString(CultureInfo.InvariantCulture));
            Done = true;
        }
    }
}
