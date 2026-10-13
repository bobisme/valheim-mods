using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // The Player rig's held poses. "wakeup" is the reborn-on-the-ground pose a player's own code switches off after a second (ours never
    // would); a bed or chair flag can outlive a reload. Either leaves the body frozen (it can't move or swing). Cleared when a brain starts,
    // when a villager stands up, and on every game for bodies nobody holds seated.
    internal static class Poses
    {
        private static readonly string[] Sticky = { "wakeup", "attach_bed", "attach_chair", "attach_throne", "attach_dragon" };
        public static void Clear(Humanoid h)
        {
            if (h == null) return;
            ZSyncAnimation z = Body_.Anim(h);
            Animator an = h.GetComponentInChildren<Animator>();
            foreach (string b in Sticky)
            {
                z?.SetBool(b, false);
                if (an != null && an.parameters.Any(p => p.name == b)) an.SetBool(b, false);
            }
            ZNetView v = h.GetComponent<ZNetView>();
            if (v != null && v.IsValid() && v.IsOwner() && v.GetZDO().GetBool(ZDOVars.s_wakeup, true)) v.GetZDO().Set(ZDOVars.s_wakeup, false);
        }
        private static float _next;
        public static void Tick()
        {
            if (Time.time < _next) return;
            _next = Time.time + 1f;
            foreach (Humanoid h in Villagers.Bodies())
            {
                Brain b = Villagers.Brain(h);
                if (b != null && b.Held != null) continue;      // really sitting or asleep
                Rigidbody body = h.GetComponent<Rigidbody>();
                if (b == null && body != null && body.isKinematic) continue; // another game holds it seated (its kinematic body says so)
                Animator an = h.GetComponentInChildren<Animator>();
                if (an != null && (an.GetBool("wakeup") || b != null && Sticky.Any(p => p != "wakeup" && an.parameters.Any(q => q.name == p) && an.GetBool(p)))) Clear(h); // (a throne's or a bench's pose too, not only a chair's or a bed's)
            }
        }
    }

    // Villagers walk through players, each other, Gary and Quad's companions (as Gary and the companions do): bodies that shove each other
    // clog doorways and push players about. Each game sets it for the bodies it has, twice a second; fighting and arrows don't use these contacts.
    internal static class Passing
    {
        private sealed class Pair { public Collider A, B; public bool Was; }
        private static readonly List<Pair> Pairs = new List<Pair>();
        private static readonly HashSet<long> Done = new HashSet<long>();
        private static readonly int Companion = "dhack_companion".GetStableHashCode();
        private static float _next;

        private static bool Through(Character c)
        {
            if (c == null || c.IsDead()) return false;
            if (c is Player || VillagerPrefab.Is(c) || c.m_group == "bob_gary") return true;
            ZNetView v = c.GetComponent<ZNetView>();
            return v != null && v.IsValid() && v.GetZDO().GetPrefab() == Companion;
        }
        public static void Tick()
        {
            if (Time.time < _next) return;
            _next = Time.time + 0.5f;
            for (int i = Pairs.Count - 1; i >= 0; i--) if (Pairs[i].A == null || Pairs[i].B == null) { Pairs.RemoveAt(i); }
            var villagers = Villagers.Bodies().ToList();
            if (villagers.Count == 0) return;
            var others = Character.GetAllCharacters().Where(Through).ToList();
            foreach (Humanoid v in villagers)
            {
                Collider a = v.GetCollider();
                if (a == null) continue;
                foreach (Character o in others)
                {
                    if (o == v) continue;
                    Collider b = o.GetCollider();
                    if (b == null) continue;
                    long key = ((long)a.GetInstanceID() << 32) ^ (uint)b.GetInstanceID();
                    if (Done.Add(key)) Pairs.Add(new Pair { A = a, B = b, Was = Physics.GetIgnoreCollision(a, b) });
                    Physics.IgnoreCollision(a, b, true); // (again each time: Unity forgets a pair when a collider is switched off)
                }
            }
            if (Done.Count > 20000) { Done.Clear(); }
        }
        public static void Clear()
        {
            foreach (Pair p in Pairs) if (p.A != null && p.B != null) Physics.IgnoreCollision(p.A, p.B, p.Was);
            Pairs.Clear(); Done.Clear();
        }
    }
}
