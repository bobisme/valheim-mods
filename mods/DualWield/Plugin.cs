using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace DualWield
{
    [BepInPlugin(Guid,Name,Version)]
    public sealed class Plugin:BaseUnityPlugin
    {
        public const string Guid="com.bobisme.dualwield";
        public const string Name="DualWield";
        public const string Version="0.1.1";
        internal static Plugin Instance;
        internal ConfigEntry<bool> Enabled;
        internal ConfigEntry<float> MinSkill,WoodcuttingCredit,DamageShare,StaminaFactor;
        internal ConfigEntry<KeyboardShortcut> SwapKey;
        internal ConfigEntry<Vector3> AxeRotation,AxeOffset,KnifeRotation,KnifeOffset;
        private Harmony _harmony;

        private void Awake()
        {
            Instance=this;
            Enabled=Config.Bind("General","Enabled",true,"Equip a matching second one-handed axe or knife into the off hand.");
            MinSkill=Config.Bind("Balance","MinSkill",20f,new ConfigDescription("Weapon skill needed to wield two of that kind.",new AcceptableValueRange<float>(0,100)));
            WoodcuttingCredit=Config.Bind("Balance","WoodcuttingCredit",0.5f,new ConfigDescription("Share of Woodcutting that counts toward wielding two axes (0.5: Axes plus half of Woodcutting must reach MinSkill).",new AcceptableValueRange<float>(0,1)));
            DamageShare=Config.Bind("Balance","DamageShare",0.62f,new ConfigDescription("Each dual hit is worth this share of both weapons' damage combined (0.62: about 1.24× one weapon when they are equal).",new AcceptableValueRange<float>(0.3f,1)));
            StaminaFactor=Config.Bind("Balance","StaminaFactor",1.3f,new ConfigDescription("Stamina per dual swing, as a multiple of the main weapon's.",new AcceptableValueRange<float>(1,3)));
            SwapKey=Config.Bind("Controls","ReplaceMainHand",new KeyboardShortcut(KeyCode.LeftAlt),"Hold while equipping a second matching weapon to replace the main-hand one instead of wielding both.");
            AxeRotation=Config.Bind("Looks","AxeRotation",Vector3.zero,"Extra rotation (degrees) for an axe held in the off hand.");
            AxeOffset=Config.Bind("Looks","AxeOffset",Vector3.zero,"Extra position (metres) for an axe held in the off hand.");
            KnifeRotation=Config.Bind("Looks","KnifeRotation",Vector3.zero,"Extra rotation (degrees) for a knife held in the off hand.");
            KnifeOffset=Config.Bind("Looks","KnifeOffset",Vector3.zero,"Extra position (metres) for a knife held in the off hand.");
            _harmony=new Harmony(Guid);_harmony.PatchAll(typeof(Plugin).Assembly);
            foreach(Player p in Player.GetAllPlayers())Hands.Refresh(p); // hot reload: re-apply the dual state to anyone holding a pair
            Logger.LogInfo($"{Name} {Version} loaded.");
        }
        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
            foreach(Player p in Player.GetAllPlayers())Hands.Refresh(p); // back to the game's own animation state
            if(Instance==this)Instance=null;
        }
    }

    // The two hands of a player, through the game's own private fields and methods.
    internal static class Hands
    {
        internal static readonly AccessTools.FieldRef<Humanoid,ItemDrop.ItemData> Right=AccessTools.FieldRefAccess<Humanoid,ItemDrop.ItemData>("m_rightItem");
        internal static readonly AccessTools.FieldRef<Humanoid,ItemDrop.ItemData> Left=AccessTools.FieldRefAccess<Humanoid,ItemDrop.ItemData>("m_leftItem");
        internal static readonly AccessTools.FieldRef<Humanoid,VisEquipment> Vis=AccessTools.FieldRefAccess<Humanoid,VisEquipment>("m_visEquipment");
        private static readonly MethodInfo Setup=AccessTools.Method(typeof(Humanoid),"SetupEquipment");
        private static readonly MethodInfo SetState=AccessTools.Method(typeof(Humanoid),"SetAnimationState");
        private static readonly MethodInfo TriggerEffect=AccessTools.Method(typeof(Humanoid),"TriggerEquipEffect");
        private static readonly MethodInfo SetupState=AccessTools.Method(typeof(Humanoid),"SetupAnimationState");

        internal static Family FamilyOf(ItemDrop.ItemData item)=>item==null?Family.None:Policy.FamilyOf(
            item.m_shared.m_itemType==ItemDrop.ItemData.ItemType.OneHandedWeapon,item.m_shared.m_skillType==Skills.SkillType.Axes,item.m_shared.m_skillType==Skills.SkillType.Knives);
        // A matching weapon in each hand.
        internal static Family Pair(Humanoid h)
        {
            if(!(h is Player)||Plugin.Instance==null||!Plugin.Instance.Enabled.Value)return Family.None;
            Family right=FamilyOf(Right(h)),left=FamilyOf(Left(h));
            return right!=Family.None&&right==left?right:Family.None;
        }
        // The game's own dual weapon of that kind: its move set and animation state.
        internal static ItemDrop.ItemData.SharedData Template(Family family)
        {
            string name=family==Family.Axes?"AxeBerzerkr":family==Family.Knives?"KnifeSkollAndHati":null;
            GameObject prefab=name!=null&&ObjectDB.instance!=null?ObjectDB.instance.GetItemPrefab(name):null;
            return prefab!=null?prefab.GetComponent<ItemDrop>().m_itemData.m_shared:null;
        }
        internal static void Equip(Humanoid h,ItemDrop.ItemData item,bool effects)
        {
            ItemDrop.ItemData old=Left(h);
            if(old!=null)h.UnequipItem(old,effects); // a shield or torch steps aside
            Left(h)=item;item.m_equipped=true;
            VisEquipment vis=Vis(h);
            if(effects&&vis!=null&&vis.m_isPlayer&&FejdStartup.instance==null)
                item.m_shared.m_equipEffect.Create(vis.m_leftHand.position,vis.m_leftHand.rotation,null,1f,-1,h.GetZDOID());
            Setup.Invoke(h,null);
            if(effects)TriggerEffect.Invoke(h,new object[]{item});
        }
        // Toward the skill gate: the weapon skill, plus partial credit from woodcutting for axes.
        internal static double Skill(Player p,ItemDrop.ItemData item,out string how)
        {
            float weapon=p.GetSkillLevel(item.m_shared.m_skillType);
            how=$"$skill_{item.m_shared.m_skillType.ToString().ToLowerInvariant()} {weapon:0}";
            if(FamilyOf(item)!=Family.Axes)return weapon;
            float wood=p.GetSkillLevel(Skills.SkillType.WoodCutting),credit=Plugin.Instance.WoodcuttingCredit.Value;
            if(credit>0)how+=$" + {credit*100:0}% of $skill_woodcutting {wood:0}";
            return Policy.Effective(weapon,wood,credit);
        }
        internal static void Setup_(Humanoid h)=>Setup.Invoke(h,null);
        internal static void State(Humanoid h,ItemDrop.ItemData.AnimationState state)=>SetState.Invoke(h,new object[]{state});
        internal static void Refresh(Humanoid h){if(h!=null&&h.GetComponent<ZNetView>()?.IsValid()==true)SetupState.Invoke(h,null);}
    }
}
