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

    // The northern lights: the host announces them like a blood moon. Each game greens its own night sky (a slow shimmer), halves its
    // night spawns, and blesses its own player while they last.
    internal static class Aurora
    {
        internal static bool Active;
        private static float _heardAt,_nextBlessing;
        private const string EffectName="SE_BobOmenAurora";
        private static SE_Stats _template;
        private static readonly Color Fog=new Color(0.05f,0.22f,0.2f),Ambient=new Color(0.12f,0.42f,0.34f),Light=new Color(0.35f,1f,0.75f),Violet=new Color(0.45f,0.3f,0.85f);

        internal static void Heard(bool active){Active=active;_heardAt=Time.time;}
        internal static bool Night=>Active&&!BloodMoon.Active&&EnvMan.IsNight(); // a blood moon outshines it
        internal static void Tick()
        {
            if(Active&&Time.time-_heardAt>75)Active=false;
            Player me=Player.m_localPlayer;
            if(!Night||me==null||me.IsDead()||Time.time<_nextBlessing)return;
            _nextBlessing=Time.time+20;
            if(_template==null)
            {
                _template=ScriptableObject.CreateInstance<SE_Stats>();
                _template.name=EffectName;_template.m_name="Northern lights";
                _template.m_tooltip=$"You heal {(Policy.AuroraRegen-1)*100:0}% faster and every skill improves {Policy.AuroraSkill*100:0}% faster.";
                _template.m_ttl=60;
                _template.m_healthRegenMultiplier=Policy.AuroraRegen;
                _template.m_raiseSkill=Skills.SkillType.All;_template.m_raiseSkillModifier=Policy.AuroraSkill;
                _template.m_icon=ObjectDB.instance?.GetItemPrefab("Crystal")?.GetComponent<ItemDrop>()?.m_itemData.GetIcon();
            }
            me.GetSEMan().AddStatusEffect(_template,true); // refreshed while the lights last; it runs out a minute after
        }
        internal static void Tint(EnvMan env,float nightInt,float eveningInt)
        {
            if(!Active||BloodMoon.Active)return;
            float k=Mathf.Clamp01(nightInt+eveningInt*0.3f)*0.55f;
            if(k<=0)return;
            float shimmer=0.5f+0.5f*Mathf.Sin(Time.time*0.25f)*Mathf.Sin(Time.time*0.11f+1.3f); // the lights ripple from green toward violet
            Color glow=Color.Lerp(Light,Violet,shimmer*0.45f);
            RenderSettings.fogColor=Color.Lerp(RenderSettings.fogColor,Fog,k*0.7f);
            RenderSettings.ambientLight=Color.Lerp(RenderSettings.ambientLight,Ambient*(0.8f+0.4f*shimmer),k);
            if(env.m_dirLight!=null)env.m_dirLight.color=Color.Lerp(env.m_dirLight.color,glow*Mathf.Max(0.3f,env.m_dirLight.color.maxColorComponent),k);
        }
        internal static void Clear(){if(_template!=null)Object.Destroy(_template);_template=null;Active=false;}
    }

    [HarmonyPatch(typeof(EnvMan),"SetEnv")]
    internal static class BloodSky
    {
        private static void Postfix(EnvMan __instance,float nightInt,float eveningInt)
        {
            BloodMoon.Tint(__instance,nightInt,eveningInt);
            Aurora.Tint(__instance,nightInt,eveningInt);
        }
    }

    // The game's spawn tables are shared, so each pass raises the night spawners' odds and caps and puts them back afterwards.
    [HarmonyPatch(typeof(SpawnSystem),"UpdateSpawnList")]
    internal static class BloodSpawns
    {
        private static void Prefix(List<SpawnSystem.SpawnData> spawners,bool eventSpawners,out List<(SpawnSystem.SpawnData data,float chance,int max)> __state)
        {
            __state=null;
            bool blood=BloodMoon.Night,calm=Aurora.Night;
            if(!(blood||calm)||eventSpawners||spawners==null)return;
            __state=new List<(SpawnSystem.SpawnData,float,int)>();
            foreach(SpawnSystem.SpawnData data in spawners)
            {
                if(data==null||!data.m_spawnAtNight||data.m_spawnAtDay&&calm)continue; // the lights only quiet what comes out at night
                __state.Add((data,data.m_spawnChance,data.m_maxSpawned));
                if(blood)
                {
                    data.m_spawnChance=Policy.SpawnChance(data.m_spawnChance,BloodMoon.Softened);
                    data.m_maxSpawned=Policy.MaxSpawned(data.m_maxSpawned,BloodMoon.Softened);
                }
                else data.m_spawnChance=Policy.AuroraSpawnChance(data.m_spawnChance);
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
