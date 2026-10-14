using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // The colony's beacon: a jarl sailing home at night (Core/Return.cs) is seen from the watch, and a guard runs to light the beacon -
    // Wayfinder's Beacon Fire if there's one, else the fire the jarl made the colony's beacon (Shift+E on it with the Mark rooms tool).
    // Lit before they land: if the guard isn't there in time, it's lit anyway. Seen on the player's game; lit on the colony's owner.
    internal static class Beacon
    {
        public const string MarkPrefix = "beacon:"; // (colony flag "beacon:<fire id>" = 1: the fire marked as its beacon)
        public const float LateAfter = 25f;
        public const string DarkKey = "darkhome"; // (colony flag: the day a jarl came home by night to no beacon - a matter for the Thing)
        private static readonly Dictionary<string, double> LastReturn = new Dictionary<string, double>();
        private static readonly Dictionary<string, (float at, string who)> Waiting = new Dictionary<string, (float, string)>();
        private static float _next;

        // ---- on the player's game: home by sea at night ----
        public static void PlayerTick()
        {
            if (Time.time < _next) return;
            _next = Time.time + 2f;
            Player me = Player.m_localPlayer; Ship ship = Ship.GetLocalShip();
            if (me == null || me.IsDead() || ship == null || ZNet.instance == null) return;
            Rigidbody body = ship.GetComponent<Rigidbody>();
            Vector3 p = ship.transform.position, v = body != null ? body.linearVelocity : Vector3.zero;
            double now = ZNet.instance.GetTimeSeconds();
            foreach (Colony c in Colony.All.Where(c => c.Valid && c.AllowedName(me.GetPlayerName())))
            {
                LastReturn.TryGetValue(c.Id, out double last);
                if (!Return.Detect(p.x, p.z, v.x, v.z, c.Position.x, c.Position.z, c.Radius, Return.Night(c.Hour), now, last)) continue;
                LastReturn[c.Id] = now;
                Net.Act(c, "beacon", "");
            }
        }

        // ---- on the colony's owner ----
        // Where the beacon is (and whether it burns), or null: Wayfinder's first, else the marked fire.
        public static Vector3? Where(Colony c, out bool lit, out Fireplace marked)
        {
            marked = null; lit = false;
            Vector3? wf = Compat.WayfinderBeacon(c.Position, c.Radius + 60f);
            if (wf.HasValue) { lit = Compat.WayfinderLit(wf.Value, 3f); return wf; }
            string id = c.R.Flags.Keys.FirstOrDefault(k => k.StartsWith(MarkPrefix))?.Substring(MarkPrefix.Length);
            if (id == null) return null;
            marked = c.PiecesIn<Fireplace>(c.Radius + 60f).FirstOrDefault(f => f != null && Colony.IdOf(f.GetComponent<ZNetView>()) == id);
            if (marked == null) return null;
            lit = Burning(marked);
            return marked.transform.position;
        }
        public static bool Burning(Fireplace f) { ZNetView v = f.GetComponent<ZNetView>(); return v != null && v.IsValid() && v.GetZDO().GetFloat(ZDOVars.s_fuel) > 0; }
        public static bool Light(Colony c)
        {
            Vector3? at = Where(c, out bool lit, out Fireplace marked);
            if (at == null || lit) return false;
            if (marked == null) return Compat.WayfinderLight(at.Value, 3f);
            ZNetView v = marked.GetComponent<ZNetView>();
            v.InvokeRPC("RPC_SetFuelAmount", marked.m_maxFuel);
            return true;
        }
        // A jarl's ship seen coming in: the nearest guard runs to light it.
        public static void Called(Colony c, string who)
        {
            Vector3? at = Where(c, out bool lit, out _);
            if (at == null) { c.R.Flags[DarkKey] = Mathf.Max(1, c.Day); c.MarkDirty(); c.TellPlayer(who, $"No beacon at {c.R.Name} to light for you: build a Beacon Fire (Wayfinder), or make a fire the colony's beacon (Shift+E on it with the Mark rooms tool)."); return; }
            if (lit) return;
            Waiting[c.Id] = (Time.time + LateAfter, who);
            RosterRow guard = c.R.Living.Where(r => r.Prof == Prof.Guard && Villagers.Body(r.Vid) != null).OrderBy(r => Utils.DistanceXZ(Villagers.Body(r.Vid).transform.position, at.Value)).FirstOrDefault()
                ?? c.R.Living.Where(r => Villagers.Body(r.Vid) != null).OrderBy(r => Utils.DistanceXZ(Villagers.Body(r.Vid).transform.position, at.Value)).FirstOrDefault();
            if (guard != null) Villagers.Command(guard.Vid, "beacon", $"{at.Value.x.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{at.Value.z.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{who}");
        }
        // (the guard lit it, or it's lit when they're late)
        public static void Tick(Colony c)
        {
            if (!c.IsOwner || !Waiting.TryGetValue(c.Id, out var w)) return;
            Where(c, out bool lit, out _);
            if (lit) { Waiting.Remove(c.Id); return; }
            if (Time.time < w.at) return;
            Waiting.Remove(c.Id);
            if (Light(c)) c.Log("beacon", "the watch", w.who, $"The watch saw {w.who}'s sail in the dark and lit the beacon.");
        }
        public static void Lit(Colony c, string by, string who) { Waiting.Remove(c.Id); c.Log("beacon", by, who, $"{by} saw {who}'s sail and lit the beacon."); }
        // Shift+E on a fire with the Mark rooms tool: the colony's beacon, or not any more.
        public static void Mark(Colony c, string id)
        {
            bool was = c.R.Flag(MarkPrefix + id) != 0;
            foreach (string k in c.R.Flags.Keys.Where(k => k.StartsWith(MarkPrefix)).ToList()) c.R.Flags.Remove(k);
            if (!was) c.R.Flags[MarkPrefix + id] = 1;
            c.MarkDirty();
            c.Tell(was ? "That fire is no longer the colony's beacon." : "That fire is the colony's beacon now: when a jarl sails home at night, the watch lights it.");
        }
        public static bool IsMarked(Colony c, Fireplace f) => f != null && c.R.Flag(MarkPrefix + Colony.IdOf(f.GetComponent<ZNetView>())) != 0;
        public static void Forget() { LastReturn.Clear(); Waiting.Clear(); }
    }

    // A guard to the beacon, at a run, to light it.
    internal sealed class BeaconTask : Task
    {
        private readonly Vector3 _at; private readonly string _who; private float _until; private bool _done;
        public BeaconTask(Vector3 at, string who) { _at = at; _who = who; Doing = "running to light the beacon"; }
        public override bool Interruptible => false; // (a minute at most)
        public override void Start(Brain b)
        {
            _until = Time.time + 60f;
            b.Say(new[] { $"A sail! It's {_who}, coming home! Light the beacon!", "A sail in the dark! The beacon!", $"{_who}'s ship! To the beacon!", "Someone's coming home! Light the fire!" }[Random.Range(0, 4)], 4);
            b.Move.To(_at, 2.5f, true);
        }
        public override void Tick(Brain b)
        {
            if (Time.time > _until || b.Move.Failed) { Done = true; return; }
            if (_done) return;
            if (Utils.DistanceXZ(b.Pos, _at) > 3.5f) { if (!b.Move.Moving) b.Move.To(_at, 2.5f, true); return; }
            b.Move.Stop(); _done = true;
            Vector3 d = _at - b.Pos; d.y = 0; if (d.sqrMagnitude > 0.01f) b.Ai.LookTowards(d.normalized);
            Body_.Anim(b.Body)?.SetTrigger("interact");
            if (b.Colony.IsOwner) { if (Beacon.Light(b.Colony)) Beacon.Lit(b.Colony, b.S.Name, _who); }
            else Net.Act(b.Colony, "beaconlit", b.S.Name + "|" + _who);
            _until = Time.time + 4f;
        }
    }
}
