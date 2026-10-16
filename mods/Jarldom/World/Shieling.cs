using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jarldom
{
    // Shielings in the game (the rules are Core's Shielings and Herd): the herd's key, the herder's drive up at the start of summer
    // (on foot, the beasts following; past the colony's edge with no one about to see, the herd goes on out of the world and is set out
    // at the shieling when its ground next loads), the walk out to fetch them at summer's end, and the stake's own part: the beasts set
    // out in the pen, and taken off it once they're home.
    internal static class Shieling
    {
        public const string KeyHead = "jarldom_shieling_", TagKey = "jarldom_herd";
        public static Season? TestSeason;
        public static Herd Get(string claim) => Claims.Keys(KeyHead).TryGetValue(KeyHead + claim, out string v) ? Herd.Decode(claim, v) : null;
        public static void Save(Herd h) => Claims.Set(KeyHead + h.Claim, h.Encode());

        // The tame beasts in the colony's pastures (not a pet, not a villager), at most a herd's worth.
        public static List<Character> Beasts(Colony c) =>
            c.R.Zones.Where(z => z.Type == ZoneType.Pasture).SelectMany(z => Character.GetAllCharacters().Where(ch => ch != null && !ch.IsDead() && !ch.IsPlayer() && !VillagerPrefab.Is(ch)
                && ch.IsTamed() && ch.GetComponent<Tameable>() != null && ch.m_group != "bob_gary" && Zones.Inside(z, ch.transform.position))).Distinct().Take(Shielings.MaxHerd).ToList();
        // Why a shieling can't be used ("" if it can).
        public static string WhyNot(Colony c, Claim x)
        {
            if (!x.Camp) return $"a shieling needs a camp round the stake: a bed, a chest and a fire within {Mathf.RoundToInt(Mining.CampNear)} m";
            if (Utils.DistanceXZ(Claims.At(x), c.Position) < Shielings.Far) return $"too near home: a shieling lies {Mathf.RoundToInt(Shielings.Far)} m or more from the colony";
            if (!Shielings.GoodGround(x.Biome)) return "a shieling wants meadow, forest or mountain grass";
            if (ZoneSystem.instance != null && ZoneSystem.instance.GetGroundHeight(Claims.At(x)) < ZoneSystem.instance.m_waterLevel) return "the stake stands in water: a shieling wants dry grass";
            return "";
        }

        // The herder's work: up at the start of summer, down at its end.
        public static Task Next(Brain b)
        {
            Colony c = b.Colony;
            if (c == null || !c.IsOwner || b.S.Prof != Prof.Herder || Time.time < b.NoClaimUntil) return null;
            var mine = Claims.Of(c).Where(x => x.Shieling).ToList();
            if (mine.Count == 0) return null;
            b.NoClaimUntil = Time.time + 30;
            Date today = Year.Today();
            if (TestSeason.HasValue) today.Season = TestSeason.Value; // (test: shieling summer|autumn)
            foreach (Claim x in mine)
            {
                Herd h = Get(x.Id);
                if (Shielings.ComeDown(today.Season, h) && !h.Fetching) return new FetchTask(x);
            }
            Claim up = mine.FirstOrDefault(x => WhyNot(c, x) == "" && Shielings.GoUp(today.Season, today.Year, Get(x.Id)));
            if (up == null) { Claim bad = mine.FirstOrDefault(x => WhyNot(c, x) != ""); if (bad != null) Gear.Problem = WhyNot(c, bad); return null; }
            if (mine.Any(x => Get(x.Id)?.Up == true)) return null; // (one herd up at a time)
            var herd = Beasts(c);
            if (herd.Count == 0) { Gear.Problem = "no tame beasts in the pasture to take up to the shieling"; return null; }
            return new DriveTask(up, herd);
        }

        // A beast set out in the world, tame, as it was.
        public static Character Spawn(string prefab, Vector3 at, int level, string name, string tag)
        {
            GameObject p = ZNetScene.instance?.GetPrefab(prefab) ?? ZNetScene.instance?.m_prefabs.FirstOrDefault(x => x != null && string.Equals(x.name, prefab, System.StringComparison.OrdinalIgnoreCase)); // (an older record, lower-cased)
            if (p == null) return null;
            GameObject go = Object.Instantiate(p, at + Vector3.up * 0.3f, Quaternion.Euler(0, Random.Range(0f, 360f), 0));
            Character ch = go.GetComponent<Character>();
            if (ch == null) return null;
            ch.SetLevel(Mathf.Max(1, level));
            ch.SetTamed(true);
            ZDO z = go.GetComponent<ZNetView>()?.GetZDO();
            if (z != null) { if (!string.IsNullOrEmpty(name)) z.Set(ZDOVars.s_tamedName, name); if (tag != null) z.Set(TagKey, tag); }
            return ch;
        }
        // A spot of dry ground near the stake for a beast (none of it in the water, if there's land within 25 m).
        public static Vector3 Grass(Vector3 at)
        {
            float sea = ZoneSystem.instance.m_waterLevel;
            for (int i = 0; i < 24; i++)
            {
                Vector3 p = at + Quaternion.Euler(0, Random.Range(0f, 360f), 0) * Vector3.forward * Random.Range(3f, 9f + i);
                p.y = ZoneSystem.instance.GetGroundHeight(p);
                if (p.y > sea + 0.3f) return p;
            }
            return at;
        }
        public static (string prefab, int level, string name) Record(Character ch) =>
            (Utils.GetPrefabName(ch.gameObject), ch.GetLevel(), ch.GetComponent<ZNetView>()?.GetZDO()?.GetString(ZDOVars.s_tamedName, "") ?? "");

        // The stake's part, on the game that has its ground loaded: the herd set out in the pen; and once it's home, taken off.
        public static void AtStake(OreClaim stake)
        {
            if (stake.Id == "" || ZNetScene.instance == null || !ZNetScene.instance.IsAreaReady(stake.transform.position)) return;
            Herd h = Get(stake.Id);
            Vector3 at = stake.transform.position;
            var here = Character.GetAllCharacters().Where(ch => ch != null && ch.GetComponent<ZNetView>()?.GetZDO()?.GetString(TagKey, "") == stake.Id && Utils.DistanceXZ(ch.transform.position, at) < 80f).ToList();
            if (h != null && h.Up && !h.Placed)
            {
                foreach (var (prefab, level, name) in h.Beasts) { Vector3 p = Grass(at); Spawn(prefab, Colony.Spot(p, 3f) ?? p, level, name, stake.Id); }
                h.Placed = true; Save(h);
                return;
            }
            if ((h == null || !h.Up) && here.Count > 0)
                foreach (Character ch in here) { ZNetView v = ch.GetComponent<ZNetView>(); if (v != null && v.IsValid()) { if (!v.IsOwner()) v.ClaimOwnership(); ZNetScene.instance.Destroy(ch.gameObject); } }
        }

        // Home from the shieling (Claims.Home, the herder's trip being a claim trip): with the herd at summer's end, or back from driving it up.
        public static void Home(Colony c, Humanoid body, VillagerState s, Claim claim)
        {
            Herd h = Get(claim.Id);
            string where = $"the shieling {Claims.Where(claim, c.Position)}";
            if (h == null || !h.Fetching) { c.Log("text", s.Name, "", $"{s.Name} came back from {where}: the herd is up there for the summer."); return; }
            var (home, lost, young) = Shielings.Summer(h.Beasts, claim.Biome, () => Random.value);
            Zone pasture = c.R.Zones.FirstOrDefault(z => z.Type == ZoneType.Pasture);
            Vector3 pen = pasture != null ? Zones.Centre(pasture) : c.Position + c.Seat.transform.forward * 6f;
            int stronger = home.Count(b => h.Beasts.Any(o => o.name == b.name && o.prefab == b.prefab && o.level < b.level));
            foreach (var (prefab, level, name) in home) Spawn(prefab, Colony.Spot(pen, 3f) ?? pen, level, name, null);
            foreach (string y in young) Spawn(y, Colony.Spot(pen, 3f) ?? pen, 1, "", null);
            h.Up = false; h.Fetching = false; h.Placed = false; h.Beasts.Clear(); Save(h); // (the stake takes the summer's beasts off the pen when its ground next loads)
            var told = new List<string> { $"{home.Count} beast{(home.Count == 1 ? "" : "s")}" };
            if (young.Count > 0) told.Add($"{young.Count} young born up there");
            if (stronger > 0) told.Add($"{stronger} grown stronger");
            c.Log("text", s.Name, "", $"{s.Name} brought the herd down from {where}: {string.Join(", ", told)}{(lost > 0 ? $". The wolves had {lost}." : ".")}");
            Brain b = Villagers.BrainFor(body);
            if (b != null) { b.S.Bump("shielings"); b.Dirty(); }
        }

        // The stake's hover lines for a shieling.
        public static string Hover(Claim c, string home)
        {
            Colony col = Colony.All.FirstOrDefault(x => x.Valid && x.Id.ToLowerInvariant() == home);
            var t = new System.Text.StringBuilder();
            string why = col != null ? WhyNot(col, c) : "";
            if (why != "") t.Append($"\n<size=80%><color=#ff8c59>{char.ToUpper(why[0]) + why.Substring(1)}.</color></size>");
            Herd h = Get(c.Id);
            Date today = Year.Today();
            if (h != null && h.Up) t.Append($"\n<size=80%>The herd: {h.Beasts.Count} beast{(h.Beasts.Count == 1 ? "" : "s")} here for the summer{(h.Fetching ? "; the herder is on the way to bring them down" : ", till autumn")}.</size>");
            else t.Append($"\n<size=80%>A summer pasture: at the start of summer the herder drives the pasture's tame beasts up here, and fetches them home when it ends, fatter and with young. {(today.Season == Season.Summer ? "It's summer now." : "")}</size>");
            var (wound, death) = (Shielings.Loss(c.Biome), 0f);
            t.Append($"\n<size=80%>Wolves and weather: about {Mathf.RoundToInt(wound * 100)}% of the herd a summer.</size>");
            return t.ToString();
        }
    }

    // Driving the herd up: on foot, the beasts following, stragglers waited for; out of sight past the edge, the rest of the way.
    internal sealed class DriveTask : Task
    {
        private readonly Claim _claim; private readonly List<Character> _herd; private Vector3 _dest, _leg; private float _until, _nextFollow, _waitSince; private int _blocked;
        public const float Leg = 40f; // (the way out walked a stretch at a time: a path the whole way runs into land not yet loaded)
        public DriveTask(Claim claim, List<Character> herd) { _claim = claim; _herd = herd; }
        public override bool Interruptible => false;
        public override void Start(Brain b)
        {
            _dest = Claims.At(_claim); _dest.y = ZoneSystem.instance.GetGroundHeight(_dest);
            _until = Time.time + 600f;
            Doing = $"driving the herd up to the shieling";
            foreach (Character ch in _herd) ch.GetComponent<ZNetView>()?.GetZDO()?.Set(Shieling.TagKey, _claim.Id);
            Follow(b);
            b.Say(new[] { "Up we go, the lot of you. Green grass all summer.", "Come on, come on. The high pasture's waiting.", "Hup! Hup! To the shieling with you.", "Summer at the shieling. Keep together now." }[Random.Range(0, 4)], 5);
            b.Colony.Log("text", b.S.Name, "", $"{b.S.Name} set out to drive the herd ({_herd.Count}) up to the shieling {Claims.Where(_claim, b.Colony.Position)}.");
            NextLeg(b, 0f);
        }
        // The next stretch toward the shieling (turned a little, the more times the way's been blocked).
        private void NextLeg(Brain b, float turn)
        {
            Vector3 dir = _dest - b.Pos; dir.y = 0; float far = dir.magnitude; if (far < 0.1f) return;
            dir = Quaternion.Euler(0, turn, 0) * (dir / far);
            Vector3 p = b.Pos + dir * Mathf.Min(Leg, far);
            p.y = ZoneSystem.instance.GetGroundHeight(p);
            _leg = Colony.Spot(p, 6f) ?? p;
            b.Move.To(_leg, 3f);
        }
        private void Follow(Brain b)
        {
            foreach (Character ch in _herd) if (ch != null) ch.GetComponent<MonsterAI>()?.SetFollowTarget(b.Body.gameObject);
        }
        public override void Tick(Brain b)
        {
            Colony c = b.Colony;
            _herd.RemoveAll(ch => ch == null || ch.IsDead());
            if (_herd.Count == 0) { Done = true; return; }
            if (Time.time >= _nextFollow) { _nextFollow = Time.time + 2f; Follow(b); }
            float fromHome = Utils.DistanceXZ(b.Pos, c.Position), toGo = Utils.DistanceXZ(b.Pos, _dest);
            float nearest = Player.GetAllPlayers().Select(p => Utils.DistanceXZ(p.transform.position, b.Pos)).DefaultIfEmpty(float.MaxValue).Min();
            bool watched = nearest < 70f;
            // (there with someone watching: the beasts left in the pen, and the herder home)
            if (toGo < 14f) { Leave(b, true); return; }
            // (past the edge, out of sight: on out of the world, to be set out at the shieling when its ground loads)
            // (or before the ground under them unloads: a player's world reaches not much past a hundred metres)
            if (fromHome > c.Radius + 10f && !watched || nearest > 80f || Time.time > _until || _blocked >= 4 && fromHome > c.Radius) { Leave(b, false); return; }
            if (_blocked >= 4) { Home(b); Done = true; b.NoClaimUntil = Time.time + 300; b.Idle("no way to drive the herd to the shieling"); return; }
            // (stragglers waited for; a beast left far behind is brought up)
            float lag = _herd.Max(ch => Utils.DistanceXZ(ch.transform.position, b.Pos));
            if (lag > 16f)
            {
                if (_waitSince == 0) { _waitSince = Time.time; b.Move.Stop(); b.Say(new[] { "Come on, keep up!", "Hup! This way!", "Don't dawdle, you lot." }[Random.Range(0, 3)], 3); }
                if (Time.time - _waitSince > 12f)
                    foreach (Character ch in _herd.Where(ch => Utils.DistanceXZ(ch.transform.position, b.Pos) > 16f)) { Vector3 p = b.Pos - b.Body.transform.forward * 3f; ch.transform.position = (Colony.Spot(p, 3f) ?? p) + Vector3.up * 0.3f; }
                else return;
            }
            _waitSince = 0;
            // (a stretch walked: the next; blocked: another heading, then another)
            if (b.Move.Failed) { _blocked++; NextLeg(b, (_blocked % 2 == 1 ? 1 : -1) * 35f * ((_blocked + 1) / 2)); return; }
            if (b.Move.Arrived || !b.Move.Moving) { _blocked = 0; NextLeg(b, 0f); }
        }
        // (the drive given up near home: the beasts back in the pasture, where a herder looks for them next time)
        private void Home(Brain b)
        {
            Release();
            Zone pasture = b.Colony?.R.Zones.FirstOrDefault(z => z.Type == ZoneType.Pasture);
            if (pasture == null) return;
            Vector3 pc = Zones.Centre(pasture);
            foreach (Character ch in _herd.Where(ch => ch != null && !Zones.Inside(pasture, ch.transform.position))) ch.transform.position = (Colony.Spot(pc, 3f) ?? pc) + Vector3.up * 0.3f;
        }
        private void Release() { foreach (Character ch in _herd) if (ch != null) { ch.GetComponent<MonsterAI>()?.SetFollowTarget(null); ch.GetComponent<ZNetView>()?.GetZDO()?.Set(Shieling.TagKey, ""); } }
        private void Leave(Brain b, bool there)
        {
            Colony c = b.Colony;
            var h = Shieling.Get(_claim.Id) ?? new Herd { Claim = _claim.Id, Colony = c.Id.ToLowerInvariant() };
            h.Up = true; h.Placed = there; h.Fetching = false; h.Year = Year.Today().Year;
            h.Beasts = _herd.Select(Shieling.Record).ToList();
            if (there) foreach (Character ch in _herd) ch.GetComponent<MonsterAI>()?.SetFollowTarget(null);
            else foreach (Character ch in _herd) { ZNetView v = ch.GetComponent<ZNetView>(); if (v != null && v.IsValid()) { if (!v.IsOwner()) v.ClaimOwnership(); ZNetScene.instance.Destroy(ch.gameObject); } } // (on up the road, out of the world)
            Shieling.Save(h);
            // (the herder's walk: the rest of the way there and all the way back)
            float far = Utils.DistanceXZ(Claims.At(_claim), c.Position), left = Utils.DistanceXZ(Claims.At(_claim), b.Pos);
            double secs = (left + far) / Mining.Pace(_claim.Biome);
            b.S.Bump("drives"); b.Dirty();
            Done = true;
            b.Away(Mining.Encode(_claim.Id, secs, false));
        }
        public override void Stop(Brain b)
        {
            if (!Done) Home(b); // (cut short - a raid, the night)
            base.Stop(b);
        }
    }

    // Fetching the herd down at summer's end: off out of the colony that way, and away for the walk there and back.
    internal sealed class FetchTask : Task
    {
        private readonly Claim _claim; private Vector3 _edge;
        public FetchTask(Claim claim) { _claim = claim; }
        public override bool Interruptible => false;
        public override void Start(Brain b)
        {
            Colony c = b.Colony;
            Vector3 dir = Claims.At(_claim) - c.Position; dir.y = 0; if (dir.sqrMagnitude < 1) dir = Vector3.forward;
            _edge = c.Position + dir.normalized * Mathf.Min(c.Radius + 10, 35f);
            _edge = Colony.Spot(_edge, 4f) ?? _edge;
            Doing = "off to bring the herd down from the shieling";
            b.Move.To(_edge, 3f);
        }
        public override void Tick(Brain b)
        {
            if (!b.Move.Failed && !b.Move.Arrived && b.Move.Distance > 6f) { if (!b.Move.Moving) b.Move.To(b.Move.Goal, 3f); return; }
            Colony c = b.Colony;
            Herd h = Shieling.Get(_claim.Id);
            if (h == null || !h.Up) { Done = true; return; }
            h.Fetching = true; Shieling.Save(h);
            float far = Utils.DistanceXZ(Claims.At(_claim), c.Position);
            c.Log("text", b.S.Name, "", $"{b.S.Name} set out to bring the herd down from the shieling {Claims.Where(_claim, c.Position)}: summer's over.");
            Done = true;
            b.Away(Mining.Encode(_claim.Id, 2 * far / Mining.Pace(_claim.Biome) + Clock.DayLength * 0.1, false));
        }
    }
}
