using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // Ranging for game: a hunter whose grounds hold nothing to hunt, and the stores short of meat, goes out past the colony's edge a while
    // (a lookout 50 m beyond it, a new way each time) to find a deer or a boar, brings it down and carries it home. Nothing seen, home
    // again, and not out again for a while. (The world past a player's own ground stands still: no further than the scout goes.)
    internal sealed class RangeTask : Task
    {
        public const float Beyond = 50f, Farthest = 150f, Sight = 40f, Every = 480f; // (past the edge's own buildings, where the game spawns no game at all: 30 m out found only necks by the water)
        public static readonly string[] Meats = { "RawMeat", "DeerMeat", "NeckTail", "WolfMeat", "LoxMeat", "SerpentMeat", "HareMeat", "ChickenMeat", "BugMeat", "AsksvinMeat", "VoltureMeat", "BoneMawSerpentMeat" };
        public static int Meat(Colony c) => Meats.Sum(m => Stock.Count(c, m));
        // Out today? Daylight, no raid, a bow (or a weapon) to hunt with, and less meat in the stores than a day's eating for two of every three.
        public static bool Wanted(Brain b)
        {
            Colony c = b.Colony;
            if (c == null || !c.IsOwner || b.S.Prof != Prof.Hunter || Time.time < b.NoRangeUntil || c.RaidOn) return false;
            if (c.Hour < 7 || c.Hour > 16.5) return false;
            if (Gear.Tool(b, "bow") == null && !Gear.CanFight(b)) return false;
            return Meat(c) < Mathf.Max(6, c.Population * 2 / 3);
        }
        private Vector3 _out; private bool _back; private float _until, _nextLook, _lookUntil;
        public override void Start(Brain b)
        {
            Colony c = b.Colony;
            b.NoRangeUntil = Time.time + Every;
            // (game seen from the edge, that no one's after yet: that way; else each time another way, the golden angle round, and two
            // hunters not the same way)
            var taken = c.Board.All.Where(x => x.Kind == "hunt" && (x.State == OrderState.Claimed || x.State == OrderState.Blocked)).Select(x => x.Target).ToList();
            Character seen = Character.GetAllCharacters().Where(x => Work.Prey(x) && Utils.DistanceXZ(x.transform.position, c.Position) < Farthest + 20f && x.GetComponent<ZNetView>()?.IsValid() == true
                    && !taken.Contains(Work.Key(x.GetComponent<ZNetView>())) && !Others(b, x.transform.position))
                .OrderBy(x => Utils.DistanceXZ(x.transform.position, b.Pos)).FirstOrDefault();
            float angle = (b.S.Count("ranged") * 137.5f + (b.S.Vid.GetHashCode() & 0xff) * 1.41f) % 360f;
            Vector3 dir = seen != null ? seen.transform.position - c.Position : Quaternion.Euler(0, angle, 0) * Vector3.forward; dir.y = 0; dir.Normalize();
            Vector3 look = seen != null ? seen.transform.position : c.Position + dir * Mathf.Min(c.Radius + Beyond, Farthest);
            _out = Colony.Spot(look, 12f) ?? look;
            Doing = "ranging for game to the " + ScoutTask.Compass(dir);
            _until = Time.time + 200f;
            b.S.Bump("ranged"); b.Dirty();
            b.Move.To(_out, 4f);
        }
        public override void Tick(Brain b)
        {
            Colony c = b.Colony;
            if (c == null || c.RaidOn || Time.time > _until) { Done = true; return; }
            if (!_back && Time.time >= _nextLook)
            {
                _nextLook = Time.time + 1.5f;
                var passed = c.Board.All.Where(x => x.Kind == "hunt" && (x.State == OrderState.Claimed || x.State == OrderState.Blocked)).Select(x => x.Target).ToList(); // (another's, or let be)
                Character prey = Character.GetAllCharacters().Where(x => Work.Prey(x) && Vector3.Distance(x.transform.position, b.Pos) < Sight && x.GetComponent<ZNetView>()?.IsValid() == true && !passed.Contains(Work.Key(x.GetComponent<ZNetView>())))
                    .OrderBy(x => Vector3.Distance(x.transform.position, b.Pos)).FirstOrDefault();
                if (prey != null)
                {
                    Vector3 p = prey.transform.position;
                    int id = c.Board.Post("hunt", Work.Key(prey.GetComponent<ZNetView>()), Prof.Hunter, 4, p.x, p.y, p.z, c.Now, Zone, "ranging");
                    Order o = c.Board.All.FirstOrDefault(x => x.Id == id);
                    if (o != null && c.Board.Claim(id, b.S.Vid, c.Now, out _))
                    {
                        b.Say(new[] { "There. Quiet now.", "Fresh tracks... there it is.", "Meat for the pot, if I'm quick.", "Hold still, hold still.", "There's supper." }[Random.Range(0, 5)], 4);
                        Stop(b); var hunt = new WorkTask(o); b.Task = hunt; hunt.Start(b); // (the hunt itself as the grounds' are hunted, and the kill carried home)
                        return;
                    }
                }
            }
            if (!_back)
            {
                if (b.Move.Failed || b.Move.Arrived || !b.Move.Moving && b.Move.Distance < 8f)
                {
                    // (a look about out there before turning home)
                    if (_lookUntil == 0) _lookUntil = Time.time + 20f;
                    if (Time.time < _lookUntil) { b.Ai.LookTowards(Quaternion.Euler(0, Time.time * 20f, 0) * Vector3.forward); return; }
                    _until = Time.time + 90f; // (the walk home)
                    _back = true; Doing = "coming back from ranging, empty-handed"; b.Move.To(c.Position + (b.Pos - c.Position).normalized * 8f, 4f);
                }
                else if (!b.Move.Moving) b.Move.To(_out, 4f);
                return;
            }
            if (b.Move.Arrived || b.Move.Failed || Utils.DistanceXZ(b.Pos, c.Position) < 14f) Done = true;
            else if (!b.Move.Moving) b.Move.To(c.Position, 6f);
        }
        // (another hunter out ranging that way already)
        private static bool Others(Brain b, Vector3 at) => Villagers.All.Any(o => o != b && o.Alive && o.S.Colony == b.S.Colony && o.Task is RangeTask r && Utils.DistanceXZ(r._out, at) < Sight);
        // (the zone a ranging hunt is posted under: the hunt's reach runs past the colony's edge for it)
        public const string Zone = "range";
    }

    // A foraging trip: a forager whose grounds have nothing ripe, and the stores short of what the kitchen and the fermenter want
    // (honey, raspberries, blueberries, mushrooms), goes out past the edge to pick it, and knocks down a wild beehive for its honey
    // and the queen (a queen bee in the stores is a beehive the jarl can build). Where they found something, they go back to next time.
    internal sealed class ForageTrip : Task
    {
        public const float Sight = 35f, Every = 420f;
        public static readonly (string item, int keep)[] Wants = { ("Honey", 10), ("Raspberry", 20), ("Blueberries", 10), ("Mushroom", 10), ("MushroomYellow", 12), ("Cloudberry", 10), ("Thistle", 6) };
        public static bool Short(Colony c, string item) => Wants.Any(w => w.item == item && Stock.Count(c, item) < w.keep);
        // (no hive of the colony's own and no queen in the stores: a wild hive's queen is wanted too)
        public static bool QueenWanted(Colony c) => Stock.Count(c, "QueenBee") == 0 && !c.PiecesIn<Beehive>(c.Radius).Any();
        public static bool Wanted(Brain b)
        {
            Colony c = b.Colony;
            if (c == null || !c.IsOwner || b.S.Prof != Prof.Forager || Time.time < b.NoForageUntil || c.RaidOn) return false;
            if (c.Hour < 7 || c.Hour > 16.5) return false;
            return Wants.Any(w => Stock.Count(c, w.item) < w.keep);
        }
        // Where each found something last (by villager): back there next time, while it's fresh.
        private static readonly Dictionary<string, (Vector3 at, float t)> Patch = new Dictionary<string, (Vector3, float)>();
        private Vector3 _out; private bool _back; private float _until, _nextLook, _lookUntil;
        public override void Start(Brain b)
        {
            Colony c = b.Colony;
            b.NoForageUntil = Time.time + Every;
            Vector3 dir;
            if (Patch.TryGetValue(b.S.Vid, out var last) && Time.time - last.t < 1800f) { _out = last.at; dir = last.at - c.Position; }
            else
            {
                // (a look round first: six ways, in the colony's own outskirts and past its edge; the way with the most worth picking,
                // else each time another way)
                long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                float a0 = (b.S.Count("trips") * 137.5f + (b.S.Vid.GetHashCode() & 0xff) * 1.41f) % 360f;
                Vector3? richest = null; int most = 0;
                for (int k = 0; k < 6; k++)
                    foreach (float far in new[] { c.Radius * 0.6f, Mathf.Min(c.Radius + RangeTask.Beyond, RangeTask.Farthest) })
                    {
                        Vector3 at = c.Position + Quaternion.Euler(0, a0 + k * 60f, 0) * Vector3.forward * far;
                        int n = Count(c, at);
                        if (n > most) { most = n; richest = at; }
                    }
                Split.Add("forage.look", t0);
                dir = Quaternion.Euler(0, a0, 0) * Vector3.forward;
                Vector3 look = richest ?? c.Position + dir * Mathf.Min(c.Radius + RangeTask.Beyond, RangeTask.Farthest);
                if (richest != null) dir = richest.Value - c.Position;
                _out = Colony.Spot(look, 12f) ?? look;
            }
            dir.y = 0;
            Doing = "foraging out to the " + ScoutTask.Compass(dir);
            _until = Time.time + 200f;
            b.S.Bump("trips"); b.Dirty();
            b.Move.To(_out, 4f);
        }
        public override void Tick(Brain b)
        {
            Colony c = b.Colony;
            if (c == null || c.RaidOn || Time.time > _until) { Done = true; return; }
            if (!_back && Time.time >= _nextLook)
            {
                _nextLook = Time.time + 3f;
                if (Find(b, out GameObject thing, out string kind))
                {
                    ZNetView v = thing.GetComponent<ZNetView>(); Vector3 p = thing.transform.position;
                    int id = c.Board.Post(kind, Work.Key(v), Prof.Forager, 3, p.x, p.y, p.z, c.Now, RangeTask.Zone, "foraging trip");
                    Order o = c.Board.All.FirstOrDefault(x => x.Id == id);
                    if (o != null && c.Board.Claim(id, b.S.Vid, c.Now, out _))
                    {
                        Patch[b.S.Vid] = (p, Time.time);
                        b.NoForageUntil = 0; // (more where that was: back to it straight after this one)
                        Stop(b); var work = new WorkTask(o); b.Task = work; work.Start(b);
                        return;
                    }
                }
            }
            if (!_back)
            {
                if (b.Move.Failed || b.Move.Arrived || !b.Move.Moving && b.Move.Distance < 8f)
                {
                    if (_lookUntil == 0) _lookUntil = Time.time + 12f;
                    if (Time.time < _lookUntil) return;
                    Patch.Remove(b.S.Vid); // (picked clean, or nothing there: somewhere new next time)
                    _back = true; _until = Time.time + 90f; Doing = "coming back from foraging, empty-handed"; b.Move.To(c.Position + (b.Pos - c.Position).normalized * 8f, 4f);
                }
                else if (!b.Move.Moving) b.Move.To(_out, 4f);
                return;
            }
            if (b.Move.Arrived || b.Move.Failed || Utils.DistanceXZ(b.Pos, c.Position) < 14f) Done = true;
            else if (!b.Move.Moving) b.Move.To(c.Position, 6f);
        }
        // How much worth picking (and wild hives, while wanted) within sight of a spot.
        private static int Count(Colony c, Vector3 at)
        {
            bool honey = Short(c, "Honey") || QueenWanted(c); int n = 0;
            foreach (GameObject go in Work.Things(at, Sight).ToList())
            {
                if (go == null) continue;
                Pickable pk = go.GetComponent<Pickable>();
                if (pk != null && pk.m_itemPrefab != null && pk.CanBePicked() && Short(c, pk.m_itemPrefab.name)) n++;
                else if (honey && Utils.GetPrefabName(go) == "Beehive") n += 5;
            }
            return n;
        }
        // Something worth the walk within sight: a ripe bush or mushroom the stores are short of, or a wild beehive while honey or a
        // queen is wanted. (not one already taken or let be)
        private static bool Find(Brain b, out GameObject thing, out string kind)
        {
            thing = null; kind = null;
            Colony c = b.Colony;
            var passed = new HashSet<string>(c.Board.All.Where(x => (x.Kind == "pick" || x.Kind == "hive") && (x.State == OrderState.Claimed || x.State == OrderState.Blocked)).Select(x => x.Target));
            bool honey = Short(c, "Honey") || QueenWanted(c);
            float best = float.MaxValue;
            foreach (GameObject go in Work.Things(b.Pos, Sight).ToList())
            {
                if (go == null) continue;
                ZNetView v = go.GetComponent<ZNetView>(); if (v == null || !v.IsValid()) continue;
                string k = null;
                Pickable pk = go.GetComponent<Pickable>();
                if (pk != null && pk.m_itemPrefab != null && pk.CanBePicked() && Short(c, pk.m_itemPrefab.name)) k = "pick";
                else if (honey && Utils.GetPrefabName(go) == "Beehive" && go.GetComponent<IDestructible>() != null) k = "hive";
                if (k == null || passed.Contains(Work.Key(v))) continue;
                float d = Vector3.Distance(go.transform.position, b.Pos);
                if (d < best) { best = d; thing = go; kind = k; }
            }
            return thing != null;
        }
    }
}
