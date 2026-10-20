using System;
using System.Collections.Generic;
using System.Linq;

namespace Shipyard
{
    // What can be built on a slipway, as data: each hull in its stages (keel, ribs, strakes, fittings...), each stage with what it
    // costs. A stage is paid IN FULL before the next is begun; what's put in is kept on the slipway (never lost: taken down, it all
    // comes back), and any number of players can put in.
    public sealed class Stage { public string Name, Model, Title; public (string item, int n)[] Cost; public bool Rigged; } // (Rigged: its sail; Sailcloth)
    public sealed class Hull
    {
        public string Name, Title, Prefab; public Stage[] Stages;
        public float Draught, Length; // (how deep the water under it must be to launch it, all along its length)
        public float KeelDepth; // (the keel's bottom below the hull's origin, its waterline: how high it stands on the stocks)
        public string Cradle; // (the blocks and shores it stands on, on the slipway only; null: the slipway's own blocks do)
        public int CargoWidth, CargoHeight; // (its hold, in slots; 0: none)
        public string Yard, Sail; public float MastZ; // (a sailing hull's yard with the sail furled on it, and the sail set: models turned with the
                                                     // wind about the mast's axis, MastZ along the hull; null: rowed)
    }

    public static class Hulls
    {
        public const string Faering = "faering", Knarr = "knarr";
        public static readonly Hull[] All =
        {
            new Hull
            {
                Name = Faering, Title = "Færing", Prefab = Names.Faering, Draught = 0.6f, Length = 5.4f, KeelDepth = 0.45f,
                Stages = new[]
                {
                    new Stage { Name = "keel", Title = "Keel and stems", Model = "faering_keel", Cost = new[] { ("Wood", 10), ("Resin", 2) } },
                    new Stage { Name = "ribs", Title = "Ribs", Model = "faering_ribs", Cost = new[] { ("Wood", 12) } },
                    new Stage { Name = "strakes", Title = "Strakes", Model = "faering_strakes", Cost = new[] { ("Wood", 20), ("Resin", 8), ("LeatherScraps", 4) } },
                    new Stage { Name = "fit", Title = "Thwarts and oars", Model = "faering_fit", Cost = new[] { ("Wood", 8), ("LeatherScraps", 4) } },
                },
            },
            // The knarr: a broad, deep cargo ship (Skuldelev 1), with one big square sail. Bronze-nailed, so after the karve; the longship
            // (iron) stays the faster ship, the knarr the one that carries.
            new Hull
            {
                Name = Knarr, Title = "Knarr", Prefab = Names.Knarr, Draught = 1.4f, Length = 16f, KeelDepth = 1.1f, Cradle = "knarr_cradle", Yard = "knarr_yard", Sail = "knarr_sail", MastZ = 0.6f,
                CargoWidth = 8, CargoHeight = 6,
                Stages = new[]
                {
                    new Stage { Name = "keel", Title = "Keel and stems", Model = "knarr_keel", Cost = new[] { ("FineWood", 10), ("Resin", 6) } },
                    new Stage { Name = "ribs", Title = "Ribs and beams", Model = "knarr_ribs", Cost = new[] { ("FineWood", 12), ("BronzeNails", 20) } },
                    new Stage { Name = "strakes", Title = "Strakes", Model = "knarr_strakes", Cost = new[] { ("FineWood", 14), ("BronzeNails", 80), ("Resin", 16) } },
                    new Stage { Name = "deck", Title = "Decks and hold", Model = "knarr_deck", Cost = new[] { ("Wood", 30), ("BronzeNails", 20) } },
                    new Stage { Name = "mast", Title = "Mast, sail and rigging", Model = "knarr_mast", Cost = new[] { ("FineWood", 4), ("DeerHide", 15), ("LeatherScraps", 10) }, Rigged = true },
                },
            },
        };
        public static Hull Of(string name) => All.FirstOrDefault(h => h.Name == name);
        // Everything a hull costs, all its stages together.
        public static Dictionary<string, int> Total(Hull h)
        {
            var t = new Dictionary<string, int>();
            foreach (Stage s in h.Stages) foreach (var (item, n) in Sailcloth.CostOf(s)) t[item] = (t.TryGetValue(item, out int had) ? had : 0) + n;
            return t;
        }
    }

    // A sail of cloth: with Homespun in, a rigged stage takes one of its wool sails (of any colour) in place of the hides stitched into
    // one, and the boat sails under that sail's colour. Without it, hides as ever. (Set from Homespun.Api at load: Sails.cs.)
    public static class Sailcloth
    {
        public const string Hide = "DeerHide";
        public static string Sail;                    // (Homespun's undyed sail; null: Homespun isn't in)
        public static Func<string, string> ColourOf;  // (a sail's colour: "" undyed, "Red"...; null: not a sail)
        public static string[] Variants = new string[0]; // (the undyed sail and every colour of it)
        public static string Colour(string prefab) => Sail == null || ColourOf == null || prefab == null ? null : ColourOf(prefab);
        public static string Canon(string prefab) => Colour(prefab) != null ? Sail : prefab;
        public static string Of(string colour) => colour == null ? null : Variants.FirstOrDefault(v => Colour(v) == colour) ?? Sail;
        public static (string item, int n)[] CostOf(Stage s) => Sail != null && s.Rigged ? s.Cost.Select(c => c.item == Hide ? (Sail, 1) : c).ToArray() : s.Cost;
        // The colour a sail's stripes take (the baked sail's are madder red, on white): undyed wool, brown; dyed, the dye's (rgb gives it);
        // null: as baked.
        public static readonly (float r, float g, float b) Undyed = (0.45f, 0.38f, 0.3f);
        public static (float r, float g, float b)? Stripe(string colour, Func<string, (float r, float g, float b)?> rgb) => colour == null ? null : colour == "" ? Undyed : rgb?.Invoke(colour);
    }

    // A slipway's build, as it's kept in its ZDO: the hull (null: nothing laid down), the stages done, and what's been put in towards the
    // stage now under way. Done stages' costs are counted in Done (what taking it down gives back).
    public sealed class Build
    {
        public Hull Hull; public int Stage; public Dictionary<string, int> Paid = new Dictionary<string, int>();
        public string Sail; // (the colour of the sail put in: "" undyed; null: none, or hides)
        public bool Empty => Hull == null;
        public bool Finished => Hull != null && Stage >= Hull.Stages.Length;
        public Stage Current => Hull != null && Stage < Hull.Stages.Length ? Hull.Stages[Stage] : null;

        // What the stage under way still needs, item by item (only those still short).
        public List<(string item, int n)> Needs()
        {
            if (Current == null) return new List<(string, int)>();
            return Sailcloth.CostOf(Current).Select(c => (c.item, c.n - Had(c.item))).Where(c => c.Item2 > 0).ToList();
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
                string canon = Sailcloth.Canon(item); // (a sail of any colour is the sail)
                int want = Needs().Where(x => x.item == canon).Sum(x => x.n), n = Math.Min(want, Math.Max(0, have));
                if (n <= 0) continue;
                Paid[canon] = Had(canon) + n; taken.Add((item, n));
                if (Sailcloth.Colour(item) is string colour) Sail = colour;
            }
            if (Needs().Count == 0) { Stage++; Paid.Clear(); }
            return taken;
        }

        // What it all comes back to, taken down or given up: every stage done, and what's in towards the one under way.
        public Dictionary<string, int> Refund()
        {
            var r = new Dictionary<string, int>();
            if (Hull == null) return r;
            for (int i = 0; i < Math.Min(Stage, Hull.Stages.Length); i++) foreach (var (item, n) in Sailcloth.CostOf(Hull.Stages[i])) r[item] = (r.TryGetValue(item, out int had) ? had : 0) + n;
            foreach (var kv in Paid) r[kv.Key] = (r.TryGetValue(kv.Key, out int had) ? had : 0) + kv.Value;
            // (the sail comes back the colour it went in)
            if (Sail != null && Sailcloth.Sail != null && r.TryGetValue(Sailcloth.Sail, out int sails) && Sailcloth.Of(Sail) is string dyed && dyed != Sailcloth.Sail) { r.Remove(Sailcloth.Sail); r[dyed] = sails; }
            return r;
        }

        // As the ZDO keeps it: "hull|stage|item:n,item:n[|sail colour]" ("" for nothing laid down).
        public string Save() => Hull == null ? "" : $"{Hull.Name}|{Stage}|{string.Join(",", Paid.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}:{kv.Value}"))}" + (Sail != null ? "|" + Sail : "");
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
            if (f.Length > 3) b.Sail = f[3];
            return b;
        }
    }

    public static class Launching
    {
        // Where a finished boat goes in: along the slipway's length (its bow end first), the first stretch with water deep enough under
        // the keel all along the hull, within reach. depthAt(metres along) gives the water's depth there (<= 0: dry). The metres along
        // to the hull's middle; null if there's no such water.
        public const float Step = 0.5f;
        public static float Reach(Hull h) => 18f + h.Length; // (past the slipway's end, and room for the whole hull)
        public static float? Spot(Hull h, Func<float, float> depthAt)
        {
            for (float d = 0; d + h.Length <= Reach(h); d += Step)
            {
                bool deep = true;
                for (float m = d; m <= d + h.Length + 0.001f && deep; m += Step) deep = depthAt(m) >= h.Draught;
                if (deep) return d + h.Length / 2;
            }
            return null;
        }
    }

    // How a hull sails. The game's sail force grows with the wind's strength (a quarter of it in a calm, all of it in a gale); a hull's
    // own curve multiplies that. The knarr's big, heavy sail barely draws in light airs and comes into its own in strong wind.
    public static class Sailing
    {
        public const float KnarrForceFactor = 0.05f; // (the longship's sail force; the curve below scales it)
        public static float WindFactor(Hull h, float windIntensity)
        {
            if (h == null || h.Name != Hulls.Knarr) return 1f;
            float w = Math.Max(0f, Math.Min(1f, windIntensity));
            float t = Math.Max(0f, Math.Min(1f, (w - 0.25f) / 0.6f)); t = t * t * (3 - 2 * t); // (smoothstep 0.25..0.85)
            return 0.6f + 0.65f * t;
        }
    }
}
