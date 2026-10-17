using System;
using System.Linq;

namespace Jarldom
{
    public enum Season { Spring, Summer, Autumn, Winter } // (in the Seasons mod's order: its Fall is our Autumn)
    public enum Festival { Sigrblot, Midsummer, WinterNights, Yule }

    // The colony's year: four seasons, from shudnal's Seasons mod when it's installed (its season and day), else ten days each counted
    // from the world's first day. One festival a season, in the middle of it: Sigrblot in spring (the start of the old summer half,
    // a sacrifice for victory), Midsummer, Winter Nights in autumn (the start of the winter half: the dead remembered), Yule at midwinter.
    public struct Date
    {
        public int Year, Day; public Season Season; public int[] Lengths; // (Day: 1-based, in its season; Lengths: days in each season)
        public int WorldDay; // (the world's own day count, for the week)
        public int YearLength => Lengths.Sum();
        public int DayOfYear => Lengths.Take((int)Season).Sum() + Day;
        public int SeasonLength => Lengths[(int)Season];
        public override string ToString() => $"day {Day} of {Calendar.Name(Season)}, year {Year}";
    }

    public static class Calendar
    {
        public const int DefaultSeason = 10, PrepDays = 2;
        public static readonly Festival[] All = { Festival.Sigrblot, Festival.Midsummer, Festival.WinterNights, Festival.Yule };
        public static string Name(Season s) => s == Season.Autumn ? "autumn" : s.ToString().ToLower();
        public static string Name(Festival f) => f == Festival.WinterNights ? "Winter Nights" : f.ToString();
        public static Season SeasonOf(Festival f) => (Season)(int)f;

        // Without the Seasons mod: ten-day seasons from the world's first day (day 0 is the first of spring, year 1).
        public static Date FromWorldDay(int worldDay, int seasonLength = DefaultSeason)
        {
            int len = Math.Max(1, seasonLength), year = len * 4, d = Math.Max(0, worldDay);
            int inYear = d % year;
            return new Date { Year = d / year + 1, Season = (Season)(inYear / len), Day = inYear % len + 1, Lengths = new[] { len, len, len, len }, WorldDay = d };
        }
        // With it: its season and day (1-based) and each season's length; the year is counted from the world's day.
        public static Date FromSeasons(int season, int day, int[] lengths, int worldDay)
        {
            int[] l = lengths.Select(x => Math.Max(1, x)).ToArray();
            int s = Math.Max(0, Math.Min(3, season));
            return new Date { Year = Math.Max(0, worldDay) / l.Sum() + 1, Season = (Season)s, Day = Math.Max(1, Math.Min(l[s], day)), Lengths = l, WorldDay = Math.Max(0, worldDay) };
        }

        // The festival's day in its season: the middle of it.
        public static int DayOf(Festival f, int[] lengths) => (lengths[(int)SeasonOf(f)] + 1) / 2;
        public static bool IsDay(Date d, Festival f) => d.Season == SeasonOf(f) && d.Day == DayOf(f, d.Lengths);
        // The next festival from this day (today's, if today is one), and how many days until it.
        public static (Festival festival, int days) Next(Date d)
        {
            int year = d.YearLength, best = int.MaxValue; Festival which = Festival.Sigrblot;
            foreach (Festival f in All)
            {
                int at = d.Lengths.Take((int)SeasonOf(f)).Sum() + DayOf(f, d.Lengths);
                int until = ((at - d.DayOfYear) % year + year) % year;
                if (until < best) { best = until; which = f; }
            }
            return (which, best);
        }
        // The days a festival is readied for: the two before it.
        public static bool Preparing(Date d, out Festival f) { var (n, days) = Next(d); f = n; return days >= 1 && days <= PrepDays; }
        // The week: seven days from the world's first day, a Sunnudagr; the seventh, Laugardagr, is washing day (its name: "bath day").
        public static readonly string[] Weekdays = { "Sunnudagr", "Mánadagr", "Týsdagr", "Óðinsdagr", "Þórsdagr", "Frjádagr", "Laugardagr" };
        public const int WashingDay = 6;
        public static int Weekday(int worldDay) => (worldDay % 7 + 7) % 7;
        public static bool IsWashingDay(int worldDay) => Weekday(worldDay) == WashingDay;
        public static int DaysToWashing(int worldDay) => (WashingDay - Weekday(worldDay) + 7) % 7;
        public static string WeekdayName(int worldDay) => Weekdays[Weekday(worldDay)];
        public static string Line(Date d)
        {
            var (f, days) = Next(d);
            return (IsWashingDay(d.WorldDay) ? "Laugardagr, washing day. " : WeekdayName(d.WorldDay) + ". ") + $"{char.ToUpper(Name(d.Season)[0])}{Name(d.Season).Substring(1)}, day {d.Day} of {d.SeasonLength}, year {d.Year}. " +
                (days == 0 ? $"{Name(f)} is today." : days == 1 ? $"{Name(f)} is tomorrow." : $"{Name(f)} in {days} days.");
        }
    }
}
