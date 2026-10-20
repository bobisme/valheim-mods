namespace Shipyard
{
    public static class Names
    {
        public const string Slipway = "Shipyard_Slipway", Faering = "Shipyard_Faering", Knarr = "Shipyard_Knarr", Naust = "Shipyard_Naust";
    }

    // Every prefab Shipyard has shipped, by name. A prefab's name is how a save finds it again: one renamed or dropped is gone from every
    // player's world. Names are only ever added here, never removed or changed; the tests hold this list to the golden one
    // (tests/Shipyard.Tests/prefabs.txt).
    public static class PrefabNames
    {
        public static readonly string[] Shipped =
        {
            "Shipyard_Slipway",
            "Shipyard_Faering",
            "Shipyard_Knarr",
            "Shipyard_Naust",
        };
    }
}
