using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Jarldom
{
    // A feast in the great hall: called from the colony window when the hall's chests hold enough, eaten at dusk.
    internal static class Feast
    {
        public const int Cooldown = 3;
        public static string Ready(Colony c, out List<(Container chest, ItemDrop.ItemData item)> food, out List<(Container chest, ItemDrop.ItemData item)> mead)
        {
            food = new List<(Container, ItemDrop.ItemData)>(); mead = new List<(Container, ItemDrop.ItemData)>();
            Room hall = c.Best(RoomType.GreatHall);
            if (hall == null || !hall.Ok) return "a feast needs a working great hall";
            if (c.Day - c.R.LastFeast < Cooldown) return $"the last feast was {c.Day - c.R.LastFeast} days ago (wait {Cooldown})";
            int pop = Math.Max(1, c.Population);
            foreach (var x in Stock.Chests(c, c.Centre(hall)).Where(x => Geometry.Inside(hall.Xz, x.chest.transform.position.x, x.chest.transform.position.z)))
                foreach (ItemDrop.ItemData i in x.chest.GetInventory().GetAllItems())
                {
                    if (Stock.Edible(i)) food.Add((x.chest, i));
                    if (Classes.IsMead(i)) mead.Add((x.chest, i));
                }
            int kinds = food.Select(f => f.item.m_shared.m_name).Distinct().Count(), portions = food.Sum(f => f.item.m_stack), meads = mead.Sum(m => m.item.m_stack);
            if (kinds < 3) return $"the hall's chests hold {kinds} kinds of food (a feast wants 3)";
            if (portions < 3 * pop) return $"{portions} portions of food in the hall (a feast for {pop} wants {3 * pop})";
            if (meads < pop) return $"{meads} meads in the hall (a feast for {pop} wants {pop})";
            return "";
        }
        public static void Call(Colony c, string who)
        {
            string why = Ready(c, out _, out _);
            if (why != "") { c.Tell("No feast: " + why + "."); return; }
            c.R.Flags["feastday"] = c.Day;
            c.MarkDirty();
            c.Tell($"{who} calls a feast in the hall tonight!", true);
            JarlDeeds.FeastCalled(c, who);
            if (c.Hour >= Schedule.HallAt) Dusk(c, c.Day); // already evening: now
        }
        public static void Dusk(Colony c, int day)
        {
            if (c.R.Flag("feastday") != day) return;
            if (Ready(c, out var food, out var mead) != "" && c.R.LastFeast != day) { c.Tell("The feast was called off: the hall's chests ran short."); c.R.Flags.Remove("feastday"); return; }
            int pop = Math.Max(1, c.Population);
            Take(food, 3 * pop); Take(mead, pop);
            c.R.LastFeast = day; c.R.Flags.Remove("feastday");
            JarlDeeds.FeastHeld(c, day);
            c.R.Deeds += Renown.Deed("feast");
            string glutton = null;
            foreach (Brain b in Villagers.All.Where(b => b.S.Colony == c.Id))
            {
                Mood.Add(b.S, "feast", b.Now); Mood.Satisfy(b.S, Need.Food, 100); Mood.Satisfy(b.S, Need.Social, 40);
                Worth.Gain(b.S, "feast"); b.S.Bump("feasts");
                if (b.S.Has("glutton")) glutton = b.S.Name;
                if (b.S.Count("feasts") >= 5 && string.IsNullOrEmpty(b.S.Byname)) { b.S.Byname = Names.Byname("feaster"); c.Log("byname", b.S.Name, b.S.Byname); }
                b.Dirty();
            }
            c.Log("feast", glutton ?? "", "");
            // With a temple (or Odin's shrine), the feast is a festival to its god: faith twice over, the god's favour, worth.
            Room temple = c.R.Rooms.Where(r => r.Ok && (r.Type == RoomType.Temple || r.Type == RoomType.OdinShrine)).OrderByDescending(r => r.Points).FirstOrDefault();
            if (temple != null)
            {
                string god = temple.Type == RoomType.OdinShrine || string.IsNullOrEmpty(temple.God) ? "odin" : temple.God;
                Faith.Add(c, god, 5, "a festival");
                c.R.Deeds += Renown.Deed("festival");
                foreach (Brain b in Villagers.All.Where(b => b.S.Colony == c.Id)) { Mood.Add(b.S, "festival", b.Now); Mood.Satisfy(b.S, Need.Faith, 40); Worth.Gain(b.S, "festival"); b.Dirty(); }
                c.Log("text", "", "", $"The feast was a festival to {Tables.God(god)?.Name}, and {Tables.God(god)?.Name} was glad of it.");
            }
            Compat.SkalToast(c);
        }
        private static void Take(List<(Container chest, ItemDrop.ItemData item)> from, int amount)
        {
            foreach (var (chest, item) in from.OrderBy(x => x.item.m_shared.m_food))
            {
                if (amount <= 0) break;
                if (ContainerAccess.InUse(chest)) continue;
                ContainerAccess.TakeOver(chest); // (may reload the chest: find the item again)
                ItemDrop.ItemData have = chest.GetInventory().GetAllItems().FirstOrDefault(i => i == item) ?? chest.GetInventory().GetAllItems().FirstOrDefault(i => i.m_shared.m_name == item.m_shared.m_name && i.m_quality == item.m_quality);
                if (have == null) continue;
                int n = Math.Min(amount, have.m_stack);
                if (chest.GetInventory().RemoveItem(have, n)) amount -= n;
            }
        }
    }

    // The Thing: every five days the colony's petitions, from what really happened; rulings change moods and opinions.
    // The Thing: petitions are raised day by day and wait; the jarl calls the Thing, everyone gathers in the great hall (or at the
    // pillars, before there is one), each petitioner in turn stands before the high seat and speaks, and the jarl rules there. Nothing
    // is decided without it, and petitions left unheard too long sour the colony.
    internal static class Thing
    {
        // The meeting's state, in the record's flags (so every game sees it): on; the petition being heard (1-based, 0 while they
        // gather); spoken (its teller has said it, the jarl may rule); when the stage began; rulings made.
        public const string On = "thing:on", Cur = "thing:cur", Spoken = "thing:spoken", Since = "thing:since", Ruled = "thing:ruled";
        public static bool Held(Colony c) => c?.R != null && c.R.Flag(On) == 1;
        public static List<Petition> Pending(Colony c) { Migrate(c); return c.R.Petitions; }
        public static Petition Current(Colony c) { int i = c.R.Flag(Cur) - 1; var l = Pending(c); return Held(c) && i >= 0 && i < l.Count ? l[i] : null; }
        public static bool Ready(Colony c) => Current(c) != null && c.R.Flag(Spoken) == 1;
        public static Room Hall(Colony c) => c.Best(RoomType.GreatHall);
        public static Vector3 Place(Colony c) { Room h = Hall(c); return h != null ? c.Centre(h) : c.Position + c.Seat.transform.forward * 4f; }
        public static bool There(Colony c, Vector3 p) { Room h = Hall(c); return h != null ? Geometry.Inside(h.Xz, p.x, p.z) && Mathf.Abs(p.y - h.Floor) < 4f : Utils.DistanceXZ(p, c.Position) < 15f; }
        // The high seat in the hall, if there is one: petitioners stand before it.
        public static Transform HighSeat(Colony c)
        {
            Room h = Hall(c); if (h == null) return null;
            return c.PiecesIn(c.Radius).FirstOrDefault(p => Classes.IsHighSeat(p.gameObject) && Geometry.Inside(h.Xz, p.transform.position.x, p.transform.position.z))?.transform;
        }
        public static Vector3 Floor(Colony c)
        {
            Transform seat = HighSeat(c);
            Vector3 at = seat != null ? seat.position + seat.forward * 3f : Place(c);
            return Colony.Spot(at, 1f) ?? at;
        }

        // (petitions once kept in the saga: moved into the record)
        private static void Migrate(Colony c)
        {
            if (c.R.Flag("petitions:moved") == 1 || !c.IsOwner || !c.SagaRead) return; // (the saga read in first: that's where they were)
            c.R.Flags["petitions:moved"] = 1;
            foreach (LogEntry e in c.R.Log.Where(e => e.Kind == "petition"))
            {
                string[] f = e.Text.Split('\u001f');
                int Num(int i) => f.Length > i && int.TryParse(f[i], out int v) ? v : 0;
                if (c.R.Petitions.Any(p => p.Id == e.A && p.About == e.B)) continue;
                c.R.Petitions.Add(new Petition { Id = e.A, About = e.B, Text = f.Length > 0 ? f[0] : "", Yes = f.Length > 1 ? f[1] : "Yes", No = f.Length > 2 ? f[2] : "No", YesMood = Num(3), NoMood = Num(4), YesFavour = Num(5), YesDeed = f.Length > 6 ? f[6] : "", Day = e.Day });
            }
            if (c.R.Log.RemoveAll(e => e.Kind == "petition") > 0) c.R.LogChanges++; // (the saga written again without them)
            c.MarkDirty();
        }

        // What the colony would ask for now.
        private static List<Petition> Standing(Colony c, int day)
        {
            var view = new ColonyView
            {
                Population = c.Population, HasBeautyRoom = c.R.Rooms.Any(r => r.Ok && r.Type == RoomType.GreatHall && Tables.GradeRank(r.Grade) >= 2),
                HasShrine = c.R.Rooms.Any(r => r.Ok && r.Type == RoomType.OdinShrine), MissedPaydays = c.R.MissedPaydays, Beds = c.Beds,
                Hungry = Villagers.All.Count(b => b.S.Colony == c.Id && b.S.Needs[(int)Need.Food] < 30) > c.Population / 2,
                GuardWage = Tables.Wage(Prof.Guard, false) + c.GuardExtra, // (raised at a Thing: not asked again)
            };
            foreach (Brain b in Villagers.All.Where(b => b.S.Colony == c.Id))
            {
                if (b.S.Count("thefts") > 0 || b.S.Count("brawls") > 0) view.Troublemakers.Add((b.S.Name, b.S.Count("thefts"), b.S.Count("brawls")));
                if (b.S.Count("seen") > 0) view.Suspects.Add((b.S.Name, b.S.Count("seen")));
            }
            foreach (var feud in Villagers.All.Where(b => b.S.Colony == c.Id).SelectMany(b => b.S.Bonds.Where(kv => kv.Value < -50).Select(kv => (b.S.Name, Villagers.ByVid(kv.Key)?.S.Name))).Where(f => f.Item2 != null && string.CompareOrdinal(f.Item1, f.Item2) < 0).Distinct())
                view.Feuds.Add((feud.Item1, feud.Item2));
            return Petitions.Raise(new System.Random(c.Id.GetStableHashCode() ^ day), view, int.MaxValue);
        }

        // Each dawn: matters raised and withdrawn; and if the oldest has waited too long, the colony grumbles.
        public static void Dawn(Colony c, int day, bool force = false)
        {
            var pending = Pending(c);
            if (c.Population < 3 && !force) return;
            if (Held(c)) return; // (not while it sits)
            var rng = new System.Random(c.Id.GetStableHashCode() ^ day * 31);
            foreach (string k in c.R.Flags.Keys.Where(k => k.StartsWith("ruled:") && day - c.R.Flags[k] >= RuledRest).ToList()) c.R.Flags.Remove(k);
            // (not again so soon after a ruling, nor while a promise on it is running)
            var standing = Standing(c, day).Where(p => !c.R.Flags.ContainsKey("ruled:" + p.Key) && !c.R.Flags.ContainsKey("promise:" + p.Id)).ToList();
            var (raised, withdrawn) = Petitions.Update(pending, standing, day, rng);
            // (only what this game can see is judged: with too few of the folk loaded here, nothing is withdrawn; a matter about
            // someone stands until they're gone from the roster)
            var living = c.R.Living.ToList();
            bool seeAll = Villagers.All.Count(b => b.S.Colony == c.Id) >= living.Count * 3 / 4;
            foreach (Petition p in withdrawn)
            {
                bool personal = p.Id == "exile" || p.Id == "accuse" || p.Id == "feud";
                bool gone = personal ? p.About.Split('|').Any(n => !living.Any(r => r.Name == n)) : seeAll;
                if (gone && (seeAll || personal)) pending.Remove(p);
            }
            var people = living.Select(r => (r.Vid, r.Name, r.Prof.ToString(), string.IsNullOrEmpty(r.Bed))).ToList();
            foreach (Petition p in raised)
            {
                p.By = Petitions.Speaker(p, people, rng);
                p.ByName = c.R.Row(p.By)?.Name ?? "";
                pending.Add(p);
                c.Log("text", p.ByName, "", $"{(p.ByName != "" ? p.ByName : "Someone")} has a matter for the Thing: “{p.Text}”");
            }
            if (raised.Count > 0 || withdrawn.Count > 0) c.MarkDirty();
            if (pending.Count > 0 && Petitions.Waited(pending, day) >= Petitions.UnheardAfter)
                foreach (Brain b in Villagers.All.Where(b => b.S.Colony == c.Id)) { Mood.Add(b.S, "unheard", b.Now); b.Dirty(); }
        }

        // The jarl calls it: everyone to the hall.
        public static string CanCall(Colony c)
        {
            if (Held(c)) return "the Thing is sitting";
            if (Pending(c).Count == 0) return "no one has a matter for the Thing yet";
            if (c.RaidOn) return "not while there's a raid";
            double h = c.Hour;
            if (h < Schedule.WakeAt || h >= Schedule.SleepAt) return "the folk are abed: call it by day or in the evening";
            return "";
        }
        public static void Call(Colony c, string who)
        {
            string why = CanCall(c);
            if (why != "") { c.Tell("No Thing: " + why + "."); return; }
            c.R.Flags[On] = 1; c.R.Flags[Cur] = 0; c.R.Flags[Spoken] = 0; c.R.Flags[Ruled] = 0; c.R.Flags[Since] = (int)c.Now;
            c.Tell($"{who} calls the Thing! Everyone to {(Hall(c) != null ? "the great hall" : "the pillars")}.", true);
            c.Log("text", who, "", $"{who} called the Thing: {Pending(c).Count} matter{(Pending(c).Count == 1 ? "" : "s")} to hear.");
            c.MarkDirty(); c.Flush();
        }
        // Who should come: those at home and on their feet (not away, abed wounded, or out with the jarl).
        public static bool Comes(Brain b) => b.Alive && b.S.InjuredUntil <= b.Now && Follow.LeaderId(b) == 0;
        public static int Gathered(Colony c, out int expected)
        {
            var folk = Villagers.All.Where(b => b.S.Colony == c.Id && Comes(b)).ToList();
            expected = folk.Count;
            return folk.Count(b => b.Task is ThingTask t && t.Arrived);
        }

        // On the owner, every second while it sits: gathering, then each matter heard in turn.
        public static void Tick(Colony c)
        {
            if (!Held(c)) return;
            double real = c.Now - c.R.Flag(Since); // (the stage's age: the world's seconds are the real ones, whatever the day length)
            if (c.RaidOn) { Close(c, "adjourned: a raid"); return; }
            // (no one who may rule anywhere near for a minute: adjourned)
            if (Player.GetAllPlayers().Any(p => p != null && c.Allows(p) && Utils.DistanceXZ(p.transform.position, Place(c)) < c.Radius + 30)) JarlSeen[c.Id] = Time.time;
            else if (!JarlSeen.TryGetValue(c.Id, out float seen)) JarlSeen[c.Id] = Time.time;
            else if (Time.time - seen > 60) { Close(c, "adjourned: the jarl went away"); return; }
            if (Pending(c).Count == 0) { Close(c, ""); return; }
            if (c.R.Flag(Cur) == 0)
            {
                int here = Gathered(c, out int expected);
                if (here >= Math.Ceiling(expected * 0.9) || real > 120) Next(c);
                return;
            }
            if (Current(c) == null) { Next(c); return; }
            // (the teller couldn't come forward, or there's none here to: it's read out)
            Petition cur = Current(c);
            bool teller = cur.By != "" && Villagers.All.Any(b => b.S.Vid == cur.By && b.Task is ThingTask);
            if (c.R.Flag(Spoken) == 0 && real > (teller ? 30 : 3)) { Speak(c); return; }
            if (c.R.Flag(Spoken) == 1 && real > 240) Close(c, "adjourned: the jarl gave no ruling");
        }
        private static void Next(Colony c)
        {
            if (Pending(c).Count == 0) { Close(c, ""); return; }
            c.R.Flags[Cur] = 1; c.R.Flags[Spoken] = 0; c.R.Flags[Since] = (int)c.Now;
            c.MarkDirty(); c.Flush();
        }
        // The teller has said it (or it was read out): the jarl may rule.
        public static void Speak(Colony c, string key = null)
        {
            Petition p = Current(c); if (p == null || c.R.Flag(Spoken) == 1 || key != null && key != p.Key) return; // (a late word about a matter already heard)
            c.R.Flags[Spoken] = 1; c.R.Flags[Since] = (int)c.Now;
            c.Tell($"{(p.ByName != "" ? p.ByName : "A voice")} speaks before the Thing: “{p.Text}”", true);
            c.MarkDirty(); c.Flush();
        }
        public static void Close(Colony c, string why)
        {
            int ruled = c.R.Flag(Ruled);
            foreach (string k in new[] { On, Cur, Spoken, Since, Ruled }) c.R.Flags.Remove(k);
            JarlSeen.Remove(c.Id);
            c.R.LastThing = c.Day;
            if (ruled > 0)
                foreach (Brain b in Villagers.All.Where(b => b.S.Colony == c.Id && b.Task is ThingTask t && t.Arrived)) { Mood.Add(b.S, "thingheld", b.Now); b.Dirty(); }
            c.Log("thing", "", $"{ruled} ruling{(ruled == 1 ? "" : "s")}{(why != "" ? ", " + why : "")}");
            int left = Pending(c).Count;
            c.Tell(why == "" ? "The Thing is over." : $"The Thing is {why}.{(left > 0 ? $" {left} matter{(left == 1 ? "" : "s")} still wait." : "")}", true);
            c.MarkDirty(); c.Flush();
        }

        public const int RuledRest = 3; // days before a matter ruled on may be raised again
        // The third word on an accusation: neither cast out nor let be, but the gothi's rite at Odin's altar (paid from the treasury).
        public static void RuleRite(Colony c, int index, string who, string key = null)
        {
            var list = Pending(c);
            if (!Held(c) || index != c.R.Flag(Cur) - 1 || index < 0 || index >= list.Count || c.R.Flag(Spoken) != 1) return;
            Petition p = list[index];
            if (key != null && key != p.Key || p.Id != "accuse") return;
            RosterRow row = c.R.Roster.FirstOrDefault(r => r.Name == p.About && r.State == SoulState.Active);
            string why = row == null ? "they're not here" : Saboteur.CleanseWhy(c, row.Vid);
            if (why != "") { c.Tell("No rite: " + why + ".", true); return; }
            c.R.Flags["ruled:" + p.Key] = c.Day;
            list.RemoveAt(index);
            foreach (Brain b in Villagers.All.Where(b => b.S.Colony == c.Id)) { Mood.Add(b.S, "thingheard", b.Now); b.Dirty(); }
            c.Log("text", who, "", $"At the Thing, {who} gave {p.About} to the gothi's rite: \"{p.Text}\"");
            Saboteur.AskCleanse(c, row.Vid, who);
            c.Tell($"{who}: “Let the gothi try them before Odin.” The Thing murmurs.", true);
            React(c, p, 0);
            c.R.Flags[Ruled] = c.R.Flag(Ruled) + 1;
            c.R.Flags[Spoken] = 0; c.R.Flags[Since] = (int)c.Now;
            if (list.Count == 0) Close(c, "");
            c.MarkDirty();
        }
        public static void Rule(Colony c, int index, bool yes, string who, string key = null)
        {
            var list = Pending(c);
            if (!Held(c) || index != c.R.Flag(Cur) - 1 || index < 0 || index >= list.Count || c.R.Flag(Spoken) != 1) return; // (only at the Thing, on the matter being heard)
            Petition p = list[index];
            if (key != null && key != p.Key) return; // (a ruling sent on a matter already decided)
            c.R.Flags["ruled:" + p.Key] = c.Day;
            list.RemoveAt(index);
            int mood = yes ? p.YesMood : p.NoMood;
            foreach (Brain b in Villagers.All.Where(b => b.S.Colony == c.Id))
            {
                Mood.Add(b.S, mood >= 0 ? "thingheard" : "thingignored", b.Now);
                b.S.Opinions[who] = Mathf.Clamp((b.S.Opinions.TryGetValue(who, out int o) ? o : 0) + mood, -100, 100);
                b.Dirty();
            }
            if (yes && p.YesFavour != 0) Faith.Add(c, "odin", p.YesFavour, "the Thing's word");
            if (yes && p.YesDeed != "") c.R.Deeds += Renown.Deed(p.YesDeed);
            if (yes && p.Id == "exile") { Brain b = Villagers.All.FirstOrDefault(x => x.S.Colony == c.Id && x.S.Name == p.About); if (b != null) { c.Log("exile", b.S.FullName, who); Villagers.Command(b.S.Vid, "dismiss", ""); } }
            if (yes && p.Id == "guardpay") c.R.Flags["guardraise"] = 1;
            if (yes && p.Id == "accuse") Saboteur.CastOut(c, p.About, who);
            if (yes && Promises.Contains(p.Id)) c.R.Flags["promise:" + p.Id] = c.Day + 5; // to be kept within five days
            c.Log("text", who, "", $"At the Thing, {who} said {(yes ? p.Yes.ToLower() : p.No.ToLower())}: \"{p.Text}\"");
            c.Tell($"{who}: “{(yes ? p.Yes : p.No)}.” {(mood > 0 ? "The Thing cheers." : mood < 0 ? "The Thing grumbles." : "The Thing murmurs.")}", true);
            React(c, p, mood);
            c.R.Flags[Ruled] = c.R.Flag(Ruled) + 1;
            c.R.Flags[Spoken] = 0; c.R.Flags[Since] = (int)c.Now; // (the next matter, at the same place in the list)
            if (list.Count == 0) Close(c, "");
            c.MarkDirty();
        }
        // The crowd's answer to a ruling: a cheer, or a grumble; the teller's word.
        private static readonly string[] Cheers = { "Hail!", "Well judged!", "Hear, hear!", "The jarl is wise.", "So it shall be!",
            "Aye, that's the way!", "A good ruling, jarl.", "The hall agrees!", "Odin would be pleased.", "Now there's a jarl!" };
        private static readonly string[] Grumbles = { "Hmph.", "That's no answer.", "We'll remember this.", "Unfair!", "Shame...",
            "Hard words for a hard winter.", "I'll not forget this.", "Bah.", "Not what we came for.", "The jarl forgets their folk." };
        private static void React(Colony c, Petition p, int mood)
        {
            var rng = new System.Random();
            foreach (Brain b in Villagers.All.Where(b => b.S.Colony == c.Id && b.Task is ThingTask t && t.Arrived))
            {
                if (rng.NextDouble() < 0.5) b.Say(mood >= 0 ? Cheers[rng.Next(Cheers.Length)] : Grumbles[rng.Next(Grumbles.Length)], 4);
                if (b.Held != null) continue; // (on a bench: words only)
                Body_.Anim(b.Body)?.SetTrigger(mood >= 0 ? "emote_cheer" : "emote_nonono");
            }
            Brain teller = Villagers.ByVid(p.By);
            teller?.Say(mood >= 0 ? new[] { "Thank you, jarl.", "My thanks, jarl.", "Well judged, jarl. Thank you.", "The hall is grateful, jarl.", "You have my thanks." }[rng.Next(5)] : new[] { "...As the jarl wills.", "Very well, jarl.", "...If the jarl says so.", "I shall do as I am told.", "Hm. As you will." }[rng.Next(5)], 5);
        }
        private static readonly Dictionary<string, float> JarlSeen = new Dictionary<string, float>();
        public static void Forget() => JarlSeen.Clear();

        // Promises made at the Thing, checked each dawn: kept is remembered well, broken (five days gone) badly.
        private static readonly string[] Promises = { "beauty", "shrine", "beds", "food", "wages" };
        public static void CheckPromises(Colony c, int day)
        {
            foreach (string key in c.R.Flags.Keys.Where(k => k.StartsWith("promise:")).ToList())
            {
                string id = key.Substring(8); int due = c.R.Flags[key];
                bool kept = id == "beauty" ? c.R.Rooms.Any(r => r.Ok && r.Type == RoomType.GreatHall && Tables.GradeRank(r.Grade) >= 2)
                    : id == "shrine" ? c.R.Rooms.Any(r => r.Ok && r.Type == RoomType.OdinShrine)
                    : id == "beds" ? c.Beds >= c.Population
                    : id == "food" ? day >= due - 3 && Villagers.All.Any(b => b.S.Colony == c.Id) && Villagers.All.Where(b => b.S.Colony == c.Id).All(b => b.S.Needs[(int)Need.Food] > 40) // (fed for a couple of days, not one morning)
                    : id == "wages" && c.R.MissedPaydays == 0 && c.R.LastPayday > due - 5;
                if (!kept && day < due) continue;
                c.R.Flags.Remove(key);
                foreach (Brain b in Villagers.All.Where(b => b.S.Colony == c.Id)) { Mood.Add(b.S, kept ? "promisekept" : "promisebroken", b.Now); b.Dirty(); }
                c.Log("text", "", "", kept ? $"The jarl kept the promise made at the Thing ({id})." : $"The promise made at the Thing ({id}) was broken.");
            }
        }
    }

    // Raids at the colony: the game's own events, found when they start near the pillars; the horn sounds, raids grow with renown.
    internal static class Defense
    {
        private static readonly Dictionary<string, (string name, int deathsBefore)> Running = new Dictionary<string, (string, int)>();
        public static void Tick(Colony c)
        {
            Plugin.Guard(() => Faith.Thunder(c));
            Plugin.Guard(() => Greet.Weather(c));
            RandomEvent ev = RandEventSystem.instance != null ? RandEventSystem.instance.GetCurrentRandomEvent() : null;
            bool near = ev != null && Utils.DistanceXZ(ev.m_pos, c.Position) < c.Radius + 80;
            bool siege = Compat.Siege(c.Position, c.Radius, out Vector3 stone);
            if (siege && !near) { near = true; ev = null; }
            bool band = !near && (Warband.Raiding(c) || Tribute.BandRaiding(c));
            if (band) { near = true; ev = null; stone = c.Position; }
            // The restless dead (one of the colony's own, lost to Hel) walking in: a raid too.
            // (it starts when one comes inside, and lasts while one is near the edge, ten minutes at most: not on and off as one wanders)
            bool wasRestless = c.RaidOn && Running.TryGetValue(c.Id, out var run) && run.name == Restless;
            float reach = wasRestless ? c.Radius + 25 : c.Radius;
            Character dead = near ? null : Character.GetAllCharacters().FirstOrDefault(ch => ch != null && !ch.IsDead() && Utils.DistanceXZ(ch.transform.position, c.Position) < reach && (ch.GetComponent<ZNetView>()?.GetZDO()?.GetString(Draugr.NameKey, "") ?? "").StartsWith("Draugr "));
            bool timedOut = wasRestless && RestlessSince.TryGetValue(c.Id, out float since) && Time.time - since > 600f;
            if (timedOut) RestlessSince[c.Id] = Time.time + 600f; // (then quiet for ten minutes: one stuck where no one can reach isn't a raid for ever)
            bool restless = dead != null && !timedOut && !(!wasRestless && RestlessSince.TryGetValue(c.Id, out float quiet) && quiet > Time.time);
            if (restless) { near = true; ev = null; stone = dead.transform.position; }
            if (near && !c.RaidOn)
            {
                c.RaidOn = true; c.RaidAt = ev != null ? ev.m_pos : stone;
                if (restless) RestlessSince[c.Id] = Time.time;
                string name = restless ? Restless : band ? "the warband of " + Warband.JarlName(c) : ev == null ? "a siege at the Warstone" : Localization.instance.Localize(ev.GetHudText()) is string t && t.Length > 0 ? t : ev.m_name;
                Running[c.Id] = (name, c.R.Log.Count(e => e.Kind == "death" || e.Kind == "lost"));
                Fought.Remove(c.Id);
                c.Log("raid", "", name);
                Returning.Begin(c.R.Flags);
                Plugin.Guard(() => Greet.Raid(c));
                c.HornUntil = Time.time + 600;
                Assets.Effect("sfx_fader_bell", c.Position);
            }
            if (c.RaidOn && !Fought.Contains(c.Id) && Foes(c, reach + 30)) Fought.Add(c.Id);
            if (c.RaidOn) Plugin.Guard(() => Einherjar.Watch(c));
            if (!near && c.RaidOn) // (just begun is near: never both)
            {
                c.RaidOn = false; c.HornUntil = 0;
                int deaths = c.R.Log.Count(e => e.Kind == "death" || e.Kind == "lost");
                bool bought = c.R.Flags.Remove("tribute:bought"); // (paid off: nothing held, no deed)
                string ended = Running.TryGetValue(c.Id, out var r) ? r.name : "the raid"; // (a reload mid-raid forgets which)
                bool came = Fought.Remove(c.Id) || !Running.ContainsKey(c.Id); // (called off before a foe showed: nothing held; a reload mid-raid gets the benefit of the doubt)
                if (!came && !bought) c.Log("text", "", "", $"{char.ToUpper(ended[0])}{ended.Substring(1)} came to nothing: no foe reached the colony.");
                if (c.Population > 0 && !bought && came && ended != Restless) // (our own dead walking aren't a raid held off)
                {
                    string name = ended;
                    c.R.Deeds += Renown.Deed("raidheld");
                    c.Log("raidheld", "", name);
                    JarlDeeds.RaidHeld(c);
                    foreach (Brain b in Villagers.All.Where(b => b.S.Colony == c.Id)) { Mood.Add(b.S, "raidheld", b.Now); b.S.Bump("raids"); if (b.S.Prof == Prof.Guard && b.S.Count("raids") >= 3 && string.IsNullOrEmpty(b.S.Byname)) { b.S.Byname = Names.Byname("defender"); c.Log("byname", b.S.Name, b.S.Byname); } Asks.FoughtWell(b); b.Dirty(); }
                }
                Running.Remove(c.Id);
                Tribute.RaidOver(c);
            }
        }

        // Bigger raids for a colony Odin has noticed: the spawners a raid near a colony uses, scaled by renown.
        [HarmonyPatch(typeof(RandEventSystem), nameof(RandEventSystem.GetCurrentSpawners))]
        private static class Scale
        {
            private static readonly Dictionary<RandomEvent, List<SpawnSystem.SpawnData>> Made = new Dictionary<RandomEvent, List<SpawnSystem.SpawnData>>();
            private static void Postfix(RandEventSystem __instance, ref List<SpawnSystem.SpawnData> __result)
            {
                if (__result == null || Plugin.Instance == null || !Plugin.Instance.Raids.Value) return;
                RandomEvent ev = __instance.GetActiveEvent();
                if (ev == null) return;
                Colony c = Colony.Nearest(ev.m_pos, 200);
                if (c == null) return;
                float scale = Renown.RaidScale(c.Renown);
                if (scale <= 1.01f) return;
                if (!Made.TryGetValue(ev, out var list))
                {
                    if (Made.Count > 8) Made.Clear();
                    list = __result.Select(s => { var copy = s.Clone(); copy.m_maxSpawned = Mathf.CeilToInt(s.m_maxSpawned * scale); copy.m_groupSizeMax = Mathf.CeilToInt(s.m_groupSizeMax * Mathf.Sqrt(scale)); return copy; }).ToList();
                    Made[ev] = list;
                }
                __result = list;
            }
        }
        public static void Forget() { Running.Clear(); RestlessSince.Clear(); Fought.Clear(); }
        // Raids a foe has come to (an alerted monster near the colony): one called off before any came isn't a raid held off.
        private static readonly HashSet<string> Fought = new HashSet<string>();
        private static bool Foes(Colony c, float reach) => Character.GetAllCharacters().Any(ch => ch != null && !ch.IsDead() && !ch.IsPlayer() && !ch.IsTamed()
            && ch.GetBaseAI() is MonsterAI m && m.IsAlerted() && Villagers.Brain(ch) == null && Utils.DistanceXZ(ch.transform.position, c.Position) < reach);
        private const string Restless = "the restless dead";
        private static readonly Dictionary<string, float> RestlessSince = new Dictionary<string, float>();
    }

    // A soul lost to Hel rises as one of the dead: a draugr wearing their name, carrying their hoard.
    internal static class Draugr
    {
        public const string NameKey = "jarldom_lost", HoardKey = "jarldom_hoard";
        public static void Raise(Colony c, VillagerState s, Vector3 at)
        {
            GameObject prefab = ZNetScene.instance.GetPrefab(s.Worth > 50 ? "Draugr_Elite" : "Draugr");
            if (prefab == null) return;
            // (out at the colony's edge, the way they fell, and walks back in: not in the hall among the unarmed, where one loss became many)
            Vector3 dir = at - c.Position; dir.y = 0; if (dir.sqrMagnitude < 1f) dir = UnityEngine.Random.insideUnitSphere; dir.y = 0;
            Vector3 edge = c.Position + dir.normalized * (c.Radius + 5f);
            Vector3 spot = Colony.Spot(edge, 6f) ?? Colony.Spot(at + UnityEngine.Random.insideUnitSphere * 6f, 4f) ?? at;
            GameObject d = Object.Instantiate(prefab, spot, Quaternion.identity);
            ZDO z = d.GetComponent<ZNetView>().GetZDO();
            z.Set(NameKey, "Draugr " + s.Name);
            z.Set(Lost.VidKey, s.Vid); z.Set(Lost.ColonyKey, c.Id);
            z.Set(HoardKey, s.Hoard);
            Assets.Effect("vfx_ghost_death", spot);
        }
        [HarmonyPatch(typeof(Character), nameof(Character.GetHoverName))]
        private static class Named
        {
            private static void Postfix(Character __instance, ref string __result)
            {
                if (__instance.IsPlayer() || VillagerPrefab.Is(__instance)) return;
                string n = __instance.GetComponent<ZNetView>()?.GetZDO()?.GetString(NameKey, "");
                if (!string.IsNullOrEmpty(n)) __result = n.StartsWith("Draugr ") ? Lost.Called(n.Substring(7)) : n;
            }
        }
        [HarmonyPatch(typeof(Character), nameof(Character.OnDeath))]
        private static class Hoard
        {
            private static void Prefix(Character __instance)
            {
                ZNetView v = __instance.GetComponent<ZNetView>();
                if (v != null && v.IsValid()) Plugin.Guard(() => Lost.Unpin(v.GetZDO())); // (every game: the pin goes)
                if (v == null || !v.IsValid() || !v.IsOwner()) return;
                Plugin.Guard(() => Lost.Remains(__instance));
                int hoard = v.GetZDO().GetInt(HoardKey, 0);
                if (hoard <= 0) return;
                GameObject coins = ZNetScene.instance.GetPrefab("Coins");
                if (coins == null) return;
                for (int left = hoard; left > 0;)
                {
                    int n = Math.Min(left, coins.GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize);
                    GameObject drop = Object.Instantiate(coins, __instance.GetCenterPoint() + UnityEngine.Random.insideUnitSphere * 0.5f, Quaternion.identity);
                    drop.GetComponent<ItemDrop>().SetStack(n);
                    left -= n;
                }
            }
        }
    }
}
