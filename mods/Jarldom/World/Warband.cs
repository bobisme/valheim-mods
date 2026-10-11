using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Jarldom
{
    // The draugr jarl's warband: once Bonemass has fallen, an ancient barrow rises in a swamp (pinned on the map) with a named draugr
    // jarl in it, and every few days at dusk his warband comes for the colony, until someone goes there and cuts him down.
    // Built of the game's own draugr and stones, made as saved world objects (the barrow is far from anyone when it appears).
    // On the colony record: barrow:x, barrow:z, barrow:next (day of the next warband), barrow:dead.
    internal static class Warband
    {
        public const string JarlKey = "jarldom_jarl", BandKey = "jarldom_warband";
        private static readonly string[] Jarls = { "Hrolf Grave-born", "Sigurd the Hollow", "Thrain Barrow-king", "Ketil Ash-beard", "Kari the Cold" };
        public static bool Stands(Colony c) => c.R.Flag("barrow:x") != 0 && c.R.Flag("barrow:dead") == 0;
        public static Vector3 Barrow(Colony c) => new Vector3(c.R.Flag("barrow:x"), 0, c.R.Flag("barrow:z"));
        public static string JarlName(Colony c) => Jarls[(c.Id.GetStableHashCode() & 0x7fffffff) % Jarls.Length];

        public static void Dawn(Colony c, int day)
        {
            if (Stands(c)) { Net.Pin(Barrow(c), "The barrow of " + JarlName(c), true); return; } // (pins aren't saved: each day again)
            if (c.R.Flag("barrow:x") != 0 || !Plugin.Instance.Raids.Value || c.R.Rank < Rank.Hamlet) return;
            if (ZoneSystem.instance == null || !ZoneSystem.instance.GetGlobalKey("defeated_bonemass")) return;
            Raise(c, day);
        }
        public static void Raise(Colony c, int day)
        {
            Vector3? spot = Swamp(c.Position);
            if (spot == null) return;
            Vector3 at = spot.Value;
            c.R.Flags["barrow:x"] = Mathf.RoundToInt(at.x); c.R.Flags["barrow:z"] = Mathf.RoundToInt(at.z);
            c.R.Flags["barrow:next"] = day + 2;
            // A ring of old stones, a heap of the dead that keeps waking, and the jarl.
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI / 2;
                Vector3 p = at + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * 6f; p.y = Ground(p);
                Make("stone_wall_4x2", p, Quaternion.Euler(0, i * 90, 0), null);
            }
            Vector3 heap = at + new Vector3(3, 0, 0); heap.y = Ground(heap);
            Make("Spawner_DraugrPile", heap, Quaternion.identity, null);
            Vector3 jarl = at; jarl.y = Ground(at) + 0.5f;
            Make("Draugr_Elite", jarl, Quaternion.identity, z => { z.Set(ZDOVars.s_level, 3); z.Set(JarlKey, c.Id); z.Set(Draugr.NameKey, "Draugr jarl " + JarlName(c)); });
            Net.Pin(at, "The barrow of " + JarlName(c), true);
            c.Log("text", "", "", $"An ancient barrow has opened in the swamp, {Mathf.RoundToInt(Utils.DistanceXZ(at, c.Position))} m to the {ScoutTask.Compass(at - c.Position)}: {JarlName(c)}, a draugr jarl, wakes and covets {c.R.Name}. His warbands will come until he is cut down.");
            c.MarkDirty();
        }
        private static float Ground(Vector3 p) => WorldGenerator.instance != null ? WorldGenerator.instance.GetHeight(p.x, p.z) : p.y;
        private static Vector3? Swamp(Vector3 from)
        {
            if (WorldGenerator.instance == null) return null;
            var rng = new System.Random(Mathf.RoundToInt(from.x * 31 + from.z));
            for (int i = 0; i < 400; i++)
            {
                double a = rng.NextDouble() * Math.PI * 2, d = 300 + rng.NextDouble() * 1500;
                Vector3 p = from + new Vector3((float)Math.Sin(a), 0, (float)Math.Cos(a)) * (float)d;
                if (WorldGenerator.instance.GetBiome(p) != Heightmap.Biome.Swamp) continue;
                float h = WorldGenerator.instance.GetHeight(p.x, p.z);
                if (h < (ZoneSystem.instance?.m_waterLevel ?? 30) + 0.5f) continue;
                p.y = h; return p;
            }
            return null;
        }
        // A saved object where no one is: its record made as the game makes one, so it appears when its ground loads.
        private static void Make(string prefab, Vector3 at, Quaternion rot, Action<ZDO> set)
        {
            GameObject p = ZNetScene.instance?.GetPrefab(prefab);
            ZNetView v = p != null ? p.GetComponent<ZNetView>() : null;
            if (v == null) return;
            int hash = prefab.GetStableHashCode();
            ZDO z = ZDOMan.instance.CreateNewZDO(at, hash);
            z.Persistent = v.m_persistent; z.Type = v.m_type; z.Distant = v.m_distant;
            z.SetPrefab(hash); z.SetRotation(rot);
            set?.Invoke(z);
        }

        // At dusk, when it's time: the warband at the colony's edge, from the barrow's side.
        public static void Dusk(Colony c, int day)
        {
            if (!Stands(c) || day < c.R.Flag("barrow:next")) return;
            c.R.Flags["barrow:next"] = day + UnityEngine.Random.Range(3, 6);
            Vector3 dir = Barrow(c) - c.Position; dir.y = 0; dir = dir.sqrMagnitude < 1 ? Vector3.forward : dir.normalized;
            int n = 3 + (int)c.R.Rank;
            for (int i = 0; i < n; i++)
            {
                string kind = i % 3 == 2 ? "Draugr_Ranged" : i == 0 ? "Draugr_Elite" : "Draugr";
                GameObject prefab = ZNetScene.instance.GetPrefab(kind);
                if (prefab == null) continue;
                Vector3 at = Colony.Spot(c.Position + dir * (c.Radius * 0.7f) + UnityEngine.Random.insideUnitSphere * 6f, 5f) ?? c.Position + dir * 40;
                GameObject go = UnityEngine.Object.Instantiate(prefab, at, Quaternion.LookRotation(-dir));
                go.GetComponent<ZNetView>().GetZDO().Set(BandKey, c.Id);
                Assets.Effect("vfx_ghost_death", at);
            }
            c.Tell($"The warband of {JarlName(c)} comes out of the dusk!", true);
            c.MarkDirty();
        }
        // While any of them stands in the colony, it's a raid (the horn, fighters out, the rest in the hall).
        public static bool Raiding(Colony c) => Character.GetAllCharacters().Any(ch => ch != null && !ch.IsDead() && Utils.DistanceXZ(ch.transform.position, c.Position) < c.Radius + 40
            && ch.GetComponent<ZNetView>()?.GetZDO()?.GetString(BandKey, "") == c.Id);

        // The jarl cut down: the barrow's power is broken.
        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        private static class Slain
        {
            private static void Prefix(Character __instance)
            {
                string id = __instance.GetComponent<ZNetView>()?.GetZDO()?.GetString(JarlKey, "") ?? "";
                if (id.Length == 0) return;
                Colony c = Colony.Of(id);
                if (c == null) return;
                if (c.IsOwner) Plugin.Guard(() => Broken(c)); else Net.Act(c, "jarlslain", "");
            }
        }
        public static void Broken(Colony c)
        {
            if (!Stands(c)) return;
            c.R.Flags["barrow:dead"] = 1;
            c.R.Deeds += Renown.Deed("raidheld") * 3;
            Net.Pin(Barrow(c), "", false);
            c.Log("text", "", "", $"{JarlName(c)} is cut down in his barrow. His warbands will trouble {c.R.Name} no more.");
            c.MarkDirty();
        }
    }
}
