using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

// A restart to install (the horn is a saved item with a recipe); the courier himself is never saved.
[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload","menu")]

namespace Delivery
{
    [BepInPlugin(Guid,Name,Version)]
    public sealed class Plugin:BaseUnityPlugin
    {
        public const string Guid="com.bobisme.delivery";
        public const string Name="Delivery";
        public const string Version="0.1.4";
        internal static Plugin Instance;
        internal static ConfigEntry<float> WaitSeconds,CourierDistance,Earshot,Volume,TipWait,GiveUpSeconds;
        internal static ConfigEntry<bool> OnlyKnown,ShowOnMap;
        private Harmony _harmony;

        private void Awake()
        {
            Instance=this;
            WaitSeconds=Config.Bind("Courier","WaitSeconds",60f,new ConfigDescription("How long the courier takes to set out, in seconds, when he thinks middling well of you. Half again as long when he's sullen, half as long when he's devoted.",new AcceptableValueRange<float>(5,600)));
            CourierDistance=Config.Bind("Courier","Distance",90f,new ConfigDescription("How far off he starts his run, in metres.",new AcceptableValueRange<float>(30,200)));
            TipWait=Config.Bind("Courier","TipWait",25f,new ConfigDescription("How long he waits on a tip, in seconds.",new AcceptableValueRange<float>(5,120)));
            GiveUpSeconds=Config.Bind("Courier","StraightAfter",90f,new ConfigDescription("After this many seconds on his way, he stops going round things and comes straight through.",new AcceptableValueRange<float>(20,600)));
            ShowOnMap=Config.Bind("Courier","ShowOnMap",true,"Your courier shows on the map and minimap while he's out.");
            OnlyKnown=Config.Bind("Menu","OnlyKnownMeals",true,"Only meals you know how to make (or have had in hand) can be ordered.");
            Earshot=Config.Bind("Horn","Earshot",120f,new ConfigDescription("How far other players hear your horn, in metres.",new AcceptableValueRange<float>(20,400)));
            Volume=Config.Bind("Horn","Volume",0.8f,new ConfigDescription("How loud the horn is.",new AcceptableValueRange<float>(0,1)));
            _harmony=new Harmony(Guid);_harmony.PatchAll(typeof(Plugin).Assembly);
            Items.RegisterLoaded();
            Logger.LogInfo($"{Name} {Version} loaded.");
        }
        internal static void Log(string text)=>Instance?.Logger.LogInfo(text);
        // One part failing mustn't stop the rest (a courier left running with no one to move him).
        private float _quietUntil;
        private void Step(System.Action part)
        {
            try{part();}
            catch(System.Exception e){if(Time.unscaledTime>_quietUntil){_quietUntil=Time.unscaledTime+10;Logger.LogError(e);}}
        }
        private void Update()
        {
            Step(Tools.Tick);
            Step(Calls.Tick);
            Step(OrderMenu.Tick);
            Step(Tip.Tick);
            Step(Run.Tick);
        }
        private void OnDestroy()
        {
            Tools.Stop();
            OrderMenu.Close();
            Run.End();
            Calls.Stop();
            Body.Forget();
            GameUi.Destroy();
            _harmony?.UnpatchSelf();
            Items.Unregister();
            if(Instance==this)Instance=null;
        }
    }

    [HarmonyPatch(typeof(ZNetScene),"Awake")]
    internal static class RegisterScene
    {
        private static void Postfix(ZNetScene __instance)=>Items.RegisterScene(__instance);
    }
    [HarmonyPatch(typeof(Game),"Start")]
    internal static class NewWorld
    {
        private static void Postfix(){Run.End();Calls.Reset();Meals.Forget();}
    }

    // Using the horn (from the hotbar or the inventory) opens its menu.
    [HarmonyPatch(typeof(Humanoid),nameof(Humanoid.UseItem))]
    internal static class BlowHorn
    {
        private static bool Prefix(Humanoid __instance,ItemDrop.ItemData item)
        {
            if(!Items.Is(item,Items.Horn)||!(__instance is Player me)||me!=Player.m_localPlayer)return true;
            OrderMenu.Open();
            return false;
        }
    }
}
