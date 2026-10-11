using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // Time off, the Norse way, for a villager with no work to do: hnefatafl at a table in the hall, glíma (wrestling) with someone who'll
    // take them on, sparring at a training dummy, shooting at an archery target, lifting stones to show their strength, a walk about
    // the colony; and failing all of those, passing the time in the great hall. What kept them idle is kept beside it (the colony
    // window's "Needs you" still hears it).
    internal static class Leisure
    {
        public const string IdleKey = "jarldom_idle";
        // Someone waiting for a partner, by colony: a tafl board or a wrestling ring.
        private static readonly Dictionary<string, LeisureTask> Seeking = new Dictionary<string, LeisureTask>();
        public static void Clear() => Seeking.Clear();

        public static Task Next(Brain b, string why, bool indoors = false)
        {
            ZDO z = b.View.GetZDO();
            if (z.GetString(IdleKey, "") != why) z.Set(IdleKey, why);
            Colony c = b.Colony;
            // A partner waiting for one more?
            if (Seeking.TryGetValue(c.Id, out LeisureTask host) && host.Seeking && host.Owner != b && host.Owner.Alive && Vector3.Distance(host.Owner.Pos, b.Pos) < 60f)
            {
                Seeking.Remove(c.Id);
                return host.Join(b, why);
            }
            var rng = new System.Random(b.S.Vid.GetHashCode() ^ (int)(Time.time / 30f));
            var kinds = new List<(string kind, int weight)>();
            // No tankard of their own: carve one at the workbench, from the stores (fine wood and resin).
            if (Gear.Tool(b, "tankard") == null && Carve.Can(c)) return new Carve();
            if (indoors)
            {
                // (out of the storm: a game at a table, or the hall and its talk)
                var inside = new LeisureTask(TaflTable(c, out _, out _) && rng.Next(3) == 0 ? "tafl" : "hall", why);
                if (inside.Kind == "tafl") { inside.Seeking = true; Seeking[c.Id] = inside; }
                return inside;
            }
            bool armed = Gear.Weapon(b) != null && Gear.Weapon(b).m_shared.m_itemType != ItemDrop.ItemData.ItemType.Bow;
            bool fighter = b.S.Prof == Prof.Guard || b.S.Prof == Prof.Hunter || b.S.Has("brave");
            if (TaflTable(c, out _, out _)) kinds.Add(("tafl", b.S.Has("lazy") ? 4 : 3));
            kinds.Add(("glima", b.S.Has("strong") || b.S.Has("brave") ? 4 : 2));
            if (armed && Find(c, "piece_TrainingDummy") != null) kinds.Add(("dummy", fighter ? 6 : 2));
            if (Gear.Tool(b, "bow") is ItemDrop.ItemData bow && Gear.Items(b).Where(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Ammo && i.m_shared.m_ammoType == bow.m_shared.m_ammoType).Sum(i => i.m_stack) >= 20 && Find(c, "piece_ArcheryTarget") is Piece target && LeisureTask.Range(target) != null) // (only a target that can be shot at)
                kinds.Add(("archery", b.S.Prof == Prof.Hunter ? 6 : 2));
            kinds.Add(("stones", b.S.Has("strong") ? 3 : 1));
            kinds.Add(("stroll", 2));
            kinds.Add(("hall", 4));
            int total = kinds.Sum(k => k.weight), roll = rng.Next(total);
            string kind = kinds.First(k => (roll -= k.weight) < 0).kind;
            var task = new LeisureTask(kind, why);
            if (kind == "tafl" || kind == "glima") { task.Seeking = true; Seeking[c.Id] = task; }
            return task;
        }
        public static Piece Find(Colony c, string prefab) => c.PiecesIn(c.Radius).FirstOrDefault(p => Utils.GetPrefabName(p.gameObject) == prefab);
        // A table in a room with two free seats at it.
        public static bool TaflTable(Colony c, out Chair a, out Chair b2)
        {
            a = b2 = null;
            foreach (Piece t in c.PiecesIn(c.Radius).Where(p => Classes.IsTable(p) && c.RoomAt(p.transform.position + Vector3.up * 0.3f) != null))
            {
                var list = new List<Piece>(); Piece.GetAllPiecesInRadius(t.transform.position, 2.6f, list);
                var chairs = list.Select(p => p.GetComponent<Chair>()).Where(ch => ch != null && ch.m_attachPoint != null && !Classes.IsHighSeat(ch.gameObject) && Seats.IsFree(ch)).ToList();
                if (chairs.Count < 2) continue;
                // (two facing each other across it, as near opposite as the seats allow)
                var pair = chairs.SelectMany(x => chairs.Where(y => y != x).Select(y => (x, y))).OrderByDescending(p => Vector3.Distance(p.x.transform.position, p.y.transform.position)).First();
                a = pair.x; b2 = pair.y;
                return true;
            }
            return false;
        }
        public static void Unseek(LeisureTask t) { foreach (var kv in Seeking.Where(kv => kv.Value == t).ToList()) Seeking.Remove(kv.Key); }
        public static void Clean(Brain b) { ZDO z = b.View?.GetZDO(); if (z != null && z.GetString(IdleKey, "") != "") z.Set(IdleKey, ""); }
    }

    internal sealed class LeisureTask : Task
    {
        public readonly string Kind, Why; public Brain Owner, Partner; public bool Seeking, Host = true;
        private float _until, _next, _seekUntil; private int _beat; private Vector3 _spot; private Chair _seat; private Piece _thing; private bool _there, _looked; private Fireplace _fire;
        public LeisureTask(string kind, string why) { Kind = kind; Why = why; }
        public string Tool => Kind == "dummy" && _there ? "weapon" : Kind == "archery" && _there ? "bow" : "";
        public LeisureTask Join(Brain b, string why)
        {
            // The one who waited sets the place; the one who came takes the other side.
            Partner = b; Seeking = false;
            Doing = Kind == "tafl" ? $"playing hnefatafl with {b.S.Name}" : $"wrestling with {b.S.Name}";
            var t = new LeisureTask(Kind, why) { Partner = Owner, Host = false };
            if (Kind == "tafl" && Leisure.TaflTable(Owner.Colony, out Chair x, out Chair y)) { _seat = x; t._seat = y; Seats.Take(Owner, x); Seats.Take(b, y); }
            else if (Kind == "glima") { _spot = Owner.Pos; t._spot = Owner.Pos + Owner.Body.transform.forward * 1.5f; }
            Owner.Move.Stop(); _there = false; _until = Time.time + 75f;
            if (_seat != null) Owner.Move.To(_seat.transform.position, 1.2f); else if (Kind == "glima") _there = true;
            return t;
        }
        public override void Start(Brain b)
        {
            Owner = b;
            _until = Time.time + Random.Range(50f, 90f);
            _seekUntil = Time.time + 20f;
            switch (Kind)
            {
                case "tafl": Doing = Partner == null ? "looking for someone to play hnefatafl" : $"playing hnefatafl with {Partner.S.Name}"; break;
                case "glima": Doing = Partner == null ? "looking for someone to wrestle" : $"wrestling with {Partner.S.Name}"; break;
                case "dummy": _thing = Leisure.Find(b.Colony, "piece_TrainingDummy"); Doing = "sparring at the training dummy"; break;
                case "archery": _thing = Leisure.Find(b.Colony, "piece_ArcheryTarget"); Doing = "shooting at the archery target"; break;
                case "stones": Doing = "lifting stones"; _spot = Colony.Spot(b.Colony.Position, 14f) ?? b.Pos; break;
                case "stroll": Doing = "taking a walk"; _spot = Colony.Spot(b.Colony.Position, b.Colony.Radius * 0.6f) ?? b.Pos; break;
                default:
                    Room hall = b.Colony.Best(RoomType.GreatHall);
                    if (hall != null) { _seat = Seats.Free(b, hall, b.Colony); _spot = _seat != null ? _seat.transform.position : Colony.Spot(b.Colony.Centre(hall), 3f) ?? b.Colony.Centre(hall); } // (a spot on the floor: the navmesh covers table tops too)
                    else _spot = b.Colony.Position + b.Colony.Seat.transform.forward * 4f;
                    Doing = hall != null ? "passing the time in the hall" : "passing the time by the pillars";
                    break;
            }
            if (Partner != null && _seat != null) b.Move.To(_seat.transform.position, 1.2f);
            else if (Partner != null && Kind == "glima") b.Move.To(_spot, 0.6f);
            else if (_thing != null) b.Move.To(Stand(b), 1.2f);
            else if (Kind != "tafl" && Kind != "glima") b.Move.To(_spot, 1.5f);
        }
        private Vector3 Stand(Brain b)
        {
            if (Kind == "archery") { if (_range == null) _range = Range(_thing) ?? _thing.transform.position - _thing.transform.forward * 12f; return _range.Value; }
            Vector3 d = b.Pos - _thing.transform.position; d.y = 0; if (d.sqrMagnitude < 0.01f) d = Vector3.forward;
            return _thing.transform.position + d.normalized * 1.4f;
        }
        private float _nextWork, _nextSip; private int _openAtStart = -1;
        // A place to shoot from: in front of the target's face, as far as 15 m, with nothing between (a fence, a wall, a hill).
        private Vector3? _range;
        private static int _sightMask = -1;
        public static Vector3? Range(Piece target)
        {
            if (_sightMask == -1) _sightMask = LayerMask.GetMask("Default", "static_solid", "terrain", "piece", "Default_small");
            Transform t = target.transform;
            Vector3 centre = (t.Find("ArcheryTarget/Center") ?? t).position;
            foreach (float d in new[] { 14f, 11f, 8f })
                foreach (float ang in new[] { 0f, -25f, 25f, -50f, 50f })
                {
                    Vector3 dir = Quaternion.Euler(0, ang, 0) * -t.forward; dir.y = 0;
                    Vector3? at = Colony.Spot(t.position + dir.normalized * d, 1.5f);
                    if (at == null) continue;
                    Vector3 eye = at.Value + Vector3.up * 1.5f;
                    if (Physics.Linecast(eye, centre, out RaycastHit hit, _sightMask, QueryTriggerInteraction.Ignore) && hit.collider.GetComponentInParent<Piece>() != target) continue; // (something in the way)
                    return at;
                }
            return null;
        }
        public override void Tick(Brain b)
        {
            if (Time.time > _until) { Finish(b); return; }
            // (work of their own trade turned up: back to it)
            if (Time.time > _nextWork && b.Colony.IsOwner && b.S.Prof != Prof.Hauler)
            {
                _nextWork = Time.time + 8f;
                int open = b.Colony.Board.All.Count(o => o.State == OrderState.Open && o.Prof == b.S.Prof); // (more than when they sat down: something new)
                if (_openAtStart < 0) _openAtStart = open; else if (open > _openAtStart) { Done = true; return; }
            }
            if (Seeking)
            {
                // Nobody came: something else to do.
                if (Time.time > _seekUntil) { Seeking = false; Done = true; return; }
                if (!b.Move.Moving && Random.value < 0.02f) { Vector3? to = Colony.Spot(b.Pos, 4f); if (to != null) b.Move.To(to.Value, 1f); }
                return;
            }
            if (Partner != null && (!Partner.Alive || !(Partner.Task is LeisureTask p) || p.Partner != b)) { Done = true; return; } // (they were called away)
            switch (Kind)
            {
                case "tafl": Tafl(b); break;
                case "glima": Glima(b); break;
                case "dummy": Spar(b); break;
                case "archery": Shoot(b); break;
                case "stones": Stones(b); break;
                case "stroll": if (!b.Move.Moving) { if (Time.time > _next) { _next = Time.time + Random.Range(8f, 15f); Vector3? to = Colony.Spot(b.Colony.Position, b.Colony.Radius * 0.6f); if (to != null) b.Move.To(to.Value, 1.5f); } } break;
                default: Hall(b); break;
            }
        }
        private bool Arrive(Brain b, Vector3 to, float within)
        {
            if (_there) return true;
            if (b.Move.Failed) { Done = true; return false; }
            if (b.Move.Moving && Utils.DistanceXZ(b.Pos, to) > within) return false;
            if (Utils.DistanceXZ(b.Pos, to) > within + 1.5f) { b.Move.To(to, within); return false; }
            b.Move.Stop(); _there = true;
            return true;
        }
        private void Face(Brain b, Vector3 at) { Vector3 d = at - b.Pos; d.y = 0; if (d.sqrMagnitude > 0.01f) b.Ai.LookTowards(d.normalized); }

        private void Tafl(Brain b)
        {
            if (_seat == null) { Done = true; return; }
            if (b.Held == null) { if (!Arrive(b, _seat.transform.position, 1.4f)) return; b.Sit(_seat.m_attachPoint, string.IsNullOrEmpty(_seat.m_attachAnimation) ? "attach_chair" : _seat.m_attachAnimation, Vector3.zero); _next = Time.time + 6f; return; }
            if (Partner.Held == null || Time.time < _next || !Host) return;
            _next = Time.time + Random.Range(8f, 14f);
            _beat++;
            string[] talk = { "Your king is boxed in!", "I'll take that one.", "Mind your corner.", "You've left a gap there.", "The king runs!", "Hm. Clever." };
            (Random.value < 0.5f ? b : Partner).Say(talk[Random.Range(0, talk.Length)], 4);
            if (_beat >= 5) { Result(b, Partner, "hnefatafl", "tafl"); _until = 0; }
        }
        private void Glima(Brain b)
        {
            if (!Arrive(b, _spot, 0.8f)) return;
            Face(b, Partner.Pos);
            if (!Host || Time.time < _next || Vector3.Distance(b.Pos, Partner.Pos) > 3f) return;
            _next = Time.time + 2.2f; _beat++;
            if (_beat == 1) { Body_.Anim(b.Body)?.SetTrigger("emote_challenge"); Body_.Anim(Partner.Body)?.SetTrigger("emote_challenge"); b.Say("Glíma, then! Belt-hold.", 4); return; }
            if (_beat < 7) { Brain who = _beat % 2 == 0 ? b : Partner; Brain other = who == b ? Partner : b; other.Body.Stagger((other.Pos - who.Pos).normalized); return; } // (the shoves and holds)
            Result(b, Partner, "glíma", "glima");
            _until = 0;
        }
        // One wins (the stronger, more often), both are the merrier.
        private static void Result(Brain a, Brain c, string game, string key)
        {
            float pa = 1f + a.S.Skill(key == "glima" ? "Unarmed" : "Tafl") / 20f + (a.S.Has("strong") && key == "glima" ? 0.5f : 0), pc = 1f + c.S.Skill(key == "glima" ? "Unarmed" : "Tafl") / 20f + (c.S.Has("strong") && key == "glima" ? 0.5f : 0);
            Brain win = Random.value * (pa + pc) < pa ? a : c, lose = win == a ? c : a;
            if (key == "glima") { lose.Body.Stagger((lose.Pos - win.Pos).normalized); Body_.Anim(win.Body)?.SetTrigger("emote_flex"); win.Say($"Down you go, {lose.S.Name}!", 4); lose.Say("Again tomorrow.", 4); }
            else { Body_.Anim(win.Body)?.SetTrigger("emote_cheer"); win.Say("The king is mine!", 4); }
            win.S.Bump(key + "wins");
            foreach (Brain x in new[] { a, c })
            {
                Mood.Add(x.S, "sport", x.Now); Mood.Satisfy(x.S, Need.Social, 15);
                x.S.Skills[key == "glima" ? "Unarmed" : "Tafl"] = Mathf.Min(100, x.S.Skill(key == "glima" ? "Unarmed" : "Tafl") + 0.5f);
                Bonds(x, x == a ? c : a, 3);
                x.Dirty();
            }
            if (win.S.Count(key + "wins") == 10) win.Colony?.Log("text", win.S.Name, "", key == "glima" ? $"{win.S.FullName} has thrown ten at glíma: none in the colony wrestles better." : $"{win.S.FullName} has won ten games of hnefatafl: the hall's best player.");
        }
        private static void Bonds(Brain x, Brain y, int by) => x.S.Bonds[y.S.Vid] = Mathf.Clamp((x.S.Bonds.TryGetValue(y.S.Vid, out int v) ? v : 0) + by, -100, 100);
        private void Spar(Brain b)
        {
            if (_thing == null) { Done = true; return; }
            if (!Arrive(b, Stand(b), 0.6f)) return;
            Character dummy = _thing.GetComponent<Character>();
            Face(b, _thing.transform.position);
            if (Time.time < _next || b.Body.InAttack()) return;
            _next = Time.time + Random.Range(1.2f, 2f);
            if (dummy != null && b.Ai.IsLookingAt(dummy.GetCenterPoint(), 30f)) { b.Body.StartAttack(dummy, false); if (++_beat % 6 == 0) Mood.Satisfy(b.S, Need.Safety, 2); }
            if (_beat >= 20) Finish(b);
        }
        private void Shoot(Brain b)
        {
            if (_thing == null) { Done = true; return; }
            if (!Arrive(b, Stand(b), 1.5f)) return;
            Transform aim = _thing.transform.Find("ArcheryTarget/Center") ?? _thing.transform;
            Vector3 from = b.Pos + Vector3.up * 1.5f;
            b.Body.SetLookDir((aim.position - from).normalized, 0f);
            Face(b, aim.position);
            if (Time.time < _next || b.Body.InAttack()) return;
            ItemDrop.ItemData bow = b.Body.GetCurrentWeapon();
            if (bow == null || bow.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Bow) return;
            _next = Time.time + 3f;
            Body_.DrawTime(b.Body) = 10f; b.Body.StartAttack(null, false); Body_.DrawTime(b.Body) = 0f;
            if (++_beat >= 5) Finish(b); // (five arrows: the rest are for the hunt)
        }
        private void Stones(Brain b)
        {
            if (!Arrive(b, _spot, 1.5f)) return;
            if (Time.time < _next) return;
            _next = Time.time + Random.Range(5f, 8f);
            Body_.Anim(b.Body)?.SetTrigger(++_beat % 3 == 0 ? "emote_roar" : "emote_flex");
            if (_beat == 4 && Random.value < 0.3f) b.Say(new[] { "Heavier than it looks.", "Watch this!", "A stone for a strong back." }[Random.Range(0, 3)], 4);
        }
        private void Hall(Brain b)
        {
            if (b.Held != null) { Tankards.Sip(b, ref _nextSip); if (Time.time > _next) { _next = Time.time + Random.Range(25f, 50f); Talk.Gossip(b); } Mood.Satisfy(b.S, Need.Social, (float)(b.GameDt / Clock.Hour) * 4f); return; }
            if (!Arrive(b, _spot, _seat != null ? 1.2f : 1f)) return;
            if (_seat != null && _seat.m_attachPoint != null) { b.Sit(_seat.m_attachPoint, string.IsNullOrEmpty(_seat.m_attachAnimation) ? "attach_chair" : _seat.m_attachAnimation, Vector3.zero); return; }
            // (no seat: standing about the hearth, talking)
            if (!_looked) { _looked = true; _fire = Classes.NearestFire(b.Pos, 8f); } // (once: not four times a second)
            if (_fire != null) Face(b, _fire.transform.position);
            if (Time.time > _next) { _next = Time.time + Random.Range(25f, 50f); Talk.Gossip(b); }
            Tankards.Sip(b, ref _nextSip);
            Mood.Satisfy(b.S, Need.Social, (float)(b.GameDt / Clock.Hour) * 3f);
        }
        private void Finish(Brain b)
        {
            if (Kind == "dummy" || Kind == "archery" || Kind == "stones") { Mood.Add(b.S, "sport", b.Now); b.Dirty(); }
            Done = true;
        }
        public override void Stop(Brain b)
        {
            if (_seat != null) Seats.Free(b);
            if (Seeking) Leisure_Unseek(b);
            if (b.View != null && b.View.IsValid()) Body_.Anim(b.Body)?.SetTrigger("emote_stop"); // (not at shutdown: the body is going)
            base.Stop(b);
        }
        private void Leisure_Unseek(Brain b) => Leisure.Unseek(this);
    }
}

namespace Jarldom
{
    // Tankards: every villager keeps one (the game's own: a drink is its blow), and in the hall lifts it now and then.
    internal static class Tankards
    {
        public static void Sip(Brain b, ref float next)
        {
            if (Time.time < next) return;
            next = Time.time + UnityEngine.Random.Range(14f, 35f);
            ItemDrop.ItemData mug = b.Body.RightItem;
            if (mug == null || !Gear.IsTankard(mug) || b.Body.InAttack()) return;
            Body_.Anim(b.Body)?.SetTrigger("emote_drink"); // (the tankard's own drink)
            if (UnityEngine.Random.value < 0.15f) b.Say(new[] { "Skål!", "To the jarl!", "Another!", "To Odin, who sent us here." }[UnityEngine.Random.Range(0, 4)], 3);
            Mood.Satisfy(b.S, Need.Social, 1);
        }
    }

    // Carving a tankard at the workbench, from the stores: a villager's own, kept.
    internal sealed class Carve : Task
    {
        private static readonly (string item, int n)[] Needs = { ("FineWood", 5), ("Resin", 2) };
        private CraftingStation _bench; private int _phase, _beats; private float _next;
        public static bool Can(Colony c) => Bench(c) != null && Needs.All(n => Crafts.InStock(c, n.item) >= n.n);
        private static CraftingStation Bench(Colony c) => c.PiecesIn(c.Radius).Select(p => p.GetComponent<CraftingStation>()).FirstOrDefault(s => s != null && Utils.GetPrefabName(s.gameObject) == "piece_workbench" && Crafts.Unusable(s) == null);
        private int Have(Brain b, string item) => b.Body.GetInventory().GetAllItems().Where(i => Classes.Prefab(i) == item).Sum(i => i.m_stack);
        public override void Start(Brain b) { _bench = Bench(b.Colony); Doing = "carving a tankard"; if (_bench == null) Done = true; }
        public override void Tick(Brain b)
        {
            if (Done || _bench == null) { Done = true; return; }
            if (_phase == 0)
            {
                var missing = Needs.Where(n => Have(b, n.item) < n.n).ToList();
                if (missing.Count == 0) { _phase = 1; b.Move.To(_bench.transform.position, 2f); return; }
                var (want, count) = missing[0];
                Container from = Stock.Chests(b.Colony, b.Pos).Where(x => x.category != "treasury" && !ContainerAccess.InUse(x.chest)).Select(x => x.chest).FirstOrDefault(ch => ch.GetInventory().GetAllItems().Any(i => Classes.Prefab(i) == want));
                if (from == null) { Done = true; return; }
                if (Vector3.Distance(b.Pos, from.transform.position) > 2.4f) { if (!b.Move.Moving) b.Move.To(from.transform.position, 1.8f); if (b.Move.Failed) Done = true; return; }
                ItemDrop.ItemData stack = from.GetInventory().GetAllItems().First(i => Classes.Prefab(i) == want);
                if (Stock.Take(from, b.Body.GetInventory(), stack, count - Have(b, want)) == 0) Done = true;
                return;
            }
            if (Vector3.Distance(b.Pos, _bench.transform.position) > 3f) { if (!b.Move.Moving) b.Move.To(_bench.transform.position, 2f); if (b.Move.Failed) Done = true; return; }
            b.Move.Stop();
            Vector3 d = _bench.transform.position - b.Pos; d.y = 0; if (d.sqrMagnitude > 0.01f) b.Ai.LookTowards(d.normalized);
            if (Time.time < _next) return;
            _next = Time.time + 1.5f;
            Body_.Anim(b.Body)?.SetInt("crafting", _bench.m_useAnimation);
            if (++_beats < 4) return;
            Body_.Anim(b.Body)?.SetInt("crafting", 0);
            Inventory bag = b.Body.GetInventory();
            if (Needs.Any(n => Have(b, n.item) < n.n)) { Done = true; return; }
            foreach (var (item, n) in Needs)
                for (int left = n; left > 0;) { ItemDrop.ItemData st = bag.GetAllItems().FirstOrDefault(i => Classes.Prefab(i) == item); if (st == null) break; int k = System.Math.Min(left, st.m_stack); bag.RemoveItem(st, k); left -= k; }
            bag.AddItem("Tankard", 1, 1, 0, Colony.Hash(b.S.Vid), b.S.Name, new Vector2i(-1, -1), false, false, false);
            _bench.m_craftItemDoneEffects.Create(_bench.transform.position, Quaternion.identity);
            b.Say("A tankard of my own. Now for something to put in it.", 4);
            b.S.Bump("tankard"); b.Dirty();
            Done = true;
        }
        public override void Stop(Brain b) { Body_.Anim(b.Body)?.SetInt("crafting", 0); base.Stop(b); }
    }
}
