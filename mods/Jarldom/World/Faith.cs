using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jarldom
{
    // The gods: favour from offerings at their altars and in the sacred bog; with Omens installed Odin's favour is Omens' Fate.
    internal static class Faith
    {
        // A god's blessing: a temple to them that passes, and their favour above nothing.
        public static bool Blessed(Colony c, string god) => c != null && c.R != null && c.R.Rooms.Any(r => r.Ok && r.Type == RoomType.Temple && r.God == god) && c.FavourOf(god) > 0;

        // Thor: the colony's villagers strike a tenth harder.
        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        private static class ThorsArm
        {
            private static void Prefix(Character __instance, HitData hit)
            {
                Character attacker = hit?.GetAttacker();
                if (attacker == null || !VillagerPrefab.Is(attacker) || VillagerPrefab.Is(__instance) || __instance.IsPlayer()) return;
                if (Blessed(Colony.At(attacker.transform.position), "thor")) hit.m_damage.Modify(1.1f);
            }
        }
        // Freyja: crops and saplings in the colony grow a fifth faster.
        [HarmonyPatch(typeof(Plant), "GetGrowTime")]
        private static class FreyjasHand
        {
            private static void Postfix(Plant __instance, ref float __result)
            {
                if (Blessed(Colony.At(__instance.transform.position), "freyja")) __result *= 0.8f;
            }
        }
        // Thor's lightning on raiders inside the radius, now and then while a raid lasts (stronger as he is better pleased).
        private static float _nextBolt;
        public static void Thunder(Colony c)
        {
            if (!c.RaidOn || Time.time < _nextBolt || !Blessed(c, "thor") || c.FavourOf("thor") < 30) return;
            _nextBolt = Time.time + UnityEngine.Random.Range(15f, 30f);
            Character foe = Character.GetAllCharacters().Where(ch => ch != null && !ch.IsDead() && !ch.IsPlayer() && !VillagerPrefab.Is(ch) && !ch.IsTamed() && !Tribute.IsHerald(ch) && !Visitors.IsMerc(ch)
                    && ch.GetFaction() != Character.Faction.Players && ch.GetFaction() != Character.Faction.AnimalsVeg && Utils.DistanceXZ(ch.transform.position, c.Position) < c.Radius)
                .OrderBy(_ => UnityEngine.Random.value).FirstOrDefault();
            if (foe == null) return;
            Assets.Effect("fx_Lightning", foe.transform.position);
            var hit = new HitData { m_point = foe.GetCenterPoint(), m_dir = Vector3.down };
            hit.m_damage.m_lightning = 40 + c.FavourOf("thor") / 2;
            foe.Damage(hit);
        }
        public static string FavourWord(int f) => f <= -60 ? "wrathful" : f < -10 ? "displeased" : f <= 10 ? "watching" : f < 60 ? "pleased" : "delighted";
        public static float OdinWorthBonus(Colony c) => c != null && Odin(c) >= 30 ? 0.25f : 0;
        public static int Odin(Colony c) => Compat.OmensFate(out int fate) ? Favour.Clamp(fate * 20 + c.R.Flag("omenspool")) : c.FavourOf("odin"); // (with Omens: its fate in steps of 20, and what's put by toward the next)

        // A weapon for the war gods: not a bow, and not a tool that happens to be held two-handed (a pickaxe).
        private static bool Blade(ItemDrop.ItemData i) => Classes.IsWeapon(i) && i.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Bow && i.m_shared.m_skillType != Skills.SkillType.Pickaxes;
        public static bool Wants(string god, ItemDrop.ItemData item)
        {
            GodInfo g = Tables.God(god);
            if (g == null || item == null) return false;
            string n = item.m_dropPrefab != null ? item.m_dropPrefab.name : "";
            var t = item.m_shared.m_itemType;
            // (by what a thing is, so every weapon, hide or stew of a kind is wanted alike; the names below are only the odd ones out)
            switch (god)
            {
                case "odin": if (Classes.IsMead(item) || t == ItemDrop.ItemData.ItemType.Trophy || n == "Coins") return true; break;
                case "thor": if (Blade(item) || t == ItemDrop.ItemData.ItemType.Trophy) return true; break;
                case "tyr": if (Blade(item) || t == ItemDrop.ItemData.ItemType.Shield) return true; break;
                case "ullr": if (t == ItemDrop.ItemData.ItemType.Bow || Stock.Category(item) == "hides") return true; break;
                case "frigg": if (t == ItemDrop.ItemData.ItemType.Consumable && item.m_shared.m_food >= 25 && Stock.Category(item) == "food" && !Classes.IsMead(item)) return true; break; // (cooked food, not raw; not the weaver's thread)
                case "freyja": if (n == "Honey" || n == "Dandelion" || n == "Flax" || n == "Barley" || n == "Carrot" || n == "Turnip" || n == "Onion") return true; break;
                case "njord": if (t == ItemDrop.ItemData.ItemType.Fish || n.StartsWith("Fish") && !Classes.IsEquipment(item) || n == "Amber" || n == "AmberPearl") return true; break;
            }
            return g.OfferingItems.Any(o => n == o || n.StartsWith(o) && !Classes.IsEquipment(item) && god == "hel");
        }
        public static void OfferFromBag(Altar altar, Player p)
        {
            ItemDrop.ItemData item = p.GetInventory().GetAllItems().Where(i => !i.m_equipped && Wants(altar.God, i)).OrderByDescending(i => Value(i)).FirstOrDefault();
            if (item == null) { p.Message(MessageHud.MessageType.Center, $"{Tables.God(altar.God)?.Name} wants {Tables.God(altar.God)?.Offerings}: you carry none."); return; }
            Offer(altar, p, item, 1);
        }
        public static double Value(ItemDrop.ItemData item) => item.m_shared.m_value > 0 ? item.m_shared.m_value : Colony.MaterialValue(item.m_dropPrefab != null ? item.m_dropPrefab.GetComponent<ItemDrop>() : null) * 3 + item.m_shared.m_food / 4 + (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Trophy ? 10 : 0);
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

namespace Jarldom
{
    // The gothi keeps the colony's temples: once a day at each, an offering from the stores of what its god wants (the cheapest such
    // thing, one of it), laid on the altar as a player would; otherwise they mind a temple.
    internal sealed class GothiTask : Task
    {
        private readonly Room _temple; private Piece _altar; private string _god; private Container _from; private string _gift; private int _phase;
        public GothiTask(Room temple) { _temple = temple; }
        public override bool Interruptible => _phase == 0;
        public static Task Next(Brain b)
        {
            Colony c = b.Colony;
            if (c == null || !c.IsOwner) return null;
            var temples = c.R.Rooms.Where(r => r.Ok && (r.Type == RoomType.Temple || r.Type == RoomType.OdinShrine)).ToList();
            if (temples.Count == 0) return null;
            Room due = Time.time > b.NoOfferUntil ? temples.FirstOrDefault(r => c.R.Flag("offered:" + r.Id) != c.Day) : null;
            if (due != null) return new GothiTask(due);
            Room home = temples[(b.S.Vid.GetStableHashCode() & 0x7fffffff) % temples.Count];
            return new WanderTask(c.Centre(home), 2.5f, "keeping the temple");
        }
        public override void Start(Brain b)
        {
            Colony c = b.Colony;
            // (by its prefab's name, as the room's check finds it: altars from before a reload keep the old Altar component)
            _altar = c.PiecesIn(c.Radius).Where(p => Utils.GetPrefabName(p.gameObject).StartsWith(Pieces.AltarPrefix) && c.RoomAt(p.transform.position + Vector3.up * 0.3f, _temple.Type) == _temple)
                .OrderBy(p => Vector3.Distance(p.transform.position, c.Centre(_temple))).FirstOrDefault();
            if (_altar == null) { Skip(b, "the temple has no altar"); return; }
            string god = _god = Utils.GetPrefabName(_altar.gameObject).Substring(Pieces.AltarPrefix.Length);
            var gifts = Stock.Chests(c, b.Pos).Where(x => x.category != "treasury" && !ContainerAccess.InUse(x.chest))
                .SelectMany(x => x.chest.GetInventory().GetAllItems().Where(i => i.m_dropPrefab != null && !Items.IsRing(i) && Faith.Wants(god, i) && Faith.Value(i) > 0).Select(i => (x.chest, i)))
                .OrderBy(g => Faith.Value(g.i)).ToList();
            if (gifts.Count == 0) { Skip(b, $"the stores hold nothing {Tables.God(god)?.Name} wants ({Tables.God(god)?.Offerings})"); return; }
            _from = gifts[0].chest; _gift = gifts[0].i.m_dropPrefab.name;
            Doing = "fetching an offering for " + Tables.God(god)?.Name;
            b.Move.To(_from.transform.position, 1.8f);
        }
        // Nothing to offer (or no altar): tried again in a while (the stores may only just be loading), and said in the saga when the
        // reason is new, or again after three days of it (not every day the same words).
        private void Skip(Brain b, string why)
        {
            Done = true; b.NoOfferUntil = Time.time + 300; b.Idle(why);
            string k = "offerwhy:" + _temple.Id, kh = "offerwhyh:" + _temple.Id; int h = why.GetStableHashCode();
            if (b.Colony.R.Flags.ContainsKey(k) && b.Colony.R.Flag(kh) == h && b.Colony.Day - b.Colony.R.Flag(k) < 3) return;
            b.Colony.R.Flags[k] = b.Colony.Day; b.Colony.R.Flags[kh] = h; b.Colony.MarkDirty();
            b.Colony.Log("text", b.S.Name, "", $"{b.S.Name} the gothi has no offering to make yet: {why}.");
        }
        public override void Tick(Brain b)
        {
            if (Done) return;
            if (_altar == null || _from == null) { Done = true; return; }
            Inventory bag = b.Body.GetInventory();
            if (_phase == 0)
            {
                if (Stock.OutOfReach(b.Pos, _from.transform.position, 2.4f)) { if (!b.Move.Moving) b.Move.To(_from.transform.position, 1.8f); if (b.Move.Failed) { Done = true; b.NoOfferUntil = Time.time + 120; } return; }
                ItemDrop.ItemData stack = _from.GetInventory().GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == _gift);
                if (stack == null || Stock.Take(_from, bag, stack, 1) == 0) { Done = true; b.NoOfferUntil = Time.time + 60; return; }
                _phase = 1; Doing = "going to the altar of " + Tables.God(_god)?.Name;
                b.Move.To(_altar.transform.position, 1.8f);
                return;
            }
            Stock.Walk aw = Stock.WalkNear(b, _altar.transform.position, 2.4f, 1.8f);
            if (aw != Stock.Walk.Reached) { if (aw == Stock.Walk.Unreachable) { Done = true; b.NoOfferUntil = Time.time + 120; } return; }
            b.Move.Stop();
            Vector3 d = _altar.transform.position - b.Pos; d.y = 0; if (d.sqrMagnitude > 0.01f) b.Ai.LookTowards(d.normalized);
            ItemDrop.ItemData gift = bag.GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == _gift);
            if (gift == null) { Done = true; return; }
            Body_.Anim(b.Body)?.SetTrigger("interact");
            int points = Favour.Offering(Faith.Value(gift), true);
            string name = Localization.instance.Localize(gift.m_shared.m_name);
            bag.RemoveItem(gift, 1);
            Assets.Effect("vfx_offering", _altar.transform.position + Vector3.up);
            Assets.Effect("sfx_offering", _altar.transform.position);
            Faith.Received(b.Colony, _god, points, name.ToLower(), b.S.Name);
            b.Colony.R.Flags["offered:" + _temple.Id] = b.Colony.Day;
            b.S.Bump("offerings");
            Mood.Satisfy(b.S, Need.Faith, 20);
            b.Dirty(); b.Colony.MarkDirty();
            Done = true;
        }
    }
}
