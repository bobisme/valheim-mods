using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace DualWield
{
    // A matching second weapon goes to the off hand, replacing a shield or torch, once the skill allows it.
    [HarmonyPatch(typeof(Humanoid),nameof(Humanoid.EquipItem))]
    internal static class EquipSecond
    {
        [HarmonyPriority(Priority.High)]
        private static bool Prefix(Humanoid __instance,ItemDrop.ItemData item,bool triggerEquipEffects,ref bool __result)
        {
            if(!(__instance is Player player)||item==null||Plugin.Instance==null||!Plugin.Instance.Enabled.Value)return true;
            ItemDrop.ItemData main=Hands.Right(player);
            if(main==null||player.IsItemEquiped(item)||!player.GetInventory().ContainsItem(item)||player.InAttack()||player.InDodge())return true;
            if(item.m_shared.m_useDurability&&item.m_durability<=0)return true;
            bool swap=player==Player.m_localPlayer&&Plugin.Instance.SwapKey.Value.MainKey!=KeyCode.None&&Input.GetKey(Plugin.Instance.SwapKey.Value.MainKey);
            Family family=Hands.FamilyOf(main);
            double skill=Hands.Skill(player,item,out string how);
            switch(Policy.Equip(family,Hands.FamilyOf(item),main==item,skill,Plugin.Instance.MinSkill.Value,swap))
            {
                case Verdict.Dual:
                    if(Hands.Template(family)==null)return true; // the game's own dual weapon is missing: no move set to borrow
                    Hands.Equip(player,item,triggerEquipEffects);
                    __result=true;return false;
                case Verdict.NeedsSkill:
                    if(player==Player.m_localPlayer)
                        player.Message(MessageHud.MessageType.TopLeft,Localization.instance.Localize(
                            $"To wield two you need {Plugin.Instance.MinSkill.Value:0}: {how} = {skill:0.#}."));
                    return true;
                default:return true;
            }
        }
    }

    // While the game equips something or empties both hands, it unequips the main hand first and then decides about the left:
    // moving the off-hand weapon across then would leave it in a hand nobody clears.
    [HarmonyPatch]
    internal static class HandsBusy
    {
        internal static int Depth;
        private static IEnumerable<MethodBase> TargetMethods()
        {yield return AccessTools.Method(typeof(Humanoid),nameof(Humanoid.EquipItem));yield return AccessTools.Method(typeof(Humanoid),nameof(Humanoid.UnequipAllItems));}
        [HarmonyPriority(Priority.First)]
        private static void Prefix()=>Depth++;
        private static void Finalizer()=>Depth--;
    }

    // Putting away the main weapon moves the off-hand one into the main hand, as the game expects a lone weapon there.
    [HarmonyPatch(typeof(Humanoid),nameof(Humanoid.UnequipItem))]
    internal static class KeepOneWeapon
    {
        private static void Postfix(Humanoid __instance)
        {
            if(HandsBusy.Depth>0||!(__instance is Player)||Hands.Right(__instance)!=null)return;
            ItemDrop.ItemData left=Hands.Left(__instance);
            if(Hands.FamilyOf(left)==Family.None)return;
            Hands.Left(__instance)=null;Hands.Right(__instance)=left;
            Hands.Setup_(__instance);
        }
    }

    // The game's own dual stance and moves for the pair: synced to everyone like any other weapon state.
    [HarmonyPatch(typeof(Humanoid),"SetupAnimationState")]
    internal static class DualStance
    {
        private static bool Prefix(Humanoid __instance)
        {
            Family pair=Hands.Pair(__instance);
            ItemDrop.ItemData.SharedData template=pair!=Family.None?Hands.Template(pair):null;
            if(template==null)return true;
            Hands.State(__instance,template.m_animationState);
            return false;
        }
    }

    // Blocking uses the main weapon's (weak) block; the dual stance shows both weapons raised.
    [HarmonyPatch(typeof(Humanoid),"GetCurrentBlocker")]
    internal static class MainHandBlock
    {
        private static void Postfix(Humanoid __instance,ref ItemDrop.ItemData __result)
        {if(Hands.Pair(__instance)!=Family.None)__result=Hands.Right(__instance);}
    }

    // Each swing borrows the game's dual weapon's attack (animation, chain, timing), keeping the main weapon for damage, skill and wear.
    [HarmonyPatch(typeof(Attack),nameof(Attack.Start))]
    internal static class DualSwing
    {
        private static readonly FieldInfo[] Fields=typeof(Attack).GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic);
        private static void Prefix(Attack __instance,Humanoid character,ItemDrop.ItemData weapon,ref bool __state)
        {
            __state=false;
            Family pair=Hands.Pair(character);
            if(pair==Family.None||weapon!=Hands.Right(character))return;
            ItemDrop.ItemData.SharedData template=Hands.Template(pair);
            if(template==null)return;
            Attack mine=weapon.m_shared.m_attack;
            bool secondary=__instance.m_attackAnimation!=mine.m_attackAnimation&&__instance.m_attackAnimation==weapon.m_shared.m_secondaryAttack.m_attackAnimation;
            if(secondary)mine=weapon.m_shared.m_secondaryAttack;
            Attack borrowed=(secondary?template.m_secondaryAttack:template.m_attack).Clone();
            foreach(FieldInfo field in Fields)field.SetValue(__instance,field.GetValue(borrowed)); // fresh clone: Start sets the runtime fields
            ItemDrop.ItemData off=Hands.Left(character);
            double single=Policy.ChainAverage(mine.m_damageMultiplier,mine.m_attackChainLevels,mine.m_lastChainDamageMultiplier);
            double dual=Policy.ChainAverage(borrowed.m_damageMultiplier,borrowed.m_attackChainLevels,borrowed.m_lastChainDamageMultiplier);
            float factor=(float)Policy.DamageFactor(weapon.GetDamage().GetTotalDamage(),off.GetDamage().GetTotalDamage(),Plugin.Instance.DamageShare.Value,single,dual);
            __instance.m_damageMultiplier*=factor;
            __instance.m_attackStamina=(float)Policy.Stamina(mine.m_attackStamina,Plugin.Instance.StaminaFactor.Value);
            __state=true;
        }
        // The off-hand weapon wears with every swing, as the main one does with every hit.
        private static void Postfix(Humanoid character,bool __result,bool __state)
        {
            if(!__result||!__state)return;
            ItemDrop.ItemData off=Hands.Left(character);
            if(off!=null&&off.m_shared.m_useDurability)off.m_durability=Mathf.Max(0,off.m_durability-off.m_shared.m_useDurabilityDrain*Game.m_durabilityRate);
        }
    }

    // A weapon in the left hand uses its right-hand attach point; a per-kind correction makes it sit in the off hand.
    [HarmonyPatch(typeof(VisEquipment),"SetLeftHandEquipped")]
    internal static class OffHandLook
    {
        private static readonly AccessTools.FieldRef<VisEquipment,GameObject> Instance=AccessTools.FieldRefAccess<VisEquipment,GameObject>("m_leftItemInstance");
        private static void Postfix(VisEquipment __instance,int hash,bool __result)
        {
            GameObject held=Instance(__instance);
            if(!__result||held==null||Plugin.Instance==null||ObjectDB.instance==null)return;
            GameObject prefab=ObjectDB.instance.GetItemPrefab(hash);
            Family family=prefab!=null?Hands.FamilyOf(prefab.GetComponent<ItemDrop>()?.m_itemData):Family.None;
            if(family==Family.None)return;
            Vector3 rotation=family==Family.Axes?Plugin.Instance.AxeRotation.Value:Plugin.Instance.KnifeRotation.Value;
            Vector3 offset=family==Family.Axes?Plugin.Instance.AxeOffset.Value:Plugin.Instance.KnifeOffset.Value;
            held.transform.localPosition+=offset;
            held.transform.localRotation*=Quaternion.Euler(rotation);
        }
    }
}
