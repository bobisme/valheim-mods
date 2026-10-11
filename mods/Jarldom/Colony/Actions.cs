using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace Jarldom
{
    // What players ask of the colony (through the window, the hammer, the altars), carried out by the colony owner.
    internal sealed partial class Colony
    {
        private static readonly HashSet<string> Open = new HashSet<string> { "offer", "horn", "bogpaid", "gone", "away", "log" }; // anyone may pray or blow the horn
        public bool AllowedName(string who) => string.IsNullOrEmpty(who) || R.Founder == 0 || who == R.FounderName || R.Allowed.Contains(who) || R.Allowed.Contains("*");

        public void Handle(string action, string arg, string who)
        {
            if (!IsOwner) return;
            if (!Open.Contains(action) && !AllowedName(who)) { TellPlayer(who, $"Only {R.FounderName} and those they allow can run {R.Name}."); return; }
            string[] a = arg.Split('|');
            switch (action)
            {
                case "accept": Migration.Accept(this, Int(a, 0), who); break;
                case "refuse": Migration.Refuse(this, Int(a, 0), who); break;
                case "prof": Villagers.Command(a[0], "prof", Get(a, 1)); break;
                case "bed": Villagers.Command(a[0], "bed", Get(a, 1)); break;
                case "autobeds": AutoBeds(); break;
                case "chest": Villagers.Command(a[0], "chest", Get(a, 1)); break;
                case "stockpile": if (Get(a, 1) == "") R.Stockpiles.Remove(a[0]); else R.Stockpiles[a[0]] = a[1]; if (R.Treasury == a[0] && Get(a, 1) != "treasury") R.Treasury = ""; break;
                case "treasury": R.Treasury = a[0]; R.Stockpiles.Remove(a[0]); Log("text", "", "", "A chest was chosen to be the treasury."); break;
                case "room": AddRoom(a, who); break;
                case "zone": AddZone(a, who); break;
                case "unroom": R.Rooms.RemoveAll(r => r.Id == a[0]); R.Zones.RemoveAll(z => z.Id == a[0]); Rerank(); break;
                case "check": CheckRoomSoon(a[0]); break;
                case "feast": Feast.Call(this, who); break;
                case "horn": HornUntil = a[0] == "on" ? Time.time + 300 : 0; if (a[0] == "on") { Assets.Effect("sfx_horn", Position); Tell("The war horn sounds!", true); } else Tell("The all-clear."); break;
                case "offer": Faith.Received(this, Get(a, 0), Int(a, 1), Get(a, 2), who); break;
                case "ascend": Ascension.Begin(this, a[0], who); break;
                case "rule": Thing.Rule(this, Int(a, 0), Get(a, 1) == "yes", who); break;
                case "dismiss": Villagers.Command(a[0], "dismiss", ""); break;
                case "disband": Disband(who); break;
                case "allow": if (a[0].Length > 0 && !R.Allowed.Contains(a[0])) R.Allowed.Add(a[0]); break;
                case "disallow": R.Allowed.Remove(a[0]); break;
                case "rename": if (a[0].Trim().Length > 0) R.Name = a[0].Trim().Substring(0, Math.Min(40, a[0].Trim().Length)); break;
                case "swear": Society.Swear(this, Get(a, 0), Get(a, 1)); break;
                case "ring": Villagers.Command(a[0], "ring", who); Log("ring", Villagers.ByVid(a[0])?.S.Name ?? a[0], who); break;
                case "summon": Villagers.Command(a[0], "summon", ""); break;
                case "log": { string[] f = arg.Split('\u001e'); if (f.Length == 4) Log(f[0], f[1], f[2], f[3]); } break;
                case "away": break; // (logged by the trader; the record follows from SoulAway)
                case "bogpaid": R.BogPaid += Int(a, 0); break;
                case "gone": { ReleaseBed(a[0]); RosterRow row = R.Row(a[0]); if (row != null) row.State = Get(a, 1) == "deserted" ? SoulState.Deserted : Get(a, 1) == "ascended" ? SoulState.Ascended : SoulState.Dismissed; Rerank(); } break;
                default: Plugin.Warn("Jarldom: unknown colony action " + action); return;
            }
            _dirty = true;
            SurveySoon(); // beds, chests and rooms may have changed
        }
        private static int Int(string[] a, int i) => a.Length > i && int.TryParse(a[i], out int v) ? v : 0;
        private static string Get(string[] a, int i) => a.Length > i ? a[i] : "";
        private static float Flt(string s) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0;
        private void TellPlayer(string who, string text)
        {
            Player p = Player.GetAllPlayers().FirstOrDefault(x => x.GetPlayerName() == who);
            p?.Message(MessageHud.MessageType.Center, text);
        }

        // "room|<type>|<god>|<floor>|<x,z,x,z...>"
        private void AddRoom(string[] a, string who)
        {
            if (!Enum.TryParse(Get(a, 0), out RoomType type)) return;
            var xz = Get(a, 3).Split(',').Select(Flt).ToList();
            if (xz.Count < 6 || xz.Count % 2 != 0 || !Geometry.Simple(xz)) { TellPlayer(who, "That outline crosses itself."); return; }
            var room = new Room { Id = NewId(), Type = type, God = Get(a, 1), Floor = Flt(Get(a, 2)), Xz = xz, Height = type == RoomType.BurialGround ? 6 : 5 };
            RoomVerdict v = Check(room);
            R.Rooms.Add(room);
            TellPlayer(who, v.Ok ? $"{Tables.Of(type).Name}: grade {v.Grade}." : $"{Tables.Of(type).Name} marked, but not working yet: {room.Failures}.");
            if (v.Ok) Log("room", "", Tables.Of(type).Name.ToLower());
            Rerank();
        }
        private void AddZone(string[] a, string who)
        {
            if (!Enum.TryParse(Get(a, 0), out ZoneType type)) return;
            var xz = Get(a, 2).Split(',').Select(Flt).ToList();
            if (xz.Count < 6 || !Geometry.Simple(xz)) { TellPlayer(who, "That outline crosses itself."); return; }
            var (cx, cz) = Geometry.Centre(xz);
            if (Utils.DistanceXZ(new Vector3(cx, 0, cz), Position) > Jarldom.Renown.ZoneReach) { TellPlayer(who, $"Work zones must be within {Jarldom.Renown.ZoneReach} m of the pillars."); return; }
            R.Zones.Add(new Zone { Id = NewId(), Type = type, Floor = Flt(Get(a, 1)), Xz = xz });
            TellPlayer(who, $"{Tables.Of(type).Name} marked.");
        }

        // A bed claimed by someone who has left is free again.
        public void ReleaseBed(string vid)
        {
            foreach (Bed bed in AllBeds())
            {
                ZNetView v = bed.GetComponent<ZNetView>();
                if (v.GetZDO().GetString(Villagers.ClaimKey, "") != vid) continue;
                if (!v.IsOwner()) v.ClaimOwnership();
                v.GetZDO().Set(Villagers.ClaimKey, ""); v.GetZDO().Set("dhc_home", 0L); v.GetZDO().Set("dhc_homename", "");
            }
        }
        // Give everyone without a bed the nearest free one.
        public void AutoBeds()
        {
            var free = FreeBeds();
            foreach (Brain b in Villagers.All.Where(b => b.S.Colony == R.Id && string.IsNullOrEmpty(b.S.Bed)))
            {
                Bed bed = free.OrderBy(x => Vector3.Distance(x.transform.position, b.Pos)).FirstOrDefault();
                if (bed == null) break;
                free.Remove(bed);
                b.Command("bed", IdOf(bed.GetComponent<ZNetView>()));
            }
        }

        // Everyone's things into a vanilla chest by the pillars, and the colony's people sent away: the way to uninstall safely.
        private void Disband(string who)
        {
            GameObject chestPrefab = ZNetScene.instance.GetPrefab("piece_chest_wood");
            int n = 0;
            foreach (Humanoid h in Villagers.Bodies().Where(h => h.GetComponent<ZNetView>().GetZDO().GetString(Villagers.ColonyKey) == R.Id).ToList())
            {
                h.GetComponent<ZNetView>().ClaimOwnership();
                Inventory bag = h.GetInventory();
                if (bag != null && bag.NrOfItems() > 0 && chestPrefab != null)
                {
                    foreach (ItemDrop.ItemData item in bag.GetAllItems().ToList()) { h.UnequipItem(item, false); item.m_equipped = false; }
                    Vector3 at = Spot(Position + Seat.transform.right * 3, 2f) ?? Position + Seat.transform.right * 3;
                    GameObject chest = UnityEngine.Object.Instantiate(chestPrefab, at, Quaternion.identity);
                    Container box = chest.GetComponent<Container>();
                    Inventory inv = box.GetInventory();
                    foreach (ItemDrop.ItemData item in bag.GetAllItems().ToList()) { if (inv.AddItem(item)) bag.RemoveItem(item); }
                    foreach (ItemDrop.ItemData item in bag.GetAllItems().ToList()) { ItemDrop.DropItem(item, item.m_stack, at + Vector3.up, Quaternion.identity); bag.RemoveItem(item); }
                }
                Brain b = Villagers.Brain(h);
                b?.Release();
                RosterRow row = R.Row(h.GetComponent<ZNetView>().GetZDO().GetString(Villagers.VidKey));
                if (row != null) row.State = SoulState.Dismissed;
                h.GetComponent<ZNetView>().Destroy();
                n++;
            }
            // The fallen waiting to rise give back their kept gear too.
            foreach (Fallen f in R.Fallen.ToList())
            {
                if (f.Gear != null && f.Gear.Length > 0 && chestPrefab != null)
                {
                    var kept = new Inventory("kept", null, VillagerPrefab.BagWidth, VillagerPrefab.BagRows); kept.Load(new ZPackage(f.Gear));
                    for (int tries = 0; kept.NrOfItems() > 0 && tries < 6; tries++)
                    {
                        Inventory inv = UnityEngine.Object.Instantiate(chestPrefab, Spot(Position, 3) ?? Position, Quaternion.identity).GetComponent<Container>().GetInventory();
                        foreach (ItemDrop.ItemData item in kept.GetAllItems().ToList()) if (inv.AddItem(item)) kept.RemoveItem(item);
                    }
                    foreach (ItemDrop.ItemData item in kept.GetAllItems().ToList()) ItemDrop.DropItem(item, item.m_stack, Position + Vector3.up, Quaternion.identity);
                }
                R.Fallen.Remove(f);
            }
            Log("text", who, "", $"{who} disbanded the colony: {n} went their way, and left their things by the pillars.");
        }
    }
}
