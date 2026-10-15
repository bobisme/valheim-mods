using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

// A restart to install: saved items with recipes and networked projectiles. (`reload Godforged force` works for testing.)
[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload","menu")]

namespace Godforged
{
    // Godforged: the gods' own treasures. Mjölnir and Járngreipr (Thor), Gungnir (Odin); more to come (Draupnir, Brísingamen, Skíðblaðnir,
    // Gjallarhorn...). Standalone, made at the forge; Fimbulwinter builds on it through Api (rewards instead of recipes).
    //
    // Core/ is Wrong Universe's (items copied from game items with models of their own, recipes, icons, RPCs that survive a reload, the
    // `gf` Claude Tools command). Each relic is a Feature in Features/; Throws is the throw that comes back, shared by Mjölnir and Gungnir.
    [BepInPlugin(Guid,Name,Version)]
    public sealed class Plugin:BaseUnityPlugin
    {
        public const string Guid="cx.bob.godforged";
        public const string Name="Godforged";
        public const string Version="0.2.50";
        internal static Plugin Instance;
        internal static ConfigEntry<float> Volume;
        private Harmony _harmony;

        private void Awake()
        {
            Instance=this;
            Volume=Config.Bind("0 - General","Volume",0.9f,new ConfigDescription("How loud the thunder is.",new AcceptableValueRange<float>(0,1)));
            foreach(Feature f in Features.All)Safe(f.Name+" settings",()=>f.Bind(Config));
            Things.BindCosts(Config);
            _harmony=new Harmony(Guid);_harmony.PatchAll(typeof(Plugin).Assembly);
            Things.Register();
            Safe("repoint",()=>Things.Repoint());   // (a hot reload: items already in the world get this copy's prefabs)
            foreach(Feature f in Features.All)Safe(f.Name+" start",f.Start);
            Logger.LogInfo($"{Name} {Version} loaded.");
        }
        internal static void Log(string text)=>Instance?.Logger.LogInfo(text);
        internal static void Warn(string text)=>Instance?.Logger.LogWarning(text);
        internal static void Safe(string what,System.Action part)
        {
            try{part();}catch(System.Exception e){Instance?.Logger.LogError(what+": "+e);}
        }

        // One relic failing mustn't stop the rest; its error is logged at most every 10 s.
        private readonly System.Collections.Generic.Dictionary<string,float> _quietUntil=new System.Collections.Generic.Dictionary<string,float>();
        private void Step(string what,System.Action part)
        {
            try{part();}
            catch(System.Exception e)
            {
                if(_quietUntil.TryGetValue(what,out float t)&&Time.unscaledTime<t)return;
                _quietUntil[what]=Time.unscaledTime+10;Logger.LogError(what+": "+e);
            }
        }
        private void Update()
        {
            Step("tools",Tools.Tick);
            Step("net",Net.Tick);
            foreach(Feature f in Features.All)Step(f.Name,f.Tick);
        }
        private void LateUpdate(){foreach(Feature f in Features.All)Step(f.Name+" late",f.LateTick);}
        private void FixedUpdate(){foreach(Feature f in Features.All)Step(f.Name+" physics",f.FixedTick);}
        private void OnDestroy()
        {
            foreach(Feature f in Features.All)Safe(f.Name+" stop",f.Stop);
            Tools.Stop();
            Net.Stop();
            Sound.Clear();
            _harmony?.UnpatchSelf();
            Things.Unload();
            if(Instance==this)Instance=null;
        }
    }

    [HarmonyPatch(typeof(ZNetScene),"Awake")]
    internal static class SceneAwake{private static void Postfix()=>Things.Register();}
    [HarmonyPatch(typeof(ObjectDB),"Awake")]
    internal static class DbAwake{private static void Postfix()=>Things.Register();}
    [HarmonyPatch(typeof(ObjectDB),"CopyOtherDB")]
    internal static class DbCopy{private static void Postfix()=>Things.Register();}
}
