using System;
using HarmonyLib;
using UnityEngine;

namespace Gary
{
    [HarmonyPatch(typeof(Teleport),nameof(Teleport.Interact))]
    internal static class GaryDungeonEntrance
    {
        private static void Postfix(Teleport __instance,Humanoid character,bool __result)
        {if(__result&&character is Player p)DungeonLoot.Enter(p,__instance);}
    }
    [HarmonyPatch(typeof(BaseAI),"Follow")]
    internal static class GaryFollow
    {
        private static bool Prefix(BaseAI __instance,GameObject go,float dt)
        {
            if(!Companion.Is(__instance))return true;
            if(go==null||!__instance.GetComponent<ZNetView>().IsOwner())return false;
            float distance=Vector3.Distance(__instance.transform.position,go.transform.position);
            if(distance<3)__instance.StopMoving();
            else Brain.Move(__instance,dt,go.transform.position,2.5f,distance>5);
            return false;
        }
    }
    [HarmonyPatch(typeof(MonsterAI),nameof(MonsterAI.UpdateAI))]
    internal static class GaryAI
    {
        private static bool Prefix(MonsterAI __instance,float dt,ref bool __result)
        {
            if(!Companion.Is(__instance))return true;
            try{return Brain.BeforeUpdate(__instance,dt,ref __result);}
            catch(Exception e)
            {
                __instance.StopMoving();__result=false;
                if(Companion.States.TryGetValue(__instance.GetComponent<Character>(),out Companion.State st)&&Time.time-st.LastError>10)
                {st.LastError=Time.time;Plugin.Instance?.Error(e);}
                return false; // fail closed; never fall back to hostile Greydwarf targeting
            }
        }
    }
    [HarmonyPatch(typeof(MonsterAI),"UpdateTarget")]
    internal static class GaryTarget
    {
        private static bool Prefix(MonsterAI __instance,float dt,ref bool canHearTarget,ref bool canSeeTarget)
        {
            if(!Companion.Is(__instance))return true;
            Brain.UpdateTarget(__instance,dt,out canHearTarget,out canSeeTarget);return false;
        }
    }
    [HarmonyPatch(typeof(MonsterAI),"OnDamaged")]
    internal static class GaryDefendSelf
    {
        private static void Postfix(MonsterAI __instance,float damage,Character attacker)
        {
            if(!Companion.Is(__instance)||!__instance.GetComponent<ZNetView>().IsOwner()||!(damage>0)||attacker==null)return;
            Character c=__instance.GetComponent<Character>();Companion.State st=Companion.Get(c);
            if(attacker.IsPlayer()||attacker.IsTamed()||attacker.IsDead()||!BaseAI.IsEnemy(c,attacker))return;
            st.SelfAttacker=attacker;st.SelfThreatAt=Companion.Now;
            ZDO z=Companion.Data(c);st.SelfThreatOwner=z.GetOwner();st.SelfThreatOrigin=c.transform.position;
            z.Set("bob_gary_danger",attacker.transform.position);z.Set("bob_gary_danger_time",Companion.Now);
        }
    }
    [HarmonyPatch(typeof(Character),nameof(Character.SetHealth))]
    internal static class GaryHealth
    {
        private static void Prefix(Character __instance,ref float health)
        {
            if(!Companion.Is(__instance)||!__instance.GetComponent<ZNetView>().IsOwner())return;
            if(health<__instance.GetHealth() && health<=__instance.GetMaxHealth()*Policy.RetreatAt)
            {
                ZDO z=Companion.Data(__instance);
                if(!z.GetBool(Companion.Retreating,false))
                {z.Set(Companion.Retreating,true);if(Companion.Owner(__instance)==Player.m_localPlayer)Plugin.Tell("Too many bruises! I'll be back after a rest.");}
                z.Set(Companion.RecoverAt,Companion.Now+TimeSpan.TicksPerMinute*3);
            }
            health=(float)Policy.ProtectedHealth(health,__instance.GetMaxHealth());
        }
    }
    [HarmonyPatch(typeof(Character),"CheckDeath")]
    internal static class GaryDeath
    {
        private static bool Prefix(Character __instance)
        {
            if(!Companion.Is(__instance))return true;
            if(__instance.GetComponent<ZNetView>().IsOwner() && __instance.GetHealth()<=0)__instance.SetHealth(1);
            return false;
        }
    }
    [HarmonyPatch(typeof(Character),"RPC_Damage")]
    internal static class GaryFriendlyFire
    {
        private static bool Prefix(Character __instance,HitData hit) =>
            !Companion.Is(__instance)||hit.GetAttacker()==null||(!hit.GetAttacker().IsPlayer()&&!hit.GetAttacker().IsTamed());
    }
    [HarmonyPatch(typeof(Character),nameof(Character.ApplyDamage))]
    internal static class GaryProtectPlayer
    {
        private static void Prefix(Character __instance,out float __state) => __state=__instance is Player?__instance.GetHealth():0;
        private static void Postfix(Character __instance,HitData hit,float __state)
        {
            if(!(__instance is Player p)||!p.GetComponent<ZNetView>().IsOwner()||p.GetHealth()>=__state)return;
            Character attacker=hit.GetAttacker();
            if(attacker==null||attacker.IsPlayer()||attacker.IsTamed()||!BaseAI.IsEnemy(p,attacker))return;
            ZDO z=Companion.Data(p);z.Set(Companion.Threat,attacker.GetZDOID());z.Set(Companion.ThreatAt,Companion.Now);z.Set(Companion.ThreatOwner,z.GetOwner());
        }
    }
    [HarmonyPatch(typeof(Player),"Interact")]
    internal static class GaryPet
    {
        private static bool Prefix(Player __instance,GameObject go,bool hold,bool alt)
        {
            if(Activities.SetHome(__instance,go,hold))return false;
            Character c=go!=null?go.GetComponentInParent<Character>():null;
            if(c==null||!Companion.Is(c))return true;
            if(!hold&&!alt)Petting.Request(__instance,c);
            return false;
        }
    }
    [HarmonyPatch(typeof(Character),nameof(Character.OnDeath))]
    internal static class GaryVictory
    {private static void Prefix(Character __instance){if(!Companion.Is(__instance))Personality.EnemyDied(__instance);}}
    [HarmonyPatch(typeof(Character),nameof(Character.RaiseSkill))]
    internal static class GarySkill
    {private static bool Prefix(Character __instance) => !Companion.Is(__instance);}
    [HarmonyPatch(typeof(Character),nameof(Character.GetHoverName))]
    internal static class GaryName
    {private static void Postfix(Character __instance,ref string __result){if(Companion.Is(__instance))__result="Gary the Greydwarf";}}
    [HarmonyPatch(typeof(Character),nameof(Character.GetHoverText))]
    internal static class GaryHover
    {
        private static void Postfix(Character __instance,ref string __result)
        {
            if(!Companion.Is(__instance))return;
            __result=Localization.instance.Localize("Gary the Greydwarf\n"+Companion.Data(__instance).GetString("bob_gary_status","following")+"\n[<color=yellow><b>$KEY_Use</b></color>] Pet Gary\nF3: call · Shift+F3: wait");
        }
    }
}
