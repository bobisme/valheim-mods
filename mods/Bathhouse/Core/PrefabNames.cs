namespace Bathhouse
{
    // Every prefab Bathhouse has shipped, by name. A prefab's name is how a save finds it again: one renamed or dropped is gone from
    // every player's world. Names are only ever added here, never removed or changed; the tests hold this list to the golden one
    // (tests/Bathhouse.Tests/prefabs.txt), and the game says at load if one of these is no longer made, or a new one isn't here yet.
    public static class PrefabNames
    {
        public static readonly string[] Shipped =
        {
            "Bathhouse_Stove",
            "Bathhouse_Bench",
            "Bathhouse_Bucket",
            "Bathhouse_PlungeTub",
            "Bathhouse_Locker",
        };
    }
}
