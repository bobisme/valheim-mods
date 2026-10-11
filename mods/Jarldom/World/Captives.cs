using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // Captives: in a Fuling raid, a villager knocked down and injured may be dragged off (one in two). They're held at the nearest Fuling
    // camp, pinned on the map, for three days: go there (within 30 m) to free them, or pay a ransom from the colony window; after that
    // they're lost. The captive waits in the colony's record like a trader on the road (Fallen, Killer "captive:x,z,day").
    internal static class Captives
    {
        public const string Prefix = "captive:";
        private const float RescueReach = 30f; private const int Days = 3;
        private static readonly string[] Camps = { "GoblinCamp2", "GoblinCamp1", "GoblinCamp" };

        // From the brain, when an injury begins: taken, if it's the Fulings' raid.
        public static bool Take(Brain b, bool force = false)
        {
            Colony c = b.Colony;
            if (c == null || !force && (!Tribute.Raiding(c) || !c.RaidOn || UnityEngine.Random.value >= 0.5f)) return false;
            Vector3 camp = Camp(c.Position);
            b.Say("No! Let go of me!", 4);
            c.Log("text", b.S.Name, "", $"The Fulings dragged {b.S.FullName} off to their camp, {Mathf.RoundToInt(Utils.DistanceXZ(camp, c.Position))} m to the {ScoutTask.Compass(camp - c.Position)}. Free them within {Days} days, or pay a ransom.");
            b.Away(Prefix + camp.x.ToString("0", CultureInfo.InvariantCulture) + "," + camp.z.ToString("0", CultureInfo.InvariantCulture) + "," + c.Day);
            Net.Pin(camp, b.S.Name + ", held by the Fulings", true);
            return true;
        }
        private static Vector3 Camp(Vector3 from)
        {
            foreach (string name in Camps)
                if (ZoneSystem.instance != null && ZoneSystem.instance.FindClosestLocation(name, from, out ZoneSystem.LocationInstance loc)) return loc.m_position;
            float a = UnityEngine.Random.Range(0, Mathf.PI * 2);
            return from + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * 600f; // (no camp this game knows of: somewhere out there)
        }
        public static bool Is(Fallen f) => f.Killer != null && f.Killer.StartsWith(Prefix);
        public static (Vector3 at, int day) Where(Fallen f)
        {
            string[] p = f.Killer.Substring(Prefix.Length).Split(',');
            float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x); float.TryParse(p.Length > 1 ? p[1] : "0", NumberStyles.Float, CultureInfo.InvariantCulture, out float z);
            int day = p.Length > 2 && int.TryParse(p[2], out int d) ? d : 0;
            return (new Vector3(x, 0, z), day);
        }
        public static IEnumerable<(string vid, string name, Vector3 at, int daysLeft)> Held(Colony c) =>
            c.R.Fallen.Where(Is).Select(f => { var (at, day) = Where(f); return (f.Vid, c.R.Row(f.Vid)?.Name ?? "someone", at, Math.Max(0, day + Days - c.Day)); });
        public static int Ransom(Colony c) => Renown.TributeAsk(c.Renown, c.R.Flag("tribute:level"));

        // Every game, every few seconds: its own player near a camp frees whoever is held there.
        private static float _next;
        public static void Tick()
        {
            if (Time.time < _next || Player.m_localPlayer == null) return;
            _next = Time.time + 3;
            Vector3 me = Player.m_localPlayer.transform.position;
            foreach (Colony c in Colony.All)
                foreach (var held in Held(c).ToList())
                    if (Utils.DistanceXZ(me, held.at) < RescueReach) Net.Act(c, "rescue", held.vid + "|" + Player.m_localPlayer.GetPlayerName());
        }
        // On the colony owner, at dawn: three days gone and they're lost to the Fulings.
        public static void Dawn(Colony c)
        {
            foreach (Fallen f in c.R.Fallen.Where(Is).ToList())
            {
                var (at, day) = Where(f);
                if (c.Day < day + Days) continue;
                c.R.Fallen.Remove(f);
                RosterRow row = c.R.Row(f.Vid); if (row != null) row.State = SoulState.Lost;
                c.ReleaseBed(f.Vid);
                Net.Pin(at, "", false);
                c.Log("lost", row?.Name ?? "someone", "the Fulings", $"No one came for {row?.Name ?? "them"}: the Fulings kept them, and they are lost to the colony.");
                foreach (Brain b in Villagers.All.Where(b => b.S.Colony == c.Id && b.S.Bonds.TryGetValue(f.Vid, out int o) && o > 40)) { Mood.Add(b.S, "grief", b.Now); b.Dirty(); }
            }
        }
        // On the owner: freed (rescued or ransomed): home at once, rising at their bed.
        public static void Free(Colony c, string vid, string how)
        {
            Fallen f = c.R.Fallen.FirstOrDefault(x => x.Vid == vid && Is(x));
            if (f == null) return;
            var (at, _) = Where(f);
            if (how == "ransom")
            {
                int cost = Ransom(c);
                Container t = Stock.Treasury(c);
                int have = t != null ? Stock.Coins(t) : 0;
                if (t == null || have < cost || !Stock.TakeCoins(t, cost)) { c.Tell($"The treasury holds {have} coins: the Fulings want {cost} for them."); return; }
                c.Log("text", c.R.Row(vid)?.Name ?? "", "", $"The colony paid the Fulings {cost} coins, and {c.R.Row(vid)?.Name} was let go.");
            }
            else
            {
                c.Log("text", c.R.Row(vid)?.Name ?? "", how, $"{how} came for {c.R.Row(vid)?.Name} at the Fuling camp and set them free.");
                c.R.Deeds += Renown.Deed("raidheld");
            }
            f.Killer = "freed"; f.At = c.Now - Plugin.Instance.RespawnSeconds.Value; // (Rise brings them home on its next pass)
            Net.Pin(at, "", false);
            c.MarkDirty();
        }
    }
}
