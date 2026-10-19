namespace Jarldom
{
    // Wool for the cold. Winter out of doors is cold; the Mountains and the far north are cold whatever the season, a roof or not; a
    // fire's warmth is never cold. Something warm worn (Homespun's wool tunic, dress, trousers or hooded cloak, or a cape that keeps
    // out frost: wolf, lox) and the cold doesn't bite. In autumn and winter villagers go and get something warm from the stores, and
    // the weaver sews hooded cloaks for those who have none.
    public static class Chill
    {
        public const string Tunic = "Homespun_WoolTunic", Dress = "Homespun_WoolDress", Trousers = "Homespun_WoolTrousers", Cloak = "Homespun_HoodedCloak";
        public static bool IsWool(string prefab) => prefab == Tunic || prefab == Dress || prefab == Trousers || prefab == Cloak;

        // Is it cold here: winter outdoors, or always up in the mountains or the north; never by a fire.
        public static bool Cold(bool mountain, bool winter, bool sheltered, bool byFire) => !byFire && (mountain || winter && !sheltered);
        // The seasons for having something warm by you (autumn: ahead of the winter).
        public static bool Wanted(Season s) => s == Season.Autumn || s == Season.Winter;
        // The "Cold" moodlet: cold and nothing warm on.
        public static bool Bites(bool cold, bool wearsWarm) => cold && !wearsWarm;
        // How many cloaks the weaver should sew: one for each of the colony short of anything warm, less what's spare in the stores.
        public static int ToSew(Season s, int withoutWarm, int spareInStores) => Wanted(s) ? System.Math.Max(0, withoutWarm - spareInStores) : 0;
    }
}
