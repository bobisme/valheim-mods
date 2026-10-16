using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // The land wears thin, in the world (the rules are Core/Worn.cs): what villagers take from each 64 m square of land (trees felled,
    // game killed, wild things picked; not the fields, which are sown again) is counted on the colony's pillars, and what they gather
    // where it's worn comes in thinner, at most 30% less. Ten days' rest and it's whole again. Only the colony's own work: a player's
    // axe and bow are never touched.
    internal static class WornLand
    {
        public const string Key = "jarldom_worn";
        private static readonly Dictionary<string, (string raw, Dictionary<(int, int), Worn.Zone> zones)> Read = new Dictionary<string, (string, Dictionary<(int, int), Worn.Zone>)>();
        private static readonly Dictionary<string, float> Unsaved = new Dictionary<string, float>(); // (colony -> when its counts were last written)
        public static void Clear() { Read.Clear(); Unsaved.Clear(); }
        public static double Today(Colony c) => c.Day + c.Hour / 24.0;
        public static Dictionary<(int, int), Worn.Zone> Counts(Colony c)
        {
            if (Read.TryGetValue(c.Id, out var r) && (Unsaved.ContainsKey(c.Id) || r.raw == (c.Z?.GetString(Key, "") ?? ""))) return r.zones; // (counts not yet written are the newest)
            string raw = c.Z?.GetString(Key, "") ?? "";
            var zones = Worn.Load(raw);
            Read[c.Id] = (raw, zones);
            return zones;
        }
        // One more use of the land there (the colony's owner counts; written to the pillars every half minute, not each felled tree).
        public static void Use(Colony c, Vector3 at, string kind)
        {
            if (c == null || !c.Valid || !c.IsOwner) return;
            Worn.Use(Counts(c), Worn.ZoneOf(at.x, at.z), kind, Today(c));
            if (!Unsaved.ContainsKey(c.Id)) Unsaved[c.Id] = Time.time;
            Flush(c, false);
        }
        public static void Flush(Colony c, bool now)
        {
            if (!Unsaved.TryGetValue(c.Id, out float since) || !now && Time.time - since < 30f || !c.Valid || !c.IsOwner) return;
            string raw = Worn.Save(Counts(c), Today(c));
            c.Z.Set(Key, raw);
            Read[c.Id] = (raw, Read[c.Id].zones);
            Unsaved.Remove(c.Id);
        }
        public static float Yield(Colony c, Vector3 at) => c == null ? 1f : Worn.YieldAt(Counts(c), Worn.ZoneOf(at.x, at.z), Today(c));
        // Days of rest till the square there yields in full again (0: it does now).
        public static int RestDays(Colony c, Vector3 at)
        {
            if (!Counts(c).TryGetValue(Worn.ZoneOf(at.x, at.z), out Worn.Zone z)) return 0;
            float left = Worn.Left(z, Today(c));
            if (left <= Worn.Threshold) return 0;
            return Mathf.CeilToInt((float)((left - Worn.Threshold) / z.Count * Worn.RecoverDays)); // (it ebbs evenly: z.Count over RecoverDays)
        }
        // What the work dropped there, thinned to the land's yield: each stack kept at its share (a share left over kept by chance),
        // what's lost taken away before anyone gathers it.
        public static void Thin(Colony c, List<ItemDrop> drops, Vector3 at)
        {
            float y = Yield(c, at);
            if (y >= 0.999f) return;
            foreach (ItemDrop d in drops.ToList())
            {
                if (d == null) continue;
                ZNetView v = d.GetComponent<ZNetView>();
                if (v == null || !v.IsValid()) continue;
                Work.TakeOver(d.gameObject);
                if (!v.IsOwner()) continue; // (another game's: left as it fell)
                int stack = d.m_itemData.m_stack;
                int keep = Mathf.FloorToInt(stack * y + Random.value);
                if (keep >= stack) continue;
                if (keep <= 0) { drops.Remove(d); ZNetScene.instance.Destroy(d.gameObject); }
                else d.SetStack(keep);
            }
        }
        // The most worn of the colony's work zones (for the Needs-you list): its name, where, how much less, the days of rest.
        public static (Zone zone, float yield, int days)? Worst(Colony c)
        {
            (Zone, float, int)? worst = null;
            foreach (Zone z in c.R.Zones.Where(z => z.Type == ZoneType.Woodlot || z.Type == ZoneType.Hunting || z.Type == ZoneType.Foraging))
            {
                Vector3 at = Zones.Centre(z);
                float y = Yield(c, at);
                if (y < 0.9f && (worst == null || y < worst.Value.Item2)) worst = (z, y, RestDays(c, at));
            }
            return worst;
        }
    }
}
