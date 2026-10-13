using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // Coming home by sea: a player who has been away a while sails up to the colony's fishing dock, and up to six villagers with
    // nothing pressing walk down to meet them and call out a welcome. Night or day. Seen on the player's own game; the villagers are
    // asked on whichever game owns them.
    internal static class Landing
    {
        private const float Away = 300f, Near = 60f, Again = 600f;
        private static readonly Dictionary<string, float> LastHome = new Dictionary<string, float>(), Met = new Dictionary<string, float>();
        private static float _next;
        public static void Tick()
        {
            if (Time.time < _next) return;
            _next = Time.time + 2f;
            Player me = Player.m_localPlayer;
            if (me == null || me.IsDead()) return;
            foreach (Colony c in Colony.All.Where(c => c.Valid))
            {
                float d = Utils.DistanceXZ(me.transform.position, c.Position);
                bool home = d < c.Radius + 60f;
                bool away = !LastHome.TryGetValue(c.Id, out float last) || Time.time - last > Away;
                if (home && !(Ship.GetLocalShip() != null && away)) { LastHome[c.Id] = Time.time; continue; } // (walked in, or never left)
                if (!home || Ship.GetLocalShip() == null || !away) continue;
                Zone dock = c.R.Zones.Where(z => z.Type == ZoneType.FishingDock).OrderBy(z => Utils.DistanceXZ(Zones.Centre(z), me.transform.position)).FirstOrDefault();
                if (dock == null || Utils.DistanceXZ(Zones.Centre(dock), me.transform.position) > Near) continue;
                LastHome[c.Id] = Time.time;
                if (Met.TryGetValue(c.Id, out float met) && Time.time - met < Again) continue;
                Met[c.Id] = Time.time;
                Meet(c, Zones.Centre(dock), me.GetPlayerName());
            }
        }
        // Up to six who are free (the colony's roster, wherever their game is): to the dock.
        public static void Meet(Colony c, Vector3 dock, string who)
        {
            foreach (RosterRow r in c.R.Living.Where(r => Villagers.Body(r.Vid) != null && Utils.DistanceXZ(Villagers.Body(r.Vid).transform.position, dock) < 120f).OrderBy(r => Utils.DistanceXZ(Villagers.Body(r.Vid).transform.position, dock)).Take(10).ToList().Take(6))
                Villagers.Command(r.Vid, "meet", $"{dock.x.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{dock.z.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{who}");
        }
        public static void Forget() { LastHome.Clear(); Met.Clear(); }
    }

    // Down to the dock to meet the jarl: wait for them, then a welcome.
    internal sealed class MeetTask : Task
    {
        private readonly Vector3 _dock; private readonly string _who; private float _until, _speakAt; private bool _greeted;
        public MeetTask(Vector3 dock, string who) { _dock = dock; _who = who; Doing = "going down to the dock to meet " + who; }
        public override bool Interruptible => false; // (two minutes at most)
        // Dry ground at the water's edge by the dock (a fishing dock's middle is out on the water): the nearest spot above it.
        internal static Vector3? Shore(Vector3 dock, Vector3 from)
        {
            float water = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;
            Vector3? best = null; float bestD = float.MaxValue;
            for (int i = 0; i < 40; i++)
            {
                Vector2 r = Random.insideUnitCircle * 16f;
                Vector3? spot = Colony.Spot(dock + new Vector3(r.x, 0, r.y), 2f);
                if (spot == null || spot.Value.y < water + 0.3f) continue;
                float d = Utils.DistanceXZ(spot.Value, dock);
                if (d < bestD) { bestD = d; best = spot; }
            }
            return best;
        }
        public override void Start(Brain b)
        {
            _until = Time.time + 120f;
            b.Move.To(Shore(_dock, b.Pos) ?? _dock, 1.5f);
        }
        public override void Tick(Brain b)
        {
            if (Time.time > _until || _greeted && Time.time > _until - 1f) { Done = true; return; }
            Player p = Player.GetAllPlayers().FirstOrDefault(x => x != null && x.GetPlayerName() == _who);
            if (p != null && Vector3.Distance(p.transform.position, b.Pos) < 16f)
            {
                Vector3 d = p.transform.position - b.Pos; d.y = 0; if (d.sqrMagnitude > 0.01f) b.Ai.LookTowards(d.normalized);
                if (_speakAt == 0) _speakAt = Time.time + Random.Range(0.3f, 7f); // (one after another, not all at once)
                if (!_greeted && Time.time >= _speakAt) { _greeted = true; Greet.Say(b, Greeting.Welcome, p, 1200f, false, true); _until = Time.time + 8f; }
                return;
            }
            if (!b.Move.Arrived && !b.Move.Moving && !b.Move.Failed) Start(b);
            if (b.Move.Failed) Done = true;
        }
    }
}
