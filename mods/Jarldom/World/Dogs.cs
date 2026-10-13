using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // Village dogs: a tamed wolf in the colony taken in as the colony's own (from the colony window). By day it trots after the
    // villagers at their work; at night it lies by the hall's hearth; and it barks when something hostile comes near.
    // Each game minds the dogs it owns (the game that runs their AI).
    internal static class Dogs
    {
        public const string Key = "jarldom_dog"; // (the colony's id on the wolf)
        private const float WarnReach = 40f, WarnEvery = 60f;
        private static readonly string[] Names = { "Sámr", "Vígi", "Garmr", "Snati", "Hrókr", "Bósi", "Gráni", "Flóki", "Kolr", "Skuggi", "Ylgr", "Freki" };
        private static readonly Dictionary<ZDOID, (GameObject follow, float until)> Following = new Dictionary<ZDOID, (GameObject, float)>();
        private static readonly Dictionary<string, float> Warned = new Dictionary<string, float>();
        private static float _next;

        public static bool IsDog(Character ch, out string colony)
        {
            colony = ch?.GetComponent<ZNetView>()?.GetZDO()?.GetString(Key, "") ?? "";
            return colony != "";
        }
        public static IEnumerable<Character> Of(Colony c) => Character.GetAllCharacters().Where(ch => ch != null && !ch.IsDead() && IsDog(ch, out string id) && id == c.Id);
        // Tamed wolves in the colony that could be taken in (not already a village dog).
        public static IEnumerable<Character> Strays(Colony c) => Character.GetAllCharacters().Where(ch => ch != null && !ch.IsDead() && ch.IsTamed()
            && Utils.GetPrefabName(ch.gameObject).StartsWith("Wolf") && !IsDog(ch, out _) && Utils.DistanceXZ(ch.transform.position, c.Position) < c.Radius);

        // From the colony window, on the player's game: the wolf is the colony's now, and gets a name if it has none.
        public static string Adopt(Colony c, Character wolf)
        {
            ZNetView view = wolf?.GetComponent<ZNetView>();
            if (view == null || !view.IsValid() || !wolf.IsTamed()) return "that wolf isn't tame";
            if (!view.IsOwner()) view.ClaimOwnership();
            view.GetZDO().Set(Key, c.Id);
            Tameable t = wolf.GetComponent<Tameable>();
            string name = t != null ? t.GetText() : "";
            if (string.IsNullOrEmpty(name)) { name = Names[Random.Range(0, Names.Length)]; t?.SetText(name); }
            c.Log("text", name, "", $"{name} the wolf became the village dog.");
            return "";
        }
        public static void Release(Character wolf)
        {
            ZNetView view = wolf?.GetComponent<ZNetView>();
            if (view == null || !view.IsValid()) return;
            if (!view.IsOwner()) view.ClaimOwnership();
            view.GetZDO().Set(Key, "");
            wolf.GetComponent<MonsterAI>()?.ResetPatrolPoint();
        }

        // Every two seconds on each game: its own dogs.
        public static void Tick()
        {
            if (Time.time < _next || ZNetScene.instance == null) return;
            _next = Time.time + 2f;
            foreach (Character dog in Character.GetAllCharacters().ToList())
            {
                if (dog == null || dog.IsDead() || !IsDog(dog, out string id)) continue;
                ZNetView view = dog.GetComponent<ZNetView>();
                if (view == null || !view.IsOwner()) continue;
                Colony c = Colony.Of(id);
                MonsterAI ai = dog.GetComponent<MonsterAI>();
                if (c == null || !c.Valid || ai == null) continue;
                Plugin.Guard(() => { Mind(c, dog, ai); Warn(c, dog, ai); });
            }
        }
        private static void Mind(Colony c, Character dog, MonsterAI ai)
        {
            if (ai.GetFollowTarget() is GameObject led && led.GetComponent<Player>() != null) return; // (a player took it along: theirs for now)
            if (ai.GetTargetCreature() != null) return; // (fighting)
            ZDOID id = dog.GetComponent<ZNetView>().GetZDO().m_uid;
            bool night = c.Hour >= Schedule.HomeAt || c.Hour < Schedule.WakeAt;
            if (night)
            {
                // By the hall's hearth (or the pillars, with no hall).
                Room hall = c.Best(RoomType.GreatHall);
                Vector3 at = hall != null ? c.Centre(hall) : c.Position;
                Fireplace fire = Classes.NearestFire(at, 12f, hall?.Xz);
                if (fire != null) at = fire.transform.position + (c.Position - fire.transform.position).normalized * 1.5f;
                ai.SetFollowTarget(null);
                Sit(dog, at);
                Following.Remove(id);
                return;
            }
            // By day: after one of the villagers at their work, a different one now and then.
            if (Following.TryGetValue(id, out var f) && f.follow != null && Time.time < f.until) { if (ai.GetFollowTarget() != f.follow) ai.SetFollowTarget(f.follow); return; }
            Brain pick = Villagers.All.Where(b => b.S.Colony == c.Id && b.Task is WorkTask && Vector3.Distance(b.Pos, dog.transform.position) < 60f).OrderBy(_ => Random.value).FirstOrDefault()
                ?? Villagers.All.Where(b => b.S.Colony == c.Id && Vector3.Distance(b.Pos, dog.transform.position) < 60f).OrderBy(_ => Random.value).FirstOrDefault();
            if (pick == null) { ai.SetFollowTarget(null); Sit(dog, c.Position); return; }
            Following[id] = (pick.Body.gameObject, Time.time + Random.Range(120f, 240f));
            dog.GetComponent<ZNetView>().GetZDO().Set(ZDOVars.s_patrol, false);
            ai.SetFollowTarget(pick.Body.gameObject);
        }
        // Stays about a spot: the game's own "stay" (a patrol point it idles around).
        private static void Sit(Character dog, Vector3 at)
        {
            ZDO z = dog.GetComponent<ZNetView>().GetZDO();
            if (z.GetBool(ZDOVars.s_patrol) && Vector3.Distance(z.GetVec3(ZDOVars.s_patrolPoint, Vector3.zero), at) < 1f) return;
            z.Set(ZDOVars.s_patrolPoint, at);
            z.Set(ZDOVars.s_patrol, true);
        }
        // Something hostile within 40 m: it barks, and the colony hears where from.
        private static void Warn(Colony c, Character dog, MonsterAI ai)
        {
            if (Warned.TryGetValue(c.Id, out float at) && Time.time - at < WarnEvery) return;
            Character foe = Character.GetAllCharacters().Where(ch => ch != null && !ch.IsDead() && !ch.IsPlayer() && !ch.IsTamed() && ch.GetComponent<Piece>() == null /* (a training dummy) */ && Villagers.Brain(ch) == null && !VillagerPrefab.Is(ch)
                && BaseAI.IsEnemy(dog, ch) && ch.GetBaseAI() is MonsterAI m && (m.IsAlerted() || m.IsEventCreature() || ch.GetComponent<Tameable>() == null) // (a greydwarf creeping in, yes; a boar grazing by the fence, only once it's roused)
                && Vector3.Distance(ch.transform.position, dog.transform.position) < WarnReach).OrderBy(ch => Vector3.Distance(ch.transform.position, dog.transform.position)).FirstOrDefault();
            if (foe == null) return;
            Warned[c.Id] = Time.time;
            ai.m_alertedEffects?.Create(dog.transform.position, dog.transform.rotation);
            string name = dog.GetComponent<Tameable>()?.GetText(); if (string.IsNullOrEmpty(name)) name = "The village dog";
            string told = $"{name} is barking: {Localization.instance.Localize(foe.m_name).ToLower()} to the {Compass(foe.transform.position - c.Position)}.";
            c.Tell(told, true); Plugin.Log(told);
        }
        private static string Compass(Vector3 d)
        {
            float a = (Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + 360f) % 360f;
            return new[] { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" }[Mathf.RoundToInt(a / 45f) % 8];
        }
        public static void Forget() { Following.Clear(); Warned.Clear(); }
    }
}
