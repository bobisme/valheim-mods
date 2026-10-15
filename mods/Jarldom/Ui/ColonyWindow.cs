using System;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Jarldom.Ui
{
    // Every window of the mod, ticked and closed together.
    internal static class Windows
    {
        public static bool AnyOpen => ColonyWindow.IsOpen || VillagerPanel.IsOpen || RoomPicker.IsOpen || RoomEditor.IsOpen || ChestPicker.IsOpen || JarlView.Active || ThingPanel.IsOpen || MemorialPanel.IsOpen;
        public static void Tick()
        {
            GameUi.IsOpen = () => AnyOpen;
            GameUi_EscapeCloses.Close = Escape;
            ColonyWindow.Tick(); VillagerPanel.Tick(); RoomPicker.Tick(); RoomEditor.Tick(); ChestPicker.Tick(); JarlView.Tick(); ThingPanel.Tick(); MemorialPanel.Tick();
        }
        private static void Escape() { if (ThingPanel.IsOpen) ThingPanel.Dismiss(); else CloseAll(); } // (Esc puts off the ruling first)
        public static void CloseAll() { ThingPanel.Close(); MemorialPanel.Close(); if (ColonyWindow.IsOpen || VillagerPanel.IsOpen || RoomPicker.IsOpen || RoomEditor.IsOpen || ChestPicker.IsOpen) { ColonyWindow.Close(); VillagerPanel.Close(); RoomPicker.Close(); RoomEditor.Close(); ChestPicker.Close(); } else JarlView.End(); } // (Esc closes a window first, then the seat's view)
        public static string Pct(float f) => Mathf.RoundToInt(f * 100) + "%";
        public static string Signed(int v) => v > 0 ? "+" + v : v.ToString();
    }

    // The colony window (E on the High-Seat Pillars): the hall, its people, rooms, work, the Thing, the gods and the saga.
    internal static class ColonyWindow
    {
        private static RectTransform _window, _list; private static Colony _c; private static string _tab = "hall", _shown = "";
        private static float _openedAt, _closedAt = -1, _nextCheck;
        public static bool IsOpen => _window != null;
        public static Colony Colony => _c;
        private static readonly Vector2 Size = new Vector2(980f, 660f);

        public static void Open(Colony c, string tab = null)
        {
            if (!GameUi.Ready() || Time.unscaledTime < _closedAt + 0.25f) return;
            VillagerPanel.Close();
            _c = c; if (tab != null) _tab = tab;
            if (c.R.ComingLanded && c.R.Coming.Count > 0 && tab == null) _tab = "newcomers";
            if (InventoryGui.IsVisible()) InventoryGui.instance.Hide();
            _openedAt = Time.unscaledTime;
            Build();
        }
        public static void Close()
        {
            if (_window == null) return; // (closed already: other tooltips, the inventory's, are left alone)
            Object.Destroy(_window.gameObject);
            _window = null; _list = null; _closedAt = Time.unscaledTime; UITooltip.HideTooltip();
        }
        public static void Tick()
        {
            if (_window == null) return;
            Player p = Player.m_localPlayer;
            bool useAgain = Time.unscaledTime > _openedAt + 0.2f && ZInput.GetButtonDown("Use") && !GameUi.Typing;
            if (p == null || p.IsDead() || global::Menu.IsVisible() || useAgain || _c == null || !_c.Valid || Vector3.Distance(p.transform.position, _c.Position) > 12f) { Close(); return; }
            if (Time.unscaledTime < _nextCheck || Input.GetMouseButton(0)) return;
            _nextCheck = Time.unscaledTime + 0.5f;
            if (Signature() != _shown && !GameUi.Typing) Build(); // (not while a name is being typed: the box would go)
        }
        private static string Signature() => _tab + "|" + (_c.Z.GetByteArray(Colony.RecordKey)?.Length ?? 0) + "|" + _c.R.Log.Count + "|" + _c.Renown + "|" + Villagers.All.Count() + "|" + _c.Board.Count(OrderState.Open) + "|" + _confirm + "|" + Mathf.FloorToInt(Time.unscaledTime / 5);

        private static void Build()
        {
            float scrolled = _list != null ? _list.anchoredPosition.y : 0f;
            if (_window != null) Object.Destroy(_window.gameObject);
            _shown = Signature();
            _window = GameUi.Window("JarldomColony", Size);
            ColonyRecord r = _c.R;
            TMP_Text title = GameUi.Label(_window, GameUi.NoParse(r.Name), new Vector2(30, -18), new Vector2(620, 44), 32, GameUi.Warm, GameUi.Norse);
            float tw = Mathf.Min(460f, title.GetPreferredValues(r.Name, 620f, 0f).x);
            Tip(GameUi.Button(_window, "Rename", new Vector2(30 + tw + 14, -24), new Vector2(96, 32), () => { _tab = "hall"; _renaming = true; _rename = _c.R.Name; _renameFor = _c; Build(); }, true, 14), "Rename the hall", "Its name in this window, on the pillars and in the saga.");
            GameUi.Line(_window, $"{Tables.Of(r.Rank).Name} · {_c.Population} {(_c.Population == 1 ? "soul" : "souls")} · renown {_c.Renown}", new Vector2(560, -26), 390, 18, GameUi.Dim, null, TextAlignmentOptions.TopRight);
            GameUi.Braid(_window, new Vector2(0, -72), 920);
            var tabs = new List<(string id, string name)> { ("hall", "The hall"), ("people", "People"), ("rooms", "Rooms"), ("work", "Work"), ("thing", "The Thing"), ("gods", "Gods"), ("saga", "Saga") };
            if (r.ComingLanded && r.Coming.Count > 0) tabs.Insert(1, ("newcomers", $"New ({r.Coming.Count})"));
            float x = 24, w = (Size.x - 48) / tabs.Count;
            foreach (var t in tabs) { string id = t.id; GameUi.Tab(_window, t.name, new Vector2(x, -90), new Vector2(w - 6, 40), _tab == id, () => { _tab = id; Build(); }, 16); x += w; }
            RectTransform box = GameUi.Box(_window, new Vector2(24, -140), new Vector2(Size.x - 48, Size.y - 164));
            _list = GameUi.Scroll(box, 8f);
            float y = 0;
            switch (_tab)
            {
                case "hall": y = Hall(_list); break;
                case "newcomers": y = Newcomers(_list); break;
                case "people": y = People(_list); break;
                case "rooms": y = Rooms(_list); break;
                case "work": y = WorkTab(_list); break;
                case "thing": y = ThingTab(_list); break;
                case "gods": y = Gods(_list); break;
                case "saga": y = Saga(_list); break;
            }
            _list.sizeDelta = new Vector2(_list.sizeDelta.x, y);
            _list.anchoredPosition = new Vector2(0, scrolled);
        }
        private static float W => Size.x - 90;
        private static float Text(RectTransform list, string text, float y, float size = 17, Color? color = null, float indent = 8)
        {
            TMP_Text t = GameUi.Label(list, text, new Vector2(indent, -y), new Vector2(W - indent, 20), size, color ?? GameUi.Text);
            float h = t.GetPreferredValues(text, W - indent, 0).y;
            ((RectTransform)t.transform).sizeDelta = new Vector2(W - indent, h);
            return y + h + 6;
        }
        // Where a chest stands, from the high seat: " (the chest 25 m south-east of the high seat)".
        private static string Where(Vector3 at)
        {
            float d = Utils.DistanceXZ(at, _c.Position);
            return d < 4f ? " (the chest by the high seat)" : $" (the chest {d:0} m {ScoutTask.Compass(at - _c.Position)} of the high seat)";
        }
        private static float Heading(RectTransform list, string text, float y) => Text(list, text, y + 6, 22, GameUi.Warm);

        // What's holding the colony back, plainly: the first thing a jarl looks at.
        // What a cooking station turns into food (the game's own: raw meat, fish, necktails...), in the colony's stores.
        private static HashSet<string> _raw;
        private static int RawInStores(Colony c)
        {
            if (_raw == null || _raw.Count == 0)
                _raw = new HashSet<string>(new[] { "piece_cookingstation", "piece_cookingstation_iron" }.Select(Assets.Find).Where(g => g != null).SelectMany(g => g.GetComponent<CookingStation>()?.m_conversion ?? new List<CookingStation.ItemConversion>()).Where(cv => cv.m_from != null).Select(cv => cv.m_from.name));
            return Stock.Chests(c).Where(x => x.category != "treasury").Sum(x => x.chest.GetInventory().GetAllItems().Where(i => _raw.Contains(Classes.Prefab(i))).Sum(i => i.m_stack));
        }
        // The jarls and what the colony calls them, from what they've done for it.
        private static float JarlsPart(RectTransform list, float y)
        {
            var players = Tally.Players(_c.R.Flags);
            string me = Player.m_localPlayer?.GetPlayerName() ?? "";
            if (me != "" && _c.AllowedName(me) && !players.Contains(me)) players.Insert(0, me);
            if (players.Count == 0) return y;
            y = Heading(list, "The jarls", y);
            foreach (string p in players)
            {
                var t = Tally.Of(_c.R.Flags, p);
                int Get(string k) => t.TryGetValue(k, out int v) ? v : 0;
                var did = new List<string>();
                if (Get(Tally.Bosses) > 0) did.Add($"{Get(Tally.Bosses)} {(Get(Tally.Bosses) == 1 ? "boss" : "bosses")} slain");
                if (Get(Tally.Rings) > 0) did.Add($"{Get(Tally.Rings)} arm {(Get(Tally.Rings) == 1 ? "ring" : "rings")} given");
                if (Get(Tally.Raids) > 0) did.Add($"{Get(Tally.Raids)} {(Get(Tally.Raids) == 1 ? "raid" : "raids")} fought at home");
                if (Get(Tally.Ascensions) > 0) did.Add($"{Get(Tally.Ascensions)} sent to Valhalla");
                if (Get(Tally.Feasts) > 0) did.Add($"{Get(Tally.Feasts)} {(Get(Tally.Feasts) == 1 ? "feast" : "feasts")}");
                if (Get(Tally.Away) > 0) did.Add($"{Get(Tally.Away)} days away");
                y = Text(list, $"<color=#ffd27a>{Bynames.Named(p, t)}</color>: {(did.Count == 0 ? "nothing yet the colony sings of" : string.Join(", ", did))}.", y, 16);
            }
            return Text(list, "Bynames are earned: slay a boss (its \"-bane\"), give 5 arm rings (Ring-giver), fight 5 raids at home (Shield of the hall), send 3 to Valhalla (Soul-guide), hold 5 feasts (Feast-giver). Twenty days away and you're the Absent.", y, 14, GameUi.Dim);
        }
        internal static List<string> NeedsYou(Colony c)
        {
            var list = new List<string>();
            var people = c.R.Living.ToList();
            if (GreatWorks.Stage(c.R.Flags, GreatWorks.Lighthouse) == GreatWorks.Promised) list.Add($"The Thing chose to raise {GreatWorks.Name(GreatWorks.Lighthouse)}: with the hammer's \"Mark rooms and zones\", outline its site on flat ground near the sea (9 × 9 m at least) and choose \"{GreatWorks.Title(GreatWorks.Lighthouse)}\" under Great work.");
            if (GreatWorks.Stage(c.R.Flags, GreatWorks.Lighthouse) == GreatWorks.Laid && !people.Any(p => p.Prof == Prof.Builder)) list.Add($"{GreatWorks.Title(GreatWorks.Lighthouse)} waits for builders: give someone the Builder trade (People tab).");
            // (a crop chosen for the season with no seeds for it: the farmers sow whatever else there is)
            foreach (Zone z in c.R.Zones.Where(z => z.Type == ZoneType.Fields))
                if (Work.ChosenNow(c, z) is var ch && ch != null && !Work.HaveInStock(c, ch.Value.seed))
                    list.Add($"The fields {(Utils.DistanceXZ(Zones.Centre(z), c.Position) is float far && far < 6 ? "by the pillars" : $"{Mathf.RoundToInt(far)} m {ScoutTask.Compass(Zones.Centre(z) - c.Position)}")} are to be sown with {Work.CropName(ch.Value.sapling.name).ToLower()} this {Calendar.Name(Year.Today().Season)}, but the stores hold no {Localization.instance.Localize(ObjectDB.instance.GetItemPrefab(ch.Value.seed)?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_name ?? ch.Value.seed).ToLower()}: farmers sow what there is until some come in.");
            // (a festival coming and the hall's chest out of the haulers' reach: the goods have nowhere to go)
            if (Calendar.Next(Year.Today()) is var nf && nf.Item2 <= Calendar.PrepDays && FestHaulTask.HallOutOfReach(c))
                list.Add($"{Calendar.Name(nf.Item1)} is coming, and the haulers can't get to the great hall's chest to ready it: put a chest in the hall where they can walk right up to it.");
            // (a festival coming, not yet readied, and no one whose work it is to carry its goods to the hall)
            if (Calendar.Next(Year.Today()) is var hf && hf.Item2 <= Calendar.PrepDays && c.Best(RoomType.GreatHall) != null && FestivalDays.NoHauler(c) && FestivalDays.Readied(c, hf.Item1) < 1f)
                list.Add($"{Calendar.Name(hf.Item1)} is coming, and no one hauls: give someone the Hauler trade (People tab), or its goods won't reach the great hall's chests.");
            int homeless = people.Count(p => string.IsNullOrEmpty(p.Bed));
            if (homeless > 0) list.Add($"{homeless} {(homeless == 1 ? "has" : "have")} no bed and sleep by the hall fire: build beds in a marked longhouse (or by the pillars).");
            int meals = Stock.Food(c, c.Position).Sum(f => f.item.m_stack);
            if (people.Count > 0 && meals < people.Count * 3)
                list.Add(meals == 0 ? "The stores hold no food: mark fields, foraging or hunting grounds, or a fishing dock, or fill a chest." : $"Food for less than a day: {meals} meals for {people.Count} people.");
            // (raw meat and fish piling up with no one to cook them: the hunters and fishers feed no one)
            if (people.Count > 0 && !people.Any(p => p.Prof == Prof.Cook))
            {
                int raw = RawInStores(c);
                if (raw >= 5) list.Add($"{raw} raw meat and fish wait in the stores and no one cooks: give someone the Cook trade (People tab), with a cooking station over a fire.");
            }
            // (whole fish as caught, and nowhere to clean them: the cook can't cook them)
            if (people.Count > 0 && Crafts.PrepRecipes() is var preps && preps.Count > 0 && !preps.Any(r => Crafts.StationFor(c, r, c.Position) != null))
            {
                int whole = Stock.Chests(c).Where(x => x.category != "treasury").Sum(x => x.chest.GetInventory().GetAllItems().Where(i => i.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Fish).Sum(i => i.m_stack));
                string where = preps.Select(r => r.m_craftingStation.m_name).Distinct().Select(n => Localization.instance.Localize(n).ToLower()).FirstOrDefault();
                if (whole >= 5) list.Add($"{whole} whole fish wait in the stores: build a {where} so the cook can clean them for the cooking station.");
            }
            var full = Stock.Chests(c).Where(x => x.category != "" && x.category != "treasury").GroupBy(x => x.category).Where(g => g.All(x => x.chest.GetInventory().GetEmptySlots() == 0)).Select(g => g.Key).ToList(); // (every chest of the kind full)
            if (full.Count > 0) list.Add($"Full stockpiles ({string.Join(", ", full)}): add chests, or what's gathered lies about.");
            // (a treasury chest so full of other things that coins can't go in: trade's takings go elsewhere and paydays come up short)
            if (Stock.Treasury(c) is Container tre && tre.GetInventory().GetEmptySlots() == 0 && tre.GetInventory().GetAllItems().Any(i => i.m_dropPrefab == null || i.m_dropPrefab.name != "Coins"))
                list.Add("The treasury chest is full, and not of coins: take out the rest (" + string.Join(", ", tre.GetInventory().GetAllItems().Where(i => i.m_dropPrefab != null && i.m_dropPrefab.name != "Coins").Select(i => Localization.instance.Localize(i.m_shared.m_name).ToLower()).Distinct().Take(3)) + "), or no more coins fit for wages.");
            // (beds that couldn't be got to last night: walled in, upstairs with no way up, past a locked door)
            var bedless = Villagers.Bodies().Where(h => h.GetComponent<ZNetView>()?.GetZDO() is ZDO z && z.GetString(Villagers.ColonyKey, "") == c.Id && Villagers.Tags(z).GetString(SleepTask.BedFailKey, "") == "1").Select(h => Villagers.DisplayName(h).Split(' ')[0]).ToList();
            if (bedless.Count > 0) list.Add($"{string.Join(", ", bedless)} couldn't get to {(bedless.Count == 1 ? "their bed" : "their beds")} last night and slept by the hall fire: make a way to it (stairs, an open door, room to stand beside it) or give them another bed.");
            var stuck = new Dictionary<string, List<string>>();
            foreach (Humanoid h in Villagers.Bodies())
            {
                ZDO z = h.GetComponent<ZNetView>().GetZDO();
                if (z.GetString(Villagers.ColonyKey, "") != c.Id) continue;
                ZDO tags = Villagers.Tags(z);
                string doing = tags.GetString(Leisure.IdleKey, "") is string idle && idle != "" ? idle : tags.GetString(Villagers.DoingKey, ""); // (idle, and at their leisure: why)
                // (what the jarl can do something about: gear, a zone to mark or widen, a place to put things; not a lull, as fields growing)
                if (!(doing.StartsWith("needs gear") || doing.StartsWith("no ") && !doing.StartsWith("no game") || doing.StartsWith("nothing left") || doing.StartsWith("nothing to sell") || doing.StartsWith("Where do I put") || doing.StartsWith("trade runs wait"))) continue;
                if (!stuck.TryGetValue(doing, out var who)) stuck[doing] = who = new List<string>();
                who.Add(Villagers.DisplayName(h).Split(' ')[0]);
            }
            // (a trade with nowhere marked to do it, even while they keep busy with other chores: a fisherman with no dock lights torches)
            foreach (RosterRow p in people)
            {
                ZoneInfo zone = Tables.Zones.FirstOrDefault(z => z.Worker == p.Prof);
                if (zone == null || p.Prof == Prof.Guard || p.Prof == Prof.Miner && Claims.Of(c).Any() || c.R.Zones.Any(z => z.Type == zone.Id)) continue; // (guards walk watch posts; miners may have claims)
                string why = $"no {zone.Name.ToLower()} marked ({zone.Text})", name = p.Name.Split(' ')[0];
                if (!stuck.TryGetValue(why, out var who)) stuck[why] = who = new List<string>();
                if (!who.Contains(name)) who.Add(name);
            }
            foreach (var kv in stuck.Take(4)) list.Add($"{string.Join(", ", kv.Value)}: {kv.Key}.");
            Container treasury = Stock.Treasury(c);
            int due = (c.IsOwner ? Payroll.Due(Villagers.All.Where(b => b.S.Colony == c.Id).Select(b => b.S), Payroll.EveryDays, c.GuardExtra) : people.Sum(p => (Tables.Wage(p.Prof, false) + (p.Prof == Prof.Guard ? c.GuardExtra : 0)) * Payroll.EveryDays)) * Plugin.Instance.WageScale.Value / 100; // (another game runs the people: from the roster)
            if (Plugin.Instance.Payroll.Value && due > 0 && (treasury == null || Stock.Coins(treasury) < due))
            {
                int payday = (c.Day / Payroll.EveryDays + 1) * Payroll.EveryDays;
                list.Add(treasury == null ? "No treasury chest: wages can't be paid." : $"The treasury holds {Stock.Coins(treasury)} coins and {due} are due on day {payday}.");
            }
            if (c.Owed() > 0) list.Add($"Wages are owed for {c.R.MissedPaydays} missed payday{(c.R.MissedPaydays == 1 ? "" : "s")}: {c.Owed()} coins (the hall tab pays them).");
            // (fires and torches gone out that no one can feed: their fuel isn't in the stores)
            var dark = c.PiecesIn<Fireplace>(c.Radius).Where(f => !f.m_infiniteFuel && f.m_fuelItem != null && !Classes.Lit(f) && c.Feeds(f.GetComponent<Piece>()) && !Work.HaveInStock(c, f.m_fuelItem.name))
                .GroupBy(f => f.m_fuelItem.name).ToList();
            foreach (var g in dark.Take(2))
            {
                int fires = g.Count(Classes.Warms), lights = g.Count() - fires;
                string what = string.Join(" and ", new[] { fires > 0 ? $"{fires} fire{(fires == 1 ? "" : "s")}" : null, lights > 0 ? $"{lights} torch{(lights == 1 ? "" : "es")}" : null }.Where(x => x != null));
                list.Add($"{what} out: no {Localization.instance.Localize(g.First().m_fuelItem.m_itemData.m_shared.m_name).ToLower()} in the stores to feed them (the haulers keep them fed from there).");
            }
            if (Thing.Pending(c).Count > 0 && !Thing.Held(c))
            {
                int waited = Petitions.Waited(Thing.Pending(c), c.Day);
                if (waited >= 2) list.Add($"{Thing.Pending(c).Count} matter{(Thing.Pending(c).Count == 1 ? "" : "s")} wait for the Thing, the oldest {waited} days{(waited >= Petitions.UnheardAfter ? " (they grumble: unheard)" : "")}: call it from The Thing tab and go to the hall.");
            }
            var failing = c.R.Rooms.Where(x => !x.Ok).Select(x => Tables.Of(x.Type).Name.ToLower()).ToList();
            if (failing.Count > 0) list.Add($"Rooms that don't pass ({string.Join(", ", failing)}): the Rooms tab says why.");
            return list;
        }

        // A young colony's way in: what to do, in the order it matters, ticked off as it's done (gone once the colony is a village or
        // everything's done).
        internal static List<(bool done, string text)> Steps(Colony c)
        {
            ColonyRecord r = c.R;
            bool Room(RoomType t) => r.Rooms.Any(x => x.Ok && x.Type == t);
            return new List<(bool, string)>
            {
                (r.Flag("named") == 1, $"Name your hall: it's \"{r.Name}\" until you do (Rename, beside the name above)."),
                (c.Beds > 0, "Build a bed for a newcomer: by the pillars, or better in a house you mark as a longhouse (hammer → Misc → Mark rooms and zones)."),
                (r.Arrivals > 0, $"Draw Odin's eye: renown {Renown.Threshold(0)} brings the first of the fallen. Build, make beds comfortable, keep coins and valuables in chests."),
                (r.Arrivals > 0 && c.Population > 0, "Take in who the Valkyrie sets down at the pillars at dusk (a New tab opens here when they land)."),
                (r.Zones.Count > 0, "Mark work: with the hammer's \"Mark rooms and zones\", click a spot twice for a woodlot, quarry, fields, foraging or hunting ground. Villagers work what's marked and haul it to the chests."),
                (r.Roster.Any(x => x.State == SoulState.Active && x.Prof != Prof.Hauler), "Give each a trade: People tab → Details → choose their work (they say what they'd like)."),
                (Stock.Treasury(c) != null, $"Choose a treasury chest (the hammer's \"Mark rooms and zones\": look at a chest, Shift+E) and put coins in it: wages are paid from it every {Payroll.EveryDays} days."),
                (Stock.Chests(c).Any(x => x.category != "" && x.category != "treasury") || Room(RoomType.Stockpile), "Sort the stores: mark a stockpile room, or with the hammer's \"Mark rooms and zones\" look at a chest and press Shift+E."),
                (Room(RoomType.GreatHall), "Raise a great hall with a fire, tables and seats: villagers gather there at dusk to eat and talk."),
            };
        }
        // The next of the first steps, short, for the pillars' hover ("" once there are none).
        internal static string NextStep(Colony c)
        {
            if (c.R.Rank >= Rank.Village) return "";
            if (_nextFor == c && Time.time < _nextAt) return _next; // (asked every frame the pillars are looked at)
            var steps = Steps(c);
            int i = steps.FindIndex(st => !st.done);
            _nextFor = c; _nextAt = Time.time + 2f; _next = i < 0 ? "" : Short[i];
            return _next;
        }
        private static Colony _nextFor; private static float _nextAt; private static string _next = "";
        private static readonly string[] Short = { "name your hall", "build a bed", "raise renown to draw Odin's eye", "take in the newcomers", "mark work zones", "give them trades", "choose a treasury", "sort the stores", "raise a great hall" };
        // Village dogs: the colony's own, and tamed wolves in the colony that could be taken in.
        private static float DogsPart(RectTransform list, float y)
        {
            var dogs = Dogs.Of(_c).ToList(); var strays = Dogs.Strays(_c).ToList();
            if (dogs.Count == 0 && strays.Count == 0) return y;
            y = Heading(list, "Village dogs", y);
            foreach (Character d in dogs)
            {
                MonsterAI ai = d.GetComponent<MonsterAI>(); GameObject f = ai?.GetFollowTarget();
                Brain led = f != null ? Villagers.Brain(f.GetComponent<Character>()) : null; Player with = f != null ? f.GetComponent<Player>() : null;
                string name = d.GetComponent<Tameable>()?.GetText(); if (string.IsNullOrEmpty(name)) name = "A wolf";
                float px = 8; Character dog = d;
                y = Text(list, $"• {name}: {(led != null ? "at " + led.S.Name + "'s heels" : with != null ? "out with " + with.GetPlayerName() : "by the hearth")}", y, 16);
                Tip(Btn(list, "Let go", ref px, y, () => { Dogs.Release(dog); Build(); }), "Let them go", "No longer the village dog: a tame wolf as any other.");
                y += 50;
            }
            foreach (Character w in strays)
            {
                float px = 8; Character wolf = w;
                string name = w.GetComponent<Tameable>()?.GetText(); if (string.IsNullOrEmpty(name)) name = "A tame wolf";
                y = Text(list, $"• {name}, {Mathf.RoundToInt(Vector3.Distance(w.transform.position, _c.Position))} m from the pillars", y, 16, GameUi.Dim);
                Tip(Btn(list, "Take in as the village dog", ref px, y, () => { string why = Dogs.Adopt(_c, wolf); if (why != "") Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "Not now: " + why + "."); Build(); }),
                    "Village dog", "By day it follows the villagers at their work; at night it lies by the hall's hearth; and it barks when something hostile comes within 40 m, so the colony hears where from. A player can still take it along (E).");
                y += 50;
            }
            return y;
        }
        private static float FirstSteps(RectTransform list, float y)
        {
            var steps = Steps(_c);
            int done = steps.Count(st => st.done);
            if (_c.R.Rank >= Rank.Village || done == steps.Count) return y;
            y = Heading(list, $"First steps ({done} of {steps.Count} done)", y);
            bool next = true;
            foreach (var (ok, text) in steps)
            {
                y = Text(list, (ok ? "<color=#99e08c>✓</color> " : next ? "<color=#ffd27a>→</color> " : "· ") + text, y, 15, ok ? GameUi.Dim : next ? GameUi.Text : GameUi.Dim, 14);
                if (!ok) next = false;
            }
            return y;
        }

        private static bool _renaming;
        private static float NameBox(RectTransform list, float y)
        {
            ColonyRecord r = _c.R;
            y = Heading(list, "The hall's name", y);
            if (_rename == null || _renameFor != _c) { _rename = r.Name; _renameFor = _c; }
            var box = GameUi.InputField(list, new Vector2(8, -y), new Vector2(420, 38), _rename, "a name for the hall", 40, t => _rename = t, t => { _rename = t; Net.Act(_c, "rename", t); _renaming = false; UnityEngine.EventSystems.EventSystem.current?.SetSelectedGameObject(null); _shown = ""; }); // (Enter: done typing, and the window shows the new name)
            if (_renaming) box.ActivateInputField();
            float nx = 440; Tip(Btn(list, "Rename", ref nx, y - 2, () => { Net.Act(_c, "rename", _rename ?? ""); _renaming = false; }), "Rename the hall", "Its name in the colony window, on the pillars and in the saga. Enter in the box does it too.");
            return y + 52;
        }
        private static float Hall(RectTransform list)
        {
            ColonyRecord r = _c.R; float y = 4;
            if (_renaming) y = NameBox(list, y);
            var needs = NeedsYou(_c);
            if (needs.Count > 0)
            {
                y = Heading(list, "Needs you", y);
                foreach (string n in needs) y = Text(list, "• " + n, y, 16, GameUi.Warn);
            }
            var asks = r.Living.Select(row => (row, ask: Asks.Shown(Villagers.Body(row.Vid)?.GetComponent<ZNetView>()?.GetZDO()))).Where(x => x.ask != null).ToList();
            if (asks.Count > 0)
            {
                y = Heading(list, "Asked of you", y);
                foreach (var (row, ask) in asks) y = Text(list, $"• {row.Name}: {ask.Value.what} ({ask.Value.daysLeft} {(ask.Value.daysLeft == 1 ? "day" : "days")} left){(ask.Value.deed ? (Villagers.Body(row.Vid) is Humanoid hb && Villagers.Brain(hb)?.S.Disgraced == true ? " <color=#ff8c59>a deed, to wash out their disgrace</color>" : " <color=#9fb8a8>a deed, to lift their dimming</color>") : "")}", y, 16);
                y = Text(list, "Talk to them (E) to take it on, hand it over or tell of it. Answered, they think better of you, gain worth and pay from their own hoard, and the colony gains renown.", y, 14, GameUi.Dim);
            }
            y = DogsPart(list, y);
            y = FirstSteps(list, y);
            y = JarlsPart(list, y);
            y = Heading(list, "The year", y);
            y = Text(list, Calendar.Line(Year.Today()) + (Year.BySeasons ? " <color=#b8b0a0>(by the Seasons mod)</color>" : ""), y);
            y = Heading(list, "Odin's attention", y);
            y = Text(list, $"Renown <color=#ffd27a>{_c.Renown}</color> = wealth {_c.WealthPart} + comfort {_c.ComfortPart} + deeds {r.Deeds} + saga {_c.SagaPart}", y);
            y = Text(list, "Saga: what the jarls do first out in the world - a new land, a landmark found, a dungeon entered, further from home than before.", y, 14, GameUi.Dim);
            string why = Migration.Why(_c);
            y = Text(list, why == "" ? $"Odin may send someone at dawn (renown {Renown.Threshold(r.Arrivals)} reached)." : "No one will come yet: " + why + ".", y, 16, GameUi.Dim);
            if (r.Coming.Count > 0 && !r.ComingLanded) y = Text(list, $"{r.Coming.Count} on their way: they arrive at dusk.", y, 16, GameUi.Good);
            y = Heading(list, "Rank", y);
            y = Text(list, $"A {Tables.Of(r.Rank).Name.ToLower()} of {_c.Population} {(_c.Population == 1 ? "soul" : "souls")}. Room for {_c.Cap} in all: as many as there are beds ({_c.Beds}), up to {Tables.Of(r.Rank).Cap} at this rank. Unlocked: {Tables.Of(r.Rank).Unlocks}.", y);
            if (r.Rank < Rank.Hold) { Rank next = r.Rank + 1; string miss = Ranks.Missing(next, _c.Population, _c.WorkingRooms()); y = Text(list, $"To become a {Tables.Of(next).Name.ToLower()}: {(miss == "" ? "ready at the next check" : miss)}.", y, 16, GameUi.Dim); }
            y = GreatWorksSection(list, y);
            y = Heading(list, "The colony", y);
            Container treasury = Stock.Treasury(_c);
            y = Text(list, $"Treasury: {(treasury == null ? "<color=#ff8c59>none chosen</color> (the hammer's \"Mark rooms and zones\": look at a chest, Shift+E)" : Stock.Coins(treasury) + " coins" + Where(treasury.transform.position))}. Wages due every {Payroll.EveryDays} days: {Payroll.Due(Villagers.All.Where(b => b.S.Colony == _c.Id).Select(b => b.S), Payroll.EveryDays, _c.GuardExtra) * Plugin.Instance.WageScale.Value / 100} coins.{(r.MissedPaydays > 0 ? $" <color=#ff8c59>{r.MissedPaydays} missed paydays.</color>" : "")}", y);
            int owed = _c.Owed();
            if (owed > 0)
            {
                int held = treasury != null ? Stock.Coins(treasury) : 0;
                y = Text(list, $"<color=#ff8c59>Wages owed: {owed} coins ({_c.R.MissedPaydays} missed payday{(_c.R.MissedPaydays == 1 ? "" : "s")}).</color>" + (held < owed ? $" The treasury holds {held}." : ""), y);
                float px = 8;
                Tip(Btn(list, "Pay what's owed", ref px, y, () => Net.Act(_c, "backpay", ""), held >= owed), "Back pay", "Everything owed for the missed paydays, from the treasury at once: the unpaid are paid and stop grumbling. The next payday comes as it would.");
                y += 50;
            }
            int tribute = Tribute.Due(_c);
            if (tribute > 0 || Tribute.Raiding(_c))
            {
                int ask = Tribute.Raiding(_c) ? Mathf.CeilToInt(_c.R.Flag("tribute:ask") * 1.5f / 5f) * 5 : tribute;
                y = Heading(list, "The Fulings", y);
                y = Text(list, Tribute.Raiding(_c) ? $"The Fulings are raiding. {ask} coins from the treasury would turn them back." : $"A Fuling herald waits at the colony's edge: {ask} coins by dusk, or they raid. (Paid {_c.R.Flag("tribute:level")} times before: each time they want more.)", y, 16, GameUi.Warn);
                float tx = 8; Btn(list, $"Pay the Fulings {ask} coins", ref tx, y, () => Net.Act(_c, "tribute", ""));
                y += 52;
            }
            if (Visitors.MercOffered(_c) || Visitors.MercDaysLeft(_c) > 0)
            {
                y = Heading(list, "The Dvergr", y);
                y = Text(list, Visitors.MercDaysLeft(_c) > 0 ? $"Three Dvergr guard the colony for {Visitors.MercDaysLeft(_c)} more day(s)." : $"A Dvergr envoy offers three fighters for {Visitors.MercDays} days.", y, 16, GameUi.Text);
                float mx = 8; Btn(list, Visitors.MercDaysLeft(_c) > 0 ? $"Keep them on ({Visitors.MercCost} coins)" : $"Hire them ({Visitors.MercCost} coins)", ref mx, y, () => Net.Act(_c, "hire", ""));
                y += 52;
            }
            var follow = Spirit.Followers(_c).Where(f => f.fans > 0).Take(4).ToList();
            if (follow.Count > 0) y = Text(list, "Followed: " + string.Join(", ", follow.Select(f => $"{f.jarl} ({f.fans} of {f.of}{(f.fans * 10 > f.of * 6 ? ", loved" : "")})")) + ".", y, 15, GameUi.Dim);
            string feast = Feast.Ready(_c, out _, out _);
            y = Text(list, feast == "" ? "A feast can be held tonight." : "Feast: " + feast + ".", y, 16, GameUi.Dim);
            y += 6;
            float bx = 8;
            Tip(Btn(list, "Hold a feast", ref bx, y, () => Net.Act(_c, "feast", ""), feast == ""), "A feast", "Tonight in the great hall: food from the hall's chests (three kinds or more) and mead if there is some. Everyone's mood rises for days, and it's a deed for renown.");
            Tip(Btn(list, "Give out free beds", ref bx, y, () => Net.Act(_c, "autobeds", "")), "Free beds", "Everyone sleeping by the hall fire takes the nearest bed no one has. (It happens by itself at dusk too.)");
            y += 50; bx = 8;
            Tip(Btn(list, _c.R.Treasury == "" ? "Treasury: none yet" : "Treasury: chosen", ref bx, y, () => Player.m_localPlayer?.Message(MessageHud.MessageType.Center, "Take the hammer's \"Mark rooms and zones\", look at a chest and press Shift+E to make it the treasury."), true), "The treasury", "Wages are paid from the treasury every five days, and feasts, ransoms, tribute and hired swords are paid from it. Its coins count to renown. To choose it: the hammer's \"Mark rooms and zones\", look at a chest, Shift+E.");
            Tip(Btn(list, _c.HornUntil > Time.time ? "Sound the all-clear" : "Blow the war horn", ref bx, y, () => Net.Act(_c, "horn", _c.HornUntil > Time.time ? "off" : "on")), "The war horn", "Fighters arm themselves and go out to the foe; everyone else runs for the great hall. It sounds by itself when a raid comes.");
            y += 52;
            y = Heading(list, "Mods it works with", y);
            y = Text(list, string.Join("   ", Compat.Report().Where(m => m.present && m.name != "ClaudeTools").Select(m => m.name)) is string s && s.Length > 0 ? s : "none of them (Jarldom needs none)", y, 15, GameUi.Dim);
            // Outposts.
            string parent = Outposts.Parent(_c);
            if (parent != "")
            {
                Vector3 at = Outposts.Where(parent) ?? _c.Position;
                y = Text(list, $"An outpost of the colony {Mathf.RoundToInt(Utils.DistanceXZ(at, _c.Position))} m to the {ScoutTask.Compass(at - _c.Position)}: its treasury meets the wages this one's can't.", y, 15, GameUi.Dim);
                float ox = 8; Btn(list, "Stand alone", ref ox, y, () => Net.Act(_c, "outpost", "")); y += 52;
            }
            else
            {
                int n = Outposts.OutpostsOf(_c);
                if (n > 0) y = Text(list, $"{n} outpost{(n == 1 ? "" : "s")} look to this colony: their wages may fall to its treasury.", y, 15, GameUi.Dim);
                var mains = Outposts.Candidates(_c);
                if (mains.Count > 0)
                {
                    y = Text(list, "Make this an outpost of another of your colonies (its own rank and people; the main colony's treasury meets its wages):", y, 15, GameUi.Dim);
                    float ox = 8;
                    foreach (var m in mains.Take(3)) { string id = m.id; Btn(list, $"Outpost of the one {Mathf.RoundToInt(Utils.DistanceXZ(m.at, _c.Position))} m {ScoutTask.Compass(m.at - _c.Position)}", ref ox, y, () => Net.Act(_c, "outpost", id)); }
                    y += 52;
                }
            }
            y += 4;
            // The hall's name: the founder's by default; any name the jarl likes.
            if (!_renaming) y = NameBox(list, y);
            // Who runs it: the founder, and those the founder lets (they mark rooms, set trades, call the Thing).
            Player me = Player.m_localPlayer;
            bool founder = me != null && (r.Founder == 0 || me.GetPlayerID() == r.Founder || me.GetPlayerName() == r.FounderName);
            y = Text(list, $"Founded by {r.FounderName}." + (r.Allowed.Count > 0 ? " Run with them by " + string.Join(", ", r.Allowed) + "." : "") + (founder ? "" : _c.Allows(me) ? " You run it with them." : $" Only {r.FounderName} and those they let can run it: mark rooms, set trades, call the Thing."), y, 15, GameUi.Dim);
            if (founder)
            {
                bx = 8;
                foreach (string name in r.Allowed.ToList()) { string n = name; Tip(Btn(list, $"Stop {n} running it", ref bx, y, () => Net.Act(_c, "disallow", n)), n, $"{n} can no longer mark rooms, set trades or call the Thing here."); }
                foreach (Player other in Player.GetAllPlayers().Where(p => p != null && p != me && !r.Allowed.Contains(p.GetPlayerName()) && Utils.DistanceXZ(p.transform.position, _c.Position) < _c.Radius + 60))
                { string n = other.GetPlayerName(); Tip(Btn(list, $"Let {n} run it", ref bx, y, () => Net.Act(_c, "allow", n)), n, $"{n} may mark rooms and zones, set trades and beds, take newcomers in and call the Thing, as you do. Only you choose who."); }
                if (bx > 8) y += 50;
            }
            y = Text(list, "Disband (Jarldom's way out: everyone's things in chests by the pillars):", y, 15, GameUi.Dim);
            bx = 8; Btn(list, "Disband the colony", ref bx, y, () => Confirm("disband"), true);
            if (_confirm == "disband") { Btn(list, "Yes: disband", ref bx, y, () => { _confirm = ""; Net.Act(_c, "disband", ""); }, true); }
            return y + 52;
        }
        private static string _confirm = "", _rename; private static Colony _renameFor;
        private static void Confirm(string what) { _confirm = _confirm == what ? "" : what; Build(); }
        // Great works (Core/GreatWorks.cs): what the Thing chose, where it stands and how far it's come.
        private static float GreatWorksSection(RectTransform list, float y)
        {
            string work = GreatWorks.Lighthouse; int stage = GreatWorks.Stage(_c.R.Flags, work);
            if (stage == 0 && _c.R.Rank < Rank.Village) return y;
            y = Heading(list, "Great works", y);
            if (stage == 0) return Text(list, GreatWork.Shore(_c) != null ? "None yet. A village by the sea may be asked at the Thing to raise a lighthouse: the whole colony's work, remembered for good." : "None yet: a village by the sea may be asked to raise a lighthouse.", y, 15, GameUi.Dim);
            if (stage == GreatWorks.Promised)
            {
                return Text(list, $"The Thing chose to raise {GreatWorks.Name(work)}: a tall timber tower on a stone plinth, ladders inside up to a railed gallery hung with banners, and the beacon's bonfire on a dragon-headed fire deck 24 m up ({GreatWorks.NeedsText(work)}). Show where it stands: with the hammer's \"Mark rooms and zones\", outline its site as you would a zone (fairly flat ground inside the colony near the sea, 9 × 9 m at least) and choose \"{GreatWorks.Title(work)}\" under Great work. It rises in the middle of the outline, its door toward the colony, and the builders raise it from the stores.", y);
            }
            int all = GreatWorks.Parts(work).Count, built = _c.R.Flag(GreatWorks.BuiltKey(work));
            if (stage == GreatWorks.Laid)
            {
                Vector3 at = GreatWork.At(_c, work, out _);
                if (GreatWork.Movable(_c, work)) y = Text(list, "Not begun yet: outline another site with \"Mark rooms and zones\" and choose \"Move it here\" under Great work to move it.", y, 15, GameUi.Dim);
                y = Text(list, $"{GreatWorks.Title(work)} is rising {Mathf.RoundToInt(Utils.DistanceXZ(at, _c.Position))} m to the {ScoutTask.Compass(at - _c.Position)} of the high seat: {built} of {all} parts stand. Builders raise it from the stores ({GreatWorks.NeedsText(work)} in all).", y);
                return _c.R.Living.Any(r => r.Prof == Prof.Builder) ? y : Text(list, "No one is a builder: give someone the Builder trade (People tab).", y, 15, GameUi.Warn);
            }
            return Text(list, $"{GreatWorks.Title(work)}, raised on day {_c.R.Flag(GreatWorks.DayKey(work))}. {GreatWorks.Gift(work)}", y, 16, GameUi.Good);
        }
        private static Button Btn(RectTransform list, string text, ref float x, float y, Action click, bool enabled = true)
        {
            float w = Mathf.Max(150, text.Length * 9.5f + 30);
            Button b = GameUi.Button(list, text, new Vector2(x, -y), new Vector2(w, 42), click, enabled, 16);
            x += w + 8;
            return b;
        }
        private static void Tip(Component on, string title, string text) => GameUi.Tip(on, title, text);

        private static float Newcomers(RectTransform list)
        {
            ColonyRecord r = _c.R; float y = 4;
            y = Text(list, "The Valkyrie set them down at the pillars. They fell in battle and Odin sent them here: will you take them in? (Turned away, they fly on. Unanswered, they wait.)", y, 16, GameUi.Dim);
            int free = _c.FreeBeds().Count;
            if (free < r.Coming.Count) y = Text(list, free == 0 ? "There's no free bed for them: they'd sleep by the hall fire, unhappily, and one with no bed who dies is slow to rise again, by the pillars. Build a bed first." : $"Only {free} free bed{(free == 1 ? "" : "s")} for {r.Coming.Count}.", y, 15, GameUi.Warn);
            for (int i = 0; i < r.Coming.Count; i++)
            {
                Newcomer n = r.Coming[i]; int idx = i;
                y = Heading(list, $"{n.Name}", y);
                y = Text(list, string.Join(", ", n.Traits.Split(',').Select(t => $"<color=#ffd27a>{Tables.Trait(t)?.Name}</color> ({Tables.Trait(t)?.Text})")) + $". Would be a {Tables.Of(n.Prof).Name.ToLower()}.", y);
                y = Text(list, n.Story, y, 16, GameUi.Dim);
                float bx = 8;
                Btn(list, "Take them in", ref bx, y, () => Net.Act(_c, "accept", idx.ToString()));
                Btn(list, "Turn them away", ref bx, y, () => Net.Act(_c, "refuse", idx.ToString()));
                y += 52;
            }
            return y;
        }

        // The living, the newest first so a newcomer is easy to find: by when they came, else by their place on the roster (which grows as
        // they come).
        public static List<RosterRow> Newest(Colony c, out Dictionary<string, double> came)
        {
            came = Villagers.All.Where(b => b.S.Colony == c.Id).GroupBy(b => b.S.Vid).ToDictionary(g => g.Key, g => g.First().S.Arrived);
            var order = c.R.Roster.Select((r, i) => (r.Vid, i)).GroupBy(x => x.Vid).ToDictionary(g => g.Key, g => g.Last().i);
            var when = came;
            return c.R.Roster.Where(r => r.State == SoulState.Active).OrderByDescending(r => when.TryGetValue(r.Vid, out double t) ? t : -1).ThenByDescending(r => order[r.Vid]).ToList();
        }
        private static float People(RectTransform list)
        {
            float y = 4;
            var living = Newest(_c, out var came);
            int today = Clock.Day(ZNet.instance.GetTimeSeconds());
            if (living.Count == 0) return Text(list, "No one lives here yet. Raise renown (build, beds with comfort, deeds) and Odin will send the fallen.", y, 16, GameUi.Dim);
            y = Text(list, "The newest first. Details shows why each feels as they do and lets you choose their work. Mood sets how fast they work.", y, 15, GameUi.Dim);
            var moods = living.Select(r => Villagers.Body(r.Vid)?.GetComponent<ZNetView>()?.GetZDO()).Where(z => z != null).Select(z => (double)z.GetInt(Villagers.MoodKey, 0)).ToList();
            if (HallSound.ForcedMood is double forced ? Soundscape.Miserable(forced) : moods.Count > 0 && Soundscape.Miserable(moods.Average()))
                y = Text(list, "The hall is quiet. They are too miserable for talk or songs, and work with half a heart. See Details for what they lack.", y, 16, GameUi.Warn);
            foreach (var held in Captives.Held(_c).ToList())
            {
                string vid = held.vid;
                y = Text(list, $"<color=#ff8c59>{held.name} is held at a Fuling camp</color>, {Mathf.RoundToInt(Utils.DistanceXZ(held.at, _c.Position))} m to the {ScoutTask.Compass(held.at - _c.Position)} (pinned on the map), {held.daysLeft} day(s) left. Go there to free them, or:", y, 16, GameUi.Text);
                float rx = 8; Btn(list, $"Pay their ransom ({Captives.Ransom(_c)} coins)", ref rx, y, () => Net.Act(_c, "ransom", vid));
                y += 52;
            }
            Skills.SkillType? master = Visitors.Master(_c);
            if (master != null) y = Text(list, $"A master of {Visitors.Skill(master.Value)} is in the hall today: {Visitors.TrainCost} coins teaches one of them +10 (Teach).", y, 16, GameUi.Good);
            foreach (RosterRow row in living)
            {
                if (master != null) { string pupil = row.Vid; GameUi.Button(list, "Teach", new Vector2(W - 86 - 6 - 74, -y + 2), new Vector2(74, 30), () => Net.Act(_c, "train", pupil), true, 14); }
                Humanoid h = Villagers.Body(row.Vid);
                ZDO z = h?.GetComponent<ZNetView>()?.GetZDO();
                string doing = z != null ? Villagers.Tags(z).GetString(Villagers.DoingKey, "") : "far away";
                int mood = z?.GetInt(Villagers.MoodKey, 0) ?? 0;
                bool fresh = came.TryGetValue(row.Vid, out double arrived) && arrived > 0 && today - Clock.Day(arrived) < 2;
                GameUi.Line(list, row.Name + (fresh ? " <size=80%><color=#99e08c>new</color></size>" : ""), new Vector2(8, -y), 250, 18, GameUi.Warm);
                GameUi.Line(list, Tables.Of(row.Prof).Name, new Vector2(262, -y), 130, 16, GameUi.Text);
                GameUi.Line(list, doing, new Vector2(396, -y), 230, 15, GameUi.Dim);
                GameUi.Line(list, $"mood {Windows.Signed(mood)}  worth {Mathf.RoundToInt(row.Worth)}{(string.IsNullOrEmpty(row.Bed) ? "  <color=#ff8c59>no bed</color>" : "")}", new Vector2(630, -y), W - 630 - (master != null ? 172 : 92), 15, mood < -30 ? GameUi.Warn : GameUi.Text);
                string vid = row.Vid;
                if (h != null) Tip(GameUi.Button(list, "Details", new Vector2(W - 86, -y + 2), new Vector2(86, 30), () => VillagerPanel.Open(h, true), true, 14), row.Name, "Their mood and why, needs, skills, gear and bag; choose their work and their bed.");
                y += 36;
            }
            var lost = _c.R.Roster.Where(r => r.State != SoulState.Active).ToList();
            if (lost.Count > 0) { y = Heading(list, "Gone", y); y = Text(list, string.Join(", ", lost.Select(r => $"{r.Name} ({r.State.ToString().ToLower()})")), y, 15, GameUi.Dim); }
            if (_c.R.Fallen.Count > 0) y = Text(list, $"Rising soon: {string.Join(", ", _c.R.Fallen.Select(f => VillagerState.Load(f.State)?.Name ?? "?"))}.", y, 15, GameUi.Dim);
            return y;
        }

        private static float Rooms(RectTransform list)
        {
            float y = 4;
            y = Text(list, "Mark rooms and work zones with the hammer: Misc → Mark rooms and zones. For a room, Ctrl+Shift+click on its floor: it's found from its walls. Or click the corners on the floor, click the first corner again, then choose what it is. Click one spot twice for a round work zone. E on a marked room or zone (marker out) checks, changes, widens or removes it.", y, 15, GameUi.Dim);
            foreach (Room room in _c.R.Rooms)
            {
                string name = Tables.Of(room.Type).Name + (room.Type == RoomType.Temple && room.God != "" ? " of " + Tables.God(room.God)?.Name : "");
                GameUi.Line(list, name, new Vector2(8, -y), 260, 18, GameUi.Warm);
                GameUi.Line(list, room.Ok ? $"grade {room.Grade} ({room.Points} points)" : "<color=#ff8c59>not working</color>", new Vector2(272, -y), 220, 16, GameUi.Text);
                GameUi.Line(list, $"{Math.Round(Geometry.Area(room.Xz))} m², {Where(room.Xz, room.Floor)}", new Vector2(496, -y), W - 496 - 196, 15, GameUi.Dim);
                string id = room.Id;
                GameUi.Button(list, "Check", new Vector2(W - 190, -y + 2), new Vector2(90, 30), () => Net.Act(_c, "check", id), true, 15);
                GameUi.Button(list, "Remove", new Vector2(W - 96, -y + 2), new Vector2(96, 30), () => Net.Act(_c, "unroom", id), true, 15);
                y += 34;
                if (!room.Ok && room.Failures != "") y = Text(list, room.Failures, y, 15, GameUi.Warn, 24);
            }
            if (_c.R.Zones.Count > 0) y = Heading(list, "Work zones", y);
            foreach (Zone z in _c.R.Zones)
            {
                GameUi.Line(list, Tables.Of(z.Type).Name, new Vector2(8, -y), 260, 18, GameUi.Warm);
                string sows = z.Type == ZoneType.Fields ? Work.CropLine(_c, z) : "";
                GameUi.Line(list, $"{Math.Round(Geometry.Area(z.Xz))} m², {Where(z.Xz, z.Floor)}: {(sows != "" ? sows : Tables.Of(z.Type).Text)}", new Vector2(272, -y), W - 272 - 104, 15, GameUi.Dim);
                string id = z.Id;
                GameUi.Button(list, "Remove", new Vector2(W - 96, -y + 2), new Vector2(96, 30), () => Net.Act(_c, "unroom", id), true, 15);
                y += 34;
            }
            y = Heading(list, "Stockpiles", y);
            var chests = Stock.Chests(_c);
            y = Text(list, chests.Count == 0 ? "No chests in the colony." : $"{chests.Count} chests: " + string.Join(", ", chests.GroupBy(c => c.category == "" ? "unsorted" : c.category).Select(g => $"{g.Count()} {g.Key}")), y, 15, GameUi.Dim);
            y = Text(list, "To choose what a chest holds (or make it the treasury): take the hammer's \"Mark rooms and zones\", look at the chest and press Shift+E. Chests in a stockpile room hold anything unless chosen.", y, 15, GameUi.Dim);
            y -= 46;
            return y + 46;
        }

        // Where a marked room or zone is, from the pillars ("12 m north-east", "upstairs, 20 m west", "by the pillars").
        private static string Where(IList<float> xz, float floor)
        {
            float x = 0, z = 0; int n = xz.Count / 2;
            for (int i = 0; i < n; i++) { x += xz[i * 2]; z += xz[i * 2 + 1]; }
            Vector3 d = new Vector3(x / n, 0, z / n) - new Vector3(_c.Position.x, 0, _c.Position.z);
            float up = floor - _c.Position.y;
            string storey = up > 2.5f ? "upstairs, " : "";
            return d.magnitude < 6 ? storey + "by the pillars" : $"{storey}{Mathf.RoundToInt(d.magnitude)} m {ScoutTask.Compass(d)}";
        }
        private static readonly Dictionary<string, string> Kinds = new Dictionary<string, string>
        {
            ["chop"] = "Felling", ["mine"] = "Mining", ["harvest"] = "Harvesting", ["pick"] = "Foraging", ["hunt"] = "Hunting", ["haul"] = "Hauling",
            ["plant"] = "Planting saplings", ["sow"] = "Sowing", ["fish"] = "Fishing", ["fuel"] = "Fuelling fires", ["smelt"] = "Smelting", ["cook"] = "Cooking",
            ["craft"] = "Commissions", ["ferment"] = "Fermenters", ["tap"] = "Tapping fermenters", ["feed"] = "Feeding beasts", ["cull"] = "Culling the herd",
            ["guard"] = "Guarding", ["build"] = "Building",
        };
        private static string KindName(string k) => Kinds.TryGetValue(k, out string n) ? n : char.ToUpper(k[0]) + k.Substring(1);

        private static float WorkTab(RectTransform list)
        {
            float y = 4;
            var orders = _c.IsOwner ? _c.Board.All.ToList() : JobBoard.FromSnapshot(_c.BoardBytes());
            int open = orders.Count(o => o.State == OrderState.Open), claimed = orders.Count(o => o.State == OrderState.Claimed), blocked = orders.Count(o => o.State == OrderState.Blocked);
            y = Text(list, $"{open} open, {claimed} being done, {blocked} blocked. Done today: {_c.DoneToday}.", y);
            y = Text(list, "Jobs come from what you mark (zones and rooms), what lies about, and fires and stations running low. Villagers take the jobs of their trade; haulers take any carrying and keep the fires and torches fed, and anyone idle helps them.", y, 15, GameUi.Dim);
            int alone = _c.LeftAloneCount;
            if (alone > 0)
            {
                Tip(GameUi.Button(list, "Try them again", new Vector2(W - 160, -y + 2), new Vector2(160, 30), () => Net.Act(_c, "tryreach", ""), true, 14), "Try them again", "The haulers go for the fires and torches they gave up on, once more.");
                string say = $"{(alone == 1 ? "A fire or torch" : alone + " fires and torches")} no one can walk to {(alone == 1 ? "is" : "are")} left alone (out on a dock, up on a roof). Build a way to {(alone == 1 ? "it" : "them")}: they're tried again in {Colony.AloneDays} days, or now.";
                float sw = W - 24 - 180; TMP_Text st = GameUi.Label(list, say, new Vector2(24, -y), new Vector2(sw, 20), 15, GameUi.Dim);
                float sh = st.GetPreferredValues(say, sw, 0).y; ((RectTransform)st.transform).sizeDelta = new Vector2(sw, sh);
                y += Math.Max(36, sh + 8);
            }
            y = Commissions(list, y);
            y = Heading(list, "Stores to keep", y);
            foreach (var (cat, who, verb, step) in new[] { ("wood", "Woodcutters", "fell trees", 500), ("stone", "Miners", "quarry stone", 500), ("ore", "Miners", "go out to claims for each kind of ore", 100), ("coal", "Smelters", "burn wood into coal in the kilns (always leaving 50 wood for building and fires)", 50) })
            {
                int keep = Stock.Keep(_c, cat), held = cat == "coal" ? Stock.Count(_c, "Coal") : Stock.Held(_c, cat); string k = cat; int st = step;
                string now = cat != "ore" ? $"Now {held}." : "Now " + string.Join(", ", new[] { "copperore", "tinore", "ironscrap", "silverore", "blackmetalscrap" }.Select(o => (o, n: Claims.Held(_c, o))).Where(x => x.n > 0 || x.o == "copperore" || x.o == "tinore").Select(x => $"{x.n} {Claims.Name(x.o).Replace(" ore", "").Replace("scrap ", "")}")) + ".";
                GameUi.Line(list, $"{who} {verb} until the stores hold {(keep == 0 ? "any amount (no limit)" : keep + (cat == "ore" ? " of it" : " " + cat))}. {now}", new Vector2(24, -y), W - 24 - 250, 15, cat != "ore" && keep > 0 && held >= keep ? GameUi.Good : GameUi.Text);
                GameUi.Button(list, $"−{st}", new Vector2(W - 240, -y + 2), new Vector2(76, 28), () => Net.Act(_c, "keep", $"{k}|{(keep == 0 ? st * 10 : Math.Max(st, keep - st))}"), true, 14);
                GameUi.Button(list, $"+{st}", new Vector2(W - 160, -y + 2), new Vector2(76, 28), () => Net.Act(_c, "keep", $"{k}|{(keep == 0 ? 0 : keep + st)}"), true, 14);
                GameUi.Button(list, "no limit", new Vector2(W - 80, -y + 2), new Vector2(80, 28), () => Net.Act(_c, "keep", $"{k}|0"), keep != 0, 13);
                y += 32;
            }
            y = Heading(list, "Ore claims", y);
            var claims = Claims.Of(_c).ToList();
            if (claims.Count == 0) y = Text(list, "None yet. Find a deposit in the wilds (copper, tin, silver; the iron in a sunken crypt) and drive an ore claim in beside it (hammer → Misc): the miners go out to it and bring the ore home. A camp round the stake (a bed, a chest and a fire) lets them work everything near it, as many as it has beds, staying longer.", y, 15, GameUi.Dim);
            foreach (Claim cl in claims.OrderBy(x => Utils.DistanceXZ(Claims.At(x), _c.Position)))
            {
                var (wound, death) = Mining.Risk(cl.Biome, cl.Crypt, cl.Camp, false);
                int out_ = Claims.Out(_c, cl.Id);
                y = Text(list, $"<color=#ffd27a>{Claims.Title(cl)}</color>, {Claims.Where(cl, _c.Position)}: {Claims.Ores(cl)}{(cl.Ore > 0 ? $" (pickaxe tier {cl.Tier})" : "")}. {(cl.Camp ? $"Camp with {cl.Beds} bed{(cl.Beds == 1 ? "" : "s")}." : "Bare claim.")}{(out_ > 0 ? $" {out_} out there now." : "")}{(wound + death > 0 ? $" Danger {Mathf.RoundToInt((wound + death) * 100)}%." : "")}", y, 15);
            }
            if (_c.R.Rank >= Rank.Village || _c.R.Roster.Any(x => x.State == SoulState.Active && x.Prof == Prof.Trader))
            {
                y = Heading(list, "Trade runs", y);
                foreach (string line in Trade.Plan(_c)) y = Text(list, line, y, 15, GameUi.Dim);
            }
            foreach (var g in orders.Where(o => o.State != OrderState.Done && o.State != OrderState.Cancelled).GroupBy(o => o.Kind).OrderByDescending(g => g.Count()))
            {
                y = Heading(list, $"{KindName(g.Key)} ({g.Count()})", y);
                foreach (Order o in g.OrderByDescending(o => o.Priority).Take(8))
                {
                    string who = o.State == OrderState.Claimed ? Villagers.ByVid(o.Vid)?.S.Name ?? _c.R.Row(o.Vid)?.Name ?? "someone" : "";
                    string state = o.State == OrderState.Claimed ? "<color=#99e08c>" + who + "</color>" : o.State == OrderState.Blocked ? "<color=#ff8c59>blocked: " + o.Blocked + "</color>" : !string.IsNullOrEmpty(o.Blocked) ? "<color=#ffd27a>waiting: " + o.Blocked + "</color>" : "open";
                    GameUi.Line(list, $"{Tables.Of(o.Prof).Name} · {o.By} · priority {o.Priority}", new Vector2(24, -y), 360, 15, GameUi.Text);
                    GameUi.Line(list, state, new Vector2(390, -y), 480, 15, GameUi.Text);
                    y += 24;
                }
            }
            return y + 10;
        }

        // Things to be made for the stores: smiths at the forge and workbench, cooks at the cauldron.
        private static bool _offerAll;
        private static float Commissions(RectTransform list, float y)
        {
            y = Heading(list, "Commissions", y);
            var wanted = Crafts.Commissions(_c);
            if (wanted.Count == 0) y = Text(list, "Nothing commissioned. Smiths make what's asked for at the forge and workbench, cooks at the cauldron, from the stockpiles.", y, 15, GameUi.Dim);
            foreach (var kv in wanted.OrderBy(k => k.Key))
            {
                string prefab = kv.Key; Recipe r = Crafts.RecipeFor(prefab);
                string note = r == null ? "no recipe" : Crafts.StationFor(_c, r, _c.Position) == null ? "<color=#ff8c59>no " + Localization.instance.Localize(r.m_craftingStation?.m_name ?? "station").ToLower() + " here</color>"
                    : !Crafts.CanAfford(_c, r) ? "<color=#ffd27a>the stores lack " + string.Join(", ", Crafts.Needs(r).Where(n => Crafts.InStock(_c, n.item) < n.amount).Select(n => Crafts.Name(n.item).ToLower())) + "</color>"
                    : "by a " + (Crafts.Maker(r) == Prof.Cook ? "cook" : "smith");
                GameUi.Line(list, $"{kv.Value} × {Crafts.Name(prefab)}", new Vector2(24, -y), 300, 16, GameUi.Text);
                GameUi.Line(list, note, new Vector2(330, -y), 420, 15, GameUi.Text);
                GameUi.Button(list, "+1", new Vector2(W - 120, -y + 2), new Vector2(54, 28), () => Net.Act(_c, "craft", prefab + "|1"), true, 14);
                GameUi.Button(list, "−1", new Vector2(W - 60, -y + 2), new Vector2(54, 28), () => Net.Act(_c, "craft", prefab + "|-1"), true, 14);
                y += 30;
            }
            // (the smith's own work: what's on the board for them, and why)
            var own = _c.Board.All.Where(o => o.Kind == "craft" && o.Target.StartsWith(Smithing.Prefix) && o.State != OrderState.Done && o.State != OrderState.Cancelled).ToList();
            if (own.Count > 0)
            {
                y = Text(list, "The smith's own work:", y + 4, 15, GameUi.Dim);
                foreach (Order o in own)
                {
                    string prefab = o.Target.Substring(Smithing.Prefix.Length), why = Smithing.Reason(_c, prefab);
                    GameUi.Line(list, Crafts.Name(prefab), new Vector2(24, -y), 300, 16, GameUi.Text);
                    GameUi.Line(list, o.State == OrderState.Blocked && o.Blocked != "" ? $"<color=#ffd27a>{o.Blocked}</color>" : why, new Vector2(330, -y), W - 340, 15, GameUi.Dim);
                    y += 26;
                }
            }
            var offered = Crafts.Offered(_c, Player.m_localPlayer);
            y = Text(list, offered.Count == 0 ? "Build a workbench, forge or cauldron in the colony to commission things." : "Commission one (what you know how to make, at the stations here):", y + 4, 15, GameUi.Dim);
            float bx = 8; int shown = 0;
            foreach (Recipe r in offered)
            {
                if (!_offerAll && shown >= 36) break;
                string prefab = r.m_item.name, label = Localization.instance.Localize(r.m_item.m_itemData.m_shared.m_name);
                if (label.Length > 18) label = label.Substring(0, 17) + "…";
                if (bx > W - 170) { bx = 8; y += 36; }
                GameUi.Button(list, label, new Vector2(bx, -y), new Vector2(166, 32), () => Net.Act(_c, "craft", prefab + "|1"), true, 13);
                bx += 172; shown++;
            }
            if (offered.Count > 36) { if (bx > W - 170) { bx = 8; y += 36; } GameUi.Button(list, _offerAll ? "fewer" : $"all {offered.Count}…", new Vector2(bx, -y), new Vector2(166, 32), () => { _offerAll = !_offerAll; Build(); }, true, 13); }
            return y + 46;
        }

        private static float ThingTab(RectTransform list)
        {
            float y = 4;
            var petitions = Thing.Pending(_c);
            y = Text(list, "The Thing is the colony's assembly. Matters are raised day by day and wait here; nothing is decided until you call the Thing. Then everyone gathers in the great hall (at the pillars before there is one), each petitioner comes before the high seat in turn and speaks, and you rule there. Matters left unheard five days sour the colony.", y, 15, GameUi.Dim);
            float bx = 8;
            if (Thing.Held(_c))
            {
                Petition cur = Thing.Current(_c);
                string who = cur == null ? "" : cur.ByName != "" ? cur.ByName : "A voice";
                string state = _c.R.Flag(Thing.Cur) == 0 ? "The Thing is gathering" + (_c.IsOwner ? $": {Thing.Gathered(_c, out int expected)} of {expected} here." : ".")
                    : cur == null ? "The Thing sits." : Thing.Ready(_c) ? $"{who} has spoken. Rule on it in the hall (or here)." : $"{who} comes forward to speak.";
                y = Text(list, state, y + 4, 18, GameUi.Good);
                Tip(Btn(list, "Adjourn the Thing", ref bx, y, () => Net.Act(_c, "thingend", "")), "Adjourn", "End the Thing now; the matters not yet heard wait for the next one.");
                y += 50;
            }
            else
            {
                string why = Thing.CanCall(_c);
                Tip(Btn(list, "Call the Thing", ref bx, y + 4, () => Net.Act(_c, "thingcall", ""), why == ""), "Call the Thing", "Everyone stops what they're doing and gathers in the great hall. Go there too: each petitioner speaks before the high seat, and you rule on each matter in turn.");
                if (why != "") GameUi.Line(list, why + ".", new Vector2(bx + 8, -y - 12), W - bx - 20, 15, GameUi.Dim);
                y += 54;
            }
            if (petitions.Count == 0) y = Text(list, "No one has a matter for the Thing yet. They come up as the colony lives: beds short, wages owed, a thief, a feud.", y, 16, GameUi.Dim);
            else y = Heading(list, $"Matters waiting ({petitions.Count})", y);
            for (int i = 0; i < petitions.Count; i++)
            {
                Petition p = petitions[i]; int idx = i;
                int waited = _c.Day - p.Day;
                y = Text(list, "“" + p.Text + "”", y + 2, 18, GameUi.Text);
                y = Text(list, $"{(p.ByName != "" ? "Raised by " + p.ByName : "Raised")} {(waited <= 0 ? "today" : waited == 1 ? "yesterday" : waited + " days ago")}. {p.Yes}: {Effect(p.YesMood)}; {p.No.ToLower()}: {Effect(p.NoMood)}.", y - 2, 14, waited >= Petitions.UnheardAfter ? GameUi.Warn : GameUi.Dim, 24);
                if (Thing.Ready(_c) && Thing.Current(_c) == p)
                {
                    float rx = 8;
                    Btn(list, p.Yes, ref rx, y, () => Net.Act(_c, "rule", idx + "|yes|" + p.Key));
                    Btn(list, p.No, ref rx, y, () => Net.Act(_c, "rule", idx + "|no|" + p.Key));
                    y += 50;
                }
            }
            return y;
        }
        private static string Effect(int mood) => mood > 0 ? "pleases them" : mood < 0 ? "angers them" : "no one minds";

        private static float Gods(RectTransform list)
        {
            float y = 4;
            y = Text(list, "Build a god's altar in the colony (hammer → Misc) and give it what the god wants: E gives the best such thing you carry, or use an item from your bag on it. A gothi makes offerings from the stores each morning. A god's blessing works while they're pleased and have a temple (a room around their altar, marked as their temple). Coins dropped into a sacred bog zone buy Odin's favour without end.", y, 15, GameUi.Dim);
            if (Compat.Has("Omens")) y = Text(list, $"Odin's favour is the gods' favour of Omens: offerings to Odin move it a step for every 20 points of favour ({_c.R.Flag("omenspool")} of 20 so far).", y, 15, GameUi.Dim);
            foreach (GodInfo g in Tables.Gods)
            {
                int f = g.Id == "odin" ? Faith.Odin(_c) : _c.FavourOf(g.Id);
                bool temple = _c.R.Rooms.Any(r => r.Ok && (g.Id == "odin" ? r.Type == RoomType.OdinShrine : r.Type == RoomType.Temple && r.God == g.Id));
                GameUi.Line(list, g.Name, new Vector2(8, -y), 120, 20, GameUi.Warm);
                GameUi.Bar(list, new Vector2(132, -y - 2), new Vector2(220, 22), Mathf.Abs(f) / 100f, f >= 0 ? GameUi.Gold : GameUi.Warn, $"{Faith.FavourWord(f)} ({f})"); // (filled toward favour or wrath from nothing)
                GameUi.Line(list, $"{(temple ? "<color=#99e08c>has a temple</color>. " : "")}wants {g.Offerings}; {g.Blessing}", new Vector2(364, -y), W - 370, 15, GameUi.Dim);
                y += 34;
            }
            y += 6;
            y = Text(list, $"The bog has taken {_c.R.BogPaid} offerings; the next favour costs {Favour.BogPrice(_c.R.BogPaid)} coins.", y, 15, GameUi.Dim);
            var worthy = Villagers.All.Where(b => b.S.Colony == _c.Id && b.S.Worthy).ToList();
            if (worthy.Count > 0)
            {
                y = Heading(list, "Worthy of Valhalla", y);
                foreach (Brain b in worthy)
                {
                    string why = Ascension.Why(_c, b.S), vid = b.S.Vid;
                    GameUi.Line(list, b.S.FullName, new Vector2(8, -y), 300, 18, GameUi.Warm);
                    GameUi.Button(list, "Send to Valhalla", new Vector2(320, -y + 2), new Vector2(200, 32), () => Net.Act(_c, "ascend", vid), why == "", 15);
                    if (why != "") GameUi.Line(list, why, new Vector2(530, -y), W - 530, 14, GameUi.Dim);
                    y += 38;
                }
            }
            return y;
        }

        private static float Saga(RectTransform list)
        {
            float y = 4;
            var lines = Stories.Chronicle(_c.R.Log, 40);
            if (lines.Count == 0) return Text(list, "Nothing has happened yet that a skald would sing of.", y, 16, GameUi.Dim);
            for (int i = lines.Count - 1; i >= 0; i--) y = Text(list, lines[i], y, 16, i == lines.Count - 1 ? GameUi.Text : GameUi.Dim);
            return y;
        }
    }
}
