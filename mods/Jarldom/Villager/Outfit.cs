using System;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jarldom
{
    // Fetching what the work needs from the colony's stockpiles: the tool of a trade (an axe, a pickaxe, a cultivator, a rod, a
    // hammer; a farmer's hoe too), a weapon for a guard, a bow and arrows for a hunter (and more arrows when they run low). Only from
    // stockpiles: a player's own chests in the colony stay theirs.
    internal sealed class OutfitTask : Task
    {
        private const int LowArrows = 10, Arrows = 50;
        private readonly Container _chest; private readonly string _prefab; private readonly int _count;
        private OutfitTask(Container chest, string prefab, int count) { _chest = chest; _prefab = prefab; _count = count; }

        public static Task Next(Brain b)
        {
            Colony c = b.Colony;
            if (c == null || Time.time < b.NoGearUntil || b.Body.GetInventory().GetEmptySlots() < 1) return null;
            foreach (string kind in Kinds(b.S.Prof).Concat(new[] { "clothes", "hat" }))
            {
                Func<ItemDrop.ItemData, bool> want = Wanted(b, kind, out int count);
                if (want == null) continue;
                var found = Stock.Chests(c, b.Pos).Where(x => x.category != "" && x.category != "treasury" && !ContainerAccess.InUse(x.chest))
                    .SelectMany(x => x.chest.GetInventory().GetAllItems().Where(i => i.m_dropPrefab != null && want(i)).Select(i => (x.chest, i))).FirstOrDefault();
                if (found.chest != null) return new OutfitTask(found.chest, found.i.m_dropPrefab.name, count);
                // (a hunter with no bow or arrows in the stores makes their own: a crude bow, flint arrows if there's flint, else wooden)
                if (kind == "bow")
                {
                    bool noBow = !Gear.Items(b).Any(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow && Gear.Usable(i));
                    string make = noBow ? "Bow" : MakeOwn.Can(c, "ArrowFlint", b.Pos) ? "ArrowFlint" : "ArrowWood";
                    if (MakeOwn.Can(c, make, b.Pos)) return new MakeOwn(make);
                }
            }
            b.NoGearUntil = Time.time + 90;
            return null;
        }
        private static string[] Kinds(Prof p)
        {
            string tool = Tables.Of(p).Tool;
            if (p == Prof.Farmer) return new[] { tool, "hoe" };
            if (p == Prof.Fisherman) return new[] { tool, "bait" };
            return tool == "" ? new string[0] : new[] { tool };
        }
        // What to look for, or null when they have it.
        private static Func<ItemDrop.ItemData, bool> Wanted(Brain b, string kind, out int count)
        {
            count = 1;
            switch (kind)
            {
                case "bow":
                    ItemDrop.ItemData bow = Gear.Items(b).Where(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow && Gear.Usable(i)).OrderByDescending(i => i.GetDamage().GetTotalDamage()).FirstOrDefault();
                    if (bow == null) return i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow && Gear.Usable(i);
                    string ammo = bow.m_shared.m_ammoType;
                    if (string.IsNullOrEmpty(ammo) || Gear.Items(b).Where(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo && i.m_shared.m_ammoType == ammo).Sum(i => i.m_stack) >= LowArrows) return null;
                    count = Arrows;
                    return i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo && i.m_shared.m_ammoType == ammo;
                case "weapon": return Gear.CanFight(b) ? null : (Func<ItemDrop.ItemData, bool>)Gear.IsWeapon;
                case "clothes": return Weaving.InRags(b) && !Gear.Items(b).Any(i => Weaving.IsClothes(i) && i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Chest) ? (Func<ItemDrop.ItemData, bool>)(i => Weaving.IsClothes(i) && i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Chest) : null;
                case "hat": return Weaving.Hatless(b) ? (Func<ItemDrop.ItemData, bool>)(i => Weaving.IsClothes(i) && i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Helmet) : null;
                case "bait":
                    if (Gear.Items(b).Where(Bait).Sum(i => i.m_stack) >= 5) return null;
                    count = 20;
                    return Bait;
                default: return Gear.Tool(b, kind) != null ? null : i => Gear.Is(kind, i);
            }
        }

        private static bool Bait(ItemDrop.ItemData i) => i.m_dropPrefab != null && i.m_dropPrefab.name.StartsWith("FishingBait");
        public override void Start(Brain b) { Doing = "fetching gear from the stores"; b.Move.To(_chest.transform.position, 1.8f); }
        public override void Tick(Brain b)
        {
            if (_chest == null) { Done = true; return; }
            if (Vector3.Distance(b.Pos, _chest.transform.position) > 2.4f) { if (!b.Move.Moving) b.Move.To(_chest.transform.position, 1.8f); if (b.Move.Failed) { Done = true; b.NoGearUntil = Time.time + 120; } return; }
            ItemDrop.ItemData item = _chest.GetInventory().GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == _prefab);
            int got = item == null ? 0 : Stock.Take(_chest, b.Body.GetInventory(), item, Math.Min(_count, item.m_stack));
            if (got > 0) { b.Say($"I'll take {(got > 1 ? got + " " : "the ")}{Localization.instance.Localize(item.m_shared.m_name).ToLower()} for the work.", 4); b.Dirty(); }
            else b.NoGearUntil = Time.time + 60;
            Done = true;
        }
    }
}

namespace Jarldom
{
    // Back for their things: a villager who rose goes to their own tombstone (in or near the colony) and takes back what's in it, as a
    // player does; the game takes the empty stone away.
    internal sealed class GraveTask : Task
    {
        private readonly TombStone _grave;
        private GraveTask(TombStone grave) { _grave = grave; }
        public static Task Next(Brain b)
        {
            Colony c = b.Colony;
            if (c == null || Time.time < b.NoGraveUntil) return null;
            b.NoGraveUntil = Time.time + 30;
            // Their own; for a hauler, also those of villagers who won't come back for theirs (ascended, lost, gone).
            bool Mine(string vid) => vid == b.S.Vid || b.S.Prof == Prof.Hauler && vid != "" && c.R.Roster.Any(r => r.Vid == vid && r.State != SoulState.Active); // (one of ours gone for good: not another colony's)
            TombStone grave = Graves()
                .Where(t => Mine(t.GetComponent<ZNetView>()?.GetZDO()?.GetString(Villagers.TombKey, "") ?? "") && Utils.DistanceXZ(t.transform.position, c.Position) < c.Radius + 60)
                .OrderBy(t => Vector3.Distance(t.transform.position, b.Pos)).FirstOrDefault();
            return grave != null && grave.GetComponent<Container>()?.GetInventory().NrOfItems() > 0 ? new GraveTask(grave) : null;
        }
        // The tombstones in the world, looked for once every 15 s for everyone (a search of the whole scene isn't for every villager).
        private static TombStone[] _graves = new TombStone[0]; private static float _gravesAt = -100f;
        private static TombStone[] Graves()
        {
            if (Time.time - _gravesAt > 15f) { _gravesAt = Time.time; _graves = Object.FindObjectsByType<TombStone>(FindObjectsSortMode.None); }
            return _graves.Where(t => t != null).ToArray();
        }
        public override void Start(Brain b)
        {
            Doing = _grave.GetComponent<ZNetView>()?.GetZDO()?.GetString(Villagers.TombKey, "") == b.S.Vid ? "going back for their things" : "clearing an old grave";
            b.Move.To(_grave.transform.position, 1.5f);
        }
        public override void Tick(Brain b)
        {
            if (_grave == null) { Done = true; return; }
            if (Vector3.Distance(b.Pos, _grave.transform.position) > 2.2f) { if (!b.Move.Moving) b.Move.To(_grave.transform.position, 1.5f); if (b.Move.Failed) { Done = true; b.NoGraveUntil = Time.time + 300; } return; }
            Container box = _grave.GetComponent<Container>();
            Inventory bag = b.Body.GetInventory();
            int got = 0;
            bool own = _grave.GetComponent<ZNetView>()?.GetZDO()?.GetString(Villagers.TombKey, "") == b.S.Vid;
            foreach (ItemDrop.ItemData item in box.GetInventory().GetAllItems().ToList())
            {
                string name = Classes.Prefab(item);
                int took = Stock.Take(box, bag, item, item.m_stack);
                if (took > 0 && !own && name != null) b.Hauled.Add(name); // (someone else's: for the stores)
                got += took;
            }
            if (got > 0) { b.Say("There it all is.", 3); b.Dirty(); }
            if (box.GetInventory().NrOfItems() > 0) b.NoGraveUntil = Time.time + 120; // (their bag is full: the rest waits)
            Done = true;
        }
    }
}
