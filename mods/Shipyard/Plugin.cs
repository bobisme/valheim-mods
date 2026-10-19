using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

// Pieces only (the slipway and the boats): reloadable in a world (PieceReload).
[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload", "world")]

namespace Shipyard
{
    // Shipyard (Naust): boats built at the shore. A slipway in the hammer's Misc tab; on it a boat is built in stages you can see
    // (keel and stems, ribs, strakes, thwarts and oars), each paid in full before the next, by anyone; finished, she's launched into the
    // water ahead. N1: the færing, a quick four-oared rowing boat. The build's rules are Core/Hulls.cs; Slipway.cs works them in the
    // world, Yard.cs makes the prefabs.
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "cx.bob.shipyard";
        public const string Name = "Shipyard";
        public const string Version = "0.1.0";
        internal static Plugin Instance;
        internal ConfigEntry<bool> Enabled;
        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Enabled = Config.Bind("General", "Enabled", true, "Put the slipway in the hammer's Misc tab.");
            _harmony = new Harmony(Guid); _harmony.PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo($"{Name} {Version} loaded: a slipway in the hammer's Misc tab, and the færing built on it.");
        }
        private void OnDestroy()
        {
            Yard.Unload();
            Tools.Stop();
            _harmony?.UnpatchSelf();
            if (Instance == this) Instance = null;
        }
        private void Update()
        {
            Guard(() => Yard.Ensure());
            Guard(Tools.Tick);
            Guard(Icon.Tick);
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
