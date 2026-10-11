using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

// Claude Tools may reload it in a world: the piece stays registered across a reload and standing portals get the new code (PortalPrefab).
[assembly: System.Reflection.AssemblyMetadata("ClaudeTools.Reload","world")]

namespace LocalPortals
{
    [BepInPlugin(Guid,Name,Version)]
    public sealed class Plugin:BaseUnityPlugin
    {
        public const string Guid="com.bobisme.localportals";
        public const string Name="Looking-Glass Portals";
        public const string Version="0.2.1";
        internal static Plugin Instance;
        internal static ConfigEntry<int> MaxViews;
        internal static ConfigEntry<float> Resolution;
        internal static ConfigEntry<bool> BodyDoubles;
        internal static ConfigEntry<float> MaxDistance;
        internal static ConfigEntry<bool> Preload;
        internal static ServerSettings Synced;
        private Harmony _harmony;

        private void Awake()
        {
            Instance=this;
            MaxViews=Config.Bind("Views","MaxViews",2,new ConfigDescription("How many portals show a live view at once (the nearest ones the camera sees). Each one draws the world again, so more costs frame rate.",new AcceptableValueRange<int>(0,8)));
            Resolution=Config.Bind("Views","Resolution",1f,new ConfigDescription("Sharpness of the views: 1 matches the screen; lower is blurrier and faster.",new AcceptableValueRange<float>(0.25f,1f)));
            BodyDoubles=Config.Bind("Views","BodyDoubles",true,"While someone is partway through a portal, show the part of them that has gone through coming out of the other mirror.");
            Synced=new ServerSettings(Guid,Config,Logger);
            MaxDistance=Synced.Add(Config.Bind("Linking","MaxDistance",200f,new ConfigDescription("The farthest apart two mirrors link, in metres. A mirror farther than about 100 m from you has the area round it kept loaded while you are near its partner (about the same few zones at any distance). [Synced with Server]",new AcceptableValueRange<float>((float)Policy.MinLinkRange,(float)Policy.MaxLinkRange))));
            Preload=Config.Bind("Linking","PreloadOnApproach",true,"Within 25 m of a mirror whose partner is far off, load all of the area round the partner (about 9 more zones), so stepping through is smooth. Off: only the partner's own zone or few, and a short hitch on arriving. (The host's or single player's setting: only they load far areas.)");
            Policy.LinkRange=MaxDistance.Value;
            MaxDistance.SettingChanged+=(s,e)=>Policy.LinkRange=MaxDistance.Value;
            _harmony=new Harmony(Guid);_harmony.PatchAll(typeof(Plugin).Assembly);
            if(ZNetScene.instance!=null)PortalPrefab.Register(ZNetScene.instance); // hot reload while in a world
            PortalPrefab.Refresh();
            Views.Start();
            Logger.LogInfo($"{Name} {Version} loaded.");
        }
        internal static void Log(string text)=>Instance?.Logger.LogInfo(text);
        private void Update()
        {
            Synced?.Update();
            Tools.Tick();
            Far.Tick();
            Crossing.Tick();
            Creatures.Tick();
        }
        private void FixedUpdate()=>Missiles.Tick();
        private void OnDestroy()
        {
            Tools.Stop();
            Synced?.Dispose();
            Views.Stop();
            Doubles.Stop();
            Crossing.Stop();
            Creatures.Stop();
            Far.Stop();
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
