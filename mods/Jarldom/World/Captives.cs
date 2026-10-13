using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // Captives: in a Fuling raid, a villager knocked down and injured may be dragged off (one in two, once a fall). They're held at the
    // nearest Fuling camp, pinned on the map, for three days: go there (within 30 m) to free them, or pay a ransom from the colony window;
    // after that they're lost. The captive waits in the colony's record like a trader on the road (Fallen, Killer "captive:x,z,day").
    // The camp is found by the server (only it knows the world's camps) and kept as a global key, "jarldom_captive_<colony>_<vid> x,z",
    // so a rescuer far from the colony's pillars can free them: their game sets "jarldom_rescue_<colony>_<vid> <name>", which the colony
    // reads when it next runs.
    internal static class Captives
    {
        public const string Prefix = "captive:";
        private const float RescueReach = 30f; private const int Days = 3;
        private static readonly string[] Camps = { "GoblinCamp2", "GoblinCamp1", "GoblinCamp" };
        public static string KeyFor(string colony, string vid) => $"jarldom_captive_{colony}_{vid}";
        private static string RescueKey(string colony, string vid) => $"jarldom_rescue_{colony}_{vid}";

        // From the brain, when an injury begins: taken, if it's the Fulings' raid.
        public static bool Take(Brain b, bool force = false)
        {
            Colony c = b.Colony;
            if (c == null || !force && (!Tribute.Raiding(c) || !c.RaidOn || UnityEngine.Random.value >= 0.5f)) return false;
            b.Say(new[] { "No! Let go of me!", "Let go! Let go of me!", "Get your hands off me!", "Unhand me, you filth!", "No! Leave me be!" }[UnityEngine.Random.Range(0, 5)], 4);
            c.Log("text", b.S.Name, "", $"The Fulings dragged {b.S.FullName} off to their camp (pinned on the map). Free them within {Days} days, or pay a ransom.");
            string vid = b.S.Vid, name = b.S.Name;
            b.Away(Prefix + "?,?," + c.Day);
            Net.FindCamp(c, vid, name);
            return true;
        }
        // On the server.
        public static Vector3 FindCamp(Vector3 from)
        {
            foreach (string name in Camps)
                if (ZoneSystem.instance != null && ZoneSystem.instance.FindClosestLocation(name, from, out ZoneSystem.LocationInstance loc)) return loc.m_position;
            // No camp in this world: somewhere out on dry land.
            var rng = new System.Random(Mathf.RoundToInt(from.x + from.z * 7));
            for (int i = 0; i < 100; i++)
            {
                double a = rng.NextDouble() * Math.PI * 2;
                Vector3 p = from + new Vector3((float)Math.Sin(a), 0, (float)Math.Cos(a)) * (400 + (float)rng.NextDouble() * 400);
                if (WorldGenerator.instance != null && WorldGenerator.instance.GetHeight(p.x, p.z) > (ZoneSystem.instance?.m_waterLevel ?? 30) + 1) return p;
            }
            return from + Vector3.forward * 500;
        }
        // On the colony owner, the server's answer: the camp into the captive's record.
        public static void CampFound(string colony, string vid, Vector3 camp)
        {
            Colony c = Colony.Of(colony);
            Fallen f = c?.R.Fallen.FirstOrDefault(x => x.Vid == vid && Is(x));
            if (f == null || !c.IsOwner) return;
            int day = Where(c, f).day;
            f.Killer = Prefix + Mathf.RoundToInt(camp.x).ToString(CultureInfo.InvariantCulture) + "," + Mathf.RoundToInt(camp.z).ToString(CultureInfo.InvariantCulture) + "," + day;
            c.Log("text", c.R.Row(vid)?.Name ?? "", "", $"{c.R.Row(vid)?.Name} is held at a Fuling camp {Mathf.RoundToInt(Utils.DistanceXZ(camp, c.Position))} m to the {ScoutTask.Compass(camp - c.Position)}.");
            c.MarkDirty();
        }
        public static bool Is(Fallen f) => f.Killer != null && f.Killer.StartsWith(Prefix);
        public static (Vector3 at, int day) Where(Colony c, Fallen f)
        {
            string[] p = f.Killer.Substring(Prefix.Length).Split(',');
            int day = p.Length > 2 && int.TryParse(p[2], out int d) ? d : 0;
            if (p[0] == "?" && Camp(c.Id, f.Vid) is Vector3 known) return (known, day); // (the server's answer hasn't come back to this game)
            float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x); float.TryParse(p.Length > 1 ? p[1] : "0", NumberStyles.Float, CultureInfo.InvariantCulture, out float z);
            return (new Vector3(x, 0, z), day);
        }
        private static Vector3? Camp(string colony, string vid)
        {
            if (ZoneSystem.instance == null || !ZoneSystem.instance.GetGlobalKey(KeyFor(colony, vid), out string v)) return null;
            string[] xz = v.Split(',');
            return xz.Length == 2 && float.TryParse(xz[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) && float.TryParse(xz[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) ? new Vector3(x, 0, z) : (Vector3?)null;
        }
        public static IEnumerable<(string vid, string name, Vector3 at, int daysLeft)> Held(Colony c) =>
            c.R.Fallen.Where(Is).Select(f => { var (at, day) = Where(c, f); return (f.Vid, c.R.Row(f.Vid)?.Name ?? "someone", at, Math.Max(0, day + Days - c.Day)); });
        public static int Ransom(Colony c) => Renown.TributeAsk(c.Renown, c.R.Flag("tribute:level"));

        // Every game, every few seconds: its own player at a camp frees whoever is held there (by the global keys: no colony need be loaded).
        private static float _next; private static readonly HashSet<string> Sent = new HashSet<string>();
        public static void Tick()
        {
            if (Time.time < _next || Player.m_localPlayer == null || ZoneSystem.instance == null) return;
            _next = Time.time + 3;
            Vector3 me = Player.m_localPlayer.transform.position;
            foreach (string line in ZoneSystem.instance.GetGlobalKeys())
            {
                if (!line.StartsWith("jarldom_captive_")) continue;
                int space = line.IndexOf(' ');
                if (space < 0) continue;
                string key = line.Substring(0, space); string[] parts = key.Substring("jarldom_captive_".Length).Split('_');
                if (parts.Length != 2) continue;
                Vector3? at = Camp(parts[0], parts[1]);
                if (at == null || Utils.DistanceXZ(me, at.Value) > RescueReach || !Sent.Add(key)) continue;
                ZoneSystem.instance.SetGlobalKey($"{RescueKey(parts[0], parts[1])} {Player.m_localPlayer.GetPlayerName().Replace(' ', '_')}");
                Player.m_localPlayer.Message(MessageHud.MessageType.Center, "You cut the captive's bonds: they run for home.");
            }
        }
        // On the colony owner, every few seconds: rescues done while the colony was far away.
        public static void Owner(Colony c)
        {
            if (ZoneSystem.instance == null) return;
            foreach (Fallen f in c.R.Fallen.Where(Is).ToList())
                if (ZoneSystem.instance.GetGlobalKey(RescueKey(c.Id, f.Vid), out string who)) Free(c, f.Vid, string.IsNullOrEmpty(who) ? "someone" : who.Replace('_', ' '));
        }
        private static void Forget(Colony c, string vid)
        {
            ZoneSystem.instance?.RemoveGlobalKey(KeyFor(c.Id, vid));
            ZoneSystem.instance?.RemoveGlobalKey(RescueKey(c.Id, vid));
            Net.Pin(KeyFor(c.Id, vid), Vector3.zero, "", false);
        }
        // On the colony owner, at dawn: three days gone and they're lost to the Fulings.
        public static void Dawn(Colony c)
        {
            foreach (Fallen f in c.R.Fallen.Where(Is).ToList())
            {
                var (at, day) = Where(c, f);
                if (c.Day < day + Days) continue;
                c.R.Fallen.Remove(f);
                RosterRow row = c.R.Row(f.Vid); if (row != null) row.State = SoulState.Lost;
                c.ReleaseBed(f.Vid);
                Forget(c, f.Vid);
                c.Log("lost", row?.Name ?? "someone", "the Fulings", $"No one came for {row?.Name ?? "them"}: the Fulings kept them, and they are lost to the colony.");
                foreach (Brain b in Villagers.All.Where(b => b.S.Colony == c.Id && b.S.Bonds.TryGetValue(f.Vid, out int o) && o > 40)) { Mood.Add(b.S, "grief", b.Now); b.Dirty(); }
            }
        }
        // On the owner: freed (rescued or ransomed): home at once, rising at their bed.
        public static void Free(Colony c, string vid, string how)
        {
            Fallen f = c.R.Fallen.FirstOrDefault(x => x.Vid == vid && Is(x));
            if (f == null) return;
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
            Forget(c, vid);
            c.MarkDirty();
        }
    }
}
