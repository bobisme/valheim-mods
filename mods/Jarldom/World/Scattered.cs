using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Jarldom
{
    // The scattered reborn in the world (rules: Core/Scatter.cs). At dusk, now and then, one of the souls the Valkyrie brings is set
    // down far out instead of at the pillars: by a ruin, a wreck, a tower, a standing stone, 400 to 1500 m away in a land the world
    // has opened. Hugin says where and the place is pinned on the jarls' maps. They're one of the colony from then (a bed kept for
    // them), and their body waits out there (its own object, loaded when a player comes): a small fire beside them, a shout and a
    // wave when someone comes near, and whoever reaches them they follow home. Home again, they owe that one their life. Three days
    // unfound, the Valkyrie takes them on to Valhalla instead: no blame.
    internal static class Scattered
    {
        public const string RecordKey = "jarldom_scatter";   // (on the pillars: Scatter.Save of the one waiting)
        public const string StrandKey = "jarldom_stranded";  // (on the body: the colony's id while they wait to be found)
        public const string RescuerKey = "jarldom_rescuer";  // (on the body: who found them, till they're home)

        // ---- the colony's owner ----
        // The land opened so far: one more for each of the Forsaken beaten, in their order.
        public static int Opened()
        {
            string[] keys = { "defeated_eikthyr", "defeated_gdking", "defeated_bonemass", "defeated_dragon", "defeated_goblinking", "defeated_queen" };
            int n = 0; foreach (string k in keys) { if (ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(k)) n++; else break; }
            return n;
        }
        public static (string prefab, float x, float z)? Site(Colony c, System.Random rng)
        {
            var all = ZoneSystem.instance?.GetLocationList();
            if (all == null) return null;
            var places = all.Where(l => l.m_location != null).Select(l => (l.m_location.m_prefabName, l.m_position.x, l.m_position.z, WorldGenerator.instance.GetBiome(l.m_position).ToString()));
            return Scatter.Site(places, c.Position.x, c.Position.z, Opened(), rng);
        }
        public static string PlaceName(string prefab)
        {
            string p = prefab ?? "";
            return p.StartsWith("ShipWreck") ? "a shipwreck" : p.StartsWith("StoneTower") ? "a ruined stone tower" : p.StartsWith("Runestone") ? "a runestone" : p.StartsWith("StoneHenge") ? "a ring of standing stones"
                : p.StartsWith("AbandonedLogCabin") ? "an abandoned log cabin" : p.StartsWith("WoodFarm") || p.StartsWith("WoodVillage") ? "a burnt-out farm" : p.StartsWith("ShipSetting") ? "a stone ship setting"
                : p.StartsWith("DrakeNest") ? "a drake's nest" : p.StartsWith("Waymarker") ? "a cairn" : "a ruin";
        }
        public static Scatter.Waiting Waiting(Colony c) => Scatter.Load(c.Z.GetString(RecordKey, ""));
        // At dusk, as the Valkyrie lands: one of the newcomers set down far out, if the roll falls so (or for a test).
        public static bool SetDown(Colony c, int day, bool force = false)
        {
            ColonyRecord r = c.R;
            if (r.Coming.Count == 0 || Waiting(c) != null || VillagerPrefab.Prefab == null) return false;
            var rng = new System.Random(c.Id.GetStableHashCode() ^ day * 104729);
            if (!force && !Scatter.Rolls(r.Rank, rng.NextDouble())) return false;
            var site = Site(c, rng);
            if (site == null) return false;
            Newcomer n = r.Coming[r.Coming.Count - 1];
            // (a little off the place itself, on its open ground: its walls and floors aren't loaded to stand on yet)
            float a = (float)rng.NextDouble() * Mathf.PI * 2f;
            Vector3 at = new Vector3(site.Value.x + Mathf.Sin(a) * 6f, 0, site.Value.z + Mathf.Cos(a) * 6f);
            at.y = Mathf.Max(WorldGenerator.instance.GetHeight(at.x, at.z), ZoneSystem.instance.m_waterLevel) + 0.6f;
            string vid = Colony.NewId();
            VillagerState s = Souls.Become(n, vid, c.Id, c.Now);
            foreach (Newcomer mate in r.Coming.Where(x => x != n && x.Group == n.Group)) s.Flags_Mate(mate.Name);
            Bed bed = c.FreeBeds().FirstOrDefault();
            if (bed != null) s.Bed = Colony.IdOf(bed.GetComponent<ZNetView>());
            Humanoid h = Villagers.Spawn(c, s, at, null, dressNew: true);
            if (h == null) return false;
            ZDO z = h.GetComponent<ZNetView>().GetZDO();
            z.Set(StrandKey, c.Id);
            SoulStore.Write(z, s, c);
            if (bed != null) { ZNetView bv = bed.GetComponent<ZNetView>(); if (!bv.IsOwner()) bv.ClaimOwnership(); bv.GetZDO().Set(Villagers.ClaimKey, vid); bv.GetZDO().Set("dhc_home", Colony.Hash(vid)); bv.GetZDO().Set("dhc_homename", s.FullName); }
            r.Coming.Remove(n);
            r.Arrivals++;
            r.Roster.Add(new RosterRow { Vid = vid, Name = s.FullName, Female = s.Female, Prof = s.Prof, State = SoulState.Active, X = at.x, Y = at.y, Z = at.z, Seen = c.Now });
            c.Z.Set(RecordKey, Scatter.Save(new Scatter.Waiting { Vid = vid, X = at.x, Z = at.z, Day = c.Now / Clock.DayLength }));
            string place = PlaceName(site.Value.prefab);
            int far = Mathf.RoundToInt(Utils.DistanceXZ(at, c.Position));
            c.Log("text", s.Name, "", $"{s.Name} fell short of the pillars: the Valkyrie set them down by {place} {far} m to the {ScoutTask.Compass(at - c.Position)}.");
            Migration.Hugin(c, "One fell far from your hall", $"{s.Name} fell short of the pillars: the Valkyrie set them down by {place}, {far} m to the {ScoutTask.Compass(at - c.Position)}. They'll hold out three days there. Go and fetch them: it's on your map.");
            Net.LegendPin(c, at, $"{s.Name} waits here");
            c.MarkDirty(); c.Rerank();
            return true;
        }
        // On the owner's tick: the one waiting found and home, or three days gone (the Valkyrie takes them on: no blame).
        private static float _next;
        public static void Tick(Colony c)
        {
            if (!c.IsOwner || Time.time < _next) return;
            _next = Time.time + 15f;
            Scatter.Waiting w = Waiting(c);
            if (w == null) return;
            ZDO body = BodyOf(w.Vid);
            if (body == null || body.GetString(StrandKey, "") == "") { if (body == null || body.GetLong(RescuerKey, 0L) == 0) c.Z.Set(RecordKey, ""); return; } // (found, or gone some other way)
            if (!Scatter.Expired(w.Day, c.Now / Clock.DayLength)) return;
            RosterRow row = c.R.Row(w.Vid);
            if (row != null) row.State = SoulState.Ascended;
            c.ReleaseBed(w.Vid);
            Vector3 at = body.GetPosition();
            if (ZNetScene.instance.FindInstance(body) is ZNetView live && live != null) ZNetScene.instance.Destroy(live.gameObject);
            else { if (!body.IsOwner()) body.SetOwner(ZDOMan.GetSessionID()); ZDOMan.instance.DestroyZDO(body); }
            c.Z.Set(RecordKey, "");
            c.Log("text", row?.Name ?? "", "", $"No one came for {row?.Name ?? "the one set down far out"}, and after three days the Valkyrie came back and carried them on to Valhalla.");
            c.Tell($"The Valkyrie came back for {row?.Name ?? "the one who waited"} out in the wilds and carried them on to Valhalla.", true);
            Net.Valkyrie_(at);
            c.MarkDirty(); c.Rerank();
        }
        public static ZDO BodyOf(string vid)
        {
            int hash = VillagerPrefab.Name.GetStableHashCode();
            var all = AccessTools.FieldRefAccess<ZDOMan, Dictionary<ZDOID, ZDO>>("m_objectsByID")(ZDOMan.instance);
            return all.Values.FirstOrDefault(z => z.GetPrefab() == hash && z.GetString(Villagers.VidKey, "") == vid);
        }

        // ---- the body's game: waiting, found ----
        internal sealed class Camp { public GameObject Fire; public float NextShout; }
        private static readonly Dictionary<string, Camp> Camps = new Dictionary<string, Camp>();
        public static void Forget() { foreach (Camp k in Camps.Values) if (k.Fire != null) Object.Destroy(k.Fire); Camps.Clear(); }
        public static bool Stranded(Brain b) => b.View != null && b.View.IsValid() && b.View.GetZDO().GetString(StrandKey, "") != "";
        // A turn of the one waiting (instead of their day): by their fire, calling to whoever comes, following the one who reaches them.
        public static string Wait(Brain b)
        {
            if (!Camps.TryGetValue(b.S.Vid, out Camp camp)) Camps[b.S.Vid] = camp = new Camp();
            if (camp.Fire == null) camp.Fire = Fire(b.Pos + b.Body.transform.forward * 1.4f, b.S.Vid);
            Player p = Player.GetClosestPlayer(b.Pos, 45f);
            if (p == null) return "waiting out in the wilds to be found";
            Vector3 to = p.transform.position - b.Pos; to.y = 0;
            if (to.sqrMagnitude > 0.01f) b.Ai.LookTowards(to.normalized);
            float d = to.magnitude;
            if (d < 7f)
            {
                ZDO z = b.View.GetZDO();
                z.Set(StrandKey, ""); z.Set(RescuerKey, p.GetPlayerID());
                if (camp.Fire != null) Object.Destroy(camp.Fire);
                Camps.Remove(b.S.Vid);
                Follow.Start(b, p.GetPlayerID(), p.GetPlayerName());
                b.Say(new[] { "You came for me! Odin's jarl, I knew it. Lead on: I'll follow you home.", "Gods, a face! Take me to the hall, I'll be right behind you.", "I'd almost given up. Lead the way home, I'm with you." }[Random.Range(0, 3)], 8);
                if (p == Player.m_localPlayer) p.Message(MessageHud.MessageType.Center, $"{b.S.Name} will follow you home.");
                return "following their rescuer";
            }
            if (Time.time >= camp.NextShout)
            {
                camp.NextShout = Time.time + 9f;
                b.Say(new[] { "Hoy! Over here!", "Here! Are you from the hall?", "Hello! Don't leave me out here!", "This way! By the fire!" }[Random.Range(0, 4)], 5);
                Body_.Anim(b.Body)?.SetTrigger("emote_wave");
            }
            return "calling out to someone coming";
        }
        // Home with their rescuer: the follow ends inside the colony, and they owe that one their life.
        public static void Home(Brain b)
        {
            ZDO z = b.View.GetZDO();
            long who = z.GetLong(RescuerKey, 0L);
            if (who == 0 || b.Colony == null || Utils.DistanceXZ(b.Pos, b.Colony.Position) > b.Colony.Radius - 10f) return;
            z.Set(RescuerKey, 0L);
            string name = z.GetString(Follow.NameKey, "");
            Follow.End(b, new[] { "Home. I'll not forget this.", "So this is the hall. Thank you, jarl.", "Home at last. I owe you my life." }[Random.Range(0, 3)]);
            if (!string.IsNullOrEmpty(name)) b.S.Opinions[name] = Mathf.Clamp((b.S.Opinions.TryGetValue(name, out int o) ? o : 0) + 40, -100, 100);
            Jarldom.Mood.Add(b.S, "rescued", b.Now);
            b.Colony.Log("text", b.S.Name, name ?? "", $"{(string.IsNullOrEmpty(name) ? "The jarl" : name)} found {b.S.Name} out in the wilds and brought them home. {b.S.Name} owes them their life.");
            b.Dirty();
        }
        // Their fire: the fire pit's own flames, light and crackle, nothing that burns or saves (only on this game).
        private static GameObject Fire(Vector3 at, string vid)
        {
            string name = "JarldomStrandedFire_" + vid;
            for (GameObject old = GameObject.Find(name); old != null; old = GameObject.Find(name)) Object.DestroyImmediate(old); // (an older copy's, after a reload)
            Transform pit = Assets.Find("fire_pit")?.transform;
            GameObject src = pit?.Find("_enabled_high")?.gameObject;
            if (src == null) return null;
            ZoneSystem.instance.GetGroundHeight(at, out float h); at.y = h;
            var go = new GameObject(name); go.transform.position = at;
            Object.Instantiate(src, go.transform, false).SetActive(true);
            // (its ring of stones and its logs: the pit's own model, nothing else of it)
            if (pit.Find("New") is Transform ring) { GameObject r = Object.Instantiate(ring.gameObject, go.transform, false); r.SetActive(true); foreach (var x in r.GetComponentsInChildren<MonoBehaviour>(true)) Object.Destroy(x); }
            foreach (var x in go.GetComponentsInChildren<Aoe>(true)) Object.Destroy(x);
            foreach (var x in go.GetComponentsInChildren<EffectArea>(true)) Object.Destroy(x);
            foreach (var x in go.GetComponentsInChildren<Collider>(true)) Object.Destroy(x);
            go.SetActive(true);
            return go;
        }
    }
}
