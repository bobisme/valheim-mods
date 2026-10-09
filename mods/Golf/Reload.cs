using System;
using System.Collections;
using System.IO;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using HarmonyLib;
using UnityEngine;

namespace MeadowGolf
{
    // A temporary runner survives destruction of the old plugin, so development reloads
    // replace only Golf and leave other mods' live sessions alone.
    internal sealed class GolfReload:MonoBehaviour
    {
        internal static void Schedule()
        {
            if(!Chainloader.PluginInfos.TryGetValue("com.bepis.bepinex.scriptengine",out var info)||info.Instance==null)
                throw new InvalidOperationException("ScriptEngine is not available. Use F6 or restart.");
            var engine=info.Instance;
            var manager=AccessTools.Field(engine.GetType(),"scriptManager")?.GetValue(engine) as GameObject;
            var load=AccessTools.Method(engine.GetType(),"LoadDLL",new[]{typeof(string),typeof(GameObject)});
            string path=Path.Combine(Paths.BepInExRootPath,"scripts","Golf.dll");
            if(manager==null||load==null||!File.Exists(path)||Plugin.Instance.gameObject!=manager)
                throw new InvalidOperationException("This Golf copy is not managed by the supported ScriptEngine.");
            var runner=new GameObject("GolfOnlyReload").AddComponent<GolfReload>();
            runner.StartCoroutine(runner.Run(engine,manager,load,path));
        }
        private IEnumerator Run(BaseUnityPlugin engine,GameObject manager,MethodInfo load,string path)
        {
            yield return new WaitForSecondsRealtime(.5f);
            Plugin old=Plugin.Instance;
            if(old!=null){Chainloader.PluginInfos.Remove(Plugin.Guid);Destroy(old);}
            yield return null;
            try{if(engine!=null&&manager!=null)load.Invoke(engine,new object[]{path,manager});}
            catch(Exception e){Debug.LogError("[Meadow Golf] Selective reload failed: "+e.GetBaseException().Message);}
            Destroy(gameObject);
        }
    }
}
