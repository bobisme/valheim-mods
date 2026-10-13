using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace Jarldom
{
    // What villagers ask of the jarl (Requests, in Core, chooses the ask): made at dawn on the game that owns the villager, shown on
    // their hover and panel and in the hall. A player who takes one on carries it in their own character (a pin for a place, the
    // kill noted as it happens, anywhere in the world); bringing the things, or telling of the deed, answers it once.
    internal static class Asks
    {
        public const string Key = "jarldom_ask";      // on the soul: deed \u001f day \u001f the short form \u001f id, "" for none
        public const string TakenKey = "jarldom_asks"; // in the player's character: what they took on
        private const float Chance = 0.2f, SeenWithin = 30f;

        // ---- the villager's side (the game that owns them) ----
        public static void Daily(Brain b)
        {
            VillagerState s = b.S; Colony c = b.Colony;
            if (c == null || !c.Valid) return;
            int day = Clock.Day(b.Now);
            if (s.Asked != null && Requests.Expired(s.Asked, day)) s.Asked = null; // (lapsed: no harm done)
            if (s.Asked != null && s.Disgraced && !s.Asked.Deed) s.Asked.Deed = true; // (what they asked already is the deed now)
            bool roll = s.Count("askday") != day;
            if (s.Asked == null && (s.Disgraced || roll && b.Now - s.Arrived > Clock.DayLength))
            {
                s.Counts["askday"] = day;
                int asking = Villagers.All.Count(o => o.S.Colony == s.Colony && o.S.Asked != null), here = Villagers.All.Count(o => o.S.Colony == s.Colony);
                var rng = new System.Random(s.Vid.GetStableHashCode() ^ day * 7919);
                if (s.Disgraced || rng.NextDouble() < Chance && asking * 3 < Math.Max(3, here)) // (a third of them at most, so it's never a chorus)
                {
                    Request q = Requests.Generate(s, Facts(c, s), day, rng, s.Disgraced);
                    if (q != null)
                    {
                        s.Asked = q;
                        b.Say(q.Deed ? new[] { "Jarl, I've shamed myself. A deed in my name would wash it out.", "Jarl... would you do a thing for me? I need it, after what I did.", "There's ash on my name, jarl. Help me wash it off." }[UnityEngine.Random.Range(0, 3)]
                            : new[] { "Jarl, a word, when you have time? I've a thing to ask.", "Jarl! There's something I'd ask of you.", "When you pass by, jarl, I've a favour to ask.", "Jarl, would you hear an ask of mine?", "I've something to ask the jarl, if the jarl will hear it." }[UnityEngine.Random.Range(0, 5)], 6);
                        c.Log("ask", s.Name, "", Requests.Short(q));
                        b.Dirty();
                    }
                }
            }
            else if (roll) s.Counts["askday"] = day;
            Tag(b);
        }
        public static void Tag(Brain b)
        {
            Request q = b.S.Asked;
            Villagers.Tag(b.View.GetZDO(), Key, q == null ? "" : $"{(q.Deed ? 1 : 0)}\u001f{q.Day}\u001f{Requests.Short(q)}\u001f{q.Id}");
        }
        // Read off a villager by any game: (short form, days left, a deed), or null.
        public static (string what, int daysLeft, bool deed)? Shown(ZDO body)
        {
            string t = body == null ? "" : Villagers.Tags(body).GetString(Key, "");
            string[] f = t.Split('\u001f');
            if (f.Length < 3 || !int.TryParse(f[1], out int day)) return null;
            int left = Math.Max(0, day + Requests.Days - Clock.Day(ZNet.instance.GetTimeSeconds()));
            return (f[2], left, f[0] == "1");
        }
        private static string ShownId(ZDO body) { string[] f = Villagers.Tags(body).GetString(Key, "").Split('\u001f'); return f.Length > 3 ? f[3] : ""; }

        // A villager's answer, on their game: once, whoever asks twice (a second hand-in finds it answered already).
        public static void Answered(Brain b, string arg)
        {
            string[] f = arg.Split('|');
            string id = f[0], who = f.Length > 1 ? f[1] : "";
            long from = f.Length > 2 && long.TryParse(f[2], out long sid) ? sid : 0;
            VillagerState s = b.S; Request q = s.Asked;
            if (q == null || q.Id != id) { Net.AskReply(from, s.Vid, id, false); b.Say(new[] { "That's been seen to already.", "That was answered already, jarl.", "I've had that already. My thanks all the same." }[UnityEngine.Random.Range(0, 3)], 4); return; }
            s.Asked = null;
            Net.AskReply(from, s.Vid, id, true); // (the player hands the things over now, and only now)
            // What was handed over is theirs now: a trophy or a precious thing to keep (hung in their corner), goods for the hall.
            if (Requests.HandIn(q) && ZNetScene.instance?.GetPrefab(q.Target) is GameObject given && given.GetComponent<ItemDrop>() != null)
            {
                Inventory bag = b.Body.GetInventory(); bool keep = q.Kind == Ask.Trophy || q.Kind == Ask.Relic;
                for (int n = keep ? Math.Min(q.Count, 2) : q.Count; n > 0; )
                {
                    ItemDrop.ItemData item = given.GetComponent<ItemDrop>().m_itemData.Clone(); item.m_dropPrefab = given; item.m_stack = Math.Min(n, item.m_shared.m_maxStackSize);
                    if (keep) CornerTask.Mark(item, "ask");
                    if (!bag.CanAddItem(item, item.m_stack) || !bag.AddItem(item)) break;
                    n -= item.m_stack;
                }
                b.NoCornerUntil = 0;
            }
            double now = b.Now;
            if (who != "") s.Opinions[who] = Mathf.Clamp((s.Opinions.TryGetValue(who, out int o) ? o : 0) + Requests.Opinion, -100, 100);
            Worth.Gain(s, "asked", Faith.OdinWorthBonus(b.Colony));
            Mood.Add(s, "asked", now);
            bool washed = q.Deed && s.Disgraced;
            if (washed) { s.Disgraced = false; s.DisgraceSince = 0; s.LowMoodSince = 0; Mood.Add(s, "washed", now); }
            int coins = Requests.CoinsFromHoard(q, s.Hoard);
            GameObject coin = ZNetScene.instance?.GetPrefab("Coins");
            if (coins > 0 && coin != null)
            {
                s.Hoard -= coins;
                ItemDrop.ItemData item = coin.GetComponent<ItemDrop>().m_itemData.Clone();
                item.m_dropPrefab = coin; // (a prefab's own data has none: it's set as a drop wakes)
                ItemDrop.DropItem(item, coins, b.Pos + b.Body.transform.forward * 0.8f + Vector3.up, Quaternion.identity);
            }
            s.Bump("asks");
            string thanks = washed ? new[] { "You did it for me. I can look them in the eye again.", "The shame's gone. I won't forget this, jarl.", "My name is clean again. Thank you, jarl." }[UnityEngine.Random.Range(0, 3)]
                : new[] { "You did it! I knew you would.", "Just as I asked. You're a jarl worth the name.", "Thank you, jarl. I won't forget it.", "That's it exactly. My thanks, jarl.", "Ha! The others won't believe it." }[UnityEngine.Random.Range(0, 5)];
            b.Say(thanks + (coins > 0 ? $" Here: {coins} coins from my own hoard." : ""), 6);
            if (b.Colony != null) Net.Act(b.Colony, "asked", string.Join("|", s.FullName, who, Requests.Short(q), washed ? "1" : "0", q.Id));
            Tag(b); b.Dirty();
        }
        // The shame gone (a raid fought, the gothi's rite): the deed they asked is no longer needed.
        public static void Wash(Brain b)
        {
            VillagerState s = b.S;
            s.Disgraced = false; s.DisgraceSince = 0; s.LowMoodSince = 0; Mood.Add(s, "washed", b.Now);
            if (s.Asked != null && s.Asked.Deed) s.Asked = null;
            Tag(b); b.Dirty();
        }
        // A disgraced soul who killed in a raid that was held: that washes the shame out too.
        public static void FoughtWell(Brain b)
        {
            VillagerState s = b.S;
            if (!s.Disgraced || !s.Counts.TryGetValue("raidkill", out int day) || Clock.Day(b.Now) - day > 1) return;
            Wash(b);
            b.Say(new[] { "I stood with you when it counted. Let them whisper now.", "Blood on my blade for the hall. The shame is gone.", "I fought for us. My name is clean again." }[UnityEngine.Random.Range(0, 3)], 6);
            b.Colony?.Log("text", s.Name, "", $"{s.FullName} fought hard in the raid, and the hall forgets their disgrace.");
            Tag(b);
        }
        // On the colony's owner: the saga and renown.
        public static void Logged(Colony c, string[] a)
        {
            if (a.Length < 5) return;
            bool washed = a[3] == "1";
            // (once an ask, a handful a day: anyone's game may send this, as anyone's game may own the villager)
            if (!Credited.Add(a[4])) return;
            if (c.R.Flag("askedday") != c.Day) { c.R.Flags["askedday"] = c.Day; c.R.Flags["askedn"] = 0; }
            c.R.Flags["askedn"] = c.R.Flag("askedn") + 1;
            if (c.R.Flag("askedn") <= 6) c.R.Deeds += Renown.Deed(washed ? "deed" : "asked");
            c.Log(washed ? "deed" : "asked", a[0], a[1], a[2]);
        }

        private static readonly HashSet<string> Credited = new HashSet<string>();
        // ---- the world, as far as an ask may go ----
        private static readonly Dictionary<string, (int day, List<Place> places)> PlacesCache = new Dictionary<string, (int, List<Place>)>();
        public static WorldFacts Facts(Colony c, VillagerState s)
        {
            var w = new WorldFacts
            {
                Stage = Requests.StageOf(k => ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(k)),
                Exists = n => ZNetScene.instance != null && ZNetScene.instance.GetPrefab(n) != null,
                Places = Places(c),
                Stags = Compat.GreatStags().Where(v => Utils.DistanceXZ(v, c.Position) < 3000).Select(v => (v.x, v.z)).ToList(),
            };
            int day = c.Day;
            foreach (LogEntry e in c.R.Log.Where(e => (e.Kind == "death" || e.Kind == "lost") && e.Day >= day - 20 && e.B != ""))
            {
                RosterRow friend = c.R.Roster.FirstOrDefault(r => r.Vid != s.Vid && e.A.StartsWith(r.Name) && s.Bonds.TryGetValue(r.Vid, out int bond) && bond > 30);
                if (friend != null) w.Avenge.Add((friend.Name, e.B));
            }
            return w;
        }
        // The landmarks within a long journey: the game's own places (all of them on the server, the ones with map icons anywhere).
        private static List<Place> Places(Colony c)
        {
            int day = c.Day;
            if (PlacesCache.TryGetValue(c.Id, out var cached) && cached.day == day) return cached.places;
            var found = new List<Place>();
            void Add(string name, Vector3 at)
            {
                var lm = Requests.Landmark(name);
                if (lm == null) return;
                float d = Utils.DistanceXZ(at, c.Position);
                if (d < 150 || d > 3000) return;
                int biome = WorldGenerator.instance != null ? BiomeStage(WorldGenerator.instance.GetBiome(at)) : 0;
                if (biome < 0) return;
                found.Add(new Place { Key = name, Label = lm.Value.label, X = at.x, Z = at.z, Stage = Math.Max(lm.Value.stage, biome) });
            }
            if (ZoneSystem.instance != null)
            {
                var icons = new Dictionary<Vector3, string>();
                ZoneSystem.instance.GetLocationIcons(icons);
                foreach (var kv in icons) Add(kv.Value, kv.Key);
                if (ZNet.instance != null && ZNet.instance.IsServer())
                    foreach (ZoneSystem.LocationInstance li in ZoneSystem.instance.m_locationInstances.Values) Add(li.m_location?.m_prefabName, li.m_position);
            }
            // The nearest of each kind, nearest first.
            var places = found.OrderBy(p => (p.X - c.Position.x) * (p.X - c.Position.x) + (p.Z - c.Position.z) * (p.Z - c.Position.z))
                .GroupBy(p => p.Label).Select(g => g.First()).Take(12).ToList();
            PlacesCache[c.Id] = (day, places);
            return places;
        }
        private static int BiomeStage(Heightmap.Biome b)
        {
            switch (b)
            {
                case Heightmap.Biome.Meadows: case Heightmap.Biome.Ocean: return 0;
                case Heightmap.Biome.BlackForest: return 1;
                case Heightmap.Biome.Swamp: return 2;
                case Heightmap.Biome.Mountain: return 3;
                case Heightmap.Biome.Plains: return 4;
                case Heightmap.Biome.Mistlands: return 5;
                case Heightmap.Biome.AshLands: return 6;
                default: return -1; // (the Deep North: not yet)
            }
        }
        public static void Forget() { PlacesCache.Clear(); _repinAt = 0; Hunted.Clear(); Struck.Clear(); }

        // ---- the player's side: what they took on, kept in their character ----
        internal sealed class Taken
        {
            public string Colony = "", Vid = "", Id = "", Name = "", Label = "", Target = ""; public Ask Kind; public int Stars; public float X, Z; public bool Done; public int Day;
            public string Encode() => string.Join("\u001f", Colony, Vid, Id, Name, Label, Target, (int)Kind, Stars, X.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture), Z.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture), Done ? 1 : 0, Day);
            public static Taken Decode(string s)
            {
                string[] f = s.Split('\u001f');
                if (f.Length < 11) return null;
                float F(string v) => float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float x) ? x : 0;
                return new Taken { Colony = f[0], Vid = f[1], Id = f[2], Name = f[3], Label = f[4], Target = f[5], Kind = (Ask)(int.TryParse(f[6], out int k) ? k : 0), Stars = int.TryParse(f[7], out int st) ? st : 0, X = F(f[8]), Z = F(f[9]), Done = f[10] == "1", Day = f.Length > 11 && int.TryParse(f[11], out int d) ? d : 0 };
            }
            public string PinKey => "jarldom_ask_" + Id;
        }
        public static List<Taken> All(Player p)
        {
            if (p == null || !p.m_customData.TryGetValue(TakenKey, out string s) || s == "") return new List<Taken>();
            return s.Split('\u001e').Select(Taken.Decode).Where(t => t != null).ToList();
        }
        private static void Save(Player p, List<Taken> list)
        {
            if (list.Count == 0) p.m_customData.Remove(TakenKey);
            else p.m_customData[TakenKey] = string.Join("\u001e", list.Skip(Math.Max(0, list.Count - 12)).Select(t => t.Encode())); // (the newest)
        }
        public static Taken Of(Player p, string vid, string id) => All(p).FirstOrDefault(t => t.Vid == vid && t.Id == id);
        public static void Take(Colony c, VillagerState s)
        {
            Player p = Player.m_localPlayer; Request q = s.Asked;
            if (p == null || q == null) return;
            var list = All(p).Where(t => t.Vid != s.Vid).ToList();
            var t = new Taken { Colony = c.Id, Vid = s.Vid, Id = q.Id, Name = s.Name, Label = q.Label, Target = q.Target, Kind = q.Kind, Stars = q.Stars, X = q.X, Z = q.Z, Day = q.Day };
            list.Add(t); Save(p, list);
            if (Pinned(q.Kind, q.Target)) Net.PinHere(t.PinKey, new Vector3(q.X, 0, q.Z), $"{s.Name}: {q.Label}", true);
            p.Message(MessageHud.MessageType.Center, q.Kind == Ask.Visit ? $"Pinned on your map: {q.Label}. See it, then tell {s.Name}."
                : q.Target == Requests.GreatStag ? $"Pinned on your map: where the great stag was seen. Hunt it, then tell {s.Name}."
                : q.Kind == Ask.Kill ? $"Kill {Requests.A(q.Label)}{(q.Stars > 0 ? $" ({q.Stars}★+)" : "")} yourself, then tell {s.Name}."
                : $"Bring {Requests.Short(q)} to {s.Name}.");
        }
        public static void Drop(Player p, string vid)
        {
            if (p == null) return;
            var list = All(p);
            foreach (Taken t in list.Where(t => t.Vid == vid)) Net.PinHere(t.PinKey, Vector3.zero, "", false);
            Save(p, list.Where(t => t.Vid != vid).ToList());
        }
        private static void Mark(Player p, Taken done)
        {
            var list = All(p);
            foreach (Taken t in list.Where(t => t.Vid == done.Vid && t.Id == done.Id)) t.Done = true;
            Save(p, list);
        }

        // Every second: a place reached; now and then the pins put back (a new session, a reload).
        private static float _next, _repinAt; private static Player _pinnedFor;
        public static void Tick()
        {
            Player p = Player.m_localPlayer;
            if (p == null || Time.time < _next) return;
            _next = Time.time + 1f;
            List<Taken> list = null;
            if (p != _pinnedFor || Time.time > _repinAt)
            {
                _pinnedFor = p; _repinAt = Time.time + 30f; list = All(p);
                // (one answered by someone else, or lapsed: off the list and the map, once their soul is in sight to say so)
                int today = Clock.Day(ZNet.instance.GetTimeSeconds());
                foreach (Taken t in list.Where(t => t.Day > 0 && today >= t.Day + Requests.Days).ToList()) { Drop(p, t.Vid); list.Remove(t); } // (lapsed, wherever they are)
                foreach (Taken t in list.ToList())
                {
                    ZDO body = Villagers.Body(t.Vid)?.GetComponent<ZNetView>()?.GetZDO();
                    if (body != null && SoulStore.Of(body) != null && ShownId(body) != t.Id) { Drop(p, t.Vid); list.Remove(t); }
                }
                foreach (Taken t in list.Where(t => Pinned(t.Kind, t.Target))) Net.PinHere(t.PinKey, new Vector3(t.X, 0, t.Z), $"{t.Name}: {t.Label}{(t.Done ? t.Kind == Ask.Visit ? " (seen)" : " (done)" : "")}", true);
            }
            if (!p.m_customData.ContainsKey(TakenKey)) { Hunted.Clear(); Struck.Clear(); return; }
            list = list ?? All(p);
            Hunted.Clear(); foreach (Taken t in list.Where(t => t.Kind == Ask.Kill && !t.Done)) Hunted.Add(t.Target);
            Watch(p);
            foreach (Taken t in list.Where(t => t.Kind == Ask.Visit && !t.Done))
            {
                if (Utils.DistanceXZ(p.transform.position, new Vector3(t.X, 0, t.Z)) > SeenWithin) continue;
                Mark(p, t);
                Net.PinHere(t.PinKey, new Vector3(t.X, 0, t.Z), $"{t.Name}: {t.Label} (seen)", true);
                p.Message(MessageHud.MessageType.Center, $"You have seen {t.Label}. Go back and tell {t.Name} of it.");
            }
        }

        // A kill by this game's player: one they took on, done. Seen where the creature dies (its owner's game, often the killer's), and
        // on the killer's own game too: a creature they struck that is dead or gone within moments (another game owned it).
        private static void Killed(Player p, string prefab, int level)
        {
            foreach (Taken t in All(p).Where(t => t.Kind == Ask.Kill && !t.Done))
            {
                if (!Requests.KillCounts(new Request { Kind = Ask.Kill, Target = t.Target, Stars = t.Stars }, prefab, level)) continue;
                Mark(p, t);
                p.Message(MessageHud.MessageType.Center, $"{t.Name} will want to hear of this: go back and tell of it.");
            }
        }
        private static readonly HashSet<string> Hunted = new HashSet<string>(); // (the targets this player took on: a cheap test on every hit)
        // A place to go: on the map (a great stag's where it was last seen).
        private static bool Pinned(Ask kind, string target) => kind == Ask.Visit || target == Requests.GreatStag;
        // What a kill counts as: its prefab, or the great stag Omens set loose.
        private static string Quarry(Character ch) => Hunted.Contains(Requests.GreatStag) && Compat.IsGreatStag(ch) ? Requests.GreatStag : Utils.GetPrefabName(ch.gameObject);
        private static readonly Dictionary<Character, (string prefab, int level, float at)> Struck = new Dictionary<Character, (string, int, float)>();
        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        private static class Kills
        {
            private static void Prefix(Character __instance)
            {
                Player p = Player.m_localPlayer;
                if (__instance == null || p == null || Hunted.Count == 0 || __instance.IsPlayer()) return;
                Plugin.Guard(() =>
                {
                    if (Combat.LastHit(__instance)?.GetAttacker() != p) return;
                    Struck.Remove(__instance);
                    Killed(p, Quarry(__instance), __instance.GetLevel());
                });
            }
        }
        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        private static class Hits
        {
            private static void Prefix(Character __instance, HitData hit)
            {
                Player p = Player.m_localPlayer;
                if (__instance == null || p == null || Hunted.Count == 0 || hit == null) return;
                try
                {
                    if (hit.GetAttacker() != p) return;
                    string prefab = Quarry(__instance);
                    if (Hunted.Contains(prefab)) Struck[__instance] = (prefab, __instance.GetLevel(), Time.time);
                }
                catch { } // (never in the way of a hit)
            }
        }
        private static void Watch(Player p)
        {
            if (Struck.Count == 0) return;
            foreach (var kv in Struck.ToList())
            {
                bool dead = kv.Key == null || kv.Key.IsDead() || kv.Key.GetHealth() <= 0;
                if (dead) { Struck.Remove(kv.Key); if (Time.time - kv.Value.at < 6) Killed(p, kv.Value.prefab, kv.Value.level); } // (struck by them moments before it fell)
                else if (Time.time - kv.Value.at > 6) Struck.Remove(kv.Key);
            }
        }

        // ---- answering it, from the panel ----
        // What a hand-in needs in the player's bag: the item's name in the game's terms (the bag counts by it), how many are there.
        public static int Have(Player p, Request q, out string itemName)
        {
            itemName = ZNetScene.instance?.GetPrefab(q.Target)?.GetComponent<ItemDrop>()?.m_itemData?.m_shared?.m_name;
            string n = itemName;
            return n == null || p == null ? 0 : Own(p, n).Sum(i => i.m_stack);
        }
        // Only what's in the bag itself: the game's count by name and take by name reach into the chests nearby with CraftFromChests
        // and the like, which would hand the colony's own stores over (and take them away).
        private static IEnumerable<ItemDrop.ItemData> Own(Player p, string name) => p.GetInventory().GetAllItems().Where(i => i.m_shared.m_name == name && !i.m_equipped).ToList();
        public static bool Ready(Player p, VillagerState s, out string why)
        {
            why = ""; Request q = s.Asked;
            if (q == null) return false;
            if (Requests.HandIn(q)) { int n = Have(p, q, out _); if (n >= q.Count) return true; why = $"you carry {n} of {q.Count}"; return false; }
            Taken t = Of(p, s.Vid, q.Id);
            if (t == null) { why = "not taken on yet"; return false; }
            if (!t.Done) { why = q.Kind == Ask.Visit ? $"not seen yet (it's pinned on your map)" : "not done yet"; return false; }
            return true;
        }
        public static void Answer(Colony c, VillagerState s)
        {
            Player p = Player.m_localPlayer; Request q = s.Asked; string why = "";
            if (p == null || q == null || !Ready(p, s, out why)) { if (p != null && why != "") p.Message(MessageHud.MessageType.Center, "Not yet: " + why + "."); return; }
            if (Pending.TryGetValue(q.Id, out float at) && Time.time - at < 10) return;
            if (Requests.HandIn(q)) Handing[q.Id] = (q.Target, q.Count); // (asked already: waiting for their answer)
            Pending[q.Id] = Time.time;
            Villagers.Command(s.Vid, "answer", $"{q.Id}|{p.GetPlayerName()}|{ZDOMan.GetSessionID()}"); // (straight to their game: they're right here)
        }
        private static readonly Dictionary<string, float> Pending = new Dictionary<string, float>();
        // Their game's answer: yes, and the things go from the bag; no, it was seen to already (by someone else, or it lapsed).
        private static readonly Dictionary<string, (string target, int count)> Handing = new Dictionary<string, (string, int)>();
        public static void Replied(string vid, string id, bool ok)
        {
            Pending.Remove(id);
            Player p = Player.m_localPlayer;
            if (p == null) return;
            if (ok && Handing.TryGetValue(id, out var h))
            {
                Have(p, new Request { Target = h.target }, out string item);
                for (int left = h.count; item != null && left > 0;)
                {
                    ItemDrop.ItemData st = Own(p, item).FirstOrDefault(); if (st == null) break;
                    int n = Math.Min(left, st.m_stack); p.GetInventory().RemoveItem(st, n); left -= n;
                }
            }
            Handing.Remove(id);
            Drop(p, vid);
            if (!ok) p.Message(MessageHud.MessageType.Center, "That was seen to already, or it lapsed.");
        }
    }
}
