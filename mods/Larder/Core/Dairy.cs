using System.Collections.Generic;
using System.Linq;

namespace Larder
{
    // The dairy, as data and nothing of the game's: what each station takes, gives and how long it is, so tests can check it and the
    // game side only carries it out. Milk is Byre's (a goat's or a cow's); without Byre there is no milk, and the stations stay out of
    // the hammer's menu.
    //
    //   cow's milk ─ butter churn ─▶ butter (3 milk)      goat's milk ─ butter churn ─▶ skyr (2 milk and a handful of blueberries: 2 bowls)
    //   either milk ─ cheese press (pressed and left to set, a milk at a time) ─▶ cheese
    public static class Names
    {
        // Stations
        public const string Churn = "Larder_Churn", Press = "Larder_CheesePress";
        public const string Workbench = "piece_workbench", Wheel = "piece_spinningwheel"; // (the game's, made over: a crafting station, and a smelter's queue)
        // Foods
        public const string Butter = "Larder_Butter", Cheese = "Larder_Cheese", Skyr = "Larder_Skyr";
        // Byre's milk
        public const string GoatMilk = "ByreGoatMilk", CowMilk = "ByreCowMilk";
        public static readonly string[] Milks = { GoatMilk, CowMilk };
    }

    // A food: what it does eaten (as the game's: health, stamina, regen a tick, and how many seconds it lasts).
    public sealed class Food
    {
        public string Prefab, Title, Description, Model; public float Health, Stamina, Regen, Seconds, Weight; public int Stack;
    }

    // A recipe at a crafting station: the item, how many it makes, and what it takes.
    public sealed class Make
    {
        public string Item, Station; public int Amount; public (string item, int n)[] Needs;
    }

    public static class Dairy
    {
        // Against the game's: cooked meat 30/10, honey 8/35, queens jam 14/40, carrot soup 15/45, deer stew 45/15 (30 minutes), and
        // Byre's own milk 12/30 (goat) and 10/38 (cow). Butter is milk's stamina, gathered; skyr evens it out with the berries; cheese is
        // the keeping food, health for a long while (a voyage's).
        public static readonly Food[] Foods =
        {
            new Food { Prefab = Names.Butter, Title = "Butter", Model = "butter", Health = 14, Stamina = 48, Regen = 2, Seconds = 1800, Weight = 0.3f, Stack = 20,
                Description = "A pat of butter churned from the cream, salted to keep. Rich: good for the stamina, and long." },
            new Food { Prefab = Names.Skyr, Title = "Skyr", Model = "skyr", Health = 25, Stamina = 25, Regen = 2, Seconds = 1500, Weight = 0.5f, Stack = 10,
                Description = "Thick soured skyr, strained from the skimmed milk, with blueberries on it. Filling, and easy on the stomach." },
            new Food { Prefab = Names.Cheese, Title = "Cheese", Model = "cheese", Health = 32, Stamina = 12, Regen = 2, Seconds = 2400, Weight = 0.5f, Stack = 20,
                Description = "A small round cheese, pressed and set hard. It keeps, and a piece of it lasts a long while: food for a voyage." },
        };

        // The churn's recipes: one milk each, so each food is listed once (a recipe per milk listed every food twice): butter from the
        // cow's, the creamier; skyr from the goat's. The press takes both.
        public static IEnumerable<Make> Churned() => new[]
        {
            new Make { Item = Names.Butter, Station = Names.Churn, Amount = 1, Needs = new[] { (Names.CowMilk, 3) } },
            new Make { Item = Names.Skyr, Station = Names.Churn, Amount = 2, Needs = new[] { (Names.GoatMilk, 2), ("Blueberries", 1) } },
        };

        // The press: a milk in, a cheese out, each after this long (a game day is 30 minutes); it holds this many.
        public const float PressSeconds = 240f;
        public const int PressHolds = 20;
        public static IEnumerable<(string from, string to)> Pressed() => Names.Milks.Select(m => (m, Names.Cheese));

        // What the stations cost to build (at a workbench).
        public static readonly (string item, int n)[] ChurnCost = { ("Wood", 12), ("Resin", 2) };
        public static readonly (string item, int n)[] PressCost = { ("Wood", 16), ("Stone", 8), ("LeatherScraps", 2) };

        public static IEnumerable<string> Items => Foods.Select(f => f.Prefab);
        public static IEnumerable<string> Pieces => new[] { Names.Churn, Names.Press };
    }
}
