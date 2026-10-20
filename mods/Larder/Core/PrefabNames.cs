namespace Larder
{
    // Every prefab Larder has shipped, by name. A prefab's name is how a save finds it again: one renamed or dropped is gone from
    // every player's world (a piece from the ground, an item from every bag and chest). Names are only ever added here, never removed or
    // changed; the tests hold this list to the golden one (tests/Larder.Tests/prefabs.txt), and the game says at load if one of these
    // is no longer made, or a new one isn't here yet.
    public static class PrefabNames
    {
        public static readonly string[] Shipped =
        {
            "Larder_Churn",
            "Larder_CheesePress",
            "Larder_Butter",
            "Larder_Cheese",
            "Larder_Skyr",
            "Larder_SmokedMeat",
            "Larder_SaltMeat",
            "Larder_Stockfish",
            "Larder_Salt",
            "Larder_Smokehouse",
            "Larder_SaltPan",
            "Larder_SaltBarrel",
            "Larder_DryingRack",
            "Larder_BakeOven",
            "Larder_FlatbreadDough",
            "Larder_Flatbread",
            "Larder_SpoiledFood",
        };
    }
}
