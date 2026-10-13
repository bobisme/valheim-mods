using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;

namespace Respite
{
    [BepInPlugin(Guid,Name,Version)]
    public sealed class Plugin:BaseUnityPlugin
    {
        public const string Guid="com.bobisme.respite";
        public const string Name="Respite";
        public const string Version="0.1.0";
        internal static Plugin Instance;
        internal ConfigEntry<float> CooldownDays,AreaRadius,WarningSeconds,Margin,LeastDistance;
        internal ConfigEntry<int> MaxPerWeek;
        internal ConfigEntry<bool> GameRaids;
        private Harmony _harmony;

        private void Awake()
        {
            Instance=this;
            CooldownDays=Config.Bind("Raid clock","CooldownDays",1.5f,new ConfigDescription("In-game days after a raid before another may come near the same place. The host's setting counts.",new AcceptableValueRange<float>(0,10)));
            AreaRadius=Config.Bind("Raid clock","AreaRadius",500f,new ConfigDescription("Metres around a raid in which the cooldown holds. Bases farther apart have their own clocks. The host's setting counts.",new AcceptableValueRange<float>(50,5000)));
            MaxPerWeek=Config.Bind("Raid clock","MaxPerWeek",3,new ConfigDescription("Most raids near one place in seven in-game days (0: no cap). The host's setting counts.",new AcceptableValueRange<int>(0,20)));
            WarningSeconds=Config.Bind("Muster","WarningSeconds",90f,new ConfigDescription("Seconds raiders gather outside the base, shown on the map, before they come (0: they come at once).",new AcceptableValueRange<float>(0,600)));
            Margin=Config.Bind("Muster","Margin",40f,new ConfigDescription("Metres past the base's farthest building where raiders gather.",new AcceptableValueRange<float>(10,150)));
            LeastDistance=Config.Bind("Muster","LeastDistance",80f,new ConfigDescription("Raiders never gather nearer the base's heart than this.",new AcceptableValueRange<float>(40,200)));
            GameRaids=Config.Bind("Muster","GameRaids",true,"The game's own raids (and the mods' raids that use them) muster too: no spawns for the warning, then they come from one place outside the base.");
            _harmony=new Harmony(Guid);_harmony.PatchAll(typeof(Plugin).Assembly);
            Logger.LogInfo($"{Name} {Version} loaded.");
        }
        private void Update()
        {
            try{Ledger.Tick();}catch(System.Exception e){Logger.LogError("Respite ledger: "+e);}
            try{Muster.Tick();}catch(System.Exception e){Logger.LogError("Respite muster: "+e);}
        }
        private void OnDestroy(){Muster.Clear();Ledger.Unregister();_harmony?.UnpatchSelf();if(Instance==this)Instance=null;}
        internal static void Log(string text)=>Instance?.Logger.LogInfo(text);
    }
}
