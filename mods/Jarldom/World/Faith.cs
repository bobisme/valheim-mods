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
            if (c.IsOwner) { Ascensions.Ordered(c.R.Flags, vid, c.Day); c.MarkDirty(); } // (remembered: an interrupted ceremony goes on)
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
            if (!Ascensions.Claim(c.R.Flags, s.Vid, c.Day)) return;
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
            b.Say(new[] { "I hear them. The Valkyries are singing.", "Can you hear that? The Valkyries are singing.", "The Valkyries sing for me. I can hear them.", "Listen. The Valkyries are singing.", "The Valkyries are singing, and it's for me." }[UnityEngine.Random.Range(0, 5)], 6);
        }
        public override void Tick(Brain b)
        {
            if (_pyre == null) { Done = true; return; }
            if (!_there) { if (b.Move.Moving && !b.Move.Failed) return; _there = true; b.Say(new[] { "Remember me in the hall.", "Remember me when the ale is poured.", "Tell them I went well, in the hall.", "Remember me at the feast.", "Don't forget me in the hall." }[UnityEngine.Random.Range(0, 5)], 5); return; }
            if (b.Colony.IsOwner) Ascension.Burn(b.Colony, _pyre, b);
            else { b.Say("(the colony's owner must be near to light the pyre)", 5); Done = true; }
        }
    }
}

namespace Jarldom
{
    // The gothi keeps the colony's temples: once a day at each, an offering from the stores of what its god wants (the cheapest such
    // thing, one of it), laid on the altar as a player would; otherwise they mind a temple.
    // The cleansing rite, an ordeal of nearly a minute: the gothi goes to Odin's altar and calls the one it's for to kneel; those idle near
    // come to watch; the gothi calls on Odin three times, walking round them, and burns the offering; then the ordeal: ash-smoke pours
    // off the touched, and Odin answers. The kneeling is held (the game's own kneel, kept on) until the verdict.
    internal sealed class CleanseTask : Task
    {
        private readonly Brain _them; private Piece _altar; private float _until, _start; private bool _called; private int _step;
        private readonly List<Brain> _watchers = new List<Brain>();
        public CleanseTask(Brain them) { _them = them; }
        public override bool Interruptible => _start == 0;
        private Vector3 Front => _front;
        private Vector3 _front;
        private Vector3 _kneel; private Room _room;
        private Vector3 Kneel => _kneel;
        // Where they kneel: before the altar, inside its shrine (closer in, in a small one), never out past its wall or door.
        private void PlaceKneel(Colony c)
        {
            _room = c.RoomAt(_altar.transform.position + Vector3.up * 0.3f);
            _kneel = _altar.transform.position + _altar.transform.forward * 1.5f;
            foreach (float d in new[] { 2.6f, 2.2f, 1.8f })
            {
                Vector3 p = _altar.transform.position + _altar.transform.forward * d;
                if (InShrine(p, 0.5f)) { _kneel = p; break; }
            }
            // (the gothi between them and the altar; in a small shrine, where there's no room for both, beside the altar)
            Transform t = _altar.transform;
            _front = t.position + t.forward * 1.2f;
            if (Utils.DistanceXZ(_kneel, t.position) < 2.4f)
                foreach (Vector3 side in new[] { t.right * 1.1f, -t.right * 1.1f, t.right * 0.8f, -t.right * 0.8f })
                    if (InShrine(t.position + side + t.forward * 0.4f, 0.25f)) { _front = t.position + side + t.forward * 0.4f; break; }
        }
        private bool InShrine(Vector3 p, float margin) => _room == null || new[] { Vector2.zero, Vector2.left, Vector2.right, Vector2.up, Vector2.down }.All(o => Geometry.Inside(_room.Xz, p.x + o.x * margin, p.z + o.y * margin));
        private bool Knelt => Utils.DistanceXZ(_them.Pos, Kneel) < 1f || Utils.DistanceXZ(_them.Pos, Kneel) < 2f && InShrine(_them.Pos, 0f);
        public override void Start(Brain b)
        {
            _altar = Saboteur.OdinAltar(b.Colony);
            Doing = "going to Odin's altar for a cleansing rite"; _until = Time.time + 120f;
            if (_altar != null) { PlaceKneel(b.Colony); b.Move.To(Front, 1.2f); }
        }
        private static readonly string[] Calling = { "Allfather, one-eyed, wise: look down on this one.", "Odin, who hung nine nights on the tree: hear us now.", "Hrafnagud, send your ravens: let them see what we cannot." };
        private static readonly string[] Pressing = { "If the Forsaken's ash is on them, let it burn!", "Nothing hides from the Allfather's eye.", "By the ring and the oath: show us what they are." };
        private static readonly string[] Burning = { "Take this gift, Odin, and give us the truth.", "Silver for the Allfather: let the fire speak.", "The offering burns. Answer us." };
        private static readonly string[] Writhe = { "It burns! Make it stop!", "Get it off me!", "No... no, not the fire...", "Something's tearing loose!" };
        private static readonly string[] Calm = { "I have nothing to hide.", "Look all you like, Allfather.", "Let him look. I'm clean.", "Get on with it, then." };
        public override void Tick(Brain b)
        {
            if (_altar == null || _them == null || !_them.Alive) { End(b); Done = true; return; }
            if (Time.time > _until && _start == 0) { Done = true; b.NoRiteUntil = Time.time + 300; b.Say("They didn't come. The rite must wait.", 4); return; } // (put off, not lost)
            if (_start == 0)
            {
                if (Utils.DistanceXZ(b.Pos, Front) > 2f) { if (!b.Move.Moving) b.Move.To(Front, 1.2f); return; }
                if (!_called) { _called = true; Call(_them, Kneel); b.Say($"{_them.S.Name}, come and kneel before Odin.", 4); }
                if (!Knelt) { if (!(_them.Task is GoTask)) Call(_them, Kneel); return; }
                _start = Time.time; Doing = "holding a cleansing rite";
                _them.Task?.Stop(_them); _them.Task = new KneelTask(Time.time + 60f, _altar.transform.position); _them.Task.Start(_them);
                Gather(b);
                return;
            }
            float t = Time.time - _start;
            bool touched = Saboteur.Touched(_them.S);
            Vector3 at = _them.Pos;
            // (the gothi walks round the kneeling one as the rite goes on: at the altar, to one side, behind, the other side, back)
            Vector3 Round(float deg) => at + Quaternion.Euler(0, deg, 0) * (Front - at).normalized * 1.8f;
            b.Ai.LookTowards((at - b.Pos).normalized);
            switch (_step)
            {
                case 0: Speak(b, Calling, "emote_point"); _step++; break;
                case 1: if (t > 8) { b.Move.To(Round(90), 0.6f); _step++; } break;
                case 2: if (t > 11) { Speak(b, Pressing, "emote_challenge"); _step++; } break;
                case 3: if (t > 18) { b.Move.To(Front, 0.6f); _step++; } break;
                case 4: if (t > 21) { Speak(b, Burning, "emote_point"); Assets.Effect("vfx_FireAddFuel", _altar.transform.position + Vector3.up); Assets.Effect("sfx_FireAddFuel", _altar.transform.position); Assets.Effect("fx_shaman_fireball_expl", _altar.transform.position + Vector3.up * 1.2f); _step++; } break;
                case 5: if (t > 26) // the ordeal
                    {
                        if (touched) { Assets.Effect("vfx_odin_despawn", at + Vector3.up); Assets.Effect("sfx_wraith_death", at); _them.Say(Writhe[UnityEngine.Random.Range(0, Writhe.Length)], 4); }
                        else _them.Say(Calm[UnityEngine.Random.Range(0, Calm.Length)], 4);
                        foreach (Brain w in _watchers.Where(w => w != null && w.Alive && UnityEngine.Random.value < 0.5f)) w.Say(touched ? new[] { "Gods, look at that!", "The ash! I knew it.", "Stand back!" }[UnityEngine.Random.Range(0, 3)] : new[] { "Nothing on them.", "Clean, by the look.", "Told you." }[UnityEngine.Random.Range(0, 3)], 3);
                        _step++;
                    }
                    break;
                case 6: if (t > 33) { b.Move.To(Round(-90), 0.6f); if (touched) Assets.Effect("vfx_odin_despawn", at + Vector3.up * 0.5f); _step++; } break;
                case 7: if (t > 36) { Speak(b, new[] { "Odin! Take what is yours, and leave what is ours!", "Allfather, give us your answer!" }, "emote_challenge"); _step++; } break;
                case 8: if (t > 44) Verdict(b, touched); break;
            }
        }
        private void Verdict(Brain b, bool touched)
        {
            bool before = touched;
            Saboteur.Rite(b.Colony, b, _them);
            bool cured = before && !Saboteur.Touched(_them.S);
            Vector3 at = _them.Pos;
            if (cured) { Assets.Effect("vfx_ghost_death", at + Vector3.up); Assets.Effect("sfx_ghost_death", at); b.Say("It's gone. Stand up, and be one of us.", 5); }
            else if (before) { _them.Say(new[] { "Ha. Your one-eyed god is deaf.", "Is that all?", "It stays." }[UnityEngine.Random.Range(0, 3)], 4); b.Say("Odin would not hear it. The ash clings still.", 5); }
            else b.Say("No ash on them. They are clean.", 5);
            End(b);
            if (cured || !before) Body_.Anim(_them.Body)?.SetTrigger("emote_cheer");
            foreach (Brain w in _watchers.Where(w => w != null && w.Alive))
            {
                if (UnityEngine.Random.value < 0.6f) w.Say(cured ? new[] { "Hail Odin!", "Well done, gothi!", "Welcome back." }[UnityEngine.Random.Range(0, 3)] : before ? new[] { "Gods help us.", "It didn't take.", "Then what now?" }[UnityEngine.Random.Range(0, 3)] : new[] { "There, you see.", "Sorry for the talk.", "Clean as snow." }[UnityEngine.Random.Range(0, 3)], 4);
                Body_.Anim(w.Body)?.SetTrigger(cured || !before ? "emote_cheer" : "emote_shrug");
            }
            Done = true;
        }
        private static void Speak(Brain b, string[] lines, string emote) { b.Say(lines[UnityEngine.Random.Range(0, lines.Length)], 6); Body_.Anim(b.Body)?.SetTrigger(emote); }
        private static void Call(Brain them, Vector3 to) { them.Task?.Stop(them); them.Task = new GoTask(to, "called to Odin's altar", 1.0f); them.Task.Start(them); }
        // Those idle near come to watch, in a half-ring behind the kneeling one (at most six).
        private void Gather(Brain b)
        {
            Vector3 c = Kneel, back = (Kneel - Front).normalized;
            var idle = Villagers.All.Where(o => o != b && o != _them && o.Alive && o.S.Colony == b.S.Colony && Vector3.Distance(o.Pos, c) < 30f && (o.Task is LeisureTask || o.Task is HallTask || o.Task is WanderTask || o.Task == null)).OrderBy(o => Vector3.Distance(o.Pos, c)).Take(6).ToList();
            for (int i = 0; i < idle.Count; i++)
            {
                float deg = -60 + 120f * (idle.Count == 1 ? 0.5f : i / (float)(idle.Count - 1));
                Vector3 spot = c + Quaternion.Euler(0, deg, 0) * back * 3.5f;
                Brain w = idle[i]; w.Task?.Stop(w); w.Task = new WatchTask(Colony.Spot(spot, 1f) ?? spot, c, Time.time + 50f); w.Task.Start(w);
                _watchers.Add(w);
            }
        }
        private void End(Brain b)
        {
            if (_them != null && _them.Task is KneelTask k) k.Release(_them);
            foreach (Brain w in _watchers) if (w != null && w.Task is WatchTask wt) wt.Release();
        }
        public override void Stop(Brain b) { End(b); base.Stop(b); }
    }

    // Kneeling at the altar until the rite is over: the game's own kneel, held (set on, not a moment's trigger), and never back to work halfway.
    internal sealed class KneelTask : Task
    {
        private readonly float _until; private readonly Vector3 _face; private bool _released;
        public KneelTask(float until, Vector3 face) { _until = until; _face = face; Doing = "kneeling at Odin's altar"; }
        public override bool Interruptible => false;
        public override void Start(Brain b) { b.Move.Stop(); b.Stand(); Kneels.Begin(b.Body, _until - Time.time); }
        public override void Tick(Brain b)
        {
            b.Move.Stop();
            Vector3 d = _face - b.Pos; d.y = 0; if (d.sqrMagnitude > 0.01f) b.Ai.LookTowards(d.normalized);
            if (Time.time > _until || _released) { Kneels.End(b.Body); Done = true; }
        }
        public void Release(Brain b) { _released = true; Kneels.End(b.Body); Done = true; }
        public override void Stop(Brain b) { Kneels.End(b.Body); base.Stop(b); }
    }

    // A held kneel. The game's kneel is a one-shot (down and straight back up), so the body is marked "kneeling" on its ZDO and every
    // game that has it loaded plays the kneel and holds the pose once down (its animator paused there), until the mark is lifted.
    internal static class Kneels
    {
        public const string Key = "jarldom_kneel";
        private static readonly Dictionary<Humanoid, float> Down = new Dictionary<Humanoid, float>(); // body -> when this game saw it kneel
        private static readonly HashSet<Character> Held = new HashSet<Character>(); // (down and paused there)
        private static float _next;
        public static void Begin(Humanoid h, float seconds)
        {
            ZNetView v = h.GetComponent<ZNetView>(); if (v == null || !v.IsValid()) return;
            v.GetZDO().Set(Key, (long)((ZNet.instance.GetTimeSeconds() + seconds) * 1000));
        }
        public static void End(Humanoid h) { ZNetView v = h != null ? h.GetComponent<ZNetView>() : null; if (v != null && v.IsValid() && v.IsOwner()) v.GetZDO().Set(Key, 0L); }
        public static bool IsDown(Character c) { ZDO z = c.GetComponent<ZNetView>()?.GetZDO(); return z != null && ZNet.instance != null && z.GetLong(Key, 0L) > ZNet.instance.GetTimeSeconds() * 1000; }
        // While kneeling a villager is in an emote, as the game sees it: so it doesn't set the animator going again each frame (a player's
        // held emote is the same), nor walk off.
        [HarmonyLib.HarmonyPatch(typeof(Character), nameof(Character.InEmote))]
        private static class InEmote { private static void Postfix(Character __instance, ref bool __result) { if (!__result && VillagerPrefab.Is(__instance) && IsDown(__instance)) __result = true; } }
        // The game sets a paused animator going again every physics step (its freeze-frame check): paused again straight after.
        [HarmonyLib.HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.CustomFixedUpdate))]
        private static class Hold { private static void Postfix(Character ___m_character, Animator ___m_animator) { if (Held.Count > 0 && ___m_character != null && Held.Contains(___m_character)) ___m_animator.speed = 0f; } }
        // On every game, a few times a second.
        public static void Tick()
        {
            if (Time.time < _next || ZNet.instance == null) return;
            _next = Time.time + 0.1f;
            double now = ZNet.instance.GetTimeSeconds() * 1000;
            foreach (Humanoid h in Villagers.Bodies())
            {
                ZDO z = h.GetComponent<ZNetView>()?.GetZDO(); if (z == null) continue;
                bool kneeling = z.GetLong(Key, 0L) > now;
                Animator an = h.GetComponentInChildren<Animator>(); if (an == null) continue;
                if (kneeling && !Down.ContainsKey(h)) { Down[h] = Time.time; an.speed = 1f; an.ResetTrigger("emote_stop"); an.SetTrigger("emote_kneel"); }
                else if (kneeling && !Held.Contains(h) && Time.time - Down[h] > 0.9f) { Held.Add(h); an.speed = 0f; } // (down on one knee: held there)
                else if (!kneeling && Down.ContainsKey(h)) { Down.Remove(h); Held.Remove(h); an.speed = 1f; an.SetTrigger("emote_stop"); } // (up again: the game's emotes wait for this to end, as a player's does when they move)
            }
            foreach (Humanoid gone in Down.Keys.Where(k => k == null).ToList()) Down.Remove(gone);
            Held.RemoveWhere(k => k == null);
        }
    }

    // Watching the rite from the ring: stood still, facing the altar, until it's over.
    internal sealed class WatchTask : Task
    {
        private readonly Vector3 _spot, _look; private readonly float _until; private bool _released;
        public WatchTask(Vector3 spot, Vector3 look, float until) { _spot = spot; _look = look; _until = until; Doing = "watching the cleansing rite"; }
        public override void Start(Brain b) => b.Move.To(_spot, 0.8f);
        public override void Tick(Brain b)
        {
            if (_released || Time.time > _until) { Done = true; return; }
            if (Utils.DistanceXZ(b.Pos, _spot) > 1.2f) { if (!b.Move.Moving && !b.Move.Failed) b.Move.To(_spot, 0.8f); return; }
            b.Move.Stop(); Vector3 d = _look - b.Pos; d.y = 0; if (d.sqrMagnitude > 0.01f) b.Ai.LookTowards(d.normalized);
        }
        public void Release() => _released = true;
    }

    internal sealed class GothiTask : Task
    {
        private readonly Room _temple; private Piece _altar; private string _god; private Container _from; private string _gift; private int _phase;
        public GothiTask(Room temple) { _temple = temple; }
        public override bool Interruptible => _phase == 0;
        public static Task Next(Brain b)
        {
            Colony c = b.Colony;
            if (c == null || !c.IsOwner) return null;
            string rite = Saboteur.NextRite(c);
            if (rite != null && Time.time > b.NoRiteUntil && Saboteur.OdinAltar(c) != null && Villagers.ByVid(rite) is Brain them && them.Alive) return new CleanseTask(them);
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
            if (gifts.Count == 0) { Skip(b, $"the stores hold nothing {Tables.God(god)?.Name} wants ({Tables.God(god)?.Offerings}{(Tables.God(god)?.OfferingItems?.Contains("Coins") == true ? "; the treasury's coins aren't the gothi's to give: put some in a stockpile chest" : "")})"); return; }
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
