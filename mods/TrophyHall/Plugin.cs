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
        public const string Version="0.1.3";
        internal static Plugin Instance;
        internal ConfigEntry<bool> Enabled,Readings;
        internal ConfigEntry<float> Range,LingerMinutes;
        private Harmony _harmony;

        private void Awake()
        {
            Instance=this;
            Enabled=Config.Bind("General","Enabled",true,"Trophies on item stands in a base give everyone there small themed perks and comfort.");
            Readings=Config.Bind("General","Readings",true,"Show the hall's name, trophy count and fallen creatures when you walk in (at most every 10 minutes per hall).");
            LingerMinutes=Config.Bind("General","LingerMinutes",30f,new ConfigDescription("Minutes the hall's perks stay with you after you leave (counted down on the status bar, kept full while you are there). 0: only while inside. Comfort always needs the hall's roof.",new AcceptableValueRange<float>(0,180)));
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
            Apply(me,mounted.Select(m=>m.prefab),mounted.Select(m=>(m.prefab,m.icon)).ToList());
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

        // The trophies behind the effect you carry. While it lasts it only grows: walking to the far end of the base (some heads out of
        // reach) or into a smaller hall never takes perks away; trophies seen anywhere are added. Being near any themed trophy in a base
        // keeps the countdown full. Once it runs out, it starts again from the hall you are in.
        private static readonly HashSet<string> Held=new HashSet<string>();
        private static void Apply(Player me,IEnumerable<string> mounted,List<(string prefab,Sprite icon)> icons)
        {
            int hash=EffectName.GetStableHashCode();
            StatusEffect active=me.GetSEMan().GetStatusEffect(hash);
            if(active==null){Held.Clear();_signature="";}
            var held=new HashSet<string>(Held);held.UnionWith(mounted);
            List<Theme> perks=Policy.Perks(held);
            if(perks.Count==0)return; // nothing to give (and nothing given is taken away)
            string signature=string.Join(",",perks.Select(p=>p.Perk));
            if(active!=null&&signature==_signature){active.ResetTime();return;}
            Remove(me);
            Held.Clear();Held.UnionWith(held);
            _signature=signature;
            Sprite icon=icons.FirstOrDefault(i=>perks[0].Trophies.Contains(i.prefab)).icon??icons[0].icon;
            var se=ScriptableObject.CreateInstance<SE_Stats>();
            se.name=EffectName;se.m_name="Trophy hall";se.m_icon=icon;
            se.m_tooltip=string.Join("\n",perks.Select(p=>$"{char.ToUpperInvariant(p.Plural[0])}{p.Plural.Substring(1)}: {p.Description}"))+
                (Linger>0?$"\nStays with you {Linger/60:0} minutes after you leave.":"");
            se.m_ttl=Linger;
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
            me.GetSEMan().AddStatusEffect(se); // the game keeps its own copy
        }
        private static float Linger=>Mathf.Max(0,Plugin.Instance.LingerMinutes.Value)*60;
        // Leaving: the perks run down on their own timer (or go at once with no linger); the comfort stays with the hall.
        private static void Leave(Player me)
        {
            if(!_inHall&&_template==null)return;
            _inHall=false;_comfort=0;
            if(me!=null&&Linger<=0)Remove(me);
            else if(_template!=null){Object.Destroy(_template);_template=null;}
        }
        private static void Remove(Player me)
        {
            me.GetSEMan().RemoveStatusEffect(EffectName.GetStableHashCode(),true);
            if(_template!=null)Object.Destroy(_template);_template=null;
            Held.Clear();_signature="";
        }
        // A lingering effect outlives a reload: the game owns its copy, and it runs down on its own.
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
