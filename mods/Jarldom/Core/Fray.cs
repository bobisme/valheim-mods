using System;

namespace Jarldom
{
    // Clothes wear out, as in Dwarf Fortress: a tunic, dress, hat or Homespun garment frays with the hours worked in it (faster at heavy
    // work, in the rain, in the cold), is mended at the sewing table (the bench that made it, without Homespun) for linen thread while
    // it's worn thin, and after a few mendings wears through and is gone: the wearer is in rags again, and the weaver has work. Armour
    // and rags don't fray (fighting wears armour, as the game has it). A garment lasts about a season and a half of work in all.
    public static class Fray
    {
        public const int MaxMends = 2, MendThread = 2;
        public const float MendBelow = 0.35f, ThreadbareBelow = 0.25f;
        public const float WorkHoursPerDay = 10;
        public const string MendsKey = "jarldom_mends"; // (on the item: times mended)

        // One length of wear (new to worn through, unmended) lasts half a season of ordinary work.
        public static float LifeHours(int seasonDays) => Math.Max(1, seasonDays) * WorkHoursPerDay / 2f;
        // What an hour's work takes off a garment of this durability.
        public static float PerHour(float maxDurability, int seasonDays, float rate) => maxDurability / LifeHours(seasonDays) * rate;
        // How fast: heavy work (felling, mining, farming, hunting, building) half again; rain and the cold too; a hat half as fast.
        public static float Rate(bool heavy, bool wet, bool cold, bool hat) => (heavy ? 1.5f : 1f) * (wet ? 1.5f : 1f) * (cold ? 1.3f : 1f) * (hat ? 0.5f : 1f);
        public static bool Heavy(Prof p) => p == Prof.Woodcutter || p == Prof.Miner || p == Prof.Farmer || p == Prof.Hunter || p == Prof.Builder || p == Prof.Shipwright;
        // Worked hours counted in one tick: a skipped night or a set clock doesn't wear a week off.
        public static double Counted(double hours) => Math.Max(0, Math.Min(1, hours));

        public static bool Mendable(float fraction, int mends) => fraction < MendBelow && mends < MaxMends;
        public static bool Threadbare(float fraction) => fraction < ThreadbareBelow;
        public static int Mends(string stored) => int.TryParse(stored, out int n) ? Math.Max(0, n) : 0;
    }
}
