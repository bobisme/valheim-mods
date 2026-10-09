using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace Shieldwall
{
    [BepInPlugin(Guid,Name,Version)]
    public sealed class Plugin:BaseUnityPlugin
    {
        public const string Guid="com.bobisme.shieldwall";
        public const string Name="Shieldwall";
        public const string Version="0.1.0";
        internal static Plugin Instance;
        internal ConfigEntry<bool> DrawRaids;
        internal ConfigEntry<float> HornWarning,RaidWarning,WaveSeconds,MaxMinutes,CooldownMinutes;
        internal ConfigEntry<int> MaxAlive;
        private Harmony _harmony;

        private void Awake()
        {
            Instance=this;
            DrawRaids=Config.Bind("Sieges","DrawRaids",true,"A base raid near a Warstone becomes a siege at the stone instead. Only the host's setting matters (a vanilla dedicated server raids as usual).");
            HornWarning=Config.Bind("Sieges","HornWarningSeconds",90f,new ConfigDescription("Seconds between sounding the horn and the horde marching: time to get ready.",new AcceptableValueRange<float>(15,600)));
            RaidWarning=Config.Bind("Sieges","RaidWarningSeconds",180f,new ConfigDescription("Seconds of warning when the stone draws a raid to itself.",new AcceptableValueRange<float>(30,900)));
            WaveSeconds=Config.Bind("Sieges","WaveSeconds",50f,new ConfigDescription("Most seconds between waves (the next comes sooner when the last is mostly down).",new AcceptableValueRange<float>(20,300)));
            MaxAlive=Config.Bind("Sieges","MaxAlive",40,new ConfigDescription("Most raiders on the field at once; the rest wait in the rift. Lower it if sieges stutter.",new AcceptableValueRange<int>(8,120)));
            MaxMinutes=Config.Bind("Sieges","MaxMinutes",25f,new ConfigDescription("A siege still standing after this many minutes is won: the horde gives up.",new AcceptableValueRange<float>(5,90)));
            CooldownMinutes=Config.Bind("Sieges","CooldownMinutes",20f,new ConfigDescription("Real minutes after a siege before the horn can be sounded again.",new AcceptableValueRange<float>(0,240)));
            _harmony=new Harmony(Guid);_harmony.PatchAll(typeof(Plugin).Assembly);
            Items.RegisterLoaded(); // hot reload while in a world
            Warstone.AttachAll();Planted.AttachAll();Raider.AttachAll();
            Logger.LogInfo($"{Name} {Version} loaded.");
        }
        internal static void Log(string text)=>Instance?.Logger.LogInfo(text);
        private void Update()
        {
            Net.Tick();
            Tools.Tick();
            try{Ward.Tick();Route.Tick();}catch(System.Exception e){Logger.LogError("Shieldwall: "+e);}
        }
        private void OnGUI(){try{SiegeBar.Draw();}catch(System.Exception){}}
        private void OnDestroy()
        {
            Tools.Stop();
            Net.Unregister();
            _harmony?.UnpatchSelf();
            Route.Clear();Ward.Clear();Director.Reset();SiegeBar.Clear();
            Warstone.DetachAll();Planted.DetachAll();Raider.DetachAll();
            Items.Unregister();
            Assets.UnregisterAll();
            Stone.Forget();Planted.Forget();
            if(Instance==this)Instance=null;
        }
    }

    [HarmonyPatch(typeof(ZNetScene),"Awake")]
    internal static class RegisterPrefabs
    {
        private static void Postfix(ZNetScene __instance)=>Items.RegisterScene(__instance);
    }
    [HarmonyPatch(typeof(ZNet),"Shutdown")]
    internal static class ForgetWorld
    {
        private static void Prefix(){Director.Reset();Route.Clear();}
    }
}
