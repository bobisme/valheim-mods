using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Bootstrap;
using UnityEngine;

namespace Jarldom
{
    // The other mods Jarldom works with when they're there (none is needed). Found by plugin id in BepInEx's list (a dictionary lookup;
    // ScriptEngine and the dev loader keep it current), reached by reflection or a published API.
    internal static class Compat
    {
        public static readonly (string name, string[] guids, string use)[] Known =
        {
            ("AICompanion", new[] { "com.quad.aicompanion", "com.dhack.aicompanion" }, "beds and chests a companion claimed are left alone (and ours are marked for it)"),
            ("Gary", new[] { "com.bobisme.gary" }, "villagers pet Gary the Greydwarf"),
            ("QualityOfLife", new[] { "com.quad.qualityoflife", "com.dhack.qualityoflife" }, "chest rules as stockpile categories"),
            ("BuildOrders", new[] { "com.quad.buildorders", "com.dhack.buildorders" }, "builders (not yet)"),
            ("BuildShapes", new[] { "com.bobisme.buildshapes" }, "Hallwright outlines as rooms (not yet)"),
            ("FeedFromChests", new[] { "com.quad.feedfromchests", "com.dhack.feedfromchests" }, "smelters fed from chests"),
            ("Farmhand", new[] { "com.bobisme.farmhand" }, "rows and batches (not yet)"),
            ("Omens", new[] { "com.bobisme.omens" }, "Odin's favour is the gods' Fate"),
            ("Shieldwall", new[] { "com.bobisme.shieldwall" }, "a siege at the colony's Warstone is a raid: villagers defend it"),
            ("TrophyHall", new[] { "com.bobisme.trophyhall" }, "trophies grade the great hall"),
            ("SkalTavern", new[] { "com.quad.skaltavern", "com.dhack.skaltavern" }, "a Skål! at feasts"),
            ("PartyHud", new[] { "com.quad.partyhud", "com.dhack.partyhud" }, "(not yet)"),
            ("LedgerChest", new[] { "com.quad.ledgerchest", "com.dhack.ledgerchest" }, "(not yet)"),
            ("ClaudeTools", new[] { "com.quad.claudetools", "com.dhack.claudetools" }, "the jarldom command"),
        };
        public static BaseUnityPlugin Find(params string[] guids)
        {
            foreach (string g in guids) if (Chainloader.PluginInfos.TryGetValue(g, out PluginInfo info) && info.Instance != null) return info.Instance;
            return null;
        }
        public static bool Has(string name) => Known.Where(k => k.name == name).Any(k => Find(k.guids) != null);
        public static IEnumerable<(string name, bool present, string version, string use)> Report() =>
            Known.Select(k => { BaseUnityPlugin p = Find(k.guids); return (k.name, p != null, p?.Info?.Metadata?.Version?.ToString() ?? "", k.use); });

        // ---- Omens: one gods' favour per world ----
        private static Type _omensApi; private static BaseUnityPlugin _omens;
        private static Type OmensApi()
        {
            BaseUnityPlugin o = Find("com.bobisme.omens");
            if (o != _omens) { _omens = o; _omensApi = o?.GetType().Assembly.GetType("Omens.Api"); }
            return _omensApi;
        }
        public static bool OmensFate(out int fate)
        {
            fate = 0;
            try
            {
                MethodInfo get = OmensApi()?.GetMethod("GetFate", BindingFlags.Public | BindingFlags.Static);
                if (get == null) return false;
                fate = (int)get.Invoke(null, null);
                return fate != int.MinValue; // (not the host: Omens' ledger lives there)
            }
            catch { return false; }
        }
        // Points of our favour (-100..100) become Omens' fate steps (-5..5) as they add up.
        public static bool OmensAddFate(Colony c, int points, string why)
        {
            try
            {
                MethodInfo add = OmensApi()?.GetMethod("AddFate", BindingFlags.Public | BindingFlags.Static);
                if (add == null) return false;
                int pool = c.R.Flag("omenspool") + points;
                int steps = pool / 20;
                c.R.Flags["omenspool"] = pool - steps * 20;
                if (steps != 0 && !(bool)add.Invoke(null, new object[] { steps, why })) { c.R.Flags["omenspool"] = pool; return false; }
                return true;
            }
            catch { return false; }
        }

        // ---- Shieldwall: a siege at the colony's Warstone is a raid ----
        private static MethodInfo _siegeAt, _stoneAt; private static BaseUnityPlugin _shieldwall;
        public static bool Siege(Vector3 at, float radius, out Vector3 stone)
        {
            stone = Vector3.zero;
            BaseUnityPlugin sw = Find("com.bobisme.shieldwall");
            if (sw != _shieldwall) { _shieldwall = sw; Type api = sw?.GetType().Assembly.GetType("Shieldwall.Api"); _siegeAt = api?.GetMethod("IsSiegeAt"); _stoneAt = api?.GetMethod("StoneAt"); }
            if (_siegeAt == null) return false;
            try
            {
                if (!(bool)_siegeAt.Invoke(null, new object[] { at, radius })) return false;
                if (_stoneAt != null) stone = (Vector3)_stoneAt.Invoke(null, new object[] { at, radius });
                return true;
            }
            catch { return false; }
        }

        // ---- Gary: a greydwarf to pet ----
        public static Character Gary(Vector3 near, float radius) =>
            Character.GetAllCharacters().FirstOrDefault(c => c != null && c.m_group == "bob_gary" && Vector3.Distance(c.transform.position, near) < radius);

        // ---- SkalTavern: a toast at the feast ----
        public static void SkalToast(Colony c)
        {
            if (!Has("SkalTavern")) return;
            foreach (Brain b in Villagers.All.Where(b => b.S.Colony == c.Id)) b.Say("Skål!", 4);
        }
    }
}
