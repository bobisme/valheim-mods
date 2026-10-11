using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Jarldom
{
    // What a colony and a villager are, as saved: plain classes written to bytes with a version byte in front.
    // The colony lives on the High-Seat Pillars' ZDO, each villager on its own ZDO (with a mirror row in the colony's roster).
    // Never a ZDOID in here: chests, beds and rooms are named by our own random ids (stored on their ZDOs as jarldom_id).

    public enum SoulState { Active, Lost, Ascended, Deserted, Dismissed }

    public sealed class RosterRow
    {
        public string Vid, Name; public bool Female; public Prof Prof; public SoulState State; public float Worth; public int Hoard;
        public float X, Y, Z; public double Seen; public string Bed;
    }

    public sealed class Room
    {
        public string Id; public RoomType Type; public List<float> Xz = new List<float>(); public float Floor, Height = 4;
        public bool Ok; public char Grade = 'D'; public int Points; public string Failures = ""; public string God = ""; public string OpenEdges = "";
        public double Checked;
        public int Corners => Xz.Count / 2;
        public (float x, float z) Corner(int i) => (Xz[i * 2], Xz[i * 2 + 1]);
    }

    public sealed class Zone
    {
        public string Id; public ZoneType Type; public List<float> Xz = new List<float>(); public float Floor;
        public int Corners => Xz.Count / 2;
        public (float x, float z) Corner(int i) => (Xz[i * 2], Xz[i * 2 + 1]);
    }

    public sealed class LogEntry
    {
        public double Time; public int Day; public string Kind, A = "", B = "", Text = "";
        public LogEntry() { }
        public LogEntry(double time, int day, string kind, string a, string b, string text) { Time = time; Day = day; Kind = kind; A = a ?? ""; B = b ?? ""; Text = text ?? ""; }
    }

    public sealed class Newcomer
    {
        public string Name; public bool Female; public string Traits; public Prof Prof; public string Story; public int Seed; public string Group;
    }

    // A villager between death and rising again at their bed: everything needed to make them again lives here, in the world.
    public sealed class Fallen
    {
        public string Vid; public byte[] State = new byte[0], Gear = new byte[0]; public double At; public float X, Y, Z; public string Killer = "";
    }

    public sealed class ColonyRecord
    {
        public const byte Version = 1; // (any change to what's saved here: raise Plugin.DataVersion too, so mixed games don't write over each other)
        public const int LogSize = 240;
        public string Id = ""; public string Name = ""; public double Founded; public long Founder; public string FounderName = "";
        public Rank Rank; public int Deeds; public int Arrivals; public int Refused;
        public string Treasury = ""; public Dictionary<string, string> Stockpiles = new Dictionary<string, string>();
        public Dictionary<string, int> Favour = new Dictionary<string, int>();
        public List<RosterRow> Roster = new List<RosterRow>();
        public List<Room> Rooms = new List<Room>();
        public List<Zone> Zones = new List<Zone>();
        public List<LogEntry> Log = new List<LogEntry>();
        public List<Newcomer> Coming = new List<Newcomer>();   // announced at dawn, arriving at dusk
        public List<Fallen> Fallen = new List<Fallen>();       // waiting to rise
        public int ComingDay = -1; public bool ComingLanded;     // landed: waiting at the pillars for the jarl's answer
        public int LastDawn = -1, LastDusk = -1, LastFeast = -100, LastPayday = -1, MissedPaydays, LastThing = -1, BogPaid, GuaranteedArrival;
        public List<string> Allowed = new List<string>();        // players (by name) the founder lets run the colony
        public Dictionary<string, int> Flags = new Dictionary<string, int>(); // small counters and switches
        public List<Petition> Petitions = new List<Petition>(); // raised day by day, waiting for the Thing

        public int LogChanges; // (not saved: counts this session's additions, so a change is never missed)
        public void Add(LogEntry e)
        {
            Log.Add(e); LogChanges++;
            if (Log.Count > LogSize) Log.RemoveRange(0, Log.Count - LogSize);
        }
        // Two copies of a saga made one: every entry of either, once, in time order, the newest LogSize. Never loses an entry to a
        // shorter copy (an owner who hadn't read the saga yet, a record carrying only the latest entries).
        public static List<LogEntry> MergeLog(IEnumerable<LogEntry> a, IEnumerable<LogEntry> b)
        {
            var seen = new HashSet<string>(); var all = new List<LogEntry>();
            foreach (LogEntry e in (a ?? Enumerable.Empty<LogEntry>()).Concat(b ?? Enumerable.Empty<LogEntry>()))
                if (seen.Add(e.Time.ToString("R") + "\u001f" + e.Kind + "\u001f" + e.A + "\u001f" + e.B + "\u001f" + e.Text)) all.Add(e);
            var sorted = all.Select((e, i) => (e, i)).OrderBy(x => x.e.Time).ThenBy(x => x.i).Select(x => x.e).ToList();
            return sorted.Count > LogSize ? sorted.GetRange(sorted.Count - LogSize, LogSize) : sorted;
        }
        public RosterRow Row(string vid) => Roster.FirstOrDefault(r => r.Vid == vid);
        public IEnumerable<RosterRow> Living => Roster.Where(r => r.State == SoulState.Active);
        public int Flag(string key) => Flags.TryGetValue(key, out int v) ? v : 0;

        // The record; without its saga when the saga is kept apart (it changes with every event, the record by the minute).
        public byte[] Save(bool withLog = true)
        {
            var w = new Writer();
            w.Byte(Version);
            w.Str(Id); w.Str(Name); w.Dbl(Founded); w.Long(Founder); w.Str(FounderName);
            w.Int((int)Rank); w.Int(Deeds); w.Int(Arrivals); w.Int(Refused);
            w.Str(Treasury);
            w.Int(Stockpiles.Count); foreach (var kv in Stockpiles) { w.Str(kv.Key); w.Str(kv.Value); }
            w.Int(Favour.Count); foreach (var kv in Favour) { w.Str(kv.Key); w.Int(kv.Value); }
            w.Int(Roster.Count);
            foreach (var r in Roster)
            {
                w.Str(r.Vid); w.Str(r.Name); w.Bool(r.Female); w.Int((int)r.Prof); w.Int((int)r.State); w.Flt(r.Worth); w.Int(r.Hoard);
                w.Flt(r.X); w.Flt(r.Y); w.Flt(r.Z); w.Dbl(r.Seen); w.Str(r.Bed);
            }
            w.Int(Rooms.Count);
            foreach (var r in Rooms)
            {
                w.Str(r.Id); w.Int((int)r.Type); w.Floats(r.Xz); w.Flt(r.Floor); w.Flt(r.Height); w.Bool(r.Ok); w.Byte((byte)r.Grade); w.Int(r.Points);
                w.Str(r.Failures); w.Str(r.God); w.Dbl(r.Checked); w.Str(r.OpenEdges);
            }
            w.Int(Zones.Count);
            foreach (var z in Zones) { w.Str(z.Id); w.Int((int)z.Type); w.Floats(z.Xz); w.Flt(z.Floor); }
            WriteLog(w, withLog ? Log : new List<LogEntry>());
            w.Int(Coming.Count);
            foreach (var n in Coming) { w.Str(n.Name); w.Bool(n.Female); w.Str(n.Traits); w.Int((int)n.Prof); w.Str(n.Story); w.Int(n.Seed); w.Str(n.Group); }
            w.Int(ComingDay); w.Bool(ComingLanded);
            w.Int(LastDawn); w.Int(LastDusk); w.Int(LastFeast); w.Int(LastPayday); w.Int(MissedPaydays); w.Int(LastThing); w.Int(BogPaid); w.Int(GuaranteedArrival);
            w.Int(Allowed.Count); foreach (var a in Allowed) w.Str(a);
            w.Int(Flags.Count); foreach (var kv in Flags) { w.Str(kv.Key); w.Int(kv.Value); }
            w.Int(Fallen.Count);
            foreach (var f in Fallen) { w.Str(f.Vid); w.Blob(f.State); w.Blob(f.Gear); w.Dbl(f.At); w.Flt(f.X); w.Flt(f.Y); w.Flt(f.Z); w.Str(f.Killer); }
            w.Int(Petitions.Count);
            foreach (var p in Petitions) { w.Str(p.Id); w.Str(p.About); w.Str(p.Text); w.Str(p.Yes); w.Str(p.No); w.Int(p.YesMood); w.Int(p.NoMood); w.Int(p.YesFavour); w.Str(p.YesDeed); w.Str(p.By); w.Str(p.ByName); w.Int(p.Day); }
            return w.Bytes();
        }

        private static void WriteLog(Writer w, List<LogEntry> log)
        {
            w.Int(log.Count);
            foreach (var e in log) { w.Dbl(e.Time); w.Int(e.Day); w.Str(e.Kind); w.Str(e.A); w.Str(e.B); w.Str(e.Text); }
        }
        // The saga on its own.
        public byte[] SaveLog() { var w = new Writer(); w.Byte(Version); WriteLog(w, Log); return w.Bytes(); }
        public static List<LogEntry> LoadLog(byte[] data)
        {
            var list = new List<LogEntry>();
            if (data == null || data.Length == 0) return list;
            var r = new Reader(data);
            byte version = r.Byte();
            if (version < 1 || version > Version) throw new InvalidDataException($"colony saga version {version} is newer than this mod ({Version})");
            for (int i = r.Int(); i > 0; i--) list.Add(new LogEntry(r.Dbl(), r.Int(), r.Str(), r.Str(), r.Str(), r.Str()));
            return list;
        }
        public static ColonyRecord Load(byte[] data)
        {
            if (data == null || data.Length == 0) return null;
            var r = new Reader(data);
            byte version = r.Byte();
            if (version < 1 || version > Version) throw new InvalidDataException($"colony record version {version} is newer than this mod ({Version})");
            var c = new ColonyRecord();
            c.Id = r.Str(); c.Name = r.Str(); c.Founded = r.Dbl(); c.Founder = r.Long(); c.FounderName = r.Str();
            c.Rank = (Rank)r.Int(); c.Deeds = r.Int(); c.Arrivals = r.Int(); c.Refused = r.Int();
            c.Treasury = r.Str();
            for (int i = r.Int(); i > 0; i--) c.Stockpiles[r.Str()] = r.Str();
            for (int i = r.Int(); i > 0; i--) c.Favour[r.Str()] = r.Int();
            for (int i = r.Int(); i > 0; i--)
                c.Roster.Add(new RosterRow
                {
                    Vid = r.Str(), Name = r.Str(), Female = r.Bool(), Prof = (Prof)r.Int(), State = (SoulState)r.Int(), Worth = r.Flt(), Hoard = r.Int(),
                    X = r.Flt(), Y = r.Flt(), Z = r.Flt(), Seen = r.Dbl(), Bed = r.Str(),
                });
            for (int i = r.Int(); i > 0; i--)
                c.Rooms.Add(new Room
                {
                    Id = r.Str(), Type = (RoomType)r.Int(), Xz = r.Floats(), Floor = r.Flt(), Height = r.Flt(), Ok = r.Bool(), Grade = (char)r.Byte(), Points = r.Int(),
                    Failures = r.Str(), God = r.Str(), Checked = r.Dbl(), OpenEdges = r.Str(),
                });
            for (int i = r.Int(); i > 0; i--) c.Zones.Add(new Zone { Id = r.Str(), Type = (ZoneType)r.Int(), Xz = r.Floats(), Floor = r.Flt() });
            for (int i = r.Int(); i > 0; i--) c.Log.Add(new LogEntry { Time = r.Dbl(), Day = r.Int(), Kind = r.Str(), A = r.Str(), B = r.Str(), Text = r.Str() });
            for (int i = r.Int(); i > 0; i--)
                c.Coming.Add(new Newcomer { Name = r.Str(), Female = r.Bool(), Traits = r.Str(), Prof = (Prof)r.Int(), Story = r.Str(), Seed = r.Int(), Group = r.Str() });
            c.ComingDay = r.Int(); c.ComingLanded = r.Bool();
            c.LastDawn = r.Int(); c.LastDusk = r.Int(); c.LastFeast = r.Int(); c.LastPayday = r.Int(); c.MissedPaydays = r.Int(); c.LastThing = r.Int(); c.BogPaid = r.Int(); c.GuaranteedArrival = r.Int();
            for (int i = r.Int(); i > 0; i--) c.Allowed.Add(r.Str());
            for (int i = r.Int(); i > 0; i--) c.Flags[r.Str()] = r.Int();
            for (int i = r.Int(); i > 0; i--)
                c.Fallen.Add(new Fallen { Vid = r.Str(), State = r.Blob(), Gear = r.Blob(), At = r.Dbl(), X = r.Flt(), Y = r.Flt(), Z = r.Flt(), Killer = r.Str() });
            if (r.More)
                for (int i = r.Int(); i > 0; i--)
                    c.Petitions.Add(new Petition { Id = r.Str(), About = r.Str(), Text = r.Str(), Yes = r.Str(), No = r.Str(), YesMood = r.Int(), NoMood = r.Int(), YesFavour = r.Int(), YesDeed = r.Str(), By = r.Str(), ByName = r.Str(), Day = r.Int() });
            return c;
        }
    }

    public sealed class Moodlet
    {
        public string Id; public double Until; public int Stacks = 1; // Until 0: held while its condition holds
    }

    public sealed class VillagerState
    {
        public const byte Version = 1;
        public string Vid = "", Colony = "", Name = "", Byname = "", Story = "", Group = ""; public bool Female;
        public List<string> Traits = new List<string>(); public Prof Prof; public Prof Wants;
        public float[] Needs = { 70, 70, 70, 60, 70, 60, 50 };
        public List<Moodlet> Moodlets = new List<Moodlet>();
        public Dictionary<string, float> Skills = new Dictionary<string, float>();
        public float Worth; public int Hoard; public int Deaths; public bool Disgraced, Worthy; public double DisgraceSince;
        public Dictionary<string, int> Bonds = new Dictionary<string, int>();       // vid → opinion -100..100
        public Dictionary<string, int> Opinions = new Dictionary<string, int>();    // player name → opinion -100..100
        public Dictionary<string, int> Counts = new Dictionary<string, int>();      // kills, trees, fish, loads, feasts, harvests...
        public string Bed = "", Chest = "", Sworn = "";
        public double Arrived; public int Wage; public int Unpaid; public double InjuredUntil; public double LowMoodSince;
        public List<string> Foods = new List<string>();                            // what they ate, newest last (variety)

        public bool Has(string trait) => Traits.Contains(trait);
        public int Count(string key) => Counts.TryGetValue(key, out int v) ? v : 0;
        public void Bump(string key, int by = 1) => Counts[key] = Count(key) + by;
        public string FullName => string.IsNullOrEmpty(Byname) ? Name : Byname.StartsWith("the ") || Byname.StartsWith("Who ") ? Name + " " + Byname : Name + " " + Byname;
        public float Skill(string s) => Skills.TryGetValue(s, out float v) ? v : 0;

        // The record; without its saga when the saga is kept apart (it changes with every event, the record by the minute).
        public byte[] Save(bool withLog = true)
        {
            var w = new Writer();
            w.Byte(Version);
            w.Str(Vid); w.Str(Colony); w.Str(Name); w.Str(Byname); w.Str(Story); w.Str(Group); w.Bool(Female);
            w.Strs(Traits); w.Int((int)Prof); w.Int((int)Wants);
            w.Floats(Needs.ToList());
            w.Int(Moodlets.Count); foreach (var m in Moodlets) { w.Str(m.Id); w.Dbl(m.Until); w.Int(m.Stacks); }
            w.Int(Skills.Count); foreach (var kv in Skills) { w.Str(kv.Key); w.Flt(kv.Value); }
            w.Flt(Worth); w.Int(Hoard); w.Int(Deaths); w.Bool(Disgraced); w.Bool(Worthy); w.Dbl(DisgraceSince);
            Map(w, Bonds); Map(w, Opinions); Map(w, Counts);
            w.Str(Bed); w.Str(Chest); w.Str(Sworn);
            w.Dbl(Arrived); w.Int(Wage); w.Int(Unpaid); w.Dbl(InjuredUntil); w.Dbl(LowMoodSince);
            w.Strs(Foods);
            return w.Bytes();
        }
        private static void Map(Writer w, Dictionary<string, int> m) { w.Int(m.Count); foreach (var kv in m) { w.Str(kv.Key); w.Int(kv.Value); } }
        private static Dictionary<string, int> Map(Reader r) { var m = new Dictionary<string, int>(); for (int i = r.Int(); i > 0; i--) m[r.Str()] = r.Int(); return m; }

        public static VillagerState Load(byte[] data)
        {
            if (data == null || data.Length == 0) return null;
            var r = new Reader(data);
            byte version = r.Byte();
            if (version < 1 || version > Version) throw new InvalidDataException($"villager version {version} is newer than this mod ({Version})");
            var v = new VillagerState();
            v.Vid = r.Str(); v.Colony = r.Str(); v.Name = r.Str(); v.Byname = r.Str(); v.Story = r.Str(); v.Group = r.Str(); v.Female = r.Bool();
            v.Traits = r.Strs(); v.Prof = (Prof)r.Int(); v.Wants = (Prof)r.Int();
            var needs = r.Floats(); for (int i = 0; i < Math.Min(needs.Count, v.Needs.Length); i++) v.Needs[i] = needs[i];
            for (int i = r.Int(); i > 0; i--) v.Moodlets.Add(new Moodlet { Id = r.Str(), Until = r.Dbl(), Stacks = r.Int() });
            for (int i = r.Int(); i > 0; i--) v.Skills[r.Str()] = r.Flt();
            v.Worth = r.Flt(); v.Hoard = r.Int(); v.Deaths = r.Int(); v.Disgraced = r.Bool(); v.Worthy = r.Bool(); v.DisgraceSince = r.Dbl();
            v.Bonds = Map(r); v.Opinions = Map(r); v.Counts = Map(r);
            v.Bed = r.Str(); v.Chest = r.Str(); v.Sworn = r.Str();
            v.Arrived = r.Dbl(); v.Wage = r.Int(); v.Unpaid = r.Int(); v.InjuredUntil = r.Dbl(); v.LowMoodSince = r.Dbl();
            v.Foods = r.Strs();
            return v;
        }
    }

    // A small binary writer/reader (BinaryWriter's 7-bit strings, little endian), so saves are compact and versioned.
    public sealed class Writer
    {
        private readonly MemoryStream _s = new MemoryStream(); private readonly BinaryWriter _w;
        public Writer() { _w = new BinaryWriter(_s, Encoding.UTF8); }
        public void Byte(byte v) => _w.Write(v);
        public void Bool(bool v) => _w.Write(v);
        public void Int(int v) => _w.Write(v);
        public void Long(long v) => _w.Write(v);
        public void Flt(float v) => _w.Write(v);
        public void Dbl(double v) => _w.Write(v);
        public void Str(string v) => _w.Write(v ?? "");
        public void Strs(List<string> v) { Int(v.Count); foreach (var s in v) Str(s); }
        public void Floats(List<float> v) { Int(v.Count); foreach (var f in v) Flt(f); }
        public void Blob(byte[] v) { v = v ?? new byte[0]; Int(v.Length); _w.Write(v); }
        public byte[] Bytes() { _w.Flush(); return _s.ToArray(); }
    }
    public sealed class Reader
    {
        private readonly BinaryReader _r;
        public Reader(byte[] data) { _r = new BinaryReader(new MemoryStream(data), Encoding.UTF8); }
        public byte Byte() => _r.ReadByte();
        public bool Bool() => _r.ReadBoolean();
        public int Int() => _r.ReadInt32();
        public long Long() => _r.ReadInt64();
        public float Flt() => _r.ReadSingle();
        public double Dbl() => _r.ReadDouble();
        public string Str() => _r.ReadString();
        public List<string> Strs() { var l = new List<string>(); for (int i = Int(); i > 0; i--) l.Add(Str()); return l; }
        public List<float> Floats() { var l = new List<float>(); for (int i = Int(); i > 0; i--) l.Add(Flt()); return l; }
        public byte[] Blob() { int n = Int(); return _r.ReadBytes(n); }
        public bool More => _r.BaseStream.Position < _r.BaseStream.Length; // (a field added later: absent from an older record)
    }
}
