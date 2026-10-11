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

        public static ItemDrop.ItemData Tool(Brain b, string kind, int tier = 0)
        {
            IEnumerable<ItemDrop.ItemData> all = Items(b).Where(i => i.m_durability > 0 || !i.m_shared.m_useDurability);
            switch (kind)
            {
                case "axe": return all.Where(i => i.GetDamage().m_chop > 0 && (i.m_shared.m_skillType == Skills.SkillType.Axes || i.m_shared.m_skillType == Skills.SkillType.WoodCutting) && i.m_shared.m_toolTier >= tier).OrderByDescending(i => i.m_shared.m_toolTier).ThenByDescending(i => i.GetDamage().m_chop).FirstOrDefault();
                case "pickaxe": return all.Where(i => i.GetDamage().m_pickaxe > 0 && i.m_shared.m_toolTier >= tier).OrderByDescending(i => i.m_shared.m_toolTier).ThenByDescending(i => i.GetDamage().m_pickaxe).FirstOrDefault();
                case "cultivator": return all.FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == "Cultivator");
                case "hammer": return all.FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == "Hammer");
                case "rod": return all.FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == "FishingRod");
                case "bow": return all.Where(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow && Ammo(b, i) != null).OrderByDescending(i => i.GetDamage().GetTotalDamage()).FirstOrDefault();
                case "weapon": return Weapon(b);
                default: return null;
            }
        }
        public static ItemDrop.ItemData Weapon(Brain b) => Items(b).Where(i => (i.m_durability > 0 || !i.m_shared.m_useDurability) &&
            (i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon || i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon || i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft)
            && i.m_shared.m_skillType != Skills.SkillType.Pickaxes && i.m_dropPrefab != null && i.m_dropPrefab.name != "Hammer" && i.m_dropPrefab.name != "Hoe" && i.m_dropPrefab.name != "Cultivator" && i.m_dropPrefab.name != "FishingRod")
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
        public static string Want(Brain b) => b.Task is WorkTask w ? w.Tool : Combat.Busy(b) || b.HasFoe ? (b.Combat_Bow ? "bow" : "weapon") : "";
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
                ItemDrop.ItemData best = inv.GetAllItems().Where(i => i.m_shared.m_itemType == type && (i.m_durability > 0 || !i.m_shared.m_useDurability)).OrderByDescending(i => i.GetArmor()).ThenByDescending(i => i.m_quality).FirstOrDefault();
                if (best != null && !h.IsItemEquiped(best)) h.EquipItem(best, false);
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
            if (tool.m_durability <= 0) b.Say($"My {Localization.instance.Localize(tool.m_shared.m_name).ToLower()} broke!", 5);
            b.Body.GetInventory().m_onChanged?.Invoke();
        }
    }
}
