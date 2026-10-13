using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // What a villager wears and holds: the best armour in the bag, and in hand what the task wants (a tool, a weapon, a bow, or nothing).
    internal static class Gear
    {
        public static string Problem; // the last reason a job was skipped for want of gear (shown when idle)

        public static IEnumerable<ItemDrop.ItemData> Items(Brain b) => b.Body.GetInventory()?.GetAllItems() ?? Enumerable.Empty<ItemDrop.ItemData>();

        public static bool Usable(ItemDrop.ItemData i) => i.m_durability > 0 || !i.m_shared.m_useDurability;
        // Is the item a tool of the kind (any one, for fetching from the stores)?
        public static bool Is(string kind, ItemDrop.ItemData i)
        {
            if (!Usable(i) || i.m_dropPrefab == null) return false;
            string n = i.m_dropPrefab.name;
            switch (kind)
            {
                case "axe": return i.GetDamage().m_chop > 0 && (i.m_shared.m_skillType == Skills.SkillType.Axes || i.m_shared.m_skillType == Skills.SkillType.WoodCutting);
                case "pickaxe": return i.GetDamage().m_pickaxe > 0;
                case "cultivator": return n == "Cultivator";
                case "hammer": return n == "Hammer";
                case "hoe": return n == "Hoe";
                case "rod": return n == "FishingRod";
                case "tankard": return IsTankard(i);
                default: return false;
            }
        }
        // A tankard (a drinking horn, Dvergr or Odin's too): the game makes it a one-handed weapon whose blow is a drink. Not for fighting.
        public static bool IsTankard(ItemDrop.ItemData i) => Classes.Prefab(i).StartsWith("Tankard");
        public static bool IsWeapon(ItemDrop.ItemData i) => Usable(i) &&
            (i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon || i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon || i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft)
            && i.m_shared.m_skillType != Skills.SkillType.Pickaxes && i.m_dropPrefab != null && !IsTankard(i) && i.m_dropPrefab.name != "Hammer" && i.m_dropPrefab.name != "Hoe" && i.m_dropPrefab.name != "Cultivator" && i.m_dropPrefab.name != "FishingRod";

        public static ItemDrop.ItemData Tool(Brain b, string kind, int tier = 0)
        {
            IEnumerable<ItemDrop.ItemData> all = Items(b).Where(i => i.m_durability > 0 || !i.m_shared.m_useDurability);
            switch (kind)
            {
                case "axe": return all.Where(i => i.GetDamage().m_chop > 0 && (i.m_shared.m_skillType == Skills.SkillType.Axes || i.m_shared.m_skillType == Skills.SkillType.WoodCutting) && i.m_shared.m_toolTier >= tier).OrderByDescending(i => i.m_shared.m_toolTier).ThenByDescending(i => i.GetDamage().m_chop).FirstOrDefault();
                case "pickaxe": return all.Where(i => i.GetDamage().m_pickaxe > 0 && i.m_shared.m_toolTier >= tier).OrderByDescending(i => i.m_shared.m_toolTier).ThenByDescending(i => i.GetDamage().m_pickaxe).FirstOrDefault();
                case "cultivator": return all.FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == "Cultivator");
                case "hammer": return all.FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == "Hammer");
                case "hoe": return all.FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == "Hoe");
                case "rod": return all.FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == "FishingRod");
                case "tankard": return Items(b).FirstOrDefault(IsTankard);
                case "bow": return all.Where(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow && Ammo(b, i) != null).OrderByDescending(i => i.GetDamage().GetTotalDamage()).FirstOrDefault();
                case "weapon": return Weapon(b);
                default: return null;
            }
        }
        public static ItemDrop.ItemData Weapon(Brain b) => Items(b).Where(IsWeapon)
            .OrderByDescending(i => i.GetDamage().m_slash + i.GetDamage().m_pierce + i.GetDamage().m_blunt + i.GetDamage().m_fire + i.GetDamage().m_frost + i.GetDamage().m_poison + i.GetDamage().m_lightning + i.GetDamage().m_spirit).FirstOrDefault();
        public static ItemDrop.ItemData Shield(Brain b) => Items(b).Where(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield).OrderByDescending(i => i.m_shared.m_blockPower).FirstOrDefault();
        public static ItemDrop.ItemData Ammo(Brain b, ItemDrop.ItemData bow) => string.IsNullOrEmpty(bow.m_shared.m_ammoType) ? null : Items(b).FirstOrDefault(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo && i.m_shared.m_ammoType == bow.m_shared.m_ammoType);
        public static bool CanFight(Brain b) => Weapon(b) != null || Tool(b, "bow") != null;

        // Gear the profession's work needs, or why not.
        public static string Lacks(Brain b, Prof p, int tier = 0)
        {
            string tool = Tables.Of(p).Tool;
            switch (tool)
            {
                case "": return null;
                case "weapon": return CanFight(b) ? null : "no weapon";
                case "bow": return Tool(b, "bow") != null ? null : "no bow and arrows";
                default:
                    if (Tool(b, tool, tier) != null) return null;
                    return tier > 0 ? $"no {tool} of tier {tier} or better" : $"no {tool}";
            }
        }

        // Once a second: armour on, the right thing in hand.
        public static string Want(Brain b) => b.Task is LeisureTask lt && lt.Tool != "" ? lt.Tool : (b.Task is HallTask ht && !ht.Shelter || b.Task is LeisureTask lh && lh.Kind == "hall") && Tool(b, "tankard") != null ? "tankard" : b.Task is BuildTask || b.Task is UpkeepTask ? "hammer" : b.Task is WorkTask w ? w.Tool : Combat.Busy(b) || b.HasFoe ? (b.Combat_Bow ? "bow" : "weapon") : b.Task is DefendTask d && d.Fighter || b.Task is FollowTask && CanFight(b) ? "weapon" : ""; // (out with the jarl: weapon in hand)
        public static void Maintain(Brain b)
        {
            Humanoid h = b.Body;
            if (h.InAttack() || b.Held != null) return;
            Inventory inv = h.GetInventory();
            if (inv == null) return;
            // Anything worn that left the bag (a player took it) comes off.
            foreach (ItemDrop.ItemData worn in new[] { h.RightItem, h.LeftItem }) if (worn != null && !inv.ContainsItem(worn)) h.UnequipItem(worn, false);
            foreach (var type in new[] { ItemDrop.ItemData.ItemType.Helmet, ItemDrop.ItemData.ItemType.Chest, ItemDrop.ItemData.ItemType.Legs, ItemDrop.ItemData.ItemType.Shoulder, ItemDrop.ItemData.ItemType.Utility })
            {
                ItemDrop.ItemData best = inv.GetAllItems().Where(i => i.m_shared.m_itemType == type && (i.m_durability > 0 || !i.m_shared.m_useDurability)).OrderByDescending(i => i.GetArmor()).ThenBy(i => Weaving.IsRags(i) ? 1 : 0).ThenByDescending(i => i.m_quality).FirstOrDefault(); // (clothes over rags, armour over both)
                if (best != null && !h.IsItemEquiped(best)) h.EquipItem(best, false);
                // (one worn per slot: a flag left on the old one from a reload comes off)
                foreach (ItemDrop.ItemData other in inv.GetAllItems().Where(i => i != best && i.m_equipped && i.m_shared.m_itemType == type).ToList()) { if (h.IsItemEquiped(other)) h.UnequipItem(other, false); other.m_equipped = false; }
            }
            string want = Want(b);
            ItemDrop.ItemData hand = want == "" ? null : Tool(b, want);
            if (hand == null && want == "weapon") hand = Tool(b, "bow");
            if (hand != null)
            {
                if (!h.IsItemEquiped(hand)) h.EquipItem(hand, false);
                if (hand.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow) { ItemDrop.ItemData ammo = Ammo(b, hand); if (ammo != null && !h.IsItemEquiped(ammo)) h.EquipItem(ammo, false); }
                if (hand.m_shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon && want == "weapon") { ItemDrop.ItemData shield = Shield(b); if (shield != null && !h.IsItemEquiped(shield)) h.EquipItem(shield, false); }
            }
            else
            {
                if (h.RightItem != null) h.UnequipItem(h.RightItem, false);
                if (h.LeftItem != null && h.LeftItem.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Shield) h.UnequipItem(h.LeftItem, false);
            }
        }
        // Wear on a tool used for work (the game's own durability drain), and an alert when it breaks.
        public static void Wear(Brain b, ItemDrop.ItemData tool)
        {
            if (tool == null || !tool.m_shared.m_useDurability) return;
            float drain = tool.m_shared.m_useDurabilityDrain * (b.S.Has("clumsy") ? 1.5f : 1f);
            tool.m_durability = Mathf.Max(0, tool.m_durability - drain);
            if (tool.m_durability <= 0) { string tn = Localization.instance.Localize(tool.m_shared.m_name).ToLower(); b.Say(new[] { $"My {tn} broke!", $"My {tn} is no more!", $"The {tn} gave out under me.", $"That's the end of my {tn}.", $"My {tn} snapped in two!" }[UnityEngine.Random.Range(0, 5)], 5); }
            b.Body.GetInventory().m_onChanged?.Invoke();
        }
    }
}
