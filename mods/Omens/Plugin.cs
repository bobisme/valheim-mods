using System.Collections.Generic;
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
        public const string Version="0.1.2";
        internal static Plugin Instance;
        internal ConfigEntry<bool> Enabled,DeadTroll,Ravens,AbandonedCamp;
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
            DeadTroll=Config.Bind("Omens","DeadTroll",true,"Black Forest: a dead troll. Unless burned with resin, trolls raid the nearest base that night.");
            Ravens=Config.Bind("Omens","Ravens",true,"Ravens circling: players nearby are rested and the land around is revealed on their map.");
            AbandonedCamp=Config.Bind("Omens","AbandonedCamp",true,"Meadows or Black Forest: a cold, abandoned camp. Greydwarfs raid the nearest base that night.");
            _harmony=new Harmony(Guid);_harmony.PatchAll(typeof(Plugin).Assembly);
            if(ZNetScene.instance!=null)SignPrefab.Register(ZNetScene.instance); // hot reload while in a world
            Logger.LogInfo($"{Name} {Version} loaded.");
        }
        internal List<Kind> EnabledKinds()
        {
            var kinds=new List<Kind>();
            if(DeadTroll.Value)kinds.Add(Kind.DeadTroll);
            if(Ravens.Value)kinds.Add(Kind.Ravens);
            if(AbandonedCamp.Value)kinds.Add(Kind.AbandonedCamp);
            return kinds;
        }
        internal static void Log(string text)=>Instance?.Logger.LogInfo(text);
        private void Update()
        {
            Net.Tick();
            Tools.Tick();
            try{Director.Tick();}catch(System.Exception e){Logger.LogError("Omens director: "+e);}
        }
        private void OnDestroy()
        {
            Director.Reset();
            Tools.Stop();
            Net.Unregister();
            _harmony?.UnpatchSelf();
            SignPrefab.Unregister();
            Looks.Clear();
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
