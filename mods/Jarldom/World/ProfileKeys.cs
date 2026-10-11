using System.Linq;
using HarmonyLib;

namespace Jarldom
{
    // Jarldom keeps a few things in the world's global keys (where colonies stand, claims and their loads, carts on the road, wages
    // owed), because they're seen by every game whatever the server runs. But the game writes every global key it hears, value and all,
    // into each player's profile stats (for achievements), and keeps them there for good: a key whose value changes leaves a new entry
    // each time, so a long game would swell everyone's character file. Jarldom's own entries are taken out again as they come in, and
    // any already there when a profile loads or saves.
    internal static class ProfileKeys
    {
        public const string Prefix = "jarldom_";
        public static int Prune(PlayerProfile profile)
        {
            if (profile?.m_playerStats == null) return 0;
            int gone = 0;
            foreach (PlayerProfile.PlayerStats stats in profile.m_playerStats)
            {
                if (stats?.m_knownWorldKeys == null) continue;
                foreach (string key in stats.m_knownWorldKeys.Keys.Where(k => k.StartsWith(Prefix)).ToList()) { stats.m_knownWorldKeys.Remove(key); gone++; }
            }
            return gone;
        }

        [HarmonyPatch(typeof(ZoneSystem), "GlobalKeyAdd")]
        private static class OnAdd
        {
            private static void Postfix(string keyStr)
            {
                if (keyStr == null || !keyStr.StartsWith(Prefix, System.StringComparison.OrdinalIgnoreCase)) return;
                Plugin.Guard(() => Prune(Game.instance?.GetPlayerProfile()));
            }
        }
        [HarmonyPatch(typeof(PlayerProfile), nameof(PlayerProfile.Load))]
        private static class OnLoad { private static void Postfix(PlayerProfile __instance) => Plugin.Guard(() => { int n = Prune(__instance); if (n > 0) Plugin.Log($"Jarldom: took {n} of its world-key entries out of the profile's stats"); }); }
        [HarmonyPatch(typeof(PlayerProfile), nameof(PlayerProfile.Save))]
        private static class OnSave { private static void Prefix(PlayerProfile __instance) => Plugin.Guard(() => Prune(__instance)); }
    }
}
