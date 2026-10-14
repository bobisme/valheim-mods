using System;
using System.Reflection;
using UnityEngine;

namespace BobsAnimations
{
    // The game's cloth (Magica Cloth 2: hair, dresses, capes) reads the skeleton just before the scripts' LateUpdate, simulates while they
    // run, and is skinned after. Clips posed in LateUpdate came too late: a crouching villager's pigtails hung from where her head would
    // have been standing. So clips are posed in Magica's own hook ahead of that read, which comes after the game's animation; the
    // plugin's LateUpdate poses only when the hook isn't there (or didn't run this frame).
    internal static class ClothHook
    {
        private static FieldInfo _field;
        private static Delegate _ours;
        internal static int PosedFrame=-1;

        internal static void Install()
        {
            if(_ours!=null)return;
            try
            {
                Type manager=null;
                foreach(Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                    if(a.GetName().Name=="MagicaClothV2"){manager=a.GetType("MagicaCloth2.MagicaManager");break;}
                _field=manager?.GetField("beforeLateUpdateDelegate",BindingFlags.Public|BindingFlags.Static);
                if(_field==null)return;
                _ours=Delegate.CreateDelegate(_field.FieldType,typeof(ClothHook).GetMethod(nameof(Pose),BindingFlags.NonPublic|BindingFlags.Static));
                // first in line: ahead of the cloth's own read of the bones
                _field.SetValue(null,Delegate.Combine(_ours,(Delegate)_field.GetValue(null)));
            }
            catch(Exception e){Plugin.Warn("Bob's Animations: no cloth hook: "+e.Message);_ours=null;}
        }
        internal static void Remove()
        {
            try{if(_field!=null&&_ours!=null)_field.SetValue(null,Delegate.Remove((Delegate)_field.GetValue(null),_ours));}
            catch(Exception){}
            _ours=null;
        }
        private static void Pose()
        {
            try{Playback.LateUpdate();PosedFrame=Time.frameCount;}
            catch(Exception e){Plugin.Instance?.Error(e);Playback.Clear();}
        }
    }
}
