using System;
using System.Linq;
using UnityEngine;
using Village;
using Object = UnityEngine.Object;

namespace Villagewright
{
    // The village's settings, in the game's own look (GameUi): what kind of village, what it needs, a new layout, Build and Undo.
    internal static class VillageWindow
    {
        private static RectTransform _window;
        private static float _closedAt = -1f, _openedAt;
        private static string _pending;
        internal static bool IsOpen => _window != null;
        private static Plugin P => Plugin.Instance;

        internal static void Open()
        {
            if (Time.unscaledTime < _closedAt + 0.25f || !GameUi.Ready() || P == null) return;
            GameUi.IsOpen = () => IsOpen;
            if (InventoryGui.IsVisible()) InventoryGui.instance.Hide();
            _openedAt = Time.unscaledTime;
            Build();
        }
        internal static void Close()
        {
            if (_window != null) Object.Destroy(_window.gameObject);
            _window = null; _closedAt = Time.unscaledTime;
        }
        internal static void Tick()
        {
            if (_window == null) return;
            bool useAgain = Time.unscaledTime > _openedAt + 0.2f && (ZInput.GetButtonDown("Use") || Input.GetKeyDown(KeyCode.Escape));
            if (Player.m_localPlayer == null || Player.m_localPlayer.IsDead() || global::Menu.IsVisible() || useAgain) { Close(); return; }
            // Work that changes the world waits for the frame after the click.
            if (_pending != null)
            {
                string action = _pending; _pending = null;
                try
                {
                    if (action == "build")
                    {
                        if (!CanBuildFree()) throw new ArgumentException("Building a whole village at once is for creative play (devcommands with nocost, or a no-cost world).");
                        P.Build(); Plugin.Say($"building the village: {P.BuildTotal} pieces..."); Close(); return;
                    }
                    if (action == "undo") Plugin.Say(P.Undo());
                }
                catch (ArgumentException ex) { Plugin.Say(ex.Message); }
                Build();
            }
        }
        private static bool CanBuildFree() => Player.m_localPlayer != null && (Player.m_localPlayer.NoCostCheat() || Terminal.m_cheat);

        private static void Change(Action a) { try { a(); P.Replan(); } catch (Exception ex) { P.Problem = ex.Message; } Build(); }

        private static readonly (Kind kind, string name)[] Kinds = { (Kind.Hamlet, "Hamlet"), (Kind.Farming, "Farming"), (Kind.Fishing, "Fishing"), (Kind.Market, "Market"), (Kind.HillFort, "Hill fort"), (Kind.Temple, "Temple town") };

        private static void Build()
        {
            if (_window != null) Object.Destroy(_window.gameObject);
            var b = P.CurrentBrief();
            _window = GameUi.Window("VillagewrightWindow", new Vector2(980f, 690f));
            GameUi.Label(_window, "Villagewright", new Vector2(30f, -18f), new Vector2(400f, 44f), 34f, GameUi.Warm, GameUi.Norse);
            string site = P.Site == null ? "no site" : P.Site.Plot != null ? $"a plot of {P.Site.Plot.Count} corners" : $"a circle {P.Site.Radius * 2:0} m across";
            GameUi.Label(_window, site, new Vector2(430f, -28f), new Vector2(520f, 30f), 17f, GameUi.Dim, GameUi.Sans, TMPro.TextAlignmentOptions.Right);
            GameUi.Braid(_window, new Vector2(0f, -70f), 920f);

            float x = 30f;
            foreach (var (kind, name) in Kinds)
            {
                var k = kind;
                GameUi.Tab(_window, name, new Vector2(x, -86f), new Vector2(148f, 40f), P.Kind == k, () => Change(() => { P.Kind = k; P.Brief = Brief.For(k, P.Site?.Radius ?? 60, b.Seed); }), 17f);
                x += 154f;
            }

            // What it needs: counts on the left, buildings and features on the right.
            float y = -150f;
            void Count(string label, string value, int[] steps, Action<int> change)
            {
                GameUi.Label(_window, label, new Vector2(30f, y), new Vector2(140f, 40f), 18f, GameUi.Text, GameUi.Serif);
                GameUi.Stepper(_window, new Vector2(170f, y), 40f, value, steps, d => Change(() => change(d)), 44f, 64f);
                y -= 48f;
            }
            Count("Houses", b.Houses.ToString(), new[] { 5, 1 }, d => b.Houses = Mathf.Clamp(b.Houses + d, 0, 40));
            Count("Fields", b.Fields.ToString(), new[] { 1 }, d => b.Fields = Mathf.Clamp(b.Fields + d, 0, 16));
            Count("Storehouses", b.Storehouses.ToString(), new[] { 1 }, d => b.Storehouses = Mathf.Clamp(b.Storehouses + d, 0, 6));
            Count("Ways out", b.Gates.ToString(), new[] { 1 }, d => b.Gates = Mathf.Clamp(b.Gates + d, 1, 6));
            Count("Intricacy", new[] { "Simple", "Crafted", "Ornate", "Grand", "King's" }[b.Detail], new[] { 1 }, d => b.Detail = Mathf.Clamp(b.Detail + d, 0, 4));
            Count("Streets", $"{b.StreetWidth:0} m", new[] { 1 }, d => b.StreetWidth = Mathf.Clamp((float)b.StreetWidth + d, 3, 6));

            float tx = 560f, ty = -150f;
            void Switch(string label, bool on, Action flip)
            {
                GameUi.Toggle(_window, label, on, new Vector2(tx, ty), new Vector2(190f, 40f), () => Change(flip));
                if (tx > 600f) { tx = 560f; ty -= 48f; } else tx += 200f;
            }
            Switch("Chieftain's hall", b.Hall, () => b.Hall = !b.Hall);
            Switch("Temple", b.Temple, () => b.Temple = !b.Temple);
            Switch("Smithy", b.Smithy, () => b.Smithy = !b.Smithy);
            Switch("Market", b.Market, () => b.Market = !b.Market);
            Switch("Palisade", b.Palisade, () => b.Palisade = !b.Palisade);
            Switch("Dock (by water)", b.Dock, () => b.Dock = !b.Dock);
            Switch("Torches", b.Torches, () => b.Torches = !b.Torches);
            ty -= tx > 600f ? 48f : 0f;
            GameUi.Label(_window, $"Layout {b.Seed}", new Vector2(560f, ty - 4f), new Vector2(190f, 40f), 18f, GameUi.Text, GameUi.Serif);
            GameUi.Button(_window, "Another layout", new Vector2(760f, ty), new Vector2(190f, 40f), () => Change(() => b.Seed = Plugin.NewSeed()));

            // What came of it.
            RectTransform box = GameUi.Box(_window, new Vector2(30f, -448f), new Vector2(920f, 150f));
            string summary;
            Color colour = GameUi.Text;
            if (P.Plan == null) { summary = P.Problem ?? "Mark a site with the stake."; colour = GameUi.Warn; }
            else
            {
                var plan = P.Plan;
                var uses = plan.Lots.GroupBy(l => l.Use).Select(g => g.Key == Use.House ? $"{g.Count()} houses ({g.Count(l => l.Style == Style.Longhouse)} longhouses)" : g.Count() == 1 ? Name(g.Key) : $"{g.Count()} {Name(g.Key)}s");
                summary = $"{plan.Lots.Count} buildings: {string.Join(", ", uses)}.\n" +
                    $"{plan.Streets.Count(s => s.Main)} streets and {plan.Streets.Count(s => !s.Main)} lanes ({plan.Streets.Sum(s => Geometry.Length(s.Points)):0} m), a square {plan.SquareRadius * 2:0} m across, {plan.Fields.Count} fields" +
                    (plan.Palisade.Count > 0 ? $", a palisade with {plan.Gates.Count} gates" : "") + (plan.Shore.HasValue ? ", a dock" : "") + $". {plan.Props.Count} other pieces.";
                if (plan.Notes.Count > 0) summary += "\n<color=#ff9a6a>" + string.Join(" ", plan.Notes) + "</color>";
                if (P.BuildState != "idle") summary += $"\nLast build: {P.BuildState}, {P.BuildPlaced} of {P.BuildTotal} pieces.";
            }
            GameUi.Label(box, summary, new Vector2(14f, -10f), new Vector2(892f, 130f), 17f, colour, GameUi.Sans, TMPro.TextAlignmentOptions.TopLeft);

            bool free = CanBuildFree();
            GameUi.Button(_window, "Build village", new Vector2(30f, -616f), new Vector2(300f, 52f), () => _pending = "build", P.Plan != null && free && !P.Building, 20f);
            GameUi.Button(_window, "Take away last village", new Vector2(345f, -616f), new Vector2(300f, 52f), () => _pending = "undo", !P.Building, 18f);
            GameUi.Button(_window, "Close", new Vector2(660f, -616f), new Vector2(290f, 52f), Close, true, 18f);
            if (!free) GameUi.Label(_window, "Building needs creative play (devcommands + nocost).", new Vector2(30f, -664f), new Vector2(620f, 24f), 15f, GameUi.Dim, GameUi.Sans);
        }
        private static string Name(Use u) => u switch { Use.Hall => "the hall", Use.Temple => "a temple", Use.Smithy => "a smithy", Use.Storehouse => "storehouse", Use.Boathouse => "a boathouse", _ => u.ToString().ToLower() };
    }
}
