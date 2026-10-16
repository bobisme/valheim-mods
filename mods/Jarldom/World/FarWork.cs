using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // A Great Work raised far from home: the Peak Temple on the highest peak within reach of the high seat (GreatWorks.HighestNear),
    // named by the Thing and pinned on the jarls' maps. The colony is left behind (unloaded, its record out of reach), so an expedition
    // carries what it needs: a builder on the crew carries the plan (on their body: the work, its god, the colony's name, the peak,
    // and the site once laid), the carriers and the builder pack its makings from the stores, and the camp's chest holds them there.
    // Word goes home by the world's global keys (any game sees them; the colony's owner reads them on its tick): the site laid, how
    // many of its parts stand, and that it's done.
    internal static class FarWork
    {
        public const string CarryKey = "jarldom_farwork"; // (on the builder's body: Plan.Pack)
        public const float NearPeak = 40f, Clearance = 10f; // (how near the peak the jarl must be; how far its middle from the camp fire)
        private static readonly CultureInfo Ic = CultureInfo.InvariantCulture;

        public sealed class Plan
        {
            public string Work, Home, HomeName; public int God; public float PeakX, PeakZ; public bool Laid; public Vector3 At; public float Yaw;
            public string GodId => God >= 0 && God < Tables.Gods.Length ? Tables.Gods[God].Id : "odin";
            public Vector3 Peak => new Vector3(PeakX, 0, PeakZ);
            public string Pack() => string.Join("|", Work, God.ToString(Ic), Home, HomeName.Replace("|", " "), PeakX.ToString("0.0", Ic), PeakZ.ToString("0.0", Ic),
                Laid ? "1" : "0", At.x.ToString("0.00", Ic), At.y.ToString("0.00", Ic), At.z.ToString("0.00", Ic), Yaw.ToString("0", Ic));
            public static Plan Parse(string s)
            {
                string[] f = (s ?? "").Split('|');
                if (f.Length < 11) return null;
                float F(string v) => float.Parse(v, Ic);
                try { return new Plan { Work = f[0], God = int.Parse(f[1], Ic), Home = f[2], HomeName = f[3], PeakX = F(f[4]), PeakZ = F(f[5]), Laid = f[6] == "1", At = new Vector3(F(f[7]), F(f[8]), F(f[9])), Yaw = F(f[10]) }; }
                catch { return null; }
            }
            public List<GreatWorks.Part> Parts() => GreatWorks.Parts(Work, GodId);
        }

        // ---- the colony's side ----
        // The far peak named for a work (found once, kept in the flags): null if there's none higher than the colony's own ground.
        public static Vector3? Peak(Colony c, string work)
        {
            if (work != GreatWorks.PeakTemple || WorldGenerator.instance == null) return null;
            if (!c.R.Flags.TryGetValue(GreatWorks.PeakXKey(work), out int px))
            {
                float floor = GreatWork.Highest(c) + GreatWorks.PeakAbove, sea = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;
                var found = GreatWorks.HighestNear((x, z) => WorldGenerator.instance.GetHeight(x, z), c.Position.x, c.Position.z, GreatWorks.PeakReach, 24f, Mathf.Max(floor, sea + 10f));
                c.R.Flags[GreatWorks.PeakXKey(work)] = px = found.HasValue ? Mathf.RoundToInt(found.Value.x * 10f) : -1;
                c.R.Flags[GreatWorks.PeakZKey(work)] = found.HasValue ? Mathf.RoundToInt(found.Value.z * 10f) : -1;
                c.MarkDirty();
            }
            if (px == -1 && c.R.Flag(GreatWorks.PeakZKey(work)) == -1) return null;
            return new Vector3(px / 10f, 0, c.R.Flag(GreatWorks.PeakZKey(work)) / 10f);
        }
        public static bool Far(Colony c, string work) => c.R.Flag(GreatWorks.FarKey(work)) == 1;
        // Where the Thing's word says it may stand: the colony's highest ground, or that peak.
        public static string Where(Colony c, string work)
        {
            if (!(Peak(c, work) is Vector3 p)) return GreatWorks.Where(work);
            float h = WorldGenerator.instance.GetHeight(p.x, p.z);
            return $"{GreatWorks.Where(work)}, or on the peak {Mathf.RoundToInt(Utils.DistanceXZ(p, c.Position))} m to the {ScoutTask.Compass(p - c.Position)} ({Mathf.RoundToInt(h - ZoneSystem.instance.m_waterLevel)} m high, pinned on your map: take a crew there with a builder and carriers, from the Expedition tab)";
        }
        // The plan a builder setting out carries, if the colony has a far work to raise: not yet sited (or not begun where it was), or
        // sited far and still rising.
        public static Plan For(Colony c)
        {
            string work = GreatWorks.PeakTemple;
            int stage = GreatWorks.Stage(c.R.Flags, work);
            bool waiting = stage == GreatWorks.Promised || GreatWork.Movable(c, work) && !Far(c, work) || GreatWork.Fallen(c, work) && !Far(c, work);
            bool rising = stage == GreatWorks.Laid && Far(c, work);
            if (!waiting && !rising || !(Peak(c, work) is Vector3 p)) return null;
            int god = c.R.Flags.TryGetValue(GreatWorks.GodKey(work), out int g) ? g : 0;
            var plan = new Plan { Work = work, God = god, Home = c.Id, HomeName = c.R.Name, PeakX = p.x, PeakZ = p.z };
            if (rising) { plan.At = GreatWork.At(c, work, out float yaw); plan.Yaw = yaw; plan.Laid = true; }
            return plan;
        }
        // What's still to come of its makings: all of them, or what's left by the parts that stood when last seen.
        public static Dictionary<string, int> StillNeeded(Colony c, string work)
        {
            var all = GreatWorks.Needs(work);
            if (GreatWorks.Stage(c.R.Flags, work) != GreatWorks.Laid) return all;
            int parts = GreatWork.Parts(c, work).Count, built = c.R.Flag(GreatWorks.BuiltKey(work));
            float left = parts > 0 ? Mathf.Clamp01(1f - built / (float)parts) : 1f;
            return all.ToDictionary(kv => kv.Key, kv => Mathf.CeilToInt(kv.Value * left));
        }
        // Word from far off, on the colony owner's tick: the site laid, how much stands, done.
        public static string LaidKey(string colony) => "jarldom_gwlaid_" + colony;
        public static string BuiltKey(string colony) => "jarldom_gwbuilt_" + colony;
        public static string DoneKey(string colony) => "jarldom_gwdone_" + colony;
        public static void Tick(Colony c)
        {
            ZoneSystem z = ZoneSystem.instance;
            if (z == null || !c.IsOwner) return;
            if (z.GetGlobalKey(LaidKey(c.Id), out string laid) && !string.IsNullOrEmpty(laid))
            {
                z.RemoveGlobalKey(LaidKey(c.Id));
                string work = laid.Split('|')[0];
                if (GreatWork.Choosable(c, work)) // (promised, or sited at home and not begun: moved to the peak)
                {
                    if (GreatWork.Movable(c, work)) GreatWork.Unplan(c, work);
                    GreatWork.Laid(c, "An expedition", laid);
                    c.R.Flags[GreatWorks.FarKey(work)] = 1; c.MarkDirty();
                }
            }
            if (z.GetGlobalKey(BuiltKey(c.Id), out string built) && int.TryParse(built, NumberStyles.Integer, Ic, out int n))
            {
                z.RemoveGlobalKey(BuiltKey(c.Id));
                string work = GreatWorks.PeakTemple;
                if (GreatWorks.Stage(c.R.Flags, work) == GreatWorks.Laid && Far(c, work) && n != c.R.Flag(GreatWorks.BuiltKey(work))) { c.R.Flags[GreatWorks.BuiltKey(work)] = n; c.MarkDirty(); }
            }
            if (z.GetGlobalKey(DoneKey(c.Id), out string done) && !string.IsNullOrEmpty(done))
            {
                z.RemoveGlobalKey(DoneKey(c.Id));
                if (GreatWorks.Stage(c.R.Flags, done) == GreatWorks.Laid && Far(c, done)) { c.R.Flags[GreatWorks.BuiltKey(done)] = GreatWork.Parts(c, done).Count; GreatWork.Finish(c, done); }
            }
        }

        // ---- the jarl's side, far from home ----
        public static Plan Of(Brain b) => b?.View != null && b.View.IsValid() ? Plan.Parse(b.View.GetZDO().GetString(CarryKey, "")) : null;
        // The crew's builder carrying a plan (null if none).
        public static Brain Carrier(long leader) => Expedition.CrewOf(leader).FirstOrDefault(m => Expedition.RoleOf(m) == "builder" && Of(m) != null);
        // Where it would stand from here: the summit by the pin (the highest ground within a few metres of it), its door toward the camp.
        public static string WhyNot(Plan p, Player me, Vector3? fire, out Vector3 at, out float yaw)
        {
            at = p.Peak; yaw = 0;
            if (p.Laid) return "it's planned already: the builder is raising it";
            if (Compat.Find("com.quad.buildorders", "com.dhack.buildorders") == null) return "it needs Quad's BuildOrders mod, which lays out its plan for the builder";
            float d = Utils.DistanceXZ(me.transform.position, p.Peak);
            if (d > NearPeak) return $"the Thing named the peak {Mathf.RoundToInt(d)} m to the {ScoutTask.Compass(p.Peak - me.transform.position)} (pinned on your map): go up to it";
            if (fire == null) return "make camp near the summit first: the builder works from the camp, and its chest holds the makings";
            // (the summit: the highest of the ground round the pin)
            float best = float.MinValue;
            for (float x = -6; x <= 6; x += 1.5f) for (float z = -6; z <= 6; z += 1.5f)
            {
                float h = ZoneSystem.instance.GetGroundHeight(p.Peak + new Vector3(x, 0, z));
                if (h > best) { best = h; at = p.Peak + new Vector3(x, 0, z); }
            }
            float gap = Utils.DistanceXZ(at, fire.Value);
            if (gap < Clearance) return $"the camp's fire is {gap:0} m from the summit: make camp at least {Clearance:0} m off it, where the temple's footing (12 × 12 m) won't take it";
            if (gap > Expedition.CampReach) return $"the camp is {gap:0} m from the summit: make camp nearer, within {Expedition.CampReach:0} m";
            at.y = Footing(at, GreatWorks.FootingOf(p.Work));
            Vector3 f = fire.Value - at; f.y = 0;
            yaw = GreatWorks.SnapYaw(f.sqrMagnitude > 0.01f ? Mathf.Atan2(f.x, f.z) * Mathf.Rad2Deg : 0f); // (its door toward the camp)
            return "";
        }
        // The height its footing is dug to: the middle of the ground's heights under it (the top taken off, the hollows filled).
        private static float Footing(Vector3 at, float half)
        {
            var hs = new List<float>();
            for (float x = -half; x <= half; x += 2f) for (float z = -half; z <= half; z += 2f) hs.Add(ZoneSystem.instance.GetGroundHeight(at + new Vector3(x, 0, z)));
            hs.Sort(); return hs[hs.Count / 2];
        }
        // The plan laid on the summit: the footing dug level, BuildOrders' ghosts for the builder, the builder's plan marked, word home.
        public static bool Lay(Brain builder, Player me, Vector3 fire)
        {
            Plan p = Of(builder);
            if (p == null) return false;
            string why = WhyNot(p, me, fire, out Vector3 at, out float yaw);
            if (why != "") { me.Message(MessageHud.MessageType.Center, "Not yet: " + why + "."); return false; }
            Level(at, GreatWorks.FootingOf(p.Work) + 1f);
            var parts = p.Parts();
            var placed = parts.Select(x => GreatWorks.Place(x, at.x, at.y, at.z, yaw)).ToList();
            object bo = Compat.Find("com.quad.buildorders", "com.dhack.buildorders");
            var method = bo?.GetType().GetMethod("TryCreateBuildingShell");
            if (method == null) { me.Message(MessageHud.MessageType.Center, "BuildOrders can't lay out a plan (too old a version?)."); return false; }
            object[] args = { me, GreatWorks.Proper(p.Work, p.HomeName), parts.Select(x => x.Prefab).ToArray(), placed.Select(x => new Vector3(x.x, x.y, x.z)).ToArray(), parts.Select(x => GreatWork.Turn(x, yaw)).ToArray(), null, null };
            bool ok;
            try { ok = (bool)method.Invoke(bo, args); } catch (System.Exception e) { ok = false; args[6] = e.InnerException?.Message ?? e.Message; }
            if (!ok) { me.Message(MessageHud.MessageType.Center, $"The plan couldn't be laid out: {args[6]}"); return false; }
            p.Laid = true; p.At = at; p.Yaw = yaw;
            builder.View.GetZDO().Set(CarryKey, p.Pack());
            ZoneSystem.instance.SetGlobalKey($"{LaidKey(p.Home)} {string.Join("|", p.Work, at.x.ToString("0.00", Ic), at.y.ToString("0.00", Ic), at.z.ToString("0.00", Ic), yaw.ToString("0", Ic))}");
            me.Message(MessageHud.MessageType.Center, $"{GreatWorks.Title(p.Work)} is planned on the summit. {builder.S.Name} raises it from the camp chest and the crew's packs ({GreatWorks.NeedsText(p.Work)}).");
            builder.Say(new[] { "Here, then, nearer the gods. We'll dig the footing and begin.", "A temple on the roof of the world. I'll start with the plinth.", "The gods will see this from Asgard. Let's build." }[Random.Range(0, 3)], 7);
            _lastBuilt = -1;
            return true;
        }
        // The ground under its footing dug flat to the site's height, blended into the slope round it.
        private static void Level(Vector3 at, float half)
        {
            var doOp = HarmonyLib.AccessTools.Method(typeof(TerrainComp), "DoOperation");
            if (doOp == null) return;
            var comps = new HashSet<TerrainComp>();
            foreach (Vector3 off in new[] { Vector3.zero, new Vector3(half + 4, 0, half + 4), new Vector3(-half - 4, 0, half + 4), new Vector3(half + 4, 0, -half - 4), new Vector3(-half - 4, 0, -half - 4) })
                foreach (TerrainComp tc in TerrainComp.FindAllTerrainCompilers(at + off)) comps.Add(tc);
            if (comps.Count == 0) { TerrainComp made = Heightmap.FindHeightmap(at)?.GetAndCreateTerrainCompiler(); if (made != null) comps.Add(made); }
            var flat = new TerrainOp.Settings { m_level = true, m_levelRadius = half, m_square = true, m_smooth = true, m_smoothRadius = half + 4f, m_smoothPower = 3f, m_paintCleared = false, m_raise = false };
            foreach (TerrainComp tc in comps)
            {
                ZNetView tv = tc.GetComponent<ZNetView>();
                if (tv == null || !tv.IsValid()) continue;
                if (!tv.IsOwner()) tv.ClaimOwnership();
                doOp.Invoke(tc, new object[] { at, Vector3.zero, flat });
            }
        }
        // From the jarl's game every few seconds: near a far work rising, how much of it stands (word home when that changes, and
        // when it's done: the crew's cheer, and the builder's plan put away).
        private static int _lastBuilt = -1; private static float _next;
        public static void Watch(Player me)
        {
            if (Time.time < _next) return;
            _next = Time.time + 6f;
            Brain builder = Carrier(me.GetPlayerID());
            Plan p = Of(builder);
            if (p == null || !p.Laid || Utils.DistanceXZ(me.transform.position, p.At) > 60f || !ZNetScene.instance.IsAreaReady(p.At)) return;
            var parts = p.Parts();
            int n = GreatWork.BuiltAt(parts, p.At, p.Yaw);
            if (n == _lastBuilt) return;
            _lastBuilt = n;
            ZoneSystem.instance.SetGlobalKey($"{BuiltKey(p.Home)} {n.ToString(Ic)}");
            if (n < parts.Count) return;
            ZoneSystem.instance.SetGlobalKey($"{DoneKey(p.Home)} {p.Work}");
            builder.View.GetZDO().Set(CarryKey, "");
            foreach (Brain m in Expedition.CrewOf(me.GetPlayerID())) { m.Say(new[] { "It stands! The gods will hear us from here.", "Look at it, jarl. They'll sing of this.", "Done. My hands are frozen, and I don't care." }[Random.Range(0, 3)], 7); Body_.Anim(m.Body)?.SetTrigger("emote_cheer"); }
            me.Message(MessageHud.MessageType.Center, $"{GreatWorks.Proper(p.Work, p.HomeName)} stands! Word goes home: the colony hears of it when you're next there.");
        }
        // What the crew's packers carry for it: what's still needed less what the crew has already packed.
        public static List<(string item, int n)> Bill(Colony c, List<Brain> crew)
        {
            var bill = new List<(string, int)>();
            if (For(c) == null) return bill;
            foreach (var kv in StillNeeded(c, GreatWorks.PeakTemple))
            {
                int have = crew.Sum(m => m.Body.GetInventory().GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == kv.Key && !i.m_equipped).Sum(i => i.m_stack));
                if (kv.Value - have > 0) bill.Add((kv.Key, kv.Value - have));
            }
            return bill;
        }
    }
}
