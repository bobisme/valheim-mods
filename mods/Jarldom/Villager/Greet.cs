using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // Villagers acknowledging the jarl in the world: a greeting as a player comes near, thanks for a gift, fear when a raid comes, a
    // grumble at the rain, good night at bedtime. Cooldowns per villager, per villager and player, and per player (a crowd doesn't
    // all call out at once); kept in memory only, on the game that owns the villager.
    internal static class Greet
    {
        private const float Reach = 6f, PairWait = 1200f, VillagerWait = 45f, PlayerWait = 15f;
        private static readonly Dictionary<string, float> ByVillager = new Dictionary<string, float>(), ByPair = new Dictionary<string, float>(), ByPlayer = new Dictionary<string, float>();
        private static readonly System.Random Rng = new System.Random();
        private static readonly List<(string line, float at)> Said = new List<(string, float)>();
        public static readonly List<string> Recent = new List<string>(); // (the last few said, for the jarldom greetings tool)

        // Says it if the cooldowns allow; pairWait: how long before this villager says this to that player again.
        // urgent: said even if this villager spoke a moment ago (thanks for a gift, a cry at a raid).
        public static bool Say(Brain b, Greeting kind, Player p, float pairWait = PairWait, bool crowd = true, bool urgent = false)
        {
            if (b == null || b.S == null) return false;
            float now = Time.time;
            string name = p != null ? p.GetPlayerName() : "", pair = b.S.Vid + "|" + name + "|" + kind, heard = name + "|" + kind; // (one of each kind at a time per player: a hello doesn't hush a good night)
            if (!urgent && ByVillager.TryGetValue(b.S.Vid, out float v) && now < v) return false;
            if (p != null && ByPair.TryGetValue(pair, out float w) && now < w) return false;
            if (p != null && crowd && ByPlayer.TryGetValue(heard, out float pl) && now < pl) return false;
            ByVillager[b.S.Vid] = now + VillagerWait;
            if (p != null) { ByPair[pair] = now + pairWait; ByPlayer[heard] = now + PlayerWait; }
            if (ByPair.Count > 2000) ByPair.Clear();
            bool evening = b.Colony != null && (b.Colony.Hour >= 17 || b.Colony.Hour < 5);
            var line = Greetings.Pick(kind, Rng, name, evening);
            Said.RemoveAll(x => Time.time - x.at > 20f);
            for (int i = 0; i < 6 && Said.Any(x => x.line == line.english); i++) line = Greetings.Pick(kind, Rng, name, evening); // (not a line someone just said)
            Said.Add((line.english, Time.time));
            b.Say(Greetings.Show(line), 5f);
            Recent.Add($"{System.DateTime.Now:HH:mm:ss} {b.S.Name} ({kind}{(name != "" ? " to " + name : "")}): {line.norse} / {line.english}");
            if (Recent.Count > 20) Recent.RemoveAt(0);
            return true;
        }
        public static void Forget() { ByVillager.Clear(); ByPair.Clear(); ByPlayer.Clear(); _wet.Clear(); Gloom.Clear(); }

        // A miserable colony (mood under -40 on average): no greetings, no gossip, no songs; the hall is quiet.
        private static readonly Dictionary<string, (bool quiet, float at)> Gloom = new Dictionary<string, (bool, float)>();
        private static float _toldQuiet;
        public static bool Quiet(Colony c)
        {
            if (c == null) return false;
            if (Gloom.TryGetValue(c.Id, out var g) && Time.time - g.at < 10f) return g.quiet;
            var mine = Villagers.All.Where(o => o.S.Colony == c.Id).ToList();
            bool quiet = mine.Count > 0 && mine.Average(o => (double)Mood.Of(o.S, o.Now)) < -40;
            Gloom[c.Id] = (quiet, Time.time);
            Player me = Player.m_localPlayer;
            if (quiet && me != null && Time.time - _toldQuiet > 600f && c.RoomAt(me.transform.position, RoomType.GreatHall) != null)
            { _toldQuiet = Time.time; me.Message(MessageHud.MessageType.Center, "The hall is quiet."); }
            return quiet;
        }

        // Every second or two: a player close by, seen, and not in a fight or asleep.
        public static void Near(Brain b)
        {
            if (b.Colony == null || Combat.Busy(b) || b.Task is SleepTask || b.Task is AscendTask || b.Colony.RaidOn || b.Task is MeetTask || Quiet(b.Colony)) return;
            if (ByVillager.TryGetValue(b.S.Vid, out float v) && Time.time < v) return;
            Player p = Player.GetAllPlayers().Where(x => x != null && !x.IsDead() && Vector3.Distance(x.transform.position, b.Pos) < Reach && Mathf.Abs(x.transform.position.y - b.Pos.y) < 2.5f)
                .OrderBy(x => Vector3.Distance(x.transform.position, b.Pos)).FirstOrDefault();
            if (p == null) return;
            Say(b, Greeting.Hello, p);
        }
        // A raid comes: a few of them cry out.
        public static void Raid(Colony c)
        {
            foreach (Brain b in Villagers.All.Where(b => b.S.Colony == c.Id).OrderBy(_ => Rng.Next()).Take(3))
                Say(b, Greeting.Fear, null, urgent: true);
        }
        // The rain starts: someone out in it grumbles, where a player can hear.
        private static readonly Dictionary<string, bool> _wet = new Dictionary<string, bool>();
        public static void Weather(Colony c)
        {
            bool wet = EnvMan.IsWet();
            bool was = _wet.TryGetValue(c.Id, out bool w) && w;
            _wet[c.Id] = wet;
            if (!wet || was) return;
            foreach (Brain b in Villagers.All.Where(b => b.S.Colony == c.Id).OrderBy(b => c.RoomAt(b.Pos) == null ? 0 : 1).ThenBy(_ => Rng.Next())) // (one out in it first)
            {
                Player p = Player.GetAllPlayers().FirstOrDefault(x => x != null && Vector3.Distance(x.transform.position, b.Pos) < 20f);
                if (p != null && Say(b, Greeting.Weather, p, 600f, false)) return;
            }
        }
        // Off to bed, with a player near.
        public static void Night(Brain b)
        {
            Player p = Player.GetAllPlayers().Where(x => x != null && Vector3.Distance(x.transform.position, b.Pos) < 10f).OrderBy(x => Vector3.Distance(x.transform.position, b.Pos)).FirstOrDefault();
            if (p != null) Say(b, Greeting.Night, p, 1800f);
        }
    }
}
