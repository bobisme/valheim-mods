using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload", "world")]

namespace Camping
{
    // Camping: somewhere to sleep away from home. A bedroll to lay down by any fire, a lean-to to keep the rain off, a tent that keeps the
    // cold out. The bedroll and the tent pack up again (Shift+E): what they cost comes back to the bag. Nothing is an item, so nothing is
    // lost if the mod is taken away.
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "cx.bob.camping";
        public const string Name = "Camping";
        public const string Version = "0.1.2";
        internal static Plugin Instance;
        internal ConfigEntry<bool> Enabled;
        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Enabled = Config.Bind("General", "Enabled", true, "Put the bedroll, lean-to and tent in the hammer's Furniture tab.");
            _harmony = new Harmony(Guid); _harmony.PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo($"{Name} {Version} loaded: a bedroll, a lean-to and a tent in the hammer's Furniture tab.");
        }
        private void OnDestroy()
        {
            Kit.Unload();
            _harmony?.UnpatchSelf();
            if (Instance == this) Instance = null;
        }
        private void Update()
        {
            try { Kit.Ensure(); } catch (Exception e) { Logger.LogError(e); Kit.Broken(); }
        }
        internal static void Log(string text) => Instance?.Logger.LogInfo(text);
        internal static void Warn(string text) => Instance?.Logger.LogWarning(text);
    }
}
