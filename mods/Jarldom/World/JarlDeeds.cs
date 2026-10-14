using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Jarldom
{
    // The jarls' deeds (Core/Jarls.cs): counted on the colony's owner as they happen at home, and a boss slain anywhere carried home on
    // the player's own character until they next come to a colony of theirs. A byname won is told in the saga.
    internal static class JarlDeeds
    {
        public const string PendingKey = "jarldom_deeds"; // (player custom data: "boss:Bonemass;boss:Eikthyr", deeds done away from home)
        private static float _next, _nextHome;

        // ---- on the colony's owner ----
        public static void Add(Colony c, string who, string what, int by = 1)
        {
            if (c == null || !c.IsOwner || string.IsNullOrEmpty(who)) return;
            Tally.Add(c.R.Flags, who, what, by);
            Title(c, who); c.MarkDirty();
        }
        // (the "tally" action: deeds a player's game carried here)
        public static void Delivered(Colony c, string who, string arg)
        {
            if (!c.AllowedName(who)) return;
            foreach (string d in arg.Split(';').Where(x => x != ""))
            {
                string[] f = d.Split(':');
                if (f[0] == "boss" && f.Length > 1) { Tally.Boss(c.R.Flags, who, f[1]); c.Log("text", "", "", $"{who} slew {Boss(f[1])}."); }
            }
            Title(c, who); c.MarkDirty();
        }
        // A new byname: in the saga, and told.
        public static void Title(Colony c, string who)
        {
            var (id, title, _) = Bynames.For(Tally.Of(c.R.Flags, who));
            if (id == Tally.Get(c.R.Flags, who, Tally.Title)) return;
            Tally.Set(c.R.Flags, who, Tally.Title, id);
            if (id == 0) return;
            c.Log("byname", who, title);
            c.Tell($"The colony has a name for you now, {who}: {who} {title}.", true);
        }
        // Every half minute: the colony's people at home are seen (the days they were away counted), the feast caller credited.
        public static void Tick(Colony c)
        {
            if (!c.IsOwner || Time.time < _nextHome) return;
            _nextHome = Time.time + 30f;
            foreach (Player p in Player.GetAllPlayers().Where(p => p != null && !p.IsDead() && Utils.DistanceXZ(p.transform.position, c.Position) < c.Radius))
            {
                string who = p.GetPlayerName();
                if (!c.AllowedName(who)) continue;
                int away = Tally.Home(c.R.Flags, who, c.Day);
                if (away > 0) Title(c, who);
                c.MarkDirty();
            }
        }
        // A feast called by a player is theirs when it's held.
        public static void FeastCalled(Colony c, string who) { if (c.IsOwner && !string.IsNullOrEmpty(who)) { Tally.Set(c.R.Flags, who, "feastday", c.Day); c.MarkDirty(); } }
        public static void FeastHeld(Colony c, int day)
        {
            foreach (string who in Tally.Players(c.R.Flags).Where(p => Tally.Get(c.R.Flags, p, "feastday") == day).ToList()) { Tally.Set(c.R.Flags, who, "feastday", 0); Add(c, who, Tally.Feasts); }
        }
        // Raids held: those home at the end of it fought for it.
        public static void RaidHeld(Colony c)
        {
            foreach (Player p in Player.GetAllPlayers().Where(p => p != null && Utils.DistanceXZ(p.transform.position, c.Position) < c.Radius + 30f))
                if (c.AllowedName(p.GetPlayerName())) Add(c, p.GetPlayerName(), Tally.Raids);
        }

        public static string Boss(string prefab) { int t = Bynames.Tier(prefab); return t > 0 ? Bynames.Bosses[t - 1].name : prefab; }
        // How a player is known in this colony: "Bob Bonemass-bane", or "Bob".
        public static string Named(Colony c, string player) => c == null || string.IsNullOrEmpty(player) ? player : Bynames.Named(player, Tally.Of(c.R.Flags, player));

        // ---- on each player's own game ----
        // A boss falls with this player near: remembered on their character, for the colony.
        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        private static class Slain
        {
            private static void Prefix(Character __instance)
            {
                if (__instance == null || !__instance.IsBoss()) return;
                Player me = Player.m_localPlayer;
                if (me == null || me.IsDead() || Vector3.Distance(me.transform.position, __instance.transform.position) > 60f) return;
                string prefab = Utils.GetPrefabName(__instance.gameObject);
                if (Bynames.Tier(prefab) == 0) return;
                Plugin.Guard(() => Carry(me, "boss:" + prefab));
            }
        }
        public static void Carry(Player me, string deed)
        {
            me.m_customData.TryGetValue(PendingKey, out string had);
            me.m_customData[PendingKey] = string.IsNullOrEmpty(had) ? deed : had + ";" + deed;
            _next = 0;
        }
        // At a colony of theirs: what they carry is given to it (every colony they belong to hears of it as they come).
        public static void PlayerTick()
        {
            Player me = Player.m_localPlayer;
            if (me == null || Time.time < _next) return;
            _next = Time.time + 5f;
            if (!me.m_customData.TryGetValue(PendingKey, out string deeds) || string.IsNullOrEmpty(deeds)) return;
            Colony c = Colony.At(me.transform.position);
            if (c == null || !c.Valid || !c.AllowedName(me.GetPlayerName())) return;
            // (each colony of theirs hears each deed once: how many of the list it has heard is kept beside it, by colony)
            string[] all = deeds.Split(';');
            string doneKey = PendingKey + ":" + c.Id;
            int heard = me.m_customData.TryGetValue(doneKey, out string h) && int.TryParse(h, out int n) ? n : 0;
            if (heard >= all.Length) return;
            string news = string.Join(";", all.Skip(heard));
            Net.Act(c, "tally", news);
            me.m_customData[doneKey] = all.Length.ToString();
            Plugin.Log($"Jarldom: {me.GetPlayerName()} brought word to {c.R.Name}: {news}");
        }
    }
}
