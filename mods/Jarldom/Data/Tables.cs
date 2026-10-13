using System;
using System.Collections.Generic;
using System.Linq;

namespace Jarldom
{
    // Everything the colony's rules are made of, as plain tables: professions, rooms, traits, moodlets, ranks, wages, gods.
    // No Unity here (tests/Jarldom.Tests compiles it); the numbers are starting values to tune, not contracts.

    public enum Prof { Hauler, Woodcutter, Forager, Farmer, Guard, Miner, Hunter, Fisherman, Smelter, Cook, Builder, Smith, Scout, Trader, Gothi, Skald, Healer, Herder, Carter, Weaver }

    public enum Need { Food, Rest, Shelter, Social, Safety, Faith, Beauty }

    public enum RoomType { Longhouse, GreatHall, Stockpile, Smithy, OdinShrine, Temple, HealersHouse, TreasureRoom, BurialGround, Watchtower, Brewhouse }

    public enum ZoneType { Woodlot, Mine, Fields, Foraging, Hunting, FishingDock, GuardPost, SacredBog, Pasture }

    public enum Rank { Camp, Homestead, Hamlet, Village, Jarldom, Hold }

    public enum Activity { Sleep, Wake, Work, Shelter, Hall, Home, Defend, Flee, Idle, Follow, Thing }

    public sealed class ProfessionInfo
    {
        public Prof Id; public string Name; public int Milestone; public string Does; public string Needs; public int Wage;
        public bool Combat; public string Tool; // the tool kind its work needs ("axe", "pickaxe", "cultivator", "bow", "rod", "hammer", "")
    }

    public sealed class TraitInfo { public string Id, Name, Text; }

    public sealed class MoodletInfo
    {
        public string Id, Text; public int Value; public double Hours; // Hours 0 = while the condition holds (refreshed by the system)
        public Stack Stack = Stack.Refresh; public int MaxStacks = 1;
    }
    public enum Stack { Refresh, Add, Max }

    public sealed class RankInfo
    {
        public Rank Id; public string Name; public int Population; public RoomType[] Rooms; public int Cap; public string Unlocks;
        public RoomType? GradedRoom; public char MinGrade = 'D'; public int RoomCount = 1;
    }

    public sealed class RoomInfo
    {
        public RoomType Id; public string Name; public bool Enclosed, Roofed, Door, Fire, Lockable, Open;
        public int Beds, Seats, Tables, Chests, Graves; public float MinArea, MinFloor; public bool Hearth, Forge, Altar, Brew;
        public string PointsFrom;
    }

    public sealed class ZoneInfo { public ZoneType Id; public string Name; public Prof? Worker; public string Text; }

    public sealed class GodInfo { public string Id, Name, Offerings, Blessing, Neglect; public string[] OfferingItems; }

    public static class Tables
    {
        public static readonly ProfessionInfo[] Professions =
        {
            P(Prof.Hauler, "Hauler", 3, "moves things to the stockpiles, keeps fires and torches fed", "", 1, tool: ""),
            P(Prof.Woodcutter, "Woodcutter", 3, "chops in the woodlot and keeps the hall's woodpile", "an axe", 3, tool: "axe"),
            P(Prof.Forager, "Forager", 3, "gathers berries, mushrooms, thistle and dandelions", "", 2, tool: ""),
            P(Prof.Farmer, "Farmer", 3, "harvests and replants the fields", "a cultivator and seeds", 3, tool: "cultivator"),
            P(Prof.Guard, "Guard", 3, "patrols and fights what comes into the colony", "a weapon", 5, tool: "weapon", combat: true),
            P(Prof.Miner, "Miner", 5, "mines rock and ore in the mine", "a pickaxe", 4, tool: "pickaxe"),
            P(Prof.Hunter, "Hunter", 5, "hunts in the hunting grounds", "a bow and arrows", 4, tool: "bow", combat: true),
            P(Prof.Fisherman, "Fisherman", 5, "fishes from the dock", "a rod and bait", 3, tool: "rod"),
            P(Prof.Smelter, "Smelter", 5, "keeps kilns and smelters fed", "", 4, tool: ""),
            P(Prof.Cook, "Cook", 5, "cooks for the hall, brews at the cauldron and keeps the fermenters going", "", 4, tool: ""),
            P(Prof.Builder, "Builder", 5, "builds planned ghosts from the stockpiles", "a hammer", 5, tool: "hammer"),
            P(Prof.Smith, "Smith", 5, "makes what's commissioned at the forge and workbench", "the stations", 8, tool: ""),
            P(Prof.Scout, "Scout", 5, "goes out past the colony's edge, fills in the map and warns of what's out there", "", 6, tool: ""),
            P(Prof.Trader, "Trader", 7, "goes on trade runs", "", 5, tool: ""),
            P(Prof.Gothi, "Gothi", 8, "keeps a temple", "a temple", 6, tool: ""),
            P(Prof.Skald, "Skald", 4, "plays in the hall at dusk", "a great hall", 4, tool: ""),
            P(Prof.Healer, "Healer", 6, "tends the injured (twice as well in a healer's house)", "", 6, tool: ""),
            P(Prof.Herder, "Herder", 5, "feeds the tame beasts in the pasture and culls the surplus", "a weapon to cull with", 3, tool: ""),
            P(Prof.Carter, "Carter", 10, "carries an outpost's ore and metal to the main colony by cart", "an outpost and a cart", 4, tool: ""),
            P(Prof.Weaver, "Weaver", 3, "spins flax into linen and weaves and dyes clothes and caps at the workbench, so no one goes in rags", "a workbench, and flax or linen thread", 4, tool: ""),
        };

        private static ProfessionInfo P(Prof id, string name, int milestone, string does, string needs, int wage, string tool, bool combat = false) =>
            new ProfessionInfo { Id = id, Name = name, Milestone = milestone, Does = does, Needs = needs, Wage = wage, Tool = tool, Combat = combat };

        public static ProfessionInfo Of(Prof p) => Professions[(int)p];

        public static readonly TraitInfo[] Traits =
        {
            T("strong", "Strong back", "carries half as much again"),
            T("coward", "Coward", "flees at half health; feels unsafe"),
            T("brave", "Brave", "never flees; more worth from kills"),
            T("pious", "Pious", "needs faith twice as much"),
            T("glutton", "Glutton", "hungers faster, loves a fine meal"),
            T("nightowl", "Night owl", "works an hour later"),
            T("early", "Early riser", "starts an hour earlier"),
            T("seasick", "Seasick", "no work on ships"),
            T("grudge", "Grudge-holder", "forgives slowly"),
            T("greedy", "Greedy", "wants half again in wages"),
            T("greenthumb", "Green thumb", "farms a fifth faster"),
            T("keeneye", "Keen eye", "hunts and scouts a fifth better"),
            T("clumsy", "Clumsy", "wears tools out faster"),
            T("merry", "Merry", "cheers the hall"),
        };
        private static TraitInfo T(string id, string name, string text) => new TraitInfo { Id = id, Name = name, Text = text };
        public static TraitInfo Trait(string id) => Traits.FirstOrDefault(t => t.Id == id);

        // Pairs that make no sense on one soul.
        public static bool Clash(string a, string b) =>
            a == b || Pair(a, b, "coward", "brave") || Pair(a, b, "nightowl", "early");
        private static bool Pair(string a, string b, string x, string y) => a == x && b == y || a == y && b == x;

        public static readonly MoodletInfo[] Moodlets =
        {
            M("finemeal", "Ate a fine meal", 8, 24),
            M("samefood", "The same food three days running", -6, 0),
            M("hungry", "Starving", -10, 0),
            M("hall", "An evening in the great hall", 6, 24),
            M("skald", "Heard the skald", 4, 24),
            M("feast", "Feasted", 20, 48),
            M("ownbed", "Slept in their own bed", 8, 24),
            M("hallfire", "Slept by the hall fire, with no bed", -10, 24),
            M("wetcold", "Slept wet or cold", -6, 24),
            M("bigcreature", "Saw a great beast in the colony", -5, 6),
            M("raidheld", "The raid was held", 10, 48),
            M("fell", "Fell in battle", -8, 48),
            M("grief", "A friend lost to Hel", -25, 120),
            M("ringgifted", "Given an arm ring", 15, 120),
            M("asked", "The jarl did as they asked", 12, 96),
            M("washed", "Their shame washed out", 15, 120),
            M("unpaid", "Unpaid", -15, 0, Stack.Add, 3),
            M("gary", "Petted Gary", 3, 6),
            M("festival", "A festival", 12, 48),
            M("neglect", "The temple is neglected", -8, 0),
            M("injured", "Injured", -6, 0),
            M("valkyries", "Hears the Valkyries", 15, 0),
            M("tribute", "We paid the Fulings", -5, 72),
            M("arrived", "Reborn in a new home", 10, 24),
            M("rain", "Caught in the rain", -3, 2),
            M("merry", "Merry company in the hall", 5, 24),
            M("blessed", "The gods smile on us", 8, 24),
            M("brawl", "Got into a brawl", -6, 24),
            M("thingheard", "Heard at the Thing", 6, 48),
            M("thingignored", "Ignored at the Thing", -8, 48),
            M("thingheld", "Had their say at the Thing", 5, 48),
            M("unheard", "Petitions wait unheard: no Thing called", -4, 24),
            M("promisekept", "The jarl kept their word", 8, 48),
            M("promisebroken", "The jarl broke their word", -10, 72),
            M("ashdreams", "Dreams of ash", -2, 24),
            M("inspired", "Inspired", 10, 24),
            M("belovedjarl", "Follows a jarl they love", 4, 24),
            M("unjust", "An innocent was cast out", -6, 72),
            M("tavern", "A drink in the hall", 5, 12),
            M("expedition", "Went out with the jarl", 8, 48),
            M("dressed", "Dressed well", 4, 0),
            M("sport", "Had some sport", 4, 24),
            M("skal", "Toasted with the jarl", 6, 12),
        };
        private static MoodletInfo M(string id, string text, int value, double hours, Stack stack = Stack.Refresh, int max = 1) =>
            new MoodletInfo { Id = id, Text = text, Value = value, Hours = hours, Stack = stack, MaxStacks = max };
        public static MoodletInfo Moodlet(string id) => Moodlets.FirstOrDefault(m => m.Id == id);

        public static readonly RankInfo[] Ranks =
        {
            new RankInfo { Id = Rank.Camp, Name = "Camp", Population = 0, Rooms = new RoomType[0], Cap = 3, Unlocks = "migration, basic work" },
            new RankInfo { Id = Rank.Homestead, Name = "Homestead", Population = 3, Rooms = new[] { RoomType.Longhouse }, Cap = 6, Unlocks = "the great hall" },
            new RankInfo { Id = Rank.Hamlet, Name = "Hamlet", Population = 6, Rooms = new[] { RoomType.GreatHall, RoomType.Stockpile }, Cap = 12, Unlocks = "Odin's shrine, payroll, bigger raids" },
            new RankInfo { Id = Rank.Village, Name = "Village", Population = 12, Rooms = new[] { RoomType.OdinShrine, RoomType.Smithy }, Cap = 20, Unlocks = "temples, trade runs (on foot, by cart), the Thing" },
            new RankInfo { Id = Rank.Jarldom, Name = "Jarldom", Population = 20, Rooms = new[] { RoomType.Temple, RoomType.GreatHall }, Cap = 30, Unlocks = "the treasure room, the Dvergr envoy, trade by karve", GradedRoom = RoomType.GreatHall, MinGrade = 'B' },
            new RankInfo { Id = Rank.Hold, Name = "Hold", Population = 30, Rooms = new[] { RoomType.Temple, RoomType.BurialGround, RoomType.Watchtower }, Cap = 40, Unlocks = "everything, trade by longship" },
        };
        public static RankInfo Of(Rank r) => Ranks[(int)r];

        public static readonly RoomInfo[] Rooms =
        {
            new RoomInfo { Id = RoomType.Longhouse, Name = "Longhouse", Enclosed = true, Roofed = true, Door = true, Fire = true, Beds = 1, PointsFrom = "comfort pieces, beds with chests, rugs" },
            new RoomInfo { Id = RoomType.GreatHall, Name = "Great hall", Enclosed = true, Roofed = true, Door = true, Hearth = true, Fire = true, Seats = 4, Tables = 2, MinArea = 40, PointsFrom = "seats, tables, banners, carvings, trophies, the high seat" },
            new RoomInfo { Id = RoomType.Stockpile, Name = "Stockpile", Roofed = true, Chests = 4, PointsFrom = "chests, item stands" },
            new RoomInfo { Id = RoomType.Smithy, Name = "Smithy", Roofed = true, Forge = true, PointsFrom = "station upgrades" },
            new RoomInfo { Id = RoomType.OdinShrine, Name = "Odin's shrine", Roofed = true, Altar = true, PointsFrom = "ravens, trophies, candles" },
            new RoomInfo { Id = RoomType.Temple, Name = "Temple", Enclosed = true, Roofed = true, Altar = true, PointsFrom = "offerings, braziers, carvings" },
            new RoomInfo { Id = RoomType.HealersHouse, Name = "Healer's house", Enclosed = true, Roofed = true, Fire = true, Beds = 2, PointsFrom = "comfort" },
            new RoomInfo { Id = RoomType.TreasureRoom, Name = "Treasure room", Enclosed = true, Roofed = true, Lockable = true, Door = true, PointsFrom = "coins and valuables" },
            new RoomInfo { Id = RoomType.BurialGround, Name = "Burial ground", Open = true, Graves = 3, PointsFrom = "runestones, candles" },
            new RoomInfo { Id = RoomType.Watchtower, Name = "Watchtower", Roofed = true, MinFloor = 4, PointsFrom = "height, braziers" },
            new RoomInfo { Id = RoomType.Brewhouse, Name = "Brewhouse", Roofed = true, Brew = true, PointsFrom = "fermenters, the cauldron" },
        };
        public static RoomInfo Of(RoomType r) => Rooms[(int)r];

        public static readonly ZoneInfo[] Zones =
        {
            new ZoneInfo { Id = ZoneType.Woodlot, Name = "Woodlot", Worker = Prof.Woodcutter, Text = "trees to fell" },
            new ZoneInfo { Id = ZoneType.Mine, Name = "Quarry", Worker = Prof.Miner, Text = "rock and ore to mine" },
            new ZoneInfo { Id = ZoneType.Fields, Name = "Fields", Worker = Prof.Farmer, Text = "crops to tend" },
            new ZoneInfo { Id = ZoneType.Foraging, Name = "Foraging grounds", Worker = Prof.Forager, Text = "wild food to gather" },
            new ZoneInfo { Id = ZoneType.Hunting, Name = "Hunting grounds", Worker = Prof.Hunter, Text = "game to hunt" },
            new ZoneInfo { Id = ZoneType.FishingDock, Name = "Fishing dock", Worker = Prof.Fisherman, Text = "water to fish" },
            new ZoneInfo { Id = ZoneType.GuardPost, Name = "Guard post", Worker = Prof.Guard, Text = "an old guard post (raise watch posts instead: guards walk them in turn)" },
            new ZoneInfo { Id = ZoneType.SacredBog, Name = "Sacred bog", Worker = null, Text = "where coins go to the gods" },
            new ZoneInfo { Id = ZoneType.Pasture, Name = "Pasture", Worker = Prof.Herder, Text = "tame beasts to keep" },
        };
        public static ZoneInfo Of(ZoneType z) => Zones[(int)z];

        public static readonly GodInfo[] Gods =
        {
            G("odin", "Odin", "trophies, mead, coins", "more and better migrants", "fewer arrivals", "Trophy", "Mead", "Coins"),
            G("thor", "Thor", "weapons, trophies of great beasts", "guards strike harder", "storms in raids", "Sword", "Axe", "Mace", "Trophy"),
            G("freyja", "Freyja", "crops, honey, flowers", "crops grow faster", "blight", "Honey", "Carrot", "Turnip", "Onion", "Barley", "Flax", "Dandelion"),
            G("njord", "Njord", "fish, amber", "better fishing and trade", "lost trade goods", "Fish", "Amber"),
            G("frigg", "Frigg", "fine food, cloth", "comfort at every bed", "less comfort at home", "LinenCloth", "Stew", "Soup", "Bread"),
            G("tyr", "Tyr", "weapons, shields", "guards never flee", "guard morale", "Shield", "Sword", "Spear"),
            G("ullr", "Ullr", "hides, bows", "better hunting", "harsh cold", "DeerHide", "LeatherScraps", "TrollHide", "Bow", "WolfPelt"),
            G("hel", "Hel", "bones, grave goods", "souls are saved from Hel", "more draugr", "BoneFragments", "WitheredBone", "Ruby", "SilverNecklace"),
        };
        private static GodInfo G(string id, string name, string offerings, string blessing, string neglect, params string[] items) =>
            new GodInfo { Id = id, Name = name, Offerings = offerings, Blessing = blessing, Neglect = neglect, OfferingItems = items };
        public static GodInfo God(string id) => Gods.FirstOrDefault(g => g.Id == id);

        // Odin's shrine needs the shrine's altar; the other temples their god's altar. An altar names its god in its ZDO.
        public static int Wage(Prof p, bool greedy) { int w = Of(p).Wage; return greedy ? (w * 3 + 1) / 2 : w; }

        public static char Grade(int points) => points >= 55 ? 'S' : points >= 35 ? 'A' : points >= 20 ? 'B' : points >= 10 ? 'C' : 'D';
        public static int GradeRank(char g) => "DCBAS".IndexOf(g);
        public static float GradeScale(char g) => 0.8f + 0.15f * Math.Max(0, GradeRank(g)); // D 0.8 .. S 1.4

        // Profession defaults for a newcomer's preference, drawn from the first milestones' jobs.
        public static readonly Prof[] Starting = { Prof.Woodcutter, Prof.Forager, Prof.Farmer, Prof.Guard, Prof.Hauler, Prof.Miner, Prof.Hunter, Prof.Skald, Prof.Cook, Prof.Smelter, Prof.Weaver };
    }
}
