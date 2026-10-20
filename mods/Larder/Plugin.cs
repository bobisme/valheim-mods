using System;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

// A restart to install: new items with recipes (the game only reads recipes at the main menu). `reload Larder force` works for testing.
[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload", "menu")]

namespace Larder
{
    // Larder: food that keeps. The dairy: a butter churn (butter, and skyr with blueberries) and a cheese press (cheese, set a milk at a
    // time), from Byre's goat's and cow's milk; without Byre there's no milk and they stay out of the hammer's menu. Preserving: a
    // smokehouse (smoked meat), a salt pan in the shallows (salt), a salting barrel (salt meat) and a drying rack under the open sky
    // (stockfish), for long voyages. What each takes and gives is Core/Dairy.cs and Core/Preserves.cs; Things.cs makes the foods and the
    // churn's recipes, Kit.cs the stations, Preserving.cs what the rack and the pan need where they stand.
    [BepInPlugin(Guid, Name, Version)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "cx.bob.larder";
        public const string Name = "Larder";
        public const string Version = "0.2.0";
        internal static Plugin Instance;
        internal ConfigEntry<bool> Enabled;
        private Harmony _harmony;

        private void Awake()
        {
            Instance = this;
            Enabled = Config.Bind("General", "Enabled", true, "Put Larder's stations in the hammer's Crafting tab: the smokehouse, salt pan, salting barrel and drying rack, and the butter churn and cheese press (with Byre's milk to use in them).");
            _harmony = new Harmony(Guid); _harmony.PatchAll(typeof(Plugin).Assembly);
            Safe("register", Things.Register);
            Safe("repoint", Things.Repoint); // (a hot reload: items already in the world get this copy's prefabs)
            Logger.LogInfo($"{Name} {Version} loaded: a smokehouse, salt pan, salting barrel and drying rack in the hammer's Crafting tab, and a butter churn and cheese press for Byre's milk.");
        }
        private void OnDestroy()
        {
            Kit.Unload();
            Tools.Stop();
            _harmony?.UnpatchSelf();
            Things.Unload();
            if (Instance == this) Instance = null;
        }
        private void Update()
        {
            try { Kit.Ensure(); } catch (Exception e) { Logger.LogError(e); Kit.Broken(); }
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
