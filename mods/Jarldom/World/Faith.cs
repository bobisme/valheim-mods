using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jarldom
{
    // The gods: favour from offerings at their altars and in the sacred bog; with Omens installed Odin's favour is Omens' Fate.
    internal static class Faith
    {
        public static string FavourWord(int f) => f <= -60 ? "wrathful" : f < -10 ? "displeased" : f <= 10 ? "watching" : f < 60 ? "pleased" : "delighted";
        public static float OdinWorthBonus(Colony c) => c != null && Odin(c) >= 30 ? 0.25f : 0;
        public static int Odin(Colony c) => Compat.OmensFate(out int fate) ? fate * 20 : c.FavourOf("odin");

        public static bool Wants(string god, ItemDrop.ItemData item)
        {
            GodInfo g = Tables.God(god);
            string n = item.m_dropPrefab != null ? item.m_dropPrefab.name : "";
            return g != null && g.OfferingItems.Any(o => n.StartsWith(o) || o == "Fish" && item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Fish || o == "Trophy" && item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy);
        }
        public static void OfferFromBag(Altar altar, Player p)
        {
            ItemDrop.ItemData item = p.GetInventory().GetAllItems().Where(i => !i.m_equipped && Wants(altar.God, i)).OrderByDescending(i => Value(i)).FirstOrDefault();
            if (item == null) { p.Message(MessageHud.MessageType.Center, $"{Tables.God(altar.God)?.Name} wants {Tables.God(altar.God)?.Offerings}: you carry none."); return; }
            Offer(altar, p, item, 1);
        }
        public static double Value(ItemDrop.ItemData item) => item.m_shared.m_value > 0 ? item.m_shared.m_value : Colony.MaterialValue(item.m_dropPrefab?.GetComponent<ItemDrop>()) * 3 + item.m_shared.m_food / 4 + (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy ? 10 : 0);
        public static void Offer(Altar altar, Player p, ItemDrop.ItemData item, int amount)
        {
            Colony c = Colony.At(altar.transform.position);
            if (c == null) { p.Message(MessageHud.MessageType.Center, "The altar must stand in a colony."); return; }
            bool wanted = Wants(altar.God, item);
            if (!wanted) { p.Message(MessageHud.MessageType.Center, $"{Tables.God(altar.God)?.Name} does not want that."); return; }
            amount = Math.Max(1, Math.Min(amount, item.m_stack));
            int points = Favour.Offering(Value(item) * amount, true);
            string name = Localization.instance.Localize(item.m_shared.m_name);
            p.GetInventory().RemoveItem(item, amount);
            Assets.Effect("vfx_offering", altar.transform.position + Vector3.up);
            Assets.Effect("sfx_offering", altar.transform.position);
            Net.Act(c, "offer", $"{altar.God}|{points}|{name}");
        }
        // On the colony owner.
        public static void Received(Colony c, string god, int points, string what, string who)
        {
            if (Tables.God(god) == null || points <= 0) return;
            Add(c, god, points, $"{who} offered {what}");
            c.Log("offering", who, Tables.God(god).Name);
            foreach (Brain b in Villagers.All.Where(b => b.S.Colony == c.Id && b.S.Has("pious"))) { Mood.Satisfy(b.S, Need.Faith, 10); b.Dirty(); }
        }
        public static void Add(Colony c, string god, int points, string why)
        {
            if (god == "odin" && Compat.OmensAddFate(c, points, why)) return;
            c.R.Favour[god] = Favour.Clamp(c.FavourOf(god) + points);
            c.MarkDirty();
        }

        // Coins thrown into the sacred bog: favour without end, at a rising price (the colony owner finds them on its scan).
        public static void Bog(Colony c)
        {
            foreach (Zone z in c.R.Zones.Where(z => z.Type == ZoneType.SacredBog))
                foreach (ItemDrop d in Work.Drops(Zones.Centre(z), Zones.Reach(z) + 1))
                {
                    if (d.m_itemData.m_dropPrefab == null || d.m_itemData.m_dropPrefab.name != "Coins" || !Zones.Inside(z, d.transform.position)) continue;
                    ZNetView v = d.GetComponent<ZNetView>();
                    if (!v.IsOwner()) { d.RequestOwn(); continue; }
                    int coins = d.m_itemData.m_stack;
                    c.R.Flags["bogcoins"] = c.R.Flag("bogcoins") + coins;
                    v.Destroy();
                    Assets.Effect("vfx_offering", d.transform.position);
                    while (c.R.Flag("bogcoins") >= Favour.BogPrice(c.R.BogPaid))
                    {
                        c.R.Flags["bogcoins"] -= Favour.BogPrice(c.R.BogPaid);
                        c.R.BogPaid++;
                        Add(c, "odin", 1, "the bog took coins");
                    }
                    c.MarkDirty();
                }
        }
    }

    // The way to Valhalla: a worthy soul, a funeral pyre, grave goods, a fire at dusk.
    internal static class Ascension
    {
        public const string BurnKey = "jarldom_burn";
        public static string Why(Colony c, VillagerState s)
        {
            if (!s.Worthy) return $"{s.Name} is not yet worthy ({Mathf.RoundToInt(s.Worth)} of 100)";
            if (Pyre(c) == null) return "the colony has no funeral pyre";
            if (c.Hour < Schedule.HallAt && c.Hour >= Schedule.WakeAt) return "a soul leaves at dusk";
            return "";
        }
        public static global::Jarldom.Pyre Pyre(Colony c) => c.PiecesIn(c.Radius).Select(p => p.GetComponent<global::Jarldom.Pyre>()).FirstOrDefault(p => p != null);
        public static void Begin(Colony c, string vid, string who)
        {
            Brain b = Villagers.ByVid(vid);
            VillagerState s = b?.S;
            if (s == null) { Villagers.Command(vid, "ascend", c.Id); return; }
            string why = Why(c, s);
            if (why != "") { c.Tell("Not now: " + why + "."); return; }
            c.Log("text", s.Name, "", $"{who} sends {s.FullName} to Valhalla.");
            Villagers.Command(vid, "ascend", c.Id);
        }
        // On the pyre's owner: the fire burns a minute after it's lit.
        public static void PyreTick(global::Jarldom.Pyre pyre)
        {
            ZNetView v = pyre.GetComponent<ZNetView>();
            if (v == null || !v.IsValid()) return;
            long lit = v.GetZDO().GetLong(BurnKey, 0L);
            bool burning = lit > 0 && (ZNet.instance.GetTime().Ticks - lit) / TimeSpan.TicksPerSecond < 90;
            Transform fire = pyre.transform.Find("PyreFire");
            if (fire != null && fire.gameObject.activeSelf != burning) fire.gameObject.SetActive(burning);
        }

        // On the colony owner's game, once (the soul's record says it has gone, so a second burn finds nothing to reward).
        public static void Burn(Colony c, global::Jarldom.Pyre pyre, Brain b)
        {
            VillagerState s = b.S;
            if (c.R.Flag("ascended:" + s.Vid) != 0) return;
            c.R.Flags["ascended:" + s.Vid] = 1;
            Container goods = pyre.GetComponent<Container>();
            int value = s.Hoard;
            if (goods != null)
            {
                ContainerAccess.TakeOver(goods);
                foreach (ItemDrop.ItemData item in goods.GetInventory().GetAllItems().ToList()) value += (int)(Faith.Value(item) * item.m_stack);
                goods.GetInventory().RemoveAll();
            }
            Inventory bag = b.Body.GetInventory();
            foreach (ItemDrop.ItemData item in bag.GetAllItems().ToList()) value += (int)(Faith.Value(item) * item.m_stack); // they take what they carry
            bool sworn = !string.IsNullOrEmpty(s.Sworn) && Villagers.ByVid(s.Sworn)?.Task is AscendTask;
            c.R.Deeds += Worth.AscensionDeeds(value, sworn);
            Faith.Add(c, "odin", 10, s.Name + " went to Valhalla");
            c.R.GuaranteedArrival = 1;
            pyre.GetComponent<ZNetView>().GetZDO().Set(BurnKey, ZNet.instance.GetTime().Ticks);
            RosterRow row = c.R.Row(s.Vid); if (row != null) row.State = SoulState.Ascended;
            c.ReleaseBed(s.Vid);
            c.Log("ascend", s.FullName, "");
            c.Log("text", "", "", $"A runestone remembers {s.FullName}: {s.Story} Worth {Mathf.RoundToInt(s.Worth)}, {s.Count("kills")} foes slain.");
            Blessing(c);
            foreach (Brain other in Villagers.All.Where(o => o != b && o.S.Colony == c.Id)) { if (other.S.Bonds.TryGetValue(s.Vid, out int op) && op > 40) Mood.Add(other.S, "festival", other.Now); other.Dirty(); }
            b.Release();
            Assets.Effect("vfx_spawn", b.Pos);
            b.View.Destroy();
            c.MarkDirty(); c.Flush();
            c.Rerank();
        }
        // A day's blessing for everyone: one of three, by the god best pleased.
        private static void Blessing(Colony c)
        {
            foreach (Brain v in Villagers.All.Where(v => v.S.Colony == c.Id)) { Mood.Add(v.S, "blessed", v.Now); v.Dirty(); }
            c.Tell("Odin is pleased: the colony is blessed for a day.", true);
        }
    }

    // Walking to the pyre and going up in flames.
    internal sealed class AscendTask : Task
    {
        private readonly string _colony; private global::Jarldom.Pyre _pyre; private bool _there;
        public AscendTask(string colony) { _colony = colony; }
        public override bool Interruptible => false;
        public override void Start(Brain b)
        {
            Doing = "going to Valhalla";
            _pyre = Ascension.Pyre(b.Colony);
            if (_pyre == null) { Done = true; return; }
            b.Move.To(_pyre.transform.position, 2f);
            b.Say("I hear them. The Valkyries are singing.", 6);
        }
        public override void Tick(Brain b)
        {
            if (_pyre == null) { Done = true; return; }
            if (!_there) { if (b.Move.Moving && !b.Move.Failed) return; _there = true; b.Say("Remember me in the hall.", 5); return; }
            if (b.Colony.IsOwner) Ascension.Burn(b.Colony, _pyre, b);
            else { b.Say("(the colony's owner must be near to light the pyre)", 5); Done = true; }
        }
    }
}
