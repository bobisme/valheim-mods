using System;
using System.Collections.Generic;
using System.Linq;

namespace Jarldom
{
    public enum OrderState { Open, Claimed, Done, Blocked, Cancelled }

    public sealed class Order
    {
        public int Id; public string Kind = "", Target = "", Zone = ""; public Prof Prof; public int Priority = 3;
        public float X, Y, Z; public double Posted; public OrderState State; public string Blocked = "";
        public string Vid = ""; public double LeaseUntil; public string By = ""; public double BlockedUntil; // who posted it (a player, a zone, the colony)
        public string Key => Kind + "|" + Target;
        public Order Copy() => (Order)MemberwiseClone();
    }

    public sealed class Reservation { public int Order; public string Chest, Item; public int Amount; }

    // The colony's work: orders posted by zones, stations, players and the colony itself, claimed by villagers as leases.
    // Lives on the colony owner's game; villagers on other games ask it by RPC. Everything is idempotent:
    // the same order posted twice is one order, a claim by the same villager twice is one lease, a completion reported twice counts once.
    public sealed class JobBoard
    {
        public const double LeaseSeconds = 60;
        private readonly Dictionary<int, Order> _orders = new Dictionary<int, Order>();
        private readonly Dictionary<string, int> _byKey = new Dictionary<string, int>();
        private readonly List<Reservation> _reserved = new List<Reservation>();
        private readonly HashSet<int> _completed = new HashSet<int>();
        private int _next = 1;
        public int CompletedCount { get; private set; }

        public IEnumerable<Order> All => _orders.Values;
        public Order Get(int id) => _orders.TryGetValue(id, out Order o) ? o : null;
        public int Count(OrderState s) => _orders.Values.Count(o => o.State == s);

        // Post an order, or refresh the one already standing for the same kind and target. Returns its id.
        public int Post(string kind, string target, Prof prof, int priority, float x, float y, float z, double now, string zone = "", string by = "")
        {
            if (LeftAlone.Contains(kind + "|" + target)) return -1; // (out of reach: not posted)
            if (_byKey.TryGetValue(kind + "|" + target, out int id) && _orders.TryGetValue(id, out Order have) && have.State != OrderState.Done && have.State != OrderState.Cancelled)
            {
                have.Priority = Math.Max(have.Priority, priority);
                have.X = x; have.Y = y; have.Z = z;
                if (have.State == OrderState.Blocked && string.IsNullOrEmpty(have.Blocked)) have.State = OrderState.Open;
                return id;
            }
            var o = new Order { Id = _next++, Kind = kind, Target = target, Prof = prof, Priority = Math.Max(1, Math.Min(5, priority)), X = x, Y = y, Z = z, Posted = now, Zone = zone ?? "", By = by ?? "" };
            _orders[o.Id] = o; _byKey[o.Key] = o.Id;
            return o.Id;
        }

        // Claim an order for a villager: a lease until now + LeaseSeconds. The same villager claiming again renews; anyone else is refused.
        public bool Claim(int id, string vid, double now, out double until)
        {
            until = 0;
            Expire(now);
            if (!_orders.TryGetValue(id, out Order o) || string.IsNullOrEmpty(vid)) return false;
            if (o.State == OrderState.Claimed && o.Vid != vid) return false;
            if (o.State != OrderState.Open && o.State != OrderState.Claimed) return false;
            // One live lease per villager: taking a new order lets the old one go.
            foreach (Order other in _orders.Values.Where(x => x.State == OrderState.Claimed && x.Vid == vid && x.Id != id).ToList()) Release(other.Id, vid);
            o.State = OrderState.Claimed; o.Vid = vid; o.LeaseUntil = now + LeaseSeconds;
            until = o.LeaseUntil;
            return true;
        }
        public bool Renew(int id, string vid, double now) => Claim(id, vid, now, out _);

        // Give an order back (the villager gave up or was called away): it opens again and its items are freed.
        public bool Release(int id, string vid)
        {
            if (!_orders.TryGetValue(id, out Order o) || o.State != OrderState.Claimed || o.Vid != vid) return false;
            o.State = OrderState.Open; o.Vid = ""; o.LeaseUntil = 0;
            Unreserve(id);
            return true;
        }

        // Done. True only the first time (and only for the villager holding it, or anyone if it lapsed and nobody took it since).
        public bool Complete(int id, string vid)
        {
            if (_completed.Contains(id)) return false;
            if (!_orders.TryGetValue(id, out Order o)) return false;
            if (o.State == OrderState.Claimed && o.Vid != vid) return false;
            if (o.State == OrderState.Cancelled || o.State == OrderState.Blocked) return false;
            o.State = OrderState.Done; o.Vid = vid; o.LeaseUntil = 0; _unreachable.Remove(o.Key);
            _completed.Add(id); CompletedCount++;
            Unreserve(id);
            _byKey.Remove(o.Key);
            return true;
        }

        public const double BlockSeconds = 300;
        private double _now;
        // Out of reach (a torch out on a dock no one can walk onto): each time again it waits twice as long, up to a quarter day;
        // and the same work right by it (the next torches along) is taken as out of reach too, not walked to one by one.
        private readonly Dictionary<string, int> _unreachable = new Dictionary<string, int>();
        public const double MostBlocked = 21600;
        // A fire or torch out of reach twice over is left alone (the colony keeps the list a while, and the scan doesn't post it).
        public const int LeaveAloneAfter = 2;
        public readonly HashSet<string> LeftAlone = new HashSet<string>();
        public readonly List<(float x, float y, float z)> LeftAloneAt = new List<(float, float, float)>(); // (where, for the colony to keep: ids don't last a reload)
        public void Block(int id, string reason)
        {
            if (!_orders.TryGetValue(id, out Order o) || o.State == OrderState.Done) return;
            double wait = BlockSeconds;
            bool reach = (reason ?? "").StartsWith("can't reach");
            if (reach) { int n = _unreachable.TryGetValue(o.Key, out int k) ? k + 1 : 1; _unreachable[o.Key] = n; wait = Math.Min(MostBlocked, BlockSeconds * Math.Pow(2, n - 1));
                if (o.Kind == "fuel" && n >= LeaveAloneAfter)
                {
                    foreach (Order gone in _orders.Values.Where(x => x.Id == id || x.Kind == o.Kind && x.State != OrderState.Done && x.State != OrderState.Cancelled && (x.X - o.X) * (x.X - o.X) + (x.Z - o.Z) * (x.Z - o.Z) < 64 && Math.Abs(x.Y - o.Y) < 3).ToList())
                    { LeftAlone.Add(gone.Key); LeftAloneAt.Add((gone.X, gone.Y, gone.Z)); gone.State = OrderState.Cancelled; gone.Vid = ""; gone.Blocked = reason ?? ""; Unreserve(gone.Id); _byKey.Remove(gone.Key); }
                    return;
                }
            }
            o.State = OrderState.Blocked; o.Blocked = reason ?? ""; o.Vid = ""; o.LeaseUntil = 0; o.BlockedUntil = _now + wait;
            Unreserve(id);
            if (!reach) return;
            foreach (Order near in _orders.Values.Where(x => x.Id != id && x.State == OrderState.Open && x.Kind == o.Kind && (x.X - o.X) * (x.X - o.X) + (x.Z - o.Z) * (x.Z - o.Z) < 64 && Math.Abs(x.Y - o.Y) < 3).ToList())
            { near.State = OrderState.Blocked; near.Blocked = reason; near.BlockedUntil = _now + wait; Unreserve(near.Id); }
        }
        public void Unblock(int id) { if (_orders.TryGetValue(id, out Order o) && o.State == OrderState.Blocked) { o.State = OrderState.Open; o.Blocked = ""; } }
        public void Cancel(int id)
        {
            if (!_orders.TryGetValue(id, out Order o) || o.State == OrderState.Done) return;
            o.State = OrderState.Cancelled; o.Vid = ""; Unreserve(id); _byKey.Remove(o.Key);
        }

        // Leases that ran out go back on the board.
        public int Expire(double now)
        {
            _now = now;
            int n = 0;
            foreach (Order o in _orders.Values)
                if (o.State == OrderState.Blocked && o.BlockedUntil > 0 && o.BlockedUntil <= now) { o.State = OrderState.Open; o.Blocked = ""; o.BlockedUntil = 0; } // try again later
            foreach (Order o in _orders.Values)
                if (o.State == OrderState.Claimed && o.LeaseUntil <= now) { o.State = OrderState.Open; o.Vid = ""; o.LeaseUntil = 0; Unreserve(o.Id); n++; }
            return n;
        }

        // Forget finished orders older than a while, so the board doesn't grow for ever.
        public void Prune(double now, double keepSeconds = 600)
        {
            foreach (Order o in _orders.Values.Where(o => (o.State == OrderState.Done || o.State == OrderState.Cancelled) && now - o.Posted > keepSeconds).ToList())
            { _orders.Remove(o.Id); _completed.Remove(o.Id); }
        }

        // The best order for a villager: what their profession allows, highest priority, then nearest, and gear permitting.
        public Order Best(string vid, Func<Prof, bool> canDo, float x, float z, Func<Order, string> gearProblem, double now)
        {
            Expire(now);
            Order mine = _orders.Values.FirstOrDefault(o => o.State == OrderState.Claimed && o.Vid == vid);
            if (mine != null) return mine;
            foreach (Order o in _orders.Values.Where(o => o.State == OrderState.Open && canDo(o.Prof))
                         .OrderByDescending(o => o.Priority).ThenBy(o => (o.X - x) * (o.X - x) + (o.Z - z) * (o.Z - z)))
            {
                string problem = gearProblem?.Invoke(o);
                if (!string.IsNullOrEmpty(problem)) { o.Blocked = problem; continue; } // stays open for others; the reason shows on the board
                return o;
            }
            return null;
        }

        // Items set aside for an order, so two haulers don't empty the same stack.
        public bool Reserve(int order, string chest, string item, int amount, int inChest)
        {
            if (amount <= 0) return false;
            if (Available(chest, item, inChest) < amount) return false;
            _reserved.Add(new Reservation { Order = order, Chest = chest, Item = item, Amount = amount });
            return true;
        }
        public int Reserved(string chest, string item) => _reserved.Where(r => r.Chest == chest && r.Item == item).Sum(r => r.Amount);
        public int Available(string chest, string item, int inChest) => Math.Max(0, inChest - Reserved(chest, item));
        public void Unreserve(int order) => _reserved.RemoveAll(r => r.Order == order);
        public IEnumerable<Reservation> ReservationsOf(int order) => _reserved.Where(r => r.Order == order);

        // The open and claimed orders, for the other games (their villagers choose from it and ask the owner for the lease).
        public byte[] Snapshot()
        {
            var w = new Writer();
            w.Byte(1);
            var live = _orders.Values.Where(o => o.State == OrderState.Open || o.State == OrderState.Claimed || o.State == OrderState.Blocked).ToList();
            w.Int(live.Count);
            foreach (Order o in live)
            {
                w.Int(o.Id); w.Str(o.Kind); w.Str(o.Target); w.Str(o.Zone); w.Int((int)o.Prof); w.Int(o.Priority); w.Flt(o.X); w.Flt(o.Y); w.Flt(o.Z);
                w.Int((int)o.State); w.Str(o.Blocked); w.Str(o.Vid); w.Str(o.By);
            }
            return w.Bytes();
        }
        public static List<Order> FromSnapshot(byte[] data)
        {
            var list = new List<Order>();
            if (data == null || data.Length == 0) return list;
            var r = new Reader(data);
            if (r.Byte() != 1) return list;
            for (int i = r.Int(); i > 0; i--)
                list.Add(new Order
                {
                    Id = r.Int(), Kind = r.Str(), Target = r.Str(), Zone = r.Str(), Prof = (Prof)r.Int(), Priority = r.Int(), X = r.Flt(), Y = r.Flt(), Z = r.Flt(),
                    State = (OrderState)r.Int(), Blocked = r.Str(), Vid = r.Str(), By = r.Str(),
                });
            return list;
        }
    }
}
