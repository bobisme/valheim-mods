using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Jarldom
{
    // Talking to the colony owner. Players' choices (accept a newcomer, set a profession, mark a room...) and other games' villagers (claims,
    // reports, deaths) go to the game that owns the High-Seat Pillars; it alone changes the colony. The sender is who the RPC came from.
    internal static class Net
    {
        private const string ActRpc = "Jarldom_Act", FallenRpc = "Jarldom_Fallen", ClaimRpc = "Jarldom_Claim", ReportRpc = "Jarldom_Report", RefusedRpc = "Jarldom_Refused", ValkyrieRpc = "Jarldom_Valkyrie", VillagerRpc = "Jarldom_Villager", HelloRpc = "Jarldom_Hello", ExploreRpc = "Jarldom_Explore", EventRpc = "Jarldom_Event", PinRpc = "Jarldom_Pin";
        private static ZRoutedRpc _on; private static int _delay;
        private static readonly string[] Names = { ActRpc, FallenRpc, ClaimRpc, ReportRpc, RefusedRpc, ValkyrieRpc, VillagerRpc, HelloRpc, HelloRpc + "?", ExploreRpc, EventRpc, PinRpc };

        // Whether the host has Jarldom too (a host without it deletes our pieces and villagers as soon as it loads them).
        public static bool HostHasJarldom => ZNet.instance == null || ZNet.instance.IsServer() || _hostAnswered;
        // On the host: the players whose games said hello (have Jarldom). The others can't run a colony or a villager.
        public static readonly HashSet<long> WithJarldom = new HashSet<long>();
        private static bool _hostAnswered; private static float _nextHello;
        public static void Tick()
        {
            ZRoutedRpc rpc = ZRoutedRpc.instance;
            // (every 5 s until the host answers, then every minute: a host that reloaded Jarldom has forgotten who said hello)
            if (rpc != null && rpc == _on && ZNet.instance != null && !ZNet.instance.IsServer() && ZNet.instance.GetServerPeer() != null && Time.time >= _nextHello)
            {
                _nextHello = Time.time + (_hostAnswered ? 60 : 5);
                rpc.InvokeRoutedRPC(HelloRpc, Plugin.Version);
            }
            if (rpc == null || rpc == _on) return;
            _hostAnswered = false; _nextHello = 0;
            if (_delay++ < 2) return; // after a reload, the old copy unregisters at the end of its last frame
            _delay = 0; _on = rpc;
            _nextOwners = Time.time + 15; // give every game time to say hello before taking anything back
            Remove(rpc);
            rpc.Register<string, string, string>(ActRpc, OnAct);
            rpc.Register<string, ZPackage>(FallenRpc, OnFallen);
            rpc.Register<string, string, int>(ClaimRpc, OnClaim);
            rpc.Register<string, string, int, string>(ReportRpc, OnReport);
            rpc.Register<string, int>(RefusedRpc, OnRefused);
            rpc.Register<string, string, string>(VillagerRpc, (sender, vid, action, arg) => { Brain b = Villagers.ByVid(vid); if (b != null) Plugin.Guard(() => b.Command(action, arg)); });
            rpc.Register<string>(HelloRpc, (sender, version) =>
            {
                if (ZNet.instance != null && ZNet.instance.IsServer()) { WithJarldom.Add(sender); if (sender != ZDOMan.GetSessionID()) ZRoutedRpc.instance.InvokeRoutedRPC(sender, HelloRpc, Plugin.Version); }
                else { if (!_hostAnswered) Plugin.Log($"Jarldom: the host has Jarldom {version}"); _hostAnswered = true; }
            });
            // A host that has just (re)loaded asks everyone: the games with Jarldom say hello back.
            if (ZNet.instance != null && ZNet.instance.IsServer()) rpc.InvokeRoutedRPC(ZRoutedRpc.Everybody, HelloRpc + "?");
            rpc.Register(HelloRpc + "?", sender =>
            {
                if (ZNet.instance != null && !ZNet.instance.IsServer()) _nextHello = 0;
            });
            rpc.Register<Vector3, float>(ExploreRpc, (sender, at, radius) => Plugin.Guard(() => { if (Minimap.instance != null) ExploreMethod.Invoke(Minimap.instance, new object[] { at, radius }); }));
            rpc.Register<string, Vector3>(EventRpc, (sender, name, at) => Plugin.Guard(() =>
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer() || RandEventSystem.instance == null) return;
                if (name == "") RandEventSystem.instance.ResetRandomEvent(); else RandEventSystem.instance.SetRandomEventByName(name, at);
            }));
            rpc.Register<Vector3, string, bool>(PinRpc, (sender, at, text, add) => Plugin.Guard(() =>
            {
                if (Minimap.instance == null) return;
                Minimap.instance.RemovePin(at, 8f);
                if (add) Minimap.instance.AddPin(at, Minimap.PinType.Icon3, text, false, false);
            }));
            rpc.Register<Vector3>(ValkyrieRpc, (sender, at) => Plugin.Guard(() => Valkyrie.Fly(at)));
        }
        // The game gives an object to whichever nearby player it likes; one without Jarldom would leave the colony or a villager frozen.
        // The host takes such objects back for itself (when it has them loaded), every few seconds.
        private static float _nextOwners;
        public static void KeepOwners()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZNet.instance.IsDedicated() || Time.time < _nextOwners) return;
            _nextOwners = Time.time + 3;
            long me = ZDOMan.GetSessionID();
            WithJarldom.Add(me);
            bool Lacks(long owner) => owner != 0 && owner != me && !WithJarldom.Contains(owner) && ZNet.instance.GetPeer(owner) != null;
            foreach (Colony c in Colony.All.ToList()) if (Lacks(c.Z.GetOwner())) { c.View.ClaimOwnership(); Plugin.Log($"Jarldom: took {c.R.Name} back from a game without Jarldom"); }
            foreach (Humanoid h in Villagers.Bodies().ToList())
            {
                ZNetView v = h.GetComponent<ZNetView>(); Container bag = h.GetComponent<Container>();
                if (Lacks(v.GetZDO().GetOwner()) && !(bag != null && ContainerAccess.InUse(bag))) v.ClaimOwnership(); // (not while someone has the bag open)
            }
        }
        // The map filled in round a scout, for everyone (each game explores its own map).
        private static readonly System.Reflection.MethodInfo ExploreMethod = AccessTools.Method(typeof(Minimap), "Explore", new[] { typeof(Vector3), typeof(float) });
        public static void Explore(Vector3 at, float radius) => ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.Everybody, ExploreRpc, at, radius);
        // A game event started (or, with no name, stopped) by the host, who runs them.
        // A pin on everyone's map (or taken off), not saved: the captives' camp.
        public static void Pin(Vector3 at, string text, bool add) => ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.Everybody, PinRpc, at, text, add);
        public static void Event(string name, Vector3 at) => ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.Everybody, EventRpc, name, at);
        public static void Unregister() { if (ZRoutedRpc.instance != null) Remove(ZRoutedRpc.instance); _on = null; }
        private static void Remove(ZRoutedRpc rpc)
        {
            var functions = AccessTools.Field(typeof(ZRoutedRpc), "m_functions").GetValue(rpc) as System.Collections.IDictionary;
            if (functions == null) return;
            foreach (string n in Names) functions.Remove(n.GetStableHashCode());
        }
        private static long Owner(Colony c) => c.Z.GetOwner();
        private static string Who(long sender)
        {
            if (ZNet.instance == null) return "";
            if (sender == ZDOMan.GetSessionID()) return Player.m_localPlayer?.GetPlayerName() ?? "";
            ZNetPeer peer = ZNet.instance.GetPeer(sender);
            return peer?.m_playerName ?? "";
        }

        // ---- players' choices ----
        public static void Act(Colony c, string action, string arg)
        {
            if (c == null || !c.Valid) return;
            if (c.IsOwner) { Plugin.Guard(() => c.Handle(action, arg ?? "", Player.m_localPlayer?.GetPlayerName() ?? "")); return; }
            ZRoutedRpc.instance?.InvokeRoutedRPC(Owner(c), ActRpc, c.Id, action, arg ?? "");
        }
        private static void OnAct(long sender, string colony, string action, string arg)
        {
            Colony c = Colony.Of(colony);
            if (c == null || !c.IsOwner) return;
            Plugin.Guard(() => c.Handle(action, arg, Who(sender)));
        }

        // ---- a villager owned by another game fell ----
        public static void SendFallen(Colony c, VillagerState s, byte[] gear, Vector3 at, string killer, bool lost)
        {
            var pkg = new ZPackage();
            pkg.Write(s.Save()); pkg.Write(gear ?? new byte[0]); pkg.Write(at); pkg.Write(killer ?? ""); pkg.Write(lost);
            ZRoutedRpc.instance?.InvokeRoutedRPC(Owner(c), FallenRpc, c.Id, pkg);
        }
        // When the colony isn't loaded here: every game hears it, and the one that owns the colony keeps the soul.
        public static void SendFallenAnywhere(VillagerState s, byte[] gear, Vector3 at, string killer, bool lost)
        {
            var pkg = new ZPackage();
            pkg.Write(s.Save()); pkg.Write(gear ?? new byte[0]); pkg.Write(at); pkg.Write(killer ?? ""); pkg.Write(lost);
            ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.Everybody, FallenRpc, s.Colony, pkg);
        }
        private static void OnFallen(long sender, string colony, ZPackage pkg)
        {
            Colony c = Colony.Of(colony);
            if (c == null || !c.IsOwner) return;
            Plugin.Guard(() =>
            {
                VillagerState s = VillagerState.Load(pkg.ReadByteArray());
                byte[] gear = pkg.ReadByteArray(); Vector3 at = pkg.ReadVector3(); string killer = pkg.ReadString(); bool lost = pkg.ReadBool();
                if (s != null && killer.StartsWith("trade:")) c.SoulAway(s, gear, at, killer);
                else if (s != null) c.SoulFell(s, gear, at, killer, lost);
            });
        }

        // ---- work for villagers on other games ----
        private static readonly Dictionary<string, (List<Order> orders, uint rev)> Shared = new Dictionary<string, (List<Order>, uint)>();
        public static Order BestShared(Colony c, Brain b, Func<Prof, bool> canDo)
        {
            uint rev = c.Z.DataRevision;
            if (!Shared.TryGetValue(c.Id, out var s) || s.rev != rev) { s = (JobBoard.FromSnapshot(c.Z.GetByteArray(Colony.BoardKey)), rev); Shared[c.Id] = s; }
            return s.orders.Where(o => o.State == OrderState.Open && canDo(o.Prof) && !Refused.Contains((b.S.Vid, o.Id)) && Work.GearFor(b, o) == null && o.Kind != "craft")
                .OrderByDescending(o => o.Priority).ThenBy(o => (o.X - b.Pos.x) * (o.X - b.Pos.x) + (o.Z - b.Pos.z) * (o.Z - b.Pos.z)).FirstOrDefault();
        }
        public static void Claim(Colony c, Brain b, Order o) => ZRoutedRpc.instance?.InvokeRoutedRPC(Owner(c), ClaimRpc, c.Id, b.S.Vid, o.Id);
        public static bool Renew(Colony c, Brain b, Order o) { if (Refused.Contains((b.S.Vid, o.Id))) return false; Claim(c, b, o); return true; }
        public static void Report(Colony c, Brain b, Order o, string what) => ZRoutedRpc.instance?.InvokeRoutedRPC(Owner(c), ReportRpc, c.Id, b.S.Vid, o.Id, what);
        private static readonly HashSet<(string, int)> Refused = new HashSet<(string, int)>();
        private static void OnClaim(long sender, string colony, string vid, int order)
        {
            Colony c = Colony.Of(colony);
            if (c == null || !c.IsOwner) return;
            if (!c.Board.Claim(order, vid, c.Now, out _)) ZRoutedRpc.instance.InvokeRoutedRPC(sender, RefusedRpc, vid, order);
        }
        private static void OnRefused(long sender, string vid, int order)
        {
            Refused.Add((vid, order));
            if (Refused.Count > 2000) Refused.Clear();
            Brain b = Villagers.ByVid(vid);
            if (b?.Task is WorkTask w && w.O.Id == order) { w.Done = true; }
        }
        private static void OnReport(long sender, string colony, string vid, int order, string what)
        {
            Colony c = Colony.Of(colony);
            if (c == null || !c.IsOwner) return;
            if (what == "done") c.Board.Complete(order, vid);
            else if (what == "release") c.Board.Release(order, vid);
            else if (what == "cancel") c.Board.Cancel(order);
            else if (what.StartsWith("block:")) c.Board.Block(order, what.Substring(6));
        }

        public static void Villager(long owner, string vid, string action, string arg) => ZRoutedRpc.instance?.InvokeRoutedRPC(owner, VillagerRpc, vid, action, arg);
        public static void Valkyrie_(Vector3 at) => ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.Everybody, ValkyrieRpc, at);
    }

    internal sealed partial class Colony
    {
        private void LoadBoard() { }
        private void ShareBoard()
        {
            byte[] snap = Board.Snapshot();
            byte[] had = Z.GetByteArray(BoardKey);
            if (had != null && had.Length == snap.Length && had.SequenceEqual(snap)) return;
            Z.Set(BoardKey, snap);
        }
    }
}
