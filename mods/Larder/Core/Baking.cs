using System.Collections.Generic;
using System.Linq;

namespace Larder
{
    // The bake oven (L2), as data and nothing of the game's: barley flatbread, the old keeping bread, baked thin and dry on the floor of
    // a stone oven so that it lasts a winter or a voyage. Dough is mixed at the game's cauldron from barley flour; the oven bakes it as the
    // game's own oven does (dough in by its mouth, wood in below, taken out when it's done or left to burn).
    //
    //   barley flour ─ cauldron ─▶ flatbread dough ─ bake oven (and firewood) ─▶ flatbread
    //
    // The game's oven bakes its bread (23/70, 25 minutes) too; it's a dvergr oven of iron and surtling cores. Ours is only stone and clay,
    // and its bread is lighter and weaker, and keeps (Api.IsPreserved). Rye bread waits for the Fields mod's rye.
    public static class Baked
    {
        public const string Oven = "Larder_BakeOven", Dough = "Larder_FlatbreadDough", Flatbread = "Larder_Flatbread";
        // The game's: its oven (a cooking station with wood for fuel and four slots), its cauldron (where dough is mixed), the flour.
        public const string GameOven = "piece_oven", Cauldron = "piece_cauldron", Flour = "BarleyFlour";
    }

    public static class Baking
    {
        public static readonly Food Flatbread = new Food { Prefab = Baked.Flatbread, Title = "Flatbread", Model = "flatbread", Health = 20, Stamina = 55, Regen = 2, Seconds = 2400,
            Weight = 0.1f, Stack = 30, Description = "Thin barley flatbread, baked dry and hard on the floor of a stone oven. Light, and it keeps through a winter or a voyage." };
        // Dough: what the oven bakes (not eaten).
        public static readonly Food Dough = new Food { Prefab = Baked.Dough, Title = "Flatbread dough", Model = "flatbreaddough", Weight = 0.3f, Stack = 20,
            Description = "Barley flour and water, rolled out thin. Bake it in a bake oven." };

        // The cauldron's recipe: two flour a bread.
        public static readonly Make Mixed = new Make { Item = Baked.Dough, Station = Baked.Cauldron, Amount = 2, Needs = new[] { (Baked.Flour, 4) } };
        // The oven: dough to bread in this long a loaf (the game's bakes in 50 s; left in twice as long, it burns), four at once.
        public const float BakeSeconds = 50f;
        public const int Slots = 4;
        public static IEnumerable<(string from, string to)> Bakes() => new[] { (Baked.Dough, Baked.Flatbread) };
        public const string Fuel = "Wood"; public const int MaxFuel = 10;
        public static readonly (string item, int n)[] OvenCost = { ("Stone", 20), ("Wood", 6) };
        public const string OvenTitle = "Bake oven";
        public const string OvenDescription = "A stone and clay dome over a firebox. Fire it with wood, and bake flatbread on its floor, four at a time.";

        public static IEnumerable<Food> Items => new[] { Flatbread, Dough };
        public static IEnumerable<string> ItemNames => Items.Select(f => f.Prefab);
        public static IEnumerable<string> Pieces => new[] { Baked.Oven };
        // The breads that keep.
        public static bool Keeps(string prefab) => prefab == Baked.Flatbread;
    }
}
