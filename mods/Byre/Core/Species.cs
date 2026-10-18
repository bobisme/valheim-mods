using System;
using System.Linq;

namespace Byre
{
    // What each kind of animal is, as numbers and names: no Unity here (tests/Byre.Tests reads it). S1: sheep and goats. Times are game
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
        };
        public static readonly Species[] All = { Sheep, Goat };

        // The kind an animal's prefab is, grown or young (null: not livestock of ours).
        public static Species Of(string prefab) => prefab == null ? null : All.FirstOrDefault(s => s.Prefab == prefab || s.Young == prefab);
        public static Species ByKey(string key) => All.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));
        public bool IsYoung(string prefab) => prefab == Young;
    }
}
