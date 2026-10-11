using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // A colony that runs while you're gone. While it's loaded, what each profession brings to the stores is counted against the hours it
    // works (flags made:<prof>:<item> and worked:<prof>, in tenths of an hour). When its pillars load again after time unloaded, the
    // missing hours are made up in the abstract at those same rates: goods into the stores (never more than they hold), meals out of
    // them, wages on the days they fell due, raids rolled; and a "While you were away" entry in the saga says what happened.
    internal static class CatchUp
    {
        public const string LiveKey = "lastlive";
        private const float MinHours = 2f, MaxDays = 10f;

        // ---- counting, while live ----
        private static readonly Dictionary<string, float> Worked = new Dictionary<string, float>(); // colony|prof → seconds not yet in the record
        public static void Work(Brain b, float seconds)
        {
            if (b.Colony == null || !b.Colony.IsOwner) return;
            string key = b.Colony.Id + "|" + b.S.Prof;
            Worked[key] = (Worked.TryGetValue(key, out float s) ? s : 0) + seconds;
        }
        public static void Made(Brain b, ItemDrop.ItemData item, int count)
        {
            if (b?.Colony == null || !b.Colony.IsOwner || item?.m_dropPrefab == null || count <= 0) return;
            if (item.m_dropPrefab.name == "Coins" || Items.IsRelic(item)) return;
            if (!Gatherers.Contains(b.S.Prof)) return; // (only what's taken from the land: refining and carrying back make nothing new)
            string key = "made:" + b.S.Prof + ":" + item.m_dropPrefab.name;
            b.Colony.R.Flags[key] = b.Colony.R.Flag(key) + count;
            b.Colony.MarkDirty();
        }
        // Every half minute on the owner: the hours into the record, and the colony marked live.
        public static void Live(Colony c)
        {
            foreach (string key in Worked.Keys.Where(k => k.StartsWith(c.Id + "|")).ToList())
            {
                int tenths = Mathf.FloorToInt(Worked[key] / (float)(Clock.Hour / 10));
                if (tenths <= 0) continue;
                Worked[key] -= tenths * (float)(Clock.Hour / 10);
                string flag = "worked:" + key.Substring(c.Id.Length + 1);
                c.R.Flags[flag] = c.R.Flag(flag) + tenths;
            }
            c.R.Flags[LiveKey] = (int)c.Now;
            c.MarkDirty();
        }
        public static void Forget() => Worked.Clear();
        private static readonly HashSet<Prof> Gatherers = new HashSet<Prof> { Prof.Woodcutter, Prof.Miner, Prof.Forager, Prof.Farmer, Prof.Hunter, Prof.Fisherman };

        // What one villager of a profession makes in an hour of work, by item (from what was counted).
        public static Dictionary<string, float> Rates(Colony c, Prof p)
        {
            float hours = c.R.Flag("worked:" + p) / 10f;
            var rates = new Dictionary<string, float>();
            if (hours < 1f) return rates; // too little seen to say
            string prefix = "made:" + p + ":";
            foreach (var kv in c.R.Flags.Where(f => f.Key.StartsWith(prefix))) rates[kv.Key.Substring(prefix.Length)] = kv.Value / hours;
            return rates;
        }

        // ---- making up the time ----
        // From the owner's tick: once the colony's people have loaded after it was away (or right away if none load).
        public static bool Due(Colony c, out double away)
        {
            int last = c.R.Flag(LiveKey);
            away = c.Now - last;
            return last > 0 && away > Clock.Hour * MinHours;
        }
        public static void Run(Colony c, double away)
        {
            double hours = Math.Min(away / Clock.Hour, MaxDays * 24);
            int days = (int)(hours / 24);
            var people = c.R.Living.ToList();
            var lines = new List<string>();
            // Goods, at the counted rates (a little under: nobody's watching).
            var made = new Dictionary<string, int>();
            foreach (var group in people.GroupBy(r => r.Prof))
            {
                string prefix = "made:" + group.Key + ":";
                foreach (var kv in c.R.Flags.Where(f => f.Key.StartsWith(prefix)).ToList())
                {
                    int n = Away.Produce(kv.Value, c.R.Flag("worked:" + group.Key), group.Count(), hours);
                    string item = kv.Key.Substring(prefix.Length);
                    if (n > 0) made[item] = (made.TryGetValue(item, out int m) ? m : 0) + n;
                }
            }
            int stored = 0, unstored = 0;
            foreach (var kv in made)
            {
                GameObject prefab = ObjectDB.instance?.GetItemPrefab(kv.Key);
                ItemDrop drop = prefab != null ? prefab.GetComponent<ItemDrop>() : null;
                if (drop == null) continue;
                int left = kv.Value;
                int stack = Math.Max(1, drop.m_itemData.m_shared.m_maxStackSize);
                foreach (Container chest in Chests(c, Stock.Of(prefab)))
                {
                    ContainerAccess.TakeOver(chest);
                    while (left > 0)
                    {
                        int before = Count(chest, kv.Key);
                        chest.GetInventory().AddItem(prefab, Math.Min(left, stack));
                        int put = Count(chest, kv.Key) - before;
                        if (put <= 0) break; // full
                        left -= put; stored += put;
                    }
                    if (left <= 0) break;
                }
                unstored += Math.Max(0, left);
            }
            if (made.Count > 0)
                lines.Add("they made " + string.Join(", ", made.OrderByDescending(kv => kv.Value).Take(6).Select(kv => $"{kv.Value} {Name(kv.Key)}")) +
                          (unstored > 0 ? $" ({unstored} more didn't fit in the stores)" : ""));
            // Meals: three a day each, from the food chests (the plainest first).
            int meals = people.Count * 3 * Math.Max(1, days), eaten = 0;
            foreach (var (chest, item) in Stock.Food(c, c.Position).OrderBy(f => f.item.m_shared.m_food).ToList())
            {
                if (eaten >= meals) break;
                if (ContainerAccess.InUse(chest)) continue;
                int n = Math.Min(item.m_stack, meals - eaten);
                ContainerAccess.TakeOver(chest);
                chest.GetInventory().RemoveItem(item, n);
                eaten += n;
            }
            lines.Add(eaten >= meals ? $"they ate {eaten} meals from the stores" : $"they ate {eaten} of the {meals} meals they needed: the stores ran out");
            bool hungry = eaten < meals;
            if (hungry) foreach (Brain b in Villagers.All.Where(b => b.S.Colony == c.Id)) { Mood.Satisfy(b.S, Need.Food, -40); b.Dirty(); }
            // Wages on each payday that fell while away (the loaded villagers are paid as at any payday).
            int paydays = 0;
            for (int d = c.Day - days; d <= c.Day; d++) if (Plugin.Instance.Payroll.Value && Payroll.IsPayday(d, c.R.LastPayday)) { c.Payday(d); paydays++; }
            if (paydays > 0) lines.Add(paydays == 1 ? "a payday came" : $"{paydays} paydays came");
            // Raids: one roll a day, more likely as renown grows; guards hold them, or the colony suffers.
            int raids = 0, held = 0;
            var rng = new System.Random(c.Id.GetStableHashCode() ^ c.Day);
            bool guards = people.Any(r => r.Prof == Prof.Guard);
            for (int d = 0; d < days; d++)
                if (Plugin.Instance.Raids.Value && rng.NextDouble() < 0.08 * Renown.RaidScale(c.Renown)) { raids++; if (guards) { held++; c.R.Deeds += Renown.Deed("raidheld"); } }
            if (raids > 0) lines.Add(held == raids ? (raids == 1 ? "a raid came and was held" : $"{raids} raids came and all were held") : $"{raids} raids came; {raids - held} got in among the houses (no guards to stop them)");
            if (raids > held) foreach (Brain b in Villagers.All.Where(b => b.S.Colony == c.Id)) { Mood.Satisfy(b.S, Need.Safety, -30); b.Dirty(); }
            // The days' dawns and dusks are done with: no flood of Things and arrivals at once.
            c.R.LastDawn = Math.Max(c.R.LastDawn, c.Day - 1);
            string span = days >= 1 ? $"{days} day{(days == 1 ? "" : "s")}" : $"{Math.Round(hours)} hours";
            c.Log("text", "", "", $"While you were away ({span}): " + string.Join("; ", lines) + ".");
            c.Tell($"While you were away ({span}), {c.R.Name} carried on: see the saga.", true);
            c.R.Flags[LiveKey] = (int)c.Now;
            c.MarkDirty(); c.Flush(); // (at once: an owner change right after mustn't see the old time and make it up again)
            Plugin.Log($"Jarldom: {c.R.Id} caught up {hours:0.0} h: {stored} goods stored, {eaten}/{meals} meals, {paydays} paydays, {raids} raids");
        }
        private static IEnumerable<Container> Chests(Colony c, ItemDrop.ItemData item)
        {
            Container best = Stock.Destination(c, item, c.Position);
            if (best != null && !ContainerAccess.InUse(best)) yield return best;
            foreach (var x in Stock.Chests(c, c.Position).Where(x => x.category != "treasury" && x.chest != best && !ContainerAccess.InUse(x.chest) && (x.category == "any" || x.category == Stock.Category(item))))
                yield return x.chest;
        }
        private static int Count(Container chest, string prefab) => chest.GetInventory().GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == prefab).Sum(i => i.m_stack);
        private static string Name(string prefab)
        {
            string n = ObjectDB.instance?.GetItemPrefab(prefab)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name;
            return n == null ? prefab : Localization.instance.Localize(n).ToLower();
        }
    }
}
