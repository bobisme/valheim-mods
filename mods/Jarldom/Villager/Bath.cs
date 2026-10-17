using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // Washing day (Laugardagr, Core/Calendar.cs): from mid-afternoon into the evening the villagers bathe in turns in the colony's
    // bathhouse, stripped down, on its sauna benches (the Bathhouse mod's) or in the game's hot tub; one throws water on the stones now
    // and then, and with a plunge tub by, they take the cold plunge after. Bathed, they're glad of it all week. A village of some size
    // with nowhere to bathe minds it, and says so at the Thing.
    internal static class Bath
    {
        public const double From = 14, Until = 20;
        public const float SitFor = 45f, PlungeFor = 6f, SteamEvery = 40f;
        public const string DayKey = "bathday", UnwashedKey = "unwasheday", InSauna = "in the sauna";
        public const string Bench = "Bathhouse_Bench", HotTub = "piece_bathtub", PlungeTub = "Bathhouse_PlungeTub";

        public static bool Wanted(Colony c) => c.R.Rank >= Rank.Hamlet && c.Population >= 6;
        public static Room Room(Colony c) => c.Best(RoomType.Bathhouse) is Room r && r.Ok ? r : null;
        public static bool Forced; // (test: "jarldom bathday on": washing day's hours now, whatever the day)
        public static bool Hours(Colony c) => Forced || Calendar.IsWashingDay(Year.WorldDay) && c.Hour >= From && c.Hour < Until;
        public static bool BathedToday(Brain b) => b.S.Counts.TryGetValue(DayKey, out int d) && d == Year.WorldDay;

        // Their turn: washing day's hours, not bathed yet today, and a seat free in the heat.
        public static bool Due(Brain b) => b.Colony != null && Hours(b.Colony) && !BathedToday(b) && Room(b.Colony) is Room r && Seat(b, r) != null;

        // The bathhouse's pieces of a kind (inside its outline).
        public static IEnumerable<Piece> Pieces(Colony c, Room room, params string[] prefabs) =>
            Colony.PiecesNear(c.Centre(room), 30).Where(p => p != null && prefabs.Contains(Utils.GetPrefabName(p.gameObject)) && Geometry.Inside(room.Xz, p.transform.position.x, p.transform.position.z));
        // A free seat in the heat: a sauna bench's, or the hot tub's.
        public static Chair Seat(Brain b, Room room) =>
            Pieces(b.Colony, room, Bench, HotTub).SelectMany(Classes.SeatsOf).Where(ch => ch != null && ch.m_attachPoint != null && Seats.IsFree(ch)).OrderBy(_ => Random.value).FirstOrDefault();
        // A plunge tub's free seat, in the bathhouse or just outside its door.
        public static Chair Plunge(Brain b, Room room) =>
            Colony.PiecesNear(b.Colony.Centre(room), 25).Where(p => p != null && Utils.GetPrefabName(p.gameObject) == PlungeTub)
                .SelectMany(Classes.SeatsOf).Where(ch => ch != null && ch.m_attachPoint != null && Seats.IsFree(ch)).OrderBy(ch => Vector3.Distance(ch.transform.position, b.Pos)).FirstOrDefault();

        // Water on the stones: one bather's job now and then, the colony over.
        private static readonly Dictionary<string, float> Steamed = new Dictionary<string, float>();
        public static bool SteamDue(Colony c) => !Steamed.TryGetValue(c.Id, out float t) || Time.time - t > SteamEvery;
        public static void SteamedNow(Colony c) => Steamed[c.Id] = Time.time;

        // The evening of washing day with nowhere to bathe: minded, once.
        public static void Evening(Brain b)
        {
            Colony c = b.Colony;
            if (c == null || !Calendar.IsWashingDay(Year.WorldDay) || c.Hour < Until - 2 || !Wanted(c) || Room(c) != null || BathedToday(b)) return;
            if (b.S.Counts.TryGetValue(UnwashedKey, out int d) && d == Year.WorldDay) return;
            b.S.Counts[UnwashedKey] = Year.WorldDay;
            Mood.Add(b.S, "unwashed", b.Now); b.Dirty();
        }

        // The Bathhouse mod's player counts its company: villagers in a sauna's heat (by what they're doing, as every game sees it).
        public static int Company(Vector3 heat) =>
            Villagers.Bodies().Count(h => Vector3.Distance(h.transform.position, heat) < 6f && Villagers.Tags(h.GetComponent<ZNetView>().GetZDO()).GetString(Villagers.DoingKey, "") == InSauna); // (the soul's tags, where they're kept)

        // Off with the armour (in the bag, put back on after): a sauna's sat in stripped down.
        public static void Strip(Brain b)
        {
            b.Stripped = true;
            Humanoid h = b.Body;
            foreach (ItemDrop.ItemData i in h.GetInventory().GetAllItems().Where(i => i.m_equipped).ToList())
                if (Worn(i)) h.UnequipItem(i, false);
            if (h.RightItem != null) h.UnequipItem(h.RightItem, false);
            if (h.LeftItem != null) h.UnequipItem(h.LeftItem, false);
        }
        public static bool Worn(ItemDrop.ItemData i)
        {
            var t = i.m_shared.m_itemType;
            return t == ItemDrop.ItemData.ItemType.Helmet || t == ItemDrop.ItemData.ItemType.Chest || t == ItemDrop.ItemData.ItemType.Legs || t == ItemDrop.ItemData.ItemType.Shoulder;
        }
    }

    internal sealed class BathTask : Task
    {
        private Chair _chair; private Vector3? _approach; private bool _there, _plunging, _sat; private float _satAt; private int _tries; private Room _room;
        public override void Start(Brain b)
        {
            _room = Bath.Room(b.Colony);
            _chair = _room != null ? Bath.Seat(b, _room) : null;
            if (_chair == null) { Done = true; return; }
            Seats.Take(b, _chair);
            Doing = "off to the bathhouse";
            Go(b);
        }
        private void Go(Brain b)
        {
            _approach = Approaches.Inside(b, _chair.m_attachPoint.position);
            b.Move.To(_approach ?? _chair.m_attachPoint.position, 1.2f);
        }
        public override void Tick(Brain b)
        {
            if (Done) return;
            if (!_there)
            {
                if (_chair == null || _chair.m_attachPoint == null) { Finish(b); return; }
                Vector3 seat = _chair.m_attachPoint.position;
                float d = Utils.DistanceXZ(b.Pos, seat);
                bool atApproach = _approach.HasValue && Utils.DistanceXZ(b.Pos, _approach.Value) < 1.3f;
                // (beside the seat is near enough: a bench's seats are inside its collider; a tub's are low in its water)
                bool beside = Mathf.Abs(b.Pos.y - seat.y) < 1.6f && (d < 2.2f || (b.Move.Failed || atApproach) && d < 4.5f);
                if (!beside)
                {
                    if (b.Move.Failed)
                    {
                        Chair other = ++_tries <= 2 ? (_plunging ? Bath.Plunge(b, _room) : Bath.Seat(b, _room)) : null;
                        if (other == null) { Finish(b); return; }
                        _chair = other; Seats.Take(b, _chair); Go(b); return;
                    }
                    if (b.Move.Moving) return;
                    if (!b.Move.Arrived || Utils.DistanceXZ(b.Pos, _approach ?? seat) > 2.5f) { Go(b); return; }
                }
                b.Move.Stop();
                _there = true; _satAt = Time.time;
                if (!_plunging) Bath.Strip(b);
                b.Sit(_chair.m_attachPoint, string.IsNullOrEmpty(_chair.m_attachAnimation) ? "attach_chair" : _chair.m_attachAnimation, Vector3.zero);
                Doing = _plunging ? "a cold plunge" : Bath.InSauna;
                return;
            }
            if (!_plunging)
            {
                Mood.Satisfy(b.S, Need.Social, (float)(b.GameDt / Clock.Hour) * 10f);
                // (water on the stones, a while in, if nobody has lately)
                if (!_sat && Time.time - _satAt > 8f && Bath.SteamDue(b.Colony))
                {
                    Bath.SteamedNow(b.Colony);
                    if (Compat.BathSteam(_chair.m_attachPoint.position)) b.Say(new[] { "Water on the stones!", "More steam!", "Hold on to your skin!", "Now it's a bath!" }[Random.Range(0, 4)], 4);
                }
                if (Time.time - _satAt < Bath.SitFor) return;
                // Bathed: the week's dirt gone. Then the plunge, if there's a tub; else dressed and out.
                b.Stand(); Seats.Free(b);
                b.S.Counts[Bath.DayKey] = Year.WorldDay; b.S.Bump("baths");
                Mood.Add(b.S, "bathed", b.Now); Mood.Remove(b.S, "unwashed"); b.Dirty();
                _sat = true;
                Chair tub = Bath.Plunge(b, _room);
                if (tub == null) { if (Random.value < 0.3f) b.Say(new[] { "That's the week's dirt gone.", "Clean as a new axe.", "Now I could sleep a week." }[Random.Range(0, 3)], 4); Finish(b); return; }
                _plunging = true; _there = false; _tries = 0; _chair = tub; Seats.Take(b, tub);
                Doing = "off to the plunge"; Go(b);
                return;
            }
            if (Time.time - _satAt < Bath.PlungeFor) return;
            Mood.Add(b.S, "plunge", b.Now); b.Dirty();
            b.Say(new[] { "Brr! That wakes you!", "Ooh, cold!", "Gods, that's cold!", "Ha! Like the fjord in spring!" }[Random.Range(0, 4)], 4);
            Finish(b);
        }
        private void Finish(Brain b) { b.Stand(); Seats.Free(b); b.Stripped = false; Done = true; }
        public override void Stop(Brain b) { Seats.Free(b); b.Stripped = false; base.Stop(b); }
    }
}
