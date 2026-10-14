using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Jarldom.Ui
{
    // A villager's panel (E on them): who they are, how they feel and why, their work, bed and bag.
    internal static class VillagerPanel
    {
        private static RectTransform _window; private static Humanoid _h; private static string _shown = ""; private static bool _confirm;
        private static float _openedAt, _closedAt = -1, _next; private static bool _remote;
        public static bool IsOpen => _window != null;
        private static readonly Vector2 Size = new Vector2(900f, 780f);

        // Opened with E beside them, it closes when they walk off; from the colony window or the seat's view (remote), it stays.
        public static void Open(Humanoid h, bool remote = false)
        {
            if (!GameUi.Ready() || h == null || Time.unscaledTime < _closedAt + 0.25f) return;
            ColonyWindow.Close();
            _h = h; _confirm = false; _remote = remote;
            if (InventoryGui.IsVisible()) InventoryGui.instance.Hide();
            _openedAt = Time.unscaledTime;
            Build();
        }
        public static void Close()
        {
            if (_window == null) return; // (closed already: other tooltips, the inventory's, are left alone)
            Object.Destroy(_window.gameObject);
            _window = null; _closedAt = Time.unscaledTime; UITooltip.HideTooltip();
        }
        public static void Tick()
        {
            if (_window == null) return;
            Player p = Player.m_localPlayer;
            bool useAgain = Time.unscaledTime > _openedAt + 0.2f && ZInput.GetButtonDown("Use");
            if (p == null || p.IsDead() || global::Menu.IsVisible() || useAgain || _h == null || _h.IsDead() || !_remote && Vector3.Distance(p.transform.position, _h.transform.position) > 10f) { Close(); return; }
            if (Time.unscaledTime < _next || Input.GetMouseButton(0)) return;
            _next = Time.unscaledTime + 0.5f;
            if (Signature() != _shown) Build();
        }
        private static ZDO Z => _h.GetComponent<ZNetView>().GetZDO();
        // What the panel shows (not the ZDO's revision: a villager's ZDO changes all the time).
        private static string Signature() => (SoulStore.Of(Z)?.DataRevision ?? 0) + "|" + Z.GetInt(Villagers.MoodKey) + "|" + _confirm + "|" + (Player.m_localPlayer?.GetInventory().GetAllItems().Any(Items.IsRing) ?? false) + "|" + Player.m_localPlayer?.GetInventory().NrOfItems() + "|" + (Player.m_localPlayer != null && Player.m_localPlayer.m_customData.TryGetValue(Asks.TakenKey, out string taken) ? taken.Length : 0) + "|" + Mathf.FloorToInt(Time.unscaledTime / 10);

        private static void Build()
        {
            if (_window != null) Object.Destroy(_window.gameObject);
            _shown = Signature();
            VillagerState s;
            try { s = VillagerState.Load(SoulStore.StateOf(Z)); } catch { s = null; }
            if (s == null) { Close(); return; }
            Colony c = Colony.Of(s.Colony);
            double now = ZNet.instance.GetTimeSeconds();
            _window = GameUi.Window("JarldomVillager", Size);
            GameUi.Label(_window, s.FullName, new Vector2(30, -18), new Vector2(560, 44), 32, GameUi.Warm, GameUi.Norse);
            GameUi.Line(_window, $"{Tables.Of(s.Prof).Name}{(s.Wants != s.Prof ? $" (would rather be a {Tables.Of(s.Wants).Name.ToLower()})" : "")}", new Vector2(560, -26), 310, 17, GameUi.Dim, null, TextAlignmentOptions.TopRight);
            GameUi.Braid(_window, new Vector2(0, -72), 840);
            // Left: who they are and how they feel.
            RectTransform left = GameUi.Box(_window, new Vector2(24, -92), new Vector2(420, 624));
            float y = 10;
            y = Text(left, string.Join(", ", s.Traits.Select(t => $"<color=#ffd27a>{Tables.Trait(t)?.Name}</color>: {Tables.Trait(t)?.Text}")), y, 15);
            y = Text(left, s.Story, y, 15, GameUi.Dim);
            int mood = Mood.Of(s, now);
            GameUi.Line(left, "Mood", new Vector2(10, -y), 80, 17, GameUi.Warm);
            GameUi.Bar(left, new Vector2(90, -y - 2), new Vector2(310, 22), (mood + 100) / 200f, mood >= 0 ? GameUi.Good : GameUi.Warn, $"{Windows.Signed(mood)} (works at {Windows.Pct(Mood.Multiplier(mood) * Dimming.Work(Dimming.Of(s, Clock.Day(ZNet.instance.GetTimeSeconds()))))})");
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
            RectTransform right = GameUi.Box(_window, new Vector2(456, -92), new Vector2(420, 624));
            float ry = 10;
            GameUi.Line(right, "Worth", new Vector2(10, -ry), 80, 17, GameUi.Warm);
            GameUi.Bar(right, new Vector2(90, -ry - 2), new Vector2(310, 22), s.Worth / 100f, GameUi.Gold, s.Worthy ? "worthy of Valhalla" : $"{Mathf.RoundToInt(s.Worth)} of 100");
            ry += 32;
            var skills = s.Skills.Where(kv => !kv.Key.EndsWith("#") && kv.Value >= 1).OrderByDescending(kv => kv.Value).Take(5).Select(kv => $"{kv.Key} {Mathf.FloorToInt(kv.Value)}");
            ry = Text(right, "Skills: " + (skills.Any() ? string.Join(", ", skills) : "none yet"), ry, 15);
            ry = Text(right, $"Kills {s.Count("kills")}, jobs done {s.Count("orders")}, deaths {s.Deaths}. Hoard {s.Hoard} coins.", ry, 15, GameUi.Dim);
            if (c != null) { try { ry = AskBlock(right, c, s, ry); } catch (Exception e) { Plugin.Warn("Jarldom: the ask in the panel: " + e.Message); } }
            Bed bed = c?.BedById(s.Bed);
            ry = Text(right, string.IsNullOrEmpty(s.Bed) ? "<color=#ff8c59>No bed</color>: sleeps by the hall fire. If they die, they're slow to rise again (by the pillars)." : bed != null ? $"Has a bed (comfort {Colony.ComfortAt(bed.transform.position)})." : "Has a bed (not loaded here).", ry, 15);
            string vid = s.Vid;
            float bx = 10;
            if (c != null)
            {
                Small(right, "Nearest free bed", ref bx, ry, () => { Bed free = c.FreeBeds().OrderBy(b => Vector3.Distance(b.transform.position, Player.m_localPlayer.transform.position)).FirstOrDefault(); if (free != null) Net.Act(c, "bed", vid + "|" + Colony.IdOf(free.GetComponent<ZNetView>())); else Player.m_localPlayer.Message(MessageHud.MessageType.Center, "No free bed in the colony."); });
                Small(right, "Open bag", ref bx, ry, () => { Humanoid h = _h; Close(); Body.OpenBag(h); });
                ry += 44; bx = 10;
                bool following = Z.GetLong(Follow.Key, 0L) != 0;
                long me = Player.m_localPlayer.GetPlayerID();
                Small(right, following ? (Z.GetLong(Follow.Key, 0L) == me ? "Go home" : "Stop following") : "Follow me", ref bx, ry, () => Net.Act(c, "follow", vid + "|" + (following ? 0 : me)), "Out with the jarl: they keep up with you wherever you go (through portals too) and fight what comes at you. Give them food with a hotbar key. The saga tells of it when they come home.");
                Small(right, "Come to the pillars", ref bx, ry, () => Net.Act(c, "summon", vid), "They stop what they're doing and come to the high-seat pillars.");
                ry += 44; bx = 10;
                Small(right, _confirm ? "Yes, send them away" : "Dismiss", ref bx, ry, () => { if (_confirm) { Net.Act(c, "dismiss", vid); Close(); } else { _confirm = true; Build(); } });
                // (the cleansing rite: only once the colony has a gothi and Odin an altar; why not, on the button's tip, when it can't be asked)
                if (c.R.Living.Any(r => r.Prof == Prof.Gothi) && Saboteur.OdinAltar(c) != null && c.AllowedName(Player.m_localPlayer.GetPlayerName()))
                {
                    string why = Saboteur.CleanseWhy(c, vid);
                    string rite = why == "" ? $"Cleansing rite ({Saboteur.CleansePrice} coins)" : "Cleansing rite";
                    if (bx + W(rite) > 410) { ry += 44; bx = 10; }
                    Small(right, rite, ref bx, ry, () => { if (Saboteur.CleanseWhy(c, vid) == "") { Net.Act(c, "cleanse", vid); Close(); } else Player.m_localPlayer.Message(MessageHud.MessageType.Center, "Not now: " + Saboteur.CleanseWhy(c, vid) + "."); },
                        why == "" ? $"The gothi takes {Saboteur.CleansePrice} coins from the treasury to Odin and holds a rite at his altar. If the Forsaken's mark is on them, Odin may burn it out (more likely the more he favours the colony); if not, they're found clean and the suspicion on them lifts. For a disgraced soul it's their atonement: if Odin hears it, the shame is washed out. The coins are spent either way." : "Not now: " + why + ".");
                }
                ItemDrop.ItemData ring = Player.m_localPlayer.GetInventory().GetAllItems().FirstOrDefault(Items.IsRing);
                if (ring != null && c.AllowedName(Player.m_localPlayer.GetPlayerName())) { if (bx + W("Give an arm ring") > 410) { ry += 44; bx = 10; } Small(right, "Give an arm ring", ref bx, ry, () => { Player me = Player.m_localPlayer; ItemDrop.ItemData r = me.GetInventory().GetAllItems().FirstOrDefault(Items.IsRing); if (r == null) return; me.GetInventory().RemoveItem(r, 1); Net.Act(c, "ring", vid); }); }
                ry += 48;
                ry = Text(right, "Work", ry, 18, GameUi.Warm);
                float px = 10;
                foreach (ProfessionInfo p in Tables.Professions.Where(p => true))
                {
                    Prof id = p.Id;
                    if (px + 128 > 410) { px = 10; ry += 36; }
                    GameUi.Tab(right, p.Name, new Vector2(px, -ry), new Vector2(124, 32), s.Prof == id, () => Net.Act(c, "prof", vid + "|" + id), 14);
                    px += 128;
                }
                ry += 40;
                ry = Text(right, $"{Tables.Of(s.Prof).Name}: {Tables.Of(s.Prof).Does}{(Tables.Of(s.Prof).Needs != "" ? " (needs " + Tables.Of(s.Prof).Needs + ")" : "")}. Wage {Tables.Wage(s.Prof, s.Has("greedy"))} a day.", ry, 14, GameUi.Dim);
            }
            // Taller than the boxes (a long ask, the rite, a ring to give): the window grows to fit, rather than spill over its edge.
            float need = Mathf.Max(y, ry) + 12;
            if (need > Size.y - 156)
            {
                left.sizeDelta = new Vector2(left.sizeDelta.x, need); right.sizeDelta = new Vector2(right.sizeDelta.x, need);
                _window.sizeDelta = new Vector2(Size.x, need + 156);
            }
        }
        // What they ask of the jarl, why, how long it stands, and the one button that moves it on.
        private static float AskBlock(RectTransform right, Colony c, VillagerState s, float ry)
        {
            Request q = s.Asked; Player me = Player.m_localPlayer;
            if (Asks.All(me).Any(x => x.Vid == s.Vid && x.Id != q?.Id)) Asks.Drop(me, s.Vid); // (one answered or lapsed since it was taken on)
            if (q == null)
            {
                int dim = Dimming.Of(s, Clock.Day(ZNet.instance.GetTimeSeconds()));
                if (dim > 0 && !s.Disgraced) ry = Text(right, $"<color=#9fb8a8>Dimming{(dim >= 2 ? ", and restless" : "")}</color>: {Dimming.Why(Clock.Day(ZNet.instance.GetTimeSeconds()), Clock.Day(ZNet.instance.GetTimeSeconds()) - Dimming.Days(s, Clock.Day(ZNet.instance.GetTimeSeconds())))}, so they fade ({(dim >= 2 ? "a fifth" : "a tenth")} slower at work). Anything worthy lifts it: a deed they ask of you, a kill, a feast, the gothi's rite at Odin's altar.", ry, 15);
                if (s.Disgraced) ry = Text(right, "<color=#ff8c59>Disgraced</color>: miserable too long, they shamed themselves. Ways back: the deed they ask of you, the gothi's cleansing rite at Odin's altar, or killing raiders in a raid the colony holds off. Left as they are, they may desert by night.", ry, 15);
                return ry;
            }
            int left = Requests.DaysLeft(q, Clock.Day(ZNet.instance.GetTimeSeconds()));
            string head = q.Deed ? s.Disgraced ? "<color=#ff8c59>Disgraced</color>. A deed in their name would wash out the shame:" : "<color=#9fb8a8>Dimming</color>. A deed in their name would lift it:" : "Asks of you:";
            ry = Text(right, $"{head} <color=#ffd27a>{Requests.Describe(q)}</color> ({left} {(left == 1 ? "day" : "days")} left{(q.Deed ? "" : ", then they let it go")})", ry, 16);
            ry = Text(right, $"<i>“{q.Why}”</i>", ry, 14, GameUi.Dim);
            Asks.Taken t = Asks.Of(me, s.Vid, q.Id);
            string vid = s.Vid; float bx = 10;
            string reward = $"Their good opinion, worth on their road to Valhalla{(s.Hoard > 0 && q.Coins > 0 ? $", up to {Requests.CoinsFromHoard(q, s.Hoard)} coins from their own hoard" : "")}, and renown for the colony{(q.Deed ? "; and their disgrace is gone" : "")}.";
            bool ready = Asks.Ready(me, s, out string why);
            if (Requests.HandIn(q))
            {
                int have = Asks.Have(me, q, out _);
                Button hand = Small(right, $"Hand over ({have}/{q.Count})", ref bx, ry, () => { if (_remote) { me.Message(MessageHud.MessageType.Center, "Go to them to hand it over."); return; } Asks.Answer(c, s); }, "Give them what they asked for, from your bag. " + reward);
                hand.interactable = ready && !_remote;
            }
            else if (t == null)
                Small(right, q.Kind == Ask.Visit ? "I'll go (pin it)" : "I'll see to it", ref bx, ry, () => { Asks.Take(c, s); Build(); }, (q.Kind == Ask.Visit ? "The place goes on your map. Go within sight of it, then come back and tell them of it. " : "Do it with your own hands, anywhere in the world, then come back and tell them of it. ") + reward);
            else
            {
                Button tell = Small(right, t.Done ? "Tell of the deed" : q.Kind == Ask.Visit ? "Not seen yet" : "Not done yet", ref bx, ry, () => { if (_remote) { me.Message(MessageHud.MessageType.Center, "Go to them to tell of it."); return; } Asks.Answer(c, s); }, t.Done ? "Tell them it's done. " + reward : (q.Kind == Ask.Visit ? "It's pinned on your map: go within sight of it." : "Do it with your own hands, then come back.") + " " + reward);
                tell.interactable = t.Done && !_remote;
                Small(right, "Give it up", ref bx, ry, () => { Asks.Drop(me, vid); Build(); }, "Stop tracking it (they still ask it of anyone, until it lapses).");
            }
            ry += 44;
            if (!ready && why != "" && Requests.HandIn(q) && !_remote) ry = Text(right, $"Not yet: {why}.", ry, 14, GameUi.Dim);
            return ry;
        }
        private static float Text(RectTransform box, string text, float y, float size, Color? color = null)
        {
            TMP_Text t = GameUi.Label(box, text, new Vector2(10, -y), new Vector2(400, 20), size, color ?? GameUi.Text);
            float h = t.GetPreferredValues(text, 400, 0).y;
            ((RectTransform)t.transform).sizeDelta = new Vector2(400, h);
            return y + h + 6;
        }
        private static float W(string text) => Mathf.Max(120, text.Length * 9f + 24); // (a small button's width)
        private static Button Small(RectTransform box, string text, ref float x, float y, Action click, string tip = null)
        {
            float w = W(text);
            Button b = GameUi.Button(box, text, new Vector2(x, -y), new Vector2(w, 38), click, true, 15);
            if (tip != null) GameUi.Tip(b, text, tip);
            x += w + 6;
            return b;
        }
    }

    // After a room or zone outline is closed: what is it?
    internal static class RoomPicker
    {
        private static RectTransform _window; private static Colony _c; private static List<float> _xz; private static float _floor; private static bool _temple, _zonesOnly; private static string _replace;
        public static bool IsOpen => _window != null;
        // replace: the room or zone this one takes the place of (changing what a marked outline is).
        public static void Open(Colony c, List<float> xz, float floor, string replace = null, bool zonesOnly = false)
        {
            if (!GameUi.Ready()) return;
            _c = c; _xz = xz; _floor = floor; _temple = false; _replace = replace; _zonesOnly = zonesOnly;
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
                // (a great work the Thing chose, not yet placed: this outline can be its site)
                if (GreatWork.Choosable(_c, GreatWorks.Lighthouse))
                {
                    bool move = GreatWork.Movable(_c, GreatWorks.Lighthouse);
                    GameUi.Line(_window, move ? "Great work (planned, not begun: move it here)" : "Great work", new Vector2(30, -y), 600, 18, GameUi.Warm); y += 30;
                    var outline = _xz;
                    Place(move ? "Move it here" : GreatWorks.Title(GreatWorks.Lighthouse), ref x, ref y, () => { if (GreatWork.RaiseIn(_c, GreatWorks.Lighthouse, outline)) { Close(); Designate.Clear(); } });
                    x = 30; y += 52;
                }
                if (!_zonesOnly) { GameUi.Line(_window, "Rooms", new Vector2(30, -y), 300, 18, GameUi.Dim); y += 30; }
                foreach (RoomInfo r in _zonesOnly ? Enumerable.Empty<RoomInfo>() : Tables.Rooms)
                {
                    RoomType t = r.Id;
                    Place(r.Name, ref x, ref y, () => { if (t == RoomType.Temple) { _temple = true; Build(); return; } Net.Act(_c, "room", $"{t}|{(t == RoomType.OdinShrine ? "odin" : "")}|{floor}|{xz}"); Close(); Designate.Clear(); });
                }
                x = 30; if (!_zonesOnly) y += 52;
                GameUi.Line(_window, "Work zones", new Vector2(30, -y), 300, 18, GameUi.Dim); y += 30;
                foreach (ZoneInfo z in Tables.Zones.Where(z => z.Id != ZoneType.GuardPost)) // (guards walk watch posts now)
                {
                    ZoneType t = z.Id;
                    Place(z.Name, ref x, ref y, () => { Net.Act(_c, "zone", $"{t}|{floor}|{xz}"); Close(); Designate.Clear(); });
                }
            }
            // (as tall as its buttons need, a great work's row and all; Cancel under the last row)
            float h = Mathf.Max(470f, y + 52 + 72);
            _window.sizeDelta = new Vector2(760, h);
            GameUi.Button(_window, "Cancel", new Vector2(760 - 180, -h + 64), new Vector2(150, 42), () => { Close(); }, true, 16);
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
