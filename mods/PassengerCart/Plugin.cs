using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace PassengerCart
{
    [BepInPlugin(Guid,Name,Version)]
    public sealed class Plugin:BaseUnityPlugin
    {
        public const string Guid="com.bobisme.passengercart";
        public const string Name="Passenger Cart";
        public const string Version="0.1.2";
        internal static Plugin Instance;
        private Harmony _harmony;

        private void Awake()
        {
            Instance=this;
            _harmony=new Harmony(Guid);_harmony.PatchAll(typeof(Plugin).Assembly);
            if(ZNetScene.instance!=null)CartPrefab.Register(ZNetScene.instance); // hot reload while in a world
            CartPrefab.Refresh();
            Logger.LogInfo($"{Name} {Version} loaded.");
        }
        internal static void Log(string text)=>Instance?.Logger.LogInfo(text);
        private void Update()=>Tools.Tick();
        private void OnDestroy()
        {
            Tools.Stop();
            _harmony?.UnpatchSelf();
            CartPrefab.Unregister();
            if(Instance==this)Instance=null;
        }
    }

    [HarmonyPatch(typeof(ZNetScene),"Awake")]
    internal static class RegisterCart
    {
        private static void Postfix(ZNetScene __instance)=>CartPrefab.Register(__instance);
    }
    // Whichever of the scene and the item database wakes last puts the cart in the hammer's menu.
    [HarmonyPatch(typeof(ObjectDB),"Awake")]
    internal static class AddToHammer
    {
        private static void Postfix()=>CartPrefab.AddToHammer();
    }
}
