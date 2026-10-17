using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Bathhouse
{
    // The bath locker: E puts away everything worn (helmet, armour, cape, belt, what's in hand) into it, E again puts it all back on.
    // Shift+E opens it as the chest it is. The things are kept in the locker's own inventory, so they're as safe as in any chest (saved
    // with the world, dropped if it's broken); whose they are is kept on it, and nobody else's E or Shift+E gets at them while they're in.
    internal static class Locker
    {
        public const string ByKey = "bathhouse_locker_by", NameKey = "bathhouse_locker_name", CellsKey = "bathhouse_locker_cells", Rpc = "Bathhouse_Locker", Answer = "Bathhouse_LockerAnswer";
        internal static bool Is(Container c) => c != null && Utils.GetPrefabName(c.gameObject) == Kit.Locker;

        internal static long By(Container c) => c.GetComponent<ZNetView>() is ZNetView v && v.IsValid() ? v.GetZDO().GetLong(ByKey, 0L) : 0;
        private static string Whose(Container c) => c.GetComponent<ZNetView>()?.GetZDO()?.GetString(NameKey, "") is string s && s != "" ? s : "Someone";
        private static long Me => Game.instance != null ? Game.instance.GetPlayerProfile().GetPlayerID() : 0;
        // (a locker holding what another put away; things put in by Shift+E, by nobody in particular, are anybody's)
        private static bool Others(Container c) => By(c) != 0 && By(c) != Me && c.GetInventory().NrOfItems() > 0;

        // Its RPCs, on each locker as it loads: the one asked (the locker's owner) hands it over, as the game's chest does to open it.
        internal static void Register(Container c)
        {
            ZNetView v = c.GetComponent<ZNetView>();
            if (v == null || !v.IsValid()) return;
            v.Register<long>(Rpc, (uid, player) =>
            {
                if (!v.IsOwner()) return;
                bool ok = !(c.IsInUse() && uid != ZNet.GetUID());
                if (ok) { ZDOMan.instance.ForceSendZDO(uid, v.GetZDO().m_uid); v.GetZDO().SetOwner(uid); }
                v.InvokeRPC(uid, Answer, ok);
            });
            v.Register<bool>(Answer, (uid, ok) =>
            {
                if (!(Player.m_localPlayer is Player p)) return;
                if (!ok) { p.Message(MessageHud.MessageType.Center, "$msg_inuse"); return; }
                v.ClaimOwnership();
                if (By(c) == Me && c.GetInventory().NrOfItems() > 0) Dress(c, p); else Strip(c, p);
            });
        }

        private static void Strip(Container c, Player p)
        {
            Inventory bag = p.GetInventory(), box = c.GetInventory();
            var worn = bag.GetEquippedItems().ToList();
            if (worn.Count == 0) { p.Message(MessageHud.MessageType.Center, "You've nothing on to put away."); return; }
            int n = 0; var cells = new System.Collections.Generic.List<string>();
            foreach (ItemDrop.ItemData i in worn)
            {
                if (!box.HaveEmptySlot()) break;
                Vector2i at = i.m_gridPos; // (where it was: the hotbar, or another mod's gear slot, it goes back to)
                p.UnequipItem(i, false);
                if (box.AddItem(i)) { bag.RemoveItem(i); n++; cells.Add($"{i.m_shared.m_name}|{i.m_quality}|{at.x}|{at.y}"); } else p.EquipItem(i, false);
            }
            ZDO z = c.GetComponent<ZNetView>().GetZDO();
            z.Set(ByKey, Me); z.Set(NameKey, p.GetPlayerName()); z.Set(CellsKey, string.Join(";", cells));
            string left = n < worn.Count ? $" ({worn.Count - n} wouldn't fit)" : "";
            p.Message(MessageHud.MessageType.Center, $"You strip down and put your things in the locker{left}.");
            Plugin.Log($"Locker: put away {n} things");
        }

        private static void Dress(Container c, Player p)
        {
            Inventory bag = p.GetInventory(), box = c.GetInventory();
            int n = 0, stuck = 0;
            var cells = (c.GetComponent<ZNetView>().GetZDO().GetString(CellsKey, "") ?? "").Split(';').Select(x => x.Split('|')).Where(x => x.Length == 4).ToList();
            foreach (ItemDrop.ItemData i in box.GetAllItems().ToList())
            {
                string name = i.m_shared.m_name; int quality = i.m_quality;
                // (back to the cell it came from, if that's free: a gear slot of another mod's isn't one the bag would choose)
                string[] cell = cells.FirstOrDefault(x => x[0] == name && x[1] == quality.ToString());
                if (cell != null) cells.Remove(cell);
                int cx = -1, cy = -1;
                bool back = cell != null && int.TryParse(cell[2], out cx) && int.TryParse(cell[3], out cy) && cx >= 0 && cy >= 0 && cx < bag.GetWidth() && cy < bag.GetHeight()
                    && bag.GetItemAt(cx, cy) == null && bag.MoveItemToThis(box, i, i.m_stack, cx, cy);
                if (!back)
                {
                    if (!bag.CanAddItem(i) || !bag.AddItem(i)) { stuck++; continue; }
                    box.RemoveItem(i);
                }
                n++;
                // (put in its cell, it's a copy there; a stack of arrows may join one in the bag: the one to put on is whichever holds them now)
                ItemDrop.ItemData mine = back ? bag.GetItemAt(cx, cy) : bag.GetAllItems().Contains(i) ? i : bag.GetAllItems().FirstOrDefault(x => x.m_shared.m_name == name && x.m_quality == quality);
                if (mine != null && mine.IsEquipable() && !p.IsItemEquiped(mine)) p.EquipItem(mine, false);
            }
            if (box.NrOfItems() == 0) { ZDO z = c.GetComponent<ZNetView>().GetZDO(); z.Set(ByKey, 0L); z.Set(NameKey, ""); z.Set(CellsKey, ""); }
            p.Message(MessageHud.MessageType.Center, stuck > 0 ? $"You dress again, but {stuck} things won't fit in your bag: they're still in the locker." : "You dress again.");
            Plugin.Log($"Locker: put back on {n} things ({stuck} left)");
        }

        internal static bool Use(Container c, Player p)
        {
            if (Others(c)) { p.Message(MessageHud.MessageType.Center, $"{Whose(c)}'s things are in this locker."); return false; }
            c.GetComponent<ZNetView>().InvokeRPC(Rpc, Me);
            return true;
        }

        [HarmonyPatch(typeof(Container), "Awake")]
        private static class Wake { private static void Postfix(Container __instance) { if (Is(__instance)) Register(__instance); } }

        [HarmonyPatch(typeof(Container), nameof(Container.Interact))]
        private static class Press
        {
            private static bool Prefix(Container __instance, Humanoid character, bool hold, bool alt, ref bool __result)
            {
                if (!Is(__instance)) return true;
                __result = false;
                if (hold || !(character is Player p) || p != Player.m_localPlayer) return false;
                if (alt)
                {
                    // (opened as a chest: but not to get at another's things)
                    if (Others(__instance)) { p.Message(MessageHud.MessageType.Center, $"{Whose(__instance)}'s things are in this locker."); return false; }
                    return true;
                }
                __result = Use(__instance, p);
                return false;
            }
        }

        [HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
        private static class Hover
        {
            private static void Postfix(Container __instance, ref string __result)
            {
                if (!Is(__instance)) return;
                string what;
                if (Others(__instance)) what = $"\n<color=#c0c0c0>{Whose(__instance)}'s things are in it.</color>";
                else if (By(__instance) == Me && __instance.GetInventory().NrOfItems() > 0) what = "\n[<color=yellow><b>$KEY_Use</b></color>] Dress again";
                else what = "\n[<color=yellow><b>$KEY_Use</b></color>] Strip down: put away all you wear";
                __result = Localization.instance.Localize("Bath locker" + what + "\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] Open");
            }
        }
    }
}
