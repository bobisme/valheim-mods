using System.Collections.Generic;
using System.Linq;

namespace Larder
{
    // Preserved foods, for long voyages (L1), as data and nothing of the game's. Meat smoked over a wood fire or packed in salt; fish split
    // and hung to dry in the wind until it's stockfish. Salt is boiled out of the sea in a pan over a fire. None of them spoils (Api.IsPreserved).
    //
    //   meat ─ smokehouse (and firewood) ─▶ smoked meat          wood ─ salt pan, standing in the sea ─▶ salt
    //   meat ─ salting barrel (and a salt each) ─▶ salt meat      raw fish ─ drying rack, under the open sky ─▶ stockfish
    //
    // The game has smoked fish and smoked moose meat of its own already (the cauldron's smoker, Deep North foods): ours are earlier and
    // plainer, so the smokehouse takes meat only, and the fish is dried.
    public static class Preserved
    {
        // Foods
        public const string SmokedMeat = "Larder_SmokedMeat", SaltMeat = "Larder_SaltMeat", Stockfish = "Larder_Stockfish";
        // Salt (an ingredient, not eaten)
        public const string Salt = "Larder_Salt";
        // Stations
        public const string Smokehouse = "Larder_Smokehouse", SaltPan = "Larder_SaltPan", SaltBarrel = "Larder_SaltBarrel", DryingRack = "Larder_DryingRack";
        // The game's: the stations are copies of its charcoal kiln (a smelter's queue, its small fire and smoke; fuel and switches are ours)
        public const string Kiln = "charcoal_kiln";
        // The meats that keep (the Meadows' and the Black Forest's), and the fish (cut from any caught one at the prep table)
        public static readonly string[] Meats = { "RawMeat", "DeerMeat", "NeckTail" };
        public const string Fish = "FishRaw";
    }

    // What a station needs where it stands, beyond its fuel: nothing, the open sky over it (a rack dries in the wind, not under a roof),
    // or the sea round its feet (a pan boils sea water).
    public enum Needs { Nothing, OpenSky, Sea }

    // A station that works through a queue as the game's smelters do (and so on its owner's game, saved on its ZDO, going on while nobody
    // is near, up to an hour): fuel (or none), a queue of one thing at a time, each taking Seconds.
    public sealed class Works
    {
        public string Name, From, Title, Model, Description, AddText, EmptyText;
        public string Fuel; public int MaxFuel, FuelPer;      // (Fuel null: none; FuelPer: how many a product burns)
        public int Holds; public float Seconds;
        public (string from, string to)[] Makes;
        public Needs Needs;
        public bool Smoke;                                    // (puffs from its top while it works, as the kiln's)
        public (string item, int n)[] Cost;
    }

    public static class Preserves
    {
        // Against the game's: cooked meat 30/10 and deer 35/12 (20 minutes), boar jerky 23/23 and wolf jerky 33/33 (30 minutes), cooked fish
        // 45/15, deer stew 45/15. Preserved food is a little more than the meat cooked fresh, and lasts much longer: what you take on a
        // voyage, not what you eat at home before a fight.
        public static readonly Food[] Foods =
        {
            new Food { Prefab = Preserved.SmokedMeat, Title = "Smoked meat", Model = "smokedmeat", Health = 36, Stamina = 14, Regen = 2, Seconds = 3000, Weight = 0.5f, Stack = 20,
                Description = "Meat hung for days in the smoke of a slow wood fire, dark and dry. It keeps for a voyage and longer." },
            new Food { Prefab = Preserved.SaltMeat, Title = "Salt meat", Model = "saltmeat", Health = 40, Stamina = 12, Regen = 2, Seconds = 3600, Weight = 0.5f, Stack = 20,
                Description = "Meat packed in sea salt in a barrel until it's cured through. Salty and tough, and it never goes off." },
            new Food { Prefab = Preserved.Stockfish, Title = "Stockfish", Model = "stockfish", Health = 28, Stamina = 26, Regen = 2, Seconds = 3600, Weight = 0.2f, Stack = 30,
                Description = "Fish split and hung on a rack in the wind until it's dry and hard as a board. Light, and it keeps for years." },
        };
        // Salt: what the barrel cures with (not eaten)
        public static readonly Food Salt = new Food { Prefab = Preserved.Salt, Title = "Salt", Model = "salt", Weight = 0.1f, Stack = 50,
            Description = "Coarse sea salt, boiled out of sea water in a pan over a fire. For curing meat in a salting barrel." };

        public static readonly Works[] Stations =
        {
            new Works { Name = Preserved.Smokehouse, From = Preserved.Kiln, Title = "Smokehouse", Model = "smokehouse", Smoke = true,
                Description = "A plank smoke hut over a stone firebox. Hang meat in it and keep the fire fed with wood: it comes out smoked, and keeps.",
                AddText = "Hang up meat", EmptyText = "Take out the smoked meat",
                Fuel = "Wood", MaxFuel = 20, FuelPer = 1, Holds = 10, Seconds = 300,
                Makes = Preserved.Meats.Select(m => (m, Preserved.SmokedMeat)).ToArray(),
                Cost = new[] { ("Stone", 12), ("Wood", 20), ("Resin", 4) } },
            new Works { Name = Preserved.SaltPan, From = Preserved.Kiln, Title = "Salt pan", Model = "saltpan", Smoke = true, Needs = Needs.Sea,
                Description = "A shallow iron pan on a ring of hearth stones. Build it in the shallows and feed it firewood: the sea water boils off, and salt is left.",
                AddText = "Feed the fire", EmptyText = "Scrape out the salt",
                Holds = 20, Seconds = 60,
                Makes = new[] { ("Wood", Preserved.Salt) },
                Cost = new[] { ("Stone", 12), ("Bronze", 2) } },
            new Works { Name = Preserved.SaltBarrel, From = Preserved.Kiln, Title = "Salting barrel", Model = "saltbarrel",
                Description = "A stout oak barrel for curing. Pack meat in it with a scoop of salt for each piece, and leave it: it comes out salt meat.",
                AddText = "Pack in meat", EmptyText = "Take out the salt meat",
                Fuel = Preserved.Salt, MaxFuel = 20, FuelPer = 1, Holds = 10, Seconds = 600,
                Makes = Preserved.Meats.Select(m => (m, Preserved.SaltMeat)).ToArray(),
                Cost = new[] { ("Wood", 12), ("BronzeNails", 6) } },
            new Works { Name = Preserved.DryingRack, From = Preserved.Kiln, Title = "Drying rack", Model = "dryingrack", Needs = Needs.OpenSky,
                Description = "A rack of rough poles, for hanging fish in the wind. Out under the open sky it dries into stockfish over days; under a roof it won't.",
                AddText = "Hang up fish", EmptyText = "Take down the stockfish",
                Holds = 8, Seconds = 1200,
                Makes = new[] { (Preserved.Fish, Preserved.Stockfish) },
                Cost = new[] { ("Wood", 12) } },
        };

        // Preserved: never spoils (Larder's spoilage, when that's switched on, passes it by).
        public static bool IsPreserved(string prefab) => Foods.Any(f => f.Prefab == prefab);

        public static IEnumerable<string> Items => Foods.Select(f => f.Prefab).Concat(new[] { Salt.Prefab });
        public static IEnumerable<string> Pieces => Stations.Select(s => s.Name);
    }
}
