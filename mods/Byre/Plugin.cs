using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload", "menu")]

namespace Byre
{
    // Byre: livestock. Wild sheep, cattle and geese in the Meadows and goats in the Mountains, in small herds that run from you; tame them
    // with food as the game's boars are tamed, and tame pairs have young. Shear a tame sheep for wool (shears in the bag, E on it), milk a
    // tame goat or cow (a milk pail), pluck a goose; fed animals give more and sooner. Geese lay eggs and see off strangers; tame boars now
    // and then have pigs' piglets, and a pig led through the Black Forest roots up truffles. A feeding trough keeps them fed. Animals, items and the trough are saved prefabs: the mod
    // needs a restart to load or update (RESTART_REQUIRED.txt), and every player and the server need it.
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "cx.bob.byre";
        public const string Name = "Byre";
        public const string Version = "0.3.0";
        internal static Plugin Instance;
        internal ServerSettings Synced;
        internal ConfigEntry<bool> Spawning;
        internal ConfigEntry<float> SheepChance, GoatChance, CowChance, GooseChance, SpawnInterval, RegrowFactor, PigFromBoarChance, TruffleMinutes;
        internal ConfigEntry<string> ShearsCost, PailCost, TroughCost, HayRackCost, StallCost;
        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Synced = new ServerSettings(Guid, Config, Logger);
            Spawning = Synced.Bind("1 - Herds", "WildHerds", true, "Wild sheep, cattle and geese in the Meadows and goats in the Mountains. Off: none spawn (those already out stay).");
            SheepChance = Synced.Bind("1 - Herds", "SheepChance", 25f, "Chance in percent, each spawn check, that a herd of sheep comes to a Meadows zone with fewer than three about.", new AcceptableValueRange<float>(0f, 100f));
            GoatChance = Synced.Bind("1 - Herds", "GoatChance", 25f, "The same for goats in the Mountains.", new AcceptableValueRange<float>(0f, 100f));
            CowChance = Synced.Bind("1 - Herds", "CowChance", 15f, "The same for cattle in the Meadows.", new AcceptableValueRange<float>(0f, 100f));
            GooseChance = Synced.Bind("1 - Herds", "GooseChance", 20f, "The same for geese, on the low ground by the water in the Meadows.", new AcceptableValueRange<float>(0f, 100f));
            SpawnInterval = Synced.Bind("1 - Herds", "SpawnInterval", 600f, "Seconds between spawn checks in a zone (the game's wolves: 1000; deer: 30).", new AcceptableValueRange<float>(30f, 6000f));
            RegrowFactor = Synced.Bind("2 - Husbandry", "RegrowFactor", 1f, "Wool, milk and feathers come back this many times slower (0.5: twice as fast). Fed: two days for wool and feathers, one for milk.", new AcceptableValueRange<float>(0.05f, 20f));
            PigFromBoarChance = Synced.Bind("2 - Husbandry", "PigFromBoarChance", 25f, "Chance in percent that a tame boar's young is a pig's piglet in place of its own (each birth on its own). Pigs breed pigs.", new AcceptableValueRange<float>(0f, 100f));
            TruffleMinutes = Synced.Bind("2 - Husbandry", "TruffleMinutes", 10f, "A tame pig led through the Black Forest roots up a truffle about once in this many minutes (never two within three minutes).", new AcceptableValueRange<float>(1f, 600f));
            ShearsCost = Synced.Bind("3 - Costs", "Shears", "Copper:3", "What the shears cost (made at the forge). " + Costs.Help);
            PailCost = Synced.Bind("3 - Costs", "MilkPail", "Wood:6", "What the milk pail costs (made at the workbench). " + Costs.Help);
            TroughCost = Synced.Bind("3 - Costs", "Trough", "Wood:10", "What the feeding trough costs to build (by a workbench). " + Costs.Help);
            HayRackCost = Synced.Bind("3 - Costs", "HayRack", "Wood:12", "What the hay rack costs to build (by a workbench). " + Costs.Help);
            StallCost = Synced.Bind("3 - Costs", "StallDivider", "Wood:6", "What a stall divider costs to build (by a workbench). " + Costs.Help);
            ShearsCost.SettingChanged += (s, e) => Costs.Apply(Items.ShearsRecipe, ShearsCost.Value, Logger);
            PailCost.SettingChanged += (s, e) => Costs.Apply(Items.PailRecipe, PailCost.Value, Logger);
            TroughCost.SettingChanged += (s, e) => Costs.Apply(Kit.TroughPiece, TroughCost.Value, Logger);
            HayRackCost.SettingChanged += (s, e) => Costs.Apply(Kit.RackPiece, HayRackCost.Value, Logger);
            StallCost.SettingChanged += (s, e) => Costs.Apply(Kit.StallPiece, StallCost.Value, Logger);
            _harmony = new Harmony(Guid); _harmony.PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo($"{Name} {Version} loaded: sheep, goats, cattle, pigs and geese, shears, a milk pail, a feeding trough, a hay rack and stall dividers.");
        }
        // (the spawn chance for a kind's wild herds)
        internal float ChanceOf(Species s) => s == Species.Goat ? GoatChance.Value : s == Species.Cow ? CowChance.Value : s == Species.Goose ? GooseChance.Value : SheepChance.Value;
        private void OnDestroy()
        {
            Tools.Stop();
            Kit.Unload();
            _harmony?.UnpatchSelf();
            Synced?.Dispose();
            if (Instance == this) Instance = null;
        }
        private void Update()
        {
            Synced?.Update();
            Guard(Kit.Tick);
            Guard(Tools.Tick);
            Guard(Icon.Tick);
        }
        private static float _quietUntil;
        internal static void Guard(Action a)
        {
            try { a(); }
            catch (Exception e) { if (UnityEngine.Time.unscaledTime > _quietUntil) { _quietUntil = UnityEngine.Time.unscaledTime + 10; Instance?.Logger.LogError(e); } }
        }
        internal BepInEx.Logging.ManualLogSource Logs => Logger;
        internal static void Log(string text) => Instance?.Logger.LogInfo(text);
        internal static void Warn(string text) => Instance?.Logger.LogWarning(text);
    }
}
