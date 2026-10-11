using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jarldom
{
    // The Fulings' tribute: now and then a herald stands at the colony's edge at dawn and names a price. Paid from the treasury by dusk, they
    // go (and the next demand is higher); unpaid, or the herald killed, they raid. Paying during their raid calls it off.
    // On the colony record: tribute:next (the day of the next herald), tribute:ask, tribute:level (demands paid), tribute:due (the day of a
    // demand standing), tribute:raid (their raid is on).
    internal static class Tribute
    {
        public const string HeraldKey = "jarldom_herald";
        private static readonly int HeraldHash = HeraldKey.GetStableHashCode();
        public static bool IsHerald(Character c) => c != null && c.GetComponent<ZNetView>()?.GetZDO()?.GetString(HeraldHash, "") is string s && s.Length > 0;
        // Only a player's blow harms the herald (it stands unarmed, and a stray wolf or wraith mustn't start a war).
        [HarmonyPatch(typeof(Character), "RPC_Damage")]
        private static class Untouchable
        {
            private static bool Prefix(Character __instance, HitData hit) => !IsHerald(__instance) || hit?.GetAttacker() is Player;
        }
        public static int Due(Colony c) => c.R.Flag("tribute:due") != 0 ? c.R.Flag("tribute:ask") : 0;
        public static bool Raiding(Colony c) => c.R.Flag("tribute:raid") != 0;

        public static void Dawn(Colony c, int day)
        {
            if (c.R.Flag("tribute:due") == 0) Leave(c); // (a herald left over from a save)
            if (Raiding(c) && day > c.R.Flag("tribute:raidday") + 1) { c.R.Flags.Remove("tribute:raid"); c.MarkDirty(); } // (their raid never came, or is long over)
            if (!Plugin.Instance.Raids.Value || !Plugin.Instance.Tribute.Value || c.R.Rank < Rank.Hamlet || c.Population < 3 || c.R.Flag("tribute:due") != 0) return;
            int next = c.R.Flag("tribute:next");
            if (next == 0) { c.R.Flags["tribute:next"] = day + 2; return; }
            if (day < next) return;
            Demand(c, day);
        }
        public static void Demand(Colony c, int day)
        {
            int ask = Renown.TributeAsk(c.Renown, c.R.Flag("tribute:level"));
            c.R.Flags["tribute:ask"] = ask; c.R.Flags["tribute:due"] = day;
            Herald(c);
            c.Log("text", "", "", $"A Fuling herald stands at the edge of {c.R.Name}: “{ask} coins by dusk, or we come and take them.” (Pay from the hall in the colony window.)");
            c.MarkDirty();
        }
        public static void Dusk(Colony c, int day) { if (c.R.Flag("tribute:due") != 0) Refused(c, day, "The sun is down and the Fulings were not paid. They are coming!"); }

        public static void Pay(Colony c, string who)
        {
            int ask = c.R.Flag("tribute:ask"); bool raid = Raiding(c);
            if (c.R.Flag("tribute:due") == 0 && !raid) { c.Tell("The Fulings ask nothing now."); return; }
            if (raid) ask = Mathf.CeilToInt(ask * 1.5f / 5f) * 5; // calling off a raid under way costs more
            Container t = Stock.Treasury(c);
            int have = t != null ? Stock.Coins(t) : 0;
            if (t == null || have < ask || !Stock.TakeCoins(t, ask)) { c.Tell($"The treasury holds {have} coins: the Fulings want {ask}."); return; }
            c.R.Flags.Remove("tribute:due");
            c.R.Flags["tribute:level"] = c.R.Flag("tribute:level") + 1;
            c.R.Deeds += Renown.Deed("tribute"); // (no glory in paying)
            c.R.Flags["tribute:next"] = c.Day + Random.Range(6, 11);
            if (raid) { c.R.Flags.Remove("tribute:raid"); c.R.Flags["tribute:bought"] = 1; Net.Event("", c.Position); Compat.CallOffSiege(c.Position, c.Radius + 80); }
            Leave(c);
            foreach (Brain b in Villagers.All.Where(b => b.S.Colony == c.Id)) { Mood.Add(b.S, "tribute", b.Now); b.Dirty(); }
            c.Log("text", who, "", raid ? $"{who} paid the Fulings {ask} coins, and their raid turned back. They'll be back, and want more."
                                         : $"{who} paid the Fulings {ask} coins. The herald goes; they'll be back, and want more.");
            c.MarkDirty();
        }
        private static void Refused(Colony c, int day, string why)
        {
            c.R.Flags.Remove("tribute:due");
            c.R.Flags["tribute:next"] = day + Random.Range(6, 11);
            c.R.Flags["tribute:raid"] = 1; c.R.Flags["tribute:raidday"] = day;
            Leave(c);
            c.Log("text", "", "", why);
            Net.Event(Plugin.Instance.TributeEvent.Value, c.Position);
            c.MarkDirty();
        }
        // The colony's Defense calls this when a raid ends: their raid is over either way.
        public static void RaidOver(Colony c) { if (Raiding(c)) { c.R.Flags.Remove("tribute:raid"); c.MarkDirty(); } }

        private static void Herald(Colony c)
        {
            GameObject prefab = ZNetScene.instance.GetPrefab("Goblin");
            if (prefab == null) return;
            Vector3 dir = Random.insideUnitSphere; dir.y = 0; dir = dir.sqrMagnitude < 0.01f ? Vector3.forward : dir.normalized;
            Vector3 at = Colony.Spot(c.Position + dir * Mathf.Min(c.Radius - 10, 35), 8f) ?? c.Position + dir * 30;
            GameObject g = Object.Instantiate(prefab, at, Quaternion.LookRotation(-dir));
            ZDO z = g.GetComponent<ZNetView>().GetZDO();
            z.Set(HeraldKey, c.Id);
            z.Set(Draugr.NameKey, "Fuling herald");
            Character ch = g.GetComponent<Character>();
            ch.SetLevel(2);
            Assets.Effect("vfx_spawn", at);
        }
        public static void Leave(Colony c)
        {
            foreach (Character h in Character.GetAllCharacters().Where(IsHerald).ToList())
            {
                ZNetView v = h.GetComponent<ZNetView>();
                if (v.GetZDO().GetString(HeraldKey, "") != c.Id) continue;
                if (!v.IsOwner()) v.ClaimOwnership();
                Assets.Effect("vfx_spawn", h.transform.position);
                ZNetScene.instance.Destroy(h.gameObject);
            }
        }

        // The herald stands and waits: no wandering, no fighting (unless struck, when it's a raid at once).
        [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
        private static class Stand
        {
            private static bool Prefix(MonsterAI __instance, ref bool __result)
            {
                Character ch = __instance.GetComponent<Character>();
                if (!IsHerald(ch)) return true;
                Colony c = Colony.Of(ch.GetComponent<ZNetView>().GetZDO().GetString(HeraldKey, ""));
                __instance.StopMoving();
                if (c != null) { Vector3 d = c.Position - ch.transform.position; d.y = 0; if (d.sqrMagnitude > 0.1f) __instance.LookTowards(d.normalized); }
                __result = true;
                return false;
            }
        }
        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        private static class Slain
        {
            private static void Prefix(Character __instance)
            {
                if (!IsHerald(__instance)) return;
                Colony c = Colony.Of(__instance.GetComponent<ZNetView>().GetZDO().GetString(HeraldKey, ""));
                if (c == null) return;
                if (c.IsOwner) Plugin.Guard(() => { if (c.R.Flag("tribute:due") != 0) Refused(c, c.Day, "The Fuling herald was cut down. The Fulings are coming!"); });
                else Net.Act(c, "heraldslain", "");
            }
        }
        public static void HeraldSlain(Colony c) { if (c.R.Flag("tribute:due") != 0) Refused(c, c.Day, "The Fuling herald was cut down. The Fulings are coming!"); }
    }
}
