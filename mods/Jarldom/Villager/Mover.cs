using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // Walking somewhere: the game's navmesh path, followed waypoint by waypoint; shut doors opened on the way (the navmesh takes a shut
    // door for a wall) and closed behind; stuck feet sidestep, jump, and at last step to the next waypoint.
    internal sealed class Mover
    {
        private readonly Brain _b;
        private readonly List<Vector3> _path = new List<Vector3>();
        private Vector3 _goal, _pathGoal; private float _arrive; private bool _run, _active;
        private float _nextPath, _nextDoor, _stuckSince, _lastProgressAt; private Vector3 _lastProgressPos;
        private int _stuckStage, _unsticks; private bool _failed; private float _failedAt, _sidestepUntil; private Vector3 _sidestepDir;
        private readonly List<(Door door, float at)> _opened = new List<(Door, float)>();
        private Vector3? _viaDoor; private Door _planDoor; private Vector3 _doorFar;
        private static readonly Collider[] Hits = new Collider[1024];
        private static int _doorMask = -1;
        public bool Arrived { get; private set; }
        public bool Failed => _failed;
        public Vector3 Goal => _goal;
        public bool Moving => _active && !Arrived;

        public Mover(Brain b) { _b = b; }

        public void To(Vector3 goal, float arrive = 1f, bool run = false)
        {
            if (_active && Vector3.Distance(goal, _goal) < 0.3f && !_failed) { _run = run; _arrive = arrive; return; }
            // The same goal again soon after failing stays failed, so a task that re-paths and then checks Failed sees it.
            if (_failed && Vector3.Distance(goal, _goal) < 0.3f && Time.time - _failedAt < 20f) return;
            _goal = goal; _arrive = Mathf.Max(0.4f, arrive); _run = run; _active = true; Arrived = false; _failed = false;
            _nextPath = 0; _stuckStage = 0; _lastProgressAt = Time.time; _failAt = 0; _unsticks = 0; _lastProgressPos = _b.Body.transform.position; _viaDoor = null; _planDoor = null; _doorNear = null;
        }
        public void Stop() { _active = false; _path.Clear(); _b.Ai.StopMoving(); _viaDoor = null; _doorNear = null; }
        public string LastDoorPlan = "";
        public string Debug => $"active {_active} arrived {Arrived} failed {_failed} path {_path.Count} arrive {_arrive:0.0} stuck {Time.time - _lastProgressAt:0.0}s stage {_stuckStage} door {(_doorNear.HasValue ? "yes" : "no")} plan [{LastDoorPlan}]";
        public float Distance => Utils.DistanceXZ(_b.Body.transform.position, _goal);

        // From the fixed update: one step.
        public void Step(float dt)
        {
            CloseBehind();
            if (!_active) return;
            Vector3 me = _b.Body.transform.position;
            if (Utils.DistanceXZ(me, _goal) <= _arrive && Mathf.Abs(me.y - _goal.y) < 2.5f) { Arrived = true; _active = false; _b.Ai.StopMoving(); return; }
            if (Time.time >= _nextPath && !_doorNear.HasValue || _path.Count == 0 && Time.time >= _nextPath) Repath(me);
            if (Time.time >= _nextDoor) { _nextDoor = Time.time + 0.4f; OpenAhead(me); }
            if (_doorNear.HasValue && Utils.DistanceXZ(me, _doorNear.Value) < 1.0f)
            {
                // At the near side of a planned door: open it and walk through to the far side.
                if (_planDoor != null) OpenDoor(_planDoor, me);
                _doorNear = null; _path.Clear(); _path.Add(_doorFar); _nextPath = Time.time + 3f;
            }
            Vector3 next = _path.Count > 0 ? _path[0] : _goal;
            while (_path.Count > 0 && Utils.DistanceXZ(me, _path[0]) < (_run ? 1f : 0.6f)) { _path.RemoveAt(0); next = _path.Count > 0 ? _path[0] : _goal; }
            Vector3 dir = next - me; dir.y = 0;
            if (dir.sqrMagnitude < 0.0001f) dir = _goal - me;
            bool run = _run && _b.Stamina > 0.2f;
            if (Time.time < _sidestepUntil) { dir = _sidestepDir; run = false; }
            _b.Ai.MoveTowards(dir.normalized, run);
            Unstick(me, dir);
        }

        private void Repath(Vector3 me)
        {
            _nextPath = Time.time + 2f;
            if (Pathfinding.instance == null) return;
            Vector3 goal = Nav.Valid(out Vector3 g, _goal, 2.5f) ? g : _goal;
            // A full path, or a door to go through; a partial path (up to a wall) only when neither.
            bool reaches = Pathfinding.instance.GetPath(me, goal, _path, _b.Ai.m_pathAgentType, requireFullPath: true);
            bool ok = reaches;
            if (!reaches && PlanDoor(me)) return;
            if (!reaches) ok = Pathfinding.instance.GetPath(me, goal, _path, _b.Ai.m_pathAgentType);
            if (reaches && _path.Count > 0 && Utils.DistanceXZ(goal, _goal) > 0.3f) _path.Add(_goal); // the last steps onto the bed or seat
            if (reaches) { _failAt = 0; _pathGoal = _goal; if (_path.Count > 0 && Utils.DistanceXZ(_path[0], me) < 0.3f) _path.RemoveAt(0); return; }
            if (!ok) { _path.Clear(); if (Utils.DistanceXZ(me, _goal) > 25f) _failedSoon(); }
        }
        private float _failAt;
        private void _failedSoon() { if (_failAt == 0) _failAt = Time.time + 6; else if (Time.time > _failAt) { _failed = true; _failedAt = Time.time; _active = false; _failAt = 0; _b.Ai.StopMoving(); } }

        private bool PlanDoor(Vector3 me)
        {
            Vector3 goal = Nav.Valid(out Vector3 g, _goal, 2.5f) ? g : _goal; // a bed or a seat stands off the navmesh
            // Doors only matter going into or out of a room (outdoors, a missing path is something else: a crop off the navmesh, a cliff).
            Room from = _b.Colony?.RoomAt(me), to = _b.Colony?.RoomAt(goal);
            if (_b.Colony != null && from == to) { LastDoorPlan = from == null ? "outdoors" : "same room"; return false; }
            int mask = DoorMask;
            int n = Physics.OverlapSphereNonAlloc(me, 16f, Hits, mask);
            var doors = new HashSet<Door>();
            for (int i = 0; i < n; i++) { Door d = Hits[i].GetComponentInParent<Door>(); if (d != null && d.m_keyItem == null) doors.Add(d); }
            LastDoorPlan = $"{doors.Count} doors";
            foreach (Door d in doors.OrderBy(d => Vector3.Distance(d.transform.position, me) + Vector3.Distance(d.transform.position, goal)).Take(8))
            {
                // Either side of the doorway, at floor level (a door's origin is at its middle, a metre up).
                Vector3 foot = d.transform.position + Vector3.down * 0.9f;
                Vector3 a = foot + d.transform.forward * 2f, b = foot - d.transform.forward * 2f;
                if (!Nav.Valid(out a, a, 1.5f) || !Nav.Valid(out b, b, 1.5f)) { LastDoorPlan += "; no ground by a door"; continue; }
                foreach (var (near, far) in new[] { (a, b), (b, a) })
                {
                    bool sameSide = _b.Colony != null && _b.Colony.RoomAt(me) is Room inside && Geometry.Inside(inside.Xz, near.x, near.z) && Utils.DistanceXZ(me, near) < 15f;
                    if (!Pathfinding.instance.HavePath(me, near, _b.Ai.m_pathAgentType) && !sameSide) { LastDoorPlan += $"; no way to {near:F1}"; continue; }
                    // The far side must lead to the goal, or at least be close to it in the same room (the navmesh behind a shut door can be cut off).
                    bool sameRoom = _b.Colony != null && _b.Colony.RoomAt(far) is Room r && Geometry.Inside(r.Xz, goal.x, goal.z);
                    if (!Pathfinding.instance.HavePath(far, goal, _b.Ai.m_pathAgentType) && Utils.DistanceXZ(far, goal) > 3f && !(sameRoom && Utils.DistanceXZ(far, goal) < 15f)) { LastDoorPlan += $"; no way from {far:F1}"; continue; }
                    LastDoorPlan += $"; through {d.name}";
                    Pathfinding.instance.GetPath(me, near, _path, _b.Ai.m_pathAgentType);
                    _path.Add(near);
                    _planDoor = d; _doorFar = far; _doorNear = near; _viaDoor = null;
                    _nextPath = Time.time + 6f;
                    return true;
                }
            }
            return false;
        }
        private Vector3? _doorNear;

        private static int DoorMask => _doorMask != -1 ? _doorMask : _doorMask = LayerMask.GetMask("piece", "piece_nonsolid", "Default", "door");

        // A shut door right ahead: open it. (Also catches being pressed against one.)
        private void OpenAhead(Vector3 me)
        {
            Vector3 dir = _b.Body.GetMoveDir(); if (dir.sqrMagnitude < 0.01f) dir = _b.Body.transform.forward;
            int n = Physics.OverlapSphereNonAlloc(me + Vector3.up + dir.normalized * 1.1f, 1.1f, Hits, DoorMask);
            for (int i = 0; i < n; i++)
            {
                Door d = Hits[i].GetComponentInParent<Door>();
                if (d == null || d.m_keyItem != null) continue;
                OpenDoor(d, me);
            }
        }
        public void OpenDoor(Door d, Vector3 me)
        {
            ZNetView v = d.GetComponent<ZNetView>();
            if (v == null || !v.IsValid() || v.GetZDO().GetInt(ZDOVars.s_state) != 0) return;
            bool forward = Vector3.Dot(d.transform.forward, (me - d.transform.position).normalized) < 0f;
            v.InvokeRPC("UseDoor", forward);
            _opened.RemoveAll(x => x.door == d);
            _opened.Add((d, Time.time));
        }
        // Shut what we opened once we're through, unless a player or another villager is about to use it.
        private void CloseBehind()
        {
            if (_opened.Count == 0) return;
            Vector3 me = _b.Body.transform.position;
            for (int i = _opened.Count - 1; i >= 0; i--)
            {
                var (d, at) = _opened[i];
                if (d == null || Time.time - at > 60) { _opened.RemoveAt(i); continue; }
                float dist = Vector3.Distance(me, d.transform.position);
                if (dist < 2.5f || Time.time - at < 1.5f) continue;
                if (dist > 15) { _opened.RemoveAt(i); continue; }
                if (_b.KeepDoorsOpen) continue;
                if (Player.GetAllPlayers().Any(p => Vector3.Distance(p.transform.position, d.transform.position) < 5f)) continue;
                if (Villagers.All.Any(o => o != _b && Vector3.Distance(o.Body.transform.position, d.transform.position) < 2.5f)) continue;
                ZNetView v = d.GetComponent<ZNetView>();
                if (v != null && v.IsValid() && v.GetZDO().GetInt(ZDOVars.s_state) != 0) v.InvokeRPC("UseDoor", true);
                _opened.RemoveAt(i);
            }
        }

        private void Unstick(Vector3 me, Vector3 dir)
        {
            if (Utils.DistanceXZ(me, _lastProgressPos) > 0.75f) { _lastProgressPos = me; _lastProgressAt = Time.time; _stuckStage = 0; return; }
            float stuck = Time.time - _lastProgressAt;
            if (stuck > 2.5f && _stuckStage == 0) { _stuckStage = 1; _b.Body.Jump(); }
            else if (stuck > 5f && _stuckStage == 1) { _stuckStage = 2; _nextPath = 0; _sidestepDir = Quaternion.Euler(0, Random.value < 0.5f ? 90 : -90, 0) * dir.normalized; _sidestepUntil = Time.time + 1f; }
            else if (stuck > 9f && _stuckStage == 2)
            {
                // Still stuck: step to the next waypoint, as if the feet had found the way (only a waypoint: the navmesh's own, reachable
                // ground; the goal itself may be behind a wall or up on a roof).
                _stuckStage = 3;
                if (_path.Count > 0 && Vector3.Distance(me, _path[0]) < 12f && Steer.SafeSpot(_path[0])) _b.Place(_path[0] + Vector3.up * 0.2f);
                _lastProgressAt = Time.time; _lastProgressPos = _b.Body.transform.position; _stuckStage = 0; _nextPath = 0;
                if (++_unsticks >= 3) { _failed = true; _failedAt = Time.time; _active = false; _b.Ai.StopMoving(); } // it isn't getting anywhere: give up the goal
            }
        }
    }

    internal static class Steer
    {
        private static readonly Collider[] Hits = new Collider[64];
        // Nowhere harmful: not in a fire, not on stakes, not over deep water.
        public static bool SafeSpot(Vector3 at)
        {
            int n = Physics.OverlapSphereNonAlloc(at + Vector3.up * 0.5f, 1f, Hits, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < n; i++)
            {
                Aoe aoe = Hits[i].GetComponentInParent<Aoe>();
                if (aoe != null && aoe.isActiveAndEnabled && aoe.m_hitCharacters && aoe.m_damage.GetTotalDamage() > 0) return false;
            }
            if (EffectArea.IsPointInsideArea(at, EffectArea.Type.Burning)) return false;
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(at, out float ground) && ground < ZoneSystem.instance.m_waterLevel - 1f) return false;
            return true;
        }
    }
}
