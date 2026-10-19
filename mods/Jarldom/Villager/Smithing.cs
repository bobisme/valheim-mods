using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // The smith's own work, besides commissions: the tools the colony's trades lack and the stores don't hold (made the best the
    // stations and stores allow), arrows kept in stock for the hunters, better weapons and armour for the guards when the stores have
    // the metal, and everyone's worn gear mended at the station (so the rest don't each walk off to do it). Orders on the board as
    // "need:<item prefab>", made by the CraftTask as commissions are.
    internal static class Smithing
    {
        public const string Prefix = "need:";
        public const int KeepArrows = 40, MostOpen = 2;
        public const int Spare = 2; // (an upgrade only when the stores hold its materials twice over)
        public const float Better = 1.25f; // (an upgrade at least this much better, and a little more: worth a guard's trip)
        // Why each was asked for (the colony owner's game): colony id + prefab -> "a bronze axe for Hafthor, who has none".
        public static readonly Dictionary<string, string> Why = new Dictionary<string, string>();
        public static string Reason(Colony c, string prefab) => Why.TryGetValue(c.Id + "|" + prefab, out string w) ? w : "";

        // Made a little while ago (on its way to the stores, or a villager's yet to fetch it): not asked for again yet.
        private static readonly Dictionary<string, float> JustMade = new Dictionary<string, float>();
        public const float Cooldown = 240f;
        public static void Made(Colony c, string target) { if (target.StartsWith(Prefix)) JustMade[c.Id + "|" + target.Substring(Prefix.Length)] = Time.time; }
        private static bool Recent(Colony c, Recipe r) => JustMade.TryGetValue(c.Id + "|" + r.m_item.name, out float t) && Time.time - t < Cooldown;
        public static bool HasSmith(Colony c) => c.R.Living.Any(p => p.Prof == Prof.Smith);
        private static IEnumerable<Brain> Folk(Colony c) => Villagers.All.Where(b => b.S.Colony == c.Id && b.Body != null);

        // ---- the owner's scan: what the smith should make next ----
        public static void Scan(Colony c, double now)
        {
            if (!HasSmith(c)) return;
            var open = c.Board.All.Where(o => o.Kind == "craft" && o.Target.StartsWith(Prefix) && o.State != OrderState.Done && o.State != OrderState.Cancelled).ToList();
            if (open.Count >= MostOpen) return;
            foreach (var (recipe, why) in Wants(c).Where(w => !open.Any(o => o.Target == Prefix + w.recipe.m_item.name) && !Recent(c, w.recipe)).Take(MostOpen - open.Count))
            {
                CraftingStation st = Crafts.StationFor(c, recipe, c.Position);
                if (st == null) continue;
                Why[c.Id + "|" + recipe.m_item.name] = why;
                c.Board.Post("craft", Prefix + recipe.m_item.name, Prof.Smith, 3, st.transform.position.x, st.transform.position.y, st.transform.position.z, now, "", "smith");
            }
        }

        // What's wanted, most needed first: a missing tool of a trade, then arrows, then a guard's upgrade.
        public static List<(Recipe recipe, string why)> Wants(Colony c)
        {
            var wants = new List<(Recipe, string)>();
            var folk = Folk(c).ToList();
            var stores = Stock.Chests(c).Where(x => x.category != "treasury").SelectMany(x => x.chest.GetInventory().GetAllItems()).ToList();
            // Tools: those whose trade lacks its tool, less what the stores already hold for them.
            var lacking = new Dictionary<string, List<string>>();
            foreach (Brain b in folk)
            {
                string kind = Kind(b.S.Prof);
                if (kind == null || !Missing(b, kind)) continue;
                if (!lacking.TryGetValue(kind, out var who)) lacking[kind] = who = new List<string>();
                who.Add(b.S.Name.Split(' ')[0]);
            }
            foreach (var kv in lacking)
            {
                Func<ItemDrop.ItemData, bool> fits = Fits(kv.Key);
                int held = stores.Where(i => fits(i)).Sum(i => kv.Key == "bow" ? 1 : i.m_stack);
                if (held >= kv.Value.Count) continue;
                Recipe best = Best(c, fits, 1, byTier: kv.Key == "axe" || kv.Key == "pickaxe"); // (a tool by what it can cut; a weapon by how hard it hits)
                if (best != null) wants.Add((best, $"{A(best)} for {Names(kv.Value)}, who {(kv.Value.Count == 1 ? "has" : "have")} none"));
            }
            // Arrows: some in the stores while there are hunters (of the kind their bows shoot).
            if (folk.Any(b => b.S.Prof == Prof.Hunter))
            {
                Func<ItemDrop.ItemData, bool> arrow = i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo && i.m_shared.m_ammoType == "$ammo_arrows";
                if (stores.Where(arrow).Sum(i => i.m_stack) < KeepArrows)
                {
                    Recipe best = Best(c, arrow);
                    if (best != null) wants.Add((best, "arrows for the hunters"));
                }
            }
            // A guard's upgrade, the worst-armed first: a weapon or a piece of armour clearly better than theirs (and not one the stores
            // hold already, which they'll fetch themselves). One at a time.
            foreach (Brain g in folk.Where(b => b.S.Prof == Prof.Guard).OrderBy(b => Score(Gear.Weapon(b))))
            {
                var up = Upgrade(c, g, stores);
                if (up.recipe != null) { wants.Add(up); break; }
            }
            return wants;
        }
        private static (Recipe recipe, string why) Upgrade(Colony c, Brain g, List<ItemDrop.ItemData> stores)
        {
            string name = g.S.Name.Split(' ')[0];
            float have = Score(Gear.Weapon(g));
            if (have > 0 && !stores.Any(i => Gear.GuardWeapon(i) && Score(i) >= have * Better + 1)) // (no weapon at all is a missing tool, asked for above)
            {
                Recipe w = Best(c, i => Gear.GuardWeapon(i) && Score(i) >= have * Better + 1, Spare);
                if (w != null) return (w, $"{A(w)} for {name}, better than {(have > 0 ? "theirs" : "nothing")}");
            }
            foreach (var slot in new[] { ItemDrop.ItemData.ItemType.Helmet, ItemDrop.ItemData.ItemType.Chest, ItemDrop.ItemData.ItemType.Legs })
            {
                float worn = Armour(g, slot);
                if (stores.Any(i => i.m_shared.m_itemType == slot && i.GetArmor() >= worn + 2)) continue;
                Recipe a = Best(c, i => i.m_shared.m_itemType == slot && i.GetArmor() >= worn + 2, Spare);
                if (a != null) return (a, $"{A(a)} for {name}, better than what they wear");
            }
            return (null, null);
        }

        // The tool a trade fetches from the stores (or a weapon to cull with, for a herder); null when it needs none.
        public static string Kind(Prof p) => p == Prof.Herder ? "weapon" : Tables.Of(p).Tool is string t && t != "" ? t : null;
        private static bool Missing(Brain b, string kind) => kind == "weapon" ? !Gear.CanFight(b) : kind == "bow" ? Gear.Tool(b, "bow") == null : Gear.Tool(b, kind) == null;
        public static Func<ItemDrop.ItemData, bool> Fits(string kind) => kind == "weapon" ? (Func<ItemDrop.ItemData, bool>)Gear.GuardWeapon
            : kind == "bow" ? i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow && Gear.Usable(i) : i => Gear.Is(kind, i);
        // The best thing of a kind the colony can make now: a recipe for it, a station for it here, and its materials in the stores.
        // spare: how many times over the stores must hold its materials (an upgrade isn't worth the last of the iron a build wants).
        public static Recipe Best(Colony c, Func<ItemDrop.ItemData, bool> fits, int spare = 1, bool byTier = false)
        {
            if (ObjectDB.instance == null) return null;
            return SmithRecipes().Where(r => r != null && fits(Made(r)))
                .Where(r => Crafts.StationFor(c, r, c.Position) != null && Crafts.Needs(r).All(n => Crafts.InStock(c, n.item) >= n.amount * spare))
                .OrderByDescending(r => byTier ? Made(r).m_shared.m_toolTier : 0).ThenByDescending(r => Score(Made(r)) + Made(r).GetArmor()).FirstOrDefault();
        }
        // The recipes a smith makes, kept until the game's list changes (a scan asks for them several times over, of hundreds).
        private static List<Recipe> _smithRecipes; private static ObjectDB _recipesOf; private static int _recipeCount = -1;
        private static List<Recipe> SmithRecipes()
        {
            ObjectDB db = ObjectDB.instance;
            if (_smithRecipes == null || _recipesOf != db || _recipeCount != db.m_recipes.Count)
            {
                _recipesOf = db; _recipeCount = db.m_recipes.Count;
                _smithRecipes = db.m_recipes.Where(r => r != null && r.m_enabled && r.m_item != null && r.m_resources.Length > 0 && !r.m_requireOnlyOneIngredient && !r.m_noCraftOnlyUpgrade && Crafts.Maker(r) == Prof.Smith).ToList();
            }
            return _smithRecipes;
        }
        // The thing a recipe makes, as it comes off the station (the recipe's own copy has no durability and no prefab to it). Kept.
        private static readonly Dictionary<Recipe, ItemDrop.ItemData> MadeOf = new Dictionary<Recipe, ItemDrop.ItemData>();
        private static ItemDrop.ItemData Made(Recipe r)
        {
            if (MadeOf.TryGetValue(r, out var i) && i != null) return i;
            i = r.m_item.m_itemData.Clone(); i.m_dropPrefab = r.m_item.gameObject; i.m_quality = 1; i.m_durability = i.GetMaxDurability();
            return MadeOf[r] = i;
        }
        public static float Score(ItemDrop.ItemData i)
        {
            if (i == null) return 0;
            var d = i.GetDamage();
            return d.m_slash + d.m_pierce + d.m_blunt + d.m_fire + d.m_frost + d.m_poison + d.m_lightning + d.m_spirit;
        }
        public static float Armour(Brain b, ItemDrop.ItemData.ItemType slot) =>
            Gear.Items(b).Where(i => i.m_shared.m_itemType == slot && Gear.Usable(i)).Select(i => i.GetArmor()).DefaultIfEmpty(0).Max();
        private static string A(Recipe r) { string n = Crafts.Name(r.m_item.name).ToLower(); return r.m_amount > 1 ? n : ("aeiou".IndexOf(n[0]) >= 0 ? "an " : "a ") + n; }
        private static string Names(List<string> n) => n.Count == 1 ? n[0] : string.Join(", ", n.Take(n.Count - 1)) + " and " + n.Last();

        // ---- a guard trading up ----
        // Something in the stores clearly better than what a guard has: a weapon, or armour for a slot.
        public static Func<ItemDrop.ItemData, bool> GuardUpgrade(Brain b)
        {
            float have = Score(Gear.Weapon(b));
            var worn = new Dictionary<ItemDrop.ItemData.ItemType, float>();
            foreach (var slot in new[] { ItemDrop.ItemData.ItemType.Helmet, ItemDrop.ItemData.ItemType.Chest, ItemDrop.ItemData.ItemType.Legs }) worn[slot] = Armour(b, slot);
            return i => Gear.Usable(i) && (Gear.GuardWeapon(i) && have > 0 && Score(i) >= have * Better + 1 || worn.TryGetValue(i.m_shared.m_itemType, out float w) && i.GetArmor() >= w + 2);
        }
        // What the new thing replaces in the bag (to go back to the chest it came from).
        public static ItemDrop.ItemData Replaced(Brain b, ItemDrop.ItemData now) =>
            Gear.IsWeapon(now) ? Gear.Items(b).Where(i => i != now && Gear.IsWeapon(i) && Score(i) < Score(now)).OrderByDescending(Score).FirstOrDefault()
            : Gear.Items(b).Where(i => i != now && i.m_shared.m_itemType == now.m_shared.m_itemType && i.GetArmor() < now.GetArmor()).OrderByDescending(i => i.GetArmor()).FirstOrDefault();
    }

    // The smith mending the colony's worn gear: to the station that can mend the most of it, a few blows, and everyone's worn things
    // there are whole again (each tells them so if near).
    internal sealed class MendTask : Task
    {
        private CraftingStation _at; private float _next; private int _beats;
        public static Task Next(Brain b)
        {
            if (b.S.Prof != Prof.Smith || Time.time < b.NoRepairUntil) return null;
            b.NoRepairUntil = Time.time + 20;
            CraftingStation st = Station(b);
            return st != null ? new MendTask { _at = st } : null;
        }
        // Worn things in the colony (those this game runs) and the station that can mend the most of them.
        private static List<(Brain owner, ItemDrop.ItemData item)> Worn(Brain smith) =>
            Villagers.All.Where(o => o.S.Colony == smith.S.Colony && o.Body != null && o.View != null && o.View.IsValid() && o.View.IsOwner())
                .SelectMany(o => RepairTask.Worn(o).Where(i => !Fraying.Frays(i)).Select(i => (o, i))).ToList(); // (not clothes: each mends their own at the sewing table)
        private static CraftingStation Station(Brain b)
        {
            var worn = Worn(b);
            if (worn.Count == 0) return null;
            return b.Colony.PiecesIn<CraftingStation>(b.Colony.Radius).Where(st => st != null && Crafts.Unusable(st) == null)
                .Select(st => (st, n: worn.Count(w => RepairTask.Can(st, w.item)))).Where(x => x.n > 0)
                .OrderByDescending(x => x.n).ThenBy(x => Vector3.Distance(x.st.transform.position, b.Pos)).Select(x => x.st).FirstOrDefault();
        }
        public override void Start(Brain b) { Doing = "mending the colony's worn gear"; b.Move.To(_at.transform.position, 2f); }
        public override void Tick(Brain b)
        {
            if (Done) return;
            if (_at == null || b.Move.Failed) { Done = true; b.NoRepairUntil = Time.time + 60; return; }
            Stock.Walk w = Stock.WalkNear(b, _at.transform.position, 3f, 2f);
            if (w == Stock.Walk.Unreachable) { Done = true; b.NoRepairUntil = Time.time + 120; return; }
            if (w == Stock.Walk.Walking) return;
            b.Move.Stop();
            Vector3 d = _at.transform.position - b.Pos; d.y = 0; if (d.sqrMagnitude > 0.01f) b.Ai.LookTowards(d.normalized);
            if (Time.time < _next) return;
            _next = Time.time + 1.5f / Mathf.Max(0.4f, b.Productivity());
            Body_.Anim(b.Body)?.SetInt("crafting", _at.m_useAnimation);
            _at.m_craftItemEffects.Create(_at.transform.position, Quaternion.identity);
            if (++_beats < 3) return;
            Body_.Anim(b.Body)?.SetInt("crafting", 0);
            var mended = new List<string>();
            foreach (var (owner, item) in Worn(b).Where(x => RepairTask.Can(_at, x.item)))
            {
                if (!Fraying.Mend(b.Colony, item)) continue; // (clothes: thread from the stores)
                b.Body.RaiseSkill(Skills.SkillType.Crafting, 1f - item.m_durability / item.GetMaxDurability());
                item.m_durability = item.GetMaxDurability();
                owner.Body.GetInventory().m_onChanged?.Invoke(); owner.Dirty();
                mended.Add(owner == b ? "my own " + Localization.instance.Localize(item.m_shared.m_name).ToLower() : $"{owner.S.Name.Split(' ')[0]}'s {Localization.instance.Localize(item.m_shared.m_name).ToLower()}");
            }
            if (mended.Count > 0)
            {
                _at.m_repairItemDoneEffects.Create(_at.transform.position, Quaternion.identity);
                b.S.Bump("mended", mended.Count); b.Dirty();
                b.Say(mended.Count == 1 ? $"There: {mended[0]}, good as new." : mended.Count <= 3 ? $"Mended {string.Join(", ", mended)}." : $"Mended {mended.Count} things, {mended[0]} and the rest.", 5);
            }
            Done = true;
        }
    }
}
