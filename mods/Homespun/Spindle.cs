using HarmonyLib;
using UnityEngine;

namespace Homespun
{
    // The drop spindle: held in the hand, it spins whatever fibre the bag has (line flax first, then wool, then Byre's fleece), as you
    // stand or walk: a thread or a yarn every minute or so (Core/Chain.cs). Spinning is this game's player's alone (their own bag), so
    // nothing goes over the network. The fibre is taken only when the thread is done and there's room for it.
    internal static class Spindle
    {
        internal static float Spun;              // (seconds into the one being spun)
        internal static Conversion On;           // (what's being spun: null when idle)
        private static float _told;

        internal static bool Holding(Player p) => p != null && p.GetCurrentWeapon() is ItemDrop.ItemData i && i.m_dropPrefab != null && i.m_dropPrefab.name == Names.Spindle;

        internal static void Tick()
        {
            Player p = Player.m_localPlayer;
            if (!Holding(p) || p.IsDead() || p.InWater() && p.IsSwimming() || p.IsSleeping() || p.InAttack() || p.IsAttached() || p.InPlaceMode())
            {
                if (On != null && !Holding(p)) { On = null; Spun = 0; }
                return;
            }
            Inventory bag = p.GetInventory();
            Conversion next = Chain.SpindleNext(n => bag.CountItems(Things.Get(n)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name ?? Fleece(n)));
            if (next == null)
            {
                if (On != null || Time.time - _told > 30) { _told = Time.time; p.Message(MessageHud.MessageType.TopLeft, "Nothing to spin: line flax or wool"); }
                On = null; Spun = 0;
                return;
            }
            if (next != On) { On = next; Spun = 0; p.Message(MessageHud.MessageType.TopLeft, $"Spinning {Title(next.From)}"); }
            bool done; (Spun, done) = Chain.Spin(Spun, Time.deltaTime, next);
            if (!done) return;
            GameObject made = Things.Get(next.To) ?? ZNetScene.instance.GetPrefab(next.To);
            ItemDrop.ItemData fibre = bag.GetItem(Shared(next.From));
            if (made == null || fibre == null) return;
            if (!bag.CanAddItem(made, next.Yield)) { p.Message(MessageHud.MessageType.Center, "No room in your bag for what's spun"); Spun = next.Seconds; return; }
            bag.RemoveItem(fibre, 1);
            bag.AddItem(made, next.Yield);
            p.Message(MessageHud.MessageType.TopLeft, $"Spun {next.Yield} {Title(next.To)}");
        }
        private static string Shared(string prefab) => Things.Get(prefab)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name ?? Fleece(prefab);
        private static string Fleece(string prefab) => ZNetScene.instance?.GetPrefab(prefab)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name ?? "";
        internal static string Title(string prefab) => Localization.instance != null ? Localization.instance.Localize(Shared(prefab)) : Shared(prefab);
    }

    // A spindle isn't a club: its attack buttons do nothing.
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    internal static class SpindleNoAttack
    {
        private static bool Prefix(Humanoid __instance, ref bool __result)
        {
            if (!(__instance is Player p) || p != Player.m_localPlayer || !Spindle.Holding(p)) return true;
            __result = false;
            return false;
        }
    }
}
