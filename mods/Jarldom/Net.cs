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
        private const string VersionRpc = "Jarldom_Version";
        private static float _nextShout;
        private const string ActRpc = "Jarldom_Act", FallenRpc = "Jarldom_Fallen", ClaimRpc = "Jarldom_Claim", ReportRpc = "Jarldom_Report", RefusedRpc = "Jarldom_Refused", ValkyrieRpc = "Jarldom_Valkyrie", VillagerRpc = "Jarldom_Villager", HelloRpc = "Jarldom_Hello", ExploreRpc = "Jarldom_Explore", EventRpc = "Jarldom_Event", PinRpc = "Jarldom_Pin", CampRpc = "Jarldom_Camp", CampAtRpc = "Jarldom_CampAt", AskRpc = "Jarldom_AskReply", CircleRpc = "Jarldom_ValkyrieCircle", BurialRpc = "Jarldom_ShipBurial2", EinherjarRpc = "Jarldom_Einherjar", FestRpc = "Jarldom_Fest";
        private static ZRoutedRpc _on; private static int _delay;
        private static readonly string[] Names = { VersionRpc, ActRpc, FallenRpc, ClaimRpc, ReportRpc, RefusedRpc, ValkyrieRpc, VillagerRpc, HelloRpc, HelloRpc + "?", ExploreRpc, EventRpc, PinRpc, CampRpc, CampAtRpc, AskRpc, CircleRpc, BurialRpc, EinherjarRpc, FestRpc };

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
            // (and to every game, now and then: each game knows the others' versions, whatever the host runs)
            if (rpc != null && rpc == _on && ZNet.instance != null && Time.time >= _nextShout)
            {
                _nextShout = Time.time + 45;
                rpc.InvokeRoutedRPC(ZRoutedRpc.Everybody, VersionRpc, $"{Plugin.Version}|{Plugin.DataVersion}|{Player.m_localPlayer?.GetPlayerName() ?? (ZNet.instance.IsDedicated() ? "the server" : "someone")}");
            }
            Versions.Warn();
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
            rpc.Register<string>(VersionRpc, (sender, said) => Versions.Heard(sender, said));
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
                RandomEvent now = RandEventSystem.instance.GetCurrentRandomEvent();
                if (name == "") { if (now != null && now.m_name == Plugin.Instance.TributeEvent.Value && Utils.DistanceXZ(now.m_pos, at) < 300) RandEventSystem.instance.ResetRandomEvent(); } // (only the Fulings' own)
                else if (now == null) RandEventSystem.instance.SetRandomEventByName(name, at); // (never over another raid somewhere)
            }));
            rpc.Register<string, Vector3, string, bool>(PinRpc, (sender, key, at, text, add) => Plugin.Guard(() => PinHere(key, at, text, add)));
            // On the server (which alone knows where the world's camps are): where a captive is taken, kept as a global key; the answer back.
            rpc.Register<string, string, Vector3, string>(CampRpc, (sender, colony, vid, from, name) => Plugin.Guard(() =>
            {
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
                Vector3 camp = Captives.FindCamp(from);
                ZoneSystem.instance.SetGlobalKey($"{Captives.KeyFor(colony, vid)} {Mathf.RoundToInt(camp.x)},{Mathf.RoundToInt(camp.z)}");
                Pin(Captives.KeyFor(colony, vid), camp, name + ", held by the Fulings", true);
                ZRoutedRpc.instance.InvokeRoutedRPC(sender, CampAtRpc, colony, vid, camp);
            }));
            rpc.Register<string, string, Vector3>(CampAtRpc, (sender, colony, vid, camp) => Plugin.Guard(() => Captives.CampFound(colony, vid, camp)));
            rpc.Register<string, string, bool>(AskRpc, (sender, vid, id, ok) => Plugin.Guard(() => Asks.Replied(vid, id, ok)));
            rpc.Register<Vector3>(ValkyrieRpc, (sender, at) => Plugin.Guard(() => Valkyrie.Fly(at)));
            rpc.Register<int, Vector3>(FestRpc, (sender, kind, at) => Plugin.Guard(() => FestivalDays.Show(kind, at)));
            rpc.Register<Vector3, string>(EinherjarRpc, (sender, at, name) => Plugin.Guard(() => Einherjar.Arriving(at, name)));
            rpc.Register<Vector3>(CircleRpc, (sender, at) => Plugin.Guard(() => Valkyrie.Fly(at, true)));
            rpc.Register<Vector3, Quaternion, Vector3, Vector3>(BurialRpc, (sender, at, rot, dir, archer) => Plugin.Guard(() => ShipBurial.Show(at, rot, dir, archer)));
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
        public static void Pin(string key, Vector3 at, string text, bool add) => ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.Everybody, PinRpc, key, at, text, add);
        // Our own pins, kept by key: never a player's (the game's RemovePin by place takes the nearest saved pin, which is theirs).
        private static readonly Dictionary<string, Minimap.PinData> Pins = new Dictionary<string, Minimap.PinData>();
        internal static void PinHere(string key, Vector3 at, string text, bool add)
        {
            if (Minimap.instance == null) return;
            if (Pins.TryGetValue(key, out Minimap.PinData old))
            {
                if (add && old.m_name == text && Vector3.Distance(old.m_pos, at) < 1f) return;
                Minimap.instance.RemovePin(old); Pins.Remove(key);
            }
            if (add) Pins[key] = Minimap.instance.AddPin(at, Minimap.PinType.Icon3, text, false, false);
        }
        public static void ForgetPins() { if (Minimap.instance != null) foreach (var p in Pins.Values) Minimap.instance.RemovePin(p); Pins.Clear(); }
        public static void FindCamp(Colony c, string vid, string name) => ZRoutedRpc.instance?.InvokeRoutedRPC(CampRpc, c.Id, vid, c.Position, name); // (to the server)
        public static void Event(string name, Vector3 at) => ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.Everybody, EventRpc, name, at);
        public static void Unregister() { if (ZRoutedRpc.instance != null) Remove(ZRoutedRpc.instance); _on = null; }
        private static void Remove(ZRoutedRpc rpc)
        {
            var functions = AccessTools.Field(typeof(ZRoutedRpc), "m_functions").GetValue(rpc) as System.Collections.IDictionary;
            if (functions == null) return;
            foreach (string n in Names) functions.Remove(n.GetStableHashCode());
        }
        private static long Owner(Colony c) => c?.Z?.GetOwner() ?? 0; // (no record while the world shuts down)
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
                if (s != null && (killer.StartsWith("trade:") || killer.StartsWith(Captives.Prefix) || killer.StartsWith(Carts.Prefix) || killer.StartsWith(Mining.Prefix) || killer.StartsWith(Mining.Escort))) c.SoulAway(s, gear, at, killer); // (away, not dead)
                else if (s != null) c.SoulFell(s, gear, at, killer, lost);
            });
        }

        // ---- work for villagers on other games ----
        private static readonly Dictionary<string, (List<Order> orders, uint rev)> Shared = new Dictionary<string, (List<Order>, uint)>();
        public static Order BestShared(Colony c, Brain b, Func<Prof, bool> canDo)
        {
            uint rev = c.BoardRevision;
            if (!Shared.TryGetValue(c.Id, out var s) || s.rev != rev) { s = (JobBoard.FromSnapshot(c.BoardBytes()), rev); Shared[c.Id] = s; }
            return s.orders.Where(o => o.State == OrderState.Open && canDo(o.Prof) && !Refused.Contains((b.S.Vid, o.Id)) && Work.GearFor(b, o) == null && o.Kind != "craft")
                .OrderByDescending(o => o.Priority).ThenBy(o => (o.X - b.Pos.x) * (o.X - b.Pos.x) + (o.Z - b.Pos.z) * (o.Z - b.Pos.z)).FirstOrDefault();
        }
        public static void Claim(Colony c, Brain b, Order o) { long to = Owner(c); if (to != 0) ZRoutedRpc.instance?.InvokeRoutedRPC(to, ClaimRpc, c.Id, b.S.Vid, o.Id); } // (0 would be everyone)
        public static bool Renew(Colony c, Brain b, Order o) { if (Refused.Contains((b.S.Vid, o.Id))) return false; Claim(c, b, o); return true; }
        public static void Report(Colony c, Brain b, Order o, string what) { long to = Owner(c); if (to != 0) ZRoutedRpc.instance?.InvokeRoutedRPC(to, ReportRpc, c.Id, b.S.Vid, o.Id, what); }
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

        // A villager's answer to the player who answered their ask (on this game: at once).
        public static void AskReply(long to, string vid, string id, bool ok)
        {
            if (to == 0) return;
            if (to == ZDOMan.GetSessionID()) Plugin.Guard(() => Asks.Replied(vid, id, ok));
            else ZRoutedRpc.instance?.InvokeRoutedRPC(to, AskRpc, vid, id, ok);
        }
        public static void Villager(long owner, string vid, string action, string arg) => ZRoutedRpc.instance?.InvokeRoutedRPC(owner, VillagerRpc, vid, action, arg);
        public static void Valkyrie_(Vector3 at) => ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.Everybody, ValkyrieRpc, at);
        public static void ShipBurial_(Vector3 at, Quaternion rot, Vector3 dir, Vector3 archer) => ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.Everybody, BurialRpc, at, rot, dir, archer);
        public static void Fest_(int kind, Vector3 at) => ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.Everybody, FestRpc, kind, at); // (a festival's fire, on every game near)
        public static void Einherjar_(Vector3 at, string name) => ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.Everybody, EinherjarRpc, at, name); // (one back from Valhalla: the Valkyrie bringing them)
        public static void ValkyrieCircle(Vector3 at) => ZRoutedRpc.instance?.InvokeRoutedRPC(ZRoutedRpc.Everybody, CircleRpc, at); // (an ascension: once round the pyre, and up)
    }

    internal sealed partial class Colony
    {
        private void LoadBoard() { }
        // The shared job board lives on a still object of its own beside the pillars: it changes every few seconds (claims, new jobs),
        // and on the pillars it would send the whole colony record with it each time.
        public const string BoardZdoKey = "jarldom_boardzdo";
        public ZDO BoardZ
        {
            get
            {
                ZDOID id = Z.GetZDOID(BoardZdoKey);
                if (id.IsNone() || ZDOMan.instance == null) return null;
                ZDO bz = ZDOMan.instance.GetZDO(id);
                if (bz != null && bz.GetPrefab() == SoulStore.PrefabName.GetStableHashCode() && bz.GetString(IdKey, "") == Id) return bz;
                bz = SoulStore.Find(IdKey, Id); // (ids are new each time the world loads: found by the colony's id)
                if (bz != null && IsOwner) Z.Set(BoardZdoKey, bz.m_uid);
                return bz;
            }
        }
        public byte[] BoardBytes() { byte[] b = BoardZ?.GetByteArray(BoardKey); return b != null && b.Length > 0 ? b : Z.GetByteArray(BoardKey); }
        public uint BoardRevision => BoardZ?.DataRevision ?? Z.DataRevision;
        private void ShareBoard()
        {
            byte[] snap = Board.Snapshot();
            ZDO bz = BoardZ;
            if (bz == null && Z.GetZDOID(BoardZdoKey).IsNone())
            {
                int hash = SoulStore.PrefabName.GetStableHashCode();
                if (ZNetScene.instance?.GetPrefab(SoulStore.PrefabName) != null)
                {
                    bz = ZDOMan.instance.CreateNewZDO(Position + Vector3.down * 3f, hash);
                    bz.Persistent = true; bz.Type = ZDO.ObjectType.Default; bz.Distant = false; bz.SetPrefab(hash);
                    bz.Set(IdKey, Id);
                    Z.Set(BoardZdoKey, bz.m_uid);
                    if (Z.GetByteArray(BoardKey)?.Length > 0) Z.Set(BoardKey, new byte[0]); // (the old copy off the pillars)
                }
            }
            if (bz == null) { byte[] old = Z.GetByteArray(BoardKey); if (old == null || !old.SequenceEqual(snap)) Z.Set(BoardKey, snap); return; }
            byte[] had = bz.GetByteArray(BoardKey);
            if (had != null && had.Length == snap.Length && had.SequenceEqual(snap)) return;
            if (!bz.IsOwner()) bz.SetOwner(ZDOMan.GetSessionID());
            bz.Set(BoardKey, snap);
        }
    }
}
