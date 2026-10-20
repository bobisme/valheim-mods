using System.Collections.Generic;
using System.Linq;

namespace Homespun
{
    // What Homespun's clothes do worn (H3), as data: light on armour, they're for the weather and for looking well. The hooded cloak keeps
    // out the cold as a wolf fur cape does (the mountains' freezing too) and its hood keeps off the rain; nalbound mittens and socks keep
    // off the chill of a night or a cold wind, but not the mountains' freezing; and dressed in dyed clothes, two pieces or more, you
    // rest the better for it (comfort, as a fine room gives). Every colour of a garment does what the undyed one does.
    public static class Wear
    {
        public const int FinePieces = 2, FineComfort = 1;

        public static Garment Of(string prefab) => Chain.GarmentOf(Dye.Undyed(prefab));
        public static bool Warm(string prefab) => Of(prefab)?.Frost == true;
        public static bool Hooded(string prefab) => Of(prefab)?.Hood == true;
        public static bool Mittens(string prefab) => prefab == Names.Nalbinding;
        // A garment of ours, dyed.
        public static bool Fine(string prefab) => Of(prefab) != null && Dye.Read(prefab)?.dye != null;
        // What the clothes worn add to comfort.
        public static int Comfort(IEnumerable<string> worn) => worn.Count(Fine) >= FinePieces ? FineComfort : 0;

        // A sail of ours, any colour.
        public static bool Sail(string prefab) => Dye.Undyed(prefab) == Names.Sail;
    }
}
