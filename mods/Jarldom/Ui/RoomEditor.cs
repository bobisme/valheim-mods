using System;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jarldom.Ui
{
    // A marked room or zone, chosen with E while marking: what it is, how it grades and why, and buttons to check it again, change what it
    // is (the same outline) or remove it.
    internal static class RoomEditor
    {
        private static RectTransform _window; private static Colony _c; private static string _id; private static LineRenderer _glow;
        public static bool IsOpen => _window != null;
        public static void Open(Colony c, Room room, Zone zone)
        {
            if (!GameUi.Ready()) return;
            _c = c; _id = room?.Id ?? zone?.Id;
            Build();
        }
        public static void Close()
        {
            if (_window != null) Object.Destroy(_window.gameObject);
            _window = null;
            if (_glow != null) Object.Destroy(_glow.gameObject);
            _glow = null;
        }
        public static void Tick()
        {
            if (_window == null) return;
            if (Player.m_localPlayer == null || global::Menu.IsVisible() || _c == null || !_c.Valid) { Close(); return; }
            if (Room() == null && Zone() == null) { Close(); return; } // removed
            if (Time.unscaledTime >= _nextRefresh && !Input.GetMouseButton(0)) { _nextRefresh = Time.unscaledTime + 2f; Refresh(); } // (a check's news)
        }
        private static float _nextRefresh;
        private static void Reshape(System.Collections.Generic.IList<float> xz, float by)
        {
            var (cx, cz) = Geometry.Centre(xz);
            var grown = new System.Collections.Generic.List<float>();
            for (int i = 0; i < xz.Count; i += 2)
            {
                Vector2 d = new Vector2(xz[i] - cx, xz[i + 1] - cz); float len = d.magnitude;
                Vector2 to = len < 0.1f ? d : d * Mathf.Max(1f, len + by) / len;
                grown.Add((float)Math.Round(cx + to.x, 2)); grown.Add((float)Math.Round(cz + to.y, 2));
            }
            Net.Act(_c, "reshape", _id + "|" + string.Join(",", grown.Select(v => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture))));
        }
        private static Room Room() => _c.R.Rooms.FirstOrDefault(r => r.Id == _id);
        private static Zone Zone() => _c.R.Zones.FirstOrDefault(z => z.Id == _id);
        private static uint _shown;
        public static void Refresh() { if (_window != null && _c != null && _c.Z.DataRevision != _shown) Build(); }
        private static void Build()
        {
            if (_window != null) Object.Destroy(_window.gameObject);
            Room room = Room(); Zone zone = Zone();
            if (room == null && zone == null) { Close(); return; }
            _shown = _c.Z.DataRevision;
            var xz = room?.Xz ?? zone.Xz; float floor = room?.Floor ?? zone.Floor;
            _window = GameUi.Window("JarldomRoomEditor", new Vector2(640, 360));
            string title = room != null ? Tables.Of(room.Type).Name + (room.Type == RoomType.Temple && Tables.God(room.God) != null ? " of " + Tables.God(room.God).Name : "") : Tables.Of(zone.Type).Name;
            GameUi.Label(_window, title, new Vector2(30, -18), new Vector2(580, 44), 30, GameUi.Warm, GameUi.Norse);
            GameUi.Braid(_window, new Vector2(0, -70), 580);
            string text;
            if (room != null)
            {
                text = $"{Math.Round(Geometry.Area(xz))} m², floor at {room.Floor:0.0} m, {room.Height:0.0} m to the ceiling.\n" +
                       (room.Ok ? $"<color=#99e08c>Grade {room.Grade}</color> ({room.Points} points from {Tables.Of(room.Type).PointsFrom})." : $"<color=#ff8c59>Not yet a {Tables.Of(room.Type).Name.ToLower()}: {room.Failures}.</color>") +
                       (room.OpenEdges.Length > 0 ? "\nThe red edges on the ground are gaps in its walls." : "");
            }
            else
            {
                var (left, standing) = Zones.Fuzz(zone.Type);
                text = $"{Math.Round(Geometry.Area(xz))} m². Worked by {(Tables.Of(zone.Type).Worker is Prof p ? Tables.Of(p).Name.ToLower() + "s" : "no one")}: {Tables.Of(zone.Type).Text}." +
                       (left > 0 || standing > 0 ? $"\n<color=#b0a890>Its edge is soft: {(left > 0 ? $"what's left lying up to {left:0} m past it is still its work, " : "")}and what stands a few metres past it may be.</color>" : "") +
                       "\n<color=#b0a890>Wider and Narrower move every edge 3 m.</color>";
            }
            TMPro.TMP_Text t = GameUi.Label(_window, text, new Vector2(30, -90), new Vector2(580, 150), 18, GameUi.Text);
            t.enableWordWrapping = true;
            float x = 30, y = 360 - 64;
            if (room != null) { GameUi.Button(_window, "Check now", new Vector2(x, -y), new Vector2(140, 42), () => { Net.Act(_c, "check", _id); }, true, 16); x += 148; }
            GameUi.Button(_window, "Change what it is", new Vector2(x, -y), new Vector2(190, 42), () =>
            {
                Colony c = _c; string id = _id; var outline = xz.ToList(); float f = floor;
                Close(); RoomPicker.Open(c, outline, f, id);
            }, true, 16); x += 198;
            GameUi.Button(_window, "Remove", new Vector2(x, -y), new Vector2(120, 42), () => { Net.Act(_c, "unroom", _id); Close(); }, true, 16); x += 128;
            if (zone != null)
            {
                // (a zone grown or shrunk about its middle, 3 m a side at a time)
                GameUi.Button(_window, "Wider", new Vector2(30, -y + 52), new Vector2(120, 38), () => Reshape(xz, 3f), true, 15);
                GameUi.Button(_window, "Narrower", new Vector2(158, -y + 52), new Vector2(120, 38), () => Reshape(xz, -3f), Zones.Reach(zone) > 5f, 15);
            }
            GameUi.Button(_window, "Close", new Vector2(640 - 150, -y), new Vector2(120, 42), Close, true, 16);
            // Its outline drawn bright while chosen.
            if (_glow == null) { _glow = Overlay_.NewLine("JarldomRoomChosen", new Color(1f, 0.95f, 0.5f, 1f), 0.16f); _glow.loop = true; }
            int n = xz.Count / 2;
            _glow.positionCount = n;
            _glow.SetPositions(Enumerable.Range(0, n).Select(i => new Vector3(xz[i * 2], (room != null ? floor : ZoneSystem.instance.GetGroundHeight(new Vector3(xz[i * 2], 0, xz[i * 2 + 1]))) + 0.25f, xz[i * 2 + 1])).ToArray());
        }
    }
}
