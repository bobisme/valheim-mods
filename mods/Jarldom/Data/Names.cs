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
            "Aki", "Alf", "Anund", "Arnkel", "Arnfinn", "Asbjorn", "Asmund", "Audun", "Aslak", "Bjarki", "Bjolf", "Bersi", "Bjorn",
            "Bragi", "Brynjolf", "Bruni", "Bui", "Bard", "Dag", "Dagfinn", "Dalli", "Eilif", "Einar", "Eindridi", "Eldgrim", "Eskil",
            "Eyjolf", "Fridleif", "Frodi", "Gaut", "Gamli", "Geirmund", "Geirod", "Gilli", "Glum", "Gudlaug", "Gudmund", "Gunnbjorn",
            "Gunnstein", "Guthorm", "Haki", "Hafthor", "Hallkel", "Hallmund", "Hallvard", "Halli", "Hamund", "Hardbein", "Hastein",
            "Hedin", "Hermund", "Hogni", "Hoskuld", "Hrafnkel", "Hrani", "Hrapp", "Ingi", "Ingjald", "Ingimund", "Illugi", "Jon", "Jokull",
            "Karl", "Ketilbjorn", "Kjartan", "Kolgrim", "Kormak", "Lambi", "Lodin", "Lodmund", "Modolf", "Oddi", "Oleif", "Onund", "Orlyg",
            "Orm", "Osvif", "Ospak", "Rafn", "Reginn", "Rolf", "Runolf", "Sighvat", "Sigmund", "Sigtrygg", "Sigvaldi", "Skeggi", "Skuli",
            "Solmund", "Starkad", "Stefni", "Stein", "Steinar", "Styr", "Sverri", "Svan", "Thord", "Thorald", "Thorgils", "Thormod",
            "Thorvald", "Torfi", "Trausti", "Tryggvi", "Tumi", "Vakr", "Vali", "Valbrand", "Vestein",
        };
        public static readonly string[] Female =
        {
            "Alfhild", "Asa", "Asdis", "Astrid", "Aud", "Bergthora", "Borghild", "Brynhild", "Dagny", "Eydis", "Freydis", "Frida", "Geirlaug",
            "Gudrun", "Gunnhild", "Gyda", "Hallgerd", "Helga", "Herdis", "Hervor", "Hildigunn", "Hlif", "Ingibjorg", "Ingrid", "Jorunn", "Katla",
            "Ragna", "Ragnhild", "Rannveig", "Signy", "Sigrid", "Solveig", "Steinunn", "Svanhild", "Thora", "Thordis", "Thorgerd", "Thorunn",
            "Thurid", "Tofa", "Ulfhild", "Unn", "Valdis", "Vigdis", "Yrsa",
            "Alfdis", "Arndis", "Asgerd", "Audhild", "Audur", "Bera", "Bergdis", "Bergljot", "Bjorg", "Dalla", "Eldgunn", "Ellisif",
            "Erna", "Estrid", "Fridgerd", "Gerdur", "Gjaflaug", "Grima", "Groa", "Gudbjorg", "Gudfrid", "Gudny", "Gudrid", "Gudveig",
            "Gullaug", "Gunnlaug", "Gunnvor", "Hafdis", "Halla", "Halldis", "Hallveig", "Helgunn", "Hildur", "Hjordis", "Holmfrid",
            "Hrefna", "Hrodny", "Ingirid", "Ingunn", "Ingveig", "Kolfinna", "Kormlod", "Lif", "Nanna", "Oda", "Oddny", "Ormhild",
            "Ragnfrid", "Sesselja", "Sigrun", "Sigurlaug", "Skadi", "Skogul", "Snaefrid", "Snjolaug", "Sunna", "Svala", "Thjodhild",
            "Thorbjorg", "Thorey", "Thorfrid", "Thorgunna", "Thorhild", "Ulfrun", "Unnur", "Valgerd", "Vilborg", "Yngvild", "Asny",
            "Bothild", "Aldis", "Ingrun", "Kadla", "Torunn", "Ketilrid",
        };

        private static readonly string[] Origin =
        {
            "Fell at a river ford", "Fell on a burning longship", "Fell holding a shield-wall", "Fell in a raid on a monastery", "Fell defending a hall",
            "Fell in a duel on a holmgang isle", "Fell in the snow of a mountain pass", "Fell storming a hill-fort", "Fell when the beacon was lit too late",
            "Fell at the prow of a dragon ship", "Fell in the last charge at a grey sea-cliff", "Fell in the press of a market-town brawl",
            "Fell in a shipwreck off the skerries", "Fell on the ice of a frozen fjord", "Fell crossing a swollen glacier river",
            "Fell when the karve broke on a skerry", "Fell in a sword-dance gone wrong", "Fell at a trading post on the Frankish coast",
            "Fell in a berserk fit at the midwinter feast", "Fell in a fire that took the longhouse", "Fell hunting a bear in the birch woods",
            "Fell in a storm off the western isles", "Fell in the marsh with a stolen sword", "Fell at the Thing, arguing over a boundary stone",
            "Fell tending the sheep in a blizzard", "Fell in a rockslide above the summer pasture", "Fell beneath a pine they were felling",
            "Fell at the ford of a flooded river, the bridge gone", "Fell in an ambush by the lake shore", "Fell in a thaw when the ice gave way",
            "Fell holding the gate of a burning farm", "Fell rowing home from the market", "Fell on the wall at a siege in the east",
            "Fell carrying the wedding ale up to the hall", "Fell in a sea-fight off the Orkney isles", "Fell in a field at harvest, scythe in hand",
            "Fell at a battle on the Hallow Mere", "Fell in a feud-raid on a neighbour's farm", "Fell at the Dublin walls, sword in hand",
            "Fell hauling a grounded longship up the beach", "Fell keeping watch on the hill above the hall", "Fell in a rockfall on the sea road",
            "Fell in a night raid on a sleeping town", "Fell at a Frankish abbey's gate", "Fell at the foot of a burial mound",
        };
        private static readonly string[] Cause =
        {
            "in a feud over a horse", "in a quarrel over an inheritance", "keeping an oath to a dead jarl", "after a bad omen nobody heeded",
            "for a debt of three silver rings", "beside a sworn brother", "chasing a stolen cow", "while the skalds watched", "over a slight at a feast",
            "for a king who never knew their name", "against a foe twice their size", "in a fog so thick friend struck friend",
            "over a wager on a goat race", "for stealing a neighbour's sheep", "for a loan of silver never paid back",
            "over a bad bet at a cockfight", "over a bride-price nobody agreed on", "after the ale ran out at a wedding",
            "while the thaw broke the fjord ice", "in a dispute over a boundary stone", "for a dead man's cloak",
            "after a fight over the last of the mead", "because a brother called them slow", "for a dare nobody should have taken",
            "while fetching a runaway pig", "over who had stolen the good whetstone", "for a stranger's horn of mead",
            "because a dream told them to", "in a quarrel over fishing rights", "over a sheep that wandered onto the wrong farm",
            "for a neighbour's daughter's good name", "chasing a hound that stole the bread", "with a borrowed seax in the wrong hand",
            "for the right to a spring", "after one drink too many at Yule", "over an oath nobody could remember",
            "because the Thing gave them no right", "trying to outrun a late winter", "for a bet that the boat would float",
            "over a boat that was never theirs", "over a beached whale on the sand",
        };
        private static readonly string[] Quirk =
        {
            "still counts {their} arrows twice", "won't sit with {their} back to a door", "hums the same verse while working", "sleeps with an axe under the pillow",
            "talks to ravens", "can't abide the smell of fish", "keeps every pretty stone {they} find{s}", "laughs at {their} own scars", "eats standing up",
            "carves runes into everything", "swears by Thor's goats", "never takes off {their} boots", "names every tool {they} own{s}",
            "spits for luck before a fight", "always wants the last word",
            "lays out every knife in a row before eating", "won't cross a threshold without spitting first",
            "tells the same bad joke at every feast", "keeps a dead gull in a chest for luck",
            "always sits nearest the fire, even in summer", "counts the rafters every night before bed",
            "won't eat bread baked by someone else", "pats every horse {they} meet{s}", "sings to the cows at milking, badly",
            "sharpens {their} knife at every meal", "blesses the ale before anyone else may drink", "names the wind and speaks to it",
            "keeps a whetstone up {their} sleeve", "won't wash in the lake, only in the sea", "twists {their} braid when {they} lie{s}",
            "mutters the names of dead kin on the walk home", "hides a spare hook in every coat", "always carries a pinch of salt",
            "thanks the sea before every catch", "can't abide a bird that sings in the hall", "carries a dead man's comb for luck",
            "knocks three times on the wall before a fight", "forgets every name but the ones that rhyme",
            "laughs at funerals and cries at weddings", "walks in circles while thinking", "names every cat, and every cat ignores it",
            "tips the first cup of every brew to the wind", "tells the children tales of ravens", "hates the sound of geese",
            "thinks every new ship is cursed", "has never once been on time for the Thing",
        };

        // A father's name to tell two of one name apart, the Norse way: Ottar Ketilsson, Tofa Thorsdottir.
        public static string Patronymic(Random rng, bool female)
        {
            string f = Male[rng.Next(Male.Length)];
            string stem = f.EndsWith("r") && f.Length > 3 ? f.Substring(0, f.Length - 1) : f;
            return stem + (female ? "sdottir" : "sson");
        }
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
