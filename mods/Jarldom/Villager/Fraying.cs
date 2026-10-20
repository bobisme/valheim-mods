using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // Clothes wearing out, in the world (the rules are Core/Fray.cs): the hours worked fray what's worn, mending at the sewing table
    // costs the stores linen thread and is counted on the garment, and one past its last mending wears through and is thrown away.
    internal static class Fraying
    {
        public const string SewingTable = "Homespun_SewingTable";
        public static bool Frays(ItemDrop.ItemData i) => i != null && i.m_shared.m_useDurability && Weaving.IsClothes(i) && !Weaving.IsRags(i);
        public static int Mends(ItemDrop.ItemData i) => Fray.Mends(i.m_customData != null && i.m_customData.TryGetValue(Fray.MendsKey, out string s) ? s : null);
        private static float Fraction(ItemDrop.ItemData i) => i.m_durability / Mathf.Max(1f, i.GetMaxDurability());

        // (from the needs tick: the hours just gone, at work or out with the jarl)
        public static void Tick(Brain b, double hours, bool wet, bool cold)
        {
            if (b.Stripped || b.Activity != Activity.Work && b.Activity != Activity.Follow) return;
            double h = Fray.Counted(hours);
            if (h <= 0) return;
            int season = Year.Today().SeasonLength;
            Inventory bag = b.Body.GetInventory();
            foreach (ItemDrop.ItemData i in bag.GetAllItems().Where(i => i.m_equipped && Frays(i)).ToList())
            {
                float rate = Fray.Rate(b.Activity == Activity.Work && Fray.Heavy(b.S.Prof), wet, cold, i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Helmet);
                i.m_durability = Mathf.Max(0, i.m_durability - Fray.PerHour(i.GetMaxDurability(), season, rate) * (float)h);
                if (i.m_durability > 0) continue;
                string name = Localization.instance.Localize(i.m_shared.m_name).ToLower();
                var slot = i.m_shared.m_itemType;
                b.Body.UnequipItem(i, false); bag.RemoveItem(i);
                // What's left of it is rags, worn till the weaver's next garment (Gear.Maintain puts them on); a hat or cloak is just gone.
                string rags = slot == ItemDrop.ItemData.ItemType.Chest ? "ArmorRagsChest" : slot == ItemDrop.ItemData.ItemType.Legs ? "ArmorRagsLegs" : null;
                if (rags != null && !bag.GetAllItems().Any(x => Classes.Prefab(x) == rags) && Assets.Find(rags) is GameObject rp) bag.AddItem(rp, 1);
                b.Say(new[] { $"My {name} has worn through.", $"That's the end of my {name}: more holes than cloth.", $"My {name} is rags now.", $"My {name} fell apart at the seams." }[Random.Range(0, 4)], 5);
                b.S.Bump("wornout");
            }
            bag.m_onChanged?.Invoke();
        }
        public static bool Threadbare(Brain b) => !b.Stripped && b.Body.GetInventory().GetAllItems().Any(i => i.m_equipped && Frays(i) && Fray.Threadbare(Fraction(i)));

        // Clothes worn thin and not mended too often yet, with the thread for it in the stores (Weaving's thread count).
        public static bool CanMend(Colony c, ItemDrop.ItemData i) => Fray.Mendable(Fraction(i), Mends(i)) && c != null && Crafts.InStock(c, Weaving.Thread) >= Fray.MendThread;
        // The clothes' bench: Homespun's sewing table mends all clothes; without it, the bench its recipe is made at.
        public static bool MendsAt(CraftingStation st) => st != null && Utils.GetPrefabName(st.gameObject) == SewingTable;
        // Mending it: thread from the stores, the mending counted on it. False (nothing done) with no thread.
        public static bool Mend(Colony c, ItemDrop.ItemData i)
        {
            if (!Frays(i)) return true;
            int left = Fray.MendThread;
            foreach (Container chest in Stock.Chests(c).Where(x => x.category != "treasury").Select(x => x.chest))
            {
                foreach (ItemDrop.ItemData t in chest.GetInventory().GetAllItems().Where(t => Classes.Prefab(t) == Weaving.Thread).ToList())
                {
                    if (left <= 0) break;
                    if (ContainerAccess.InUse(chest)) break;
                    ContainerAccess.TakeOver(chest);
                    int n = Mathf.Min(left, t.m_stack); chest.GetInventory().RemoveItem(t, n); left -= n;
                }
                if (left <= 0) break;
            }
            if (left == Fray.MendThread) return false;
            i.m_customData[Fray.MendsKey] = (Mends(i) + 1).ToString();
            return true;
        }
    }
}
