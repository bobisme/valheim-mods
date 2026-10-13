using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

[assembly: System.Reflection.AssemblyMetadata("AI_Assisted_Creation", "This assembly was partially or fully created with the assistance of Generative AI.")]
[assembly: System.Reflection.AssemblyMetadata("AI_Model_Vendor", "Anthropic")]
[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload", "menu")]

namespace Jarldom
{
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.bobisme.jarldom";
        public const string Name = "Jarldom";
        public const string Version = "0.1.67";
        public const int DataVersion = 1; // raised whenever what's saved changes shape: games of different data versions mustn't write each other's colonies
        internal static Plugin Instance;
        internal ServerSettings Synced;
        internal ConfigEntry<float> LeaseSeconds, RespawnSeconds, WorkRate, ArrivalChance, RadiusBonus;
        internal ConfigEntry<int> MaxVillagers, WageScale, MaxBrainsPerFrame;
        internal ConfigEntry<bool> Migration, Payroll, Raids, Tribute, Debug, Overlay;
        internal ConfigEntry<string> TributeEvent;
        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Config.SaveOnConfigSet = false;
            Synced = new ServerSettings(Guid, Config, Logger);
            Migration = Synced.Add(Config.Bind("1 - Colony", "Migration", true, "The reborn come to a hall Odin notices. Off: only the jarldom command brings them. [Synced with Server]"));
            ArrivalChance = Synced.Add(Config.Bind("1 - Colony", "ArrivalChance", 0.6f, new ConfigDescription("Chance at dawn, once renown is high enough and a bed is free, that Odin sends someone. [Synced with Server]", new AcceptableValueRange<float>(0f, 1f))));
            MaxVillagers = Synced.Add(Config.Bind("1 - Colony", "MaxVillagers", 40, new ConfigDescription("Most villagers a colony can hold, whatever its rank. [Synced with Server]", new AcceptableValueRange<int>(1, 60))));
            RadiusBonus = Synced.Add(Config.Bind("1 - Colony", "RadiusBonus", 0f, new ConfigDescription("Metres added to every colony's radius. [Synced with Server]", new AcceptableValueRange<float>(0f, 100f))));
            Payroll = Synced.Add(Config.Bind("2 - Economy", "Payroll", true, "Villagers want wages from the treasury every five days. [Synced with Server]"));
            WageScale = Synced.Add(Config.Bind("2 - Economy", "WagePercent", 100, new ConfigDescription("Wages as a percentage of the table's. [Synced with Server]", new AcceptableValueRange<int>(0, 500))));
            WorkRate = Synced.Add(Config.Bind("3 - Work", "WorkRate", 1f, new ConfigDescription("How fast villagers work, before mood and skill. [Synced with Server]", new AcceptableValueRange<float>(0.25f, 4f))));
            LeaseSeconds = Synced.Add(Config.Bind("3 - Work", "LeaseSeconds", 60f, new ConfigDescription("How long a claimed job stays a villager's without news. [Synced with Server]", new AcceptableValueRange<float>(15f, 600f))));
            RespawnSeconds = Synced.Add(Config.Bind("3 - Work", "RespawnSeconds", 30f, new ConfigDescription("Seconds before a fallen villager rises at their bed. [Synced with Server]", new AcceptableValueRange<float>(5f, 600f))));
            Raids = Synced.Add(Config.Bind("4 - Defense", "ColonyRaids", true, "The game's raids find colonies, more often as renown grows. [Synced with Server]"));
            Tribute = Synced.Add(Config.Bind("4 - Defense", "FulingTribute", true, "Now and then a Fuling herald demands coins at a colony of hamlet rank or more: pay, or they raid. [Synced with Server]"));
            TributeEvent = Synced.Add(Config.Bind("4 - Defense", "TributeRaid", "army_goblin", "The game's event the Fulings raid with when unpaid, once the world has reached the Plains (Moder defeated); before that a small Fuling band comes, sized to the colony. [Synced with Server]"));
            MaxBrainsPerFrame = Config.Bind("5 - Performance", "BrainsPerFrame", 6, new ConfigDescription("How many villagers think each frame (the rest wait their turn).", new AcceptableValueRange<int>(1, 40)));
            Overlay = Config.Bind("6 - Display", "RoomOverlay", true, "With the hammer out, show the colony's rooms and zones.");
            Debug = Config.Bind("9 - Debug", "Debug", false, "Log what villagers decide.");
            Config.SaveOnConfigSet = true; Config.Save();

            _harmony = new Harmony(Guid);
            _harmony.PatchAll(typeof(Plugin).Assembly);
            Assets.RegisterLoaded();
            Guard(Overlay_.SweepOld); // (lines an older copy of the mod left drawn)
            Colony.AttachAll();
            Villagers.AttachAll();
            Logger.LogInfo($"{Name} {Version} loaded.");
        }
        internal static void Log(string text) => Instance?.Logger.LogInfo(text);
        internal static void Warn(string text) => Instance?.Logger.LogWarning(text);
        internal static void Trace(string text) { if (Instance != null && Instance.Debug.Value) Instance.Logger.LogInfo(text); }

        // What Jarldom costs a frame (its Update and the villagers' fixed-update thinking), for jarldom perf.
        internal static readonly System.Diagnostics.Stopwatch Clock = new System.Diagnostics.Stopwatch();
        internal static double CostMs, CostFrames, ThinkMs;
        private void LateUpdate() { CostFrames++; }
        private void Update()
        {
            Clock.Restart();
            try { Tick(); } finally { CostMs += Clock.Elapsed.TotalMilliseconds; }
        }
        private void Tick()
        {
            Synced?.Update();
            Tools.Tick();
            ServerMailbox.Tick();
            Guard(Claims.Sweep);
            Guard(Net.Tick);
            Guard(Net.KeepOwners);
            Guard(Colony.TickAll);
            Guard(Villagers.Tick);
            Guard(Passing.Tick);
            Guard(Poses.Tick);
            Guard(SeatedEmotes.Tick);
            Guard(Ui.Windows.Tick);
            Guard(Designate.Tick);
            Guard(Overlay_.Tick);
            Guard(Assets.Tick);
            Guard(Captives.Tick);
            Guard(StairLinks.Tick);
            Guard(Doors.Tick);
            Guard(Skal.Tick);
            Guard(Kneels.Tick);
            Guard(Asks.Tick);
            Guard(Dogs.Tick);
            Guard(Landing.Tick);
            Guard(Lost.Tick);
        }
        private static float _lastError;
        internal static void Guard(Action a)
        {
            try { a(); }
            catch (Exception e)
            {
                if (UnityEngine.Time.unscaledTime - _lastError > 5) { _lastError = UnityEngine.Time.unscaledTime; Instance?.Logger.LogError("Jarldom: " + e); }
            }
        }
        private void OnDestroy()
        {
            Guard(Ui.Windows.CloseAll);
            Guard(Ui.JarlView.End);
            Guard(Designate.Stop);
            Guard(Overlay_.Stop);
            Guard(CatchUp.Forget);
            Guard(Net.ForgetPins);
            Guard(Asks.Forget);
            Guard(Greet.Forget);
            Guard(Sky.Forget);
            Guard(Songs.Forget);
            Guard(Dogs.Forget);
            Guard(Landing.Forget);
            Guard(Lost.Forget);
            Guard(StairLinks.Clear);
            Guard(Doors.Clear);
            Guard(Leisure.Clear);
            Guard(SeatedEmotes.Forget);
            Guard(Upkeep.Forget);
            Guard(Skal.Unhook);
            Guard(Weaving.Forget);
            Guard(SoulStore.Forget);
            Tools.Stop();
            Net.Unregister();
            _harmony?.UnpatchSelf();
            Guard(Villagers.DetachAll);
            Guard(PieceScripts.DetachAll);
            Guard(Colony.DetachAll);
            Guard(Passing.Clear); Guard(Seats.Clear); Guard(Stock.Forget); Guard(Thing.Forget); Guard(Versions.Forget); Guard(Claims.Forget); Guard(Defense.Forget); Guard(Work.Forget); Guard(Valkyrie.Clear);
            Guard(Assets.UnregisterAll);
            Guard(GameUi.Destroy);
            Synced?.Dispose();
            if (Instance == this) Instance = null;
        }
    }

    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class RegisterPrefabs { private static void Postfix(ZNetScene __instance) => Assets.RegisterScene(__instance); }
    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class RegisterItems { private static void Postfix(ObjectDB __instance) => Assets.RegisterDB(__instance); }
    [HarmonyPatch(typeof(ObjectDB), "CopyOtherDB")]
    internal static class RegisterItemsMenu { private static void Postfix(ObjectDB __instance) => Assets.RegisterDB(__instance); }
    [HarmonyPatch(typeof(ZNet), "Shutdown")]
    internal static class ForgetWorld { private static void Prefix() { Plugin.Guard(Colony.ForgetWorld); Plugin.Guard(Villagers.ForgetWorld); } }
}
