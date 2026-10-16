using System.Collections.Generic;

namespace Jarldom
{
    // A god's relic: a thing from the god's own kind of place, laid on its altar. A temple without its god's relic on the altar grades
    // no higher than B (Bob's call, 2026-10-16); Odin's shrine isn't a temple and has no cap. Four are the game's own rare things, four
    // are Jarldom's (their prefab names are for ever): Thor's storm stone, found on a high peak in a storm; Freyja's golden wildflower,
    // now and then among the meadow flowers; Frigg's golden spindle, in the Black Forest's burial chambers; Odin's raven feather, in
    // the Mistlands' ruins.
    public static class Relics
    {
        public const string StormStone = "JarldomRelicStormStone", GoldenFlower = "JarldomRelicGoldenFlower", Spindle = "JarldomRelicSpindle", RavenFeather = "JarldomRelicRavenFeather";
        public static readonly Dictionary<string, (string prefab, string name, string where)> Of = new Dictionary<string, (string, string, string)>
        {
            ["odin"] = (RavenFeather, "a raven feather", "in a chest in the Mistlands' ruins"),
            ["thor"] = (StormStone, "a storm stone", "high on a mountain peak in a storm, where the lightning strikes"),
            ["freyja"] = (GoldenFlower, "a golden wildflower", "now and then among the meadow flowers you pick"),
            ["frigg"] = (Spindle, "a golden spindle", "in a chest in a Black Forest burial chamber"),
            ["njord"] = ("SerpentScale", "a serpent scale", "from a sea serpent"),
            ["tyr"] = ("TrophyGoblinBrute", "a Fuling berserker's trophy", "from a Fuling berserker on the plains"),
            ["ullr"] = ("TrophyFenring", "a Fenring's trophy", "from a Fenring, on the mountains at night"),
            ["hel"] = ("Wishbone", "the wishbone", "from Bonemass, in the swamp"),
        };
        public static readonly string[] Ours = { StormStone, GoldenFlower, Spindle, RavenFeather };
        public static string GodOf(string prefab) { foreach (var kv in Of) if (kv.Value.prefab == prefab) return kv.Key; return null; }
        // The grade a temple may have: anything up to B without its relic; with it, what it earns.
        public static char Cap(char grade, bool hasRelic) => hasRelic || grade == 'B' || grade == 'C' || grade == 'D' ? grade : 'B';
    }
}
