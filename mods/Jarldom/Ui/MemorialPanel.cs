using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jarldom.Ui
{
    // The memorial stone, read: the colony's dead, a page at a time (who went to Valhalla and their deed; who rests with Hel; who
    // walks still, unburied).
    internal static class MemorialPanel
    {
        private static RectTransform _window; private static Colony _c; private static Vector3 _at; private static int _page; private static float _openedAt, _closedAt = -1f;
        public static bool IsOpen => _window != null;
        public static bool JustClosed => Time.unscaledTime < _closedAt + 0.25f;
        public static void Open(Colony c, Vector3 at)
        {
            if (!GameUi.Ready() || c == null) return;
            GameUi.IsOpen = () => Windows.AnyOpen;
            if (InventoryGui.IsVisible()) InventoryGui.instance.Hide();
            _c = c; _at = at; _page = int.MaxValue; _openedAt = Time.unscaledTime; // (the last page: the newest names)
            Build();
        }
        public static void Close() { if (_window == null) return; Object.Destroy(_window.gameObject); _window = null; _closedAt = Time.unscaledTime; UITooltip.HideTooltip(); }
        public static void Tick()
        {
            if (_window == null) return;
            Player p = Player.m_localPlayer;
            bool useAgain = Time.unscaledTime > _openedAt + 0.2f && ZInput.GetButtonDown("Use");
            if (p == null || p.IsDead() || global::Menu.IsVisible() || useAgain || _c == null || !_c.Valid || Vector3.Distance(p.transform.position, _at) > 6f) Close();
        }
        private static void Build()
        {
            if (_window != null) Object.Destroy(_window.gameObject);
            List<Remembered> all = Memorial.From(_c.R.Roster, _c.R.Flags, _c.R.Log);
            int pages = Memorial.Pages(all.Count); _page = Mathf.Clamp(_page, 0, pages - 1);
            const float w = 760, h = 620;
            _window = GameUi.Window("JarldomMemorial", new Vector2(w, h));
            GameUi.Label(_window, "The Remembered", new Vector2(30, -18), new Vector2(w - 60, 44), 32, GameUi.Warm, GameUi.Norse);
            GameUi.Label(_window, $"{_c.R.Name}: {all.Count(x => !x.Hel)} gone to Valhalla, {all.Count(x => x.Hel && !x.Unburied)} resting with Hel, {all.Count(x => x.Unburied)} unburied", new Vector2(30, -60), new Vector2(w - 60, 24), 16, GameUi.Dim);
            GameUi.Braid(_window, new Vector2(0, -90), w - 60);
            float y = 106;
            if (all.Count == 0) GameUi.Label(_window, "No names yet. A soul who grows worthy goes to Valhalla from the funeral pyre, and is remembered here; so is one lost to Hel and laid to rest.", new Vector2(40, -y), new Vector2(w - 80, 80), 17, GameUi.Text);
            foreach (Remembered r in Memorial.Page(all, _page))
            {
                Color name = r.Unburied ? GameUi.Warn : r.Hel ? new Color(0.72f, 0.8f, 0.7f) : GameUi.Gold;
                GameUi.Label(_window, $"{r.Name}{(r.Day > 0 ? $"  <size=75%><color=#b8b0a0>day {r.Day}</color></size>" : "")}", new Vector2(40, -y), new Vector2(w - 80, 26), 20, name);
                GameUi.Label(_window, r.Deed, new Vector2(52, -y - 26), new Vector2(w - 100, 34), 14, GameUi.Dim);
                y += 58;
            }
            float by = -h + 70;
            if (pages > 1)
            {
                GameUi.Button(_window, "Earlier", new Vector2(30, by), new Vector2(130, 42), () => { _page--; Build(); }, _page > 0, 16);
                GameUi.Label(_window, $"{_page + 1} of {pages}", new Vector2(170, by - 8), new Vector2(120, 30), 17, GameUi.Text);
                GameUi.Button(_window, "Later", new Vector2(290, by), new Vector2(130, 42), () => { _page++; Build(); }, _page < pages - 1, 16);
            }
            GameUi.Button(_window, "Close", new Vector2(w - 180, by), new Vector2(150, 42), Close, true, 16);
        }
    }
}
