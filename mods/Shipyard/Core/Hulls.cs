using System;
using System.Collections.Generic;
using System.Linq;

namespace Shipyard
{
    // What can be built on a slipway, as data: each hull in its stages (keel, ribs, strakes, fittings...), each stage with what it
    // costs. A stage is paid IN FULL before the next is begun; what's put in is kept on the slipway (never lost: taken down, it all
    // comes back), and any number of players can put in.
    public sealed class Stage { public string Name, Model, Title; public (string item, int n)[] Cost; }
    public sealed class Hull
    {
        public string Name, Title, Prefab; public Stage[] Stages;
        public float Draught, Length; // (how deep the water under it must be to launch it, all along its length)
    }

    public static class Hulls
    {
        public const string Faering = "faering";
        public static readonly Hull[] All =
        {
            new Hull
            {
                Name = Faering, Title = "Færing", Prefab = Names.Faering, Draught = 0.6f, Length = 5.4f,
                Stages = new[]
                {
                    new Stage { Name = "keel", Title = "Keel and stems", Model = "faering_keel", Cost = new[] { ("Wood", 10), ("Resin", 2) } },
                    new Stage { Name = "ribs", Title = "Ribs", Model = "faering_ribs", Cost = new[] { ("Wood", 12) } },
                    new Stage { Name = "strakes", Title = "Strakes", Model = "faering_strakes", Cost = new[] { ("Wood", 20), ("Resin", 8), ("LeatherScraps", 4) } },
                    new Stage { Name = "fit", Title = "Thwarts and oars", Model = "faering_fit", Cost = new[] { ("Wood", 8), ("LeatherScraps", 4) } },
                },
            },
        };
        public static Hull Of(string name) => All.FirstOrDefault(h => h.Name == name);
        // Everything a hull costs, all its stages together.
        public static Dictionary<string, int> Total(Hull h)
        {
            var t = new Dictionary<string, int>();
            foreach (Stage s in h.Stages) foreach (var (item, n) in s.Cost) t[item] = (t.TryGetValue(item, out int had) ? had : 0) + n;
            return t;
        }
    }

    // A slipway's build, as it's kept in its ZDO: the hull (null: nothing laid down), the stages done, and what's been put in towards the
    // stage now under way. Done stages' costs are counted in Done (what taking it down gives back).
    public sealed class Build
    {
        public Hull Hull; public int Stage; public Dictionary<string, int> Paid = new Dictionary<string, int>();
        public bool Empty => Hull == null;
        public bool Finished => Hull != null && Stage >= Hull.Stages.Length;
        public Stage Current => Hull != null && Stage < Hull.Stages.Length ? Hull.Stages[Stage] : null;

        // What the stage under way still needs, item by item (only those still short).
        public List<(string item, int n)> Needs()
        {
            if (Current == null) return new List<(string, int)>();
            return Current.Cost.Select(c => (c.item, c.n - Had(c.item))).Where(c => c.Item2 > 0).ToList();
        }
        public int Had(string item) => Paid.TryGetValue(item, out int n) ? n : 0;

        // Putting in: of what's offered (item, how many the giver has), how many of each it takes (no more than the stage still needs).
        // The stage is done and the next begun when the last of it is in; what's offered past that is not taken.
        public List<(string item, int n)> Take(IEnumerable<(string item, int have)> offered)
        {
            var taken = new List<(string, int)>();
            if (Current == null) return taken;
            foreach (var (item, have) in offered)
            {
                int want = Needs().Where(x => x.item == item).Sum(x => x.n), n = Math.Min(want, Math.Max(0, have));
                if (n <= 0) continue;
                Paid[item] = Had(item) + n; taken.Add((item, n));
            }
            if (Needs().Count == 0) { Stage++; Paid.Clear(); }
            return taken;
        }

        // What it all comes back to, taken down or given up: every stage done, and what's in towards the one under way.
        public Dictionary<string, int> Refund()
        {
            var r = new Dictionary<string, int>();
            if (Hull == null) return r;
            for (int i = 0; i < Math.Min(Stage, Hull.Stages.Length); i++) foreach (var (item, n) in Hull.Stages[i].Cost) r[item] = (r.TryGetValue(item, out int had) ? had : 0) + n;
            foreach (var kv in Paid) r[kv.Key] = (r.TryGetValue(kv.Key, out int had) ? had : 0) + kv.Value;
            return r;
        }

        // As the ZDO keeps it: "hull|stage|item:n,item:n" ("" for nothing laid down).
        public string Save() => Hull == null ? "" : $"{Hull.Name}|{Stage}|{string.Join(",", Paid.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"))}";
        public static Build Load(string s)
        {
            var b = new Build();
            if (string.IsNullOrEmpty(s)) return b;
            string[] f = s.Split('|');
            b.Hull = Hulls.Of(f[0]);
            if (b.Hull == null) return new Build();
            b.Stage = f.Length > 1 && int.TryParse(f[1], out int st) ? Math.Max(0, Math.Min(st, b.Hull.Stages.Length)) : 0;
            if (f.Length > 2)
                foreach (string kv in f[2].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int c = kv.LastIndexOf(':');
                    if (c > 0 && int.TryParse(kv.Substring(c + 1), out int n) && n > 0) b.Paid[kv.Substring(0, c)] = n;
                }
            return b;
        }
    }

    public static class Launching
    {
        // Where a finished boat goes in: along the slipway's length (its bow end first), the first stretch with water deep enough under
        // the keel all along the hull, within reach. depthAt(metres along) gives the water's depth there (<= 0: dry). The metres along
        // to the hull's middle; null if there's no such water.
        public const float Reach = 24f, Step = 0.5f;
        public static float? Spot(Hull h, Func<float, float> depthAt)
        {
            for (float d = 0; d + h.Length <= Reach; d += Step)
            {
                bool deep = true;
                for (float m = d; m <= d + h.Length + 0.001f && deep; m += Step) deep = depthAt(m) >= h.Draught;
                if (deep) return d + h.Length / 2;
            }
            return null;
        }
    }
}
