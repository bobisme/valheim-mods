namespace Byre
{
    // Every prefab Byre has shipped, by name. A prefab's name is how a save finds it again: one renamed or dropped is gone from every
    // player's world (a herd, a bag of wool, a trough). Names are only ever added here, never removed or changed; the tests hold this list
    // to the golden one (tests/Byre.Tests/prefabs.txt).
    public static class PrefabNames
    {
        public static readonly string[] Shipped =
        {
            "ByreSheep",
            "ByreLamb",
            "ByreSheep_ragdoll",
            "ByreGoat",
            "ByreKid",
            "ByreGoat_ragdoll",
            "ByreWool",
            "ByreGoatMilk",
            "ByreShears",
            "ByreMilkPail",
            "ByreTrough",
            "ByreHayRack",
            "ByreStall",
        };
    }
}
