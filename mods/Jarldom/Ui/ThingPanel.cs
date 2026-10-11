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
        private static readonly Vector2 Size = new Vector2(680f, 330f);

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
            _window = GameUi.Window("JarldomThing", Size);
            GameUi.Label(_window, "The Thing", new Vector2(30, -18), new Vector2(400, 44), 30, GameUi.Warm, GameUi.Norse);
            GameUi.Line(_window, left > 0 ? $"{left} more after this" : "the last matter", new Vector2(420, -28), 230, 16, GameUi.Dim, null, TextAlignmentOptions.TopRight);
            GameUi.Braid(_window, new Vector2(0, -70), Size.x - 60);
            GameUi.Line(_window, $"{(p.ByName != "" ? p.ByName : "A voice")} speaks before the Thing:", new Vector2(30, -90), Size.x - 60, 17, GameUi.Dim);
            TMP_Text said = GameUi.Line(_window, "“" + p.Text + "”", new Vector2(30, -118), Size.x - 60, 22, GameUi.Text);
            float y = 118 + said.GetPreferredValues(said.text, Size.x - 60, 0).y + 20;
            GameUi.Button(_window, p.Yes, new Vector2(30, -y), new Vector2(300, 44), () => { Net.Act(c, "rule", idx + "|yes"); Close(); });
            GameUi.Button(_window, p.No, new Vector2(Size.x - 330, -y), new Vector2(300, 44), () => { Net.Act(c, "rule", idx + "|no"); Close(); });
            y += 56;
            GameUi.Button(_window, "Adjourn the Thing", new Vector2(30, -y), new Vector2(220, 34), () => { Net.Act(c, "thingend", ""); Close(); }, true, 15);
            GameUi.Line(_window, $"Esc: decide later (E on {(p.ByName != "" ? p.ByName : "the petitioner")} brings this back)", new Vector2(270, -y - 8), Size.x - 300, 14, GameUi.Dim);
        }
    }
}
