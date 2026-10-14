using System;
using System.Collections.Generic;
using System.Linq;

namespace Jarldom
{
    // A jarl's fights in Quad's Arena: the colony's folk wager their hoards on them (a tenth at most; for them, or against a jarl they
    // think little of), even money. Those watching from the stands wager there; the rest when word comes home.
    public static class Wagers
    {
        public const int MaxBettors = 6, MinHoard = 10, Against = -15; // (an opinion below Against bets against the jarl)
        public const string Prefix = "arena:"; // (the deed carried home: "arena:<kind>:<outcome>:<crowned 0/1>:<coins>:<vids settled in the stands, comma-separated>")

        // What one stakes: up to a tenth of their hoard, for the jarl (positive) or against (negative); 0 with too little to bet.
        public static int Stake(int hoard, int opinion, Random rng)
        {
            if (hoard < MinHoard) return 0;
            int most = Math.Max(1, hoard / 10);
            int n = Math.Max(1, (int)Math.Round(most * (0.4 + 0.6 * rng.NextDouble())));
            return opinion < Against ? -n : n;
        }
        // What the wager pays (the change to their hoard): even money.
        public static int Settle(int stake, bool won) => stake == 0 ? 0 : (stake > 0) == won ? Math.Abs(stake) : -Math.Abs(stake);

        public static string Kind(string kind) => kind == "road" ? "the Long Road" : kind == "champion" ? "a Champion Bout" : kind == "endless" ? "the Endless Horde" : kind == "trial" ? "the day's trial" : "a contest";
        // How a fight that wasn't won ended, for the saga.
        public static string Ended(string outcome) => outcome == "cashout" ? "took the purse and walked out" : outcome == "yield" ? "yielded" : outcome == "died" ? "fell in the ring"
            : outcome == "timeup" ? "ran out of time" : outcome == "leftring" ? "left the ring" : outcome == "carried" ? "was carried out" : "saw the fight called off";

        public static string Pack(string kind, string outcome, bool crowned, int coins, IEnumerable<string> settled) =>
            Prefix + string.Join(":", Clean(kind), Clean(outcome), crowned ? "1" : "0", coins.ToString(), string.Join(",", settled ?? new string[0]));
        public static bool Unpack(string deed, out string kind, out string outcome, out bool crowned, out int coins, out HashSet<string> settled)
        {
            kind = outcome = ""; crowned = false; coins = 0; settled = new HashSet<string>();
            if (deed == null || !deed.StartsWith(Prefix)) return false;
            string[] f = deed.Substring(Prefix.Length).Split(':');
            if (f.Length < 4 || !int.TryParse(f[3], out coins)) return false;
            kind = f[0]; outcome = f[1]; crowned = f[2] == "1";
            if (f.Length > 4) foreach (string v in f[4].Split(',').Where(v => v != "")) settled.Add(v);
            return true;
        }
        private static string Clean(string s) => new string((s ?? "").Where(ch => ch != ':' && ch != ';' && ch != ',').ToArray());

        // Those who wager when word comes home: the richest few who haven't wagered in the stands already.
        public static List<(string vid, int stake)> Bettors(IEnumerable<(string vid, int hoard, int opinion)> folk, ICollection<string> settled, Random rng) =>
            folk.Where(f => !settled.Contains(f.vid) && f.hoard >= MinHoard).OrderByDescending(f => f.hoard).ThenBy(f => f.vid, StringComparer.Ordinal).Take(MaxBettors)
                .Select(f => (f.vid, stake: Stake(f.hoard, f.opinion, rng))).Where(x => x.stake != 0).ToList();
    }
}
