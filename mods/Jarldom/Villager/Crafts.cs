using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // Things made for the stores: players' commissions ("three bronze axes"), made by smiths at the forge and workbench and by cooks at the
    // cauldron, the game's recipes at the game's stations, paid for from the stockpiles; and mead bases a cook brews for an empty fermenter.
    // A commission is a flag on the colony record: "craft:<item prefab>" = how many are still wanted.
    internal static class Crafts
    {
        public const string Prefix = "craft:", BrewPrefix = "brew:";
        // The way to make a thing: the game's own recipe for it first (other mods add recipes for the same thing: a recycler's, an
        // upgrade path, one asking for some mod's idol), else another.
        // (kept per item: the game's whole recipe list was gone through on every ask, a hundred a scan; made again when the list changes)
        private static readonly Dictionary<string, Recipe> Recipes = new Dictionary<string, Recipe>(); private static ObjectDB _recipesOf; private static int _recipeCount;
        public static Recipe RecipeFor(string prefab)
        {
            ObjectDB db = ObjectDB.instance;
            if (db != _recipesOf || db != null && db.m_recipes.Count != _recipeCount) { Recipes.Clear(); _recipesOf = db; _recipeCount = db?.m_recipes.Count ?? 0; }
            if (Recipes.TryGetValue(prefab, out Recipe kept)) return kept;
            return Recipes[prefab] = FindRecipe(prefab);
        }
        private static Recipe FindRecipe(string prefab) =>
            ObjectDB.instance?.m_recipes.Where(r => r != null && r.m_enabled && r.m_item != null && r.m_item.name == prefab && r.m_resources.Length > 0 && !r.m_requireOnlyOneIngredient && !r.m_noCraftOnlyUpgrade)
                .OrderBy(r => r.name == "Recipe_" + prefab ? 0 : 1).ThenBy(r => r.m_resources.Length).FirstOrDefault() ?? Weaving.RecipeFor(prefab); // (clothes: the colony's own weaving)
        public static bool AtCauldron(Recipe r) => r?.m_craftingStation != null && r.m_craftingStation.name == "piece_cauldron";
        public static Prof Maker(Recipe r) => Weaving.Is(r) ? Prof.Weaver : AtCauldron(r) ? Prof.Cook : Prof.Smith;
        // A station of the recipe's kind in the colony, at the recipe's level (null if it needs one and the colony has none).
        public static CraftingStation StationFor(Colony c, Recipe r, Vector3 near)
        {
            if (r == null || r.m_craftingStation == null) return null;
            return c.PiecesIn<CraftingStation>(c.Radius)
                .Where(st => st != null && st.m_name == r.m_craftingStation.m_name && st.GetLevel() >= r.m_minStationLevel)
                .OrderBy(st => Unusable(st) == null ? 0 : 1).ThenBy(st => Vector3.Distance(st.transform.position, near)).FirstOrDefault(); // (one that works first)
        }
        // What keeps a station from working, the game's way (a roof over it, a lit fire under a cauldron); null if it works.
        private static readonly HarmonyLib.AccessTools.FieldRef<CraftingStation, bool> HaveFire = HarmonyLib.AccessTools.FieldRefAccess<CraftingStation, bool>("m_haveFire");
        // (kept a frame for each station: asked for every station on every look for one)
        private static readonly Dictionary<CraftingStation, string> UnusableNow = new Dictionary<CraftingStation, string>(); private static int _unusableFrame = -1;
        public static string Unusable(CraftingStation st)
        {
            if (_unusableFrame != Time.frameCount) { UnusableNow.Clear(); _unusableFrame = Time.frameCount; }
            if (UnusableNow.TryGetValue(st, out string why)) return why;
            return UnusableNow[st] = UnusableFresh(st);
        }
        private static string UnusableFresh(CraftingStation st)
        {
            string name = Localization.instance.Localize(st.m_name).ToLower();
            if (st.m_craftRequireRoof && st.m_roofCheckPoint != null)
            {
                Cover.GetCoverForPoint(st.m_roofCheckPoint.position, out float cover, out bool roof);
                if (!roof) return $"the {name} has no roof over it";
                if (cover < 0.7f) return $"the {name} is too open to the weather";
            }
            if (st.m_craftRequireFire && !HaveFire(st)) return $"the {name} has no fire";
            return null;
        }
        public static List<(string item, int amount)> Needs(Recipe r) =>
            r.m_resources.Where(q => q.m_resItem != null && !q.m_upgraderResource && q.GetAmount(1) > 0).Select(q => (q.m_resItem.name, q.GetAmount(1))).ToList();
        public static int InStock(Colony c, string item) => Stock.Count(c, item);
        public static bool CanAfford(Colony c, Recipe r) => Needs(r).All(n => InStock(c, n.item) >= n.amount);
        public static string Name(string prefab) { GameObject p = Assets.Find(prefab); string n = p?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name; return n == null ? prefab : Localization.instance.Localize(n); }

        // Why each fermenter isn't being kept going (for the jarldom craft command).
        public static List<string> BrewWhy(Colony c) => c.PiecesIn<Fermenter>(c.Radius).Select(f =>
        {
            string head = $"fermenter at {f.transform.position.x:0},{f.transform.position.z:0}: {Work.FermentStatus(f)}, roofed {Work.Roofed(f.transform.position)}";
            var bases = f.m_conversion.Where(cv => cv.m_from != null).Select(cv => cv.m_from.name).ToList();
            string stock = string.Join(", ", bases.Where(n => InStock(c, n) > 0).Select(n => n + " x" + InStock(c, n)));
            string recipes = string.Join("; ", bases.Select(n => { Recipe r = RecipeFor(n); if (r == null) return n + ": no recipe"; CraftingStation st = StationFor(c, r, f.transform.position);
                return $"{n}: {(st == null ? "no " + r.m_craftingStation?.name : Unusable(st) ?? "station ok")}, {(CanAfford(c, r) ? "affordable" : "lacks " + string.Join("/", Needs(r).Where(q => InStock(c, q.item) < q.amount).Select(q => q.item)))}"; }));
            return head + " | in stock: " + (stock == "" ? "none" : stock) + " | " + recipes;
        }).ToList();
        public static Dictionary<string, int> Commissions(Colony c) =>
            c.R.Flags.Where(f => f.Key.StartsWith(Prefix) && f.Value > 0).ToDictionary(f => f.Key.Substring(Prefix.Length), f => f.Value);
        // What the local player could commission here: recipes they know, for stations the colony has.
        public static List<Recipe> Offered(Colony c, Player p)
        {
            if (ObjectDB.instance == null || p == null) return new List<Recipe>();
            var stations = c.PiecesIn<CraftingStation>(c.Radius);
            return ObjectDB.instance.m_recipes.Where(r => r != null && r.m_enabled && r.m_item != null && r.m_craftingStation != null && r.m_resources.Length > 0 && !r.m_requireOnlyOneIngredient && !r.m_noCraftOnlyUpgrade
                    && p.IsRecipeKnown(r.m_item.m_itemData.m_shared.m_name)
                    && stations.Any(s => s.m_name == r.m_craftingStation.m_name && s.GetLevel() >= r.m_minStationLevel))
                .Concat(stations.Any(s => Utils.GetPrefabName(s.gameObject) == "piece_workbench") ? Weaving.All() : Enumerable.Empty<Recipe>()) // (a weaver's clothes, wherever there's a workbench)
                .GroupBy(r => r.m_item.name).Select(g => g.First())
                .OrderBy(r => r.m_craftingStation.m_name).ThenBy(r => Localization.instance.Localize(r.m_item.m_itemData.m_shared.m_name)).ToList();
        }

        // ---- the owner's scan ----
        public static void Scan(Colony c, double now)
        {
            foreach (var kv in Commissions(c))
            {
                Recipe r = RecipeFor(kv.Key);
                CraftingStation st = StationFor(c, r, c.Position);
                if (r == null || st == null) continue; // (shown in the window as waiting for a station)
                Vector3 p = st.transform.position;
                c.Board.Post("craft", Prefix + kv.Key, Maker(r), 3, p.x, p.y, p.z, now, "", "commission");
            }
            // An empty fermenter and no mead base in the stores: a cook brews one at the cauldron, if the stores have what it takes.
            foreach (Fermenter f in c.PiecesIn<Fermenter>(c.Radius))
            {
                if (Work.FermentStatus(f) != "Empty" || !Work.Roofed(f.transform.position)) continue;
                if (f.m_conversion.Any(cv => cv.m_from != null && InStock(c, cv.m_from.name) > 0)) continue;
                Recipe brew = f.m_conversion.Where(cv => cv.m_from != null).Select(cv => RecipeFor(cv.m_from.name)).FirstOrDefault(r => r != null && StationFor(c, r, f.transform.position) != null && CanAfford(c, r));
                if (brew == null) continue;
                CraftingStation st = StationFor(c, brew, f.transform.position);
                c.Board.Post("craft", BrewPrefix + brew.m_item.name, Prof.Cook, 3, st.transform.position.x, st.transform.position.y, st.transform.position.z, now, "", "fermenter");
                break; // one at a time
            }
        }
        // On the owner, when one is made.
        public static void Made(Colony c, string target, string who)
        {
            if (!target.StartsWith(Prefix)) return;
            string key = target; int left = c.R.Flag(key) - 1;
            if (left > 0) c.R.Flags[key] = left; else c.R.Flags.Remove(key);
            if (left <= 0) c.Log("text", who, "", $"{who} finished the commission: {Name(target.Substring(Prefix.Length)).ToLower()}.");
            c.MarkDirty();
        }
    }

    // Making one thing: the recipe's exact materials from the stores, the station's own work, the thing to the stores. Nothing is taken
    // from the bag until it's made.
    internal sealed class CraftTask : Task
    {
        private readonly Order O; private Recipe _r; private CraftingStation _st; private int _phase, _beats; private float _next;
        private List<(string item, int amount)> _needs; private ItemDrop.ItemData _made; private Container _to;
        public CraftTask(Order o) { O = o; }
        public override bool Interruptible => _phase == 0;
        private int Have(Brain b, string item) => b.Body.GetInventory().GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == item && !i.m_equipped).Sum(i => i.m_stack);
        public override void Start(Brain b)
        {
            string prefab = O.Target.Substring(O.Target.IndexOf(':') + 1);
            _r = Crafts.RecipeFor(prefab);
            _st = Crafts.StationFor(b.Colony, _r, b.Pos);
            if (_r == null || _st == null) { Release(b); b.NoCraftUntil = Time.time + 60; return; }
            string why = Crafts.Unusable(_st);
            if (why != null) { Done = true; b.NoCraftUntil = Time.time + 30; if (b.Colony.IsOwner) b.Colony.Board.Block(O.Id, why); b.Idle(why); return; }
            _needs = Crafts.Needs(_r);
            Doing = "making " + Crafts.Name(prefab).ToLower();
        }
        private void Release(Brain b) { Done = true; if (b.Colony?.IsOwner == true) b.Colony.Board.Release(O.Id, b.S.Vid); }
        public override void Tick(Brain b)
        {
            if (Done) return;
            if (_st == null) { Release(b); return; }
            if (_phase == 0)
            {
                var missing = _needs.Where(n => Have(b, n.item) < n.amount).ToList();
                if (missing.Count == 0) { _phase = 1; b.Move.To(_st.transform.position, 2f); return; }
                var chests = Stock.ChestsFor(b).Where(x => x.category != "treasury" && !ContainerAccess.InUse(x.chest)).ToList();
                foreach (var (item, amount) in missing)
                    if (chests.Sum(x => x.chest.GetInventory().GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == item).Sum(i => i.m_stack)) + Have(b, item) < amount)
                    {
                        if (b.Colony.IsOwner) b.Colony.Board.Block(O.Id, "the stores lack " + Crafts.Name(item).ToLower());
                        Done = true; b.NoCraftUntil = Time.time + 30; return;
                    }
                var (want, count) = missing[0];
                Container from = chests.Select(x => x.chest).FirstOrDefault(ch => ch.GetInventory().GetAllItems().Any(i => i.m_dropPrefab != null && i.m_dropPrefab.name == want));
                if (from == null) { Release(b); b.NoCraftUntil = Time.time + 20; return; }
                if (Stock.WalkTo(b, from) != Stock.Walk.Reached) return; // (one out of reach is passed over: another is chosen next time round)
                ItemDrop.ItemData stack = from.GetInventory().GetAllItems().First(i => i.m_dropPrefab != null && i.m_dropPrefab.name == want);
                if (Stock.Take(from, b.Body.GetInventory(), stack, count - Have(b, want)) == 0) { Release(b); b.NoCraftUntil = Time.time + 30; }
                return;
            }
            if (_phase == 1)
            {
                Stock.Walk sw = Stock.WalkNear(b, _st.transform.position, 3f, 2f);
                if (sw != Stock.Walk.Reached) { if (sw == Stock.Walk.Unreachable) { Release(b); b.NoCraftUntil = Time.time + 30; } return; }
                b.Move.Stop();
                Vector3 d = _st.transform.position - b.Pos; d.y = 0; if (d.sqrMagnitude > 0.01f) b.Ai.LookTowards(d.normalized);
                if (Time.time < _next) return;
                _next = Time.time + 1.5f / Mathf.Max(0.4f, b.Productivity());
                Body_.Anim(b.Body)?.SetInt("crafting", _st.m_useAnimation);
                _st.m_craftItemEffects.Create(_st.transform.position, Quaternion.identity);
                if (Crafts.Unusable(_st) is string stop) { Release(b); b.Idle(stop); b.NoCraftUntil = Time.time + 30; return; }
                ItemDrop.ItemData product = _r.m_item.m_itemData.Clone(); product.m_stack = _r.m_amount;
                if (!b.Body.GetInventory().CanAddItem(product, _r.m_amount)) { Release(b); b.Idle("no room in the bag for what I'd make"); b.NoCraftUntil = Time.time + 30; b.NoStoreUntil = 0; return; } // (before anything is used up)
                if (++_beats < 3) return;
                Body_.Anim(b.Body)?.SetInt("crafting", 0);
                if (_needs.Any(n => Have(b, n.item) < n.amount)) { Release(b); return; }
                Inventory bag = b.Body.GetInventory();
                foreach (var (item, amount) in _needs)
                    for (int left = amount; left > 0;)
                    {
                        ItemDrop.ItemData st = bag.GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == item && !i.m_equipped);
                        if (st == null) break;
                        int n = Math.Min(left, st.m_stack); bag.RemoveItem(st, n); left -= n;
                    }
                _made = bag.AddItem(_r.m_item.name, _r.m_amount, 1, 0, Colony.Hash(b.S.Vid), b.S.Name, new Vector2i(-1, -1), false, false, false) ?? bag.GetAllItems().LastOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == _r.m_item.name);
                if (_made != null) b.Hauled.Add(_r.m_item.name); // (the colony's, not the smith's: to the stores even if this delivery fails)
                _st.m_craftItemDoneEffects.Create(_st.transform.position, Quaternion.identity);
                b.Body.RaiseSkill(Skills.SkillType.Crafting, 1f);
                b.S.Bump("made");
                if (b.Colony.IsOwner) { if (b.Colony.Board.Complete(O.Id, b.S.Vid)) { b.S.Bump("orders"); Crafts.Made(b.Colony, O.Target, b.S.Name); } }
                b.Dirty();
                if (_made == null) { Done = true; return; } // (a full bag: it dropped at their feet, and the haulers will see it)
                if (_r.m_item.name == Weaving.Thread && b.S.Prof == Prof.Weaver) { b.Hauled.Remove(_r.m_item.name); Done = true; return; } // (spun thread stays with the weaver, for the loom)
                _to = Stock.Destination(b.Colony, _made, b.Pos) ?? Stock.ChestsFor(b).Where(x => x.category != "treasury" && x.chest.GetInventory().CanAddItem(_made)).Select(x => x.chest).FirstOrDefault();
                Doing = "taking " + Localization.instance.Localize(_made.m_shared.m_name).ToLower() + " to the stores";
                _phase = 2;
                if (_to == null) { Done = true; return; }
                b.Move.To(_to.transform.position, 1.8f);
                return;
            }
            // Phase 2: to the stores.
            if (_to == null || _made == null || b.Move.Failed) { Done = true; return; }
            if (Stock.OutOfReach(b.Pos, _to.transform.position, 2.5f)) { if (!b.Move.Moving) b.Move.To(_to.transform.position, 1.8f); return; }
            if (b.Body.GetInventory().ContainsItem(_made)) Stock.Put(b.Body.GetInventory(), _to, _made);
            Done = true;
        }
        public override void Stop(Brain b)
        {
            if (!Done && _phase < 2) Release(b);
            Body_.Anim(b.Body)?.SetInt("crafting", 0);
            base.Stop(b);
        }
    }

    // The healer goes to the injured and tends them: health back, and the injury mends faster (twice as fast in a healer's house).
    internal sealed class HealTask : Task
    {
        private readonly Brain _patient; private float _next, _until;
        public static readonly HashSet<Brain> Tended = new HashSet<Brain>();
        public HealTask(Brain patient) { _patient = patient; }
        public static bool Needs(Brain p) => p.Alive && (p.S.InjuredUntil > p.Now || p.Body.GetHealthPercentage() < 0.6f);
        public override void Start(Brain b)
        {
            Doing = "tending " + _patient.S.Name;
            Tended.Add(_patient);
            _until = Time.time + 120;
            b.Move.To(_patient.Pos, 1.5f);
        }
        public override void Tick(Brain b)
        {
            if (!Needs(_patient) || Time.time > _until) { Done = true; return; }
            if (Vector3.Distance(b.Pos, _patient.Pos) > 2.2f)
            {
                if (b.Move.Failed) { Done = true; b.NoHealUntil = Time.time + 30; return; }
                if (!b.Move.Moving || Vector3.Distance(b.Move.Goal, _patient.Pos) > 2f) b.Move.To(_patient.Pos, 1.5f);
                return;
            }
            b.Move.Stop();
            Vector3 d = _patient.Pos - b.Pos; d.y = 0; if (d.sqrMagnitude > 0.01f) b.Ai.LookTowards(d.normalized);
            if (Time.time < _next) return;
            _next = Time.time + 3f / Mathf.Max(0.4f, b.Productivity());
            Body_.Anim(b.Body)?.SetTrigger("interact");
            bool house = b.Colony.RoomAt(_patient.Pos, RoomType.HealersHouse) != null;
            _patient.Body.Heal(house ? 8 : 5, true);
            if (_patient.S.InjuredUntil > _patient.Now) _patient.S.InjuredUntil -= Clock.Hour * (house ? 1.0 : 0.5); // a day's injury mends in hours
            Mood.Satisfy(_patient.S, Need.Safety, 3);
            _patient.Dirty();
            b.S.Bump("tended");
            if (b.S.Count("tended") % 50 == 0) Worth.Gain(b.S, "orders100");
        }
        public override void Stop(Brain b) { Tended.Remove(_patient); base.Stop(b); }
        // The healer's day: the worst hurt first; otherwise minding the healer's house.
        public static Task Next(Brain b)
        {
            if (Time.time < b.NoHealUntil) return null;
            Brain p = Villagers.All.Where(o => o != b && o.S.Colony == b.S.Colony && Needs(o) && !Tended.Contains(o) && Vector3.Distance(o.Pos, b.Pos) < b.Colony.Radius + 20)
                .OrderBy(o => o.Body.GetHealthPercentage()).FirstOrDefault();
            if (p != null) return new HealTask(p);
            Room house = b.Colony.Best(RoomType.HealersHouse);
            return house != null ? new WanderTask(b.Colony.Centre(house), 2.5f, "minding the healer's house") : null;
        }
    }

    // A scout's round: out past the colony's edge and back, a different way each time; the map fills in for everyone at the colony, and
    // what's out there is reported.
    internal sealed class ScoutTask : Task
    {
        private Vector3 _out, _far; private bool _back; private float _until, _nextLook; private readonly HashSet<string> _seen = new HashSet<string>();
        public override bool Interruptible => true;
        public override void Start(Brain b)
        {
            Colony c = b.Colony;
            float angle = (b.S.Count("rounds") * 137.5f) % 360f; // the golden angle: rounds spread all around
            Vector3 dir = Quaternion.Euler(0, angle, 0) * Vector3.forward;
            // (out to a lookout 55 m off at most, and the land beyond it to the edge and 40 m past is what they report: the world past a
            // player's own ground stands still, so a long run would stall out there)
            _far = c.Position + dir * (c.Radius + 40);
            Vector3 look = c.Position + dir * Mathf.Min(c.Radius + 40, 55f);
            _out = Colony.Spot(look, 10f) ?? look;
            Doing = "scouting to the " + Compass(dir);
            _until = Time.time + 180;
            b.Move.To(_out, 4f, run: true);
        }
        public override void Tick(Brain b)
        {
            if (Time.time > _until) { Finish(b); return; }
            if (Time.time >= _nextLook) { _nextLook = Time.time + 2f; Look(b); }
            if (!_back)
            {
                if (b.Move.Failed || b.Move.Arrived || !b.Move.Moving && b.Move.Distance < 8f) { _back = true; Net.Explore(b.Pos, 80f); Net.Explore(_far, 80f); b.Move.To(b.Colony.Position + (b.Pos - b.Colony.Position).normalized * 6f, 4f, run: true); Doing = "coming back from scouting"; }
                else if (!b.Move.Moving) b.Move.To(_out, 4f, run: true);
                return;
            }
            if (b.Move.Arrived || b.Move.Failed || Utils.DistanceXZ(b.Pos, b.Colony.Position) < 12f) Finish(b);
            else if (!b.Move.Moving) b.Move.To(b.Colony.Position, 6f, run: true);
        }
        private void Finish(Brain b) { Done = true; b.S.Bump("rounds"); b.NoScoutUntil = Time.time + 90; b.Dirty(); }
        private void Look(Brain b)
        {
            Net.Explore(b.Pos, 50f);
            var foes = Character.GetAllCharacters().Where(c => c != null && !c.IsDead() && !c.IsPlayer() && !VillagerPrefab.Is(c) && !c.IsTamed() && !Tribute.IsHerald(c) && !Visitors.IsMerc(c)
                && c.GetFaction() != Character.Faction.AnimalsVeg && c.GetFaction() != Character.Faction.Players && Vector3.Distance(c.transform.position, b.Pos) < 45f).ToList();
            if (foes.Count == 0) return;
            var groups = foes.GroupBy(f => Localization.instance.Localize(f.m_name)).OrderByDescending(g => g.Count()).ToList();
            string what = string.Join(", ", groups.Take(3).Select(g => g.Count() == 1 ? "a " + g.Key.ToLower() : g.Count() + " " + Plural(g.Key.ToLower())));
            string key = b.Colony.Id + what;
            if (!_seen.Add(key) || Reported.TryGetValue(key, out float last) && Time.time - last < 300f) return; // (each sighting once in five minutes)
            Reported[key] = Time.time;
            if (Reported.Count > 300) foreach (string old in Reported.Where(kv => Time.time - kv.Value > 300).Select(kv => kv.Key).ToList()) Reported.Remove(old);
            Vector3 dir = foes[0].transform.position - b.Colony.Position;
            string text = $"{b.S.Name} the scout saw {what} to the {Compass(dir)}, {Mathf.RoundToInt(Utils.DistanceXZ(foes[0].transform.position, b.Colony.Position))} m out.";
            b.Say("I see " + what + "!", 5);
            if (Time.time >= _nextWarn) { _nextWarn = Time.time + 60; b.Colony.Log("text", b.S.Name, "", text); }
        }
        private static float _nextWarn;
        private static readonly Dictionary<string, float> Reported = new Dictionary<string, float>();
        private static string Plural(string n) => n.EndsWith("s") ? n : n.EndsWith("f") ? n.Substring(0, n.Length - 1) + "ves" : n + "s";
        public static string Compass(Vector3 d)
        {
            float a = (Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + 360f) % 360f;
            string[] names = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
            return names[Mathf.RoundToInt(a / 45f) % 8];
        }
    }
}
