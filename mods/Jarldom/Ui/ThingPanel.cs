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
        public static void Close() { if (_window == null) return; Object.Destroy(_window.gameObject); _window = null; UITooltip.HideTooltip(); } // (only when it was open: Tick closes it three times a second, and every tooltip in the game went with it)

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
            // (an accusation has a third word: the gothi's rite, once the colony has a gothi and Odin an altar; a thief has four: the bót, a
            // season's outlawry, exile, or forgiveness)
            bool rite = p.Id == "accuse" && c.R.Living.Any(r => r.Prof == Prof.Gothi) && Saboteur.OdinAltar(c) != null;
            bool thief = p.Id == "exile";
            int n = thief ? 4 : rite ? 3 : 2;
            float adjourn = thief ? 130 : 200;
            float w = (Size.x - 48 - adjourn - 12 * n) / n;
            float font = thief ? 16 : 18;
            int slot = 0;
            float X() => 24 + (w + 12) * slot++;
            if (thief)
            {
                string fineWhy = Thing.JusticeWhy(c, p, "fine"), outWhy = Thing.JusticeWhy(c, p, "outlaw");
                Brain b = Villagers.All.FirstOrDefault(x => x.S.Colony == c.Id && x.S.Name == p.About);
                int fine = b != null ? Justice.Fine(b.S, c.GuardExtra) * Plugin.Instance.WageScale.Value / 100 : 0;
                var fb = GameUi.Button(_window, "Fine them", new Vector2(X(), -y), new Vector2(w, 40), () => { Net.Act(c, "rule", idx + "|fine|" + p.Key); Dismiss(); }, fineWhy == "", font);
                GameUi.Tip(fb, "The bót", fineWhy == "" ? $"{p.About} pays for it: their next {fine} coins of wages stay in the treasury{(Compat.HomespunOn ? $" (wool bolts pay it at once, {Vadmal.BoltCoins} coins a bolt: theirs first, then the stores' out of their hoard)" : "")}. They're shamed (-6 mood, 3 days) and like you less; everyone else: {Thing.EffectText(Thing.JusticeOpinion)}. Their slate is wiped; if they steal again, they can't be fined twice." : "Not now: " + fineWhy + ".");
                var ob = GameUi.Button(_window, $"Outlaw {Justice.OutlawDays} days", new Vector2(X(), -y), new Vector2(w, 40), () => { Net.Act(c, "rule", idx + "|outlaw|" + p.Key); Dismiss(); }, outWhy == "", font);
                GameUi.Tip(ob, "A season's outlawry", outWhy == "" ? $"{p.About} leaves the colony for {Justice.OutlawDays} days, then walks back in with all they had: their skills, their gear, their bed. They're shamed (-6 mood, 3 days) and like you a good deal less; everyone else: {Thing.EffectText(Thing.JusticeOpinion)}. Their slate is wiped." : "Not now: " + outWhy + ".");
            }
            var yb = GameUi.Button(_window, thief ? "Exile" : p.Yes, new Vector2(X(), -y), new Vector2(w, 40), () => { Net.Act(c, "rule", idx + "|yes|" + p.Key); Dismiss(); }, true, font);
            if (thief) GameUi.Tip(yb, "Full outlawry", $"{p.About} is cast out for good: their things are left in a chest by the pillars. Everyone: {Thing.EffectText(p.YesMood)}.");
            if (rite)
            {
                RosterRow row = c.R.Roster.FirstOrDefault(r => r.Name == p.About && r.State == SoulState.Active);
                string why = row == null ? "they're not here" : Saboteur.CleanseWhy(c, row.Vid);
                var rb = GameUi.Button(_window, why == "" ? $"The gothi's rite ({Saboteur.CleansePrice} coins)" : "The gothi's rite", new Vector2(X(), -y), new Vector2(w, 40), () => { Net.Act(c, "rule", idx + "|rite|" + p.Key); Dismiss(); }, why == "");
                GameUi.Tip(rb, "The gothi's rite", why == "" ? $"Neither cast out nor let be: the gothi takes {Saboteur.CleansePriceText()} to Odin and tries them at his altar. If the Forsaken's mark is on them it may be burned out, and they stay; if not, they're found clean. The coins are spent either way." : "Not now: " + why + ".");
            }
            var nb = GameUi.Button(_window, p.No, new Vector2(X(), -y), new Vector2(w, 40), () => { Net.Act(c, "rule", idx + "|no|" + p.Key); Dismiss(); }, true, font);
            if (thief) GameUi.Tip(nb, "Forgive", $"{p.About} is let be, nothing paid. Everyone: {Thing.EffectText(p.NoMood)}.");
            GameUi.Button(_window, "Adjourn", new Vector2(Size.x - 24 - adjourn, -y), new Vector2(adjourn, 40), () => { Net.Act(c, "thingend", ""); Dismiss(); }, true, 15);
            GameUi.Line(_window, $"Esc: decide later (E on {(p.ByName != "" ? p.ByName : "the petitioner")} brings this back)", new Vector2(24, -y - 48), Size.x - 48, 13, GameUi.Dim);
            _window.sizeDelta = new Vector2(Size.x, y + 48 + 26); // (as tall as what's in it)
        }
    }
}
