using System;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jarldom
{
    // Moving or rebuilding the pillars: when they come down (taken down with the hammer, or broken in a raid) they leave the heart of the
    // high seat, an item holding the colony's record and job board. Raising new pillars while carrying it brings that colony back there,
    // with the same id, so its villagers, rooms and saga carry on.
    internal static class Relic
    {
        public const string RecordKey = "jarldom_colony", BoardKey = "jarldom_board", IdKey = "jarldom_colony_id", NameKey = "jarldom_colony_name";
        public static string ColonyName(ItemDrop.ItemData item) => item != null && item.m_customData.TryGetValue(NameKey, out string n) ? n : "";

        // Taken down or broken: the heart drops where they stood (on the pillars' owner's game, once).
        [HarmonyPatch(typeof(WearNTear), "Destroy")]
        private static class Fallen
        {
            private static void Prefix(WearNTear __instance, out Saved __state)
            {
                __state = null;
                // A Jarldom piece made before its model was rebuilt (a reload) points at fragment parts that are gone: break into its own look.
                if (__instance.m_fragmentRoots != null && __instance.name.StartsWith("Jarldom") && __instance.m_fragmentRoots.Any(g => g == null)) __instance.m_fragmentRoots = null;
                HighSeat seat = __instance.GetComponent<HighSeat>();
                ZNetView v = __instance.GetComponent<ZNetView>();
                if (seat == null || v == null || !v.IsValid() || !v.IsOwner() || seat.Colony?.R == null) return;
                Colony c = seat.Colony;
                Saved saved = null;
                Plugin.Guard(() =>
                {
                    c.GatherSaga(); c.MarkDirty(); c.Flush();
                    byte[] record = c.R.Save(withLog: true); // (the whole of it, saga and all: the saga's own object goes with the pillars)
                    if (record != null && record.Length > 0) saved = new Saved { Colony = c, Id = c.R.Id, Name = c.R.Name, Record = record, Board = c.BoardBytes(), At = seat.transform.position };
                });
                __state = saved;
            }
            // Only once they're really down (a removal that fails leaves the pillars standing, and no second colony in a heart).
            private static void Postfix(Saved __state) { if (__state != null) Plugin.Guard(() => Drop(__state)); }
        }
        private sealed class Saved { public Colony Colony; public string Id, Name; public byte[] Record, Board; public Vector3 At; }
        private static void Drop(Saved s)
        {
            GameObject prefab = Items.Relic;
            if (prefab == null) { Plugin.Warn("Jarldom: no heart of the high seat to drop: the colony record is lost with the pillars"); return; }
            GameObject go = Object.Instantiate(prefab, s.At + Vector3.up * 1.2f, Quaternion.identity);
            ItemDrop drop = go.GetComponent<ItemDrop>();
            drop.m_autoDestroy = false;
            drop.m_itemData.m_customData[RecordKey] = Convert.ToBase64String(s.Record);
            if (s.Board != null && s.Board.Length > 0) drop.m_itemData.m_customData[BoardKey] = Convert.ToBase64String(s.Board);
            drop.m_itemData.m_customData[IdKey] = s.Id;
            drop.m_itemData.m_customData[NameKey] = s.Name;
            ItemDrop.SaveToZDO(drop.m_itemData, go.GetComponent<ZNetView>().GetZDO());
            Assets.Effect("vfx_offering", go.transform.position);
            s.Colony.Tell($"The pillars of {s.Name} are down. Their heart lies where they stood: carry it to new High-seat pillars to raise the colony again.", true);
            Plugin.Log($"Jarldom: the pillars of {s.Id} came down; its heart was dropped at {go.transform.position}");
        }

        // Raising pillars with a heart in the bag: the next pillars this game raises take its colony (Colony.Attach asks).
        private static ItemDrop.ItemData _pending; private static float _pendingUntil; private static Vector3 _pendingAt;
        public static void Placing(Player p, Vector3 at)
        {
            _pending = p.GetInventory().GetAllItems().FirstOrDefault(Items.IsRelic);
            _pendingUntil = Time.time + 3f; _pendingAt = at;
        }
        // From Colony.Attach on new pillars without a record (their owner's game).
        public static bool Take(Vector3 at, out byte[] record, out byte[] board)
        {
            record = null; board = null;
            ItemDrop.ItemData item = _pending; _pending = null;
            Player p = Player.m_localPlayer;
            if (item == null || p == null || Time.time > _pendingUntil || Vector3.Distance(at, _pendingAt) > 3f || !p.GetInventory().ContainsItem(item)) return false;
            if (!item.m_customData.TryGetValue(RecordKey, out string r)) return false;
            try { record = Convert.FromBase64String(r); if (item.m_customData.TryGetValue(BoardKey, out string bd)) board = Convert.FromBase64String(bd); }
            catch { return false; }
            if (item.m_customData.TryGetValue(IdKey, out string id) && Colony.Of(id) != null) { p.Message(MessageHud.MessageType.Center, "That colony stands already."); record = null; return false; }
            p.GetInventory().RemoveItem(item);
            return true;
        }
    }
}
