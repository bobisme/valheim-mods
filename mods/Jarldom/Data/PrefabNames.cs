namespace Jarldom
{
    // Every prefab Jarldom has shipped, by name. A prefab's name is how a save finds it again: one renamed or dropped is gone from every
    // player's world (pieces vanish, items leave bags). Names are only ever added here, never removed or changed; the tests hold this list
    // to the golden one (tests/Jarldom.Tests/prefabs.txt), and the game says at load if one of these is no longer made, or a new one isn't
    // here yet.
    public static class PrefabNames
    {
        public static readonly string[] Shipped =
        {
            "JarldomAltar_freyja",
            "JarldomAltar_frigg",
            "JarldomAltar_hel",
            "JarldomAltar_njord",
            "JarldomAltar_odin",
            "JarldomAltar_thor",
            "JarldomAltar_tyr",
            "JarldomAltar_ullr",
            "JarldomArmRing",
            "JarldomDesignate",
            "JarldomDrum",
            "JarldomEinherjar",
            "JarldomFuneralKarve",
            "JarldomGildedBanner",
            "JarldomGildedSeat",
            "JarldomHighSeatPillars",
            "JarldomMemorial",
            "JarldomOreClaim",
            "JarldomPyre",
            "JarldomRelic",
            "JarldomRelicGoldenFlower",
            "JarldomRelicRavenFeather",
            "JarldomRelicSpindle",
            "JarldomRelicStormStone",
            "JarldomRemains",
            "JarldomSoul",
            "JarldomVillager",
            "JarldomWarHorn",
            "JarldomWatchPost",
        };
    }
}
