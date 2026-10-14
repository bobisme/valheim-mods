using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // A jarl's fights in Quad's Arena (Core/Wagers.cs): those following them watch from the stands, cheer and wager there; how it ended
    // is carried home on the jarl's character, and when they come home the rest of the colony settles its wagers, the saga tells it,
    // and three contests won (or a place in the Hall of Champions) make them "the Champion".
    internal static class ArenaWatch
    {
        public const string StakeKey = "jarldom_arenastake"; // (on a watcher's body: what they wagered in the stands, for or against)
        public const float Near = 90f; // (from the ring's middle: close enough to be watching)
        private static int _seen = -1; private static float _next;

        // The leader's fight is on in the ring (where it is): watchers go to the stands.
        public static string TestFighter = ""; public static float TestUntil; // (test: a fight as if under way, "arena fight")
        public static bool On(Player leader, out Vector3 ring)
        {
            ring = Vector3.zero;
            if (leader != null && Time.time < TestUntil && TestFighter == leader.GetPlayerName()) return Compat.ArenaCentre(out ring);
            return leader != null && Compat.ArenaContest(out string fighter, out _) && fighter == leader.GetPlayerName() && Compat.ArenaCentre(out ring);
        }
        // In the ring itself (a follower in the stands leaves the fight to the jarl).
        public static bool InRing(Brain b, Vector3 p)
        {
            Player l = Follow.Leader(b);
            return On(l, out Vector3 ring) && Utils.DistanceXZ(p, ring) < 17f;
        }

        // ---- a watcher, on the game that runs them ----
        public static void Wager(Brain b, string jarl)
        {
            ZDO z = b.View.GetZDO();
            if (z.GetInt(StakeKey, 0) != 0) return;
            int stake = Wagers.Stake(b.S.Hoard, b.S.Opinions.TryGetValue(jarl, out int o) ? o : 0, new System.Random(System.Environment.TickCount ^ b.S.Vid.GetStableHashCode()));
            if (stake == 0) { b.Say(new[] { $"Go on, {jarl}!", "Odin's eye is on you, jarl!", $"For the hall, {jarl}!", "Show them, jarl!" }[Random.Range(0, 4)], 5); return; }
            z.Set(StakeKey, stake);
            int n = Mathf.Abs(stake);
            b.Say(stake > 0 ? new[] { $"{n} {Coins(n)} on {jarl}!", $"My {n} {Coins(n)} say {jarl} wins this.", $"{n} {Coins(n)} on our jarl, who'll take it?" }[Random.Range(0, 3)]
                : new[] { $"{n} {Coins(n)} on the beasts. Sorry, jarl.", $"I'll put {n} {Coins(n)} against. Prove me wrong, {jarl}." }[Random.Range(0, 2)], 6);
        }
        private static string Coins(int n) => n == 1 ? "coin" : "coins";
        // The fight's over: each watcher run here settles what they wagered, and says what they think of it.
        private static List<string> SettleStands(string fighter, bool won)
        {
            var settled = new List<string>();
            foreach (Brain b in Villagers.All.Where(b => b.View != null && b.View.IsValid() && b.View.IsOwner() && Follow.Leader(b)?.GetPlayerName() == fighter).ToList())
            {
                ZDO z = b.View.GetZDO();
                int stake = z.GetInt(StakeKey, 0);
                Body_.Anim(b.Body)?.SetTrigger(won ? "emote_cheer" : "emote_nonono");
                if (stake == 0) { b.Say(won ? new[] { "Ha! That's our jarl!", "Did you see that?", "Skål to the jarl!" }[Random.Range(0, 3)] : new[] { "Next time, jarl.", "Ah, so close.", "They'll sing of it anyway." }[Random.Range(0, 3)], 5); continue; }
                z.Set(StakeKey, 0);
                int gain = Wagers.Settle(stake, won);
                b.S.Hoard = Mathf.Max(0, b.S.Hoard + gain);
                Mood.Add(b.S, gain > 0 ? "wonbet" : "lostbet", b.Now);
                b.Say(gain > 0 ? new[] { $"Pay up! {gain} {Coins(gain)} to me!", $"{gain} {Coins(gain)} richer, thank you jarl!", $"I knew it! {gain} {Coins(gain)}!" }[Random.Range(0, 3)]
                    : new[] { $"There go my {-gain} {Coins(-gain)}.", $"{-gain} {Coins(-gain)}, gone. Ah well.", "Never again. Well, next time." }[Random.Range(0, 3)], 6);
                b.Dirty();
                settled.Add(b.S.Vid);
            }
            return settled;
        }

        // ---- each player's own game: the end of a fight heard of ----
        public static void PlayerTick()
        {
            if (Time.time < _next || !Compat.ArenaPresent) return;
            _next = Time.time + 1f;
            int n = Compat.ArenaResults();
            if (_seen < 0 || n < _seen) { _seen = n; return; } // (the results heard before this game started watching aren't news)
            if (n == _seen) return;
            _seen = n;
            if (!Compat.ArenaLast(out string fighter, out string kind, out string outcome, out bool won, out bool crowned, out int coins)) return;
            Ended(fighter, kind, outcome, won, crowned, coins);
        }
        public static void Ended(string fighter, string kind, string outcome, bool won, bool crowned, int coins)
        {
            List<string> settled = SettleStands(fighter, won);
            Player me = Player.m_localPlayer;
            if (me == null || fighter != me.GetPlayerName() || outcome == "aborted" || outcome == "calledoff") return;
            JarlDeeds.Carry(me, Wagers.Pack(kind, outcome, crowned, coins, settled));
        }

        // ---- the colony's owner: word of it comes home ----
        public static void Heard(Colony c, string who, string deed)
        {
            if (!Wagers.Unpack(deed, out string kind, out string outcome, out bool crowned, out int coins, out HashSet<string> settled)) return;
            bool won = outcome == "win";
            if (won) Tally.Add(c.R.Flags, who, Tally.ArenaWins);
            if (crowned) Tally.Add(c.R.Flags, who, Tally.Crowned);
            string what = Wagers.Kind(kind);
            if (won) c.Log("arenawin", who, what, $"{who} won {what} in the arena{(crowned ? ", and stands in the Hall of Champions" : "")}{(coins > 0 ? $", with a purse of {coins} coins" : "")}.");
            else c.Log("arena", who, what, $"{who} fought {what} in the arena, and {Wagers.Ended(outcome)}.");
            // (the rest wager when the news comes in: those home and loaded here, who didn't watch it)
            var folk = Villagers.All.Where(b => b.S.Colony == c.Id && b.View != null && b.View.IsOwner() && !Mood.Has(b.S, "arenawatch", b.Now)).ToList();
            var bets = Wagers.Bettors(folk.Select(b => (b.S.Vid, b.S.Hoard, b.S.Opinions.TryGetValue(who, out int o) ? o : 0)), settled, new System.Random(System.Environment.TickCount));
            int up = 0, down = 0; var winners = new List<string>(); var losers = new List<string>();
            foreach (var (vid, stake) in bets)
            {
                Brain b = folk.First(x => x.S.Vid == vid);
                int gain = Wagers.Settle(stake, won);
                b.S.Hoard = Mathf.Max(0, b.S.Hoard + gain); b.Dirty();
                Mood.Add(b.S, gain > 0 ? "wonbet" : "lostbet", b.Now);
                if (gain > 0) { up += gain; winners.Add(b.S.Name); } else { down -= gain; losers.Add(b.S.Name); }
            }
            if (bets.Count > 0) c.Tell($"Word of your fight reached {c.R.Name}: {(winners.Count > 0 ? $"{Names(winners)} won {up} coins on it" : "")}{(winners.Count > 0 && losers.Count > 0 ? ", and " : "")}{(losers.Count > 0 ? $"{Names(losers)} lost {down}" : "")}.");
            JarlDeeds.Title(c, who);
            c.MarkDirty();
        }
        private static string Names(List<string> n) => n.Count == 1 ? n[0] : string.Join(", ", n.Take(n.Count - 1)) + " and " + n.Last();
    }
}
