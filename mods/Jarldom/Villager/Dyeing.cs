using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // The colony's colours, in the world (the rules are Core/Colours.cs): with Homespun's dyes, the weaver takes the stores' plain
    // garments and the dyestuff for them to a dye vat in the colony, puts them in, and once they've steeped takes the dyed ones back
    // out to the stores; villagers take those in place of their plain clothes (the jarls' sworn and the worthiest first), and at the
    // festivals put on the finest they have. Without Homespun (or one from before dyeing), none of it: no colours, nothing dyed.
    internal static class Dyeing
    {
        public const string Vat = "Homespun_DyeVat";
        public static bool TestFestival; // (jarldom finery on: as if today were a festival's day, for tests only)
        private static readonly ItemDrop.ItemData.ItemType[] Slots = { ItemDrop.ItemData.ItemType.Chest, ItemDrop.ItemData.ItemType.Legs, ItemDrop.ItemData.ItemType.Shoulder };

        // The colony's colours, first colour first (asked by every villager's gear each second: kept a couple of seconds).
        private static readonly Dictionary<string, (float at, List<string> colours)> Kept = new Dictionary<string, (float, List<string>)>();
        public static List<string> Of(Colony c)
        {
            if (c == null) return new List<string>();
            if (Kept.TryGetValue(c.Id, out var k) && Time.time - k.at < 2f) return k.colours;
            var list = Compat.HomespunDyeing ? Colours.Chosen(c.R.Flags, Compat.HomespunDyes) : new List<string>();
            Kept[c.Id] = (Time.time, list);
            return list;
        }
        public static void Forget(Colony c) { if (c != null) Kept.Remove(c.Id); }
        public static string Title(string dye) => (dye ?? "").ToLowerInvariant();
        public static string Titles(IList<string> colours) => colours.Count == 0 ? "none" : string.Join(" and ", colours.Select(Title));

        public static string DyeOf(ItemDrop.ItemData i) => Weaving.IsClothes(i) ? Compat.HomespunDye(Classes.Prefab(i)) : null;
        public static int Fine(ItemDrop.ItemData i, IList<string> colours) => Finery.Of(DyeOf(i), colours);
        public static bool FestivalToday() => TestFestival || Calendar.All.Any(f => Calendar.IsDay(Year.Today(), f));
        private static bool Garment(ItemDrop.ItemData i, ItemDrop.ItemData.ItemType slot) => i.m_shared.m_itemType == slot && Weaving.IsClothes(i) && Gear.Usable(i);

        // A finer garment than what's worn in its slot, kept by them for the festivals (not spare for the stores).
        public static bool KeptBy(Brain b, ItemDrop.ItemData i)
        {
            if (i == null || i.m_equipped || !Weaving.IsClothes(i)) return false;
            var colours = Of(b.Colony);
            int fine = Fine(i, colours);
            if (fine == 0) return false;
            var bag = Gear.Items(b).ToList(); var slot = i.m_shared.m_itemType;
            ItemDrop.ItemData worn = bag.FirstOrDefault(o => o.m_equipped && o.m_shared.m_itemType == slot);
            var rest = bag.Where(o => !o.m_equipped && o.m_shared.m_itemType == slot && Weaving.IsClothes(o)).ToList();
            int finest = rest.Max(o => Fine(o, colours));
            return Finery.KeptBy(fine, worn != null ? Fine(worn, colours) : 0, finest, rest.First(o => Fine(o, colours) == finest) == i);
        }
        // Wearing (or keeping by) the colony's colours in a slot.
        private static bool Has(Brain b, ItemDrop.ItemData.ItemType slot, IList<string> colours) => Gear.Items(b).Any(i => Garment(i, slot) && Finery.Of(DyeOf(i), colours) >= 2);
        // Would take the colony's colours for this slot: plain clothes, rags or nothing there (never armour off a fighter's back), and none of
        // the colours yet. (a guard keeps their armour on)
        private static bool Wants(Brain b, ItemDrop.ItemData.ItemType slot, IList<string> colours)
        {
            if (b.Stripped || b.S.Prof == Prof.Guard || Has(b, slot, colours)) return false;
            ItemDrop.ItemData worn = Gear.Items(b).FirstOrDefault(i => i.m_equipped && i.m_shared.m_itemType == slot);
            return worn == null || Weaving.IsClothes(worn) || Weaving.IsRags(worn);
        }
        // Their turn for something fine from the stores: more spare than there are above them who want one.
        private static bool Turn(Brain b, ItemDrop.ItemData.ItemType slot, int spare, Func<Brain, bool> wants)
        {
            Colony c = b.Colony;
            var folk = Colours.ByStanding(Villagers.All.Where(v => v.S.Colony == c.Id && v.Alive), v => v.S.Sworn != "", v => v.S.Worth, v => v.S.Arrived).ToList();
            int me = folk.IndexOf(b);
            return me >= 0 && Colours.Turn(folk.Take(me).Count(wants), spare);
        }
        // The stockpiles' things (not the treasury's, nor a player's own chest in the colony, nor a chest someone has open).
        public static bool Stockpile((Container chest, string id, string category) x) => x.category != "" && x.category != "treasury" && !ContainerAccess.InUse(x.chest);
        private static IEnumerable<ItemDrop.ItemData> Stores(Colony c) => Stock.Chests(c).Where(Stockpile).SelectMany(x => x.chest.GetInventory().GetAllItems());

        // For OutfitTask: a garment in the colony's colours from the stores, for a slot they'd take one in, when it's their turn (those
        // above them counted only if one of the spares would suit them too).
        public static Func<ItemDrop.ItemData, bool> Wanted(Brain b)
        {
            var colours = Of(b.Colony);
            if (colours.Count == 0) return null;
            foreach (var slot in Slots)
            {
                if (!Wants(b, slot, colours)) continue;
                var spares = Stores(b.Colony).Where(i => Garment(i, slot) && Finery.Of(DyeOf(i), colours) >= 2).Select(Classes.Prefab).ToList();
                int mine = spares.Count(p => Suits(b, p));
                if (mine == 0 || !Turn(b, slot, mine, v => Wants(v, slot, colours) && spares.Any(p => Suits(v, p)))) continue;
                return i => Garment(i, slot) && Finery.Of(DyeOf(i), colours) >= 2 && Suits(b, Classes.Prefab(i));
            }
            return null;
        }
        // (a dress for a woman, a wool tunic for a man, as the weaver sews them; the rest for anyone)
        private static bool Suits(Brain b, string prefab) => prefab.Contains("Dress") ? b.S.Female : prefab.Contains("WoolTunic") ? !b.S.Female : true;
        // What a garment in the colours takes the place of (to go back to the chest): the plain one worn in that slot, if it's no sturdier.
        public static ItemDrop.ItemData Replaced(Brain b, ItemDrop.ItemData now) =>
            Gear.Items(b).FirstOrDefault(i => i != now && i.m_equipped && i.m_shared.m_itemType == now.m_shared.m_itemType && Weaving.IsClothes(i) && i.GetArmor() <= now.GetArmor() && Fine(i, Of(b.Colony)) < Fine(now, Of(b.Colony)));
        // For one in rags, clothes from the stores: the finest when it's their turn for fine ones, else the plainest (left for those above).
        public static Func<ItemDrop.ItemData, int> ClothesOrder(Brain b)
        {
            var colours = Of(b.Colony);
            if (!Compat.HomespunDyeing) return _ => 0;
            int spare = Stores(b.Colony).Count(i => Garment(i, ItemDrop.ItemData.ItemType.Chest) && Fine(i, colours) > 0);
            bool turn = spare > 0 && Turn(b, ItemDrop.ItemData.ItemType.Chest, spare, v => Weaving.InRags(v));
            return i => turn ? -Fine(i, colours) : Fine(i, colours);
        }

        // ---- the weaver at the vat ----
        // Things on their way to the vat or back from it: never worn on the way, and back to the stores if the work is left.
        private static readonly HashSet<ItemDrop.ItemData> Carried = new HashSet<ItemDrop.ItemData>();
        public static bool Carrying(ItemDrop.ItemData i) => Carried.Count > 0 && Carried.Contains(i);
        public static bool Astray(Brain b, ItemDrop.ItemData i) => Carrying(i) && !(b.Task is DyeTask d && !d.Done);
        public static bool Carries(Brain b) => Carried.Count > 0 && Gear.Items(b).Any(i => Astray(b, i));
        public static void Carry(ItemDrop.ItemData i) { if (Carried.Count > 200) Carried.Clear(); Carried.Add(i); }
        public static void Drop(ItemDrop.ItemData i) => Carried.Remove(i);

        public static Container VatNear(Colony c, Vector3 at) => c.PiecesIn<Container>(c.Radius).Where(v => v != null && Colony.NameOf(v) == Vat && v.GetInventory() != null)
            .OrderBy(v => Vector3.Distance(v.transform.position, at)).FirstOrDefault();
        public static List<(string prefab, int stack)> Contents(Container vat) => vat.GetInventory().GetAllItems().Select(i => (Classes.Prefab(i), i.m_stack)).ToList();
        // The dye a dyestuff gives, among the colours Homespun has (null: not a dyestuff).
        public static string DyeOfStuff(string prefab) => Compat.HomespunDyes.FirstOrDefault(d => Compat.HomespunDyestuff(d)?.stuff == prefab);
        public static int Need(string prefab, string dye) => Compat.HomespunDyedAs(prefab, dye) == null ? 0 : Compat.HomespunMeasures(prefab) * (Compat.HomespunDyestuff(dye)?.perMeasure ?? 0);
        public static bool Steeping(Container vat) => Colours.Steeping(Contents(vat), DyeOfStuff, Need);
        // What the weaver takes back out of a vat that's done: things dyed in the colony's colours, and the dyestuff of one.
        public static bool Ours(string prefab, IList<string> colours) => colours.Contains(Compat.HomespunDye(prefab) ?? "") || colours.Contains(DyeOfStuff(prefab) ?? "");
        // The plain garments in the stores to dye, and the garments of each colour the colony has (stores and backs).
        public static List<ItemDrop.ItemData> Waiting(Colony c, string dye) => Stores(c).Where(i => Weaving.IsClothes(i) && Gear.Usable(i) && Compat.HomespunDye(Classes.Prefab(i)) == null && Compat.HomespunDyedAs(Classes.Prefab(i), dye) != null).ToList();
        public static int In(Colony c, string dye) => Stores(c).Concat(Villagers.All.Where(v => v.S.Colony == c.Id).SelectMany(Gear.Items)).Count(i => DyeOf(i) == dye);

        // The colours and what the weaver would do now, for the colony window and the test command.
        public static string Plan(Colony c)
        {
            var colours = Of(c);
            if (colours.Count == 0) return "";
            Container vat = VatNear(c, c.Position);
            if (vat == null) return "There's no dye vat in the colony: build one (Homespun) and the weaver dyes there.";
            if (!c.R.Living.Any(r => r.Prof == Prof.Weaver)) return "No one is a weaver to dye (People tab).";
            if (Steeping(vat)) return "The vat is steeping.";
            foreach (string dye in colours)
            {
                var stuff = Compat.HomespunDyestuff(dye);
                int waiting = Waiting(c, dye).Count;
                if (stuff == null) continue;
                if (waiting == 0) return $"No plain garments in the stores to dye: everyone's {(colours.Count > 1 ? "in the colours" : "in " + Title(dye))} who can be, or the weaver's next ones will be.";
                return $"{waiting} plain garment{(waiting == 1 ? "" : "s")} in the stores to dye; the stores hold {Crafts.InStock(c, stuff.Value.stuff)} {Crafts.Name(stuff.Value.stuff).ToLower()} ({stuff.Value.perMeasure} a measure, a garment two or three, and {Colours.StuffKept} kept back) for {Title(dye)}.";
            }
            return "";
        }
    }

    // The weaver dyeing: the plain garments and the dyestuff from the stores into the vat (Homespun steeps them a minute on its own), or
    // the dyed ones back out of a vat that's done, for the stores.
    internal sealed class DyeTask : Task
    {
        private readonly Container _vat; private readonly bool _collect; private readonly string _dye, _stuff; private readonly int _stuffN;
        private readonly List<string> _things; private readonly List<ItemDrop.ItemData> _got = new List<ItemDrop.ItemData>();
        private DyeTask(Container vat, bool collect, string dye = null, string stuff = null, int stuffN = 0, List<string> things = null)
        { _vat = vat; _collect = collect; _dye = dye; _stuff = stuff; _stuffN = stuffN; _things = things ?? new List<string>(); }

        public static Task Next(Brain b, bool force = false)
        {
            Colony c = b.Colony;
            if (c == null || !c.IsOwner || b.S.Prof != Prof.Weaver || !force && Time.time < b.NoDyeUntil) return null;
            b.NoDyeUntil = Time.time + 60;
            var colours = Dyeing.Of(c);
            if (colours.Count == 0) { b.DyeShort = null; return null; }
            Container vat = Dyeing.VatNear(c, b.Pos);
            if (vat == null || ContainerAccess.InUse(vat) || b.Body.GetInventory().GetEmptySlots() < Colours.Batch + 2) return null;
            if (vat.GetInventory().NrOfItems() > 0)
            {
                if (Dyeing.Steeping(vat)) return null; // (at work: back for it after)
                return Dyeing.Contents(vat).Any(x => Dyeing.Ours(x.prefab, colours)) ? new DyeTask(vat, true) : null; // (someone else's things in it: left alone)
            }
            // (the colour with fewest garments first; another if that one's dyestuff is short)
            string shortOf = null;
            foreach (string dye in colours.OrderBy(d => Dyeing.In(c, d)).ThenBy(colours.IndexOf))
            {
                var stuff = Compat.HomespunDyestuff(dye);
                var waiting = stuff == null ? new List<ItemDrop.ItemData>() : Dyeing.Waiting(c, dye);
                if (waiting.Count == 0) continue;
                var (picked, n) = Colours.Pick(waiting.Select(i => Compat.HomespunMeasures(Classes.Prefab(i))).ToList(), stuff.Value.perMeasure, Crafts.InStock(c, stuff.Value.stuff));
                if (picked.Count == 0) { shortOf = shortOf ?? $"{Crafts.Name(stuff.Value.stuff).ToLower()} to dye {Dyeing.Title(dye)}"; continue; }
                b.DyeShort = null;
                return new DyeTask(vat, false, dye, stuff.Value.stuff, n, picked.Select(x => Classes.Prefab(waiting[x])).ToList());
            }
            if (shortOf != null && b.DyeShort != shortOf)
                b.Say(new[] { $"Not enough {shortOf} in the stores.", $"The stores are short of {shortOf}.", $"I'd dye our colours, but there's not enough {shortOf}." }[UnityEngine.Random.Range(0, 3)], 6);
            b.DyeShort = shortOf;
            if (shortOf != null) b.NoDyeUntil = Time.time + 300;
            return null;
        }
        public override void Start(Brain b) => Doing = _collect ? "taking the dyed cloth out of the vat" : $"dyeing the colony's clothes {Dyeing.Title(_dye)}";
        public override void Stop(Brain b) { base.Stop(b); Done = true; } // (what's carried goes back to the stores: Dyeing.Astray)
        private int Have(Brain b, string prefab) => b.Body.GetInventory().GetAllItems().Where(i => Classes.Prefab(i) == prefab && !i.m_equipped && (prefab == _stuff || _got.Contains(i))).Sum(i => i.m_stack);
        public override void Tick(Brain b)
        {
            if (Done) return;
            if (_vat == null || ContainerAccess.InUse(_vat)) { Done = true; return; }
            Inventory bag = b.Body.GetInventory();
            if (!_collect)
            {
                // fetch: each garment, then the dyestuff
                var need = _things.GroupBy(x => x).Select(g => (item: g.Key, n: g.Count())).Concat(new[] { (item: _stuff, n: _stuffN) }).FirstOrDefault(x => Have(b, x.item) < x.n);
                if (need.item != null)
                {
                    var from = Stock.ChestsFor(b).Where(Dyeing.Stockpile)
                        .Select(x => (x.chest, item: x.chest.GetInventory().GetAllItems().FirstOrDefault(i => Classes.Prefab(i) == need.item && (need.item == _stuff || Compat.HomespunDye(need.item) == null && Gear.Usable(i)))))
                        .FirstOrDefault(x => x.item != null);
                    if (from.chest == null)
                    {
                        if (need.item == _stuff) { Done = true; b.NoDyeUntil = Time.time + 120; return; } // (taken meanwhile: the rest goes back)
                        _things.Remove(need.item); // (a garment taken meanwhile: one fewer)
                        if (_things.Count == 0) Done = true;
                        return;
                    }
                    Stock.Walk walk = Stock.WalkTo(b, from.chest);
                    if (walk == Stock.Walk.Unreachable) { Done = true; return; }
                    if (walk == Stock.Walk.Walking) return;
                    var before = bag.GetAllItems().ToList();
                    if (Stock.Take(from.chest, bag, from.item, need.item == _stuff ? need.n - Have(b, _stuff) : 1) == 0) { Done = true; return; }
                    foreach (ItemDrop.ItemData i in bag.GetAllItems().Where(i => !before.Contains(i) && Classes.Prefab(i) == need.item)) { _got.Add(i); Dyeing.Carry(i); }
                    if (need.item == _stuff) b.Hauled.Add(_stuff); // (not eaten on the way; back to the stores if left)
                    return;
                }
                // into the vat (only an empty one: another's dye in it would spoil the batch), the dyestuff first
                if (!Reached(b)) return;
                if (_vat.GetInventory().NrOfItems() > 0) { Done = true; return; }
                int left = _stuffN;
                foreach (ItemDrop.ItemData st in bag.GetAllItems().Where(i => Classes.Prefab(i) == _stuff && !i.m_equipped).ToList())
                {
                    if (left <= 0) break;
                    if (st.m_stack > left) { ItemDrop.ItemData part = st.Clone(); part.m_stack = left; if (!_vat.GetInventory().CanAddItem(part)) break; ContainerAccess.TakeOver(_vat); _vat.GetInventory().AddItem(part); bag.RemoveItem(st, left); left = 0; }
                    else left -= Stock.Put(bag, _vat, st);
                }
                if (!bag.GetAllItems().Any(i => Classes.Prefab(i) == _stuff)) b.Hauled.Remove(_stuff);
                int put = 0;
                foreach (ItemDrop.ItemData g in _got.Where(i => bag.ContainsItem(i) && Classes.Prefab(i) != _stuff).ToList()) { if (Stock.Put(bag, _vat, g) > 0) { Dyeing.Drop(g); put++; } }
                b.Body.RaiseSkill(Skills.SkillType.Crafting, 0.2f); b.Dirty();
                b.Say(new[] { $"Into the vat, and out {Dyeing.Title(_dye)}.", $"{put} for the vat: {Dyeing.Title(_dye)}, for the colony.", $"Steep well, and come out {Dyeing.Title(_dye)}.", "Our colours, soon enough." }[UnityEngine.Random.Range(0, 4)], 4);
                b.NoDyeUntil = Time.time + 75; // (back once it has steeped)
                Done = true; return;
            }
            // out of the vat: what's ours, for the stores (Dyeing.Astray: carried back by the next errand)
            if (!Reached(b)) return;
            if (Dyeing.Steeping(_vat)) { Done = true; return; }
            var colours = Dyeing.Of(b.Colony); int took = 0;
            foreach (ItemDrop.ItemData i in _vat.GetInventory().GetAllItems().Where(i => Dyeing.Ours(Classes.Prefab(i), colours)).ToList())
            {
                string p = Classes.Prefab(i); bool stuff = Dyeing.DyeOfStuff(p) != null;
                var before = bag.GetAllItems().ToList();
                if (Stock.Take(_vat, bag, i, i.m_stack) == 0) continue;
                took++;
                if (stuff) b.Hauled.Add(p);
                else foreach (ItemDrop.ItemData n in bag.GetAllItems().Where(n => !before.Contains(n) && Classes.Prefab(n) == p)) Dyeing.Carry(n);
            }
            if (took > 0) { b.S.Bump("dyed", took); b.Dirty(); b.Say(new[] { "Look at that colour.", "Well dyed, if I say so myself.", "The colony's colours, fresh from the vat.", "Out of the vat, and to the stores." }[UnityEngine.Random.Range(0, 4)], 4); }
            Done = true;
        }
        private bool Reached(Brain b)
        {
            Stock.Walk w = Stock.WalkTo(b, _vat);
            if (w == Stock.Walk.Unreachable) { Done = true; b.NoDyeUntil = Time.time + 300; return false; }
            return w == Stock.Walk.Reached;
        }
    }
}
