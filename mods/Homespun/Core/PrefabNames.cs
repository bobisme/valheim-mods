namespace Homespun
{
    // Every prefab Homespun has shipped, by name. A prefab's name is how a save finds it again: one renamed or dropped is gone from
    // every player's world (a piece from the ground, an item from every bag and chest). Names are only ever added here, never removed or
    // changed; the tests hold this list to the golden one (tests/Homespun.Tests/prefabs.txt), and the game says at load if one of these
    // is no longer made, or a new one isn't here yet.
    public static class PrefabNames
    {
        public static readonly string[] Shipped =
        {
            "Homespun_RettingPool",
            "Homespun_FlaxBrake",
            "Homespun_Loom",
            "Homespun_SewingTable",
            "Homespun_RettedFlax",
            "Homespun_LineFlax",
            "Homespun_Wool",
            "Homespun_WoolYarn",
            "Homespun_LinenBolt",
            "Homespun_WoolBolt",
            "Homespun_Band",
            "Homespun_Spindle",
            "Homespun_LinenUndertunic",
            "Homespun_WoolTunic",
            "Homespun_WoolDress",
            "Homespun_WoolTrousers",
            "Homespun_HoodedCloak",
            "Homespun_Nettle",
            "Homespun_RettedNettle",
            "Homespun_LineNettle",
            "Homespun_Pickable_Nettle",
            "Homespun_Pickable_NettleCrop",
            "Homespun_sapling_nettle",
        };
    }
}
