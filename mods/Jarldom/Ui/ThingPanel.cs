using System.Linq;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jarldom.Ui
{
    // The jarl's ruling at the Thing: shown by itself in the hall once a petitioner has spoken, to a player who may rule. Esc puts it
    // off until E on the one who spoke.
    internal static class ThingPanel
    {
        private static RectTransform _window; private static Colony _c; private static string _shown = "", _dismissed = ""; private static float _next;
        public static bool IsOpen => _window != null;
        private static readonly Vector2 Size = new Vector2(760f, 230f);
        private static string _readyKey = ""; private static float _readyAt;

        private static string Key(Colony c) => c.Id + "|" + Thing.Current(c)?.Key + "|" + c.R.Flag(Thing.Ruled);
        public static void Tick()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 0.3f;
            Player p = Player.m_localPlayer;
            Colony c = p == null || p.IsDead() ? null : Colony.All.FirstOrDefault(x => x.Valid && Thing.Ready(x) && x.Allows(p) && Thing.There(x, p.transform.position));
            if (c == null || global::Menu.IsVisible()) { Close(); return; }
            string key = Key(c);
            if (key == _dismissed) { Close(); return; }
            if (_window != null && key == _shown) return;
            // (a few seconds to hear it said and see who says it, before the answer's asked for)
            if (key != _readyKey) { _readyKey = key; _readyAt = Time.unscaledTime; }
            if (_window == null && Time.unscaledTime - _readyAt < 3f) return;
            if (_window == null && (ColonyWindow.IsOpen || VillagerPanel.IsOpen || RoomPicker.IsOpen || RoomEditor.IsOpen || JarlView.Active || InventoryGui.IsVisible() || GameUi.Typing)) return; // (not over another window)
            _c = c; Build(key);
        }
        // E on the one who spoke: back again.
        public static void Recall() { _dismissed = ""; _next = 0; }
        public static void Dismiss() { _dismissed = _shown; Close(); }
        public static void Close() { if (_window != null) Object.Destroy(_window.gameObject); _window = null; UITooltip.HideTooltip(); }

        private static void Build(string key)
        {
            Close();
            Petition p = Thing.Current(_c);
            if (p == null || !GameUi.Ready()) return;
            _shown = key;
            int idx = _c.R.Flag(Thing.Cur) - 1; Colony c = _c;
            int left = Thing.Pending(c).Count - 1;
            _window = GameUi.BottomWindow("JarldomThing", Size);
            string who = p.ByName != "" ? p.ByName : "A voice";
            GameUi.Label(_window, "The Thing", new Vector2(24, -12), new Vector2(300, 34), 24, GameUi.Warm, GameUi.Norse);
            GameUi.Line(_window, left > 0 ? $"{left} more after this" : "the last matter", new Vector2(440, -18), 296, 15, GameUi.Dim, null, TextAlignmentOptions.TopRight);
            TMP_Text said = GameUi.Line(_window, $"<color=#ffd27a>{who}:</color> “{p.Text}”", new Vector2(24, -52), Size.x - 48, 19, GameUi.Text);
            float y = 52 + said.GetPreferredValues(said.text, Size.x - 48, 0).y + 14;
            float w = (Size.x - 48 - 200 - 24) / 2f;
            GameUi.Button(_window, p.Yes, new Vector2(24, -y), new Vector2(w, 40), () => { Net.Act(c, "rule", idx + "|yes|" + p.Key); Dismiss(); });
            GameUi.Button(_window, p.No, new Vector2(24 + w + 12, -y), new Vector2(w, 40), () => { Net.Act(c, "rule", idx + "|no|" + p.Key); Dismiss(); });
            GameUi.Button(_window, "Adjourn", new Vector2(Size.x - 24 - 200, -y), new Vector2(200, 40), () => { Net.Act(c, "thingend", ""); Dismiss(); }, true, 15);
            GameUi.Line(_window, $"Esc: decide later (E on {(p.ByName != "" ? p.ByName : "the petitioner")} brings this back)", new Vector2(24, -y - 48), Size.x - 48, 13, GameUi.Dim);
            _window.sizeDelta = new Vector2(Size.x, y + 48 + 26); // (as tall as what's in it)
        }
    }
}
