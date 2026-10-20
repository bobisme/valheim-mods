using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // The boatyard: a zone marked out over the shore where the colony's shipwright builds the ships the jarl orders (the Work tab).
    // The order lays the game's own ship as a BuildOrders plan at the water's edge, its bow to open water; the shipwright fetches
    // its makings from the stores and works on it a long while, and it's launched there. Trade runs go by karve or longship once
    // one lies at the colony's shore. One ship at a time.
    internal static class Shipyard
    {
        // (Hull: one of the Shipyard mod's, built on its slipway stage by stage, not laid out with BuildOrders; Blows: a stage's, then)
        public sealed class Kind { public string Prefab, Name, Hull; public float Draught; public int Blows; }
        private static readonly Kind[] Game =
        {
            new Kind { Prefab = "Raft", Name = "raft", Draught = 0.6f, Blows = 12 },
            new Kind { Prefab = "Karve", Name = "karve", Draught = 1.0f, Blows = 40 },
            new Kind { Prefab = "VikingShip", Name = "longship", Draught = 1.6f, Blows = 80 },
        };
        private static Kind[] _withSlipways; private static float _kindsAt = -1;
        public static Kind[] Kinds
        {
            get
            {
                if (_withSlipways != null && Time.time < _kindsAt) return _withSlipways;
                _kindsAt = Time.time + 10;
                var hulls = Compat.ShipyardOn ? Compat.ShipyardHulls : new string[0];
                _withSlipways = Game.Concat(hulls.Select(h => new Kind { Prefab = Compat.ShipyardPrefab(h), Name = (Compat.ShipyardTitle(h) ?? h).ToLower(), Hull = h, Blows = h == "knarr" ? 30 : 12 })
                    .Where(k => k.Prefab != null)).ToArray();
                return _withSlipways;
            }
        }
        public static bool IsShip(string prefab) => Game.Any(k => k.Prefab == prefab); // (the game's, laid out as plans; a slipway's isn't a plan)
        public static Kind Of(string prefab) => Kinds.FirstOrDefault(k => k.Prefab == prefab);
        public static Kind OfHull(string hull) => Kinds.FirstOrDefault(k => k.Hull == hull);
        public static string Plan(Colony c, Kind k) => $"Ship: {k.Name} for {c.R.Name}";

        public static List<Zone> Yards(Colony c) => c.R.Zones.Where(z => z.Type == ZoneType.Boatyard).ToList();
        // The Shipyard mod's slipways at the colony's boatyards (in one, or just by it on the shore).
        public static List<Vector3> Slipways(Colony c)
        {
            if (!Compat.ShipyardOn) return new List<Vector3>();
            return Yards(c).SelectMany(y => Compat.Slipways(Zones.Centre(y), YardReach(y))).Distinct().ToList();
        }
        private static float YardReach(Zone y)
        {
            Vector3 c = Zones.Centre(y); float r = 0;
            for (int i = 0; i + 1 < y.Xz.Count; i += 2) r = Mathf.Max(r, Utils.DistanceXZ(c, new Vector3(y.Xz[i], 0, y.Xz[i + 1])));
            return r + 12f; // (just by it on the shore counts)
        }
        public const string SlipwayId = "slipway";
        // The ship being built (its plan's id and prefab), if there is one: a ship's ghost in or near the colony, or a hull on a slipway at
        // the boatyard (its id "slipway", at the slipway).
        public static (string id, string prefab, Vector3 at)? Building(Colony c)
        {
            foreach (Vector3 sw in Slipways(c))
                if (Compat.SlipwayHull(sw) is string hull && OfHull(hull) is Kind k) return (SlipwayId, k.Prefab, sw);
            foreach (string g in Compat.NextGhosts(c.Position, c.Radius + 40f, 600))
            {
                string[] f = g.Split('|');
                if (f.Length >= 5 && IsShip(f[1])) return (f[0], f[1], new Vector3(F(f[2]), F(f[3]), F(f[4])));
            }
            return null;
        }
        private static float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        // Where a ship of that kind is laid at a boatyard: the point in or just off it with water deep enough under its keel, nearest
        // the shore; bow to open water.
        public static bool Launch(Colony c, Zone yard, Kind k, out Vector3 at, out Quaternion rot)
        {
            float sea = ZoneSystem.instance.m_waterLevel;
            Vector3 centre = Zones.Centre(yard); at = centre; rot = Quaternion.identity;
            Vector3 best = Vector3.zero; float bestD = float.MaxValue;
            var pts = Geometry.Grid(yard.Xz, 1.5f).Select(p => new Vector3(p.Item1, 0, p.Item2)).ToList();
            // (and a little out past the yard's water side: the deep water may lie just beyond what was marked)
            Vector3 outward = ShipBurial.Out(centre, c.Position);
            foreach (Vector3 p in pts.ToList()) for (float r = 2f; r <= 12f; r += 2f) pts.Add(p + outward * r);
            foreach (Vector3 p in pts)
            {
                if (sea - ZoneSystem.instance.GetGroundHeight(p) < k.Draught) continue;
                // (nearest the yard's dry ground: the shipwright works from the shore)
                float d = Utils.DistanceXZ(p, centre);
                if (d < bestD) { bestD = d; best = p; }
            }
            if (bestD == float.MaxValue) return false;
            Vector3 dir = ShipBurial.Out(best, c.Position);
            at = new Vector3(best.x, sea, best.z);
            rot = Quaternion.LookRotation(dir); // (the game's ships go bow first along +z)
            return true;
        }
        // Why that ship can't be ordered now ("" if it can).
        public static string WhyNot(Colony c, Kind k)
        {
            Player me = Player.m_localPlayer;
            if (me == null) return "no player";
            if (k.Hull != null)
            {
                if (Building(c) is var on && on.HasValue) return $"a {Of(on.Value.prefab)?.Name ?? "ship"} is being built already";
                if (Yards(c).Count == 0) return "no boatyard: mark one out over the shore (\"Mark rooms and zones\", Boatyard)";
                if (Slipways(c).Count == 0) return "no slipway at the boatyard: build one there (hammer, Misc), its bow end to the water";
                if (!c.R.Living.Any(r => r.Prof == Prof.Shipwright)) return "no one is a shipwright: give someone the trade (People tab)";
                return "";
            }
            if (Compat.Find("com.quad.buildorders", "com.dhack.buildorders") == null) return "it needs Quad's BuildOrders mod, which lays out the plan";
            if (Building(c) is var b && b.HasValue) return $"a {Of(b.Value.prefab)?.Name ?? "ship"} is being built already";
            var yards = Yards(c);
            if (yards.Count == 0) return "no boatyard: mark one out over the shore (\"Mark rooms and zones\", Boatyard)";
            Piece piece = Assets.Find(k.Prefab)?.GetComponent<Piece>();
            if (piece == null) return "no such ship";
            if (!me.IsRecipeKnown(piece.m_name)) return $"you don't know how to build a {k.Name} yet";
            if (!yards.Any(y => Launch(c, y, k, out _, out _))) return $"the boatyard's water isn't deep enough for a {k.Name} (mark it further out over the water)";
            Zone yard = yards.First(y => Launch(c, y, k, out _, out _));
            Launch(c, yard, k, out Vector3 at, out _);
            if (piece.m_craftingStation != null && CraftingStation.HaveBuildStationInRange(piece.m_craftingStation.m_name, at) == null)
                return $"a {Localization.instance.Localize(piece.m_craftingStation.m_name).ToLower()} must stand by the boatyard, as for any ship";
            if (!c.R.Living.Any(r => r.Prof == Prof.Shipwright)) return "no one is a shipwright: give someone the trade (People tab)";
            return "";
        }
        public static string Cost(Kind k) => k?.Hull != null ? SlipwayCost(k) : string.Join(", ", (Assets.Find(k?.Prefab)?.GetComponent<Piece>()?.m_resources ?? new Piece.Requirement[0])
            .Where(r => r.m_resItem != null && r.m_amount > 0).Select(r => $"{r.m_amount} {Localization.instance.Localize(r.m_resItem.m_itemData.m_shared.m_name).ToLower()}"));
        // A slipway hull's cost, all its stages together.
        private static string SlipwayCost(Kind k)
        {
            var total = new Dictionary<string, int>();
            foreach (string stage in Compat.ShipyardStageCosts(k.Hull))
                foreach (string kv in stage.Split(','))
                {
                    int at = kv.LastIndexOf(':');
                    if (at > 0 && int.TryParse(kv.Substring(at + 1), out int n)) total[kv.Substring(0, at)] = (total.TryGetValue(kv.Substring(0, at), out int had) ? had : 0) + n;
                }
            return string.Join(", ", total.Select(kv => $"{kv.Value} {Localization.instance.Localize(Assets.Find(kv.Key)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name ?? kv.Key).ToLower()}")) + " over its stages";
        }
        // On the jarl's game (BuildOrders lays plans for the player beside them): the plan at the boatyard; or a keel laid on a slipway there.
        public static bool Order(Colony c, Kind k)
        {
            Player me = Player.m_localPlayer;
            string why = WhyNot(c, k);
            if (why != "") { me?.Message(MessageHud.MessageType.Center, $"No {k.Name} yet: {why}."); return false; }
            if (k.Hull != null)
            {
                Vector3 sw = Slipways(c).OrderBy(x => Vector3.Distance(x, c.Position)).First();
                if (!Compat.SlipwayStart(sw, k.Hull)) { me?.Message(MessageHud.MessageType.Center, $"No {k.Name} yet: the slipway has a build on it already."); return false; }
                me?.Message(MessageHud.MessageType.Center, $"A {k.Name}'s keel is laid on the slipway: the shipwright builds her stage by stage from the stores ({Cost(k)}).");
                return true;
            }
            Zone yard = Yards(c).First(y => Launch(c, y, k, out _, out _));
            Launch(c, yard, k, out Vector3 at, out Quaternion rot);
            object bo = Compat.Find("com.quad.buildorders", "com.dhack.buildorders");
            var method = bo?.GetType().GetMethod("TryCreateBuildingShell");
            if (method == null) { me.Message(MessageHud.MessageType.Center, "BuildOrders can't lay out a plan (too old a version?)."); return false; }
            object[] args = { me, Plan(c, k), new[] { k.Prefab }, new[] { at }, new[] { rot }, null, null };
            bool ok;
            try { ok = (bool)method.Invoke(bo, args); } catch (System.Exception e) { ok = false; args[6] = e.InnerException?.Message ?? e.Message; }
            if (!ok) { me.Message(MessageHud.MessageType.Center, $"The plan couldn't be laid out: {args[6]}"); return false; }
            me.Message(MessageHud.MessageType.Center, $"A {k.Name} is planned at the boatyard: the shipwright builds it from the stores ({Cost(k)}).");
            return true;
        }
        // From the shipwright's game once it's launched.
        public static void Launched(Brain b, Kind k, Vector3 at)
        {
            Colony c = b.Colony;
            c?.Log("text", b.S.Name, "", $"{b.S.Name} built a {k.Name} at the boatyard, and it's afloat.");
            c?.Tell($"{b.S.Name} launched the colony's new {k.Name}!", true);
            b.S.Bump("ships"); b.Dirty();
            foreach (Brain m in Villagers.All.Where(m => m.S.Colony == b.S.Colony && Vector3.Distance(m.Pos, at) < 40f))
            {
                Body_.Anim(m.Body)?.SetTrigger("emote_cheer");
                if (m != b && Random.value < 0.4f) m.Say(new[] { "Look at her ride the water!", "A fine keel. Odin, bless her.", $"Our own {k.Name}! Where to first?", "She floats! Hah!" }[Random.Range(0, 4)], 5);
            }
            b.Say(new[] { $"There. She's sound, every plank.", $"Done. Treat her well, jarl.", $"A {k.Name} of ours. Njord, take care of her." }[Random.Range(0, 3)], 6);
        }
    }
}
