using System;

namespace Jarldom
{
    // Vadmal as money (with Homespun): the north paid in cloth as in silver, and a bolt of homespun wool was a measure of worth. The Thing's
    // bót can be paid in wool bolts at a fixed rate (the thief's own first, then the stores' bought out of their hoard); and the trader
    // sells bolts on the runs: wool and linen, dyed ones dearer. A few of each are left in the stores for the weaver's sewing.
    public static class Vadmal
    {
        public const string WoolBolt = "Homespun_WoolBolt", LinenBolt = "Homespun_LinenBolt";
        public const int BoltCoins = 10;                 // (a bolt of undyed wool, in coins: the bót's rate, and its price on foot)
        public const int WoolTenths = BoltCoins * 10, LinenTenths = 80, DyedPercent = 150; // (linen a little less; dyed half again)
        public const int KeptForSewing = 6;              // (of each kind of bolt, left in the stores for the weaver)

        // A bolt's price for the trader, in tenths of a coin (null: not a bolt): wool or linen, dyed any colour ("_Blue") or not.
        public static int? Tenths(string prefab)
        {
            if (prefab == null) return null;
            foreach (var (bolt, tenths) in new[] { (WoolBolt, WoolTenths), (LinenBolt, LinenTenths) })
            {
                if (prefab == bolt) return tenths;
                if (prefab.StartsWith(bolt + "_") && prefab.Length > bolt.Length + 1 && prefab.IndexOf('_', bolt.Length + 1) < 0) return tenths * DyedPercent / 100;
            }
            return null;
        }
        public static bool IsBolt(string prefab) => Tenths(prefab) != null;
        // How many of a kind of bolt the trader takes from the stores.
        public static int ForSale(int held) => Math.Max(0, held - KeptForSewing);

        // The bót paid in wool: of what's owed, how many bolts (whole ones, never more than it comes to) and what's left owing in coins.
        public static (int bolts, int left) Pay(int owed, int bolts)
        {
            if (owed <= 0 || bolts <= 0) return (0, Math.Max(0, owed));
            int n = Math.Min(bolts, owed / BoltCoins);
            return (n, owed - n * BoltCoins);
        }
        // Of the stores' bolts, how many a thief's hoard can buy for the bót.
        public static int Buys(int hoard, int storeBolts) => Math.Max(0, Math.Min(storeBolts, hoard / BoltCoins));
    }
}
