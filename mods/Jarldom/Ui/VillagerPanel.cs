using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jarldom.Ui
{
    // A villager's panel (E on them): who they are, how they feel and why, their work, bed and bag.
    internal static class VillagerPanel
    {
        private static RectTransform _window; private static Humanoid _h; private static string _shown = ""; private static bool _confirm;
        private static float _openedAt, _closedAt = -1, _next;
        public static bool IsOpen => _window != null;
        private static readonly Vector2 Size = new Vector2(900f, 640f);

        public static void Open(Humanoid h)
        {
            if (!GameUi.Ready() || h == null || Time.unscaledTime < _closedAt + 0.25f) return;
            ColonyWindow.Close();
            _h = h; _confirm = false;
            if (InventoryGui.IsVisible()) InventoryGui.instance.Hide();
            _openedAt = Time.unscaledTime;
            Build();
        }
        public static void Close()
        {
            if (_window != null) Object.Destroy(_window.gameObject);
            _window = null; _closedAt = Time.unscaledTime; UITooltip.HideTooltip();
        }
        public static void Tick()
        {
            if (_window == null) return;
            Player p = Player.m_localPlayer;
            bool useAgain = Time.unscaledTime > _openedAt + 0.2f && ZInput.GetButtonDown("Use");
            if (p == null || p.IsDead() || global::Menu.IsVisible() || useAgain || _h == null || _h.IsDead() || Vector3.Distance(p.transform.position, _h.transform.position) > 10f) { Close(); return; }
            if (Time.unscaledTime < _next || Input.GetMouseButton(0)) return;
            _next = Time.unscaledTime + 0.5f;
            if (Signature() != _shown) Build();
        }
        private static ZDO Z => _h.GetComponent<ZNetView>().GetZDO();
        // What the panel shows (not the ZDO's revision: a villager's ZDO changes all the time).
        private static string Signature() => (Z.GetByteArray(Villagers.StateKey)?.Length ?? 0) + "|" + Z.GetInt(Villagers.MoodKey) + "|" + _confirm + "|" + (Player.m_localPlayer?.GetInventory().GetAllItems().Any(Items.IsRing) ?? false) + "|" + Mathf.FloorToInt(Time.unscaledTime / 10);

        private static void Build()
        {
            if (_window != null) Object.Destroy(_window.gameObject);
            _shown = Signature();
            VillagerState s;
            try { s = VillagerState.Load(Z.GetByteArray(Villagers.StateKey)); } catch { s = null; }
            if (s == null) { Close(); return; }
            Colony c = Colony.Of(s.Colony);
            double now = ZNet.instance.GetTimeSeconds();
            _window = GameUi.Window("JarldomVillager", Size);
            GameUi.Label(_window, s.FullName, new Vector2(30, -18), new Vector2(560, 44), 32, GameUi.Warm, GameUi.Norse);
            GameUi.Line(_window, $"{Tables.Of(s.Prof).Name}{(s.Wants != s.Prof ? $" (would rather be a {Tables.Of(s.Wants).Name.ToLower()})" : "")}", new Vector2(560, -26), 310, 17, GameUi.Dim, null, TextAlignmentOptions.TopRight);
            GameUi.Braid(_window, new Vector2(0, -72), 840);
            // Left: who they are and how they feel.
            RectTransform left = GameUi.Box(_window, new Vector2(24, -92), new Vector2(420, 524));
            float y = 10;
            y = Text(left, string.Join(", ", s.Traits.Select(t => $"<color=#ffd27a>{Tables.Trait(t)?.Name}</color>: {Tables.Trait(t)?.Text}")), y, 15);
            y = Text(left, s.Story, y, 15, GameUi.Dim);
            int mood = Mood.Of(s, now);
            GameUi.Line(left, "Mood", new Vector2(10, -y), 80, 17, GameUi.Warm);
            GameUi.Bar(left, new Vector2(90, -y - 2), new Vector2(310, 22), (mood + 100) / 200f, mood >= 0 ? GameUi.Good : GameUi.Warn, $"{Windows.Signed(mood)} (works at {Windows.Pct(Mood.Multiplier(mood))})");
            y += 30;
            foreach (var (value, text) in Mood.Reasons(s, now).Take(9)) y = Text(left, $"<color={(value >= 0 ? "#99e08c" : "#ff8c59")}>{Windows.Signed(value)}</color>  {text}", y, 15) - 3;
            y += 4;
            foreach (Need n in Enum.GetValues(typeof(Need)))
            {
                GameUi.Line(left, n.ToString(), new Vector2(10, -y), 80, 15, GameUi.Dim);
                GameUi.Bar(left, new Vector2(90, -y - 1), new Vector2(310, 16), s.Needs[(int)n] / 100f, GameUi.Gold, "");
                y += 21;
            }
            // Right: worth, skills, bed and work.
            RectTransform right = GameUi.Box(_window, new Vector2(456, -92), new Vector2(420, 524));
            float ry = 10;
            GameUi.Line(right, "Worth", new Vector2(10, -ry), 80, 17, GameUi.Warm);
            GameUi.Bar(right, new Vector2(90, -ry - 2), new Vector2(310, 22), s.Worth / 100f, GameUi.Gold, s.Worthy ? "worthy of Valhalla" : $"{Mathf.RoundToInt(s.Worth)} of 100");
            ry += 32;
            var skills = s.Skills.Where(kv => !kv.Key.EndsWith("#") && kv.Value >= 1).OrderByDescending(kv => kv.Value).Take(5).Select(kv => $"{kv.Key} {Mathf.FloorToInt(kv.Value)}");
            ry = Text(right, "Skills: " + (skills.Any() ? string.Join(", ", skills) : "none yet"), ry, 15);
            ry = Text(right, $"Kills {s.Count("kills")}, jobs done {s.Count("orders")}, deaths {s.Deaths}. Hoard {s.Hoard} coins.", ry, 15, GameUi.Dim);
            Bed bed = c?.BedById(s.Bed);
            ry = Text(right, string.IsNullOrEmpty(s.Bed) ? "<color=#ff8c59>No bed</color>: sleeps by the hall fire. A soul with no bed who dies is lost." : bed != null ? $"Has a bed (comfort {Colony.ComfortAt(bed.transform.position)})." : "Has a bed (not loaded here).", ry, 15);
            string vid = s.Vid;
            float bx = 10;
            if (c != null)
            {
                Small(right, "Nearest free bed", ref bx, ry, () => { Bed free = c.FreeBeds().OrderBy(b => Vector3.Distance(b.transform.position, Player.m_localPlayer.transform.position)).FirstOrDefault(); if (free != null) Net.Act(c, "bed", vid + "|" + Colony.IdOf(free.GetComponent<ZNetView>())); else Player.m_localPlayer.Message(MessageHud.MessageType.Center, "No free bed in the colony."); });
                Small(right, "Open bag", ref bx, ry, () => { Humanoid h = _h; Close(); Body.OpenBag(h); });
                ry += 44; bx = 10;
                Small(right, "Come to the pillars", ref bx, ry, () => Net.Act(c, "summon", vid));
                Small(right, _confirm ? "Yes, send them away" : "Dismiss", ref bx, ry, () => { if (_confirm) { Net.Act(c, "dismiss", vid); Close(); } else { _confirm = true; Build(); } });
                ItemDrop.ItemData ring = Player.m_localPlayer.GetInventory().GetAllItems().FirstOrDefault(Items.IsRing);
                if (ring != null && c.AllowedName(Player.m_localPlayer.GetPlayerName())) { ry += 44; bx = 10; Small(right, "Give an arm ring", ref bx, ry, () => { Player me = Player.m_localPlayer; ItemDrop.ItemData r = me.GetInventory().GetAllItems().FirstOrDefault(Items.IsRing); if (r == null) return; me.GetInventory().RemoveItem(r, 1); Net.Act(c, "ring", vid); }); }
                ry += 48;
                ry = Text(right, "Work", ry, 18, GameUi.Warm);
                float px = 10;
                foreach (ProfessionInfo p in Tables.Professions.Where(p => p.Id != Prof.Gothi || p.Id == s.Prof))
                {
                    Prof id = p.Id;
                    if (px + 128 > 410) { px = 10; ry += 36; }
                    GameUi.Tab(right, p.Name, new Vector2(px, -ry), new Vector2(124, 32), s.Prof == id, () => Net.Act(c, "prof", vid + "|" + id), 14);
                    px += 128;
                }
                ry += 40;
                ry = Text(right, $"{Tables.Of(s.Prof).Name}: {Tables.Of(s.Prof).Does}{(Tables.Of(s.Prof).Needs != "" ? " (needs " + Tables.Of(s.Prof).Needs + ")" : "")}. Wage {Tables.Wage(s.Prof, s.Has("greedy"))} a day.", ry, 14, GameUi.Dim);
            }
        }
        private static float Text(RectTransform box, string text, float y, float size, Color? color = null)
        {
            TMP_Text t = GameUi.Label(box, text, new Vector2(10, -y), new Vector2(400, 20), size, color ?? GameUi.Text);
            float h = t.GetPreferredValues(text, 400, 0).y;
            ((RectTransform)t.transform).sizeDelta = new Vector2(400, h);
            return y + h + 6;
        }
        private static void Small(RectTransform box, string text, ref float x, float y, Action click)
        {
            float w = Mathf.Max(120, text.Length * 9f + 24);
            GameUi.Button(box, text, new Vector2(x, -y), new Vector2(w, 38), click, true, 15);
            x += w + 6;
        }
    }

    // After a room or zone outline is closed: what is it?
    internal static class RoomPicker
    {
        private static RectTransform _window; private static Colony _c; private static List<float> _xz; private static float _floor; private static bool _temple; private static string _replace;
        public static bool IsOpen => _window != null;
        // replace: the room or zone this one takes the place of (changing what a marked outline is).
        public static void Open(Colony c, List<float> xz, float floor, string replace = null)
        {
            if (!GameUi.Ready()) return;
            _c = c; _xz = xz; _floor = floor; _temple = false; _replace = replace;
            Build();
        }
        public static void Close() { if (_window != null) Object.Destroy(_window.gameObject); _window = null; }
        public static void Tick()
        {
            if (_window == null) return;
            if (Player.m_localPlayer == null || global::Menu.IsVisible() || _c == null || !_c.Valid) Close();
        }
        private static void Build()
        {
            Close();
            _window = GameUi.Window("JarldomRoomPicker", new Vector2(760, 470));
            GameUi.Label(_window, _temple ? "A temple to whom?" : $"What is it? ({Math.Round(Geometry.Area(_xz))} m²)", new Vector2(30, -18), new Vector2(700, 44), 30, GameUi.Warm, GameUi.Norse);
            GameUi.Braid(_window, new Vector2(0, -70), 700);
            string xz = string.Join(",", _xz.Select(v => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)));
            string floor = _floor.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            float x = 30, y = 90;
            if (_temple)
            {
                foreach (GodInfo g in Tables.Gods.Where(g => g.Id != "odin"))
                {
                    string god = g.Id;
                    Place(g.Name, ref x, ref y, () => { Net.Act(_c, "room", $"{RoomType.Temple}|{god}|{floor}|{xz}"); Close(); Designate.Clear(); });
                }
            }
            else
            {
                GameUi.Line(_window, "Rooms", new Vector2(30, -y), 300, 18, GameUi.Dim); y += 30;
                foreach (RoomInfo r in Tables.Rooms)
                {
                    RoomType t = r.Id;
                    Place(r.Name, ref x, ref y, () => { if (t == RoomType.Temple) { _temple = true; Build(); return; } Net.Act(_c, "room", $"{t}|{(t == RoomType.OdinShrine ? "odin" : "")}|{floor}|{xz}"); Close(); Designate.Clear(); });
                }
                x = 30; y += 52;
                GameUi.Line(_window, "Work zones", new Vector2(30, -y), 300, 18, GameUi.Dim); y += 30;
                foreach (ZoneInfo z in Tables.Zones)
                {
                    ZoneType t = z.Id;
                    Place(z.Name, ref x, ref y, () => { Net.Act(_c, "zone", $"{t}|{floor}|{xz}"); Close(); Designate.Clear(); });
                }
            }
            GameUi.Button(_window, "Cancel", new Vector2(760 - 180, -470 + 64), new Vector2(150, 42), () => { Close(); }, true, 16);
        }
        private static void Place(string text, ref float x, ref float y, Action click)
        {
            Action then = click; click = () => { string old = _replace; Colony c = _c; bool wasTemple = _temple; then(); if (!string.IsNullOrEmpty(old) && (wasTemple || !_temple)) Net.Act(c, "unroom", old); }; // (not when it only opened the list of gods)
            if (x + 170 > 740) { x = 30; y += 48; }
            GameUi.Button(_window, text, new Vector2(x, -y), new Vector2(164, 42), click, true, 15);
            x += 172;
        }
    }
}
