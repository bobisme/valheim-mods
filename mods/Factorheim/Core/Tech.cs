using System;
using BepInEx.Configuration;
using UnityEngine;

namespace Factorheim
{
    // How far the world's research has got: tier 0 is known from the start; tiers 1-5 are unlocked by research (the Science feature) and
    // kept as world global keys "fh_tier1".."fh_tier5" (the server keeps them and tells everyone; they survive restarts). Pieces, hand
    // recipes and machine recipes of a tier above Level stay out of the menus.
    internal static class Tech
    {
        internal const int Top=5;
        internal static readonly string[] Names={"Burner age","Automation","Logistics","Tar processing","Eitr works","Rocket silo"};
        internal static ConfigEntry<bool> All;
        private static int _level=-1;private static float _next;
        // Something unlocked (or a world loaded): menus are rebuilt.
        internal static event Action Changed;

        internal static void Bind(ConfigFile config)
        {
            All=config.Bind("0 - General","Unlock everything",false,"Every tier unlocked without research (to try things out; a single player's setting).");
            All.SettingChanged+=(_,__)=>Poll(true);
        }
        internal static int Level{get{if(_level<0)Poll(true);return _level;}}
        internal static bool Unlocked(int tier)=>tier<=Level;

        internal static void Unlock(int tier)
        {
            if(ZoneSystem.instance==null)return;
            for(int t=1;t<=tier&&t<=Top;t++)if(!Has(t))ZoneSystem.instance.SetGlobalKey(Key(t));
            Poll(true);
        }
        // Takes research back (testing): "fh tech 0".
        internal static void Forget(int above)
        {
            if(ZoneSystem.instance==null)return;
            for(int t=Top;t>above;t--)if(Has(t))ZoneSystem.instance.RemoveGlobalKey(Key(t));
            Poll(true);
        }
        internal static string Key(int tier)=>"fh_tier"+tier;
        private static bool Has(int tier)=>ZoneSystem.instance!=null&&ZoneSystem.instance.GetGlobalKey(Key(tier));

        // Once a second from the plugin's Update.
        internal static void Tick(){if(Time.unscaledTime>=_next)Poll(false);}
        private static void Poll(bool force)
        {
            _next=Time.unscaledTime+1f;
            int level=0;
            if(All!=null&&All.Value)level=Top;
            else for(int t=1;t<=Top&&Has(t);t++)level=t;
            if(level==_level&&!force)return;
            bool changed=level!=_level;
            _level=level;
            if(changed)foreach(Action a in Changed?.GetInvocationList()??new Delegate[0])Plugin.Safe("tech changed",a);
        }
        internal static void Reset(){_level=-1;Changed=null;}
    }
}
