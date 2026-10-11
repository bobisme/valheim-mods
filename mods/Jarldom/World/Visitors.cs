using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jarldom
{
    // Visitors who come for coins. A master (at village rank): for a day, 150 coins teaches one villager +10 in the master's skill.
    // Dvergr mercenaries (at jarldom rank, once the Queen has fallen): an envoy offers a band of three for three days at 300 coins; they
    // guard the colony and go the moment their time is up unless paid again.
    // On the colony record: master:skill (the skill, as a number), master:until (day), merc:offer (day), merc:until (day).
    internal static class Visitors
    {
        public const int TrainCost = 150, MercCost = 300, MercDays = 3;
        public const string MercKey = "jarldom_merc";
        private static readonly Skills.SkillType[] Taught = { Skills.SkillType.Swords, Skills.SkillType.Axes, Skills.SkillType.Bows, Skills.SkillType.Spears, Skills.SkillType.Blocking,
            Skills.SkillType.WoodCutting, Skills.SkillType.Pickaxes, Skills.SkillType.Farming, Skills.SkillType.Cooking, Skills.SkillType.Crafting, Skills.SkillType.Fishing };

        public static Skills.SkillType? Master(Colony c) => c.R.Flag("master:until") >= c.Day && c.R.Flag("master:skill") > 0 ? (Skills.SkillType)c.R.Flag("master:skill") : (Skills.SkillType?)null;
        public static bool MercOffered(Colony c) => c.R.Flag("merc:offer") == c.Day;
        public static int MercDaysLeft(Colony c) => Math.Max(0, c.R.Flag("merc:until") - c.Day);

        public static void Dawn(Colony c, int day)
        {
            var rng = new System.Random(c.Id.GetStableHashCode() ^ (day * 7919));
            if (c.R.Rank >= Rank.Village && Master(c) == null && rng.NextDouble() < 0.15)
            {
                Skills.SkillType skill = Taught[rng.Next(Taught.Length)];
                c.R.Flags["master:skill"] = (int)skill; c.R.Flags["master:until"] = day;
                c.Log("text", "", "", $"A master of {Skill(skill)} has come to the hall for the day: {TrainCost} coins and they'll teach one of yours (the People tab).");
            }
            if (c.R.Rank >= Rank.Jarldom && ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey("defeated_queen") && MercDaysLeft(c) == 0 && rng.NextDouble() < 0.2)
            {
                c.R.Flags["merc:offer"] = day;
                c.Log("text", "", "", $"A Dvergr envoy waits at the pillars: three of their fighters for {MercDays} days, {MercCost} coins (the hall tab).");
            }
            if (c.R.Flag("merc:until") > 0 && c.R.Flag("merc:until") <= day) { Dismiss(c); c.R.Flags.Remove("merc:until"); c.Log("text", "", "", "The Dvergr's time is up: unpaid, they shoulder their arms and go."); }
            c.MarkDirty();
        }
        public static string Skill(Skills.SkillType s) => s == Skills.SkillType.WoodCutting ? "woodcutting" : s.ToString().ToLower();

        // On the owner: training paid for.
        public static void Train(Colony c, string vid, string who)
        {
            Skills.SkillType? skill = Master(c);
            if (skill == null) { c.Tell("No master is here."); return; }
            if (!Pay(c, TrainCost)) return;
            Villagers.Command(vid, "train", skill.Value.ToString());
            c.R.Flags.Remove("master:until"); c.R.Flags.Remove("master:skill"); // (one pupil a visit)
            string name = Villagers.ByVid(vid)?.S.Name ?? c.R.Row(vid)?.Name ?? "a villager";
            c.Log("text", name, who, $"{who} paid the master {TrainCost} coins to teach {name} {Skill(skill.Value)}.");
        }
        // On the owner: the Dvergr hired (or kept on).
        public static void Hire(Colony c, string who)
        {
            if (!MercOffered(c) && MercDaysLeft(c) == 0) { c.Tell("No Dvergr are offering."); return; }
            if (!Pay(c, MercCost)) return;
            bool more = MercDaysLeft(c) > 0;
            c.R.Flags["merc:until"] = Math.Max(c.R.Flag("merc:until"), c.Day) + MercDays;
            c.R.Flags.Remove("merc:offer");
            if (!more) Arrive(c);
            c.Log("text", who, "", more ? $"{who} kept the Dvergr on for {MercDays} more days ({MercCost} coins)." : $"{who} hired three Dvergr fighters for {MercDays} days ({MercCost} coins).");
        }
        private static bool Pay(Colony c, int cost)
        {
            Container t = Stock.Treasury(c);
            int have = t != null ? Stock.Coins(t) : 0;
            if (t == null || have < cost || !Stock.TakeCoins(t, cost)) { c.Tell($"The treasury holds {have} coins: that costs {cost}."); return false; }
            return true;
        }
        private static void Arrive(Colony c)
        {
            string[] band = { "Dverger", "Dverger", "DvergerMage" };
            foreach (string name in band)
            {
                GameObject prefab = ZNetScene.instance.GetPrefab(name);
                if (prefab == null) continue;
                Vector3 at = Colony.Spot(c.Position + UnityEngine.Random.insideUnitSphere * 6f, 4f) ?? c.Position + Vector3.up;
                GameObject go = Object.Instantiate(prefab, at, Quaternion.identity);
                go.GetComponent<ZNetView>().GetZDO().Set(MercKey, c.Id);
                Assets.Effect("vfx_spawn", at);
            }
        }
        private static void Dismiss(Colony c)
        {
            foreach (Character m in Character.GetAllCharacters().Where(ch => ch != null && IsMerc(ch, c)).ToList())
            {
                ZNetView v = m.GetComponent<ZNetView>();
                if (!v.IsOwner()) v.ClaimOwnership();
                Assets.Effect("vfx_spawn", m.transform.position);
                ZNetScene.instance.Destroy(m.gameObject);
            }
        }
        public static bool IsMerc(Character ch, Colony c = null)
        {
            string id = ch.GetComponent<ZNetView>()?.GetZDO()?.GetString(MercKey, "") ?? "";
            return id.Length > 0 && (c == null || id == c.Id);
        }

        // A mercenary keeps to the colony: back toward the pillars when they stray past its edge, and never at the colony's people.
        [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
        private static class Keep
        {
            private static readonly AccessTools.FieldRef<MonsterAI, Character> TargetRef = AccessTools.FieldRefAccess<MonsterAI, Character>("m_targetCreature");
            private static bool Prefix(MonsterAI __instance, float dt, ref bool __result)
            {
                Character ch = __instance.GetComponent<Character>();
                if (ch == null || !IsMerc(ch)) return true;
                Colony c = Colony.Of(ch.GetComponent<ZNetView>().GetZDO().GetString(MercKey, ""));
                if (c == null) return true;
                Character target = __instance.GetTargetCreature();
                if (target != null && (target.IsPlayer() || VillagerPrefab.Is(target))) { TargetRef(__instance) = null; target = null; } // (theirs to guard, not to fight)
                if (Utils.DistanceXZ(ch.transform.position, c.Position) > c.Radius && (target == null || Utils.DistanceXZ(target.transform.position, c.Position) > c.Radius))
                {
                    Vector3 dir = c.Position - ch.transform.position; dir.y = 0;
                    __instance.MoveTowards(dir.normalized, true);
                    __result = true;
                    return false;
                }
                return true;
            }
        }
        [HarmonyPatch(typeof(Character), nameof(Character.GetHoverName))]
        private static class Named
        {
            private static void Postfix(Character __instance, ref string __result) { if (IsMerc(__instance)) __result += " (hired)"; }
        }
    }
}
