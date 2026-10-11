using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jarldom
{
    // Every villager this game knows, the brains of those it owns, and what happens to them as bodies: being made, dying, rising, hover.
    // A villager's state is a VillagerState on its ZDO ("jarldom_v"), its id "jarldom_vid", its colony "jarldom_colony", its name for
    // everyone's hover "jarldom_name" and what it's doing "jarldom_doing".
    internal static class Villagers
    {
        public const string StateKey = "jarldom_v", VidKey = "jarldom_vid", ColonyKey = "jarldom_colony", NameKey = "jarldom_name", DoingKey = "jarldom_doing", MoodKey = "jarldom_mood";
        public const string TombKey = "jarldom_tombof", ClaimKey = "jarldom_owner";

        // A change to one villager, carried out by the game that owns it.
        public static void Command(string vid, string action, string arg)
        {
            Brain b = ByVid(vid);
            if (b != null) { Plugin.Guard(() => b.Command(action, arg)); return; }
            Humanoid h = Body(vid);
            if (h == null) return;
            if (h.GetComponent<ZNetView>().IsOwner()) { Brain mine = BrainFor(h); if (mine != null) Plugin.Guard(() => mine.Command(action, arg)); return; }
            Net.Villager(h.GetComponent<ZNetView>().GetZDO().GetOwner(), vid, action, arg);
        }
        private static readonly Dictionary<Character, Brain> Brains = new Dictionary<Character, Brain>();
        private static readonly List<Brain> Order = new List<Brain>();
        private static int _turn;

        public static IEnumerable<Brain> All => Order.Where(b => b.Alive);
        public static Brain Brain(Character c) => c != null && Brains.TryGetValue(c, out Brain b) ? b : null;
        public static Brain ByVid(string vid) => Order.FirstOrDefault(b => b.Alive && b.S != null && b.S.Vid == vid);

        // Villager bodies loaded here (owned or not), found through the game's character list.
        public static IEnumerable<Humanoid> Bodies()
        {
            foreach (Character c in Character.GetAllCharacters())
                if (c is Humanoid h && VillagerPrefab.Is(c) && !c.IsDead() && c.GetComponent<ZNetView>() is ZNetView v && v.IsValid()) yield return h; // (not one destroyed this frame)
        }
        public static Humanoid Body(string vid) => Bodies().FirstOrDefault(h => h.GetComponent<ZNetView>().GetZDO().GetString(VidKey) == vid);

        public static void AttachAll() { }  // brains come lazily, from the first think of each body this game owns

        // From MonsterAI.UpdateAI (the owner's FixedUpdate). Movement every call; decisions a few times a second, spread over frames.
        public static bool Think(MonsterAI ai, float dt)
        {
            Humanoid h = ai.GetComponent<Humanoid>();
            if (h == null) return false;
            ZNetView view = h.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || !view.IsOwner())
            {
                // Only the owner thinks (the game's own UpdateAI checks this too); a brain left from before ownership moved lets go.
                if (Brains.TryGetValue(h, out Brain gone)) { Plugin.Guard(gone.Stand); Brains.Remove(h); Order.Remove(gone); }
                return false;
            }
            var watch = System.Diagnostics.Stopwatch.StartNew();
            Brain b = BrainFor(h, ai);
            if (b == null) return false;
            b.Fixed(dt);
            Plugin.ThinkMs += watch.Elapsed.TotalMilliseconds;
            return true;
        }
        // The brain of a body this game owns, made (and its state read) the first time it's needed.
        public static Brain BrainFor(Humanoid h, MonsterAI ai = null)
        {
            if (Brains.TryGetValue(h, out Brain b) && b.Alive) return b;
            ZNetView view = h.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || !view.IsOwner()) return null;
            b = new Brain(h, ai ?? h.GetComponent<MonsterAI>());
            if (!b.Load()) return null;
            if (Brains.TryGetValue(h, out Brain old)) Order.Remove(old);
            Brains[h] = b; Order.Add(b);
            return b;
        }

        public static void Tick()
        {
            Order.RemoveAll(b => !b.Alive);
            foreach (var dead in Brains.Where(kv => kv.Key == null || !kv.Value.Alive).Select(kv => kv.Key).ToList()) Brains.Remove(dead);
            int n = Order.Count; if (n == 0) return;
            int per = Math.Min(n, Plugin.Instance.MaxBrainsPerFrame.Value);
            for (int i = 0; i < per; i++)
            {
                _turn = (_turn + 1) % n;
                Brain b = Order[_turn];
                Plugin.Guard(b.Decide);
            }
            Talk.Tick();
        }

        public static void DetachAll()
        {
            foreach (Brain b in Order) Plugin.Guard(b.Release);
            Brains.Clear(); Order.Clear();
            Jarldom.Body.Forget();
        }
        public static void ForgetWorld() { DetachAll(); VillagerPrefab.ForgetKnown(); }

        // ---- making ----
        public static Humanoid Spawn(Colony colony, VillagerState s, Vector3 at, byte[] gear = null, bool dressNew = false)
        {
            GameObject prefab = VillagerPrefab.Prefab;
            if (prefab == null || colony == null) return null;
            GameObject go = Object.Instantiate(prefab, at, Quaternion.Euler(0, UnityEngine.Random.Range(0, 360f), 0));
            ZNetView view = go.GetComponent<ZNetView>();
            ZDO z = view.GetZDO();
            s.Colony = colony.Id;
            z.Set(VidKey, s.Vid); z.Set(ColonyKey, colony.Id); z.Set(NameKey, s.FullName); SoulStore.Write(z, s, colony);
            Looks(go, s);
            Humanoid h = go.GetComponent<Humanoid>();
            h.m_name = s.FullName;
            Inventory bag = h.GetInventory();
            if (gear != null && gear.Length > 0 && bag != null)
            {
                try { var kept = new Inventory("kept", null, VillagerPrefab.BagWidth, VillagerPrefab.BagRows); kept.Load(new ZPackage(gear)); bag.MoveAll(kept); }
                catch (Exception e) { Plugin.Warn("Jarldom: could not give back " + s.Name + "'s gear: " + e.Message); }
            }
            if (dressNew && bag != null)
                foreach (string item in new[] { "ArmorRagsChest", "ArmorRagsLegs" })
                {
                    GameObject p = Assets.Find(item);
                    if (p != null) bag.AddItem(p, 1);
                }
            return h;
        }

        // Looks from the soul's seed: body, hair, beard, skin and hair colours from the character creator's ranges.
        public static void Looks(GameObject go, VillagerState s)
        {
            VisEquipment vis = go.GetComponent<VisEquipment>();
            ZDO z = go.GetComponent<ZNetView>().GetZDO();
            var rng = new System.Random(s.Vid.GetStableHashCode());
            var hairs = Styles("Hair"); var beards = Styles("Beard");
            int model = s.Female ? 1 : 0;
            z.Set(ZDOVars.s_modelIndex, model);
            int hair = hairs.Count > 0 && rng.Next(10) > 0 ? hairs[rng.Next(hairs.Count)].GetStableHashCode() : 0;
            int beard = !s.Female && beards.Count > 0 && rng.Next(6) > 0 ? beards[rng.Next(beards.Count)].GetStableHashCode() : 0;
            z.Set(ZDOVars.s_hairItem, hair); z.Set(ZDOVars.s_noHair, hair == 0);
            z.Set(ZDOVars.s_beardItem, beard); z.Set(ZDOVars.s_noBeard, beard == 0);
            float skinT = (float)rng.NextDouble() * 0.8f, hairT = (float)rng.NextDouble(), shade = 0.25f + (float)rng.NextDouble() * 0.75f;
            Vector3 skin = Utils.ColorToVec3(Color.Lerp(Color.white, new Color(0.32f, 0.23f, 0.17f), skinT));
            Vector3 hc = Utils.ColorToVec3(Color.Lerp(new Color(1f, 0.86f, 0.55f), new Color(0.45f, 0.24f, 0.11f), hairT) * Mathf.Lerp(0.1f, 1f, shade));
            if (rng.Next(12) == 0) hc = Utils.ColorToVec3(new Color(0.75f, 0.73f, 0.7f)); // grey
            if (vis != null)
            {
                vis.SetModel(model); vis.SetHairItem(hair); vis.SetBeardItem(beard);
                vis.SetSkinColor(skin); vis.SetHairColor(hc);
            }
            z.Set(ZDOVars.s_skinColor, skin); z.Set(ZDOVars.s_hairColor, hc);
        }
        private static List<string> Styles(string prefix)
        {
            if (ObjectDB.instance == null) return new List<string>();
            return ObjectDB.instance.GetAllItems(ItemDrop.ItemData.ItemType.Customization, prefix).Where(i => i != null && !i.name.EndsWith("None")).Select(i => i.name).OrderBy(n => n).ToList();
        }

        // ---- dying and rising ----
        public static void Died(Humanoid h)
        {
            Brain b = Brain(h);
            ZDO z = h.GetComponent<ZNetView>().GetZDO();
            VillagerState s = b?.S ?? VillagerState.Load(SoulStore.StateOf(z));
            if (s == null) return;
            Colony colony = Colony.Of(s.Colony);
            string killer = b?.LastAttacker ?? "";
            s.Deaths++;
            foreach (string k in s.Skills.Keys.ToList()) s.Skills[k] *= 0.95f;
            bool lost = string.IsNullOrEmpty(s.Bed) || s.Disgraced;
            if (lost && colony != null && colony.HelSaves()) { lost = false; colony.Log("text", s.Name, "", $"Hel let {s.Name} go."); }
            Inventory bag = h.GetInventory();
            // Worn gear stays on a soul that will rise; the rest (and everything, for one lost to Hel) goes to a tombstone, filled the game's way.
            byte[] gear = new byte[0];
            if (bag != null)
            {
                var worn = bag.GetEquippedItems().ToList();
                foreach (ItemDrop.ItemData item in worn) { h.UnequipItem(item, false); item.m_equipped = false; }
                if (!lost)
                {
                    var kept = new Inventory("kept", null, VillagerPrefab.BagWidth, VillagerPrefab.BagRows);
                    foreach (ItemDrop.ItemData item in worn) { if (kept.AddItem(item.Clone())) bag.RemoveItem(item); }
                    var pkg = new ZPackage(); kept.Save(pkg); gear = pkg.GetArray();
                }
                if (bag.NrOfItems() > 0) Tomb(h, bag, s);
            }
            if (b != null) b.Release();
            Brains.Remove(h);
            if (colony == null) { Net.SendFallenAnywhere(s, gear, h.transform.position, killer, lost); return; }
            colony.SoulFell(s, gear, h.transform.position, killer, lost);
        }
        private static void Tomb(Humanoid h, Inventory bag, VillagerState s)
        {
            GameObject tombPrefab = ZNetScene.instance.GetPrefab("Player")?.GetComponent<Player>()?.m_tombstone ?? ZNetScene.instance.GetPrefab("CargoCrate");
            if (tombPrefab == null) return;
            GameObject tomb = Object.Instantiate(tombPrefab, h.GetCenterPoint(), h.transform.rotation);
            Container grave = tomb.GetComponent<Container>();
            if (grave != null) grave.GetInventory().MoveInventoryToGrave(bag);
            tomb.GetComponent<TombStone>()?.Setup(s.FullName, 0L);
            ZDO tz = tomb.GetComponent<ZNetView>()?.GetZDO();
            tz?.Set(TombKey, s.Vid);
            // Anything that didn't fit lands on the ground, never lost.
            foreach (ItemDrop.ItemData item in bag.GetAllItems().ToList()) { ItemDrop.DropItem(item, item.m_stack, h.GetCenterPoint() + UnityEngine.Random.insideUnitSphere * 0.5f, Quaternion.identity); bag.RemoveItem(item); }
        }

        // ---- talking to players ----
        public static string DisplayName(Character c)
        {
            ZDO z = c?.GetComponent<ZNetView>()?.GetZDO();
            return z?.GetString(NameKey, "Villager") ?? "Villager";
        }
        public static string Hover(Character c)
        {
            ZDO z = c?.GetComponent<ZNetView>()?.GetZDO();
            if (z == null) return "";
            string doing = z.GetString(DoingKey, "");
            int mood = z.GetInt(MoodKey, 0);
            string moodWord = mood >= 50 ? "<color=#99e08c>content</color>" : mood >= 10 ? "fine" : mood > -30 ? "<color=#ffd27a>unhappy</color>" : "<color=#ff8c59>miserable</color>";
            return Localization.instance.Localize($"{DisplayName(c)}\n<size=80%>{doing} ({moodWord})</size>\n[<color=yellow><b>$KEY_Use</b></color>] Talk   [<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] Bag");
        }
        public static bool Feed(Humanoid h, Humanoid giver, ItemDrop.ItemData item)
        {
            if (h == null || giver == null || item == null) return false;
            if (item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Consumable) { giver.Message(MessageHud.MessageType.Center, "Only food and potions can be handed over like that."); return true; }
            Inventory bag = h.GetInventory();
            ItemDrop.ItemData one = item.Clone(); one.m_stack = 1;
            if (bag == null || !bag.CanAddItem(one, 1)) { giver.Message(MessageHud.MessageType.Center, "Their bag is full."); return true; }
            if (!h.GetComponent<ZNetView>().IsOwner()) { giver.Message(MessageHud.MessageType.Center, "Open their bag to give them things (they belong to another player's game right now)."); return true; }
            giver.GetInventory().RemoveOneItem(item);
            bag.AddItem(one);
            giver.Message(MessageHud.MessageType.TopLeft, $"Gave {DisplayName(h)} {Localization.instance.Localize(item.m_shared.m_name)}");
            Brain(h)?.Thanked(giver as Player);
            return true;
        }
        public static void Hurt(Character c, HitData hit)
        {
            Brain b = Brain(c);
            if (b == null) return;
            Character a = hit.GetAttacker();
            if (a != null) b.Attacked(a);
        }
    }
}
