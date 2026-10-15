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
        public int Renown => WealthPart + ComfortPart + (R?.Deeds ?? 0) + SagaPart;
        // (Odin the Wanderer: the jarls' firsts out in the world, Core/Wanderer.cs; summed once a second at most)
        private int _saga; private float _sagaAt = -10;
        public int SagaPart { get { if (R == null) return 0; if (Time.time - _sagaAt > 1f) { _sagaAt = Time.time; _saga = Wanderer.Saga(R.Flags); } return _saga; } }

        private Colony(HighSeat seat) { Seat = seat; View = seat.GetComponent<ZNetView>(); _attachedAt = Time.time; }
        private readonly float _attachedAt; private bool _caughtUp; private float _nextLive;

        // From HighSeat.Awake (and a reload): the record, or a new colony when the pillars were just raised.
        public static Colony Attach(HighSeat seat)
        {
            var c = new Colony(seat);
            if (c.View == null || !c.View.IsValid()) return null;
            byte[] data = c.Z.GetByteArray(RecordKey);
            try { c.R = ColonyRecord.Load(data); }
            catch (Exception e) { Plugin.Warn("Jarldom: a colony record could not be read and is kept as it is: " + e.Message); return null; }
            c._revision = c.Z.DataRevision;
            c.LoadSaga();
            if (c.R == null)
            {
                if (!c.IsOwner) return null; // the owner founds it; others see it once the record arrives (HighSeat retries)
                if (Relic.Take(c.Position, out byte[] record, out byte[] board))
                {
                    // Raised again with the heart of the high seat: the same colony, here.
                    c.R = ColonyRecord.Load(record);
                    if (board != null) c.Z.Set(BoardKey, board);
                    c.Z.Set(RecordKey, record);
                    c._revision = c.Z.DataRevision;
                    c.R.Flags[CatchUp.LiveKey] = (int)c.Now; c.R.LastDawn = c.Day; c.R.LastDusk = c.Hour >= Schedule.HallAt ? c.Day : c.Day - 1; // (no making up the time it lay in a bag)
                    c.Log("text", "", "", $"The high-seat pillars of {c.R.Name} stand again.");
                    c._dirty = true;
                }
                else c.Found();
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
        public void Detach()
        {
            if (R != null && ById.TryGetValue(R.Id, out Colony c) && c == this) ById.Remove(R.Id);
            if (R != null && IsOwner && _caughtUp) { R.Flags[CatchUp.LiveKey] = (int)Now; _dirty = true; } // (live until now: a reload or a walk away isn't time to make up from before)
            Flush();
        }
        public static void DetachAll() { foreach (Colony c in ById.Values.ToList()) c.Flush(); ById.Clear(); }
        public static void ForgetWorld() => ById.Clear();
        public static void AttachAll() => HighSeat.AttachAll();

        private void Found()
        {
            Piece piece = Seat.GetComponent<Piece>();
            long creator = piece != null ? piece.GetCreator() : 0;
            string founder = Player.GetAllPlayers().FirstOrDefault(p => p.GetPlayerID() == creator)?.GetPlayerName() ?? Player.m_localPlayer?.GetPlayerName() ?? "the jarl";
            R = new ColonyRecord { Id = NewId(), Founded = Now, Founder = creator, FounderName = founder };
            R.Name = DefaultName(founder, R.Id);
            if (creator == 0 && Player.m_localPlayer != null && Vector3.Distance(Player.m_localPlayer.transform.position, Position) < 30) R.FounderName = Player.m_localPlayer.GetPlayerName();
            R.LastDawn = Day; R.LastDusk = Hour >= 18 ? Day : Day - 1; R.LastPayday = Day;
            Log("found", founder, "", quiet: true);
            Tell($"{R.Name} is founded.\n<size=70%>Use the pillars (E) to hold council: the hall tab lists the first steps.</size>", true);
            _dirty = true; Flush();
            Plugin.Log($"Jarldom: {founder} founded colony {R.Id} at {Position}");
        }
        private void Founder()
        {
            long creator = Seat.GetComponent<Piece>()?.GetCreator() ?? 0;
            if (creator == 0) return;
            R.Founder = creator;
            string name = Player.GetAllPlayers().FirstOrDefault(p => p.GetPlayerID() == creator)?.GetPlayerName();
            if (!string.IsNullOrEmpty(name) && name != R.FounderName) { R.FounderName = name; if (R.Flag("named") == 0) R.Name = DefaultName(name, R.Id); } // (a name the jarl gave it stays)
            _dirty = true;
        }
        // A founder's first colony is their hall; the others (outposts) take the name of a place, so the two aren't confused.
        private static readonly string[] Places = { "Ravnsholt", "Eikvik", "Ulfsdal", "Hrafnsey", "Birkiness", "Steinvik", "Askrholm", "Fjallby", "Sigrsstead", "Vargtorp", "Myrkvik", "Eldhaug", "Sjøvangr", "Hjartaby", "Kaldbekk", "Gullnes", "Lindholm", "Ormsfjord", "Haukadal", "Tyrsvik" };
        private static string DefaultName(string founder, string id)
        {
            string me = founder.Replace(' ', '_').ToLowerInvariant();
            bool other = ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKeys().Any(l => l.StartsWith("jarldom_colony_") && !l.StartsWith("jarldom_colony_" + id) && l.EndsWith("," + me));
            return other ? Places[(id.GetStableHashCode() & 0x7fffffff) % Places.Length] : founder + "'s hall";
        }
        public static string NewId() => Guid.NewGuid().ToString("N").Substring(0, 12);

        // ---- saving ----
        public void MarkDirty() => _dirty = true;
        public int GuardExtra => R.Flag("guardraise") > 0 ? Payroll.GuardRaise : 0; // (the Thing raised the guards' pay)
        public bool SagaRead => _sagaRead;
        public void Flush()
        {
            if (!_dirty || !IsOwner || R == null) return;
            if (Versions.Mismatch) return; // (another game on another shape of Jarldom's data: nothing written until they match)
            _dirty = false;
            bool apart = SaveSaga();
            Z.Set(RecordKey, R.Save(withLog: !apart));
            _revision = Z.DataRevision;
        }

        // The saga on a still object of its own beside the pillars (found by the colony's id, as the board is): it changes with every
        // event, and in the record it would send the whole record with it each time. Every copy is merged, never replaced: the saga's
        // own, the one in memory, and any entries a record carried while the saga couldn't be found; so a game that becomes the owner
        // before the saga has reached it can't write a short saga over the long one.
        public const string SagaKey = "jarldom_saga", SagaLogKey = "jarldom_log", SagaZdoKey = "jarldom_sagazdo";
        private int _sagaSaved = -1; private uint _sagaRevision; private bool _sagaRead; private float _sagaSafeAt, _sagaMissSince;
        public ZDO SagaZ
        {
            get
            {
                if (ZDOMan.instance == null) return null;
                ZDOID id = Z.GetZDOID(SagaZdoKey);
                ZDO sz = id.IsNone() ? null : ZDOMan.instance.GetZDO(id);
                if (sz != null && sz.GetPrefab() == SoulStore.PrefabName.GetStableHashCode() && sz.GetString(SagaKey, "") == Id) return sz;
                if (id.IsNone()) return null;
                sz = SoulStore.Find(SagaKey, Id);
                if (sz != null && IsOwner) Z.Set(SagaZdoKey, sz.m_uid);
                return sz;
            }
        }
        // On the owner: the saga written apart (made the first time). False while it can't be: then the record carries the log as well
        // (and for a while after the saga is made, until it has surely reached the server).
        private bool SaveSaga()
        {
            ZDO sz = SagaZ;
            if (sz == null && Z.GetZDOID(SagaZdoKey).IsNone() && ZNetScene.instance?.GetPrefab(SoulStore.PrefabName) != null)
            {
                int hash = SoulStore.PrefabName.GetStableHashCode();
                sz = ZDOMan.instance.CreateNewZDO(Position + Vector3.down * 4f, hash);
                sz.Persistent = true; sz.Type = ZDO.ObjectType.Default; sz.Distant = false; sz.SetPrefab(hash);
                sz.Set(SagaKey, Id);
                Z.Set(SagaZdoKey, sz.m_uid);
                _sagaSaved = -1; _sagaRead = true; _sagaSafeAt = Time.time + 60f;
            }
            if (sz == null)
            {
                // (its saga gone for good: two minutes missing while the pillars stand loaded, a new one, from the log in memory, which
                // the record has carried meanwhile)
                if (_sagaMissSince == 0) _sagaMissSince = Time.time;
                else if (Time.time - _sagaMissSince > 120f) { Z.Set(SagaZdoKey, ZDOID.None); _sagaMissSince = 0; _dirty = true; }
                return false;
            }
            _sagaMissSince = 0;
            if (!_sagaRead && !LoadSaga()) return false; // (not written before what's there has been read in)
            if (R.LogChanges != _sagaSaved || sz.GetByteArray(SagaLogKey) == null)
            {
                if (!sz.IsOwner()) sz.SetOwner(ZDOMan.GetSessionID());
                sz.Set(SagaLogKey, R.SaveLog());
                _sagaSaved = R.LogChanges; _sagaRevision = sz.DataRevision;
            }
            return Time.time >= _sagaSafeAt;
        }
        // The saga read and merged into the log in memory: when it changes on the owner's game (others), or before an owner first writes.
        // False while it can't be found or read.
        private bool LoadSaga()
        {
            if (R == null) return false;
            ZDO sz = SagaZ;
            if (sz == null) return false;
            if (_sagaRead && sz.DataRevision == _sagaRevision) return true;
            byte[] bytes = sz.GetByteArray(SagaLogKey);
            if (bytes == null || bytes.Length == 0) { _sagaRead = true; _sagaRevision = sz.DataRevision; return true; } // (made, not yet written)
            try
            {
                var merged = ColonyRecord.MergeLog(ColonyRecord.LoadLog(bytes), R.Log);
                if (!merged.SequenceEqual(R.Log)) { R.Log = merged; R.LogChanges++; }
                _sagaRead = true; _sagaRevision = sz.DataRevision;
                return true;
            }
            catch (Exception e) { Plugin.Warn("Jarldom: a colony's saga could not be read (kept as it is): " + e.Message); return false; }
        }
        // Before the pillars come down: the whole saga in memory, for the heart to carry.
        // For tests: as if this game had become the owner before the saga reached it (only the last entries known, the saga unread).
        public void ForgetSagaForTest() { R.Log = R.Log.Skip(Math.Max(0, R.Log.Count - 2)).ToList(); R.LogChanges++; _sagaRead = false; _sagaRevision = 0; }
        public void GatherSaga() { _sagaRevision = 0; _sagaRead = false; LoadSaga(); }
        // Other games pick up the owner's changes; a new owner starts from the record as saved (and the saga, merged).
        private void Reload()
        {
            if (Z.DataRevision == _revision) return;
            _revision = Z.DataRevision;
            try
            {
                var fresh = ColonyRecord.Load(Z.GetByteArray(RecordKey));
                if (fresh != null) { fresh.Log = ColonyRecord.MergeLog(R?.Log, fresh.Log); fresh.LogChanges = (R?.LogChanges ?? 0) + 1; R = fresh; }
            }
            catch (Exception e) { Plugin.Warn("Jarldom: colony record unreadable: " + e.Message); }
            LoadBoard();
        }

        public void Log(string kind, string a, string b, string text = "", bool quiet = false)
        {
            if (R == null) return;
            if (!IsOwner) { Net.Act(this, "log", string.Join("\u001e", kind, a ?? "", b ?? "", text ?? "")); return; } // the owner keeps the saga
            R.Add(new LogEntry(Now, Day, kind, a, b, text));
            _dirty = true;
            string line = Stories.Line(R.Log[R.Log.Count - 1]);
            if (line != null && kind != "kill" && !quiet) Tell(line);
        }
        // The corner's news holds a line or two: a long entry is cut at a word, the rest left to the saga.
        public const int CornerChars = 120;
        public static string Short(string line)
        {
            if (line.Length <= CornerChars) return line;
            int cut = line.LastIndexOf(' ', CornerChars - 1);
            return line.Substring(0, cut > 60 ? cut : CornerChars).TrimEnd(',', ';', ':', ' ') + "… (the rest is in the saga)";
        }
        // News for the players in and near the colony.
        public void Tell(string text, bool big = false)
        {
            foreach (Player p in Player.GetAllPlayers())
                if (p != null && Utils.DistanceXZ(p.transform.position, Position) <= Radius + 60)
                    p.Message(big ? MessageHud.MessageType.Center : MessageHud.MessageType.TopLeft, big ? text : Short(text));
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
            if (!IsOwner) { Reload(); LoadSaga(); return; }
            if (!_sagaRead) LoadSaga(); // (an owner reads the saga in as soon as it can)
            if (Time.time >= _nextSave) { _nextSave = Time.time + 15; Flush(); } // (the record goes to every game when written: a few times a minute at most; players' actions write at once)
            if (Time.time >= _nextBoardShare) { _nextBoardShare = Time.time + 5; ShareBoard(); }
            if (Time.time < _nextTick) return;
            _nextTick = Time.time + 1;
            double now = Now; int day = Day; double hour = Hour;
            if (R.Founder == 0) Founder(); // the piece's creator is set just after it wakes
            Board.Expire(now); Board.Prune(now);
            if (Time.time >= _nextRoster) { _nextRoster = Time.time + 5; if (R.Flag(PayRetry) != 0) Plugin.Guard(() => Payday(R.Flag(PayRetry))); Roster(); CountDone(); Plugin.Guard(() => Captives.Owner(this)); Plugin.Guard(() => Warband.Owner(this)); Plugin.Guard(() => Carts.Unload(this)); }
            Rise(now);
            // Back after time unloaded: the missing hours made up first (once its people have loaded, or after a while if none do).
            if (!_caughtUp)
            {
                if (!CatchUp.Due(this, out double away)) _caughtUp = true;
                else if (Villagers.All.Any(b => b.S.Colony == R.Id) && Time.time - _attachedAt > 5 || Time.time - _attachedAt > 20) { _caughtUp = true; Plugin.Guard(() => CatchUp.Run(this, away)); }
                else return;
            }
            if (Time.time >= _nextLive || Now - R.Flag(CatchUp.LiveKey) > Clock.Hour) { _nextLive = Time.time + 60; CatchUp.Live(this); Plugin.Guard(() => Outposts.Announce(this)); } // (a minute, or an hour of the world's clock, whichever comes first)
            // (dawn and dusk wait until the colony has settled in after loading: its chests and people there, or payday finds no treasury
            // and nobody to pay)
            bool settled = Time.time - _attachedAt > 8 && (Villagers.All.Any(b => b.S.Colony == R.Id) || Time.time - _attachedAt > 30);
            if (!settled) return;
            // (a late dawn if the colony only loaded after the morning: its word, tribute and Thing aren't skipped for that)
            // (and the time jumped past a morning, or whole days: the dawn still comes, late, and paydays in the days skipped still fall due)
            if (hour >= Schedule.WakeAt && R.LastDawn != day)
            {
                int from = R.LastDawn >= 0 ? Mathf.Max(R.LastDawn + 1, day - 10) : day;
                for (int d = from; d < day; d++) { int skipped = d; if (Plugin.Instance.Payroll.Value && Payroll.IsPayday(skipped, R.LastPayday)) Plugin.Guard(() => Payday(skipped)); foreach (string god in R.Favour.Keys.ToList()) R.Favour[god] = Favour.Decay(R.Favour[god], 1); }
                R.LastDawn = day; _dirty = true; Plugin.Guard(() => Dawn(day));
            }
            if (hour >= Schedule.HallAt && R.LastDusk != day) { R.LastDusk = day; _dirty = true; Plugin.Guard(() => Dusk(day)); }
            if (Time.time >= _nextSurvey) { _nextSurvey = Time.time + 300; Plugin.Guard(Survey); Plugin.Guard(Rerank); }
            if (Time.time >= _nextRooms) { _nextRooms = Time.time + 20; Plugin.Guard(RoomsDue); }
            if (Time.time >= _nextScan && _scan == null) { _nextScan = Time.time + 8; Plugin.Guard(LeftAloneSync); _scan = Work.Steps(this).GetEnumerator(); }
            if (_scan != null) Plugin.Guard(ScanWork); // (a step a frame)
            Plugin.Guard(DefenseTick);
            Plugin.Guard(() => JarlDeeds.Tick(this));
            Plugin.Guard(() => Beacon.Tick(this));
            Plugin.Guard(() => TrueLegends.Tick(this));
            Plugin.Guard(() => GreatWork.Tick(this));
            Plugin.Guard(() => Thing.Tick(this));
        }
        // Jobs done today, for the colony window (the board's own count starts again each time it loads).
        private int _doneSeen = -1;
        private void CountDone()
        {
            int now = Board.CompletedCount;
            if (_doneSeen >= 0 && now > _doneSeen)
            {
                string key = "done:" + Day;
                foreach (string old in R.Flags.Keys.Where(k => k.StartsWith("done:") && k != key).ToList()) R.Flags.Remove(old);
                R.Flags[key] = R.Flag(key) + (now - _doneSeen); // (goes out with the next change worth sending)
            }
            _doneSeen = now;
        }
        public int DoneToday => R.Flag("done:" + Day);
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
                row.X = p.x; row.Y = p.y; row.Z = p.z; row.Seen = Now; // (noted, but no reason to send the record again: it goes with the next real change)
            }
        }
        public int Population => R.Living.Count();

        // ---- dawn and dusk ----
        // Fires and torches no one could reach (twice), kept in the record so they stay left alone through reloads and owner changes;
        // after a few days they're tried again (a way to them may have been built), or at once with "Try them again" in the Work tab.
        public const string AlonePrefix = "alone:"; public const int AloneDays = 3;
        // (kept by where they stand, "alone:x,y,z": the game's object ids are renumbered when a world loads)
        private void LeftAloneSync()
        {
            foreach (var (x, y, z) in Board.LeftAloneAt) { string k = AlonePrefix + string.Join(",", new[] { x, y, z }.Select(f => Mathf.Round(f).ToString(System.Globalization.CultureInfo.InvariantCulture))); if (!R.Flags.ContainsKey(k)) { R.Flags[k] = Day; _dirty = true; } }
            Board.LeftAloneAt.Clear();
            bool expired = false;
            foreach (var kv in R.Flags.Where(f => f.Key.StartsWith(AlonePrefix)).ToList())
                if (Day - kv.Value >= AloneDays) { R.Flags.Remove(kv.Key); _dirty = true; expired = true; }
            if (expired) Board.LeftAlone.Clear(); // (tried again: the board's own list was only this session's)
            _alone = null;
        }
        private List<Vector3> _alone;
        // Is the fire standing here one that's left alone?
        public bool LeftAloneAt(Vector3 p)
        {
            if (_alone == null) _alone = R.Flags.Keys.Where(k => k.StartsWith(AlonePrefix)).Select(k => k.Substring(AlonePrefix.Length).Split(',')).Where(v => v.Length == 3)
                .Select(v => new Vector3(Flt(v[0]), Flt(v[1]), Flt(v[2]))).ToList();
            return _alone.Any(q => Utils.DistanceXZ(q, p) < 1.2f && Mathf.Abs(q.y - p.y) < 1.5f);
        }
        public int LeftAloneCount => R.Flags.Keys.Count(k => k.StartsWith(AlonePrefix));
        private void TryReachAgain() { foreach (string k in R.Flags.Keys.Where(f => f.StartsWith(AlonePrefix)).ToList()) R.Flags.Remove(k); Board.LeftAlone.Clear(); _alone = null; _dirty = true; }
        private void Dawn(int day)
        {
            foreach (string god in R.Favour.Keys.ToList()) R.Favour[god] = Favour.Decay(R.Favour[god], 1);
            if (Plugin.Instance.Payroll.Value && Payroll.IsPayday(day, R.LastPayday)) Payday(day);
            Migration.Dawn(this, day);
            Thing.CheckPromises(this, day);
            Plugin.Guard(() => FestivalDays.Dawn(this, day));
            Tribute.Dawn(this, day);
            Saboteur.Dawn(this);
            Spirit.Dawn(this);
            Visitors.Dawn(this, day);
            Captives.Dawn(this);
            Warband.Dawn(this, day);
            Thing.Dawn(this, day); // (matters raised for the Thing, which the jarl calls)
            Plugin.Guard(() => MorningWord(day));
        }
        // Hugin's word at dawn to the jarl at home: yesterday's work, and the one thing that most needs them (the hall tab has the rest).
        private void MorningWord(int day)
        {
            if (Population == 0) return;
            int done = R.Flag("done:" + (day - 1));
            var needs = Ui.ColonyWindow.NeedsYou(this);
            string need = needs.Count == 0 ? "All is well." : (needs[0].Length > 110 ? needs[0].Substring(0, 107) + "..." : needs[0]) + (needs.Count > 1 ? $" (+{needs.Count - 1} more in the hall tab)" : "");
            Tell($"Hugin, at dawn: {(done > 0 ? done + " jobs were done yesterday at " + R.Name + "." : "Day " + day + " at " + R.Name + ".")} {need}");
        }
        private void Dusk(int day)
        {
            Plugin.Guard(AutoBeds); // (anyone sleeping by the fire takes a bed that stands free)
            Migration.Dusk(this, day);
            Feast.Dusk(this, day);
            Tribute.Dusk(this, day);
            Warband.Dusk(this, day);
            Plugin.Guard(() => Lost.Dusk(this, day));
            Plugin.Guard(() => FestivalDays.Dusk(this, day));
        }

        // (named, so a slow one is logged by its name)
        private IEnumerator<int> _scan;
        private void ScanWork()
        {
            IEnumerator<int> scan = _scan;
            _scan = null; // (a step that throws ends this scan: the next starts afresh)
            if (scan.MoveNext()) _scan = scan;
        }
        private void DefenseTick() => Defense.Tick(this);

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
            double away = Mining.Decode(why, out _, out double dig, out _) ? dig : why.StartsWith(Mining.Escort) && double.TryParse(why.Substring(Mining.Escort.Length), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double guarding) ? guarding : why.StartsWith(Carts.Prefix) && double.TryParse(why.Substring(Carts.Prefix.Length), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double trip) ? trip : Trading.Decode(why, out _, out TradeWay way) ? Clock.DayLength * Trading.AwayDays(way) : Clock.DayLength; // (a carter's round trip; a trader's day, longer by sea)
            R.Fallen.Add(new Fallen { Vid = s.Vid, State = s.Save(), Gear = bag ?? new byte[0], At = Now + away - Plugin.Instance.RespawnSeconds.Value, X = at.x, Y = at.y, Z = at.z, Killer = why });
            _dirty = true; Flush();
        }
        private static int Renown_Deed(string kind) => Jarldom.Renown.Deed(kind);

        private void Rise(double now)
        {
            if (R.Fallen.Count == 0) return;
            float wait = Plugin.Instance.RespawnSeconds.Value;
            foreach (Fallen f in R.Fallen.ToList())
            {
                if (now - f.At < wait || Captives.Is(f)) continue; // (a captive comes home only when freed)
                VillagerState s;
                try { s = VillagerState.Load(f.State); } catch { s = null; }
                if (s == null) { R.Fallen.Remove(f); continue; }
                if (string.IsNullOrEmpty(s.Bed) && now - f.At < wait * 3 && !f.Killer.StartsWith("trade:")) continue; // (no bed to rise at: slower, by the pillars)
                if (Villagers.Body(s.Vid) != null) { R.Fallen.Remove(f); continue; } // already up somewhere
                bool carter = f.Killer.StartsWith(Carts.Prefix);
                bool miner = f.Killer.StartsWith(Mining.Prefix) || f.Killer.StartsWith(Mining.Escort);
                bool trader = f.Killer.StartsWith("trade:") || carter || miner;
                if (RaidOn && !trader) continue; // (no one rises while a raid is on: up at their bed in the thick of it, they only fell again, and again, until Hel kept them)
                // (one back from the road comes walking in from the colony's edge, on the side they went; the dead rise at their bed)
                Vector3? at = trader ? Road(f) ?? Spot(Position + Seat.transform.forward * 3, 2.5f) : Survey_BedSpot(s.Bed) ?? (Vector3?)Spot(Position, 3);
                if (at == null) continue;
                if (!ZNetScene.instance.IsAreaReady(at.Value)) continue;
                if (!trader) Villagers.Rose(s, Now); // (the day's grace from Hel starts)
                Humanoid h = Villagers.Spawn(this, s, at.Value, f.Gear);
                if (h == null) continue;
                R.Fallen.Remove(f);
                if (!trader) Assets.Effect("vfx_spawn", at.Value);
                if (miner) Plugin.Guard(() => Claims.Home(this, h, s, f));
                else if (trader)
                {
                    TradeWay way = TradeWay.Foot;
                    int coins = !carter && Trading.Decode(f.Killer, out int n, out way) ? n : 0;
                    string from = carter ? "" : way == TradeWay.Foot ? "the traders" : way == TradeWay.Cart ? "the traders with the cart" : way == TradeWay.Karve ? "the markets down the coast" : "the far markets";
                    int earned = coins;
                    if (!carter) coins = Weaving.Shop(this, at.Value, s.Name, coins); // (flax seed, Hildir's clothes)
                    GameObject coin = ZNetScene.instance.GetPrefab("Coins");
                    for (int left = coins; left > 0 && coin != null;) { int stack = Math.Min(left, 999); if (!h.GetInventory().AddItem(coin, stack)) { ItemDrop.DropItem(coin.GetComponent<ItemDrop>().m_itemData.Clone(), stack, at.Value, Quaternion.identity); } left -= stack; }
                    if (carter) Log("text", s.Name, "", $"{s.Name} brought the cart back from the main colony.");
                    else Log("text", s.Name, "", earned > 0 ? $"{s.Name} came back from {from} with {coins} coins{(coins < earned ? $" (of {earned})" : "")}." : $"{s.Name} came back from the trade run empty-handed: the goods were lost {(way >= TradeWay.Karve ? "at sea" : "on the road")}.");
                }
                else Log("text", s.Name, "", $"{s.FullName} rose again{(string.IsNullOrEmpty(s.Bed) ? " by the pillars" : " at their bed")}.");
                _dirty = true;
            }
        }

        // Where one coming home from a trip appears: on the ground at the colony's edge, toward where they were (the claim, the main
        // colony, the coast), far enough out to be seen walking in, and loaded.
        private Vector3? Road(Fallen f)
        {
            Vector3 dir = Vector3.zero;
            if (Mining.Decode(f.Killer, out string claim, out _, out _) && Claims.Get(claim) is Claim cl) dir = Claims.At(cl) - Position;
            else if (f.Killer.StartsWith(Carts.Prefix) && Outposts.Where(Outposts.Parent(this)) is Vector3 main) dir = main - Position;
            else { dir = new Vector3(f.X, 0, f.Z) - Position; } // (where they set out from)
            dir.y = 0;
            if (dir.sqrMagnitude < 1f) dir = Seat.transform.forward;
            dir.Normalize();
            for (float far = Mathf.Clamp(Radius * 0.9f, 30f, 70f); far >= 20f; far -= 10f)
            {
                Vector3 p = Position + dir * far;
                if (!ZNetScene.instance.IsAreaReady(p)) continue;
                Vector3? spot = Spot(p, 5f);
                if (spot != null) return spot;
            }
            return null;
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
                // (and never in a fire: a hearth's spot was found to be in it)
                if (Nav.Valid(out Vector3 v, p, 2f) && v.y - ZoneSystem.instance.GetGroundHeight(v) < 1.2f && Mathf.Abs(v.y - near.y) < 4f && Steer.SafeSpot(v)) return v;
                if (ZoneSystem.instance.GetSolidHeight(p, out float h) && h - ground < 1.2f && Steer.SafeSpot(new Vector3(p.x, h, p.z))) return new Vector3(p.x, h + 0.2f, p.z);
            }
            return null;
        }

        // ---- money ----
        public const string PayRetry = "payretry";
        public void Payday(int day)
        {
            R.LastPayday = day;
            var souls = Villagers.All.Where(b => b.S.Colony == R.Id).ToList();
            int due = Payroll.Due(souls.Select(b => b.S), Payroll.EveryDays, GuardExtra) * Plugin.Instance.WageScale.Value / 100;
            if (due <= 0) return;
            Container treasury = Stock.Treasury(this);
            int coins = treasury != null ? Stock.Coins(treasury) : 0;
            // (the treasury chest open, or left stuck open: tried again every few seconds through the day, not a payday missed with the coins there)
            bool open = treasury != null && ContainerAccess.InUse(treasury);
            if (open && Payroll.CanPay(coins, due) && Day <= day) { if (R.Flag(PayRetry) == 0) Tell("Payday waits: the treasury chest is open."); R.Flags[PayRetry] = day; _dirty = true; return; }
            R.Flags.Remove(PayRetry);
            bool paid = treasury != null && Payroll.CanPay(coins, due) && Stock.TakeCoins(treasury, due) || Outposts.Credit(this, due); // (an outpost's wages fall to its main colony)
            if (paid)
            {
                foreach (Brain b in souls)
                {
                    int wage = (Tables.Wage(b.S.Prof, b.S.Has("greedy")) + (b.S.Prof == Prof.Guard ? GuardExtra : 0)) * Payroll.EveryDays * Plugin.Instance.WageScale.Value / 100;
                    b.S.Hoard += Payroll.Hoarded(wage); b.S.Unpaid = 0; Mood.Remove(b.S, "unpaid"); b.Dirty();
                }
                R.MissedPaydays = 0;
                Log("payday", "", due.ToString());
            }
            else
            {
                foreach (Brain b in souls) { Mood.Add(b.S, "unpaid", Now); b.S.Unpaid++; if (b.S.Unpaid >= 3 && string.IsNullOrEmpty(b.S.Byname)) { b.S.Byname = Names.Byname("unpaid"); } b.Dirty(); }
                R.MissedPaydays++;
                Log("unpaid", "", due.ToString(), $"Payday: {due} coins were due and the treasury {(treasury == null ? "isn't chosen" : open ? "chest was open all day (someone had it open, or it was stuck)" : "holds " + coins)}. Nobody was paid.");
            }
            _dirty = true;
        }

        // What's owed for the paydays missed: a payday's wages for each (as they are now), paid at once from the treasury, or not at all.
        public int Owed() => R.MissedPaydays <= 0 ? 0 : R.MissedPaydays * (IsOwner ? Payroll.Due(Villagers.All.Where(b => b.S.Colony == R.Id).Select(b => b.S), Payroll.EveryDays, GuardExtra)
            : R.Living.Sum(p => (Tables.Wage(p.Prof, false) + (p.Prof == Prof.Guard ? GuardExtra : 0)) * Payroll.EveryDays)) * Plugin.Instance.WageScale.Value / 100; // (another game runs the people: from the roster)
        public void BackPay(string who)
        {
            int owed = Owed();
            if (owed <= 0) return;
            Container treasury = Stock.Treasury(this);
            int coins = treasury != null ? Stock.Coins(treasury) : 0;
            if (treasury == null || coins < owed || !Stock.TakeCoins(treasury, owed)) { Tell($"{owed} coins are owed and the treasury holds {coins}."); return; }
            foreach (Brain b in Villagers.All.Where(x => x.S.Colony == R.Id))
            {
                int wage = (Tables.Wage(b.S.Prof, b.S.Has("greedy")) + (b.S.Prof == Prof.Guard ? GuardExtra : 0)) * Payroll.EveryDays * Plugin.Instance.WageScale.Value / 100 * R.MissedPaydays;
                b.S.Hoard += Payroll.Hoarded(wage); b.S.Unpaid = 0; Mood.Remove(b.S, "unpaid"); b.Dirty();
            }
            Log("payday", who ?? "", owed.ToString(), $"What was owed ({owed} coins, {R.MissedPaydays} payday{(R.MissedPaydays == 1 ? "" : "s")}) was paid.");
            R.MissedPaydays = 0; _dirty = true;
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
            if (now > was) { Log("rank", "", Tables.Of(now).Name); string rk = Tables.Of(now).Name.ToLower(); Tell($"The pillars of {R.Name} are raised anew.\n<size=70%>{("aeiou".IndexOf(rk[0]) >= 0 ? "An" : "A")} {rk} now: {Tables.Of(now).Unlocks}.</size>", true); }
            else Log("text", "", "", $"The colony fell back to a {Tables.Of(now).Name.ToLower()}.");
        }
        public int Cap => Math.Min(Plugin.Instance.MaxVillagers.Value, Ranks.Cap(R.Rank, Beds));
        public bool Allows(Player p) => p != null && (R.Founder == 0 || p.GetPlayerID() == R.Founder || R.Allowed.Contains(p.GetPlayerName()) || ZNet.instance.IsServer() && p == Player.m_localPlayer || R.Allowed.Contains("*"));
        public int FavourOf(string god) => R.Favour.TryGetValue(god, out int f) ? f : 0;
        public bool HelSaves() => R.Rooms.Any(r => r.Ok && r.Type == RoomType.Temple && r.God == "hel" && Tables.GradeRank(r.Grade) >= Tables.GradeRank('B')) && UnityEngine.Random.value < 0.5f;
    }
}
