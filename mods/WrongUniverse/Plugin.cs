using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

// A restart to install: saved items with recipes, a build piece and networked prefabs. (`reload WrongUniverse force` works for testing.)
[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload","menu")]

namespace WrongUniverse
{
    // Wrong Universe: somebody's sci-fi kit fell through a crack into the tenth world. Jetpacks, laser guns and swords, drones, an orbital
    // ion cannon, infrared goggles, a hover speeder and other things that have no business here.
    //
    // Core/ is shared: Things (items, recipes, prefabs), Look (materials, model building), Icon, Beam (laser beams seen by everyone), Hits
    // (damage), Net (RPCs that survive a reload), Tools (the `wu` Claude Tools command). Each gadget is a Feature in Features/.
    [BepInPlugin(Guid,Name,Version)]
    public sealed class Plugin:BaseUnityPlugin
    {
        public const string Guid="cx.bob.wronguniverse";
        public const string Name="Wrong Universe";
        public const string Version="0.2.7";
        internal static Plugin Instance;
        internal static ConfigFile Cfg;
        internal static ConfigEntry<float> Volume;
        private Harmony _harmony;

        private void Awake()
        {
            Instance=this;Cfg=Config;
            Volume=Config.Bind("0 - General","Volume",0.8f,new ConfigDescription("How loud the gadgets are.",new AcceptableValueRange<float>(0,1)));
            foreach(Feature f in Features.All)Safe(f.Name+" settings",()=>f.Bind(Config));
            Things.BindCosts(Config);
            _harmony=new Harmony(Guid);_harmony.PatchAll(typeof(Plugin).Assembly);
            Things.Register();
            Safe("repoint",()=>Things.Repoint());   // (a hot reload: items already in the world get this copy's prefabs)
            Beam.Start();
            foreach(Feature f in Features.All)Safe(f.Name+" start",f.Start);
            Logger.LogInfo($"{Name} {Version} loaded.");
        }
        internal static void Log(string text)=>Instance?.Logger.LogInfo(text);
        internal static void Warn(string text)=>Instance?.Logger.LogWarning(text);
        internal static void Safe(string what,System.Action part)
        {
            try{part();}catch(System.Exception e){Instance?.Logger.LogError(what+": "+e);}
        }

        // One gadget failing mustn't stop the rest; its error is logged at most every 10 s.
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
            Step("beams",Beam.Tick);
            foreach(Feature f in Features.All)Step(f.Name,f.Tick);
        }
        private void LateUpdate(){foreach(Feature f in Features.All)Step(f.Name+" late",f.LateTick);}
        private void FixedUpdate(){foreach(Feature f in Features.All)Step(f.Name+" physics",f.FixedTick);}
        private void OnGUI(){if(Event.current.type!=EventType.Repaint)return;Hud.Begin();foreach(Feature f in Features.All)Step(f.Name+" gui",f.Gui);}
        private void OnDestroy()
        {
            foreach(Feature f in Features.All)Safe(f.Name+" stop",f.Stop);
            Tools.Stop();
            Net.Stop();
            Beam.Clear();
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
