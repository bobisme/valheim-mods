using System;
using System.Collections.Generic;
using System.Linq;

namespace Jarldom
{
    // Who the reborn are: Old Norse names, the bynames they earn, and the one line of how they lived and fell.
    // Everything is drawn from a seeded random, so the same soul is the same on every game that makes it.
    public static class Names
    {
        public static readonly string[] Male =
        {
            "Arnbjorn", "Asgeir", "Atli", "Bjarni", "Bodvar", "Bolli", "Brand", "Egil", "Eirik", "Eyvind", "Finnbogi", "Flosi", "Geir", "Gisli",
            "Grettir", "Grim", "Gunnar", "Hakon", "Halfdan", "Hallbjorn", "Hallstein", "Harald", "Hauk", "Helgi", "Hjalti", "Hrafn", "Hrolf",
            "Ingolf", "Ivar", "Jorund", "Kari", "Ketil", "Kolbein", "Leif", "Ljot", "Magnus", "Njal", "Odd", "Olaf", "Ormar", "Ottar", "Ragnar",
            "Rognvald", "Sigurd", "Skapti", "Snorri", "Sturla", "Svein", "Thorbjorn", "Thorfinn", "Thorgeir", "Thorir", "Thorkel", "Thorolf",
            "Thorstein", "Ulf", "Valgard", "Vermund", "Vigfus", "Yngvar",
        };
        public static readonly string[] Female =
        {
            "Alfhild", "Asa", "Asdis", "Astrid", "Aud", "Bergthora", "Borghild", "Brynhild", "Dagny", "Eydis", "Freydis", "Frida", "Geirlaug",
            "Gudrun", "Gunnhild", "Gyda", "Hallgerd", "Helga", "Herdis", "Hervor", "Hildigunn", "Hlif", "Ingibjorg", "Ingrid", "Jorunn", "Katla",
            "Ragna", "Ragnhild", "Rannveig", "Signy", "Sigrid", "Solveig", "Steinunn", "Svanhild", "Thora", "Thordis", "Thorgerd", "Thorunn",
            "Thurid", "Tofa", "Ulfhild", "Unn", "Valdis", "Vigdis", "Yrsa",
        };

        private static readonly string[] Origin =
        {
            "Fell at a river ford", "Fell on a burning longship", "Fell holding a shield-wall", "Fell in a raid on a monastery", "Fell defending a hall",
            "Fell in a duel on a holmgang isle", "Fell in the snow of a mountain pass", "Fell storming a hill-fort", "Fell when the beacon was lit too late",
            "Fell at the prow of a dragon ship", "Fell in the last charge at a grey sea-cliff", "Fell in the press of a market-town brawl",
        };
        private static readonly string[] Cause =
        {
            "in a feud over a horse", "in a quarrel over an inheritance", "keeping an oath to a dead jarl", "after a bad omen nobody heeded",
            "for a debt of three silver rings", "beside a sworn brother", "chasing a stolen cow", "while the skalds watched", "over a slight at a feast",
            "for a king who never knew their name", "against a foe twice their size", "in a fog so thick friend struck friend",
        };
        private static readonly string[] Quirk =
        {
            "still counts {their} arrows twice", "won't sit with {their} back to a door", "hums the same verse while working", "sleeps with an axe under the pillow",
            "talks to ravens", "can't abide the smell of fish", "keeps every pretty stone {they} find{s}", "laughs at {their} own scars", "eats standing up",
            "carves runes into everything", "swears by Thor's goats", "never takes off {their} boots", "names every tool {they} own{s}",
            "spits for luck before a fight", "always wants the last word",
        };

        public static string PickName(Random rng, bool female) => female ? Female[rng.Next(Female.Length)] : Male[rng.Next(Male.Length)];

        public static string Backstory(Random rng, bool female, string battle = null)
        {
            string origin = battle ?? Origin[rng.Next(Origin.Length)];
            string q = Quirk[rng.Next(Quirk.Length)].Replace("{their}", female ? "her" : "his").Replace("{they}", female ? "she" : "he").Replace("{s}", "s");
            return $"{origin} {Cause[rng.Next(Cause.Length)]}; {q}.";
        }
        // Souls that fell in the same battle come together, and say so.
        public static string Battle(Random rng) => Origin[rng.Next(Origin.Length)];

        // Bynames: the first one earned is kept (a byname, once given, sticks).
        public static readonly (string id, string byname, string why)[] Bynames =
        {
            ("starkill", "-bane", "for their first starred kill"),
            ("unlucky", "the Unlucky", "for falling three times"),
            ("fisher", "Net-hand", "for the most fish caught"),
            ("feller", "Tree-feller", "for felling a hundred trees"),
            ("dock", "Who Fell Off the Dock", "for a swim nobody planned"),
            ("hauler", "Broad-back", "for carrying a thousand loads"),
            ("defender", "Shield-wall", "for holding three raids"),
            ("feaster", "the Belly", "for five feasts"),
            ("farmer", "Furrow-maker", "for a hundred harvests"),
            ("skald", "Sweet-tongue", "for a hundred songs in the hall"),
            ("unpaid", "Coin-wanting", "for three missed paydays"),
        };
        public static string Byname(string id, string creature = null)
        {
            var b = Bynames.FirstOrDefault(x => x.id == id);
            if (b.id == null) return null;
            return b.byname.StartsWith("-") ? (creature ?? "Troll") + b.byname : b.byname;
        }

        // A creature's prefab name as a word for a byname ("Troll-bane", "Greydwarf-bane").
        public static string Creature(string prefab)
        {
            if (string.IsNullOrEmpty(prefab)) return "Beast";
            string s = prefab.Split('_')[0];
            if (s.StartsWith("Greydwarf")) return "Greydwarf";
            if (s.StartsWith("Skeleton")) return "Bone";
            if (s.StartsWith("Draugr")) return "Draugr";
            if (s.StartsWith("Goblin")) return "Fuling";
            return s.Length > 1 ? char.ToUpper(s[0]) + s.Substring(1).ToLower() : s;
        }
    }
}
