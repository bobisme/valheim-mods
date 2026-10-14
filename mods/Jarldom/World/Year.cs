using UnityEngine;

namespace Jarldom
{
    // Today in the colony's year: the Seasons mod's date when it's installed, else ten-day seasons from the world's first day.
    internal static class Year
    {
        public static int WorldDay => ZNet.instance != null ? Clock.Day(ZNet.instance.GetTimeSeconds()) : 0;
        public static bool BySeasons => Compat.Seasons(out _, out _, out _);
        public static Date Today()
        {
            int day = WorldDay;
            return Compat.Seasons(out int season, out int inSeason, out int[] lengths) && lengths != null && lengths.Length == 4
                ? Calendar.FromSeasons(season, inSeason, lengths, day)
                : Calendar.FromWorldDay(day);
        }
    }
}
