using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BuildKit
{
    // kit test: every kit piece checked beside the game piece it was made from, live, on a test pad, one at a time (the donor 4 m
    // behind it). For each: its snap points and its WearNTear (material, health, support) beside the donor's; a copy built on the
    // ground (full support), one stacked on it and one beside it by their snap points (the support passed on, less), and one in the
    // air with nothing under it (it should fall); and for a roof, the game's cover and roof check under the one in the air before it falls. Then
    // everything set out is taken down, and whatever fell from them picked up: nothing is left behind. Golden: the pieces the kit should
    // have and each one's snap points, to catch one gone missing or changed.
    internal static class Test
    {
        internal const string TagKey = "buildkit_test";
        // (piece: snap points) as made in BuildKit 0.4.x; a change here is a change to say in the CHANGELOG.
        internal static readonly Dictionary<string, int> Golden = new Dictionary<string, int>
        {
            ["BK_turf_roof"] = 4, ["BK_turf_roof_top"] = 6, ["BK_turf_roof_ocorner"] = 4, ["BK_turf_roof_icorner"] = 4,
            ["BK_turf_roof_45"] = 4, ["BK_turf_roof_top_45"] = 6, ["BK_turf_roof_ocorner_45"] = 4, ["BK_turf_roof_icorner_45"] = 4,
            ["BK_turf_roof_smoke"] = 4, ["BK_turf_roof_smoke_45"] = 4, ["BK_turf_ridge_vent"] = 6, ["BK_turf_ridge_vent_45"] = 6,
            ["BK_wattle_fence"] = 4, ["BK_wattle_daub_wall"] = 4,
        };
        private const float Behind = 4f, Air = 6f, Settle = 15f;

        internal static int Snaps(GameObject go) => go.GetComponentsInChildren<Transform>(true).Count(t => t.CompareTag("snappoint"));
        private static List<Vector3> SnapsAt(GameObject go) => go.GetComponentsInChildren<Transform>(true).Where(t => t.CompareTag("snappoint")).Select(t => t.position).ToList();
        private static string Wear(GameObject go)
        {
            WearNTear w = go.GetComponent<WearNTear>();
            return w == null ? "no WearNTear" : $"{w.m_materialType}, health {w.m_health:0}, supports {w.m_supports}, falls unsupported {w.m_noSupportWear}";
        }

        internal static IEnumerator Run(List<Kit.Def> defs, Vector3 pad, JObject report)
        {
            var pieces = new JArray(); int down = 0, picked = 0;
            var golden = new JArray(Golden.Keys.Where(n => !Kit.Made.ContainsKey(n) || Kit.Made[n] == null).Select(n => "missing: " + n));
            foreach (string n in Kit.All.Select(d => d.Name).Where(n => !Golden.ContainsKey(n))) golden.Add("not in the golden list: " + n);
            foreach (Kit.Def d in defs)
            {
                var o = new JObject();
                yield return One(d, pad, o, golden);
                down += (int)o["down"]; picked += (int)o["picked"]; o.Remove("down"); o.Remove("picked");
                pieces.Add(o);
            }
            report["pieces"] = pieces;
            report["golden"] = golden;
            report["pad"] = new JArray(pad.x, pad.z);
            report["cleanup"] = $"{down} pieces taken down, {picked} drops picked up";
        }

        // One piece and its donor, alone on the pad (the cover of one roof isn't another's), settled (till the ones in the air have
        // fallen, 15 s at most: the game works out support a few pieces a frame), then taken down.
        private static IEnumerator One(Kit.Def d, Vector3 pad, JObject o, JArray golden)
        {
            ZNetScene scene = ZNetScene.instance;
            Player me = Player.m_localPlayer;
            Vector3 back = me != null ? me.transform.forward : Vector3.forward;
            const float radius = 14f;
            var dropsBefore = new HashSet<ItemDrop>(Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None).Where(x => Vector3.Distance(x.transform.position, pad) < radius));
            var placed = new List<GameObject>();
            var rows = new List<(string who, GameObject ground, GameObject stacked, GameObject beside, GameObject air, string cover)>();
            GameObject kit = Kit.Made.TryGetValue(d.Name, out GameObject k) ? k : null, donor = scene.GetPrefab(d.From);
            o["piece"] = d.Name; o["from"] = d.From;
            if (kit != null)
            {
                int snaps = Snaps(kit);
                o["snaps"] = donor != null ? $"{snaps} (donor {Snaps(donor)})" : $"{snaps}";
                if (Golden.TryGetValue(d.Name, out int want) && want != snaps) golden.Add($"{d.Name}: {snaps} snap points, golden {want}");
                o["wear"] = Wear(kit); if (donor != null) o["donorWear"] = Wear(donor);
            }
            foreach (var (who, prefab, at) in new[] { ("kit", kit, pad), ("donor", donor, pad + back * Behind) })
            {
                if (prefab == null) { o[who] = "not made"; continue; }
                Vector3 g = at; g.y = ZoneSystem.instance.GetGroundHeight(at);
                GameObject ground = Build(prefab, g, placed);
                // (stacked: its lowest snap point on the grounded one's highest; beside: hung off the stacked one, off the ground, its -x
                // side's snap on the stacked one's +x side's at the same height, as a row of roof, a ridge or a wall runs on)
                var up = SnapsAt(ground); GameObject stacked = null, beside = null;
                var own = prefab.GetComponentsInChildren<Transform>(true).Where(t => t.CompareTag("snappoint")).Select(t => prefab.transform.InverseTransformPoint(t.position)).ToList();
                if (up.Count > 0)
                {
                    Vector3 top = up.OrderByDescending(p => p.y).First();
                    stacked = Build(prefab, top - own.OrderBy(p => p.y).First(), placed);
                    Vector3 side = SnapsAt(stacked).OrderByDescending(p => p.x).ThenBy(p => p.y).First(), local = side - stacked.transform.position;
                    Vector3 match = own.OrderBy(p => p.x).ThenBy(p => Mathf.Abs(p.y - local.y)).First();
                    if (match.x < local.x - 0.5f) beside = Build(prefab, side - match, placed);
                }
                GameObject air = Build(prefab, g + Vector3.up * Air, placed);
                // (the cover under a roof in the air, before it falls: its colliders moved into place now)
                string cover = null;
                if (d.Wattle == null)
                {
                    Physics.SyncTransforms();
                    Bounds b = Bounds(air);
                    Cover.GetCoverForPoint(new Vector3(b.center.x, b.min.y - 0.8f, b.center.z), out float c, out bool roofed);
                    cover = $"{c:0.00}{(roofed ? ", under a roof" : ", NOT under a roof")}";
                }
                rows.Add((who, ground, stacked, beside, air, cover));
            }
            float until = Time.time + Settle;
            while (Time.time < until && rows.Any(r => Alive(r.air))) yield return new WaitForSeconds(0.5f);
            yield return new WaitForSeconds(1f); // (the stacked ones' support settled too)
            foreach (var row in rows)
                o[row.who] = new JObject
                {
                    ["ground"] = Support(row.ground), ["stacked"] = Support(row.stacked), ["beside"] = Support(row.beside),
                    ["air"] = Alive(row.air) ? "STILL STANDING with nothing under it" : "fell", ["cover"] = row.cover,
                };
            // (all of it taken down; and what fell from them picked up)
            int down = 0, picked = 0;
            foreach (GameObject go in placed)
            {
                if (!Alive(go)) continue;
                ZNetView v = go.GetComponent<ZNetView>(); v.ClaimOwnership(); ZNetScene.instance.Destroy(go); down++;
            }
            yield return new WaitForSeconds(0.5f);
            foreach (ItemDrop drop in Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None).Where(x => x != null && !dropsBefore.Contains(x) && Vector3.Distance(x.transform.position, pad) < radius).ToList())
            {
                ZNetView v = drop.GetComponent<ZNetView>(); if (v == null || !v.IsValid()) continue;
                v.ClaimOwnership(); ZNetScene.instance.Destroy(drop.gameObject); picked++;
            }
            o["down"] = down; o["picked"] = picked;
        }

        private static GameObject Build(GameObject prefab, Vector3 at, List<GameObject> placed)
        {
            GameObject go = Object.Instantiate(prefab, at, Quaternion.identity);
            go.GetComponent<ZNetView>()?.GetZDO()?.Set(TagKey, true);
            Piece piece = go.GetComponent<Piece>();
            if (Player.m_localPlayer != null) piece?.SetCreator(Player.m_localPlayer.GetPlayerID(), Splatform.PlatformManager.DistributionPlatform.LocalUser.PlatformUserID);
            go.GetComponent<WearNTear>()?.OnPlaced();
            placed.Add(go);
            return go;
        }
        // (the game's own, private: maximum, minimum, horizontal and vertical loss)
        private static readonly System.Reflection.MethodInfo MaterialProps = AccessTools.Method(typeof(WearNTear), "GetMaterialProperties");
        private static readonly System.Reflection.MethodInfo SupportOf = AccessTools.Method(typeof(WearNTear), "GetSupport");
        private static bool Alive(GameObject go) => go != null && go.GetComponent<ZNetView>() is ZNetView v && v.IsValid();
        private static string Support(GameObject go)
        {
            if ((object)go == null) return "no snap points to set it by";
            if (!Alive(go)) return "FELL";
            WearNTear w = go.GetComponent<WearNTear>();
            if (w == null) return "standing (no WearNTear)";
            if (MaterialProps == null || SupportOf == null) return "standing";
            var args = new object[4]; MaterialProps.Invoke(w, args);
            return $"standing, support {(float)SupportOf.Invoke(w, null):0.#} of {(float)args[0]:0.#} (falls under {(float)args[1]:0.#})";
        }
        private static Bounds Bounds(GameObject go)
        {
            var cs = go.GetComponentsInChildren<Collider>().Where(c => c.enabled && !c.isTrigger).ToList();
            if (cs.Count == 0) return new Bounds(go.transform.position, Vector3.one);
            Bounds b = cs[0].bounds; foreach (Collider c in cs.Skip(1)) b.Encapsulate(c.bounds);
            return b;
        }
    }
}
