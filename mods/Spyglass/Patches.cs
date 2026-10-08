using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Spyglass
{
    [HarmonyPatch(typeof(ZNetScene),"Awake")]
    internal static class RegisterScene
    {
        private static void Postfix(ZNetScene __instance)=>Item.RegisterScene(__instance);
    }
    // Runs for ObjectDB.Awake and CopyOtherDB, whichever order the world wakes in.
    [HarmonyPatch(typeof(ObjectDB),"UpdateRegisters")]
    internal static class RegisterItem
    {
        private static void Prefix(ObjectDB __instance)=>Item.BeforeRegisters(__instance);
    }
    [HarmonyPatch(typeof(GameCamera),"UpdateCamera")]
    internal static class LookThrough
    {
        private static readonly AccessTools.FieldRef<GameCamera,Camera> View=AccessTools.FieldRefAccess<GameCamera,Camera>("m_camera");
        private static void Postfix(GameCamera __instance,float dt)=>Zoom.Apply(__instance,View(__instance),dt);
    }
    // Slower turning at high magnification, so the aim crosses the screen at the usual speed.
    [HarmonyPatch(typeof(PlayerController),"LateUpdate")]
    internal static class SteadyLook
    {
        private static void Prefix(out float __state){__state=PlayerController.m_mouseSens;PlayerController.m_mouseSens*=Zoom.LookScale();}
        private static void Finalizer(float __state){PlayerController.m_mouseSens=__state;}
    }
    // The wheel changes magnification instead of the camera distance.
    [HarmonyPatch(typeof(ZInput),nameof(ZInput.GetMouseScrollWheel))]
    internal static class WheelZoom
    {
        private static void Postfix(ref float __result){if(Plugin.Zooming&&!Zoom.ReadingScroll)__result=0;}
    }
    [HarmonyPatch(typeof(Menu),"Update")]
    internal static class KeepEscape
    {
        private static bool Prefix()=>!Plugin.ReservesEscape;
    }

    // Other mods' shortcut helpers (GearSlots, QualityOfLife, BuildOrders: static bool Pressed(KeyboardShortcut)) accept extra held keys,
    // so their plain Z would also fire on Shift+Z. While the whole spyglass shortcut is held, a shorter one on the same key reports false.
    // Mods hot-reload as new assemblies, so assemblies loaded later are checked too.
    internal static class ShortcutGuard
    {
        private static Harmony _harmony;
        private static ManualLogSource _log;
        private static readonly Queue<Assembly> Pending=new Queue<Assembly>();
        private static readonly HashSet<MethodInfo> Patched=new HashSet<MethodInfo>();
        private static readonly string[] Skip={"System","Mono.","mscorlib","netstandard","Unity","assembly_","BepInEx","0Harmony","Newtonsoft","Splatform","HarmonyX","MonoMod"};

        internal static void Start(Harmony harmony,ManualLogSource log)
        {
            _harmony=harmony;_log=log;
            AppDomain.CurrentDomain.AssemblyLoad+=Loaded;
            // Each hot reload leaves the old copy loaded under a new name (GearSlots-<ticks>); only the last-loaded copy still runs.
            Assembly[] loaded=AppDomain.CurrentDomain.GetAssemblies();
            lock(Pending)foreach(Assembly assembly in loaded.Where((a,i)=>!loaded.Skip(i+1).Any(b=>ModName(b)==ModName(a))))Pending.Enqueue(assembly);
        }
        private static string ModName(Assembly assembly)=>Policy.ModName(assembly.GetName().Name);
        internal static void Stop(){AppDomain.CurrentDomain.AssemblyLoad-=Loaded;lock(Pending)Pending.Clear();Patched.Clear();_harmony=null;}
        private static void Loaded(object sender,AssemblyLoadEventArgs e){lock(Pending)Pending.Enqueue(e.LoadedAssembly);}
        internal static void Tick()
        {
            if(_harmony==null)return;
            while(true)
            {
                Assembly assembly;
                lock(Pending){if(Pending.Count==0)return;assembly=Pending.Dequeue();}
                try{Scan(assembly);}
                catch(Exception e){_log?.LogWarning($"Could not check {assembly.GetName().Name} for shortcut helpers: {e.Message}");}
            }
        }
        private static void Scan(Assembly assembly)
        {
            if(assembly==typeof(ShortcutGuard).Assembly||assembly.IsDynamic)return;
            string name=assembly.GetName().Name;
            if(Skip.Any(name.StartsWith)||!assembly.GetReferencedAssemblies().Any(r=>r.Name=="BepInEx"))return;
            Type[] types;
            try{types=assembly.GetTypes();}catch(ReflectionTypeLoadException e){types=e.Types.Where(t=>t!=null).ToArray();}
            foreach(Type type in types)
            foreach(MethodInfo method in type.GetMethods(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly))
            {
                if(method.Name!="Pressed"||method.ReturnType!=typeof(bool)||method.IsGenericMethodDefinition)continue;
                ParameterInfo[] parameters=method.GetParameters();
                if(parameters.Length!=1||parameters[0].ParameterType!=typeof(KeyboardShortcut)||!Patched.Add(method))continue;
                _harmony.Patch(method,postfix:new HarmonyMethod(typeof(ShortcutGuard),nameof(Postfix)));
                _log?.LogInfo($"{Plugin.Instance?.Toggle.Value} takes priority over shorter shortcuts in {type.FullName}.{method.Name}.");
            }
        }
        private static void Postfix(object[] __args,ref bool __result)
        {
            if(!__result||Plugin.Instance==null||!(__args[0] is KeyboardShortcut theirs))return;
            KeyboardShortcut ours=Plugin.Instance.Toggle.Value;
            if(Policy.Shadows((int)ours.MainKey,ours.Modifiers.Select(k=>(int)k),(int)theirs.MainKey,theirs.Modifiers.Select(k=>(int)k),k=>Input.GetKey((KeyCode)k)))
                __result=false;
        }
    }
}
