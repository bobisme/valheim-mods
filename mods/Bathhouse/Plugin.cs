using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload", "world")]

namespace Bathhouse
{
    // Bathhouse: the badstofa, the Norse steam bath. Build a small room of your own, roofed and walled; put a sauna stove and a sauna
    // bench in it; light the stove and sit in the heat a minute (throw water on the stones from the bucket for steam): Bathed. Straight
    // out into cold water (the sea, a lake, a plunge tub): Invigorated. The game's own hot tub counts in a room too. Nothing is an item.
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "cx.bob.bathhouse";
        public const string Name = "Bathhouse";
        public const string Version = "0.1.6";
        internal static Plugin Instance;
        internal ConfigEntry<bool> Enabled;
        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Enabled = Config.Bind("General", "Enabled", true, "Put the sauna stove, sauna bench, water bucket and plunge tub in the hammer's Furniture tab.");
            _harmony = new Harmony(Guid); _harmony.PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo($"{Name} {Version} loaded: a sauna stove, a sauna bench, a water bucket, a plunge tub and a bath locker in the hammer's Furniture tab.");
        }
        private void OnDestroy()
        {
            Kit.Unload();
            Tools.Stop();
            _harmony?.UnpatchSelf();
            if (Instance == this) Instance = null;
        }
        private void Update()
        {
            try { Kit.Ensure(); } catch (Exception e) { Logger.LogError(e); Kit.Broken(); }
            Guard(Effects.Ensure);
            Guard(Bathing.Tick);
            Guard(Tools.Tick);
        }
        private static float _quietUntil;
        internal static void Guard(Action a)
        {
            try { a(); }
            catch (Exception e) { if (UnityEngine.Time.unscaledTime > _quietUntil) { _quietUntil = UnityEngine.Time.unscaledTime + 10; Instance?.Logger.LogError(e); } }
        }
        internal static void Log(string text) => Instance?.Logger.LogInfo(text);
        internal static void Warn(string text) => Instance?.Logger.LogWarning(text);
    }
}
