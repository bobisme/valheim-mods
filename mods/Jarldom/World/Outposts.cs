using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // Outposts: a second colony (300 m or more away) linked to one of its founder's others. It keeps its own rank and roster, and its
    // wages come out of the main colony's treasury when its own can't meet them. Colonies are far apart, so the links live in global keys
    // (saved with the world, seen by every game): "jarldom_colony_<id> x,z,founder", "jarldom_outpost_<id> <parent>", and the coins owed
    // to outposts' wages, "jarldom_owed_<parent> <coins>", which the main colony pays out of its treasury when it next runs.
    internal static class Outposts
    {
        private const int MaxOwed = 5000;
        private static string ColonyKey(string id) => "jarldom_colony_" + id;
        private static string LinkKey(string id) => "jarldom_outpost_" + id;
        private static string OwedKey(string id) => "jarldom_owed_" + id;
        private static ZoneSystem Z => ZoneSystem.instance;

        // On the owner, now and then: where this colony stands and who founded it, for the others to find.
        public static void Announce(Colony c)
        {
            if (Z == null) return;
            string value = $"{Mathf.RoundToInt(c.Position.x)},{Mathf.RoundToInt(c.Position.z)},{c.R.FounderName.Replace(' ', '_').ToLowerInvariant()}";
            if (!Z.GetGlobalKey(ColonyKey(c.Id), out string had) || had != value) Z.SetGlobalKey(ColonyKey(c.Id) + " " + value);
            PayOwed(c);
        }
        // A colony disbanded: its marks in the world go (an outpost of it stands alone).
        public static void Forget(Colony c)
        {
            if (Z == null) return;
            Z.RemoveGlobalKey(ColonyKey(c.Id)); Z.RemoveGlobalKey(LinkKey(c.Id)); Z.RemoveGlobalKey(OwedKey(c.Id));
            foreach (string line in Z.GetGlobalKeys().Where(l => l.StartsWith("jarldom_outpost_") && l.EndsWith(" " + c.Id)).ToList()) Z.RemoveGlobalKey(line.Substring(0, line.IndexOf(' ')));
        }
        public static string Parent(Colony c) => Z != null && Z.GetGlobalKey(LinkKey(c.Id), out string p) ? p : "";
        public static bool IsOutpost(Colony c) => Parent(c) != "";
        // The founder's other colonies far enough off to be this one's main colony: id → where.
        public static List<(string id, Vector3 at)> Candidates(Colony c)
        {
            var list = new List<(string, Vector3)>();
            if (Z == null) return list;
            string me = c.R.FounderName.Replace(' ', '_').ToLowerInvariant();
            foreach (string line in Z.GetGlobalKeys().Where(l => l.StartsWith("jarldom_colony_")))
            {
                int space = line.IndexOf(' '); if (space < 0) continue;
                string id = line.Substring("jarldom_colony_".Length, space - "jarldom_colony_".Length);
                string[] v = line.Substring(space + 1).Split(',');
                if (id == c.Id || v.Length < 3 || v[2] != me) continue;
                if (!float.TryParse(v[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) || !float.TryParse(v[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float zz)) continue;
                Vector3 at = new Vector3(x, 0, zz);
                if (Utils.DistanceXZ(at, c.Position) >= Renown.MinApart && !Z.GetGlobalKey(LinkKey(id))) list.Add((id, at)); // (not an outpost itself)
            }
            return list;
        }
        public static Vector3? Where(string id)
        {
            if (Z == null || !Z.GetGlobalKey(ColonyKey(id), out string v)) return null;
            string[] p = v.Split(',');
            return p.Length >= 2 && float.TryParse(p[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x) && float.TryParse(p[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float z) ? new Vector3(x, 0, z) : (Vector3?)null;
        }
        // Every colony the world knows of (from the marks they leave): id → where.
        public static List<(string id, Vector3 at)> All()
        {
            var list = new List<(string, Vector3)>();
            if (Z == null) return list;
            foreach (string line in Z.GetGlobalKeys().Where(l => l.StartsWith("jarldom_colony_")))
            {
                int space = line.IndexOf(' '); if (space < 0) continue;
                string id = line.Substring("jarldom_colony_".Length, space - "jarldom_colony_".Length);
                if (Where(id) is Vector3 at) list.Add((id, at));
            }
            return list;
        }
        // An outpost raised by an expedition: linked to the crew's home (or, if that's an outpost itself, to its main colony), whoever
        // founded either: the jarl who led the crew out runs both.
        public static void LinkFromExpedition(Colony c, string home, string who)
        {
            if (Z == null || string.IsNullOrEmpty(home) || home == c.Id) return;
            string parent = Z.GetGlobalKey(LinkKey(home), out string up) && !string.IsNullOrEmpty(up) ? up : home;
            if (!(Where(parent) is Vector3 at) || Utils.DistanceXZ(at, c.Position) < Renown.MinApart) return;
            Z.SetGlobalKey(LinkKey(c.Id) + " " + parent);
            c.Log("text", who, "", $"{c.R.Name} was raised by an expedition from the colony {Mathf.RoundToInt(Utils.DistanceXZ(at, c.Position))} m to the {ScoutTask.Compass(at - c.Position)}, and is its outpost: that colony's treasury meets the wages this one can't.");
        }
        public static int OutpostsOf(Colony c) => Z == null ? 0 : Z.GetGlobalKeys().Count(l => l.StartsWith("jarldom_outpost_") && l.EndsWith(" " + c.Id));

        // On the owner: linked, or standing alone again.
        public static void Link(Colony c, string parent, string who)
        {
            if (Z == null) return;
            if (parent == "") { Z.RemoveGlobalKey(LinkKey(c.Id)); c.Log("text", who, "", $"{c.R.Name} stands alone now, an outpost no longer."); return; }
            if (!Candidates(c).Any(x => x.id == parent)) { c.Tell("That colony can't be this one's main colony."); return; }
            Z.SetGlobalKey(LinkKey(c.Id) + " " + parent);
            Vector3 at = Where(parent) ?? Vector3.zero;
            c.Log("text", who, "", $"{c.R.Name} is now an outpost of the colony {Mathf.RoundToInt(Utils.DistanceXZ(at, c.Position))} m to the {ScoutTask.Compass(at - c.Position)}: its treasury meets the wages this one can't.");
        }
        // From an outpost's payday that its own treasury can't meet: the main colony's treasury owes them (paid out when it next runs).
        public static bool Credit(Colony c, int due)
        {
            string parent = Parent(c);
            if (parent == "" || Z == null) return false;
            int owed = Z.GetGlobalKey(OwedKey(parent), out string v) && int.TryParse(v, out int n) ? n : 0;
            if (owed + due > MaxOwed) return false; // (the main colony hasn't paid what it owes already)
            Z.SetGlobalKey($"{OwedKey(parent)} {owed + due}");
            c.Log("text", "", "", $"The wages ({due} coins) are owed by the main colony's treasury.");
            return true;
        }
        private static int _lastWarnDay = -1;
        private static void PayOwed(Colony c)
        {
            if (!Z.GetGlobalKey(OwedKey(c.Id), out string v) || !int.TryParse(v, out int owed) || owed <= 0) return;
            Container t = Stock.Treasury(c);
            if (t != null && Stock.Coins(t) >= owed && Stock.TakeCoins(t, owed))
            {
                Z.RemoveGlobalKey(OwedKey(c.Id));
                c.Log("text", "", "", $"The treasury sent {owed} coins for the outposts' wages.");
            }
            else if (_lastWarnDay != c.Day) { _lastWarnDay = c.Day; c.Log("text", "", "", $"The outposts' wages are owed: {owed} coins, and the treasury can't meet them."); }
        }
    }
}
