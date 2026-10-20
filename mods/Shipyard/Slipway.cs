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
        private int _choice; // (the hull a keel would be laid for: Shift+E goes through them; this player's own, not the world's)
        private Hull Chosen => Hulls.All[Math.Abs(_choice) % Hulls.All.Length];
        private float _next;

        internal static readonly List<Slipway> All = new List<Slipway>();
        internal const float RepairReach = 30f; // (a ship this near a slipway is in the shipyard: the hammer mends her there, no workbench needed)
        internal static bool Near(Vector3 at, float reach) => All.Any(s => s != null && Vector3.Distance(s.transform.position, at) <= reach);
        private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        private void OnDisable() => All.Remove(this);

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
            Sails.Bind(); // (Homespun's sails, if it's in: looked for every few seconds)
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
            _hull.localPosition = new Vector3(0, BlockTop + b.Hull.KeelDepth, 0);
            var parts = new List<(string model, string name, Vector3 at)>();
            if (b.Hull.Cradle != null) parts.Add((b.Hull.Cradle, "cradle", Vector3.zero));
            for (int i = 0; i < Math.Min(b.Stage, b.Hull.Stages.Length); i++) parts.Add((b.Hull.Stages[i].Model, b.Hull.Stages[i].Name, Vector3.zero));
            if (b.Finished && b.Hull.Yard != null) parts.Add((b.Hull.Yard, "yard", new Vector3(0, 0, b.Hull.MastZ))); // (the sail furled on its yard, up)
            foreach (var (model, name, at) in parts)
                if (Looks.Add(_hull, ZNetScene.instance, model, name) is GameObject part)
                {
                    part.transform.localPosition = at;
                    if (name == "yard") Sails.Paint(part, model, b.Sail); // (furled, in the colour of the sail put in)
                    foreach (Transform t in part.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = gameObject.layer;
                }
        }

        // ---- looking and E ----
        public string GetHoverName() => "Slipway";
        public float GetHoverOffset() => 0;
        public string GetHoverText()
        {
            Build b = Now();
            string text = "Slipway";
            if (b.Empty)
                return Localization.instance.Localize(text + "\n[<color=yellow><b>$KEY_Use</b></color>] Lay a keel: " + Chosen.Title.ToLower() + " (" + Costs(Chosen) + ")"
                    + (Hulls.All.Length > 1 ? "\n[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] Another hull" : ""));
            text += $"\n{b.Hull.Title}";
            if (b.Finished) return Localization.instance.Localize(text + ", finished\n[<color=yellow><b>$KEY_Use</b></color>] Launch her");
            text += $": {b.Current.Title.ToLower()} (stage {b.Stage + 1} of {b.Hull.Stages.Length})";
            text += "\nNeeds " + string.Join(", ", b.Needs().Select(n => $"{n.n} {ItemName(n.item)}" + (b.Had(n.item) > 0 ? $" ({b.Had(n.item)} in)" : "")));
            return Localization.instance.Localize(text + "\n[<color=yellow><b>$KEY_Use</b></color>] " + (Player.m_localPlayer is Player p && Free(p) ? "Build it" : "Put in from your bag"));
        }
        private static string Costs(Hull h) => string.Join(", ", Hulls.Total(h).Select(kv => $"{kv.Value} {ItemName(kv.Key)}")) + $" in {h.Stages.Length} stages";
        internal static string ItemName(string prefab, bool raw = false)
        {
            string n = ObjectDB.instance?.GetItemPrefab(prefab)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name;
            return raw ? n ?? "" : n == null ? prefab : Localization.instance.Localize(n).ToLower();
        }

        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !(user is Player p) || _view == null || !_view.IsValid()) return false;
            Build b = Now();
            if (b.Empty && alt) { _choice++; return true; }
            if (b.Empty) { _view.InvokeRPC("Shipyard_Lay", Chosen.Name); return true; }
            if (b.Finished) { _view.InvokeRPC("Shipyard_Launch"); return true; }
            if (Free(p))
            {
                // (as the hammer builds there: for nothing; a sail in the bag gives its colour, and stays in the bag)
                foreach (var (item, n) in b.Needs())
                    _view.InvokeRPC("Shipyard_Pay", item == Sailcloth.Sail ? Sailcloth.Variants.FirstOrDefault(v => p.GetInventory().CountItems(ItemName(v, raw: true)) > 0) ?? item : item, n);
                return true;
            }
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
            foreach (var (need, count) in b.Needs())
            {
                int n = count;
                // (the sail: one of any colour; the first in the bag)
                foreach (string item in need == Sailcloth.Sail ? Sailcloth.Variants.Prepend(need).Distinct() : new[] { need })
                {
                    string name = ObjectDB.instance?.GetItemPrefab(item)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name;
                    if (name == null || n <= 0) continue;
                    int k = Math.Min(n, inv.CountItems(name));
                    if (k <= 0) continue;
                    inv.RemoveItem(name, k);
                    _view.InvokeRPC("Shipyard_Pay", item, k);
                    gave += k; n -= k;
                }
            }
            return gave;
        }

        // ---- for other mods (Api.cs): the same rules, worked on this game (it takes the slipway over first) ----
        internal bool Lay(string hull)
        {
            if (_view == null || !_view.IsValid() || Hulls.Of(hull) == null || !Now().Empty) return false;
            if (!_view.IsOwner()) _view.ClaimOwnership();
            Keep(new Build { Hull = Hulls.Of(hull) });
            return true;
        }
        internal int Put(string item, int n)
        {
            if (_view == null || !_view.IsValid() || n <= 0) return 0;
            if (!_view.IsOwner()) _view.ClaimOwnership();
            Build b = Now();
            int took = b.Take(new[] { (item, n) }).Where(x => x.item == item).Sum(x => x.n);
            if (took > 0) Keep(b);
            return took;
        }
        internal bool LaunchNow()
        {
            if (_view == null || !_view.IsValid() || !Now().Finished || !(Where(Now().Hull) is Vector3)) return false;
            if (!_view.IsOwner()) _view.ClaimOwnership();
            RPC_Launch(0);
            return Now().Empty;
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
        private void Tell(long to, string text) { if (to != 0) ZRoutedRpc.instance.InvokeRoutedRPC(to, "ShowMessage", (int)MessageHud.MessageType.Center, text); }
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
            if (!(Where(b.Hull) is Vector3 at)) { Tell(sender, $"No water deep enough for her within {Launching.Reach(b.Hull):0} m ahead of the bow end"); return; }
            GameObject boat = ZNetScene.instance.GetPrefab(b.Hull.Prefab);
            if (boat == null) { Tell(sender, "The game doesn't know the " + b.Hull.Title.ToLower()); return; }
            GameObject launched = Object.Instantiate(boat, at, transform.rotation);
            if (b.Sail != null) launched.GetComponent<ZNetView>()?.GetZDO()?.Set(Sails.Key, b.Sail); // (she sails under the sail put in)
            Keep(new Build());
            Tell(sender, $"The {b.Hull.Title.ToLower()} is launched");
        }
        // Where the boat goes in: ahead along the slipway (its +z), at the water's surface where it's deep enough.
        internal Vector3? Where(Hull h)
        {
            float sea = ZoneSystem.instance.m_waterLevel;
            Vector3 fwd = transform.forward; fwd.y = 0; fwd.Normalize();
            // (water with a boat already in it is no place to put another: as if dry; a boat is its hull, what's stood on and bumped)
            int boats = LayerMask.GetMask("vehicle");
            float? d = Launching.Spot(h, m =>
            {
                Vector3 p = transform.position + fwd * m;
                if (Physics.CheckCapsule(new Vector3(p.x, sea - 1f, p.z), new Vector3(p.x, sea + 2f, p.z), h.Length > 8 ? 2.6f : 1.0f, boats, QueryTriggerInteraction.Ignore)) return 0;
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

    // Ships are mended (and broken up) with the hammer at a slipway as at a workbench: the game asks for the ship's crafting station in
    // reach of the player; a slipway near the ship does as well.
    [HarmonyLib.HarmonyPatch(typeof(Player), "CheckCanRemovePiece")]
    internal static class RepairAtSlipway
    {
        private static bool Prefix(Piece piece, ref bool __result)
        {
            if (piece == null || piece.GetComponent<Ship>() == null || !Slipway.Near(piece.transform.position, Slipway.RepairReach)) return true;
            __result = true;
            return false;
        }
    }
}
