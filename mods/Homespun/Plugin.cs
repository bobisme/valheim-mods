using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

// A restart to install: new items with recipes (the game only reads recipes at the main menu). `reload Homespun force` works for testing.
[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload", "menu")]

namespace Homespun
{
    // Homespun: cloth made at home, from the field to the garment. Flax soaks in a retting pool and is broken and combed at a flax brake
    // into line flax; it and wool (Byre's fleece, or wool combed from a lox pelt) are spun on a drop spindle in the hand or on the game's
    // spinning wheel; thread and yarn are woven on a warp-weighted loom into bolts of linen and wool and tablet-woven bands; and those
    // are sewn at a sewing table into undertunics, tunics, dresses, trousers and cloaks, and Hildir's own clothes. The chain itself
    // (what each step takes and gives) is Core/Chain.cs; Things.cs makes the items and recipes, Kit.cs the stations.
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "cx.bob.homespun";
        public const string Name = "Homespun";
        public const string Version = "0.1.3";
        internal static Plugin Instance;
        internal ConfigEntry<bool> Enabled;
        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Enabled = Config.Bind("General", "Enabled", true, "Put the retting pool, flax brake, loom and sewing table in the hammer's Crafting tab.");
            Things.BindCosts(Config);
            _harmony = new Harmony(Guid); _harmony.PatchAll(typeof(Plugin).Assembly);
            Safe("register", Things.Register);
            Safe("repoint", Things.Repoint); // (a hot reload: items already in the world get this copy's prefabs)
            Logger.LogInfo($"{Name} {Version} loaded: a retting pool, a flax brake, a loom and a sewing table in the hammer's Crafting tab, and a drop spindle at the workbench.");
        }
        private void OnDestroy()
        {
            Kit.Unload();
            Wheel.Unload();
            Tools.Stop();
            _harmony?.UnpatchSelf();
            Things.Unload();
            if (Instance == this) Instance = null;
        }
        private void Update()
        {
            try { Kit.Ensure(); } catch (Exception e) { Logger.LogError(e); Kit.Broken(); }
            Guard(Wheel.Ensure);
            Guard(Spindle.Tick);
            Guard(Tools.Tick);
            Guard(Icon.Tick);
        }
        private static float _quietUntil;
        internal static void Guard(Action a)
        {
            try { a(); }
            catch (Exception e) { if (UnityEngine.Time.unscaledTime > _quietUntil) { _quietUntil = UnityEngine.Time.unscaledTime + 10; Instance?.Logger.LogError(e); } }
        }
        internal static void Safe(string what, Action part)
        {
            try { part(); } catch (Exception e) { Instance?.Logger.LogError(what + ": " + e); }
        }
        internal static void Log(string text) => Instance?.Logger.LogInfo(text);
        internal static void Warn(string text) => Instance?.Logger.LogWarning(text);
    }

    // Items and recipes go in wherever the game builds its lists: the scene and the item database wake in either order, and a world
    // load swaps the database's lists.
    [HarmonyPatch(typeof(ZNetScene), "Awake")]
    internal static class SceneAwake { private static void Postfix() => Plugin.Safe("register", Things.Register); }
    [HarmonyPatch(typeof(ObjectDB), "Awake")]
    internal static class DbAwake { private static void Postfix() => Plugin.Safe("register", Things.Register); }
    [HarmonyPatch(typeof(ObjectDB), "CopyOtherDB")]
    internal static class DbCopy { private static void Postfix() => Plugin.Safe("register", Things.Register); }
}
