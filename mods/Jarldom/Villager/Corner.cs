using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Jarldom
{
    // A villager's own corner: a keepsake of theirs (a trophy or a precious thing the jarl brought them for a request) hung on a
    // player-built item stand within 4 m of their bed; two at most. Kept in their bag until then, never carried to the stores.
    internal sealed class CornerTask : Task
    {
        public const string KeepKey = "jarldom_keepsake", StandKey = "jarldom_corner";
        private const float Reach = 4f; private const int Most = 2;
        private static readonly string[] Stands = { "itemstand", "itemstandh" };
        private static readonly MethodInfo CanAttach = AccessTools.Method(typeof(ItemStand), "CanAttach"), Orientation = AccessTools.Method(typeof(ItemStand), "GetOrientation");
        private readonly ItemStand _stand; private readonly ItemDrop.ItemData _item;
        private CornerTask(ItemStand stand, ItemDrop.ItemData item) { _stand = stand; _item = item; Doing = "hanging up something of their own"; }

        public static bool Keepsake(ItemDrop.ItemData i) => i?.m_customData != null && i.m_customData.ContainsKey(KeepKey);
        public static void Mark(ItemDrop.ItemData i, string why) { if (i != null) i.m_customData[KeepKey] = why; }
        public static Task Next(Brain b)
        {
            Colony c = b.Colony;
            if (c == null || Time.time < b.NoCornerUntil) return null;
            b.NoCornerUntil = Time.time + 60f;
            ItemDrop.ItemData keep = b.Body.GetInventory().GetAllItems().FirstOrDefault(Keepsake);
            Bed bed = keep != null ? c.BedById(b.S.Bed) : null;
            if (bed == null) return null;
            var stands = c.PiecesIn(c.Radius).Where(p => p != null && Stands.Contains(Colony.NameOf(p)) && p.GetCreator() != 0 && Vector3.Distance(p.transform.position, bed.transform.position) < Reach)
                .Select(p => p.GetComponent<ItemStand>()).Where(s => s != null && s.GetComponent<ZNetView>()?.GetZDO() != null).ToList();
            if (stands.Count(s => s.HaveAttachment() && s.GetComponent<ZNetView>().GetZDO().GetString(StandKey, "") == b.S.Vid) >= Most) return null;
            ItemStand free = stands.Where(s => !s.HaveAttachment() && Fits(s, keep)).OrderBy(s => Vector3.Distance(s.transform.position, bed.transform.position)).FirstOrDefault();
            return free != null ? new CornerTask(free, keep) : null;
        }
        private static bool Fits(ItemStand s, ItemDrop.ItemData i) { try { return CanAttach != null && (bool)CanAttach.Invoke(s, new object[] { i }); } catch { return false; } }

        public override void Start(Brain b) => b.Move.To(_stand.transform.position, 1.8f);
        public override void Tick(Brain b)
        {
            if (_stand == null || _stand.HaveAttachment() || !b.Body.GetInventory().ContainsItem(_item)) { Done = true; return; }
            Stock.Walk w = Stock.WalkNear(b, _stand.transform.position, 2.4f, 1.8f);
            if (w == Stock.Walk.Unreachable) { Done = true; b.NoCornerUntil = Time.time + 600f; return; }
            if (w != Stock.Walk.Reached) return;
            // (as the game hangs a player's: the stand's owner writes the item to it and everyone is told to show it)
            ZNetView view = _stand.GetComponent<ZNetView>();
            if (!view.IsOwner()) view.ClaimOwnership();
            ItemDrop.ItemData one = _item.Clone(); one.m_stack = 1;
            int hash = _item.m_dropPrefab.name.GetStableHashCode();
            int turn = 0; try { turn = Orientation != null ? (int)Orientation.Invoke(_stand, null) : 0; } catch { }
            ZDO z = view.GetZDO();
            z.Set(ZDOVars.s_item, hash);
            ItemDrop.SaveToZDO(one, z);
            z.Set(StandKey, b.S.Vid);
            view.InvokeRPC(ZNetView.Everybody, "SetVisualItem", hash, one.m_variant, one.m_quality, turn);
            b.Body.GetInventory().RemoveOneItem(_item);
            Body_.Anim(b.Body)?.SetTrigger("interact");
            string what = Localization.instance.Localize(_item.m_shared.m_name).ToLower();
            b.Say(new[] { $"There. The {what}, where I can see it.", $"My {what}. It goes here, by my bed.", $"Now the {what} has a place of its own.", $"Something of mine, by my bed: the {what}." }[Random.Range(0, 4)], 5);
            Mood.Add(b.S, "corner", b.Now); b.Dirty();
            Done = true;
        }
    }
}
