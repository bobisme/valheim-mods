using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Jarldom
{
    // Clothes. Everyone is reborn in rags; a colony dresses itself the Norse way: flax grown in its fields (the colony's farmers know it,
    // whatever the biome), or with Homespun nettle too (Core/Fibre.cs), spun into linen thread (at a spinning wheel, or by hand with a spindle), woven into tunics and dresses at
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
            if (prefab == Thread && Compat.HomespunOn) return Spindle(Thread, 2, (LineFlax, 1), (LineNettle, 2)); // (a line flax spins two threads, a line nettle one)
            if (prefab == Yarn && Compat.HomespunOn) return Spindle(Yarn, 1, (HsWool, 1));
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
        // With Homespun, spun by hand from its line flax or nettle or combed wool (a spindle's work) where there's no spinning wheel: the
        // soaking and breaking are its stations' (the weaver keeps them going: Work.cs's station scan), not skipped as plain flax spun at once.
        public const string LineFlax = Fibre.LineFlax, LineNettle = Fibre.LineNettle, Nettle = Fibre.Nettle, HsWool = "Homespun_Wool", Yarn = "Homespun_WoolYarn", Wheel = "piece_spinningwheel", Pool = "Homespun_RettingPool", Brake = "Homespun_FlaxBrake";
        // (of more than one fibre: any one of them, the first the stores have enough of, as the game's "any one" recipes. A Homespun
        // from before nettle has no line nettle: line flax only.)
        private static Recipe Spindle(string to, int n, params (string from, int amount)[] froms)
        {
            string key = "spindle:" + to;
            if (Recipes.TryGetValue(key, out Recipe have) && have != null) return have;
            ItemDrop item = ObjectDB.instance?.GetItemPrefab(to)?.GetComponent<ItemDrop>();
            var res = froms.Select(f => ObjectDB.instance?.GetItemPrefab(f.from)?.GetComponent<ItemDrop>() is ItemDrop d ? new Piece.Requirement { m_resItem = d, m_amount = f.amount } : null).Where(q => q != null).ToArray();
            CraftingStation bench = ZNetScene.instance?.GetPrefab("piece_workbench")?.GetComponent<CraftingStation>();
            if (item == null || res.Length == 0 || bench == null) return null;
            Recipe r = ScriptableObject.CreateInstance<Recipe>();
            r.name = "Jarldom_Weave_Spindle_" + to; r.m_item = item; r.m_amount = n; r.m_enabled = true; r.m_craftingStation = bench; r.m_minStationLevel = 1;
            r.m_resources = res; r.m_requireOnlyOneIngredient = res.Length > 1;
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
        // (the game's wheel spins only under a roof: one out in the rain is neither fed nor counted on to spin)
        public static bool Works(Smelter s) => s != null && (!s.m_requiresRoof || Cover.IsUnderRoof(s.m_roofCheckPoint != null ? s.m_roofCheckPoint.position : s.transform.position + Vector3.up));
        public const int Plenty = 40; // (thread or yarn in the stores: enough, the stations rest)
        // What a textile station is fed: flax and nettle to soak (never what's kept for seed), soaked to break, line flax and nettle and
        // wool to spin; a lox pelt is combed only while yarn runs short, and two kept back (it's a rare hide); raw flax never straight onto
        // the wheel (soaked first).
        public static bool FeedTextile(Colony c, Smelter s, string item)
        {
            if (!Works(s)) return false;
            int Has(string x) => Crafts.InStock(c, x);
            string at = Utils.GetPrefabName(s.gameObject);
            bool linenPlenty = Fibre.ThreadToCome(Has(Thread), Has(LineFlax), Has(LineNettle)) >= Plenty, woolPlenty = Has(Yarn) + Has(HsWool) + Has("ByreWool") >= Plenty;
            switch (item)
            {
                case Flax: case Nettle: return at == Pool && !linenPlenty && Has(item) > SeedKept(c);
                case Fibre.RettedFlax: case Fibre.RettedNettle: return !linenPlenty && Has(item) > 0;
                case LineFlax: case LineNettle: return Has(Thread) < Plenty && Has(item) > 0; // (spun while thread's short: line fibre waiting isn't thread)
                case "LoxPelt": return !woolPlenty && Has(Yarn) + Has(HsWool) < 8 && Has(item) > 2;
                case HsWool: case "ByreWool": return Has(Yarn) < Plenty && Has(item) > 0;
                default: return Has(item) > 0;
            }
        }
        // How many to bring at once: no more than FeedTextile's keep-back leaves (two lox pelts kept, the seed flax and nettle), and pelts only as many
        // as the yarn wants (four combed wool a pelt).
        public static int FeedCap(Colony c, string item)
        {
            int Has(string x) => Crafts.InStock(c, x);
            switch (item)
            {
                case "LoxPelt": return Math.Max(0, Math.Min(Has(item) - 2, (8 - Has(Yarn) - Has(HsWool) + 3) / 4));
                case Flax: case Nettle: return Math.Max(0, Has(item) - SeedKept(c));
                default: return int.MaxValue;
            }
        }
        // What a textile station is fed first: the fibre the stores are shorter of (wool, counted as yarn to come, against linen), so a
        // brake with flax to break still combs the odd lox pelt.
        public static List<string> FeedOrder(Colony c, List<string> wants)
        {
            int Has(string x) => Crafts.InStock(c, x);
            bool woolFirst = Has(Yarn) + Has(HsWool) + Has("ByreWool") < Fibre.ThreadToCome(Has(Thread), Has(LineFlax), Has(LineNettle));
            bool Wool(string x) => x == "LoxPelt" || x == HsWool || x == "ByreWool";
            return wants.OrderBy(x => Wool(x) == woolFirst ? 0 : 1).ToList();
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
            if (b.Stripped) return false; // (bathing: the clothes are in the bag)
            var bag = b.Body.GetInventory().GetAllItems();
            ItemDrop.ItemData chest = bag.FirstOrDefault(i => i.m_equipped && i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Chest);
            return (chest == null || IsRags(chest)) && !bag.Any(i => !i.m_equipped && IsClothes(i) && i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Chest); // (and none on hand to put on)
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
            bool forShip = false; int priority = 2;
            if (SailNext(c, thread) is string sail)
            { make = sail; forShip = true; priority = 4; } // (first: a wool sail for the knarr the jarl ordered, when her mast stage waits on one; one sail, and she sails)
            else if (ragged > spare.chest && Compat.HomespunOn)
            {
                // With Homespun: a garment sewn at its table from cloth woven at its loom, the next step of it that can be made now.
                bool women = people.Where(InRags).Count(b => b.S.Female) * 2 >= ragged;
                make = HomespunNext(c, women, rng, thread); priority = 4;
            }
            else if (ragged > spare.chest)
            {
                // Something to wear: colours when there's dye, plain undyed when not; a dress or a tunic as the ragged are women or men.
                bool women = people.Where(InRags).Count(b => b.S.Female) * 2 >= ragged;
                var fits = Table.Where(t => t.item.StartsWith("ArmorTunic") || t.item.StartsWith("ArmorDress")).Select(t => RecipeFor(t.item)).Where(r => r != null && Afford(r)).ToList();
                priority = 4;
                if (fits.Count > 0) make = fits.OrderBy(r => r.m_item.name.StartsWith(women ? "ArmorDress" : "ArmorTunic") ? 0 : 1).ThenBy(r => r.m_item.name.EndsWith("10") ? 1 : 0).ThenBy(_ => rng.Next()).First().m_item.name;
            }
            else if (Compat.HomespunOn && Chill.ToSew(Year.Today().Season, people.Count(b => !Cold.Has(b)), SpareWarm(c)) > 0 && WarmNext(c, thread) is string toward)
            { make = toward; priority = 3; } // (wool trousers or a hooded cloak for each of them with nothing warm, in autumn and winter)
            else if (hatless > spare.hat + people.Count / 2 && c.R.Flag("weave:hatday") != c.Day) // (a cap now and then, not one each at once)
            {
                var hats = Table.Where(t => t.item.StartsWith("HelmetHat")).Select(t => RecipeFor(t.item)).Where(r => r != null && Afford(r)).ToList();
                if (hats.Count > 0) { make = hats[rng.Next(hats.Count)].m_item.name; c.R.Flags["weave:hatday"] = c.Day; }
            }
            // No thread to weave with: spin some from the flax in the stores, by hand if there's no spinning wheel (one feeds itself).
            if (make == null && !Compat.HomespunOn && (ragged > spare.chest || hatless > 0) && thread < 6 && Crafts.InStock(c, Flax) - SeedKept(c) >= 2
                && !c.PiecesIn(c.Radius).Any(p => p.GetComponent<Smelter>() is Smelter s && s.m_conversion.Any(cv => cv.m_from != null && cv.m_from.name == Flax)))
            { make = Thread; priority = 3; }
            if (make == null) return;
            Vector3 at = (Crafts.StationFor(c, Crafts.RecipeFor(make), bench.transform.position) ?? bench).transform.position;
            // (the stations' tending is priority 3 and always waiting, so a craft below it never got made: the ship's sail and clothes for
            // the ragged come first (4); warm clothes in the cold and thread for the ragged take turns with the tending, nearest first (3);
            // caps can wait (2))
            c.Board.Post("craft", Prefix + make, Prof.Weaver, priority, at.x, at.y, at.z, now, "", forShip ? "ship" : "clothes");
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
        // Something warm or what it takes next (a bolt, a band, thread): one that can be sewn now first, the trousers (two bolts) before
        // the cloak (three).
        private static string WarmNext(Colony c, int thread)
        {
            foreach (bool now in new[] { true, false })
                foreach (string g in new[] { Chill.Trousers, Chill.Cloak })
                    if (Step(c, g, x => x == Thread ? thread : Crafts.InStock(c, x), 0) is string step && (step == g) == now) return step;
            return null;
        }
        // A sail, or the next step towards one, while the knarr on the colony's slipway needs one the stores don't hold: in the colony's
        // colour when its dyed bolts are in the stores, else undyed.
        private static string SailNext(Colony c, int thread) => SailNext(c, thread, out _);
        // (jarldom ship: what the weaver would do about a sail, and why)
        internal static string SailWhy(Colony c) => (SailNext(c, Crafts.InStock(c, Thread), out string why) ?? "nothing") + ": " + why;
        private static string SailNext(Colony c, int thread, out string why)
        {
            string sail = Compat.HomespunOn ? Compat.HomespunSail : null;
            why = sail == null ? "no Homespun sails" : "no knarr on a slipway";
            if (sail == null || !(Shipyard.Building(c) is var on) || on == null || on.Value.id != Shipyard.SlipwayId) return null;
            int need = Compat.SlipwayNeeds(on.Value.at).Where(n => n.item == sail).Sum(n => n.n);
            why = "her stage under way needs no sail";
            if (need == 0) return null;
            int held = Stock.Chests(c).Where(x => x.category != "treasury").SelectMany(x => x.chest.GetInventory().GetAllItems())
                .Concat(Villagers.All.Where(b => b.S.Colony == c.Id && b.Body != null).SelectMany(b => b.Body.GetInventory().GetAllItems()))
                .Where(i => i.m_dropPrefab != null && Compat.HomespunIsSail(i.m_dropPrefab.name)).Sum(i => i.m_stack);
            why = held + " held of " + need;
            if (held >= need) return null;
            Func<string, int> has = x => x == Thread ? thread : Crafts.InStock(c, x);
            foreach (string colour in Dyeing.Of(c))
                if (Compat.HomespunDyedAs(sail, colour) is string dyed && Step(c, dyed, has, 0) == dyed) return dyed;
            string step = Step(c, sail, has, 0);
            why += step == null ? ", and nothing towards it can be made (" + (Crafts.RecipeFor(sail) == null ? "no recipe" : Crafts.StationFor(c, Crafts.RecipeFor(sail), c.Position) == null ? "no sewing table" : "short of " + string.Join(", ", Crafts.Needs(Crafts.RecipeFor(sail)).Select(n => n.item + " " + has(n.item) + "/" + n.amount))) + ")" : "";
            return step;
        }
        internal static int SpareWarm(Colony c) => Stock.Chests(c).Where(x => x.category != "treasury").SelectMany(x => x.chest.GetInventory().GetAllItems()).Count(Cold.Warm);
        // The next thing to make towards an item: itself if the stores hold what it takes, else (for cloth, a band, thread) what it lacks.
        private static string Step(Colony c, string item, Func<string, int> has, int depth)
        {
            Recipe r = Crafts.RecipeFor(item);
            if (r == null || depth > 3 || Crafts.StationFor(c, r, c.Position) == null) return null;
            // (spun by hand only where there's no wheel that works: with one, the stations make it)
            if (r.name.StartsWith("Jarldom_Weave_Spindle_") && c.PiecesIn(c.Radius).Any(p => Colony.NameOf(p) == Wheel && Works(p.GetComponent<Smelter>()))) return null;
            var needs = Crafts.Needs(r);
            if (r.m_requireOnlyOneIngredient) // (any one of them: thread from line flax or line nettle)
                return needs.Any(n => has(n.item) >= n.amount) ? item : needs.Where(n => n.item != item).Select(n => Step(c, n.item, has, depth + 1)).FirstOrDefault(s => s != null);
            foreach (var (need, amount) in needs)
                if (has(need) < amount) return need == item ? null : Step(c, need, has, depth + 1);
            return item;
        }

        // Flax and nettle kept back for sowing, where the colony has fields (each; Core/Fibre.cs).
        public static int SeedKept(Colony c) => Fibre.Kept(c.R.Zones.Any(z => z.Type == ZoneType.Fields));
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
