using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using Random=System.Random;

namespace GraveGoods
{
    // What the dead were buried with: weapons, armour and shields from the game's own recipes, sorted by how deep in the world
    // their ingredients come from.
    internal static class Pool
    {
        internal sealed class Entry{internal ItemDrop Prefab;internal Slot Slot;internal int Tier;}
        private static readonly List<Entry> Entries=new List<Entry>();
        private static ObjectDB _builtFor;

        internal static IReadOnlyList<Entry> All{get{Build();return Entries;}}
        private static void Build()
        {
            if(ObjectDB.instance==null||_builtFor==ObjectDB.instance&&Entries.Count>0)return;
            _builtFor=ObjectDB.instance;
            Entries.Clear();
            var seen=new HashSet<string>();
            foreach(Recipe r in ObjectDB.instance.m_recipes)
            {
                if(r==null||r.m_item==null||!r.m_enabled||r.m_resources==null||!seen.Add(r.m_item.name))continue;
                if(Novelty(r.m_item.name))continue;
                ItemDrop.ItemData.SharedData s=r.m_item.m_itemData.m_shared;
                Slot? slot=SlotOf(s);
                if(slot==null||s.m_icons==null||s.m_icons.Length==0)continue;
                int tier=Policy.RecipeTier(r.m_resources.Where(q=>q!=null&&q.m_resItem!=null).Select(q=>q.m_resItem.name));
                if(tier>0)Entries.Add(new Entry{Prefab=r.m_item,Slot=slot.Value,Tier=tier});
            }
            Plugin.Log($"Grave goods: {Entries.Count} kinds of weapons and gear, by tier: "+string.Join(", ",Enumerable.Range(1,Policy.MaxTier).Select(t=>t+": "+Entries.Count(e=>e.Tier==t))));
        }
        // Things no warrior was buried with: tankards, feast and festival gear, farm tools, a training sword.
        internal static bool Novelty(string prefab)=>prefab.Contains("Tankard")||prefab.Contains("Odin")||prefab.Contains("Celebration")||prefab.Contains("Anniversary")
            ||prefab=="Shovel"||prefab=="Scythe"||prefab=="SwordWood"||prefab=="KnifeButcher";
        internal static Slot? SlotOf(ItemDrop.ItemData.SharedData s)
        {
            switch(s.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.OneHandedWeapon:case ItemDrop.ItemData.ItemType.TwoHandedWeapon:
                case ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft:case ItemDrop.ItemData.ItemType.Bow:
                    switch(s.m_skillType)
                    {
                        case Skills.SkillType.Pickaxes:case Skills.SkillType.WoodCutting:case Skills.SkillType.Fishing:case Skills.SkillType.None:return null;
                    }
                    return Slot.Weapon;
                case ItemDrop.ItemData.ItemType.Shield:return Slot.Shield;
                case ItemDrop.ItemData.ItemType.Helmet:case ItemDrop.ItemData.ItemType.Chest:case ItemDrop.ItemData.ItemType.Legs:case ItemDrop.ItemData.ItemType.Shoulder:return Slot.Armor;
                default:return null;
            }
        }
        internal static string ArmorClass(ItemDrop.ItemData.SharedData s)
        {
            switch(s.m_itemType)
            {
                case ItemDrop.ItemData.ItemType.Helmet:return "Helmet";
                case ItemDrop.ItemData.ItemType.Chest:return "Chest";
                case ItemDrop.ItemData.ItemType.Legs:return "Legs";
                case ItemDrop.ItemData.ItemType.Shoulder:return "Shoulder";
                case ItemDrop.ItemData.ItemType.Shield:return "Shield";
                default:return "";
            }
        }
        // Something of this tier; if the world has nothing that deep (or that shallow), the nearest tier that has.
        internal static Entry Pick(int tier,Random rng)
        {
            IReadOnlyList<Entry> all=All;
            if(all.Count==0)return null;
            for(int d=0;d<Policy.MaxTier;d++)
                foreach(int t in new[]{tier-d,tier+d})
                {
                    Entry[] fit=all.Where(e=>e.Tier==t).ToArray();
                    if(fit.Length>0)return fit[rng.Next(fit.Length)];
                }
            return null;
        }
    }

    internal static class Loot
    {
        internal static readonly Random Rng=new Random();
        internal const string RolledKey="bob_gravegoods_rolled";

        // One buried thing: a new item of the kind, at a quality fitting its rarity, with its powers recorded on it.
        // record: a real find (a saga found is remembered in the world, so it comes only once); test finds aren't.
        internal static ItemDrop.ItemData Make(Pool.Entry e,Rarity rarity,int tier,Random rng,bool record=false)
        {
            ItemDrop.ItemData item=e.Prefab.m_itemData.Clone();
            item.m_dropPrefab=e.Prefab.gameObject;
            item.m_stack=1;
            item.m_quality=Policy.RollQuality(rarity,item.m_shared.m_maxQuality,rng);
            item.m_worldLevel=(byte)Game.m_worldLevel;
            string skill=e.Slot==Slot.Weapon?item.m_shared.m_skillType.ToString():"";
            ZoneSystem zs=ZoneSystem.instance;
            Goods g=Policy.Roll(rarity,tier,e.Slot,skill,Pool.ArmorClass(item.m_shared),rng,record&&zs!=null?n=>zs.GetGlobalKey(Policy.SagaKey(n)):(Func<string,bool>)null);
            if(record&&zs!=null&&g.Saga!="")zs.SetGlobalKey(Policy.SagaKey(g.Name));
            item.m_customData=new Dictionary<string,string>(item.m_customData??new Dictionary<string,string>()){[Policy.Key]=Policy.Save(g)};
            item.m_durability=item.GetMaxDurability();
            return item;
        }
        internal static ItemDrop.ItemData Roll(int chestTier,Random rng,int bump=0,bool record=false)
        {
            int tier=Policy.ChestTier(chestTier,rng);
            Pool.Entry e=Pool.Pick(tier,rng);
            return e==null?null:Make(e,Policy.RollRarity(Plugin.Weights(),rng,bump),tier,rng,record);
        }
        internal static bool IsTreasure(string prefab)=>prefab.StartsWith("TreasureChest",StringComparison.Ordinal)||prefab=="shipwreck_karve_chest";
        internal static int TierAt(Vector3 at)=>WorldGenerator.instance==null?1:Policy.BiomeTier(WorldGenerator.instance.GetBiome(at).ToString());
    }

    // Each treasure chest is rolled once, by whoever owns it, the first time it is loaded and not in use (chests from before
    // the mod too: the dead have stirred).
    [HarmonyPatch(typeof(Container),"CheckForChanges")]
    internal static class SeedChests
    {
        private static readonly ConditionalWeakTable<Container,object> NotTreasure=new ConditionalWeakTable<Container,object>();
        private static readonly AccessTools.FieldRef<Container,ZNetView> View=AccessTools.FieldRefAccess<Container,ZNetView>("m_nview");
        private static readonly System.Reflection.MethodInfo LoadItems=AccessTools.Method(typeof(Container),"Load");
        private static void Postfix(Container __instance)
        {
            if(NotTreasure.TryGetValue(__instance,out _))return;
            ZNetView nv=View(__instance);
            if(nv==null||!nv.IsValid())return;
            if(!Loot.IsTreasure(Utils.GetPrefabName(nv.gameObject))){NotTreasure.Add(__instance,null);return;}
            ZDO z=nv.GetZDO();
            if(!nv.IsOwner()||__instance.IsInUse()||z.GetBool(Loot.RolledKey))return;
            z.Set(Loot.RolledKey,true);
            if(Loot.Rng.NextDouble()>=Plugin.ChestChance.Value)return;
            LoadItems.Invoke(__instance,null); // (what's in it already, before anything is added)
            Inventory inv=__instance.GetInventory();
            if(!inv.HaveEmptySlot())return;
            ItemDrop.ItemData item=Loot.Roll(Loot.TierAt(__instance.transform.position),Loot.Rng,record:true);
            if(item==null)return;
            inv.AddItem(item);
            Goods g=Effects.Of(item);
            Plugin.Log($"Grave goods: {Policy.RarityNames[(int)g.Rarity]} {item.m_dropPrefab.name} (tier {g.Tier}) in {Utils.GetPrefabName(nv.gameObject)} at {__instance.transform.position:F0}");
        }
    }
}
