namespace Jarldom
{
    // Fibre crops: what the weaver's linen comes from. Flax (the game's), and Homespun's nettle when it's in (the cloth plant of the
    // Meadows and the Black Forest: retted, broken and spun as flax is, for half the thread). Both are their own seed, both are sown
    // first while a weaver wants fibre and only where nothing else grows when not, and of both a little is kept back for sowing.
    // Without Homespun there's no nettle anywhere, and these only ever see flax.
    public static class Fibre
    {
        public const string Flax = "Flax", Nettle = "Homespun_Nettle";
        public const string RettedFlax = "Homespun_RettedFlax", LineFlax = "Homespun_LineFlax", RettedNettle = "Homespun_RettedNettle", LineNettle = "Homespun_LineNettle";
        public static bool Is(string seed) => seed == Flax || seed == Nettle;

        // Sown first while there's a weaver and the stores hold fewer than this of the two together (food first, else: thousands of flax
        // in the stores sowed every field with it, and the colony starved).
        public const int WantedBelow = 30;
        public static bool Wanted(bool weaver, int flax, int nettle) => weaver && flax + nettle < WantedBelow;

        // Of each, kept back for sowing where the colony has fields (spin the last of it and there's none to grow).
        public const int SeedKept = 10;
        public static int Kept(bool fields) => fields ? SeedKept : 0;

        // Linen thread to come, in the stores: the thread itself, and the line fibre waiting to be spun (a line flax spins two, a line nettle one).
        public static int ThreadToCome(int thread, int lineFlax, int lineNettle) => thread + 2 * lineFlax + lineNettle;

        // Of two fibre crops that both grow somewhere, the one sown first: flax, which gives twice the thread.
        public static int SowRank(string seed) => seed == Flax ? 0 : seed == Nettle ? 1 : 2;
    }
}
