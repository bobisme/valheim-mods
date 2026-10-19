using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // Wool for the cold, in the world (the rules are Core/Chill.cs): what's warm to wear, whether it's cold where a villager stands, and
    // the "Cold" moodlet for one with nothing warm on. Without Homespun only frost-proof capes are warm.
    internal static class Cold
    {
        // Homespun's wool garments, and anything that keeps out frost (the wolf and lox capes, and on).
        public static bool Warm(ItemDrop.ItemData i) => i != null && (Chill.IsWool(Classes.Prefab(i)) || i.m_shared.m_damageModifiers != null
            && i.m_shared.m_damageModifiers.Any(m => m.m_type == HitData.DamageType.Frost && (m.m_modifier == HitData.DamageModifier.Resistant || m.m_modifier == HitData.DamageModifier.VeryResistant)));
        public static bool Wears(Brain b) => b.Body.GetInventory().GetAllItems().Any(i => i.m_equipped && Warm(i));
        public static bool Has(Brain b) => b.Body.GetInventory().GetAllItems().Any(Warm);

        public static bool Mountain(Vector3 at)
        {
            Heightmap.Biome biome = WorldGenerator.instance != null ? WorldGenerator.instance.GetBiome(at) : Heightmap.Biome.Meadows;
            return biome == Heightmap.Biome.Mountain || biome == Heightmap.Biome.DeepNorth;
        }
        // Wanting something warm by them: autumn and winter, or up in the cold now.
        public static bool Wants(Brain b) => Chill.Wanted(Year.Today().Season) || Mountain(b.Pos);

        // (from the needs tick, with the shelter it has just worked out: refreshed while cold, gone a few hours after)
        public static void Tick(Brain b, bool sheltered, bool byFire, double now)
        {
            if (b.Stripped) return; // (the sauna: warm enough)
            bool cold = Chill.Cold(Mountain(b.Pos), Year.Today().Season == Season.Winter, sheltered, byFire);
            if (Chill.Bites(cold, Wears(b))) Mood.Add(b.S, "chilled", now);
        }
    }
}
