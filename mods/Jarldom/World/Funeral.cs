using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // The ship burial's farewell: the colony comes down to the shore, the ship is launched, an archer looses a burning arrow into it,
    // and they call after the one going (World/ShipBurial.cs draws the voyage on every game).
    internal static class Funeral
    {
        public static float LaunchedAt = -1f; public static string Going = "";
        // On the colony's owner, when the soul is aboard: everyone free to the shore.
        public static void Gather(Colony c, Vector3 ship, Brain soul)
        {
            Going = soul.S.Name; LaunchedAt = -1f;
            c.Tell($"{soul.S.FullName} goes to Valhalla by ship. Come down to the shore and see them off.", true);
            foreach (RosterRow r in c.R.Living.Where(r => r.Vid != soul.S.Vid))
                Villagers.Command(r.Vid, "farewell", $"{ship.x.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{ship.z.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{soul.S.Name}");
            soul.Say(new[] { "I'm ready.", "Let them come. I'll wait.", "So this is how it ends. Well enough.", "It's a fine ship. Thank you, jarl." }[Random.Range(0, 4)], 6);
        }
        // Who looses the arrow: a hunter if one came, else a guard, else whoever stands nearest.
        public static Brain Archer(Colony c, Vector3 ship)
        {
            var there = Villagers.All.Where(b => b.S.Colony == c.Id && b.Task is FarewellTask && Vector3.Distance(b.Pos, ship) < 40f).ToList();
            return there.Where(b => b.S.Prof == Prof.Hunter).OrderBy(b => Vector3.Distance(b.Pos, ship)).FirstOrDefault()
                ?? there.Where(b => b.S.Prof == Prof.Guard).OrderBy(b => Vector3.Distance(b.Pos, ship)).FirstOrDefault()
                ?? there.OrderBy(b => Vector3.Distance(b.Pos, ship)).FirstOrDefault();
        }
    }

    // On the shore to see them off: stand facing the ship; when it's launched, a farewell called after it, each in turn.
    internal sealed class FarewellTask : Task
    {
        private readonly Vector3 _ship; private readonly string _who; private float _until, _speakAt; private bool _said;
        public FarewellTask(Vector3 ship, string who) { _ship = ship; _who = who; Doing = "seeing " + who + " off to Valhalla"; }
        public override bool Interruptible => false; // (two minutes at most)
        public override void Start(Brain b)
        {
            _until = Time.time + 130f;
            b.Move.To(MeetTask.Shore(_ship, b.Pos) ?? _ship, 2f);
        }
        public override void Tick(Brain b)
        {
            if (Time.time > _until) { Done = true; return; }
            if (b.Move.Moving && !b.Move.Failed) return;
            Vector3 d = _ship - b.Pos; d.y = 0; if (d.sqrMagnitude > 0.01f) b.Ai.LookTowards(d.normalized);
            if (Funeral.LaunchedAt < 0) return;
            if (_speakAt == 0) { _speakAt = Funeral.LaunchedAt + Random.Range(14f, 40f); _until = Mathf.Min(_until, Funeral.LaunchedAt + 75f); }
            if (!_said && Time.time >= _speakAt) { _said = true; Greet.Say(b, Greeting.Farewell, null, urgent: true, about: _who); }
        }
    }
}
