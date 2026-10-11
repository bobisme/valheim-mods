using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Jarldom
{
    // Fighting and running away. Guards (and anyone armed who is attacked) fight what comes into the colony; the rest run for the hall.
    internal static class Combat
    {
        private static readonly AccessTools.FieldRef<Character, HitData> LastHit = AccessTools.FieldRefAccess<Character, HitData>("m_lastHit");
        public static bool Busy(Brain b) => b.Fighting || b.FleeUntil > Time.time;

        public static void Think(Brain b)
        {
            Vector3 me = b.Pos;
            if (b.Fled && b.FleeUntil < Time.time) { b.Fled = false; b.Move.Stop(); } // the scare is over: the run's goal goes with it
            bool fighter = b.S.Prof == Prof.Guard || b.S.Prof == Prof.Hunter || b.S.Has("brave");
            bool armed = Gear.CanFight(b);
            float hp = b.Body.GetHealthPercentage();
            float fleeAt = b.S.Has("brave") ? 0f : b.S.Has("coward") ? 0.5f : 0.3f;
            bool tyr = b.Colony.R.Rooms.Any(r => r.Ok && r.Type == RoomType.Temple && r.God == "tyr") && b.Colony.FavourOf("tyr") > 0;
            // A guard without a ring may lose heart early in a raid (one roll a raid).
            if (b.S.Prof == Prof.Guard && b.S.Count("rings") == 0 && b.Colony.RaidOn && new System.Random(b.S.Vid.GetStableHashCode() ^ b.Colony.R.Log.Count(e => e.Kind == "raid")).NextDouble() < 0.2) fleeAt = 0.7f;
            if (tyr && b.S.Prof == Prof.Guard) fleeAt = 0;
            Character foe = b.HasFoe ? b.Foe : null;
            if (foe == null)
            {
                float reach = fighter ? 25f : 8f;
                foe = Character.GetAllCharacters().Where(c => c != null && !c.IsDead() && c != b.Body && !c.IsPlayer() && !VillagerPrefab.Is(c) && BaseAI.IsEnemy(b.Body, c)
                        && (fighter || Hunting(c) || Vector3.Distance(c.transform.position, me) < 4f)
                        && (c.GetFaction() != Character.Faction.AnimalsVeg || b.Task is WorkTask w && w.Kind == "hunt" && w.Target == c)
                        && Vector3.Distance(c.transform.position, me) < reach && Utils.DistanceXZ(c.transform.position, b.Colony.Position) < b.Colony.Radius + 25)
                    .OrderBy(c => Vector3.Distance(c.transform.position, me)).FirstOrDefault();
            }
            if (foe == null) { b.Fighting = false; return; }
            if (Utils.DistanceXZ(foe.transform.position, b.Colony.Position) < b.Colony.Radius && foe.IsBoss() == false && foe.GetMaxHealth() > 300 && !Mood.Has(b.S, "bigcreature", b.Now)) Mood.Add(b.S, "bigcreature", b.Now);
            // Armed and the beast is small: stand and fight it. Otherwise those who aren't fighters run.
            bool small = foe.GetMaxHealth() <= Mathf.Max(60f, b.Body.GetMaxHealth() * 1.5f);
            if (!armed || !fighter && !b.HasFoe && !small || hp < fleeAt)
            {
                b.Fighting = false;
                if (Vector3.Distance(foe.transform.position, me) < 12f) Flee(b, foe);
                return;
            }
            b.Stand();
            b.Fighting = true; b.FightTarget = foe;
            b.Combat_Bow = Gear.Tool(b, "bow") != null && (Gear.Weapon(b) == null || Vector3.Distance(foe.transform.position, me) > 6f);
            b.SetDoing(b.Combat_Bow ? "shooting at " + Localization.instance.Localize(foe.m_name) : "fighting " + Localization.instance.Localize(foe.m_name));
        }

        // Is it after someone (a player or a villager)?
        private static bool Hunting(Character c) { Character t = (c.GetBaseAI() as MonsterAI)?.GetTargetCreature(); return t != null && (t.IsPlayer() || VillagerPrefab.Is(t)); }
        private static void Flee(Brain b, Character from)
        {
            b.FleeUntil = Time.time + 4f; b.Fled = true;
            Room hall = b.Colony.Best(RoomType.GreatHall);
            Vector3 safe = hall != null ? b.Colony.Centre(hall) : b.Colony.Position;
            if (Vector3.Distance(safe, from.transform.position) < 6f) safe = b.Pos + (b.Pos - from.transform.position).normalized * 15f;
            b.Stand();
            b.Move.To(Colony.Spot(safe, 2f) ?? safe, 1.5f, true);
            b.SetDoing("running from " + Localization.instance.Localize(from.m_name));
        }

        public static void Fixed(Brain b, float dt)
        {
            Humanoid h = b.Body;
            if (!b.Fighting || b.FightTarget == null || b.FightTarget.IsDead()) { if (b.Fighting) { b.Fighting = false; b.Move.Stop(); } Body_.Blocking(h) = false; return; }
            Character t = b.FightTarget;
            ItemDrop.ItemData weapon = h.GetCurrentWeapon();
            bool ranged = weapon != null && weapon.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow;
            float dist = Vector3.Distance(t.transform.position, b.Pos) - t.GetRadius();
            float reach = ranged ? 25f : Mathf.Max(1.2f, (weapon?.m_shared.m_attack.m_attackRange ?? 1.5f) * 0.9f);
            if (dist > reach) { if (!b.Move.Moving || Vector3.Distance(b.Move.Goal, t.transform.position) > 1.5f) b.Move.To(t.transform.position, reach * 0.8f, dist > 6f); return; }
            b.Move.Stop();
            Vector3 aim = t.GetCenterPoint();
            Vector3 from = b.Pos + Vector3.up * (weapon?.m_shared.m_attack.m_attackHeight ?? 1f);
            h.SetLookDir((aim - from).normalized, 0f);
            b.Ai.LookTowards((aim - b.Pos).normalized);
            // Block when it swings and we hold a shield.
            bool shield = h.LeftItem != null && h.LeftItem.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield;
            Body_.Blocking(h) = shield && !ranged && t.InAttack() && dist < 3.5f && !h.InAttack();
            if (Body_.Blocking(h)) return;
            if (h.InAttack() || !b.Ai.IsLookingAt(aim, 25f) || h.GetTimeSinceLastAttack() < 0.4f) return;
            if (ranged) Body_.DrawTime(h) = 10f; // the AI can't hold a draw: a bow shot undrawn does nothing
            h.StartAttack(t, false);
            if (ranged) Body_.DrawTime(h) = 0f;
        }

        // Credit for kills: worth, the saga and bynames.
        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        private static class Kills
        {
            private static void Prefix(Character __instance)
            {
                if (__instance == null || __instance.IsPlayer() || VillagerPrefab.Is(__instance)) return;
                Character killer = LastHit(__instance)?.GetAttacker();
                if (killer == null || !VillagerPrefab.Is(killer)) return;
                Brain b = Villagers.Brain(killer);
                if (b == null) return;
                Plugin.Guard(() =>
                {
                    bool star = __instance.GetLevel() > 1, boss = __instance.IsBoss();
                    Worth.Gain(b.S, boss ? "bosskill" : star ? "starkill" : "kill", Faith.OdinWorthBonus(b.Colony));
                    b.S.Bump("kills");
                    string what = Localization.instance.Localize(__instance.m_name);
                    if (star || boss)
                    {
                        b.Colony?.Log("starkill", b.S.Name, what);
                        if (string.IsNullOrEmpty(b.S.Byname)) { b.S.Byname = Names.Byname("starkill", Names.Creature(Utils.GetPrefabName(__instance.gameObject))); b.Colony?.Log("byname", b.S.Name, b.S.Byname); b.Body.m_name = b.S.FullName; }
                    }
                    else b.Colony?.Log("kill", b.S.Name, what);
                    if (b.S.Worthy && b.S.Count("worthylogged") == 0) { b.S.Bump("worthylogged"); b.Colony?.Log("worthy", b.S.FullName, ""); }
                    b.Dirty();
                });
            }
        }
    }
}
