using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
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
