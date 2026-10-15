using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jarldom.Ui
{
    // What a chest holds, chosen at the chest: with the hammer's "Mark rooms and zones" chosen, the colony chest under the crosshair
    // gets a label saying what it's for, and Shift+E asks: the treasury, a kind of stores, or nothing of the colony's. (In build mode
    // the game looks at nothing, no hover and no E: the chest is found here.)
    internal static class ChestPicker
    {
        private static RectTransform _window; private static Colony _c; private static Container _chest;
        public static bool IsOpen => _window != null;
        public static void Close() { if (_window != null) Object.Destroy(_window.gameObject); _window = null; }
        public static void Tick()
        {
            if (_window == null) return;
            if (Player.m_localPlayer == null || global::Menu.IsVisible() || _c == null || !_c.Valid || _chest == null) Close();
        }

        // The chest under the crosshair, within reach (what the game's own look would find out of build mode).
        private static int _mask = -1;
        public static Container Looked(Player p)
        {
            if (GameCamera.instance == null) return null;
            if (_mask == -1) _mask = LayerMask.GetMask("piece", "piece_nonsolid", "Default", "static_solid", "terrain", "vehicle");
            Transform cam = GameCamera.instance.transform;
            if (!Physics.Raycast(cam.position, cam.forward, out RaycastHit hit, 50f, _mask, QueryTriggerInteraction.Ignore)) return null;
            if (Vector3.Distance(p.m_eye.position, hit.point) > 6f) return null;
            return hit.collider.GetComponentInParent<Container>();
        }
        private static GameObject _label; private static Container _labelled; private static string _labelText = "";
        // The label over the chest looked at, kept up while it's looked at (and changed when what it's for changes).
        public static void Hint(Container chest)
        {
            string text = "";
            if (chest != null && Of(chest, out _, out _, out string cat)) text = $"{Name(cat)}\n<size=70%>Shift+E: choose what it's for</size>";
            else if (chest != null) text = "not one of the colony's chests";
            if (chest == _labelled && text == _labelText && (_label != null) == (text != "")) return;
            if (_label != null) Object.Destroy(_label);
            _label = null; _labelled = chest; _labelText = text;
            if (text == "") return;
            Vector3 top = chest.transform.position + Vector3.up * 1.4f;
            foreach (Collider col in chest.GetComponentsInChildren<Collider>()) if (!col.isTrigger) top.y = Mathf.Max(top.y, col.bounds.max.y + 0.5f);
            _label = Overlay_.Label(top, text, new Color(1f, 0.85f, 0.45f, 1f), 2.4f);
        }
        // A fire under the crosshair, within reach: the colony's beacon, or one that could be.
        public static Fireplace LookedFire(Player p)
        {
            if (GameCamera.instance == null) return null;
            if (_mask == -1) _mask = LayerMask.GetMask("piece", "piece_nonsolid", "Default", "static_solid", "terrain", "vehicle");
            Transform cam = GameCamera.instance.transform;
            if (!Physics.Raycast(cam.position, cam.forward, out RaycastHit hit, 50f, _mask, QueryTriggerInteraction.Ignore)) return null;
            if (Vector3.Distance(p.m_eye.position, hit.point) > 8f) return null;
            Fireplace f = hit.collider.GetComponentInParent<Fireplace>();
            return f != null && f.GetComponent<ZNetView>() != null && Colony.Nearest(f.transform.position, Renown.ZoneReach + 20) != null ? f : null;
        }
        private static GameObject _fireLabel; private static Fireplace _fireLabelled; private static string _fireText = "";
        public static void FireHint(Fireplace fire)
        {
            Colony c = fire != null ? Colony.Nearest(fire.transform.position, Renown.ZoneReach + 20) : null;
            string text = c == null ? "" : Beacon.IsMarked(c, fire) ? "the colony's beacon\n<size=70%>lit when a jarl sails home at night. Shift+E: not any more</size>" : "<size=70%>Shift+E: make it the colony's beacon</size>";
            if (fire == _fireLabelled && text == _fireText && (_fireLabel != null) == (text != "")) return;
            if (_fireLabel != null) Object.Destroy(_fireLabel);
            _fireLabel = null; _fireLabelled = fire; _fireText = text;
            if (text == "") return;
            Vector3 top = fire.transform.position + Vector3.up * 1.6f;
            foreach (Collider col in fire.GetComponentsInChildren<Collider>()) if (!col.isTrigger) top.y = Mathf.Max(top.y, col.bounds.max.y + 0.5f);
            _fireLabel = Overlay_.Label(top, text, new Color(1f, 0.7f, 0.35f, 1f), 2.4f);
        }
        // The colony this chest is a store of (and its id and what it holds now), or why not.
        public static bool Of(Container chest, out Colony c, out string id, out string category)
        {
            id = category = ""; c = Colony.Nearest(chest.transform.position, Renown.ZoneReach + 20);
            if (c == null) return false;
            foreach (var x in Stock.Chests(c, null, true)) if (x.chest == chest) { id = x.id; category = x.category; return true; }
            return false;
        }
        public static string Name(string category) =>
            category == "treasury" ? "the treasury" : category == Stock.TradeCategory ? "trade goods (a trader sells them)" : category == "any" ? "stores of anything" : category == "" ? "not a store" : category + " stores";
        public static void Open(Container chest)
        {
            if (!GameUi.Ready()) return;
            if (!Of(chest, out Colony c, out string id, out string now))
            {
                Player.m_localPlayer?.Message(MessageHud.MessageType.Center, c == null ? "Not in a colony." : chest.m_privacy == Container.PrivacySetting.Private ? "A private chest isn't the colony's." : "That isn't one of the colony's chests.");
                return;
            }
            if (id.Length == 0) id = Colony.IdOf(chest.GetComponent<ZNetView>());
            _c = c; _chest = chest;
            Close();
            const float w = 640, h = 330;
            _window = GameUi.Window("JarldomChestPicker", new Vector2(w, h));
            GameUi.Label(_window, "What is this chest for?", new Vector2(30, -18), new Vector2(w - 60, 44), 30, GameUi.Warm, GameUi.Norse);
            GameUi.Label(_window, "Now: " + Name(now), new Vector2(30, -58), new Vector2(w - 60, 24), 16, GameUi.Dim);
            GameUi.Braid(_window, new Vector2(0, -86), w - 60);
            float x = 30, y = 100;
            void Choice(string text, string tip, bool chosen, System.Action act)
            {
                if (x + 140 > w - 20) { x = 30; y += 46; }
                var b = GameUi.Button(_window, chosen ? "• " + text : text, new Vector2(x, -y), new Vector2(136, 40), () => { act(); Close(); }, true, 15);
                if (tip != null) GameUi.Tip(b, text, tip);
                x += 144;
            }
            string chestId = id;
            Choice("Treasury", "Wages, feasts, ransoms, tribute and hired swords are paid from it; coins brought in go there. One chest: choosing it here moves the treasury.", now == "treasury", () => Net.Act(_c, "treasury", chestId));
            foreach (string cat in Stock.Categories)
            {
                string k = cat;
                Choice(cat == "any" ? "anything" : cat, cat == "any" ? "Whatever has no stockpile of its own kind." : null, now == cat && _c.R.Stockpiles.ContainsKey(chestId), () => Net.Act(_c, "stockpile", chestId + "|" + k));
            }
            Choice("trade goods", "What you put in it, a trader takes to market and sells, coins to the treasury: wood, crops, hides, ore, metal, food, gear, anything (the rest of the stores, they sell only treasure). No villager takes from it or fills it.", now == Stock.TradeCategory, () => Net.Act(_c, "stockpile", chestId + "|" + Stock.TradeCategory));
            Choice("nothing", "Not the colony's stores: villagers leave it alone unless it stands in a stockpile room.", now == "" || now == "any" && !_c.R.Stockpiles.ContainsKey(chestId), () => Net.Act(_c, "stockpile", chestId + "|"));
            GameUi.Button(_window, "Cancel", new Vector2(w - 180, -h + 64), new Vector2(150, 42), Close, true, 16);
        }
    }
}
