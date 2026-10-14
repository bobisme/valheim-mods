using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // Great Works in the world (Core/GreatWorks.cs): the plan laid out as BuildOrders' ghosts by the jarl who shows where it stands
    // (their own game: BuildOrders makes plans for the player beside them), the colony's owner counting what's built, and the work's
    // gifts once it's done.
    internal static class GreatWork
    {
        public const float Steep = 1.0f; // (most the ground may rise or fall across its footing)
        private static float _next;

        // A shore within the colony: open water near enough for a lighthouse to light (null if none).
        public static Vector3? Shore(Colony c)
        {
            if (ZoneSystem.instance == null) return null;
            float sea = ZoneSystem.instance.m_waterLevel;
            for (float r = 15f; r <= c.Radius + 20f; r += 10f)
                for (int i = 0; i < 16; i++)
                {
                    float a = i * Mathf.PI / 8f;
                    Vector3 p = c.Position + new Vector3(Mathf.Sin(a) * r, 0, Mathf.Cos(a) * r);
                    if (WorldGenerator.instance != null && WorldGenerator.instance.GetHeight(p.x, p.z) < sea - 2f) return p;
                }
            return null;
        }

        // ---- the jarl's game: an outline drawn with the Mark tool, chosen as the great work's site (Ui/RoomPicker) ----
        // Why it can't stand at that spot ("" if it can): inside the colony, on ground flat enough, near the water.
        public static string Why(Colony c, string work, Player me, Vector3 spot, out Vector3 at, out float yaw)
        {
            yaw = 0; at = spot;
            if (me == null) return "no player";
            if (Compat.Find("com.quad.buildorders", "com.dhack.buildorders") == null) return "it needs Quad's BuildOrders mod, which lays out its plan for the builders";
            if (Utils.DistanceXZ(at, c.Position) > c.Radius - 6f) return "it must stand inside the colony, where builders work";
            if (!ZoneSystem.instance.GetSolidHeight(at, out float ground)) return "there's no ground there";
            float sea = ZoneSystem.instance.m_waterLevel;
            if (ground < sea + 0.3f) return "that's in the water: step back onto dry ground";
            float lo = ground, hi = ground;
            for (int i = 0; i < 8; i++) { float a = i * Mathf.PI / 4f; if (ZoneSystem.instance.GetSolidHeight(at + new Vector3(Mathf.Sin(a) * 4f, 0, Mathf.Cos(a) * 4f), out float h)) { lo = Mathf.Min(lo, h); hi = Mathf.Max(hi, h); } }
            if (hi - lo > Steep) return $"the ground's too steep there ({hi - lo:0.0} m across its footing): level it with the hoe, or find flatter ground";
            if (work == GreatWorks.Lighthouse)
            {
                bool water = false;
                for (int i = 0; i < 16 && !water; i++) { float a = i * Mathf.PI / 8f; for (float r = 8f; r <= 40f && !water; r += 4f) water = WorldGenerator.instance.GetHeight(at.x + Mathf.Sin(a) * r, at.z + Mathf.Cos(a) * r) < sea - 1f; }
                if (!water) return "a lighthouse wants the sea within 40 m";
            }
            at.y = ground;
            Vector3 f = c.Position - at; f.y = 0; // (its door toward the colony)
            yaw = f.sqrMagnitude > 0.01f ? Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg : 0f;
            return "";
        }
        // Not begun yet (planned, nothing of it built): its site can still move.
        public static bool Movable(Colony c, string work) => GreatWorks.Stage(c.R.Flags, work) == GreatWorks.Laid && c.R.Flag(GreatWorks.BuiltKey(work)) == 0;
        public static bool Choosable(Colony c, string work) => GreatWorks.Stage(c.R.Flags, work) == GreatWorks.Promised || Movable(c, work) || Fallen(c, work);
        // Finished once, and since taken down (less than a quarter of it standing, where it's loaded here): it can be raised again.
        public static bool Fallen(Colony c, string work)
        {
            if (GreatWorks.Stage(c.R.Flags, work) != GreatWorks.Done) return false;
            int built = Built(c, work);
            return built >= 0 && built < GreatWorks.Parts(work).Count / 4;
        }
        // Its plan taken away (each plan BuildOrders keeps under its name: whoever laid it): how many ghosts went.
        public static int Plans(Colony c, string work) => PlanKeys(c, work).Count;
        // (test) BuildOrders' own reckoning of how its plan would stand: each ghost of it as "prefab height support/min", weakest first.
        public static List<string> Stability(Colony c, string work, Vector3? near = null)
        {
            var bf = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
            object bo = Compat.Find("com.quad.buildorders", "com.dhack.buildorders");
            var orders = bo?.GetType().GetField("_orders", bf)?.GetValue(bo) as System.Collections.IDictionary;
            var stab = bo?.GetType().GetField("_stability", bf)?.GetValue(bo) as System.Collections.IDictionary;
            var compute = bo?.GetType().GetMethod("ComputeStability", bf);
            if (orders == null || stab == null || compute == null) return new List<string> { "no BuildOrders" };
            compute.Invoke(bo, new object[] { Player.m_localPlayer, null });
            var keys = PlanKeys(c, work); Vector3 at = near ?? At(c, work, out _);
            var rows = new List<(float s, string line)>();
            foreach (System.Collections.DictionaryEntry e in orders)
            {
                object o = e.Value; var t = o.GetType();
                if (!keys.Contains(t.GetField("By").GetValue(o) as string)) continue;
                object st = stab.Contains(e.Key) ? stab[e.Key] : null;
                if (st == null) continue;
                var stt = st.GetType(); float sup = (float)stt.GetField("Support").GetValue(st), min = (float)stt.GetField("Min").GetValue(st);
                Vector3 pos = (Vector3)t.GetField("Pos").GetValue(o);
                if (Utils.DistanceXZ(pos, at) > 8f) continue;
                rows.Add((sup - min, $"{t.GetField("Prefab").GetValue(o)} {pos.y - at.y:0.0} {sup:0}/{min:0}"));
            }
            return rows.OrderBy(r => r.s).Select(r => r.line).ToList();
        }
        private static HashSet<string> PlanKeys(Colony c, string work)
        {
            object bo = Compat.Find("com.quad.buildorders", "com.dhack.buildorders");
            var orders = bo?.GetType().GetField("_orders", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(bo) as System.Collections.IDictionary;
            var keys = new HashSet<string>();
            if (orders == null) return keys;
            string head = "Blueprint: Addon " + GreatWorks.Proper(work, c.R.Name) + " [";
            foreach (object o in orders.Values) { string by = o?.GetType().GetField("By")?.GetValue(o) as string; if (by != null && by.StartsWith(head)) keys.Add(by); }
            return keys;
        }
        public static int Unplan(Colony c, string work)
        {
            object bo = Compat.Find("com.quad.buildorders", "com.dhack.buildorders");
            var remove = bo?.GetType().GetMethod("TryRemoveGhostPlan");
            if (remove == null) return 0;
            var keys = PlanKeys(c, work);
            int n = 0;
            foreach (string key in keys) { object[] args = { Player.m_localPlayer, key, 0, null }; try { if ((bool)remove.Invoke(bo, args)) n += (int)args[2]; } catch { } }
            return n;
        }
        // The site from an outline: planned in its middle, or why not (said to the player). True if it's planned.
        public static bool RaiseIn(Colony c, string work, IList<float> xz)
        {
            Player me = Player.m_localPlayer;
            string site = GreatWorks.Site(xz, out float x, out float z);
            if (site != "") { me?.Message(MessageHud.MessageType.Center, $"Not here: {site}."); return false; }
            Vector3 spot = new Vector3(x, 0, z);
            ZoneSystem.instance.GetSolidHeight(spot, out float gy); spot.y = gy;
            if (Movable(c, work))
            {
                string why = Why(c, work, me, spot, out _, out _);
                if (why != "") { me?.Message(MessageHud.MessageType.Center, $"Not here: {why}."); return false; }
                Unplan(c, work); // (moved: the old plan goes, the new one is laid)
            }
            return RaiseHere(c, work, spot);
        }
        // A finished work its design has grown since (the Lighthouse's fire deck): what's missing laid out as a plan of its own, for
        // the builders. How many parts, or -1 if it couldn't be.
        public static int Amend(Colony c, string work, string only = null)
        {
            Player me = Player.m_localPlayer;
            int stage = GreatWorks.Stage(c.R.Flags, work);
            if (me == null || stage != GreatWorks.Done && stage != GreatWorks.Laid) return -1;
            if (stage == GreatWorks.Laid) Unplan(c, work); // (being raised: its plan laid again, as the design is now)
            Vector3 at = At(c, work, out float yaw);
            var near = new List<Piece>(); Piece.GetAllPiecesInRadius(at + Vector3.up * 12f, 22f, near);
            var missing = GreatWorks.Parts(work).Where(p => only == null || p.Prefab == only).Where(p => { var w = GreatWorks.Place(p, at.x, at.y, at.z, yaw); Vector3 spot = new Vector3(w.x, w.y, w.z);
                return !near.Any(x => x != null && Utils.GetPrefabName(x.gameObject) == p.Prefab && Vector3.Distance(x.transform.position, spot) < 0.5f); }).ToList();
            if (missing.Count == 0) return 0;
            object bo = Compat.Find("com.quad.buildorders", "com.dhack.buildorders");
            var method = bo?.GetType().GetMethod("TryCreateBuildingShell");
            if (method == null) return -1;
            var placed = missing.Select(p => GreatWorks.Place(p, at.x, at.y, at.z, yaw)).ToList();
            object[] args = { me, stage == GreatWorks.Done ? GreatWorks.Proper(work, c.R.Name) + " (finished)" : GreatWorks.Proper(work, c.R.Name), missing.Select(p => p.Prefab).ToArray(), placed.Select(p => new Vector3(p.x, p.y, p.z)).ToArray(), missing.Select(p => Turn(p, yaw)).ToArray(), null, null };
            try { if (!(bool)method.Invoke(bo, args)) { me.Message(MessageHud.MessageType.Center, $"The plan couldn't be laid out: {args[6]}"); return -1; } } catch { return -1; }
            return missing.Count;
        }
        public static bool RaiseHere(Colony c, string work, Vector3 spot)
        {
            Player me = Player.m_localPlayer;
            string why = Why(c, work, me, spot, out Vector3 at, out float yaw);
            if (why != "") { me?.Message(MessageHud.MessageType.Center, $"Not here: {why}."); return false; }
            var parts = GreatWorks.Parts(work);
            var placed = parts.Select(p => GreatWorks.Place(p, at.x, at.y, at.z, yaw)).ToList();
            object bo = Compat.Find("com.quad.buildorders", "com.dhack.buildorders");
            var method = bo?.GetType().GetMethod("TryCreateBuildingShell");
            if (method == null) { me.Message(MessageHud.MessageType.Center, "BuildOrders can't lay out a plan (too old a version?)."); return false; }
            object[] args = { me, GreatWorks.Proper(work, c.R.Name), parts.Select(p => p.Prefab).ToArray(),
                placed.Select(p => new Vector3(p.x, p.y, p.z)).ToArray(), parts.Select(p => Turn(p, yaw)).ToArray(), null, null };
            bool ok;
            try { ok = (bool)method.Invoke(bo, args); } catch (System.Exception e) { ok = false; args[6] = e.InnerException?.Message ?? e.Message; }
            if (!ok) { me.Message(MessageHud.MessageType.Center, $"The plan couldn't be laid out: {args[6]}"); return false; }
            Net.Act(c, "greatwork", $"{work}|{at.x.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{at.y.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{at.z.ToString(System.Globalization.CultureInfo.InvariantCulture)}|{yaw.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            me.Message(MessageHud.MessageType.Center, $"{GreatWorks.Title(work)} is planned here: the builders raise it from the stores ({GreatWorks.NeedsText(work)}).");
            return true;
        }

        // A part's turn in the world: the work's own turn, then the part's (a leaning log along its line).
        public static Quaternion Turn(GreatWorks.Part p, float yaw) =>
            Quaternion.Euler(0, yaw, 0) * (p.Leans ? Quaternion.FromToRotation(Vector3.up, new Vector3(p.UpX, p.UpY, p.UpZ)) : Quaternion.Euler(0, p.Yaw, 0));

        // ---- the colony's owner ----
        public static void Laid(Colony c, string who, string arg)
        {
            string[] f = arg.Split('|');
            if (f.Length < 5 || !Choosable(c, f[0])) return;
            float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
            string work = f[0];
            bool moved = GreatWorks.Stage(c.R.Flags, work) == GreatWorks.Laid;
            if (GreatWorks.Stage(c.R.Flags, work) == GreatWorks.Done) c.R.Flags[GreatWorks.AgainKey(work)] = 1;
            c.R.Flags[GreatWorks.Key(work)] = GreatWorks.Laid;
            c.R.Flags[GreatWorks.AtKey(work)] = TrueLegend.Pack(F(f[1]), F(f[3]));
            c.R.Flags[GreatWorks.YKey(work)] = Mathf.RoundToInt(F(f[2]) * 10f);
            c.R.Flags[GreatWorks.XKey(work)] = Mathf.RoundToInt(F(f[1]) * 10f); c.R.Flags[GreatWorks.ZKey(work)] = Mathf.RoundToInt(F(f[3]) * 10f);
            c.R.Flags[GreatWorks.YawKey(work)] = Mathf.RoundToInt(F(f[4]));
            c.R.Flags[GreatWorks.BuiltKey(work)] = 0;
            c.Log("text", who, "", moved ? $"{who} moved {GreatWorks.Name(work)} to a new site before a stick of it was raised." : c.R.Flag(GreatWorks.AgainKey(work)) != 0 ? $"{who} set the builders to raise {GreatWorks.Name(work)} again." : $"{who} chose where {GreatWorks.Name(work)} will stand, and the builders began.");
            c.MarkDirty();
        }
        public static Vector3 At(Colony c, string work, out float yaw)
        {
            var (x, z) = TrueLegend.Unpack(c.R.Flag(GreatWorks.AtKey(work)));
            if (c.R.Flags.ContainsKey(GreatWorks.XKey(work))) { x = c.R.Flag(GreatWorks.XKey(work)) / 10f; z = c.R.Flag(GreatWorks.ZKey(work)) / 10f; }
            yaw = c.R.Flag(GreatWorks.YawKey(work));
            return new Vector3(x, c.R.Flag(GreatWorks.YKey(work)) / 10f, z);
        }
        // How many of its parts stand (pieces of the right kind where the plan has them); -1 where it isn't loaded here.
        public static int Built(Colony c, string work)
        {
            Vector3 at = At(c, work, out float yaw);
            if (!ZNetScene.instance.IsAreaReady(at)) return -1;
            var near = new List<Piece>(); Piece.GetAllPiecesInRadius(at + Vector3.up * 12f, 22f, near);
            int n = 0;
            foreach (var p in GreatWorks.Parts(work))
            {
                var w = GreatWorks.Place(p, at.x, at.y, at.z, yaw);
                Vector3 spot = new Vector3(w.x, w.y, w.z);
                if (near.Any(x => x != null && Utils.GetPrefabName(x.gameObject) == p.Prefab && Vector3.Distance(x.transform.position, spot) < 0.5f)) n++;
            }
            return n;
        }
        // The parts of its plan that don't stand (local x, y, z, prefab), for a look at what's missing.
        public static List<string> Missing(Colony c, string work)
        {
            Vector3 at = At(c, work, out float yaw);
            var near = new List<Piece>(); Piece.GetAllPiecesInRadius(at + Vector3.up * 12f, 22f, near);
            return GreatWorks.Parts(work).Where(p => { var w = GreatWorks.Place(p, at.x, at.y, at.z, yaw); Vector3 spot = new Vector3(w.x, w.y, w.z);
                return !near.Any(x => x != null && Utils.GetPrefabName(x.gameObject) == p.Prefab && Vector3.Distance(x.transform.position, spot) < 0.5f); }).Select(p => $"{p.Prefab} {p.X} {p.Y:0.##} {p.Z}").ToList();
        }
        public static void Tick(Colony c)
        {
            if (!c.IsOwner || Time.time < _next) return;
            _next = Time.time + 20f;
            string work = GreatWorks.Lighthouse;
            if (GreatWorks.Stage(c.R.Flags, work) == GreatWorks.Done) { Rekindle(c, work); return; }
            if (GreatWorks.Stage(c.R.Flags, work) != GreatWorks.Laid) return;
            int built = Built(c, work), all = GreatWorks.Parts(work).Count;
            if (built < 0) return;
            if (built != c.R.Flag(GreatWorks.BuiltKey(work))) { c.R.Flags[GreatWorks.BuiltKey(work)] = built; c.MarkDirty(); }
            if (built >= all) Finish(c, work);
        }
        public static void Finish(Colony c, string work)
        {
            Vector3 at = At(c, work, out _);
            bool again = c.R.Flag(GreatWorks.AgainKey(work)) != 0;
            c.R.Flags[GreatWorks.Key(work)] = GreatWorks.Done; c.R.Flags.Remove(GreatWorks.AgainKey(work));
            if (again)
            {
                // (raised again: the beacon moves to its new fire, the saga hears of it, and the rest was had the first time)
                c.Log("text", "", "", $"{GreatWorks.Title(work)} stands again, taller than before.");
                Rekindle(c, work);
                c.Tell($"{GreatWorks.Title(work)} stands again! Its fire is the colony's beacon.", true);
                c.MarkDirty();
                return;
            }
            c.R.Flags[GreatWorks.DayKey(work)] = c.Day;
            c.R.Deeds += GreatWorks.Renown;
            c.Log("greatwork", c.R.Name, GreatWorks.Name(work), $"{c.R.Name} raised {GreatWorks.Name(work)} on the shore. Every ship of ours comes home by its light.");
            foreach (Brain b in Villagers.All.Where(b => b.S.Colony == c.Id)) { Mood.Add(b.S, "greatwork", b.Now); if (b.S.Prof == Prof.Builder) Worth.Gain(b.S, "festival"); b.Dirty(); }
            if (work == GreatWorks.Lighthouse)
            {
                var near = new List<Piece>(); Piece.GetAllPiecesInRadius(at + Vector3.up * GreatWorks.FireHeight(work), 5f, near);
                Fireplace hearth = near.Where(p => p != null && Utils.GetPrefabName(p.gameObject) == "bonfire").Select(p => p.GetComponent<Fireplace>()).FirstOrDefault(f => f != null);
                if (hearth != null && !Beacon.IsMarked(c, hearth)) Beacon.Mark(c, Colony.IdOf(hearth.GetComponent<ZNetView>()));
            }
            Net.LegendPin(c, at, GreatWorks.Proper(work, c.R.Name));
            c.Tell($"{GreatWorks.Title(work)} stands! {GreatWorks.Gift(work)} (+{GreatWorks.Renown} renown)", true);
            c.MarkDirty();
        }
        // Its bonfire, once it stands (built since, on a Lighthouse finished before it had one), is the colony's beacon.
        private static void Rekindle(Colony c, string work)
        {
            if (work != GreatWorks.Lighthouse) return;
            Vector3 at = At(c, work, out _);
            if (!ZNetScene.instance.IsAreaReady(at)) return;
            var near = new List<Piece>(); Piece.GetAllPiecesInRadius(at + Vector3.up * GreatWorks.FireHeight(work), 5f, near);
            Fireplace fire = near.Where(p => p != null && Utils.GetPrefabName(p.gameObject) == "bonfire").Select(p => p.GetComponent<Fireplace>()).FirstOrDefault(f => f != null);
            if (fire != null && !Beacon.IsMarked(c, fire)) Beacon.Mark(c, Colony.IdOf(fire.GetComponent<ZNetView>()));
        }
        public static bool Has(Colony c, string work) => c != null && GreatWorks.Stage(c.R.Flags, work) == GreatWorks.Done;
    }
}
