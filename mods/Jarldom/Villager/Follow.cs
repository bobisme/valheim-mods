using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // Out with the jarl: a villager asked to follow keeps by a player wherever they go (through portals too: one left far behind
    // catches up), fights what comes at either of them, and comes home when told, or when the jarl is gone. What they did out there
    // goes into the saga. The follow is kept on the body (a player id), so whichever game owns them next knows.
    internal static class Follow
    {
        public const string Key = "jarldom_follow", NameKey = "jarldom_followname", KillsKey = "jarldom_followkills", SinceKey = "jarldom_followsince", CaddieKey = "jarldom_caddie";
        public const float Keep = 4f, Reach = 30f;
        public static long LeaderId(Brain b) => b.View.GetZDO().GetLong(Key, 0L);
        public static Player Leader(Brain b)
        {
            long id = LeaderId(b);
            if (id == 0) return null;
            Player p = Player.GetAllPlayers().FirstOrDefault(x => x != null && x.GetPlayerID() == id);
            return p != null && !p.IsDead() ? p : null;
        }
        public static bool Following(Brain b) => Leader(b) != null;
        // Within what a follower fights for: near the jarl out there, the colony at home.
        public static bool Near(Brain b, Vector3 p, float extra)
        {
            Player l = Leader(b);
            if (l != null) return Utils.DistanceXZ(p, l.transform.position) < Reach + extra * 0.4f && !ArenaWatch.InRing(b, p); // (the jarl's fight in the arena is theirs alone)
            return b.Colony == null || !b.Colony.Valid || Utils.DistanceXZ(p, b.Colony.Position) < b.Colony.Radius + extra;
        }
        public static void Start(Brain b, long id, string name)
        {
            ZDO z = b.View.GetZDO();
            if (z.GetLong(Key, 0L) == id) return;
            z.Set(Key, id); z.Set(NameKey, name ?? ""); z.Set(KillsKey, b.S.Count("kills")); z.Set(SinceKey, ZNet.instance.GetTime().Ticks);
            b.Task?.Stop(b); b.Task = null;
            b.Say(Gear.CanFight(b) ? new[] { "I'm with you, jarl.", "Lead on, jarl.", "Ready when you are, jarl.", "At your side, jarl.", "Where to, jarl? I'm coming." }[UnityEngine.Random.Range(0, 5)] : new[] { "I'll come, but I've nothing to fight with.", "I'll come, but I have no weapon.", "Coming, jarl, but I've nothing to fight with.", "I'll walk with you, but I've nothing to fight with.", "I'd come, but I've no weapon to fight with." }[UnityEngine.Random.Range(0, 5)], 5);
        }
        // Home again: the saga hears of it, and a soul who went out is the prouder for it.
        public static void End(Brain b, string why)
        {
            ZDO z = b.View.GetZDO();
            if (z.GetLong(Key, 0L) == 0) return;
            int kills = b.S.Count("kills") - z.GetInt(KillsKey, b.S.Count("kills"));
            string jarl = z.GetString(NameKey, "the jarl");
            double hours = (ZNet.instance.GetTime().Ticks - z.GetLong(SinceKey, ZNet.instance.GetTime().Ticks)) / (double)System.TimeSpan.TicksPerSecond / Clock.Hour;
            z.Set(Key, 0L); z.Set(CaddieKey, false);
            Expedition.Ended(b);
            b.Task?.Stop(b); b.Task = null;
            if (hours >= 0.5 || kills > 0)
            {
                Mood.Add(b.S, "expedition", b.Now);
                if (kills > 0) Worth.Gain(b.S, "expedition", Faith.OdinWorthBonus(b.Colony));
                b.S.Bump("expeditions");
                Tell(b, kills > 0 ? $"{b.S.FullName} went out with {jarl} and came home with {kills} {(kills == 1 ? "kill" : "kills")}." : $"{b.S.FullName} went out with {jarl}.");
            }
            b.Dirty();
            b.Say(why, 5);
        }
        // The saga's line, held on the body when the colony wasn't loaded to hear it.
        public const string TellKey = "jarldom_followtell";
        private static void Tell(Brain b, string text)
        {
            if (b.Colony != null && b.Colony.Valid) b.Colony.Log("text", b.S.Name, "", text);
            else b.View.GetZDO().Set(TellKey, text);
        }
        public static void Told(Brain b)
        {
            ZDO z = b.View.GetZDO(); string t = z.GetString(TellKey, "");
            if (t == "" || b.Colony == null) return;
            z.Set(TellKey, ""); b.Colony.Log("text", b.S.Name, "", t);
        }
        // From the brain, a few times a second: is the follow still on?
        public static bool Check(Brain b)
        {
            if (LeaderId(b) == 0) return false;
            if (Leader(b) != null) { b.LeaderGoneAt = 0; return true; }
            if (b.LeaderGoneAt == 0) b.LeaderGoneAt = Time.time;
            if (Time.time - b.LeaderGoneAt < 20f) return true; // (a portal's blink, a load: wait a little)
            b.LeaderGoneAt = 0;
            End(b, "The jarl's gone: I'm going home.");
            HomeFrom(b);
            return false;
        }
        // One far from home walks back if it's near; from farther, they make their own way and are next seen at the pillars.
        public static void HomeFrom(Brain b)
        {
            bool loaded = b.Colony != null && b.Colony.Valid;
            Vector3? home = loaded ? b.Colony.Position : Outposts.Where(b.S.Colony);
            if (home == null || loaded && Utils.DistanceXZ(b.Pos, home.Value) < 250f) return; // (near enough to walk; a hall not loaded here can't be walked to)
            Vector3 at = b.Colony != null && b.Colony.Valid ? Colony.Spot(b.Colony.Position + b.Colony.Seat.transform.forward * 3f, 2.5f) ?? b.Colony.Position + Vector3.up : new Vector3(home.Value.x + 3f, (WorldGenerator.instance?.GetHeight(home.Value.x + 3f, home.Value.z) ?? 40f) + 2f, home.Value.z); // (its ground isn't loaded: the world's own height, and a little drop)
            Assets.Effect("vfx_spawn", b.Pos);
            b.Place(at);
        }
    }

    // A jarl going through a portal (or any teleport) takes those following close behind along: they're set down by the far end, and
    // load there when the jarl does.
    [HarmonyLib.HarmonyPatch(typeof(Player), nameof(Player.TeleportTo))]
    internal static class FollowThroughPortals
    {
        private static void Postfix(Player __instance, Vector3 pos, bool __result)
        {
            if (!__result || __instance == null) return;
            Plugin.Guard(() =>
            {
                long id = __instance.GetPlayerID(); int k = 0;
                foreach (Brain b in Villagers.All.Where(x => Follow.LeaderId(x) == id && Vector3.Distance(x.Pos, __instance.transform.position) < 45f).ToList())
                {
                    float a = k++ * 2.1f;
                    b.Task?.Stop(b); b.Task = null; b.Move.Stop();
                    b.Place(pos + new Vector3(Mathf.Sin(a) * 2.5f, 0.3f, Mathf.Cos(a) * 2.5f));
                }
            });
        }
    }

    internal sealed class FollowTask : Task
    {
        private float _nextHungry, _nextCheer, _nextRole; private bool _watching; private Vector3? _seat;
        private readonly Caddie.State _caddie = new Caddie.State();
        private readonly Expedition.CampState _camp = new Expedition.CampState();
        public override void Start(Brain b) { Doing = "following the jarl"; }
        public override void Stop(Brain b) { _camp.Wake(b); base.Stop(b); }
        // The jarl fighting in Quad's Arena: up to the stands beside a seat, to watch, wager and cheer (not into the ring).
        private void Watch(Brain b, Player l, Vector3 ring)
        {
            if (!_watching)
            {
                _watching = true;
                Vector3[] stands = Compat.ArenaStands(24);
                if (stands.Length > 0)
                {
                    Vector3 s = stands[(b.S.Vid.GetStableHashCode() & 0x7fffffff) % stands.Length], side = Vector3.Cross(Vector3.up, ring - s); side.y = 0;
                    _seat = s + (side.sqrMagnitude > 0.01f ? side.normalized * 0.8f : Vector3.zero); // (beside a seat: the crowd's figures sit on them)
                    b.Move.To(_seat.Value, 1.2f);
                }
                ArenaWatch.Wager(b, l.GetPlayerName());
                Mood.Add(b.S, "arenawatch", b.Now); b.Dirty();
            }
            Doing = $"watching {l.GetPlayerName()} fight in the arena";
            if (b.Move.Moving && !b.Move.Failed) return;
            Vector3 d = ring - b.Pos; d.y = 0; if (d.sqrMagnitude > 0.01f) b.Ai.LookTowards(d.normalized);
            if (Time.time < _nextCheer) return;
            _nextCheer = Time.time + UnityEngine.Random.Range(6f, 14f);
            Body_.Anim(b.Body)?.SetTrigger(UnityEngine.Random.value < 0.75f ? "emote_cheer" : "emote_point");
            if (UnityEngine.Random.value < 0.3f) b.Say(new[] { $"Get up, {l.GetPlayerName()}!", "Behind you, jarl!", "Hit it again!", "That's our jarl!", "Skål!", "Make them bleed!", "Odin sees you, jarl!" }[UnityEngine.Random.Range(0, 7)], 4);
        }
        public override void Tick(Brain b)
        {
            Player l = Follow.Leader(b);
            if (l == null) { Done = true; return; }
            if (ArenaWatch.On(l, out Vector3 ring) && Utils.DistanceXZ(b.Pos, ring) < ArenaWatch.Near) { Watch(b, l, ring); return; }
            if (_watching) { _watching = false; _seat = null; b.Move.Stop(); }
            if (Caddie.Tick(b, l, _caddie)) { Doing = _caddie.Doing; return; }
            Vector3 to = l.transform.position;
            float d = Vector3.Distance(b.Pos, to);
            string role = Expedition.RoleOf(b);
            Doing = role != "" ? $"out with {l.GetPlayerName()} ({role})" : $"following {l.GetPlayerName()}";
            Expedition.OnTheRoad(b, l, ref _nextRole);
            // (the jarl at the crew's camp: each to their work there, not at the jarl's heels)
            if (role != "" && Expedition.AtCamp(l, _camp) && !(l.IsAttachedToShip() || l.GetStandingOnShip() != null)) { Doing = Expedition.CampWork(b, l, _camp); return; }
            else if (_camp.Phase != 0 || _camp.Item != null || _camp.Bed != null || (object)_camp.OnRoll != null) { _camp.Phase = 0; _camp.Item = null; _camp.Wake(b); b.Move.Stop(); }
            // (the jarl at sea: wait on the shore, and catch up when they land, rather than swim after the ship)
            if (l.IsAttachedToShip() || l.GetStandingOnShip() != null) { Doing = $"waiting for {l.GetPlayerName()}'s ship"; if (b.Move.Moving) b.Move.Stop(); return; }
            // Left far behind (a portal, a ship, a fall): they catch up out of sight, as companions do.
            if (d > 70f || d > 25f && Mathf.Abs(b.Pos.y - to.y) > 12f)
            {
                Vector3 behind = to - l.transform.forward * 3f;
                Vector3 spot = Nav.Valid(out Vector3 snapped, behind, 3f) ? snapped : behind;
                Assets.Effect("vfx_spawn", spot);
                b.Place(spot + Vector3.up * 0.2f); b.Move.Stop();
                return;
            }
            if (d > Follow.Keep + 2f) { if (!b.Move.Moving || Vector3.Distance(b.Move.Goal, to) > 3f) b.Move.To(to, Follow.Keep, d > 9f); }
            else if (d < Follow.Keep && b.Move.Moving) b.Move.Stop();
            if (b.S.Needs[(int)Need.Food] < 40 && Food.CanEat(b)) Food.EatFromBag(b);
            else if (b.S.Needs[(int)Need.Food] < 15 && Time.time > _nextHungry) { _nextHungry = Time.time + 120f; b.Say(new[] { "I'm starving, jarl: have you any food? (hotbar key on me)", "Jarl, I'm near starved: any food? (hotbar key on me)", "My belly is empty, jarl. Food? (hotbar key on me)", "Any food to spare, jarl? (hotbar key on me)", "Hungry, jarl, and weak with it. Food? (hotbar key on me)" }[UnityEngine.Random.Range(0, 5)], 6); }
        }
    }
}
