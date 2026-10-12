using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace GraveGoods
{
    // The powers at work. An item's own numbers (damage, armour, block, durability, weight) change wherever the game reads
    // them; what worn things give their wearer (health, regeneration, speed, skills, wards) is added up for the local player.
    internal static class Effects
    {
        private sealed class Cached{internal string Raw;internal Goods Goods;}
        private static readonly ConditionalWeakTable<ItemDrop.ItemData,Cached> Cache=new ConditionalWeakTable<ItemDrop.ItemData,Cached>();

        internal static Goods Of(ItemDrop.ItemData item)
        {
            if(item?.m_customData==null||!item.m_customData.TryGetValue(Policy.Key,out string raw))return null;
            Cached c=Cache.GetValue(item,_=>new Cached());
            if(c.Raw!=raw){c.Raw=raw;c.Goods=Policy.Load(raw);}
            return c.Goods;
        }
        internal static double Sum(ItemDrop.ItemData item,string id)=>Of(item)?.Sum(id)??0;

        internal sealed class Totals
        {
            internal readonly Dictionary<string,double> Stat=new Dictionary<string,double>();
            internal readonly Dictionary<Skills.SkillType,float> Skill=new Dictionary<Skills.SkillType,float>();
            internal double this[string id]=>Stat.TryGetValue(id,out double v)?v:0;
        }
        private static Totals _worn=new Totals();
        private static int _wornFrame=-1;
        // What the local player's worn and wielded grave goods add up to (once a frame).
        internal static Totals Worn()
        {
            if(_wornFrame==Time.frameCount)return _worn;
            _wornFrame=Time.frameCount;
            var t=new Totals();
            Player me=Player.m_localPlayer;
            if(me!=null)
                foreach(ItemDrop.ItemData item in me.GetInventory().GetEquippedItems())
                {
                    Goods g=Of(item);
                    if(g==null)continue;
                    foreach(Affix a in g.Affixes)
                    {
                        if(a.Id=="wskill"||a.Id=="askill")
                        {
                            if(Enum.TryParse(a.Arg,out Skills.SkillType s)){t.Skill.TryGetValue(s,out float v);t.Skill[s]=v+(float)a.Value;}
                        }
                        else{t.Stat.TryGetValue(a.Id,out double v);t.Stat[a.Id]=v+a.Value;}
                    }
                }
            _worn=t;
            return t;
        }
        internal static bool IsMe(SEMan seman)=>Player.m_localPlayer!=null&&SEManOwner(seman)==Player.m_localPlayer;
        private static readonly AccessTools.FieldRef<SEMan,Character> SEManOwner=AccessTools.FieldRefAccess<SEMan,Character>("m_character");
        // The weapon in the local player's hand (its own powers count while it's swung, not just while it's worn).
        internal static ItemDrop.ItemData Weapon()=>Player.m_localPlayer?.GetCurrentWeapon();
    }

    // ---- an item's own numbers ----

    [HarmonyPatch(typeof(ItemDrop.ItemData),nameof(ItemDrop.ItemData.GetDamage),new[]{typeof(int),typeof(float)})]
    internal static class ItemDamage
    {
        private static void Postfix(ItemDrop.ItemData __instance,ref HitData.DamageTypes __result)
        {
            Goods g=Effects.Of(__instance);
            if(g==null)return;
            double more=g.Sum("dmg");
            if(more!=0)__result.Modify((float)(1+more));
            float total=__result.GetTotalDamage();
            __result.m_fire+=total*(float)g.Sum("fire");
            __result.m_frost+=total*(float)g.Sum("frost");
            __result.m_lightning+=total*(float)g.Sum("lightning");
            __result.m_poison+=total*(float)g.Sum("poison");
            __result.m_spirit+=total*(float)g.Sum("spirit");
        }
    }
    [HarmonyPatch(typeof(ItemDrop.ItemData),nameof(ItemDrop.ItemData.GetArmor),new[]{typeof(int),typeof(float)})]
    internal static class ItemArmor
    {
        private static void Postfix(ItemDrop.ItemData __instance,ref float __result){double v=Effects.Sum(__instance,"armor");if(v!=0)__result*=(float)(1+v);}
    }
    [HarmonyPatch(typeof(ItemDrop.ItemData),nameof(ItemDrop.ItemData.GetMaxDurability),new[]{typeof(int)})]
    internal static class ItemDurability
    {
        private static void Postfix(ItemDrop.ItemData __instance,ref float __result){double v=Effects.Sum(__instance,"dura");if(v!=0)__result*=(float)(1+v);}
    }
    [HarmonyPatch(typeof(ItemDrop.ItemData),nameof(ItemDrop.ItemData.GetBaseBlockPower),new[]{typeof(int)})]
    internal static class ItemBlock
    {
        private static void Postfix(ItemDrop.ItemData __instance,ref float __result){double v=Effects.Sum(__instance,"block");if(v!=0)__result*=(float)(1+v);}
    }
    [HarmonyPatch(typeof(ItemDrop.ItemData),nameof(ItemDrop.ItemData.GetWeight),new[]{typeof(int)})]
    internal static class ItemWeight
    {
        private static void Postfix(ItemDrop.ItemData __instance,ref float __result){double v=Effects.Sum(__instance,"weight");if(v!=0)__result*=(float)Math.Max(0,1-v);}
    }

    // ---- what the wearer gets ----

    [HarmonyPatch(typeof(Player),"GetTotalFoodValue")]
    internal static class WornVigour
    {
        private static void Postfix(Player __instance,ref float hp,ref float stamina,ref float eitr)
        {
            if(__instance!=Player.m_localPlayer)return;
            Effects.Totals t=Effects.Worn();
            hp=Mathf.Max(1,hp+(float)t["hp"]);
            stamina=Mathf.Max(0,stamina+(float)t["stamina"]);
            eitr=Mathf.Max(0,eitr+(float)t["eitr"]);
        }
    }
    [HarmonyPatch(typeof(SEMan),nameof(SEMan.ModifyHealthRegen))]
    internal static class WornHealing
    {
        private static void Postfix(SEMan __instance,ref float regenMultiplier){if(Effects.IsMe(__instance))regenMultiplier*=(float)(1+Effects.Worn()["hregen"]);}
    }
    [HarmonyPatch(typeof(SEMan),nameof(SEMan.ModifyStaminaRegen))]
    internal static class WornRest
    {
        private static void Postfix(SEMan __instance,ref float staminaMultiplier){if(Effects.IsMe(__instance))staminaMultiplier*=(float)(1+Effects.Worn()["sregen"]);}
    }
    [HarmonyPatch(typeof(SEMan),nameof(SEMan.ModifyMaxCarryWeight))]
    internal static class WornBurden
    {
        private static void Postfix(SEMan __instance,ref float limit){if(Effects.IsMe(__instance))limit+=(float)Effects.Worn()["carry"];}
    }
    [HarmonyPatch(typeof(SEMan),nameof(SEMan.ModifySkillLevel))]
    internal static class WornSkill
    {
        private static void Postfix(SEMan __instance,Skills.SkillType skill,ref float level)
        {
            if(Effects.IsMe(__instance)&&Effects.Worn().Skill.TryGetValue(skill,out float more))level+=more;
        }
    }
    [HarmonyPatch(typeof(SEMan),nameof(SEMan.ApplyStatusEffectSpeedMods))]
    internal static class WornSpeed
    {
        private static void Postfix(SEMan __instance,ref float speed){if(Effects.IsMe(__instance))speed*=(float)(1+Effects.Worn()["speed"]);}
    }
    // A weapon's own powers as it swings: less stamina, more stagger, a crueller stab in the back.
    [HarmonyPatch(typeof(SEMan),nameof(SEMan.ModifyAttackStaminaUsage))]
    internal static class WeaponEase
    {
        private static void Postfix(SEMan __instance,ref float staminaUse)
        {
            if(!Effects.IsMe(__instance))return;
            double v=Effects.Sum(Effects.Weapon(),"stam");
            if(v!=0)staminaUse*=(float)Math.Max(0,1-v);
        }
    }
    [HarmonyPatch(typeof(SEMan),nameof(SEMan.ModifyAttack))]
    internal static class WeaponBlow
    {
        private static void Postfix(SEMan __instance,ref HitData hitData)
        {
            if(!Effects.IsMe(__instance)||hitData==null)return;
            ItemDrop.ItemData w=Effects.Weapon();
            hitData.m_staggerMultiplier*=(float)(1+Effects.Sum(w,"stagger"));
            hitData.m_backstabBonus*=(float)(1+Effects.Sum(w,"backstab"));
        }
    }
    // Blood-drinking: a share of each blow you strike comes back to you as health.
    [HarmonyPatch(typeof(Character),nameof(Character.Damage))]
    internal static class WeaponLeech
    {
        private static void Prefix(Character __instance,HitData hit)
        {
            Player me=Player.m_localPlayer;
            if(me==null||hit==null||__instance==me||hit.GetAttacker()!=me||me.IsDead())return;
            double v=Effects.Sum(Effects.Weapon(),"leech");
            if(v<=0)return;
            float heal=hit.GetTotalDamage()*(float)v;
            if(heal>0.05f)me.Heal(heal,showText:heal>=1);
        }
    }
    // Wards: less of each kind of harm reaches you.
    [HarmonyPatch(typeof(Character),"RPC_Damage")]
    internal static class WornWards
    {
        private static void Prefix(Character __instance,HitData hit)
        {
            if(hit==null||__instance!=Player.m_localPlayer)return;
            Effects.Totals t=Effects.Worn();
            hit.m_damage.m_fire*=Ward(t["rfire"]);
            hit.m_damage.m_frost*=Ward(t["rfrost"]);
            hit.m_damage.m_poison*=Ward(t["rpoison"]);
            hit.m_damage.m_lightning*=Ward(t["rlightning"]);
            hit.m_damage.m_spirit*=Ward(t["rspirit"]);
        }
        private static float Ward(double v)=>(float)Math.Max(0.2,1-v); // (never more than 80% warded, however much is worn)
    }
}
