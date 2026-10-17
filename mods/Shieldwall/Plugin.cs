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
        public const string Version = "0.4.24";
        internal static Plugin Instance;
        internal ConfigEntry<bool> DrawRaids,DebugHits,ShowRoad,ShatteredStone;
        internal ConfigEntry<float> HornWarning,RaidWarning,WaveSeconds,BreatherSeconds,MaxMinutes,CooldownMinutes;
        internal ConfigEntry<int> ExtraWaves,MaxAlive;
        private Harmony _harmony;

        private void Awake()
        {
            Instance=this;
            DrawRaids=Config.Bind("Sieges","DrawRaids",true,"A base raid near a Warstone becomes a siege at the stone instead. Only the host's setting matters (a vanilla dedicated server raids as usual).");
            ShowRoad=Config.Bind("Sieges","ShowRoad",false,"Draw a glowing line along the road the horde will take to the stone, and a red one where it will break through a wall. Off: only the rift's beacon and map pin show the way.");
            ShatteredStone=Config.Bind("Sieges","ShatteredStone",false,"A Warstone broken in a siege shatters and is gone, with its marks, level and boons (its upgrades, sockets and staves stand). Off: it only cracks, and its ward is weaker until it holds a siege again. The setting of the game running the siege (the player nearest the stone) decides.");
            ShowRoad.SettingChanged+=(_,__)=>Route.Clear();
            DebugHits=Config.Bind("Debug","LogTowerHits",false,"Log every blow the horde lands on stave sockets and Warstone upgrades (for balancing).");
            HornWarning=Config.Bind("Sieges","HornWarningSeconds",90f,new ConfigDescription("Seconds between sounding the horn and the horde marching: time to get ready.",new AcceptableValueRange<float>(15,600)));
            RaidWarning=Config.Bind("Sieges","RaidWarningSeconds",180f,new ConfigDescription("Seconds of warning when the stone draws a raid to itself.",new AcceptableValueRange<float>(30,900)));
            WaveSeconds=Config.Bind("Sieges","WaveSeconds",40f,new ConfigDescription("Most seconds between waves (the next comes sooner when the last is mostly down).",new AcceptableValueRange<float>(20,300)));
            BreatherSeconds=Config.Bind("Sieges","BreatherSeconds",25f,new ConfigDescription("Seconds of breather between one wave breaking and the next marching (half under No respite), counted down on the siege bar.",new AcceptableValueRange<float>(0,120)));
            ExtraWaves=Config.Bind("Sieges","ExtraWaves",2,new ConfigDescription("Waves added to every siege, each with its own raiders (a new stone's proving has 3 waves, then 4 to 8 as it gains marks, before these).",new AcceptableValueRange<int>(0,8)));
            MaxAlive=Config.Bind("Sieges","MaxAlive",40,new ConfigDescription("Most raiders on the field at once; the rest wait in the rift. Lower it if sieges stutter.",new AcceptableValueRange<int>(8,120)));
            MaxMinutes=Config.Bind("Sieges","MaxMinutes",25f,new ConfigDescription("A siege still standing after this many minutes is won: the horde gives up.",new AcceptableValueRange<float>(5,90)));
            CooldownMinutes=Config.Bind("Sieges","CooldownMinutes",20f,new ConfigDescription("Real minutes after a siege before the horn can be sounded again.",new AcceptableValueRange<float>(0,240)));
            _harmony=new Harmony(Guid);_harmony.PatchAll(typeof(Plugin).Assembly);
            Items.RegisterLoaded(); // hot reload while in a world
            Warstone.AttachAll();Planted.AttachAll();Raider.AttachAll();PitchPit.AttachAll();Spoils.AttachAll();
            Logger.LogInfo($"{Name} {Version} loaded.");
        }
        internal static void Log(string text)=>Instance?.Logger.LogInfo(text);
        private void Update()
        {
            Net.Tick();
            Tools.Tick();
            try{Ward.Tick();Route.Tick();SiegeBar.Tick();Council.Tick();Saga.Tick();}catch(System.Exception e){Logger.LogError("Shieldwall: "+e);}
        }
        private void OnDestroy()
        {
            Council.Close();Saga.Forget();
            Tools.Stop();
            Net.Unregister();
            _harmony?.UnpatchSelf();
            Route.Clear();Ward.Clear();Director.Reset();SiegeBar.Clear();
            Warstone.DetachAll();Planted.DetachAll();Raider.DetachAll();PitchPit.DetachAll();Spoils.DetachAll();
            Items.Unregister();
            Assets.UnregisterAll();
            Stone.Forget();Pieces.Forget();LegacyPlanted.Forget();
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
