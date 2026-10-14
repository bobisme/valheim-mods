namespace GraveGoods
{
    // For other mods, found by reflection: the type "GraveGoods.Api" in this assembly (see Jarldom's Compat/API-CONVENTION.md).
    // Rules: bump Version when a method is added; never change a method's signature (add a new one).
    public static class Api
    {
        public const int Version = 1;

        // An item's grave-goods rarity: 0 Plain, 1 Fine, 2 Runed, 3 Heirloom, 4 Saga; -1 if it isn't grave goods.
        public static int Rarity(ItemDrop.ItemData item)
        {
            if (item?.m_customData == null || !item.m_customData.TryGetValue(Policy.Key, out string s)) return -1;
            Goods g = Policy.Load(s);
            return g == null ? -1 : (int)g.Rarity;
        }
        // A heirloom's or saga weapon's own name ("" for the rest, or if it isn't grave goods).
        public static string Name(ItemDrop.ItemData item)
        {
            if (item?.m_customData == null || !item.m_customData.TryGetValue(Policy.Key, out string s)) return "";
            return Policy.Load(s)?.Name ?? "";
        }
    }
}
