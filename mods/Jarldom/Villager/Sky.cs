using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // The northern lights (an Omens aurora): villagers with nothing pressing go out under the open sky, look up, and say so; a night
    // they'll remember. Once a night each, never in a raid.
    internal static class Sky
    {
        private static readonly HashSet<string> Seen = new HashSet<string>(); // ("vid|day": once a night)
        // On the game that owns them, every second or two.
        public static void Look(Brain b)
        {
            Colony c = b.Colony;
            if (c == null || c.RaidOn || !Free(b) || Seen.Contains(b.S.Vid + "|" + c.Day) || !Compat.Aurora()) return;
            Seen.Add(b.S.Vid + "|" + c.Day);
            if (Seen.Count > 2000) Seen.Clear();
            if (Random.value > 0.7f) return; // (not everyone gets up for it)
            Vector3? spot = Outside(c);
            if (spot == null) return;
            b.Task?.Stop(b);
            b.Task = new SkyTask(spot.Value);
            b.Task.Start(b);
        }
        public static void Forget() { Seen.Clear(); }
        // Awake and idle: in the hall of an evening, wandering, between chores. Not asleep, not fighting, not out with the jarl.
        private static bool Free(Brain b) => b.Alive && !Combat.Busy(b) && Follow.LeaderId(b) == 0 && (b.Task == null || b.Task is LeisureTask || b.Task is HallTask || b.Task is WanderTask);
        // Open sky near the pillars: off every room, nothing overhead.
        private static Vector3? Outside(Colony c)
        {
            for (int i = 0; i < 12; i++)
            {
                Vector2 r = Random.insideUnitCircle.normalized * Random.Range(6f, 16f);
                Vector3? spot = Colony.Spot(c.Position + new Vector3(r.x, 0, r.y), 2f);
                if (spot == null || c.RoomAt(spot.Value) != null) continue;
                Cover.GetCoverForPoint(spot.Value + Vector3.up, out float cover, out bool roof);
                if (!roof) return spot;
            }
            return null;
        }
    }

    internal sealed class SkyTask : Task
    {
        private readonly Vector3 _to; private float _until, _speakAt; private bool _said;
        public SkyTask(Vector3 to) { _to = to; Doing = "going out to see the northern lights"; }
        public override bool Interruptible => false; // (two minutes at most)
        public override void Start(Brain b) { _until = Time.time + 120f; b.Move.To(_to, 1.5f); }
        public override void Tick(Brain b)
        {
            if (Time.time > _until || !Compat.Aurora() || b.Colony == null || b.Colony.RaidOn) { Done = true; return; }
            if (!b.Move.Arrived) { if (b.Move.Failed) Done = true; else if (!b.Move.Moving) b.Move.To(_to, 1.5f); return; }
            if (_speakAt == 0)
            {
                _speakAt = Time.time + Random.Range(2f, 20f); _until = Time.time + Random.Range(35f, 55f); // (one after another, not a chorus)
                Doing = "watching the northern lights";
                b.Ai.LookTowards(Vector3.forward); // (north, where they burn)
                Mood.Add(b.S, "aurora", b.Now); b.Dirty();
            }
            if (!_said && Time.time >= _speakAt) { _said = true; Greet.Say(b, Greeting.Awe, null, urgent: true); }
        }
    }
}
