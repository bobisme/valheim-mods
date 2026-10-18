using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload", "menu")]

namespace Byre
{
    // Byre: livestock. Wild sheep in the Meadows and goats in the Mountains, in small herds that run from you; tame them with food as the
    // game's boars are tamed, and tame pairs have young. Shear a tame sheep for wool (shears in the bag, E on it), milk a tame goat (a milk
    // pail); fed animals give more and sooner. A feeding trough keeps them fed. Animals, items and the trough are saved prefabs: the mod
    // needs a restart to load or update (RESTART_REQUIRED.txt), and every player and the server need it.
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "cx.bob.byre";
        public const string Name = "Byre";
        public const string Version = "0.1.2";
        internal static Plugin Instance;
        internal ServerSettings Synced;
        internal ConfigEntry<bool> Spawning;
        internal ConfigEntry<float> SheepChance, GoatChance, SpawnInterval, RegrowFactor;
        internal ConfigEntry<string> ShearsCost, PailCost, TroughCost;
        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Synced = new ServerSettings(Guid, Config, Logger);
            Spawning = Synced.Bind("1 - Herds", "WildHerds", true, "Wild sheep herds in the Meadows and goats in the Mountains. Off: none spawn (those already out stay).");
            SheepChance = Synced.Bind("1 - Herds", "SheepChance", 25f, "Chance in percent, each spawn check, that a herd of sheep comes to a Meadows zone with fewer than three about.", new AcceptableValueRange<float>(0f, 100f));
            GoatChance = Synced.Bind("1 - Herds", "GoatChance", 25f, "The same for goats in the Mountains.", new AcceptableValueRange<float>(0f, 100f));
            SpawnInterval = Synced.Bind("1 - Herds", "SpawnInterval", 600f, "Seconds between spawn checks in a zone (the game's wolves: 1000; deer: 30).", new AcceptableValueRange<float>(30f, 6000f));
            RegrowFactor = Synced.Bind("2 - Husbandry", "RegrowFactor", 1f, "Wool and milk come back this many times slower (0.5: twice as fast). Fed: two days for wool, one for milk.", new AcceptableValueRange<float>(0.05f, 20f));
            ShearsCost = Synced.Bind("3 - Costs", "Shears", "Copper:3", "What the shears cost (made at the forge). " + Costs.Help);
            PailCost = Synced.Bind("3 - Costs", "MilkPail", "Wood:6", "What the milk pail costs (made at the workbench). " + Costs.Help);
            TroughCost = Synced.Bind("3 - Costs", "Trough", "Wood:10", "What the feeding trough costs to build (by a workbench). " + Costs.Help);
            ShearsCost.SettingChanged += (s, e) => Costs.Apply(Items.ShearsRecipe, ShearsCost.Value, Logger);
            PailCost.SettingChanged += (s, e) => Costs.Apply(Items.PailRecipe, PailCost.Value, Logger);
            TroughCost.SettingChanged += (s, e) => Costs.Apply(Kit.TroughPiece, TroughCost.Value, Logger);
            _harmony = new Harmony(Guid); _harmony.PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo($"{Name} {Version} loaded: sheep and goats, shears, a milk pail and a feeding trough.");
        }
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
