using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jarldom
{
    // A builder with nothing planned keeps the colony standing: mends what's damaged (as a player's hammer does, for nothing), raises
    // again what was destroyed (broken in a fight, fallen down; not what a player took down on purpose) from the stores, and paves the
    // ground where villagers' feet have worn a path (a stone a stretch, and a road is walked a sixth faster).
    internal static class Upkeep
    {
        public const string RuinsKey = "jarldom_ruins";
        public const int MaxRuins = 60, RoadAfter = 30;

        // ---- ruins: what was destroyed, kept on the pillars ("prefab|x|y|z|rx|ry|rz;...") ----
        public struct Ruin { public string Prefab; public Vector3 At; public Quaternion Rot; public string Entry; }
        public static List<Ruin> Ruins(Colony c)
        {
            var list = new List<Ruin>();
            foreach (string e in (c.Z.GetString(RuinsKey, "") ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] f = e.Split('|');
                if (f.Length < 7) continue;
                float F(int i) => float.Parse(f[i], CultureInfo.InvariantCulture);
                list.Add(new Ruin { Prefab = f[0], At = new Vector3(F(1), F(2), F(3)), Rot = Quaternion.Euler(F(4), F(5), F(6)), Entry = e });
            }
            return list;
        }
        public static string Entry(string prefab, Vector3 at, Quaternion rot)
        {
            Vector3 r = rot.eulerAngles;
            return string.Join("|", prefab, at.x.ToString("0.###", CultureInfo.InvariantCulture), at.y.ToString("0.###", CultureInfo.InvariantCulture), at.z.ToString("0.###", CultureInfo.InvariantCulture),
                r.x.ToString("0.#", CultureInfo.InvariantCulture), r.y.ToString("0.#", CultureInfo.InvariantCulture), r.z.ToString("0.#", CultureInfo.InvariantCulture));
        }
        // On the colony owner (the "ruin" and "unruin" actions).
        public static void Add(Colony c, string entry)
        {
            var all = (c.Z.GetString(RuinsKey, "") ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (all.Contains(entry)) return;
            all.Add(entry);
            if (all.Count > MaxRuins) all.RemoveRange(0, all.Count - MaxRuins); // (the oldest are let go)
            c.Z.Set(RuinsKey, string.Join(";", all));
        }
        public static void Remove(Colony c, string entry)
        {
            var all = (c.Z.GetString(RuinsKey, "") ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).Where(e => e != entry).ToList();
            c.Z.Set(RuinsKey, string.Join(";", all));
        }

        // A player taking a piece down with the hammer goes through Remove: that one isn't a ruin.
        private static WearNTear _removing;
        [HarmonyPatch(typeof(WearNTear), "RPC_Remove")]
        private static class Removing
        {
            private static void Prefix(WearNTear __instance) => _removing = __instance;
            private static void Finalizer() => _removing = null;
        }
        [HarmonyPatch(typeof(WearNTear), "Destroy")]
        private static class Destroyed
        {
            private static void Prefix(WearNTear __instance, HitData hitData)
            {
                if (__instance == null || __instance == _removing) return;
                Plugin.Guard(() =>
                {
                    ZNetView v = __instance.GetComponent<ZNetView>();
                    Piece p = __instance.GetComponent<Piece>();
                    if (v == null || !v.IsValid() || !v.IsOwner() || p == null || p.GetCreator() == 0) return; // (a player's building, by the game that has it)
                    if (hitData != null && hitData.GetAttacker() is Character by && by.IsPlayer()) return; // (a player breaking their own on purpose)
                    string name = Utils.GetPrefabName(p.gameObject);
                    if (name.StartsWith("Jarldom") || p.GetComponent<Plant>() != null || p.GetComponent<Ship>() != null || p.GetComponent<Vagon>() != null) return;
                    Vector3 at = p.transform.position;
                    Colony c = Colony.At(at);
                    if (c == null || !c.Tends(at)) return;
                    Net.Act(c, "ruin", Entry(name, at, p.transform.rotation));
                });
            }
        }

        // ---- worn paths: where villagers walk outdoors, counted in 2 m squares (kept while the colony is loaded) ----
        private static readonly Dictionary<string, Dictionary<long, int>> Steps = new Dictionary<string, Dictionary<long, int>>();
        public static void Forget() => Steps.Clear();
        private static long Cell(Vector3 p) => ((long)Mathf.FloorToInt(p.x / 2f) << 32) ^ (uint)Mathf.FloorToInt(p.z / 2f);
        private static Vector3 Centre(long cell) => new Vector3((int)(cell >> 32) * 2f + 1f, 0, (int)(uint)(cell & 0xffffffff) * 2f + 1f);
        public static void Step(Brain b)
        {
            Colony c = b.Colony;
            if (c == null || !b.Move.Moving || b.Held != null) return;
            Vector3 p = b.Pos;
            if (Mathf.Abs(p.y - ZoneSystem.instance.GetGroundHeight(p)) > 0.4f || !c.Tends(p) || Paved(p)) return; // (on the land, in the colony, not yet a road)
            if (c.R.Zones.Any(z => (z.Type == ZoneType.Fields || z.Type == ZoneType.SacredBog) && Zones.Inside(z, p))) return; // (not through the crops)
            if (!Steps.TryGetValue(c.Id, out var cells)) Steps[c.Id] = cells = new Dictionary<long, int>();
            long k = Cell(p);
            cells[k] = (cells.TryGetValue(k, out int n) ? n : 0) + 1;
            if (cells.Count > 4000) foreach (long old in cells.Where(kv => kv.Value < 3).Select(kv => kv.Key).ToList()) cells.Remove(old);
        }
        public static bool Paved(Vector3 p) { Heightmap hm = Heightmap.FindHeightmap(p); return hm != null && hm.GetPaintMask(p).b > 0.5f; }
        public static Vector3? NextRoad(Colony c, Vector3 near)
        {
            if (!Steps.TryGetValue(c.Id, out var cells)) return null;
            foreach (var kv in cells.Where(kv => kv.Value >= RoadAfter).OrderByDescending(kv => kv.Value).ThenBy(kv => Vector3.Distance(Centre(kv.Key), near)).Take(10).ToList())
            {
                Vector3 at = Centre(kv.Key); at.y = ZoneSystem.instance.GetGroundHeight(at);
                if (Paved(at)) { cells.Remove(kv.Key); continue; }
                return at;
            }
            return null;
        }
        public static void Walked(Colony c, Vector3 at) { if (Steps.TryGetValue(c.Id, out var cells)) cells.Remove(Cell(at)); }
        public static int Worn(Colony c) => Steps.TryGetValue(c.Id, out var cells) ? cells.Count(kv => kv.Value >= RoadAfter) : 0;
        public static string Busiest(Colony c) => Steps.TryGetValue(c.Id, out var cells) && cells.Count > 0 ? string.Join(", ", cells.OrderByDescending(kv => kv.Value).Take(5).Select(kv => $"{Centre(kv.Key).x:0},{Centre(kv.Key).z:0}:{kv.Value}")) + $" ({cells.Count} squares)" : "none";
        // The game's own paint, straight on the land's terrain records (as the hoe's paved path, without the hoe).
        private static readonly System.Reflection.MethodInfo DoOp = AccessTools.Method(typeof(TerrainComp), "DoOperation");
        public static void Pave(Vector3 at)
        {
            var settings = new TerrainOp.Settings { m_level = false, m_raise = false, m_smooth = false, m_paintCleared = true, m_paintType = TerrainModifier.PaintType.Paved, m_paintRadius = 1.3f };
            var comps = new HashSet<TerrainComp>(TerrainComp.FindAllTerrainCompilers(at));
            if (comps.Count == 0) { TerrainComp made = Heightmap.FindHeightmap(at)?.GetAndCreateTerrainCompiler(); if (made != null) comps.Add(made); }
            foreach (TerrainComp tc in comps)
            {
                ZNetView tv = tc.GetComponent<ZNetView>();
                if (tv == null || !tv.IsValid()) continue;
                if (!tv.IsOwner()) tv.ClaimOwnership();
                DoOp.Invoke(tc, new object[] { at, Vector3.zero, settings });
            }
        }

        // ---- damage: the colony's pieces below three quarters' health, looked for every 20 s ----
        private static readonly Dictionary<string, (float at, List<WearNTear> list)> Damaged = new Dictionary<string, (float, List<WearNTear>)>();
        public static List<WearNTear> Hurt(Colony c)
        {
            if (Damaged.TryGetValue(c.Id, out var hit) && Time.time - hit.at < 20f) return hit.list.Where(w => w != null).ToList();
            var list = c.PiecesIn(c.Radius).Select(p => p.GetComponent<WearNTear>()).Where(w => w != null && c.Tends(w.transform.position) && w.GetComponent<ZNetView>()?.GetZDO() is ZDO z && z.GetFloat(ZDOVars.s_health, w.m_health) < w.m_health * 0.75f).ToList();
            Damaged[c.Id] = (Time.time, list);
            return list;
        }

        // What an idle builder does next, if anything.
        public static Task Next(Brain b)
        {
            Colony c = b.Colony;
            if (Gear.Tool(b, "hammer") == null || Time.time < b.NoUpkeepUntil) return null;
            // (badly damaged first, then what was lost, then the weather's wear, then roads: wear never ends, and would keep the rest waiting)
            var hurtAll = Hurt(c).Where(w => !UpkeepTask.Taken(w.transform.position)).OrderBy(w => Vector3.Distance(w.transform.position, b.Pos)).ToList();
            WearNTear bad = hurtAll.FirstOrDefault(w => w.GetComponent<ZNetView>().GetZDO().GetFloat(ZDOVars.s_health, w.m_health) < w.m_health * 0.5f);
            if (bad != null) return new UpkeepTask("mend", bad.transform.position) { Wnt = bad };
            if (c.IsOwner)
                foreach (Ruin r in Ruins(c))
                {
                    if (UpkeepTask.Taken(r.At)) continue;
                    Piece prefab = Assets.Find(r.Prefab)?.GetComponent<Piece>();
                    if (prefab == null) { Net.Act(c, "unruin", r.Entry); continue; }
                    var needs = prefab.m_resources.Where(q => q.m_resItem != null && !q.m_upgraderResource && q.m_amount > 0).Select(q => (q.m_resItem.name, q.m_amount)).ToList();
                    if (needs.All(n => Crafts.InStock(c, n.Item1) >= n.Item2)) return new UpkeepTask("rebuild", r.At) { Ruin = r, Needs = needs, Prefab = prefab };
                }
            WearNTear hurt = hurtAll.FirstOrDefault();
            if (hurt != null && UnityEngine.Random.value < 0.6f) return new UpkeepTask("mend", hurt.transform.position) { Wnt = hurt }; // (now and then a road instead)
            Vector3? road = Crafts.InStock(c, "Stone") > 0 ? NextRoad(c, b.Pos) : null;
            if (road != null && !UpkeepTask.Taken(road.Value)) return new UpkeepTask("pave", road.Value) { Needs = new List<(string, int)> { ("Stone", 1) } };
            if (hurt != null) return new UpkeepTask("mend", hurt.transform.position) { Wnt = hurt };
            b.NoUpkeepUntil = Time.time + 20;
            return null;
        }
    }

    internal sealed class UpkeepTask : Task
    {
        public readonly string Kind; public readonly Vector3 At; public WearNTear Wnt; public Upkeep.Ruin Ruin; public Piece Prefab; public List<(string item, int amount)> Needs = new List<(string, int)>();
        private int _phase, _beats; private float _next;
        private static readonly Dictionary<Vector3, float> Held = new Dictionary<Vector3, float>(); // (a spot one builder is on)
        public static bool Taken(Vector3 at) => Held.Any(kv => kv.Value > Time.time && Vector3.Distance(kv.Key, at) < 0.5f);
        public UpkeepTask(string kind, Vector3 at) { Kind = kind; At = at; }
        public override bool Interruptible => _phase == 0;
        public override void Start(Brain b)
        {
            Held[At] = Time.time + 120; if (Held.Count > 50) foreach (var k in Held.Where(kv => kv.Value < Time.time).Select(kv => kv.Key).ToList()) Held.Remove(k);
            Doing = Kind == "mend" ? "mending " + Localization.instance.Localize(Wnt?.GetComponent<Piece>()?.m_name ?? "a wall").ToLower()
                : Kind == "rebuild" ? "rebuilding " + Localization.instance.Localize(Prefab.m_name).ToLower() : "paving the path";
            if (Needs.Count == 0) { _phase = 1; b.Move.To(At, 2.5f); }
        }
        public override void Stop(Brain b) { Held.Remove(At); base.Stop(b); }
        private int Have(Brain b, string item) => b.Body.GetInventory().GetAllItems().Where(i => Classes.Prefab(i) == item).Sum(i => i.m_stack);
        public override void Tick(Brain b)
        {
            if (Done) return;
            if (_phase == 0)
            {
                // From the stores, all of it, before setting out.
                var missing = Needs.Where(n => Have(b, n.item) < n.amount).ToList();
                if (missing.Count == 0) { _phase = 1; b.Move.To(At, 2.5f); return; }
                var (want, count) = missing[0];
                Container from = Stock.Chests(b.Colony, b.Pos).Where(x => x.category != "treasury" && !ContainerAccess.InUse(x.chest)).Select(x => x.chest).FirstOrDefault(ch => ch.GetInventory().GetAllItems().Any(i => Classes.Prefab(i) == want));
                if (from == null) { Done = true; b.NoUpkeepUntil = Time.time + 30; return; }
                if (Vector3.Distance(b.Pos, from.transform.position) > 2.4f) { if (!b.Move.Moving) b.Move.To(from.transform.position, 1.8f); if (b.Move.Failed) Done = true; return; }
                ItemDrop.ItemData stack = from.GetInventory().GetAllItems().First(i => Classes.Prefab(i) == want);
                if (Stock.Take(from, b.Body.GetInventory(), stack, count - Have(b, want)) == 0) { Done = true; b.NoUpkeepUntil = Time.time + 30; }
                return;
            }
            if (Kind == "mend" && (Wnt == null || !Wnt.GetComponent<ZNetView>().IsValid())) { Done = true; return; }
            // (near enough across the ground, and within reach in height)
            if (Utils.DistanceXZ(b.Pos, At) > 3.2f || At.y - b.Pos.y > 6f || b.Pos.y - At.y > 3f) { if (!b.Move.Moving) b.Move.To(At, 2.5f); if (b.Move.Failed) { Done = true; b.NoUpkeepUntil = Time.time + 20; } return; }
            b.Move.Stop();
            Vector3 d = At - b.Pos; d.y = 0; if (d.sqrMagnitude > 0.01f) b.Ai.LookTowards(d.normalized);
            if (Time.time < _next) return;
            _next = Time.time + 1.2f / Mathf.Max(0.4f, b.Productivity());
            ItemDrop.ItemData hammer = Gear.Tool(b, "hammer");
            if (hammer == null) { Done = true; return; }
            if (!b.Body.IsItemEquiped(hammer)) b.Body.EquipItem(hammer, false);
            Body_.Anim(b.Body)?.SetTrigger("swing_hammer");
            if (++_beats < 3) return;
            Colony c = b.Colony;
            switch (Kind)
            {
                case "mend":
                    Wnt.Repair();
                    b.S.Bump("mended");
                    break;
                case "rebuild":
                    if (Needs.Any(n => Have(b, n.item) < n.amount)) { Done = true; return; }
                    // (one stands there again already: a player's rebuilt it)
                    var near = new List<Piece>(); Piece.GetAllPiecesInRadius(At, 0.6f, near);
                    if (!near.Any(p => Utils.GetPrefabName(p.gameObject) == Ruin.Prefab))
                    {
                        GameObject made = Object.Instantiate(Prefab.gameObject, At, Ruin.Rot);
                        Piece piece = made.GetComponent<Piece>();
                        piece.SetCreator(c.R.Founder, Splatform.PlatformManager.DistributionPlatform.LocalUser.PlatformUserID); // (the founder's, as it was)
                        piece.m_placeEffect.Create(At, Ruin.Rot, made.transform);
                        made.GetComponent<WearNTear>()?.OnPlaced();
                        Take(b);
                        b.S.Bump("rebuilt");
                        c.Log("text", b.S.Name, "", $"{b.S.Name} rebuilt the {Localization.instance.Localize(Prefab.m_name).ToLower()} that was lost.");
                    }
                    Net.Act(c, "unruin", Ruin.Entry);
                    break;
                case "pave":
                    if (Have(b, "Stone") < 1) { Done = true; return; }
                    Upkeep.Pave(At);
                    Take(b);
                    Upkeep.Walked(c, At);
                    b.S.Bump("paved");
                    if (b.S.Count("paved") % 25 == 0) c.Log("text", b.S.Name, "", $"{b.S.Name} has laid {b.S.Count("paved")} stretches of road where the colony walks.");
                    break;
            }
            b.Body.RaiseSkill(Skills.SkillType.Crafting, 0.3f);
            b.Dirty();
            Done = true;
        }
        private void Take(Brain b)
        {
            Inventory bag = b.Body.GetInventory();
            foreach (var (item, amount) in Needs)
                for (int left = amount; left > 0;)
                {
                    ItemDrop.ItemData st = bag.GetAllItems().FirstOrDefault(i => Classes.Prefab(i) == item);
                    if (st == null) break;
                    int n = Math.Min(left, st.m_stack); bag.RemoveItem(st, n); left -= n;
                }
        }
    }
}
