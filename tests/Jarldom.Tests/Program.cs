using Jarldom;

int checks = 0;
void Check(bool ok, string name) { checks++; if (!ok) throw new Exception(name); }

// ---- Tables ----
Check(Tables.Professions.Length == Enum.GetValues<Prof>().Length && Tables.Professions.Select((p, i) => (int)p.Id == i).All(x => x), "Professions in enum order");
Check(Tables.Rooms.Select((r, i) => (int)r.Id == i).All(x => x) && Tables.Rooms.Length == Enum.GetValues<RoomType>().Length, "Rooms in enum order");
Check(Tables.Zones.Select((z, i) => (int)z.Id == i).All(x => x), "Zones in enum order");
Check(Tables.Ranks.Select((r, i) => (int)r.Id == i).All(x => x), "Ranks in order");
Check(Tables.Ranks.Zip(Tables.Ranks.Skip(1)).All(p => p.First.Population < p.Second.Population && p.First.Cap < p.Second.Cap), "Ranks grow");
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
Check(Renown.Threshold(0) == 20 && Renown.Threshold(1) == 25 && Renown.Threshold(2) == 31 && Renown.Threshold(10) > 180, "Arrival thresholds grow by a quarter");
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
var vs = Souls.Become(grp[0], "vx", "c1", 10); Check(vs.Traits.Count == 2 && vs.Prof == Prof.Hauler && vs.Wants == grp[0].Prof && Mood.Has(vs, "arrived", 10), "Becoming a villager");
var log = new List<LogEntry> { new LogEntry(0, 0, "found", "Bob", "", ""), new LogEntry(10, 0, "arrive", "Ulf", "", ""), new LogEntry(20, 0, "kill", "Ulf", "Neck", ""), new LogEntry(1900, 1, "starkill", "Ulf", "Troll", "") };
var saga = Stories.Chronicle(log);
Check(saga.Count == 2 && saga[0].StartsWith("Day 0:") && saga[0].Contains("Ulf came") && saga[0].Contains("One foe") && saga[1].Contains("mighty Troll"), "The chronicle tells the days");
for (int i = 0; i < 50; i++)
{
    string gossip = Stories.Gossip(new Random(i), "Astrid", log, 2000);
    Check(gossip == null || gossip.StartsWith("Astrid") && (gossip.Contains("Ulf") || gossip.Contains("Bob")), "Gossip is about logged events");
}
Check(Stories.Gossip(new Random(1), "Astrid", log, 999999) == null, "Old news isn't gossip");
var view = new ColonyView { Population = 5, Troublemakers = { ("Ulf", 2, 0) }, MissedPaydays = 1, Beds = 3 };
var pets = Petitions.Raise(new Random(1), view);
Check(pets.Count == 3 && pets.All(p => !string.IsNullOrEmpty(p.Text)), "The Thing raises three petitions");
Check(Petitions.Raise(new Random(1), view, 10).Any(p => p.Id == "exile" && p.About == "Ulf"), "A thief is named");
var suspicious = new ColonyView { Population = 5, Beds = 5, HasBeautyRoom = true, HasShrine = true, GuardWage = 9, Suspects = { ("Hrafn", 2), ("Oddny", 1) } };
var accused = Petitions.Raise(new Random(1), suspicious, 10).Where(p => p.Id == "accuse").ToList();
Check(accused.Count == 1 && accused[0].About == "Hrafn", "The Thing names one seen twice near the mischief, not one seen once");
Check(Renown.TributeAsk(0, 0) == 40 && Renown.TributeAsk(80, 0) == 80 && Renown.TributeAsk(80, 1) == 120 && Renown.TributeAsk(80, 2) == 180, "The Fulings want more each time");
Check(Renown.TributeAsk(1000, 99) > 0, "The Fulings' price never overflows");
// Away: 40 wood in 2 worked hours is 20 an hour; two woodcutters, 48 hours away (11 of every 24 working) → 2 × 20 × 22 × 0.9.
int awayWood = Away.Produce(40, 20, 2, 48);
Check(awayWood == (int)Math.Floor(20.0 * 48 * 11 / 24 * 2 * 0.9), "Time away is made up at the live rate");
Check(awayWood >= 0.8 * (20.0 * 48 * 11 / 24 * 2) && awayWood <= 1.2 * (20.0 * 48 * 11 / 24 * 2), "Within a fifth of the live rate (the spec's test)");
Check(Away.Produce(40, 5, 2, 48) == 0, "Too little seen live to say: nothing made up");
Check(Away.Produce(40, 20, 0, 48) == 0 && Away.Produce(0, 20, 2, 48) == 0, "No workers or no output: nothing");
Check(Names.Byname("starkill", "Troll") == "Troll-bane" && Names.Byname("unlucky") == "the Unlucky" && Names.Creature("Greydwarf_Elite") == "Greydwarf", "Bynames");

Console.WriteLine($"Jarldom: {checks} checks passed");
