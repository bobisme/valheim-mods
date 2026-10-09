using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace TrophyHall
{
    [BepInPlugin(Guid,Name,Version)]
    public sealed class Plugin:BaseUnityPlugin
    {
        public const string Guid="com.bobisme.trophyhall";
        public const string Name="TrophyHall";
        public const string Version="0.1.1";
        internal static Plugin Instance;
        internal ConfigEntry<bool> Enabled,Readings;
        internal ConfigEntry<float> Range;
        private Harmony _harmony;

        private void Awake()
        {
            Instance=this;
            Enabled=Config.Bind("General","Enabled",true,"Trophies on item stands in a base give everyone there small themed perks and comfort.");
            Readings=Config.Bind("General","Readings",true,"Show the hall's name, trophy count and fallen creatures when you walk in (at most every 10 minutes per hall).");
            Range=Config.Bind("General","Range",40f,new ConfigDescription("Metres around you in which mounted trophies count, inside a base.",new AcceptableValueRange<float>(15,80)));
            _harmony=new Harmony(Guid);_harmony.PatchAll(typeof(Plugin).Assembly);
            Hall.Seed(); // stands already loaded (a hot reload while in a world)
            Logger.LogInfo($"{Name} {Version} loaded.");
        }
        private void Update(){try{Hall.Tick();}catch(System.Exception e){Logger.LogError("Trophy hall: "+e);}}
        private void OnDestroy(){Hall.Clear();_harmony?.UnpatchSelf();if(Instance==this)Instance=null;}
    }

    // The hall around the local player: trophies on player-built item stands inside a base, read every two seconds.
    internal static class Hall
    {
        private const string EffectName="SE_BobTrophyHall";
        private static float _next;
        private static bool _inHall;
        private static Vector3 _center;
        private static int _comfort;
        private static string _signature="";
        private static SE_Stats _template;
        private static readonly Dictionary<string,float> Greeted=new Dictionary<string,float>();
        // Every loaded item stand, kept as they wake (StandSeen) instead of searching every loaded object each time.
        internal static readonly HashSet<ItemStand> Stands=new HashSet<ItemStand>();
        internal static void Seed(){Stands.Clear();foreach(ItemStand s in Object.FindObjectsByType<ItemStand>(FindObjectsSortMode.None))Stands.Add(s);}

        internal static void Tick()
        {
            if(Time.time<_next)return;
            _next=Time.time+2;
            Player me=Player.m_localPlayer;
            if(me==null||me.IsDead()||ObjectDB.instance==null||!Plugin.Instance.Enabled.Value||
               EffectArea.IsPointInsideArea(me.transform.position,EffectArea.Type.PlayerBase)==null){Leave(me);return;}
            float range=Plugin.Instance.Range.Value;
            var mounted=new List<(string prefab,Sprite icon,Vector3 pos)>();
            Stands.RemoveWhere(s=>s==null);
            foreach(ItemStand stand in Stands)
            {
                if(stand==null||stand.m_guardianPower!=null||!stand.HaveAttachment())continue; // boss altars hold their trophies for powers
                Vector3 pos=stand.transform.position;
                if(Vector3.Distance(pos,me.transform.position)>range||stand.GetComponent<Piece>()?.GetCreator()==0L)continue;
                if(EffectArea.IsPointInsideArea(pos,EffectArea.Type.PlayerBase)==null)continue;
                GameObject prefab=ObjectDB.instance.GetItemPrefab(stand.GetAttachedItem());
                ItemDrop.ItemData item=prefab!=null?prefab.GetComponent<ItemDrop>()?.m_itemData:null;
                if(item==null||!Policy.Counts(prefab.name,item.m_shared.m_itemType==ItemDrop.ItemData.ItemType.Trophy))continue;
                mounted.Add((prefab.name,item.GetIcon(),pos));
            }
            if(mounted.Count==0){Leave(me);return;}
            int distinct=mounted.Select(m=>m.prefab).Distinct().Count();
            List<Theme> perks=Policy.Perks(mounted.Select(m=>m.prefab));
            _center=mounted.Aggregate(Vector3.zero,(sum,m)=>sum+m.pos)/mounted.Count;
            _comfort=Policy.Comfort(distinct);
            if(!_inHall)Greet(me,mounted.Count,perks);
            _inHall=true;
            Apply(me,perks,mounted.FirstOrDefault(m=>perks.Count>0&&perks[0].Trophies.Contains(m.prefab)).icon??mounted[0].icon);
        }

        // Comfort for a spot near the hall's trophies, counted with the game's own (only under a roof, as other comfort is).
        internal static int ComfortAt(Vector3 position)=>_inHall&&Vector3.Distance(position,_center)<=Plugin.Instance.Range.Value?_comfort:0;

        private static void Greet(Player me,int count,List<Theme> perks)
        {
            if(!Plugin.Instance.Readings.Value)return;
            string key=$"{Mathf.Round(_center.x/20)}:{Mathf.Round(_center.z/20)}";
            if(Greeted.TryGetValue(key,out float last)&&Time.time-last<600)return;
            Greeted[key]=Time.time;
            PrivateArea ward=Wards()
                .Where(w=>w!=null&&Vector3.Distance(w.transform.position,_center)<Plugin.Instance.Range.Value).OrderBy(w=>Vector3.Distance(w.transform.position,_center)).FirstOrDefault();
            string owner=ward!=null&&ward.GetComponent<ZNetView>() is ZNetView v&&v.IsValid()?v.GetZDO().GetString(ZDOVars.s_creatorName,""):""; // the ward's owner names the hall
            string title=string.IsNullOrEmpty(owner)?"A trophy hall":owner+"'s hall";
            string line=$"{title}: {count} {(count==1?"trophy":"trophies")}.";
            string fallen=Policy.Fallen(perks);
            me.Message(MessageHud.MessageType.Center,fallen==""?line:line+"\n"+fallen);
        }

        private static void Apply(Player me,List<Theme> perks,Sprite icon)
        {
            string signature=string.Join(",",perks.Select(p=>p.Perk))+"|"+_comfort;
            if(signature==_signature&&me.GetSEMan().HaveStatusEffect(EffectName.GetStableHashCode()))return;
            Remove(me);
            _signature=signature;
            if(perks.Count==0&&_comfort==0)return;
            var se=ScriptableObject.CreateInstance<SE_Stats>();
            se.name=EffectName;se.m_name="Trophy hall";se.m_icon=icon;
            se.m_tooltip=string.Join("\n",perks.Select(p=>$"{char.ToUpperInvariant(p.Plural[0])}{p.Plural.Substring(1)}: {p.Description}"))+
                (_comfort>0?$"\nTrophies: +{_comfort} comfort under a roof":"");
            foreach(Theme perk in perks)
                switch(perk.Perk)
                {
                    case Perk.Carry:se.m_addMaxCarryWeight=30;break;
                    case Perk.Poison:se.m_mods.Add(new HitData.DamageModPair{m_type=HitData.DamageType.Poison,m_modifier=HitData.DamageModifier.SlightlyResistant});break;
                    case Perk.Stamina:se.m_staminaRegenMultiplier=1.1f;break;
                    case Perk.Swim:se.m_swimSpeedModifier=0.1f;break;
                    case Perk.Woodcutting:se.m_raiseSkill=Skills.SkillType.WoodCutting;se.m_raiseSkillModifier=0.1f;break;
                    case Perk.Health:se.m_healthRegenMultiplier=1.1f;break;
                    case Perk.Speed:se.m_speedModifier=0.05f;break;
                }
            _template=se;
            me.GetSEMan().AddStatusEffect(se); // the game keeps its own copy while you stay in the hall
        }
        private static void Leave(Player me)
        {
            if(!_inHall&&_template==null)return;
            _inHall=false;_comfort=0;
            if(me!=null)Remove(me);
            _signature="";
        }
        private static void Remove(Player me)
        {
            me.GetSEMan().RemoveStatusEffect(EffectName.GetStableHashCode(),true);
            if(_template!=null)Object.Destroy(_template);_template=null;
        }
        internal static void Clear(){Leave(Player.m_localPlayer);Greeted.Clear();Stands.Clear();}
        private static List<PrivateArea> Wards()=>AccessTools.StaticFieldRefAccess<List<PrivateArea>>(typeof(PrivateArea),"m_allAreas");
    }

    [HarmonyPatch(typeof(ItemStand),"Awake")]
    internal static class StandSeen
    {
        private static void Postfix(ItemStand __instance){if(__instance!=null)Hall.Stands.Add(__instance);}
    }

    [HarmonyPatch(typeof(SE_Rested),nameof(SE_Rested.CalculateComfortLevel),new[]{typeof(bool),typeof(Vector3)})]
    internal static class HallComfort
    {
        private static void Postfix(bool inShelter,Vector3 position,ref int __result)
        {if(inShelter&&Plugin.Instance!=null&&Plugin.Instance.Enabled.Value)__result+=Hall.ComfortAt(position);}
    }
}
