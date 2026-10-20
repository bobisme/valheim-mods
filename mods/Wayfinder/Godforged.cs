using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Wayfinder
{
    // Godforged, found by reflection (its Api 3), when it's there: whether a ship is Skíðblaðnir with her fair wind, so the HUD doesn't
    // tell her helmsman to row into a headwind she doesn't feel.
    internal static class Godforged
    {
        private static float _looked=-100;
        private static Func<Ship,bool> _fair;
        private static void Bind()
        {
            if(Time.realtimeSinceStartup-_looked<10)return;
            _looked=Time.realtimeSinceStartup;
            _fair=null;
            Type api=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType("Godforged.Api",false)).LastOrDefault(t=>t!=null);
            if(api==null||!(api.GetField("Version",BindingFlags.Public|BindingFlags.Static)?.GetValue(null) is int v)||v<3)return;
            try{_fair=(Func<Ship,bool>)Delegate.CreateDelegate(typeof(Func<Ship,bool>),api.GetMethod("FairWind",new[]{typeof(Ship)}));}
            catch(Exception e){Plugin.Log("Godforged's fair wind: "+e.Message);_fair=null;}
        }
        internal static bool FairWind(Ship ship){Bind();return _fair!=null&&ship!=null&&_fair(ship);}
    }
}
