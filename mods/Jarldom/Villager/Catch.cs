using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jarldom
{
    // An empty pasture stocked: when it holds fewer than two beasts, the herder (a hunter, if there's none) goes out after a wild boar
    // near the colony, ropes it and leads it home into the pasture, where feeding tames it the game's way (fed and calm, in time).
    internal static class Catching
    {
        public const string Wild = "Boar";
        private static float _busyUntil; // (one catch at a time, colony-wide)
        public static Task Next(Brain b)
        {
            Colony c = b.Colony;
            if (Time.time < b.NoCatchUntil || Time.time < _busyUntil || c == null || !c.IsOwner) return null;
            if (b.S.Prof == Prof.Hunter && Villagers.All.Any(v => v.S.Colony == c.Id && v.S.Prof == Prof.Herder)) return null; // (a herder's work first)
            b.NoCatchUntil = Time.time + 60;
            Zone pasture = c.R.Zones.FirstOrDefault(z => z.Type == ZoneType.Pasture && Count(z) < 2);
            if (pasture == null) return null;
            Vector3 centre = Zones.Centre(pasture);
            Character boar = Character.GetAllCharacters().Where(ch => ch != null && !ch.IsDead() && !ch.IsTamed() && Utils.GetPrefabName(ch.gameObject) == Wild && !Zones.Inside(pasture, ch.transform.position)
                    && Utils.DistanceXZ(ch.transform.position, centre) < 150f)
                .OrderBy(ch => Vector3.Distance(ch.transform.position, b.Pos)).FirstOrDefault();
            if (boar == null) { b.NoCatchUntil = Time.time + 180; return null; }
            _busyUntil = Time.time + 240;
            return new CatchTask(boar, pasture);
        }
        // Beasts in a pasture, tame or not (the kinds that can be tamed).
        public static int Count(Zone z)
        {
            Vector3 centre = Zones.Centre(z); float reach = Zones.Reach(z) + 4;
            return Character.GetAllCharacters().Count(ch => ch != null && !ch.IsDead() && !ch.IsPlayer() && !VillagerPrefab.Is(ch) && ch.GetComponent<Tameable>() != null && ch.m_group != "bob_gary"
                && Vector3.Distance(ch.transform.position, centre) < reach && Zones.Inside(z, ch.transform.position));
        }
        public static void Done() => _busyUntil = 0;
    }

    internal sealed class CatchTask : Task
    {
        private Character _boar; private readonly Zone _pasture;
        public Character Target => _boar; // (not a foe: a beast being roped is taken, kicks and all)
        private int _phase, _beats, _level = 1; private float _next, _until;
        public CatchTask(Character boar, Zone pasture) { _boar = boar; _pasture = pasture; }
        public override bool Interruptible => _phase == 0;
        public override void Start(Brain b) { Doing = "going after a wild boar for the pasture"; _until = Time.time + 180; b.Move.To(_boar.transform.position, 2f); }
        public override void Tick(Brain b)
        {
            if (Time.time > _until) { Done = true; return; }
            if (_phase == 0)
            {
                if (_boar == null || _boar.IsDead() || _boar.IsTamed()) { Done = true; return; }
                Vector3 at = _boar.transform.position;
                if (Vector3.Distance(b.Pos, at) > 2.5f) { if (!b.Move.Moving || Vector3.Distance(b.Move.Goal, at) > 2f) b.Move.To(at, 1.8f, Vector3.Distance(b.Pos, at) > 8f); if (b.Move.Failed) { Done = true; b.NoCatchUntil = Time.time + 120; } return; }
                b.Move.Stop();
                Vector3 d = at - b.Pos; d.y = 0; if (d.sqrMagnitude > 0.01f) b.Ai.LookTowards(d.normalized);
                if (Time.time < _next) return;
                _next = Time.time + 1.2f;
                Body_.Anim(b.Body)?.SetTrigger("interact");
                if (++_beats < 3) return;
                // Roped: it goes along (out of the world for the walk home, and back in the pasture).
                _level = _boar.GetLevel();
                ZNetView v = _boar.GetComponent<ZNetView>();
                if (v != null && v.IsValid()) { if (!v.IsOwner()) v.ClaimOwnership(); v.Destroy(); }
                _boar = null; _phase = 1;
                Doing = "leading a wild boar home on a rope";
                b.Say("Come on, you. A warm pen and all the carrots you can eat.", 4);
                b.Move.To(Pen(), 2f);
                return;
            }
            Vector3 pen = Pen();
            if (Utils.DistanceXZ(b.Pos, pen) > 3f && !Zones.Inside(_pasture, b.Pos)) { if (!b.Move.Moving) b.Move.To(pen, 2f); if (b.Move.Failed) Release(b, b.Pos); return; }
            Release(b, b.Pos + b.Body.transform.forward * 1.5f);
            b.Colony.Log("text", b.S.Name, "", $"{b.S.Name} brought a wild boar to the pasture, to be tamed.");
            b.S.Bump("caught"); b.Dirty();
        }
        private Vector3 Pen() { Vector3 c = Zones.Centre(_pasture); return Colony.Spot(c, 2f) ?? c; }
        // The boar back in the world (in the pasture, or wherever they had to stop).
        private void Release(Brain b, Vector3 at)
        {
            if (_phase == 1)
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(Catching.Wild);
                if (prefab != null) { GameObject go = Object.Instantiate(prefab, at + Vector3.up * 0.3f, Quaternion.identity); go.GetComponent<Character>()?.SetLevel(_level); }
                _phase = 2;
            }
            Catching.Done();
            Done = true;
        }
        public override void Stop(Brain b) { if (_phase == 1 && b.View != null && b.View.IsValid()) Release(b, b.Pos); else Catching.Done(); base.Stop(b); }
    }
}
