using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

// A restart to install: saved items with recipes and build pieces. (`reload Factorheim force` works for testing.)
[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload","menu")]

namespace Factorheim
{
    // Factorheim: Factorio in the tenth world. Burner drills on copper veins, belts and inserters, steam engines and power poles,
    // assemblers, tar refining, rune science and research, and at the end a rocket silo: one rocket, one ride, straight to Valhalla.
    //
    // Core/ is shared: Things (items), Pieces (hammer pieces), Machine (stores, fuel, power, hover, ZDO), Ports (moving items between
    // machines, belts, chests, smelters, the ground), Crafts (machine recipes), Tech (research tiers), ItemView (item pictures on belts),
    // Look/Icon/Meshes/Sound/Hud/GameUi, Net, Tools (the `fh` Claude Tools command). Each part of the factory is a Feature in Features/.
    // DESIGN.md has the tech tree, the items and who makes what.
    [BepInPlugin(Guid,Name,Version)]
    public sealed class Plugin:BaseUnityPlugin
    {
        public const string Guid="cx.bob.factorheim";
        public const string Name="Factorheim";
        public const string Version="0.1.2";
        internal static Plugin Instance;
        internal static ConfigFile Cfg;
        internal static ConfigEntry<float> Volume;
        private Harmony _harmony;

        private void Awake()
        {
            Instance=this;Cfg=Config;
            Volume=Config.Bind("0 - General","Volume",0.7f,new ConfigDescription("How loud the machines are.",new AcceptableValueRange<float>(0,1)));
            Tech.Bind(Config);
            foreach(Feature f in Features.All)Safe(f.Name+" settings",()=>f.Bind(Config));
            Things.BindCosts(Config);
            Pieces.BindCosts(Config);
            Tech.Changed+=Things.Retier;
            GameUi.IsOpen=()=>Features.All.Any(f=>f.WindowOpen);
            GameUi_EscapeCloses.Close=()=>{foreach(Feature f in Features.All)Safe(f.Name+" close",f.CloseWindow);};
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

        // One feature failing mustn't stop the rest; its error is logged at most every 10 s.
        private readonly System.Collections.Generic.Dictionary<string,float> _quietUntil=new System.Collections.Generic.Dictionary<string,float>();
        internal void Step(string what,System.Action part)
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
            Step("tech",Tech.Tick);
            Step("pieces",Pieces.Tick);
            foreach(Feature f in Features.All)Step(f.Name,f.Tick);
        }
        private void LateUpdate(){foreach(Feature f in Features.All)Step(f.Name+" late",f.LateTick);}
        private void FixedUpdate(){foreach(Feature f in Features.All)Step(f.Name+" physics",f.FixedTick);}
        private void OnGUI(){if(Event.current.type!=EventType.Repaint)return;Hud.Begin();foreach(Feature f in Features.All)Step(f.Name+" gui",f.Gui);}
        private void OnDestroy()
        {
            foreach(Feature f in Features.All){Safe(f.Name+" close",f.CloseWindow);Safe(f.Name+" stop",f.Stop);}
            GameUi.Destroy();
            Tools.Stop();
            Net.Stop();
            Sound.Clear();
            _harmony?.UnpatchSelf();
            Pieces.Unload();
            Things.Unload();
            ItemView.Clear();
            Live<Machine>.Clear();
            Tech.Reset();
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
