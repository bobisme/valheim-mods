using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Shipyard
{
    // The slipway: building stocks at the shore. E lays a keel down, then E puts in what the stage under way still needs from your bag
    // (any player, as much as you have of it); when a stage is all in, it stands on the stocks and the next is begun. Finished, E
    // launches it: the boat goes into the water ahead of the bow end. Taken down, everything put in comes back out on the ground. Where
    // building costs nothing, as for the hammer's pieces: a stage goes up for the asking, and nothing comes back.
    // The build lives in the ZDO (Core/Hulls.cs's Build); what's put in is counted by the slipway's owner, and anything it didn't need
    // (another player got there first) is handed back.
    internal sealed class Slipway : MonoBehaviour, Hoverable, Interactable
    {
        internal const string Key = "shipyard_build";
        // (Models/README: the top of the keel blocks above the slipway's origin; a hull's keel bottom is 0.45 below its waterline origin)
        internal const float BlockTop = 0.55f, KeelDepth = 0.45f;
        private ZNetView _view; private Transform _hull; private string _shown;
        private float _next;

        private void Awake()
        {
            _view = GetComponent<ZNetView>();
            if (_view == null || _view.GetZDO() == null) return;
            foreach (string rpc in new[] { "Shipyard_Pay", "Shipyard_Refund", "Shipyard_Lay", "Shipyard_Launch" }) _view.Unregister(rpc); // (an older copy's, after a hot reload)
            _view.Register<string, int>("Shipyard_Pay", RPC_Pay);
            _view.Register<string, int>("Shipyard_Refund", RPC_Refund);
            _view.Register<string>("Shipyard_Lay", RPC_Lay);
            _view.Register("Shipyard_Launch", RPC_Launch);
            if (GetComponent<WearNTear>() is WearNTear w) w.m_onDestroyed += Dropped;
        }

        private void OnDestroy() { if (GetComponent<WearNTear>() is WearNTear w) w.m_onDestroyed -= Dropped; } // (a hot reload's older copy: its hook goes with it)

        internal Build Now() => _view != null && _view.IsValid() ? Build.Load(_view.GetZDO().GetString(Key)) : new Build();
        private void Keep(Build b) => _view.GetZDO().Set(Key, b.Save());

        private void Update()
        {
            if (Time.time < _next || _view == null || !_view.IsValid()) return;
            _next = Time.time + 0.5f;
            string s = _view.GetZDO().GetString(Key);
            if (s != _shown) { _shown = s; Show(Build.Load(s)); }
        }

        // The stages done, standing on the stocks.
        private void Show(Build b)
        {
            if (_hull != null) Destroy(_hull.gameObject);
            _hull = null;
            if (b.Empty || b.Stage == 0) return;
            _hull = new GameObject("hull").transform; _hull.SetParent(transform, false);
            _hull.localPosition = new Vector3(0, BlockTop + KeelDepth, 0);
            for (int i = 0; i < Math.Min(b.Stage, b.Hull.Stages.Length); i++)
                if (Looks.Add(_hull, ZNetScene.instance, b.Hull.Stages[i].Model, b.Hull.Stages[i].Name) is GameObject part)
                    foreach (Transform t in part.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = gameObject.layer;
        }

        // ---- looking and E ----
        public string GetHoverName() => "Slipway";
        public float GetHoverOffset() => 0;
        public string GetHoverText()
        {
            Build b = Now();
            string text = "Slipway";
            if (b.Empty) return Localization.instance.Localize(text + "\n[<color=yellow><b>$KEY_Use</b></color>] Lay a keel: " + Hulls.All[0].Title.ToLower());
            text += $"\n{b.Hull.Title}";
            if (b.Finished) return Localization.instance.Localize(text + ", finished\n[<color=yellow><b>$KEY_Use</b></color>] Launch her");
            text += $": {b.Current.Title.ToLower()} (stage {b.Stage + 1} of {b.Hull.Stages.Length})";
            text += "\nNeeds " + string.Join(", ", b.Needs().Select(n => $"{n.n} {ItemName(n.item)}" + (b.Had(n.item) > 0 ? $" ({b.Had(n.item)} in)" : "")));
            return Localization.instance.Localize(text + "\n[<color=yellow><b>$KEY_Use</b></color>] " + (Player.m_localPlayer is Player p && Free(p) ? "Build it" : "Put in from your bag"));
        }
        internal static string ItemName(string prefab)
        {
            string n = ObjectDB.instance?.GetItemPrefab(prefab)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name;
            return n == null ? prefab : Localization.instance.Localize(n).ToLower();
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !(user is Player p) || _view == null || !_view.IsValid()) return false;
            Build b = Now();
            if (b.Empty) { _view.InvokeRPC("Shipyard_Lay", Hulls.All[0].Name); return true; }
            if (b.Finished) { _view.InvokeRPC("Shipyard_Launch"); return true; }
            if (Free(p)) { foreach (var (item, n) in b.Needs()) _view.InvokeRPC("Shipyard_Pay", item, n); return true; } // (as the hammer builds there: for nothing)
            int gave = Give(p, b);
            if (gave == 0) p.Message(MessageHud.MessageType.Center, "You have none of what it needs: " + string.Join(", ", b.Needs().Select(n => $"{n.n} {ItemName(n.item)}")));
            return true;
        }
        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

        // A world where building costs nothing (or the no-cost cheat): stages go up for nothing, and taking it down gives nothing back.
        private static bool FreeWorld() => ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(GlobalKeys.NoBuildCost);
        private static bool Free(Player p) => FreeWorld() || p.NoCostCheat();

        // What the stage still needs, out of the player's bag and sent to the owner to count. How many things went.
        internal int Give(Player p, Build b)
        {
            Inventory inv = p.GetInventory(); int gave = 0;
            foreach (var (item, n) in b.Needs())
            {
                string name = ObjectDB.instance?.GetItemPrefab(item)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name;
                if (name == null) continue;
                int k = Math.Min(n, inv.CountItems(name));
                if (k <= 0) continue;
                inv.RemoveItem(name, k);
                _view.InvokeRPC("Shipyard_Pay", item, k);
                gave += k;
            }
            return gave;
        }

        // ---- the owner's side ----
        private void RPC_Lay(long sender, string hull)
        {
            if (!_view.IsOwner()) return;
            Build b = Now();
            if (!b.Empty || Hulls.Of(hull) == null) return;
            Keep(new Build { Hull = Hulls.Of(hull) });
        }
        private void RPC_Pay(long sender, string item, int n)
        {
            if (!_view.IsOwner()) return;
            Build b = Now(); int stage = b.Stage;
            int took = b.Take(new[] { (item, n) }).Where(x => x.item == item).Sum(x => x.n);
            Keep(b);
            if (took < n) _view.InvokeRPC(sender, "Shipyard_Refund", item, n - took); // (another got there first: back to the giver)
            if (b.Stage > stage && !b.Finished) Tell(sender, $"{b.Hull.Stages[stage].Title} done. Next: {b.Current.Title.ToLower()}");
            else if (b.Stage > stage) Tell(sender, $"The {b.Hull.Title.ToLower()} is finished: launch her");
        }
        private void Tell(long to, string text) => ZRoutedRpc.instance.InvokeRoutedRPC(to, "ShowMessage", (int)MessageHud.MessageType.Center, text);
        private void RPC_Refund(long sender, string item, int n)
        {
            Player p = Player.m_localPlayer;
            GameObject prefab = ObjectDB.instance?.GetItemPrefab(item);
            if (p == null || prefab == null || n <= 0) return;
            if (!p.GetInventory().AddItem(prefab, n)) Drop(prefab, n, p.transform.position + Vector3.up);
        }
        private void RPC_Launch(long sender)
        {
            if (!_view.IsOwner()) return;
            Build b = Now();
            if (!b.Finished) return;
            if (!(Where(b.Hull) is Vector3 at)) { Tell(sender, $"No water deep enough within {Launching.Reach:0} m ahead of the bow end"); return; }
            GameObject boat = ZNetScene.instance.GetPrefab(b.Hull.Prefab);
            if (boat == null) { Tell(sender, "The game doesn't know the " + b.Hull.Title.ToLower()); return; }
            Object.Instantiate(boat, at, transform.rotation);
            Keep(new Build());
            Tell(sender, $"The {b.Hull.Title.ToLower()} is launched");
        }
        // Where the boat goes in: ahead along the slipway (its +z), at the water's surface where it's deep enough.
        internal Vector3? Where(Hull h)
        {
            float sea = ZoneSystem.instance.m_waterLevel;
            Vector3 fwd = transform.forward; fwd.y = 0; fwd.Normalize();
            float? d = Launching.Spot(h, m =>
            {
                Vector3 p = transform.position + fwd * m;
                return sea - ZoneSystem.instance.GetGroundHeight(p);
            });
            if (d == null) return null;
            Vector3 at = transform.position + fwd * (d.Value + 0.5f); // (a little further: the stern clear of the shallows)
            at.y = sea + 0.1f;
            return at;
        }

        // Taken down: everything put in comes back out (on the owner's game: the piece's last owner drops it).
        private void Dropped()
        {
            if (_view == null || !_view.IsValid() || !_view.IsOwner() || FreeWorld()) return;
            foreach (var kv in Now().Refund())
                if (ObjectDB.instance?.GetItemPrefab(kv.Key) is GameObject prefab) Drop(prefab, kv.Value, transform.position + Vector3.up);
        }
        private static void Drop(GameObject prefab, int n, Vector3 at)
        {
            int max = Math.Max(1, prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize);
            while (n > 0)
            {
                int k = Math.Min(n, max); n -= k;
                GameObject go = Object.Instantiate(prefab, at + UnityEngine.Random.insideUnitSphere * 0.5f, Quaternion.identity);
                go.GetComponent<ItemDrop>().SetStack(k);
            }
        }
    }
}
