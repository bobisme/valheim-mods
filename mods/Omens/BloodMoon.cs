using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Omens
{
    // A blood moon night as every game sees it: the host announces it (and repeats it for late arrivals); each game reddens its own
    // sky and, for the areas it runs, spawns night creatures more often, more at once and stronger.
    internal static class BloodMoon
    {
        internal static bool Active,Softened;
        private static float _heardAt;
        private static readonly Color Fog=new Color(0.42f,0.03f,0.02f),Ambient=new Color(0.32f,0.05f,0.04f),Moon=new Color(0.95f,0.18f,0.12f);

        internal static void Heard(bool active,bool softened){Active=active;Softened=softened;_heardAt=Time.time;}
        // Silence from the host (it left, or the mod there was removed) ends it here too.
        internal static void Tick(){if(Active&&Time.time-_heardAt>75)Active=false;}
        internal static bool Night=>Active&&EnvMan.IsNight();

        // How red, from the game's own blend of night into the colours: none by day, full at night.
        internal static void Tint(EnvMan env,float nightInt,float eveningInt)
        {
            if(!Active)return;
            float k=Mathf.Clamp01(nightInt+eveningInt*0.5f)*(Softened?0.45f:0.6f);
            if(k<=0)return;
            RenderSettings.fogColor=Color.Lerp(RenderSettings.fogColor,Fog,k);
            RenderSettings.ambientLight=Color.Lerp(RenderSettings.ambientLight,Ambient,k*0.8f);
            if(env.m_dirLight!=null)env.m_dirLight.color=Color.Lerp(env.m_dirLight.color,Moon*env.m_dirLight.color.maxColorComponent,k);
        }
    }

    [HarmonyPatch(typeof(EnvMan),"SetEnv")]
    internal static class BloodSky
    {
        private static void Postfix(EnvMan __instance,float nightInt,float eveningInt)=>BloodMoon.Tint(__instance,nightInt,eveningInt);
    }

    // The game's spawn tables are shared, so each pass raises the night spawners' odds and caps and puts them back afterwards.
    [HarmonyPatch(typeof(SpawnSystem),"UpdateSpawnList")]
    internal static class BloodSpawns
    {
        private static void Prefix(List<SpawnSystem.SpawnData> spawners,bool eventSpawners,out List<(SpawnSystem.SpawnData data,float chance,int max)> __state)
        {
            __state=null;
            if(!BloodMoon.Night||eventSpawners||spawners==null)return;
            __state=new List<(SpawnSystem.SpawnData,float,int)>();
            foreach(SpawnSystem.SpawnData data in spawners)
            {
                if(data==null||!data.m_spawnAtNight)continue;
                __state.Add((data,data.m_spawnChance,data.m_maxSpawned));
                data.m_spawnChance=Policy.SpawnChance(data.m_spawnChance,BloodMoon.Softened);
                data.m_maxSpawned=Policy.MaxSpawned(data.m_maxSpawned,BloodMoon.Softened);
            }
        }
        private static System.Exception Finalizer(System.Exception __exception,List<(SpawnSystem.SpawnData data,float chance,int max)> __state)
        {
            if(__state!=null)foreach(var (data,chance,max) in __state){data.m_spawnChance=chance;data.m_maxSpawned=max;}
            return __exception;
        }
    }

    [HarmonyPatch(typeof(SpawnSystem),nameof(SpawnSystem.GetLevelUpChance),new[]{typeof(Vector3),typeof(float)})]
    internal static class BloodLevels
    {
        private static void Postfix(ref float __result){if(BloodMoon.Night)__result=Policy.LevelUpChance(__result,BloodMoon.Softened);}
    }
}
