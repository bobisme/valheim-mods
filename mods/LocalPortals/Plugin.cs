using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace LocalPortals
{
    [BepInPlugin(Guid,Name,Version)]
    public sealed class Plugin:BaseUnityPlugin
    {
        public const string Guid="com.bobisme.localportals";
        public const string Name="Local Portals";
        public const string Version="0.1.4";
        internal static Plugin Instance;
        internal static ConfigEntry<int> MaxViews;
        internal static ConfigEntry<float> Resolution;
        internal static ConfigEntry<bool> BodyDoubles;
        private Harmony _harmony;

        private void Awake()
        {
            Instance=this;
            MaxViews=Config.Bind("Views","MaxViews",2,new ConfigDescription("How many portals show a live view at once (the nearest ones the camera sees). Each one draws the world again, so more costs frame rate.",new AcceptableValueRange<int>(0,8)));
            Resolution=Config.Bind("Views","Resolution",1f,new ConfigDescription("Sharpness of the views: 1 matches the screen; lower is blurrier and faster.",new AcceptableValueRange<float>(0.25f,1f)));
            BodyDoubles=Config.Bind("Views","BodyDoubles",true,"While someone is partway through a portal, show the part of them that has gone through coming out of the other mirror.");
            _harmony=new Harmony(Guid);_harmony.PatchAll(typeof(Plugin).Assembly);
            if(ZNetScene.instance!=null)PortalPrefab.Register(ZNetScene.instance); // hot reload while in a world
            PortalPrefab.Refresh();
            Views.Start();
            Logger.LogInfo($"{Name} {Version} loaded.");
        }
        internal static void Log(string text)=>Instance?.Logger.LogInfo(text);
        private void Update()
        {
            Tools.Tick();
            Crossing.Tick();
        }
        private void OnDestroy()
        {
            Tools.Stop();
            Views.Stop();
            Doubles.Stop();
            Crossing.Stop();
            _harmony?.UnpatchSelf();
            PortalPrefab.Unregister();
            if(Instance==this)Instance=null;
        }
    }

    [HarmonyPatch(typeof(ZNetScene),"Awake")]
    internal static class RegisterPortal
    {
        private static void Postfix(ZNetScene __instance)=>PortalPrefab.Register(__instance);
    }
    // After the game places its camera, carry it through a portal the player has just stepped out of.
    [HarmonyPatch(typeof(GameCamera),"LateUpdate")]
    internal static class CarryCamera
    {
        private static void Postfix(GameCamera __instance)=>Crossing.CarryCamera(__instance);
    }
    // Keep the camera's wall test from hitting the exit portal's frame when it looks back through the glass.
    [HarmonyPatch(typeof(GameCamera),"CollideRay2")]
    internal static class CameraThroughGlass
    {
        private static bool Prefix(GameCamera __instance,Vector3 offsetedEyePos,ref Vector3 end)=>!Crossing.CollideCamera(__instance,offsetedEyePos,ref end);
        private static void Finalizer()=>Crossing.Reblock();
    }
    // Whichever of the scene and the item database wakes last puts the portal in the hammer's menu.
    [HarmonyPatch(typeof(ObjectDB),"Awake")]
    internal static class AddToHammer
    {
        private static void Postfix()=>PortalPrefab.AddToHammer();
    }
}
