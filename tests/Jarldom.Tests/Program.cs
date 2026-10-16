using Jarldom;

int checks = 0;
void Check(bool ok, string name) { checks++; if (!ok) throw new Exception(name); }

// ---- Tables ----
Check(Tables.Professions.Length == Enum.GetValues<Prof>().Length && Tables.Professions.Select((p, i) => (int)p.Id == i).All(x => x), "Professions in enum order");
Check(Tables.Rooms.Select((r, i) => (int)r.Id == i).All(x => x) && Tables.Rooms.Length == Enum.GetValues<RoomType>().Length, "Rooms in enum order");
Check(Tables.Zones.Select((z, i) => (int)z.Id == i).All(x => x), "Zones in enum order");
Check(Tables.Ranks.Select((r, i) => (int)r.Id == i).All(x => x), "Ranks in order");
Check(Tables.Ranks.Zip(Tables.Ranks.Skip(1)).All(p => p.First.Population < p.Second.Population && p.First.Cap < p.Second.Cap), "Ranks grow");
Check(Tables.Ranks.Zip(Tables.Ranks.Skip(1)).All(p => p.First.Cap >= p.Second.Population), "Each rank holds enough souls to reach the next");
Check(Tables.Wage(Prof.Smith, false) == 8 && Tables.Wage(Prof.Guard, true) == 8 && Tables.Wage(Prof.Hauler, true) == 2, "Wages, greedy half again");
Check(Tables.Grade(0) == 'D' && Tables.Grade(10) == 'C' && Tables.Grade(20) == 'B' && Tables.Grade(35) == 'A' && Tables.Grade(55) == 'S', "Grade bands");
Check(Tables.Moodlets.Select(m => m.Id).Distinct().Count() == Tables.Moodlets.Length, "Moodlet ids unique");
Check(Tables.Traits.Select(t => t.Id).Distinct().Count() == Tables.Traits.Length, "Trait ids unique");
Check(Tables.Clash("brave", "coward") && Tables.Clash("early", "nightowl") && !Tables.Clash("brave", "merry"), "Trait clashes");

// ---- Records round-trip ----
var colony = new ColonyRecord { Id = "c1", Name = "Bob's hall", Founded = 123.5, Founder = 77, FounderName = "Bob", Rank = Rank.Hamlet, Deeds = 40, Treasury = "t1" };
colony.Stockpiles["ch1"] = "wood"; colony.Favour["odin"] = 12;
colony.Roster.Add(new RosterRow { Vid = "v1", Name = "Ulf", Prof = Prof.Woodcutter, State = SoulState.Active, Worth = 5.5f, X = 1, Y = 2, Z = 3, Bed = "b1" });
colony.Rooms.Add(new Room { Id = "r1", Type = RoomType.GreatHall, Xz = new List<float> { 0, 0, 10, 0, 10, 8, 0, 8 }, Ok = true, Grade = 'B', Points = 22, Failures = "" });
colony.Zones.Add(new Zone { Id = "z1", Type = ZoneType.Woodlot, Xz = new List<float> { 0, 0, 30, 0, 30, 30 } });
colony.Coming.Add(new Newcomer { Name = "Astrid", Female = true, Traits = "brave,merry", Prof = Prof.Guard, Story = "x", Seed = 5, Group = "g" });
colony.Flags["testflag"] = 3; colony.Allowed.Add("Quad");
for (int i = 0; i < 300; i++) colony.Add(new LogEntry(i, i / 10, "kill", "Ulf", "Greydwarf", ""));
Check(colony.Log.Count == ColonyRecord.LogSize && colony.Log[0].Time == 300 - ColonyRecord.LogSize, "The log is a ring");
var back = ColonyRecord.Load(colony.Save());
Check(back.Id == "c1" && back.Rank == Rank.Hamlet && back.Deeds == 40 && back.Treasury == "t1" && back.Stockpiles["ch1"] == "wood" && back.Favour["odin"] == 12, "Colony round-trip: header");
Check(back.Roster.Single().Name == "Ulf" && back.Roster[0].Prof == Prof.Woodcutter && back.Roster[0].Bed == "b1" && Math.Abs(back.Roster[0].Worth - 5.5f) < 1e-6, "Colony round-trip: roster");
Check(back.Rooms[0].Grade == 'B' && back.Rooms[0].Corners == 4 && back.Zones[0].Corners == 3 && back.Coming[0].Name == "Astrid", "Colony round-trip: rooms, zones, newcomers");
Check(back.Log.Count == ColonyRecord.LogSize && back.Flag("testflag") == 3 && back.Allowed[0] == "Quad", "Colony round-trip: log and flags");
Check(Convert.ToBase64String(back.Save()) == Convert.ToBase64String(colony.Save()), "Saving what was loaded gives the same bytes");
bool threw = false; var newer = colony.Save(); newer[0] = 99; try { ColonyRecord.Load(newer); } catch (InvalidDataException) { threw = true; }
Check(threw, "A newer save is refused, not misread");
Check(ColonyRecord.Load(null) == null && ColonyRecord.Load(new byte[0]) == null, "No data, no colony");
// The saga kept apart: the record without it, the saga on its own, and the two together again.
var bare = ColonyRecord.Load(colony.Save(withLog: false));
Check(bare.Log.Count == 0 && bare.Roster.Single().Name == "Ulf" && bare.Flag("testflag") == 3 && bare.Coming[0].Name == "Astrid", "A record saved without its saga keeps the rest");
Check(colony.Save(withLog: false).Length < colony.Save().Length / 3, "The record without its saga is small");
var sagaApart = ColonyRecord.LoadLog(colony.SaveLog());
Check(sagaApart.Count == ColonyRecord.LogSize && sagaApart[0].Time == colony.Log[0].Time && sagaApart.Last().Kind == "kill" && sagaApart.Last().B == "Greydwarf", "The saga round-trips on its own");
bare.Log = sagaApart;
Check(Convert.ToBase64String(bare.Save()) == Convert.ToBase64String(colony.Save()), "Record and saga together are the record as it was");
Check(ColonyRecord.LoadLog(null).Count == 0 && ColonyRecord.LoadLog(new byte[0]).Count == 0, "No saga, an empty one");
// Copies of a saga merged, never replaced: a short copy (an owner who hadn't read it yet, a record's latest entries) adds, never shrinks.
var full = colony.Log.ToList();
var shortCopy = new List<LogEntry> { new LogEntry(1000, 100, "text", "", "", "a new day") };
var merged = ColonyRecord.MergeLog(full, shortCopy);
Check(merged.Count == ColonyRecord.LogSize && merged.Last().Text == "a new day" && merged[0].Time == full[1].Time, "A short copy adds its entry to the long saga (the oldest drops off the ring)");
Check(ColonyRecord.MergeLog(full, full).SequenceEqual(full), "A saga merged with itself is itself");
Check(ColonyRecord.MergeLog(new List<LogEntry>(), full).Count == full.Count && ColonyRecord.MergeLog(full, null).Count == full.Count, "An empty copy loses nothing");
var early = new List<LogEntry> { new LogEntry(5, 0, "a", "", "", "x"), new LogEntry(7, 0, "c", "", "", "z") };
var late = new List<LogEntry> { new LogEntry(6, 0, "b", "", "", "y"), new LogEntry(7, 0, "c", "", "", "z") };
Check(string.Join(",", ColonyRecord.MergeLog(early, late).Select(e => e.Kind)) == "a,b,c", "Entries from both copies, once each, in time order");
bool newerSaga = false; var sagaBytes = colony.SaveLog(); sagaBytes[0] = 99; try { ColonyRecord.LoadLog(sagaBytes); } catch (InvalidDataException) { newerSaga = true; }
Check(newerSaga, "A newer saga is refused, not read as empty");

var soul = new VillagerState { Vid = "v1", Name = "Ulf", Traits = { "brave", "glutton" }, Prof = Prof.Guard, Worth = 12, Hoard = 30, Bed = "b1" };
soul.Skills["Axes"] = 21.5f; soul.Bonds["v2"] = 60; soul.Opinions["Bob"] = 40; soul.Bump("kills", 3); soul.Foods.Add("CookedMeat");
Mood.Add(soul, "feast", 0);
var soul2 = VillagerState.Load(soul.Save());
Check(soul2.Name == "Ulf" && soul2.Has("glutton") && soul2.Prof == Prof.Guard && soul2.Skill("Axes") == 21.5f && soul2.Bonds["v2"] == 60 && soul2.Count("kills") == 3, "Villager round-trip");
Check(soul2.Moodlets.Single().Id == "feast" && soul2.Foods[0] == "CookedMeat" && soul2.Opinions["Bob"] == 40 && soul2.Bed == "b1", "Villager round-trip: moodlets, food, opinions");

// ---- Renown and ranks ----
Check(Renown.Wealth(0, 0, 0) == 0 && Renown.Wealth(400, 0, 0) == 10 && Renown.Wealth(0, 40, 0) == 10, "Wealth: a square root, coins count tenfold");
Check(Renown.Wealth(-5, -5, -5) == 0, "Wealth never negative");
Check(Renown.Comfort(new int[0]) == 0 && Renown.Comfort(new[] { 4, 2 }) == 15, "Comfort: three times the best bed plus the average");
// ---- Trade runs grow with the colony ----
Check(Trading.Choose(Rank.Village, false, false, false, 40) == TradeWay.Foot, "No cart: on foot");
Check(Trading.Choose(Rank.Village, true, false, false, 4) == TradeWay.Foot && Trading.Choose(Rank.Village, true, false, false, 10) == TradeWay.Cart, "The cart only when there's more than a pack");
Check(Trading.Choose(Rank.Village, true, true, true, 50) == TradeWay.Cart, "Ships wait for their rank");
Check(Trading.Choose(Rank.Jarldom, true, true, false, 25) == TradeWay.Karve && Trading.Choose(Rank.Jarldom, true, true, true, 100) == TradeWay.Karve, "A karve at jarldom rank, not yet the longship");
Check(Trading.Choose(Rank.Hold, false, false, true, 100) == TradeWay.Longship && Trading.Choose(Rank.Hold, true, true, true, 15) == TradeWay.Cart, "A longship for a big load; a small load stays small");
// Trade chests: goods the game gives no price, priced by kind; treasure at its own price
Check(Trading.GoodsPrice("Amber", "valuables", 5, 3) == 15, "A valuable sells at the game's price");
Check(Trading.GoodsPrice("Wood", "wood", 0, 50) == 5 && Trading.GoodsPrice("Carrot", "rawfood", 0, 50) == 10, "Wood a coin a ten, carrots two");
Check(Trading.GoodsPrice("Wood", "wood", 0, 9) == 0 && Trading.GoodsPrice("Wood", "wood", 0, 0) == 0, "Less than a coin's worth fetches nothing");
Check(Trading.GoodsPrice("Iron", "metal", 0, 10) == 50 && Trading.GoodsPrice("IronScrap", "ore", 0, 10) == 15, "Smelted metal pays far better than ore");
Check(Trading.GoodsPrice("SwordBronze", "gear", 0, 1, 2) == 20 && Trading.GoodsPrice("SwordBronze", "gear", 0, 1, 1) == 10, "Gear by quality");
Check(Trading.GoodsPrice("Mystery", null, 0, 10) == 2, "Anything unknown still sells, cheaply");
{
    // (a day's work in the Creative colony: 107 wood, 99 carrots, 32 flax, 13 copper ore = about half of twenty villagers' daily wages, 98)
    int day = Trading.GoodsPrice("Wood", "wood", 0, 107) + Trading.GoodsPrice("Carrot", "rawfood", 0, 99) + Trading.GoodsPrice("Flax", "seeds", 0, 32) + Trading.GoodsPrice("CopperOre", "ore", 0, 13);
    Check(day >= 35 && day <= 70, $"A day's raw work sells for about half the day's wages ({day} of 98)");
}
Check(Trading.Choose(Rank.Jarldom, false, false, true, 25) == TradeWay.Karve, "A longship sails as a karve before hold rank");
Check(Trading.Lots(TradeWay.Foot) < Trading.Lots(TradeWay.Cart) && Trading.Lots(TradeWay.Cart) < Trading.Lots(TradeWay.Karve) && Trading.Lots(TradeWay.Karve) < Trading.Lots(TradeWay.Longship), "Each way carries more");
Check(Trading.PricePercent(TradeWay.Longship) > Trading.PricePercent(TradeWay.Karve) && Trading.PricePercent(TradeWay.Karve) > Trading.PricePercent(TradeWay.Cart), "Further markets pay better");
Check(Trading.LossChance(TradeWay.Karve, 5, true) < Trading.LossChance(TradeWay.Karve, 0, false) && Trading.LossChance(TradeWay.Karve, -20, true) > Trading.LossChance(TradeWay.Foot, 0, false), "Njord rules the sea");
Check(Trading.Decode(Trading.Encode(120, TradeWay.Karve), out int tc, out TradeWay tw) && tc == 120 && tw == TradeWay.Karve, "Trade record round-trip");
Check(Trading.Decode("trade:75", out tc, out tw) && tc == 75 && tw == TradeWay.Foot && !Trading.Decode("cart:300", out _, out _), "An old trade record is on foot");

Check(Renown.Threshold(0) == 20 && Renown.Threshold(1) == 25 && Renown.Threshold(2) == 31 && Renown.Threshold(10) == 83 && Renown.Threshold(30) < 300, "Arrival thresholds grow by a quarter at first, then gently");
Check(Renown.RaidScale(0) == 1 && Renown.RaidScale(400) == 2 && Renown.RaidScale(9999) == 2 && Renown.RaidScale(200) == 1.5f, "Raid scale 1x to 2x");
Check(Renown.Radius(Rank.Camp) == 60 && Renown.Radius(Rank.Hold) == 110, "Radius grows with rank");
var none = new List<(RoomType, char)>();
Check(Ranks.Evaluate(0, none) == Rank.Camp && Ranks.Evaluate(5, none) == Rank.Camp, "No longhouse, still a camp");
Check(Ranks.Evaluate(3, new[] { (RoomType.Longhouse, 'D') }) == Rank.Homestead, "Three souls and a longhouse: a homestead");
var hamlet = new[] { (RoomType.Longhouse, 'D'), (RoomType.GreatHall, 'C'), (RoomType.Stockpile, 'D') };
Check(Ranks.Evaluate(6, hamlet) == Rank.Hamlet && Ranks.Evaluate(5, hamlet) == Rank.Homestead, "A hamlet needs six");
var village = hamlet.Concat(new[] { (RoomType.OdinShrine, 'D'), (RoomType.Smithy, 'D') }).ToList();
Check(Ranks.Evaluate(20, village) == Rank.Village, "A village without a temple stops there");
Check(Ranks.Evaluate(20, village.Concat(new[] { (RoomType.Temple, 'D') })) == Rank.Village, "A jarldom's hall must be grade B");
Check(Ranks.Evaluate(20, village.Concat(new[] { (RoomType.Temple, 'D'), (RoomType.GreatHall, 'B') })) == Rank.Jarldom, "Grade B hall makes a jarldom");
Check(Ranks.Evaluate(30, village.Concat(new[] { (RoomType.Temple, 'D'), (RoomType.GreatHall, 'B'), (RoomType.BurialGround, 'D'), (RoomType.Watchtower, 'D'), (RoomType.Watchtower, 'D') })) == Rank.Jarldom, "A hold needs a second temple");
Check(Ranks.Evaluate(30, village.Concat(new[] { (RoomType.Temple, 'D'), (RoomType.Temple, 'C'), (RoomType.GreatHall, 'B'), (RoomType.BurialGround, 'D'), (RoomType.Watchtower, 'D'), (RoomType.Watchtower, 'D') })) == Rank.Hold, "A hold");
Check(Ranks.Missing(Rank.Homestead, 1, none).Contains("2 more souls") && Ranks.Missing(Rank.Homestead, 1, none).Contains("longhouse"), "What the next rank is missing");
Check(Ranks.Cap(Rank.Camp, 10) == 3 && Ranks.Cap(Rank.Hold, 7) == 7, "Cap: the rank or the beds");

// ---- Mood ----
var v = new VillagerState(); for (int i = 0; i < v.Needs.Length; i++) v.Needs[i] = 50;
Check(Mood.Of(v, 0) == 0, "Half-met needs and no moodlets: neutral");
Mood.Add(v, "feast", 0); Check(Mood.Of(v, 0) == 20, "A feast is +20");
Check(Mood.Of(v, Clock.Hours(47.9)) == 20 && Mood.Of(v, Clock.Hours(48.1)) == 0, "The feast lasts two days");
Mood.Add(v, "unpaid", 0); Mood.Add(v, "unpaid", 0); Mood.Add(v, "unpaid", 0); Mood.Add(v, "unpaid", 0);
Check(v.Moodlets.Single(m => m.Id == "unpaid").Stacks == 3 && Mood.Of(v, Clock.Hours(100)) == -45, "Unpaid stacks to three");
Mood.Remove(v, "unpaid"); Check(!Mood.Has(v, "unpaid", 0), "Paid at last");
Mood.Add(v, "hall", 0); Mood.Add(v, "hall", Clock.Hours(10)); Check(v.Moodlets.Count(m => m.Id == "hall") == 1 && Mood.Has(v, "hall", Clock.Hours(33)), "Refresh keeps one, from the latest");
Mood.Expire(v, Clock.Hours(100)); Check(v.Moodlets.Count == 0, "Old moodlets expire");
for (int i = 0; i < v.Needs.Length; i++) v.Needs[i] = 100; Check(Mood.Of(v, 0) == 70, "Every need met: +70");
for (int i = 0; i < v.Needs.Length; i++) v.Needs[i] = 0; Check(Mood.Of(v, 0) == -70, "Every need empty: -70");
Check(Mood.Multiplier(-100) == 0.6f && Mood.Multiplier(0) == 1f && Math.Abs(Mood.Multiplier(100) - 1.3f) < 1e-6 && Math.Abs(Mood.Multiplier(-50) - 0.8f) < 1e-6, "Mood multiplier");
var g = new VillagerState { Traits = { "glutton", "pious" } }; var plain = new VillagerState();
Mood.Tick(g, 10); Mood.Tick(plain, 10);
Check(g.Needs[(int)Need.Food] < plain.Needs[(int)Need.Food] && g.Needs[(int)Need.Faith] < plain.Needs[(int)Need.Faith], "Gluttons hunger and the pious long faster");
Mood.Add(g, "finemeal", 0); Mood.Add(plain, "finemeal", 0);
Check(Mood.MoodletPart(g, 0) == 16 && Mood.MoodletPart(plain, 0) == 8, "A glutton loves a fine meal twice as much");
Check(Mood.Reasons(g, 0).First().value != 0, "Reasons are listed");

// ---- Schedule ----
Check(Schedule.At(3, null) == Activity.Sleep && Schedule.At(6.5, null) == Activity.Wake && Schedule.At(12, null) == Activity.Work, "Night, morning, day");
Check(Schedule.At(19, null) == Activity.Hall && Schedule.At(21.5, null) == Activity.Home && Schedule.At(23, null) == Activity.Sleep, "Hall, home, bed");
Check(Schedule.At(5.5, new[] { "early" }) == Activity.Wake && Schedule.At(6.5, new[] { "nightowl" }) == Activity.Sleep, "Early risers and night owls");
Check(Schedule.At(18.5, new[] { "nightowl" }) == Activity.Work, "Night owls work an hour later");
Check(Schedule.At(12, null, raid: true) == Activity.Defend && Schedule.At(3, null, raid: true) == Activity.Defend, "Raids override everything");
Check(Schedule.At(12, null, heavyRain: true, outdoorTask: true) == Activity.Shelter && Schedule.At(12, null, heavyRain: true, outdoorTask: true, urgent: true) == Activity.Work, "Shelter from heavy rain unless urgent");
for (double hh = 0; hh < 24; hh += 0.5) Check(Math.Abs(Clock.HourOfDay(Clock.At(3, hh)) - hh) < 1e-6, "Clock.At inverts HourOfDay: " + hh);
Check(Math.Abs(Clock.HourOfDay(0.15 * Clock.DayLength) - 6) < 1e-9 && Math.Abs(Clock.HourOfDay(0.85 * Clock.DayLength) - 18) < 1e-9 && Math.Abs(Clock.HourOfDay(0)) < 1e-9, "Dawn at 15%, dusk at 85%, midnight at 0");
Check(Clock.Day(1799) == 0 && Clock.Day(1800) == 1 && Math.Abs(Clock.HourOfDay(900) - 12) < 1e-9 && Clock.IsNight(100) && !Clock.IsNight(900), "Clock");

// ---- Job board ----
var board = new JobBoard();
int a = board.Post("chop", "tree1", Prof.Woodcutter, 3, 10, 0, 0, 0);
int a2 = board.Post("chop", "tree1", Prof.Woodcutter, 4, 10, 0, 0, 1);
Check(a == a2 && board.Get(a).Priority == 4 && board.All.Count() == 1, "Posting twice is one order (priority raised)");
Check(board.Claim(a, "v1", 0, out double until) && until == JobBoard.LeaseSeconds, "Claim gives a lease");
Check(!board.Claim(a, "v2", 1, out _), "A claimed order can't be taken by another");
Check(board.Claim(a, "v1", 30, out until) && until == 30 + JobBoard.LeaseSeconds, "Claiming again renews");
Check(board.Expire(30 + JobBoard.LeaseSeconds + 1) == 1 && board.Get(a).State == OrderState.Open, "A lapsed lease opens the order");
Check(board.Claim(a, "v2", 100, out _), "Then another can take it");
Check(!board.Complete(a, "v1"), "Only the holder completes it");
Check(board.Complete(a, "v2") && !board.Complete(a, "v2") && board.CompletedCount == 1, "Completion counts once");
int b = board.Post("chop", "tree1", Prof.Woodcutter, 3, 10, 0, 0, 200);
Check(b != a, "A finished order's target can be posted again");
int c = board.Post("haul", "drop1", Prof.Hauler, 2, 0, 0, 0, 200);
Check(board.Claim(b, "v3", 200, out _) && board.Claim(c, "v3", 201, out _) && board.Get(b).State == OrderState.Open, "One lease per villager: taking a new one lets the old go");
Check(board.Reserve(c, "ch1", "Wood", 10, 15) && !board.Reserve(c, "ch1", "Wood", 10, 15) && board.Available("ch1", "Wood", 15) == 5, "Reservations stop double takes");
board.Release(c, "v3"); Check(board.Available("ch1", "Wood", 15) == 15, "Releasing frees the items");
board.Expire(300); board.Block(b, "No axe"); Check(board.Get(b).State == OrderState.Blocked && board.Get(b).Blocked == "No axe" && !board.Claim(b, "v1", 300, out _), "Blocked orders say why and can't be claimed");
board.Expire(300 + JobBoard.BlockSeconds - 1); Check(board.Get(b).State == OrderState.Blocked, "A block holds a while");
board.Expire(300 + JobBoard.BlockSeconds + 1); Check(board.Get(b).State == OrderState.Open, "and then the job is tried again");
{
    var reach = new JobBoard();
    int t1 = reach.Post("fuel", "torch1", Prof.Hauler, 3, 0, 0, 0, 0), t2 = reach.Post("fuel", "torch2", Prof.Hauler, 3, 5, 0, 0, 0), t3 = reach.Post("fuel", "torch3", Prof.Hauler, 3, 30, 0, 0, 0);
    reach.Expire(10); reach.Block(t1, "can't reach it");
    Check(reach.Get(t2).State == OrderState.Blocked && reach.Get(t3).State == OrderState.Open, "Out of reach: the same work right by it is too, not what's farther off");
    reach.Expire(10 + JobBoard.BlockSeconds + 1); Check(reach.Get(t1).State == OrderState.Open, "An unreachable job is tried again after a while");
    reach.Block(t1, "can't reach it");
    Check(reach.Get(t1).State == OrderState.Cancelled && reach.Get(t2).State == OrderState.Cancelled && reach.LeftAlone.Contains("fuel|torch1") && reach.LeftAlone.Contains("fuel|torch2"), "A fire out of reach twice is left alone, and the one right by it too");
    Check(reach.Get(t3).State == OrderState.Open && !reach.LeftAlone.Contains("fuel|torch3"), "but not one farther off");
    Check(reach.Post("fuel", "torch1", Prof.Hauler, 3, 0, 0, 0, 20) == -1, "and a fire left alone isn't posted again");
    reach.LeftAlone.Clear(); Check(reach.Post("fuel", "torch1", Prof.Hauler, 3, 0, 0, 0, 20) >= 0, "until it's tried again");
    var walk = new JobBoard();
    int h1 = walk.Post("haul", "log1", Prof.Hauler, 3, 0, 0, 0, 0);
    walk.Expire(10); walk.Block(h1, "can't reach it"); walk.Expire(10 + JobBoard.BlockSeconds + 1);
    walk.Block(h1, "can't reach it"); walk.Expire(10 + JobBoard.BlockSeconds * 2.5); Check(walk.Get(h1).State == OrderState.Blocked && walk.LeftAlone.Count == 0, "Other work out of reach again waits twice as long (only fires are left alone)");
}
var hi = board.Post("chop", "tree9", Prof.Woodcutter, 5, 100, 0, 100, 300);
var near = board.Post("chop", "tree8", Prof.Woodcutter, 3, 1, 0, 1, 300);
Check(board.Best("v9", p => p == Prof.Woodcutter, 0, 0, null, 300).Id == hi, "Highest priority first");
Check(board.Best("v9", p => p == Prof.Woodcutter, 0, 0, o => o.Id == hi ? "No axe of tier 2" : null, 300).Id != hi, "Gear problems skip an order");
Check(board.Best("v9", p => p == Prof.Farmer, 0, 0, null, 300) == null, "Nothing for the wrong profession");
// Double-claim stress: many villagers race for the same orders.
var race = new JobBoard(); var rng = new Random(1);
for (int i = 0; i < 50; i++) race.Post("haul", "d" + i, Prof.Hauler, 1 + rng.Next(5), rng.Next(100), 0, rng.Next(100), 0);
var holders = new Dictionary<int, string>();
for (int step = 0; step < 2000; step++)
{
    string who = "v" + rng.Next(8); double now = step * 0.5;
    int id = 1 + rng.Next(50);
    if (race.Claim(id, who, now, out _)) holders[id] = who;
    if (rng.Next(4) == 0 && holders.TryGetValue(id, out string h)) race.Complete(id, h);
    Check(race.All.Where(o => o.State == OrderState.Claimed).GroupBy(o => o.Vid).All(gr => gr.Count() == 1), "Never two leases for one villager");
}
Check(race.CompletedCount == race.All.Count(o => o.State == OrderState.Done), "Each completion counted once");

// ---- Rooms ----
var hallFacts = new RoomFacts { Walls = new[] { 1f, 0.95f, 0.9f, 1f }, Roof = 0.97f, Doors = 1, Area = 48 };
hallFacts.Count["fire"] = 1; hallFacts.Count["seat"] = 6; hallFacts.Count["table"] = 2;
var verdict = RoomRules.Judge(RoomType.GreatHall, hallFacts);
Check(verdict.Ok && verdict.Grade == 'C', "A plain great hall works: grade C");
hallFacts.Walls[2] = 0.4f; verdict = RoomRules.Judge(RoomType.GreatHall, hallFacts);
Check(!verdict.Ok && verdict.OpenEdges.Single() == 2 && verdict.Failures[0].Contains("gap"), "A missing wall shows where");
hallFacts.Walls[2] = 1; hallFacts.Count["banner"] = 4; hallFacts.Count["carving"] = 2; hallFacts.Count["seat"] = 10;
Check(RoomRules.Judge(RoomType.GreatHall, hallFacts).Points > verdict.Points, "Seats and banners raise the grade");
hallFacts.Count["highseat"] = 1; hallFacts.Count["gilded"] = 6; hallFacts.Count["trophy"] = 10; hallFacts.Count["rug"] = 6; hallFacts.Comfort = 8;
Check(RoomRules.Judge(RoomType.GreatHall, hallFacts).Grade == 'S', "Gilding and the high seat make an S hall");
// Storeys: a ground floor at 35 (3 m to the floor above) and an upper room at 38 over the same outline (from a real house).
var square = new List<float> { 0, 0, 10, 0, 10, 8, 0, 8 };
var ground = new Room { Id = "g", Type = RoomType.Longhouse, Floor = 35, Height = 3.04f, Xz = square };
var upper = new Room { Id = "u", Type = RoomType.Stockpile, Floor = 38, Height = 4.49f, Xz = square };
var both = new List<Room> { upper, ground };
Check(RoomRules.At(both, 5, 35.1f, 4)?.Id == "g" && RoomRules.At(both, 5, 36.8f, 4)?.Id == "g", "On the ground floor: the ground-floor room");
Check(RoomRules.At(both, 5, 38.05f, 4)?.Id == "u" && RoomRules.At(both, 5, 40f, 4)?.Id == "u", "Upstairs (even just on the floor): the upper room");
Check(RoomRules.At(both, 5, 37.7f, 4)?.Id == "g", "Just under the floor above (a chest's top): still the room below");
Check(RoomRules.At(both, 5, 43f, 4) == null && RoomRules.At(both, 12, 36f, 4) == null, "Above the roof, or outside the walls: no room");
Check(RoomRules.At(both, 5, 39f, 4, RoomType.Longhouse) == null, "Asked for the longhouse while upstairs: not the one below");
// A room drawn inside another (a shrine in a corner of the great hall, from the live colony), its floor a little lower.
var hall = new Room { Id = "h", Type = RoomType.GreatHall, Floor = 40.6f, Height = 3, Xz = new List<float> { 285, 1115, 293, 1115, 293, 1123, 285, 1123 } };
var shrineRoom = new Room { Id = "s", Type = RoomType.OdinShrine, Floor = 40.55f, Height = 3, Xz = new List<float> { 290.5f, 1120, 293, 1120, 293, 1123, 290.5f, 1123 } };
var nested = new List<Room> { hall, shrineRoom };
Check(RoomRules.At(nested, 291.8f, 41f, 1121.6f)?.Id == "s", "In the shrine in the hall's corner: the shrine");
Check(RoomRules.At(nested, 287f, 41f, 1117f)?.Id == "h", "Elsewhere in the hall: the hall");
Check(RoomRules.At(nested, 291.8f, 41f, 1121.6f, RoomType.GreatHall)?.Id == "h", "Asked for the hall in the shrine's corner: the hall");
var loft = new RoomFacts { Walls = new[] { 1f, 1f, 1f, 1f }, Roof = 1, Doors = 0, Area = 40 }; loft.Count["fire"] = 1; loft.Count["bed"] = 1;
Check(!RoomRules.Judge(RoomType.Longhouse, loft).Ok, "An upstairs room with no way in fails");
loft.Count["stairs"] = 1;
Check(RoomRules.Judge(RoomType.Longhouse, loft).Ok, "Stairs up into it are its door");
var brew = new RoomFacts { Walls = new[] { 1f, 1f, 1f, 1f }, Roof = 1, Doors = 1, Area = 40 };
Check(!RoomRules.Judge(RoomType.Brewhouse, brew).Ok && RoomRules.Judge(RoomType.Brewhouse, brew).Failures.Any(f => f.Contains("cauldron")), "A brewhouse needs a cauldron or a fermenter");
brew.Count["fermenter"] = 1;
var brewed = RoomRules.Judge(RoomType.Brewhouse, brew);
Check(brewed.Ok, "A fermenter under a roof makes a brewhouse");
brew.Count["fermenter"] = 3; brew.Count["cauldron"] = 1;
Check(RoomRules.Judge(RoomType.Brewhouse, brew).Points > brewed.Points, "More fermenters and a cauldron grade it higher");
brew.Roof = 0.3f;
Check(!RoomRules.Judge(RoomType.Brewhouse, brew).Ok, "A brewhouse must be roofed");
Check(Tables.Rooms.Select((r, i) => (int)r.Id == i).All(x => x), "Room types and their table line up (Brewhouse appended last)");
Check(Tables.Professions.Select((p, i) => (int)p.Id == i).All(x => x) && Tables.Zones.Select((z, i) => (int)z.Id == i).All(x => x), "Professions and zones line up with their tables (Herder, Pasture appended)");
var small = new RoomFacts { Walls = new[] { 1f, 1f, 1f }, Roof = 1, Doors = 1, Area = 12 }; small.Count["fire"] = 1; small.Count["seat"] = 4; small.Count["table"] = 2;
Check(RoomRules.Judge(RoomType.GreatHall, small).Failures.Any(f => f.Contains("m²")), "Halls need room");
var lh = new RoomFacts { Walls = new[] { 1f, 1f, 1f, 1f }, Roof = 0.5f, Doors = 0 }; lh.Count["bed"] = 2;
var lv = RoomRules.Judge(RoomType.Longhouse, lh);
Check(!lv.Ok && lv.Failures.Count == 3, "Longhouse: roof, door and fire missing");
var shrine = new RoomFacts { Roof = 1 }; shrine.Count["altar_odin"] = 1;
Check(RoomRules.Judge(RoomType.OdinShrine, shrine).Ok && !RoomRules.Judge(RoomType.Temple, shrine, "thor").Ok, "Altars are per god");
var tower = new RoomFacts { Roof = 1, FloorHeight = 5 }; Check(RoomRules.Judge(RoomType.Watchtower, tower).Ok && !RoomRules.Judge(RoomType.Watchtower, new RoomFacts { Roof = 1, FloorHeight = 2 }).Ok, "Watchtowers stand tall");

// ---- Geometry ----
var sq = new List<float> { 0, 0, 10, 0, 10, 10, 0, 10 };
Check(Math.Abs(Geometry.Area(sq) - 100) < 1e-3 && Geometry.Inside(sq, 5, 5) && !Geometry.Inside(sq, 11, 5), "Area and inside");
Check(Geometry.Distance(sq, 5, 5) == 0 && Math.Abs(Geometry.Distance(sq, 13, 5) - 3) < 1e-4, "Distance to a polygon");
Check(Geometry.EdgeSamples(sq, 0.5f).Count() == 80 && Geometry.Grid(sq, 1).Count() == 100, "Edge samples and grid");
Check(Geometry.Simple(sq) && !Geometry.Simple(new List<float> { 0, 0, 10, 10, 10, 0, 0, 10 }), "Self-crossing outlines are refused");
var ell = new List<float> { 0, 0, 10, 0, 10, 4, 4, 4, 4, 10, 0, 10 };
Check(Math.Abs(Geometry.Area(ell) - 64) < 1e-3 && !Geometry.Inside(ell, 7, 7) && Geometry.Inside(ell, 2, 7), "Concave rooms");

// ---- Economy, favour, worth ----
var team = new[] { new VillagerState { Prof = Prof.Guard }, new VillagerState { Prof = Prof.Smith, Traits = { "greedy" } } };
Check(Payroll.Due(team) == (5 + 12) * 5, "Payroll over five days");
Check(Payroll.CanPay(85, 85) && !Payroll.CanPay(84, 85), "Pay in full or not at all");
Check(Payroll.Hoarded(10) == 3 && Payroll.IsPayday(5, 0) && !Payroll.IsPayday(5, 5) && !Payroll.IsPayday(6, 0) && !Payroll.IsPayday(0, -1), "Hoard and paydays");
Check(Favour.BogPrice(0) == 50 && Favour.BogPrice(1) == 55 && Favour.BogPrice(20) > 300, "The bog wants more each time, without end");
Check(Favour.Decay(10, 3) == 4 && Favour.Decay(1, 3) == 0 && Favour.Decay(-20, 3) == -20, "Favour fades to nothing, anger stays");
Check(Favour.Offering(55, true) == 6 && Favour.Offering(2, true) == 1 && Favour.Offering(500, false) == 0, "Offerings the god wants");
Check(Favour.ArrivalChance(0, false) == 0.6f && Favour.ArrivalChance(0, true) == 0.85f && Favour.ArrivalChance(-1000, false) == 0.1f, "Arrival chance");
var w = new VillagerState();
for (int i = 0; i < 20; i++) Worth.Gain(w, "starkill");
Check(w.Worth == 100 && w.Worthy, "Worth fills to a hundred and marks them worthy");
var br = new VillagerState { Traits = { "brave" } }; Worth.Gain(br, "kill"); Check(br.Worth == 1.5f, "The brave earn more from kills");
Worth.Gain(br, "disgrace"); Check(br.Worth == 0, "Disgrace takes worth, never below nothing");
Check(Worth.AscensionDeeds(500, false) == 35 && Worth.AscensionDeeds(500, true) == 70, "Ascension deeds, doubled for the sworn");

// ---- Souls and stories ----
var s1 = Souls.Make(42, "g", null); var s2 = Souls.Make(42, "g", null);
Check(s1.Name == s2.Name && s1.Traits == s2.Traits && s1.Story == s2.Story, "The same seed makes the same soul");
for (int seed = 0; seed < 500; seed++)
{
    var n = Souls.Make(seed, "g", null); var t = n.Traits.Split(',');
    Check(t.Length == 2 && !Tables.Clash(t[0], t[1]), "Two traits that fit: " + n.Traits);
    Check(n.Story.EndsWith(".") && !n.Story.Contains("{"), "Backstory grammar filled: " + n.Story);
}
var grp = Souls.Group(new Random(3), 3);
Check(grp.Count >= 1 && grp.Count <= 3 && grp.Select(x => x.Group).Distinct().Count() == 1 && grp.Select(x => x.Name).Distinct().Count() == grp.Count, "A group shares a battle and not a name");
Check(Souls.Group(new Random(3), 1).Count == 1, "Group size capped by free beds");
var vs = Souls.Become(grp[0], "vx", "c1", 10); Check(vs.Traits.Count == 2 && vs.Prof == grp[0].Prof && vs.Wants == grp[0].Prof && Mood.Has(vs, "arrived", 10), "Becoming a villager");
var log = new List<LogEntry> { new LogEntry(0, 0, "found", "Bob", "", ""), new LogEntry(10, 0, "arrive", "Ulf", "", ""), new LogEntry(20, 0, "kill", "Ulf", "Neck", ""), new LogEntry(1900, 1, "starkill", "Ulf", "Troll", "") };
var saga = Stories.Chronicle(log);
Check(saga.Count == 2 && saga[0].StartsWith("Day 0:") && saga[0].Contains("Ulf came") && saga[0].Contains("One foe") && saga[1].Contains("mighty Troll"), "The chronicle tells the days");
var everyday = new List<LogEntry> { new LogEntry(5, 2, "inspired", "Odd", "", ""), new LogEntry(6, 2, "text", "Alfhild", "", "Alfhild woke inspired."), new LogEntry(7, 2, "offering", "Odd", "Odin", ""), new LogEntry(8, 2, "offering", "Odd", "Odin", ""), new LogEntry(9, 2, "offering", "Odd", "Thor", "") };
var told = Stories.Chronicle(everyday);
Check(told.Count == 1 && told[0].Contains("Odd and Alfhild woke inspired.") && told[0].Contains("Two offerings were made to Odin.") && told[0].Contains("An offering was made to Thor."), "The everyday is told once a day: " + told[0]);
for (int i = 0; i < 50; i++)
{
    string gossip = Stories.Gossip(new Random(i), "Astrid", log, 2000);
    Check(gossip == null || gossip.StartsWith("Astrid") && (gossip.Contains("Ulf") || gossip.Contains("Bob")), "Gossip is about logged events");
}
Check(Stories.Gossip(new Random(1), "Astrid", log, 999999) == null, "Old news isn't gossip");
var hallTold = new List<string>(); var news = new List<LogEntry> { new LogEntry(100, 1, "starkill", "Ulf", "Neck", ""), new LogEntry(110, 1, "ask", "Ulf", "", "a piece of amber"), new LogEntry(120, 1, "brawl", "Bera", "Ulf", "") };
for (int i = 0; i < 20; i++)
{
    var said = Enumerable.Range(0, 3).Select(k => Stories.Gossip(new Random(i * 7 + k), "Astrid", news, 200, hallTold)).Select(g => g.Contains("Neck") ? "kill" : g.Contains("amber") ? "ask" : "brawl").ToList();
    Check(said.Distinct().Count() == 3 && said[0] != said[1] && said[1] != said[2], "A crowd doesn't all tell the same news at once: " + string.Join(",", said));
}
Check(Enumerable.Range(0, 12).Any(k => Stories.Gossip(new Random(k), "Astrid", news.Skip(1).Take(1).ToList(), 200, hallTold) != null), "One bit of news can still be told again");
Check(Enumerable.Range(0, 30).Count(k => Stories.Gossip(new Random(k), "Astrid", news.Skip(1).Take(1).ToList(), 200, hallTold) == null) > 10, "News all told already is mostly let lie");
var shortPay = new List<LogEntry> { new LogEntry(100, 1, "unpaid", "", "290", ""), new LogEntry(101, 1, "unpaid", "", "290", ""), new LogEntry(102, 1, "unpaid", "", "290", "") };
Check(Enumerable.Range(0, 20).All(k => { string g = Stories.Gossip(new Random(k), "Astrid", shortPay, 200); return g == null || g.Contains("short") || g.Contains("wages"); }), "A short payday is gossip");
Check(Enumerable.Range(0, 20).All(k => Stories.Gossip(new Random(k), "Astrid", shortPay.Concat(new[] { new LogEntry(110, 1, "payday", "Bob", "870", "What was owed was paid.") }).ToList(), 200) == null), "Paid in full, a short payday is old news");
var twice = new List<string>(); string g1 = Stories.Gossip(new Random(1), "Astrid", shortPay, 200, twice);
Check(g1 != null && twice.Count == 1 && Enumerable.Range(0, 20).Count(k => Stories.Gossip(new Random(k), "Astrid", shortPay, 200, twice) != null) < 15, "Three short paydays are one bit of news");
var view = new ColonyView { Population = 5, Troublemakers = { ("Ulf", 2, 0) }, MissedPaydays = 1, Beds = 3 };
var pets = Petitions.Raise(new Random(1), view);
Check(pets.Count == 3 && pets.All(p => !string.IsNullOrEmpty(p.Text)), "The Thing raises three petitions");
Check(Petitions.Raise(new Random(1), view, 10).Any(p => p.Id == "exile" && p.About == "Ulf"), "A thief is named");
var suspicious = new ColonyView { Population = 5, Beds = 5, HasBeautyRoom = true, HasShrine = true, GuardWage = 9, Suspects = { ("Hrafn", 2), ("Oddny", 1) } };
var accused = Petitions.Raise(new Random(1), suspicious, 10).Where(p => p.Id == "accuse").ToList();
Check(accused.Count == 1 && accused[0].About == "Hrafn", "The Thing names one seen twice near the mischief, not one seen once");
// The Thing's matters pile up one a day, are withdrawn when they no longer stand, and are told by one they touch.
var standing = Petitions.Raise(new Random(1), view, 99);
var waiting = new List<Petition>();
for (int d = 10; d < 20; d++) { var (up, gone) = Petitions.Update(waiting, Petitions.Raise(new Random(1), view, 99), d, new Random(d)); Check(up.Count <= 1 && gone.Count == 0, "One matter a day at most"); waiting.AddRange(up); }
Check(waiting.Count == Math.Min(standing.Count, Petitions.Most) && waiting.Select(p => p.Key).Distinct().Count() == waiting.Count, "Matters pile up, never twice");
Check(waiting[0].Day == 10 && Petitions.Waited(waiting, 20) == 10 && Petitions.Waited(new List<Petition>(), 20) == 0, "How long the oldest has waited");
var mended = new ColonyView { Population = 5, Troublemakers = { ("Ulf", 2, 0) }, MissedPaydays = 0, Beds = 5 };
var (_, withdrawn) = Petitions.Update(waiting, Petitions.Raise(new Random(1), mended, 99), 21, new Random(21));
Check(withdrawn.Any(p => p.Id == "wages") && withdrawn.Any(p => p.Id == "beds") && !withdrawn.Any(p => p.Id == "exile"), "Matters seen to are withdrawn");
var folk = new List<(string vid, string name, string prof, bool bedless)> { ("v1", "Ulf", "Guard", false), ("v2", "Astrid", "Farmer", true), ("v3", "Odd", "Guard", false) };
var exile = new Petition { Id = "exile", About = "Ulf" };
for (int i = 0; i < 20; i++) Check(Petitions.Speaker(exile, folk, new Random(i)) != "v1", "The accused doesn't speak against themselves");
Check(Petitions.Speaker(new Petition { Id = "beds" }, folk, new Random(3)) == "v2" && Petitions.Speaker(new Petition { Id = "guardpay" }, folk, new Random(3)) is "v1" or "v3", "The bedless ask for beds, a guard for the guards' pay");
var withPets = new ColonyRecord { Id = "p1", Name = "x", FounderName = "Bob" };
withPets.Petitions.Add(new Petition { Id = "beds", About = "", Text = "Build beds", Yes = "Promise", No = "No", YesMood = 4, NoMood = -4, By = "v2", ByName = "Astrid", Day = 7 });
var pBack = ColonyRecord.Load(withPets.Save());
Check(pBack.Petitions.Count == 1 && pBack.Petitions[0].ByName == "Astrid" && pBack.Petitions[0].Day == 7 && pBack.Petitions[0].NoMood == -4, "Petitions round-trip in the record");
var oldBytes = new ColonyRecord { Id = "o1", Name = "x", FounderName = "Bob" }.Save();
Check(ColonyRecord.Load(oldBytes.Take(oldBytes.Length - 4).ToArray()).Petitions.Count == 0, "A record from before petitions were kept loads");
// Ore claims: kept in a global key, worked by trips; a camp takes more, safer.
var cl = new Claim { Id = "k1", Colony = "c1", X = 412.6f, Z = -88.2f, Biome = "BlackForest", Beds = 2 };
cl.Left["copperore"] = (240, 2); cl.Left["tinore"] = (40, 1); cl.Taken["copperore"] = 30;
var clBack = Claim.Decode("k1", cl.Encode());
Check(clBack != null && clBack.Colony == "c1" && clBack.X == 413 && clBack.Z == -88 && clBack.Beds == 2 && clBack.Camp && clBack.Left["copperore"] == (240, 2) && clBack.Taken["copperore"] == 30 && clBack.Ore == 280 && clBack.Tier == 1, "A claim round-trips its key");
Check(Claim.Decode("k2", new Claim { Colony = "c1", Biome = "Swamp", Crypt = true }.Encode()) is Claim empty && empty.Crypt && !empty.Camp && empty.Ore == 0 && empty.Taken.Count == 0, "An empty crypt claim round-trips");
Check(Claim.Decode("k3", "nonsense") == null && Claim.Decode("k3", "") == null, "A bad claim key is ignored");
var load = Mining.Take(cl.Left, 60, 2);
Check(load.Count == 1 && load["copperore"] == 60, "The most plentiful ore first, a load's worth");
Check(Mining.Take(cl.Left, 60, 1).Keys.Single() == "tinore" && Mining.Take(cl.Left, 60, 1)["tinore"] == 40 && Mining.Take(cl.Left, 60, 0).Count == 0, "A weak pickaxe takes only the ore it can");
Check(Mining.Take(cl.Left, 300, 2).Values.Sum() == 280, "Never more than is left");
Mining.Apply(cl, load);
Check(cl.Left["copperore"].n == 180 && cl.Taken["copperore"] == 90, "A load comes off what's left and onto what's to be taken out of the world");
Check(Mining.Carry(10, false, false) == 25 && Mining.Carry(10, true, false) == 90 && Mining.Carry(10, true, true) == Mining.CryptDig && Mining.Carry(10, false, true) == 20, "A back carries what a person can (25 ore), a cart more, a crypt's dig less");
Check(Mining.CartGoes("BlackForest", false) && !Mining.CartGoes("swamp", false) && !Mining.CartGoes("meadows", true) && !Mining.CartGoes("Mountain", false), "A cart goes only where the ground is open");
Check(Mining.TripSeconds(500, true, 1800) > Mining.TripSeconds(500, false, 1800) && Mining.TripSeconds(1000, false, 1800) > Mining.TripSeconds(500, false, 1800) && Mining.TripSeconds(500, false, 1800, "swamp") > Mining.TripSeconds(500, false, 1800, "meadows"), "Further, longer at a camp, and slower through a swamp");
var meadow = Mining.Risk("meadows", false, false, false); var crypt = Mining.Risk("swamp", true, false, false); var guarded = Mining.Risk("swamp", true, true, true);
Check(meadow.wound == 0 && crypt.wound > Mining.Risk("Swamp", false, false, false).wound && crypt.death > 0 && guarded.death < crypt.death && guarded.wound < crypt.wound, "Crypts are dangerous; a camp and a guard make them less so; biome names in any case");
Check(Mining.Need("copperore", 300, 300, 300, 0) == 0 && Mining.Need("copperore", 0, 300, 0, 0) > Mining.Need("copperore", 200, 300, 200, 0), "Ore the stores hold enough of isn't wanted; less held, more wanted");
Check(Mining.Need("tinore", 50, 300, 200, 50) > Mining.Need("copperore", 200, 300, 200, 50) && Mining.Need("tinore", 150, 300, 200, 150) < Mining.Need("tinore", 50, 300, 200, 50), "Tin wanted while short of half the copper (bronze)");
Check(Mining.Score(1.0, 200) > Mining.Score(1.0, 2000) && Mining.Score(1.0, 2000) > Mining.Score(0.2, 200) && Mining.Score(0.3, 2000) < Mining.Score(0.3, 200) && Mining.Score(0.9, 400) > Mining.Score(0.3, 200), "A big lack is worth a longer walk, a small one isn't");
Check(Mining.Need("silverore", 500, 0, 0, 0) > 0, "No limit set: still wanted, less as it piles up");
Check(Mining.Room(new Claim()) == 1 && Mining.Room(cl) == 2, "One miner at a bare claim, one a bed at a camp");
Check(Mining.Decode(Mining.Encode("k1", 1234.4, true), out string mc, out double ms, out bool mg) && mc == "k1" && ms == 1234 && mg && !Mining.Decode("trade:40", out _, out _, out _), "A miner's away record round-trips");
Check(Renown.TributeAsk(0, 0) == 40 && Renown.TributeAsk(80, 0) == 80 && Renown.TributeAsk(80, 1) == 120 && Renown.TributeAsk(80, 2) == 180, "The Fulings want more each time");
Check(Renown.TributeAsk(1000, 99) > 0, "The Fulings' price never overflows");
// Away: 40 wood in 2 worked hours is 20 an hour; two woodcutters, 48 hours away (11 of every 24 working) → 2 × 20 × 22 × 0.9.
int awayWood = Away.Produce(40, 20, 2, 48);
Check(awayWood == (int)Math.Floor(20.0 * 48 * 11 / 24 * 2 * 0.9), "Time away is made up at the live rate");
Check(awayWood >= 0.8 * (20.0 * 48 * 11 / 24 * 2) && awayWood <= 1.2 * (20.0 * 48 * 11 / 24 * 2), "Within a fifth of the live rate (the spec's test)");
Check(Away.Produce(40, 5, 2, 48) == 0, "Too little seen live to say: nothing made up");
Check(Away.Produce(40, 20, 0, 48) == 0 && Away.Produce(0, 20, 2, 48) == 0, "No workers or no output: nothing");
Check(Names.Byname("starkill", "Troll") == "Troll-bane" && Names.Byname("unlucky") == "the Unlucky" && Names.Creature("Greydwarf_Elite") == "Greydwarf", "Bynames");

// ---- Requests ----
{
    var known = new HashSet<string>(Requests.Trophies.Select(x => x.prefab).Concat(Requests.Goods.Select(x => x.prefab)).Concat(Requests.Relics.Select(x => x.prefab)).Concat(Requests.Creatures.Select(x => x.prefab)));
    int StageOfTarget(Request q) => q.Kind switch
    {
        Ask.Trophy => Requests.Trophies.First(x => x.prefab == q.Target).stage, Ask.Goods => Requests.Goods.First(x => x.prefab == q.Target).stage,
        Ask.Relic => Requests.Relics.First(x => x.prefab == q.Target).stage, Ask.Kill => Requests.Creatures.First(x => x.prefab == q.Target).stage, _ => -1,
    };
    var kinds = new HashSet<Ask>(); bool fair = true, real = true, worded = true, deedEasy = true;
    string[] stories = { "Fell in the snow of a mountain pass in a feud over a horse; talks to ravens.", "Fell in the marsh with a stolen sword; keeps every pretty stone she finds.", "Fell at a river ford; eats standing up." };
    for (int stage = 0; stage <= 6; stage++)
        for (int seed = 0; seed < 400; seed++)
        {
            var qr = new Random(seed * 31 + stage);
            var qv = new VillagerState { Vid = "v", Name = "Ulf", Story = stories[seed % 3], Traits = new List<string> { Tables.Traits[seed % Tables.Traits.Length].Id, Tables.Traits[(seed / 3 + 5) % Tables.Traits.Length].Id } };
            var wf = new WorldFacts { Stage = stage, Exists = n => n != "TrophyDeer" }; // (a prefab this game lacks is never asked for)
            for (int p = 0; p <= 6; p++) wf.Places.Add(new Place { Key = "loc" + p, Label = "place " + p, X = p * 100, Z = 0, Stage = p });
            if (seed % 5 == 0) wf.Avenge.Add(("Hild", "troll"));
            bool deed = seed % 7 == 0;
            Request q = Requests.Generate(qv, wf, 50, qr, deed);
            if (q == null) { real = false; continue; }
            kinds.Add(q.Kind);
            int ts = q.Kind == Ask.Visit ? wf.Places.First(p => p.Key == q.Target).Stage : StageOfTarget(q);
            if (ts > stage) fair = false;
            if (deed && ts > Math.Max(0, stage - 1)) deedEasy = false;
            if (q.Kind != Ask.Visit && (!known.Contains(q.Target) || q.Target == "TrophyDeer")) real = false;
            if (q.Day != 50 || q.Id == "" || q.Deed != deed || Requests.Describe(q) == "" || Requests.Short(q) == "" || q.Why == "" || q.Coins <= 0 || q.Count < 1) worded = false;
            if (q.Kind == Ask.Kill && qv.Has("coward")) fair = false;
        }
    Check(fair, "Every request names something reachable at the world's stage (and no coward is asked to kill)");
    Check(real, "Every request names a thing that exists in this game, and there is always one to make");
    Check(worded, "Every request is dated, worded, explained and rewarded");
    Check(deedEasy, "A disgraced soul's deed is never the hardest thing in the world");
    Check(kinds.Count == 5, "All five kinds of request come up");
    // The great stag (Omens): asked for only while one runs, never of a coward, and only its kill counts.
    var stagged = new WorldFacts { Stage = 0 }; stagged.Stags.Add((120f, -40f));
    var stagAsks = Enumerable.Range(0, 300).Select(i => Requests.Generate(new VillagerState { Traits = new List<string> { "brave" } }, stagged, 1, new Random(i))).Where(q => q != null && q.Target == Requests.GreatStag).ToList();
    Check(stagAsks.Count > 30 && stagAsks.All(q => q.Kind == Ask.Kill && q.X == 120f && q.Z == -40f && Requests.Describe(q) == "Kill the great stag." && Requests.Short(q) == "the great stag killed"), "A brave soul asks for the great stag while one runs, pinned where it was: " + stagAsks.Count);
    Check(Enumerable.Range(0, 300).All(i => Requests.Generate(new VillagerState { Traits = new List<string> { "brave" } }, new WorldFacts { Stage = 0 }, 1, new Random(i))?.Target != Requests.GreatStag), "No great stag running: never asked for");
    Check(Enumerable.Range(0, 300).All(i => Requests.Generate(new VillagerState { Traits = new List<string> { "coward" } }, stagged, 1, new Random(i))?.Target != Requests.GreatStag), "A coward doesn't ask for the stag");
    Check(Requests.KillCounts(stagAsks[0], Requests.GreatStag, 3) && !Requests.KillCounts(stagAsks[0], "Deer", 3), "Only the great stag's own kill counts, not any deer");
    var meadows = new WorldFacts { Stage = 0 };
    bool onlyMeadows = Enumerable.Range(0, 300).Select(i => Requests.Generate(new VillagerState { Traits = new List<string> { "brave" } }, meadows, 1, new Random(i))).All(q => q != null && (q.Kind == Ask.Visit || StageOfTarget(q) == 0));
    Check(onlyMeadows, "A Meadows-stage colony never asks for Mistlands things");
    var nothing = new WorldFacts { Stage = 3, Exists = _ => false };
    Check(Requests.Generate(new VillagerState(), nothing, 1, new Random(1)) == null, "Nothing that exists: no request");
    var avenger = new WorldFacts { Stage = 2 }; avenger.Avenge.Add(("Hild", "Troll"));
    int avenged = Enumerable.Range(0, 200).Count(i => Requests.Generate(new VillagerState(), avenger, 1, new Random(i)) is Request q && q.Kind == Ask.Kill && q.Target == "Troll" && q.Why.Contains("Hild"));
    Check(avenged > 80, "A friend's killer is the likeliest ask: avenge them");
    Check(Requests.Generate(new VillagerState { Traits = new List<string> { "coward" } }, avenger, 1, new Random(3)) is Request cq && cq.Kind != Ask.Kill, "A coward asks someone else to do the killing elsewhere");
    var ask = new Request { Day = 10, Kind = Ask.Kill, Target = "Wolf", Stars = 1 };
    Check(!Requests.Expired(ask, 19) && Requests.Expired(ask, 20) && Requests.DaysLeft(ask, 13) == 7 && Requests.DaysLeft(ask, 40) == 0, "A request lapses after ten days");
    Check(Requests.KillCounts(ask, "Wolf", 2) && Requests.KillCounts(ask, "Wolf", 3) && !Requests.KillCounts(ask, "Wolf", 1) && !Requests.KillCounts(ask, "Fenring", 3), "A kill counts when it's the creature asked for, starred enough");
    Check(Requests.CoinsFromHoard(new Request { Coins = 30 }, 12) == 12 && Requests.CoinsFromHoard(new Request { Coins = 30 }, 100) == 30, "Coins only from what their hoard holds");
    Check(Requests.HomeStage("Fell in the snow of a mountain pass") == 3 && Requests.HomeStage("Fell in the marsh") == 2 && Requests.HomeStage("Fell at a river ford") == 0, "A home land from the backstory");
    Check(Requests.Describe(new Request { Kind = Ask.Kill, Label = "asksvin", Stars = 2 }) == "Kill an asksvin of two stars or more." && Requests.Describe(new Request { Kind = Ask.Trophy, Label = "a wolf trophy", Count = 3 }) == "Bring me three wolf trophies.", "Requests read right");
    Check(Requests.Landmark("Crypt3")?.label == "a burial chamber" && Requests.Landmark("SunkenCrypt4")?.label == "a sunken crypt" && Requests.Landmark("Meadows_Rock") == null, "Landmarks by the game's location names");
    // Saved with the soul; a soul saved before requests loads with none.
    var asker = new VillagerState { Vid = "v9", Name = "Ulf", Asked = new Request { Id = "ab", Kind = Ask.Visit, Target = "Crypt2", Label = "a burial chamber", X = 12.5f, Z = -40, Day = 7, Coins = 20, Why = "why", Deed = true } };
    var rt = VillagerState.Load(asker.Save());
    Check(rt.Asked != null && rt.Asked.Id == "ab" && rt.Asked.Kind == Ask.Visit && rt.Asked.X == 12.5f && rt.Asked.Z == -40 && rt.Asked.Deed && rt.Asked.Coins == 20 && rt.Asked.Day == 7, "A request is saved with the soul");
    Check(VillagerState.Load(new VillagerState { Vid = "v" }.Save()).Asked == null, "No request saved, none loaded");
    byte[] old = new VillagerState { Vid = "v" }.Save(); old = old.Take(old.Length - 1).ToArray(); // (as saved before requests: no flag byte at the end)
    Check(VillagerState.Load(old).Vid == "v" && VillagerState.Load(old).Asked == null, "A soul saved before requests still loads");
}

{
    // Other mods' Apis: bound once, gated by version, never a throw.
    var warned = new List<string>();
    var api = new ApiBinder(typeof(Dummy.NewApi), w => warned.Add(w));
    Check(api.Present && api.Version == 2, "An Api's Version is read");
    Check(api.Bind<Func<int, int>>("Twice")?.Invoke(21) == 42 && api.Bind<Func<string, string>>("Hello")?.Invoke("Bob") == "hi Bob", "Its methods bind to typed delegates");
    Check(ReferenceEquals(api.Bind<Func<int, int>>("Twice"), api.Bind<Func<int, int>>("Twice")), "A method is bound once and kept");
    Check(api.Bind<Func<int, int>>("Twice", 3) == null && warned.Count == 0, "A method from a later version than the mod has: off, quietly");
    Check(api.Bind<Func<int>>("Missing") == null && api.Bind<Func<int>>("Missing") == null && warned.Count == 1, "A missing method is off, and told once");
    Check(api.Bind<Func<string, int>>("Twice") == null && warned.Count == 2, "A method with another shape is off, not miscalled");
    var oldApi = new ApiBinder(typeof(Dummy.OldApi), w => warned.Add(w));
    Check(oldApi.Version == 0 && oldApi.Bind<Func<bool>>("Ping")?.Invoke() == true && oldApi.Bind<Func<bool>>("Ping", 1) == null, "An Api from before Version is version 0");
    var absent = new ApiBinder(null, w => warned.Add(w));
    Check(!absent.Present && absent.Bind<Func<int>>("Anything") == null && warned.Count == 2, "A mod that isn't installed: nothing, and nothing in the log");
    Check(ApiBinder.Usable(1, 1) && !ApiBinder.Usable(0, 1), "Version gating");
}

{
    // Greetings in the world: Old Norse with English under it.
    var gl = Greetings.Lines;
    Check(Enum.GetValues(typeof(Greeting)).Cast<Greeting>().All(k => gl.ContainsKey(k) && gl[k].Length >= 3), "Every situation has at least three lines");
    int total = gl.Values.Sum(a => a.Length);
    Check(total >= 36, "At least forty-odd greeting lines: " + total);
    bool placeholders = gl.Values.SelectMany(a => a).All(l => System.Text.RegularExpressions.Regex.Matches(l.norse + l.english, "\\{[^}]*\\}").Cast<System.Text.RegularExpressions.Match>().All(m => m.Value == "{name}"));
    Check(placeholders, "Only {name} as a placeholder");
    bool fontSafe = Greetings.Letters().All(ch => ch < 128 || Greetings.Glyphs.IndexOf(ch) >= 0);
    Check(fontSafe, "Only letters the game's font has: " + new string(Greetings.Letters().Where(ch => ch >= 128 && Greetings.Glyphs.IndexOf(ch) < 0).ToArray()));
    bool named = Enumerable.Range(0, 200).Select(i => Greetings.Pick(Greeting.Hello, new Random(i), "Bob")).All(l => !l.norse.Contains("{") && !l.english.Contains("{")) && Enumerable.Range(0, 200).Any(i => Greetings.Pick(Greeting.Hello, new Random(i), "Bob").english.Contains("Bob"));
    Check(named, "The player's name goes into the line");
    Check(Greetings.Pick(Greeting.Night, new Random(4), "").english.Contains("{") == false, "No player: no stray placeholder");
    Check(Enumerable.Range(0, 300).Select(i => Greetings.Pick(Greeting.Hello, new Random(i), "Bob", true)).All(l => !l.english.Contains("Good day")) && Enumerable.Range(0, 300).Any(i => Greetings.Pick(Greeting.Hello, new Random(i), "Bob", true).english == "Good evening, jarl."), "Of an evening it's good evening, not good day");
    Check(Greetings.Show(("Heill, jarl!", "Hail, jarl!")) == "<size=75%><i>Heill, jarl!</i></size>\nHail, jarl!", "Old Norse small, English beneath");
}

{
    // Work songs: three or more together, and not a miserable lot.
    Check(!WorkSongs.Sing(2, 50) && WorkSongs.Sing(3, 0) && !WorkSongs.Sing(5, -60), "Three together sing; two don't; a miserable crew works in silence");
    Check(WorkSongs.Letters().All(ch => ch < 128 || Greetings.Glyphs.IndexOf(ch) >= 0), "Songs use only letters the game's font has: " + new string(WorkSongs.Letters().Where(ch => ch >= 128 && Greetings.Glyphs.IndexOf(ch) < 0).ToArray()));
    var chops = Enumerable.Range(0, 200).Select(i => WorkSongs.Pick("chop", new Random(i)).english).ToList();
    Check(chops.Any(l => l.Contains("axe") || l.Contains("oak") || l.Contains("Chop")) && chops.All(l => l != ""), "Felling has its own chants");
    Check(Enumerable.Range(0, 50).All(i => WorkSongs.Any.Contains(WorkSongs.Pick("cook", new Random(i)))), "Other work sings the common chants");
}

{
    // The hall's murmur: as loud as the hall is hFull, quieter and broken up when low, a roar on a feast night, silent when miserable.
    var hEmpty = Soundscape.HallLevel(0, 10, 20, false); var one = Soundscape.HallLevel(1, 10, 20, false);
    var few = Soundscape.HallLevel(3, 10, 20, false); var hFull = Soundscape.HallLevel(10, 10, 20, false);
    Check(hEmpty.Volume == 0 && one.Volume == 0, "An hEmpty hall, or one alone in it, is silent");
    Check(few.Volume > 0 && hFull.Volume > few.Volume && hFull.Volume <= 1, $"A fuller hall is louder ({few.Volume:0.00} < {hFull.Volume:0.00})");
    var low = Soundscape.HallLevel(10, 10, -30, false);
    Check(low.Volume < hFull.Volume && low.Sparse > 0.5f && hFull.Sparse == 0, $"A low colony murmurs quieter and sparser ({low.Volume:0.00}, sparse {low.Sparse:0.00})");
    Check(Soundscape.HallLevel(10, 10, -41, false).Volume == 0 && Soundscape.HallLevel(10, 10, -41, true).Volume == 0 && Soundscape.Miserable(-41) && !Soundscape.Miserable(-40), "Miserable: the hall is quiet, feast or not");
    var roar = Soundscape.HallLevel(10, 10, 20, true);
    Check(roar.Roar && roar.Volume > hFull.Volume && roar.LaughsPerMinute > hFull.LaughsPerMinute, "A feast is a roar, with more laughter");
    Check(Soundscape.HallLevel(10, 10, -30, false).LaughsPerMinute == 0, "No laughing in a low hall");
    Check(Soundscape.HallLevel(3, 12, 20, false).Full < 0.3f && Soundscape.HallLevel(12, 12, 20, false).Full == 1f, "A few in the hall is a small crowd; all of them, a full hall");
}

{
    // The Dimming: 8 and 16 days without a rise in worth; never further; a worthy soul doesn't dim; it ends with any rise.
    Check(Dimming.Stage(20, 13, false) == 0 && Dimming.Stage(20, 12, false) == 1 && Dimming.Stage(20, 5, false) == 1 && Dimming.Stage(20, 4, false) == 2, "Dimming starts at 8 days, deepens at 16");
    Check(Dimming.Stage(500, 0, false) == 2, "Never beyond stage 2");
    Check(Dimming.Stage(500, 0, true) == 0, "A worthy soul doesn't dim");
    Check(Dimming.Stage(20, 20, false) == 0, "A rise in worth today ends it");
    Check(Dimming.Work(0) == 1f && Dimming.Work(1) == 0.9f && Dimming.Work(2) == 0.8f, "A tenth, then a fifth slower at work");
    var skin = (0.8f, 0.6f, 0.5f); var t1 = Dimming.Tint(skin, 1); var t2 = Dimming.Tint(skin, 2);
        float Sat((float, float, float) c) => Math.Max(c.Item1, Math.Max(c.Item2, c.Item3)) - Math.Min(c.Item1, Math.Min(c.Item2, c.Item3));
    foreach (var own in new[] { skin, (0.97f, 0.96f, 0.96f), (0.53f, 0.47f, 0.42f) })
    {
        var d0 = Dimming.Drawn(own); var d1 = Dimming.Drawn(Dimming.Tint(own, 1)); var d2 = Dimming.Drawn(Dimming.Tint(own, 2));
        Check(Dimming.Tint(own, 0) == own && Sat(d1) < Sat(d0) * 0.7f && Sat(d2) < Sat(d0) * 0.3f && Math.Abs(Dimming.Luma(d1) - Dimming.Luma(d0)) < 0.01f && Math.Abs(Dimming.Luma(d2) - Dimming.Luma(d0)) < 0.01f,
            $"The Dimming greys the face as drawn (stage 2 near ashen), no darker; stage 0 is their own skin: {own} -> {d0} {d1} {d2}");
    }
    Check(t1.Item1 / skin.Item1 < t1.Item2 / skin.Item2 && t2.Item1 / skin.Item1 < t2.Item3 / skin.Item3, "The warmth goes first (red drawn out most)");
    var hair = (0.4f, 0.25f, 0.1f);
    Check(Dimming.Hair(hair, 1) == hair && Dimming.Hair(hair, 0) == hair && Dimming.Hair(hair, 2).Item3 > hair.Item3 && Math.Abs(Dimming.Hair(hair, 2).Item1 - Dimming.Hair(hair, 2).Item3) < Math.Abs(hair.Item1 - hair.Item3), "At stage 2 the hair goes toward grey");
    Check(Dimming.Why(30, 18) == "Hasn't done anything worthy in 12 days", "The reason in their words");
    var dv = new VillagerState { Worth = 10 }; float before = dv.Worth; Worth.Gain(dv, "rite"); Check(dv.Worth > before, "The gothi's rite counts as worth");
}

{
    // An ascension's rewards, once: a second burn or a reload mid-way finds it claimed; an ordered one is pending until then.
    var fl = new Dictionary<string, int>();
    Ascensions.Ordered(fl, "v1", 40);
    Check(Ascensions.Pending(fl, "v1") && !Ascensions.Pending(fl, "v2"), "An ordered ascension waits to be finished");
    Check(Ascensions.Claim(fl, "v1") && !Ascensions.Claim(fl, "v1") && !Ascensions.Pending(fl, "v1"), "Rewards once: the second claim finds it taken, nothing pending");
    var reloaded = new Dictionary<string, int>(fl); Check(!Ascensions.Claim(reloaded, "v1"), "After a reload (the same record) still taken");
}

{
    // The memorial: the ascended with their deed and day, the lost who rest with Hel, the lost who walk still; pages of eight.
    var mroster = new List<RosterRow> { new RosterRow { Vid = "a", Name = "Thorstein", State = SoulState.Ascended, Worth = 100 }, new RosterRow { Vid = "b", Name = "Ingrid", State = SoulState.Lost }, new RosterRow { Vid = "c", Name = "Hervor", State = SoulState.Lost }, new RosterRow { Vid = "d", Name = "Ulf", State = SoulState.Active } };
    var mflags = new Dictionary<string, int> { ["ascended:a"] = 40, ["rested:b"] = 90 };
    var mlog = new List<LogEntry> { new LogEntry(0, 40, "ascend", "Thorstein the Bold", "", ""), new LogEntry(1, 40, "text", "", "", "A runestone remembers Thorstein the Bold: Fell at sea. Worth 100, 12 foes slain."), new LogEntry(2, 60, "lost", "Ingrid", "Draugr", ""), new LogEntry(3, 70, "lost", "Hervor", "Troll", ""), new LogEntry(4, 90, "rest", "Ingrid", "Bob", "") };
    var mem = Memorial.From(mroster, mflags, mlog);
    Check(mem.Count == 3 && mem[0].Name == "Thorstein the Bold" && mem[0].Day == 40 && mem[0].Deed.StartsWith("Fell at sea") && !mem[0].Hel, "The ascended: their full name, the day, the deed from their runestone");
    Check(mem.Any(r => r.Name == "Hervor" && r.Unburied && r.Deed.Contains("Troll") && r.Deed.Contains("unburied")), "The lost who walk still");
    Check(mem.Any(r => r.Name == "Ingrid" && r.Hel && !r.Unburied && r.Day == 90 && r.Deed.Contains("by Bob") && r.Deed.Contains("Rests with Hel")), "The lost laid to rest, and by whom");
    Check(mem.All(r => r.Name != "Ulf"), "The living aren't on it");
    var many = Enumerable.Range(0, 35).Select(i => i).ToList();
    Check(Memorial.Pages(35) == 5 && Memorial.Page(many, 0).Count == 8 && Memorial.Page(many, 4).Count == 3 && Memorial.Page(many, 99).SequenceEqual(Memorial.Page(many, 4)) && Memorial.Pages(0) == 1, "Thirty-five names: five pages of eight, the last one short");
    var fl2 = new Dictionary<string, int>(); Ascensions.Claim(fl2, "z", 55); Check(fl2["ascended:z"] == 55, "The ascension's day is kept for the memorial");
}

{
    // They answer back: a losing raid (the hall attacked, or half the fighters down) brings back the worthiest soul not yet returned.
    Check(Returning.Losing(true, 4, 0) && Returning.Losing(false, 4, 2) && !Returning.Losing(false, 4, 1) && !Returning.Losing(false, 0, 0) && Returning.Losing(false, 1, 1), "Losing: the hall attacked, or half the fighters down");
    var souls = new List<(string vid, float worth)> { ("a", 100), ("b", 140), ("c", 120) };
    var ef = new Dictionary<string, int>();
    Check(Returning.Pick(ef, true, souls) == null, "No raid begun: no one comes");
    Returning.Begin(ef);
    Check(Returning.Pick(ef, false, souls) == null, "Not losing: no one comes");
    string first = Returning.Pick(ef, true, souls);
    Check(first == "b", "The worthiest comes first");
    Returning.Mark(ef, first, 30);
    Check(Returning.Pick(ef, true, souls) == null, "One a raid");
    Returning.Begin(ef);
    Check(Returning.Pick(ef, true, souls) == "c" && Returning.Returned(ef, "b") && ef["einherjar:b"] == 30, "Next raid: the next worthiest; never the same soul twice");
    Returning.Mark(ef, "c", 31); Returning.Begin(ef); Returning.Mark(ef, "a", 32); Returning.Begin(ef);
    Check(Returning.Pick(ef, true, souls) == null, "All have come back once: no more");
    string kept = Returning.Gear(new[] { ("SwordIron", 2), ("ShieldBanded", 0), ("bad,name", 1), ("", 1) });
    Check(kept == "SwordIron:2,ShieldBanded:1" && Returning.Worn(kept).SequenceEqual(new[] { ("SwordIron", 2), ("ShieldBanded", 1) }) && Returning.Worn(null).Count == 0, "Their gear kept as a short list, and read back");
}

{
    // The unburied walk again: half the nights, one at a time, the one away longest first, never one already about or laid to rest.
    var ur = new List<RosterRow> { new RosterRow { Vid = "i", Name = "Ingrid", State = SoulState.Lost }, new RosterRow { Vid = "h", Name = "Hervor", State = SoulState.Lost }, new RosterRow { Vid = "u", Name = "Ulf", State = SoulState.Active }, new RosterRow { Vid = "r", Name = "Rested", State = SoulState.Lost } };
    var uf = new Dictionary<string, int> { ["rested:r"] = 5 };
    var noneAbout = new HashSet<string>();
    Check(Unburied.Walker(ur, uf, 10, noneAbout, 0.7) == null, "Not every night");
    string first = Unburied.Walker(ur, uf, 10, noneAbout, 0.1);
    Check(first == "h", "One of the unburied walks (never the living or the rested)");
    Unburied.Walked(uf, first, 10);
    Check(Unburied.Walker(ur, uf, 11, noneAbout, 0.1) == "i", "Next night: the other one");
    Unburied.Walked(uf, "i", 11);
    Check(Unburied.Walker(ur, uf, 11, noneAbout, 0.1) == null, "Each at most every other night");
    Check(Unburied.Walker(ur, uf, 12, new HashSet<string> { "h" }, 0.1) == null && Unburied.Walker(ur, uf, 12, noneAbout, 0.1) == "h", "Not one whose draugr is already about");
    var mem2 = Memorial.From(ur, uf, new List<LogEntry>());
    Check(mem2.Any(m => m.Unburied && m.Deed.Contains("burial ground")), "The stone says how to bring them home");
}

{
    // The colony's year: ten-day seasons from the world's first day without the Seasons mod; its dates with it; a festival mid-season.
    var d0 = Calendar.FromWorldDay(0);
    Check(d0.Year == 1 && d0.Season == Season.Spring && d0.Day == 1 && d0.YearLength == 40, "World day 0: the first of spring, year 1, a 40-day year");
    var d47 = Calendar.FromWorldDay(47);
    Check(d47.Year == 2 && d47.Season == Season.Spring && d47.Day == 8, "Day 47: spring day 8 of year 2");
    Check(Calendar.FromWorldDay(129).Season == Season.Spring && Calendar.FromWorldDay(129).Day == 10 && Calendar.FromWorldDay(130).Season == Season.Summer, "Seasons turn every ten days");
    Check(Calendar.IsDay(Calendar.FromWorldDay(4), Festival.Sigrblot) && Calendar.IsDay(Calendar.FromWorldDay(14), Festival.Midsummer) && Calendar.IsDay(Calendar.FromWorldDay(24), Festival.WinterNights) && Calendar.IsDay(Calendar.FromWorldDay(34), Festival.Yule), "One festival a season, mid-season: Sigrblot, Midsummer, Winter Nights, Yule");
    Check(Calendar.Next(Calendar.FromWorldDay(4)) == (Festival.Sigrblot, 0) && Calendar.Next(Calendar.FromWorldDay(5)) == (Festival.Midsummer, 9) && Calendar.Next(Calendar.FromWorldDay(35)) == (Festival.Sigrblot, 9), "The next festival and the days until it, round the turn of the year");
    Check(Calendar.Preparing(Calendar.FromWorldDay(12), out Festival pf) && pf == Festival.Midsummer && Calendar.Preparing(Calendar.FromWorldDay(13), out _) && !Calendar.Preparing(Calendar.FromWorldDay(14), out _) && !Calendar.Preparing(Calendar.FromWorldDay(11), out _), "Readied in the two days before");
    var odd = Calendar.FromSeasons(1, 7, new[] { 5, 20, 5, 30 }, 130);
    Check(odd.Season == Season.Summer && odd.Day == 7 && odd.YearLength == 60 && odd.Year == 3 && Calendar.DayOf(Festival.Midsummer, odd.Lengths) == 10 && Calendar.DayOf(Festival.Yule, odd.Lengths) == 15, "With Seasons: its own lengths, the festival mid-season in each");
    Check(Calendar.Next(odd) == (Festival.Midsummer, 3), "With Seasons: days to the next festival by its lengths");
    var tiny = Calendar.FromWorldDay(0, 1);
    Check(tiny.YearLength == 4 && Calendar.IsDay(tiny, Festival.Sigrblot), "One-day seasons still work");
    Check(Calendar.Line(Calendar.FromWorldDay(12)) == "Summer, day 3 of 10, year 1. Midsummer in 2 days.", "A line for the window");
}

{
    // Festivals: readied by what's in the hall, held at a quarter or more, a blessing as strong as the readying until the next; let slip below.
    var mid = Festivals.Needs(Festival.Midsummer, 10);
    Check(mid.Any(n => n.kind == "wood" && n.amount == 40) && mid.Any(n => n.kind == "food" && n.amount == 20), "Midsummer wants wood for the bonfire and food by the people");
    Check(Festivals.Readied(mid, k => k == "wood" ? 40 : k == "dandelion" ? 10 : 20) == 1f && Math.Abs(Festivals.Readied(mid, k => k == "wood" ? 20 : 0) - 1f / 6) < 0.001f, "Readied: each want alike, met in part");
    Check(Festivals.Needs(Festival.Sigrblot, 1).First(n => n.kind == "sacrifice").amount == 3 && Festivals.Needs(Festival.Yule, 12).First(n => n.kind == "mead").amount == 12, "Small colonies still want a little; Yule a mead each");
    var ff = new Dictionary<string, int>();
    Check(Festivals.Active(ff).festival == null, "No festival yet: no blessing");
    Festivals.Hold(ff, Festival.Midsummer, 0.6f, 14);
    Check(Festivals.Active(ff) == (Festival.Midsummer, 0.6f) && Math.Abs(Festivals.Of(ff, Festival.Midsummer, Festivals.MidsummerWork) - 0.03f) < 0.0001f && Festivals.Of(ff, Festival.Sigrblot, Festivals.SigrDamage) == 0, "Held: its blessing as strong as the readying, no other's");
    Check(Festivals.Done(ff, 14) && !Festivals.Done(ff, 15), "Once a festival");
    Festivals.Neglect(ff, Festival.WinterNights, 24);
    Check(Festivals.Active(ff).festival == null && Festivals.Neglected(ff, 24) && Festivals.Neglected(ff, 25) && !Festivals.Neglected(ff, 26), "Let slip: the last blessing is gone, two unhappy days");
    Festivals.Hold(ff, Festival.Yule, 1f, 34);
    Check(Festivals.Active(ff) == (Festival.Yule, 1f) && !Festivals.Neglected(ff, 34), "The next one held: its blessing replaces, the neglect is over");
}

{
    // Jarls: a tally per player in the colony's flags, and a byname from it.
    var jf = new Dictionary<string, int>();
    Check(Bynames.Named("Bob", Tally.Of(jf, "Bob")) == "Bob", "No deeds: just the name");
    Tally.Boss(jf, "Bob", "Eikthyr");
    Check(Tally.Get(jf, "Bob", Tally.Bosses) == 1 && Bynames.Named("Bob", Tally.Of(jf, "Bob")) == "Bob Eikthyr-bane", "A boss slain: its bane");
    for (int i = 0; i < 5; i++) Tally.Add(jf, "Bob", Tally.Rings);
    Check(Bynames.Named("Bob", Tally.Of(jf, "Bob")) == "Bob Ring-giver", "Five rings given outrank an early boss");
    Tally.Boss(jf, "Bob", "Bonemass"); Tally.Boss(jf, "Bob", "gd_king");
    Check(Bynames.Named("Bob", Tally.Of(jf, "Bob")) == "Bob Bonemass-bane" && Tally.Get(jf, "Bob", Tally.Bane) == 3 && Tally.Get(jf, "Bob", Tally.Bosses) == 3, "Bonemass and on outrank all; an earlier boss after doesn't lower it");
    Check(Tally.Players(jf).SequenceEqual(new[] { "Bob" }) && Tally.Of(jf, "Quad").Count == 0, "Each player apart");
    Tally.Add(jf, "Quad Tester", Tally.Feasts, 5);
    Check(Tally.Players(jf).Contains("Quad Tester") && Bynames.For(Tally.Of(jf, "Quad Tester")).title == "Feast-giver", "Names with spaces");
    Check(Tally.Home(jf, "Ann", 10) == 0 && Tally.Home(jf, "Ann", 11) == 0 && Tally.Home(jf, "Ann", 40) == 29 && Tally.Get(jf, "Ann", Tally.Away) == 29, "Days away: counted on coming home");
    Check(Bynames.For(Tally.Of(jf, "Ann")).title == "the Absent", "Long away: the Absent");
    for (int d = 41; d <= 45; d++) Tally.Home(jf, "Ann", d);
    Check(Bynames.For(Tally.Of(jf, "Ann")).title == "the Absent", "Still, soon after");
    for (int d = 46; d <= 50; d++) Tally.Home(jf, "Ann", d);
    Check(Bynames.For(Tally.Of(jf, "Ann")).title == "" && Tally.Get(jf, "Ann", Tally.Away) == 29, "Forgotten ten days home; the days away still counted");
    Tally.Add(jf, "Ann", Tally.Raids, 5); Tally.Add(jf, "Ann", Tally.Ascensions, 3);
    Check(Bynames.For(Tally.Of(jf, "Ann")).title == "Shield of the hall", "Raids outrank ascensions");
    Check(Bynames.Tier("Fader") == 7 && Bynames.Tier("Troll") == 0, "Boss tiers");
}

{
    // The skald's verse: the weightiest of the day's deeds, each kind once, 2-4 lines, alliterating, the same for the same seed.
    var day = new List<LogEntry> {
        new LogEntry(1, 5, "kill", "Ulf", "Greyling", ""), new LogEntry(2, 5, "slew", "Bob", "Bonemass", ""), new LogEntry(3, 5, "raidheld", "", "the Fulings", ""),
        new LogEntry(4, 5, "starkill", "Ulf", "Troll", ""), new LogEntry(5, 5, "starkill", "Ase", "Draugr", ""), new LogEntry(6, 5, "ring", "Ulf", "Bob", ""), new LogEntry(7, 5, "payday", "", "", "") };
    var verse = Verse.Make(day, 7, p => p == "Bob" ? "Bob Ring-giver" : p);
    Check(verse.Count == 4 && verse[0].Contains("Bonemass") && verse[0].StartsWith("Bob Ring-giver"), "A boss slain is sung first, by the jarl's byname");
    Check(verse.Count(l => l.Contains("mighty")) == 1 && verse.Any(l => l.Contains("Ase")) && !verse.Any(l => l.Contains("Greyling")), "Each kind once (its latest); plain kills and paydays aren't sung");
    Check(Verse.Make(day, 7, null).SequenceEqual(Verse.Make(day, 7, null)), "Deterministic");
    var one = Verse.Make(new[] { new LogEntry(1, 5, "arrive", "Gudrun", "", "") }, 1);
    Check(one.Count == 2 && one[0].StartsWith("Gudrun"), "One deed: its line and a closing one");
    Check(Verse.Make(new[] { new LogEntry(1, 5, "payday", "", "", "") }, 1).Count == 0, "Nothing worth singing: no verse");
    var vrng = new Random(3);
    Check(Verse.Alliter(vrng, new[] { "bold", "grim", "brave" }, "Bob").StartsWith("b") && Verse.Alliter(vrng, new[] { "bold" }, "Xavier") == "bold", "Alliteration on the name's first sound, else any word");
    Check(Verse.Make(day, 11).All(l => !l.Contains("{") && l.Length > 10), "Lines are filled in");
}

{
    // Coming home by sea at night: within 250 m of the colony's edge, under way, heading in, once a return.
    Check(Return.Detect(0, 300, 0, -4, 0, 0, 80, true, 1000, 0), "Heading in from 220 m off the edge at night");
    Check(!Return.Detect(0, 300, 0, 4, 0, 0, 80, true, 1000, 0), "Heading out: no");
    Check(!Return.Detect(0, 300, 4, 0, 0, 0, 80, true, 1000, 0), "Sailing past: no");
    Check(!Return.Detect(0, 340, 0, -4, 0, 0, 80, true, 1000, 0) && Return.Detect(0, 329, 0, -4, 0, 0, 80, true, 1000, 0), "The 250 m boundary past the edge");
    Check(!Return.Detect(0, 300, 0, -4, 0, 0, 80, false, 1000, 0), "By day: no");
    Check(!Return.Detect(0, 300, 0, -0.5f, 0, 0, 80, true, 1000, 0), "Drifting: no");
    Check(!Return.Detect(0, 300, 0, -4, 0, 0, 80, true, 1000, 800) && Return.Detect(0, 300, 0, -4, 0, 0, 80, true, 1500, 800), "Ten minutes between returns");
    Check(!Return.Detect(0, 30, 0, -4, 0, 0, 80, true, 1000, 0), "Already in: no");
    Check(Return.Night(19) && Return.Night(3) && !Return.Night(12), "Night is dusk to dawn");
}

{
    // Co-jarls compared: only with two bynamed players; the favourite has the last word; close opinions praise both.
    var ring = (2, "Ring-giver", "rings"); var bane = (13, "Bonemass-bane", "bane"); var untitled = (0, "", ""); var gone = (1, "the Absent", "away");
    var cr = new Random(1);
    Check(Bynames.Compare("Bob", ring, 10, "Quad", untitled, 10, cr) == null, "One bynamed jarl: no comparison");
    string favQ = Bynames.Compare("Bob", ring, 0, "Quad", bane, 60, cr);
    Check(favQ != null && favQ.Contains("Bonemass") && favQ.Contains("rings") && favQ.IndexOf("Quad") > favQ.IndexOf("Bob") || favQ.StartsWith("Give me Quad"), "Quad favoured: Quad has the last word (or is asked for first)");
    string even = Bynames.Compare("Bob", ring, 30, "Quad", bane, 35, cr);
    Check(even.Contains("Bob") && even.Contains("Quad") && !even.Contains("but"), "Close: both praised");
    string abs = Bynames.Compare("Bob", gone, 50, "Quad", ring, 0, cr);
    Check(abs == null || abs.Contains("never home") && abs.Contains("At least Quad"), "Even the favourite is no praise when away: the one at home is");
    Check(Bynames.Deed("bane", "Moder-bane") == "brought down Moder", "A bane's deed names the boss");
}

{
    // True legends: the nearest place of a kind the world is ready for, 150 m to 1.5 km off, not told before; the tale says where.
    var places = new List<(string, float, float)> { ("Crypt4", 300, 0), ("SunkenCrypt4", 200, 0), ("MountainCave02", 180, 0), ("TrollCave02", 1400, 0), ("Crypt2", 50, 0), ("Eikthyrnir", 400, 0), ("Crypt3", 2000, 0) };
    Check(TrueLegend.Pick(places, 0, 0, 0, new List<int>()) == 0, "No bosses yet: one stage ahead at most (a burial chamber, not the nearer sunken barrow)");
    Check(TrueLegend.Pick(places, 0, 0, 0, new List<int> { TrueLegend.Pack(300, 0) }) == 3, "A told one isn't told again: the next ready one");
    Check(TrueLegend.Pick(places, 0, 0, 2, new List<int>()) == 2, "Further on, a frost cave may be told");
    Check(TrueLegend.Pick(new List<(string, float, float)> { ("Crypt2", 50, 0), ("Crypt3", 2000, 0), ("Vendor", 300, 0) }, 0, 0, 5, new List<int>()) == -1, "Too near, too far, not a legend: none");
    var pk = TrueLegend.Unpack(TrueLegend.Pack(-1234.4f, 5678.6f));
    Check(pk.x == -1234 && pk.z == 5679, "Positions packed into a flag and back");
    string tale = TrueLegend.Tale(1, 700, "north-west", 1, new Random(2));
    Check(tale.Contains("burial chamber") && tale.Contains("north-west") && tale.Contains("morning") && !tale.Contains("three times"), "The tale: what, where, how far");
    Check(TrueLegend.Tale(1, 700, "west", 3, new Random(2)).Contains("three times"), "The third telling says so");
}

{
    // Odin the Wanderer: firsts in the world are the colony's saga, and renown.
    var wf = new Dictionary<string, int>();
    Check(Wanderer.Saga(wf) == 0 && Wanderer.Points("biome:Meadows") == 0, "Nothing yet; home's own meadows count for nothing");
    wf[Wanderer.Prefix + "biome:Swamp"] = 3; wf[Wanderer.Prefix + "place:Crypt"] = 4; wf[Wanderer.Prefix + "dungeon:SunkenCrypt"] = 4; wf[Wanderer.Prefix + "far:2"] = 5;
    Check(Wanderer.Saga(wf) == 6 + 2 + 5 + 4, "Each first's worth summed");
    Check(Wanderer.Points("place:NotALandmark") == 0 && Wanderer.Points("far:3") == 0, "Only landmarks and the milestones count");
    Check(Wanderer.Far(900) == 0 && Wanderer.Far(2500) == 2 && Wanderer.Far(9000) == 8, "Milestones by distance");
    Check(Wanderer.Tell("biome:BlackForest", "Bob") == "Bob was the first of the hall to set foot in the Black Forest." && Wanderer.Tell("dungeon:Crypt4", "Ann").Contains("burial chamber"), "Told in the saga");
}

{
    // The verse sings the Einherjar, the barrow jarl, the beacon and the hall's champions too; the saga keeps their words as written.
    var ev = new List<LogEntry> { new LogEntry(1, 5, "einherjar", "Ingrid", "", "In the colony's need, Ingrid came back from Valhalla to fight."), new LogEntry(2, 5, "barrowjarl", "", "Draugr jarl Hrolf", "x"),
        new LogEntry(3, 5, "beacon", "the watch", "Bob", "y"), new LogEntry(4, 5, "champion", "Ottar", "hnefatafl", "z") };
    var vv = Verse.Make(ev, 3, p => p == "Bob" ? "Bob Ring-giver" : p);
    Check(vv.Count == 4 && vv[0].Contains("Hrolf") && vv[1].StartsWith("Ingrid") && vv.Any(l => l.Contains("beacon for Bob Ring-giver")) && vv.Any(l => l.Contains("no match at hnefatafl")), "Each sung, the weightiest first");
    Check(Stories.Line(ev[0]) == "In the colony's need, Ingrid came back from Valhalla to fight.", "The saga's words unchanged");
}

{
    // The Thing hears of a dark homecoming and of the unburied with nowhere to rest.
    var cv = new ColonyView { Population = 5, HasBeautyRoom = true, HasShrine = true, GuardWage = 9, Beds = 5, DarkHome = true, UnburiedNoGround = true };
    var raised = Petitions.Raise(new Random(1), cv, 10);
    Check(raised.Any(p => p.Id == "beacon") && raised.Any(p => p.Id == "burial"), "A beacon and a burial ground asked for");
    Check(!Petitions.Raise(new Random(1), new ColonyView { Population = 5, HasBeautyRoom = true, HasShrine = true, GuardWage = 9, Beds = 5 }, 10).Any(p => p.Id == "beacon" || p.Id == "burial"), "Not when there's no need");
}

// ---- Fields: the crop chosen per season ----
{
    var cf = new Dictionary<string, int>();
    var crops = new List<string> { "sapling_barley", "sapling_carrot", "sapling_flax" };
    Check(CropChoice.Chosen(cf, "z1", Season.Summer, crops) == null, "Nothing chosen: anything");
    CropChoice.Set(cf, "z1", Season.Summer, "sapling_carrot");
    CropChoice.Set(cf, "z2", Season.Summer, "sapling_flax");
    Check(CropChoice.Chosen(cf, "z1", Season.Summer, crops) == "sapling_carrot" && CropChoice.Chosen(cf, "z1", Season.Winter, crops) == null, "A crop for one season only");
    Check(CropChoice.Chosen(cf, "z1", Season.Summer, new[] { "sapling_barley" }) == null, "A crop no longer known (its mod gone): anything");
    Check(CropChoice.Hash("sapling_carrot") == CropChoice.Hash("sapling_carrot") && CropChoice.Hash("sapling_carrot") != CropChoice.Hash("sapling_turnip") && CropChoice.Hash("") != 0, "A stable hash, never 0");
    Check(CropChoice.Cycle(crops, null, 1) == "sapling_barley" && CropChoice.Cycle(crops, "sapling_flax", 1) == null && CropChoice.Cycle(crops, null, -1) == "sapling_flax" && CropChoice.Cycle(crops, "sapling_barley", -1) == null, "Cycling through the crops and back to anything");
    Check(CropChoice.Cycle(crops, "sapling_gone", 1) == "sapling_barley", "Cycling from a crop no longer known starts over");
    CropChoice.Set(cf, "z1", Season.Summer, null);
    Check(!cf.ContainsKey(CropChoice.Key("z1", Season.Summer)), "Anything again: the flag gone");
    CropChoice.Set(cf, "z1", Season.Spring, "sapling_barley");
    CropChoice.Forget(cf, "z1");
    Check(cf.Count == 1 && cf.ContainsKey(CropChoice.Key("z2", Season.Summer)), "A zone removed: its choices only");
}

// ---- Quad's Arena: wagers on the jarl, and the Champion ----
{
    var wrng = new Random(3);
    Check(Wagers.Stake(5, 50, wrng) == 0, "Too little hoard to wager");
    for (int i = 0; i < 30; i++) { int st = Wagers.Stake(200, 30, wrng); Check(st >= 8 && st <= 20, "A tenth of the hoard at most, on the jarl: " + st); }
    Check(Wagers.Stake(200, -40, wrng) < 0, "One who thinks little of the jarl wagers against");
    Check(Wagers.Settle(10, true) == 10 && Wagers.Settle(10, false) == -10 && Wagers.Settle(-10, true) == -10 && Wagers.Settle(-10, false) == 10 && Wagers.Settle(0, true) == 0, "Even money, either way");
    string deed = Wagers.Pack("champion", "win", true, 340, new[] { "v1", "v2" });
    Check(Wagers.Unpack(deed, out string k, out string o, out bool cr, out int co, out var set) && k == "champion" && o == "win" && cr && co == 340 && set.SetEquals(new[] { "v1", "v2" }), "The fight carried home, read back: " + deed);
    Check(!deed.Contains(";") && Wagers.Unpack(Wagers.Pack("road", "died", false, 0, null), out _, out string o2, out _, out _, out var wnone) && o2 == "died" && wnone.Count == 0, "A loss carried home, wnone wagered in the stands");
    var wfolk = Enumerable.Range(0, 10).Select(i => ($"v{i}", i * 20, 0)).ToList();
    var bets = Wagers.Bettors(wfolk, new HashSet<string> { "v9" }, new Random(1));
    Check(bets.Count == Wagers.MaxBettors && !bets.Any(b => b.vid == "v9") && bets[0].vid == "v8" && !bets.Any(b => b.vid == "v0"), "The richest few wager, not those who did in the stands, nor the penniless");
    Check(Wagers.Kind("road") == "the Long Road" && Wagers.Ended("died") == "fell in the ring", "The arena's words");
    var af = new Dictionary<string, int>();
    Tally.Add(af, "Bob", Tally.ArenaWins, 2);
    Check(Bynames.For(Tally.Of(af, "Bob")).id == 0, "Two wins aren't a name yet");
    Tally.Add(af, "Bob", Tally.ArenaWins);
    Check(Bynames.Named("Bob", Tally.Of(af, "Bob")) == "Bob the Champion", "Three contests won: the Champion");
    var cf = new Dictionary<string, int>(); Tally.Add(cf, "Bob", Tally.Crowned); Tally.Add(cf, "Bob", Tally.Rings, 5);
    Check(Bynames.For(Tally.Of(cf, "Bob")).title == "the Champion", "In the Hall of Champions: outranks Ring-giver");
    Tally.Set(cf, "Bob", Tally.Bane, 4);
    Check(Bynames.For(Tally.Of(cf, "Bob")).title == "Moder-bane", "A great boss slain still comes first");
    var aday = new List<LogEntry> { new LogEntry(10, 5, "arenawin", "Bob", "a Champion Bout", "Bob won a Champion Bout in the arena.") };
    Check(Verse.Make(aday, 1, p => p).Any(l => l.Contains("Bob")), "The skald sings an arena win");
    Check(Stories.Gossip(new Random(1), "Astrid", aday, 12) == null || Stories.Gossip(new Random(1), "Astrid", aday, 12).Contains("Champion Bout"), "Gossip tells it");
}

// ---- Great Works: the Lighthouse ----
{
    var parts = GreatWorks.LighthouseParts();
    Check(GreatWorks.Stairwell(-0.5f, -1.5f) && GreatWorks.Stairwell(0.5f, -0.5f) && !GreatWorks.Stairwell(0.5f, 0.5f) && !GreatWorks.Stairwell(-0.5f, 1.5f), "The stairwell: open over the landing and the ladders' tops, floor where they come out");
    Check(parts.Count(p => p.Prefab == "bonfire") == 1 && parts.Count(p => p.Prefab == "wood_door") == 1 && parts.Count(p => p.Prefab == "wood_stepladder") == 10 && GreatWorks.FireHeight(GreatWorks.Lighthouse) > 23.5f, "The Lighthouse: a door, ten ladders up, the bonfire 24 m up: " + parts.Count + " parts");
    var fireDeck = parts.Where(p => p.Prefab == "wood_floor" && p.Y > 14).ToList(); var gallery = parts.Where(p => p.Prefab == "wood_floor_1x1").Max(p => p.Y);
    Check(fireDeck.Count == 8 && fireDeck.All(p => p.Y - gallery > 2.9f), "The bonfire's deck: 6 m square round the bonfire, 3 m of head room over the gallery");
    Check(parts.Count(p => p.Prefab.StartsWith("stone_")) >= 16 && parts.Where(p => p.Prefab.StartsWith("stone_")).All(p => p.Y < 2f), "A stone plinth and course under the timber");
        double Reach(GreatWorks.Part p, double x, double z) => Math.Sqrt(Math.Pow(p.X - x, 2) + p.Y * p.Y + Math.Pow(p.Z - z, 2));
    Check(parts.Where(p => p.Prefab == "woodiron_pole").All(p => Reach(p, 0, -3.3) < 20), "Every iron-banded pole within the forge's reach");
    var cores = parts.Where(p => p.Prefab == "woodiron_pole").ToList();
    Check(Enumerable.Range(0, 5).All(s => cores.Count(p => Math.Abs(p.Y - (0.25 + 4 * s + 1)) < 0.05) == 4 && cores.Count(p => Math.Abs(p.Y - (0.25 + 4 * s + 3)) < 0.05) == 4), "An iron core in each corner, every storey, from the plinth to the gallery");
    Check(parts.Count(p => p.Prefab.StartsWith("piece_workbench_ext")) == 2 && parts.Where(p => !p.Prefab.StartsWith("stone_") && !p.Prefab.StartsWith("piece_") && p.Prefab != "bonfire" && p.Prefab != "hearth").All(p => Reach(p, 3.1, 0) + 3.5 < 28), "Every timber piece within its workbench's reach (two extensions: 28 m), with room to spare");
    Check(parts.Where(p => p.Prefab.StartsWith("stone_")).All(p => Reach(p, -3.05, 0) < 18), "Every stone piece within the stonecutter's reach");
    var backs = parts.Where(p => p.Prefab == "wood_wall_log_4x0.5" && p.Yaw == 0 && p.Z < -1.9f && p.Y > 5).ToList();
    Check(backs.Count == 8, "A log across the back of every trestle ring, outside the legs: " + backs.Count);
    Check(parts.Count(p => p.Prefab == "wood_dragon1") == 4 && parts.Count(p => p.Prefab == "piece_banner03") == 5, "Dragon heads on the fire deck, banners on the gallery and by the door");
    var legs = parts.Where(p => p.Leans).ToList();
    Check(legs.Count(p => p.Prefab == "wood_pole_log_4") == 16 && legs.Count(p => p.Prefab == "woodiron_pole") == 32 && legs.All(p => Math.Abs(Math.Abs(p.UpX) - (p.Prefab == "woodiron_pole" ? 0.0625f : 0.125f)) < 0.01f && Math.Sign(p.UpX) == -Math.Sign(p.X)), "Its trestle legs lean inward, an eighth of a metre a storey, four storeys");
    var spots = parts.Select(p => (p.Prefab, Math.Round(p.X, 2), Math.Round(p.Y, 2), Math.Round(p.Z, 2))).ToList();
    Check(spots.Distinct().Count() == spots.Count, "No two parts in the same place");
    // the way up: each ladder's top meets a floor or a landing, and nothing hangs over a ladder within head height
    var ladders = parts.Where(p => p.Prefab == "wood_stepladder").ToList();
    var floors = parts.Where(p => p.Prefab == "wood_floor_1x1").ToList();
    foreach (var l in ladders)
    {
        double dir = l.Yaw == 180 ? 1 : -1, topZ = l.Z + dir;
        Check(floors.Any(f => Math.Abs(f.X - l.X) < 0.01 && Math.Abs(f.Y + 0.08 - (l.Y + 2)) < 0.05 && Math.Abs(f.Z - (topZ + dir * 0.5)) < 0.01), $"A floor at the top of the ladder at {l.X},{l.Y},{l.Z}");
        // each metre of the ladder: where one stands on it, and 2.4 m clear over that (a player stands about 2 m)
        foreach (double cz in new[] { l.Z - 0.5, l.Z + 0.5 })
        {
            double stand = l.Y + 1 + (cz - l.Z) * dir + 0.5; // (the step's height at the high side of that metre)
            Check(!floors.Any(f => Math.Abs(f.X - l.X) < 0.01 && Math.Abs(f.Z - cz) < 0.01 && f.Y > stand - 0.3 && f.Y < stand + 2.4), $"Head room over the ladder at {l.X},{l.Y},{cz}");
        }
    }
    foreach (var land in floors.Where(f => Math.Abs(f.Z + 1.5) < 0.01 && Math.Abs(f.X) < 1 && floors.Count(g => Math.Abs(g.X - f.X) < 0.01 && Math.Abs(g.Z - f.Z) < 0.01) > 0 && (f.Y + 0.08) % 4 > 1.5))
    {
        Check(!floors.Any(f => Math.Abs(f.X - land.X) < 0.01 && Math.Abs(f.Z - land.Z) < 0.01 && f.Y > land.Y + 0.3 && f.Y < land.Y + 2.4), $"Head room over the landing at {land.X},{land.Y}");
        Check(!parts.Any(p => p.Prefab.StartsWith("wood_wall_log") && p.Yaw == 0 && Math.Abs(p.Z - land.Z) < 0.3 && p.Y > land.Y && p.Y - 0.27 < land.Y + 2.4), $"No log across the landing at {land.X},{land.Y}");
    }
    var needs = GreatWorks.Needs(GreatWorks.Lighthouse);
    Check(needs["Stone"] > 50 && needs["Iron"] == 42 && needs["SurtlingCore"] == 1, "Its cost: " + GreatWorks.NeedsText(GreatWorks.Lighthouse));
    var front = new GreatWorks.Part("stone_wall_4x2", 0, 1, 2, 0);
    var turned = GreatWorks.Place(front, 100, 30, 200, 90);
    Check(Math.Abs(turned.x - 102) < 0.01f && Math.Abs(turned.z - 200) < 0.01f && Math.Abs(turned.y - 31) < 0.01f && Math.Abs(turned.yaw - 90) < 0.01f, "A part turned with the work: its front wall faces where the work faces");
    var gview = new ColonyView { Population = 6, HasBeautyRoom = true, HasShrine = true, GuardWage = 9, Beds = 6, GreatWork = true };
    Check(Petitions.Raise(new Random(1), gview, 10).Any(p => p.Id == "greatwork" && p.About == GreatWorks.Lighthouse), "The Thing may ask for a lighthouse");
    gview.GreatWork = false;
    Check(!Petitions.Raise(new Random(1), gview, 10).Any(p => p.Id == "greatwork"), "Not once one's begun, or away from the sea");
    var gf = new Dictionary<string, int> { [GreatWorks.Key(GreatWorks.Lighthouse)] = GreatWorks.Laid };
    Check(GreatWorks.Stage(gf, GreatWorks.Lighthouse) == GreatWorks.Laid && GreatWorks.Proper(GreatWorks.Lighthouse, "Shithole") == "Lighthouse of Shithole", "Its stage and its name on the map");
    var site = new List<float> { 8, 8, 20, 8, 20, 20, 8, 20 };
    Check(GreatWorks.Site(site, out float sx, out float sz) == "" && Math.Abs(sx - 14) < 0.01f && Math.Abs(sz - 14) < 0.01f, "A work stands in the middle of the outline drawn for it");
    Check(GreatWorks.Site(new List<float> { 0, 0, 6, 0, 6, 6, 0, 6 }, out _, out _).Contains("too small") && GreatWorks.Site(new List<float> { 0, 0, 4, 0 }, out _, out _) != "", "Not in an outline too small for its footing, or not an outline");
    Check(Verse.Make(new List<LogEntry> { new LogEntry(5, 3, "greatwork", "Shithole", "the Lighthouse", "") }, 2, p => p).Any(l => l.Contains("Lighthouse")), "The skald sings it");
}

// ---- Great Works: the Peak Temple ----
{
    var parts = GreatWorks.TempleParts("thor");
    Check(parts.Count(p => p.Prefab == "JarldomAltar_thor") == 1 && GreatWorks.TempleParts().Count(p => p.Prefab == "JarldomAltar_odin") == 1, "The Peak Temple's altar is its god's (Odin's, before one's chosen)");
    Check(parts.All(p => Math.Abs(p.X) <= GreatWorks.FootingOf(GreatWorks.PeakTemple) && Math.Abs(p.Z) <= GreatWorks.FootingOf(GreatWorks.PeakTemple) || p.Prefab == "wood_dragon1"), "Everything but the dragons' heads on its footing");
    var spots = parts.Select(p => (p.Prefab, Math.Round(p.X, 2), Math.Round(p.Y, 2), Math.Round(p.Z, 2))).ToList();
    Check(spots.Distinct().Count() == spots.Count, "No two of its parts in the same place");
    Check(parts.Count(p => p.Prefab == "wood_door") == 1 && parts.Count(p => p.Prefab == "wood_roof_67") == 20 && parts.Count(p => p.Prefab == "wood_roof_top_67") == 5 && parts.Count(p => p.Prefab == "wood_dragon1") == 2, "A door, the steepest roof two rows a side, a capped ridge, dragons at its ends: " + parts.Count + " parts");
    var roofs = parts.Where(p => p.Prefab == "wood_roof_67").ToList();
    Check(roofs.All(p => (p.X > 0) == (p.Yaw == 90)) && roofs.Where(p => Math.Abs(p.X) > 2).All(p => Math.Abs(p.Y - 1.27) < 0.01 && Math.Abs(Math.Abs(p.X) + 1.06 - GreatWorks.TempleHalf) < 0.01), "Each side's roof falls outward, its foot on the plinth at the hall's edge");
    Check(roofs.Where(p => Math.Abs(p.X) < 2).All(p => Math.Abs(p.Y - 1.02 - (0.25 + 4)) < 0.01), "Its upper row starting where the lower one ends, 4 m up, up to the ridge 8 m up");
    var gableBits = parts.Where(p => (p.Prefab == "woodwall" || p.Prefab == "wood_wall_roof_67_a") && Math.Abs(Math.Abs(p.Z) - GreatWorks.TempleEnd) < 0.01).ToList();
    Check(gableBits.All(p => p.Prefab != "woodwall" || p.Y + 1 - 0.25 <= GreatWorks.TempleRidge - 2 * (Math.Abs(p.X) + 1) + 0.01), "No plank of its gables pokes out through the roof");
    var needs = GreatWorks.Needs(GreatWorks.PeakTemple);
    Check(needs["Stone"] > 200 && needs["Wood"] > 50 && needs["Coins"] == 10 && needs["Iron"] == 2, "What it costs (stone and timber, the altar's coins, the stonecutter's 2 iron): " + GreatWorks.NeedsText(GreatWorks.PeakTemple));
    Check(GreatWorks.Site(GreatWorks.PeakTemple, new List<float> { 0, 0, 10, 0, 10, 10, 0, 10 }, out _, out _).Contains("12 × 12") && GreatWorks.Site(GreatWorks.PeakTemple, new List<float> { 0, 0, 14, 0, 14, 14, 0, 14 }, out _, out _) == "", "Its outline must hold 12 × 12 m");
    // (a far peak: a hill 1000 m north-east of the high seat, its top 120 m up; another higher one beyond reach)
    float Hill(float x, float z, float cx, float cz, float top, float r) { float d = (float)Math.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz)); return Math.Max(0, top * (1 - d / r)); }
    var peak = GreatWorks.HighestNear((x, z) => Math.Max(Hill(x, z, 707, 707, 120, 300), Hill(x, z, 2500, 0, 400, 300)), 0, 0, 1500, 24, 60);
    Check(peak.HasValue && Math.Abs(peak.Value.x - 707) < 4 && Math.Abs(peak.Value.z - 707) < 4 && peak.Value.h > 115, $"The far peak is the hill's top, not the higher one out of reach: {peak}");
    Check(GreatWorks.HighestNear((x, z) => 20f, 0, 0, 1500, 24, 60) == null, "No peak where nothing stands above the floor");
    // (island outposts: two hills in the sea, joined by a ridge or not; a ford shallow enough to wade)
    float Isle(float x, float z, float cx, float cz) => 40f - (float)Math.Sqrt((x - cx) * (x - cx) + (z - cz) * (z - cz)) / 10f;
    Func<float, float, float> apart = (x, z) => Math.Max(Isle(x, z, 0, 0), Isle(x, z, 1200, 0));
    Func<float, float, float> joined = (x, z) => Math.Max(apart(x, z), Math.Abs(z) < 30 ? 35f : 0f);
    Func<float, float, float> ford = (x, z) => Math.Max(apart(x, z), Math.Abs(z) < 30 ? 29.7f : 0f);
    Check(Island.Connected(apart, 30, 0, 0, 1200, 0) == false, "Two islands: no way over land");
    Check(Island.Connected(joined, 30, 0, 0, 1200, 0) == true, "A ridge between them: joined");
    Check(Island.Connected(ford, 30, 0, 0, 1200, 0) == true, "Water a hand deep between them: waded");
    Check(Island.Connected(apart, 30, 0, 0, 90000, 0) == null, "Too far apart to tell");
    Check(GreatWorks.SnapYaw(35) == 45 && GreatWorks.SnapYaw(326.5f) == 337.5f && GreatWorks.SnapYaw(-10) == 0 && GreatWorks.SnapYaw(-20) == 337.5f && GreatWorks.SnapYaw(359) == 0 && GreatWorks.SnapYaw(11) == 0 && GreatWorks.SnapYaw(12) == 22.5f, "A great work faces on the game's 22.5° steps");
    var tview = new ColonyView { Population = 6, HasBeautyRoom = true, HasShrine = true, GuardWage = 9, Beds = 6, TempleGod = "freyja", TempleGodName = "Freyja" };
    var asked = Petitions.Raise(new Random(1), tview, 10).FirstOrDefault(p => p.Id == "greatwork");
    Check(asked != null && asked.About == "peaktemple|freyja" && asked.Text.Contains("Freyja"), "The Thing may ask for a temple to the best-pleased god");
}

// ---- Expectations rise: the fare a colony of standing wants ----
{
    Check(Fare.Of("CookedMeat", 30) == 0 && Fare.Of("Sausages", 55) == 2 && Fare.Of("LoxPie", 75) == 4 && Fare.Of("MeatPlatter", 80) == 5, "Dishes by their land");
    Check(Fare.Of("SomeModStew", 20) == 0 && Fare.Of("SomeModStew", 60) == 4 && Fare.Of("SomeModStew", 90) == 6, "Another mod's dish by how much it feeds");
    Check(Fare.Opened(k => false) == 0 && Fare.Opened(k => k == "defeated_eikthyr" || k == "defeated_gdking") == 2, "The lands open as the bosses fall");
    Check(Fare.Wanted(Rank.Camp, 6) == 0 && Fare.Wanted(Rank.Homestead, 6) == 0 && Fare.Wanted(Rank.Hamlet, 6) == 1 && Fare.Wanted(Rank.Village, 6) == 2 && Fare.Wanted(Rank.Jarldom, 6) == 4 && Fare.Wanted(Rank.Hold, 6) == 5, "Each rank wants more");
    Check(Fare.Wanted(Rank.Hold, 2) == 2 && Fare.Wanted(Rank.Jarldom, 0) == 0, "Never past what the world has opened");
    Check(Fare.Met(new List<int> { 0, 0, 0, 0, 0, 0, 0, 0 }, 0), "Nothing wanted: met");
    Check(Fare.Met(new List<int> { 0, 0 }, 3), "Too few meals to tell: met");
    Check(!Fare.Met(new List<int> { 1, 1, 1, 1, 1, 1, 1, 1 }, 2) && Fare.Met(new List<int> { 1, 1, 1, 2, 1, 1, 3, 1 }, 2), "Now and then: one meal in four of the tier");
    Check(!Fare.Met(new List<int> { 3, 3, 0, 0, 0, 0, 0, 0, 0, 0 }, 3), "Only the last eight meals count");
    Check(Fare.Filling(4, 4) == 1f && Fare.Filling(5, 4) == 1f && Math.Abs(Fare.Filling(2, 4) - 0.8f) < 0.001f && Fare.Filling(0, 6) == 0.7f, "Plain fare fills a little less, never under 70%");
    Check(Fare.Wants(4).Contains("Plains") && Fare.Wants(4).Contains("bread") && Fare.Lands.Length == Fare.Examples.Length && Fare.Lands.Length == Fare.Keys.Length + 1, "It names the land and its dishes");
}

// ---- The land wears thin ----
{
    Check(Worn.Yield(0) == 1f && Worn.Yield(Worn.Threshold) == 1f && Worn.Yield(Worn.Full) == Worn.Least && Worn.Yield(10000) == Worn.Least, "Full yields up to the threshold, never below 0.7");
    Check(Worn.Yield((Worn.Threshold + Worn.Full) / 2) is float mid && mid < 1f && mid > Worn.Least, "Falling evenly between");
    var zones = new Dictionary<(int, int), Worn.Zone>(); var at = Worn.ZoneOf(100, -10);
    Check(at == (1, -1), "Zones of 64 m, by where the use was");
    for (int i = 0; i < 200; i++) Worn.Use(zones, at, "chop", 5.0 + i * 0.001);
    Check(Worn.YieldAt(zones, at, 5.2) == Worn.Least, "Two hundred trees felled in a zone in a morning: it yields 70%");
    Check(Worn.YieldAt(zones, at, 5.2 + Worn.RecoverDays) == 1f && Worn.Left(zones[at], 5.2 + Worn.RecoverDays) == 0, "Ten idle days and it's whole again");
    Check(Worn.YieldAt(zones, at, 5.2 + Worn.RecoverDays / 2) > Worn.Least, "Half way, half recovered");
    Check(Worn.YieldAt(zones, (9, 9), 5) == 1f, "A zone never used yields fully");
    var picks = new Dictionary<(int, int), Worn.Zone>(); for (int i = 0; i < 160; i++) Worn.Use(picks, at, "pick", 1);
    Check(Math.Abs(picks[at].Count - 40) < 0.01f && Worn.YieldAt(picks, at, 1) == 1f, "A wild plant picked weighs a quarter of a tree");
    // (steady use: a woodcutter's ten trees a day in one zone settles where yields are down a fifth, not at the floor)
    var steady = new Dictionary<(int, int), Worn.Zone>(); for (int d = 0; d < 60; d++) for (int t = 0; t < 10; t++) Worn.Use(steady, at, "chop", d + t * 0.05);
    float y = Worn.YieldAt(steady, at, 60);
    Check(y > 0.75f && y < 0.9f, $"Ten trees a day for two months settles at {y:0.00}: pressure, not ruin");
    var kept = Worn.Load(Worn.Save(zones, 6));
    Check(kept.Count == 1 && Math.Abs(kept[at].Count - zones[at].Count) < 0.01f && Worn.Save(zones, 5.2 + Worn.RecoverDays + 1) == "", "Kept compactly; recovered zones dropped");
    Check(Worn.Load("junk;1,2,x,3;").Count == 0, "Bad record entries are skipped");
}

// ---- The scattered reborn ----
{
    Check(!Scatter.Rolls(Rank.Homestead, 0.01) && Scatter.Rolls(Rank.Hamlet, 0.19) && !Scatter.Rolls(Rank.Hold, 0.2), "One arrival in five, once a hamlet");
    var places = new List<(string, float, float, string)> { ("WoodHouse3", 500, 0, "Meadows"), ("Ruin1", 300, 0, "Meadows"), ("StoneTower1", 0, 900, "BlackForest"), ("ShipWreck01", 1600, 0, "Meadows"), ("Crypt2", 600, 0, "BlackForest"), ("WoodVillage1", 0, -700, "Swamp"), ("Vendor_BlackForest", 0, 800, "BlackForest") };
    var srng = new Random(3); var seen = new HashSet<string>();
    for (int i = 0; i < 200; i++) { var site = Scatter.Site(places, 0, 0, 1, srng); if (site != null) seen.Add(site.Value.prefab); }
    Check(seen.SetEquals(new[] { "WoodHouse3", "StoneTower1" }), "Only shelters 400-1500 m away, in an opened land (" + string.Join(", ", seen) + ")");
    Check(Scatter.Site(places, 0, 0, 0, new Random(1))?.prefab == "WoodHouse3", "Nothing opened: the Meadows only");
    Check(Scatter.Site(new List<(string, float, float, string)> { ("Ruin1", 50, 0, "Meadows") }, 0, 0, 6, new Random(1)) == null, "Nowhere fit: none");
    Check(!Scatter.Expired(10, 12.9) && Scatter.Expired(10, 13) && Math.Abs(Scatter.DaysLeft(10, 11.5) - 1.5) < 1e-9, "They hold out three days");
    var sw = Scatter.Load(Scatter.Save(new Scatter.Waiting { Vid = "abc", X = 512.3f, Z = -77.1f, Day = 41.25 }));
    Check(sw != null && sw.Vid == "abc" && Math.Abs(sw.X - 512.3f) < 0.06f && Math.Abs(sw.Day - 41.25) < 1e-6 && Scatter.Load("") == null && Scatter.Load("a,b,c,d") == null, "Kept in the record and read back");
}

// ---- Prefab names: a save finds a thing again by its prefab's name; none shipped may go ----
{
    var golden = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "prefabs.txt")).Select(l => l.Trim()).Where(l => l != "").ToList();
    var shipped = PrefabNames.Shipped.ToList();
    var gone = golden.Except(shipped).ToList();
    Check(gone.Count == 0, "Every prefab name ever shipped is still in Data/PrefabNames.cs (gone: " + string.Join(", ", gone) + "): put it back; a renamed or dropped prefab is lost from every save");
    Check(shipped.Distinct().Count() == shipped.Count, "No prefab name twice in Data/PrefabNames.cs");
    var unlisted = shipped.Except(golden).ToList();
    Check(unlisted.Count == 0, "New prefab names are added to tests/Jarldom.Tests/prefabs.txt too (append: " + string.Join(", ", unlisted) + ")");
}

Console.WriteLine($"Jarldom: {checks} checks passed");
