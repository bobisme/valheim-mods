using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload", "world")]

namespace BuildKit
{
    // BuildKit: Norse building pieces for the hammer. K1: turf roofs (sod on birch bark, in every shape the game's roofs come in, snapping
    // with them; a turf roof over a house is warmer: +1 comfort).
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.bobisme.buildkit";
        public const string Name = "BuildKit";
        public const string Version = "0.2.0";
        internal static Plugin Instance;
        internal ConfigEntry<bool> Enabled;
        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Enabled = Config.Bind("General", "Enabled", true, "Put the kit's pieces in the hammer's build menu.");
            _harmony = new Harmony(Guid); _harmony.PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo($"{Name} {Version} loaded: turf roofs in the hammer's Building tab.");
        }
        private void OnDestroy()
        {
            Tools.Stop();
            Kit.Unload();
            _harmony?.UnpatchSelf();
            if (Instance == this) Instance = null;
        }
        private void Update()
        {
            try { Kit.Ensure(); } catch (Exception e) { Logger.LogError(e); Kit.Broken(); }
            try { Tools.Tick(); } catch (Exception e) { Logger.LogError("BuildKit tools: " + e); }
        }
        internal static void Log(string text) => Instance?.Logger.LogInfo(text);
        internal static void Warn(string text) => Instance?.Logger.LogWarning(text);
    }
}
