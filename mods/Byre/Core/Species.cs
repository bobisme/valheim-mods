using System;
using System.Linq;

namespace Byre
{
    // What each kind of animal is, as numbers and names: no Unity here (tests/Byre.Tests reads it). S1: sheep and goats; S2: cattle, pigs
    // (bred from tame boars, never wild) and geese (on the game's hen: they lay eggs, are plucked by hand, and see off strangers). Times are game
    // seconds (ZNet time; a game day is EnvMan's day length, 1800 s unless a mod changes it), so a night slept through counts in full.
    public sealed class Species
    {
        public string Key, Title, YoungTitle;
        public string Prefab, Young, Ragdoll;     // (prefab names: forever, see PrefabNames)
        public string From, YoungFrom, RagdollFrom; // (the game's animal each is made from: its skeleton and animations)
        public string Biome;                       // (Heightmap.Biome's name: the mod parses it)
        public int HerdMin, HerdMax;               // (a wild herd, as it spawns)
        public float MinTilt, MaxTilt;             // (the ground it spawns on, in degrees: goats like a slope)
        public float FleeRange;                    // (a wild one runs from a player this near; half that for one crouching)
        public float TamingSeconds, FedSeconds;    // (Tameable: how long fed and near to tame; how long a meal lasts)
        public float GrowSeconds;                  // (young to grown)
        public int MaxNear;                        // (Procreation: no more young once this many of the kind are near)
        public float Health;
        public string[] Foods;                     // (what it eats from the ground or a trough: item prefab names)
        public string Product, Tool;               // (what it gives, and what in the bag takes it: ByreWool with ByreShears)
        public float PeriodDays;                   // (a fed animal's product grows back in this many days; hungry, Husbandry.HungryFactor times longer)
        public int YieldMin, YieldMax, YieldHungry;
        public string Verb, Ready, NotReady;       // ("Shear", "Wool ready", "Wool grows back")
        // (S2)
        public string Model, YoungModel;           // (the Blender models each wears: Models/<name>.json.gz; null: the key's)
        public float Scale = 1, YoungScale = 1;    // (the prefab's size against its donor's: a cow is a boar made 1.9 times over)
        public string Voice = "Deer";              // (whose hurt, death and idle sounds it has in place of the donor's; null: the donor's own)
        public bool Wild = true;                   // (herds spawn: pigs are only ever bred)
        public float MinAltitude = 1, MaxAltitude = 1000;
        public bool Commandable;                   // (a tame one follows when told: a pig led into the forest, a cow to the plow)
        public string Egg;                         // (what a tame one lays in place of live young: the goose's egg, which hatches the young)
        public bool Guards, Roots;                 // (geese chase strangers; pigs root up truffles)
        public Drop[] Drops = new Drop[0], YoungDrops = new Drop[0];

        public sealed class Drop
        {
            public string Item; public int Min, Max; public float Chance;
            public Drop(string item, int min, int max, float chance = 1f) { Item = item; Min = min; Max = max; Chance = chance; }
        }
        public string ModelOf(bool young) => young ? YoungModel ?? Model ?? Key : Model ?? Key;
        public float ScaleOf(bool young) => young ? YoungScale : Scale;
        public bool HasProduct => Product != null;

        private static readonly string[] Meadow = { "Dandelion", "Raspberry", "Blueberries", "Carrot", "Turnip", "Barley", "Cloudberry" };

        public static readonly Species Sheep = new Species
        {
            Key = "sheep", Title = "Sheep", YoungTitle = "Lamb",
            Prefab = "ByreSheep", Young = "ByreLamb", Ragdoll = "ByreSheep_ragdoll",
            From = "Boar", YoungFrom = "Boar_piggy", RagdollFrom = "boar_ragdoll",
            Biome = "Meadows", HerdMin = 2, HerdMax = 4, MinTilt = 0, MaxTilt = 25,
            FleeRange = 8, TamingSeconds = 1200, FedSeconds = 1500, GrowSeconds = 3000, MaxNear = 6, Health = 30,
            Foods = Meadow,
            Product = "ByreWool", Tool = "ByreShears", PeriodDays = 2, YieldMin = 2, YieldMax = 3, YieldHungry = 1,
            Verb = "Shear", Ready = "Wool ready", NotReady = "Wool grows back",
            Drops = new[] { new Drop("RawMeat", 1, 2), new Drop("LeatherScraps", 1, 1, 0.5f) }, YoungDrops = new[] { new Drop("RawMeat", 1, 1, 0.5f) },
        };
        public static readonly Species Goat = new Species
        {
            Key = "goat", Title = "Goat", YoungTitle = "Kid",
            Prefab = "ByreGoat", Young = "ByreKid", Ragdoll = "ByreGoat_ragdoll",
            From = "Boar", YoungFrom = "Boar_piggy", RagdollFrom = "boar_ragdoll",
            Biome = "Mountain", HerdMin = 2, HerdMax = 4, MinTilt = 15, MaxTilt = 60,
            FleeRange = 10, TamingSeconds = 1800, FedSeconds = 1500, GrowSeconds = 3000, MaxNear = 6, Health = 40,
            Foods = Meadow.Concat(new[] { "Thistle", "Onion" }).ToArray(), // (a goat eats anything)
            Product = "ByreGoatMilk", Tool = "ByreMilkPail", PeriodDays = 1, YieldMin = 1, YieldMax = 2, YieldHungry = 1,
            Verb = "Milk", Ready = "Milk ready", NotReady = "Milk comes back",
            Drops = new[] { new Drop("RawMeat", 1, 2), new Drop("LeatherScraps", 1, 2) }, YoungDrops = new[] { new Drop("RawMeat", 1, 1, 0.5f) },
        };
        // Cattle: the boar's skeleton made 1.9 times over (a small, short-legged old breed, red with white splashes, horned). Milked with the
        // pail as a goat is, more of it; a big hide when slaughtered; a tame grown one can be harnessed (Api.CanPull).
        public static readonly Species Cow = new Species
        {
            Key = "cow", Title = "Cow", YoungTitle = "Calf",
            Prefab = "ByreCow", Young = "ByreCalf", Ragdoll = "ByreCow_ragdoll",
            From = "Boar", YoungFrom = "Boar_piggy", RagdollFrom = "boar_ragdoll",
            Biome = "Meadows", HerdMin = 2, HerdMax = 3, MinTilt = 0, MaxTilt = 20,
            FleeRange = 10, TamingSeconds = 2400, FedSeconds = 1800, GrowSeconds = 4500, MaxNear = 6, Health = 150,
            Foods = Meadow,
            Product = "ByreCowMilk", Tool = "ByreMilkPail", PeriodDays = 1, YieldMin = 2, YieldMax = 3, YieldHungry = 1,
            Verb = "Milk", Ready = "Milk ready", NotReady = "Milk comes back",
            Scale = 1.9f, YoungScale = 1.9f, Commandable = true,
            Drops = new[] { new Drop("RawMeat", 3, 5), new Drop("DeerHide", 2, 3), new Drop("LeatherScraps", 2, 3) },
            YoungDrops = new[] { new Drop("RawMeat", 1, 2), new Drop("LeatherScraps", 1, 1, 0.5f) },
        };
        // Pigs: never wild. A tame boar's litter is now and then piglets (Breeding: the domestic kind, pink); pigs breed pigs. Led into the
        // Black Forest, a tame pig roots up truffles. Nothing to harvest: meat when slaughtered.
        public static readonly Species Pig = new Species
        {
            Key = "pig", Title = "Pig", YoungTitle = "Piglet",
            Prefab = "ByrePig", Young = "ByrePiglet", Ragdoll = "ByrePig_ragdoll",
            From = "Boar", YoungFrom = "Boar_piggy", RagdollFrom = "boar_ragdoll",
            Biome = "BlackForest", HerdMin = 1, HerdMax = 1, MinTilt = 0, MaxTilt = 30,
            FleeRange = 4, TamingSeconds = 900, FedSeconds = 1500, GrowSeconds = 3000, MaxNear = 6, Health = 50,
            Foods = Meadow.Concat(new[] { "Mushroom", "MushroomYellow", "Onion", "Thistle" }).ToArray(), // (a pig eats anything)
            Wild = false, Commandable = true, Roots = true, Voice = null, // (a pig grunts as the boar does)
            Drops = new[] { new Drop("RawMeat", 2, 4), new Drop("LeatherScraps", 1, 2) }, YoungDrops = new[] { new Drop("RawMeat", 1, 1, 0.5f) },
        };
        // Geese: on the game's hen (its skeleton, legs and voice), a greylag goose's look. Wild ones by the water in the Meadows; a tame,
        // fed pair lays eggs, which hatch goslings under a roof by a fire as the hen's do. Plucked by hand for feathers; a tame one chases
        // any stranger near (a wild creature, a player who hasn't petted it) with a honk, and stops short: no harm done.
        public static readonly Species Goose = new Species
        {
            Key = "goose", Title = "Goose", YoungTitle = "Gosling",
            Prefab = "ByreGoose", Young = "ByreGosling", Ragdoll = null,
            From = "Hen", YoungFrom = "Hen", RagdollFrom = null,
            Biome = "Meadows", HerdMin = 2, HerdMax = 4, MinTilt = 0, MaxTilt = 25, MinAltitude = 0.3f, MaxAltitude = 5,
            FleeRange = 7, TamingSeconds = 900, FedSeconds = 1200, GrowSeconds = 3000, MaxNear = 8, Health = 20,
            Foods = new[] { "Dandelion", "Barley", "BeechSeeds", "BirchSeeds", "CarrotSeeds", "OnionSeeds", "TurnipSeeds", "Blueberries" },
            Product = "Feathers", Tool = null, PeriodDays = 2, YieldMin = 2, YieldMax = 3, YieldHungry = 1,
            Verb = "Pluck", Ready = "Feathers ready", NotReady = "Feathers grow back",
            Model = "goose", YoungModel = "gosling", YoungScale = 0.55f, Voice = null, Egg = "ByreGooseEgg", Guards = true,
            Drops = new[] { new Drop("ChickenMeat", 1, 2), new Drop("Feathers", 2, 4) }, YoungDrops = new[] { new Drop("Feathers", 1, 1, 0.5f) },
        };
        public static readonly Species[] All = { Sheep, Goat, Cow, Pig, Goose };

        // The kind an animal's prefab is, grown or young (null: not livestock of ours).
        public static Species Of(string prefab) => prefab == null ? null : All.FirstOrDefault(s => s.Prefab == prefab || s.Young == prefab);
        public static Species ByKey(string key) => All.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
        public bool IsYoung(string prefab) => prefab == Young;
    }
}
