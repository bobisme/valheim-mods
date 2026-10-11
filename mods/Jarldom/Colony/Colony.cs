using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // A colony as this game sees it: the record on the High-Seat Pillars' ZDO, and on the pillars' owner (the colony owner) everything that
    // runs it: the clock (dawn, dusk, paydays), migration, rising souls, the job board, renown and rank. Other games read the record.
    internal sealed partial class Colony
    {
        public const string RecordKey = "jarldom_colony", BoardKey = "jarldom_board", IdKey = "jarldom_id";
        private static readonly Dictionary<string, Colony> ById = new Dictionary<string, Colony>();
        public static IEnumerable<Colony> All => ById.Values.Where(c => c.Valid);
        public static Colony Of(string id) => id != null && ById.TryGetValue(id, out Colony c) && c.Valid ? c : null;
        public static Colony At(Vector3 p, float extra = 0) => All.Where(c => Utils.DistanceXZ(c.Position, p) <= c.Radius + extra).OrderBy(c => Utils.DistanceXZ(c.Position, p)).FirstOrDefault();
        public static Colony Nearest(Vector3 p, float max) => All.Where(c => Utils.DistanceXZ(c.Position, p) <= max).OrderBy(c => Utils.DistanceXZ(c.Position, p)).FirstOrDefault();

        public readonly ZNetView View;
        public readonly HighSeat Seat;
        public ColonyRecord R;
        public readonly JobBoard Board = new JobBoard();
        public string Id => R?.Id ?? "";
        public bool Valid => View != null && View.IsValid() && R != null;
        public bool IsOwner => View != null && View.IsValid() && View.IsOwner();
        public Vector3 Position => View.transform.position;
        public float Radius => Jarldom.Renown.Radius(R?.Rank ?? Rank.Camp) + (Plugin.Instance?.RadiusBonus.Value ?? 0);
        public ZDO Z => View.GetZDO();
        private uint _revision; private bool _dirty; private float _nextSave, _nextScan, _nextSurvey, _nextRoster, _nextBoardShare, _nextRooms;
        public int WealthPart, ComfortPart, PieceValue, Beds;
        // A raid and the horn: kept on the pillars' ZDO, so villagers run by every game hear them.
        public const string RaidKey = "jarldom_raid", HornKey = "jarldom_horn";
        public Vector3 RaidAt;
        public bool RaidOn { get => Z.GetBool(RaidKey); set { if (IsOwner && Z.GetBool(RaidKey) != value) Z.Set(RaidKey, value); } }
        public float HornUntil
        {
            get { double until = Z.GetFloat(HornKey, 0f); return until <= 0 ? 0 : Time.time + (float)(until - Now); }
            set { if (IsOwner) Z.Set(HornKey, value <= 0 ? 0f : (float)(Now + (value - Time.time))); }
        }
        public double Now => ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : 0;
        public int Day => Clock.Day(Now);
        public double Hour => Clock.HourOfDay(Now);
        public int Renown => WealthPart + ComfortPart + (R?.Deeds ?? 0);

        private Colony(HighSeat seat) { Seat = seat; View = seat.GetComponent<ZNetView>(); }

        // From HighSeat.Awake (and a reload): the record, or a new colony when the pillars were just raised.
        public static Colony Attach(HighSeat seat)
        {
            var c = new Colony(seat);
            if (c.View == null || !c.View.IsValid()) return null;
            byte[] data = c.Z.GetByteArray(RecordKey);
            try { c.R = ColonyRecord.Load(data); }
            catch (Exception e) { Plugin.Warn("Jarldom: a colony record could not be read and is kept as it is: " + e.Message); return null; }
            c._revision = c.Z.DataRevision;
            if (c.R == null)
            {
                if (!c.IsOwner) return null; // the owner founds it; others see it once the record arrives (HighSeat retries)
                c.Found();
            }
            if (ById.TryGetValue(c.R.Id, out Colony old) && old != null && old.Valid && old.View != c.View)
            {
                Plugin.Warn($"Jarldom: two pillars claim colony {c.R.Id}; the second is ignored");
                return null;
            }
            ById[c.R.Id] = c;
            c.LoadBoard();
            return c;
        }
        public void Detach() { if (R != null && ById.TryGetValue(R.Id, out Colony c) && c == this) ById.Remove(R.Id); Flush(); }
        public static void DetachAll() { foreach (Colony c in ById.Values.ToList()) c.Flush(); ById.Clear(); }
        public static void ForgetWorld() => ById.Clear();
        public static void AttachAll() => HighSeat.AttachAll();

        private void Found()
        {
            Piece piece = Seat.GetComponent<Piece>();
            long creator = piece != null ? piece.GetCreator() : 0;
            string founder = Player.GetAllPlayers().FirstOrDefault(p => p.GetPlayerID() == creator)?.GetPlayerName() ?? Player.m_localPlayer?.GetPlayerName() ?? "the jarl";
            R = new ColonyRecord { Id = NewId(), Founded = Now, Founder = creator, FounderName = founder, Name = founder + "'s hall" };
            if (creator == 0 && Player.m_localPlayer != null && Vector3.Distance(Player.m_localPlayer.transform.position, Position) < 30) R.FounderName = Player.m_localPlayer.GetPlayerName();
            R.LastDawn = Day; R.LastDusk = Hour >= 18 ? Day : Day - 1; R.LastPayday = Day;
            Log("found", founder, "");
            _dirty = true; Flush();
            Plugin.Log($"Jarldom: {founder} founded colony {R.Id} at {Position}");
        }
        private void Founder()
        {
            long creator = Seat.GetComponent<Piece>()?.GetCreator() ?? 0;
            if (creator == 0) return;
            R.Founder = creator;
            string name = Player.GetAllPlayers().FirstOrDefault(p => p.GetPlayerID() == creator)?.GetPlayerName();
            if (!string.IsNullOrEmpty(name) && name != R.FounderName) { R.FounderName = name; R.Name = name + "'s hall"; }
            _dirty = true;
        }
        public static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 12);

        // ---- saving ----
        public void MarkDirty() => _dirty = true;
        public void Flush()
        {
            if (!_dirty || !IsOwner || R == null) return;
            _dirty = false;
            Z.Set(RecordKey, R.Save());
            _revision = Z.DataRevision;
        }
        // Other games pick up the owner's changes; a new owner starts from the record as saved.
        private void Reload()
        {
            if (Z.DataRevision == _revision) return;
            _revision = Z.DataRevision;
            try { var fresh = ColonyRecord.Load(Z.GetByteArray(RecordKey)); if (fresh != null) R = fresh; }
            catch (Exception e) { Plugin.Warn("Jarldom: colony record unreadable: " + e.Message); }
            LoadBoard();
        }

        public void Log(string kind, string a, string b, string text = "")
        {
            if (R == null) return;
            if (!IsOwner) { Net.Act(this, "log", string.Join("\u001e", kind, a ?? "", b ?? "", text ?? "")); return; } // the owner keeps the saga
            R.Add(new LogEntry(Now, Day, kind, a, b, text));
            _dirty = true;
            string line = Stories.Line(R.Log[R.Log.Count - 1]);
            if (line != null && kind != "kill") Tell(line);
        }
        // News for the players in and near the colony.
        public void Tell(string text, bool big = false)
        {
            foreach (Player p in Player.GetAllPlayers())
                if (p != null && Utils.DistanceXZ(p.transform.position, Position) <= Radius + 60)
                    p.Message(big ? MessageHud.MessageType.Center : MessageHud.MessageType.TopLeft, text);
        }

        // ---- the tick ----
        public static void TickAll()
        {
            if (ZNet.instance == null) return;
            Clock.DayLength = EnvMan.instance != null ? EnvMan.instance.m_dayLengthSec : 1200;
            foreach (Colony c in All.ToList()) Plugin.Guard(c.Tick);
        }
        private float _nextTick;
        private void Tick()
        {
            if (!IsOwner) { Reload(); return; }
            if (Time.time >= _nextSave) { _nextSave = Time.time + 2; Flush(); }
            if (Time.time >= _nextBoardShare) { _nextBoardShare = Time.time + 3; ShareBoard(); }
            if (Time.time < _nextTick) return;
            _nextTick = Time.time + 1;
            double now = Now; int day = Day; double hour = Hour;
            if (R.Founder == 0) Founder(); // the piece's creator is set just after it wakes
            Board.Expire(now); Board.Prune(now);
            if (Time.time >= _nextRoster) { _nextRoster = Time.time + 5; Roster(); }
            Rise(now);
            if (hour >= Schedule.WakeAt && hour < 12 && R.LastDawn != day) { R.LastDawn = day; _dirty = true; Plugin.Guard(() => Dawn(day)); }
            if (hour >= Schedule.HallAt && R.LastDusk != day) { R.LastDusk = day; _dirty = true; Plugin.Guard(() => Dusk(day)); }
            if (Time.time >= _nextSurvey) { _nextSurvey = Time.time + 300; Plugin.Guard(Survey); Plugin.Guard(Rerank); }
            if (Time.time >= _nextRooms) { _nextRooms = Time.time + 20; Plugin.Guard(RoomsDue); }
            if (Time.time >= _nextScan) { _nextScan = Time.time + 8; Plugin.Guard(() => Work.Scan(this)); }
            Plugin.Guard(() => Defense.Tick(this));
        }
        public void SurveySoon() => _nextSurvey = Mathf.Min(_nextSurvey, Time.time + 2);
        public void RoomsSoon() => _nextRooms = Mathf.Min(_nextRooms, Time.time + 2);

        // The roster's mirror rows follow the bodies loaded here.
        private void Roster()
        {
            foreach (Brain b in Villagers.All.Where(b => b.S.Colony == R.Id))
            {
                RosterRow row = R.Row(b.S.Vid);
                if (row == null) { row = new RosterRow { Vid = b.S.Vid }; R.Roster.Add(row); }
                Vector3 p = b.Body.transform.position;
                if (row.Name != b.S.FullName || row.Prof != b.S.Prof || row.Bed != b.S.Bed || Math.Abs(row.Worth - b.S.Worth) > 0.5f || row.Hoard != b.S.Hoard) _dirty = true;
                row.Name = b.S.FullName; row.Female = b.S.Female; row.Prof = b.S.Prof; row.Worth = b.S.Worth; row.Hoard = b.S.Hoard; row.Bed = b.S.Bed;
                if (row.State != SoulState.Active) row.State = SoulState.Active;
                if (Vector3.Distance(new Vector3(row.X, row.Y, row.Z), p) > 4f || Now - row.Seen > 120) { row.X = p.x; row.Y = p.y; row.Z = p.z; row.Seen = Now; _dirty = true; }
            }
        }
        public int Population => R.Living.Count();

        // ---- dawn and dusk ----
        private void Dawn(int day)
        {
            foreach (string god in R.Favour.Keys.ToList()) R.Favour[god] = Favour.Decay(R.Favour[god], 1);
            if (Plugin.Instance.Payroll.Value && Payroll.IsPayday(day, R.LastPayday)) Payday(day);
            Migration.Dawn(this, day);
            if (day - R.LastThing >= 5 && Population >= 3) Thing.Call(this, day);
        }
        private void Dusk(int day)
        {
            Migration.Dusk(this, day);
            Feast.Dusk(this, day);
        }

        // ---- souls falling and rising ----
        public void SoulFell(VillagerState s, byte[] gear, Vector3 at, string killer, bool lost)
        {
            if (!IsOwner) { Net.SendFallen(this, s, gear, at, killer, lost); return; }
            RosterRow row = R.Row(s.Vid);
            foreach (Brain other in Villagers.All.Where(b => b.S.Colony == R.Id && b.S.Bonds.TryGetValue(s.Vid, out int o) && o > 40)) Mood.Add(other.S, lost ? "grief" : "fell", Now);
            if (lost)
            {
                if (row != null) row.State = SoulState.Lost;
                R.Deeds += Renown_Deed("lost");
                Log("lost", s.FullName, killer);
                Draugr.Raise(this, s, at);
                _dirty = true;
                return;
            }
            Mood.Add(s, "fell", Now);
            if (s.Deaths >= 3 && string.IsNullOrEmpty(s.Byname)) { s.Byname = Names.Byname("unlucky"); Log("byname", s.Name, s.Byname); }
            R.Fallen.RemoveAll(f => f.Vid == s.Vid);
            R.Fallen.Add(new Fallen { Vid = s.Vid, State = s.Save(), Gear = gear ?? new byte[0], At = Now, X = at.x, Y = at.y, Z = at.z, Killer = killer ?? "" });
            Log("death", s.FullName, killer);
            _dirty = true; Flush();
        }
        // Away for a day (a trade run): kept with the fallen, but rising at the pillars when the day is up, with what they earned.
        public void SoulAway(VillagerState s, byte[] bag, Vector3 at, string why)
        {
            if (!IsOwner) { Net.SendFallen(this, s, bag, at, why, false); return; }
            R.Fallen.RemoveAll(f => f.Vid == s.Vid);
            R.Fallen.Add(new Fallen { Vid = s.Vid, State = s.Save(), Gear = bag ?? new byte[0], At = Now + Clock.DayLength - Plugin.Instance.RespawnSeconds.Value, X = at.x, Y = at.y, Z = at.z, Killer = why });
            _dirty = true; Flush();
        }
        private static int Renown_Deed(string kind) => Jarldom.Renown.Deed(kind);

        private void Rise(double now)
        {
            if (R.Fallen.Count == 0) return;
            float wait = Plugin.Instance.RespawnSeconds.Value;
            foreach (Fallen f in R.Fallen.ToList())
            {
                if (now - f.At < wait) continue;
                VillagerState s;
                try { s = VillagerState.Load(f.State); } catch { s = null; }
                if (s == null) { R.Fallen.Remove(f); continue; }
                if (Villagers.Body(s.Vid) != null) { R.Fallen.Remove(f); continue; } // already up somewhere
                bool trader = f.Killer.StartsWith("trade:");
                Vector3? at = trader ? Spot(Position + Seat.transform.forward * 3, 2.5f) : Survey_BedSpot(s.Bed) ?? (Vector3?)Spot(Position, 3);
                if (at == null) continue;
                if (!ZNetScene.instance.IsAreaReady(at.Value)) continue;
                Humanoid h = Villagers.Spawn(this, s, at.Value, f.Gear);
                if (h == null) continue;
                R.Fallen.Remove(f);
                Assets.Effect("vfx_spawn", at.Value);
                if (trader)
                {
                    int coins = int.TryParse(f.Killer.Substring(6), out int n) ? n : 0;
                    GameObject coin = ZNetScene.instance.GetPrefab("Coins");
                    for (int left = coins; left > 0 && coin != null;) { int stack = Math.Min(left, 999); if (!h.GetInventory().AddItem(coin, stack)) { ItemDrop.DropItem(coin.GetComponent<ItemDrop>().m_itemData.Clone(), stack, at.Value, Quaternion.identity); } left -= stack; }
                    Log("text", s.Name, "", coins > 0 ? $"{s.Name} came back from the traders with {coins} coins." : $"{s.Name} came back from the trade run empty-handed: the goods were lost on the way.");
                }
                else Log("text", s.Name, "", $"{s.FullName} rose again{(string.IsNullOrEmpty(s.Bed) ? " by the pillars" : " at their bed")}.");
                _dirty = true;
            }
        }

        // A free spot on the ground near a point (navmesh first, then the solid height).
        public static Vector3? Spot(Vector3 near, float radius)
        {
            for (int i = 0; i < 12; i++)
            {
                Vector2 r = UnityEngine.Random.insideUnitCircle * radius;
                Vector3 p = near + new Vector3(r.x, 0, r.y);
                // On the ground or a floor, never on top of a piece (the navmesh covers the tops of pillars and walls too).
                float ground = ZoneSystem.instance.GetGroundHeight(p);
                if (Nav.Valid(out Vector3 v, p, 2f) && v.y - ZoneSystem.instance.GetGroundHeight(v) < 1.2f && Mathf.Abs(v.y - near.y) < 4f) return v;
                if (ZoneSystem.instance.GetSolidHeight(p, out float h) && h - ground < 1.2f) return new Vector3(p.x, h + 0.2f, p.z);
            }
            return null;
        }

        // ---- money ----
        public void Payday(int day)
        {
            R.LastPayday = day;
            var souls = Villagers.All.Where(b => b.S.Colony == R.Id).ToList();
            int due = Payroll.Due(souls.Select(b => b.S)) * Plugin.Instance.WageScale.Value / 100;
            if (due <= 0) return;
            Container treasury = Stock.Treasury(this);
            int coins = treasury != null ? Stock.Coins(treasury) : 0;
            if (treasury != null && Payroll.CanPay(coins, due) && Stock.TakeCoins(treasury, due))
            {
                foreach (Brain b in souls)
                {
                    int wage = Tables.Wage(b.S.Prof, b.S.Has("greedy")) * Payroll.EveryDays * Plugin.Instance.WageScale.Value / 100;
                    b.S.Hoard += Payroll.Hoarded(wage); b.S.Unpaid = 0; Mood.Remove(b.S, "unpaid"); b.Dirty();
                }
                R.MissedPaydays = 0;
                Log("payday", "", due.ToString());
            }
            else
            {
                foreach (Brain b in souls) { Mood.Add(b.S, "unpaid", Now); b.S.Unpaid++; if (b.S.Unpaid >= 3 && string.IsNullOrEmpty(b.S.Byname)) { b.S.Byname = Names.Byname("unpaid"); } b.Dirty(); }
                R.MissedPaydays++;
                Log("unpaid", "", due.ToString(), $"Payday: {due} coins were due and the treasury {(treasury == null ? "isn't chosen" : "holds " + coins)}. Nobody was paid.");
            }
            _dirty = true;
        }

        // ---- rank ----
        public IEnumerable<(RoomType type, char grade)> WorkingRooms() => R.Rooms.Where(r => r.Ok).Select(r => (r.Type, r.Grade));
        public void Rerank()
        {
            Rank was = R.Rank;
            Rank now = Ranks.Evaluate(Population, WorkingRooms());
            if (now == was) return;
            R.Rank = now;
            _dirty = true;
            if (now > was) Log("rank", "", Tables.Of(now).Name);
            else Log("text", "", "", $"The colony fell back to a {Tables.Of(now).Name.ToLower()}.");
        }
        public int Cap => Math.Min(Plugin.Instance.MaxVillagers.Value, Ranks.Cap(R.Rank, Beds));
        public bool Allows(Player p) => p != null && (R.Founder == 0 || p.GetPlayerID() == R.Founder || R.Allowed.Contains(p.GetPlayerName()) || ZNet.instance.IsServer() && p == Player.m_localPlayer || R.Allowed.Contains("*"));
        public int FavourOf(string god) => R.Favour.TryGetValue(god, out int f) ? f : 0;
        public bool HelSaves() => R.Rooms.Any(r => r.Ok && r.Type == RoomType.Temple && r.God == "hel" && Tables.GradeRank(r.Grade) >= Tables.GradeRank('B')) && UnityEngine.Random.value < 0.5f;
    }
}
