using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Jarldom
{
    // Fighting and running away. Guards (and anyone armed who is attacked) fight what comes into the colony; the rest run for the hall.
    internal static class Combat
    {
        private static readonly AccessTools.FieldRef<Character, HitData> LastHit = AccessTools.FieldRefAccess<Character, HitData>("m_lastHit");
        // Never a foe: players, villagers, players' companions (on the players' side), tame beasts, and pieces (a training dummy).
        public static bool Friendly(Character c) => c == null || c.IsPlayer() || VillagerPrefab.Is(c) || c.IsTamed() || c.GetFaction() == Character.Faction.Players || c.GetComponent<Piece>() != null;
        public static bool Busy(Brain b) => b.Fighting || b.FleeUntil > Time.time;

        public static void Think(Brain b)
        {
            Vector3 me = b.Pos;
            if (b.Fled && b.FleeUntil < Time.time) { b.Fled = false; b.Move.Stop(); } // the scare is over: the run's goal goes with it
            bool out_ = Follow.Following(b);
            bool fighter = b.S.Prof == Prof.Guard || b.S.Prof == Prof.Hunter || b.S.Has("brave") || out_; // (one who goes out with the jarl goes to fight)
            bool armed = Gear.CanFight(b);
            float hp = b.Body.GetHealthPercentage();
            float fleeAt = b.S.Has("brave") ? 0f : b.S.Has("coward") ? 0.5f : 0.3f;
            Colony home = b.Colony != null && b.Colony.Valid ? b.Colony : null; // (none far away with the jarl)
            bool tyr = home != null && home.R.Rooms.Any(r => r.Ok && r.Type == RoomType.Temple && r.God == "tyr") && home.FavourOf("tyr") > 0;
            // A guard without a ring may lose heart early in a raid (one roll a raid).
            if (home != null && !out_ && b.S.Prof == Prof.Guard && b.S.Count("rings") == 0 && home.RaidOn && new System.Random(b.S.Vid.GetStableHashCode() ^ home.R.Log.Count(e => e.Kind == "raid")).NextDouble() < 0.2) fleeAt = 0.7f;
            if (tyr && b.S.Prof == Prof.Guard) fleeAt = 0;
            // (one that hit them from outside the colony isn't chased out there, but it's still run from)
            if (b.HasFoe && !Follow.Near(b, b.Foe.transform.position, 25))
            {
                b.Fighting = false;
                if (Vector3.Distance(b.Foe.transform.position, me) < 12f) Flee(b, b.Foe);
                return;
            }
            Character foe = b.HasFoe ? b.Foe : null;
            if (foe == null)
            {
                // Fighters meet what comes within 25 m; in a raid they go out to the raiders anywhere in the colony (raiders first).
                bool raid = fighter && !out_ && home != null && home.RaidOn;
                float reach = fighter ? 25f : 8f;
                foe = Character.GetAllCharacters().Where(c => c != null && !c.IsDead() && c != b.Body && !Friendly(c) && !Tribute.IsHerald(c) && BaseAI.IsEnemy(b.Body, c)
                        && (fighter || Hunting(c) || Vector3.Distance(c.transform.position, me) < 4f)
                        && (c.GetFaction() != Character.Faction.AnimalsVeg || b.Task is WorkTask w && w.Kind == "hunt" && w.Target == c)
                        && (Vector3.Distance(c.transform.position, me) < reach || raid && Raider(c, home)) && Follow.Near(b, c.transform.position, 25))
                    .OrderBy(c => raid && Raider(c, home) ? 0 : 1).ThenBy(c => Vector3.Distance(c.transform.position, me)).FirstOrDefault();
            }
            if (foe != null && b.Task is CatchTask ct && ct.Target == foe) foe = null; // (the boar being roped)
            if (foe == null) { b.Fighting = false; return; }
            if (!out_ && home != null && Utils.DistanceXZ(foe.transform.position, home.Position) < home.Radius && foe.IsBoss() == false && foe.GetMaxHealth() > 300 && !Mood.Has(b.S, "bigcreature", b.Now)) Mood.Add(b.S, "bigcreature", b.Now);
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
            b.Combat_Bow = Gear.Tool(b, "bow") != null && (b.S.Prof == Prof.Hunter || Gear.Weapon(b) == null || Vector3.Distance(foe.transform.position, me) > 6f); // (a hunter keeps to the bow)
            b.SetDoing(b.Combat_Bow ? "shooting at " + Localization.instance.Localize(foe.m_name) : "fighting " + Localization.instance.Localize(foe.m_name));
        }

        // One of a raid: the draugr jarl's warband, an event's creature (the Fulings' army), or one hunting players or villagers.
        private static bool Raider(Character c, Colony col)
        {
            ZDO z = c.GetComponent<ZNetView>()?.GetZDO();
            return z != null && (z.GetString(Warband.BandKey, "") == (col?.Id ?? "-") || z.GetBool(ZDOVars.s_eventCreature) || z.GetString(Draugr.NameKey, "").StartsWith("Draugr ")) || Hunting(c);
        }
        // Is it after someone (a player or a villager)?
        private static bool Hunting(Character c) { Character t = (c.GetBaseAI() as MonsterAI)?.GetTargetCreature(); return t != null && (t.IsPlayer() || VillagerPrefab.Is(t)); }
        private static void Flee(Brain b, Character from)
        {
            b.FleeUntil = Time.time + 4f; b.Fled = true;
            Player leader = Follow.Leader(b);
            bool home = b.Colony != null && b.Colony.Valid;
            Room hall = home ? b.Colony.Best(RoomType.GreatHall) : null;
            Vector3 safe = leader != null ? leader.transform.position : hall != null ? b.Colony.Centre(hall) : home ? b.Colony.Position : b.Pos + (b.Pos - from.transform.position).normalized * 20f; // (out there: back to the jarl)
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
            if (!Follow.Near(b, t.transform.position, 25)) { b.Fighting = false; b.FightTarget = null; b.Move.Stop(); return; } // (it ran off: not chased out of the colony)
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
            // The shield goes up for the blow, then down to strike back (blocking holds you still: you can't turn or swing).
            if (shield && !ranged && t.InAttack() && dist < 3.5f && !h.InAttack() && Time.time > b.NoBlockUntil && b.BlockUntil < Time.time) { b.BlockUntil = Time.time + 0.6f; b.NoBlockUntil = Time.time + 1.6f; }
            Body_.Blocking(h) = Time.time < b.BlockUntil;
            if (Body_.Blocking(h)) { b.CombatNote = "blocking"; return; }
            if (h.InAttack()) { b.CombatNote = "swinging"; return; }
            if (!b.Ai.IsLookingAt(aim, 25f)) { b.CombatNote = "turning"; return; }
            if (h.GetTimeSinceLastAttack() < 0.4f) return;
            if (ranged) Body_.DrawTime(h) = 10f; // the AI can't hold a draw: a bow shot undrawn does nothing
            bool started = h.StartAttack(t, false);
            if (ranged) Body_.DrawTime(h) = 0f;
            b.Swings++; if (started) b.SwingsStarted++;
            b.CombatNote = started ? "struck" : "couldn't swing:" + (h.InDodge() ? " dodge" : "") + (!h.CanMove() ? " cantmove" + (h.IsSitting() ? "(sitting)" : "") + (h.IsStaggering() ? "(stagger)" : "") : "") + (h.IsKnockedBack() ? " knocked" : "") + (h.IsStaggering() ? " stagger" : "") + (h.InMinorAction() ? " minor" : "") + (weapon == null || !weapon.HavePrimaryAttack() ? " noattack" : "") + (h.InAttack() ? " inattack" : "");
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
