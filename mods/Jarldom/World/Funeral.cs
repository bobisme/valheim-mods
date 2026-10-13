using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // The ship burial's farewell: the colony comes down to the shore, the ship is launched, an archer looses a burning arrow into it,
    // and they call after the one going (World/ShipBurial.cs draws the voyage on every game).
    internal static class Funeral
    {
        public static float LaunchedAt = -1f; public static string Going = "", ArcherVid = "";
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
            bool archer = b.S.Vid == Funeral.ArcherVid;
            if (_speakAt == 0)
            {
                // (the archer calls out as they loose; the rest after the ship has caught, each in turn)
                _speakAt = archer ? Funeral.LaunchedAt + ShipBurial.Voyage.Loose - 1.5f : Funeral.LaunchedAt + ShipBurial.Voyage.Loose + Random.Range(4f, 28f);
                _until = Mathf.Min(_until, Funeral.LaunchedAt + 80f);
            }
            if (!_said && Time.time >= _speakAt)
            {
                _said = true;
                if (archer) b.Say(new[] { $"To Valhalla, {_who}!", $"Odin, take {_who}!", $"Fly true. For {_who}.", $"Skål, {_who}. Burn bright." }[Random.Range(0, 4)], 6);
                else Greet.Say(b, Greeting.Farewell, null, urgent: true, about: _who);
            }
        }
    }
}
