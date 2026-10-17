using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Bathhouse
{
    // Bathed (from a sauna's heat) and Invigorated (the cold plunge straight after): the game's own kind of status effect, put in the
    // game's list of them for this world, and what they do that a plain one can't (comfort, the cold kept off, more stamina).
    internal static class Effects
    {
        public const string BathedName = "Bathhouse_Bathed", InvigoratedName = "Bathhouse_Invigorated";
        internal static readonly int Bathed = BathedName.GetStableHashCode(), Invigorated = InvigoratedName.GetStableHashCode();
        private static ObjectDB _db; private static float _next;
        private static readonly System.Reflection.FieldInfo TimeField = AccessTools.Field(typeof(StatusEffect), "m_time");

        internal static void Ensure()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 2;
            ObjectDB db = ObjectDB.instance;
            if (db == null || db.m_StatusEffects == null || ZNetScene.instance == null) return;
            if (db == _db && db.m_StatusEffects.Any(s => s != null && s.name == BathedName) && db.m_StatusEffects.Any(s => s != null && s.name == InvigoratedName)) return;
            _db = db;
            db.m_StatusEffects.RemoveAll(s => s == null || s.name == BathedName || s.name == InvigoratedName); // (an older copy's: this one's in their place)
            var bathed = ScriptableObject.CreateInstance<SE_Stats>();
            bathed.name = BathedName; bathed.m_name = "Bathed"; bathed.m_ttl = Sauna.BathedSeconds;
            bathed.m_tooltip = $"Clean and warm through from the sauna's heat.\nComfort +{Sauna.Comfort}, Rested +{Sauna.RestedSeconds / 60:0} minutes, stamina comes back {(Sauna.StaminaRegen - 1) * 100:0}% faster, the cold can't reach you.\nStraight into cold water for Invigorated.";
            bathed.m_staminaRegenMultiplier = Sauna.StaminaRegen;
            bathed.m_icon = ZNetScene.instance.GetPrefab(Kit.HotTub)?.GetComponent<Piece>()?.m_icon;
            bathed.m_startMessageType = MessageHud.MessageType.TopLeft; bathed.m_startMessage = "Bathed: clean and warm through.";
            var invig = ScriptableObject.CreateInstance<SE_Stats>();
            invig.name = InvigoratedName; invig.m_name = "Invigorated"; invig.m_ttl = Sauna.InvigoratedSeconds;
            invig.m_tooltip = $"From the sauna's heat straight into cold water: every bit of you awake.\n{Sauna.Speed * 100:0}% faster on foot, {(Sauna.Stamina - 1) * 100:0}% more stamina.";
            invig.m_speedModifier = Sauna.Speed;
            invig.m_icon = db.GetItemPrefab("MeadFrostResist")?.GetComponent<ItemDrop>()?.m_itemData.GetIcon();
            invig.m_startMessageType = MessageHud.MessageType.TopLeft; invig.m_startMessage = "Invigorated!";
            db.m_StatusEffects.Add(bathed); db.m_StatusEffects.Add(invig);
        }

        internal static bool Has(Character c, int hash) => c != null && c.GetSEMan() != null && c.GetSEMan().HaveStatusEffect(hash);
        internal static float Left(Character c, int hash) => c?.GetSEMan()?.GetStatusEffect(hash) is StatusEffect se ? se.GetRemaningTime() : 0;

        // Bathed now, for longer in company: the wet washed off, and the rest a while longer.
        internal static void Bathe(Player p, float lengthen)
        {
            SEMan se = p.GetSEMan();
            se.RemoveStatusEffect(Bathed, quiet: true);
            StatusEffect b = se.AddStatusEffect(Bathed, resetTime: true);
            if (b == null) b = se.GetStatusEffect(Bathed);
            if (b != null) { b.m_ttl = Sauna.BathedSeconds * lengthen; TimeField?.SetValue(b, 0f); }
            se.RemoveStatusEffect(SEMan.s_statusEffectWet, quiet: true);
            se.RemoveStatusEffect(SEMan.s_statusEffectCold, quiet: true);
            StatusEffect rested = se.GetStatusEffect(SEMan.s_statusEffectRested) ?? se.AddStatusEffect(SEMan.s_statusEffectRested);
            if (rested != null) rested.m_ttl += Sauna.RestedSeconds;
        }
        internal static void Invigorate(Player p, float lengthen)
        {
            SEMan se = p.GetSEMan();
            se.RemoveStatusEffect(Invigorated, quiet: true);
            StatusEffect s = se.AddStatusEffect(Invigorated, resetTime: true) ?? se.GetStatusEffect(Invigorated);
            if (s != null) { s.m_ttl = Sauna.InvigoratedSeconds * lengthen; TimeField?.SetValue(s, 0f); }
        }

        // Bathed: comfort +2 (the game reckons it every few seconds while resting).
        [HarmonyPatch(typeof(SE_Rested), nameof(SE_Rested.CalculateComfortLevel), typeof(Player))]
        private static class Comfort
        {
            private static void Postfix(Player player, ref int __result) { if (Has(player, Bathed)) __result += Sauna.Comfort; }
        }
        // Bathed: the cold can't reach you (freezing still can: a mountain night is more than a chill).
        [HarmonyPatch(typeof(SEMan), nameof(SEMan.AddStatusEffect), typeof(int), typeof(bool), typeof(int), typeof(float), typeof(short))]
        private static class NoCold
        {
            private static bool Prefix(SEMan __instance, int nameHash, ref StatusEffect __result, Character ___m_character)
            {
                if (nameHash != SEMan.s_statusEffectCold || !__instance.HaveStatusEffect(Bathed)) return true;
                __result = null; return false;
            }
        }
        // Invigorated: 10% more stamina, on top of what food gives.
        [HarmonyPatch(typeof(Player), "GetTotalFoodValue")]
        private static class MoreStamina
        {
            private static void Postfix(Player __instance, ref float stamina) { if (Has(__instance, Invigorated)) stamina *= Sauna.Stamina; }
        }
    }
}
