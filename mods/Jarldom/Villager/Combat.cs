using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Jarldom
{
    // Fighting and running away. Guards (and anyone armed who is attacked) fight what comes into the colony; the rest run for the hall.
    internal static class Combat
    {
        // (the game aims an AI's arrow from where it leaves the bow at its target's middle: a villager's AI has a target only for that)
        private static readonly AccessTools.FieldRef<MonsterAI, Character> AimAt = AccessTools.FieldRefAccess<MonsterAI, Character>("m_targetCreature");
        internal static readonly AccessTools.FieldRef<Character, HitData> LastHit = AccessTools.FieldRefAccess<Character, HitData>("m_lastHit");
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
                if (Vector3.Distance(b.Foe.transform.position, me) < 12f && b.Foe.GetFaction() != Character.Faction.AnimalsVeg) Flee(b, b.Foe); // (not from a deer)
                return;
            }
            Character foe = b.HasFoe ? b.Foe : null;
            if (foe == null)
            {
                // Fighters meet what comes within 25 m; in a raid they go out to the raiders anywhere in the colony (raiders first).
                bool raid = fighter && !out_ && home != null && home.RaidOn;
                float reach = fighter ? 25f : 8f;
                foe = Character.GetAllCharacters().Where(c => c != null && !c.IsDead() && c != b.Body && !Friendly(c) && !Tribute.IsHerald(c) && BaseAI.IsEnemy(b.Body, c) && !(c == b.Unseen && Time.time < b.UnseenUntil) && !LetBe(c)
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
        internal static bool Raider(Character c, Colony col)
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

        // Arrows that never hurt it: seven loosed and not a scratch, and it's let be an hour, by everyone (one villager after another
        // emptied a quiver at the same neck: one owned by a game no longer there, frozen where it stood, every hit sent off to nobody).
        private static readonly Dictionary<Character, float> Untouchable = new Dictionary<Character, float>();
        public static bool LetBe(Character c) => c != null && Untouchable.TryGetValue(c, out float until) && Time.time < until;
        private static bool Missed(Brain b, Character t)
        {
            if (b.ShotAt != t || t.GetHealth() < b.ShotHp) { b.ShotAt = t; b.ShotHp = t.GetHealth(); b.ShotsSince = 0; }
            if (++b.ShotsSince < 7) return false;
            if (Untouchable.Count > 50) foreach (var k in Untouchable.Where(kv => kv.Key == null || Time.time > kv.Value).Select(kv => kv.Key).ToList()) Untouchable.Remove(k);
            Untouchable[t] = Time.time + 3600f; b.ShotsSince = 0;
            return true;
        }
        private static int _sight = -1;
        private static int Sight => _sight != -1 ? _sight : _sight = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
        public static void Fixed(Brain b, float dt)
        {
            Humanoid h = b.Body;
            if (!b.Fighting || b.FightTarget == null || b.FightTarget.IsDead()) { if (b.Fighting) { b.Fighting = false; b.Move.Stop(); } Body_.Blocking(h) = false; AimAt(b.Ai) = null; return; }
            Character t = b.FightTarget;
            if (!Follow.Near(b, t.transform.position, b.Task is WorkTask rw && rw.O.Zone == RangeTask.Zone ? RangeTask.Beyond + 45 : 25)) { b.Fighting = false; b.FightTarget = null; b.Move.Stop(); return; } // (it ran off: not chased out of the colony)
            ItemDrop.ItemData weapon = h.GetCurrentWeapon();
            bool ranged = weapon != null && weapon.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow;
            float dist = Vector3.Distance(t.transform.position, b.Pos) - t.GetRadius();
            // (the game aims an arrow straight at the middle, and it drops on the way: from 25 m it falls short of a neck, into the
            // grass under it; game doesn't fight back, so a hunter closes to 12 m for the shot)
            float reach = ranged ? (Work.Prey(t) ? 12f : 25f) : Mathf.Max(1.2f, (weapon?.m_shared.m_attack.m_attackRange ?? 1.5f) * 0.9f);
            if (dist > reach) { if (!b.Move.Moving || Vector3.Distance(b.Move.Goal, t.transform.position) > 1.5f) b.Move.To(t.transform.position, reach * 0.8f, dist > 6f); return; }
            Vector3 aim = t.GetCenterPoint();
            Vector3 from = b.Pos + Vector3.up * (weapon?.m_shared.m_attack.m_attackHeight ?? 1f);
            // A bow wants a clear shot: a wall between them, and the arrows only feed the wall. Go round for one; none in a while, let it be.
            if (ranged && Physics.Linecast(from, aim, Sight, QueryTriggerInteraction.Ignore))
            {
                if (b.BlindSince < 0) b.BlindSince = Time.time;
                if (Time.time - b.BlindSince > 8f) { b.Unseen = t; b.UnseenUntil = Time.time + 30f; b.BlindSince = -1; b.Fighting = false; b.FightTarget = null; b.Move.Stop(); return; }
                if (!b.Move.Moving || Vector3.Distance(b.Move.Goal, t.transform.position) > 1.5f) b.Move.To(t.transform.position, 3f, dist > 6f);
                b.CombatNote = "no clear shot"; return;
            }
            b.BlindSince = -1;
            b.Move.Stop();
            h.SetLookDir((aim - from).normalized, 0f);
            b.Ai.LookTowards((aim - from).normalized); // (from the bow, not the feet: the AI's look is where the arrow goes, and from the feet it flies over a boar)
            // Block when it swings and we hold a shield.
            bool shield = h.LeftItem != null && h.LeftItem.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield;
            // The shield goes up for the blow, then down to strike back (blocking holds you still: you can't turn or swing).
            if (shield && !ranged && t.InAttack() && dist < 3.5f && !h.InAttack() && Time.time > b.NoBlockUntil && b.BlockUntil < Time.time) { b.BlockUntil = Time.time + 0.6f; b.NoBlockUntil = Time.time + 1.6f; }
            Body_.Blocking(h) = Time.time < b.BlockUntil;
            if (Body_.Blocking(h)) { b.CombatNote = "blocking"; return; }
            if (h.InAttack()) { b.CombatNote = "swinging"; return; }
            if (!b.Ai.IsLookingAt(aim, 25f)) { b.CombatNote = "turning"; return; }
            if (h.GetTimeSinceLastAttack() < 0.4f) return;
            if (ranged) { Body_.DrawTime(h) = 10f; AimAt(b.Ai) = t; } // the AI can't hold a draw: a bow shot undrawn does nothing
            Work.TakeOver(t.gameObject); // (a beast no game here runs: its hurts go to nobody)
            bool started = h.StartAttack(t, false);
            if (ranged) Body_.DrawTime(h) = 0f;
            if (ranged && started && Work.Prey(t) && !Raider(t, b.Colony) && Missed(b, t)) { b.Fighting = false; b.FightTarget = null; b.Foe = null; b.Move.Stop(); b.CombatNote = "can't get an arrow into it"; return; }
            b.Swings++; if (started) b.SwingsStarted++;
            b.CombatNote = started ? "struck" : "couldn't swing:" + (h.InDodge() ? " dodge" : "") + (!h.CanMove() ? " cantmove" + (h.IsSitting() ? "(sitting)" : "") + (h.IsStaggering() ? "(stagger)" : "") : "") + (h.IsKnockedBack() ? " knocked" : "") + (h.IsStaggering() ? " stagger" : "") + (h.InMinorAction() ? " minor" : "") + (weapon == null || !weapon.HavePrimaryAttack() ? " noattack" : "") + (h.InAttack() ? " inattack" : "");
        }

        // Credit for kills: worth, the saga and bynames.
        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        private static class Kills
        {
            // One of the raiders, not a neck by the river: the game's raid spawns, a Warstone siege's, a warband's, the Fulings'.
            private static bool Raider(Character ch)
            {
                if (ch.GetBaseAI() is MonsterAI m && m.IsEventCreature()) return true;
                ZDO z = ch.GetComponent<ZNetView>()?.GetZDO();
                return z != null && (z.GetLong("bob_sw_siege", 0L) != 0L || z.GetString(Warband.BandKey, "") != "" || z.GetString(Tribute.BandKey, "") != "");
            }
            private static void Prefix(Character __instance)
            {
                if (__instance == null || __instance.IsPlayer() || VillagerPrefab.Is(__instance)) return;
                if (!__instance.IsTamed() && Colony.At(__instance.transform.position) is Colony home && home.IsOwner) KillSites.Mark(home, __instance.transform.position); // (what it drops is the colony's to carry in)
                Character killer = LastHit(__instance)?.GetAttacker();
                if (killer == null || !VillagerPrefab.Is(killer)) return;
                Brain b = Villagers.Brain(killer);
                if (b == null) return;
                Plugin.Guard(() =>
                {
                    bool star = __instance.GetLevel() > 1, boss = __instance.IsBoss();
                    Worth.Gain(b.S, boss ? "bosskill" : star ? "starkill" : "kill", Faith.OdinWorthBonus(b.Colony));
                    b.S.Bump("kills");
                    if (Work.IsGame(__instance) && b.Colony != null) WornLand.Use(b.Colony, __instance.transform.position, "hunt"); // (game taken from the land there)
                    if (b.Colony != null && b.Colony.RaidOn && Raider(__instance)) b.S.Counts["raidkill"] = Clock.Day(b.Now); // (fought in a raid: a disgraced soul's way back)
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

    // Where something was killed in the colony in the last half hour (a siege's greydwarfs, a neck by the shore, a deer the hunter left):
    // what it dropped is the colony's, carried in by the haulers wherever in the colony it fell, not only in its rooms and zones.
    internal static class KillSites
    {
        public const float Keep = 1800f, Near = 6f;
        private static readonly List<(string colony, Vector3 at, float t)> Spots = new List<(string, Vector3, float)>();
        public static void Mark(Colony c, Vector3 at)
        {
            Spots.RemoveAll(x => Time.time - x.t > Keep);
            if (Spots.Count < 200) Spots.Add((c.Id, at, Time.time));
        }
        public static bool At(Colony c, Vector3 p) => Spots.Any(x => x.colony == c.Id && Time.time - x.t < Keep && Utils.DistanceXZ(x.at, p) < Near);
        public static void Clear() => Spots.Clear();
    }
}
