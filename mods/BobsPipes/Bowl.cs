using System;
using System.Globalization;

namespace BobsPipes
{
    internal readonly struct Bowl
    {
        internal readonly int Blend;
        internal readonly double Remaining, Smoked;
        internal bool Empty => Remaining <= 0;
        internal Bowl(int blend, double remaining, double smoked = 0)
        { Blend = blend; Remaining = remaining; Smoked = smoked; }
        internal static Bowl Pack(int blend, double seconds, double smoked = 0)
        {
            if (blend < 0 || blend > 2 || !Finite(seconds) || seconds < 30 || seconds > 3600 || !Finite(smoked) || smoked < 0)
                throw new ArgumentException("Invalid pipe bowl.");
            return new Bowl(blend, seconds, Math.Min(smoked, 10000000));
        }
        internal Bowl Burn(double seconds)
        {
            if (!Finite(seconds) || seconds < 0) throw new ArgumentException("Invalid burn time.");
            double spent = Math.Min(Remaining, seconds);
            return new Bowl(Blend, Math.Max(0, Remaining-spent), Math.Min(10000000, Smoked+spent));
        }
        internal string Save() => "1|" + Blend + "|" + Remaining.ToString("R", CultureInfo.InvariantCulture) + "|" + Smoked.ToString("R", CultureInfo.InvariantCulture);
        internal static Bowl Read(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length > 128) return default;
            string[] parts = text.Split('|');
            if (parts.Length != 4 || parts[0] != "1" ||
                !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int blend) || blend < 0 || blend > 2 ||
                !double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double remaining) || !Finite(remaining) || remaining < 0 || remaining > 3600 ||
                !double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out double smoked) || !Finite(smoked) || smoked < 0 || smoked > 10000000) return default;
            return new Bowl(blend, remaining, smoked);
        }
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
