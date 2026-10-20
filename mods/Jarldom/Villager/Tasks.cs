using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jarldom
{
    // What a villager is doing now. A task starts, ticks a few times a second, and says when it's done.
    internal abstract class Task
    {
        public bool Done; public string Doing = "";
        public virtual bool Interruptible => true;
        public virtual void Start(Brain b) { }
        public abstract void Tick(Brain b);
        public virtual void Stop(Brain b) { b.Stand(); b.Move.Stop(); }
        public virtual string Debug(Brain b) => GetType().Name;
        protected float Started = Time.time;
    }

    internal sealed class WanderTask : Task
    {
        private readonly Vector3 _centre; private readonly float _radius; private float _until, _pauseUntil;
        public WanderTask(Vector3 centre, float radius, string doing) { _centre = centre; _radius = radius; Doing = doing; }
        public override void Start(Brain b) { _until = Time.time + UnityEngine.Random.Range(15f, 30f); }
        public override void Tick(Brain b)
        {
            if (Time.time > _until) { Done = true; return; }
            if (b.Move.Moving || Time.time < _pauseUntil) return;
            Vector3? to = Colony.Spot(_centre, _radius);
            if (to != null) b.Move.To(to.Value, 1f);
            _pauseUntil = Time.time + UnityEngine.Random.Range(4f, 10f);
        }
    }

    // A walk to somewhere, then a wait (used by work and errands).
    internal sealed class GoTask : Task
    {
        private readonly Vector3 _to; private readonly float _arrive; private readonly bool _run;
        public GoTask(Vector3 to, string doing, float arrive = 1.5f, bool run = false) { _to = to; Doing = doing; _arrive = arrive; _run = run; }
        public override void Start(Brain b) => b.Move.To(_to, _arrive, _run);
        public override void Tick(Brain b) { if (b.Move.Arrived || b.Move.Failed) Done = true; else if (!b.Move.Moving) b.Move.To(_to, _arrive, _run); }
    }

    // Called to the pillars: there, and then wait for the jarl a minute, facing them (they were called for something), unless a raid
    // comes or they're given something else to do.
    internal sealed class SummonTask : Task
    {
        private readonly Vector3 _to; private float _waitUntil;
        public SummonTask(Vector3 to) { _to = to; Doing = "coming to the pillars"; }
        public override bool Interruptible => false;
        public override void Start(Brain b) => b.Move.To(_to, 2f, true);
        public override void Tick(Brain b)
        {
            if (_waitUntil == 0)
            {
                if (b.Move.Failed) { Done = true; b.Say(new[] { "I can't find a way to the pillars.", "There's no way through to the pillars from here.", "I can't get to the pillars." }[UnityEngine.Random.Range(0, 3)], 4); return; }
                if (!b.Move.Arrived) { if (!b.Move.Moving) b.Move.To(_to, 2f, true); return; }
                _waitUntil = Time.time + 60f; Doing = "waiting at the pillars for the jarl";
            }
            if (Time.time > _waitUntil) { Done = true; return; }
            Player p = Player.GetClosestPlayer(b.Pos, 20f);
            if (p != null) { Vector3 d = p.transform.position - b.Pos; d.y = 0; if (d.sqrMagnitude > 0.01f) b.Ai.LookTowards(d.normalized); }
        }
    }

    // Sleep: to their own bed, else by the hall's fire, else by the pillars. Rest fills with the bed's comfort.
    internal sealed class SleepTask : Task
    {
        public bool JustRest;
        public const string BedFailKey = "jarldom_bedfail"; // (tag: their own bed couldn't be reached last night)
        private Bed _bed; private Transform _spot; private GameObject _marker; private bool _down; private float _comfort; private double _since; private bool _own, _fireTried;
        public override bool Interruptible => false;
        public override void Start(Brain b)
        {
            Doing = JustRest ? "resting" : "going to bed";
            if (!JustRest) Plugin.Guard(() => Greet.Night(b));
            _bed = b.Colony.BedById(b.S.Bed);
            // The injured rest in the healer's house, if it has a bed nobody holds.
            if (JustRest && b.Colony.Best(RoomType.HealersHouse) != null)
            {
                Bed sick = b.Colony.AllBeds().FirstOrDefault(x => b.Colony.RoomAt(x.transform.position, RoomType.HealersHouse) != null && (Colony.BedOwner(x, out _) == "" || x == _bed));
                if (sick != null) { _spot = sick.m_spawnPoint != null ? sick.m_spawnPoint : sick.transform; Doing = "resting at the healer's"; b.Move.To(_spot.position, 1.2f); _own = true; return; }
            }
            if (_bed != null) { _own = true; _spot = _bed.m_spawnPoint != null ? _bed.m_spawnPoint : _bed.transform; _approach = Approaches.Inside(b, _spot.position); b.Move.To(_approach ?? _spot.position, 1.2f); return; }
            ByTheFire(b);
        }
        // The floor nearest the bed inside its own room (the navmesh's nearest point may be outside the wall, beyond packed beds).
        private Vector3? _approach;
        // A place by the hall fire (no bed, or one that can't be got to).
        private void ByTheFire(Brain b)
        {
            _own = false; _approach = null;
            if (_marker != null) Object.Destroy(_marker);
            Room hall = b.Colony.Best(RoomType.GreatHall);
            Vector3 at = hall != null ? b.Colony.Centre(hall) : b.Colony.Position + b.Colony.Seat.transform.forward * 3;
            Fireplace fire = Classes.NearestFire(at, 12f, hall?.Xz);
            if (fire != null) at = fire.transform.position + (b.Pos - fire.transform.position).normalized * 1.8f;
            Vector3 spot = Colony.Spot(at, 1.5f) ?? at;
            _marker = new GameObject("JarldomSleepSpot");
            _marker.transform.position = spot;
            _marker.transform.rotation = Quaternion.LookRotation(fire != null ? (fire.transform.position - spot).normalized : Vector3.forward) * Quaternion.Euler(0, 90, 0);
            _spot = _marker.transform;
            b.Move.To(spot, 1.2f);
        }
        public override void Tick(Brain b)
        {
            if (_spot == null) { Done = true; return; }
            if (!JustRest && b.Colony.Hour >= Schedule.WakeAt && b.Colony.Hour < Schedule.HallAt) { Done = true; return; } // morning (not an early riser gone to bed at eight)
            if (!_down)
            {
                // (beside the bed in its room is near enough: beds packed against walls and each other leave no floor to walk onto)
                float d = Utils.DistanceXZ(b.Pos, _spot.position);
                Room here = b.Colony.RoomAt(b.Pos + Vector3.up * 0.3f);
                bool sameRoom = here != null && here == b.Colony.RoomAt(_spot.position + Vector3.up * 0.3f);
                bool atApproach = _approach.HasValue && Utils.DistanceXZ(b.Pos, _approach.Value) < 1.3f;
                bool beside = sameRoom && Mathf.Abs(b.Pos.y - _spot.position.y) < 1.5f && (d < 2.6f || (b.Move.Failed || atApproach) && d < 4.5f);
                if (!beside && b.Move.Failed)
                {
                    // (a bed that can't be got to: by the hall fire tonight, not trying all night)
                    if (_own && !_fireTried) { _fireTried = true; Doing = "can't get to my bed: sleeping by the hall fire"; Villagers.Tag(b.View.GetZDO(), BedFailKey, "1"); ByTheFire(b); return; }
                    Done = true; return;
                }
                if (!beside && !b.Move.Arrived && d > 1.6f) { if (!b.Move.Moving) b.Move.To(_approach ?? _spot.position, 1.2f); return; }
                b.Move.Stop();
                _down = true; _since = b.Now;
                _comfort = Colony.ComfortAt(_spot.position);
                b.Sit(_spot, "attach_bed", Vector3.zero);
                Doing = JustRest ? "resting in bed" : _own ? "asleep" : "asleep by the fire";
                if (_own && _bed != null) Villagers.Tag(b.View.GetZDO(), BedFailKey, ""); // (got to it: no longer a matter for the jarl)
                return;
            }
            double hours = (b.Now - _since) / Clock.Hour;
            Mood.Satisfy(b.S, Need.Rest, (float)(b.GameDt / Clock.Hour * (4 + _comfort))); // a night in a good bed fills it
            if (JustRest && b.Colony.Hour >= 22) JustRest = false;
            if (JustRest && Activity_Over(b)) Done = true;
        }
        private static bool Activity_Over(Brain b) => b.Body.GetHealthPercentage() > 0.5f && b.S.InjuredUntil < b.Now;
        public override void Stop(Brain b)
        {
            if (_down)
            {
                double hours = (b.Now - _since) / Clock.Hour;
                if (hours >= 3)
                {
                    if (_own) Mood.Add(b.S, "ownbed", b.Now); else Mood.Add(b.S, "hallfire", b.Now);
                    if (EnvMan.IsWet() && Colony.ComfortAt(b.Pos) <= 1) Mood.Add(b.S, "wetcold", b.Now);
                    Mood.Satisfy(b.S, Need.Rest, 100);
                }
            }
            base.Stop(b);
            if (_marker != null) Object.Destroy(_marker);
        }
        public static T Nearest<T>(Vector3 at, float radius) where T : Component
        {
            var list = Colony.PiecesNear(at, radius);
            return list.Select(p => Colony.CompOf<T>(p)).Where(c => c != null).OrderBy(c => Vector3.Distance(c.transform.position, at)).FirstOrDefault();
        }
    }

    // Eat: from the bag, else fetch from the colony's food chests (the hall's first).
    internal sealed class EatTask : Task
    {
        private Container _chest; private ItemDrop.ItemData _item;
        public override void Start(Brain b)
        {
            Doing = "having a bite";
            if (Food.EatFromBag(b)) { Done = true; return; }
            var near = Stock.Food(b.Colony, b.Pos).Where(x => Food.Fresh(b, x.item)).OrderByDescending(x => x.item.m_shared.m_food).FirstOrDefault();
            _chest = near.chest; _item = near.item;
            if (_chest == null) { Done = true; b.NoFoodUntil = Time.time + 60; return; }
            Doing = "fetching food";
            b.Move.To(_chest.transform.position, 1.8f);
        }
        public override void Tick(Brain b)
        {
            if (Done) return;
            if (_chest == null || b.Move.Failed) { Done = true; return; }
            if (!b.Move.Arrived && Stock.OutOfReach(b.Pos, _chest.transform.position, 2.4f)) { if (!b.Move.Moving) b.Move.To(_chest.transform.position, 1.8f); return; }
            int took = Stock.Take(_chest, b.Body.GetInventory(), _item, 2);
            if (took > 0) Food.EatFromBag(b); else b.NoFoodUntil = Time.time + 30;
            Done = true;
        }
    }

    // Evening in the great hall: a seat, food and company. With Shelter set: only out of the rain.
    internal sealed class HallTask : Task
    {
        public bool Shelter;
        private Chair _chair; private Vector3 _stand; private bool _there, _counted, _drank; private float _nextTalk, _nextSip; private int _tries;
        public override void Start(Brain b)
        {
            Room hall = b.Colony.Best(RoomType.GreatHall);
            if (hall == null && Shelter) hall = b.Colony.R.Rooms.FirstOrDefault(r => r.Ok);
            Doing = Shelter ? "out of the rain" : hall != null ? "off to the hall" : "by the pillars";
            Vector3 at = hall != null ? b.Colony.Centre(hall) : b.Colony.Position + b.Colony.Seat.transform.forward * 4;
            if (hall != null && !Shelter && b.S.Prof != Prof.Skald) _chair = Seats.Free(b, hall, b.Colony); // (a skald stands by the hearth to play)
            Fireplace hearth = hall != null && b.S.Prof == Prof.Skald ? Classes.NearestFire(at, 12f, hall.Xz) : null;
            if (hearth != null) at = hearth.transform.position + (b.Pos - hearth.transform.position).normalized * 2f;
            _stand = _chair != null ? _chair.transform.position : Colony.Spot(at, hearth != null ? 1f : 3f) ?? at;
            _approach = _chair != null && _chair.m_attachPoint != null ? Approaches.Inside(b, _chair.m_attachPoint.position) : null;
            b.Move.To(_approach ?? _stand, _chair != null ? 1.2f : 2f);
        }
        private Vector3? _approach;
        public override void Tick(Brain b)
        {
            if (!_there)
            {
                // (beside the seat is near enough to sit: a bench's seats are inside its collider, and walking onto one fails)
                Vector3? seat = _chair != null && _chair.m_attachPoint != null ? _chair.m_attachPoint.position : (Vector3?)null;
                float d = seat.HasValue ? Utils.DistanceXZ(b.Pos, seat.Value) : 99f;
                bool atApproach = _approach.HasValue && Utils.DistanceXZ(b.Pos, _approach.Value) < 1.3f;
                bool beside = seat.HasValue && Mathf.Abs(b.Pos.y - seat.Value.y) < 1.5f && (d < 2.2f || (b.Move.Failed || atApproach) && d < 4.5f) && b.Colony.RoomAt(b.Pos) == b.Colony.RoomAt(seat.Value); // (not through the wall)
                if (beside) b.Move.Stop();
                else if (b.Move.Failed && _chair != null && ++_tries <= 2 && b.Colony.Best(RoomType.GreatHall) is Room hall)
                {
                    // (that seat can't be got to: another, else a place on the floor)
                    _chair = Seats.Free(b, hall, b.Colony);
                    _stand = _chair != null ? _chair.transform.position : Colony.Spot(b.Colony.Centre(hall), 3f) ?? b.Colony.Centre(hall);
                    _approach = _chair != null && _chair.m_attachPoint != null ? Approaches.Inside(b, _chair.m_attachPoint.position) : null;
                    b.Move.To(_approach ?? _stand, _chair != null ? 1.2f : 2f);
                    return;
                }
                else if (b.Move.Failed) { Seats.Free(b); _chair = null; _there = true; Doing = "keeping company"; return; }
                else if (b.Move.Moving) return;
                if (!beside && !b.Move.Arrived && Utils.DistanceXZ(b.Pos, _approach ?? _stand) > 2.5f) { b.Move.To(_approach ?? _stand, _chair != null ? 1.2f : 2f); return; } // stopped by a fight
                _there = true;
                if (_chair != null && _chair.m_attachPoint != null) b.Sit(_chair.m_attachPoint, string.IsNullOrEmpty(_chair.m_attachAnimation) ? "attach_chair" : _chair.m_attachAnimation, Vector3.zero);
                Doing = Shelter ? "sheltering" : _chair != null ? "in the hall" : "keeping company";
            }
            Mood.Satisfy(b.S, Need.Social, (float)(b.GameDt / Clock.Hour) * (Shelter ? 6f : 12f)); // (huddled together in a raid is company too)
            if (Shelter) return;
            if (b.S.Prof == Prof.Skald && b.Held == null && Recite(b)) return; // (the night's verse first)
            if (b.S.Prof == Prof.Skald && b.Held == null && Time.time > _nextSong) Sing(b);
            if (!_counted && b.Colony.RoomAt(b.Pos, RoomType.GreatHall) != null) { _counted = true; Mood.Add(b.S, "hall", b.Now); b.S.Bump("halls"); }
            if (_counted && !_drank) { _drank = true; Drink(b); }
            if (b.S.Needs[(int)Need.Food] < 70 && Food.CanEat(b)) Food.EatFromBag(b);
            if (Time.time > _nextTalk) { _nextTalk = Time.time + UnityEngine.Random.Range(25f, 60f); Talk.Gossip(b); }
            Tankards.Sip(b, ref _nextSip);
        }
        public override void Stop(Brain b) { Seats.Free(b); base.Stop(b); }
        // A drink of the evening (SkalTavern's ale, mead and wine) from a chest in the hall, and a toast.
        private static void Drink(Brain b)
        {
            Room hall = b.Colony.RoomAt(b.Pos, RoomType.GreatHall);
            if (hall == null) return;
            foreach (var x in Stock.ChestsFor(b))
            {
                if (x.category == "treasury" || ContainerAccess.InUse(x.chest) || b.Colony.RoomAt(x.chest.transform.position + Vector3.up * 0.3f) != hall) continue;
                ItemDrop.ItemData drink = x.chest.GetInventory().GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name.StartsWith("dh_drink_"));
                if (drink == null) continue;
                string name = Localization.instance.Localize(drink.m_shared.m_name).ToLower();
                ContainerAccess.TakeOver(x.chest);
                x.chest.GetInventory().RemoveItem(drink, 1);
                Body_.Anim(b.Body)?.SetTrigger("emote_toast");
                b.Say(UnityEngine.Random.value < 0.5f ? "Skål!" : new[] { $"Good {name}!", $"Ah, that's a {name}!", $"A fine {name}!", $"That {name} goes down well!", $"Good {name}, that!" }[UnityEngine.Random.Range(0, 5)], 4);
                Mood.Add(b.S, "tavern", b.Now); Mood.Satisfy(b.S, Need.Social, 6);
                b.S.Bump("drinks"); b.Dirty();
                return;
            }
        }
        // The night's verse: once an evening, the skald sings the day's deeds (Core/Verse.cs) a line at a time, to the hall and to the
        // players in it, and the verse goes into the chronicle.
        private List<string> _verse; private int _line, _sung; private float _nextLine, _thereAt; private string _legend;
        public override bool Interruptible => _verse == null || _line >= _verse.Count; // (the verse is sung to its end, past bedtime; a raid still breaks in)
        public const string VerseKey = "verse:day", VerseAtKey = "verse:at";
        private bool Recite(Brain b)
        {
            Colony c = b.Colony;
            if (_verse == null)
            {
                // (a little after the skald has taken their place by the hall's hearth, whatever their hours)
                Room at = c.Best(RoomType.GreatHall);
                if (!c.IsOwner || !_there || Shelter || at == null || Utils.DistanceXZ(b.Pos, c.Centre(at)) > 25f || c.R.Flag(VerseKey) == c.Day) return false;
                if (_thereAt == 0) _thereAt = Time.time;
                if (Time.time - _thereAt < 20f) return false;
                c.R.Flags[VerseKey] = c.Day; c.MarkDirty();
                _verse = TheVerse(c); _sung = _verse.Count;
                // (then a tale read from a runestone, not yet told, as the colony's own)
                _legend = Legends.Untold(c);
                if (_legend != null) { var tale = Legends.Lines(_legend); if (tale.Count > 0) _verse.AddRange(tale); else { c.R.Flags[Legends.Prefix + _legend] = Legends.Told; _legend = null; } }
                _line = 0; _nextLine = Time.time + 2f;
                if (_verse.Count == 0) return false;
                c.R.Flags[VerseAtKey] = (int)c.Now;
                Body_.Anim(b.Body)?.SetTrigger("emote_cheer");
            }
            if (_line >= _verse.Count) return false;
            if (Time.time < _nextLine) return true;
            _nextLine = Time.time + 6.5f;
            string line = _verse[_line++];
            if (!WorkEmotes.Sing(b, true)) Body_.Anim(b.Body)?.SetTrigger("emote_headbang");
            Assets.Effect("sfx_wood_hit", b.Pos + Vector3.up);
            b.Say(line, 6.5f);
            Room hall = c.Best(RoomType.GreatHall);
            foreach (Player p in Player.GetAllPlayers().Where(p => p != null && hall != null && (c.RoomAt(p.transform.position) == hall || Vector3.Distance(p.transform.position, b.Pos) < 12f)))
                p.Message(MessageHud.MessageType.Center, $"<size=80%><i>{b.S.Name} sings: “{line}”</i></size>");
            if (_line >= _verse.Count)
            {
                if (_sung > 0) c.Log("verse", b.S.Name, "", string.Join(" / ", _verse.Take(_sung)));
                if (_legend != null) { c.R.Flags[Legends.Prefix + _legend] = Legends.Told; c.Log("legend", b.S.Name, "", string.Join(" ", _verse.Skip(_sung + 1))); c.MarkDirty(); }
                foreach (Brain o in Villagers.All.Where(o => o.S.Colony == c.Id && Vector3.Distance(o.Pos, b.Pos) < 20)) { Mood.Satisfy(o.S, Need.Social, 8); if (!Mood.Has(o.S, "skald", o.Now)) Mood.Add(o.S, "skald", o.Now); o.Dirty(); }
                b.S.Bump("verses");
            }
            return true;
        }
        // The deeds since the last verse (or today's), sung by the jarls' bynames.
        public static List<string> TheVerse(Colony c)
        {
            int since = c.R.Flag(VerseAtKey);
            var deeds = c.R.Log.Where(e => since > 0 ? e.Time > since : e.Day >= c.Day - 1).ToList();
            return Verse.Make(deeds, c.Day ^ c.Id.GetStableHashCode(), p => JarlDeeds.Named(c, p));
        }
        // The skald plays: a verse, a cheer, and everyone in the hall feels it.
        private float _nextSong;
        private static readonly string[] Verses = { "Cattle die, kinsmen die, the self must also die; but glory never dies for the one who earns it.", "Hear, all you hearers, of the hall of the slain!",
            "The wolf's food falls, the raven's feast is spread...", "Sing of the jarl who built this hall!", "Bring the horn, fill it high, and drink to those who fell!",
            "Fall in the shield-wall with your kin; the skalds will sing it.", "Fire takes the hall, but the song stays on.",
            "Sing for the sea-wolves who never came home.", "Ale for the living, a horn for the dead.", "Odin takes the brave, and the brave go gladly." };
        private void Sing(Brain b)
        {
            _nextSong = Time.time + UnityEngine.Random.Range(14f, 24f);
            // With a drum the hall dances; without one a skald can only chant, and it lifts no one much.
            bool drum = b.Body.GetInventory().GetAllItems().Any(Items.IsDrum);
            string[] moves = drum ? new[] { "emote_cheer", "emote_headbang", "emote_dance", "emote_toast" } : new[] { "emote_cheer", "emote_toast" };
            if (!WorkEmotes.Sing(b, drum)) Body_.Anim(b.Body)?.SetTrigger(moves[UnityEngine.Random.Range(0, moves.Length)]); // (drumming or reciting, with Bob's Animations)
            bool sung = HallSound.SongOn(b.S.Colony); // (singing one of the hall's songs aloud: no drum knocks or verses over it)
            if (drum && !sung) { Assets.Effect("sfx_wood_hit", b.Pos + Vector3.up); Assets.Effect("sfx_wood_hit", b.Pos + Vector3.up * 1.1f); }
            if (!sung) b.Say(drum ? Verses[UnityEngine.Random.Range(0, Verses.Length)] : UnityEngine.Random.value < 0.3f ? "(chants) ...if only I had a drum." : "(chants) " + Verses[UnityEngine.Random.Range(0, Verses.Length)], 6);
            if (!drum) { foreach (Brain o in Villagers.All.Where(o => o != b && o.S.Colony == b.S.Colony && Vector3.Distance(o.Pos, b.Pos) < 20)) { Mood.Satisfy(o.S, Need.Social, 1); o.Dirty(); } b.S.Bump("songs"); return; }
            b.S.Bump("songs");
            if (b.S.Count("songs") >= 100 && string.IsNullOrEmpty(b.S.Byname)) { b.S.Byname = Names.Byname("skald"); b.Colony.Log("byname", b.S.Name, b.S.Byname); }
            foreach (Brain o in Villagers.All.Where(o => o != b && o.S.Colony == b.S.Colony && Vector3.Distance(o.Pos, b.Pos) < 20)) { if (!Mood.Has(o.S, "skald", o.Now)) Mood.Add(o.S, "skald", o.Now); Mood.Satisfy(o.S, Need.Social, 3); o.Dirty(); }
        }
    }

    // Worn gear to a station that can mend it, the game's way: the recipe's own station (or its repair station), at the recipe's level.
    internal sealed class RepairTask : Task
    {
        private CraftingStation _at;
        public static List<ItemDrop.ItemData> Worn(Brain b) => b.Body.GetInventory().GetAllItems()
            .Where(i => i.m_shared.m_useDurability && i.m_shared.m_canBeReparied && i.m_durability < i.GetMaxDurability() * 0.35f && (!Fraying.Frays(i) || Fraying.CanMend(b.Colony, i))).ToList(); // (clothes: while they can still be mended)
        public static bool Can(CraftingStation st, ItemDrop.ItemData item)
        {
            if (Fraying.Frays(item) && Fraying.MendsAt(st)) return true; // (the sewing table mends all clothes)
            Recipe recipe = ObjectDB.instance?.GetRecipe(item);
            if (st == null || recipe == null || recipe.m_craftingStation == null && recipe.m_repairStation == null) return false;
            bool right = recipe.m_repairStation != null && recipe.m_repairStation.m_name == st.m_name || recipe.m_craftingStation != null && recipe.m_craftingStation.m_name == st.m_name;
            return right && Mathf.Min(st.GetLevel(), 4) >= recipe.m_minStationLevel;
        }
        public static CraftingStation Station(Brain b, List<ItemDrop.ItemData> worn) =>
            b.Colony.PiecesIn(b.Colony.Radius).Select(p => p.GetComponent<CraftingStation>()).Where(st => st != null && worn.Any(i => Can(st, i)))
                .OrderBy(st => Vector3.Distance(st.transform.position, b.Pos)).FirstOrDefault();
        public override void Start(Brain b)
        {
            Doing = "taking worn gear to be mended";
            _at = Station(b, Worn(b));
            if (_at == null) { Done = true; b.NoRepairUntil = Time.time + 120; return; }
            b.Move.To(_at.transform.position, 2f);
        }
        public override void Tick(Brain b)
        {
            if (_at == null || b.Move.Failed) { Done = true; b.NoRepairUntil = Time.time + 120; return; }
            Stock.Walk rw = Stock.WalkNear(b, _at.transform.position, 3f, 2f);
            if (rw == Stock.Walk.Unreachable) { Done = true; b.NoRepairUntil = Time.time + 120; return; }
            if (rw == Stock.Walk.Walking) return;
            int n = 0;
            foreach (ItemDrop.ItemData item in Worn(b).Where(i => Can(_at, i)))
            {
                if (!Fraying.Mend(b.Colony, item)) continue; // (clothes: thread from the stores)
                b.Body.RaiseSkill(Skills.SkillType.Crafting, 1f - item.m_durability / item.GetMaxDurability());
                item.m_durability = item.GetMaxDurability();
                n++;
            }
            if (n > 0) { _at.m_repairItemDoneEffects.Create(_at.transform.position, Quaternion.identity); b.Body.GetInventory().m_onChanged?.Invoke(); b.Say(n == 1 ? new[] { "Good as new.", "Mended and sound.", "Whole again.", "That's sound now.", "Back to new." }[UnityEngine.Random.Range(0, 5)] : new[] { $"All {n} mended.", $"{n} mended and good as new.", $"Mended, all {n} of them.", $"Those {n} are whole again.", $"All {n} are sound again." }[UnityEngine.Random.Range(0, 5)], 3); }
            Done = true;
        }
    }

    // Building a planned ghost (BuildOrders): the piece's exact materials from the stores, then the hammer at the spot. Paid in full or not
    // at all: nothing is taken from the bag until the piece stands.
    internal sealed class BuildTask : Task
    {
        private string _id, _prefab; private Vector3 _at; private Piece _piece; private int _phase; private float _next;
        private List<(string item, int amount)> _needs;
        private static readonly Dictionary<string, float> Held = new Dictionary<string, float>(); // plan id -> until (a builder that vanished lets go)
        private static class Taken
        {
            public static bool Contains(string id) => Held.TryGetValue(id, out float until) && until > Time.time;
            public static void Add(string id) => Held[id] = Time.time + 180;
        }
        public override void Stop(Brain b) { if (_id != null) Held.Remove(_id); base.Stop(b); }
        public override bool Interruptible => _phase == 0;
        // (far from home, on an expedition: the plans round a far site, built from the camp chest; no colony stores)
        private readonly Vector3? _site; private readonly float _reach; private readonly Container _camp;
        public BuildTask() { }
        public BuildTask(Vector3 site, float reach, Container camp) { _site = site; _reach = reach; _camp = camp; }
        // (a shipwright: only the ships at the boatyard, worked a long while, reached from the shore; builders leave ships alone)
        private readonly bool _ships; private Shipyard.Kind _kind;
        public static BuildTask Ships() => new BuildTask(true);
        private BuildTask(bool ships) { _ships = ships; }
        public string Short; // (far: what the camp chest lacked, for the jarl)
        public override void Start(Brain b)
        {
            Vector3 centre = _site ?? b.Colony.Position; float radius = _site.HasValue ? _reach : b.Colony.Radius;
            // The first plan no other builder is on (each fetches the whole cost: two on one plan would waste a trip).
            if (_ships) radius += 40f; // (a ship lies out on the water, maybe past the colony's edge)
            string[] next = Compat.NextGhosts(centre, radius, _ships ? 600 : 40).Where(g => !Taken.Contains(g.Split('|')[0]) && Shipyard.IsShip(g.Split('|')[1]) == _ships).Take(1).ToArray();
            if (next.Length == 0) { Done = true; b.NoBuildUntil = Time.time + 30; string[] why = Compat.GhostBlockers(centre, radius); if (why.Length > 0) b.Say(new[] { "I can't build the plans: ", "No plans can go up: ", "The plans won't go up: ", "I can't raise these plans: ", "Stuck on the plans: " }[UnityEngine.Random.Range(0, 5)] + why[0].Replace("|", " ").Trim(), 6); return; }
            string[] f = next[0].Split('|');
            _id = f[0]; _prefab = f[1]; Taken.Add(_id);
            _at = new Vector3(float.Parse(f[2], System.Globalization.CultureInfo.InvariantCulture), float.Parse(f[3], System.Globalization.CultureInfo.InvariantCulture), float.Parse(f[4], System.Globalization.CultureInfo.InvariantCulture));
            _piece = Assets.Find(_prefab)?.GetComponent<Piece>();
            if (_piece == null) { Done = true; b.NoBuildUntil = Time.time + 30; return; }
            _needs = _piece.m_resources.Where(r => r.m_resItem != null && !r.m_upgraderResource && r.m_amount > 0).Select(r => (r.m_resItem.name, r.m_amount)).ToList();
            Doing = "building " + Localization.instance.Localize(_piece.m_name).ToLower();
            if (_ships) { _kind = Shipyard.Of(_prefab); Doing = $"building a {_kind?.Name ?? "ship"} at the boatyard"; }
        }
        // (far: what the rest of the crew carries; walked to, and handed over within reach)
        private static IEnumerable<Brain> Crew(Brain b) => Expedition.CrewOf(Follow.LeaderId(b)).Where(m => m != b && Utils.DistanceXZ(m.Pos, b.Pos) < 40f);
        private static int Count(Brain m, string item) => m.Body.GetInventory().GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == item && !i.m_equipped).Sum(i => i.m_stack);
        private static int CrewHas(Brain b, string item) => Crew(b).Sum(m => Count(m, item));
        private bool FromCrew(Brain b, string item, int n)
        {
            Brain m = Crew(b).Where(x => Count(x, item) > 0).OrderBy(x => Utils.DistanceXZ(x.Pos, b.Pos)).FirstOrDefault();
            if (m == null) return false;
            if (Utils.DistanceXZ(m.Pos, b.Pos) > 2.5f) { if (!b.Move.Moving || Vector3.Distance(b.Move.Goal, m.Pos) > 2f) b.Move.To(m.Pos, 1.8f); Doing = $"getting {Localization.instance.Localize(Assets.Find(item)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name ?? item).ToLower()} from {m.S.Name}"; return true; }
            b.Move.Stop();
            foreach (ItemDrop.ItemData i in m.Body.GetInventory().GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == item && !i.m_equipped).ToList())
            {
                if (n <= 0) break;
                int k = Math.Min(n, i.m_stack); var copy = i.Clone(); copy.m_stack = k;
                if (!b.Body.GetInventory().AddItem(copy)) break;
                m.Body.GetInventory().RemoveItem(i, k); n -= k;
            }
            m.Dirty(); b.Dirty();
            return true;
        }
        private int Have(Brain b, string item) => b.Body.GetInventory().GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == item).Sum(i => i.m_stack);
        public override void Tick(Brain b)
        {
            if (Done) return;
            if (_phase == 0)
            {
                // Fetch what's missing from the stores; refuse the piece if the stores don't hold it all.
                var missing = _needs.Where(n => Have(b, n.item) < n.amount).ToList();
                if (missing.Count == 0) { _phase = 1; b.Move.To(_at, 2.5f); return; }
                var chests = _site.HasValue ? (_camp != null ? new List<(Container chest, string id, string category)> { (_camp, "", "camp") } : new List<(Container chest, string id, string category)>())
                    : Stock.ChestsFor(b).Where(x => x.category != "treasury" && !ContainerAccess.InUse(x.chest)).ToList();
                foreach (var (item, amount) in missing)
                    if (chests.Sum(x => x.chest.GetInventory().GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == item).Sum(i => i.m_stack)) + Have(b, item) + (_site.HasValue ? CrewHas(b, item) : 0) < amount)
                    {
                        Done = true; b.NoBuildUntil = Time.time + 60; string lacking = Localization.instance.Localize(Assets.Find(item)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name ?? item).ToLower();
                        if (_site.HasValue) { Short = $"{amount - Have(b, item)} {lacking}"; b.Say(new[] { $"The camp chest is short of {lacking} for this, jarl: bring some.", $"I need {Short} in the camp chest to go on.", $"Out of {lacking} up here. Fetch some, jarl?" }[UnityEngine.Random.Range(0, 3)], 7); return; }
                        if (_ships) { string ship = Shipyard.Of(_prefab)?.Name ?? "ship"; b.Say(new[] { $"The stores have too little {lacking} for the {ship}.", $"I can't go on with the {ship}: we're short of {lacking}.", $"No {lacking} in the stores for the {ship}." }[UnityEngine.Random.Range(0, 3)], 6); b.ShipShort = lacking; return; }
                        b.Say(new[] { $"The stores have too little {lacking} to build the plans.", $"The stores are short of {lacking} for these plans.", $"No {lacking} in the stores for the plans.", $"The plans need more {lacking} than the stores hold.", $"We're short of {lacking} for the plans." }[UnityEngine.Random.Range(0, 5)], 6); return; }
                var (want, count) = missing[0];
                Container from = chests.Select(x => x.chest).FirstOrDefault(ch => ch.GetInventory().GetAllItems().Any(i => i.m_dropPrefab != null && i.m_dropPrefab.name == want));
                if (from == null && _site.HasValue && FromCrew(b, want, count - Have(b, want))) return; // (far: a carrier's pack, handed over)
                if (from == null) { Done = true; b.NoBuildUntil = Time.time + 20; return; }
                Stock.Walk walk = Stock.WalkTo(b, from, _site.HasValue ? 3.5f : 2.4f);
                if (walk == Stock.Walk.Unreachable && _site.HasValue) { Done = true; b.NoBuildUntil = Time.time + 30; b.Say("I can't get to the camp chest.", 5); return; }
                if (walk != Stock.Walk.Reached) return; // (one out of reach is passed over: another is chosen next time round)
                ItemDrop.ItemData stack = from.GetInventory().GetAllItems().First(i => i.m_dropPrefab != null && i.m_dropPrefab.name == want);
                if (Stock.Take(from, b.Body.GetInventory(), stack, count - Have(b, want)) == 0) { Done = true; b.NoBuildUntil = Time.time + 30; return; }
                return;
            }
            // (near enough across the ground, and within reach in height: a roof or upper wall is built from below)
            // (straight under it, a ridge's end can be reached 10 m up, as a player's hammer reaches it)
            float near = _ships ? 9f : 3.5f; // (a ship's hull is worked from the shore beside it)
            if (_ships && b.Move.Failed && Utils.DistanceXZ(b.Pos, _at) < 16f) near = 16f;
            if (Utils.DistanceXZ(b.Pos, _at) > near || _at.y - b.Pos.y > (Utils.DistanceXZ(b.Pos, _at) < 3f ? 10f : 6f) || b.Pos.y - _at.y > 3f) { if (!b.Move.Moving) b.Move.To(_at, 2.5f); if (b.Move.Failed) { Done = true; b.NoBuildUntil = Time.time + 20; } return; }
            b.Move.Stop();
            Vector3 d = _at - b.Pos; d.y = 0; if (d.sqrMagnitude > 0.01f) b.Ai.LookTowards(d.normalized);
            if (Time.time < _next) return;
            _next = Time.time + 1.2f / Mathf.Max(0.4f, b.Productivity());
            ItemDrop.ItemData hammer = Gear.Tool(b, "hammer");
            if (hammer == null) { Done = true; b.NoBuildUntil = Time.time + 30; return; }
            if (!b.Body.IsItemEquiped(hammer)) b.Body.EquipItem(hammer, false);
            Body_.Anim(b.Body)?.SetTrigger("swing_hammer");
            if (++_phase < (_kind?.Blows ?? 3)) // a couple of blows, then it stands (a ship: many, over a good while)
            {
                if (_kind != null && _phase % 10 == 0) b.Say(new[] { "Another strake on.", "Ribs first, then planks.", "Tar, and more tar.", "She's taking shape.", "Mind the keel, it's the backbone of her." }[UnityEngine.Random.Range(0, 5)], 4);
                return;
            }
            if (_needs.Any(n => Have(b, n.item) < n.amount)) { Done = true; b.NoBuildUntil = Time.time + 20; return; }
            if (!Compat.PlaceGhost(_id)) { Done = true; b.NoBuildUntil = Time.time + 10; return; }
            if (_kind != null) Plugin.Guard(() => Shipyard.Launched(b, _kind, _at));
            Inventory bag = b.Body.GetInventory();
            foreach (var (item, amount) in _needs)
                for (int left = amount; left > 0;)
                {
                    ItemDrop.ItemData st = bag.GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == item);
                    if (st == null) break;
                    int n = Math.Min(left, st.m_stack); bag.RemoveItem(st, n); left -= n;
                }
            b.Body.RaiseSkill(Skills.SkillType.Crafting, 0.5f);
            b.S.Bump("built");
            Done = true;
        }
    }

    // A trade run: the colony's surplus valuables to the traders, coins back at the traders' own prices (never better), a day later.
    // The shipwright at a Shipyard slipway (World/Shipyard.cs): the stage under way's makings fetched from the stores, carried to the stocks
    // and worked in with the hammer a good while; every stage paid in full (the slipway takes no more than it needs), the next begun; the
    // boat launched when she's finished. One stage a task.
    internal sealed class SlipwayTask : Task
    {
        private Vector3 _at; private Shipyard.Kind _kind; private List<(string item, int amount)> _needs; private int _blows; private float _next;
        private bool _launch;
        public override bool Interruptible => _blows == 0;
        private static int Have(Brain b, string item) => b.Body.GetInventory().GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == item && !i.m_equipped).Sum(i => i.m_stack);
        public override void Start(Brain b)
        {
            var on = Shipyard.Building(b.Colony);
            if (!on.HasValue || on.Value.id != Shipyard.SlipwayId) { Done = true; b.NoBuildUntil = Time.time + 30; return; }
            _at = on.Value.at; _kind = Shipyard.Of(on.Value.prefab);
            _launch = Compat.SlipwayFinished(_at);
            _needs = Compat.SlipwayNeeds(_at).ToList();
            Doing = $"building a {_kind?.Name ?? "ship"} at the boatyard";
        }
        public override void Tick(Brain b)
        {
            if (Done) return;
            // fetch what the stage still needs (all of it before going to the stocks: a stage is paid in full)
            var missing = _launch ? new List<(string item, int amount)>() : _needs.Where(n => Have(b, n.item) < n.amount).ToList();
            if (missing.Count > 0 && _blows == 0)
            {
                var chests = Stock.ChestsFor(b).Where(x => x.category != "treasury" && !ContainerAccess.InUse(x.chest)).ToList();
                foreach (var (item, amount) in missing)
                    if (chests.Sum(x => x.chest.GetInventory().GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name == item).Sum(i => i.m_stack)) + Have(b, item) < amount)
                    {
                        Done = true; b.NoBuildUntil = Time.time + 60;
                        string lacking = Localization.instance.Localize(Assets.Find(item)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name ?? item).ToLower(), ship = _kind?.Name ?? "ship";
                        b.Say(new[] { $"The stores have too little {lacking} for the {ship}.", $"I can't go on with the {ship}: we're short of {lacking}.", $"No {lacking} in the stores for the {ship}." }[UnityEngine.Random.Range(0, 3)], 6);
                        b.ShipShort = lacking; return;
                    }
                var (want, count) = missing[0];
                Container from = chests.Select(x => x.chest).FirstOrDefault(ch => ch.GetInventory().GetAllItems().Any(i => i.m_dropPrefab != null && i.m_dropPrefab.name == want));
                if (from == null) { Done = true; b.NoBuildUntil = Time.time + 20; return; }
                Stock.Walk walk = Stock.WalkTo(b, from, 2.4f);
                if (walk != Stock.Walk.Reached) return;
                ItemDrop.ItemData stack = from.GetInventory().GetAllItems().First(i => i.m_dropPrefab != null && i.m_dropPrefab.name == want);
                if (Stock.Take(from, b.Body.GetInventory(), stack, count - Have(b, want)) == 0) { Done = true; b.NoBuildUntil = Time.time + 30; }
                return;
            }
            // to the stocks (worked from beside them: the hull stands high on them)
            if (Utils.DistanceXZ(b.Pos, _at) > 5f) { if (!b.Move.Moving) b.Move.To(_at, 3.5f); if (b.Move.Failed && Utils.DistanceXZ(b.Pos, _at) > 9f) { Done = true; b.NoBuildUntil = Time.time + 20; } return; }
            b.Move.Stop();
            Vector3 d = _at - b.Pos; d.y = 0; if (d.sqrMagnitude > 0.01f) b.Ai.LookTowards(d.normalized);
            if (Time.time < _next) return;
            _next = Time.time + 1.2f / Mathf.Max(0.4f, b.Productivity());
            ItemDrop.ItemData hammer = Gear.Tool(b, "hammer");
            if (hammer == null) { Done = true; b.NoBuildUntil = Time.time + 30; return; }
            if (!b.Body.IsItemEquiped(hammer)) b.Body.EquipItem(hammer, false);
            Body_.Anim(b.Body)?.SetTrigger("swing_hammer");
            if (_launch)
            {
                Done = true;
                if (Compat.SlipwayLaunch(_at) && _kind != null) Plugin.Guard(() => Shipyard.Launched(b, _kind, _at));
                else { b.NoBuildUntil = Time.time + 60; b.Say(new[] { "She's finished, but there's no water deep enough ahead of the stocks to launch her.", "No deep water clear of boats ahead of the slipway: she waits on the stocks." }[UnityEngine.Random.Range(0, 2)], 6); }
                return;
            }
            if (++_blows < (_kind?.Blows ?? 12))
            {
                if (_blows % 10 == 0) b.Say(new[] { "Another strake on.", "Ribs first, then planks.", "Tar, and more tar.", "She's taking shape.", "Mind the keel, it's the backbone of her." }[UnityEngine.Random.Range(0, 5)], 4);
                return;
            }
            // the stage's makings worked in: as much as the slipway takes of each, out of the bag
            Inventory bag = b.Body.GetInventory();
            int stage = Compat.SlipwayStage(_at);
            foreach (var (item, amount) in _needs)
            {
                int took = Compat.SlipwayPut(_at, item, Math.Min(amount, Have(b, item)));
                for (int left = took; left > 0;)
                {
                    ItemDrop.ItemData st = bag.GetAllItems().FirstOrDefault(i => i.m_dropPrefab != null && i.m_dropPrefab.name == item && !i.m_equipped);
                    if (st == null) break;
                    int n = Math.Min(left, st.m_stack); bag.RemoveItem(st, n); left -= n;
                }
            }
            b.Dirty();
            if (Compat.SlipwayStage(_at) > stage) { b.Body.RaiseSkill(Skills.SkillType.Crafting, 0.5f); b.S.Bump("built"); b.Say(new[] { "That's that stage done.", "On to the next part of her.", "Good work, that." }[UnityEngine.Random.Range(0, 3)], 4); }
            Done = true;
        }
    }

    internal sealed class TradeTask : Task
    {
        private Vector3 _edge; private bool _packed; private TradeWay _way; private readonly TradeWay? _test;
        private readonly List<Trade.Packed> _goods = new List<Trade.Packed>(); // (packed from the trade chests: sold whatever they are)
        public TradeTask(TradeWay? test = null) { _test = test; } // (a test: this way, whatever the rank)
        public override bool Interruptible => false;
        public override void Start(Brain b)
        {
            Colony c = b.Colony;
            var stores = Stock.Chests(c, b.Pos).Where(x => x.category != "treasury" && !ContainerAccess.InUse(x.chest)).ToList();
            var trade = Stock.TradeChests(c, b.Pos).Where(ch => !ContainerAccess.InUse(ch)).ToList();
            int lots = Trade.Lots(c);
            Component cart = Carts.CartNear(c), karve = Trade.Ship(c, false), longship = Trade.Ship(c, true);
            _way = _test ?? Trading.Choose(c.R.Rank, cart != null, karve != null, longship != null, lots);
            Doing = "packing for a trade run " + Trading.Names[(int)_way];
            Inventory bag = b.Body.GetInventory();
            int room = Trading.Lots(_way), packed = bag.GetAllItems().Count(Trade.Sellable);
            // (the trade chests first, whatever is in them; then the stores' treasure)
            foreach (Container ch in trade)
                foreach (ItemDrop.ItemData item in ch.GetInventory().GetAllItems().Where(Trade.Goods).ToList())
                {
                    if (bag.GetEmptySlots() <= 1 || packed >= room) break;
                    int price = Trade.Price(item), had = item.m_stack, moved = Stock.Take(ch, bag, item, item.m_stack);
                    if (moved <= 0) continue;
                    _goods.Add(new Trade.Packed { Name = item.m_shared.m_name, N = moved, Coins = price * moved / had }); packed++;
                }
            foreach (var x in stores)
                foreach (ItemDrop.ItemData item in x.chest.GetInventory().GetAllItems().Where(Trade.Sellable).ToList())
                    if (bag.GetEmptySlots() > 1 && packed < room && Stock.Take(x.chest, bag, item, item.m_stack) > 0) packed++;
            // (and with Homespun, the stores' bolts of cloth past a few of each kept for the weaver: vadmal, Core/Vadmal.cs)
            foreach (var (prefab, n) in Trade.BoltsForSale(c))
            {
                int left = n;
                foreach (var x in stores)
                    foreach (ItemDrop.ItemData item in x.chest.GetInventory().GetAllItems().Where(i => Classes.Prefab(i) == prefab).ToList())
                    {
                        if (left <= 0 || bag.GetEmptySlots() <= 1 || packed >= room) break;
                        int price = Trade.Price(item), had = item.m_stack, moved = Stock.Take(x.chest, bag, item, Math.Min(left, item.m_stack));
                        if (moved <= 0) continue;
                        _goods.Add(new Trade.Packed { Name = item.m_shared.m_name, N = moved, Coins = price * moved / had }); packed++; left -= moved;
                    }
            }
            if (!bag.GetAllItems().Any(Trade.Sellable) && _goods.Count == 0) { Done = true; b.NoTradeUntil = Time.time + 60; return; }
            // (by cart or ship: to it, and the rest is loaded there; on foot a little way down the road and gone: the world past a
            // player's own ground stands still, so a walk to the far edge could stall)
            Component by = _way == TradeWay.Cart ? cart : _way == TradeWay.Karve ? karve ?? longship : _way == TradeWay.Longship ? longship : null;
            Vector3 dir = (b.Pos - c.Position); dir.y = 0; if (dir.sqrMagnitude < 1) dir = Vector3.forward;
            _edge = by != null ? (Trading.AtSea(_way) ? Trade.Shore(by.transform.position, c.Position) : Colony.Spot(by.transform.position, 3f)) ?? b.Pos : c.Position + dir.normalized * Mathf.Min(c.Radius + 10, 35f);
            if (by == null) _edge = Colony.Spot(_edge, 4f) ?? _edge;
            b.Move.To(_edge, 3f);
            _packed = true;
        }
        public override void Tick(Brain b)
        {
            if (!_packed) { Done = true; return; }
            Doing = _way == TradeWay.Foot ? "off on a trade run" : Trading.AtSea(_way) ? "down to the ship for a trade run" : "off to the cart for a trade run";
            // (a fight or a flee stops the walk: walk on unless really there, or the way is lost)
            if (!b.Move.Failed && !b.Move.Arrived && b.Move.Distance > 6f) { if (!b.Move.Moving) b.Move.To(b.Move.Goal, 3f); return; }
            Trade.Leave(b, _way, _goods);
            Done = true;
        }
    }
    internal static class Trade
    {
        public static bool Sellable(ItemDrop.ItemData i) => i != null && i.m_shared.m_value > 0 && !i.m_equipped && !(i.m_dropPrefab != null && i.m_dropPrefab.name == "Coins") && !Items.IsRing(i);
        // In a trade chest: anything but coins and arm rings (given by the jarl, not sold).
        public static bool Goods(ItemDrop.ItemData i) => i != null && !(i.m_dropPrefab != null && i.m_dropPrefab.name == "Coins") && !Items.IsRing(i);
        public static int Price(ItemDrop.ItemData i) => Trading.GoodsPrice(i.m_dropPrefab != null ? i.m_dropPrefab.name : null, Stock.Category(i), i.m_shared.m_value, i.m_stack, i.m_quality);
        // Lots waiting to be sold: everything in the trade chests, and the stores' treasure.
        public static int Lots(Colony c) => Stock.TradeChests(c).Sum(ch => ch.GetInventory().GetAllItems().Count(Goods))
            + Stock.Chests(c).Where(x => x.category != "treasury").Sum(x => x.chest.GetInventory().GetAllItems().Count(Sellable)) + BoltsForSale(c).Count;
        // The stores' bolts of cloth for sale (Homespun's wool and linen, dyed or not), each kind past what's kept for the weaver's sewing.
        public static List<(string prefab, int n)> BoltsForSale(Colony c)
        {
            if (!Compat.HomespunOn) return new List<(string, int)>();
            return Stock.Chests(c).Where(x => x.category != "treasury").SelectMany(x => x.chest.GetInventory().GetAllItems()).Select(Classes.Prefab).Where(Vadmal.IsBolt).Distinct()
                .Select(p => (p, Vadmal.ForSale(Stock.Count(c, p)))).Where(x => x.Item2 > 0).ToList();
        }
        // A ship of the colony's lying at its shore, no one aboard: a karve, or a longship (the drakkar too, and Shipyard's knarr: a cargo ship).
        public static Ship Ship(Colony c, bool longship) => c.PiecesIn(c.Radius + 40).Select(p => p.GetComponent<Ship>()).FirstOrDefault(s => s != null && !s.HasPlayerOnboard()
            && (longship ? Utils.GetPrefabName(s.gameObject).StartsWith("VikingShip") || Utils.GetPrefabName(s.gameObject) == "Shipyard_Knarr" : Utils.GetPrefabName(s.gameObject) == "Karve"));
        // Where to go aboard a ship moored off the shore: the first dry ground walking back from it towards the colony (the ship may lie
        // well out; a spot beside it is water).
        public static Vector3? Shore(Vector3 ship, Vector3 home)
        {
            Vector3 dir = home - ship; dir.y = 0; float len = dir.magnitude;
            if (len < 1f) return Colony.Spot(ship, 8f);
            dir /= len;
            float sea = ZoneSystem.instance.m_waterLevel;
            for (float d = 2f; d < Mathf.Min(len, 80f); d += 1.5f)
            {
                Vector3 p = ship + dir * d;
                if (!ZoneSystem.instance.GetSolidHeight(p, out float h) || h < sea - 0.2f) continue; // (still water)
                p.y = h;
                if (Nav.Valid(out Vector3 v, p, 2.5f) && v.y > sea - 0.2f) return v;
            }
            return Colony.Spot(ship, 8f);
        }
        // For the Work tab: how a trader would go now, and what the next way needs.
        public static List<string> Plan(Colony c)
        {
            var lines = new List<string>();
            Rank rank = c.R.Rank;
            if (rank < Rank.Village) { lines.Add("Trade runs start at village rank: a trader takes amber, silver, gems and the like from the stores and comes back with coins."); return lines; }
            if (Stock.TradeChests(c).Count == 0) lines.Add("To sell the colony's own goods (wood, crops, hides, ore, metal, food, gear), mark a chest for trade goods (Shift+E on it with the staff) and fill it: a trader sells what's in it. From the stores they take only treasure" + (Compat.HomespunOn ? $", and bolts of cloth past {Vadmal.KeptForSewing} of each kind (a bolt of wool {Vadmal.WoolTenths / 10} coins, linen {Vadmal.LinenTenths / 10}, dyed half again)" : "") + ".");
            bool cart = Carts.CartNear(c) != null, karve = Ship(c, false) != null, longship = Ship(c, true) != null;
            int lots = Lots(c);
            TradeWay now = Trading.Choose(rank, cart, karve, longship, lots), most = Trading.Choose(rank, cart, karve, longship, int.MaxValue);
            string Way(TradeWay w) => $"{Trading.Names[(int)w]} ({Trading.Lots(w)} lots{(Trading.PricePercent(w) > 100 ? $", {Trading.PricePercent(w)}% of the price" : "")})";
            lines.Add($"{lots} lots for sale in the stores: a trader would go {Way(now)}." + (most != now ? $" With more to sell, {Way(most)}." : ""));
            if (!cart) lines.Add("Build a cart in the colony and they take it: " + Way(TradeWay.Cart) + ".");
            if (rank < Trading.MinRank(TradeWay.Karve)) lines.Add("At jarldom rank, a karve moored by the colony sails to better markets: " + Way(TradeWay.Karve) + ".");
            else if (!karve && !longship) lines.Add("Moor a karve by the colony (no one aboard) and they sail it: " + Way(TradeWay.Karve) + ".");
            if (rank >= Trading.MinRank(TradeWay.Karve) && rank < Trading.MinRank(TradeWay.Longship)) lines.Add("At hold rank, a longship or drakkar sails to the far markets: " + Way(TradeWay.Longship) + ".");
            else if (rank >= Trading.MinRank(TradeWay.Longship) && !longship) lines.Add("Moor a longship or drakkar by the colony and they sail to the far markets: " + Way(TradeWay.Longship) + ".");
            return lines;
        }
        // Goods packed from a trade chest: by name and how many (in the bag they may have joined a stack of the trader's own), and their price.
        public sealed class Packed { public string Name; public int N, Coins; }
        public static void Leave(Brain b, TradeWay way, List<Packed> packed = null)
        {
            Colony c = b.Colony;
            Inventory bag = b.Body.GetInventory();
            var goods = bag.GetAllItems().Where(Sellable).ToList();
            int lots = goods.Count, coins = goods.Sum(Price);
            foreach (var g in goods) bag.RemoveItem(g);
            foreach (Packed p in packed ?? new List<Packed>())
            {
                // (still in the bag, and never the trader's own worn gear of the same name)
                int left = p.N, gone = 0;
                foreach (ItemDrop.ItemData i in bag.GetAllItems().Where(i => i.m_shared.m_name == p.Name && !i.m_equipped).ToList())
                {
                    if (left <= 0) break;
                    int n = Math.Min(left, i.m_stack); bag.RemoveItem(i, n); left -= n; gone += n;
                }
                if (gone > 0) { coins += p.Coins * gone / p.N; lots++; }
            }
            // (a cart or a ship holds more than the trader's pack: the rest of the trade chests and the stores' treasure is loaded there)
            foreach (Container ch in Stock.TradeChests(c, b.Pos).Where(ch => !ContainerAccess.InUse(ch)))
                foreach (ItemDrop.ItemData i in ch.GetInventory().GetAllItems().Where(Goods).ToList())
                {
                    if (lots >= Trading.Lots(way)) break;
                    coins += Price(i); lots++;
                    ContainerAccess.TakeOver(ch); ch.GetInventory().RemoveItem(i);
                }
            foreach (var x in Stock.Chests(c, b.Pos).Where(x => x.category != "treasury" && !ContainerAccess.InUse(x.chest)))
                foreach (ItemDrop.ItemData i in x.chest.GetInventory().GetAllItems().Where(Sellable).ToList())
                {
                    if (lots >= Trading.Lots(way)) break;
                    coins += i.m_shared.m_value * i.m_stack; lots++;
                    ContainerAccess.TakeOver(x.chest); x.chest.GetInventory().RemoveItem(i);
                }
            bool temple = c.R.Rooms.Any(r => r.Ok && r.Type == RoomType.Temple && r.God == "njord");
            int njord = Mathf.RoundToInt(c.FavourOf("njord"));
            coins = coins * Trading.PricePercent(way) / 100;
            float njordBless = GreatWork.Blessing(c, "njord");
            if (njordBless > 0) coins = coins * (100 + Mathf.RoundToInt(10 * njordBless)) / 100; // (twice the boon with the Peak Temple raised to him)
            coins = Mathf.RoundToInt(coins * (1 + Festivals.Of(c.R.Flags, Festival.Sigrblot, Festivals.SigrTrade))); // (Sigrblot's blessing)
            bool lost = UnityEngine.Random.value < Trading.LossChance(way, njord, temple) * (Trading.AtSea(way) && GreatWork.Has(c, GreatWorks.Lighthouse) ? GreatWorks.LossAtSea : 1f); // (the Lighthouse brings them home)
            b.S.Bump("trades");
            string how = way == TradeWay.Foot ? "set out on foot to trade" : way == TradeWay.Cart ? "took the cart to the traders with" : way == TradeWay.Karve ? "sailed the karve to the markets down the coast with" : "sailed the longship to the far markets with";
            c.Log("text", b.S.Name, "", $"{b.S.Name} {how} {lots} lots worth {coins} coins.");
            Net.Act(c, "away", $"{b.S.Vid}|{(lost ? 0 : coins)}");
            b.Away(Trading.Encode(lost ? 0 : coins, way));
        }
    }

    // A raid: fighters to their posts, everyone else into the great hall (door shut) or back to the pillars.
    internal sealed class DefendTask : Task
    {
        private Vector3 _to; private bool _shelter; public bool Fighter;
        public override void Start(Brain b)
        {
            bool fighter = Fighter = Gear.CanFight(b) && (b.S.Prof == Prof.Guard || b.S.Prof == Prof.Hunter || b.S.Has("brave"));
            Room hall = b.Colony.Best(RoomType.GreatHall);
            if (fighter)
            {
                Zone post = b.Colony.R.Zones.Where(z => z.Type == ZoneType.GuardPost).OrderBy(z => Geometry.Distance(z.Xz, b.Pos.x, b.Pos.z)).FirstOrDefault();
                _to = post != null ? Zones.Centre(post) : b.Colony.RaidAt != Vector3.zero ? b.Colony.RaidAt : b.Colony.Position; // a guard post, else the stone under siege
                Doing = "to arms!";
            }
            else if (hall != null) { _to = b.Colony.Centre(hall); _shelter = true; Doing = "running for the hall"; }
            else { _to = b.Colony.Position; Doing = "falling back to the pillars"; }
            b.Move.To(Colony.Spot(_to, 2f) ?? _to, 2f, true);
        }
        public override void Tick(Brain b)
        {
            b.KeepDoorsOpen = false;
            if (!b.Move.Moving && Vector3.Distance(b.Pos, _to) > 5f) b.Move.To(Colony.Spot(_to, 2f) ?? _to, 2f, true);
            if (!b.Move.Moving) Doing = _shelter ? "hiding in the hall" : "holding";
        }
    }

    // At the Thing: to the hall, a bench or a place on the floor; at their turn, before the high seat to speak their matter, and
    // back to their place once the jarl has ruled.
    internal sealed class ThingTask : Task
    {
        public bool Arrived; private Chair _chair; private Vector3 _stand; private bool _speaking, _spoke; private float _faceAt, _since; private string _matter;
        public override void Start(Brain b) => Place(b);
        private void Place(Brain b)
        {
            Colony c = b.Colony; Room hall = Thing.Hall(c);
            _chair = hall != null ? Seats.Free(b, hall, c) : null;
            Vector3 at = Thing.Place(c);
            _stand = _chair != null ? _chair.transform.position : Colony.Spot(at, 4f) ?? at;
            if (_chair != null && _chair.m_attachPoint != null && Approaches.Inside(b, _chair.m_attachPoint.position) is Vector3 near) _stand = near; // (beside the bench, from inside the hall)
            Arrived = false; Doing = "off to the Thing"; _since = Time.time;
            b.Move.To(_stand, _chair != null ? 1.2f : 2f);
        }
        public override void Tick(Brain b)
        {
            Colony c = b.Colony;
            Petition cur = Thing.Current(c);
            if (_speaking && cur != null && cur.Key != _matter && cur.By == b.S.Vid && c.R.Flag(Thing.Spoken) == 0) { _spoke = false; _matter = cur.Key; } // (their next matter, straight after)
            if (!_speaking && cur != null && cur.By == b.S.Vid && c.R.Flag(Thing.Spoken) == 0)
            {
                _speaking = true; _spoke = false; _matter = cur.Key; Seats.Free(b); b.Stand(); _chair = null;
                _stand = Thing.Floor(c); b.Move.To(_stand, 1.2f); Doing = "coming forward at the Thing";
                return;
            }
            if (_speaking)
            {
                if (cur == null || cur.By != b.S.Vid) { _speaking = false; Place(b); return; } // (ruled on: back to their place)
                if (!_spoke)
                {
                    if (b.Move.Moving && !b.Move.Failed && Utils.DistanceXZ(b.Pos, _stand) > 1.5f) return;
                    Face(b, c);
                    b.Say(cur.Text, 12);
                    _spoke = true; Doing = "speaking at the Thing";
                    if (c.IsOwner) Thing.Speak(c, cur.Key); else Net.Act(c, "thingspoke", cur.Key);
                }
                if (Time.time > _faceAt) { _faceAt = Time.time + 2f; Face(b, c); }
                return;
            }
            if (!Arrived)
            {
                // (a seat they can't get to: a place on the floor instead)
                if (_chair != null && (b.Move.Failed || Time.time - _since > 20f)) { Seats.Free(b); _chair = null; Vector3 at = Thing.Place(c); _stand = Colony.Spot(at, 4f) ?? at; _since = Time.time; b.Move.To(_stand, 2f); return; }
                if (b.Move.Moving && !b.Move.Failed) return;
                if (!b.Move.Failed && !b.Move.Arrived && Utils.DistanceXZ(b.Pos, _stand) > 2.5f) { b.Move.To(_stand, _chair != null ? 1.2f : 2f); return; }
                Arrived = true; Doing = "at the Thing";
                if (_chair != null && _chair.m_attachPoint != null) b.Sit(_chair.m_attachPoint, string.IsNullOrEmpty(_chair.m_attachAnimation) ? "attach_chair" : _chair.m_attachAnimation, Vector3.zero);
            }
            if (_chair == null && Time.time > _faceAt) { _faceAt = Time.time + 3f; Face(b, c); }
            Mood.Satisfy(b.S, Need.Social, (float)(b.GameDt / Clock.Hour) * 10f);
        }
        // Toward the high seat, or the jarl where there's none.
        private static void Face(Brain b, Colony c)
        {
            Vector3? to = Thing.HighSeat(c)?.position ?? Player.GetAllPlayers().Where(p => p != null && c.Allows(p)).OrderBy(p => Utils.DistanceXZ(p.transform.position, b.Pos)).FirstOrDefault()?.transform.position;
            if (to == null) return;
            Vector3 d = to.Value - b.Pos; d.y = 0; if (d.sqrMagnitude > 0.01f) b.Ai.LookTowards(d.normalized);
        }
        public override void Stop(Brain b) { Seats.Free(b); base.Stop(b); }
    }

    // Getting next to a bed or a seat: the navmesh's nearest point to one packed against a wall (or between others, or by a fire) may
    // be outside the wall, and the walk goes out of the door and round, and back, and out. The floor nearest it inside its own room
    // that can be walked to instead (null: none found, go for the thing itself).
    internal static class Approaches
    {
        private static readonly List<Vector3> Path = new List<Vector3>();
        public static string Last = ""; // (test: why the last search came out as it did)
        // The floor just inside a room's door nearest a point (where one walks in), or null.
        public static Vector3? Entry(Colony c, Room room, Vector3 near)
        {
            foreach (Door d in c.PiecesIn<Door>(c.Radius + 20).Where(d => d != null && !Classes.IsShutter(d.gameObject) && Geometry.Distance(room.Xz, d.transform.position.x, d.transform.position.z) < 1.5f)
                .OrderBy(d => Vector3.Distance(d.transform.position, near)))
            {
                Vector3 foot = d.transform.position + Vector3.down * 0.9f;
                foreach (Vector3 side in new[] { foot + d.transform.forward * 1.5f, foot - d.transform.forward * 1.5f })
                    if (c.RoomAt(side + Vector3.up * 0.3f) == room && Nav.Valid(out Vector3 v, side, 1f)) return v;
            }
            return null;
        }
        // (the answer for each bed or seat, kept ten minutes: the same for everyone coming from in the room, or from outside it, and
        // finding it is a search of the floor round it and paths to each spot; the hall's seats were searched for each who sat)
        private static readonly Dictionary<(int, int, int, bool), (float at, Vector3? v)> Kept = new Dictionary<(int, int, int, bool), (float, Vector3?)>();
        public static Vector3? Inside(Brain b, Vector3 bed)
        {
            Last = "";
            Room room = b.Colony.RoomAt(bed + Vector3.up * 0.3f);
            if (room == null || Pathfinding.instance == null) { Last = "no room there"; return null; }
            var key = (Mathf.RoundToInt(bed.x * 2), Mathf.RoundToInt(bed.y * 2), Mathf.RoundToInt(bed.z * 2), b.Colony.RoomAt(b.Pos + Vector3.up * 0.3f) == room);
            if (Kept.TryGetValue(key, out var kept) && Time.time - kept.at < 600f) { Last = "kept"; return kept.v; }
            if (Kept.Count > 500) Kept.Clear();
            Vector3? found = Search(b, bed, room);
            Kept[key] = (Time.time, found);
            return found;
        }
        private static Vector3? Search(Brain b, Vector3 bed, Room room)
        {
            // (the plain way first: if it stays in the room and ends beside it, nothing more to find; a whole way, not the game's best
            // try, which for a bed on a raised bay ended on the bay's top, cut off, and walked them to the nearest rocks outside)
            if (Pathfinding.instance.GetPath(b.Pos, bed, Path, b.Ai.m_pathAgentType, requireFullPath: true) && Path.Count > 0 && Utils.DistanceXZ(Path[Path.Count - 1], bed) < 2f && b.Colony.RoomAt(Path[Path.Count - 1] + Vector3.up * 0.3f) == room
                && Path.All(q => b.Colony.RoomAt(q + Vector3.up * 0.3f) == room || Utils.DistanceXZ(q, b.Pos) < 1f || b.Colony.RoomAt(b.Pos + Vector3.up * 0.3f) != room && b.Colony.RoomAt(q + Vector3.up * 0.3f) == null)) { Last = "the plain way does"; return null; }
            var found = new List<(Vector3 v, float d)>();
            for (float dx = -4f; dx <= 4f; dx += 0.5f)
                for (float dz = -4f; dz <= 4f; dz += 0.5f)
                {
                    Vector3 p = bed + new Vector3(dx, 0, dz);
                    // (the floor: not a table top or a bench seat, which the navmesh covers too - nor a raised sleeping bay's top, which
                    // the way-in test below throws out; a bay-lined longhouse's measured floor was the bays', 0.56 m over the aisle)
                    if (!Nav.Valid(out Vector3 v, p, 0.35f) || Mathf.Abs(v.y - room.Floor) > 0.75f || b.Colony.RoomAt(v + Vector3.up * 0.3f) != room) continue;
                    found.Add((v, Utils.DistanceXZ(v, bed)));
                }
            // (the nearest that can be walked to: a pocket between a fire and the wall is floor too, but cut off)
            // (reachable from inside the room: from where they stand if they're in it, else from its middle - a shut door between
            // them and it would fail every way in, and the walk opens doors as it goes)
            Vector3 from = b.Colony.RoomAt(b.Pos + Vector3.up * 0.3f) == room ? b.Pos : Entry(b.Colony, room, b.Pos) ?? Colony.Spot(b.Colony.Centre(room), 3f) ?? b.Colony.Centre(room);
            foreach (var (v, _) in found.GroupBy(x => (Mathf.RoundToInt(x.v.x * 2), Mathf.RoundToInt(x.v.z * 2))).Select(g => g.First()).OrderBy(x => x.d).Take(40)) // (many sample points snap to one spot; the nearest may be pockets between benches)
                if (Pathfinding.instance.GetPath(from, v, Path, b.Ai.m_pathAgentType, requireFullPath: true)) { Last = $"{found.Count} floor points; from {from}"; return v; }
            Last = $"{found.Count} floor points, none reached from {from}";
            return null;
        }
    }

    // Chairs in use by villagers (the game only sees players on them).
    internal static class Seats
    {
        private static readonly Dictionary<Chair, Brain> Taken = new Dictionary<Chair, Brain>();
        public static Chair Free(Brain b, Room hall, Colony c)
        {
            Free(b);
            var list = Colony.PiecesNear(c.Centre(hall), 30);
            Chair best = list.SelectMany(Classes.SeatsOf).Where(ch => ch != null && !Classes.IsHighSeat(ch) && Geometry.Inside(hall.Xz, ch.m_attachPoint.position.x, ch.m_attachPoint.position.z) // (every seat of a bench; the high seat is the jarl's)
                && !(Taken.TryGetValue(ch, out Brain o) && o.Alive && o != b) && Player.GetClosestPlayer(ch.m_attachPoint != null ? ch.m_attachPoint.position : ch.transform.position, 0.3f) == null)
                .OrderBy(_ => UnityEngine.Random.value).FirstOrDefault();
            if (best != null) Taken[best] = b;
            return best;
        }
        public static bool IsFree(Chair ch) => !(Taken.TryGetValue(ch, out Brain o) && o.Alive) && Player.GetClosestPlayer(ch.m_attachPoint != null ? ch.m_attachPoint.position : ch.transform.position, 0.3f) == null;
        public static void Take(Brain b, Chair ch) { Free(b); Taken[ch] = b; }
        public static void Free(Brain b) { foreach (Chair ch in Taken.Where(kv => kv.Value == b || kv.Key == null).Select(kv => kv.Key).ToList()) Taken.Remove(ch); }
        public static void Clear() => Taken.Clear();
    }

    // Food: three meals at most, burning as a player's do; health follows them.
    internal static class Food
    {
        private static readonly Dictionary<Brain, (float at, bool can)> Asked = new Dictionary<Brain, (float, bool)>();
        public static bool CanEat(Brain b)
        {
            if (b.Meals.Count(m => m.Left > m.Total * 0.5f) >= 3) return false;
            if (Bag(b).Any()) return true;
            if (Asked.TryGetValue(b, out var hit) && Time.time - hit.at < 5f) return hit.can;
            bool can = b.Colony != null && b.Colony.Valid && Stock.Food(b.Colony, b.Pos).Any(x => Fresh(b, x.item)); // (far from home: only what they carry)
            if (Asked.Count > 200) Asked.Clear();
            Asked[b] = (Time.time, can);
            return can;
        }
        // A food that may be eaten now: as a player may, not while the same meal is still more than half there.
        public static bool Fresh(Brain b, ItemDrop.ItemData i) => !b.Meals.Any(m => m.Prefab == Classes.Prefab(i) && m.Left > m.Total * 0.5f);
        private static IEnumerable<ItemDrop.ItemData> Bag(Brain b) =>
            b.Body.GetInventory().GetAllItems().Where(i => Stock.Edible(i) && Fresh(b, i) && !(b.S.Prof == Prof.Weaver && Weaving.IsDye(i))); // (a weaver's berries are for the dye pot)
        public static bool EatFromBag(Brain b)
        {
            ItemDrop.ItemData item = Bag(b).OrderByDescending(i => i.m_shared.m_food).FirstOrDefault();
            if (item == null) return false;
            Eat(b, item);
            return true;
        }
        public static void Eat(Brain b, ItemDrop.ItemData item)
        {
            string prefab = item.m_dropPrefab != null ? item.m_dropPrefab.name : item.m_shared.m_name;
            b.Meals.RemoveAll(m => m.Prefab == prefab);
            if (b.Meals.Count >= 3) b.Meals.Remove(b.Meals.OrderBy(m => m.Left).First());
            b.Meals.Add(new Brain.Meal { Prefab = prefab, Health = item.m_shared.m_food, Stamina = item.m_shared.m_foodStamina, Left = item.m_shared.m_foodBurnTime, Total = item.m_shared.m_foodBurnTime });
            b.Body.GetInventory().RemoveOneItem(item);
            b.Body.m_consumeItemEffects.Create(b.Body.transform.position, Quaternion.identity);
            Body_.Anim(b.Body)?.SetTrigger("eat");
            Mood.Satisfy(b.S, Need.Food, (25 + item.m_shared.m_food) * Fare.Filling(Fare.Of(prefab, item.m_shared.m_food), Expect.Wanted(b.Colony))); // (plain fare fills a little less, in a colony of standing)
            if (item.m_shared.m_food >= 30) Mood.Add(b.S, "finemeal", b.Now);
            b.S.Foods.Add(prefab); if (b.S.Foods.Count > 9) b.S.Foods.RemoveAt(0);
            b.Body.SetMaxHealth(b.MaxHealth());
            b.Dirty();
        }
        public static void Burn(Brain b, float seconds)
        {
            foreach (Brain.Meal m in b.Meals) m.Left -= seconds * Game.m_foodRate;
            b.Meals.RemoveAll(m => m.Left <= 0);
        }
    }
}
