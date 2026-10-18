using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Jarldom
{
    // Clothes. Everyone is reborn in rags; a colony dresses itself the Norse way: flax grown in its fields (the colony's farmers know it,
    // whatever the biome), spun into linen thread (at a spinning wheel, or by hand with a spindle), woven into tunics and dresses at
    // the workbench by a weaver and dyed with what the land gives (blueberries for blue as woad gave, raspberries for red as madder,
    // dandelions for yellow as weld, bark for brown, nettle-green from thistle), with a shawl or cape, or amber beads. Caps from hides.
    // The clothes are the game's own (Hildir's tunics, dresses and hats), so they look as they should. Once Hildir is known, the
    // trader also spends some of a run's coins at her stall for those still in rags. Recipes here are the colony's own: they're
    // made only by villagers and never added to the game's crafting, so a player's crafting stays as it was.
    internal static class Weaving
    {
        public const string Prefix = "weave:", Thread = "LinenThread", Flax = "Flax", FlaxSapling = "sapling_flax";
        private static readonly (string item, (string res, int n)[] needs)[] Table =
        {
            ("ArmorTunic10", new[] { (Thread, 4) }), ("ArmorDress10", new[] { (Thread, 4) }),
            ("ArmorTunic1", new[] { (Thread, 4), ("Blueberries", 5) }), ("ArmorTunic4", new[] { (Thread, 4), ("Raspberry", 5) }), ("ArmorTunic7", new[] { (Thread, 4), ("Dandelion", 5) }),
            ("ArmorDress1", new[] { (Thread, 4), ("Wood", 4) }), ("ArmorDress4", new[] { (Thread, 4), ("Blueberries", 5) }), ("ArmorDress7", new[] { (Thread, 4), ("Dandelion", 5) }),
            ("ArmorTunic2", new[] { (Thread, 6), ("Blueberries", 5), ("LeatherScraps", 2) }), ("ArmorTunic5", new[] { (Thread, 6), ("Raspberry", 5), ("LeatherScraps", 2) }), ("ArmorTunic8", new[] { (Thread, 6), ("Dandelion", 5), ("LeatherScraps", 2) }),
            ("ArmorDress2", new[] { (Thread, 7), ("Wood", 4) }), ("ArmorDress5", new[] { (Thread, 7), ("Blueberries", 5) }), ("ArmorDress8", new[] { (Thread, 7), ("Dandelion", 5) }),
            ("ArmorTunic3", new[] { (Thread, 4), ("Blueberries", 5), ("Amber", 1) }), ("ArmorTunic6", new[] { (Thread, 4), ("Raspberry", 5), ("Amber", 1) }), ("ArmorTunic9", new[] { (Thread, 4), ("Dandelion", 5), ("Amber", 1) }),
            ("ArmorDress3", new[] { (Thread, 4), ("Wood", 4), ("Amber", 1) }), ("ArmorDress6", new[] { (Thread, 4), ("Blueberries", 5), ("Amber", 1) }), ("ArmorDress9", new[] { (Thread, 4), ("Dandelion", 5), ("Amber", 1) }),
            ("HelmetHat3", new[] { ("DeerHide", 2) }), ("HelmetHat8", new[] { ("WolfPelt", 1) }),
            ("HelmetHat1", new[] { (Thread, 2), ("Blueberries", 3) }), ("HelmetHat2", new[] { (Thread, 2), ("Thistle", 3) }), ("HelmetHat6", new[] { (Thread, 2), ("Dandelion", 3) }), ("HelmetHat7", new[] { (Thread, 2), ("Raspberry", 3) }),
            ("HelmetHat5", new[] { (Thread, 2), ("Raspberry", 3), ("LeatherScraps", 1) }),
            (Thread, new[] { (Flax, 2) }), // (by hand, with a spindle, where there's no spinning wheel)
        };
        private static readonly Dictionary<string, Recipe> Recipes = new Dictionary<string, Recipe>();
        public static void Forget() { foreach (Recipe r in Recipes.Values) if (r != null) UnityEngine.Object.Destroy(r); Recipes.Clear(); }
        // The colony's own recipe for a thing (at the workbench), or null.
        public static Recipe RecipeFor(string prefab)
        {
            if (prefab == Thread && Compat.HomespunOn) return Spindle(Thread, LineFlax, 2);
            if (prefab == Yarn && Compat.HomespunOn) return Spindle(Yarn, HsWool, 1);
            if (Recipes.TryGetValue(prefab, out Recipe have) && have != null) return have;
            var row = Table.FirstOrDefault(t => t.item == prefab);
            if (row.item == null || ObjectDB.instance == null || ZNetScene.instance == null) return null;
            ItemDrop item = ObjectDB.instance.GetItemPrefab(prefab)?.GetComponent<ItemDrop>();
            CraftingStation bench = ZNetScene.instance.GetPrefab("piece_workbench")?.GetComponent<CraftingStation>();
            var needs = row.needs.Select(n => ObjectDB.instance.GetItemPrefab(n.res)?.GetComponent<ItemDrop>() is ItemDrop d ? new Piece.Requirement { m_resItem = d, m_amount = n.n } : null).ToArray();
            if (item == null || bench == null || needs.Any(n => n == null)) return null;
            Recipe r = ScriptableObject.CreateInstance<Recipe>();
            r.name = "Jarldom_Weave_" + prefab; r.m_item = item; r.m_amount = 1; r.m_enabled = true; r.m_craftingStation = bench; r.m_minStationLevel = 1; r.m_resources = needs;
            Recipes[prefab] = r;
            return r;
        }
        // With Homespun, spun by hand from its line flax or combed wool (a spindle's work) where there's no spinning wheel: the soaking and
        // breaking are its stations' (the weaver keeps them going: Work.cs's station scan), not skipped as plain flax spun at once.
        public const string LineFlax = "Homespun_LineFlax", HsWool = "Homespun_Wool", Yarn = "Homespun_WoolYarn", Wheel = "piece_spinningwheel", Pool = "Homespun_RettingPool", Brake = "Homespun_FlaxBrake";
        private static Recipe Spindle(string to, string from, int n)
        {
            string key = "spindle:" + to;
            if (Recipes.TryGetValue(key, out Recipe have) && have != null) return have;
            ItemDrop item = ObjectDB.instance?.GetItemPrefab(to)?.GetComponent<ItemDrop>(), res = ObjectDB.instance?.GetItemPrefab(from)?.GetComponent<ItemDrop>();
            CraftingStation bench = ZNetScene.instance?.GetPrefab("piece_workbench")?.GetComponent<CraftingStation>();
            if (item == null || res == null || bench == null) return null;
            Recipe r = ScriptableObject.CreateInstance<Recipe>();
            r.name = "Jarldom_Weave_Spindle_" + to; r.m_item = item; r.m_amount = n; r.m_enabled = true; r.m_craftingStation = bench; r.m_minStationLevel = 1;
            r.m_resources = new[] { new Piece.Requirement { m_resItem = res, m_amount = 1 } };
            return Recipes[key] = r;
        }
        public static bool Is(Recipe r) => r != null && (r.name.StartsWith("Jarldom_Weave_") || OnLoom(r));
        // Made at Homespun's loom or sewing table: the weaver's work.
        public static bool OnLoom(Recipe r) => r?.m_craftingStation != null && Compat.HomespunOn && Compat.HomespunStations.Contains(Utils.GetPrefabName(r.m_craftingStation.gameObject));

        // ---- Homespun's stations: the retting pool, the flax brake and the spinning wheel are the weaver's to keep ----
        public static bool IsTextile(Smelter s)
        {
            if (s == null || !Compat.HomespunOn) return false;
            string n = Utils.GetPrefabName(s.gameObject);
            return n == Wheel || Compat.HomespunStations.Contains(n);
        }
        public const int Plenty = 40; // (thread or yarn in the stores: enough, the stations rest)
        // What a textile station is fed: flax to soak (never the seed flax), soaked flax to break, line flax and wool to spin; a lox pelt is
        // combed only while yarn runs short, and two kept back (it's a rare hide); raw flax never straight onto the wheel (soaked first).
        public static bool FeedTextile(Colony c, Smelter s, string item)
        {
            int Has(string x) => Crafts.InStock(c, x);
            string at = Utils.GetPrefabName(s.gameObject);
            bool linenPlenty = Has(Thread) + 2 * Has(LineFlax) >= Plenty, woolPlenty = Has(Yarn) + Has(HsWool) + Has("ByreWool") >= Plenty;
            switch (item)
            {
                case Flax: return at == Pool && !linenPlenty && Has(Flax) > SeedFlax(c);
                case "Homespun_RettedFlax": return !linenPlenty && Has(item) > 0;
                case LineFlax: return !linenPlenty && Has(item) > 0;
                case "LoxPelt": return !woolPlenty && Has(Yarn) + Has(HsWool) < 8 && Has(item) > 2;
                case HsWool: case "ByreWool": return !woolPlenty && Has(item) > 0;
                default: return Has(item) > 0;
            }
        }
        public static IEnumerable<Recipe> All() => Table.Select(t => RecipeFor(t.item)).Where(r => r != null);

        // What's clothes (the colony's own and Hildir's), and what's rags.
        private static readonly HashSet<string> Dyes = new HashSet<string> { "Blueberries", "Raspberry", "Dandelion", "Thistle" };
        public static bool IsDye(ItemDrop.ItemData i) => i?.m_dropPrefab != null && Dyes.Contains(i.m_dropPrefab.name);
        public static bool IsRags(ItemDrop.ItemData i) => i?.m_dropPrefab != null && i.m_dropPrefab.name.StartsWith("ArmorRags");
        public static bool IsClothes(ItemDrop.ItemData i)
        {
            string n = Classes.Prefab(i) ?? "";
            return n.StartsWith("ArmorTunic") || n.StartsWith("ArmorDress") || n.StartsWith("HelmetHat") || n == "HelmetStrawHat" || n.StartsWith("Homespun_") && Compat.HomespunGarments.Contains(n);
        }
        public static bool InRags(Brain b)
        {
            ItemDrop.ItemData chest = b.Body.GetInventory().GetAllItems().FirstOrDefault(i => i.m_equipped && i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Chest);
            return chest == null || IsRags(chest);
        }
        public static bool Hatless(Brain b) => !b.Body.GetInventory().GetAllItems().Any(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Helmet);

        // ---- the owner's scan: what the weaver should make next ----
        public static void Scan(Colony c, double now)
        {
            var people = Villagers.All.Where(b => b.S.Colony == c.Id).ToList();
            if (!people.Any(b => b.S.Prof == Prof.Weaver)) return;
            if (c.Board.All.Any(o => o.Target.StartsWith(Prefix) && (o.State == OrderState.Open || o.State == OrderState.Claimed))) return; // (one thing at a time)
            CraftingStation bench = c.PiecesIn(c.Radius).Select(p => p.GetComponent<CraftingStation>()).FirstOrDefault(s => s != null && Utils.GetPrefabName(s.gameObject) == "piece_workbench");
            if (bench == null) return;
            var spare = Crafts_Spare(c);
            int thread = Crafts.InStock(c, Thread) + people.Where(b => b.S.Prof == Prof.Weaver).Select(b => b.Body.GetInventory().GetAllItems().Where(i => Classes.Prefab(i) == Thread).Sum(i => i.m_stack)).DefaultIfEmpty(0).Max(); // (and what one weaver has spun and kept by them: a weaver can't use another's)
            bool Afford(Recipe r) => Crafts.Needs(r).All(n => (n.item == Thread ? thread : Crafts.InStock(c, n.item)) >= n.amount);
            int ragged = people.Count(InRags), hatless = people.Count(Hatless);
            string make = null;
            var rng = new System.Random((int)(now / 60) ^ c.Id.GetStableHashCode());
            if (ragged > spare.chest && Compat.HomespunOn)
            {
                // With Homespun: a garment sewn at its table from cloth woven at its loom, the next step of it that can be made now.
                bool women = people.Where(InRags).Count(b => b.S.Female) * 2 >= ragged;
                make = HomespunNext(c, women, rng, thread);
            }
            else if (ragged > spare.chest)
            {
                // Something to wear: colours when there's dye, plain undyed when not; a dress or a tunic as the ragged are women or men.
                bool women = people.Where(InRags).Count(b => b.S.Female) * 2 >= ragged;
                var fits = Table.Where(t => t.item.StartsWith("ArmorTunic") || t.item.StartsWith("ArmorDress")).Select(t => RecipeFor(t.item)).Where(r => r != null && Afford(r)).ToList();
                if (fits.Count > 0) make = fits.OrderBy(r => r.m_item.name.StartsWith(women ? "ArmorDress" : "ArmorTunic") ? 0 : 1).ThenBy(r => r.m_item.name.EndsWith("10") ? 1 : 0).ThenBy(_ => rng.Next()).First().m_item.name;
            }
            else if (hatless > spare.hat + people.Count / 2 && c.R.Flag("weave:hatday") != c.Day) // (a cap now and then, not one each at once)
            {
                var hats = Table.Where(t => t.item.StartsWith("HelmetHat")).Select(t => RecipeFor(t.item)).Where(r => r != null && Afford(r)).ToList();
                if (hats.Count > 0) { make = hats[rng.Next(hats.Count)].m_item.name; c.R.Flags["weave:hatday"] = c.Day; }
            }
            // No thread to weave with: spin some from the flax in the stores, by hand if there's no spinning wheel (one feeds itself).
            if (make == null && !Compat.HomespunOn && (ragged > spare.chest || hatless > 0) && thread < 6 && Crafts.InStock(c, Flax) - SeedFlax(c) >= 2
                && !c.PiecesIn(c.Radius).Any(p => p.GetComponent<Smelter>() is Smelter s && s.m_conversion.Any(cv => cv.m_from != null && cv.m_from.name == Flax)))
                make = Thread;
            if (make == null) return;
            Vector3 at = (Crafts.StationFor(c, Crafts.RecipeFor(make), bench.transform.position) ?? bench).transform.position;
            c.Board.Post("craft", Prefix + make, Prof.Weaver, 2, at.x, at.y, at.z, now, "", "clothes");
        }
        // The garments in the order tried: Homespun's own in wool, Hildir's (linen, dyed), the plain linen undertunic.
        private static readonly string[] Women = { "Homespun_WoolDress" }, Men = { "Homespun_WoolTunic" };
        internal static string HomespunNext(Colony c, bool women, System.Random rng, int thread)
        {
            int Has(string x) => x == Thread ? thread : Crafts.InStock(c, x);
            var hildir = Table.Where(t => t.item.StartsWith(women ? "ArmorDress" : "ArmorTunic")).Select(t => t.item).OrderBy(i => i.EndsWith("10") ? 1 : 0).ThenBy(_ => rng.Next());
            var wanted = (women ? Women : Men).Concat(hildir).Concat(new[] { "Homespun_LinenUndertunic" }).ToList();
            // (a garment that can be sewn now first; else the first step towards one: a bolt to weave, thread to spin by hand)
            foreach (bool now in new[] { true, false })
                foreach (string g in wanted)
                {
                    string step = Step(c, g, Has, 0);
                    if (step != null && (step == g) == now) return step;
                }
            return null;
        }
        // The next thing to make towards an item: itself if the stores hold what it takes, else (for cloth, a band, thread) what it lacks.
        private static string Step(Colony c, string item, Func<string, int> has, int depth)
        {
            Recipe r = Crafts.RecipeFor(item);
            if (r == null || depth > 3 || Crafts.StationFor(c, r, c.Position) == null) return null;
            // (spun by hand only where there's no wheel: with one, the stations make it)
            if (r.name.StartsWith("Jarldom_Weave_Spindle_") && c.PiecesIn(c.Radius).Any(p => Colony.NameOf(p) == Wheel)) return null;
            foreach (var (need, amount) in Crafts.Needs(r))
                if (has(need) < amount) return need == item ? null : Step(c, need, has, depth + 1);
            return item;
        }

        // Flax kept back for sowing, where the colony has fields (spin the last of it and there's none to grow).
        public static int SeedFlax(Colony c) => c.R.Zones.Any(z => z.Type == ZoneType.Fields) ? 10 : 0;
        internal static (int chest, int hat) Crafts_Spare(Colony c)
        {
            var items = Stock.Chests(c).Where(x => x.category != "treasury").SelectMany(x => x.chest.GetInventory().GetAllItems()).Where(IsClothes).ToList();
            return (items.Count(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Chest), items.Count(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Helmet));
        }

        // ---- flax in the colony's fields ----
        public static bool GrowsHere(GameObject sapling, Vector3 at) => sapling != null && Utils.GetPrefabName(sapling) == FlaxSapling && InFields(at);
        private static bool InFields(Vector3 at) => Colony.All.Any(c => c.Valid && c.R.Zones.Any(z => z.Type == ZoneType.Fields && Zones.Inside(z, at)));
        private static readonly AccessTools.FieldRef<Plant, Plant.Status> StatusRef = AccessTools.FieldRefAccess<Plant, Plant.Status>("m_status");
        private static readonly System.Reflection.MethodInfo HaveRoof = AccessTools.Method(typeof(Plant), "HaveRoof"), HaveGrowSpace = AccessTools.Method(typeof(Plant), "HaveGrowSpace");
        [HarmonyPatch(typeof(Plant), nameof(Plant.UpdateHealth))]
        private static class FlaxInFields
        {
            private static void Postfix(Plant __instance)
            {
                if (StatusRef(__instance) != Plant.Status.WrongBiome || !GrowsHere(__instance.gameObject, __instance.transform.position)) return;
                Heightmap hm = Heightmap.FindHeightmap(__instance.transform.position);
                Plant.Status s = Plant.Status.Healthy;
                if (__instance.m_needCultivatedGround && hm != null && !hm.IsCultivated(__instance.transform.position)) s = Plant.Status.NotCultivated;
                else if ((bool)HaveRoof.Invoke(__instance, null)) s = Plant.Status.NoSun;
                else if (!(bool)HaveGrowSpace.Invoke(__instance, null)) s = Plant.Status.NoSpace;
                StatusRef(__instance) = s;
            }
        }

        // ---- the trader's shopping, on the way home ----
        public static bool TestHildir; // (jarldom hildir on: as if she were known, for tests only, without touching the world's keys)
        public static bool HildirKnown() => TestHildir ||
            ZoneSystem.instance != null && (new[] { "Hildir1", "Hildir2", "Hildir3" }.Any(k => ZoneSystem.instance.GetGlobalKey(k))
                || Minimap.instance != null && AccessTools.FieldRefAccess<Minimap, List<Minimap.PinData>>("m_pins")(Minimap.instance).Any(p => p.m_type == Minimap.PinType.Hildir1));
        // Coins in: coins out. What was bought goes to the stores (or by the pillars, for the haulers), and the saga hears of it.
        private static bool Stow(Colony c, GameObject prefab, int n, Vector3 at)
        {
            ItemDrop.ItemData item = Stock.Of(prefab); item.m_stack = n;
            Container to = Stock.Destination(c, item, at) ?? Stock.Chests(c, at).Where(x => x.category != "treasury" && x.chest.GetInventory().CanAddItem(item)).Select(x => x.chest).FirstOrDefault();
            if (to != null && to.GetInventory().AddItem(prefab, n)) return true;
            ItemDrop.DropItem(item, n, at + Vector3.up, Quaternion.identity);
            return true;
        }
        public static int Shop(Colony c, Vector3 at, string name, int coins)
        {
            if (coins <= 0) return coins;
            var bought = new List<string>();
            var people = c.R.Living.ToList();
            bool weaver = people.Any(p => p.Prof == Prof.Weaver);
            // Flax seed for the fields, the first time (or when it's all gone): a weaver needs it.
            if (weaver && coins >= 60 && Crafts.InStock(c, Flax) == 0 && Crafts.InStock(c, Thread) == 0 && ObjectDB.instance.GetItemPrefab(Flax) is GameObject flax && Stow(c, flax, 10, at))
            { coins -= 50; bought.Add("ten flax seed from the Plains farmers"); }
            // Hildir's clothes for those still in rags, with up to half of what's left.
            int ragged = Villagers.All.Count(b => b.S.Colony == c.Id && InRags(b)) - Crafts_Spare(c).chest;
            if (ragged > 0 && HildirKnown())
            {
                Trader hildir = ZNetScene.instance.GetPrefab("Hildir")?.GetComponent<Trader>();
                var stall = hildir?.m_items.Where(t => t.m_prefab != null && (string.IsNullOrEmpty(t.m_requiredGlobalKey) || ZoneSystem.instance.GetGlobalKey(t.m_requiredGlobalKey))
                    && (t.m_prefab.name.StartsWith("ArmorTunic") || t.m_prefab.name.StartsWith("ArmorDress"))).ToList() ?? new List<Trader.TradeItem>();
                int budget = coins / 2, n = 0;
                var rng = new System.Random(Environment.TickCount);
                foreach (Trader.TradeItem t in stall.OrderBy(_ => rng.Next()))
                {
                    if (n >= Math.Min(ragged, 2) || t.m_price > budget) continue;
                    Stow(c, t.m_prefab.gameObject, 1, at);
                    budget -= t.m_price; coins -= t.m_price; n++;
                    string what = Localization.instance.Localize(t.m_prefab.m_itemData.m_shared.m_name).ToLower();
                    bought.Add(("aeiou".IndexOf(what[0]) >= 0 ? "an " : "a ") + what + " from Hildir");
                }
            }
            if (bought.Count > 0) c.Log("text", name, "", $"{name} bought {Stories.And(bought)}.");
            return coins;
        }
    }
}
