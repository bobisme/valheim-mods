using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace Omens
{
    [BepInPlugin(Guid,Name,Version)]
    public sealed class Plugin:BaseUnityPlugin
    {
        public const string Guid="com.bobisme.omens";
        public const string Name="Omens";
        public const string Version="0.4.0";
        internal static Plugin Instance;
        internal ConfigEntry<bool> Enabled;
        private readonly Dictionary<Kind,ConfigEntry<bool>> _omens=new Dictionary<Kind,ConfigEntry<bool>>();
        internal ConfigEntry<float> IntervalDays,BadChance,ExpireDays,BaseRange;
        internal ConfigEntry<int> MaxActive;
        private Harmony _harmony;

        private void Awake()
        {
            Instance=this;
            Enabled=Config.Bind("General","Enabled",true,"Place omens near players. Only the host's setting matters.");
            IntervalDays=Config.Bind("General","IntervalDays",1.5f,new ConfigDescription("Average in-game days between omens (randomized 0.75–1.25×).",new AcceptableValueRange<float>(0.25f,10)));
            BadChance=Config.Bind("General","BadChance",0.6f,new ConfigDescription("Chance an omen is bad. Worlds with raids turned off get only good omens.",new AcceptableValueRange<float>(0,1)));
            ExpireDays=Config.Bind("General","ExpireDays",1f,new ConfigDescription("In-game days an unseen omen waits before fading without effect.",new AcceptableValueRange<float>(0.25f,5)));
            MaxActive=Config.Bind("General","MaxActive",2,new ConfigDescription("Most omens waiting to be seen or come to pass at once.",new AcceptableValueRange<int>(1,5)));
            BaseRange=Config.Bind("General","BaseRange",1500f,new ConfigDescription("A bad omen's raid goes to the nearest base (workbench or bed) within this many metres of the sign; with none, it fizzles.",new AcceptableValueRange<float>(200,5000)));
            // One switch per omen, named after its kind (the names earlier versions used).
            foreach(Omen omen in Policy.All)_omens[omen.Kind]=Config.Bind("Omens",omen.Kind.ToString(),true,omen.Config);
            _harmony=new Harmony(Guid);_harmony.PatchAll(typeof(Plugin).Assembly);
            if(ZNetScene.instance!=null)SignPrefab.Register(ZNetScene.instance); // hot reload while in a world
            OmenSign.AttachAll();
            Logger.LogInfo($"{Name} {Version} loaded.");
        }
        internal List<Kind> EnabledKinds()=>Policy.All.Where(o=>_omens.TryGetValue(o.Kind,out var on)&&on.Value).Select(o=>o.Kind).ToList();
        internal static void Log(string text)=>Instance?.Logger.LogInfo(text);
        private void Update()
        {
            Net.Tick();
            BloodMoon.Tick();
            Tools.Tick();
            try{Director.Tick();}catch(System.Exception e){Logger.LogError("Omens director: "+e);}
        }
        private void OnDestroy()
        {
            Director.Reset();
            OmenSign.DetachAll();
            Tools.Stop();
            Net.Unregister();
            _harmony?.UnpatchSelf();
            SignPrefab.Unregister();
            Looks.Clear();
            Favour.Clear();
            if(Instance==this)Instance=null;
        }
    }

    [HarmonyPatch(typeof(ZNetScene),"Awake")]
    internal static class RegisterSign
    {
        private static void Postfix(ZNetScene __instance)=>SignPrefab.Register(__instance);
    }
    // A new world or a return to the main menu: forget the previous world's ledger.
    [HarmonyPatch(typeof(ZNet),"Shutdown")]
    internal static class ForgetWorld
    {
        private static void Prefix()=>Director.Reset();
    }
}
