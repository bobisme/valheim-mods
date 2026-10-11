using System;
using System.Collections.Generic;
using System.Linq;

namespace Jarldom
{
    // The game's clock in the colony's terms. World time is ZNet's seconds; a day is EnvMan's day length (30 real minutes by default).
    public static class Clock
    {
        public static double DayLength = 1800;
        public static double Hour => DayLength / 24.0;
        public static double Hours(double h) => h * Hour;
        public static int Day(double time) => (int)Math.Floor(time / DayLength);
        // The game's dawn is at 15% of the day and dusk at 85% (EnvMan stretches daylight): those are 6:00 and 18:00 here.
        public static double HourOfDay(double time)
        {
            double f = time / DayLength - Math.Floor(time / DayLength);
            if (f >= 0.15 && f < 0.85) return 6 + (f - 0.15) / 0.7 * 12;
            double night = f >= 0.85 ? f - 0.85 : f + 0.15; // 0 .. 0.3
            double h = 18 + night / 0.3 * 12;
            return h >= 24 ? h - 24 : h;
        }
        // World time of an hour on a day (the inverse of HourOfDay).
        public static double At(int day, double hour)
        {
            double f = hour >= 6 && hour < 18 ? 0.15 + (hour - 6) / 12 * 0.7 : 0.85 + ((hour < 6 ? hour + 24 : hour) - 18) / 12 * 0.3;
            return (day + f) * DayLength;
        }
        // The game's night is the first and last quarter of the day (EnvMan: before 6:00 and after 18:00).
        public static bool IsNight(double time) { double h = HourOfDay(time); return h < 6 || h >= 18; }
    }

    // Odin's attention: what a colony has built, how well it sleeps, and what it has done.
    // A colony's time away, made up at the rates counted while it was live (a little under: nobody's watching).
    public static class Away
    {
        public const double Efficiency = 0.9;
        public static double WorkShare => (Schedule.HallAt - Schedule.WorkAt) / 24.0; // the working part of a day
        // How many of an item: made while live in worked hours (tenths of an hour of work, one villager), for workers over hours away.
        public static int Produce(int madeLive, int workedTenths, int workers, double hoursAway)
        {
            if (workedTenths < 10 || madeLive <= 0 || workers <= 0 || hoursAway <= 0) return 0;
            double perHour = madeLive / (workedTenths / 10.0);
            return (int)Math.Floor(perHour * hoursAway * WorkShare * workers * Efficiency);
        }
    }

    public static class Renown
    {
        // The Fulings' price: more for a colony worth more, and half again each time it's been paid (rounded to fives).
        public static int TributeAsk(int renown, int paid) => (int)Math.Round((40 + Math.Max(0, renown) * 0.5) * Math.Pow(1.5, Math.Max(0, Math.Min(paid, 12))) / 5.0) * 5;
        public const double WealthScale = 0.5;
        public static int Wealth(double pieceValue, int coins, double valuables, bool treasureRoomDoubles = false, double treasureValue = 0)
        {
            double v = Math.Max(0, pieceValue) + 10.0 * Math.Max(0, coins) + Math.Max(0, valuables) + (treasureRoomDoubles ? Math.Max(0, treasureValue) : 0);
            return (int)Math.Round(Math.Sqrt(v) * WealthScale);
        }
        // Comfort: the best bed's comfort counts most, the average a little (so one fine bed doesn't carry a camp of bedrolls).
        public static int Comfort(IList<int> bedComforts)
        {
            if (bedComforts == null || bedComforts.Count == 0) return 0;
            return 3 * bedComforts.Max() + (int)Math.Round(bedComforts.Average());
        }
        public static readonly Dictionary<string, int> DeedPoints = new Dictionary<string, int>
        {
            ["boss"] = 50, ["raidheld"] = 10, ["feast"] = 5, ["ascend"] = 25, ["lost"] = -15, ["festival"] = 5, ["tribute"] = -5, ["exiled"] = -5,
        };
        public static int Deed(string kind) => DeedPoints.TryGetValue(kind, out int v) ? v : 0;

        // The next arrival needs this much renown: 20, then a quarter more for each soul that came.
        public static int Threshold(int arrivals) => (int)Math.Round(20 * Math.Pow(1.25, Math.Max(0, arrivals)));

        // Raids grow with renown: 1x at nothing, 2x at 400.
        public static float RaidScale(int renown) => 1f + Math.Min(1f, Math.Max(0, renown) / 400f);

        public static float Radius(Rank rank) => 60 + 10 * (int)rank;
        public const float ZoneReach = 150, MinApart = 300;
    }

    public static class Ranks
    {
        // The highest rank whose population and rooms (and every lower rank's) the colony has. Rooms: each working room's type and grade.
        public static Rank Evaluate(int population, IEnumerable<(RoomType type, char grade)> rooms)
        {
            var list = rooms.ToList();
            Rank best = Rank.Camp;
            foreach (RankInfo info in Tables.Ranks.Skip(1))
            {
                if (population < info.Population || !HasRooms(info, list)) break;
                best = info.Id;
            }
            return best;
        }
        public static bool HasRooms(RankInfo info, List<(RoomType type, char grade)> rooms)
        {
            foreach (var group in info.Rooms.GroupBy(r => r))
            {
                int need = group.Count();
                if (info.Id == Rank.Hold && group.Key == RoomType.Temple) need = 2; // a second temple
                if (info.Id == Rank.Hold && group.Key == RoomType.Watchtower) need = 2;
                int have = rooms.Count(r => r.type == group.Key && (info.GradedRoom != group.Key || Tables.GradeRank(r.grade) >= Tables.GradeRank(info.MinGrade)));
                if (have < need) return false;
            }
            return true;
        }
        public static string Missing(Rank next, int population, IEnumerable<(RoomType type, char grade)> rooms)
        {
            RankInfo info = Tables.Of(next);
            var list = rooms.ToList(); var parts = new List<string>();
            if (population < info.Population) parts.Add(info.Population - population == 1 ? "1 more soul" : $"{info.Population - population} more souls");
            foreach (var t in info.Rooms.Distinct())
                if (!HasRooms(new RankInfo { Id = info.Id, Rooms = info.Rooms.Where(r => r == t).ToArray(), GradedRoom = info.GradedRoom, MinGrade = info.MinGrade }, list))
                    parts.Add(info.GradedRoom == t ? $"a {Tables.Of(t).Name.ToLower()} of grade {info.MinGrade} or better" : $"a {Tables.Of(t).Name.ToLower()}");
            return parts.Count == 0 ? "" : string.Join(", ", parts);
        }
        public static int Cap(Rank r, int beds) => Math.Min(Tables.Of(r).Cap, Math.Max(0, beds));
    }

    // Needs, moodlets and the mood they add up to.
    public static class Mood
    {
        // Points per in-game hour each need falls.
        public static readonly float[] Decay = { 2.2f, 2.0f, 1.0f, 1.0f, 0.8f, 0.5f, 0.6f }; // (Social 1.0: an evening in the hall and company at work keep it up; at 1.5 it never got over half)
        // The most each need adds (full) or takes (empty) from mood.
        public static readonly float[] Weight = { 15, 15, 10, 10, 10, 5, 5 };

        public static float DecayRate(VillagerState v, Need n)
        {
            float rate = Decay[(int)n];
            if (n == Need.Food && v.Has("glutton")) rate *= 1.5f;
            if (n == Need.Faith && v.Has("pious")) rate *= 2f;
            return rate;
        }
        public static void Tick(VillagerState v, double hours)
        {
            for (int i = 0; i < v.Needs.Length; i++) v.Needs[i] = Clamp(v.Needs[i] - DecayRate(v, (Need)i) * (float)hours, 0, 100);
        }
        public static void Satisfy(VillagerState v, Need n, float amount) => v.Needs[(int)n] = Clamp(v.Needs[(int)n] + amount, 0, 100);

        public static float NeedPart(VillagerState v, Need n)
        {
            float w = Weight[(int)n];
            if (n == Need.Faith && v.Has("pious")) w *= 2;
            if (n == Need.Safety && v.Has("coward")) w *= 1.5f;
            return (v.Needs[(int)n] - 50) / 50f * w;
        }
        public static int MoodletPart(VillagerState v, double now)
        {
            int sum = 0;
            foreach (Moodlet m in v.Moodlets)
            {
                if (m.Until > 0 && m.Until <= now) continue;
                MoodletInfo info = Tables.Moodlet(m.Id);
                if (info == null) continue;
                int value = info.Value * Math.Max(1, m.Stacks);
                if (m.Id == "finemeal" && v.Has("glutton")) value *= 2;
                if (m.Id == "festival" && v.Has("pious")) value *= 2;
                sum += value;
            }
            return sum;
        }
        public static int Of(VillagerState v, double now)
        {
            float needs = 0;
            foreach (Need n in Enum.GetValues(typeof(Need))) needs += NeedPart(v, n);
            return (int)Math.Round(Clamp(needs + MoodletPart(v, now), -100, 100));
        }
        // -100 → 0.6, 0 → 1.0, +100 → 1.3.
        public static float Multiplier(int mood) => mood < 0 ? 1f + 0.4f * Math.Max(-100, mood) / 100f : 1f + 0.3f * Math.Min(100, mood) / 100f;

        public static void Add(VillagerState v, string id, double now, double? hoursOverride = null)
        {
            MoodletInfo info = Tables.Moodlet(id);
            if (info == null) return;
            double hours = hoursOverride ?? info.Hours;
            double until = hours <= 0 ? 0 : now + Clock.Hours(hours);
            Moodlet have = v.Moodlets.FirstOrDefault(m => m.Id == id);
            if (have == null) { v.Moodlets.Add(new Moodlet { Id = id, Until = until, Stacks = 1 }); return; }
            switch (info.Stack)
            {
                case Stack.Add: have.Stacks = Math.Min(info.MaxStacks, have.Stacks + 1); have.Until = until == 0 ? 0 : Math.Max(have.Until, until); break;
                case Stack.Max: have.Until = until == 0 ? 0 : Math.Max(have.Until, until); break;
                default: have.Until = until; have.Stacks = 1; break;
            }
        }
        public static void Remove(VillagerState v, string id) => v.Moodlets.RemoveAll(m => m.Id == id);
        public static bool Has(VillagerState v, string id, double now) => v.Moodlets.Any(m => m.Id == id && (m.Until == 0 || m.Until > now));
        public static void Expire(VillagerState v, double now) => v.Moodlets.RemoveAll(m => m.Until > 0 && m.Until <= now);

        // Why they feel as they do, strongest first: "+8 Slept in their own bed", "-12 Hungry".
        public static List<(int value, string text)> Reasons(VillagerState v, double now)
        {
            var list = new List<(int, string)>();
            string[] low = { "Hungry", "Tired", "Cold and wet", "Lonely", "Afraid", "Faithless", "Sick of ugly rooms" };
            string[] high = { "Well fed", "Well rested", "Snug and dry", "Good company", "Safe", "Faithful", "Lives somewhere fine" };
            foreach (Need n in Enum.GetValues(typeof(Need)))
            {
                int part = (int)Math.Round(NeedPart(v, n));
                if (Math.Abs(part) >= 2) list.Add((part, part < 0 ? low[(int)n] : high[(int)n]));
            }
            foreach (Moodlet m in v.Moodlets)
            {
                if (m.Until > 0 && m.Until <= now) continue;
                MoodletInfo info = Tables.Moodlet(m.Id);
                if (info == null) continue;
                list.Add((info.Value * Math.Max(1, m.Stacks), m.Stacks > 1 ? $"{info.Text} (x{m.Stacks})" : info.Text));
            }
            return list.OrderByDescending(x => Math.Abs(x.Item1)).ToList();
        }
        // How hungry: food need falls from full to empty; they eat when under this.
        public const float EatBelow = 55;
        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
    }

    // What a villager should be doing at an hour of the day.
    public static class Schedule
    {
        public const double WakeAt = 6, WorkAt = 7, HallAt = 18, HomeAt = 21, SleepAt = 22;
        public static Activity At(double hour, IList<string> traits, bool raid = false, bool heavyRain = false, bool outdoorTask = false, bool urgent = false)
        {
            if (raid) return Activity.Defend;
            double shift = 0;
            if (traits != null && traits.Contains("early")) shift -= 1;
            if (traits != null && traits.Contains("nightowl")) shift += 1;
            double h = hour - shift;
            if (h < 0) h += 24; if (h >= 24) h -= 24;
            if (h >= SleepAt || h < WakeAt) return Activity.Sleep;
            if (h < WorkAt) return Activity.Wake;
            if (h < HallAt) return heavyRain && outdoorTask && !urgent ? Activity.Shelter : Activity.Work;
            if (h < HomeAt) return Activity.Hall;
            return Activity.Home;
        }
    }

    // Wages and the treasury. Pay in full or not at all.
    public static class Payroll
    {
        public const int EveryDays = 5;
        public const float HoardShare = 0.3f;
        public static int Due(IEnumerable<VillagerState> souls, int days = EveryDays) => souls.Sum(v => Tables.Wage(v.Prof, v.Has("greedy")) * days);
        public static bool CanPay(int treasury, int due) => due <= 0 || treasury >= due;
        public static int Hoarded(int wage) => (int)Math.Floor(wage * HoardShare);
        public static bool IsPayday(int day, int lastPayday) => day > 0 && day % EveryDays == 0 && day != lastPayday;
    }

    // The gods' favour, offerings and the bog.
    public static class Favour
    {
        public const int Min = -100, Max = 100, DecayPerDay = 2;
        public static int Clamp(int f) => f < Min ? Min : f > Max ? Max : f;
        public static int Decay(int f, int days) => f > 0 ? Math.Max(0, f - DecayPerDay * days) : f;
        // Coins for the next point of favour in the sacred bog: 50, then a tenth more each time, with no end.
        public static int BogPrice(int paidPoints) => (int)Math.Round(50 * Math.Pow(1.1, Math.Max(0, paidPoints)));
        // An offering's worth in favour: one point per 10 of its value, at least one for anything the god wants.
        public static int Offering(double value, bool wanted) => wanted ? Math.Max(1, (int)Math.Round(value / 10)) : 0;
        // Odin's favour brings more and better migrants: the dawn chance and the renown needed both move.
        public static float ArrivalChance(int odin, bool feastYesterday) => Math.Max(0.1f, Math.Min(0.95f, 0.6f + odin / 400f + (feastYesterday ? 0.25f : 0)));
    }

    // A villager's worth on the road to Valhalla.
    public static class Worth
    {
        public const float Full = 100;
        public static readonly Dictionary<string, float> Points = new Dictionary<string, float>
        {
            ["kill"] = 1, ["starkill"] = 5, ["bosskill"] = 15, ["orders100"] = 1, ["feast"] = 1, ["ring"] = 3, ["festival"] = 2, ["disgrace"] = -20,
        };
        public static float Gain(VillagerState v, string source, float odinBonus = 0)
        {
            if (!Points.TryGetValue(source, out float p)) return 0;
            if (p > 0 && (source == "kill" || source == "starkill") && v.Has("brave")) p *= 1.5f;
            if (p > 0) p *= 1 + odinBonus;
            float before = v.Worth;
            v.Worth = Math.Max(0, Math.Min(Full, v.Worth + p));
            if (v.Worth >= Full) v.Worthy = true;
            return v.Worth - before;
        }
        // Ascension: deeds for the soul plus what was burned with them.
        public static int AscensionDeeds(int graveGoodsValue, bool sworn) => (Renown.Deed("ascend") + graveGoodsValue / 50) * (sworn ? 2 : 1);
    }

    // Polygons on the ground (x, z), for rooms and zones.
    public static class Geometry
    {
        public static float Area(IList<float> xz)
        {
            int n = xz.Count / 2; double a = 0;
            for (int i = 0; i < n; i++) { int j = (i + 1) % n; a += xz[i * 2] * xz[j * 2 + 1] - xz[j * 2] * xz[i * 2 + 1]; }
            return (float)Math.Abs(a / 2);
        }
        public static bool Inside(IList<float> xz, float x, float z)
        {
            int n = xz.Count / 2; bool inside = false;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                float xi = xz[i * 2], zi = xz[i * 2 + 1], xj = xz[j * 2], zj = xz[j * 2 + 1];
                if ((zi > z) != (zj > z) && x < (xj - xi) * (z - zi) / (zj - zi + 1e-9f) + xi) inside = !inside;
            }
            return inside;
        }
        // Distance from a point to the polygon's edge (0 inside).
        public static float Distance(IList<float> xz, float x, float z)
        {
            if (Inside(xz, x, z)) return 0;
            int n = xz.Count / 2; float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                best = Math.Min(best, Segment(x, z, xz[i * 2], xz[i * 2 + 1], xz[j * 2], xz[j * 2 + 1]));
            }
            return best;
        }
        public static float Segment(float px, float pz, float ax, float az, float bx, float bz)
        {
            float dx = bx - ax, dz = bz - az; float len = dx * dx + dz * dz;
            float t = len < 1e-6f ? 0 : Math.Max(0, Math.Min(1, ((px - ax) * dx + (pz - az) * dz) / len));
            float cx = ax + t * dx - px, cz = az + t * dz - pz;
            return (float)Math.Sqrt(cx * cx + cz * cz);
        }
        // Points along each edge every step metres (for the wall check), with the edge index.
        public static IEnumerable<(int edge, float x, float z)> EdgeSamples(IList<float> xz, float step)
        {
            int n = xz.Count / 2;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                float ax = xz[i * 2], az = xz[i * 2 + 1], bx = xz[j * 2], bz = xz[j * 2 + 1];
                float len = (float)Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
                int count = Math.Max(1, (int)Math.Floor(len / step));
                for (int k = 0; k < count; k++) { float t = (k + 0.5f) / count; yield return (i, ax + (bx - ax) * t, az + (bz - az) * t); }
            }
        }
        // A grid of points inside the polygon (for the roof check).
        public static IEnumerable<(float x, float z)> Grid(IList<float> xz, float step)
        {
            int n = xz.Count / 2; if (n < 3) yield break;
            float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
            for (int i = 0; i < n; i++) { minX = Math.Min(minX, xz[i * 2]); maxX = Math.Max(maxX, xz[i * 2]); minZ = Math.Min(minZ, xz[i * 2 + 1]); maxZ = Math.Max(maxZ, xz[i * 2 + 1]); }
            for (float x = minX + step / 2; x < maxX; x += step)
                for (float z = minZ + step / 2; z < maxZ; z += step)
                    if (Inside(xz, x, z)) yield return (x, z);
        }
        public static (float x, float z) Centre(IList<float> xz)
        {
            int n = xz.Count / 2; float x = 0, z = 0;
            for (int i = 0; i < n; i++) { x += xz[i * 2]; z += xz[i * 2 + 1]; }
            return n == 0 ? (0, 0) : (x / n, z / n);
        }
        // A simple polygon: no edge crosses another (rooms drawn corner by corner).
        public static bool Simple(IList<float> xz)
        {
            int n = xz.Count / 2; if (n < 3) return false;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                {
                    if (Math.Abs(i - j) <= 1 || i == 0 && j == n - 1) continue;
                    if (Cross(xz, i, (i + 1) % n, j, (j + 1) % n)) return false;
                }
            return true;
        }
        private static bool Cross(IList<float> p, int a, int b, int c, int d)
        {
            float o1 = Orient(p, a, b, c), o2 = Orient(p, a, b, d), o3 = Orient(p, c, d, a), o4 = Orient(p, c, d, b);
            return o1 * o2 < 0 && o3 * o4 < 0;
        }
        private static float Orient(IList<float> p, int a, int b, int c) =>
            (p[b * 2] - p[a * 2]) * (p[c * 2 + 1] - p[a * 2 + 1]) - (p[b * 2 + 1] - p[a * 2 + 1]) * (p[c * 2] - p[a * 2]);
    }
}
