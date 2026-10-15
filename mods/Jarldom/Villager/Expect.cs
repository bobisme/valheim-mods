using System.Collections.Generic;
using System.Linq;

namespace Jarldom
{
    // The game's side of Fare: the tier the colony's standing wants (no more than the world has opened), and the tiers of what a villager
    // ate lately (from their record of meals).
    internal static class Expect
    {
        public static int Wanted(Colony c) => c == null || ZoneSystem.instance == null ? 0 : Fare.Wanted(c.R.Rank, OpenedTest >= 0 ? OpenedTest : Fare.Opened(k => ZoneSystem.instance.GetGlobalKey(k)));
        public static int OpenedTest = -1; // (test: "jarldom fare <lands opened>", this session only; -1 is the world's own)
        public static int TierOf(string prefab)
        {
            var item = ObjectDB.instance?.GetItemPrefab(prefab)?.GetComponent<ItemDrop>()?.m_itemData;
            return Fare.Of(prefab, item?.m_shared.m_food ?? 0);
        }
        public static List<int> Recent(VillagerState s) => s.Foods.Select(TierOf).ToList();
        public static bool Met(Colony c, VillagerState s) => Fare.Met(Recent(s), Wanted(c));
    }
}
