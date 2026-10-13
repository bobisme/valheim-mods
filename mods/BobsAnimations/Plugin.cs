using System;
using System.Reflection;
using BepInEx;
using UnityEngine;

[assembly: AssemblyMetadata("ClaudeTools.Reload","world")]

namespace BobsAnimations
{
    // Bob's Animations: keyframed clips (made in Blender, see Authoring/) played over a character's own animation, a bone at a time, so a
    // villager in a chair keeps sitting while their arms lift a tankard. Other mods call Anim.Play; what plays is kept on the character's
    // network object, so every game near it shows the same.
    [BepInPlugin(Guid,Name,Version)]
    public sealed class Plugin:BaseUnityPlugin
    {
        public const string Guid="com.bobisme.animations";
        public const string Name="Bob's Animations";
        public const string Version="0.3.4";
        internal static Plugin Instance;

        private void Awake()
        {
            Instance=this;
            Logger.LogInfo($"{Name} {Version} loaded: {Clip.Names().Length} clips ({string.Join(", ",Clip.Names())}).");
        }
        private void OnDestroy()
        {
            Playback.Clear();Tools.Stop();
            if(Instance==this)Instance=null;
        }
        private void Update()
        {
            try{Tools.Tick();Playback.Scan();}
            catch(Exception e){Error(e);}
        }
        private void LateUpdate()
        {
            try{Playback.LateUpdate();}
            catch(Exception e){Error(e);Playback.Clear();}
        }
        internal void Error(Exception e)=>Logger.LogError(e);
        internal static void Log(string text)=>Instance?.Logger.LogInfo(text);
        internal static void Warn(string text)=>Instance?.Logger.LogWarning(text);
    }
}
