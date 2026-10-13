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
            ("BuildOrders", new[] { "com.quad.buildorders", "com.dhack.buildorders" }, "builders build its planned ghosts from the stores"),
            ("BuildShapes", new[] { "com.bobisme.buildshapes" }, "Hallwright outlines as rooms (not yet)"),
            ("FeedFromChests", new[] { "com.quad.feedfromchests", "com.dhack.feedfromchests" }, "smelters fed from chests"),
            ("Farmhand", new[] { "com.bobisme.farmhand" }, "rows and batches (not yet)"),
            ("Omens", new[] { "com.bobisme.omens" }, "Odin's favour is the gods' Fate"),
            ("Shieldwall", new[] { "com.bobisme.shieldwall" }, "a siege at the colony's Warstone is a raid: villagers defend it"),
            ("TrophyHall", new[] { "com.bobisme.trophyhall" }, "trophies grade the great hall"),
            ("SkalTavern", new[] { "com.quad.skaltavern", "com.dhack.skaltavern" }, "a Skål! at feasts; its ale, mead and wine drunk in the hall from its chests"),
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

        // ---- Omens: one gods' favour per world; the great stag ----
        private static readonly ApiRef Omens = new ApiRef("Omens.Api", "com.bobisme.omens");
        public static bool OmensFate(out int fate)
        {
            fate = 0;
            Func<int> get = Omens.Bind<Func<int>>("GetFate");
            if (get == null) return false;
            try { fate = get(); } catch { return false; }
            return fate != int.MinValue; // (not the host: Omens' ledger lives there)
        }
        // Points of our favour (-100..100) become Omens' fate steps (-5..5) as they add up.
        public static bool OmensAddFate(Colony c, int points, string why)
        {
            Func<int, string, bool> add = Omens.Bind<Func<int, string, bool>>("AddFate");
            if (add == null) return false;
            try
            {
                int pool = c.R.Flag("omenspool") + points;
                int steps = pool / 20;
                c.R.Flags["omenspool"] = pool - steps * 20;
                if (steps != 0 && !add(steps, why)) { c.R.Flags["omenspool"] = pool; return false; }
                return true;
            }
            catch { return false; }
        }
        // The great stag an omen set loose (Omens 0.6.3 and later, Api version 1): is this it, and where do they run now.
        public static bool IsGreatStag(Character ch)
        {
            Func<Character, bool> f = ch == null ? null : Omens.Bind<Func<Character, bool>>("IsGreatStag", 1);
            return f != null && Safe(() => f(ch));
        }
        // The sky tonight (Omens 0.6.4 and later, Api version 2).
        public static bool Aurora() { Func<bool> f = Omens.Bind<Func<bool>>("IsAurora", 2); return f != null && Safe(f); }
        public static bool BloodMoon() { Func<bool> f = Omens.Bind<Func<bool>>("IsBloodMoon", 2); return f != null && Safe(f); }
        public static Vector3[] GreatStags()
        {
            Func<Vector3[]> f = Omens.Bind<Func<Vector3[]>>("GreatStags", 1);
            return f == null ? new Vector3[0] : Safe(f) ?? new Vector3[0];
        }

        // ---- Shieldwall: a siege at the colony's Warstone is a raid ----
        private static readonly ApiRef Shieldwall = new ApiRef("Shieldwall.Api", "com.bobisme.shieldwall");
        public static bool Siege(Vector3 at, float radius, out Vector3 stone)
        {
            stone = Vector3.zero;
            Func<Vector3, float, bool> siegeAt = Shieldwall.Bind<Func<Vector3, float, bool>>("IsSiegeAt");
            if (siegeAt == null) return false;
            try
            {
                if (!siegeAt(at, radius)) return false;
                Func<Vector3, float, Vector3> stoneAt = Shieldwall.Bind<Func<Vector3, float, Vector3>>("StoneAt");
                if (stoneAt != null) stone = stoneAt(at, radius);
                return true;
            }
            catch { return false; }
        }

        // Paid off: the siege ends (on the stone's owner's game; Shieldwall 0.4.12 and later). How many ended.
        public static int CallOffSiege(Vector3 at, float radius)
        {
            Func<Vector3, float, int> callOff = Shieldwall.Bind<Func<Vector3, float, int>>("CallOff");
            return callOff == null ? 0 : Safe(() => callOff(at, radius));
        }

        // ---- BuildOrders: the helper hooks it publishes (AppDomain data, no types shared) ----
        public static int GhostCount(Vector3 at, float radius) => AppDomain.CurrentDomain.GetData("DHack.BuildOrders.Count") is Func<Vector3, float, int> f ? Safe(() => f(at, radius)) : 0;
        public static string[] NextGhosts(Vector3 at, float radius, int max) => AppDomain.CurrentDomain.GetData("DHack.BuildOrders.Next") is Func<Vector3, float, int, string[]> f ? Safe(() => f(at, radius, max)) ?? new string[0] : new string[0];
        public static bool PlaceGhost(string id) => AppDomain.CurrentDomain.GetData("DHack.BuildOrders.Place") is Func<string, bool> f && Safe(() => f(id));
        public static string[] GhostBlockers(Vector3 at, float radius) => AppDomain.CurrentDomain.GetData("DHack.BuildOrders.Blockers") is Func<Vector3, float, string[]> f ? Safe(() => f(at, radius)) ?? new string[0] : new string[0];
        private static T Safe<T>(Func<T> f) { try { return f(); } catch { return default; } }

        // ---- Gary: a greydwarf to pet ----
        public static Character Gary(Vector3 near, float radius) =>
            Character.GetAllCharacters().FirstOrDefault(c => c != null && c.m_group == "bob_gary" && Vector3.Distance(c.transform.position, near) < radius);

        // ---- SkalTavern: a toast at the feast ----
        public static void SkalToast(Colony c)
        {
            if (!Has("SkalTavern")) return;
            foreach (Brain b in Villagers.All.Where(b => b.S.Colony == c.Id)) b.Say(new[] { "Skål!", "To the feast!", "Skål to the hall!", "Drink up, all of you!", "To the jarl and the hall!" }[UnityEngine.Random.Range(0, 5)], 4);
        }
    }
}
