using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Omens
{
    // The great stag: a deer the host marks when it releases it. Every game draws it larger and names it, and its drops include its antlers.
    internal static class Stag
    {
        internal const string Key="bob_omen_stag";
        internal static bool Is(Component c)=>c!=null&&c.GetComponent<ZNetView>() is ZNetView view&&view.IsValid()&&view.GetZDO().GetBool(Key,false);
    }

    [HarmonyPatch(typeof(Character),"Start")]
    internal static class StagLooks
    {
        private static void Postfix(Character __instance)
        {
            if(!Stag.Is(__instance))return;
            __instance.transform.localScale=Vector3.one*Policy.StagScale;
            __instance.m_name="Great stag";
        }
    }

    [HarmonyPatch(typeof(CharacterDrop),nameof(CharacterDrop.GenerateDropList))]
    internal static class StagDrops
    {
        private static void Postfix(CharacterDrop __instance,List<KeyValuePair<GameObject,int>> __result)
        {
            if(__result==null||!Stag.Is(__instance)||ObjectDB.instance==null)return;
            foreach(var (name,amount) in Policy.StagDrops)
            {
                GameObject prefab=ObjectDB.instance.GetItemPrefab(name);
                if(prefab!=null&&!__result.Exists(d=>d.Key==prefab))__result.Add(new KeyValuePair<GameObject,int>(prefab,amount));
            }
        }
    }

    // The wanderer's favour: every skill learns faster for a while. One template; the game keeps its own copy on each player.
    internal static class Favour
    {
        private const string EffectName="SE_BobOmenFavour";
        private static SE_Stats _template;

        internal static void Give(Player me)
        {
            if(_template==null)
            {
                _template=ScriptableObject.CreateInstance<SE_Stats>();
                _template.name=EffectName;_template.m_name="Wanderer's favour";
                _template.m_tooltip=$"Every skill improves {Policy.FavourSkill*100:0}% faster.";
                _template.m_ttl=Policy.FavourSeconds;
                _template.m_raiseSkill=Skills.SkillType.All;_template.m_raiseSkillModifier=Policy.FavourSkill;
                _template.m_icon=ObjectDB.instance?.GetItemPrefab("Feathers")?.GetComponent<ItemDrop>()?.m_itemData.GetIcon();
            }
            me.GetSEMan().AddStatusEffect(_template,true);
        }
        internal static void Clear(){if(_template!=null)Object.Destroy(_template);_template=null;}
    }
}
